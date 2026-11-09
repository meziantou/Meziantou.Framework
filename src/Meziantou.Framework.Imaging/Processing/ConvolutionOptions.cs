namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>.</summary>
/// <remarks>
/// <para>
/// Every sample of a pixel is replaced by the weighted sum of the same sample of its neighbors, with the weights of
/// <see cref="Kernel"/>, the pixel itself taking the weight at <see cref="Anchor"/>. Sums are computed in double precision
/// at the storage precision of the image (16-bit pixel formats are filtered with 16-bit precision), divided by
/// <see cref="Divisor"/>, plus <see cref="Bias"/> for the colors, rounded to nearest with ties upward and clamped to the
/// sample range: the result of SVG's <c>feConvolveMatrix</c>, <c>sum / divisor + bias</c>.
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

    /// <summary>
    /// Gets the weight of <see cref="Kernel"/> applied to the pixel being computed, as a column and a row of the matrix:
    /// the weight at column <c>x</c> and row <c>y</c> reads the pixel <c>x - Anchor.X</c> columns to the right and
    /// <c>y - Anchor.Y</c> rows below. Defaults to <see langword="null"/>: the column <c>Width / 2</c> and the row
    /// <c>Height / 2</c>, rounded down (the middle weight of odd dimensions, and the default of SVG's <c>targetX</c> and
    /// <c>targetY</c> and of OpenCV's <c>anchor</c> for even ones).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The column or the row is outside the matrix.</exception>
    public Point? Anchor
    {
        get;
        init
        {
            if (value is { } anchor && ((uint)anchor.X >= (uint)Kernel.Width || (uint)anchor.Y >= (uint)Kernel.Height))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The anchor must be a column and a row of the kernel.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the number every sum is divided by, before <see cref="Bias"/> is added: each weight of the matrix is divided by
    /// it. Defaults to 1. Set it to the sum of the weights to keep the overall brightness with a matrix of integer weights,
    /// as the <c>divisor</c> of SVG's <c>feConvolveMatrix</c>. Alpha is divided too.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number, or is zero.</exception>
    public double Divisor
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value == 0)
                throw new ArgumentException("The divisor must be a finite number other than zero.", nameof(value));

            field = value;
        }
    } = 1;

    /// <summary>Gets the matrix applied with the anchor in its middle and the divisor applied to its weights.</summary>
    internal ConvolutionKernel GetEffectiveKernel()
    {
        var anchor = Anchor ?? new Point(Kernel.Width / 2, Kernel.Height / 2);
        return Kernel.GetCentered(anchor.X, anchor.Y, Divisor);
    }

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
