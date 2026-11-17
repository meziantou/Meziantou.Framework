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

    /// <summary>
    /// Fits a quadrilateral to the edges of a symbol in a photograph, from a rough quadrilateral around it: rays from outside
    /// each side find the first dark pixel, and a line is fitted to the outermost of these edge points. The rays that cross
    /// a light module of an edge (the alternating sides of a Data Matrix symbol) go further, and are left out. The corners
    /// are the intersections of the lines of adjacent sides.
    /// </summary>
    /// <returns>The 4 corners, in the order of <paramref name="corners"/>, or <see langword="null"/> when an edge is not found.</returns>
    public static PointD[]? FitToEdges(BinaryImage image, PointD[] corners)
    {
        var center = new PointD(corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
        var lines = new (PointD Point, PointD Direction)[4];
        for (var side = 0; side < 4; side++)
        {
            var from = corners[side];
            var to = corners[(side + 1) % 4];
            var length = from.DistanceTo(to);
            if (length < 8)
                return null;

            // The normal to the side, toward the center
            var direction = new PointD((to.X - from.X) / length, (to.Y - from.Y) / length);
            var normal = new PointD(-direction.Y, direction.X);
            var middle = Lerp(from, to, 0.5);
            if (((center.X - middle.X) * normal.X) + ((center.Y - middle.Y) * normal.Y) < 0)
            {
                normal = new PointD(-normal.X, -normal.Y);
            }

            var outside = Math.Max(3, length * 0.05);
            var maximumDepth = outside + (length * 0.3);
            var points = new List<PointD>();
            const int Rays = 40;
            for (var i = 0; i < Rays; i++)
            {
                var start = Lerp(from, to, 0.08 + (0.84 * i / (Rays - 1)));
                for (var depth = -outside; depth < maximumDepth; depth += 0.5)
                {
                    var point = new PointD(start.X + (normal.X * depth), start.Y + (normal.Y * depth));
                    if (image.IsDark(point))
                    {
                        // A ray that starts on a dark pixel is not outside the symbol
                        if (depth > -outside)
                        {
                            points.Add(point);
                        }

                        break;
                    }
                }
            }

            if (points.Count < Rays / 4 || FitOuterLine(points, normal) is not { } line)
                return null;

            lines[side] = line;
        }

        var result = new PointD[4];
        for (var corner = 0; corner < 4; corner++)
        {
            if (Intersect(lines[(corner + 3) % 4], lines[corner]) is not { } intersection || intersection.DistanceTo(corners[corner]) > corners[corner].DistanceTo(center) * 0.5)
                return null;

            result[corner] = intersection;
        }

        return result;
    }

    // A line through the points, refitted without the points that are too far inside it (toward the normal) a few times:
    // the points of the outer edge remain
    private static (PointD Point, PointD Direction)? FitOuterLine(List<PointD> points, PointD normal)
    {
        var current = points;
        (PointD Point, PointD Direction) line = default;
        for (var iteration = 0; iteration < 4; iteration++)
        {
            if (current.Count < 3)
                return null;

            line = FitLine(current);
            var lineNormal = new PointD(-line.Direction.Y, line.Direction.X);
            if ((lineNormal.X * normal.X) + (lineNormal.Y * normal.Y) < 0)
            {
                lineNormal = new PointD(-lineNormal.X, -lineNormal.Y);
            }

            var residuals = current.Select(point => ((point.X - line.Point.X) * lineNormal.X) + ((point.Y - line.Point.Y) * lineNormal.Y)).ToList();
            var spread = residuals.Average(Math.Abs);
            if (spread < 0.75)
                break;

            current = [.. current.Where((_, index) => residuals[index] <= Math.Max(0.75, spread * 0.5))];
        }

        return line;
    }

    // The line through points, by principal component analysis: the direction of the largest variance
    public static (PointD Point, PointD Direction) FitLine(List<PointD> points)
    {
        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);
        double xx = 0, xy = 0, yy = 0;
        foreach (var point in points)
        {
            var x = point.X - meanX;
            var y = point.Y - meanY;
            xx += x * x;
            xy += x * y;
            yy += y * y;
        }

        var angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
        return (new PointD(meanX, meanY), new PointD(Math.Cos(angle), Math.Sin(angle)));
    }

    public static PointD? Intersect((PointD Point, PointD Direction) a, (PointD Point, PointD Direction) b)
    {
        var denominator = (a.Direction.X * b.Direction.Y) - (a.Direction.Y * b.Direction.X);
        if (Math.Abs(denominator) < 1e-6)
            return null;

        var t = (((b.Point.X - a.Point.X) * b.Direction.Y) - ((b.Point.Y - a.Point.Y) * b.Direction.X)) / denominator;
        return new PointD(a.Point.X + (t * a.Direction.X), a.Point.Y + (t * a.Direction.Y));
    }

    private static double Cross(PointD origin, PointD a, PointD b) => ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));

    public static PointD Lerp(PointD a, PointD b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
}
