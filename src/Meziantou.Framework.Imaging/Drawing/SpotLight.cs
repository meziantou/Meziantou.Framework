using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A light at a point, that shines toward another point: <c>feSpotLight</c> of SVG.</summary>
/// <remarks>
/// The light is the one of a <see cref="PointLight"/>, multiplied by <c>max(-L.S, 0)^SpecularExponent</c>, with <c>L</c>
/// the unit vector toward the light and <c>S</c> the unit vector from the light to the point it shines at; outside
/// <see cref="LimitingConeAngle"/>, where <c>-L.S</c> is less than the cosine of that angle, there is no light.
/// </remarks>
public sealed class SpotLight : FilterLightSource
{
    /// <summary>Initializes a new instance of the <see cref="SpotLight"/> class.</summary>
    /// <param name="x">The x of the light, in the drawing.</param>
    /// <param name="y">The y of the light, in the drawing.</param>
    /// <param name="z">The height of the light above the plane, in units of the drawing.</param>
    /// <param name="pointsAtX">The x of the point the light shines at: <c>pointsAtX</c>.</param>
    /// <param name="pointsAtY">The y of the point the light shines at.</param>
    /// <param name="pointsAtZ">The height of the point the light shines at.</param>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public SpotLight(double x, double y, double z, double pointsAtX, double pointsAtY, double pointsAtZ)
    {
        DrawingGuard.Coordinate(x, nameof(x));
        DrawingGuard.Coordinate(y, nameof(y));
        DrawingGuard.Coordinate(z, nameof(z));
        DrawingGuard.Coordinate(pointsAtX, nameof(pointsAtX));
        DrawingGuard.Coordinate(pointsAtY, nameof(pointsAtY));
        DrawingGuard.Coordinate(pointsAtZ, nameof(pointsAtZ));
        X = x;
        Y = y;
        Z = z;
        PointsAtX = pointsAtX;
        PointsAtY = pointsAtY;
        PointsAtZ = pointsAtZ;
        SpecularExponent = 1;
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

    /// <summary>Gets how fast the light fades away from its axis: <c>specularExponent</c>. Defaults to 1.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 128.</exception>
    public double SpecularExponent
    {
        get;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The exponent must be a finite number.", nameof(value));

            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 128);
            field = value;
        }
    }

    /// <summary>Gets the angle between the axis of the light and the edge of its cone, in degrees, or <see langword="null"/> (the default) for no cone: <c>limitingConeAngle</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double? LimitingConeAngle
    {
        get;
        init
        {
            if (value is { } angle)
            {
                DrawingGuard.Angle(angle, nameof(value));
            }

            field = value;
        }
    }

    internal override FilterLight Place(FilterProcessor processor)
    {
        var scale = Math.Sqrt(processor.ScaleX * processor.ScaleY);
        var (x, y) = processor.ToCanvas(X, Y);
        var (targetX, targetY) = processor.ToCanvas(PointsAtX, PointsAtY);
        var sx = targetX - x;
        var sy = targetY - y;
        var sz = (PointsAtZ - Z) * scale;
        var length = Math.Sqrt((sx * sx) + (sy * sy) + (sz * sz));
        var cone = double.NegativeInfinity;
        if (LimitingConeAngle is { } angle)
        {
            DeterministicAngle.SinCos(Math.Abs(angle) / 180, out _, out cone);
        }

        return new FilterLight
        {
            Kind = FilterLightKind.Spot,
            X = x,
            Y = y,
            Z = Z * scale,
            AxisX = length > 0 ? sx / length : 0,
            AxisY = length > 0 ? sy / length : 0,
            AxisZ = length > 0 ? sz / length : 0,
            Exponent = SpecularExponent,
            ConeCosine = cone,
        };
    }
}
