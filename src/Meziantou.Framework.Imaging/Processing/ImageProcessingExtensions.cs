using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>In-place processing operations on images and frames.</summary>
/// <remarks>
/// <para>
/// Image-level operations apply to every displayed frame and to the poster frame. Geometry-changing operations
/// (<see cref="Crop"/>, <see cref="AutoCrop(Image, AutoCropOptions?, CancellationToken)"/>, <see cref="Resize"/>,
/// <see cref="Rotate"/>, <see cref="AutoOrient"/>) build all replacement buffers and metadata before committing them
/// together: on failure or cancellation before the commit, the image is left exactly as it was (dimensions, pixels, frame
/// identities and order, metadata). The old and new buffers are budgeted together. After a successful commit, existing
/// <see cref="ImageFrame"/> references designate the same logical frames.
/// </para>
/// <para>
/// Pixel-only operations (<see cref="Flip(Image, FlipMode, CancellationToken)"/>, <see cref="Grayscale(Image, CancellationToken)"/>,
/// <see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/>) work in place; on failure or cancellation some frames
/// or rows may already be modified, but the image remains structurally valid.
/// </para>
/// <para>
/// Operations work in stored-pixel coordinates. Except for <see cref="AutoOrient"/>, they keep
/// <see cref="ImageMetadata.Orientation"/> unchanged (the typed orientation stays authoritative when EXIF is serialized).
/// Geometry changes update the existing EXIF pixel-dimension tags of <see cref="ImageMetadata.ExifProfile"/> to the new
/// canvas, and every operation that changes pixels removes the EXIF thumbnail, which no longer matches them. A malformed
/// EXIF profile is left as is (serializing it fails).
/// </para>
/// <para>
/// Crop, auto-crop, rotations, mirrors and auto-orient are exact pixel permutations: every sample, including 16-bit low
/// bits and alpha, is copied unchanged (an auto-crop that enlarges the canvas also writes the detected background color
/// around the copied pixels). Operations run on the calling thread, except that <see cref="Resize"/> and
/// <see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/> may also use up to
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers for large frames (identical results).
/// </para>
/// </remarks>
public static class ImageProcessingExtensions
{
    /// <summary>Crops every frame of the image to a rectangle.</summary>
    /// <param name="image">The image to modify.</param>
    /// <param name="rectangle">The region to keep. It must be non-empty and entirely inside the canvas; it is never clamped.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rectangle"/> is empty or not entirely inside the canvas.</exception>
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
    /// <exception cref="UnsupportedImageFeatureException">The canvas must be enlarged but <see cref="AutoCropAnalysis.BackgroundColor"/> is not exactly representable in the pixel format of the image (it comes from an image of another pixel format). The image is unchanged.</exception>
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
    /// Colors are filtered premultiplied by alpha and unpremultiplied afterward; a pixel whose resulting alpha is zero is
    /// transparent black. Samples are filtered in double precision and written at the storage precision (16-bit samples
    /// never go through 8 bits), rounded to nearest with ties upward and clamped. Nearest neighbor copies source pixels
    /// exactly. The scratch storage is charged to the image allocation scope together with the original and replacement
    /// storages.
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
    /// <exception cref="ArgumentException">Upscaling is required but <see cref="ResizeOptions.AllowUpscaling"/> is <see langword="false"/> (for <see cref="ResizeMode.Stretch"/> and <see cref="ResizeMode.Cover"/>).</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ResizeWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile.</exception>
    /// <exception cref="ImageResourceLimitException">The replacement buffers would exceed the allocation limit. The image is unchanged.</exception>
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
        using var lease = storage.AcquireLease();
        frame.OwnerImage.Metadata.RemoveStaleThumbnail();
        frame.FlipPixels(lease, mode, cancellationToken);
    }

    /// <summary>
    /// Converts every frame of the image (including the poster frame) to grayscale in place, keeping the pixel format and
    /// alpha. Uses Rec. 709 luma coefficients (0.2126, 0.7152, 0.0722) on the encoded sample values, nearest rounding with
    /// ties upward, and clamping, at the storage precision: <c>Y = (2126 R + 7152 G + 722 B + 5000) / 10000</c>, stored
    /// into R, G and B. Gray formats are left unchanged. The pixel format is kept, so a compatible RGB profile stays. Use <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> to obtain
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
    /// The image is rewritten in place; the scratch storage (a few rows per worker, proportional to the kernel height) is
    /// charged to the image allocation scope and rented before any pixel changes. When the image configuration allows
    /// several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>), the rows of a large frame are split into
    /// bands convolved concurrently, each with its own scratch; the result is identical to the single-worker result.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The convolution options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be convolved.</param>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile. Nothing is changed.</exception>
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
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile. Nothing is changed.</exception>
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
}
