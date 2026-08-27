using System.Diagnostics;

namespace IndexingLab.Core;

/// <summary>
/// Samples process memory on a background thread.
/// </summary>
/// <remarks>
/// The peak has to be sampled on a timer rather than at batch boundaries. Batch sizes differ
/// between runs and between configurations, so a peak recorded only at commit points would be
/// measuring the batching cadence instead of the workload — and would flatter whichever run
/// committed least often.
/// </remarks>
public sealed class MemorySampler : IDisposable
{
    private const int SampleIntervalMilliseconds = 50;

    private readonly Process process = Process.GetCurrentProcess();
    private readonly CancellationTokenSource cancellation = new();

    private Thread? thread;
    private long peakPrivateBytes;
    private long peakWorkingSetBytes;
    private long lastPrivateBytes;
    private long baselineAllocated;
    private bool disposed;

    public long PeakPrivateBytes => Interlocked.Read(ref peakPrivateBytes);

    public long PeakWorkingSetBytes => Interlocked.Read(ref peakWorkingSetBytes);

    public long LastPrivateBytes => Interlocked.Read(ref lastPrivateBytes);

    public long TotalAllocatedBytes => GC.GetTotalAllocatedBytes(precise: false) - baselineAllocated;

    public void Start()
    {
        baselineAllocated = GC.GetTotalAllocatedBytes(precise: false);

        thread = new Thread(SampleLoop)
        {
            IsBackground = true,
            Name = "memory-sampler",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
    }

    public void StopSampling()
    {
        if (thread is null)
        {
            return;
        }

        cancellation.Cancel();
        thread.Join(TimeSpan.FromSeconds(2));
        thread = null;
    }

    private void SampleLoop()
    {
        CancellationToken token = cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            Sample();
            token.WaitHandle.WaitOne(SampleIntervalMilliseconds);
        }
    }

    private void Sample()
    {
        try
        {
            process.Refresh();
            long priv = process.PrivateMemorySize64;
            long ws = process.WorkingSet64;

            Interlocked.Exchange(ref lastPrivateBytes, priv);
            RaiseTo(ref peakPrivateBytes, priv);
            RaiseTo(ref peakWorkingSetBytes, ws);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void RaiseTo(ref long target, long candidate)
    {
        long observed = Interlocked.Read(ref target);
        while (candidate > observed)
        {
            long previous = Interlocked.CompareExchange(ref target, candidate, observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    public TimelineSample Capture(
        int batch,
        double elapsedSeconds,
        int documentsIndexed,
        long sourceBytesConsumed,
        long indexBytesOnDisk,
        int segmentCount,
        long estimatedTermCount = 0)
    {
        Sample();

        return new TimelineSample
        {
            Batch = batch,
            ElapsedSeconds = elapsedSeconds,
            DocumentsIndexed = documentsIndexed,
            SourceBytesConsumed = sourceBytesConsumed,
            IndexBytesOnDisk = indexBytesOnDisk,
            SegmentCount = segmentCount,
            AllocatedBytes = TotalAllocatedBytes,
            ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            PrivateBytes = LastPrivateBytes,
            LargeObjectHeapBytes = GetLargeObjectHeapBytes(),
            EstimatedTermCount = estimatedTermCount,
        };
    }

    /// <summary>
    /// Retained memory after a forced blocking collection. An unforced heap reading counts
    /// uncollected garbage and is not a residency measurement.
    /// </summary>
    public static (long ManagedHeapBytes, long LargeObjectHeapBytes) MeasureRetained()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

        return (GC.GetTotalMemory(forceFullCollection: true), GetLargeObjectHeapBytes());
    }

    private static long GetLargeObjectHeapBytes()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;

        // gen0, gen1, gen2, LOH, POH
        const int LargeObjectHeapIndex = 3;
        return generations.Length > LargeObjectHeapIndex
            ? generations[LargeObjectHeapIndex].SizeAfterBytes
            : 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopSampling();
        cancellation.Dispose();
        process.Dispose();
    }
}
