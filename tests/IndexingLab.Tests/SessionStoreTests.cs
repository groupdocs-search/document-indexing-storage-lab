using IndexingLab.Core;

namespace IndexingLab.Tests;

/// <summary>
/// Sessions are the durable artifact, and a multi-hour run flushes its record repeatedly while
/// the UI reads the same folder. These tests cover the round trip, the ordering the UI relies on,
/// and the write-then-replace behaviour that stops a killed run leaving a truncated file.
/// </summary>
public sealed class SessionStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "lab-sessions-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static SessionRecord Sample(string id, DateTime startedUtc) => new()
    {
        Id = id,
        Label = "sample",
        Status = SessionStatus.Completed,
        StartedUtc = startedUtc,
        Environment = new EnvironmentInfo { SearchAssemblyVersion = "26.8.0.0", IsServerGC = true, IsLicensed = true },
        Configuration = new RunConfiguration { CorpusRootPath = @"d:\corpus", SourceBudgetBytes = 1024 },
        Corpus = new CorpusFingerprint { FilesSelected = 3, SourceBytesSelected = 300, SelectionHash = "ABCD" },
        Outcome = new RunOutcome { DocumentsIndexed = 3, PeakPrivateBytes = 1234 },
    };

    [Fact]
    public void CreatesTheFolderItIsPointedAt()
    {
        SessionStore store = new(folder);
        Assert.True(Directory.Exists(folder));
        Assert.Equal(folder, store.RootPath);
    }

    [Fact]
    public void RoundTripsEveryFieldTheCompareToolWillNeed()
    {
        SessionStore store = new(folder);
        SessionRecord original = Sample("s1", new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc));
        original.StorageBeforeOptimize = new IndexStorageBreakdown
        {
            TotalBytes = 500,
            TermOffsetBytes = 200,
            EstimatedTermCount = 50,
        };

        store.Save(original);
        SessionRecord? loaded = store.Load("s1");

        Assert.NotNull(loaded);
        Assert.Equal("26.8.0.0", loaded!.Environment.SearchAssemblyVersion);
        Assert.True(loaded.Environment.IsServerGC);
        Assert.Equal("ABCD", loaded.Corpus.SelectionHash);
        Assert.Equal(1024, loaded.Configuration.SourceBudgetBytes);
        Assert.Equal(1234, loaded.Outcome.PeakPrivateBytes);
        Assert.Equal(50, loaded.StorageBeforeOptimize!.EstimatedTermCount);
        Assert.Equal(SessionRecord.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void ListReturnsNewestFirst()
    {
        SessionStore store = new(folder);
        store.Save(Sample("old", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        store.Save(Sample("new", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
        store.Save(Sample("mid", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)));

        IReadOnlyList<SessionRecord> listed = store.List();

        Assert.Equal(["new", "mid", "old"], listed.Select(s => s.Id));
    }

    [Fact]
    public void SavingTwiceOverwritesRatherThanDuplicating()
    {
        // A running session is flushed every few batches, so this happens hundreds of times.
        SessionStore store = new(folder);
        SessionRecord session = Sample("s1", DateTime.UtcNow);

        store.Save(session);
        session.Outcome.DocumentsIndexed = 99;
        store.Save(session);

        Assert.Single(store.List());
        Assert.Equal(99, store.Load("s1")!.Outcome.DocumentsIndexed);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void UnknownSessionLoadsAsNull()
    {
        SessionStore store = new(folder);
        Assert.Null(store.Load("nope"));
    }

    [Fact]
    public void CorruptFileIsSkippedRatherThanBreakingTheList()
    {
        // A file caught mid-flush by an unlucky read must not take out the whole session list.
        SessionStore store = new(folder);
        store.Save(Sample("good", DateTime.UtcNow));
        File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json");

        Assert.Single(store.List());
        Assert.Null(store.Load("broken"));
    }

    [Fact]
    public void DeleteRemovesTheSession()
    {
        SessionStore store = new(folder);
        store.Save(Sample("s1", DateTime.UtcNow));
        store.Delete("s1");

        Assert.Empty(store.List());
    }

    [Fact]
    public void SessionIdsSortChronologicallyAsText()
    {
        // The id doubles as the file name, so lexical order must match time order.
        string earlier = SessionStore.NewSessionId(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        string later = SessionStore.NewSessionId(new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc));

        Assert.StartsWith("20260102-030405-", earlier);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }
}
