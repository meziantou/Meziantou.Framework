namespace Meziantou.Framework.VectorGraphics;

/// <summary>A light infinitely far away, whose rays are parallel: <c>feDistantLight</c> of SVG.</summary>
public sealed class SceneDistantLight : SceneLightSource
{
    /// <summary>Initializes a new instance of the <see cref="SceneDistantLight"/> class.</summary>
    /// <param name="azimuth">The direction of the light in the plane, in degrees clockwise from the x axis: <c>azimuth</c>.</param>
    /// <param name="elevation">The angle of the light above the plane, in degrees: <c>elevation</c>.</param>
    /// <exception cref="ArgumentException">An angle is not a finite number.</exception>
    public SceneDistantLight(double azimuth, double elevation)
    {
        Azimuth = FilterValidation.Finite(azimuth, nameof(azimuth));
        Elevation = FilterValidation.Finite(elevation, nameof(elevation));
    }

    /// <summary>Gets the direction of the light in the plane, in degrees.</summary>
    public double Azimuth { get; }

    /// <summary>Gets the angle of the light above the plane, in degrees.</summary>
    public double Elevation { get; }
}
