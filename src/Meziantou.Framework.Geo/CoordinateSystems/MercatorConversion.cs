namespace Meziantou.Framework.Geo;

/// <summary>
/// The Mercator projection on an ellipsoid (EPSG methods 9804, 9805 and 1044), as World Mercator (EPSG:3395) uses it, or
/// on a sphere whose radius is the semi-major axis of the ellipsoid (EPSG method 1024, the pseudo-Mercator of Web
/// Mercator, EPSG:3857) when <see cref="IsSpherical"/> is <see langword="true"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="MercatorProjection"/>, which draws maps, it does not limit the latitudes: the poles are at infinity.
/// <see cref="CoordinateConversion.LatitudeOfOrigin"/> is the latitude whose northing is the false northing, 0 for
/// the usual definitions.
/// </remarks>
public sealed class MercatorConversion : CoordinateConversion
{
    /// <summary>Gets the scale factor on the equator. It is ignored when <see cref="StandardParallel"/> is set. The default is 1.</summary>
    public double ScaleFactor { get; init; } = 1;

    /// <summary>Gets the latitude whose scale is true, in degrees (the Mercator 2SP), or <see langword="null"/> to use <see cref="ScaleFactor"/>. The default is <see langword="null"/>.</summary>
    public double? StandardParallel { get; init; }

    /// <summary>Gets a value that indicates whether the formulas are those of a sphere whose radius is the semi-major axis of the ellipsoid, as for Web Mercator. The default is <see langword="false"/>.</summary>
    public bool IsSpherical { get; init; }

    /// <inheritdoc />
    public override (double Easting, double Northing) Project(double lambda, double phi, Ellipsoid ellipsoid)
    {
        ArgumentNullException.ThrowIfNull(ellipsoid);
        var radius = GetRadius(ellipsoid);
        var psi = GetIsometricLatitude(ellipsoid, phi) - GetIsometricLatitude(ellipsoid, LatitudeOfOrigin * Math.PI / 180);
        return (FalseEasting + (radius * GetLongitudeFromCentralMeridian(lambda)), FalseNorthing + (radius * psi));
    }

    /// <inheritdoc />
    public override (double Lambda, double Phi) Unproject(double easting, double northing, Ellipsoid ellipsoid)
    {
        ArgumentNullException.ThrowIfNull(ellipsoid);
        var radius = GetRadius(ellipsoid);
        var psi = ((northing - FalseNorthing) / radius) + GetIsometricLatitude(ellipsoid, LatitudeOfOrigin * Math.PI / 180);
        var phi = IsSpherical ? Math.Atan(Math.Sinh(psi)) : ellipsoid.GetLatitudeFromIsometric(psi);
        return (((easting - FalseEasting) / radius) + (CentralMeridian * Math.PI / 180), phi);
    }

    private double GetIsometricLatitude(Ellipsoid ellipsoid, double phi) => IsSpherical ? Math.Asinh(Math.Tan(phi)) : ellipsoid.GetIsometricLatitude(phi);

    // a k0, with k0 = cos φ1 / √(1 - e² sin² φ1) for the 2SP
    private double GetRadius(Ellipsoid ellipsoid)
    {
        var scale = ScaleFactor;
        if (StandardParallel is { } parallel)
        {
            var phi1 = parallel * Math.PI / 180;
            var sinPhi1 = Math.Sin(phi1);
            scale = IsSpherical ? Math.Cos(phi1) : Math.Cos(phi1) / Math.Sqrt(1 - (ellipsoid.EccentricitySquared * sinPhi1 * sinPhi1));
        }

        return ellipsoid.SemiMajorAxis * scale;
    }
}
