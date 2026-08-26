using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndexingLab.Core;

/// <summary>
/// Reads and writes session records as JSON files in one folder.
/// </summary>
/// <remarks>
/// Deliberately a folder of files rather than a database: sessions are the artifact we want to
/// keep, copy between machines, diff, and commit next to a report. One file per session, named by
/// the sortable session id, so listing is a directory read and the newest sorts last by name.
/// </remarks>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string rootPath;
    private readonly object writeSync = new();

    public SessionStore(string rootPath)
    {
        this.rootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    public string RootPath => rootPath;

    public static string NewSessionId(DateTime utcNow) =>
        $"{utcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    public string GetPath(string sessionId) => Path.Combine(rootPath, $"{sessionId}.json");

    public void Save(SessionRecord session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (writeSync)
        {
            string finalPath = GetPath(session.Id);
            string tempPath = finalPath + ".tmp";

            // Write-then-replace: a session flushed every few batches must never be left
            // half-written if the process is killed mid-run, which for multi-hour runs is likely.
            File.WriteAllText(tempPath, JsonSerializer.Serialize(session, Options));

            if (File.Exists(finalPath))
            {
                File.Replace(tempPath, finalPath, null);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
        }
    }

    public SessionRecord? Load(string sessionId)
    {
        string path = GetPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Session summaries, newest first. Reads every file, which is fine for the hundreds of
    /// sessions this tool will realistically accumulate.
    /// </summary>
    public IReadOnlyList<SessionRecord> List()
    {
        List<SessionRecord> sessions = [];

        foreach (string path in Directory.EnumerateFiles(rootPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                SessionRecord? session = JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(path), Options);
                if (session is not null && !string.IsNullOrEmpty(session.Id))
                {
                    sessions.Add(session);
                }
            }
            catch (JsonException)
            {
                // A session being written right now can be caught mid-flush by an unlucky read.
            }
            catch (IOException)
            {
            }
        }

        return [.. sessions.OrderByDescending(s => s.StartedUtc)];
    }

    public void Delete(string sessionId)
    {
        string path = GetPath(sessionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
