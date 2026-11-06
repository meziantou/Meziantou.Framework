namespace Meziantou.Framework.Charts;

/// <summary>Projects the rows of a series to points, split into segments at the missing values.</summary>
internal static class PointSeries
{
    public static List<List<((double X, double Y) Point, int Row)>> Split(RenderContext context, ChannelValues? x, ChannelValues? y, IEnumerable<int> rows)
    {
        var segments = new List<List<((double X, double Y) Point, int Row)>>();
        List<((double X, double Y) Point, int Row)>? current = null;
        foreach (var row in rows)
        {
            var px = Center(context.X, context.MapX(x, row));
            var py = Center(context.Y, context.MapY(y, row));
            if (double.IsNaN(px) || double.IsNaN(py))
            {
                current = null;
                continue;
            }

            if (current is null)
            {
                current = [];
                segments.Add(current);
            }

            current.Add((context.Coordinates.Project(px, py), row));
        }

        return segments;
    }

    /// <summary>The middle of the band of a position on a band scale.</summary>
    public static double Center(ResolvedScale? scale, double position) => scale is null ? position : position + (scale.Bandwidth / 2);
}
