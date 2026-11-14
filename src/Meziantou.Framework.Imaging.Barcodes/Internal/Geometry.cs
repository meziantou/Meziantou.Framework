namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>Geometry helpers of the locators: convex hulls and minimum-area rectangles of pixel regions.</summary>
internal static class Geometry
{
    /// <summary>Gets the convex hull of points, counterclockwise in the coordinates of the image (y down: clockwise on screen).</summary>
    public static List<PointD> GetConvexHull(List<PointD> points)
    {
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToList();
        if (sorted.Count < 3)
            return sorted;

        var hull = new List<PointD>(sorted.Count * 2);
        foreach (var pass in (ReadOnlySpan<bool>)[false, true])
        {
            var start = hull.Count;
            var sequence = pass ? Enumerable.Reverse(sorted) : sorted;
            foreach (var point in sequence)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], point) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    /// <summary>Gets the rectangle of minimum area that contains a convex polygon: its 4 corners, in order around it.</summary>
    public static PointD[]? GetMinimumAreaRectangle(List<PointD> hull)
    {
        if (hull.Count < 3)
            return null;

        PointD[]? best = null;
        var bestArea = double.MaxValue;
        for (var i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            var length = a.DistanceTo(b);
            if (length < 1e-9)
                continue;

            var ux = (b.X - a.X) / length;
            var uy = (b.Y - a.Y) / length;
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var point in hull)
            {
                var u = ((point.X - a.X) * ux) + ((point.Y - a.Y) * uy);
                var v = (-(point.X - a.X) * uy) + ((point.Y - a.Y) * ux);
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }

            var area = (maxU - minU) * (maxV - minV);
            if (area < bestArea)
            {
                bestArea = area;
                PointD At(double u, double v) => new(a.X + (u * ux) - (v * uy), a.Y + (u * uy) + (v * ux));
                best = [At(minU, minV), At(maxU, minV), At(maxU, maxV), At(minU, maxV)];
            }
        }

        return best;
    }

    private static double Cross(PointD origin, PointD a, PointD b) => ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));

    public static PointD Lerp(PointD a, PointD b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
}
