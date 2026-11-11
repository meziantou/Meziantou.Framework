namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A primitive that lights the surface whose height is the alpha of its input with a shiny light:
/// <c>feSpecularLighting</c> of SVG. The result is the highlights, to add to the drawing.
/// </summary>
public sealed class SceneSpecularLightingPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneSpecularLightingPrimitive"/> class.</summary>
    /// <param name="light">The light.</param>
    /// <param name="color">The color of the light: <c>lighting-color</c>. Its alpha is ignored, as in SVG.</param>
    /// <exception cref="ArgumentNullException"><paramref name="light"/> is <see langword="null"/>.</exception>
    public SceneSpecularLightingPrimitive(SceneLightSource light, SceneColor color)
    {
        ArgumentNullException.ThrowIfNull(light);
        Light = light;
        Color = color;
    }

    /// <summary>Gets the light.</summary>
    public SceneLightSource Light { get; }

    /// <summary>Gets the color of the light.</summary>
    public SceneColor Color { get; }

    /// <summary>Gets the height of the surface where the alpha is 1: <c>surfaceScale</c>. The default is 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double SurfaceScale { get; init => field = FilterValidation.Finite(value, nameof(value)); } = 1;

    /// <summary>Gets the factor of the light: <c>specularConstant</c>. The default is 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double SpecularConstant { get; init => field = FilterValidation.NonNegative(value, nameof(value)); } = 1;

    /// <summary>Gets how shiny the surface is: <c>specularExponent</c>, from 1 to 128. The default is 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or more than 128.</exception>
    public double SpecularExponent
    {
        get;
        init
        {
            FilterValidation.Finite(value, nameof(value));
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 128);
            field = value;
        }
    } = 1;

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];

    internal override double GetReach() => 1;
}
