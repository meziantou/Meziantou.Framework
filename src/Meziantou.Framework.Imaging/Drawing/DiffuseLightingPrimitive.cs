using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that lights the surface whose height is the alpha of its input, as a matte surface: <c>feDiffuseLighting</c> of SVG.</summary>
/// <remarks>
/// Each pixel of the result is opaque, of the color <c>kd (N.L) C</c>: <c>kd</c> the <see cref="DiffuseConstant"/>,
/// <c>N</c> the normal of the surface (from the alpha of the input, scaled by <see cref="SurfaceScale"/>), <c>L</c> the
/// unit vector toward the light, and <c>C</c> the color of the light in the colors of the primitive, each component
/// clamped to the range from 0 to 1.
/// </remarks>
public sealed class DiffuseLightingPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="DiffuseLightingPrimitive"/> class.</summary>
    /// <param name="light">The light.</param>
    /// <param name="color">The color of the light: <c>lighting-color</c>. Its alpha is not used. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="light"/> is <see langword="null"/>.</exception>
    public DiffuseLightingPrimitive(FilterLightSource light, Rgba64 color)
    {
        ArgumentNullException.ThrowIfNull(light);
        Light = light;
        Color = color;
        SurfaceScale = 1;
        DiffuseConstant = 1;
    }

    /// <summary>Gets the light.</summary>
    public FilterLightSource Light { get; }

    /// <summary>Gets the color of the light.</summary>
    public Rgba64 Color { get; }

    /// <summary>Gets the height of the surface where the alpha is 1: <c>surfaceScale</c>. Defaults to 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of the value exceeds 16777216.</exception>
    public double SurfaceScale
    {
        get;
        init
        {
            DrawingGuard.Coordinate(value, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets the factor of the light: <c>diffuseConstant</c>. Defaults to 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 16777216.</exception>
    public double DiffuseConstant
    {
        get;
        init
        {
            DrawingGuard.Length(value, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override (double X, double Y) GetReach() => (1, 1);

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
        => FilterLighting.Evaluate(context, result, subregion, Light, Color, SurfaceScale, isSpecular: false, DiffuseConstant, exponent: 1);
}
