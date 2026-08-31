using IndexingLab.Recipes;

// ---------------------------------------------------------------------------
// Small, self-contained recipes for the problems that come up on large corpora.
// Each one is a single file meant to be read and copied into your own code,
// not a framework to adopt.
//
//   diagnose <indexFolder>              what an existing index is made of
//   progress <corpus> <indexFolder>     progress events + async cancellable Optimize
//   sharded  <corpus> <indexFolder>     many small indexes, queried as one
//   alphabet <corpus> <indexFolder>     vocabulary reduction, measured before/after
// ---------------------------------------------------------------------------

if (args.Length == 0)
{
    Console.WriteLine("Recipes for GroupDocs.Search on large corpora.");
    Console.WriteLine();
    Console.WriteLine("  diagnose <indexFolder>");
    Console.WriteLine("      Reports what an existing index is made of. Reads only - it never");
    Console.WriteLine("      opens or modifies the index, so it is safe on a production folder.");
    Console.WriteLine();
    Console.WriteLine("  progress <corpusFolder> <indexFolder>");
    Console.WriteLine("      Wires up Add and Optimize progress, and runs Optimize asynchronously");
    Console.WriteLine("      with cancellation.");
    Console.WriteLine();
    Console.WriteLine("  sharded <corpusFolder> <indexFolder> [docsPerShard]");
    Console.WriteLine("      Builds several small indexes instead of one large one and queries");
    Console.WriteLine("      them together through IndexRepository.");
    Console.WriteLine();
    Console.WriteLine("  alphabet <corpusFolder> <indexFolder>");
    Console.WriteLine("      Indexes the same documents twice, with and without digits treated as");
    Console.WriteLine("      separators, and reports the vocabulary difference.");
    Console.WriteLine();
    Console.WriteLine("Set GROUPDOCS_SEARCH_LIC_PATH first: without a licence the document count");
    Console.WriteLine("is capped and every number below is meaningless.");
    return 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "diagnose" => DiagnoseIndex.Run(Arg(args, 1, "indexFolder")),
        "progress" => ProgressReporting.Run(Arg(args, 1, "corpusFolder"), Arg(args, 2, "indexFolder")),
        "sharded" => ShardedIndexing.Run(
            Arg(args, 1, "corpusFolder"),
            Arg(args, 2, "indexFolder"),
            args.Length > 3 ? int.Parse(args[3]) : 2000),
        "alphabet" => VocabularyTuning.Run(Arg(args, 1, "corpusFolder"), Arg(args, 2, "indexFolder")),
        _ => Unknown(args[0]),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ERROR: {exception.Message}");
    return 1;
}

static string Arg(string[] args, int index, string name) =>
    index < args.Length
        ? args[index]
        : throw new ArgumentException($"Missing argument <{name}>. Run with no arguments for usage.");

static int Unknown(string name)
{
    Console.Error.WriteLine($"Unknown recipe '{name}'. Run with no arguments for usage.");
    return 1;
}
