using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that draws its input over its shadow: <c>feDropShadow</c> of SVG.</summary>
/// <remarks>
/// The shadow is the one of <see cref="DropShadowFilter"/>, with a standard deviation per axis: the alpha of the input,
/// moved by <see cref="Offset"/>, blurred (transparent beyond the subregion of the input), painted with
/// <see cref="Color"/> in the colors of the primitive; the input is drawn over it.
/// </remarks>
public sealed class DropShadowPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="DropShadowPrimitive"/> class.</summary>
    /// <param name="color">The color of the shadow, with straight alpha: <c>flood-color</c> and <c>flood-opacity</c>. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="offset">The distance from the input to its shadow, in units of the drawing: <c>dx</c> and <c>dy</c>.</param>
    /// <param name="sigmaX">The standard deviation of the blur along x, in units of the drawing. Must be between 0 and 10000.</param>
    /// <param name="sigmaY">The standard deviation of the blur along y, in units of the drawing. Must be between 0 and 10000.</param>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="offset"/>, or a standard deviation, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="offset"/> exceeds 16777216, or a standard deviation is negative or exceeds 10000.</exception>
    public DropShadowPrimitive(Rgba64 color, PointD offset, double sigmaX, double sigmaY)
    {
        DrawingGuard.Point(offset, nameof(offset));
        GaussianKernel.Validate(sigmaX, nameof(sigmaX));
        GaussianKernel.Validate(sigmaY, nameof(sigmaY));
        Color = color;
        Offset = offset;
        SigmaX = sigmaX;
        SigmaY = sigmaY;
    }

    /// <summary>Gets the color of the shadow.</summary>
    public Rgba64 Color { get; }

    /// <summary>Gets the distance from the input to its shadow.</summary>
    public PointD Offset { get; }

    /// <summary>Gets the standard deviation of the blur along x.</summary>
    public double SigmaX { get; }

    /// <summary>Gets the standard deviation of the blur along y.</summary>
    public double SigmaY { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override (double X, double Y) GetReach() => (GaussianKernel.GetRadius(SigmaX) + Math.Abs(Offset.X), GaussianKernel.GetRadius(SigmaY) + Math.Abs(Offset.Y));

    // The shadow is blurred with the edge mode of every filter, or transparent
    internal override bool DependsOnSurfaceOrigin(FilterEdgeMode? edgeOverride) => edgeOverride is { } mode && mode != FilterEdgeMode.Transparent;

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        context.GetInput(0).Values.CopyTo(result.Values);
        var (red, green, blue, alpha) = context.GetColor(Color);
        var processor = context.Processor;
        var edges = processor.Edges;
        processor.Edges = context.GetInputSubregion(0);
        try
        {
            processor.DropShadow(result, red, green, blue, alpha, Offset, SigmaX, SigmaY, shadowOnly: false);
        }
        finally
        {
            processor.Edges = edges;
        }

        FilterGraphContext.ClearOutside(result, subregion);
    }
}
