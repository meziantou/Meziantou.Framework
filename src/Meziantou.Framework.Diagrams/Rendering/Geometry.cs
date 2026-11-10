using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Points, paths, polygons and their intersections.</summary>
internal static class Geometry
{
    // The control points of a cubic Bézier curve that approximates a quarter of a circle
    private const double Kappa = 0.5522847498307936;

    public static PathData Polygon(params ReadOnlySpan<(double X, double Y)> points)
    {
        var builder = new PathBuilder();
        for (var i = 0; i < points.Length; i++)
        {
            if (i == 0)
            {
                builder.MoveTo(points[i].X, points[i].Y);
            }
            else
            {
                builder.LineTo(points[i].X, points[i].Y);
            }
        }

        return builder.Close().Build();
    }

    public static PathBuilder AddEllipse(this PathBuilder builder, double centerX, double centerY, double radiusX, double radiusY)
    {
        var kx = radiusX * Kappa;
        var ky = radiusY * Kappa;
        return builder
            .MoveTo(centerX + radiusX, centerY)
            .CubicTo(centerX + radiusX, centerY + ky, centerX + kx, centerY + radiusY, centerX, centerY + radiusY)
            .CubicTo(centerX - kx, centerY + radiusY, centerX - radiusX, centerY + ky, centerX - radiusX, centerY)
            .CubicTo(centerX - radiusX, centerY - ky, centerX - kx, centerY - radiusY, centerX, centerY - radiusY)
            .CubicTo(centerX + kx, centerY - radiusY, centerX + radiusX, centerY - ky, centerX + radiusX, centerY)
            .Close();
    }

    public static PathData Ellipse(double centerX, double centerY, double radiusX, double radiusY) => new PathBuilder().AddEllipse(centerX, centerY, radiusX, radiusY).Build();

    /// <summary>Returns a rounded rectangle centered on the origin.</summary>
    public static PathData RoundedRectangle(double width, double height, double radius)
    {
        var x = -width / 2;
        var y = -height / 2;
        radius = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
        if (radius == 0)
            return Polygon((x, y), (x + width, y), (x + width, y + height), (x, y + height));

        var k = radius * (1 - Kappa);
        return new PathBuilder()
            .MoveTo(x + radius, y)
            .LineTo(x + width - radius, y)
            .CubicTo(x + width - k, y, x + width, y + k, x + width, y + radius)
            .LineTo(x + width, y + height - radius)
            .CubicTo(x + width, y + height - k, x + width - k, y + height, x + width - radius, y + height)
            .LineTo(x + radius, y + height)
            .CubicTo(x + k, y + height, x, y + height - k, x, y + height - radius)
            .LineTo(x, y + radius)
            .CubicTo(x, y + k, x + k, y, x + radius, y)
            .Close()
            .Build();
    }

    /// <summary>Converts the subpaths of a path to polygons, the curves split into segments.</summary>
    public static List<List<LayoutPoint>> Flatten(PathData path)
    {
        var result = new List<List<LayoutPoint>>();
        List<LayoutPoint>? current = null;
        var x = 0.0;
        var y = 0.0;
        foreach (var command in path.Commands)
        {
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    current = [new(command.X, command.Y)];
                    result.Add(current);
                    break;

                case PathCommandKind.LineTo:
                case PathCommandKind.ArcTo:
                    current ??= Start();
                    current.Add(new(command.X, command.Y));
                    break;

                case PathCommandKind.CubicTo:
                    current ??= Start();
                    for (var i = 1; i <= 8; i++)
                    {
                        var t = i / 8.0;
                        var u = 1 - t;
                        current.Add(new(
                            (u * u * u * x) + (3 * u * u * t * command.X1) + (3 * u * t * t * command.X2) + (t * t * t * command.X),
                            (u * u * u * y) + (3 * u * u * t * command.Y1) + (3 * u * t * t * command.Y2) + (t * t * t * command.Y)));
                    }

                    break;

                case PathCommandKind.QuadraticTo:
                    current ??= Start();
                    for (var i = 1; i <= 8; i++)
                    {
                        var t = i / 8.0;
                        var u = 1 - t;
                        current.Add(new((u * u * x) + (2 * u * t * command.X1) + (t * t * command.X), (u * u * y) + (2 * u * t * command.Y1) + (t * t * command.Y)));
                    }

                    break;

                case PathCommandKind.Close:
                    if (current is { Count: > 0 })
                    {
                        current.Add(current[0]);
                        x = current[0].X;
                        y = current[0].Y;
                        current = null;
                    }

                    continue;
            }

            if (current is { Count: > 0 })
            {
                x = current[^1].X;
                y = current[^1].Y;
            }
        }

        return result;

        List<LayoutPoint> Start()
        {
            var points = new List<LayoutPoint> { new(x, y) };
            result.Add(points);
            return points;
        }
    }

    /// <summary>Returns where the segment from <paramref name="inside"/> to <paramref name="outside"/> leaves an outline: the intersection closest to <paramref name="outside"/>.</summary>
    /// <returns>The intersection, or <paramref name="inside"/> when the segment does not cross the outline.</returns>
    public static LayoutPoint Intersect(IReadOnlyList<List<LayoutPoint>> outline, LayoutPoint inside, LayoutPoint outside)
    {
        var best = inside;
        var bestT = double.NegativeInfinity;
        foreach (var polygon in outline)
        {
            for (var i = 1; i < polygon.Count; i++)
            {
                if (TryIntersectSegments(inside, outside, polygon[i - 1], polygon[i], out var t, out var point) && t > bestT)
                {
                    bestT = t;
                    best = point;
                }
            }
        }

        return best;
    }

    /// <summary>Returns where the segment from the center of a rectangle to a point leaves it.</summary>
    public static LayoutPoint IntersectRectangle(SceneBounds rectangle, LayoutPoint outside)
    {
        var center = new LayoutPoint(rectangle.CenterX, rectangle.CenterY);
        var dx = outside.X - center.X;
        var dy = outside.Y - center.Y;
        var w = rectangle.Width / 2;
        var h = rectangle.Height / 2;
        if (dx == 0 && dy == 0)
            return center;

        double sx;
        double sy;
        if (Math.Abs(dy) * w > Math.Abs(dx) * h)
        {
            // The top or the bottom side
            if (dy < 0)
            {
                h = -h;
            }

            sx = dy == 0 ? 0 : h * dx / dy;
            sy = h;
        }
        else
        {
            if (dx < 0)
            {
                w = -w;
            }

            sx = w;
            sy = dx == 0 ? 0 : w * dy / dx;
        }

        return new LayoutPoint(center.X + sx, center.Y + sy);
    }

    public static bool Contains(SceneBounds rectangle, LayoutPoint point) => point.X >= rectangle.X && point.X <= rectangle.Right && point.Y >= rectangle.Y && point.Y <= rectangle.Bottom;

    public static double Distance(LayoutPoint a, LayoutPoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>Returns the point at a distance from <paramref name="from"/>, toward <paramref name="to"/>.</summary>
    public static LayoutPoint MoveToward(LayoutPoint from, LayoutPoint to, double distance)
    {
        var length = Distance(from, to);
        if (length == 0)
            return from;

        var t = distance / length;
        return new LayoutPoint(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
    }

    private static bool TryIntersectSegments(LayoutPoint p1, LayoutPoint p2, LayoutPoint q1, LayoutPoint q2, out double t, out LayoutPoint point)
    {
        var rX = p2.X - p1.X;
        var rY = p2.Y - p1.Y;
        var sX = q2.X - q1.X;
        var sY = q2.Y - q1.Y;
        var denominator = (rX * sY) - (rY * sX);
        t = 0;
        point = default;
        if (Math.Abs(denominator) < 1e-12)
            return false;

        var qpX = q1.X - p1.X;
        var qpY = q1.Y - p1.Y;
        t = ((qpX * sY) - (qpY * sX)) / denominator;
        var u = ((qpX * rY) - (qpY * rX)) / denominator;
        if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9)
            return false;

        point = new LayoutPoint(p1.X + (t * rX), p1.Y + (t * rY));
        return true;
    }

    public static bool IsFinite(LayoutPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
