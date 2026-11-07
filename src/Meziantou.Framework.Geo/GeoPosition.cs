using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo;

/// <summary>A position on the Earth, in degrees, with an optional altitude.</summary>
/// <param name="Longitude">The longitude, from -180 (west) to 180 (east).</param>
/// <param name="Latitude">The latitude, from -90 (south) to 90 (north).</param>
/// <param name="Altitude">The altitude, in meters, or <see langword="null"/> when the position has none.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoPosition(double Longitude, double Latitude, double? Altitude = null)
{
    /// <summary>The mean radius of the Earth, in meters, as defined by the IUGG.</summary>
    public const double EarthRadius = 6_371_008.8;

    /// <summary>Gets the great-circle distance to another position, in meters, on a sphere of radius <see cref="EarthRadius"/>. The altitudes are ignored.</summary>
    /// <param name="other">The other position.</param>
    /// <returns>The distance, in meters.</returns>
    /// <remarks>The haversine formula: the Earth is not a sphere, so the result can differ from the distance on the ellipsoid by up to 0.5%.</remarks>
    public double GetDistance(GeoPosition other)
    {
        var phi1 = Latitude * Math.PI / 180;
        var phi2 = other.Latitude * Math.PI / 180;
        var sinDeltaPhi = Math.Sin((phi2 - phi1) / 2);
        var sinDeltaLambda = Math.Sin((other.Longitude - Longitude) * Math.PI / 180 / 2);
        var a = (sinDeltaPhi * sinDeltaPhi) + (Math.Cos(phi1) * Math.Cos(phi2) * sinDeltaLambda * sinDeltaLambda);
        return 2 * EarthRadius * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
