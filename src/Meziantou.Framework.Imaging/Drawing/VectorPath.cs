using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// An immutable sequence of contours made of straight segments and Bézier curves, built with a <see cref="VectorPathBuilder"/>
/// and drawn with <see cref="DrawingCanvas.Fill(Brush, VectorPath, FillRule)"/> or <see cref="DrawingCanvas.Stroke(Pen, VectorPath)"/>.
/// </summary>
/// <remarks>
/// A path is thread-safe and independent of any image: create it once and draw it as many times as needed. Coordinates
/// are canvas coordinates (see <see cref="PointD"/>).
/// </remarks>
public sealed class VectorPath
{
    private readonly PathCommand[] _commands;
    private readonly double[] _points;

    internal VectorPath(PathCommand[] commands, double[] points, RectangleD bounds)
    {
        _commands = commands;
        _points = points;
        Bounds = bounds;
    }

    /// <summary>Gets a value indicating whether the path has no element.</summary>
    public bool IsEmpty => _commands.Length == 0;

    /// <summary>
    /// Gets the smallest rectangle containing every point and control point of the path (a curve lies inside the box of
    /// its control points). It does not account for the width of a stroke. Empty for an empty path.
    /// </summary>
    public RectangleD Bounds { get; }

    /// <summary>Creates the path every point of this one becomes when a transformation is applied to it.</summary>
    /// <remarks>
    /// The result is exact: curves are transformed as curves, and an ellipse stays an ellipse, whatever the
    /// transformation. Flattening happens when the result is drawn, so a path that is enlarged stays smooth.
    /// </remarks>
    /// <param name="transform">The transformation.</param>
    /// <returns>The transformed path; this instance when the transformation is the identity.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of the result exceeds 16777216.</exception>
    public VectorPath Transform(AffineTransform transform)
    {
        if (transform.IsIdentity || IsEmpty)
            return this;

        // An ellipse keeps its compact form when its axes stay horizontal and vertical and it is not mirrored
        var keepsEllipses = transform.M12 == 0 && transform.M21 == 0 && transform.M11 > 0 && transform.M22 > 0;
        var commands = (PathCommand[])_commands.Clone();
        var points = new List<double>(_points.Length);
        var source = _points;
        var index = 0;
        for (var i = 0; i < commands.Length; i++)
        {
            switch (commands[i])
            {
                case PathCommand.MoveTo or PathCommand.LineTo:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    index += 2;
                    break;

                case PathCommand.QuadraticTo:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    AddPoint(points, transform, source[index + 2], source[index + 3]);
                    index += 4;
                    break;

                case PathCommand.CubicTo:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    AddPoint(points, transform, source[index + 2], source[index + 3]);
                    AddPoint(points, transform, source[index + 4], source[index + 5]);
                    index += 6;
                    break;

                case PathCommand.Ellipse:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    if (keepsEllipses)
                    {
                        points.Add(transform.M11 * source[index + 2]);
                        points.Add(transform.M22 * source[index + 3]);
                    }
                    else
                    {
                        commands[i] = PathCommand.EllipseAxes;
                        AddVector(points, transform, source[index + 2], 0);
                        AddVector(points, transform, 0, source[index + 3]);
                    }

                    index += 4;
                    break;

                case PathCommand.EllipseAxes:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    AddVector(points, transform, source[index + 2], source[index + 3]);
                    AddVector(points, transform, source[index + 4], source[index + 5]);
                    index += 6;
                    break;
            }
        }

        var result = points.ToArray();
        foreach (var value in result)
        {
            // Written so that a value that is not a number is rejected too
            if (!(Math.Abs(value) <= DrawingGuard.MaxCoordinate))
                throw new ArgumentOutOfRangeException(nameof(transform), "The magnitude of a coordinate of the transformed path exceeds 16777216.");
        }

        return new VectorPath(commands, result, ComputeBounds(commands, result));

        static void AddPoint(List<double> points, AffineTransform transform, double x, double y)
        {
            points.Add(transform.TransformX(x, y));
            points.Add(transform.TransformY(x, y));
        }

        static void AddVector(List<double> points, AffineTransform transform, double x, double y)
        {
            points.Add((transform.M11 * x) + (transform.M21 * y));
            points.Add((transform.M12 * x) + (transform.M22 * y));
        }

    }

    /// <summary>Gets the smallest rectangle containing every point and control point of a list of elements.</summary>
    internal static RectangleD ComputeBounds(ReadOnlySpan<PathCommand> commands, ReadOnlySpan<double> points)
    {
        if (commands.IsEmpty)
            return RectangleD.Empty;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var index = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case PathCommand.MoveTo or PathCommand.LineTo:
                    Include(points[index], points[index + 1], 0, 0);
                    index += 2;
                    break;

                case PathCommand.QuadraticTo:
                    Include(points[index], points[index + 1], 0, 0);
                    Include(points[index + 2], points[index + 3], 0, 0);
                    index += 4;
                    break;

                case PathCommand.CubicTo:
                    Include(points[index], points[index + 1], 0, 0);
                    Include(points[index + 2], points[index + 3], 0, 0);
                    Include(points[index + 4], points[index + 5], 0, 0);
                    index += 6;
                    break;

                case PathCommand.Ellipse:
                    Include(points[index], points[index + 1], points[index + 2], points[index + 3]);
                    index += 4;
                    break;

                case PathCommand.EllipseAxes:
                    // The ellipse is inside the parallelogram of its two half-diameters
                    Include(points[index], points[index + 1], Math.Abs(points[index + 2]) + Math.Abs(points[index + 4]), Math.Abs(points[index + 3]) + Math.Abs(points[index + 5]));
                    index += 6;
                    break;
            }
        }

        return RectangleD.FromLTRB(minX, minY, maxX, maxY);

        void Include(double x, double y, double extentX, double extentY)
        {
            minX = Math.Min(minX, x - extentX);
            minY = Math.Min(minY, y - extentY);
            maxX = Math.Max(maxX, x + extentX);
            maxY = Math.Max(maxY, y + extentY);
        }
    }

    /// <summary>Gets the elements.</summary>
    internal ReadOnlySpan<PathCommand> Commands => _commands;

    /// <summary>Gets the coordinates consumed by the elements, as consecutive x and y values.</summary>
    internal ReadOnlySpan<double> Points => _points;
}
