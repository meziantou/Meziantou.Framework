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
/// ignored, and so is what lies outside the clips of <see cref="Clip(VectorPath, DrawingAction, FillRule)"/>. Every
/// coordinate must be a finite number whose magnitude does not exceed 16777216.
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
/// opaque. See <see cref="Brush"/> for the conversion of colors to the pixel format of the frame, and
/// <see cref="Brush.CompositeMode"/> for the other ways a paint is combined with the pixels (replacing, erasing,
/// painting behind, masking).
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

    /// <summary>Replaces every pixel of the canvas by a color, alpha included: a transparent color erases the canvas.</summary>
    /// <remarks>
    /// This is filling the canvas with <see cref="CompositeMode.Source"/>; <see cref="Fill(Brush)"/> paints over the
    /// pixels instead. On a pixel format without alpha, the pixels get the color multiplied by its alpha.
    /// </remarks>
    /// <param name="color">The color, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Clear(Rgba64 color) => GetSession().Clear(color);

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

    /// <summary>Draws the pixels of a frame of another image, without scaling them.</summary>
    /// <remarks>
    /// <para>
    /// The image is composited over the canvas like a paint, with its own alpha. At a position on whole pixels, its
    /// pixels are those of the source, exactly; elsewhere they are interpolated (see <see cref="ImageBrush"/>, which
    /// this method is a shortcut for, with the pixels of the edges extended). The pixels are converted to the pixel
    /// format of the canvas; no color profile is applied.
    /// </para>
    /// <para>
    /// The frame is read while it is drawn: it must not be one of the frames the canvas draws on.
    /// </para>
    /// </remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="position">The position of its top-left corner on the canvas. A <see cref="Point"/> converts implicitly.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, PointD position, double opacity = 1) => GetSession().DrawImage(source, position, opacity);

    /// <summary>Draws the pixels of a frame of another image, stretched to a rectangle.</summary>
    /// <remarks>
    /// The pixels are interpolated (see <see cref="ImageBrush"/>): an image that is made much smaller skips pixels.
    /// <see cref="DrawImage(ImageFrame, RectangleD, ImageSampling, double)"/> with <see cref="ImageSampling.Smooth"/>
    /// reads them all. See <see cref="DrawImage(ImageFrame, PointD, double)"/>.
    /// </remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="destination">The rectangle of the canvas the frame fills. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, RectangleD destination, double opacity = 1) => GetSession().DrawImage(source, sourceRegion: null, destination, ImageSampling.Bilinear, opacity);

    /// <summary>Draws some of the pixels of a frame of another image, stretched to a rectangle.</summary>
    /// <remarks>See <see cref="DrawImage(ImageFrame, RectangleD, double)"/>. The pixels around the region are not read.</remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="sourceRegion">The pixels of the frame to draw. Must be inside the frame.</param>
    /// <param name="destination">The rectangle of the canvas the region fills. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, <paramref name="sourceRegion"/> is not inside the frame, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, Rectangle sourceRegion, RectangleD destination, double opacity = 1) => GetSession().DrawImage(source, sourceRegion, destination, ImageSampling.Bilinear, opacity);

    /// <summary>Draws the pixels of a frame of another image, placed on the canvas by a transformation: turned, scaled, skewed or mirrored.</summary>
    /// <remarks>
    /// <para>
    /// The transformation applies to the image as it would be drawn at the origin of the canvas, without scaling: the
    /// pixel <c>(i, j)</c> of the frame is the square from <c>(i, j)</c> to <c>(i + 1, j + 1)</c> before it applies.
    /// <see cref="AffineTransform.CreateRotation(double, PointD)"/> around the center of the frame, then a
    /// translation, draws the image turned around its center.
    /// </para>
    /// <para>
    /// The edges of the image are antialiased as the edges of a shape are, and its pixels are interpolated (see
    /// <see cref="ImageBrush"/>, which this method is a shortcut for, with the pixels of the edges extended). A
    /// transformation that flattens the image to a line draws nothing. See <see cref="DrawImage(ImageFrame, PointD, double)"/>.
    /// </para>
    /// </remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="transform">The transformation that places the frame on the canvas.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of a corner of the image on the canvas exceeds 16777216, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, AffineTransform transform, double opacity = 1) => GetSession().DrawImage(source, transform, ImageSampling.Bilinear, opacity);

    /// <summary>Draws the pixels of a frame of another image, stretched to a rectangle, read in a given way.</summary>
    /// <remarks>
    /// <see cref="ImageSampling.Smooth"/> reduces an image without skipping pixels (a thumbnail of a photograph), and
    /// <see cref="ImageSampling.Nearest"/> enlarges one with its pixels as squares. See
    /// <see cref="DrawImage(ImageFrame, PointD, double)"/>.
    /// </remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="destination">The rectangle of the canvas the frame fills. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, <paramref name="sampling"/> is not a defined <see cref="ImageSampling"/>, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, RectangleD destination, ImageSampling sampling, double opacity = 1) => GetSession().DrawImage(source, sourceRegion: null, destination, sampling, opacity);

    /// <summary>Draws a region of the pixels of a frame of another image, stretched to a rectangle, read in a given way.</summary>
    /// <remarks>See <see cref="DrawImage(ImageFrame, RectangleD, ImageSampling, double)"/> and <see cref="DrawImage(ImageFrame, Rectangle, RectangleD, double)"/>.</remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="sourceRegion">The pixels of the frame that are drawn. Its pixels on the edges are extended; the pixels around it are not read.</param>
    /// <param name="destination">The rectangle of the canvas the region fills. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sourceRegion"/> is not inside the frame, the magnitude of a coordinate exceeds 16777216, <paramref name="sampling"/> is not a defined <see cref="ImageSampling"/>, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, Rectangle sourceRegion, RectangleD destination, ImageSampling sampling, double opacity = 1) => GetSession().DrawImage(source, sourceRegion, destination, sampling, opacity);

    /// <summary>Draws the pixels of a frame of another image, placed on the canvas by a transformation, read in a given way.</summary>
    /// <remarks>See <see cref="DrawImage(ImageFrame, AffineTransform, double)"/> and <see cref="DrawImage(ImageFrame, RectangleD, ImageSampling, double)"/>.</remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="transform">The transformation that places the frame on the canvas.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of a corner of the image on the canvas exceeds 16777216, <paramref name="sampling"/> is not a defined <see cref="ImageSampling"/>, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, AffineTransform transform, ImageSampling sampling, double opacity = 1) => GetSession().DrawImage(source, transform, sampling, opacity);

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

    /// <summary>Paints a rectangle whose corners are quarters of circles, each with its own radius.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="radii">The radius of each corner; radii that do not fit are reduced together (see <see cref="CornerRadii"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillRoundedRectangle(Brush brush, RectangleD rectangle, CornerRadii radii) => GetSession().FillRoundedRectangle(brush, rectangle, radii);

    /// <summary>Paints the outline of a rectangle whose corners are quarters of circles, each with its own radius. The stroke is centered on the outline.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="radii">The radius of each corner; radii that do not fit are reduced together (see <see cref="CornerRadii"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeRoundedRectangle(Pen pen, RectangleD rectangle, CornerRadii radii) => GetSession().StrokeRoundedRectangle(pen, rectangle, radii);

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

    /// <summary>Runs a callback whose drawings only change the inside of a path.</summary>
    /// <remarks>
    /// <para>
    /// The clip lasts as long as the callback, and applies to every drawing made with the canvas it receives:
    /// <see cref="Fill(Brush)"/> then paints the inside of the path. A clip started inside another one keeps what both
    /// let through.
    /// </para>
    /// <para>
    /// The edge of a clip is antialiased like the edge of a shape: each pixel has the area of it that is inside the
    /// path, and the coverage of what is drawn is multiplied by it, rounded to the nearest 1/65536. A pixel on the edge
    /// of both a shape and the clip therefore gets the product of the two areas, not the area of what they have in
    /// common. The clip keeps one value per pixel of the bounds of the path, rented from the allocation scope of the
    /// image when the clip starts.
    /// </para>
    /// </remarks>
    /// <param name="path">The path. Open contours are closed by a straight segment.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="fillRule">The rule deciding what is inside when contours overlap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fillRule"/> is not a defined <see cref="FillRule"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Clip(VectorPath path, DrawingAction action, FillRule fillRule = FillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushClip(path, fillRule);
        try
        {
            action(this);
        }
        finally
        {
            session.PopClip();
        }
    }

    /// <summary>Runs a callback whose drawings only change the inside of a path, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="path">The path. Open contours are closed by a straight segment.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="fillRule">The rule deciding what is inside when contours overlap.</param>
    /// <remarks>See <see cref="Clip(VectorPath, DrawingAction, FillRule)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fillRule"/> is not a defined <see cref="FillRule"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Clip<TState>(VectorPath path, TState state, DrawingAction<TState> action, FillRule fillRule = FillRule.NonZero)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushClip(path, fillRule);
        try
        {
            action(this, state);
        }
        finally
        {
            session.PopClip();
        }
    }

    /// <summary>Runs a callback whose drawings only change the inside of a rectangle.</summary>
    /// <remarks>
    /// A rectangle on whole coordinates (a <see cref="Rectangle"/> of pixels) is exact and costs nothing: its pixels are
    /// in or out. Any other rectangle is a clip like the one of a path: see <see cref="Clip(VectorPath, DrawingAction, FillRule)"/>.
    /// </remarks>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Clip(RectangleD rectangle, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushClip(rectangle);
        try
        {
            action(this);
        }
        finally
        {
            session.PopClip();
        }
    }

    /// <summary>Runs a callback whose drawings only change the inside of a rectangle, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="rectangle">The rectangle. A <see cref="Rectangle"/> of pixels converts implicitly.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Clip(RectangleD, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Clip<TState>(RectangleD rectangle, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushClip(rectangle);
        try
        {
            action(this, state);
        }
        finally
        {
            session.PopClip();
        }
    }

    /// <summary>Runs a callback whose drawings are made on a transparent layer, which is then drawn on the canvas as a whole: with an opacity, or with an operator.</summary>
    /// <remarks>
    /// <para>
    /// Shapes drawn one after the other with a translucent paint show through each other where they overlap. In a
    /// layer, they are drawn as they are, then the whole is made translucent: <c>canvas.Layer(0.5, ...)</c> fades a
    /// drawing without changing it. With a <paramref name="compositeMode"/>, the whole drawing is combined with the
    /// canvas: a layer drawn with <see cref="CompositeMode.DestinationIn"/> keeps the canvas where the drawing is
    /// opaque (a mask made of several shapes, or of a text), and one drawn with
    /// <see cref="CompositeMode.DestinationOut"/> erases it there.
    /// </para>
    /// <para>
    /// The layer is an image of the size of the canvas, transparent when the callback starts, with colors and alpha
    /// (8-bit samples on an 8-bit color canvas, 16-bit samples otherwise): a drawing of opaque colors on whole pixels
    /// is the same in a layer and on the canvas. Its pixels are the pixels of the canvas: the coordinates do not change. When the callback
    /// returns, the layer is drawn on the canvas (or on the layer it was started in) as an image is: its pixels are
    /// converted to the pixel format of the canvas, and the opacity multiplies the coverage, as that of
    /// <see cref="DrawImage(ImageFrame, PointD, double)"/> does. When the callback fails, the layer is not drawn.
    /// </para>
    /// <para>
    /// A clip that is active when the layer starts applies to the layer as a whole. The image of a layer is taken from
    /// the allocation scope of the image when a layer first starts at its depth, and kept until the drawing ends; a
    /// layer costs the clearing and the drawing of the pixels the clip can show: start a layer inside
    /// <see cref="Clip(RectangleD, DrawingAction)"/> when it only covers a part of a large canvas.
    /// </para>
    /// </remarks>
    /// <param name="opacity">The opacity of the layer, from 0 (nothing is drawn; the callback still runs) to 1.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="compositeMode">The operator that combines the layer with the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not between 0 and 1, or <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Layer(double opacity, DrawingAction action, CompositeMode compositeMode = CompositeMode.SourceOver)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushLayer(opacity, compositeMode);
        var isDrawn = false;
        try
        {
            action(this);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are made on a transparent layer, which is then drawn on the canvas as a whole, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="opacity">The opacity of the layer, from 0 (nothing is drawn; the callback still runs) to 1.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="compositeMode">The operator that combines the layer with the canvas.</param>
    /// <remarks>See <see cref="Layer(double, DrawingAction, CompositeMode)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not between 0 and 1, or <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Layer<TState>(double opacity, TState state, DrawingAction<TState> action, CompositeMode compositeMode = CompositeMode.SourceOver)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushLayer(opacity, compositeMode);
        var isDrawn = false;
        try
        {
            action(this, state);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

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
