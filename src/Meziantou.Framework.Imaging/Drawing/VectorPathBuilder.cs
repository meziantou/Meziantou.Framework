using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Builds a <see cref="VectorPath"/>.</summary>
/// <remarks>
/// <para>
/// A contour starts with <see cref="MoveTo(double, double)"/> and continues with segments from the current point. A contour
/// that is not closed is open when it is stroked (it gets the caps of the pen) and is closed implicitly when it is
/// filled. After <see cref="Close"/> or a method that adds a closed contour (<see cref="AddRectangle"/>,
/// <see cref="AddRoundedRectangle(RectangleD, double)"/>, <see cref="AddEllipse(PointD, double, double)"/>,
/// <see cref="AddCircle"/>, <see cref="AddPolygon"/>, <see cref="AddPie"/>), the current point is the first point of
/// the contour that was just closed, and the next segment starts a new contour there.
/// </para>
/// <para>
/// Angles are in degrees, from the positive x axis; Y grows downward, so a positive angle turns clockwise on the image.
/// The ends of an arc do not depend on the platform (see <see cref="AffineTransform"/>).
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

    /// <summary>Adds an arc of an ellipse from the current point to a point, described as the arcs of SVG paths are.</summary>
    /// <remarks>
    /// <para>
    /// Two ellipses of the given radii and rotation pass through both points (or one, or none), and each has two arcs
    /// between them: <paramref name="largeArc"/> chooses the arcs of more than half a turn, and
    /// <paramref name="clockwise"/> the one that turns clockwise on the image. Radii too small for an ellipse to reach both
    /// points are enlarged, in proportion, until one does.
    /// </para>
    /// <para>
    /// When a radius is zero, the arc is a straight segment; when the point is the current point, nothing is added.
    /// </para>
    /// </remarks>
    /// <param name="radiusX">The first radius of the ellipse. Must be zero or positive.</param>
    /// <param name="radiusY">The second radius of the ellipse. Must be zero or positive.</param>
    /// <param name="rotation">The angle of the first radius, in degrees from the x axis, clockwise on the image.</param>
    /// <param name="largeArc"><see langword="true"/> for an arc of more than half a turn.</param>
    /// <param name="clockwise"><see langword="true"/> for the arc that turns clockwise on the image.</param>
    /// <param name="x">The horizontal coordinate of the end point.</param>
    /// <param name="y">The vertical coordinate of the end point.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate, a radius or the rotation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, a radius is negative or exceeds 16777216, or the arc goes beyond the largest coordinate.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool clockwise, double x, double y)
    {
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        DrawingGuard.Angle(rotation, nameof(rotation));
        DrawingGuard.Coordinate(x, nameof(x));
        DrawingGuard.Coordinate(y, nameof(y));
        if (!_hasCurrentPoint)
            throw new InvalidOperationException("The path has no current point: start a contour with MoveTo first.");

        if (x == _currentX && y == _currentY)
            return this;

        if (!ArcGeometry.TryGetCenterForm(_currentX, _currentY, x, y, radiusX, radiusY, rotation, largeArc, clockwise, out var centerX, out var centerY, out var ux, out var uy, out var vx, out var vy, out var start, out var sweep))
        {
            AppendLineTo(x, y);
            return this;
        }

        // Nothing is added before the whole arc is known to be within the limit
        var validator = default(ArcValidator);
        ArcGeometry.Append(ref validator, centerX, centerY, ux, uy, vx, vy, start, sweep, x, y);
        if (!validator.IsValid)
            throw new ArgumentOutOfRangeException(nameof(radiusX), radiusX, "The arc goes beyond the largest coordinate (16777216).");

        BeginSegment();
        var receiver = new Receiver(this);
        ArcGeometry.Append(ref receiver, centerX, centerY, ux, uy, vx, vy, start, sweep, x, y);
        return this;
    }

    /// <summary>Adds an arc of an ellipse from the current point to a point, described as the arcs of SVG paths are.</summary>
    /// <param name="radiusX">The first radius of the ellipse. Must be zero or positive.</param>
    /// <param name="radiusY">The second radius of the ellipse. Must be zero or positive.</param>
    /// <param name="rotation">The angle of the first radius, in degrees from the x axis, clockwise on the image.</param>
    /// <param name="largeArc"><see langword="true"/> for an arc of more than half a turn.</param>
    /// <param name="clockwise"><see langword="true"/> for the arc that turns clockwise on the image.</param>
    /// <param name="point">The end point.</param>
    /// <returns>This builder.</returns>
    /// <remarks>See <see cref="ArcTo(double, double, double, bool, bool, double, double)"/>.</remarks>
    /// <exception cref="ArgumentException">A coordinate, a radius or the rotation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, a radius is negative or exceeds 16777216, or the arc goes beyond the largest coordinate.</exception>
    /// <exception cref="InvalidOperationException">There is no current point: no contour was started.</exception>
    public VectorPathBuilder ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool clockwise, PointD point)
    {
        DrawingGuard.Point(point, nameof(point));
        return ArcTo(radiusX, radiusY, rotation, largeArc, clockwise, point.X, point.Y);
    }

    /// <summary>
    /// Adds an arc of an ellipse given by its center, joined to the current point by a straight segment. When there is no
    /// current point, the arc starts a contour.
    /// </summary>
    /// <param name="center">The center of the ellipse.</param>
    /// <param name="radiusX">The horizontal radius. Must be zero or positive.</param>
    /// <param name="radiusY">The vertical radius. Must be zero or positive.</param>
    /// <param name="startAngle">The angle of the first point of the arc, in degrees from the positive x axis, clockwise on the image.</param>
    /// <param name="sweepAngle">The angle of the arc, in degrees: positive to turn clockwise on the image, negative for the other way. Beyond 360 degrees either way, the arc is the whole ellipse.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate, a radius or an angle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, a radius is negative or exceeds 16777216, or the ellipse goes beyond the largest coordinate.</exception>
    public VectorPathBuilder ArcTo(PointD center, double radiusX, double radiusY, double startAngle, double sweepAngle)
    {
        ValidateCenterArc(center, radiusX, radiusY, startAngle, sweepAngle);
        var lineToStart = _hasCurrentPoint;
        if (lineToStart)
        {
            BeginSegment();
        }

        var receiver = new Receiver(this);
        ArcGeometry.AppendCenterArc(ref receiver, center.X, center.Y, radiusX, radiusY, startAngle, sweepAngle, lineToStart);
        return this;
    }

    /// <summary>Starts a new contour with an arc of an ellipse given by its center. The contour stays open.</summary>
    /// <param name="center">The center of the ellipse.</param>
    /// <param name="radiusX">The horizontal radius. Must be zero or positive.</param>
    /// <param name="radiusY">The vertical radius. Must be zero or positive.</param>
    /// <param name="startAngle">The angle of the first point of the arc, in degrees from the positive x axis, clockwise on the image.</param>
    /// <param name="sweepAngle">The angle of the arc, in degrees: positive to turn clockwise on the image, negative for the other way. Beyond 360 degrees either way, the arc is the whole ellipse.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate, a radius or an angle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, a radius is negative or exceeds 16777216, or the ellipse goes beyond the largest coordinate.</exception>
    public VectorPathBuilder AddArc(PointD center, double radiusX, double radiusY, double startAngle, double sweepAngle)
    {
        ValidateCenterArc(center, radiusX, radiusY, startAngle, sweepAngle);
        var receiver = new Receiver(this);
        ArcGeometry.AppendCenterArc(ref receiver, center.X, center.Y, radiusX, radiusY, startAngle, sweepAngle, lineToStart: false);
        return this;
    }

    /// <summary>Adds a closed contour shaped like a slice of a pie: the center of an ellipse, an arc of it, and the two radii that join them.</summary>
    /// <param name="center">The center of the ellipse, and the first point of the contour.</param>
    /// <param name="radiusX">The horizontal radius. Must be zero or positive.</param>
    /// <param name="radiusY">The vertical radius. Must be zero or positive.</param>
    /// <param name="startAngle">The angle of the first point of the arc, in degrees from the positive x axis, clockwise on the image.</param>
    /// <param name="sweepAngle">The angle of the arc, in degrees: positive to turn clockwise on the image, negative for the other way. Beyond 360 degrees either way, the arc is the whole ellipse.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate, a radius or an angle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, a radius is negative or exceeds 16777216, or the ellipse goes beyond the largest coordinate.</exception>
    public VectorPathBuilder AddPie(PointD center, double radiusX, double radiusY, double startAngle, double sweepAngle)
    {
        ValidateCenterArc(center, radiusX, radiusY, startAngle, sweepAngle);
        AppendMoveTo(center.X, center.Y);
        var receiver = new Receiver(this);
        ArcGeometry.AppendCenterArc(ref receiver, center.X, center.Y, radiusX, radiusY, startAngle, sweepAngle, lineToStart: true);
        return Close();
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

    /// <summary>Adds a closed rectangular contour whose corners are quarters of a circle.</summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <param name="radius">The radius of the corners. Must be zero or positive; it is limited to half the shortest side of the rectangle.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate or the radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    public VectorPathBuilder AddRoundedRectangle(RectangleD rectangle, double radius)
    {
        DrawingGuard.Length(radius, nameof(radius));

        // The corners stay circular: the radius is limited by the shortest side
        radius = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2);
        return AddRoundedRectangle(rectangle, radius, radius);
    }

    /// <summary>Adds a closed rectangular contour whose corners are quarters of an ellipse.</summary>
    /// <remarks>
    /// The contour starts at the left end of the straight part of its top side and turns clockwise on the image. With a
    /// radius of zero it is the rectangle; with radii of half the sides it is an ellipse.
    /// </remarks>
    /// <param name="rectangle">The rectangle.</param>
    /// <param name="radiusX">The horizontal radius of the corners. Must be zero or positive; it is limited to half the width of the rectangle.</param>
    /// <param name="radiusY">The vertical radius of the corners. Must be zero or positive; it is limited to half the height of the rectangle.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate or a radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or a radius is negative or exceeds 16777216.</exception>
    public VectorPathBuilder AddRoundedRectangle(RectangleD rectangle, double radiusX, double radiusY)
    {
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        var receiver = new Receiver(this);
        ArcGeometry.AppendRoundedRectangle(ref receiver, rectangle, radiusX, radiusY);
        return this;
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

    /// <summary>Adds a closed elliptical contour whose axes are turned by an angle.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radiusX">The radius along the axis that is horizontal before the rotation. Must be zero or positive.</param>
    /// <param name="radiusY">The radius along the other axis. Must be zero or positive.</param>
    /// <param name="rotation">The angle of the axes, in degrees, clockwise on the image.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A coordinate, a radius or the rotation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, a radius is negative or exceeds 16777216, or the ellipse goes beyond the largest coordinate.</exception>
    public VectorPathBuilder AddEllipse(PointD center, double radiusX, double radiusY, double rotation)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        DrawingGuard.Angle(rotation, nameof(rotation));
        if (rotation == 0)
            return AddEllipse(center, radiusX, radiusY);

        var axes = AffineTransform.CreateRotation(rotation);
        var ux = axes.M11 * radiusX;
        var uy = axes.M12 * radiusX;
        var vx = axes.M21 * radiusY;
        var vy = axes.M22 * radiusY;
        var extentX = Math.Abs(ux) + Math.Abs(vx);
        var extentY = Math.Abs(uy) + Math.Abs(vy);
        if (Math.Abs(center.X) + extentX > DrawingGuard.MaxCoordinate || Math.Abs(center.Y) + extentY > DrawingGuard.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(center), center, "The ellipse goes beyond the largest coordinate (16777216).");

        _commands.Add(PathCommand.EllipseAxes);
        _points.Add(center.X);
        _points.Add(center.Y);
        _points.Add(ux);
        _points.Add(uy);
        _points.Add(vx);
        _points.Add(vy);
        Include(center.X - extentX, center.Y - extentY);
        Include(center.X + extentX, center.Y + extentY);

        // The contour starts and ends at the end of the first radius
        _startX = center.X + ux;
        _startY = center.Y + uy;
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

    private static void ValidateCenterArc(PointD center, double radiusX, double radiusY, double startAngle, double sweepAngle)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        DrawingGuard.Angle(startAngle, nameof(startAngle));
        DrawingGuard.Angle(sweepAngle, nameof(sweepAngle));
        if (Math.Abs(center.X) + radiusX > DrawingGuard.MaxCoordinate || Math.Abs(center.Y) + radiusY > DrawingGuard.MaxCoordinate)
            throw new ArgumentOutOfRangeException(nameof(center), center, "The ellipse goes beyond the largest coordinate (16777216).");
    }

    private void AppendArc(scoped in ArcPiece piece)
    {
        _commands.Add(piece.IsContinuation ? PathCommand.ArcContinuation : PathCommand.Arc);
        _points.Add(piece.CenterX);
        _points.Add(piece.CenterY);
        _points.Add(piece.Ux);
        _points.Add(piece.Uy);
        _points.Add(piece.Vx);
        _points.Add(piece.Vy);
        _points.Add(piece.Ax);
        _points.Add(piece.Ay);
        _points.Add(piece.Bx);
        _points.Add(piece.By);
        _points.Add(piece.EndX);
        _points.Add(piece.EndY);
        piece.GetCorner(out var cornerX, out var cornerY);
        Include(cornerX, cornerY);
        Include(piece.EndX, piece.EndY);
        SetCurrentPoint(piece.EndX, piece.EndY);
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

    /// <summary>Adds the elements of a shape to the builder.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct Receiver(VectorPathBuilder builder) : IPathReceiver
    {
        public void MoveTo(double x, double y) => builder.AppendMoveTo(x, y);

        public void LineTo(double x, double y) => builder.AppendLineTo(x, y);

        public void Arc(scoped in ArcPiece piece) => builder.AppendArc(piece);

        public void Close() => builder.Close();
    }

    /// <summary>Checks that the pieces of an arc stay within the coordinates a path accepts.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct ArcValidator : IPathReceiver
    {
        private bool _isInvalid;

        public readonly bool IsValid => !_isInvalid;

        public readonly void MoveTo(double x, double y)
        {
        }

        public readonly void LineTo(double x, double y)
        {
        }

        public void Arc(scoped in ArcPiece piece)
        {
            piece.GetCorner(out var x, out var y);

            // Written so that a value that is not a number is rejected too
            _isInvalid |= !(Math.Abs(x) <= DrawingGuard.MaxCoordinate && Math.Abs(y) <= DrawingGuard.MaxCoordinate
                && Math.Abs(piece.EndX) <= DrawingGuard.MaxCoordinate && Math.Abs(piece.EndY) <= DrawingGuard.MaxCoordinate);
        }

        public readonly void Close()
        {
        }
    }
}
