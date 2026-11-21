namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="ImageProcessingExtensions.WienerDeconvolve(Image, WienerDeconvolutionOptions, CancellationToken)"/>.</summary>
/// <remarks>
/// <para>
/// The deconvolution undoes the blur that
/// <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/> applies with
/// <see cref="Kernel"/> and the default anchor (the middle weight, column <c>Width / 2</c> and row <c>Height / 2</c>): the
/// point spread function of the blur. Use <see cref="ConvolutionKernel.CreateGaussianBlur"/> for an out-of-focus or soft
/// image, <see cref="ConvolutionKernel.CreateMotionBlur"/> for the trail of a camera that moved, or a measured kernel.
/// </para>
/// <para>
/// <see cref="NoiseToSignalRatio"/> sets how much the details the blur weakened are amplified back: the frequencies where
/// the squared gain of the blur is much larger than it are restored, those where it is much smaller (fine detail the blur
/// erased, and the noise there) are left attenuated.
/// </para>
/// </remarks>
public sealed class WienerDeconvolutionOptions
{
    /// <summary>The smallest value of <see cref="NoiseToSignalRatio"/>: the gain of a frequency is at most 500,000.</summary>
    public const double MinNoiseToSignalRatio = 1e-12;

    /// <summary>Initializes a new instance of the <see cref="WienerDeconvolutionOptions"/> class.</summary>
    /// <param name="kernel">The kernel of the blur to undo (see <see cref="Kernel"/>).</param>
    /// <param name="noiseToSignalRatio">The ratio of the power of the noise to that of the image (see <see cref="NoiseToSignalRatio"/>): finite, at least <see cref="MinNoiseToSignalRatio"/>. 0.001 to 0.01 suits a photograph with a little noise.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kernel"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="noiseToSignalRatio"/> is not finite or is less than <see cref="MinNoiseToSignalRatio"/>.</exception>
    public WienerDeconvolutionOptions(ConvolutionKernel kernel, double noiseToSignalRatio)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (!(noiseToSignalRatio >= MinNoiseToSignalRatio && double.IsFinite(noiseToSignalRatio)))
            throw new ArgumentOutOfRangeException(nameof(noiseToSignalRatio), noiseToSignalRatio, "The noise-to-signal ratio must be finite and at least 1e-12.");

        Kernel = kernel;
        NoiseToSignalRatio = noiseToSignalRatio;
    }

    /// <summary>
    /// Gets the kernel of the blur to undo, applied as <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>
    /// applies it: not flipped, its middle weight on the pixel itself, and not normalized (a kernel whose weights add up to 1
    /// keeps the overall brightness).
    /// </summary>
    public ConvolutionKernel Kernel { get; }

    /// <summary>
    /// Gets the ratio <c>K</c> of the power of the noise to that of the image. Every frequency <c>u</c> of the frame is
    /// multiplied by <c>conj(H(u)) / (|H(u)|² + K)</c>, where <c>H(u)</c> is the gain of the blur at that frequency (1 for
    /// the mean of a kernel whose weights add up to 1). A smaller value restores more detail, and amplifies the noise and
    /// the ringing near edges more; a larger one gives a softer result.
    /// </summary>
    public double NoiseToSignalRatio { get; }

    /// <summary>
    /// Gets the pixels the blur read beyond the edges of the frame. Defaults to <see cref="ConvolutionEdgeMode.Clamp"/>.
    /// <see cref="ConvolutionEdgeMode.Wrap"/> undoes the blur of a tiling image exactly. <see cref="ConvolutionEdgeMode.Clamp"/>
    /// and <see cref="ConvolutionEdgeMode.Mirror"/> extend the blurred frame by the edge mode and join its ends smoothly.
    /// <see cref="ConvolutionEdgeMode.Zero"/> surrounds the blurred frame by transparent black too: it suits content whose
    /// blur lies inside the frame (a sprite on a transparent canvas), not an image cut out of a larger scene.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ConvolutionEdgeMode"/>.</exception>
    public ConvolutionEdgeMode EdgeMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The edge mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the numeric space in which the samples are deconvolved. Defaults to <see cref="ConvolutionWorkingSpace.Encoded"/>;
    /// <see cref="ConvolutionWorkingSpace.LinearSrgb"/> undoes a blur of light, as the optics of a camera blur it, with the
    /// rules of the convolution.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ConvolutionWorkingSpace"/>.</exception>
    public ConvolutionWorkingSpace WorkingSpace
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The working space is not valid.");

            field = value;
        }
    }
}
