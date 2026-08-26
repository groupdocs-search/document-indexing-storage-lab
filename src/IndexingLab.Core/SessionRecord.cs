using System.Text.Json.Serialization;

namespace IndexingLab.Core;

/// <summary>
/// One profiling run, persisted as a single JSON file.
/// </summary>
/// <remarks>
/// This is the durable artifact of the whole tool. A future compare-sessions feature reads
/// these and nothing else, so everything needed to judge whether two runs are comparable
/// has to be in here — product version, runtime, GC mode, corpus fingerprint and index
/// settings included. Treat it as an append-only schema: add fields, never repurpose one,
/// and bump <see cref="SchemaVersion"/> when the shape changes.
/// </remarks>
public sealed class SessionRecord
{
    /// <summary>Increment when the shape changes so old sessions stay readable.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Sortable id: yyyyMMdd-HHmmss-{short guid}. Also the file name.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Free-text label so a human can tell runs apart in the list.</summary>
    public string Label { get; set; } = string.Empty;

    public string Status { get; set; } = SessionStatus.Pending;

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public double ElapsedSeconds { get; set; }

    /// <summary>Populated when Status is Failed.</summary>
    public string? FailureMessage { get; set; }

    public EnvironmentInfo Environment { get; set; } = new();

    public RunConfiguration Configuration { get; set; } = new();

    public CorpusFingerprint Corpus { get; set; } = new();

    public RunOutcome Outcome { get; set; } = new();

    /// <summary>Periodic samples taken during the run.</summary>
    public List<TimelineSample> Timeline { get; set; } = [];

    /// <summary>Index storage breakdown before the optional final Optimize().</summary>
    public IndexStorageBreakdown? StorageBeforeOptimize { get; set; }

    /// <summary>Index storage breakdown after the final Optimize(), when it was run.</summary>
    public IndexStorageBreakdown? StorageAfterOptimize { get; set; }

    /// <summary>Set when Optimize() was requested but did not complete (commonly OOM at scale).</summary>
    public string? OptimizeFailureMessage { get; set; }

    public List<string> Errors { get; set; } = [];
}

public static class SessionStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// What the run executed on. Two sessions are only comparable if these match — the compare
/// tool should warn loudly rather than silently diffing runs from different products or GC modes.
/// </summary>
public sealed class EnvironmentInfo
{
    /// <summary>Resolved GroupDocs.Search assembly version — the thing under test.</summary>
    public string SearchAssemblyVersion { get; set; } = string.Empty;

    /// <summary>Package version as declared in Directory.Packages.props, when discoverable.</summary>
    public string SearchPackageVersion { get; set; } = string.Empty;

    public string RuntimeVersion { get; set; } = string.Empty;

    public string OperatingSystem { get; set; } = string.Empty;

    public string ProcessArchitecture { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public int ProcessorCount { get; set; }

    public bool IsServerGC { get; set; }

    public bool IsConcurrentGC { get; set; }

    public string BuildConfiguration { get; set; } = string.Empty;

    public bool IsLicensed { get; set; }

    public long TotalPhysicalMemoryBytes { get; set; }
}

/// <summary>Everything the operator chose. Replaying a session means replaying this.</summary>
public sealed class RunConfiguration
{
    public string CorpusRootPath { get; set; } = string.Empty;

    public long SourceBudgetBytes { get; set; }

    public int MaxFiles { get; set; }

    public long MaxFileSizeBytes { get; set; }

    /// <summary>Extensions to index. Empty means "anything not excluded".</summary>
    public List<string> IncludeExtensions { get; set; } = [];

    /// <summary>Extensions to skip — images and binaries by default.</summary>
    public List<string> ExcludeExtensions { get; set; } = [];

    /// <summary>
    /// "Stratified" draws proportionally from each immediate subfolder of the root so a partial
    /// budget still reflects the corpus format mix. "Sequential" walks in sorted order and will
    /// fill from the alphabetically first folders only.
    /// </summary>
    public string SamplingMode { get; set; } = "Stratified";

    /// <summary>Bytes of source per Add() call, mirroring the customer's byte-driven batching.</summary>
    public long BatchSizeBytes { get; set; }

    public int MaxDocumentsPerBatch { get; set; }

    public string IndexType { get; set; } = "NormalIndex";

    public bool UseStopWords { get; set; }

    public bool UseTextStorage { get; set; }

    public string TextStorageCompression { get; set; } = "High";

    public int IndexingThreads { get; set; }

    public int MaxIndexingReportCount { get; set; }

    /// <summary>Characters reclassified as separators before indexing, e.g. "0123456789".</summary>
    public string SeparatorCharacters { get; set; } = string.Empty;

    public bool OptimizeAtEnd { get; set; }

    public string IndexFolderPath { get; set; } = string.Empty;
}

/// <summary>
/// Identifies the exact slice of the corpus consumed, so a later run can be checked for
/// having indexed the same material rather than merely the same folder.
/// </summary>
public sealed class CorpusFingerprint
{
    public int FilesSelected { get; set; }

    public long SourceBytesSelected { get; set; }

    /// <summary>SHA-256 over the ordered "relativePath:length" list of selected files.</summary>
    public string SelectionHash { get; set; } = string.Empty;

    /// <summary>Per-extension counts and bytes, so format mix differences are visible.</summary>
    public List<ExtensionBucket> ByExtension { get; set; } = [];
}

public sealed class ExtensionBucket
{
    public string Extension { get; set; } = string.Empty;

    public int FileCount { get; set; }

    public long Bytes { get; set; }
}

public sealed class RunOutcome
{
    public int DocumentsAttempted { get; set; }

    public int DocumentsIndexed { get; set; }

    public int ExtractionErrors { get; set; }

    public long SourceBytesConsumed { get; set; }

    public int BatchesCommitted { get; set; }

    public long TotalAllocatedBytes { get; set; }

    public long PeakPrivateBytes { get; set; }

    public long PeakWorkingSetBytes { get; set; }

    public long RetainedManagedHeapBytes { get; set; }

    public long RetainedLargeObjectHeapBytes { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }

    /// <summary>Source MB processed per second, end to end.</summary>
    public double ThroughputMBPerSecond { get; set; }
}

public sealed class TimelineSample
{
    public int Batch { get; set; }

    public double ElapsedSeconds { get; set; }

    public int DocumentsIndexed { get; set; }

    public long SourceBytesConsumed { get; set; }

    public long IndexBytesOnDisk { get; set; }

    public int SegmentCount { get; set; }

    public long AllocatedBytes { get; set; }

    public long ManagedHeapBytes { get; set; }

    public long PrivateBytes { get; set; }

    public long LargeObjectHeapBytes { get; set; }
}

/// <summary>
/// Where the index bytes actually went. This is the analysis that answers the forum question,
/// so it is captured as data rather than derived in the UI.
/// </summary>
public sealed class IndexStorageBreakdown
{
    public long TotalBytes { get; set; }

    /// <summary>index*.body — the postings.</summary>
    public long PostingsBytes { get; set; }

    /// <summary>index*.head — term-offset tables, 4 bytes per term per segment.</summary>
    public long TermOffsetBytes { get; set; }

    /// <summary>index1.info — the term dictionary.</summary>
    public long TermDictionaryBytes { get; set; }

    /// <summary>index.info — per-document metadata.</summary>
    public long DocumentMetadataBytes { get; set; }

    /// <summary>index0.info — image hash data.</summary>
    public long ImageDataBytes { get; set; }

    /// <summary>Dictionary and tokenisation side files.</summary>
    public long DictionaryFileBytes { get; set; }

    /// <summary>Text archive, present only when text storage is enabled.</summary>
    public long TextStorageBytes { get; set; }

    public long OtherBytes { get; set; }

    public int SegmentCount { get; set; }

    public int EmptySegmentCount { get; set; }

    /// <summary>Largest .head file ÷ 4. The index's vocabulary size.</summary>
    public long EstimatedTermCount { get; set; }

    /// <summary>4 × EstimatedTermCount — what a single merged segment's .head would cost.</summary>
    public long TermOffsetBytesIfSingleSegment { get; set; }

    /// <summary>Recoverable .head overhead: TermOffsetBytes - TermOffsetBytesIfSingleSegment.</summary>
    public long RecoverableTermOffsetBytes { get; set; }

    /// <summary>Index size as a fraction of the source bytes consumed.</summary>
    public double RatioToSourceBytes { get; set; }

    /// <summary>
    /// Index size (excluding the text archive) as a fraction of extracted text volume.
    /// Only meaningful when text storage was enabled — otherwise extracted text is unknown.
    /// </summary>
    public double? RatioToExtractedText { get; set; }

    public List<SegmentDetail> Segments { get; set; } = [];
}

public sealed class SegmentDetail
{
    public int Index { get; set; }

    public long BodyBytes { get; set; }

    public long HeadBytes { get; set; }

    /// <summary>HeadBytes ÷ 4 — vocabulary known when this segment was written.</summary>
    public long TermSlots { get; set; }
}
