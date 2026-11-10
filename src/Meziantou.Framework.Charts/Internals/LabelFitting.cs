namespace Meziantou.Framework.Charts;

/// <summary>Fits labels in the shapes of a chart.</summary>
internal static class LabelFitting
{
    /// <summary>Indicates whether a centered label of a width and a height fits in a circle.</summary>
    public static bool FitsInCircle(TextMetrics metrics, double radius)
    {
        var halfWidth = metrics.Width / 2;
        var halfHeight = metrics.Height / 2;
        return (halfWidth * halfWidth) + (halfHeight * halfHeight) <= radius * radius;
    }
}
