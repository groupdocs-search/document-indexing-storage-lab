using System.Text.RegularExpressions;

namespace IndexingLab.Core;

/// <summary>
/// Breaks a finished index folder down by what each file actually is.
/// </summary>
/// <remarks>
/// <para>
/// File roles, from the product's own <c>Constants</c>:
/// <c>index.info</c> document metadata, <c>index0.info</c> image hash data,
/// <c>index1.info</c> the term dictionary, <c>index{N}.body</c> segment postings,
/// <c>index{N}.head</c> segment term-offset tables, <c>*.term</c> / <c>*.dat</c> dictionaries.
/// </para>
/// <para>
/// The <c>.head</c> files are the interesting part. Each is a flat <c>int[]</c> indexed by term
/// id — <c>SegmentInFile.GetOffset</c> reads at <c>termIndex * sizeof(int)</c> — so every segment
/// carries a 4-byte slot for <em>every term in the whole index</em>, present in that segment or
/// not. That gives two things for free: vocabulary size is the largest <c>.head</c> divided by 4,
/// and the recoverable waste is everything above what a single merged segment would need.
/// </para>
/// </remarks>
public static class IndexStorageAnalyzer
{
    private const int BytesPerTermOffset = sizeof(int);

    private static readonly Regex SegmentBody = new(@"^index(\d+)\.body$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SegmentHead = new(@"^index(\d+)\.head$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IndexStorageBreakdown Analyze(
        string indexFolderPath,
        long sourceBytesConsumed,
        bool textStorageEnabled)
    {
        IndexStorageBreakdown breakdown = new();

        if (!Directory.Exists(indexFolderPath))
        {
            return breakdown;
        }

        Dictionary<int, SegmentDetail> segments = [];

        foreach (string path in Directory.EnumerateFiles(indexFolderPath, "*", SearchOption.AllDirectories))
        {
            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                continue;
            }

            breakdown.TotalBytes += length;
            string name = Path.GetFileName(path);

            Match bodyMatch = SegmentBody.Match(name);
            if (bodyMatch.Success)
            {
                int index = int.Parse(bodyMatch.Groups[1].Value);
                GetSegment(segments, index).BodyBytes = length;
                breakdown.PostingsBytes += length;
                continue;
            }

            Match headMatch = SegmentHead.Match(name);
            if (headMatch.Success)
            {
                int index = int.Parse(headMatch.Groups[1].Value);
                SegmentDetail detail = GetSegment(segments, index);
                detail.HeadBytes = length;
                detail.TermSlots = length / BytesPerTermOffset;
                breakdown.TermOffsetBytes += length;
                continue;
            }

            switch (name.ToLowerInvariant())
            {
                case "index.info":
                    breakdown.DocumentMetadataBytes += length;
                    break;
                case "index0.info":
                    breakdown.ImageDataBytes += length;
                    break;
                case "index1.info":
                    breakdown.TermDictionaryBytes += length;
                    break;
                default:
                    // Location before extension: the stored-text archive is numbered .dat files
                    // under "Source", so an extension test would classify every one of them as a
                    // dictionary side-file. On a real 20 GB run that silently moved 473 MB into
                    // the index total and left the extracted-text ratio unavailable.
                    if (IsTextArchive(path, indexFolderPath))
                    {
                        breakdown.TextStorageBytes += length;
                    }
                    else if (name.EndsWith(".term", StringComparison.OrdinalIgnoreCase) ||
                             name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                    {
                        breakdown.DictionaryFileBytes += length;
                    }
                    else
                    {
                        breakdown.OtherBytes += length;
                    }

                    break;
            }
        }

        breakdown.Segments = [.. segments.Values.OrderBy(s => s.Index)];
        breakdown.SegmentCount = breakdown.Segments.Count;
        breakdown.EmptySegmentCount = breakdown.Segments.Count(s => s.BodyBytes == 0);

        // Vocabulary: the largest offset table covers the whole vocabulary as of its creation,
        // and segments are written in increasing order, so the largest is the current total.
        breakdown.EstimatedTermCount = breakdown.Segments.Count > 0
            ? breakdown.Segments.Max(s => s.TermSlots)
            : 0;

        breakdown.TermOffsetBytesIfSingleSegment = breakdown.EstimatedTermCount * BytesPerTermOffset;
        breakdown.RecoverableTermOffsetBytes = Math.Max(
            0,
            breakdown.TermOffsetBytes - breakdown.TermOffsetBytesIfSingleSegment);

        breakdown.RatioToSourceBytes = sourceBytesConsumed > 0
            ? (double)breakdown.TotalBytes / sourceBytesConsumed
            : 0;

        // Extracted text volume is only knowable when the text archive was written. Comparing the
        // index against the raw source is misleading when the corpus contains binaries that yield
        // no text at all, so report this ratio whenever we legitimately can.
        if (textStorageEnabled && breakdown.TextStorageBytes > 0)
        {
            long indexWithoutText = breakdown.TotalBytes - breakdown.TextStorageBytes;
            breakdown.RatioToExtractedText = (double)indexWithoutText / breakdown.TextStorageBytes;
        }

        return breakdown;
    }

    /// <summary>
    /// The stored-text archive sits in a "Source" subfolder as numbered .dat files. Getting this
    /// wrong is expensive rather than cosmetic: those files would otherwise be counted as index
    /// data, inflating the total and the size ratio, and the index-vs-extracted-text ratio would
    /// silently disappear because TextStorageBytes stayed zero. Measured on a real run, the
    /// archive was 473 MB that did not belong in the index figure.
    /// </summary>
    private static bool IsTextArchive(string path, string indexFolderPath)
    {
        string relative = Path.GetRelativePath(indexFolderPath, path);
        string? topFolder = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .FirstOrDefault();

        return string.Equals(topFolder, "Source", StringComparison.OrdinalIgnoreCase)
            || relative.Contains("text", StringComparison.OrdinalIgnoreCase);
    }

    private static SegmentDetail GetSegment(Dictionary<int, SegmentDetail> segments, int index)
    {
        if (!segments.TryGetValue(index, out SegmentDetail? detail))
        {
            detail = new SegmentDetail { Index = index };
            segments.Add(index, detail);
        }

        return detail;
    }

    /// <summary>Total bytes currently on disk for an index folder. Cheap enough to call per batch.</summary>
    public static long MeasureIndexBytes(string indexFolderPath)
    {
        if (!Directory.Exists(indexFolderPath))
        {
            return 0;
        }

        long total = 0;
        foreach (string path in Directory.EnumerateFiles(indexFolderPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(path).Length;
            }
            catch (IOException)
            {
                // A segment file can be renamed mid-commit; skipping it costs one sample's accuracy.
            }
        }

        return total;
    }

    /// <summary>
    /// Vocabulary estimate without walking the whole index: only the <c>.head</c> files are
    /// stat-ed, and the largest divided by four is the current term count.
    /// </summary>
    public static long EstimateTermCount(string indexFolderPath)
    {
        if (!Directory.Exists(indexFolderPath))
        {
            return 0;
        }

        long largest = 0;
        foreach (string path in Directory.EnumerateFiles(indexFolderPath, "index*.head", SearchOption.TopDirectoryOnly))
        {
            try
            {
                long length = new FileInfo(path).Length;
                if (length > largest)
                {
                    largest = length;
                }
            }
            catch (IOException)
            {
            }
        }

        return largest / BytesPerTermOffset;
    }

    public static int CountSegments(string indexFolderPath)
    {
        if (!Directory.Exists(indexFolderPath))
        {
            return 0;
        }

        return Directory.EnumerateFiles(indexFolderPath, "index*.body", SearchOption.TopDirectoryOnly).Count();
    }
}
