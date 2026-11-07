namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>.</summary>
/// <remarks>
/// <para>
/// Every sample of a pixel is replaced by the weighted sum of the same sample of its neighbors, with the weights of
/// <see cref="Kernel"/>. Sums are computed in double precision at the storage precision of the image (16-bit pixel
/// formats are filtered with 16-bit precision), plus <see cref="Bias"/> for the colors, rounded to nearest with ties upward
/// and clamped to the sample range.
/// </para>
/// <para>
/// By default the filter is alpha-aware: colors are premultiplied by alpha while filtering and unpremultiplied afterward,
/// alpha is filtered like the colors, and pixels whose resulting alpha is zero become transparent black. A kernel whose
/// weights add up to zero (edge detection) therefore makes every pixel transparent: set <see cref="PreserveAlpha"/> for
/// such kernels.
/// </para>
/// </remarks>
public sealed class ConvolutionOptions
{
    /// <summary>Initializes a new instance of the <see cref="ConvolutionOptions"/> class.</summary>
    /// <param name="kernel">The convolution matrix.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kernel"/> is <see langword="null"/>.</exception>
    public ConvolutionOptions(ConvolutionKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        Kernel = kernel;
    }

    /// <summary>Gets the convolution matrix.</summary>
    public ConvolutionKernel Kernel { get; }

    /// <summary>Gets the pixels read outside the image. Defaults to <see cref="ConvolutionEdgeMode.Clamp"/>.</summary>
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
    /// Gets a value indicating whether alpha is kept unchanged. Defaults to <see langword="false"/>: colors are filtered
    /// premultiplied by alpha and alpha is filtered too. When <see langword="true"/>, colors are filtered as stored (the
    /// hidden colors of transparent pixels contribute) and the alpha of every pixel is left untouched. It has no effect on
    /// pixel formats without alpha.
    /// </summary>
    public bool PreserveAlpha { get; init; }

    /// <summary>
    /// Gets the value added to every color sum before it is rounded, a fraction of the range of a color: 0.5 adds half of
    /// the maximum sample (128 of 255 after rounding, 0.5 for floating-point samples, half the light in
    /// <see cref="ConvolutionWorkingSpace.LinearSrgb"/>). Defaults to 0. It shows the negative sums of a matrix whose
    /// weights add up to zero (edge detection, embossing) instead of clamping them to 0: flat areas become mid-gray. Alpha
    /// never gets it; with the alpha-aware filter, it is added to the colors once they are divided by the filtered alpha.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double Bias
    {
        get;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The bias must be a finite number.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets the numeric space used for filtering. Defaults to <see cref="ConvolutionWorkingSpace.Encoded"/>.</summary>
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
