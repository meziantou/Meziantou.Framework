using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Replaces curves by chords whose vertices are on the curve and which stay within <see cref="Tolerance"/> of it.</summary>
/// <remarks>
/// <para>
/// A Bézier curve is cut at uniform parameters. With second differences <c>D</c> of the control points, the second
/// derivative is bounded by <c>2 |D|</c> (quadratic) or <c>6 max |D|</c> (cubic), and a chord over a parameter interval
/// <c>1 / n</c> deviates from the curve by at most <c>|B''| / (8 n^2)</c>: <c>n</c> is the smallest count that keeps this
/// bound within the tolerance.
/// </para>
/// <para>
/// A circle of radius <c>r</c> cut in equal arcs of angle <c>a</c> deviates from its chords by
/// <c>r (1 - cos(a / 2)) &lt;= r a^2 / 8</c>. Each quadrant is halved until that bound is within the tolerance; an ellipse
/// is that polygon scaled by its radii, which scales the deviation by at most the larger radius. The directions come from
/// repeated bisection of the axes (normalized sums), so the polygon has the symmetries of the ellipse exactly and no
/// trigonometric function is involved.
/// </para>
/// <para>Only additions, multiplications, divisions and square roots are used: the vertices do not depend on the platform.</para>
/// </remarks>
internal static class PathFlattener
{
    /// <summary>The largest distance between a curve and its chords, in pixels.</summary>
    public const double Tolerance = 1.0 / 16;

    /// <summary>The deepest bisection of an arc: far more than any radius the API accepts needs.</summary>
    public const int MaxArcDepth = 24;

    /// <summary>Sends the contours of a path to a sink.</summary>
    public static void Flatten(VectorPath path, PathSink sink)
    {
        var points = path.Points;
        var index = 0;
        double currentX = 0, currentY = 0, startX = 0, startY = 0;
        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case PathCommand.MoveTo:
                    currentX = startX = points[index];
                    currentY = startY = points[index + 1];
                    index += 2;
                    sink.MoveTo(currentX, currentY);
                    break;

                case PathCommand.LineTo:
                    currentX = points[index];
                    currentY = points[index + 1];
                    index += 2;
                    sink.LineTo(currentX, currentY, smooth: false);
                    break;

                case PathCommand.QuadraticTo:
                    Quadratic(sink, currentX, currentY, points[index], points[index + 1], points[index + 2], points[index + 3]);
                    currentX = points[index + 2];
                    currentY = points[index + 3];
                    index += 4;
                    break;

                case PathCommand.CubicTo:
                    Cubic(sink, currentX, currentY, points[index], points[index + 1], points[index + 2], points[index + 3], points[index + 4], points[index + 5]);
                    currentX = points[index + 4];
                    currentY = points[index + 5];
                    index += 6;
                    break;

                case PathCommand.Close:
                    sink.Close(smooth: false);
                    currentX = startX;
                    currentY = startY;
                    break;

                case PathCommand.Ellipse:
                    Ellipse(sink, points[index], points[index + 1], points[index + 2], points[index + 3]);
                    currentX = startX = points[index] + points[index + 2];
                    currentY = startY = points[index + 1];
                    index += 4;
                    break;
            }
        }

        sink.End();
    }

    /// <summary>Sends the chords of a quadratic Bézier curve, from the current point of the sink (the first control point).</summary>
    public static void Quadratic(PathSink sink, double x0, double y0, double x1, double y1, double x2, double y2)
    {
        var count = GetSegmentCount(Length(x0 - (2 * x1) + x2, y0 - (2 * y1) + y2) / (4 * Tolerance));
        for (var i = 1; i < count; i++)
        {
            var t = i / (double)count;
            var s = 1 - t;
            var a = s * s;
            var b = 2 * s * t;
            var c = t * t;
            sink.LineTo((a * x0) + (b * x1) + (c * x2), (a * y0) + (b * y1) + (c * y2), smooth: i > 1);
        }

        sink.LineTo(x2, y2, smooth: count > 1);
    }

    /// <summary>Sends the chords of a cubic Bézier curve, from the current point of the sink (the first control point).</summary>
    public static void Cubic(PathSink sink, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        var deviation = Math.Max(Length(x0 - (2 * x1) + x2, y0 - (2 * y1) + y2), Length(x1 - (2 * x2) + x3, y1 - (2 * y2) + y3));
        var count = GetSegmentCount(3 * deviation / (4 * Tolerance));
        for (var i = 1; i < count; i++)
        {
            var t = i / (double)count;
            var s = 1 - t;
            var a = s * s * s;
            var b = 3 * s * s * t;
            var c = 3 * s * t * t;
            var d = t * t * t;
            sink.LineTo((a * x0) + (b * x1) + (c * x2) + (d * x3), (a * y0) + (b * y1) + (c * y2) + (d * y3), smooth: i > 1);
        }

        sink.LineTo(x3, y3, smooth: count > 1);
    }

    /// <summary>Sends a closed ellipse as one contour starting at the right end of its horizontal axis.</summary>
    public static void Ellipse(PathSink sink, double centerX, double centerY, double radiusX, double radiusY)
    {
        // Each quadrant is cut in 2^depth arcs of angle a = (pi / 2) / 2^depth; r a^2 / 8 <= Tolerance when
        // 4^depth >= pi^2 r / (32 Tolerance), which 4^depth >= 5 r guarantees for a tolerance of 1/16
        var limit = 5 * Math.Max(radiusX, radiusY);
        var depth = 0;
        for (double arcs = 1; arcs < limit && depth < MaxArcDepth; arcs *= 4)
        {
            depth++;
        }

        sink.MoveTo(centerX + radiusX, centerY);
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            EllipseArc(sink, centerX, centerY, radiusX, radiusY, quadrant, 1, 0, 0, 1, depth);
        }

        sink.Close(smooth: true);
    }

    /// <summary>Gets the smallest positive integer whose square is at least <paramref name="value"/>.</summary>
    private static int GetSegmentCount(double value)
    {
        // The coordinates are bounded (DrawingGuard), so the count is: at most a few tens of thousands of chords
        var count = (int)Math.Ceiling(Math.Sqrt(value));
        return count < 1 ? 1 : count;
    }

    private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

    /// <summary>Sends the vertices of the arc from the unit vector <c>u</c> (excluded) to <c>v</c> (included) of the first quadrant, rotated to a quadrant.</summary>
    private static void EllipseArc(PathSink sink, double centerX, double centerY, double radiusX, double radiusY, int quadrant, double ux, double uy, double vx, double vy, int depth)
    {
        if (depth == 0)
        {
            var (x, y) = quadrant switch
            {
                0 => (vx, vy),
                1 => (-vy, vx),
                2 => (-vx, -vy),
                _ => (vy, -vx),
            };

            sink.LineTo(centerX + (radiusX * x), centerY + (radiusY * y), smooth: true);
            return;
        }

        var mx = ux + vx;
        var my = uy + vy;
        var length = Length(mx, my);
        mx /= length;
        my /= length;
        EllipseArc(sink, centerX, centerY, radiusX, radiusY, quadrant, ux, uy, mx, my, depth - 1);
        EllipseArc(sink, centerX, centerY, radiusX, radiusY, quadrant, mx, my, vx, vy, depth - 1);
    }
}
