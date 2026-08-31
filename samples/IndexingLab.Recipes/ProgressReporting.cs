using GroupDocs.Search.Common;
using GroupDocs.Search.Options;
using IndexingLab.Core;
using Index = GroupDocs.Search.Index;

namespace IndexingLab.Recipes;

/// <summary>
/// Progress reporting for <c>Add()</c> and <c>Optimize()</c>, plus an asynchronous cancellable merge.
/// </summary>
/// <remarks>
/// This exists because "we cannot optimise rarely, our users would see an unexplained freeze" is a
/// common and reasonable objection — and it is already solvable. Both events below ship in the
/// product today. <c>OptimizationProgressChanged</c> fires once per merged segment, so a merge over
/// a many-segment index reports steadily rather than sitting silent.
/// <para>
/// Optimising rarely matters: <c>Optimize()</c> goes through the same expensive commit path as
/// <c>Add()</c>, so calling it on a tight schedule multiplies that cost rather than amortising it.
/// </para>
/// </remarks>
public static class ProgressReporting
{
    public static int Run(string corpusFolder, string indexFolder)
    {
        LicenseHelper.TryApply(out string licenceNote);
        Console.WriteLine(licenceNote);

        Directory.CreateDirectory(indexFolder);
        using Index index = new(indexFolder);

        // ---- 1. progress while documents are being added ------------------------------------
        index.Events.OperationProgressChanged += (sender, e) =>
            Console.WriteLine(
                $"  indexing  {e.ProcessedDocuments,7:N0}/{e.TotalDocuments,-7:N0} " +
                $"({e.ProgressPercentage,5:F1}%)  skipped {e.SkippedDocuments}  {Short(e.LastDocumentKey)}");

        // ---- 2. progress while segments are being merged -------------------------------------
        index.Events.OptimizationProgressChanged += (sender, e) =>
            Console.WriteLine(
                $"  merging   segment {e.ProcessedSegments,4}/{e.TotalSegments,-4} ({e.ProgressPercentage,5:F1}%)");

        index.Events.ErrorOccurred += (sender, e) =>
            Console.WriteLine($"  ! {Short(e.Message)}");

        string[] files = Directory.GetFiles(corpusFolder, "*", SearchOption.AllDirectories);
        Console.WriteLine($"Adding {files.Length:N0} documents from {corpusFolder}");
        index.Add(files, new IndexingOptions());

        Console.WriteLine();
        Console.WriteLine($"Indexed {index.IndexInfo.TermCount:N0} terms across {index.IndexInfo.SegmentCount} segment(s).");
        Console.WriteLine();

        // ---- 3. optimise in the background, cancellable --------------------------------------
        // IsAsync returns immediately, so a UI stays responsive; Cancellation lets a user abort a
        // merge that is taking longer than they are willing to wait.
        Cancellation cancellation = new();
        MergeOptions mergeOptions = new()
        {
            IsAsync = true,
            Cancellation = cancellation,
        };

        Console.WriteLine("Optimize() started asynchronously. Press C to cancel.");
        index.Optimize(mergeOptions);

        // Console.KeyAvailable throws when stdin is redirected, which is exactly what happens when
        // this runs from a script or CI, so the interactive path has to be optional.
        if (!Console.IsInputRedirected &&
            Console.KeyAvailable &&
            char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar) == 'c')
        {
            Console.WriteLine("Cancelling the merge...");
            cancellation.Cancel();
        }

        // The async call returned immediately, so wait for the index to settle before reporting.
        while (index.IndexInfo.IndexStatus is IndexStatus.Merging or IndexStatus.Optimizing)
        {
            Thread.Sleep(500);
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {index.IndexInfo.SegmentCount} segment(s), {index.IndexInfo.TermCount:N0} terms.");
        Console.WriteLine($"Index folder: {indexFolder}");
        return 0;
    }

    private static string Short(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Length <= 60 ? value : "..." + value[^57..];
}
