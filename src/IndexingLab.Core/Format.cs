using System.Globalization;

namespace IndexingLab.Core;

public static class Format
{
    public static string Bytes(long bytes)
    {
        const double K = 1024, M = K * 1024, G = M * 1024, T = G * 1024;
        double abs = Math.Abs((double)bytes);
        return abs switch
        {
            >= T => (bytes / T).ToString("F2", CultureInfo.InvariantCulture) + " TB",
            >= G => (bytes / G).ToString("F2", CultureInfo.InvariantCulture) + " GB",
            >= M => (bytes / M).ToString("F1", CultureInfo.InvariantCulture) + " MB",
            >= K => (bytes / K).ToString("F0", CultureInfo.InvariantCulture) + " KB",
            _ => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        };
    }

    public static string Duration(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");

    public static string Percent(double ratio) =>
        (ratio * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";
}
