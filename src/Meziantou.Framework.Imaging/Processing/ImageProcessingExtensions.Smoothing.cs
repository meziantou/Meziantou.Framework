namespace Meziantou.Framework.Imaging;

// The edge-preserving filters (bilateral, guided) and the radial (zoom and spin) blur
public static partial class ImageProcessingExtensions
{
    private const int MaxBilateralRadius = 100;
    private const int MaxGuidedRadius = 1000;

    /// <summary>
    /// Smooths every displayed frame and the poster frame with a bilateral filter, in place: every pixel becomes a weighted
    /// mean of the pixels around it, where the weight falls with the distance and with the difference of colors, so noise
    /// and texture are smoothed and edges stay sharp. The EXIF thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The neighbors are the pixels of the disk of <paramref name="radius"/> around the pixel (<c>dx² + dy² ≤ radius²</c>).
    /// A neighbor weighs <c>exp(-(dx² + dy²) / (2 spatialSigma²)) × exp(-δ² / (2 rangeSigma²))</c>, where <c>δ</c> is the
    /// difference between the two pixels: the root mean square of the differences of their color samples, as fractions of
    /// the sample range, with the difference of their alphas added in quadrature. A <paramref name="rangeSigma"/> of 0.1
    /// smooths away differences of about 25 levels of 255 and keeps larger ones. The range weights are read from a table
    /// (a relative error below 2e-6), and are 0 for <c>δ</c> beyond 8 <paramref name="rangeSigma"/>.
    /// </para>
    /// <para>
    /// For a gray image this is OpenCV's <c>cv2.bilateralFilter(src, 2 radius + 1, rangeSigma × 255, spatialSigma)</c> (on
    /// 8-bit samples; 65,535 for 16-bit ones, 1 for floating-point ones). For colors, OpenCV adds the absolute differences
    /// of the three samples instead: its range sigma is about <c>rangeSigma × 255 × √3</c> for equal differences. Colors are
    /// filtered premultiplied by alpha and alpha is filtered like them, so the hidden color of a transparent pixel does not
    /// spread. The samples are filtered as stored (gamma-encoded); 16-bit samples keep their precision, and floating-point
    /// samples are not clipped. A sample that is not a number gets no weight in its neighbors, and stays so.
    /// </para>
    /// <para>
    /// The time per pixel grows with the area of the disk. The frames are copied as single-precision working samples (4
    /// bytes per sample), charged to the image and rented before any pixel changes. Frames of at least 65,536 pixels are
    /// split between up to <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers, and the result does not depend on
    /// their number.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/SmoothingTests.cs" ref:region="BilateralFilter_SmoothNoiseKeepEdges" ref:dedent="true">
    /// [Fact]
    /// public void BilateralFilter_SmoothNoiseKeepEdges()
    /// {
    ///     // A dark and a bright half, both with a little noise
    ///     using var image = new Image&lt;Gray8&gt;(32, 16);
    ///     for (var y = 0; y &lt; 16; y++)
    ///     {
    ///         for (var x = 0; x &lt; 32; x++)
    ///         {
    ///             var noise = ((x * 7) + (y * 13)) % 5 * 2;
    ///             image.Frames[0][x, y] = new Gray8((byte)((x &lt; 16 ? 40 : 200) + noise));
    ///         }
    ///     }
    ///
    ///     // Differences of about 10 levels of 255 are smoothed, the step of 160 levels is kept
    ///     image.BilateralFilter(radius: 3, spatialSigma: 2, rangeSigma: 0.1);
    ///
    ///     Assert.InRange(image.Frames[0][5, 8].Value, 42, 46);
    ///     Assert.InRange(image.Frames[0][15, 8].Value, 42, 46);
    ///     Assert.InRange(image.Frames[0][16, 8].Value, 202, 206);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="radius">The radius of the disk of neighbors, in pixels. Must be between 0 and 100; 0 changes nothing.</param>
    /// <param name="spatialSigma">The standard deviation of the spatial weights, in pixels. Must be positive and finite; about half the radius.</param>
    /// <param name="rangeSigma">The standard deviation of the range weights, as a fraction of the sample range. Must be positive and finite.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 100, <paramref name="spatialSigma"/> or <paramref name="rangeSigma"/> is not positive and finite, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The working copy would exceed a limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void BilateralFilter(this Image image, int radius, double spatialSigma, double rangeSigma, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateBilateral(radius, spatialSigma, rangeSigma, edgeMode);
        if (radius == 0)
        {
            image.Owner.EnsureCanModify("filter the image");
            return;
        }

        image.BilateralFilterFrames(frames: null, radius, spatialSigma, rangeSigma, edgeMode, cancellationToken);
    }

    /// <summary>
    /// Smooths one frame with a bilateral filter, in place. The EXIF thumbnail of the image, now stale, is removed. See
    /// <see cref="BilateralFilter(Image, int, double, double, ConvolutionEdgeMode, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="radius">The radius of the disk of neighbors, in pixels. Must be between 0 and 100; 0 changes nothing.</param>
    /// <param name="spatialSigma">The standard deviation of the spatial weights, in pixels. Must be positive and finite.</param>
    /// <param name="rangeSigma">The standard deviation of the range weights, as a fraction of the sample range. Must be positive and finite.</param>
    /// <param name="edgeMode">The pixels the filter reads beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 100, <paramref name="spatialSigma"/> or <paramref name="rangeSigma"/> is not positive and finite, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The working copy would exceed a limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void BilateralFilter(this ImageFrame frame, int radius, double spatialSigma, double rangeSigma, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateBilateral(radius, spatialSigma, rangeSigma, edgeMode);
        _ = frame.GetStorage();
        if (radius == 0)
            return;

        frame.OwnerImage.BilateralFilterFrames([frame], radius, spatialSigma, rangeSigma, edgeMode, cancellationToken);
    }

    /// <summary>
    /// Smooths every displayed frame and the poster frame with a guided filter that uses the frame as its own guide, in
    /// place: an edge-preserving smoothing that takes a constant time per pixel, whatever the radius. The EXIF thumbnail,
    /// now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter of He, Sun and Tang ("Guided Image Filtering", 2010): in every window of <c>(2 radius + 1)²</c> pixels, each
    /// sample is approached by a linear function of the colors of the window, <c>a · I + b</c>, whose slope is held back by
    /// <paramref name="epsilon"/>; every pixel then takes the mean of the functions of the windows that contain it. Where the
    /// colors of a window vary much less than <c>√epsilon</c> (noise, fine texture), the slope is close to 0 and the window
    /// is flattened to its mean; where they vary much more (an edge), the slope is close to 1 and the pixels are kept. The
    /// guide of a color image is its three colors (the color version of the paper, with their 3×3 covariance), that of a
    /// gray image its gray level, both as fractions of the sample range: an <paramref name="epsilon"/> of 0.01 (0.1²) treats
    /// variations of about 25 levels of 255 as noise. The result matches OpenCV's
    /// <c>cv2.ximgproc.guidedFilter(image, image, radius, epsilon × 255²)</c> on 8-bit samples, whose windows reach beyond
    /// the edges like <see cref="ConvolutionEdgeMode.Mirror"/>.
    /// </para>
    /// <para>
    /// Colors are filtered premultiplied by alpha, and alpha is filtered too, guided by the colors. The samples are filtered
    /// as stored (gamma-encoded), in double precision; the result can overshoot a little near edges, so integer samples are
    /// clamped to their range when stored and floating-point colors are not. A sample that is not a number spreads to the
    /// pixels whose windows contain it.
    /// </para>
    /// <para>
    /// The frames are copied as single-precision working samples (4 bytes per sample), and the coefficients of every pixel
    /// are kept in single precision (up to 64 bytes per pixel), both charged to the image with a scratch of
    /// <c>2 radius + 3</c> rows of window sums (up to 25 doubles per pixel), and rented before any pixel changes. The frames
    /// are filtered on the calling thread.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/SmoothingTests.cs" ref:region="GuidedFilter_SmoothNoiseKeepEdges" ref:dedent="true">
    /// [Fact]
    /// public void GuidedFilter_SmoothNoiseKeepEdges()
    /// {
    ///     // A dark and a bright half, both with a little noise
    ///     using var image = new Image&lt;Gray8&gt;(32, 16);
    ///     for (var y = 0; y &lt; 16; y++)
    ///     {
    ///         for (var x = 0; x &lt; 32; x++)
    ///         {
    ///             var noise = ((x * 7) + (y * 13)) % 5 * 2;
    ///             image.Frames[0][x, y] = new Gray8((byte)((x &lt; 16 ? 40 : 200) + noise));
    ///         }
    ///     }
    ///
    ///     // Variations below √0.01 = 0.1 (25 levels of 255) are smoothed, the step of 160 levels is kept
    ///     image.GuidedFilter(radius: 4, epsilon: 0.01);
    ///
    ///     Assert.InRange(image.Frames[0][5, 8].Value, 42, 46);
    ///     Assert.InRange(image.Frames[0][11, 8].Value, 40, 50);
    ///     Assert.InRange(image.Frames[0][20, 8].Value, 200, 208);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="radius">The radius of the windows, in pixels. Must be between 0 and 1000; 0 changes nothing.</param>
    /// <param name="epsilon">The regularization, a squared fraction of the sample range. Must be positive and finite.</param>
    /// <param name="edgeMode">The pixels the windows read beyond the edges of the image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 1000, <paramref name="epsilon"/> is not positive and finite, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed a limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void GuidedFilter(this Image image, int radius, double epsilon, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateGuided(radius, epsilon, edgeMode);
        if (radius == 0)
        {
            image.Owner.EnsureCanModify("filter the image");
            return;
        }

        image.GuidedFilterFrames(frames: null, radius, epsilon, edgeMode, cancellationToken);
    }

    /// <summary>
    /// Smooths one frame with a guided filter that uses the frame as its own guide, in place. The EXIF thumbnail of the
    /// image, now stale, is removed. See <see cref="GuidedFilter(Image, int, double, ConvolutionEdgeMode, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="radius">The radius of the windows, in pixels. Must be between 0 and 1000; 0 changes nothing.</param>
    /// <param name="epsilon">The regularization, a squared fraction of the sample range. Must be positive and finite.</param>
    /// <param name="edgeMode">The pixels the windows read beyond the edges of the frame.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be filtered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 1000, <paramref name="epsilon"/> is not positive and finite, or <paramref name="edgeMode"/> is not a defined value.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed a limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void GuidedFilter(this ImageFrame frame, int radius, double epsilon, ConvolutionEdgeMode edgeMode = ConvolutionEdgeMode.Clamp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateGuided(radius, epsilon, edgeMode);
        _ = frame.GetStorage();
        if (radius == 0)
            return;

        frame.OwnerImage.GuidedFilterFrames([frame], radius, epsilon, edgeMode, cancellationToken);
    }

    /// <summary>
    /// Blurs every displayed frame and the poster frame along lines through a center (a zoom blur) or circles around it (a
    /// spin blur), in place: the streaks of a zoom or a rotation during the exposure, sharp at the center. The EXIF
    /// thumbnail, now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every pixel becomes the mean of the frame sampled along a path centered on it, whose length grows with its distance
    /// to the center (see <see cref="RadialBlurOptions"/>): the samples are evenly spaced, at most one pixel apart up to
    /// <see cref="RadialBlurOptions.MaxSampleCount"/>, and read with bilinear interpolation; the samples beyond the edges are
    /// read with <see cref="RadialBlurOptions.EdgeMode"/>. The time per pixel grows with the length of its path, up to that
    /// count.
    /// </para>
    /// <para>
    /// As for <see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/>, colors are averaged premultiplied by
    /// alpha and alpha is averaged too, 16-bit samples keep their precision, floating-point samples are not clipped, and
    /// <see cref="RadialBlurOptions.WorkingSpace"/> can average linear light. The frames are copied as single-precision
    /// working samples (4 bytes per sample), charged to the image and rented before any pixel changes. Frames of at least
    /// 65,536 pixels are split between up to <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers, and the result
    /// does not depend on their number.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/SmoothingTests.cs" ref:region="RadialBlur_Zoom" ref:dedent="true">
    /// [Fact]
    /// public void RadialBlur_Zoom()
    /// {
    ///     // Vertical stripes, 2 pixels wide
    ///     using var image = new Image&lt;Rgb24&gt;(64, 64);
    ///     for (var y = 0; y &lt; 64; y++)
    ///     {
    ///         for (var x = 0; x &lt; 64; x++)
    ///         {
    ///             image.Frames[0][x, y] = x / 2 % 2 == 0 ? new Rgb24(0, 0, 0) : new Rgb24(255, 255, 255);
    ///         }
    ///     }
    ///
    ///     // A zoom blur toward the center: the center stays sharp, the stripes far away along the horizontal axis blur to gray
    ///     image.RadialBlur(new RadialBlurOptions(RadialBlurKind.Zoom, center: new PointD(34.5, 32.5), amount: 0.5));
    ///
    ///     Assert.Equal(255, image.Frames[0][34, 32].R);
    ///     Assert.InRange(image.Frames[0][10, 32].R, 96, 160);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The kind of blur, its center and its amount.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be blurred.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The working copy would exceed a limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void RadialBlur(this Image image, RadialBlurOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        image.RadialBlurFrames(frames: null, options, cancellationToken);
    }

    /// <summary>
    /// Blurs one frame along lines through a center or circles around it, in place. The EXIF thumbnail of the image, now
    /// stale, is removed. See <see cref="RadialBlur(Image, RadialBlurOptions, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="options">The kind of blur, its center and its amount.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be blurred.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The working copy would exceed a limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void RadialBlur(this ImageFrame frame, RadialBlurOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        _ = frame.GetStorage();
        frame.OwnerImage.RadialBlurFrames([frame], options, cancellationToken);
    }

    private static void ValidateBilateral(int radius, double spatialSigma, double rangeSigma, ConvolutionEdgeMode edgeMode)
    {
        ValidateRadius(radius, MaxBilateralRadius);
        if (!(spatialSigma > 0 && double.IsFinite(spatialSigma)))
            throw new ArgumentOutOfRangeException(nameof(spatialSigma), spatialSigma, "The spatial sigma must be positive and finite.");

        if (!(rangeSigma > 0 && double.IsFinite(rangeSigma)))
            throw new ArgumentOutOfRangeException(nameof(rangeSigma), rangeSigma, "The range sigma must be positive and finite.");

        if (!Enum.IsDefined(edgeMode))
            throw new ArgumentOutOfRangeException(nameof(edgeMode), edgeMode, "The edge mode is not valid.");
    }

    private static void ValidateGuided(int radius, double epsilon, ConvolutionEdgeMode edgeMode)
    {
        ValidateRadius(radius, MaxGuidedRadius);
        if (!(epsilon > 0 && double.IsFinite(epsilon)))
            throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon, "The regularization must be positive and finite.");

        if (!Enum.IsDefined(edgeMode))
            throw new ArgumentOutOfRangeException(nameof(edgeMode), edgeMode, "The edge mode is not valid.");
    }
}
