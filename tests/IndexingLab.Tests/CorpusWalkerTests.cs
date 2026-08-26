using IndexingLab.Core;

namespace IndexingLab.Tests;

/// <summary>
/// Selection correctness decides whether a session means anything. Two properties matter most:
/// the budget must be respected exactly, and a partial budget must still draw from every subfolder
/// — a sequential walk of a corpus organised by format fills from the alphabetically first folder
/// and produces a size ratio that describes one format rather than the corpus.
/// </summary>
public sealed class CorpusWalkerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lab-corpus-" + Guid.NewGuid().ToString("N")[..8]);

    public CorpusWalkerTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Write(string relativePath, int bytes)
    {
        string full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
    }

    private RunConfiguration Config(Action<RunConfiguration>? tweak = null)
    {
        RunConfiguration configuration = new()
        {
            CorpusRootPath = root,
            SourceBudgetBytes = long.MaxValue,
            MaxFiles = 0,
            MaxFileSizeBytes = long.MaxValue,
            SamplingMode = "Stratified",
        };

        tweak?.Invoke(configuration);
        return configuration;
    }

    [Fact]
    public void StratifiedDrawsFromEverySubfolderUnderAPartialBudget()
    {
        for (int i = 0; i < 20; i++)
        {
            Write($"Html/h{i:D2}.html", 100);
            Write($"Pdf/p{i:D2}.pdf", 100);
            Write($"Words/w{i:D2}.doc", 100);
        }

        // Budget for 9 files out of 60.
        CorpusSelection selection = CorpusWalker.Select(
            Config(c => c.SourceBudgetBytes = 900),
            CancellationToken.None);

        Assert.Equal(9, selection.Files.Count);
        Assert.Equal(3, selection.Fingerprint.ByExtension.Count);
        Assert.All(selection.Fingerprint.ByExtension, b => Assert.Equal(3, b.FileCount));
    }

    [Fact]
    public void SequentialFillsFromTheFirstFolderOnly()
    {
        // Pinned deliberately: this is the behaviour stratified sampling exists to avoid, and a
        // reader comparing the two modes should be able to see the difference stated as a test.
        for (int i = 0; i < 20; i++)
        {
            Write($"Html/h{i:D2}.html", 100);
            Write($"Pdf/p{i:D2}.pdf", 100);
        }

        CorpusSelection selection = CorpusWalker.Select(
            Config(c => { c.SourceBudgetBytes = 900; c.SamplingMode = "Sequential"; }),
            CancellationToken.None);

        Assert.Equal(9, selection.Files.Count);
        Assert.Single(selection.Fingerprint.ByExtension);
        Assert.Equal(".html", selection.Fingerprint.ByExtension[0].Extension);
    }

    [Fact]
    public void StopsBeforeExceedingTheBudget()
    {
        for (int i = 0; i < 10; i++)
        {
            Write($"a{i}.txt", 300);
        }

        CorpusSelection selection = CorpusWalker.Select(
            Config(c => c.SourceBudgetBytes = 1000),
            CancellationToken.None);

        Assert.Equal(900, selection.TotalBytes);
        Assert.True(selection.TotalBytes <= 1000);
        Assert.True(selection.BudgetReached);
    }

    [Fact]
    public void FileCapActsAsAGuard()
    {
        for (int i = 0; i < 50; i++)
        {
            Write($"a{i:D2}.txt", 10);
        }

        CorpusSelection selection = CorpusWalker.Select(
            Config(c => c.MaxFiles = 7),
            CancellationToken.None);

        Assert.Equal(7, selection.Files.Count);
        Assert.True(selection.FileCapReached);
    }

    [Fact]
    public void ExcludedExtensionsAreSkipped()
    {
        Write("a.txt", 100);
        Write("b.jpg", 100);
        Write("c.png", 100);

        CorpusSelection selection = CorpusWalker.Select(
            Config(c => c.ExcludeExtensions = [".jpg", ".png"]),
            CancellationToken.None);

        Assert.Single(selection.Files);
        Assert.EndsWith("a.txt", selection.Files[0].Path);
    }

    [Fact]
    public void IncludeListWinsOverEverythingNotListed()
    {
        Write("a.txt", 100);
        Write("b.pdf", 100);
        Write("c.doc", 100);

        CorpusSelection selection = CorpusWalker.Select(
            Config(c => c.IncludeExtensions = [".pdf"]),
            CancellationToken.None);

        Assert.Single(selection.Files);
        Assert.EndsWith("b.pdf", selection.Files[0].Path);
    }

    [Fact]
    public void EmptyAndOversizeFilesAreSkipped()
    {
        Write("empty.txt", 0);
        Write("huge.txt", 5000);
        Write("ok.txt", 100);

        CorpusSelection selection = CorpusWalker.Select(
            Config(c => c.MaxFileSizeBytes = 1000),
            CancellationToken.None);

        Assert.Single(selection.Files);
        Assert.EndsWith("ok.txt", selection.Files[0].Path);
    }

    [Fact]
    public void SelectionHashIsStableAcrossRunsAndSensitiveToContent()
    {
        Write("Html/a.html", 100);
        Write("Pdf/b.pdf", 100);

        string first = CorpusWalker.Select(Config(), CancellationToken.None).Fingerprint.SelectionHash;
        string second = CorpusWalker.Select(Config(), CancellationToken.None).Fingerprint.SelectionHash;
        Assert.Equal(first, second);
        Assert.NotEmpty(first);

        // A different file set must produce a different fingerprint, or the compare tool would
        // treat two unrelated runs as comparable.
        Write("Pdf/c.pdf", 100);
        string third = CorpusWalker.Select(Config(), CancellationToken.None).Fingerprint.SelectionHash;
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void FingerprintRecordsPerExtensionTotals()
    {
        Write("Html/a.html", 100);
        Write("Html/b.html", 100);
        Write("Pdf/c.pdf", 700);

        CorpusFingerprint fingerprint = CorpusWalker.Select(Config(), CancellationToken.None).Fingerprint;

        ExtensionBucket pdf = fingerprint.ByExtension.Single(b => b.Extension == ".pdf");
        ExtensionBucket html = fingerprint.ByExtension.Single(b => b.Extension == ".html");
        Assert.Equal(700, pdf.Bytes);
        Assert.Equal(2, html.FileCount);
        Assert.Equal(900, fingerprint.SourceBytesSelected);
    }

    [Fact]
    public void FilesDirectlyUnderTheRootAreTreatedAsTheirOwnStratum()
    {
        Write("loose.txt", 100);
        Write("Sub/a.txt", 100);

        CorpusSelection selection = CorpusWalker.Select(Config(), CancellationToken.None);

        Assert.Equal(2, selection.Files.Count);
    }

    [Fact]
    public void MissingRootIsReportedAsAConfigurationProblem()
    {
        RunConfiguration configuration = Config(c => c.CorpusRootPath = Path.Combine(root, "nope"));

        Assert.Throws<DirectoryNotFoundException>(
            () => CorpusWalker.Select(configuration, CancellationToken.None));
    }

    [Fact]
    public void DefaultExclusionsCoverImagesAndArchives()
    {
        // These formats yield no text without OCR, so indexing them inflates the source side of
        // the size ratio while contributing nothing to the index.
        Assert.Contains(".jpg", CorpusWalker.DefaultExcludedExtensions);
        Assert.Contains(".png", CorpusWalker.DefaultExcludedExtensions);
        Assert.Contains(".zip", CorpusWalker.DefaultExcludedExtensions);
    }
}
