namespace Meziantou.Framework.Imaging;

// The Wiener deconvolution, which undoes a known blur
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Undoes a known blur of every displayed frame and the poster frame with a Wiener filter, in place: a soft focus
    /// (<see cref="ConvolutionKernel.CreateGaussianBlur"/>), the trail of a camera that moved
    /// (<see cref="ConvolutionKernel.CreateMotionBlur"/>), or any blur a convolution kernel describes. The EXIF thumbnail,
    /// now stale, is removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blur is modeled as <see cref="Convolve(Image, ConvolutionOptions, CancellationToken)"/> with
    /// <see cref="WienerDeconvolutionOptions.Kernel"/>. Every frequency <c>u</c> of the frame (its two-dimensional discrete
    /// Fourier transform) is multiplied by <c>conj(H(u)) / (|H(u)|² + K)</c>, where <c>H(u)</c> is the gain of the blur at
    /// that frequency and <c>K</c> is <see cref="WienerDeconvolutionOptions.NoiseToSignalRatio"/>: the frequencies the blur
    /// kept are restored, those it erased stay attenuated instead of amplifying the noise. This is the filter MATLAB's
    /// <c>deconvwnr</c> applies with a scalar noise-to-signal ratio.
    /// </para>
    /// <para>
    /// With <see cref="ConvolutionEdgeMode.Wrap"/>, the transform covers the frame exactly, and the result is the exact
    /// Wiener inverse of the convolution with wrapped edges. With the other edge modes, the frame is extended on each side
    /// by the edge mode over the radius of the kernel, the two ends are joined by a linear ramp at least
    /// <c>max(2 radius, 8)</c> pixels long so that no step rings across the frame, and the result is cropped back. Pixels
    /// near the edges are less accurate than inside, as what the blur read beyond them is guessed. With
    /// <see cref="ConvolutionEdgeMode.Zero"/>, the extension is transparent black: the blur of the content must lie inside
    /// the frame. The mean of a frame is multiplied by <c>H(0)² / (H(0)² + K)</c>, 1 / (1 + K) for a kernel whose weights add
    /// up to 1, as with any Wiener filter.
    /// </para>
    /// <para>
    /// As for the convolution, colors are deconvolved premultiplied by alpha and alpha is deconvolved too (an opaque frame
    /// as its colors alone, except with <see cref="ConvolutionEdgeMode.Zero"/>), in double precision, then rounded once and
    /// clamped to the sample range; 16-bit samples keep their precision. Floating-point samples are not clipped (beyond the
    /// range of single precision, they are clamped to it), and a sample that is not finite reads as 0 and keeps its value.
    /// <see cref="WienerDeconvolutionOptions.WorkingSpace"/> can deconvolve linear light. The arithmetic is scalar and the
    /// sines and cosines are computed without the functions of the platform, so the result is the same on every platform.
    /// </para>
    /// <para>
    /// The transforms have lengths whose prime factors are 2, 3 and 5, or the exact size of a wrapped frame (Bluestein's
    /// algorithm for the other lengths). The time grows with <c>N M log(N M)</c> for a plane of <c>N × M</c> pixels, two
    /// working samples per transform, whatever the size of the kernel. The memory is the working copy of the frame (4
    /// bytes per sample), the plane (16 bytes per pixel of the plane) and, for a kernel that is not separable, its transfer
    /// (8 bytes per pixel of the plane), charged to the image and rented before any pixel changes. The frames are
    /// deconvolved on the calling thread.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ConvolutionTests.cs" ref:region="WienerDeconvolve_UndoMotionBlur" ref:dedent="true">
    /// [Fact]
    /// public void WienerDeconvolve_UndoMotionBlur()
    /// {
    ///     // Vertical bars, 8 pixels wide, blurred by a horizontal motion of 9 pixels: the edges become ramps
    ///     using var image = new Image&lt;Gray8&gt;(64, 32);
    ///     for (var y = 0; y &lt; 32; y++)
    ///     {
    ///         for (var x = 0; x &lt; 64; x++)
    ///         {
    ///             image.Frames[0][x, y] = new Gray8((byte)(x / 8 % 2 == 0 ? 50 : 200));
    ///         }
    ///     }
    ///
    ///     var blur = ConvolutionKernel.CreateMotionBlur(length: 9, degrees: 0);
    ///     image.Convolve(new ConvolutionOptions(blur));
    ///     Assert.InRange(image.Frames[0][31, 16].Value, 120, 140);
    ///     Assert.InRange(image.Frames[0][32, 16].Value, 110, 130);
    ///
    ///     // The deconvolution by the same kernel brings the edges back, within the few levels the blur erased for good
    ///     image.WienerDeconvolve(new WienerDeconvolutionOptions(blur, noiseToSignalRatio: 0.0001));
    ///
    ///     Assert.InRange(image.Frames[0][31, 16].Value, 180, 210);
    ///     Assert.InRange(image.Frames[0][32, 16].Value, 40, 70);
    ///     Assert.InRange(image.Frames[0][28, 16].Value, 190, 210);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="options">The blur to undo, the noise-to-signal ratio, the edge mode and the working space.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some frames or rows may already be deconvolved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed a limit. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void WienerDeconvolve(this Image image, WienerDeconvolutionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfCmyk(image.PixelFormat, nameof(WienerDeconvolve));
        ArgumentNullException.ThrowIfNull(options);
        image.WienerDeconvolveFrames(frames: null, options, cancellationToken);
    }

    /// <summary>
    /// Undoes a known blur of one frame with a Wiener filter, in place. The EXIF thumbnail of the image, now stale, is
    /// removed. See <see cref="WienerDeconvolve(Image, WienerDeconvolutionOptions, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="options">The blur to undo, the noise-to-signal ratio, the edge mode and the working space.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, some rows may already be deconvolved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ConvolutionWorkingSpace.LinearSrgb"/> is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light. Nothing is changed.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory would exceed a limit. The frame is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void WienerDeconvolve(this ImageFrame frame, WienerDeconvolutionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfCmyk(frame.PixelFormat, nameof(WienerDeconvolve));
        ArgumentNullException.ThrowIfNull(options);
        _ = frame.GetStorage();
        frame.OwnerImage.WienerDeconvolveFrames([frame], options, cancellationToken);
    }
}
