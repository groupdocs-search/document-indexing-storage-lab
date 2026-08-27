using IndexingLab.Core;

namespace IndexingLab.Tests;

/// <summary>
/// The analyzer is the reason this tool exists, and its central claim — that a <c>.head</c> file is
/// 4 bytes per term and therefore reveals the vocabulary — is an inference from the product's
/// storage format rather than something the product tells us. These tests pin that inference and
/// the arithmetic built on top of it against synthetic index folders.
/// </summary>
public sealed class IndexStorageAnalyzerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "lab-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public IndexStorageAnalyzerTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private void WriteFile(string name, long bytes) =>
        File.WriteAllBytes(Path.Combine(folder, name), new byte[bytes]);

    [Fact]
    public void ClassifiesEachFileRoleSeparately()
    {
        WriteFile("index0.body", 5000);
        WriteFile("index0.head", 400);
        WriteFile("index.info", 300);
        WriteFile("index0.info", 20);
        WriteFile("index1.info", 900);
        WriteFile("indexAlphabet.term", 100);
        WriteFile("indexSpellingCorrector.dat", 50);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, sourceBytesConsumed: 10000, textStorageEnabled: false);

        Assert.Equal(5000, b.PostingsBytes);
        Assert.Equal(400, b.TermOffsetBytes);
        Assert.Equal(900, b.TermDictionaryBytes);
        Assert.Equal(300, b.DocumentMetadataBytes);
        Assert.Equal(20, b.ImageDataBytes);
        Assert.Equal(150, b.DictionaryFileBytes);
        Assert.Equal(6770, b.TotalBytes);
    }

    [Fact]
    public void DerivesVocabularyFromTheLargestOffsetTable()
    {
        // Segments are written in increasing order and each .head covers the whole vocabulary
        // as of its creation, so the largest is the current total.
        WriteFile("index0.body", 10);
        WriteFile("index0.head", 400);   // 100 terms known then
        WriteFile("index1.body", 10);
        WriteFile("index1.head", 1200);  // 300 terms known now

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, 1000, false);

        Assert.Equal(300, b.EstimatedTermCount);
        Assert.Equal(1200, b.TermOffsetBytesIfSingleSegment);
    }

    [Fact]
    public void RecoverableWasteIsEverythingAboveOneMergedSegment()
    {
        WriteFile("index0.body", 10);
        WriteFile("index0.head", 400);
        WriteFile("index1.body", 10);
        WriteFile("index1.head", 1200);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, 1000, false);

        // 1600 bytes of offsets exist; a single merged segment would need 1200.
        Assert.Equal(1600, b.TermOffsetBytes);
        Assert.Equal(400, b.RecoverableTermOffsetBytes);
    }

    [Fact]
    public void RecoverableWasteIsNeverNegativeForASingleSegment()
    {
        WriteFile("index0.body", 10);
        WriteFile("index0.head", 800);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, 1000, false);

        Assert.Equal(0, b.RecoverableTermOffsetBytes);
    }

    [Fact]
    public void CountsEmptySegmentsSeparately()
    {
        // Merging leaves zero-length segment files behind; they cost no disk but do inflate
        // the segment count, so they are reported rather than silently included.
        WriteFile("index0.body", 500);
        WriteFile("index0.head", 40);
        WriteFile("index1.body", 0);
        WriteFile("index1.head", 0);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, 1000, false);

        Assert.Equal(2, b.SegmentCount);
        Assert.Equal(1, b.EmptySegmentCount);
    }

    [Fact]
    public void RatioToSourceUsesTheWholeIndexFolder()
    {
        WriteFile("index0.body", 250);
        WriteFile("index0.head", 250);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, sourceBytesConsumed: 1000, textStorageEnabled: false);

        Assert.Equal(0.5, b.RatioToSourceBytes, 6);
    }

    [Fact]
    public void ExtractedTextRatioIsUnavailableWithoutTextStorage()
    {
        WriteFile("index0.body", 500);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, 1000, textStorageEnabled: false);

        Assert.Null(b.RatioToExtractedText);
    }

    [Fact]
    public void ExtractedTextRatioExcludesTheTextArchiveFromTheNumerator()
    {
        // Otherwise the archive would be counted as index data and inflate the ratio it defines.
        Directory.CreateDirectory(Path.Combine(folder, "text"));
        File.WriteAllBytes(Path.Combine(folder, "text", "archive0.bin"), new byte[400]);
        WriteFile("index0.body", 800);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, 2000, textStorageEnabled: true);

        Assert.Equal(400, b.TextStorageBytes);
        Assert.NotNull(b.RatioToExtractedText);
        Assert.Equal(2.0, b.RatioToExtractedText!.Value, 6);
    }

    [Fact]
    public void TextArchiveIsRecognisedInTheSourceFolder()
    {
        // The product writes stored text as numbered .dat files under "Source". Classifying them
        // as dictionary side-files instead would inflate the index total and null the
        // extracted-text ratio - which is exactly what happened on a real 20 GB run.
        Directory.CreateDirectory(Path.Combine(folder, "Source"));
        File.WriteAllBytes(Path.Combine(folder, "Source", "0.dat"), new byte[300]);
        File.WriteAllBytes(Path.Combine(folder, "Source", "1.dat"), new byte[200]);
        WriteFile("index0.body", 1000);
        WriteFile("indexAlphabet.term", 50);

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(folder, 4000, textStorageEnabled: true);

        Assert.Equal(500, b.TextStorageBytes);
        Assert.Equal(50, b.DictionaryFileBytes);
        Assert.NotNull(b.RatioToExtractedText);
        Assert.Equal(2.1, b.RatioToExtractedText!.Value, 6);
    }

    [Fact]
    public void MissingFolderYieldsAnEmptyBreakdownRatherThanThrowing()
    {
        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(
            Path.Combine(folder, "does-not-exist"), 1000, false);

        Assert.Equal(0, b.TotalBytes);
        Assert.Equal(0, b.SegmentCount);
    }

    [Fact]
    public void CountsSegmentsFromBodyFilesOnly()
    {
        WriteFile("index0.body", 1);
        WriteFile("index1.body", 1);
        WriteFile("index0.head", 1);
        WriteFile("index.info", 1);

        Assert.Equal(2, IndexStorageAnalyzer.CountSegments(folder));
    }

    [Fact]
    public void MeasureIndexBytesSumsTheWholeTree()
    {
        WriteFile("index0.body", 100);
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllBytes(Path.Combine(folder, "nested", "x.bin"), new byte[50]);

        Assert.Equal(150, IndexStorageAnalyzer.MeasureIndexBytes(folder));
    }
}
