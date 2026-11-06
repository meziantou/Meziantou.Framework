namespace Meziantou.Framework.Charts;

/// <summary>Fits labels in the space a chart gives them.</summary>
internal static class TextFitting
{
    public const string Ellipsis = "…";

    /// <summary>Shortens a text by whole grapheme clusters and appends an ellipsis, so it fits a width.</summary>
    /// <returns>The text, the shortened text, or <see langword="null"/> when not even the first character fits.</returns>
    public static string? Truncate(ITextMeasurer measurer, string text, ChartFont font, double maxWidth)
    {
        if (measurer.Measure(text, font).Width <= maxWidth)
            return text;

        var boundaries = new List<int>();
        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            boundaries.Add(range.End.GetOffset(text.Length));
        }

        // The longest prefix that fits with the ellipsis, found by bisection
        var low = 0;
        var high = boundaries.Count - 1;
        string? best = null;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var candidate = string.Concat(text.AsSpan(0, boundaries[middle - 1]).TrimEnd(), Ellipsis);
            if (measurer.Measure(candidate, font).Width <= maxWidth)
            {
                best = candidate;
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best;
    }

    /// <summary>Indicates whether a centered label of a width and a height fits in a circle.</summary>
    public static bool FitsInCircle(TextMetrics metrics, double radius)
    {
        var halfWidth = metrics.Width / 2;
        var halfHeight = metrics.Height / 2;
        return (halfWidth * halfWidth) + (halfHeight * halfHeight) <= radius * radius;
    }
}
