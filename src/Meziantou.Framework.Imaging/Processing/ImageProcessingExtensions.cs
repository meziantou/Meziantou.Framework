using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>In-place processing operations on images and frames.</summary>
/// <remarks>
/// <para>
/// Image-level operations apply to every displayed frame and to the poster frame. Geometry-changing operations
/// (<see cref="Crop"/>, <see cref="AutoCrop(Image, AutoCropOptions?, CancellationToken)"/>, <see cref="Resize"/>,
/// <see cref="Pad"/>, <see cref="ExtendCanvas"/>,
/// <see cref="Rotate(Image, RotateMode, CancellationToken)"/>, <see cref="Rotate(Image, double, TransformOptions?, CancellationToken)"/>,
/// <see cref="Skew"/>, <see cref="Transform(Image, ProjectiveTransform, TransformOptions?, CancellationToken)"/>,
/// <see cref="AutoOrient"/>) build all replacement buffers and metadata before committing them
/// together: on failure or cancellation before the commit, the image is left exactly as it was (dimensions, pixels, frame
/// identities and order, metadata). The old and new buffers are budgeted together. After a successful commit, existing
/// <see cref="ImageFrame"/> references designate the same logical frames.
/// <see cref="ConvertColorProfile"/> is transactional in the same way, so that pixels and their color profile always
/// change together.
/// </para>
/// <para>
/// Pixel-only operations (<see cref="Flip(Image, FlipMode, CancellationToken)"/>, <see cref="Grayscale(Image, CancellationToken)"/>,
/// <see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/>, the blurs, <see cref="Sharpen(Image, double, double, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>,
/// <see cref="Equalize(Image, HistogramEqualizationOptions?, CancellationToken)"/>, <see cref="AutoLevels(Image, AutoLevelsOptions?, CancellationToken)"/>)
/// work in place; on failure or cancellation some frames or rows may already be modified, but the image remains
/// structurally valid.
/// </para>
/// <para>
/// Operations work in stored-pixel coordinates. Except for <see cref="AutoOrient"/>, they keep
/// <see cref="ImageMetadata.Orientation"/> unchanged (the typed orientation stays authoritative when EXIF is serialized).
/// Geometry changes update the existing EXIF pixel-dimension tags of <see cref="ImageMetadata.ExifProfile"/> to the new
/// canvas, and every operation that changes pixels removes the EXIF thumbnail, which no longer matches them. A malformed
/// EXIF profile is left as is (serializing it fails).
/// </para>
/// <para>
/// A cursor hotspot (<see cref="FrameMetadata.Hotspot"/>) follows the pixel it designates: crops, auto-crops and paddings translate it, rotations,
/// mirrors and auto-orient permute it exactly, and a resize moves it to <c>floor(x * newWidth / width)</c> (and likewise
/// vertically, through the kept region for <see cref="ResizeMode.Cover"/>, and then by the padding for
/// <see cref="ResizeMode.Pad"/> and <see cref="ResizeMode.BoxPad"/>), so a hotspot in the top-left corner of an image that
/// is not padded stays there; an arbitrary transformation moves it to the pixel that contains the image of its pixel center. When the pixel
/// it designates is not part of the result (a crop, an auto-crop, a cover resize or a transformation that removes it), the operation
/// throws an <see cref="UnsupportedImageFeatureException"/> and leaves the image unchanged: clear or move the hotspot
/// first. Pixel-only operations keep it.
/// </para>
/// <para>
/// Crop, auto-crop, rotations, mirrors and auto-orient are exact pixel permutations: every sample, including 16-bit low
/// bits, floating-point samples of any value (not-a-number payloads included) and alpha, is copied unchanged (an
/// auto-crop that enlarges the canvas also writes the detected background color around the copied pixels, as
/// <see cref="Pad"/> and <see cref="ExtendCanvas"/> write their padding color). Operations run on the calling thread, except that <see cref="Resize"/>,
/// the transformations, <see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/>, the blurs and the sharpening
/// may also use up to <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers for large frames (identical results).
/// </para>
/// </remarks>
public static class ImageProcessingExtensions
{
    private const int MaxBoxBlurRadius = 10000;
    private const int MaxMedianRadius = 1000;

    /// <summary>Crops every frame of the image to a rectangle.</summary>
    /// <param name="image">The image to modify.</param>
    /// <param name="rectangle">The region to keep. It must be non-empty and entirely inside the canvas; it is never clamped.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rectangle"/> is empty or not entirely inside the canvas.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The cursor hotspot of a frame is outside <paramref name="rectangle"/>. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Crop(this Image image, Rectangle rectangle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (rectangle.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(rectangle), rectangle, "The crop rectangle must not be empty.");

        var size = image.Size;
        if (rectangle.X < 0 || rectangle.Y < 0 || rectangle.Right > size.Width || rectangle.Bottom > size.Height)
            throw new ArgumentOutOfRangeException(nameof(rectangle), rectangle, string.Create(CultureInfo.InvariantCulture, $"The crop rectangle must be entirely inside the {size.Width}x{size.Height} canvas; it is never clamped."));

        if (rectangle.Width == size.Width && rectangle.Height == size.Height)
        {
            // The whole canvas: nothing changes, but the state is validated as for a real crop
            image.Owner.EnsureCanModify("crop the image");
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        image.TransformGeometry(rectangle, OrientationTransform.Identity, normalizeOrientation: false, "crop the image", cancellationToken);
    }

    /// <summary>Detects the background and the bounding box of the content of the image, without changing it.</summary>
    /// <remarks>
    /// <para>
    /// The analysis reads every displayed frame and the poster frame, in stored-pixel coordinates (call
    /// <see cref="AutoOrient"/> first to work on the displayed orientation), at the storage precision; thresholds are on
    /// the 8-bit scale and are multiplied by 257 for 16-bit samples. A fully transparent pixel is read as transparent
    /// black: hidden colors never count.
    /// </para>
    /// <para>
    /// Floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) are analyzed as they are stored, in
    /// double precision and without clipping, so values above 1 and below 0 count as they are: the reduction to 8 bits of
    /// the first step is the nearest point of the 8-bit grid, <c>floor(255 v + 1/2)</c>, which can be outside [0, 255];
    /// the luma bucket is that of the luma of that point divided by 255, and is 0 below 0 and 10 above 1; and the
    /// thresholds are divided by 255 (the differences are those of the samples). A sample that is not a number or is
    /// infinite has no distance to the background: the analysis rejects it. The background of the analysis is the
    /// border pixel as stored (<see cref="AutoCropAnalysis{TPixel}.BackgroundColor"/>).
    /// </para>
    /// <list type="number">
    /// <item>The pixels of the one-pixel outer border of every frame are tallied by color, after reduction to 8 bits, in
    /// frame order and row by row. At most <see cref="AutoCropOptions.ColorThreshold"/> distinct colors are tracked; later
    /// ones are ignored. The background is the first pixel seen of the most frequent tracked color (the first tracked
    /// among equals).</item>
    /// <item>A border is found when fewer than <see cref="AutoCropOptions.ColorThreshold"/> distinct colors were seen, or
    /// when <see cref="AutoCropOptions.BucketThreshold"/> is set and at least that share of the border pixels falls in the
    /// luma bucket of the background: <c>min(10, Y * 11 / 255)</c>, where <c>Y</c> is the 8-bit Rec. 709 luma of the color
    /// flattened onto white.</item>
    /// <item>Otherwise the first two steps are retried once with half the threshold (rounded up) and without the bucket
    /// test, on the canvas without 1/20 of its width and height on each side (rounded down). When the retry finds a border,
    /// the halved threshold and the smaller rectangle are used for the next step.</item>
    /// <item>A pixel is background when <c>2126 |dR| + 7152 |dG| + 722 |dB| &lt;= threshold * 10000</c> and
    /// <c>|dA| &lt; threshold</c>, where the differences are taken from the background color. The content box is the
    /// bounding box, over all frames, of the other pixels of the rectangle. The analysis succeeds when the box is at least
    /// 3 pixels wide and high.</item>
    /// <item>With <see cref="AutoCropOptions.AnalyzeWeights"/>, the weights are the mean over all pixels of all frames of
    /// <c>p * d</c>, where <c>p</c> is the position of the pixel center relative to the canvas center, from -1 to 1, and
    /// <c>d</c> is the color difference above divided by its maximum and multiplied by alpha, from 0 to 1.</item>
    /// </list>
    /// <para>
    /// A threshold of 1 cannot find a border without <see cref="AutoCropOptions.BucketThreshold"/>, since a border always
    /// has at least one color. The paddings and <see cref="AutoCropOptions.PaddingMode"/> are not used by the analysis.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to analyze.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="AutoCropOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The analysis, an <see cref="AutoCropAnalysis{TPixel}"/> of the pixel type of the image.
    /// <see cref="AutoCropAnalysis.Success"/> is <see langword="false"/> when no border or no content of at least 3x3 pixels
    /// is found, for example for a uniform image.
    /// </returns>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is not a number or is infinite.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active on a frame.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static AutoCropAnalysis AnalyzeAutoCrop(this Image image, AutoCropOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.AnalyzeAutoCropCore(options ?? AutoCropOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Detects the background and the bounding box of the content of the image, without changing it. The background is
    /// given as a pixel of the image. See <see cref="AnalyzeAutoCrop(Image, AutoCropOptions?, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="image">The image to analyze.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="AutoCropOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The analysis.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is not a number or is infinite.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active on a frame.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static AutoCropAnalysis<TPixel> AnalyzeAutoCrop<TPixel>(this Image<TPixel> image, AutoCropOptions? options = null, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(image);
        return (AutoCropAnalysis<TPixel>)image.AnalyzeAutoCropCore(options ?? AutoCropOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Crops every frame of the image to the bounding box of its content, detected by
    /// <see cref="AnalyzeAutoCrop(Image, AutoCropOptions?, CancellationToken)"/>, plus the padding of the options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The kept rectangle is the content box extended by <see cref="AutoCropOptions.PaddingX"/> pixels on the left and on
    /// the right and by <see cref="AutoCropOptions.PaddingY"/> pixels above and below, then moved by
    /// <c>padding * weight</c> pixels (truncated toward zero) on each axis when
    /// <see cref="AutoCropOptions.AnalyzeWeights"/> is set. Padding inside the canvas keeps the original pixels.
    /// </para>
    /// <para>
    /// When the rectangle reaches outside the canvas, <see cref="AutoCropPaddingMode.Contain"/> clamps it to the canvas,
    /// and <see cref="AutoCropPaddingMode.Expand"/> enlarges the canvas: the pixels outside the original canvas get the
    /// detected background color in every frame. The image is left unchanged when the analysis does not succeed or when
    /// the rectangle is the whole canvas.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="AutoCropOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if the image was cropped or enlarged; <see langword="false"/> if it is unchanged.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The cursor hotspot of a frame designates a pixel that the crop removes, or a floating-point sample is not a number or is infinite. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The enlarged canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static bool AutoCrop(this Image image, AutoCropOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.AutoCropCore(analysis: null, options ?? AutoCropOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Crops every frame of the image to the content box of an existing analysis, plus the padding of the options, without
    /// analyzing the image again. See <see cref="AutoCrop(Image, AutoCropOptions?, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// The analysis may come from another image of the same canvas size, for example a clone processed differently. Only
    /// the paddings and <see cref="AutoCropOptions.PaddingMode"/> of the options are used.
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="analysis">The analysis to apply. The image is left unchanged when <see cref="AutoCropAnalysis.Success"/> is <see langword="false"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="AutoCropOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if the image was cropped or enlarged; <see langword="false"/> if it is unchanged.</returns>
    /// <exception cref="ArgumentException"><see cref="AutoCropAnalysis.CanvasSize"/> is not the size of the image.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The canvas must be enlarged but <see cref="AutoCropAnalysis.BackgroundColor"/> is not exactly representable in the pixel format of the image (it comes from an image of another pixel format, or from an image of floating-point pixels of another pixel type, whose background is exact in its own type only), or the cursor hotspot of a frame designates a pixel that the crop removes. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The enlarged canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static bool AutoCrop(this Image image, AutoCropAnalysis analysis, AutoCropOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(analysis);
        var size = image.Size;
        if (analysis.CanvasSize != size)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The analysis was computed on a {analysis.CanvasSize.Width}x{analysis.CanvasSize.Height} canvas, but the image is {size.Width}x{size.Height}."), nameof(analysis));

        return image.AutoCropCore(analysis, options ?? AutoCropOptions.Default, cancellationToken);
    }

    /// <summary>Resizes every frame of the image.</summary>
    /// <remarks>
    /// <para>
    /// The output size is computed exactly from <see cref="ResizeOptions.Size"/> and <see cref="ResizeOptions.Mode"/>
    /// (nearest rounding, ties upward, at least one pixel); a resize to the current size leaves the image unchanged.
    /// Output pixel centers are mapped exactly onto the source, kernels are widened when downsampling and normalized, and
    /// image edges are clamped.
    /// </para>
    /// <para>
    /// <see cref="ResizeMode.Pad"/> and <see cref="ResizeMode.BoxPad"/> resize the image as <see cref="ResizeMode.Contain"/>
    /// does, then place it on a canvas of exactly the target size at <see cref="ResizeOptions.Anchor"/>: the padding offset
    /// is <c>floor((target - size) * a / 2)</c> on each axis, where <c>a</c> is 0 for the left or top side, 1 for the
    /// center and 2 for the right or bottom side. The padding is <see cref="ResizeOptions.BackgroundColor"/>, and the
    /// edges of the image are clamped as for any resize: the padding does not blend into the image. When no resampling
    /// is needed (<see cref="ResizeMode.BoxPad"/> of an image that fits), the pixels are copied unchanged. Otherwise each
    /// frame is resized into a temporary storage of the resized size, charged to the image allocation scope too.
    /// </para>
    /// <para>
    /// Colors are filtered premultiplied by alpha and unpremultiplied afterward; a pixel whose resulting alpha is zero is
    /// transparent black. A frame whose pixels read by the filter are all opaque is filtered as its colors alone and keeps
    /// the maximum alpha. Samples are filtered in double precision and written at the storage precision (16-bit samples
    /// never go through 8 bits), rounded to nearest with ties upward and clamped. 8-bit samples filtered as stored
    /// (<see cref="ResizeWorkingSpace.Encoded"/>) without translucent pixels are filtered in fixed point (14-bit weights,
    /// 16-bit intermediate rows): about 0.2% of them are one unit off the correctly rounded value, none more. Nearest
    /// neighbor copies source pixels exactly. The scratch storage is charged to the image allocation scope together with the original and replacement
    /// storages.
    /// </para>
    /// <para>
    /// Floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) go through the same double-precision
    /// filters, their values being the working samples (premultiplied by alpha, which is a fraction of 1). The colors are
    /// rounded to the nearest <see cref="float"/> or <see cref="Half"/> and are not clamped, so values above 1 (high
    /// dynamic range highlights) and the negative lobes of the filters survive; a finite value beyond the largest half
    /// becomes an infinity. Alpha is clamped to [0, 1]. A sample that is not a number is not treated apart: it spreads to
    /// every output sample whose filter reads it. Nearest neighbor copies them exactly too.
    /// </para>
    /// <para>
    /// With <see cref="ResizeWorkingSpace.LinearSrgb"/>, floating-point samples labeled as linear light
    /// (<see cref="ImageMetadata.TransferFunction"/>, the default of these formats) are filtered as they are, whatever
    /// their values. Samples labeled with another transfer function are decoded and encoded again with it in double
    /// precision: its domain is [0, 1], so a color sample outside it, or that is not a number, is rejected before anything
    /// changes, and the filtered light is clamped to [0, 1] before it is encoded, as for the integer formats.
    /// </para>
    /// <para>
    /// When the image configuration allows several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>), the
    /// output rows of a large frame are split into bands resized concurrently, each with its own scratch; the result is
    /// identical to the single-worker result.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The resize options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException">Upscaling is required but <see cref="ResizeOptions.AllowUpscaling"/> is <see langword="false"/> (for <see cref="ResizeMode.Stretch"/> and <see cref="ResizeMode.Cover"/>), or downscaling is required but <see cref="ResizeOptions.AllowDownscaling"/> is <see langword="false"/> (for every mode but <see cref="ResizeMode.Contain"/>).</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ResizeWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile or with reserved transfer characteristics, or for floating-point samples outside [0, 1] that are not labeled as linear light; the result is padded, the pixel format has no alpha and <see cref="ResizeOptions.BackgroundColor"/> is not an opaque color; or the cursor hotspot of a frame designates a pixel that <see cref="ResizeMode.Cover"/> removes. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The new canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Resize(this Image image, ResizeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        var geometry = ResizeGeometry.Compute(image.Size, options);
        image.Resize(geometry, options, cancellationToken);
    }

    /// <summary>Adds a border around every frame of the image: the canvas grows by the given numbers of pixels on each side.</summary>
    /// <remarks>
    /// The pixels are copied unchanged, and the border is <paramref name="backgroundColor"/> converted to the pixel format
    /// of the image. Padding by zero on every side leaves the image unchanged. To center the image on a canvas of a given
    /// size, use <see cref="ExtendCanvas"/>.
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="left">The number of columns added on the left. Must not be negative.</param>
    /// <param name="top">The number of rows added on the top. Must not be negative.</param>
    /// <param name="right">The number of columns added on the right. Must not be negative.</param>
    /// <param name="bottom">The number of rows added on the bottom. Must not be negative.</param>
    /// <param name="backgroundColor">The color of the border, or <see langword="null"/> for transparent black, which requires a pixel format with alpha.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentOutOfRangeException">A padding is negative.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha and <paramref name="backgroundColor"/> is not an opaque color. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The new canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ResizeTests.cs" ref:region="Pad_Border" ref:dedent="true">
    /// using var image = new Image&lt;Rgb24&gt;(4, 3, new Rgb24(10, 20, 30));
    ///
    /// // A 2-pixel white border on the left and on the right, 1 pixel above and below
    /// image.Pad(left: 2, top: 1, right: 2, bottom: 1, backgroundColor: new Rgba64(65535, 65535, 65535));
    /// Assert.Equal(new Size(8, 5), image.Size);
    /// Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][0, 0]);
    /// Assert.Equal(new Rgb24(10, 20, 30), image.Frames[0][2, 1]);
    /// </code>
    /// </example>
    public static void Pad(this Image image, int left, int top, int right, int bottom, Rgba64? backgroundColor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        ArgumentOutOfRangeException.ThrowIfNegative(top);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        ArgumentOutOfRangeException.ThrowIfNegative(bottom);
        var size = image.Size;
        image.ExtendCanvasCore(left, top, (long)size.Width + left + right, (long)size.Height + top + bottom, backgroundColor, "pad the image", nameof(backgroundColor), cancellationToken);
    }

    /// <summary>Enlarges the canvas of every frame of the image to a given size, and places the image on it at an anchor.</summary>
    /// <remarks>
    /// The pixels are copied unchanged, and the rest of the canvas is <paramref name="backgroundColor"/> converted to the
    /// pixel format of the image. The image lies at <c>floor((size - imageSize) * a / 2)</c> on each axis, where <c>a</c>
    /// is 0 for the left or top side, 1 for the center and 2 for the right or bottom side: a centered image gets the extra
    /// pixel of an odd padding on its right or bottom side. A canvas of the current size leaves the image unchanged.
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="size">The size of the new canvas. Each dimension must be at least that of the image; use <see cref="Crop"/> to make the canvas smaller.</param>
    /// <param name="anchor">Where the image is placed on the canvas. Defaults to <see cref="ResizeAnchor.Center"/>.</param>
    /// <param name="backgroundColor">The color of the new area, or <see langword="null"/> for transparent black, which requires a pixel format with alpha.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is smaller than the image in a dimension, or <paramref name="anchor"/> is not a defined <see cref="ResizeAnchor"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha and <paramref name="backgroundColor"/> is not an opaque color. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The new canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ResizeTests.cs" ref:region="ExtendCanvas_Square" ref:dedent="true">
    /// using var banner = new Image&lt;Rgba32&gt;(4, 2, new Rgba32(200, 0, 0, 255));
    ///
    /// // A square canvas with the image at the bottom; the new area is transparent
    /// banner.ExtendCanvas(new Size(4, 4), ResizeAnchor.Bottom);
    /// Assert.Equal(new Rgba32(0, 0, 0, 0), banner.Frames[0][0, 0]);
    /// Assert.Equal(new Rgba32(200, 0, 0, 255), banner.Frames[0][0, 2]);
    /// </code>
    /// </example>
    public static void ExtendCanvas(this Image image, Size size, ResizeAnchor anchor = ResizeAnchor.Center, Rgba64? backgroundColor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Enum.IsDefined(anchor))
            throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "The anchor is not valid.");

        var current = image.Size;
        if (size.Width < current.Width || size.Height < current.Height)
            throw new ArgumentOutOfRangeException(nameof(size), size, string.Create(CultureInfo.InvariantCulture, $"The canvas must be at least as large as the {current.Width}x{current.Height} image in each dimension; use Crop to make it smaller."));

        var (anchorX, anchorY) = ResizeGeometry.GetAnchorPosition(anchor);
        var left = (long)(size.Width - current.Width) * anchorX / 2;
        var top = (long)(size.Height - current.Height) * anchorY / 2;
        image.ExtendCanvasCore(left, top, size.Width, size.Height, backgroundColor, "extend the canvas", nameof(backgroundColor), cancellationToken);
    }

    /// <summary>Rotates every frame of the image clockwise by a multiple of 90 degrees (an exact pixel permutation).</summary>
    /// <param name="image">The image to modify.</param>
    /// <param name="mode">The rotation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined <see cref="RotateMode"/>.</exception>
    /// <exception cref="ImageResourceLimitException">The rotated canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/> or <see cref="ImageResourceLimits.MaxHeight"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Rotate(this Image image, RotateMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The rotation is not valid.");

        var size = image.Size;
        if (mode == RotateMode.None)
        {
            image.Owner.EnsureCanModify("rotate the image");
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        image.TransformGeometry(new Rectangle(0, 0, size.Width, size.Height), OrientationTransform.ForRotation(mode), normalizeOrientation: false, "rotate the image", cancellationToken);
    }

    /// <summary>Rotates every frame of the image clockwise by an angle, around its center.</summary>
    /// <remarks>
    /// <para>
    /// The center of the image goes to the center of the canvas. With the default
    /// <see cref="TransformCanvasMode.Expand"/>, the canvas grows to hold the whole rotated image, and the corners it
    /// gains get <see cref="TransformOptions.BackgroundColor"/> (transparent by default). A rotation by a multiple of 90
    /// degrees whose canvas holds exactly the rotated image (always the case with <see cref="TransformCanvasMode.Expand"/>)
    /// is the exact pixel permutation of <see cref="Rotate(Image, RotateMode, CancellationToken)"/>, which also swaps the
    /// axes of <see cref="ImageMetadata.PixelAspectRatio"/>; a rotation by a multiple of 360 degrees leaves the image
    /// unchanged. See <see cref="Transform(Image, ProjectiveTransform, TransformOptions?, CancellationToken)"/> for the
    /// sampling, the background and the canvas.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="degrees">The angle, in degrees. A positive angle turns clockwise.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="TransformOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a finite number, or <see cref="TransformOptions.CanvasMode"/> is <see cref="TransformCanvasMode.Explicit"/> and <see cref="TransformOptions.CanvasSize"/> is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha, the result shows the background and <see cref="TransformOptions.BackgroundColor"/> is not opaque; <see cref="ResizeWorkingSpace.LinearSrgb"/> is requested for an image it cannot decode; or the cursor hotspot of a frame leaves the canvas. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Rotate(this Image image, double degrees, TransformOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var rotation = AffineTransform.CreateRotation(degrees);
        options = ValidateTransformOptions(options);
        var size = image.Size;
        image.Warp(AffineTransform.CreateTranslation(-size.Width / 2.0, -size.Height / 2.0).Then(rotation), options, centered: true, nameof(degrees), cancellationToken);
    }

    /// <summary>Skews every frame of the image around its center: its vertical lines lean by one angle and its horizontal lines by another one.</summary>
    /// <remarks>
    /// <para>
    /// A point moves horizontally by <c>tan(degreesX)</c> times its distance to the center row, and vertically by
    /// <c>tan(degreesY)</c> times its distance to the center column (<see cref="AffineTransform.CreateSkew"/>). The center
    /// of the image goes to the center of the canvas. With the default <see cref="TransformCanvasMode.Expand"/>, the
    /// canvas grows to hold the whole skewed image. See
    /// <see cref="Transform(Image, ProjectiveTransform, TransformOptions?, CancellationToken)"/> for the sampling, the
    /// background and the canvas.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="degreesX">The angle the vertical lines lean by, in degrees. Must be between -90 and 90, excluded.</param>
    /// <param name="degreesY">The angle the horizontal lines lean by, in degrees. Must be between -90 and 90, excluded.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="TransformOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentOutOfRangeException">An angle is not between -90 and 90 degrees, excluded.</exception>
    /// <exception cref="ArgumentException">The two angles flatten the image to a line (their tangents multiply to 1), or <see cref="TransformOptions.CanvasMode"/> is <see cref="TransformCanvasMode.Explicit"/> and <see cref="TransformOptions.CanvasSize"/> is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha, the result shows the background and <see cref="TransformOptions.BackgroundColor"/> is not opaque; <see cref="ResizeWorkingSpace.LinearSrgb"/> is requested for an image it cannot decode; or the cursor hotspot of a frame leaves the canvas. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Skew(this Image image, double degreesX, double degreesY, TransformOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var skew = AffineTransform.CreateSkew(degreesX, degreesY);
        options = ValidateTransformOptions(options);
        var size = image.Size;
        image.Warp(AffineTransform.CreateTranslation(-size.Width / 2.0, -size.Height / 2.0).Then(skew), options, centered: true, nameof(degreesX), cancellationToken);
    }

    /// <summary>Transforms every frame of the image with an affine transformation: any combination of translations, scalings, rotations and skews.</summary>
    /// <remarks>
    /// <para>
    /// See <see cref="Transform(Image, ProjectiveTransform, TransformOptions?, CancellationToken)"/>. An affine
    /// transformation shrinks the image by the same factors everywhere, and its kernels are widened by those factors
    /// without limit, as a resize does.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="transform">The transformation, in stored-pixel coordinates: the image covers <c>[0, width] x [0, height]</c>.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="TransformOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException"><paramref name="transform"/> flattens the image to a line or a point, or <see cref="TransformOptions.CanvasMode"/> is <see cref="TransformCanvasMode.Explicit"/> and <see cref="TransformOptions.CanvasSize"/> is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha, the result shows the background and <see cref="TransformOptions.BackgroundColor"/> is not opaque; <see cref="ResizeWorkingSpace.LinearSrgb"/> is requested for an image it cannot decode; or the cursor hotspot of a frame leaves the canvas. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Transform(this Image image, AffineTransform transform, TransformOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options = ValidateTransformOptions(options);
        image.Warp(transform, options, centered: false, nameof(transform), cancellationToken);
    }

    /// <summary>Transforms every frame of the image with a projective transformation: the image seen in perspective.</summary>
    /// <remarks>
    /// <para>
    /// <b>Geometry.</b> The image covers <c>[0, width] x [0, height]</c> in stored-pixel coordinates (pixel <c>(i, j)</c>
    /// is the unit square centered at <c>(i + 1/2, j + 1/2)</c>), and <paramref name="transform"/> moves it to the plane of
    /// the result. With <see cref="TransformCanvasMode.Expand"/> (the default), the canvas is the smallest one that holds
    /// the bounds of the transformed image (a size within 1e-6 of an integer is that integer), and the bounds are
    /// centered in it: the translation of the transformation does not matter, and every corner of the image must be in
    /// front of the viewer (<c>W</c> positive). With <see cref="TransformCanvasMode.Keep"/> and
    /// <see cref="TransformCanvasMode.Explicit"/>, the canvas has the size of the image or
    /// <see cref="TransformOptions.CanvasSize"/>, and the coordinates of the plane are those of the canvas: what lands
    /// outside of it is cut. A transformation that moves every pixel onto a pixel of a canvas it fills exactly (a quarter
    /// turn, a mirror) is an exact pixel permutation, as <see cref="Rotate(Image, RotateMode, CancellationToken)"/> and
    /// <see cref="Flip(Image, FlipMode, CancellationToken)"/> are; one that moves nothing leaves the image unchanged.
    /// </para>
    /// <para>
    /// <b>Sampling.</b> Every output pixel takes the value of the source at the point its center comes from, filtered with
    /// <see cref="TransformOptions.Filter"/>, the kernels of <see cref="Resize"/>. Where the transformation shrinks the
    /// source along one of its axes, the kernel of that axis is widened by the same factor, as a resize does (a rotation
    /// or a skew widens nothing); a projective transformation, whose factors grow without bound toward its horizon,
    /// widens them by 16 at most. The weights are those of the kernel at the distance of each source pixel center,
    /// normalized by their sum. Colors are filtered premultiplied by alpha, in double precision, in the working space of
    /// <see cref="TransformOptions.WorkingSpace"/>, and rounded once to the storage precision as a resize rounds them
    /// (a pixel whose alpha is zero is transparent black); floating-point samples follow the rules of
    /// <see cref="Resize"/>. Nearest neighbor copies the source pixel that contains the point, exactly. The box filter
    /// averages the source pixels whose centers lie in <c>(p - f/2, p + f/2]</c> on each axis, where <c>p</c> is the point
    /// and <c>f</c> the widening of the axis: without widening, the pixel that contains the point.
    /// </para>
    /// <para>
    /// <b>Background.</b> The pixels beyond the edges of the source are <see cref="TransformOptions.BackgroundColor"/>
    /// (transparent black by default), converted to the pixel format and filtered like the others: the edges of the
    /// image blend into the background, and the canvas the image does not reach is the background. A pixel format
    /// without alpha needs an opaque background, unless no output pixel reads beyond the edges of the source (a
    /// projective transformation always may).
    /// </para>
    /// <para>
    /// <b>Frames.</b> The operation is transactional across every frame and the poster, like <see cref="Resize"/>. A
    /// cursor hotspot moves to the pixel that contains the image of its pixel center; when that pixel is not on the
    /// canvas, the operation throws and the image is unchanged. <see cref="ImageMetadata.PixelAspectRatio"/> is kept,
    /// except by the permutations that swap the axes. When the image configuration allows several workers
    /// (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>), the output rows of a large frame are split into bands
    /// transformed concurrently; the result is identical to the single-worker result.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="transform">The transformation, in stored-pixel coordinates: the image covers <c>[0, width] x [0, height]</c>.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="TransformOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException"><paramref name="transform"/> flattens the image to a line or a point; or puts part of the image on or behind its horizon with <see cref="TransformCanvasMode.Expand"/>; or <see cref="TransformOptions.CanvasMode"/> is <see cref="TransformCanvasMode.Explicit"/> and <see cref="TransformOptions.CanvasSize"/> is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha and <see cref="TransformOptions.BackgroundColor"/> is not opaque; <see cref="ResizeWorkingSpace.LinearSrgb"/> is requested for an image it cannot decode; or the cursor hotspot of a frame leaves the canvas. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/>, <see cref="ImageResourceLimits.MaxHeight"/> or <see cref="ImageResourceLimits.MaxFramePixels"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Transform(this Image image, ProjectiveTransform transform, TransformOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options = ValidateTransformOptions(options);
        image.Warp(transform, options, centered: false, nameof(transform), cancellationToken);
    }

    /// <summary>
    /// Transforms every frame so that the stored pixels are displayed upright, according to
    /// <see cref="ImageMetadata.Orientation"/>, then sets the orientation to <see cref="ExifOrientation.TopLeft"/> (an
    /// existing EXIF orientation tag is rewritten to 1 as well). All eight EXIF orientations are supported: mirrors (2, 4),
    /// rotations (3, 6, 8) and the diagonal reflections (5: transpose, 7: transverse). Does nothing when the orientation is
    /// already <see cref="ExifOrientation.TopLeft"/>.
    /// </summary>
    /// <param name="image">The image to modify.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ImageResourceLimitException">The reoriented canvas exceeds <see cref="ImageResourceLimits.MaxWidth"/> or <see cref="ImageResourceLimits.MaxHeight"/>, or the replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void AutoOrient(this Image image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var size = image.Size;
        var orientation = image.Metadata.Orientation;
        if (orientation == ExifOrientation.TopLeft)
        {
            image.Owner.EnsureCanModify("auto-orient the image");
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        image.TransformGeometry(new Rectangle(0, 0, size.Width, size.Height), OrientationTransform.ForOrientation(orientation), normalizeOrientation: true, "auto-orient the image", cancellationToken);
    }

    /// <summary>Mirrors every frame of the image (including the poster frame) in place, in stored-pixel coordinates.</summary>
    /// <param name="image">The image to modify.</param>
    /// <param name="mode">The mirror axis.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames may already be mirrored.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined <see cref="FlipMode"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Flip(this Image image, FlipMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The flip mode is not valid.");

        image.FlipAllFrames(mode, cancellationToken);
    }

    /// <summary>Mirrors one frame in place. The EXIF thumbnail of the image, now stale, is removed.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="mode">The mirror axis.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be mirrored.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined <see cref="FlipMode"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static void Flip(this ImageFrame frame, FlipMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The flip mode is not valid.");

        var storage = frame.GetStorage();
        cancellationToken.ThrowIfCancellationRequested();
        using (var lease = storage.AcquireLease())
        {
            frame.OwnerImage.Metadata.RemoveStaleThumbnail();
            frame.FlipPixels(lease, mode, cancellationToken);
        }

        Image.FlipHotspot(frame, mode);
    }

    /// <summary>
    /// Converts every frame of the image (including the poster frame) to grayscale in place, keeping the pixel format and
    /// alpha. Uses Rec. 709 luma coefficients (0.2126, 0.7152, 0.0722) on the encoded sample values, nearest rounding with
    /// ties upward, and clamping, at the storage precision: <c>Y = (2126 R + 7152 G + 722 B + 5000) / 10000</c>, stored
    /// into R, G and B. Floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) get
    /// <c>0.2126 R + 0.7152 G + 0.0722 B</c> of their stored values in double precision, rounded to the nearest sample
    /// and not clamped (a sample that is not a number gives a luma that is not a number). Gray formats are left unchanged. The pixel format is kept, so a compatible RGB profile stays. Use <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> to obtain
    /// <see cref="Gray8"/> or <see cref="Gray16"/> storage.
    /// </summary>
    /// <param name="image">The image to modify.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames may already be converted.</param>
    /// <exception cref="UnsupportedImageFeatureException">The retained ICC profile is incompatible with the pixel format (it would be kept silently on gray pixels otherwise). Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Grayscale(this Image image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.GrayscaleAllFrames(cancellationToken);
    }

    /// <summary>Converts one frame to grayscale in place, keeping the pixel format and alpha. See <see cref="Grayscale(Image, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be converted.</param>
    /// <exception cref="UnsupportedImageFeatureException">The ICC profile of the image is incompatible with the pixel format. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static void Grayscale(this ImageFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var storage = frame.GetStorage();
        var image = frame.OwnerImage;
        image.EnsureColorProfileCompatible();
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        image.Metadata.RemoveStaleThumbnail();
        frame.GrayscalePixels(lease, cancellationToken);
    }

    /// <summary>
    /// Reduces the colors of every frame of the image (including the poster frame) to a palette, in place, so that each frame
    /// has at most <see cref="QuantizeOptions.MaxColors"/> distinct colors, or only colors of
    /// <see cref="QuantizeOptions.Palette"/>. The pixel format is kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pixels are read as 8-bit RGBA, with the conversion of <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>, and every pixel gets the
    /// palette color of its frame that the mapping gives it (see <see cref="QuantizeOptions"/> and
    /// <see cref="DitheringMode"/>): a 16-bit or floating-point sample gets the exact value of an 8-bit sample, and every fully
    /// transparent pixel becomes transparent black, so that the frame really has no more colors than its palette. Without a
    /// fixed palette, each frame gets its own palette, which holds its exact colors when they fit (such a frame only changes
    /// by those two rules). Alpha is reduced with the colors: translucent colors are palette entries of their own, and an
    /// opaque pixel always gets an opaque color.
    /// </para>
    /// <para>
    /// A fixed palette is restricted to the colors the pixel format stores: the opaque entries for a format without alpha,
    /// the opaque gray entries for a gray format. The colors of an image are those of its color space: no color profile is
    /// applied. The result is deterministic: the same on every platform.
    /// </para>
    /// <para>
    /// The working memory (about 1.3 MiB, 2.6 MiB for a pixel format with alpha, and a few rows) is charged to the image and
    /// rented before a pixel changes. On failure or cancellation, some frames or rows may already be reduced.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/QuantizeTests.cs" ref:region="Quantize_Dithering" ref:dedent="true">
    /// [Fact]
    /// public void Quantize_Dithering()
    /// {
    ///     // A smooth gradient: 64 x 64 pixels of 4,096 different colors
    ///     using var image = new Image&lt;Rgba32&gt;(64, 64);
    ///     image.Frames[0].ProcessPixelRows(pixels =&gt;
    ///     {
    ///         for (var y = 0; y &lt; pixels.Height; y++)
    ///         {
    ///             var row = pixels.GetRowSpan(y);
    ///             for (var x = 0; x &lt; row.Length; x++)
    ///             {
    ///                 row[x] = new Rgba32((byte)(x * 4), (byte)(y * 4), 128);
    ///             }
    ///         }
    ///     });
    ///
    ///     // At most 16 colors, with Floyd–Steinberg error diffusion to hide the bands
    ///     image.Quantize(new QuantizeOptions { MaxColors = 16, Dithering = DitheringMode.FloydSteinberg });
    ///
    ///     var colors = new HashSet&lt;Rgba32&gt;();
    ///     image.Frames[0].ProcessPixelRows(pixels =&gt;
    ///     {
    ///         for (var y = 0; y &lt; pixels.Height; y++)
    ///         {
    ///             colors.UnionWith(pixels.GetRowSpan(y).ToArray());
    ///         }
    ///     });
    ///
    ///     Assert.HasCountLessThanOrEqual(16, colors);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="QuantizeOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The fixed palette has no color the pixel format can store. Nothing is changed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Quantize(this Image image, QuantizeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.QuantizeFrames(frames: null, options ?? QuantizeOptions.Default, cancellationToken);
    }

    /// <summary>Reduces the colors of one frame to a palette, in place. See <see cref="Quantize(Image, QuantizeOptions?, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="QuantizeOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be reduced.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The fixed palette has no color the pixel format can store. Nothing is changed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Quantize(this ImageFrame frame, QuantizeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _ = frame.GetStorage();
        frame.OwnerImage.QuantizeFrames([frame], options ?? QuantizeOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Gives every pixel of every frame of the image (including the poster frame) the color a matrix makes of its own
    /// one, in place: brightness, contrast, saturation, a tint, a negative. The pixel format is kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The matrix is applied to the stored (encoded) sample values, with straight alpha, at the storage precision:
    /// see <see cref="ColorMatrix"/> for the arithmetic. A pixel format without alpha gets the color of the result
    /// over black, and a grayscale format the Rec. 709 luma of the result. Every pixel gets the samples the matrix
    /// gives it, the color of a fully transparent pixel included, as <see cref="Grayscale(Image, CancellationToken)"/>
    /// does: with <see cref="ColorMatrix.CreateGrayscale(double)"/>, the pixels are the same, bit for bit. No color
    /// profile is applied: the colors of the matrix are those of the color space of the image.
    /// </para>
    /// <para>
    /// The working storage (one row) is charged to the image and rented before a pixel changes. On cancellation, some
    /// rows may already be converted, the same ones in every frame. To change the colors of a part of an image, or of
    /// a drawing, see <see cref="DrawingCanvas.FilterBackdrop(ColorMatrix)"/> and
    /// <see cref="DrawingCanvas.Filter(ColorMatrix, DrawingAction)"/>.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="matrix">The matrix.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="matrix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyColorMatrix(this Image image, ColorMatrix matrix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(matrix);
        image.Draw(matrix, static (canvas, matrix) => canvas.FilterBackdrop(matrix), cancellationToken);
    }

    /// <summary>Gives every pixel of one frame the color a matrix makes of its own one, in place. See <see cref="ApplyColorMatrix(Image, ColorMatrix, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="matrix">The matrix.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be converted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="matrix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void ApplyColorMatrix(this ImageFrame frame, ColorMatrix matrix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(matrix);
        frame.Draw(matrix, static (canvas, matrix) => canvas.FilterBackdrop(matrix), cancellationToken);
    }

    /// <summary>
    /// Counts the pixels of every level of a channel, over every frame of the image (including the poster frame). The image
    /// is not changed.
    /// </summary>
    /// <remarks>
    /// The levels are the stored (encoded) sample values, at the storage precision: 256 bins for 8-bit pixel formats,
    /// 65,536 for 16-bit ones (see <see cref="ImageHistogram"/>). No color profile is applied. Floating-point pixel formats
    /// have no discrete levels: convert the image to <see cref="Rgba64"/> with <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>
    /// first. The working storage (one row) is charged to the image.
    /// </remarks>
    /// <param name="image">The image.</param>
    /// <param name="channel">The channel to count.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The histogram.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is not a defined <see cref="HistogramChannel"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static ImageHistogram ComputeHistogram(this Image image, HistogramChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateHistogramChannel(channel);
        return image.ComputeHistogramCore(frames: null, channel, cancellationToken);
    }

    /// <summary>Counts the pixels of every level of a channel of one frame. See <see cref="ComputeHistogram(Image, HistogramChannel, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="channel">The channel to count.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The histogram.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is not a defined <see cref="HistogramChannel"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static ImageHistogram ComputeHistogram(this ImageFrame frame, HistogramChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateHistogramChannel(channel);
        _ = frame.GetStorage();
        return frame.OwnerImage.ComputeHistogramCore([frame], channel, cancellationToken);
    }

    /// <summary>
    /// Equalizes the histogram of every frame of the image (including the poster frame) in place, which spreads its levels
    /// over the whole sample range: globally, or by tiles with a contrast limit (CLAHE). The pixel format and alpha are kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="HistogramEqualizationMode"/> for the mappings and <see cref="HistogramColorMode"/> for the samples
    /// they remap: by default, the luminance, so that the hues do not shift. The mappings work on the stored (encoded)
    /// sample values at the storage precision, with 256 levels for 8-bit pixel formats and 65,536 for 16-bit ones, and are
    /// deterministic: the same on every platform. No color profile is applied. Fully transparent pixels are not counted.
    /// Floating-point pixel formats have no discrete levels: convert the image to <see cref="Rgba64"/> with
    /// <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> first.
    /// </para>
    /// <para>
    /// Each frame is equalized alone by default; <see cref="HistogramEqualizationOptions.ShareHistogramAcrossFrames"/>
    /// gives every frame the mapping of their combined histograms, so that an animation does not flicker.
    /// </para>
    /// <para>
    /// The working memory (the tables of the tiles: up to 25 MiB for 8 x 8 tiles of 16-bit samples remapped per channel)
    /// is charged to the image and rented before a pixel changes. On failure or cancellation, some frames or rows may
    /// already be remapped.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/HistogramTests.cs" ref:region="Histogram_Clahe" ref:dedent="true">
    /// [Fact]
    /// public void Histogram_Clahe()
    /// {
    ///     // A dim, low-contrast image: the levels are between 100 and 131
    ///     using var image = new Image&lt;Rgb24&gt;(64, 64);
    ///     image.Frames[0].ProcessPixelRows(pixels =&gt;
    ///     {
    ///         for (var y = 0; y &lt; pixels.Height; y++)
    ///         {
    ///             var row = pixels.GetRowSpan(y);
    ///             for (var x = 0; x &lt; row.Length; x++)
    ///             {
    ///                 var level = (byte)(100 + ((x + y) % 32));
    ///                 row[x] = new Rgb24(level, level, (byte)(level - 20));
    ///             }
    ///         }
    ///     });
    ///
    ///     var before = image.ComputeHistogram(HistogramChannel.Luminance);
    ///     var contrastBefore = before.Counts.LastIndexOfAnyExcept(0L) - before.Counts.IndexOfAnyExcept(0L);
    ///
    ///     // Contrast-limited adaptive equalization of the luminance, on a 4 x 4 grid of tiles
    ///     image.Equalize(new HistogramEqualizationOptions
    ///     {
    ///         Mode = HistogramEqualizationMode.Adaptive,
    ///         TileCountX = 4,
    ///         TileCountY = 4,
    ///         ClipLimit = 2,
    ///     });
    ///
    ///     // The levels spread over a range more than twice as wide; the clip limit keeps the contrast from growing further
    ///     var after = image.ComputeHistogram(HistogramChannel.Luminance);
    ///     var contrastAfter = after.Counts.LastIndexOfAnyExcept(0L) - after.Counts.IndexOfAnyExcept(0L);
    ///     Assert.InRange(contrastAfter, 2 * contrastBefore, 255);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HistogramEqualizationOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Equalize(this Image image, HistogramEqualizationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.EqualizeFrames(frames: null, options ?? HistogramEqualizationOptions.Default, cancellationToken);
    }

    /// <summary>Equalizes the histogram of one frame in place. See <see cref="Equalize(Image, HistogramEqualizationOptions?, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="HistogramEqualizationOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be remapped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void Equalize(this ImageFrame frame, HistogramEqualizationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _ = frame.GetStorage();
        frame.OwnerImage.EqualizeFrames([frame], options ?? HistogramEqualizationOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Stretches the contrast of every frame of the image (including the poster frame) in place: the levels between two
    /// percentiles of the histogram are spread linearly over the whole sample range. The pixel format and alpha are kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="AutoLevelsOptions"/> for the mapping and <see cref="HistogramColorMode"/> for the samples it remaps:
    /// by default, the luminance, so that the hues do not shift. The mapping works on the stored (encoded) sample values at
    /// the storage precision, with 256 levels for 8-bit pixel formats and 65,536 for 16-bit ones. No color profile is
    /// applied. Fully transparent pixels are not counted. Floating-point pixel formats have no discrete levels: convert the
    /// image to <see cref="Rgba64"/> with <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> first.
    /// </para>
    /// <para>
    /// Each frame is stretched alone by default; <see cref="AutoLevelsOptions.ShareHistogramAcrossFrames"/> gives every
    /// frame the levels of their combined histograms, so that an animation does not flicker. The working memory is
    /// charged to the image and rented before a pixel changes. On failure or cancellation, some frames or rows may already
    /// be remapped.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="AutoLevelsOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="AutoLevelsOptions.LowPercentile"/> is not less than <see cref="AutoLevelsOptions.HighPercentile"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void AutoLevels(this Image image, AutoLevelsOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= AutoLevelsOptions.Default;
        ValidateAutoLevels(options);
        image.AutoLevelsFrames(frames: null, options, cancellationToken);
    }

    /// <summary>Stretches the contrast of one frame in place. See <see cref="AutoLevels(Image, AutoLevelsOptions?, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="AutoLevelsOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be remapped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="AutoLevelsOptions.LowPercentile"/> is not less than <see cref="AutoLevelsOptions.HighPercentile"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed the allocation limit. Nothing is changed.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void AutoLevels(this ImageFrame frame, AutoLevelsOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        options ??= AutoLevelsOptions.Default;
        ValidateAutoLevels(options);
        _ = frame.GetStorage();
        frame.OwnerImage.AutoLevelsFrames([frame], options, cancellationToken);
    }

    private static void ValidateHistogramChannel(HistogramChannel channel)
    {
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "The histogram channel is not valid.");
    }

    private static void ValidateAutoLevels(AutoLevelsOptions options)
    {
        if (options.LowPercentile >= options.HighPercentile)
            throw new ArgumentException("The low percentile must be less than the high percentile.", nameof(options));
    }

    /// <summary>
    /// Converts the colors of every frame of the image (including the poster frame) to the color space described by an
    /// ICC profile, and labels the image with that profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source color space is the one of <see cref="ImageMetadata.IccProfile"/>. An image without profile is sRGB, or
    /// sGray for grayscale pixel formats (<see cref="IccProfile.Srgb"/>, <see cref="IccProfile.SrgbGray"/>); when
    /// <see cref="ImageMetadata.TransferFunction"/> is <see cref="ColorTransferFunction.Linear"/>, it is linear light with
    /// the sRGB primaries and white point. This is the only operation that applies a color profile: loading, saving,
    /// pixel format conversion and the other processing operations never do.
    /// </para>
    /// <para>
    /// An image without profile whose CICP color description (<see cref="ImageMetadata.Cicp"/>) is not equivalent to
    /// sRGB is converted from the color space of that description: the samples are decoded with the function of its
    /// transfer characteristics (Rec. ITU-T H.273, table 3; unspecified ones are sRGB), and its color primaries (table 2;
    /// unspecified ones are those of BT.709) are adapted to the white of the profile connection space with the linear
    /// Bradford transform. Its matrix coefficients and its range describe how a file stored the samples and play no part.
    /// High dynamic range samples (PQ and HLG) are converted only when
    /// <see cref="IccColorTransformOptions.ToneMapping"/> says how their highlights are compressed
    /// (<see cref="HdrToneMappingOptions"/>). After the conversion, a description that is not equivalent to sRGB,
    /// <see cref="ImageMetadata.ContentLightLevel"/> and <see cref="ImageMetadata.MasteringDisplay"/> are removed: the
    /// profile describes the pixels.
    /// </para>
    /// <para>
    /// The pixel format is kept, so the destination profile must be able to label it: an RGB profile for color formats, a
    /// grayscale profile for <see cref="Gray8"/> and <see cref="Gray16"/>. To change the color model, convert the pixel
    /// format with <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>, or convert sample buffers with
    /// <see cref="IccColorTransform"/> (the only way to reach CMYK).
    /// </para>
    /// <para>
    /// Colors are converted at the storage precision (16-bit formats are never reduced to 8 bits) by the scalar reference
    /// conversion, on the calling thread. Alpha is copied unchanged and the color of transparent pixels is converted like
    /// any other. Colors outside the destination gamut are clipped per channel.
    /// </para>
    /// <para>
    /// Floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) are read as they are and the results are
    /// stored as the nearest <see cref="float"/> or <see cref="Half"/>, without reduction to 16 bits. A profile and a CICP
    /// color description encode values in [0, 1]: a color sample outside that range, or that is not a number, is rejected
    /// before anything changes (bring the samples into range first, for example with the tone mapping of
    /// <see cref="PixelConversionOptions.ToneMapping"/>). Alpha is copied unchanged, whatever its value.
    /// </para>
    /// <para>
    /// The operation is transactional: the converted pixels are built in new buffers, budgeted together with the current
    /// ones, and published with the new profile. On failure or cancellation the image is left exactly as it was. After a
    /// successful conversion, <see cref="ImageMetadata.IccProfile"/> is <paramref name="destinationProfile"/>,
    /// <see cref="ImageMetadata.TransferFunction"/> is <see cref="ColorTransferFunction.Srgb"/> (the samples are described
    /// by the profile) and the EXIF thumbnail, which no longer matches the pixels, is removed. When the source and
    /// destination profiles have identical bytes, the pixels are not touched and only the label is set.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="destinationProfile">The profile of the converted pixels.</param>
    /// <param name="options">The conversion options, or <see langword="null"/> for <see cref="IccColorTransformOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="destinationProfile"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The current or the destination profile cannot label the pixel format, the image has both a profile and the linear
    /// transfer function label, a profile is of a kind that is not supported, the CICP color description of an image
    /// without profile has reserved color primaries or transfer characteristics, or the samples are high dynamic range
    /// ones and <paramref name="options"/> has no <see cref="IccColorTransformOptions.ToneMapping"/>, or a floating-point
    /// color sample is outside [0, 1] or is not a number. The image is unchanged.
    /// </exception>
    /// <exception cref="InvalidImageContentException">The current or the destination profile is malformed. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void ConvertColorProfile(this Image image, IccProfile destinationProfile, IccColorTransformOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(destinationProfile);
        image.ConvertAllFramesToColorProfile(destinationProfile, options ?? IccColorTransformOptions.Default, cancellationToken);
    }

    /// <summary>Applies a convolution matrix to every frame of the image (including the poster frame) in place.</summary>
    /// <remarks>
    /// <para>
    /// Every sample of a pixel becomes the sum of the same sample of its neighbors multiplied by the weights of
    /// <see cref="ConvolutionOptions.Kernel"/>, which is applied as written (it is not flipped) and not normalized. Pixels
    /// read outside the image are chosen by <see cref="ConvolutionOptions.EdgeMode"/>. The pixel format, the size and the
    /// metadata are kept, except for the EXIF thumbnail, which is removed.
    /// </para>
    /// <para>
    /// Colors are filtered premultiplied by alpha and unpremultiplied afterward, and alpha is filtered too; a pixel whose
    /// resulting alpha is zero is transparent black. With <see cref="ConvolutionOptions.PreserveAlpha"/>, colors are
    /// filtered as stored and alpha is left untouched. Samples are filtered in double precision and written at the storage
    /// precision (16-bit samples never go through 8 bits), rounded to nearest with ties upward and clamped: negative sums
    /// become 0.
    /// </para>
    /// <para>
    /// Floating-point samples (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) follow the rules of
    /// <see cref="Resize"/>: their values are the working samples, the colors are rounded to the nearest
    /// <see cref="float"/> or <see cref="Half"/> without clamping (negative sums and values above 1 are kept), alpha is
    /// clamped to [0, 1], and a sample that is not a number spreads to the pixels whose kernel reads it. With
    /// <see cref="ConvolutionWorkingSpace.LinearSrgb"/>, samples labeled as linear light are filtered as they are, and the
    /// other ones must be numbers in [0, 1], the domain of their transfer function: otherwise nothing changes and the
    /// operation is rejected.
    /// </para>
    /// <para>
    /// The image is rewritten in place; the scratch storage (a few rows per worker, proportional to the kernel height) is
    /// charged to the image allocation scope and rented before any pixel changes. When the image configuration allows
    /// several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>), the rows of a large frame are split into
    /// bands convolved concurrently, each with its own scratch; the result is identical to the single-worker result.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The convolution options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be convolved.</param>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile or with reserved transfer characteristics, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Convolve(this Image image, ConvolutionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        image.ConvolveAllFrames(options, cancellationToken);
    }

    /// <summary>
    /// Applies a convolution matrix to one frame in place. The EXIF thumbnail of the image, now stale, is removed. See
    /// <see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="options">The convolution options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be convolved.</param>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile or with reserved transfer characteristics, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static void Convolve(this ImageFrame frame, ConvolutionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        var storage = frame.GetStorage();
        frame.OwnerImage.ConvolveFrame(frame, storage, options, cancellationToken);
    }

    /// <summary>Blurs every displayed frame and the poster frame in place with a Gaussian blur. The EXIF thumbnail, now stale, is removed.</summary>
    /// <remarks>
    /// <para>
    /// Each pixel becomes the mean of the pixels around it, weighted by a bell curve of standard deviation
    /// <paramref name="sigma"/>: the weight of a pixel at a distance <c>d</c> is <c>exp(-d^2 / (2 sigma^2))</c>, up to
    /// three standard deviations. A deviation of 1 softens an image; the blur a CSS filter or a shadow calls a radius
    /// is twice the deviation.
    /// </para>
    /// <para>
    /// The blur is one separable convolution (<see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/>): every
    /// row is filtered by a row of <c>2 ceil(3 sigma) + 1</c> weights, then the filtered rows by the same weights as a
    /// column, in double precision without rounding in between, so the result is the one of the two-dimensional matrix of
    /// their products, and the time grows with the deviation, not with its square. It follows the rules of a convolution
    /// (colors filtered premultiplied by alpha, samples rounded to nearest at the storage precision once, the same result
    /// whatever the number of workers); an opaque frame is blurred as its colors alone and keeps the maximum alpha, except
    /// with <see cref="ConvolutionEdgeMode.Zero"/>, which reads transparent pixels beyond the frame. The weights are
    /// computed without any function of the platform, and are the same everywhere. Without deviation, nothing changes.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="sigma">The standard deviation of the blur, in pixels. Must be between 0 and 10000.</param>
    /// <param name="edgeMode">The pixels the blur reads beyond the edges of the image.</param>
    /// <param name="workingSpace">The numeric space the samples are filtered in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be blurred.</param>
    /// <exception cref="ArgumentException"><paramref name="sigma"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sigma"/> is negative or exceeds 10000, or <paramref name="edgeMode"/> or <paramref name="workingSpace"/> is not a defined value.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void GaussianBlur(this Image image, double sigma, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, ConvolutionWorkingSpace workingSpace = ConvolutionWorkingSpace.Encoded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        GaussianKernel.Validate(sigma, nameof(sigma));
        ValidateBlur(edgeMode, workingSpace);
        if (sigma == 0)
        {
            image.Owner.EnsureCanModify("blur the image");
            return;
        }

        image.BlurAllFrames(GaussianKernel.Create(sigma), edgeMode, workingSpace, sharpenAmount: 0, sharpenThreshold: 0, cancellationToken);
    }

    /// <summary>
    /// Blurs one frame in place with a Gaussian blur. The EXIF thumbnail of the image, now stale, is removed. See
    /// <see cref="GaussianBlur(Image, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="sigma">The standard deviation of the blur, in pixels. Must be between 0 and 10000.</param>
    /// <param name="edgeMode">The pixels the blur reads beyond the edges of the frame.</param>
    /// <param name="workingSpace">The numeric space the samples are filtered in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be blurred.</param>
    /// <exception cref="ArgumentException"><paramref name="sigma"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sigma"/> is negative or exceeds 10000, or <paramref name="edgeMode"/> or <paramref name="workingSpace"/> is not a defined value.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static void GaussianBlur(this ImageFrame frame, double sigma, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, ConvolutionWorkingSpace workingSpace = ConvolutionWorkingSpace.Encoded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        GaussianKernel.Validate(sigma, nameof(sigma));
        ValidateBlur(edgeMode, workingSpace);
        var storage = frame.GetStorage();
        if (sigma == 0)
            return;

        frame.OwnerImage.BlurFrame(frame, storage, GaussianKernel.Create(sigma), edgeMode, workingSpace, sharpenAmount: 0, sharpenThreshold: 0, cancellationToken);
    }

    /// <summary>Blurs every displayed frame and the poster frame in place with a box blur. The EXIF thumbnail, now stale, is removed.</summary>
    /// <remarks>
    /// <para>
    /// Each sample becomes the mean of the same sample of the <c>(2 radius + 1)^2</c> pixels of the square centered on its
    /// pixel: the convolution by a matrix whose weights are all <c>1 / (2 radius + 1)^2</c>, which is what
    /// <c>cv2.blur</c> and <c>cv2.boxFilter</c> of OpenCV compute. Pixels read beyond the edges are chosen by
    /// <paramref name="edgeMode"/>.
    /// </para>
    /// <para>
    /// The time per pixel does not depend on the radius: the sums of the rows of the square, then of its columns, are
    /// computed by blocks of <c>2 radius + 1</c> values, three additions per sample whatever their number, without
    /// subtractions (a sample that is not a number or an infinity only reaches the pixels whose square contains it), and
    /// the sum of the square is divided once and rounded once at the storage precision. It follows the rules of a
    /// convolution (colors filtered premultiplied by alpha, the same result whatever the number of workers); an opaque
    /// frame is blurred as its colors alone and keeps the maximum alpha, except with
    /// <see cref="ConvolutionEdgeMode.Zero"/>, which reads transparent pixels beyond the frame. The scratch storage
    /// (about three times the radius rows of double-precision samples per worker) is charged to the image allocation
    /// scope. With a radius of 0, nothing changes.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="radius">The number of pixels averaged on each side of a pixel. Must be between 0 and 10000.</param>
    /// <param name="edgeMode">The pixels the blur reads beyond the edges of the image.</param>
    /// <param name="workingSpace">The numeric space the samples are filtered in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be blurred.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 10000, or <paramref name="edgeMode"/> or <paramref name="workingSpace"/> is not a defined value.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void BoxBlur(this Image image, int radius, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, ConvolutionWorkingSpace workingSpace = ConvolutionWorkingSpace.Encoded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateRadius(radius, MaxBoxBlurRadius);
        ValidateBlur(edgeMode, workingSpace);
        if (radius == 0)
        {
            image.Owner.EnsureCanModify("blur the image");
            return;
        }

        image.BoxBlurAllFrames(radius, edgeMode, workingSpace, cancellationToken);
    }

    /// <summary>
    /// Blurs one frame in place with a box blur. The EXIF thumbnail of the image, now stale, is removed. See
    /// <see cref="BoxBlur(Image, int, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="radius">The number of pixels averaged on each side of a pixel. Must be between 0 and 10000.</param>
    /// <param name="edgeMode">The pixels the blur reads beyond the edges of the frame.</param>
    /// <param name="workingSpace">The numeric space the samples are filtered in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be blurred.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 10000, or <paramref name="edgeMode"/> or <paramref name="workingSpace"/> is not a defined value.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static void BoxBlur(this ImageFrame frame, int radius, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, ConvolutionWorkingSpace workingSpace = ConvolutionWorkingSpace.Encoded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateRadius(radius, MaxBoxBlurRadius);
        ValidateBlur(edgeMode, workingSpace);
        var storage = frame.GetStorage();
        if (radius == 0)
            return;

        frame.OwnerImage.BoxBlurFrame(frame, storage, radius, edgeMode, workingSpace, cancellationToken);
    }

    /// <summary>Sharpens every displayed frame and the poster frame in place with an unsharp mask. The EXIF thumbnail, now stale, is removed.</summary>
    /// <remarks>
    /// <para>
    /// The image is compared with its Gaussian blur of standard deviation <paramref name="sigma"/> (the blur of
    /// <see cref="GaussianBlur(Image, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>) and
    /// their difference is added back, multiplied by <paramref name="amount"/>: every sample <c>o</c> whose blur is
    /// <c>b</c> becomes <c>o + amount (o - b)</c>, which raises the contrast of the details smaller than the blur. A sample
    /// whose difference <c>|o - b|</c> is less than <paramref name="threshold"/> times the range of the sample keeps its
    /// value, so that flat areas and their noise are left alone. The default amount of 1 with no threshold is the
    /// classic Gaussian sharpen; <c>-unsharp 0x{sigma}+{amount}+0</c> of ImageMagick computes the same.
    /// </para>
    /// <para>
    /// The sharpened samples are those of the blur: colors premultiplied by alpha and alpha sharpened too, in double
    /// precision, rounded once at the storage precision and clamped to the sample range (ringing beyond black and white is
    /// clipped, except for the colors of floating-point samples). An opaque frame is sharpened as its colors alone and keeps
    /// the maximum alpha, except with <see cref="ConvolutionEdgeMode.Zero"/>. The threshold is compared with the samples as
    /// they are filtered: premultiplied colors range up to the maximum sample squared, and linear light from 0 to 1 (times
    /// alpha). Without deviation or amount, nothing changes.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="sigma">The standard deviation of the blur, in pixels. Must be between 0 and 10000.</param>
    /// <param name="amount">The multiple of the difference from the blur that is added. Must be a finite number, not negative.</param>
    /// <param name="threshold">The smallest difference from the blur that is sharpened, a fraction of the sample range. Must be between 0 and 1.</param>
    /// <param name="edgeMode">The pixels the blur reads beyond the edges of the image.</param>
    /// <param name="workingSpace">The numeric space the samples are filtered in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be sharpened.</param>
    /// <exception cref="ArgumentException"><paramref name="sigma"/>, <paramref name="amount"/> or <paramref name="threshold"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sigma"/> is negative or exceeds 10000, <paramref name="amount"/> is negative, <paramref name="threshold"/> is outside [0, 1], or <paramref name="edgeMode"/> or <paramref name="workingSpace"/> is not a defined value.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Sharpen(this Image image, double sigma, double amount = 1, double threshold = 0, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, ConvolutionWorkingSpace workingSpace = ConvolutionWorkingSpace.Encoded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateSharpen(sigma, amount, threshold);
        ValidateBlur(edgeMode, workingSpace);
        if (sigma == 0 || amount == 0)
        {
            image.Owner.EnsureCanModify("sharpen the image");
            return;
        }

        image.BlurAllFrames(GaussianKernel.Create(sigma), edgeMode, workingSpace, amount, threshold, cancellationToken);
    }

    /// <summary>
    /// Sharpens one frame in place with an unsharp mask. The EXIF thumbnail of the image, now stale, is removed. See
    /// <see cref="Sharpen(Image, double, double, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="sigma">The standard deviation of the blur, in pixels. Must be between 0 and 10000.</param>
    /// <param name="amount">The multiple of the difference from the blur that is added. Must be a finite number, not negative.</param>
    /// <param name="threshold">The smallest difference from the blur that is sharpened, a fraction of the sample range. Must be between 0 and 1.</param>
    /// <param name="edgeMode">The pixels the blur reads beyond the edges of the frame.</param>
    /// <param name="workingSpace">The numeric space the samples are filtered in.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be sharpened.</param>
    /// <exception cref="ArgumentException"><paramref name="sigma"/>, <paramref name="amount"/> or <paramref name="threshold"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sigma"/> is negative or exceeds 10000, <paramref name="amount"/> is negative, <paramref name="threshold"/> is outside [0, 1], or <paramref name="edgeMode"/> or <paramref name="workingSpace"/> is not a defined value.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static void Sharpen(this ImageFrame frame, double sigma, double amount = 1, double threshold = 0, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, ConvolutionWorkingSpace workingSpace = ConvolutionWorkingSpace.Encoded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateSharpen(sigma, amount, threshold);
        ValidateBlur(edgeMode, workingSpace);
        var storage = frame.GetStorage();
        if (sigma == 0 || amount == 0)
            return;

        frame.OwnerImage.BlurFrame(frame, storage, GaussianKernel.Create(sigma), edgeMode, workingSpace, amount, threshold, cancellationToken);
    }

    /// <summary>
    /// Replaces every sample of every displayed frame and the poster frame by the median of its neighborhood, in place. The
    /// EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each sample becomes the median of the same sample of the <c>(2 radius + 1)^2</c> pixels of the square centered on its
    /// pixel: the middle value once they are sorted, one of them. Isolated dots (salt-and-pepper noise) disappear while
    /// edges stay sharp. Every sample of a pixel is filtered on its own, alpha included, as stored: the result is the one of
    /// <c>cv2.medianBlur</c> of OpenCV and of <c>-statistic Median</c> of ImageMagick. Pixels read beyond the edges are
    /// chosen by <paramref name="edgeMode"/> (<see cref="ConvolutionEdgeMode.Zero"/> reads transparent black).
    /// </para>
    /// <para>
    /// Nothing is computed or rounded, so 16-bit samples keep their precision, and there is no working space: a transfer
    /// function keeps the order of the samples, so their median in linear light is the same sample. Floating-point samples
    /// are ordered as numbers, -0 below +0, and every sample that is not a number above the positive infinity (it comes
    /// back with a positive sign). Samples of up to 16 bits (the integer formats and <see cref="RgbaHalf"/>) are counted in
    /// histograms, and the time per pixel grows with the radius; the samples of <see cref="RgbaFloat"/> are sorted for every
    /// pixel, and the time grows with the square of the radius. The result does not depend on the number of workers. With
    /// a radius of 0, nothing changes.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="radius">The number of pixels on each side of a pixel. Must be between 0 and 1000.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 1000, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void MedianBlur(this Image image, int radius, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateRadius(radius, MaxMedianRadius);
        ValidateBlur(edgeMode, ConvolutionWorkingSpace.Encoded);
        if (radius == 0)
        {
            image.Owner.EnsureCanModify("filter the image");
            return;
        }

        image.MedianAllFrames(radius, edgeMode, cancellationToken);
    }

    /// <summary>
    /// Replaces every sample of one frame by the median of its neighborhood, in place. The EXIF thumbnail of the image, now
    /// stale, is removed. See <see cref="MedianBlur(Image, int, ConvolutionEdgeMode, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="radius">The number of pixels on each side of a pixel. Must be between 0 and 1000.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 1000, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch storage would exceed the allocation limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static void MedianBlur(this ImageFrame frame, int radius, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateRadius(radius, MaxMedianRadius);
        ValidateBlur(edgeMode, ConvolutionWorkingSpace.Encoded);
        var storage = frame.GetStorage();
        if (radius == 0)
            return;

        frame.OwnerImage.MedianFrame(frame, storage, radius, edgeMode, cancellationToken);
    }

    private static TransformOptions ValidateTransformOptions(TransformOptions? options)
    {
        options ??= TransformOptions.Default;
        if (options.CanvasMode == TransformCanvasMode.Explicit && options.CanvasSize.IsEmpty)
            throw new ArgumentException("TransformCanvasMode.Explicit requires a canvas size that is not empty (TransformOptions.CanvasSize).", nameof(options));

        return options;
    }

    private static void ValidateRadius(int radius, int max)
    {
        if (radius < 0 || radius > max)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, string.Create(CultureInfo.InvariantCulture, $"The radius must be between 0 and {max}."));
    }

    private static void ValidateSharpen(double sigma, double amount, double threshold)
    {
        GaussianKernel.Validate(sigma, nameof(sigma));
        if (!double.IsFinite(amount))
            throw new ArgumentException("The amount must be a finite number.", nameof(amount));

        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must not be negative.");

        if (!double.IsFinite(threshold))
            throw new ArgumentException("The threshold must be a finite number.", nameof(threshold));

        if (threshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "The threshold must be between 0 and 1.");
    }

    private static void ValidateBlur(ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace)
    {
        if (!Enum.IsDefined(edgeMode))
            throw new ArgumentOutOfRangeException(nameof(edgeMode), edgeMode, "The edge mode is not valid.");

        if (!Enum.IsDefined(workingSpace))
            throw new ArgumentOutOfRangeException(nameof(workingSpace), workingSpace, "The working space is not valid.");
    }
}
