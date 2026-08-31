using GroupDocs.Search;
using GroupDocs.Search.Options;
using GroupDocs.Search.Results;
using IndexingLab.Core;
using Index = GroupDocs.Search.Index;

namespace IndexingLab.Recipes;

/// <summary>
/// Builds several small indexes instead of one large one, and queries them together.
/// </summary>
/// <remarks>
/// This is the structural answer to <c>OutOfMemoryException</c> during commit on a very large index.
/// The allocation that fails is a clone of the term dictionary, and that tree's size is proportional
/// to vocabulary. Ten indexes of a tenth the documents means ten trees of roughly a tenth the size:
/// the allocation that fails today never gets large enough to fail.
/// <para>
/// Two secondary benefits. Each shard's <c>Optimize()</c> is over a small index, so it finishes in
/// seconds rather than minutes. And shards can be built on separate machines or processes and simply
/// copied into place.
/// </para>
/// <para>
/// The cost is that every open shard holds its own metadata and dictionary in memory, so keep only
/// the shards you need open — or accept the resident cost and take the win on the build side, which
/// is where the crash happens.
/// </para>
/// </remarks>
public static class ShardedIndexing
{
    public static int Run(string corpusFolder, string indexRoot, int documentsPerShard)
    {
        LicenseHelper.TryApply(out string licenceNote);
        Console.WriteLine(licenceNote);

        string[] files = Directory.GetFiles(corpusFolder, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"ERROR: no files under '{corpusFolder}'.");
            return 1;
        }

        int shardCount = (files.Length + documentsPerShard - 1) / documentsPerShard;
        Console.WriteLine($"{files.Length:N0} documents -> {shardCount} shard(s) of up to {documentsPerShard:N0}");
        Console.WriteLine();

        List<string> shardFolders = new(shardCount);

        for (int shard = 0; shard < shardCount; shard++)
        {
            string shardFolder = Path.Combine(indexRoot, $"shard-{shard:D3}");
            Directory.CreateDirectory(shardFolder);
            shardFolders.Add(shardFolder);

            string[] slice = files
                .Skip(shard * documentsPerShard)
                .Take(documentsPerShard)
                .ToArray();

            // The using block is the point: the shard's dictionary and metadata leave memory when
            // it closes, so the next shard starts from the same baseline rather than adding to it.
            using (Index index = new(shardFolder))
            {
                index.Add(slice, new IndexingOptions());

                // Cheap here, because this shard's vocabulary is small.
                index.Optimize();

                Console.WriteLine(
                    $"  shard {shard + 1,3}/{shardCount}  {slice.Length,6:N0} docs  " +
                    $"{index.IndexInfo.TermCount,12:N0} terms  {index.IndexInfo.SegmentCount} segment(s)");
            }
        }

        // ---- query every shard as one logical index -------------------------------------------
        Console.WriteLine();
        Console.WriteLine($"Loading {shardFolders.Count} shard(s) into an IndexRepository...");

        using IndexRepository repository = new();
        foreach (string shardFolder in shardFolders)
        {
            repository.AddToRepository(shardFolder);
        }

        const string query = "information";
        SearchResult result = repository.Search(query);

        Console.WriteLine(
            $"Search '{query}' across all shards: {result.DocumentCount:N0} document(s), " +
            $"{result.OccurrenceCount:N0} occurrence(s)");
        Console.WriteLine();
        Console.WriteLine("Search semantics are unchanged - the repository federates the query.");
        return 0;
    }
}
