namespace Meziantou.Framework.Charts;

/// <summary>
/// The hexagonal binning of d3-hexbin (ISC license): pointy-topped hexagons of a radius, in rows <c>1.5 * radius</c>
/// apart, the odd rows shifted by half a hexagon. A point goes to the hexagon whose center is the nearest.
/// </summary>
internal static class Hexbin
{
    /// <summary>The bins of points, in the order of their first point: the center of each hexagon and the indices of its points.</summary>
    public static List<(double X, double Y, List<int> Points)> Compute(IReadOnlyList<(double X, double Y)> points, double radius)
    {
        var dx = radius * 2 * Math.Sin(Math.PI / 3);
        var dy = radius * 1.5;
        var bins = new List<(double X, double Y, List<int> Points)>();
        var indexes = new Dictionary<(long, long), int>();
        for (var i = 0; i < points.Count; i++)
        {
            var (px, py) = points[i];
            if (double.IsNaN(px) || double.IsNaN(py))
                continue;

            py /= dy;
            var pj = RoundHalfUp(py);
            px = (px / dx) - (IsOdd(pj) ? 0.5 : 0);
            var pi = RoundHalfUp(px);
            var py1 = py - pj;

            // Near the top or the bottom of the row, the point may be closer to a hexagon of the next row
            if (Math.Abs(py1) * 3 > 1)
            {
                var px1 = px - pi;
                var pi2 = pi + ((px < pi ? -1 : 1) / 2.0);
                var pj2 = pj + (py < pj ? -1 : 1);
                var px2 = px - pi2;
                var py2 = py - pj2;
                if ((px1 * px1) + (py1 * py1) > (px2 * px2) + (py2 * py2))
                {
                    pi = pi2 + ((IsOdd(pj) ? 1 : -1) / 2.0);
                    pj = pj2;
                }
            }

            var key = ((long)pi, (long)pj);
            if (!indexes.TryGetValue(key, out var index))
            {
                index = bins.Count;
                indexes.Add(key, index);
                bins.Add(((pi + (IsOdd(pj) ? 0.5 : 0)) * dx, pj * dy, []));
            }

            bins[index].Points.Add(i);
        }

        return bins;
    }

    /// <summary>The corners of the hexagon of a radius around a center, clockwise from the top.</summary>
    public static (double X, double Y)[] Hexagon(double centerX, double centerY, double radius)
    {
        var corners = new (double X, double Y)[6];
        for (var i = 0; i < 6; i++)
        {
            corners[i] = PolarGeometry.Point(centerX, centerY, radius, i * Math.PI / 3);
        }

        return corners;
    }

    // Math.round of JavaScript: halves round up, toward positive infinity
    private static double RoundHalfUp(double value) => Math.Floor(value + 0.5);

    private static bool IsOdd(double value) => ((long)value & 1) is not 0;
}
