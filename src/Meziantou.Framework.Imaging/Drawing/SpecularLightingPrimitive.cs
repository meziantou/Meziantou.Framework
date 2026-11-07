using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that lights the surface whose height is the alpha of its input, as a shiny surface: <c>feSpecularLighting</c> of SVG.</summary>
/// <remarks>
/// Each pixel of the result is <c>ks (N.H)^exponent C</c>: <c>ks</c> the <see cref="SpecularConstant"/>, <c>N</c> the
/// normal of the surface (from the alpha of the input, scaled by <see cref="SurfaceScale"/>), <c>H</c> the unit vector
/// half way between the light and the eye <c>(0, 0, 1)</c>, and <c>C</c> the color of the light in the colors of the
/// primitive, each component clamped to the range from 0 to 1; its alpha is the largest of its components. It is meant
/// to be added to what it lights (<see cref="FilterCompositeOperator.Arithmetic"/>).
/// </remarks>
public sealed class SpecularLightingPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SpecularLightingPrimitive"/> class.</summary>
    /// <param name="light">The light.</param>
    /// <param name="color">The color of the light: <c>lighting-color</c>. Its alpha is not used. An <see cref="Rgba32"/> converts implicitly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="light"/> is <see langword="null"/>.</exception>
    public SpecularLightingPrimitive(FilterLightSource light, Rgba64 color)
    {
        ArgumentNullException.ThrowIfNull(light);
        Light = light;
        Color = color;
        SurfaceScale = 1;
        SpecularConstant = 1;
        SpecularExponent = 1;
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

    /// <summary>Gets the factor of the light: <c>specularConstant</c>. Defaults to 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 16777216.</exception>
    public double SpecularConstant
    {
        get;
        init
        {
            DrawingGuard.Length(value, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets how shiny the surface is: <c>specularExponent</c>, from 1 to 128. Defaults to 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or more than 128.</exception>
    public double SpecularExponent
    {
        get;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The exponent must be a finite number.", nameof(value));

            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 128);
            field = value;
        }
    }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override (double X, double Y) GetReach() => (1, 1);

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
        => FilterLighting.Evaluate(context, result, subregion, Light, Color, SurfaceScale, isSpecular: true, SpecularConstant, SpecularExponent);
}
