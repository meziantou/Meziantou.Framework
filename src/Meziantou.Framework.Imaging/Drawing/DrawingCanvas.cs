using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
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
/// <see cref="Antialiasing(bool, DrawingAction)"/> draws with hard edges instead: a pixel is painted entirely or not at
/// all.
/// </para>
/// <para>
/// <b>Compositing.</b> The paint is composited over the existing pixels (source-over) on the stored sample values, with
/// straight alpha and exact integer arithmetic, rounded to nearest with ties upward. Pixel formats without alpha stay
/// opaque. See <see cref="Brush"/> for the conversion of colors to the pixel format of the frame, and
/// <see cref="Brush.CompositeMode"/> for the other ways a paint is combined with the pixels (replacing, erasing,
/// painting behind, masking), and <see cref="Brush.BlendMode"/> for the ways its color is mixed with theirs
/// (multiplying, screening, tinting). <see cref="Compositing(CompositingSpace, DrawingAction)"/> combines the paints
/// with the pixels in linear light instead of on the stored sample values.
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
    public int Width => _session?.CanvasWidth ?? 0;

    /// <summary>Gets the height of the canvas, in pixels (0 for a default instance).</summary>
    public int Height => _session?.CanvasHeight ?? 0;

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

    /// <summary>Draws a rectangle of a frame of another image that need not be made of whole pixels, stretched to a rectangle: <c>drawImage</c> of the HTML canvas with a source rectangle.</summary>
    /// <remarks>
    /// The pixels the rectangle touches are read, and the position each pixel of the canvas reads is brought into the
    /// rectangle, as browsers do: to <c>[left + 1/2, right - 1/2]</c> (the middle of the rectangle when it is narrower
    /// than a pixel) with <see cref="ImageSampling.Bilinear"/>, <see cref="ImageSampling.Bicubic"/> and
    /// <see cref="ImageSampling.Smooth"/>, so that the pixels around it weigh as little as they can, and to
    /// <c>[left, right)</c> with <see cref="ImageSampling.Nearest"/>. With a rectangle of whole pixels, the pixels are the ones
    /// of <see cref="DrawImage(ImageFrame, Rectangle, RectangleD, ImageSampling, double)"/> except near its edges, where
    /// this one does not extend the pixels of the edges but clamps the positions.
    /// </remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="sourceRegion">The rectangle of the frame that is drawn, in its pixels.</param>
    /// <param name="destination">The rectangle of the canvas the region fills.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sourceRegion"/> is not inside the frame, the magnitude of a coordinate exceeds 16777216, <paramref name="sampling"/> is not a defined <see cref="ImageSampling"/>, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, RectangleD sourceRegion, RectangleD destination, ImageSampling sampling, double opacity = 1) => GetSession().DrawImageRectangle(source, sourceRegion, destination, sampling, opacity);

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

    /// <summary>Draws the pixels of a frame of another image, placed on the canvas by a projective transformation: the image seen in perspective.</summary>
    /// <remarks>
    /// <para>
    /// The pixel <c>(x, y)</c> of the frame covers the square from <c>(x, y)</c> to <c>(x + 1, y + 1)</c>, which the
    /// transformation places on the canvas (then the transformation of the canvas). What is on the other side of the
    /// horizon of the transformation (where its <c>W</c> is not positive) is not drawn. The edges of the image are
    /// antialiased, and its pixels on the edges are extended, as for the other ways of drawing an image. An affine transformation draws what
    /// <see cref="DrawImage(ImageFrame, AffineTransform, ImageSampling, double)"/> draws, with the same pixels.
    /// </para>
    /// <para>
    /// With <see cref="ImageSampling.Smooth"/>, each pixel of the canvas reads what it covers of the image, which
    /// changes from the near side to the far side: use it for an image that the perspective reduces.
    /// </para>
    /// </remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="transform">The transformation that places the pixels of the frame on the canvas.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampling"/> is not a defined <see cref="ImageSampling"/>, <paramref name="opacity"/> is not between 0 and 1, or the transformation is affine and moves a corner of the image beyond 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, ProjectiveTransform transform, ImageSampling sampling, double opacity = 1) => GetSession().DrawImage(source, transform, sampling, opacity);

    /// <summary>Draws a frame of another image on a quadrilateral, as it is seen in perspective: on a screen, a page or a wall of a photograph.</summary>
    /// <remarks>
    /// <para>
    /// The four corners of the image are drawn on the four points, and the image between them as a plane seen in
    /// perspective: straight lines of the image stay straight, and what is further away is smaller. When the points
    /// are the corners of a parallelogram, the image is not seen in perspective, and this is
    /// <see cref="DrawImage(ImageFrame, AffineTransform, ImageSampling, double)"/>, with the same pixels.
    /// </para>
    /// <para>
    /// The points must be the corners of a convex quadrilateral, given in order around it (either way: the other way
    /// around mirrors the image). Points that are all on one line draw nothing. The edges of the quadrilateral are
    /// antialiased, and the pixels on the edges of the image are extended, as for the other ways of drawing an image.
    /// With <see cref="ImageSampling.Smooth"/>, each pixel of the canvas reads what it covers of the image, which
    /// changes from the near side to the far side: use it for an image that the perspective reduces.
    /// </para>
    /// <para>
    /// The frame is read while it is drawn: it must not be one of the frames the canvas draws on.
    /// </para>
    /// </remarks>
    /// <param name="source">The frame to draw.</param>
    /// <param name="topLeft">Where the top-left corner of the image is drawn.</param>
    /// <param name="topRight">Where the top-right corner of the image is drawn.</param>
    /// <param name="bottomRight">Where the bottom-right corner of the image is drawn.</param>
    /// <param name="bottomLeft">Where the bottom-left corner of the image is drawn.</param>
    /// <param name="sampling">How the image is read between the centers of its pixels.</param>
    /// <param name="opacity">The opacity of the image, from 0 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number, or the points are not the corners of a convex quadrilateral, in order.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, <paramref name="sampling"/> is not a defined <see cref="ImageSampling"/>, or <paramref name="opacity"/> is not between 0 and 1.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned, or <paramref name="source"/> is a frame that is being drawn on or whose pixels are being accessed.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> no longer belongs to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void DrawImage(ImageFrame source, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft, ImageSampling sampling = ImageSampling.Bilinear, double opacity = 1)
        => GetSession().DrawImage(source, topLeft, topRight, bottomRight, bottomLeft, sampling, opacity);

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

    /// <summary>Paints a curve that goes smoothly through consecutive points, with the caps of the pen at both ends: the line of a chart.</summary>
    /// <remarks>
    /// The curve is the one of <see cref="VectorPathBuilder.AddCurve"/>: a cubic Bézier curve between each point and the
    /// next one, which has at each point the direction of the line between the points around it.
    /// </remarks>
    /// <param name="pen">The stroke.</param>
    /// <param name="points">The points the curve goes through. At least one is required.</param>
    /// <param name="tension">How far the curve keeps its direction around each point, from 0 (straight segments) to 1; 0.5 is the Catmull-Rom spline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or a coordinate or <paramref name="tension"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, <paramref name="tension"/> is not between 0 and 1, or the curve goes beyond the largest coordinate.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeCurve(Pen pen, ReadOnlySpan<PointD> points, double tension = 0.5) => GetSession().StrokeCurve(pen, points, tension, close: false);

    /// <summary>Paints the outline of the closed curve that goes smoothly through consecutive points, and from the last one back to the first one.</summary>
    /// <param name="pen">The stroke.</param>
    /// <param name="points">The points the curve goes through. At least one is required.</param>
    /// <param name="tension">How far the curve keeps its direction around each point, from 0 (straight segments) to 1; 0.5 is the Catmull-Rom spline.</param>
    /// <remarks>See <see cref="VectorPathBuilder.AddClosedCurve"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or a coordinate or <paramref name="tension"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, <paramref name="tension"/> is not between 0 and 1, or the curve goes beyond the largest coordinate.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeClosedCurve(Pen pen, ReadOnlySpan<PointD> points, double tension = 0.5) => GetSession().StrokeCurve(pen, points, tension, close: true);

    /// <summary>Paints the inside of the closed curve that goes smoothly through consecutive points, and from the last one back to the first one.</summary>
    /// <param name="brush">The paint. A color converts implicitly.</param>
    /// <param name="points">The points the curve goes through. At least one is required.</param>
    /// <param name="tension">How far the curve keeps its direction around each point, from 0 (a polygon) to 1; 0.5 is the Catmull-Rom spline.</param>
    /// <param name="fillRule">The rule deciding what is inside when the curve crosses itself.</param>
    /// <remarks>See <see cref="VectorPathBuilder.AddClosedCurve"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or a coordinate or <paramref name="tension"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, <paramref name="tension"/> is not between 0 and 1, the curve goes beyond the largest coordinate, or <paramref name="fillRule"/> is not a defined <see cref="FillRule"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillClosedCurve(Brush brush, ReadOnlySpan<PointD> points, double tension = 0.5, FillRule fillRule = FillRule.NonZero) => GetSession().FillClosedCurve(brush, points, tension, fillRule);

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
    /// <param name="origin">The position of the top-left corner of the layout; the first baseline is <see cref="TextLayout.Baseline"/> below it, or on its right for a vertical layout.</param>
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
    /// <param name="origin">The position of the top-left corner of the layout; the first baseline is <see cref="TextLayout.Baseline"/> below it, or on its right for a vertical layout.</param>
    /// <remarks>The strokes of all the outlines are one shape: where they overlap, the brush of the pen is applied once.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeText(Pen pen, TextLayout layout, PointD origin) => GetSession().StrokeText(pen, layout, origin);

    /// <summary>Fills a run of glyphs placed by the caller: the glyphs of a font at the positions a text shaper gives.</summary>
    /// <remarks>
    /// <para>
    /// Each glyph is filled on its own, over the ones before it. Under a transformation that keeps the axes (or swaps
    /// them), the origin of a glyph is moved to the nearest quarter of a pixel along the line and to the nearest pixel
    /// across it, as browsers do, and its coverage is computed once per position in a pixel when
    /// <see cref="GlyphRunOptions.Cache"/> keeps it: the pixels are the same with or without a cache. The glyphs a font
    /// draws with its own colors (<c>COLR</c>, <c>sbix</c>, <c>CBDT</c>) are drawn with them, from the palette of the
    /// options; a variable font draws the glyphs of its instance (<see cref="Font.WithVariations(ReadOnlySpan{FontVariationSetting})"/>).
    /// </para>
    /// </remarks>
    /// <param name="brush">The paint of the glyphs, and the color of the glyphs a font draws with its own colors where they ask for the color of the text.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size of the text: the length of an em, in units of the drawing.</param>
    /// <param name="glyphs">The glyphs and their origins, in the coordinates of the drawing.</param>
    /// <param name="options">The synthesis, the palette and the cache, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> or <paramref name="font"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The size or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A glyph is not a glyph of the font, the size is negative, or the magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidImageContentException">The outline or the display list of a glyph is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FillGlyphs(Brush brush, Font font, double size, ReadOnlySpan<PositionedGlyph> glyphs, GlyphRunOptions? options = null) => GetSession().FillGlyphs(brush, font, size, glyphs, options);

    /// <summary>Strokes the outlines of a run of glyphs placed by the caller, as one shape: <c>strokeText</c> of the HTML canvas.</summary>
    /// <remarks>A synthetic oblique shears the outlines; a synthetic bold does not widen the pen, as for <see cref="StrokeText(Pen, TextLayout, PointD)"/>. The glyphs a font draws with its own colors are stroked as their outlines.</remarks>
    /// <param name="pen">The pen.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size of the text: the length of an em, in units of the drawing.</param>
    /// <param name="glyphs">The glyphs and their origins, in the coordinates of the drawing.</param>
    /// <param name="options">The synthesis, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> or <paramref name="font"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The size or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A glyph is not a glyph of the font, the size is negative, or the magnitude of a coordinate exceeds 16777216.</exception>
    /// <exception cref="InvalidImageContentException">The outline of a glyph is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void StrokeGlyphs(Pen pen, Font font, double size, ReadOnlySpan<PositionedGlyph> glyphs, GlyphRunOptions? options = null) => GetSession().StrokeGlyphs(pen, font, size, glyphs, options);

    /// <summary>Paints the glyphs of a text layout along a path.</summary>
    /// <param name="brush">The paint of the spans that have none.</param>
    /// <param name="layout">The text, laid out.</param>
    /// <param name="path">The path the text follows: the horizontal axis of the layout, with its first baseline on it. A vertical layout follows it from the top of its lines, with the central baseline of its first line on it.</param>
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
    /// <param name="path">The path the text follows: the horizontal axis of the layout, with its first baseline on it. A vertical layout follows it from the top of its lines, with the central baseline of its first line on it.</param>
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
    /// <see cref="CompositeMode.DestinationOut"/> erases it there. With a <paramref name="blendMode"/>, its colors
    /// are mixed with those of the canvas as those of a brush are (see <see cref="BlendMode"/>).
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
    /// <param name="blendMode">The blend mode that mixes the colors of the layer with those of the canvas, before the operator: a drawing that multiplies or screens what is under it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not between 0 and 1, <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>, or <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Layer(double opacity, DrawingAction action, CompositeMode compositeMode = CompositeMode.SourceOver, BlendMode blendMode = BlendMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushLayer(opacity, compositeMode, blendMode);
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
    /// <param name="blendMode">The blend mode that mixes the colors of the layer with those of the canvas, before the operator.</param>
    /// <remarks>See <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not between 0 and 1, <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>, or <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Layer<TState>(double opacity, TState state, DrawingAction<TState> action, CompositeMode compositeMode = CompositeMode.SourceOver, BlendMode blendMode = BlendMode.Normal)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushLayer(opacity, compositeMode, blendMode);
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

    /// <summary>Runs a callback whose drawings are made on a transparent layer that holds the pixels of a rectangle only, which is then drawn on the canvas as a whole.</summary>
    /// <remarks>
    /// <para>
    /// The layer is drawn as the one of <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/> is, with the
    /// same pixels when the drawings are inside the rectangle, but it holds the pixels of the box of the rectangle on the
    /// canvas that the clip can show, instead of the pixels of the whole canvas: what is drawn outside them is not kept.
    /// The memory of the layer is the one of that box, rented when the layer starts and released when it ends, which
    /// suits a small group of drawings on a large canvas (an element of a page with an opacity). Its drawings are made
    /// by a canvas of their own, whose coordinates are those of this one: the callback receives it.
    /// </para>
    /// <para>Under a projective transformation, the layer holds what the canvas can show, as any layer.</para>
    /// </remarks>
    /// <param name="bounds">The rectangle the drawings of the layer are in, in the coordinates of the drawing.</param>
    /// <param name="opacity">The opacity of the layer, from 0 (nothing is drawn; the callback still runs) to 1.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="compositeMode">The operator that combines the layer with the canvas.</param>
    /// <param name="blendMode">The blend mode that mixes the colors of the layer with those of the canvas, before the operator.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="bounds"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="bounds"/> exceeds 16777216, <paramref name="opacity"/> is not between 0 and 1, <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>, or <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Layer(RectangleD bounds, double opacity, DrawingAction action, CompositeMode compositeMode = CompositeMode.SourceOver, BlendMode blendMode = BlendMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var inner = session.PushBoundedLayer(bounds, opacity, compositeMode, blendMode);
        var isDrawn = false;
        try
        {
            action(new DrawingCanvas(inner));
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are made on a transparent layer that holds the pixels of a rectangle only, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="bounds">The rectangle the drawings of the layer are in, in the coordinates of the drawing.</param>
    /// <param name="opacity">The opacity of the layer, from 0 (nothing is drawn; the callback still runs) to 1.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="compositeMode">The operator that combines the layer with the canvas.</param>
    /// <param name="blendMode">The blend mode that mixes the colors of the layer with those of the canvas, before the operator.</param>
    /// <remarks>See <see cref="Layer(RectangleD, double, DrawingAction, CompositeMode, BlendMode)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="bounds"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="bounds"/> exceeds 16777216, <paramref name="opacity"/> is not between 0 and 1, <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>, or <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Layer<TState>(RectangleD bounds, double opacity, TState state, DrawingAction<TState> action, CompositeMode compositeMode = CompositeMode.SourceOver, BlendMode blendMode = BlendMode.Normal)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var inner = session.PushBoundedLayer(bounds, opacity, compositeMode, blendMode);
        var isDrawn = false;
        try
        {
            action(new DrawingCanvas(inner), state);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are transformed: moved, turned, scaled, skewed or mirrored.</summary>
    /// <remarks>
    /// <para>
    /// Everything the callback draws is drawn as it would be without the transformation, then transformed: shapes,
    /// strokes (the pen is transformed with the path it strokes: a scaling makes it wider, and stretches its dashes,
    /// unless <see cref="Pen.ScalesWithTransform"/> is off: the pen then strokes the transformed path, as wide as it is),
    /// the gradients and the images of the brushes, images, texts with the paints of their spans and the glyphs a font
    /// draws with its own colors, the paths of the clips, and the offset and the blur of a shadow. This is what
    /// <see cref="VectorPath.Transform"/> does to a path, for any drawing: a text along a diagonal, or a drawing made
    /// for one size drawn at another.
    /// </para>
    /// <para>
    /// A transformation started inside another one applies first, then the one around it. <see cref="Fill(Brush)"/>
    /// and <see cref="Clear"/> still cover the whole canvas (the brush is transformed), and <see cref="Width"/> and
    /// <see cref="Height"/> stay those of the canvas. Curves are drawn at the size the transformation gives them: they
    /// stay smooth however they are enlarged. A transformation that flattens the plane to a line draws nothing.
    /// </para>
    /// <para>
    /// The coordinates given to a draw call are validated as they are without transformation, and a coordinate that
    /// the transformation moves beyond 16777216 is rejected, before the call changes a pixel. A dashed stroke is cut
    /// into the dashes the canvas can show, as without transformation.
    /// </para>
    /// <para>
    /// <see cref="Transform(ProjectiveTransform, DrawingAction)"/> and <see cref="Transform(Transform3D, DrawingAction)"/>
    /// draw in perspective.
    /// </para>
    /// </remarks>
    /// <param name="transform">The transformation.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coefficient of the transformation, combined with the one it starts in, is not a finite number.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Transform(AffineTransform transform, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushTransform(transform);
        try
        {
            action(this);
        }
        finally
        {
            session.PopTransform();
        }
    }

    /// <summary>Runs a callback whose drawings are transformed, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="transform">The transformation.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Transform(AffineTransform, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coefficient of the transformation, combined with the one it starts in, is not a finite number.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Transform<TState>(AffineTransform transform, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushTransform(transform);
        try
        {
            action(this, state);
        }
        finally
        {
            session.PopTransform();
        }
    }

    /// <summary>Runs a callback whose drawings are transformed by a projective transformation: drawn on a plane seen in perspective.</summary>
    /// <remarks>
    /// <para>
    /// Everything <see cref="Transform(AffineTransform, DrawingAction)"/> transforms is transformed: shapes, strokes,
    /// the paints of the brushes, images, texts, clips and layers. A straight line stays straight, and what is further
    /// away is smaller. A point whose <c>W</c> is not positive (on the other side of the horizon of the plane, behind
    /// the viewer) has no image: a drawing that crosses the horizon is cut there, and the paint of a brush has no
    /// color beyond it. Nothing is rejected because of the transformation: what it sends beyond 16777216 is cut off.
    /// </para>
    /// <para>
    /// Curves are cut into chords that stay within 1/16 of a pixel of them on the canvas, wherever the transformation
    /// enlarges a length by less than 65536. What has a size in the drawing (the blur and the offset of a shadow, a
    /// blur, the glyphs a font draws with its own colors) is drawn on an image of the plane of the drawing, then that
    /// image is drawn in perspective; that image has the resolution the transformation has where <c>W</c> is the one of
    /// the origin, and at most 4,194,304 pixels. A pattern is drawn at that resolution too.
    /// </para>
    /// <para>
    /// An affine transformation (whose third column is <c>(0, 0, 1)</c>) draws what
    /// <see cref="Transform(AffineTransform, DrawingAction)"/> draws, bit for bit. Inside a transformation of space
    /// (<see cref="Transform(Transform3D, DrawingAction)"/>), the transformation is combined as the transformation of
    /// space it is.
    /// </para>
    /// </remarks>
    /// <param name="transform">The transformation.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coefficient of the transformation, combined with the one it starts in, is not a finite number.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Transform(ProjectiveTransform transform, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushTransform(transform);
        try
        {
            action(this);
        }
        finally
        {
            session.PopTransform();
        }
    }

    /// <summary>Runs a callback whose drawings are transformed by a projective transformation, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="transform">The transformation.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Transform(ProjectiveTransform, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coefficient of the transformation, combined with the one it starts in, is not a finite number.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Transform<TState>(ProjectiveTransform transform, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushTransform(transform);
        try
        {
            action(this, state);
        }
        finally
        {
            session.PopTransform();
        }
    }

    /// <summary>Runs a callback whose drawings are the plane <c>z = 0</c> transformed by a transformation of space: turned in three dimensions and seen in perspective, as by the 3D transforms of CSS.</summary>
    /// <remarks>
    /// <para>
    /// The drawings are transformed by what the transformation does to the plane <c>z = 0</c>
    /// (<see cref="Transform3D.ToProjectiveTransform"/>), as <see cref="Transform(ProjectiveTransform, DrawingAction)"/>
    /// transforms them. A transformation started inside this one is combined with it in three dimensions
    /// (<see cref="Transform3D.Then"/>), whatever its kind: what an inner rotation does to z reaches an outer
    /// perspective, as the transforms of nested elements of CSS do.
    /// </para>
    /// </remarks>
    /// <param name="transform">The transformation of space.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coefficient of the transformation, combined with the one it starts in, is not a finite number.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Transform(Transform3D transform, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushTransform(transform);
        try
        {
            action(this);
        }
        finally
        {
            session.PopTransform();
        }
    }

    /// <summary>Runs a callback whose drawings are transformed by a transformation of space, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="transform">The transformation of space.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Transform(Transform3D, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coefficient of the transformation, combined with the one it starts in, is not a finite number.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Transform<TState>(Transform3D transform, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushTransform(transform);
        try
        {
            action(this, state);
        }
        finally
        {
            session.PopTransform();
        }
    }

    /// <summary>Runs a callback whose drawings are drawn with their shadow: a drop shadow, or a glow without offset.</summary>
    /// <remarks>
    /// <para>
    /// The drawings are made on a layer (see <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>).
    /// When the callback returns, the shadow of the layer is painted on the canvas, then the layer over it. The
    /// shadow has the shape of everything the callback drew, as a whole: its opacity is the alpha of the layer,
    /// blurred by a Gaussian blur of standard deviation <paramref name="blur"/> (what CSS calls a blur radius is twice
    /// that), moved by <paramref name="offset"/> and multiplied by the alpha of <paramref name="color"/>.
    /// </para>
    /// <para>
    /// A drawing that goes beyond an edge of the canvas is taken to continue as it does on that edge, so that its
    /// shadow does not fade there. A clip that is active when the callback starts applies to the drawings and to
    /// their shadow; what is drawn around it still casts its shadow in it. The time and the storage grow with the
    /// pixels the clip can show and with the blur: start the shadow of a small drawing inside
    /// <see cref="Clip(RectangleD, DrawingAction)"/>, with room for the shadow.
    /// </para>
    /// </remarks>
    /// <param name="color">The color of the shadow, with straight alpha: black at a third of its opacity is a soft shadow. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="offset">The distance from the drawings to their shadow: to the right and downward when positive.</param>
    /// <param name="blur">The standard deviation of the blur of the shadow, in pixels: 0 for a shadow with the edges of the drawings. Must be between 0 and 10000.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="offset"/>, or <paramref name="blur"/>, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="offset"/> exceeds 16777216, or <paramref name="blur"/> is negative or exceeds 10000.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Shadow(Rgba64 color, PointD offset, double blur, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushShadow(color, offset, blur));
        var isDrawn = false;
        try
        {
            action(target);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are drawn with their shadow, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="color">The color of the shadow, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="offset">The distance from the drawings to their shadow: to the right and downward when positive.</param>
    /// <param name="blur">The standard deviation of the blur of the shadow, in pixels. Must be between 0 and 10000.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Shadow(Rgba64, PointD, double, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="offset"/>, or <paramref name="blur"/>, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="offset"/> exceeds 16777216, or <paramref name="blur"/> is negative or exceeds 10000.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Shadow<TState>(Rgba64 color, PointD offset, double blur, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushShadow(color, offset, blur));
        var isDrawn = false;
        try
        {
            action(target, state);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are kept only where a mask is: the drawings of another callback, by their opacity or by their luminance.</summary>
    /// <remarks>
    /// <para>
    /// What <paramref name="content"/> draws is drawn on a layer (see <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>),
    /// and what <paramref name="mask"/> draws on a layer of its own. Each pixel of the content is then kept in
    /// proportion to the mask there, and the result is drawn on the canvas. With <see cref="MaskMode.Alpha"/>, the
    /// mask is the alpha of its drawings: a shape, a text or a gradient to a transparent color, whatever their
    /// colors. With <see cref="MaskMode.Luminance"/>, as in the masks of SVG, it is the luminance of their colors
    /// multiplied by their alpha: white keeps, black removes, and a gradient from white to black fades.
    /// </para>
    /// <para>
    /// The luminance of a pixel of the mask is its Rec. 709 luma on the stored sample values, the gray level
    /// <c>Grayscale</c> gives it. The content is drawn first, then the mask; both callbacks run once. When a callback
    /// fails, nothing is drawn. A clip that is active when the mask starts applies to the result.
    /// </para>
    /// </remarks>
    /// <param name="mask">The callback that draws the mask. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="content">The callback that draws what is masked. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="mode">What the mask keeps the content by: its alpha, or its luminance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mask"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined <see cref="MaskMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layers or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Mask(DrawingAction mask, DrawingAction content, MaskMode mode = MaskMode.Alpha)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(content);
        DrawingSession.ValidateMaskMode(mode, nameof(mode));
        var session = GetSession();
        session.PushLayer(1, CompositeMode.SourceOver, BlendMode.Normal);
        var isDrawn = false;
        try
        {
            content(this);
            session.PushMask(mode);
            var isMasked = false;
            try
            {
                mask(this);
                isMasked = true;
            }
            finally
            {
                session.PopLayer(isMasked);
            }

            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are kept only where a mask is, passing caller state to both callbacks to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">The state passed to <paramref name="mask"/> and to <paramref name="content"/>.</param>
    /// <param name="mask">The callback that draws the mask. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="content">The callback that draws what is masked. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="mode">What the mask keeps the content by: its alpha, or its luminance.</param>
    /// <remarks>See <see cref="Mask(DrawingAction, DrawingAction, MaskMode)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="mask"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined <see cref="MaskMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layers or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Mask<TState>(TState state, DrawingAction<TState> mask, DrawingAction<TState> content, MaskMode mode = MaskMode.Alpha)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(content);
        DrawingSession.ValidateMaskMode(mode, nameof(mode));
        var session = GetSession();
        session.PushLayer(1, CompositeMode.SourceOver, BlendMode.Normal);
        var isDrawn = false;
        try
        {
            content(this, state);
            session.PushMask(mode);
            var isMasked = false;
            try
            {
                mask(this, state);
                isMasked = true;
            }
            finally
            {
                session.PopLayer(isMasked);
            }

            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are blurred, as a whole, before they are drawn on the canvas: a soft light, something out of focus.</summary>
    /// <remarks>
    /// <para>
    /// The drawings are made on a layer (see <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>).
    /// When the callback returns, the layer is blurred by a Gaussian blur of standard deviation
    /// <paramref name="blur"/>, colors and alpha together (the colors are weighted by their alpha, so a transparent
    /// pixel does not darken its neighbors), then drawn on the canvas.
    /// </para>
    /// <para>
    /// A drawing that goes beyond an edge of the canvas is taken to continue as it does on that edge. A clip that is
    /// active when the callback starts applies to the result; what is drawn around it still spreads into it. The time
    /// and the storage grow with the pixels the clip can show and with the blur. Under a transformation of the canvas,
    /// the blur is enlarged with the drawing.
    /// </para>
    /// </remarks>
    /// <param name="blur">The standard deviation of the blur, in pixels: 0 for no blur. Must be between 0 and 10000.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="blur"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blur"/> is negative or exceeds 10000.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Blur(double blur, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushBlur(blur));
        var isDrawn = false;
        try
        {
            action(target);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are blurred before they are drawn on the canvas, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="blur">The standard deviation of the blur, in pixels. Must be between 0 and 10000.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Blur(double, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="blur"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blur"/> is negative or exceeds 10000.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Blur<TState>(double blur, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushBlur(blur));
        var isDrawn = false;
        try
        {
            action(target, state);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Blurs what is already drawn, where the clip lets a drawing through: the frosted glass behind a panel.</summary>
    /// <remarks>
    /// <para>
    /// The pixels of the canvas (of the layer, inside <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>)
    /// are replaced by their Gaussian blur of standard deviation <paramref name="blur"/>, colors and alpha together.
    /// Without a clip, the whole canvas is blurred; inside <see cref="Clip(VectorPath, DrawingAction, FillRule)"/>,
    /// only the inside of the clip is, and a pixel on its edge in proportion to the area of it that is inside. What
    /// is around the clip is read by the blur, but not changed.
    /// </para>
    /// <para>
    /// The pixels are read as 16-bit colors and the result is converted to the pixel format like a brush color. What
    /// is beyond an edge of the canvas is taken to continue as on that edge. This is not a drawing: the blend mode
    /// and the operator of a brush do not apply, and neither does the opacity of a layer the canvas is not inside.
    /// The time grows with the pixels that are blurred and with the blur.
    /// </para>
    /// </remarks>
    /// <param name="blur">The standard deviation of the blur, in pixels: 0 changes nothing. Must be between 0 and 10000.</param>
    /// <exception cref="ArgumentException"><paramref name="blur"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blur"/> is negative or exceeds 10000.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void BlurBackdrop(double blur) => GetSession().BlurBackdrop(blur);

    /// <summary>Runs a callback whose drawings go through a filter, as a whole, before they are drawn on the canvas: the <c>filter</c> property of CSS.</summary>
    /// <remarks>
    /// <para>
    /// The drawings are made on a layer (see <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>) that
    /// holds the pixels the clip can show and, around them, as far as the filter moves what it filters (the reach of a
    /// blur, the offset and the blur of a shadow). When the callback returns, the layer goes through the filter (see
    /// <see cref="Drawing.Filter"/> for the arithmetic), then it is drawn on the canvas. What a filter reads beyond the
    /// edges of the canvas is given by its edge mode: transparent by default.
    /// </para>
    /// <para>
    /// The lengths of the filter are lengths of the drawing: under a transformation that scales the axes, they are
    /// scaled with them; under one that turns, skews or puts the drawing in perspective, the drawings and the filter are
    /// made on an image of the plane of the drawing, which is then drawn transformed (see
    /// <see cref="Transform(ProjectiveTransform, DrawingAction)"/>).
    /// </para>
    /// </remarks>
    /// <param name="filter">The filter.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Filter(Filter filter, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushFilter(filter));
        var isDrawn = false;
        try
        {
            action(target);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings go through a filter before they are drawn, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="filter">The filter.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Filter(Drawing.Filter, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Filter<TState>(Filter filter, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushFilter(filter));
        var isDrawn = false;
        try
        {
            action(target, state);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>
    /// Runs a callback whose drawings are made on a layer that starts with what is already drawn behind a rectangle,
    /// filtered: the <c>backdrop-filter</c> property of CSS (frosted glass, a dimmed or grayed backdrop, the shadow of
    /// what is behind).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The backdrop is the pixels already drawn (on the canvas, or on the layer this one starts in) whose center is in
    /// the rectangle on the canvas (in the box of its image under a transformation of the canvas). It goes through the
    /// filter as an image of its own, whose pixels are mirrored beyond its edges, whatever the edge modes of the filter:
    /// a blur of a backdrop does not fade at its edges. The result is the start of a layer (see
    /// <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>), transparent around it; the callback draws
    /// on it, then the layer is drawn on the canvas with the opacity, the operator and the blend mode. A clip that is
    /// active when the layer starts (the rounded corners of a panel) applies to the layer as a whole.
    /// </para>
    /// <para>
    /// The lengths of the filter are lengths of the drawing, which the linear part of the transformation of the canvas
    /// scales: a deviation along an axis by the length of the image of the unit vector of that axis. When an image of
    /// several frames is drawn on, each frame starts with its own backdrop.
    /// </para>
    /// </remarks>
    /// <param name="filter">The filter of the backdrop, or <see langword="null"/> to start with the backdrop as it is.</param>
    /// <param name="bounds">The rectangle of the backdrop, in the coordinates of the drawing.</param>
    /// <param name="opacity">The opacity of the layer, from 0 to 1.</param>
    /// <param name="action">The callback that draws on the backdrop. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="compositeMode">The operator that combines the layer with the canvas.</param>
    /// <param name="blendMode">The blend mode that mixes the colors of the layer with those of the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="bounds"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="bounds"/> exceeds 16777216, <paramref name="opacity"/> is not between 0 and 1, <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>, or <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void BackdropLayer(Filter? filter, RectangleD bounds, double opacity, DrawingAction action, CompositeMode compositeMode = CompositeMode.SourceOver, BlendMode blendMode = BlendMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushBackdropLayer(filter, bounds, opacity, compositeMode, blendMode);
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

    /// <summary>Runs a callback whose drawings are made on a layer that starts with the filtered backdrop of a rectangle, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="filter">The filter of the backdrop, or <see langword="null"/> to start with the backdrop as it is.</param>
    /// <param name="bounds">The rectangle of the backdrop, in the coordinates of the drawing.</param>
    /// <param name="opacity">The opacity of the layer, from 0 to 1.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws on the backdrop. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="compositeMode">The operator that combines the layer with the canvas.</param>
    /// <param name="blendMode">The blend mode that mixes the colors of the layer with those of the canvas.</param>
    /// <remarks>See <see cref="BackdropLayer(Drawing.Filter, RectangleD, double, DrawingAction, CompositeMode, BlendMode)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="bounds"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="bounds"/> exceeds 16777216, <paramref name="opacity"/> is not between 0 and 1, <paramref name="compositeMode"/> is not a defined <see cref="CompositeMode"/>, or <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void BackdropLayer<TState>(Filter? filter, RectangleD bounds, double opacity, TState state, DrawingAction<TState> action, CompositeMode compositeMode = CompositeMode.SourceOver, BlendMode blendMode = BlendMode.Normal)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushBackdropLayer(filter, bounds, opacity, compositeMode, blendMode);
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

    /// <summary>Runs a callback whose drawings get new colors, as a whole, before they are drawn on the canvas: a photograph in gray levels, an icon in another color, a drawing that is dimmed.</summary>
    /// <remarks>
    /// <para>
    /// The drawings are made on a layer (see <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>).
    /// When the callback returns, each pixel of the layer gets the color <paramref name="matrix"/> gives it from its
    /// own color and alpha, then the layer is drawn on the canvas. The matrix is applied to the stored samples of
    /// the layer, with straight alpha: 8-bit samples on a canvas of 8-bit colors, 16-bit samples otherwise (see
    /// <see cref="ColorMatrix"/> for the arithmetic).
    /// </para>
    /// <para>
    /// The matrix applies to every pixel the clip can show, also those the callback did not draw on, which are
    /// transparent black: a matrix that gives an alpha to a transparent pixel (a constant in its last row) paints
    /// them.
    /// </para>
    /// </remarks>
    /// <param name="matrix">The matrix.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Filter(ColorMatrix matrix, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushFilter(matrix);
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

    /// <summary>Runs a callback whose drawings get new colors before they are drawn on the canvas, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="matrix">The matrix.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Filter(ColorMatrix, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Filter<TState>(ColorMatrix matrix, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        session.PushFilter(matrix);
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

    /// <summary>Gives new colors to what is already drawn, where the clip lets a drawing through: the background of a dialog that is dimmed, a region that is shown in gray levels or inverted.</summary>
    /// <remarks>
    /// <para>
    /// Each pixel of the canvas (of the layer, inside <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>)
    /// is replaced by the color <paramref name="matrix"/> gives it from its own color and alpha. Without a clip, every
    /// pixel is; inside <see cref="Clip(VectorPath, DrawingAction, FillRule)"/>, only the inside of the clip is, and
    /// a pixel on its edge in proportion to the area of it that is inside.
    /// </para>
    /// <para>
    /// The matrix is applied to the stored samples of the pixels, at their precision (see <see cref="ColorMatrix"/>
    /// for the arithmetic and for the pixel formats without alpha or without colors). A pixel on the edge of a clip
    /// is replaced as a paint drawn with <see cref="CompositeMode.Source"/> replaces it. This is not a drawing: the
    /// blend mode and the operator of a brush do not apply, and neither does the opacity of a layer the canvas is not
    /// inside.
    /// </para>
    /// </remarks>
    /// <param name="matrix">The matrix.</param>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. No pixel is changed by this call.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void FilterBackdrop(ColorMatrix matrix) => GetSession().FilterBackdrop(matrix);

    /// <summary>Runs a callback whose drawings are drawn with a shadow inside them: the shadow cast on them by what is around them, for a shape that looks cut out or pressed in.</summary>
    /// <remarks>
    /// <para>
    /// The drawings are made on a layer (see <see cref="Layer(double, DrawingAction, CompositeMode, BlendMode)"/>).
    /// When the callback returns, the shadow is painted on the layer, where the drawings are, then the layer is drawn
    /// on the canvas. The shadow has the shape of everything the callback did not draw: its opacity is what is
    /// missing for the layer to be opaque, blurred by a Gaussian blur of standard deviation <paramref name="blur"/>,
    /// moved by <paramref name="offset"/> and multiplied by the alpha of <paramref name="color"/>. It does not change
    /// the alpha of the drawings: nothing is painted outside them. A translucent drawing lacks opacity everywhere,
    /// and is shadowed all over: draw it opaque, in a layer that fades it, for a shadow along its edges only.
    /// </para>
    /// <para>
    /// A drawing that goes beyond an edge of the canvas is taken to continue as it does on that edge. The time and the
    /// storage grow with the pixels the clip can show and with the blur, as for <see cref="Shadow(Rgba64, PointD, double, DrawingAction)"/>.
    /// </para>
    /// </remarks>
    /// <param name="color">The color of the shadow, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="offset">The distance the shadow is moved by: to the right and downward when positive, for a light that comes from the top left.</param>
    /// <param name="blur">The standard deviation of the blur of the shadow, in pixels. Must be between 0 and 10000.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="offset"/>, or <paramref name="blur"/>, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="offset"/> exceeds 16777216, or <paramref name="blur"/> is negative or exceeds 10000.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void InnerShadow(Rgba64 color, PointD offset, double blur, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushShadow(color, offset, blur, isInner: true));
        var isDrawn = false;
        try
        {
            action(target);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are drawn with a shadow inside them, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="color">The color of the shadow, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="offset">The distance the shadow is moved by: to the right and downward when positive.</param>
    /// <param name="blur">The standard deviation of the blur of the shadow, in pixels. Must be between 0 and 10000.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="InnerShadow(Rgba64, PointD, double, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="offset"/>, or <paramref name="blur"/>, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="offset"/> exceeds 16777216, or <paramref name="blur"/> is negative or exceeds 10000.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The layer or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void InnerShadow<TState>(Rgba64 color, PointD offset, double blur, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var target = new DrawingCanvas(session.PushShadow(color, offset, blur, isInner: true));
        var isDrawn = false;
        try
        {
            action(target, state);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings have hard edges, or antialiased ones: pixel art, a mask of whole pixels, an image whose colors must stay those of a palette.</summary>
    /// <remarks>
    /// <para>
    /// Without antialiasing, a pixel is painted entirely or not at all, as browsers draw hard edges: it is painted when
    /// its center is inside the shape, a center on the top or the left side of the shape being outside and one on its
    /// bottom or its right side inside. An opaque paint then writes its color and nothing else: no pixel gets a color
    /// between the paint and what was under it. This applies to the shapes, the strokes, the texts and the edges of the
    /// images that are drawn while the callback runs, and to the clips that start in it.
    /// </para>
    /// <para>
    /// What is not an edge is unchanged: the opacity of a paint, of an image or of a layer, the blur of a shadow, the
    /// pixels inside an image, and the glyphs a font draws with its own colors. A thin line is drawn where it covers the
    /// centers of pixels: a horizontal line one pixel wide between two rows of centers covers one of them.
    /// </para>
    /// </remarks>
    /// <param name="enabled"><see langword="false"/> for hard edges; <see langword="true"/> (what a canvas starts with) for antialiased ones, inside a callback that turned them off.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Antialiasing(bool enabled, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var previous = session.SetAntialiasing(enabled);
        try
        {
            action(this);
        }
        finally
        {
            session.RestoreAntialiasing(previous);
        }
    }

    /// <summary>Runs a callback whose drawings have hard edges, or antialiased ones, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="enabled"><see langword="false"/> for hard edges; <see langword="true"/> for antialiased ones.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Antialiasing(bool, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Antialiasing<TState>(bool enabled, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var previous = session.SetAntialiasing(enabled);
        try
        {
            action(this, state);
        }
        finally
        {
            session.RestoreAntialiasing(previous);
        }
    }

    /// <summary>Runs a callback whose paints are combined with the pixels in a numeric space: the stored sample values, or linear light.</summary>
    /// <remarks>
    /// <para>
    /// With <see cref="CompositingSpace.LinearSrgb"/>, the samples of a paint and of the pixels it is drawn on are
    /// converted to linear light with the transfer function of the image (sRGB, unless the image declares another
    /// one or has an ICC profile with tone curves of its own: see <see cref="CompositingSpace.LinearSrgb"/>),
    /// combined (coverage, opacity, blend mode and operator, with the formulas of the stored values), and the result
    /// is converted back. The edge between two colors, a translucent paint and the blur of a shadow then mix light as
    /// light mixes: red over green goes through a yellow instead of a dark olive, and white text on black keeps its
    /// weight. <see cref="Blur(double, DrawingAction)"/> and <see cref="BlurBackdrop(double)"/> blur light too. The
    /// alpha is not a color and is combined as it is.
    /// </para>
    /// <para>
    /// The colors of the brushes are still sample values in the color space of the image, unless
    /// <see cref="PaintProfile(IccProfile, DrawingAction, IccColorTransformOptions)"/> says otherwise; a gradient mixes its stops
    /// as its <see cref="GradientBrush.ColorSpace"/> says, and an image is interpolated on its stored values. Layers
    /// hold samples of the image too: a drawing made on a layer is combined in linear light on the layer, and the
    /// layer on the canvas. The arithmetic is in double precision, and uses the power function of the platform: a
    /// sample within about 1e-12 of a rounding boundary can differ by one from a platform to another. A pixel that a
    /// shape does not touch is still left untouched, and an opaque paint that covers a pixel entirely still writes its
    /// color exactly.
    /// </para>
    /// </remarks>
    /// <param name="space">The space the paints are combined with the pixels in.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is not a defined <see cref="CompositingSpace"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="CompositingSpace.LinearSrgb"/> is requested for an image whose ICC profile has no tone curve for each channel, or whose transfer characteristics are reserved. The callback is not run.</exception>
    /// <exception cref="ImageResourceLimitException">The tables of the tone curves of the ICC profile of the image would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Compositing(CompositingSpace space, DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var previous = session.SetCompositingSpace(space);
        try
        {
            action(this);
        }
        finally
        {
            session.RestoreCompositingSpace(previous);
        }
    }

    /// <summary>Runs a callback whose drawings are made with 16-bit samples, and rounded to the samples of the canvas once when it returns.</summary>
    /// <remarks>
    /// <para>
    /// On a canvas of 8-bit samples, every draw call rounds its result to 8 bits: many translucent drawings on top of
    /// each other, layers inside layers and filters accumulate those roundings. Inside the callback, the drawings are
    /// made on a copy of the pixels the clip can show with 16-bit samples and alpha, which starts with the pixels of the
    /// canvas; layers started in it have 16-bit samples too. When the callback returns, the copy replaces the pixels as
    /// <see cref="CompositeMode.Source"/> does, through the clip: each color is rounded once. The coordinates do not
    /// change. On a canvas of 16-bit samples, the callback draws on the canvas itself.
    /// </para>
    /// <para>The copy is rented when the callback starts and released when it returns: 8 bytes per pixel the clip can show, for each frame.</para>
    /// </remarks>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The copy or the working storage would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void HighPrecision(DrawingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        if (session.HasSixteenBitSamples)
        {
            action(this);
            return;
        }

        var inner = session.PushPrecisionScope();
        var isDrawn = false;
        try
        {
            action(new DrawingCanvas(inner));
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose drawings are made with 16-bit samples, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="HighPrecision(DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The copy or the working storage would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void HighPrecision<TState>(TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        if (session.HasSixteenBitSamples)
        {
            action(this, state);
            return;
        }

        var inner = session.PushPrecisionScope();
        var isDrawn = false;
        try
        {
            action(new DrawingCanvas(inner), state);
            isDrawn = true;
        }
        finally
        {
            session.PopLayer(isDrawn);
        }
    }

    /// <summary>Runs a callback whose paints are combined with the pixels in a numeric space, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="space">The space the paints are combined with the pixels in.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <remarks>See <see cref="Compositing(CompositingSpace, DrawingAction)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is not a defined <see cref="CompositingSpace"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="CompositingSpace.LinearSrgb"/> is requested for an image whose ICC profile has no tone curve for each channel, or whose transfer characteristics are reserved. The callback is not run.</exception>
    /// <exception cref="ImageResourceLimitException">The tables of the tone curves of the ICC profile of the image would exceed the allocation limit. The callback is not run.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void Compositing<TState>(CompositingSpace space, TState state, DrawingAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var previous = session.SetCompositingSpace(space);
        try
        {
            action(this, state);
        }
        finally
        {
            session.RestoreCompositingSpace(previous);
        }
    }

    /// <summary>Runs a callback whose paint colors are colors of an ICC profile: they are converted to the color space of the image before they are painted.</summary>
    /// <remarks>
    /// <para>
    /// Without this, the color of a paint is taken as sample values of the image, whatever its color space: the red
    /// (255, 0, 0) is the most saturated red of the image, which is not the same color on an sRGB image and on a
    /// Display P3 or an Adobe RGB one. Inside the callback, a color is a color of <paramref name="profile"/>
    /// (<see cref="IccProfile.Srgb"/> for the colors of CSS, of a design tool or of <see cref="Colors"/>), and the
    /// image gets the samples that show that color: those of its own ICC profile
    /// (<see cref="ImageMetadata.IccProfile"/>), else of its CICP color description
    /// (<see cref="ImageMetadata.Cicp"/>), else of sRGB, for which the colors of <see cref="IccProfile.Srgb"/> are
    /// not changed.
    /// </para>
    /// <para>
    /// The colors that are converted are those the caller gives: the color of a <see cref="SolidBrush"/>, the stops
    /// of a gradient, the color of a shadow and of <see cref="Clear(Rgba64)"/>, and the brushes of a text. A gradient
    /// that mixes its stops in linear light or in Oklab mixes the colors as they are given, then converts each color
    /// it obtains; one that mixes encoded values mixes the converted stops. A color outside the color space of the
    /// image is clipped, component by component. The alpha of a color is not converted.
    /// </para>
    /// <para>
    /// What is already samples of an image is not converted: the images that are drawn
    /// (<see cref="DrawImage(ImageFrame, PointD, double)"/>, <see cref="ImageBrush"/>: convert them with
    /// <see cref="ImageProcessingExtensions.ConvertColorProfile(Image, IccProfile, IccColorTransformOptions?, CancellationToken)"/>
    /// first), the glyphs a font draws with its own colors, and the constants of a <see cref="ColorMatrix"/>. On a
    /// grayscale image, a color becomes the gray level that has its luminance, as a conversion to a gray profile
    /// gives it, and not the luma of its samples. The
    /// conversion is the one of <see cref="IccColorTransform"/>, in double precision: it uses the functions of the
    /// platform, and a sample within about 1e-12 of a rounding boundary can differ by one from a platform to another.
    /// </para>
    /// </remarks>
    /// <param name="profile">The profile of the colors of the paints: an RGB profile.</param>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="options">The options of the conversion, or <see langword="null"/> for <see cref="IccColorTransformOptions.Default"/>. Its tone mapping is not used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed. The callback is not run.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// <paramref name="profile"/> is not an RGB profile, a profile is not supported in its role, the color space of
    /// the image is ambiguous or unknown, or its samples are high dynamic range ones (PQ, HLG), among which the light
    /// of the white of a paint is not chosen implicitly. The callback is not run.
    /// </exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void PaintProfile(IccProfile profile, DrawingAction action, IccColorTransformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var previous = session.SetPaintProfile(profile, options);
        try
        {
            action(this);
        }
        finally
        {
            session.RestorePaintProfile(previous);
        }
    }

    /// <summary>Runs a callback whose paint colors are colors of an ICC profile, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="profile">The profile of the colors of the paints: an RGB profile.</param>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="options">The options of the conversion, or <see langword="null"/> for <see cref="IccColorTransformOptions.Default"/>.</param>
    /// <remarks>See <see cref="PaintProfile(IccProfile, DrawingAction, IccColorTransformOptions)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed. The callback is not run.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A profile is not supported in its role, or the color space of the image is unknown, ambiguous or of a high dynamic range. The callback is not run.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public void PaintProfile<TState>(IccProfile profile, TState state, DrawingAction<TState> action, IccColorTransformOptions? options = null)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(action);
        var session = GetSession();
        var previous = session.SetPaintProfile(profile, options);
        try
        {
            action(this, state);
        }
        finally
        {
            session.RestorePaintProfile(previous);
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
