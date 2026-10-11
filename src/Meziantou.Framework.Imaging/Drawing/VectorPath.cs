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

    /// <summary>Gets the elements.</summary>
    internal ReadOnlySpan<PathCommand> Commands => _commands;

    /// <summary>Gets the coordinates consumed by the elements, as consecutive x and y values.</summary>
    internal ReadOnlySpan<double> Points => _points;
}
