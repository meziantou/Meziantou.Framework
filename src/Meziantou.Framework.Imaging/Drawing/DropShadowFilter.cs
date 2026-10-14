using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A drop shadow: <c>drop-shadow()</c> of CSS, <c>feDropShadow</c> of SVG, the shadow of the HTML canvas.</summary>
/// <remarks>
/// The shadow is the alpha of what is filtered, moved by <see cref="Offset"/> (between pixels, the four around are
/// interpolated), blurred by a Gaussian blur of standard deviation <see cref="Sigma"/> (transparent beyond the
/// edges), and painted with <see cref="Color"/>; what is filtered is then drawn over it, unless
/// <see cref="ShadowOnly"/> is set.
/// </remarks>
public sealed class DropShadowFilter : Filter
{
    /// <summary>Initializes a new instance of the <see cref="DropShadowFilter"/> class.</summary>
    /// <param name="color">The color of the shadow, with straight alpha. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <param name="offset">The distance from what is filtered to its shadow, in units of the drawing.</param>
    /// <param name="sigma">The standard deviation of the blur of the shadow, in units of the drawing: what CSS calls a blur radius is twice that. Must be between 0 and 10000.</param>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="offset"/>, or <paramref name="sigma"/>, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="offset"/> exceeds 16777216, or <paramref name="sigma"/> is negative or exceeds 10000.</exception>
    public DropShadowFilter(Rgba64 color, PointD offset, double sigma)
    {
        DrawingGuard.Point(offset, nameof(offset));
        GaussianKernel.Validate(sigma, nameof(sigma));
        Color = color;
        Offset = offset;
        Sigma = sigma;
    }

    /// <summary>Gets the color of the shadow.</summary>
    public Rgba64 Color { get; }

    /// <summary>Gets the distance from what is filtered to its shadow.</summary>
    public PointD Offset { get; }

    /// <summary>Gets the standard deviation of the blur of the shadow.</summary>
    public double Sigma { get; }

    /// <summary>Gets a value indicating whether the result is the shadow alone, without what casts it: the shadow of a drawing of the HTML canvas, drawn before the drawing.</summary>
    public bool ShadowOnly { get; init; }

    internal override void Apply(FilterProcessor processor, FilterSurface surface) => processor.DropShadow(surface, Color, Offset, Sigma, ShadowOnly);

    internal override (double X, double Y) GetReach() => (GaussianKernel.GetRadius(Sigma) + Math.Abs(Offset.X), GaussianKernel.GetRadius(Sigma) + Math.Abs(Offset.Y));
}
