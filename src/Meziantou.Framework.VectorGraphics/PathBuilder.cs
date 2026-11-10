namespace Meziantou.Framework.VectorGraphics;

/// <summary>Builds a <see cref="PathData"/>.</summary>
public sealed class PathBuilder
{
    private readonly List<PathCommand> _commands = [];

    /// <summary>Gets a value that indicates whether no command was added.</summary>
    public bool IsEmpty => _commands.Count is 0;

    /// <summary>Starts a new contour.</summary>
    /// <param name="x">The x coordinate of the start.</param>
    /// <param name="y">The y coordinate of the start.</param>
    /// <returns>The builder.</returns>
    public PathBuilder MoveTo(double x, double y) => Add(new(PathCommandKind.MoveTo, x, y));

    /// <summary>Draws a straight line.</summary>
    /// <param name="x">The x coordinate of the end.</param>
    /// <param name="y">The y coordinate of the end.</param>
    /// <returns>The builder.</returns>
    public PathBuilder LineTo(double x, double y) => Add(new(PathCommandKind.LineTo, x, y));

    /// <summary>Draws a cubic Bézier curve.</summary>
    /// <param name="x1">The x coordinate of the first control point.</param>
    /// <param name="y1">The y coordinate of the first control point.</param>
    /// <param name="x2">The x coordinate of the second control point.</param>
    /// <param name="y2">The y coordinate of the second control point.</param>
    /// <param name="x">The x coordinate of the end.</param>
    /// <param name="y">The y coordinate of the end.</param>
    /// <returns>The builder.</returns>
    public PathBuilder CubicTo(double x1, double y1, double x2, double y2, double x, double y) => Add(new(PathCommandKind.CubicTo, x, y, x1, y1, x2, y2));

    /// <summary>Draws a quadratic Bézier curve.</summary>
    /// <param name="x1">The x coordinate of the control point.</param>
    /// <param name="y1">The y coordinate of the control point.</param>
    /// <param name="x">The x coordinate of the end.</param>
    /// <param name="y">The y coordinate of the end.</param>
    /// <returns>The builder.</returns>
    public PathBuilder QuadraticTo(double x1, double y1, double x, double y) => Add(new(PathCommandKind.QuadraticTo, x, y, x1, y1));

    /// <summary>Draws an elliptical arc, as the SVG <c>A</c> command.</summary>
    /// <param name="radiusX">The x radius.</param>
    /// <param name="radiusY">The y radius.</param>
    /// <param name="rotation">The rotation of the ellipse, in degrees.</param>
    /// <param name="largeArc">Whether the arc is the larger of the two candidate arcs.</param>
    /// <param name="sweep">Whether the arc is drawn clockwise.</param>
    /// <param name="x">The x coordinate of the end.</param>
    /// <param name="y">The y coordinate of the end.</param>
    /// <returns>The builder.</returns>
    public PathBuilder ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool sweep, double x, double y) => Add(new(PathCommandKind.ArcTo, x, y, radiusX, radiusY, rotation, 0, largeArc, sweep));

    /// <summary>Closes the current contour.</summary>
    /// <returns>The builder.</returns>
    public PathBuilder Close() => Add(new(PathCommandKind.Close, 0, 0));

    /// <summary>Adds a circle, as two half arcs, clockwise.</summary>
    /// <param name="centerX">The x coordinate of the center.</param>
    /// <param name="centerY">The y coordinate of the center.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The builder.</returns>
    public PathBuilder AddCircle(double centerX, double centerY, double radius)
    {
        MoveTo(centerX, centerY - radius);
        ArcTo(radius, radius, 0, largeArc: false, sweep: true, centerX, centerY + radius);
        ArcTo(radius, radius, 0, largeArc: false, sweep: true, centerX, centerY - radius);
        return Close();
    }

    /// <summary>Adds a rectangle, clockwise.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The builder.</returns>
    public PathBuilder AddRectangle(double x, double y, double width, double height)
    {
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        return Close();
    }

    /// <summary>Creates the path.</summary>
    /// <returns>The path.</returns>
    public PathData Build() => new([.. _commands]);

    private PathBuilder Add(PathCommand command)
    {
        _commands.Add(command);
        return this;
    }
}
