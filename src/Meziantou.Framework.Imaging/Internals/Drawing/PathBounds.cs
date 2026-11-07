namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the rectangle a path reaches: the extreme points of its curves, not their control points.</summary>
/// <remarks>
/// A coordinate of a Bézier curve is a polynomial of its parameter, extreme where its derivative is zero (a root of a
/// polynomial of degree one or two) or at an end. A coordinate of an ellipse <c>center + x u + y v</c> over the unit
/// circle is extreme where <c>(x, y)</c> is along the vector of the two factors of that coordinate, at a distance of
/// the length of that vector from the center. Only additions, multiplications, divisions and square roots are used.
/// </remarks>
internal static class PathBounds
{
    public static RectangleD ComputeTight(ReadOnlySpan<PathCommand> commands, ReadOnlySpan<double> points)
    {
        if (commands.IsEmpty)
            return RectangleD.Empty;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var index = 0;
        double currentX = 0, currentY = 0, startX = 0, startY = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case PathCommand.MoveTo:
                    currentX = startX = points[index];
                    currentY = startY = points[index + 1];
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, currentX, currentY);
                    index += 2;
                    break;

                case PathCommand.LineTo:
                    currentX = points[index];
                    currentY = points[index + 1];
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, currentX, currentY);
                    index += 2;
                    break;

                case PathCommand.QuadraticTo:
                    Quadratic(ref minX, ref maxX, currentX, points[index], points[index + 2]);
                    Quadratic(ref minY, ref maxY, currentY, points[index + 1], points[index + 3]);
                    currentX = points[index + 2];
                    currentY = points[index + 3];
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, currentX, currentY);
                    index += 4;
                    break;

                case PathCommand.CubicTo:
                    Cubic(ref minX, ref maxX, currentX, points[index], points[index + 2], points[index + 4]);
                    Cubic(ref minY, ref maxY, currentY, points[index + 1], points[index + 3], points[index + 5]);
                    currentX = points[index + 4];
                    currentY = points[index + 5];
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, currentX, currentY);
                    index += 6;
                    break;

                case PathCommand.Close:
                    currentX = startX;
                    currentY = startY;
                    break;

                case PathCommand.Ellipse:
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, points[index] - points[index + 2], points[index + 1] - points[index + 3]);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, points[index] + points[index + 2], points[index + 1] + points[index + 3]);
                    currentX = startX = points[index] + points[index + 2];
                    currentY = startY = points[index + 1];
                    index += 4;
                    break;

                case PathCommand.EllipseAxes:
                    {
                        var extentX = Length(points[index + 2], points[index + 4]);
                        var extentY = Length(points[index + 3], points[index + 5]);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, points[index] - extentX, points[index + 1] - extentY);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, points[index] + extentX, points[index + 1] + extentY);
                        currentX = startX = points[index] + points[index + 2];
                        currentY = startY = points[index + 1] + points[index + 3];
                        index += 6;
                        break;
                    }

                case PathCommand.Arc or PathCommand.ArcContinuation:
                    {
                        var piece = new ArcPiece(points.Slice(index, ArcPiece.CoordinateCount), isContinuation: false);
                        Arc(ref minX, ref maxX, piece, piece.CenterX, piece.Ux, piece.Vx);
                        Arc(ref minY, ref maxY, piece, piece.CenterY, piece.Uy, piece.Vy);
                        currentX = piece.EndX;
                        currentY = piece.EndY;
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, currentX, currentY);
                        index += ArcPiece.CoordinateCount;
                        break;
                    }
            }
        }

        return RectangleD.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>Includes the extreme value of a coordinate of a quadratic Bézier curve strictly inside the curve, if any.</summary>
    private static void Quadratic(ref double min, ref double max, double p0, double p1, double p2)
    {
        // The derivative is zero at t = (p0 - p1) / (p0 - 2 p1 + p2)
        var divisor = p0 - (2 * p1) + p2;
        if (divisor == 0)
            return;

        var t = (p0 - p1) / divisor;
        if (t > 0 && t < 1)
        {
            var s = 1 - t;
            Include(ref min, ref max, (s * s * p0) + (2 * s * t * p1) + (t * t * p2));
        }
    }

    /// <summary>Includes the extreme values of a coordinate of a cubic Bézier curve strictly inside the curve, if any.</summary>
    private static void Cubic(ref double min, ref double max, double p0, double p1, double p2, double p3)
    {
        // A third of the derivative is a t^2 + b t + c
        var a = (p3 - p0) + (3 * (p1 - p2));
        var b = 2 * (p0 - (2 * p1) + p2);
        var c = p1 - p0;
        if (a == 0)
        {
            if (b != 0)
            {
                CubicAt(ref min, ref max, -c / b, p0, p1, p2, p3);
            }

            return;
        }

        var discriminant = (b * b) - (4 * a * c);
        if (discriminant < 0)
            return;

        var root = Math.Sqrt(discriminant);
        CubicAt(ref min, ref max, (-b + root) / (2 * a), p0, p1, p2, p3);
        CubicAt(ref min, ref max, (-b - root) / (2 * a), p0, p1, p2, p3);
    }

    private static void CubicAt(ref double min, ref double max, double t, double p0, double p1, double p2, double p3)
    {
        if (t > 0 && t < 1)
        {
            var s = 1 - t;
            Include(ref min, ref max, (s * s * s * p0) + (3 * s * s * t * p1) + (3 * s * t * t * p2) + (t * t * t * p3));
        }
    }

    /// <summary>Includes the extreme values of a coordinate of an arc strictly inside the arc, if any.</summary>
    /// <param name="min">The smallest value so far.</param>
    /// <param name="max">The largest value so far.</param>
    /// <param name="piece">The arc.</param>
    /// <param name="center">The coordinate of the center.</param>
    /// <param name="u">The coordinate of the first half-diameter.</param>
    /// <param name="v">The coordinate of the second half-diameter.</param>
    private static void Arc(ref double min, ref double max, scoped in ArcPiece piece, double center, double u, double v)
    {
        // The coordinate is center + u x + v y on the unit circle: extreme at (x, y) = +-(u, v) / |(u, v)|
        var extent = Length(u, v);
        if (!(extent > 0))
            return;

        var x = u / extent;
        var y = v / extent;

        // A direction is inside an arc of less than half a turn from a to b when it is on the side of b from a, and on
        // the side of a from b
        var turn = (piece.Ax * piece.By) - (piece.Ay * piece.Bx);
        var fromA = (piece.Ax * y) - (piece.Ay * x);
        var toB = (x * piece.By) - (y * piece.Bx);
        if (turn > 0 ? (fromA > 0 && toB > 0) : (turn < 0 && fromA < 0 && toB < 0))
        {
            Include(ref min, ref max, center + extent);
        }
        else if (turn > 0 ? (fromA < 0 && toB < 0) : (turn < 0 && fromA > 0 && toB > 0))
        {
            Include(ref min, ref max, center - extent);
        }
    }

    private static void Include(ref double min, ref double max, double value)
    {
        min = Math.Min(min, value);
        max = Math.Max(max, value);
    }

    private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

    private static void IncludePoint(ref double minX, ref double minY, ref double maxX, ref double maxY, double x, double y)
    {
        minX = Math.Min(minX, x);
        minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x);
        maxY = Math.Max(maxY, y);
    }
}
