using GroupDocs.Search.Dictionaries;
using GroupDocs.Search.Options;
using IndexingLab.Core;
using Index = GroupDocs.Search.Index;

namespace IndexingLab.Recipes;

/// <summary>
/// Indexes the same documents twice — as-is, and with digits and base64 punctuation reclassified as
/// separators — and reports the difference in vocabulary and index size.
/// </summary>
/// <remarks>
/// Vocabulary is what makes the term dictionary large, and the dictionary is what gets cloned on
/// every commit. On a real corpus we measured 30.5 million terms, most of which were hex fragments,
/// base64 runs, GUIDs and timestamps rather than words — each one a distinct term carrying its own
/// entry in the tree and a slot in every segment's offset table.
/// <para>
/// Reclassifying digits as separators means "invoice-2024-11-07" stops contributing a distinct term
/// for every date and identifier in the corpus. <b>This changes tokenisation</b>, so it needs a full
/// reindex and it changes what queries match — a search for a bare number will no longer find
/// anything. Measure it on a subset before deciding, which is exactly what this recipe does.
/// </para>
/// </remarks>
public static class VocabularyTuning
{
    /// <summary>Digits, plus the characters that make base64 and hex runs into single huge terms.</summary>
    private const string SeparatorCharacters = "0123456789+/=";

    public static int Run(string corpusFolder, string indexRoot)
    {
        LicenseHelper.TryApply(out string licenceNote);
        Console.WriteLine(licenceNote);

        string[] files = Directory.GetFiles(corpusFolder, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"ERROR: no files under '{corpusFolder}'.");
            return 1;
        }

        Console.WriteLine($"Indexing {files.Length:N0} documents twice for comparison.");
        Console.WriteLine();

        Result asIs = Build(Path.Combine(indexRoot, "default-alphabet"), files, separators: null);
        Result tuned = Build(Path.Combine(indexRoot, "digits-as-separators"), files, SeparatorCharacters);

        Console.WriteLine();
        Console.WriteLine($"{"",-26}{"default",16}{"separators",16}{"change",12}");
        Console.WriteLine(new string('-', 70));
        Compare("vocabulary (terms)", asIs.Terms, tuned.Terms);
        Compare("term dictionary", asIs.DictionaryBytes, tuned.DictionaryBytes, bytes: true);
        Compare("index total", asIs.TotalBytes, tuned.TotalBytes, bytes: true);
        Console.WriteLine();
        Console.WriteLine("The dictionary column is the one that matters for OutOfMemoryException:");
        Console.WriteLine("that is the structure cloned on every Add() and every Optimize().");
        Console.WriteLine();
        Console.WriteLine($"Separators applied: '{SeparatorCharacters}'");
        Console.WriteLine("Remember this changes what queries match. Verify your real queries still work.");
        return 0;
    }

    private static Result Build(string indexFolder, string[] files, string? separators)
    {
        if (Directory.Exists(indexFolder))
        {
            Directory.Delete(indexFolder, recursive: true);
        }

        Directory.CreateDirectory(indexFolder);

        using Index index = new(indexFolder);

        // Must happen before any document is added: it changes how text is split into terms.
        if (!string.IsNullOrEmpty(separators))
        {
            index.Dictionaries.Alphabet.SetRange(separators.ToCharArray(), CharacterType.Separator);
        }

        index.Add(files, new IndexingOptions());
        index.Optimize();

        IndexStorageBreakdown breakdown = IndexStorageAnalyzer.Analyze(indexFolder, 0, textStorageEnabled: false);

        Console.WriteLine(
            $"  {(separators is null ? "default alphabet    " : "digits as separators")}  " +
            $"{index.IndexInfo.TermCount,12:N0} terms  {Format.Bytes(breakdown.TotalBytes),12}");

        return new Result(index.IndexInfo.TermCount, breakdown.TermDictionaryBytes, breakdown.TotalBytes);
    }

    private static void Compare(string label, long before, long after, bool bytes = false)
    {
        string b = bytes ? Format.Bytes(before) : before.ToString("N0");
        string a = bytes ? Format.Bytes(after) : after.ToString("N0");
        string change = before > 0 ? $"{(after - before) / (double)before,11:P1}" : string.Empty;
        Console.WriteLine($"{label,-26}{b,16}{a,16}{change}");
    }

    private sealed record Result(long Terms, long DictionaryBytes, long TotalBytes);
}
