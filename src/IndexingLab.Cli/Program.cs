using System.Text.Json;
using IndexingLab.Core;

// ---------------------------------------------------------------------------
// Headless runner. Same core as the web UI, for unattended runs where you do
// not want a web host alive for hours. Sessions land in the same folder, so
// the UI lists CLI runs alongside its own.
//
//   IndexingLab.Cli [profileName] [--sessions <path>]
// ---------------------------------------------------------------------------

try
{
    string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"ERROR: {configPath} not found.");
        return 1;
    }

    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
    JsonElement root = document.RootElement;

    string sessionsPath = LabPaths.Resolve(
        ArgValue(args, "--sessions")
            ?? Environment.GetEnvironmentVariable("INDEXING_LAB_SESSIONS")
            ?? root.GetProperty("Lab").GetProperty("SessionsPath").GetString()
            ?? "sessions");

    string profileName = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "default";

    JsonElement profiles = root.GetProperty("Profiles");
    if (!profiles.TryGetProperty(profileName, out JsonElement profile))
    {
        Console.Error.WriteLine($"ERROR: profile '{profileName}' not found. Available:");
        foreach (JsonProperty p in profiles.EnumerateObject())
        {
            Console.Error.WriteLine($"  {p.Name}");
        }

        return 1;
    }

    RunConfiguration configuration = JsonSerializer.Deserialize<RunConfiguration>(
        profile.GetProperty("Configuration").GetRawText(),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    string label = profile.TryGetProperty("Label", out JsonElement l)
        ? l.GetString() ?? profileName
        : profileName;

    // Shipped profiles carry relative paths and no corpus, so a fresh clone runs anywhere and
    // nobody inherits someone else's drive layout. Point them at real folders here.
    configuration.CorpusRootPath = Environment.GetEnvironmentVariable("INDEXING_LAB_CORPUS")
        ?? configuration.CorpusRootPath;

    if (string.IsNullOrWhiteSpace(configuration.CorpusRootPath))
    {
        Console.Error.WriteLine("ERROR: no corpus folder configured.");
        Console.Error.WriteLine("  Set INDEXING_LAB_CORPUS to a folder of documents, or fill in");
        Console.Error.WriteLine($"  CorpusRootPath for the '{profileName}' profile in appsettings.json.");
        return 1;
    }

    configuration.IndexFolderPath = LabPaths.Resolve(configuration.IndexFolderPath);

    SessionStore store = new(sessionsPath);
    Console.WriteLine($"Sessions folder : {store.RootPath}");
    Console.WriteLine($"Profile         : {profileName}");

    using CancellationTokenSource cancellation = new();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine("Cancelling after the current batch...");
        cancellation.Cancel();
    };

    IndexRunner runner = new(store, Console.WriteLine);
    SessionRecord session = runner.Run(configuration, label, cancellation.Token);

    Console.WriteLine();
    Console.WriteLine($"Session {session.Id} -> {session.Status}");
    Console.WriteLine($"Saved to {store.GetPath(session.Id)}");

    return session.Status == SessionStatus.Completed ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ERROR: {exception.Message}");
    return 1;
}

static string? ArgValue(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
