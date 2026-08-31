using IndexingLab.Core;

namespace IndexingLab.Recipes;

/// <summary>
/// Reports what an existing index is made of, without opening it.
/// </summary>
/// <remarks>
/// Reads file sizes only — it never constructs an <c>Index</c>, so it costs nothing, cannot modify
/// anything, and is safe to point at a production folder while a service is using it.
/// <para>
/// The two numbers that decide whether an index is in trouble are <b>vocabulary</b> and
/// <b>segment count</b>. Both are recoverable from the folder: each <c>index{N}.head</c> is a flat
/// <c>int[]</c> indexed by term id, so it holds four bytes for every term in the whole index —
/// present in that segment or not. Largest <c>.head</c> ÷ 4 is the vocabulary, and the rest of those
/// files is padding that a merge removes.
/// </para>
/// </remarks>
public static class DiagnoseIndex
{
    public static int Run(string indexFolder)
    {
        if (!Directory.Exists(indexFolder))
        {
            Console.Error.WriteLine($"ERROR: '{indexFolder}' does not exist.");
            return 1;
        }

        IndexStorageBreakdown b = IndexStorageAnalyzer.Analyze(
            indexFolder,
            sourceBytesConsumed: 0,      // unknown here; ratios to source are skipped
            textStorageEnabled: true);   // detected from the folder, not assumed

        Console.WriteLine($"Index: {indexFolder}");
        Console.WriteLine(new string('-', 74));
        Row("Postings (index*.body)", b.PostingsBytes, b.TotalBytes);
        Row("Term offsets (index*.head)", b.TermOffsetBytes, b.TotalBytes);
        Row("Term dictionary (index1.info)", b.TermDictionaryBytes, b.TotalBytes);
        Row("Document metadata (index.info)", b.DocumentMetadataBytes, b.TotalBytes);
        Row("Image data (index0.info)", b.ImageDataBytes, b.TotalBytes);
        Row("Stored text", b.TextStorageBytes, b.TotalBytes);
        Row("Dictionaries and other", b.DictionaryFileBytes + b.OtherBytes, b.TotalBytes);
        Console.WriteLine(new string('-', 74));
        Row("TOTAL", b.TotalBytes, b.TotalBytes);
        Console.WriteLine();
        Console.WriteLine($"  segments            {b.SegmentCount}   ({b.EmptySegmentCount} empty)");
        Console.WriteLine($"  vocabulary          {b.EstimatedTermCount:N0} terms");
        Console.WriteLine();

        // The headline: how much of the index is offset padding rather than index data.
        if (b.TermOffsetBytes > 0 && b.TotalBytes > 0)
        {
            double share = (double)b.TermOffsetBytes / b.TotalBytes;
            Console.WriteLine($"  term offsets are {share:P1} of this index");
            Console.WriteLine($"  a single merged segment would need {Format.Bytes(b.TermOffsetBytesIfSingleSegment)}");
            Console.WriteLine($"  recoverable by Optimize(): {Format.Bytes(b.RecoverableTermOffsetBytes)}");

            long projected = b.TotalBytes - b.RecoverableTermOffsetBytes;
            Console.WriteLine($"  projected size after a full merge: {Format.Bytes(projected)}");
        }

        Console.WriteLine();
        Console.WriteLine("Two numbers are also available from the API without this tool:");
        Console.WriteLine("    index.IndexInfo.TermCount");
        Console.WriteLine("    index.IndexInfo.SegmentCount");

        return 0;
    }

    private static void Row(string label, long bytes, long total)
    {
        if (bytes == 0 && !label.StartsWith("TOTAL", StringComparison.Ordinal))
        {
            return;
        }

        string share = total > 0 ? $"{(double)bytes / total,8:P1}" : string.Empty;
        Console.WriteLine($"  {label,-34} {Format.Bytes(bytes),12} {share}");
    }
}
