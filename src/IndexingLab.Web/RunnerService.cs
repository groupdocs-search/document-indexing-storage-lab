using System.Collections.Concurrent;
using IndexingLab.Core;

namespace IndexingLab.Web;

/// <summary>
/// Owns the single in-flight run and keeps it alive independently of any browser connection.
/// </summary>
/// <remarks>
/// Runs are hours long, so nothing about their lifetime may depend on a Blazor circuit. The UI
/// only ever reads state from here and from the session store; closing the page, reloading it or
/// restarting the browser has no effect on the run.
/// </remarks>
public sealed class RunnerService : IDisposable
{
    private readonly SessionStore store;
    private readonly ILogger<RunnerService> logger;
    private readonly ConcurrentQueue<string> logLines = new();

    private CancellationTokenSource? cancellation;
    private Task? runTask;
    private string? activeSessionId;

    public RunnerService(SessionStore store, ILogger<RunnerService> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public SessionStore Store => store;

    public bool IsRunning => runTask is { IsCompleted: false };

    public string? ActiveSessionId => activeSessionId;

    /// <summary>Fires whenever the runner logs a line, so the UI can refresh without polling hard.</summary>
    public event Action? StateChanged;

    public IReadOnlyList<string> RecentLog => [.. logLines];

    public string Start(RunConfiguration configuration, string label)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A run is already in progress.");
        }

        logLines.Clear();
        cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;

        string pendingId = SessionStore.NewSessionId(DateTime.UtcNow);
        activeSessionId = null;

        runTask = Task.Run(
            () =>
            {
                IndexRunner runner = new(store, Log);
                SessionRecord session = runner.Run(configuration, label, token);
                activeSessionId = session.Id;
                Log($"Session {session.Id} finished with status {session.Status}.");
                StateChanged?.Invoke();
            },
            CancellationToken.None);

        return pendingId;
    }

    public void Cancel()
    {
        cancellation?.Cancel();
        Log("Cancellation requested; the current batch will finish first.");
    }

    private void Log(string line)
    {
        string stamped = $"{DateTime.Now:HH:mm:ss}  {line}";
        logger.LogInformation("{Line}", line);
        logLines.Enqueue(stamped);

        while (logLines.Count > 500 && logLines.TryDequeue(out _))
        {
        }

        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
