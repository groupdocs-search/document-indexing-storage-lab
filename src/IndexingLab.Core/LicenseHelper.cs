using GroupDocs.Search;

namespace IndexingLab.Core;

/// <summary>
/// Applies a licence from the environment. An unlicensed run is document-capped, which makes
/// every number in the session meaningless, so the outcome is recorded in the session record.
/// </summary>
public static class LicenseHelper
{
    public const string PrimaryVariable = "GROUPDOCS_LIC_PATH";
    public const string FallbackFolderVariable = "LIC_PATH";
    private const string FallbackRelativePath = "Conholdate/Total/Subscription/Valid/Conholdate.Total.Product.Family.lic";

    public static bool TryApply(out string note)
    {
        string? path = Environment.GetEnvironmentVariable(PrimaryVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            string? folder = Environment.GetEnvironmentVariable(FallbackFolderVariable);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                string relative = FallbackRelativePath
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                path = Path.Combine(folder, relative);
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            note = $"NO LICENCE - set {PrimaryVariable}. Trial limits apply and results are NOT representative.";
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
}
