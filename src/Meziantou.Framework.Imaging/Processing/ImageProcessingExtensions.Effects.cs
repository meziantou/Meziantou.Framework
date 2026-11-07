using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging;

// The effects that are not color matrices: the vignette and the glow (radial gradients drawn on the image), and the
// pixel kernels of EffectKernels (pixelation, oil painting, flattening)
public static partial class ImageProcessingExtensions
{
    private const double MaxEffectRadius = 16777216;
    // The premultiplied 16-bit samples of a window of 1001 x 1001 pixels add up to less than 2^53: the sums are exact
    private const int MaxOilPaintRadius = 500;
    private const int MaxOilPaintLevels = 256;

    /// <summary>
    /// Darkens (or tints) the edges of every frame of the image (including the poster frame) toward a color, in place:
    /// the corners of an old lens. The pixel format and the alpha of the pixels are kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The effect is a radial gradient (<see cref="RadialGradientBrush"/>) centered on <paramref name="rectangle"/>,
    /// drawn on the pixels of the rectangle with <see cref="CompositeMode.SourceAtop"/>: transparent at the center, it
    /// reaches <paramref name="color"/> on the ellipse of half-axes <paramref name="radiusX"/> and
    /// <paramref name="radiusY"/>, and is <paramref name="color"/> beyond it. At a pixel whose distance to the center is
    /// the fraction <c>t</c> of the ellipse in its direction, the color is composited with <c>t</c> times its alpha.
    /// The alpha of every pixel is kept: a transparent pixel stays transparent. The arithmetic is the one of the
    /// drawings (see <see cref="DrawingCanvas"/>): exact integer compositing of the 16-bit gradient, on the encoded
    /// samples.
    /// </para>
    /// <para>
    /// With the half-diagonal of the rectangle as both radii, the corners get the whole color and the center nothing; with
    /// half its width and half its height, the ellipse touches its sides. A color that is not opaque gives a lighter
    /// vignette. On failure or cancellation, some frames or rows may already be changed.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="color">The color of the edges, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="radiusX">The horizontal half-axis of the ellipse where the color is reached, in pixels. Must be positive.</param>
    /// <param name="radiusY">The vertical half-axis of the ellipse where the color is reached, in pixels. Must be positive.</param>
    /// <param name="rectangle">The pixels the effect applies to, centered on it, or <see langword="null"/> (the default) for the whole canvas. It may extend beyond the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A radius is not positive, is not a finite number or exceeds 16777216, or <paramref name="rectangle"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Vignette(this Image image, Rgba64 color, double radiusX, double radiusY, Rectangle? rectangle = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (brush, area) = CreateRadialEffect(color, radiusX, radiusY, rectangle, image.Size, vignette: true);
        image.Draw((brush, area), static (canvas, state) => canvas.FillRectangle(state.brush, state.area), cancellationToken);
    }

    /// <summary>Darkens (or tints) the edges of one frame toward a color, in place. See <see cref="Vignette(Image, Rgba64, double, double, Rectangle?, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="color">The color of the edges, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="radiusX">The horizontal half-axis of the ellipse where the color is reached, in pixels. Must be positive.</param>
    /// <param name="radiusY">The vertical half-axis of the ellipse where the color is reached, in pixels. Must be positive.</param>
    /// <param name="rectangle">The pixels the effect applies to, centered on it, or <see langword="null"/> (the default) for the whole canvas. It may extend beyond the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A radius is not positive, is not a finite number or exceeds 16777216, or <paramref name="rectangle"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Vignette(this ImageFrame frame, Rgba64 color, double radiusX, double radiusY, Rectangle? rectangle = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var (brush, area) = CreateRadialEffect(color, radiusX, radiusY, rectangle, frame.Size, vignette: true);
        frame.Draw((brush, area), static (canvas, state) => canvas.FillRectangle(state.brush, state.area), cancellationToken);
    }

    /// <summary>
    /// Lights up (or tints) the center of every frame of the image (including the poster frame) with a color that fades
    /// out toward the edges, in place. The pixel format and the alpha of the pixels are kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The effect is the opposite of <see cref="Vignette(Image, Rgba64, double, double, Rectangle?, CancellationToken)"/>:
    /// a radial gradient centered on <paramref name="rectangle"/>, drawn on the pixels of the rectangle with
    /// <see cref="CompositeMode.SourceAtop"/>, that is <paramref name="color"/> at the center and transparent on the
    /// ellipse of half-axes <paramref name="radiusX"/> and <paramref name="radiusY"/> and beyond it. At a pixel whose
    /// distance to the center is the fraction <c>t</c> of the ellipse in its direction, the color is composited with
    /// <c>1 - t</c> times its alpha. The alpha of every pixel is kept: to light up transparent pixels too, fill a
    /// <see cref="RadialGradientBrush"/> with the canvas (<see cref="Image.Draw(DrawingAction, CancellationToken)"/>).
    /// </para>
    /// <para>
    /// The color is composited over the pixels: a light color with a partial alpha gives a soft glow. On failure or
    /// cancellation, some frames or rows may already be changed.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="color">The color at the center, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="radiusX">The horizontal half-axis of the ellipse where the glow ends, in pixels. Must be positive.</param>
    /// <param name="radiusY">The vertical half-axis of the ellipse where the glow ends, in pixels. Must be positive.</param>
    /// <param name="rectangle">The pixels the effect applies to, centered on it, or <see langword="null"/> (the default) for the whole canvas. It may extend beyond the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A radius is not positive, is not a finite number or exceeds 16777216, or <paramref name="rectangle"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Glow(this Image image, Rgba64 color, double radiusX, double radiusY, Rectangle? rectangle = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (brush, area) = CreateRadialEffect(color, radiusX, radiusY, rectangle, image.Size, vignette: false);
        image.Draw((brush, area), static (canvas, state) => canvas.FillRectangle(state.brush, state.area), cancellationToken);
    }

    /// <summary>Lights up (or tints) the center of one frame, in place. See <see cref="Glow(Image, Rgba64, double, double, Rectangle?, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="color">The color at the center, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="radiusX">The horizontal half-axis of the ellipse where the glow ends, in pixels. Must be positive.</param>
    /// <param name="radiusY">The vertical half-axis of the ellipse where the glow ends, in pixels. Must be positive.</param>
    /// <param name="rectangle">The pixels the effect applies to, centered on it, or <see langword="null"/> (the default) for the whole canvas. It may extend beyond the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A radius is not positive, is not a finite number or exceeds 16777216, or <paramref name="rectangle"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Glow(this ImageFrame frame, Rgba64 color, double radiusX, double radiusY, Rectangle? rectangle = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var (brush, area) = CreateRadialEffect(color, radiusX, radiusY, rectangle, frame.Size, vignette: false);
        frame.Draw((brush, area), static (canvas, state) => canvas.FillRectangle(state.brush, state.area), cancellationToken);
    }

    /// <summary>
    /// Replaces every block of pixels of every frame of the image (including the poster frame) by its mean color, in
    /// place: a mosaic of large pixels. The pixel format is kept. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blocks are the squares of <paramref name="size"/> by <paramref name="size"/> pixels from the top-left corner;
    /// those of the right and bottom edges have the pixels that remain. Every pixel of a block gets the mean of its
    /// pixels, alpha-aware: the colors are averaged premultiplied by their alpha, so that the hidden color of a
    /// transparent pixel takes no part, and the alpha is the mean alpha. The means are computed on the stored sample
    /// values in double precision and rounded once, as the blurs do (a block whose alpha rounds to 0 becomes transparent
    /// black); floating-point samples are not clamped.
    /// </para>
    /// <para>
    /// The working storage (two rows) is charged to the image and rented before a pixel changes. With a size of 1,
    /// nothing changes. On cancellation, some frames or rows may already be changed.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="size">The side of a block, in pixels. Must be positive.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive.</exception>
    /// <exception cref="NotSupportedException">The pixel format is not supported. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Pixelate(this Image image, int size, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidatePixelateSize(size);
        if (size == 1)
        {
            image.Owner.EnsureCanModify("pixelate the image");
            return;
        }

        image.PixelateFrames(frames: null, size, cancellationToken);
    }

    /// <summary>
    /// Replaces every block of pixels of one frame by its mean color, in place. The EXIF thumbnail of the image, now stale,
    /// is removed. See <see cref="Pixelate(Image, int, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="size">The side of a block, in pixels. Must be positive.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive.</exception>
    /// <exception cref="NotSupportedException">The pixel format is not supported. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Pixelate(this ImageFrame frame, int size, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidatePixelateSize(size);
        _ = frame.GetStorage();
        if (size == 1)
            return;

        frame.OwnerImage.PixelateFrames([frame], size, cancellationToken);
    }

    /// <summary>
    /// Gives every frame of the image (including the poster frame) the look of an oil painting, in place: flat strokes of
    /// color that keep the edges. The pixel format is kept. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The intensity of every pixel, the Rec. 709 luma <c>0.2126 R + 0.7152 G + 0.0722 B</c> of its color over black
    /// (premultiplied by its alpha) as a fraction of the largest sample, is cut into <paramref name="levels"/> equal
    /// levels: <c>floor(intensity levels)</c>, at most <c>levels - 1</c> (a gray level is its own intensity). Each pixel
    /// then gets the mean of the pixels of the square of side <c>2 radius + 1</c> around it, inside the image, that have
    /// the most frequent level of that square (the lowest level among the most frequent ones). The mean is alpha-aware:
    /// premultiplied colors and alpha, computed on the stored sample values in double precision and rounded once, as
    /// the blurs do; floating-point samples are not clamped. Fewer levels give flatter areas, a larger radius larger
    /// strokes.
    /// </para>
    /// <para>
    /// The working storage (a window of rows) is charged to the image and rented before a pixel changes. The window moves
    /// by one column at a time for the integer formats, whose sums are exact, so the time per pixel grows with the
    /// radius; for <see cref="RgbaFloat"/> and <see cref="RgbaHalf"/>, the sums of every window are made again, and it
    /// grows with its square. With a radius of 0, nothing changes. On cancellation, some frames or rows may already be
    /// changed.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="levels">The number of intensity levels, between 1 and 256: 20 is a good start.</param>
    /// <param name="radius">The number of pixels of the brush on each side of a pixel, between 0 and 500.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="levels"/> is not between 1 and 256, or <paramref name="radius"/> is not between 0 and 500.</exception>
    /// <exception cref="NotSupportedException">The pixel format is not supported. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void OilPaint(this Image image, int levels, int radius, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateOilPaint(levels, radius);
        if (radius == 0)
        {
            image.Owner.EnsureCanModify("apply the oil painting effect");
            return;
        }

        image.OilPaintFrames(frames: null, levels, radius, cancellationToken);
    }

    /// <summary>
    /// Gives one frame the look of an oil painting, in place. The EXIF thumbnail of the image, now stale, is removed. See
    /// <see cref="OilPaint(Image, int, int, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="levels">The number of intensity levels, between 1 and 256.</param>
    /// <param name="radius">The number of pixels of the brush on each side of a pixel, between 0 and 500.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="levels"/> is not between 1 and 256, or <paramref name="radius"/> is not between 0 and 500.</exception>
    /// <exception cref="NotSupportedException">The pixel format is not supported. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void OilPaint(this ImageFrame frame, int levels, int radius, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateOilPaint(levels, radius);
        _ = frame.GetStorage();
        if (radius == 0)
            return;

        frame.OwnerImage.OilPaintFrames([frame], levels, radius, cancellationToken);
    }

    /// <summary>
    /// Composites every pixel of every frame of the image (including the poster frame) over an opaque color and makes it
    /// opaque, in place, without changing the pixel format: what a conversion to a format without alpha does with
    /// <see cref="PixelConversionOptions.BackgroundColor"/>, but the alpha stays in the pixels, at its maximum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The formulas are those of the conversions (<see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>), on the
    /// encoded samples: <c>(s a + bg (max - a) + max / 2) / max</c> for the integer formats, with the background reduced to
    /// their precision first, so that a flattened <see cref="Rgba32"/> image converted to <see cref="Rgb24"/> is the
    /// <see cref="Rgb24"/> conversion of the image with that background, bit for bit; <c>c a + bg (1 - a)</c> in double
    /// precision for the floating-point formats, with the alpha kept between 0 and 1 and the colors not clamped. An opaque
    /// pixel is not changed. A pixel format without alpha is already opaque: nothing changes. A gray format with alpha
    /// (<see cref="La16"/>, <see cref="La32"/>) gets the luma of the flattened colors, as its conversion to
    /// <see cref="Gray8"/> or <see cref="Gray16"/> does. An alpha mask (<see cref="A8"/>) has no color to hold the
    /// result: it is rejected.
    /// </para>
    /// <para>
    /// No color profile is applied: the color is in the color space of the image. On failure or cancellation, some frames
    /// or rows may already be changed.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="backgroundColor">The opaque color under the pixels. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="backgroundColor"/> is not fully opaque.</exception>
    /// <exception cref="NotSupportedException">The pixel format is not supported (<see cref="A8"/>). Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Flatten(this Image image, Rgba64 backgroundColor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateFlattenBackground(backgroundColor);
        image.FlattenFrames(frames: null, backgroundColor, cancellationToken);
    }

    /// <summary>
    /// Composites every pixel of one frame over an opaque color and makes it opaque, in place, without changing the pixel
    /// format. See <see cref="Flatten(Image, Rgba64, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="backgroundColor">The opaque color under the pixels. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="backgroundColor"/> is not fully opaque.</exception>
    /// <exception cref="NotSupportedException">The pixel format is not supported (<see cref="A8"/>). Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Flatten(this ImageFrame frame, Rgba64 backgroundColor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateFlattenBackground(backgroundColor);
        _ = frame.GetStorage();
        frame.OwnerImage.FlattenFrames([frame], backgroundColor, cancellationToken);
    }

    /// <summary>The brush and the rectangle of a vignette or a glow: an elliptic gradient centered on the rectangle, composited on the pixels only.</summary>
    private static (Brush Brush, RectangleD Area) CreateRadialEffect(Rgba64 color, double radiusX, double radiusY, Rectangle? rectangle, Size canvas, bool vignette)
    {
        ValidateEffectRadius(radiusX, nameof(radiusX));
        ValidateEffectRadius(radiusY, nameof(radiusY));
        var area = rectangle ?? new Rectangle(0, 0, canvas.Width, canvas.Height);
        if (area.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(rectangle), area, "The rectangle must not be empty.");

        var transparent = new Rgba64(color.R, color.G, color.B, 0);
        var center = new PointD(area.X + (area.Width / 2.0), area.Y + (area.Height / 2.0));
        ReadOnlySpan<GradientStop> stops = vignette ? [new(0, transparent), new(1, color)] : [new(0, color), new(1, transparent)];
        var brush = new RadialGradientBrush(center, radiusX, radiusY, stops) { CompositeMode = CompositeMode.SourceAtop };
        return (brush, area);
    }

    // Written so that a value that is not a number is rejected too
    private static void ValidateEffectRadius(double radius, string paramName)
    {
        if (!(radius > 0 && radius <= MaxEffectRadius))
            throw new ArgumentOutOfRangeException(paramName, radius, "The radius must be positive and at most 16777216.");
    }

    private static void ValidatePixelateSize(int size)
    {
        if (size < 1)
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size of a block must be positive.");
    }

    private static void ValidateOilPaint(int levels, int radius)
    {
        if (levels is < 1 or > MaxOilPaintLevels)
            throw new ArgumentOutOfRangeException(nameof(levels), levels, string.Create(CultureInfo.InvariantCulture, $"The number of levels must be between 1 and {MaxOilPaintLevels}."));

        ValidateRadius(radius, MaxOilPaintRadius);
    }

    private static void ValidateFlattenBackground(Rgba64 backgroundColor)
    {
        if (backgroundColor.A != ushort.MaxValue)
            throw new ArgumentException("The background color must be fully opaque.", nameof(backgroundColor));
    }
}
