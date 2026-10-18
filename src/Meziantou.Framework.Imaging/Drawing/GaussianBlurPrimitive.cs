using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that blurs its input: <c>feGaussianBlur</c> of SVG.</summary>
/// <remarks>
/// The blur is the one of <see cref="BlurFilter"/>, with a standard deviation per axis. What it reads beyond the
/// subregion of its input is given by <see cref="EdgeMode"/>, relative to that subregion: transparent (<c>none</c>, the
/// default), the nearest pixel (<c>duplicate</c>), the pixel of the opposite side (<c>wrap</c>), or the mirrored pixel.
/// </remarks>
public sealed class GaussianBlurPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="GaussianBlurPrimitive"/> class with the same standard deviation along both axes.</summary>
    /// <param name="sigma">The standard deviation, in units of the drawing. Must be between 0 and 10000.</param>
    /// <exception cref="ArgumentException"><paramref name="sigma"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sigma"/> is negative or exceeds 10000.</exception>
    public GaussianBlurPrimitive(double sigma)
        : this(sigma, sigma)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GaussianBlurPrimitive"/> class with a standard deviation per axis: <c>stdDeviation</c>.</summary>
    /// <param name="sigmaX">The standard deviation along x, in units of the drawing. Must be between 0 and 10000.</param>
    /// <param name="sigmaY">The standard deviation along y, in units of the drawing. Must be between 0 and 10000.</param>
    /// <exception cref="ArgumentException">A standard deviation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A standard deviation is negative or exceeds 10000.</exception>
    public GaussianBlurPrimitive(double sigmaX, double sigmaY)
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

    /// <summary>Gets what the blur reads beyond the subregion of its input: <c>edgeMode</c>. Defaults to <see cref="FilterEdgeMode.Transparent"/>.</summary>
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

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override (double X, double Y) GetReach() => (GaussianKernel.GetRadius(SigmaX), GaussianKernel.GetRadius(SigmaY));

    internal override bool DependsOnSurfaceOrigin(FilterEdgeMode? edgeOverride) => (edgeOverride ?? EdgeMode) != FilterEdgeMode.Transparent;

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        context.GetInput(0).Values.CopyTo(result.Values);
        var processor = context.Processor;
        var edges = processor.Edges;
        processor.Edges = context.GetInputSubregion(0);
        try
        {
            processor.Blur(result, SigmaX, SigmaY, EdgeMode);
        }
        finally
        {
            processor.Edges = edges;
        }

        FilterGraphContext.ClearOutside(result, subregion);
    }
}
