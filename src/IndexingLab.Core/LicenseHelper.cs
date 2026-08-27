using GroupDocs.Search;

namespace IndexingLab.Core;

/// <summary>
/// Applies a licence from the environment. An unlicensed run is document-capped, which makes
/// every number in the session meaningless, so the outcome is recorded in the session record.
/// </summary>
public static class LicenseHelper
{
    /// <summary>
    /// The only source of a licence: the full path to a <c>.lic</c> file. There is deliberately no
    /// folder-plus-convention fallback -- a wrong guess there produces an unlicensed run that still
    /// looks complete.
    /// </summary>
    public const string PrimaryVariable = "GROUPDOCS_SEARCH_LIC_PATH";

    public static bool TryApply(out string note)
    {
        if (!TryResolvePath(Environment.GetEnvironmentVariable(PrimaryVariable), File.Exists, out string path, out note))
        {
            return false;
        }

        try
        {
            new License().SetLicense(path);
            note = $"Licence applied from {path}";
            return true;
        }
        catch (Exception exception)
        {
            note = $"Licence failed from '{path}': {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Decides which file to license from, with no GroupDocs dependency so the offline test suite
    /// can pin it without loading a product assembly.
    /// </summary>
    public static bool TryResolvePath(string? configuredPath, Func<string, bool> fileExists, out string path, out string note)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            note = $"NO LICENCE - {PrimaryVariable} is not set. Trial limits apply and results are NOT representative.";
            return false;
        }

        string candidate = configuredPath.Trim();

        if (!fileExists(candidate))
        {
            note = $"NO LICENCE - {PrimaryVariable} points at '{candidate}', which does not exist. "
                 + "Trial limits apply and results are NOT representative.";
            return false;
        }

        path = candidate;
        note = string.Empty;
        return true;
    }
}
