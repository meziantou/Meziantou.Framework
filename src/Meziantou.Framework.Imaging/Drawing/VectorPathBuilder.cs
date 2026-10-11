using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Builds a <see cref="VectorPath"/>.</summary>
/// <remarks>
/// <para>
/// A contour starts with <see cref="MoveTo(double, double)"/> and continues with segments from the current point. A contour
/// that is not closed is open when it is stroked (it gets the caps of the pen) and is closed implicitly when it is
/// filled. After <see cref="Close"/>, <see cref="AddRectangle"/>, <see cref="AddEllipse"/>, <see cref="AddCircle"/> or
/// <see cref="AddPolygon"/>, the current point is the first point of the contour that was just closed, and the next
/// segment starts a new contour there.
/// </para>
/// <para>
/// A builder is not thread-safe. Every coordinate must be a finite number whose magnitude does not exceed 16777216.
/// </para>
/// </remarks>
public sealed class VectorPathBuilder
{
    private readonly List<PathCommand> _commands = [];
    private readonly List<double> _points = [];
    private double _startX;
    private double _startY;
    private double _currentX;
    private double _currentY;
    private bool _hasCurrentPoint;
    private bool _contourEnded;
    private bool _hasBounds;
    private double _minX;
    private double _minY;
    private double _maxX;
    private double _maxY;

    /// <summary>Starts a new contour.</summary>
    /// <param name="x">The horizontal coordinate of the first point.</param>
    /// <param name="y">The vertical coordinate of the first point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public VectorPathBuilder MoveTo(double x, double y)
    {
        DrawingGuard.Coordinate(x, nameof(x));
        DrawingGuard.Coordinate(y, nameof(y));
        AppendMoveTo(x, y);
        return this;
    }

    /// <summary>Starts a new contour.</summary>
    /// <param name="point">The first point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public VectorPathBuilder MoveTo(PointD point)
    {
        DrawingGuard.Point(point, nameof(point));
        AppendMoveTo(point.X, point.Y);
        return this;
    }

    /// <summary>Adds a straight segment from the current point.</summary>
    /// <param name="x">The horizontal coordinate of the end point.</param>
    /// <param name="y">The vertical coordinate of the end point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder LineTo(double x, double y)
    {
        DrawingGuard.Coordinate(x, nameof(x));
        DrawingGuard.Coordinate(y, nameof(y));
        AppendLineTo(x, y);
        return this;
    }

    /// <summary>Adds a straight segment from the current point.</summary>
    /// <param name="point">The end point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder LineTo(PointD point)
    {
        DrawingGuard.Point(point, nameof(point));
        AppendLineTo(point.X, point.Y);
        return this;
    }

    /// <summary>Adds a quadratic Bézier curve from the current point.</summary>
    /// <param name="controlX">The horizontal coordinate of the control point.</param>
    /// <param name="controlY">The vertical coordinate of the control point.</param>
    /// <param name="x">The horizontal coordinate of the end point.</param>
    /// <param name="y">The vertical coordinate of the end point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder QuadraticBezierTo(double controlX, double controlY, double x, double y)
    {
        DrawingGuard.Coordinate(controlX, nameof(controlX));
        DrawingGuard.Coordinate(controlY, nameof(controlY));
        DrawingGuard.Coordinate(x, nameof(x));
        DrawingGuard.Coordinate(y, nameof(y));
        BeginSegment();
        _commands.Add(PathCommand.QuadraticTo);
        AppendPoint(controlX, controlY);
        AppendPoint(x, y);
        SetCurrentPoint(x, y);
        return this;
    }

    /// <summary>Adds a quadratic Bézier curve from the current point.</summary>
    /// <param name="control">The control point.</param>
    /// <param name="point">The end point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder QuadraticBezierTo(PointD control, PointD point)
    {
        DrawingGuard.Point(control, nameof(control));
        DrawingGuard.Point(point, nameof(point));
        return QuadraticBezierTo(control.X, control.Y, point.X, point.Y);
    }

    /// <summary>Adds a cubic Bézier curve from the current point.</summary>
    /// <param name="control1X">The horizontal coordinate of the first control point.</param>
    /// <param name="control1Y">The vertical coordinate of the first control point.</param>
    /// <param name="control2X">The horizontal coordinate of the second control point.</param>
    /// <param name="control2Y">The vertical coordinate of the second control point.</param>
    /// <param name="x">The horizontal coordinate of the end point.</param>
    /// <param name="y">The vertical coordinate of the end point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder CubicBezierTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
    {
        DrawingGuard.Coordinate(control1X, nameof(control1X));
        DrawingGuard.Coordinate(control1Y, nameof(control1Y));
        DrawingGuard.Coordinate(control2X, nameof(control2X));
        DrawingGuard.Coordinate(control2Y, nameof(control2Y));
        DrawingGuard.Coordinate(x, nameof(x));
        DrawingGuard.Coordinate(y, nameof(y));
        BeginSegment();
        _commands.Add(PathCommand.CubicTo);
        AppendPoint(control1X, control1Y);
        AppendPoint(control2X, control2Y);
        AppendPoint(x, y);
        SetCurrentPoint(x, y);
        return this;
    }

    /// <summary>Adds a cubic Bézier curve from the current point.</summary>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="point">The end point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder CubicBezierTo(PointD control1, PointD control2, PointD point)
    {
        DrawingGuard.Point(control1, nameof(control1));
        DrawingGuard.Point(control2, nameof(control2));
        DrawingGuard.Point(point, nameof(point));
        return CubicBezierTo(control1.X, control1.Y, control2.X, control2.Y, point.X, point.Y);
    }

    /// <summary>Closes the current contour with a straight segment to its first point. Nothing is done when no contour is open.</summary>
    /// <returns>This builder.</returns>
    public VectorPathBuilder Close()
    {
        if (!_hasCurrentPoint || _contourEnded)
            return this;

        _commands.Add(PathCommand.Close);
        EndContour();
        return this;
    }

    /// <summary>Adds a closed rectangular contour.</summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public VectorPathBuilder AddRectangle(RectangleD rectangle)
    {
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        AppendMoveTo(rectangle.Left, rectangle.Top);
        AppendLineTo(rectangle.Right, rectangle.Top);
        AppendLineTo(rectangle.Right, rectangle.Bottom);
        AppendLineTo(rectangle.Left, rectangle.Bottom);
        return Close();
    }

    /// <summary>Adds a closed elliptical contour whose axes are horizontal and vertical.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radiusX">The horizontal radius. Must be zero or positive.</param>
    /// <param name="radiusY">The vertical radius. Must be zero or positive.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate or a radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or a radius is negative or exceeds 16777216.</exception>
    public VectorPathBuilder AddEllipse(PointD center, double radiusX, double radiusY)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        _commands.Add(PathCommand.Ellipse);
        _points.Add(center.X);
        _points.Add(center.Y);
        _points.Add(radiusX);
        _points.Add(radiusY);
        Include(center.X - radiusX, center.Y - radiusY);
        Include(center.X + radiusX, center.Y + radiusY);

        // The contour starts and ends at the right end of the horizontal axis
        _startX = center.X + radiusX;
        _startY = center.Y;
        _hasCurrentPoint = true;
        EndContour();
        return this;
    }

    /// <summary>Adds a closed circular contour.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius. Must be zero or positive.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate or the radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    public VectorPathBuilder AddCircle(PointD center, double radius)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radius, nameof(radius));
        return AddEllipse(center, radius, radius);
    }

    /// <summary>Adds a closed contour made of straight segments between consecutive points.</summary>
    /// <param name="points">The vertices. At least one is required.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public VectorPathBuilder AddPolygon(ReadOnlySpan<PointD> points)
    {
        if (points.IsEmpty)
            throw new ArgumentException("A polygon needs at least one point.", nameof(points));

        foreach (var point in points)
        {
            DrawingGuard.Point(point, nameof(points));
        }

        AppendMoveTo(points[0].X, points[0].Y);
        foreach (var point in points[1..])
        {
            AppendLineTo(point.X, point.Y);
        }

        return Close();
    }

    /// <summary>Creates the path. The builder keeps its content and can be extended to build another path.</summary>
    /// <returns>An immutable path.</returns>
    public VectorPath Build()
    {
        var bounds = _hasBounds ? RectangleD.FromLTRB(_minX, _minY, _maxX, _maxY) : RectangleD.Empty;
        return new VectorPath([.. _commands], [.. _points], bounds);
    }

    /// <summary>Removes every element, so that the builder can be reused.</summary>
    public void Clear()
    {
        _commands.Clear();
        _points.Clear();
        _hasCurrentPoint = false;
        _contourEnded = false;
        _hasBounds = false;
    }

    private void AppendMoveTo(double x, double y)
    {
        _commands.Add(PathCommand.MoveTo);
        AppendPoint(x, y);
        _startX = x;
        _startY = y;
        _hasCurrentPoint = true;
        _contourEnded = false;
        SetCurrentPoint(x, y);
    }

    private void AppendLineTo(double x, double y)
    {
        BeginSegment();
        _commands.Add(PathCommand.LineTo);
        AppendPoint(x, y);
        SetCurrentPoint(x, y);
    }

    /// <summary>Makes sure a contour is open at the current point before a segment is appended.</summary>
    private void BeginSegment()
    {
        if (!_hasCurrentPoint)
            throw new InvalidOperationException("The path has no current point: start a contour with MoveTo first.");

        if (_contourEnded)
        {
            AppendMoveTo(_currentX, _currentY);
        }
    }

    private void EndContour()
    {
        _contourEnded = true;
        SetCurrentPoint(_startX, _startY);
    }

    private void SetCurrentPoint(double x, double y)
    {
        _currentX = x;
        _currentY = y;
    }

    private void AppendPoint(double x, double y)
    {
        _points.Add(x);
        _points.Add(y);
        Include(x, y);
    }

    private void Include(double x, double y)
    {
        if (!_hasBounds)
        {
            _minX = _maxX = x;
            _minY = _maxY = y;
            _hasBounds = true;
            return;
        }

        _minX = Math.Min(_minX, x);
        _minY = Math.Min(_minY, y);
        _maxX = Math.Max(_maxX, x);
        _maxY = Math.Max(_maxY, y);
    }
}
