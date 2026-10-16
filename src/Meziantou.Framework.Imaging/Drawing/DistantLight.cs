using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A light infinitely far away, whose rays are parallel: <c>feDistantLight</c> of SVG.</summary>
/// <remarks>The unit vector toward the light is <c>(cos(azimuth) cos(elevation), sin(azimuth) cos(elevation), sin(elevation))</c>, with sines and cosines that do not come from the platform.</remarks>
public sealed class DistantLight : FilterLightSource
{
    /// <summary>Initializes a new instance of the <see cref="DistantLight"/> class.</summary>
    /// <param name="azimuth">The direction of the light in the plane, in degrees, clockwise from the x axis on the canvas: <c>azimuth</c>.</param>
    /// <param name="elevation">The angle of the light above the plane, in degrees: <c>elevation</c>.</param>
    /// <exception cref="ArgumentException">An angle is not a finite number.</exception>
    public DistantLight(double azimuth, double elevation)
    {
        DrawingGuard.Angle(azimuth, nameof(azimuth));
        DrawingGuard.Angle(elevation, nameof(elevation));
        Azimuth = azimuth;
        Elevation = elevation;
    }

    /// <summary>Gets the direction of the light in the plane, in degrees.</summary>
    public double Azimuth { get; }

    /// <summary>Gets the angle of the light above the plane, in degrees.</summary>
    public double Elevation { get; }

    internal override FilterLight Place(FilterProcessor processor)
    {
        DeterministicAngle.SinCos(Azimuth / 180, out var sinAzimuth, out var cosAzimuth);
        DeterministicAngle.SinCos(Elevation / 180, out var sinElevation, out var cosElevation);
        return new FilterLight { Kind = FilterLightKind.Distant, X = cosAzimuth * cosElevation, Y = sinAzimuth * cosElevation, Z = sinElevation };
    }
}
