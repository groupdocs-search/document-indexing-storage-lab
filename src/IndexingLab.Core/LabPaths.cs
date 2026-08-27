namespace IndexingLab.Core;

/// <summary>
/// Resolves the relative paths shipped in configuration.
/// </summary>
/// <remarks>
/// Relative paths resolve against the <b>repository root</b>, not the executable directory.
/// That distinction matters: resolving against the executable puts sessions under
/// <c>bin/Release/net10.0/</c>, where they are invisible in the project tree and are destroyed by
/// the next <c>dotnet clean</c> or rebuild. Session records are the artifact this tool exists to
/// produce, and losing a twelve-hour run to a rebuild is not an acceptable default.
/// </remarks>
public static class LabPaths
{
    /// <summary>
    /// Resolves <paramref name="path"/> against the repository root when it is relative.
    /// Absolute paths are returned unchanged, so an appsettings.local.json or an environment
    /// variable can still point anywhere.
    /// </summary>
    public static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(RepositoryRoot, path);
    }

    /// <summary>
    /// Nearest ancestor of the executable containing a <c>.sln</c>. Falls back to the executable
    /// directory when there is none — a published copy deployed on its own still works, it just
    /// keeps its sessions beside itself.
    /// </summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot(AppContext.BaseDirectory);

    private static string FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);

        while (directory is not null)
        {
            try
            {
                if (directory.EnumerateFiles("*.sln").Any())
                {
                    return directory.FullName;
                }
            }
            catch (UnauthorizedAccessException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }

            directory = directory.Parent;
        }

        return startDirectory;
    }
}
