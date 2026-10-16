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
/// <para>
/// A sink whose content is enlarged before it is drawn (<see cref="PathSink.Scale"/>: a transformation of the canvas)
/// gets chords that much closer to the curve: every length that is compared with the tolerance is multiplied by that
/// factor, which is 1, and changes nothing, without transformation. The tolerance is the one of the sink
/// (<see cref="PathSink.Tolerance"/>): <see cref="Tolerance"/> unless the drawing asks for a finer one.
/// </para>
/// <para>
/// Under a projective transformation (<see cref="PathSink.Projection"/>), the factor depends on the place in the plane,
/// and only what is seen matters (<see cref="ProjectiveView"/>): a Bézier curve is halved (de Casteljau) until each part
/// needs at most <see cref="ProjectedSegmentCount"/> chords for the factor on its own control points, then cut at
/// uniform parameters; an arc is halved while its chord is further from it than the tolerance over the factor on the
/// triangle that contains it. A part of the plane that is not seen is cut with few chords, which stay in the convex
/// hull of its control points.
/// </para>
/// <para>Only additions, multiplications, divisions and square roots are used: the vertices do not depend on the platform.</para>
/// </remarks>
internal static class PathFlattener
{
    /// <summary>The largest distance between a curve and its chords, in pixels, unless a drawing asks for a finer one.</summary>
    public const double Tolerance = 1.0 / 16;

    /// <summary>The deepest bisection of an arc: far more than any radius the API accepts needs.</summary>
    public const int MaxArcDepth = 24;

    /// <summary>The largest number of chords a part of a curve drawn through a projective transformation is cut into: a part that needs more is halved.</summary>
    private const int ProjectedSegmentCount = 8;

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

                case PathCommand.Arc or PathCommand.ArcContinuation:
                    Arc(sink, new ArcPiece(points.Slice(index, ArcPiece.CoordinateCount), command == PathCommand.ArcContinuation));
                    currentX = points[index + 10];
                    currentY = points[index + 11];
                    index += ArcPiece.CoordinateCount;
                    break;

                case PathCommand.EllipseAxes:
                    Ellipse(sink, points[index], points[index + 1], points[index + 2], points[index + 3], points[index + 4], points[index + 5]);
                    currentX = startX = points[index] + points[index + 2];
                    currentY = startY = points[index + 1] + points[index + 3];
                    index += 6;
                    break;
            }
        }

        sink.End();
    }

    /// <summary>Sends the chords of a quadratic Bézier curve, from the current point of the sink (the first control point).</summary>
    public static void Quadratic(PathSink sink, double x0, double y0, double x1, double y1, double x2, double y2)
    {
        if (sink.Projection is not null)
        {
            var started = false;
            ProjectedQuadratic(sink, x0, y0, x1, y1, x2, y2, depth: 0, ref started);
            return;
        }

        var count = GetSegmentCount(Length(x0 - (2 * x1) + x2, y0 - (2 * y1) + y2) * sink.Scale / (4 * sink.Tolerance));
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
        if (sink.Projection is not null)
        {
            var started = false;
            ProjectedCubic(sink, x0, y0, x1, y1, x2, y2, x3, y3, depth: 0, ref started);
            return;
        }

        var deviation = Math.Max(Length(x0 - (2 * x1) + x2, y0 - (2 * y1) + y2), Length(x1 - (2 * x2) + x3, y1 - (2 * y2) + y3));
        var count = GetSegmentCount(3 * deviation * sink.Scale / (4 * sink.Tolerance));
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
        if (sink.Projection is not null)
        {
            ProjectedEllipse(sink, centerX, centerY, radiusX, 0, 0, radiusY);
            return;
        }

        var depth = GetArcDepth(Math.Max(radiusX, radiusY) * sink.Scale * (Tolerance / sink.Tolerance));
        sink.MoveTo(centerX + radiusX, centerY);
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            EllipseArc(sink, centerX, centerY, radiusX, radiusY, quadrant, 1, 0, 0, 1, depth);
        }

        sink.Close(smooth: true);
    }

    /// <summary>
    /// Sends a closed ellipse given by its center and two conjugate half-diameters <c>u</c> and <c>v</c> (the points
    /// <c>center + x u + y v</c> for the points <c>(x, y)</c> of the unit circle), as one contour that starts at
    /// <c>center + u</c> and turns toward <c>center + v</c>.
    /// </summary>
    /// <remarks>
    /// The ellipse is the image of the polygon of the unit circle, whose chords stay within the tolerance of a circle of
    /// the largest radius of the ellipse: no chord is further from the ellipse than that.
    /// </remarks>
    public static void Ellipse(PathSink sink, double centerX, double centerY, double ux, double uy, double vx, double vy)
    {
        if (sink.Projection is not null)
        {
            ProjectedEllipse(sink, centerX, centerY, ux, uy, vx, vy);
            return;
        }

        var depth = GetArcDepth(GetLargestRadius(ux, uy, vx, vy) * sink.Scale * (Tolerance / sink.Tolerance));
        sink.MoveTo(centerX + ux, centerY + uy);
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            EllipseArc(sink, centerX, centerY, ux, uy, vx, vy, quadrant, 1, 0, 0, 1, depth);
        }

        sink.Close(smooth: true);
    }

    /// <summary>
    /// Sends the chords of an arc of at most a quarter turn, from the current point of the sink (the start of the arc) to
    /// its end point.
    /// </summary>
    /// <remarks>
    /// The arc is halved until its chords are within the tolerance: on a circle of radius <c>r</c>, the chord of two unit
    /// vectors <c>a</c> and <c>b</c> is at <c>r |a + b| / 2</c> from the center, and an ellipse is the image of the unit
    /// circle, which moves no chord further from its arc than its largest radius does.
    /// </remarks>
    public static void Arc(PathSink sink, scoped in ArcPiece piece)
    {
        var smooth = piece.IsContinuation;
        if (sink.Projection is not null)
        {
            ProjectedArcInterior(sink, piece.CenterX, piece.CenterY, piece.Ux, piece.Uy, piece.Vx, piece.Vy, piece.Ax, piece.Ay, piece.Bx, piece.By, depth: 0, ref smooth);
        }
        else
        {
            var radius = GetLargestRadius(piece.Ux, piece.Uy, piece.Vx, piece.Vy) * sink.Scale;
            ArcInterior(sink, piece, radius, piece.Ax, piece.Ay, piece.Bx, piece.By, depth: 0, ref smooth);
        }

        sink.LineTo(piece.EndX, piece.EndY, smooth);
    }

    /// <summary>
    /// Sends the chords of a quadratic curve drawn through a projective transformation: a part of the curve whose
    /// chords would be more than a few is cut in two halves (de Casteljau), each cut for the factor of the
    /// transformation on its own control points, so that only what is seen enlarged is cut finely.
    /// </summary>
    private static void ProjectedQuadratic(PathSink sink, double x0, double y0, double x1, double y1, double x2, double y2, int depth, ref bool started)
    {
        var scale = sink.GetScale(Math.Min(x0, Math.Min(x1, x2)), Math.Min(y0, Math.Min(y1, y2)), Math.Max(x0, Math.Max(x1, x2)), Math.Max(y0, Math.Max(y1, y2)));
        var count = GetSegmentCount(Length(x0 - (2 * x1) + x2, y0 - (2 * y1) + y2) * scale / (4 * sink.Tolerance));
        if (count > ProjectedSegmentCount && depth < MaxArcDepth)
        {
            var ax = (x0 + x1) / 2;
            var ay = (y0 + y1) / 2;
            var bx = (x1 + x2) / 2;
            var by = (y1 + y2) / 2;
            var mx = (ax + bx) / 2;
            var my = (ay + by) / 2;
            ProjectedQuadratic(sink, x0, y0, ax, ay, mx, my, depth + 1, ref started);
            ProjectedQuadratic(sink, mx, my, bx, by, x2, y2, depth + 1, ref started);
            return;
        }

        for (var i = 1; i <= count; i++)
        {
            var t = i / (double)count;
            var s = 1 - t;
            var a = s * s;
            var b = 2 * s * t;
            var c = t * t;
            sink.LineTo(i == count ? x2 : (a * x0) + (b * x1) + (c * x2), i == count ? y2 : (a * y0) + (b * y1) + (c * y2), smooth: started);
            started = true;
        }
    }

    /// <summary>Sends the chords of a cubic curve drawn through a projective transformation: see <see cref="ProjectedQuadratic"/>.</summary>
    private static void ProjectedCubic(PathSink sink, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, int depth, ref bool started)
    {
        var deviation = Math.Max(Length(x0 - (2 * x1) + x2, y0 - (2 * y1) + y2), Length(x1 - (2 * x2) + x3, y1 - (2 * y2) + y3));
        var scale = sink.GetScale(Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), Math.Max(Math.Max(x0, x1), Math.Max(x2, x3)), Math.Max(Math.Max(y0, y1), Math.Max(y2, y3)));
        var count = GetSegmentCount(3 * deviation * scale / (4 * sink.Tolerance));
        if (count > ProjectedSegmentCount && depth < MaxArcDepth)
        {
            var ax = (x0 + x1) / 2;
            var ay = (y0 + y1) / 2;
            var bx = (x1 + x2) / 2;
            var by = (y1 + y2) / 2;
            var cx = (x2 + x3) / 2;
            var cy = (y2 + y3) / 2;
            var dx = (ax + bx) / 2;
            var dy = (ay + by) / 2;
            var ex = (bx + cx) / 2;
            var ey = (by + cy) / 2;
            var mx = (dx + ex) / 2;
            var my = (dy + ey) / 2;
            ProjectedCubic(sink, x0, y0, ax, ay, dx, dy, mx, my, depth + 1, ref started);
            ProjectedCubic(sink, mx, my, ex, ey, cx, cy, x3, y3, depth + 1, ref started);
            return;
        }

        for (var i = 1; i <= count; i++)
        {
            var t = i / (double)count;
            var s = 1 - t;
            var a = s * s * s;
            var b = 3 * s * s * t;
            var c = 3 * s * t * t;
            var d = t * t * t;
            sink.LineTo(i == count ? x3 : (a * x0) + (b * x1) + (c * x2) + (d * x3), i == count ? y3 : (a * y0) + (b * y1) + (c * y2) + (d * y3), smooth: started);
            started = true;
        }
    }

    /// <summary>Sends a closed ellipse drawn through a projective transformation: its four quarters, each cut where it needs to be (<see cref="ProjectedArcInterior"/>).</summary>
    private static void ProjectedEllipse(PathSink sink, double centerX, double centerY, double ux, double uy, double vx, double vy)
    {
        sink.MoveTo(centerX + ux, centerY + uy);
        var smooth = true;
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            var (ax, ay, bx, by) = quadrant switch
            {
                0 => (1d, 0d, 0d, 1d),
                1 => (0d, 1d, -1d, 0d),
                2 => (-1d, 0d, 0d, -1d),
                _ => (0d, -1d, 1d, 0d),
            };
            ProjectedArcInterior(sink, centerX, centerY, ux, uy, vx, vy, ax, ay, bx, by, depth: 0, ref smooth);
            sink.LineTo(centerX + ((ux * bx) + (vx * by)), centerY + ((uy * bx) + (vy * by)), smooth: true);
        }

        sink.Close(smooth: true);
    }

    /// <summary>
    /// Sends the vertices strictly between the unit vectors <c>a</c> and <c>b</c> (at most a quarter of a turn apart) of
    /// an arc of the ellipse <c>center + x u + y v</c> drawn through a projective transformation: the arc is halved while
    /// its chord is further from it than the tolerance divided by the factor of the transformation on the triangle of
    /// its ends and of the meeting point of its tangents, which contains it.
    /// </summary>
    private static void ProjectedArcInterior(PathSink sink, double centerX, double centerY, double ux, double uy, double vx, double vy, double ax, double ay, double bx, double by, int depth, ref bool smooth)
    {
        var mx = ax + bx;
        var my = ay + by;
        var length = Length(mx, my);
        if (depth >= MaxArcDepth)
            return;

        // The ends of the arc, and where their tangents meet: (a + b) / (1 + a . b) on the unit circle
        var toTangents = 2 / (length * length);
        var x0 = centerX + ((ux * ax) + (vx * ay));
        var y0 = centerY + ((uy * ax) + (vy * ay));
        var x1 = centerX + ((ux * bx) + (vx * by));
        var y1 = centerY + ((uy * bx) + (vy * by));
        var tx = centerX + (((ux * mx) + (vx * my)) * toTangents);
        var ty = centerY + (((uy * mx) + (vy * my)) * toTangents);
        var scale = sink.GetScale(Math.Min(x0, Math.Min(x1, tx)), Math.Min(y0, Math.Min(y1, ty)), Math.Max(x0, Math.Max(x1, tx)), Math.Max(y0, Math.Max(y1, ty)));
        if (!(GetLargestRadius(ux, uy, vx, vy) * scale * (1 - (length / 2)) > sink.Tolerance))
            return;

        mx /= length;
        my /= length;
        ProjectedArcInterior(sink, centerX, centerY, ux, uy, vx, vy, ax, ay, mx, my, depth + 1, ref smooth);
        sink.LineTo(centerX + ((ux * mx) + (vx * my)), centerY + ((uy * mx) + (vy * my)), smooth);
        smooth = true;
        ProjectedArcInterior(sink, centerX, centerY, ux, uy, vx, vy, mx, my, bx, by, depth + 1, ref smooth);
    }

    /// <summary>
    /// Gets the largest distance from the center of the ellipse of two conjugate half-diameters to one of its points: the
    /// largest singular value of the matrix of the two vectors.
    /// </summary>
    public static double GetLargestRadius(double ux, double uy, double vx, double vy)
    {
        // The squares of the singular values are the roots of t^2 - s t + d^2, with s the sum of the squares of the
        // coefficients and d the determinant
        var sum = (ux * ux) + (uy * uy) + (vx * vx) + (vy * vy);
        var determinant = (ux * vy) - (uy * vx);
        var discriminant = (sum * sum) - (4 * determinant * determinant);
        return Math.Sqrt((sum + Math.Sqrt(discriminant > 0 ? discriminant : 0)) / 2);
    }

    /// <summary>Gets the number of times a quarter of a circle of a radius is halved for its chords to stay within the tolerance.</summary>
    public static int GetArcDepth(double radius)
    {
        // Each quadrant is cut in 2^depth arcs of angle a = (pi / 2) / 2^depth; r a^2 / 8 <= Tolerance when
        // 4^depth >= pi^2 r / (32 Tolerance), which 4^depth >= 5 r guarantees for a tolerance of 1/16
        var limit = 5 * radius;
        var depth = 0;
        for (double arcs = 1; arcs < limit && depth < MaxArcDepth; arcs *= 4)
        {
            depth++;
        }

        return depth;
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

    /// <summary>Sends the vertices strictly between the unit vectors <c>a</c> and <c>b</c> of an arc.</summary>
    private static void ArcInterior(PathSink sink, scoped in ArcPiece piece, double radius, double ax, double ay, double bx, double by, int depth, ref bool smooth)
    {
        var mx = ax + bx;
        var my = ay + by;
        var length = Length(mx, my);
        if (depth >= MaxArcDepth || !(radius * (1 - (length / 2)) > sink.Tolerance))
            return;

        mx /= length;
        my /= length;
        ArcInterior(sink, piece, radius, ax, ay, mx, my, depth + 1, ref smooth);
        sink.LineTo(piece.CenterX + ((piece.Ux * mx) + (piece.Vx * my)), piece.CenterY + ((piece.Uy * mx) + (piece.Vy * my)), smooth);
        smooth = true;
        ArcInterior(sink, piece, radius, mx, my, bx, by, depth + 1, ref smooth);
    }

    /// <summary>The same for an ellipse given by two conjugate half-diameters <c>a</c> and <c>b</c>.</summary>
    private static void EllipseArc(PathSink sink, double centerX, double centerY, double ax, double ay, double bx, double by, int quadrant, double ux, double uy, double vx, double vy, int depth)
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

            sink.LineTo(centerX + ((ax * x) + (bx * y)), centerY + ((ay * x) + (by * y)), smooth: true);
            return;
        }

        var mx = ux + vx;
        var my = uy + vy;
        var length = Length(mx, my);
        mx /= length;
        my /= length;
        EllipseArc(sink, centerX, centerY, ax, ay, bx, by, quadrant, ux, uy, mx, my, depth - 1);
        EllipseArc(sink, centerX, centerY, ax, ay, bx, by, quadrant, mx, my, vx, vy, depth - 1);
    }
}
