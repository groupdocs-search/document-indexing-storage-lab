using IndexingLab.Core;

namespace IndexingLab.Tests;

/// <summary>
/// The licence path comes from one environment variable and nothing else. A folder-plus-convention
/// fallback used to fill in a guessed path, which turns a typo into an unlicensed run that still
/// produces a complete-looking session. These tests pin the single-source rule and the wording that
/// tells the operator which of the two failure modes they hit.
///
/// They exercise <see cref="LicenseHelper.TryResolvePath"/> rather than <c>TryApply</c> on purpose:
/// the suite must never load a GroupDocs assembly at run time.
/// </summary>
public sealed class LicenseHelperTests
{
    private static bool NoFileExists(string path) => false;

    [Fact]
    public void ResolvesTheConfiguredPathWhenTheFileExists()
    {
        bool resolved = LicenseHelper.TryResolvePath(
            @"C:\licences\GroupDocs.Total.lic",
            path => path == @"C:\licences\GroupDocs.Total.lic",
            out string path,
            out string note);

        Assert.True(resolved);
        Assert.Equal(@"C:\licences\GroupDocs.Total.lic", path);
        Assert.Equal(string.Empty, note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReportsAnUnsetVariable(string? configured)
    {
        bool resolved = LicenseHelper.TryResolvePath(configured, NoFileExists, out string path, out string note);

        Assert.False(resolved);
        Assert.Equal(string.Empty, path);
        Assert.Contains(LicenseHelper.PrimaryVariable, note);
        Assert.Contains("is not set", note);
        Assert.Contains("NOT representative", note);
    }

    [Fact]
    public void ReportsAMissingFileDistinctlyFromAnUnsetVariable()
    {
        bool resolved = LicenseHelper.TryResolvePath(
            @"D:\gone\GroupDocs.Total.lic",
            NoFileExists,
            out string path,
            out string note);

        Assert.False(resolved);
        Assert.Equal(string.Empty, path);
        Assert.Contains(@"D:\gone\GroupDocs.Total.lic", note);
        Assert.Contains("does not exist", note);
    }

    [Fact]
    public void DoesNotFallBackToAFolderConvention()
    {
        // The old behaviour appended a fixed relative path to a licences folder held in LIC_PATH.
        // A folder handed in as the licence path must now simply fail as "not a file".
        bool resolved = LicenseHelper.TryResolvePath(
            @"C:\licences",
            path => path.EndsWith(".lic", StringComparison.OrdinalIgnoreCase),
            out string path,
            out string note);

        Assert.False(resolved);
        Assert.Equal(string.Empty, path);
        Assert.Contains("does not exist", note);
    }

    [Fact]
    public void TrimsSurroundingWhitespaceFromTheConfiguredPath()
    {
        bool resolved = LicenseHelper.TryResolvePath(
            @"  C:\licences\GroupDocs.Total.lic  ",
            path => path == @"C:\licences\GroupDocs.Total.lic",
            out string path,
            out string note);

        Assert.True(resolved);
        Assert.Equal(@"C:\licences\GroupDocs.Total.lic", path);
    }
}
