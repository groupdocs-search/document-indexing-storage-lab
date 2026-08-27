using System.Globalization;
using System.Text;

namespace IndexingLab.Core;

/// <summary>
/// Builds inline SVG path data from a numeric series.
/// </summary>
/// <remarks>
/// Deliberately dependency-free. A charting library would mean a JS bundle and a build step for a
/// tool whose primary consumer is the session JSON; the shapes needed here are a line and the area
/// under it, which is a few lines of arithmetic.
/// </remarks>
public static class Sparkline
{
    /// <summary>Polyline through the series, scaled to fit a <paramref name="width"/> x <paramref name="height"/> box.</summary>
    public static string Path(IReadOnlyList<double> values, double width, double height, double padding = 2)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (values.Count == 1)
        {
            double mid = height / 2;
            return $"M 0 {F(mid)} L {F(width)} {F(mid)}";
        }

        double max = values.Max();
        double min = values.Min();
        double range = max - min;
        if (range <= 0)
        {
            range = max > 0 ? max : 1;
            min = 0;
        }

        double usable = height - (padding * 2);
        StringBuilder builder = new();

        for (int i = 0; i < values.Count; i++)
        {
            double x = width * i / (values.Count - 1);
            double y = padding + usable - ((values[i] - min) / range * usable);
            builder.Append(i == 0 ? "M " : " L ").Append(F(x)).Append(' ').Append(F(y));
        }

        return builder.ToString();
    }

    /// <summary>The same line closed down to the baseline, for a filled area under the curve.</summary>
    public static string Area(IReadOnlyList<double> values, double width, double height, double padding = 2)
    {
        string line = Path(values, width, height, padding);
        return line.Length == 0
            ? string.Empty
            : $"{line} L {F(width)} {F(height)} L 0 {F(height)} Z";
    }

    private static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
