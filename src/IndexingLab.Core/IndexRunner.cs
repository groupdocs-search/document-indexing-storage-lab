using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using GroupDocs.Search;
using GroupDocs.Search.Common;
using GroupDocs.Search.Dictionaries;
using GroupDocs.Search.Events;
using GroupDocs.Search.Options;
using Index = GroupDocs.Search.Index;

namespace IndexingLab.Core;

/// <summary>Executes one profiling run and keeps its <see cref="SessionRecord"/> up to date.</summary>
public sealed class IndexRunner
{
    private const int FlushEveryBatches = 5;

    private readonly SessionStore store;
    private readonly Action<string>? log;
    private readonly Action<SessionRecord>? onProgress;

    private readonly object errorSync = new();
    private int errorCount;
    private readonly List<string> firstErrors = [];

    /// <param name="onProgress">
    /// Invoked with the live session after every batch. The UI reads structured metrics from this
    /// rather than parsing the log, which is why the log stays purely human-readable.
    /// </param>
    public IndexRunner(SessionStore store, Action<string>? log = null, Action<SessionRecord>? onProgress = null)
    {
        this.store = store;
        this.log = log;
        this.onProgress = onProgress;
    }

    public SessionRecord Run(RunConfiguration configuration, string label, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        DateTime startedUtc = DateTime.UtcNow;
        SessionRecord session = new()
        {
            Id = SessionStore.NewSessionId(startedUtc),
            Label = string.IsNullOrWhiteSpace(label) ? "run" : label,
            Status = SessionStatus.Running,
            StartedUtc = startedUtc,
            Configuration = configuration,
        };

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            bool licensed = LicenseHelper.TryApply(out string licenseNote);
            log?.Invoke(licenseNote);

            session.Environment = CaptureEnvironment(licensed);
            store.Save(session);

            log?.Invoke($"Scanning corpus {configuration.CorpusRootPath} ...");
            CorpusSelection selection = CorpusWalker.Select(configuration, cancellationToken);
            session.Corpus = selection.Fingerprint;
            store.Save(session);
            onProgress?.Invoke(session);

            log?.Invoke($"Selected {selection.Files.Count:N0} files, {Format.Bytes(selection.TotalBytes)}");

            if (selection.Files.Count == 0)
            {
                throw new InvalidOperationException(
                    "No files matched the corpus filters. Check the root path, include/exclude extensions and max file size.");
            }

            RunIndexing(session, configuration, selection, stopwatch, cancellationToken);

            session.Status = SessionStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            session.Status = SessionStatus.Cancelled;
            log?.Invoke("Cancelled.");

            // Analyse whatever was built before stopping. Cancellation used to discard this,
            // which meant abandoning a multi-hour run also threw away its single most valuable
            // output - and the index is sitting on disk either way, so it costs a folder walk.
            if (session.StorageBeforeOptimize is null && Directory.Exists(configuration.IndexFolderPath))
            {
                session.StorageBeforeOptimize = IndexStorageAnalyzer.Analyze(
                    configuration.IndexFolderPath,
                    session.Outcome.SourceBytesConsumed,
                    configuration.UseTextStorage);
                LogBreakdown("at cancellation", session.StorageBeforeOptimize);
            }
        }
        catch (Exception exception)
        {
            session.Status = SessionStatus.Failed;
            session.FailureMessage = exception.Message;
            log?.Invoke($"FAILED: {exception.Message}");
        }
        finally
        {
            stopwatch.Stop();
            session.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            session.CompletedUtc = DateTime.UtcNow;

            lock (errorSync)
            {
                session.Errors = [.. firstErrors];
                session.Outcome.ExtractionErrors = errorCount;
            }

            store.Save(session);
            onProgress?.Invoke(session);
        }

        return session;
    }

    private void RunIndexing(
        SessionRecord session,
        RunConfiguration configuration,
        CorpusSelection selection,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(configuration.IndexFolderPath);
        if (Directory.EnumerateFileSystemEntries(configuration.IndexFolderPath).Any())
        {
            Directory.Delete(configuration.IndexFolderPath, recursive: true);
            Directory.CreateDirectory(configuration.IndexFolderPath);
        }

        IndexSettings settings = new()
        {
            IndexType = Enum.Parse<IndexType>(configuration.IndexType, ignoreCase: true),
            UseStopWords = configuration.UseStopWords,
            MaxIndexingReportCount = configuration.MaxIndexingReportCount,
            MaxSearchReportCount = 0,
        };

        if (configuration.UseTextStorage)
        {
            settings.TextStorageSettings = new TextStorageSettings(
                Enum.Parse<Compression>(configuration.TextStorageCompression, ignoreCase: true));
        }

        using Index index = new(configuration.IndexFolderPath, settings);
        index.Events.ErrorOccurred += OnErrorOccurred;

        // Reclassifying characters changes tokenisation, so it must happen before any document
        // is added. Digits as separators is the main lever against runaway vocabularies.
        if (!string.IsNullOrEmpty(configuration.SeparatorCharacters))
        {
            index.Dictionaries.Alphabet.SetRange(
                configuration.SeparatorCharacters.ToCharArray(),
                CharacterType.Separator);
            log?.Invoke($"Alphabet: '{configuration.SeparatorCharacters}' set as separators");
        }

        IndexingOptions indexingOptions = new() { Threads = configuration.IndexingThreads };

        using MemorySampler sampler = new();
        sampler.Start();

        int documentsIndexed = 0;
        int batches = 0;
        long sourceBytesConsumed = 0;

        foreach (List<CorpusFile> batch in Batch(selection.Files, configuration))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Document[] documents = new Document[batch.Count];
            for (int i = 0; i < batch.Count; i++)
            {
                documents[i] = Document.CreateFromFile(batch[i].Path);
            }

            index.Add(documents, indexingOptions);

            batches++;
            documentsIndexed += batch.Count;
            sourceBytesConsumed += batch.Sum(f => f.Length);

            long indexBytes = IndexStorageAnalyzer.MeasureIndexBytes(configuration.IndexFolderPath);
            int segmentCount = IndexStorageAnalyzer.CountSegments(configuration.IndexFolderPath);
            long termCount = IndexStorageAnalyzer.EstimateTermCount(configuration.IndexFolderPath);

            session.Timeline.Add(sampler.Capture(
                batches,
                stopwatch.Elapsed.TotalSeconds,
                documentsIndexed,
                sourceBytesConsumed,
                indexBytes,
                segmentCount,
                termCount));

            session.Outcome.DocumentsAttempted = documentsIndexed;
            session.Outcome.BatchesCommitted = batches;
            session.Outcome.SourceBytesConsumed = sourceBytesConsumed;
            session.Outcome.PeakPrivateBytes = sampler.PeakPrivateBytes;
            session.Outcome.TotalAllocatedBytes = sampler.TotalAllocatedBytes;
            session.Outcome.ThroughputMBPerSecond = stopwatch.Elapsed.TotalSeconds > 0
                ? sourceBytesConsumed / 1024d / 1024d / stopwatch.Elapsed.TotalSeconds
                : 0;
            lock (errorSync)
            {
                session.Outcome.ExtractionErrors = errorCount;
            }

            onProgress?.Invoke(session);

            log?.Invoke(
                $"batch {batches} | docs {documentsIndexed:N0} | src {Format.Bytes(sourceBytesConsumed)} | " +
                $"index {Format.Bytes(indexBytes)} | segs {segmentCount} | " +
                $"private {Format.Bytes(sampler.LastPrivateBytes)} | {stopwatch.Elapsed:hh\\:mm\\:ss}");

            if (batches % FlushEveryBatches == 0)
            {
                lock (errorSync)
                {
                    session.Outcome.ExtractionErrors = errorCount;
                }

                store.Save(session);
            }
        }

        sampler.StopSampling();

        session.Outcome.DocumentsIndexed = index.GetIndexedDocuments().Length;
        session.Outcome.TotalAllocatedBytes = sampler.TotalAllocatedBytes;
        session.Outcome.PeakPrivateBytes = sampler.PeakPrivateBytes;
        session.Outcome.PeakWorkingSetBytes = sampler.PeakWorkingSetBytes;
        session.Outcome.Gen0Collections = GC.CollectionCount(0);
        session.Outcome.Gen1Collections = GC.CollectionCount(1);
        session.Outcome.Gen2Collections = GC.CollectionCount(2);
        session.Outcome.ThroughputMBPerSecond = stopwatch.Elapsed.TotalSeconds > 0
            ? sourceBytesConsumed / 1024d / 1024d / stopwatch.Elapsed.TotalSeconds
            : 0;

        (long retainedHeap, long retainedLoh) = MemorySampler.MeasureRetained();
        session.Outcome.RetainedManagedHeapBytes = retainedHeap;
        session.Outcome.RetainedLargeObjectHeapBytes = retainedLoh;

        session.StorageBeforeOptimize = IndexStorageAnalyzer.Analyze(
            configuration.IndexFolderPath,
            sourceBytesConsumed,
            configuration.UseTextStorage);

        store.Save(session);
        LogBreakdown("before Optimize()", session.StorageBeforeOptimize);

        if (configuration.OptimizeAtEnd)
        {
            log?.Invoke("Running Optimize() ...");
            try
            {
                index.Optimize();
                session.StorageAfterOptimize = IndexStorageAnalyzer.Analyze(
                    configuration.IndexFolderPath,
                    sourceBytesConsumed,
                    configuration.UseTextStorage);
                LogBreakdown("after Optimize()", session.StorageAfterOptimize);
            }
            catch (Exception exception)
            {
                // Optimize takes the same deep-copy path as indexing and allocates an
                // int[vocabulary] per merge step, so failing here at scale is a result worth
                // recording rather than an error worth aborting the whole session for.
                session.OptimizeFailureMessage = exception.Message;
                log?.Invoke($"Optimize() failed: {exception.Message}");
            }
        }

        store.Save(session);
    }

    private void LogBreakdown(string stage, IndexStorageBreakdown? breakdown)
    {
        if (breakdown is null)
        {
            return;
        }

        log?.Invoke(
            $"index {stage}: total {Format.Bytes(breakdown.TotalBytes)} | " +
            $"postings {Format.Bytes(breakdown.PostingsBytes)} | " +
            $"term offsets {Format.Bytes(breakdown.TermOffsetBytes)} | " +
            $"dictionary {Format.Bytes(breakdown.TermDictionaryBytes)} | " +
            $"segments {breakdown.SegmentCount} | terms {breakdown.EstimatedTermCount:N0} | " +
            $"ratio {breakdown.RatioToSourceBytes:P1}");
    }

    /// <summary>
    /// Groups files into byte-bounded batches, mirroring the customer's own pipeline, which
    /// flushes on accumulated size rather than on a document count.
    /// </summary>
    private static IEnumerable<List<CorpusFile>> Batch(List<CorpusFile> files, RunConfiguration configuration)
    {
        List<CorpusFile> current = [];
        long currentBytes = 0;

        foreach (CorpusFile file in files)
        {
            current.Add(file);
            currentBytes += file.Length;

            bool sizeReached = configuration.BatchSizeBytes > 0 && currentBytes >= configuration.BatchSizeBytes;
            bool countReached = configuration.MaxDocumentsPerBatch > 0 && current.Count >= configuration.MaxDocumentsPerBatch;

            if (sizeReached || countReached)
            {
                yield return current;
                current = [];
                currentBytes = 0;
            }
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private void OnErrorOccurred(object? sender, IndexErrorEventArgs e)
    {
        Interlocked.Increment(ref errorCount);
        lock (errorSync)
        {
            if (firstErrors.Count < 20)
            {
                string message = e.Message.ReplaceLineEndings(" ");
                firstErrors.Add(message.Length > 300 ? message[..300] + "..." : message);
            }
        }
    }

    private static EnvironmentInfo CaptureEnvironment(bool licensed)
    {
        long physicalMemory = 0;
        try
        {
            physicalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch (PlatformNotSupportedException)
        {
        }

        return new EnvironmentInfo
        {
            SearchAssemblyVersion = typeof(Index).Assembly.GetName().Version?.ToString() ?? "unknown",
            SearchPackageVersion = typeof(Index).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? string.Empty,
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            MachineName = System.Environment.MachineName,
            ProcessorCount = System.Environment.ProcessorCount,
            IsServerGC = GCSettings.IsServerGC,
            IsConcurrentGC = GCSettings.LatencyMode != GCLatencyMode.Batch,
            BuildConfiguration = BuildConfiguration,
            IsLicensed = licensed,
            TotalPhysicalMemoryBytes = physicalMemory,
        };
    }

    private static string BuildConfiguration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif
}
