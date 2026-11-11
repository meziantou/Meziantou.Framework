using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The length of a path and its points at a distance: the path is flattened into line segments, its curves within
/// 1/100 of a pixel. The subpaths are measured one after the other; the moves between them have no length.
/// </summary>
internal sealed class PathMeasure
{
    private const double Tolerance = 0.01;
    private const int MaxDepth = 12;

    private readonly List<Segment> _segments = [];

    // The point of the path when it has no segment: its first move, or null for an empty path
    private readonly (double X, double Y)? _firstPoint;

    public PathMeasure(PathData path)
    {
        // The first point of the path, also when the cubic curves omit its commands (an arc that ends where it starts)
        if (!path.IsEmpty)
        {
            _firstPoint = path.Commands[0].Kind is PathCommandKind.MoveTo ? (path.Commands[0].X, path.Commands[0].Y) : (0, 0);
        }

        var (x, y) = (0.0, 0.0);
        var (startX, startY) = (0.0, 0.0);
        foreach (var command in path.ToCubicCurves().Commands)
        {
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    (x, y) = (command.X, command.Y);
                    (startX, startY) = (x, y);
                    break;

                case PathCommandKind.LineTo:
                    AddLine(x, y, command.X, command.Y);
                    (x, y) = (command.X, command.Y);
                    break;

                case PathCommandKind.CubicTo:
                    AddCubic(x, y, command.X1, command.Y1, command.X2, command.Y2, command.X, command.Y, depth: 0);
                    (x, y) = (command.X, command.Y);
                    break;

                case PathCommandKind.Close:
                    AddLine(x, y, startX, startY);
                    (x, y) = (startX, startY);
                    break;
            }
        }
    }

    /// <summary>Gets the length of the path.</summary>
    public double Length { get; private set; }

    /// <summary>Gets the point at a distance from the start of the path, clamped to the path.</summary>
    public PathPoint? GetPoint(double distance)
    {
        if (_segments.Count is 0)
            return _firstPoint is var (firstX, firstY) ? new PathPoint(firstX, firstY, 0) : null;

        distance = double.IsNaN(distance) ? 0 : Math.Clamp(distance, 0, Length);

        // The last segment that starts at or before the distance
        var (low, high) = (0, _segments.Count - 1);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_segments[middle].Start <= distance)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        // The direction goes from the tangent at the start of the segment to the tangent at its end: on a curve, it is
        // the one of the curve, not of its chord
        var segment = _segments[low];
        var t = Math.Clamp((distance - segment.Start) / segment.Length, 0, 1);
        var (dx, dy) = (segment.X1 - segment.X0, segment.Y1 - segment.Y0);
        var turn = Math.IEEERemainder(segment.EndAngle - segment.StartAngle, 360);
        return new PathPoint(segment.X0 + (t * dx), segment.Y0 + (t * dy), Math.IEEERemainder(segment.StartAngle + (t * turn), 360));
    }

    // A segment without length or with a coordinate that is not finite has no direction: it is not measured
    private void AddLine(double x0, double y0, double x1, double y1, double? startAngle = null, double? endAngle = null)
    {
        var length = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));
        if (!(length > 0) || !double.IsFinite(length))
            return;

        var angle = Angle(x1 - x0, y1 - y0);
        _segments.Add(new Segment(x0, y0, x1, y1, Length, length, startAngle ?? angle, endAngle ?? angle));
        Length += length;
    }

    private static double Angle(double dx, double dy) => Math.Atan2(dy, dx) * 180 / Math.PI;

    // A curve is split in halves until its control points are on its chord, within the tolerance
    private void AddCubic(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, int depth)
    {
        if (depth >= MaxDepth || IsFlat(x0, y0, x1, y1, x2, y2, x3, y3))
        {
            // The tangents at the ends of the curve: toward the first control point that is not on the end
            double? startAngle = (x1, y1) != (x0, y0) ? Angle(x1 - x0, y1 - y0) : (x2, y2) != (x0, y0) ? Angle(x2 - x0, y2 - y0) : null;
            double? endAngle = (x3, y3) != (x2, y2) ? Angle(x3 - x2, y3 - y2) : (x3, y3) != (x1, y1) ? Angle(x3 - x1, y3 - y1) : null;
            if (startAngle is { } start && endAngle is { } end && double.IsFinite(start) && double.IsFinite(end))
            {
                AddLine(x0, y0, x3, y3, start, end);
            }
            else
            {
                AddLine(x0, y0, x3, y3);
            }

            return;
        }

        var (ax, ay) = ((x0 + x1) / 2, (y0 + y1) / 2);
        var (bx, by) = ((x1 + x2) / 2, (y1 + y2) / 2);
        var (cx, cy) = ((x2 + x3) / 2, (y2 + y3) / 2);
        var (dx, dy) = ((ax + bx) / 2, (ay + by) / 2);
        var (ex, ey) = ((bx + cx) / 2, (by + cy) / 2);
        var (mx, my) = ((dx + ex) / 2, (dy + ey) / 2);
        AddCubic(x0, y0, ax, ay, dx, dy, mx, my, depth + 1);
        AddCubic(mx, my, ex, ey, cx, cy, x3, y3, depth + 1);
    }

    // A curve is flat when its control points are on its chord, within the tolerance, and between its ends: control
    // points on the line of the chord but beyond an end make the curve double back
    private static bool IsFlat(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2))
            return true;

        // The distance of the control points from the chord, or from its start when it has no length
        var (dx, dy) = (x3 - x0, y3 - y0);
        var chord = Math.Sqrt((dx * dx) + (dy * dy));
        if (!(chord > 0))
            return Math.Max(Distance(x0, y0, x1, y1), Distance(x0, y0, x2, y2)) <= Tolerance;

        var d1 = Math.Abs(((x1 - x0) * dy) - ((y1 - y0) * dx)) / chord;
        var d2 = Math.Abs(((x2 - x0) * dy) - ((y2 - y0) * dx)) / chord;
        if (Math.Max(d1, d2) > Tolerance)
            return false;

        // The position of the control points along the chord, from its start
        var p1 = (((x1 - x0) * dx) + ((y1 - y0) * dy)) / chord;
        var p2 = (((x2 - x0) * dx) + ((y2 - y0) * dy)) / chord;
        return Math.Min(p1, p2) >= -Tolerance && Math.Max(p1, p2) <= chord + Tolerance;
    }

    private static double Distance(double x0, double y0, double x1, double y1) => Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(double X0, double Y0, double X1, double Y1, double Start, double Length, double StartAngle, double EndAngle);
}
