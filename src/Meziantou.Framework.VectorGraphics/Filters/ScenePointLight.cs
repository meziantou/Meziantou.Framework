namespace Meziantou.Framework.VectorGraphics;

/// <summary>A light at a point, that shines in every direction: <c>fePointLight</c> of SVG.</summary>
public sealed class ScenePointLight : SceneLightSource
{
    /// <summary>Initializes a new instance of the <see cref="ScenePointLight"/> class.</summary>
    /// <param name="x">The x of the light.</param>
    /// <param name="y">The y of the light.</param>
    /// <param name="z">The height of the light above the plane.</param>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    public ScenePointLight(double x, double y, double z)
    {
        X = FilterValidation.Finite(x, nameof(x));
        Y = FilterValidation.Finite(y, nameof(y));
        Z = FilterValidation.Finite(z, nameof(z));
    }

    /// <summary>Gets the x of the light.</summary>
    public double X { get; }

    /// <summary>Gets the y of the light.</summary>
    public double Y { get; }

    /// <summary>Gets the height of the light above the plane.</summary>
    public double Z { get; }
}
