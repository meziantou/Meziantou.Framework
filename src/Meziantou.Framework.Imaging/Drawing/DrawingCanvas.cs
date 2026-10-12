using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// The drawing surface handed to the callback of <see cref="ImageFrame.Draw(DrawingAction, CancellationToken)"/> or
/// <see cref="Image.Draw(DrawingAction, CancellationToken)"/>. It is only valid until the callback returns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coordinates.</b> The origin is the top-left corner of the stored pixels and Y grows downward. Integer coordinates are
/// pixel boundaries: the pixel <c>(x, y)</c> covers the square from <c>(x, y)</c> to <c>(x + 1, y + 1)</c>. A rectangle
/// on integer coordinates is therefore filled exactly, and a one-pixel horizontal line is crisp when its path runs
/// through the middle of a row of pixels (<c>y + 0.5</c>). Shapes are clipped to the canvas; what lies outside is
/// ignored. Every coordinate must be a finite number whose magnitude does not exceed 16777216.
/// </para>
/// <para>
/// <b>Antialiasing.</b> Each pixel is painted in proportion to the area of it that the shape covers. That area is exact,
/// including where the contours of a shape overlap or intersect themselves, and is rounded to the nearest 1/65536. A
/// pixel that is not covered is left untouched, bit for bit; a pixel that is entirely covered by an opaque color
/// receives exactly that color. Curves are replaced by chords that stay within 1/16 of a pixel of the curve.
/// </para>
/// <para>
/// <b>Compositing.</b> The paint is composited over the existing pixels (source-over) on the stored sample values, with
/// straight alpha and exact integer arithmetic, rounded to nearest with ties upward. Pixel formats without alpha stay
/// opaque. See <see cref="Brush"/> for the conversion of colors to the pixel format of the frame.
/// </para>
/// <para>
/// <b>Failures.</b> Drawing is a pixel-only edit: when a call fails or is canceled, the calls that completed stay, and
/// some rows of the failing call may be painted. A call rents all its working storage before it changes a pixel, so a
/// call that exceeds <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> changes nothing.
/// </para>
/// </remarks>
public readonly ref struct DrawingCanvas
{
    private readonly DrawingSession? _session;

    internal DrawingCanvas(DrawingSession session)
    {
        _session = session;
    }

    /// <summary>Gets the width of the canvas, in pixels (0 for a default instance).</summary>
    public int Width => _session?.Width ?? 0;

    /// <summary>Gets the height of the canvas, in pixels (0 for a default instance).</summary>
    public int Height => _session?.Height ?? 0;

    /// <summary>Paints the whole canvas.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Fill(Brush brush) => GetSession().Fill(brush);

    /// <summary>Paints the inside of a path. Open contours are closed by a straight segment.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="path">The path.</param>
    /// <param name="fillRule">The rule deciding what is inside when contours overlap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fillRule"/> is not a defined <see cref="FillRule"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Fill(Brush brush, VectorPath path, FillRule fillRule = FillRule.NonZero) => GetSession().Fill(brush, path, fillRule);

    /// <summary>Paints the outline of a path.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="path">The path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Stroke(Pen pen, VectorPath path) => GetSession().Stroke(pen, path);

    /// <summary>Paints a rectangle.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillRectangle(Brush brush, RectangleD rectangle) => GetSession().FillRectangle(brush, rectangle);

    /// <summary>Paints the outline of a rectangle. The stroke is centered on the sides of the rectangle.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeRectangle(Pen pen, RectangleD rectangle) => GetSession().StrokeRectangle(pen, rectangle);

    /// <summary>Paints a rectangle whose corners are quarters of a circle.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="radius">The radius of the corners. Must be zero or positive; it is limited to half the shortest side of the rectangle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate or the radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillRoundedRectangle(Brush brush, RectangleD rectangle, double radius) => GetSession().FillRoundedRectangle(brush, rectangle, radius);

    /// <summary>Paints the outline of a rectangle whose corners are quarters of a circle. The stroke is centered on the outline.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="radius">The radius of the corners. Must be zero or positive; it is limited to half the shortest side of the rectangle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate or the radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeRoundedRectangle(Pen pen, RectangleD rectangle, double radius) => GetSession().StrokeRoundedRectangle(pen, rectangle, radius);

    /// <summary>Paints the inside of the closed contour made of straight segments between consecutive points.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="points">The vertices. At least one is required.</param>
    /// <param name="fillRule">The rule deciding what is inside when the contour crosses itself.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or <paramref name="fillRule"/> is not a defined <see cref="FillRule"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillPolygon(Brush brush, ReadOnlySpan<PointD> points, FillRule fillRule = FillRule.NonZero) => GetSession().FillPolygon(brush, points, fillRule);

    /// <summary>Paints the outline of the closed contour made of straight segments between consecutive points.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="points">The vertices. At least one is required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokePolygon(Pen pen, ReadOnlySpan<PointD> points) => GetSession().StrokePoints(pen, points, close: true);

    /// <summary>Paints the straight segments between consecutive points, with the caps of the pen at both ends.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="points">The points. At least one is required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokePolyline(Pen pen, ReadOnlySpan<PointD> points) => GetSession().StrokePoints(pen, points, close: false);

    /// <summary>Paints a slice of a disk: the part between two radii and the arc that joins them.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="center">The center of the disk.</param>
    /// <param name="radius">The radius. Must be zero or positive.</param>
    /// <param name="startAngle">The angle of the first radius, in degrees from the positive x axis, clockwise on the image.</param>
    /// <param name="sweepAngle">The angle of the slice, in degrees: positive to turn clockwise on the image, negative for the other way. Beyond 360 degrees either way, the slice is the whole disk.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate, the radius or an angle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillPie(Brush brush, PointD center, double radius, double startAngle, double sweepAngle) => GetSession().FillPie(brush, center, radius, startAngle, sweepAngle);

    /// <summary>Paints an arc of a circle, with the caps of the pen at both ends.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="center">The center of the circle.</param>
    /// <param name="radius">The radius. Must be zero or positive.</param>
    /// <param name="startAngle">The angle of the first point, in degrees from the positive x axis, clockwise on the image.</param>
    /// <param name="sweepAngle">The angle of the arc, in degrees: positive to turn clockwise on the image, negative for the other way. Beyond 360 degrees either way, the arc is the whole circle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate, the radius or an angle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeArc(Pen pen, PointD center, double radius, double startAngle, double sweepAngle) => GetSession().StrokeArc(pen, center, radius, startAngle, sweepAngle);

    /// <summary>Paints an ellipse whose axes are horizontal and vertical.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="center">The center.</param>
    /// <param name="radiusX">The horizontal radius. Must be zero or positive.</param>
    /// <param name="radiusY">The vertical radius. Must be zero or positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate or a radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or a radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillEllipse(Brush brush, PointD center, double radiusX, double radiusY) => GetSession().FillEllipse(brush, center, radiusX, radiusY);

    /// <summary>Paints the outline of an ellipse whose axes are horizontal and vertical.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="center">The center.</param>
    /// <param name="radiusX">The horizontal radius. Must be zero or positive.</param>
    /// <param name="radiusY">The vertical radius. Must be zero or positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate or a radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or a radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeEllipse(Pen pen, PointD center, double radiusX, double radiusY) => GetSession().StrokeEllipse(pen, center, radiusX, radiusY);

    /// <summary>Paints a disk.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius. Must be zero or positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate or the radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillCircle(Brush brush, PointD center, double radius)
    {
        DrawingGuard.Length(radius, nameof(radius));
        GetSession().FillEllipse(brush, center, radius, radius);
    }

    /// <summary>Paints the outline of a circle.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius. Must be zero or positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate or the radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeCircle(Pen pen, PointD center, double radius)
    {
        DrawingGuard.Length(radius, nameof(radius));
        GetSession().StrokeEllipse(pen, center, radius, radius);
    }

    /// <summary>Paints a straight line, with the caps of the pen at both ends.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="start">The first point.</param>
    /// <param name="end">The last point.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeLine(Pen pen, PointD start, PointD end) => GetSession().StrokeLine(pen, start, end);

    /// <summary>Paints a quadratic Bézier curve, with the caps of the pen at both ends.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="start">The first point.</param>
    /// <param name="control">The control point.</param>
    /// <param name="end">The last point.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeBezier(Pen pen, PointD start, PointD control, PointD end) => GetSession().StrokeBezier(pen, start, control, end);

    /// <summary>Paints a cubic Bézier curve, with the caps of the pen at both ends.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="start">The first point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The last point.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeBezier(Pen pen, PointD start, PointD control1, PointD control2, PointD end) => GetSession().StrokeBezier(pen, start, control1, control2, end);

    /// <summary>Paints the glyphs of a text layout.</summary>
    /// <param name="brush">The paint.</param>
    /// <param name="layout">The text, laid out.</param>
    /// <param name="origin">The position of the top-left corner of the layout; the first baseline is <see cref="TextLayout.Baseline"/> below it.</param>
    /// <remarks>
    /// The glyphs are one shape: where they overlap, the brush is applied once. The call allocates nothing once the
    /// working storage of the canvas has grown to the largest glyph. Glyphs are not snapped to the pixel grid.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillText(Brush brush, TextLayout layout, PointD origin) => GetSession().FillText(brush, layout, origin);

    /// <summary>Lays out a text on one line per line of the text and paints its glyphs.</summary>
    /// <param name="brush">The paint.</param>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size of the font, in pixels per em.</param>
    /// <param name="origin">The position of the top-left corner of the text; the first baseline is half the line gap of the font plus its ascent below it.</param>
    /// <remarks>
    /// This overload creates a <see cref="TextLayout"/> each time it is called, which allocates. Create the layout once
    /// with <see cref="TextLayout.Create(string, Font, double)"/> to measure a text, or to draw it several times.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/>, <paramref name="text"/> or <paramref name="font"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or <paramref name="size"/> is not a positive number up to 65536.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of the font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The text exceeds a limit of the configuration of the font, or the working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillText(Brush brush, string text, Font font, double size, PointD origin)
    {
        ArgumentNullException.ThrowIfNull(brush);
        DrawingGuard.Point(origin, nameof(origin));
        var session = GetSession();
        session.FillText(brush, TextLayout.Create(text, font, size), origin);
    }

    /// <summary>Paints the outlines of the glyphs of a text layout with a pen.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="layout">The text, laid out.</param>
    /// <param name="origin">The position of the top-left corner of the layout; the first baseline is <see cref="TextLayout.Baseline"/> below it.</param>
    /// <remarks>The strokes of all the outlines are one shape: where they overlap, the brush of the pen is applied once.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeText(Pen pen, TextLayout layout, PointD origin) => GetSession().StrokeText(pen, layout, origin);

    /// <summary>Paints the glyphs of a text layout along a path.</summary>
    /// <param name="brush">The paint of the spans that have none.</param>
    /// <param name="layout">The text, laid out.</param>
    /// <param name="path">The path the text follows: the horizontal axis of the layout, with its first baseline on it.</param>
    /// <param name="options">Where the text is on the path, or <see langword="null"/> for <see cref="TextPathOptions.Default"/>.</param>
    /// <remarks>
    /// <para>
    /// The contours of the path are followed one after the other, in their direction; a path of one closed contour is
    /// followed around as many times as the text needs. Each cluster of the text (a character with its marks, a
    /// ligature) is placed as a whole: the middle of its advance is on the path, and it is turned to the direction of
    /// the path there. Glyphs are not bent, so they spread on the outside of a bend and overlap on the inside.
    /// </para>
    /// <para>
    /// A path is measured as it is drawn: by its chords, within 1/16 of a pixel. A text on a path that draws nothing
    /// (no segment) is not drawn.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/>, <paramref name="layout"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillText(Brush brush, TextLayout layout, VectorPath path, TextPathOptions? options = null) => GetSession().FillText(brush, layout, path, options);

    /// <summary>Paints the outlines of the glyphs of a text layout along a path, with a pen.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="layout">The text, laid out.</param>
    /// <param name="path">The path the text follows: the horizontal axis of the layout, with its first baseline on it.</param>
    /// <param name="options">Where the text is on the path, or <see langword="null"/> for <see cref="TextPathOptions.Default"/>.</param>
    /// <remarks>The text is placed as by <see cref="FillText(Brush, TextLayout, VectorPath, TextPathOptions?)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/>, <paramref name="layout"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeText(Pen pen, TextLayout layout, VectorPath path, TextPathOptions? options = null) => GetSession().StrokeText(pen, layout, path, options);

    private DrawingSession GetSession()
    {
        var session = _session;
        if (session is null || !session.IsActive)
            ThrowNotActive();

        return session;
    }

    [DoesNotReturn]
    private static void ThrowNotActive()
        => throw new InvalidOperationException("The drawing canvas is only valid during the callback that received it.");
}
