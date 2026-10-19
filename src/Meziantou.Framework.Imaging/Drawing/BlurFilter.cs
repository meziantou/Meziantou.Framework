using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A Gaussian blur, of one standard deviation or of one per axis: <c>blur()</c> of CSS, <c>feGaussianBlur</c> of SVG.</summary>
/// <remarks>
/// Below a deviation of 2 pixels, the weights are those of
/// <see cref="ImageProcessingExtensions.GaussianBlur(Image, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>
/// (three standard deviations on each side); from 2 pixels, the blur is three successive box blurs of
/// <c>d = floor(3 sqrt(2 pi) s / 4 + 1/2)</c> pixels, as "Filter Effects Module Level 1" defines it and browsers
/// draw it, whose cost does not grow with the deviation. The rows are blurred, then the columns. What is read beyond
/// the edges of what is filtered is given by <see cref="EdgeMode"/>; a deviation of zero leaves its axis as it is.
/// </remarks>
public sealed class BlurFilter : Filter
{
    /// <summary>Initializes a new instance of the <see cref="BlurFilter"/> class with the same standard deviation along both axes.</summary>
    /// <param name="sigma">The standard deviation, in units of the drawing. Must be between 0 and 10000.</param>
    /// <exception cref="ArgumentException"><paramref name="sigma"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sigma"/> is negative or exceeds 10000.</exception>
    public BlurFilter(double sigma)
        : this(sigma, sigma)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BlurFilter"/> class with a standard deviation per axis.</summary>
    /// <param name="sigmaX">The standard deviation along x, in units of the drawing. Must be between 0 and 10000.</param>
    /// <param name="sigmaY">The standard deviation along y, in units of the drawing. Must be between 0 and 10000.</param>
    /// <exception cref="ArgumentException">A standard deviation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A standard deviation is negative or exceeds 10000.</exception>
    public BlurFilter(double sigmaX, double sigmaY)
    {
        GaussianKernel.Validate(sigmaX, nameof(sigmaX));
        GaussianKernel.Validate(sigmaY, nameof(sigmaY));
        SigmaX = sigmaX;
        SigmaY = sigmaY;
    }

    /// <summary>Gets the standard deviation along x.</summary>
    public double SigmaX { get; }

    /// <summary>Gets the standard deviation along y.</summary>
    public double SigmaY { get; }

    /// <summary>
    /// Gets what the blur reads beyond the edges of what it filters: the edges of the canvas for a drawing, the edges
    /// of the rectangle for a backdrop (which is always mirrored). Defaults to <see cref="FilterEdgeMode.Transparent"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FilterEdgeMode"/>.</exception>
    public FilterEdgeMode EdgeMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The edge mode is not valid.");

            field = value;
        }
    }

    internal override void Apply(FilterProcessor processor, FilterSurface surface) => processor.Blur(surface, SigmaX, SigmaY, EdgeMode);

    internal override (double X, double Y) GetReach() => (GaussianKernel.GetRadius(SigmaX), GaussianKernel.GetRadius(SigmaY));

    // The other edge modes read the pixels of the edges of the canvas, which may be far from what is drawn
    internal override int GetLocalSteps(bool isAfterReach) => EdgeMode == FilterEdgeMode.Transparent ? 1 : -1;
}
