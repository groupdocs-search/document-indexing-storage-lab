using System.Security.Cryptography;
using System.Text;

namespace IndexingLab.Core;

public sealed record CorpusFile(string Path, long Length, string Extension);

public sealed class CorpusSelection
{
    public List<CorpusFile> Files { get; init; } = [];

    public long TotalBytes { get; init; }

    public CorpusFingerprint Fingerprint { get; init; } = new();

    public bool BudgetReached { get; init; }

    public bool FileCapReached { get; init; }
}

/// <summary>
/// Selects a bounded, reproducible slice of any folder tree.
/// </summary>
/// <remarks>
/// No assumptions about layout — it walks whatever is under the root. Enumeration is sorted so
/// two runs against an unchanged tree pick the same files in the same order, which is what makes
/// sessions comparable across product versions.
/// </remarks>
public static class CorpusWalker
{
    /// <summary>
    /// Formats that contain no extractable text without OCR. Indexing them costs extraction time
    /// and inflates "index as a share of source bytes" while contributing nothing to the index,
    /// so they are excluded by default.
    /// </summary>
    public static readonly string[] DefaultExcludedExtensions =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".ico", ".svg",
        ".mp3", ".mp4", ".avi", ".mov", ".wmv", ".wav", ".flac", ".mkv",
        ".zip", ".rar", ".7z", ".gz", ".tar", ".cab", ".iso",
        ".exe", ".dll", ".so", ".dylib", ".bin", ".dat", ".db", ".sqlite",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
    ];

    public static CorpusSelection Select(RunConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!Directory.Exists(configuration.CorpusRootPath))
        {
            throw new DirectoryNotFoundException(
                $"Corpus root '{configuration.CorpusRootPath}' does not exist.");
        }

        HashSet<string> include = new(configuration.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
        HashSet<string> exclude = new(configuration.ExcludeExtensions, StringComparer.OrdinalIgnoreCase);

        List<CorpusFile> selected = [];
        Dictionary<string, ExtensionBucket> buckets = new(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        bool budgetReached = false;
        bool fileCapReached = false;

        IEnumerable<string> candidates =
            configuration.SamplingMode.Equals("Sequential", StringComparison.OrdinalIgnoreCase)
                ? EnumerateSorted(configuration.CorpusRootPath, cancellationToken)
                : EnumerateStratified(configuration.CorpusRootPath, cancellationToken);

        foreach (string path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string extension = Path.GetExtension(path);

            if (exclude.Contains(extension))
            {
                continue;
            }

            if (include.Count > 0 && !include.Contains(extension))
            {
                continue;
            }

            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (length == 0 || length > configuration.MaxFileSizeBytes)
            {
                continue;
            }

            if (configuration.SourceBudgetBytes > 0 &&
                totalBytes + length > configuration.SourceBudgetBytes)
            {
                budgetReached = true;
                break;
            }

            selected.Add(new CorpusFile(path, length, extension));
            totalBytes += length;

            if (!buckets.TryGetValue(extension, out ExtensionBucket? bucket))
            {
                bucket = new ExtensionBucket { Extension = string.IsNullOrEmpty(extension) ? "(none)" : extension };
                buckets.Add(extension, bucket);
            }

            bucket.FileCount++;
            bucket.Bytes += length;

            if (configuration.MaxFiles > 0 && selected.Count >= configuration.MaxFiles)
            {
                fileCapReached = true;
                break;
            }
        }

        return new CorpusSelection
        {
            Files = selected,
            TotalBytes = totalBytes,
            BudgetReached = budgetReached,
            FileCapReached = fileCapReached,
            Fingerprint = new CorpusFingerprint
            {
                FilesSelected = selected.Count,
                SourceBytesSelected = totalBytes,
                SelectionHash = ComputeSelectionHash(selected, configuration.CorpusRootPath),
                ByExtension = [.. buckets.Values.OrderByDescending(b => b.Bytes)],
            },
        };
    }

    /// <summary>
    /// Round-robins across the immediate subfolders of the root, so a budget that covers only part
    /// of the corpus still draws from every subfolder.
    /// </summary>
    /// <remarks>
    /// This matters more than it looks. A sorted sequential walk of a corpus organised by format
    /// fills the budget from the alphabetically first folders and never reaches the rest, so an
    /// "index size as a share of source" figure taken from it describes one or two formats rather
    /// than the corpus. Round-robin keeps the format mix representative at any budget, and stays
    /// deterministic because each subfolder is itself walked in sorted order.
    /// </remarks>
    private static IEnumerable<string> EnumerateStratified(string root, CancellationToken cancellationToken)
    {
        string[] subdirectories;
        try
        {
            subdirectories = Directory.GetDirectories(root);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        Array.Sort(subdirectories, StringComparer.OrdinalIgnoreCase);

        if (subdirectories.Length == 0)
        {
            foreach (string file in EnumerateSorted(root, cancellationToken))
            {
                yield return file;
            }

            yield break;
        }

        // Files directly under the root are a stratum of their own.
        List<IEnumerator<string>> streams = [EnumerateRootFiles(root).GetEnumerator()];
        streams.AddRange(subdirectories.Select(d => EnumerateSorted(d, cancellationToken).GetEnumerator()));

        try
        {
            bool any = true;
            while (any)
            {
                any = false;
                cancellationToken.ThrowIfCancellationRequested();

                foreach (IEnumerator<string> stream in streams)
                {
                    if (stream.MoveNext())
                    {
                        any = true;
                        yield return stream.Current;
                    }
                }
            }
        }
        finally
        {
            foreach (IEnumerator<string> stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    private static IEnumerable<string> EnumerateRootFiles(string root)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(root);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            yield return file;
        }
    }

    /// <summary>
    /// Walks the tree depth-first with directories and files sorted, so selection order is stable
    /// across runs and across machines rather than depending on filesystem enumeration order.
    /// </summary>
    private static IEnumerable<string> EnumerateSorted(string root, CancellationToken cancellationToken)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
        };

        Stack<string> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Pop();

            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(current, "*", options);
                directories = Directory.GetDirectories(current, "*", options);
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                yield return file;
            }

            // Reverse so the stack pops in ascending order.
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = directories.Length - 1; i >= 0; i--)
            {
                pending.Push(directories[i]);
            }
        }
    }

    private static string ComputeSelectionHash(List<CorpusFile> files, string root)
    {
        StringBuilder builder = new();
        foreach (CorpusFile file in files)
        {
            builder.Append(Path.GetRelativePath(root, file.Path))
                   .Append(':')
                   .Append(file.Length)
                   .Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }
}
