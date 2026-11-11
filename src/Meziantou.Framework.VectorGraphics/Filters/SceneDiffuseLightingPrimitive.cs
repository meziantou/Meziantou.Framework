namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A primitive that lights the surface whose height is the alpha of its input with a matte light:
/// <c>feDiffuseLighting</c> of SVG. The result is opaque.
/// </summary>
public sealed class SceneDiffuseLightingPrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneDiffuseLightingPrimitive"/> class.</summary>
    /// <param name="light">The light.</param>
    /// <param name="color">The color of the light: <c>lighting-color</c>. Its alpha is ignored, as in SVG.</param>
    /// <exception cref="ArgumentNullException"><paramref name="light"/> is <see langword="null"/>.</exception>
    public SceneDiffuseLightingPrimitive(SceneLightSource light, SceneColor color)
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

    /// <summary>Gets the factor of the light: <c>diffuseConstant</c>. The default is 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double DiffuseConstant { get; init => field = FilterValidation.NonNegative(value, nameof(value)); } = 1;

    /// <summary>Gets the input: <c>in</c>.</summary>
    public SceneFilterInput Input { get; init; }

    internal override IEnumerable<SceneFilterInput> GetInputs() => [Input];

    internal override double GetReach() => 1;
}
