namespace Meziantou.Framework.VectorGraphics;

/// <summary>A light at a point, that shines toward another point: <c>feSpotLight</c> of SVG.</summary>
public sealed class SceneSpotLight : SceneLightSource
{
    /// <summary>Initializes a new instance of the <see cref="SceneSpotLight"/> class.</summary>
    /// <param name="x">The x of the light.</param>
    /// <param name="y">The y of the light.</param>
    /// <param name="z">The height of the light above the plane.</param>
    /// <param name="pointsAtX">The x of the point the light shines at: <c>pointsAtX</c>.</param>
    /// <param name="pointsAtY">The y of the point the light shines at.</param>
    /// <param name="pointsAtZ">The height of the point the light shines at.</param>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    public SceneSpotLight(double x, double y, double z, double pointsAtX, double pointsAtY, double pointsAtZ)
    {
        X = FilterValidation.Finite(x, nameof(x));
        Y = FilterValidation.Finite(y, nameof(y));
        Z = FilterValidation.Finite(z, nameof(z));
        PointsAtX = FilterValidation.Finite(pointsAtX, nameof(pointsAtX));
        PointsAtY = FilterValidation.Finite(pointsAtY, nameof(pointsAtY));
        PointsAtZ = FilterValidation.Finite(pointsAtZ, nameof(pointsAtZ));
    }

    /// <summary>Gets the x of the light.</summary>
    public double X { get; }

    /// <summary>Gets the y of the light.</summary>
    public double Y { get; }

    /// <summary>Gets the height of the light above the plane.</summary>
    public double Z { get; }

    /// <summary>Gets the x of the point the light shines at.</summary>
    public double PointsAtX { get; }

    /// <summary>Gets the y of the point the light shines at.</summary>
    public double PointsAtY { get; }

    /// <summary>Gets the height of the point the light shines at.</summary>
    public double PointsAtZ { get; }

    /// <summary>Gets how fast the light fades away from its axis: <c>specularExponent</c>. The default is 1; the image renderer limits it to 128.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double SpecularExponent { get; init => field = FilterValidation.NonNegative(value, nameof(value)); } = 1;

    /// <summary>Gets the angle between the axis of the light and the edge of its cone, in degrees, or <see langword="null"/> (the default) for no cone: <c>limitingConeAngle</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double? LimitingConeAngle
    {
        get;
        init => field = value is { } angle ? FilterValidation.Finite(angle, nameof(value)) : null;
    }
}
