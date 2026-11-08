namespace Meziantou.Framework.Geo;

/// <summary>A geographic coordinate reference system: longitudes and latitudes on the ellipsoid of a datum, such as WGS 84, ETRS89 or NTF.</summary>
public sealed class GeographicCoordinateSystem : CoordinateReferenceSystem
{
    private const double DegreesToRadians = Math.PI / 180;

    /// <summary>Initializes a new instance of the <see cref="GeographicCoordinateSystem"/> class.</summary>
    /// <param name="name">The name of the coordinate reference system.</param>
    /// <param name="ellipsoid">The ellipsoid of the datum.</param>
    public GeographicCoordinateSystem(string name, Ellipsoid ellipsoid)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(ellipsoid);
        Ellipsoid = ellipsoid;
    }

    /// <summary>Gets the ellipsoid of the datum.</summary>
    public Ellipsoid Ellipsoid { get; }

    /// <summary>
    /// Gets the transformation from the datum to WGS 84, or <see langword="null"/> when the longitudes and the latitudes
    /// of the datum are taken as those of WGS 84, as for ETRS89, RGF93 or NAD83, which differ by less than two meters.
    /// The default is <see langword="null"/>.
    /// </summary>
    public HelmertTransformation? ToWgs84Transformation { get; init; }

    /// <summary>Gets the longitude of the prime meridian east of Greenwich, in degrees, such as 2.33722917 for the meridian of Paris. The default is 0.</summary>
    public double PrimeMeridian { get; init; }

    /// <summary>Gets the angular unit of the coordinates, in radians, such as π / 200 for grads. The default is a degree, π / 180.</summary>
    public double AngularUnit { get; init; } = DegreesToRadians;

    /// <inheritdoc />
    public override bool IsGeographic => true;

    private bool IsWgs84Equivalent => ToWgs84Transformation is null && PrimeMeridian is 0 && AngularUnit == DegreesToRadians;

    /// <inheritdoc />
    public override GeoPosition ToWgs84(GeoPosition position)
    {
        if (IsWgs84Equivalent)
            return position;

        return ToWgs84Radians(position.Longitude * AngularUnit, position.Latitude * AngularUnit, position.Altitude);
    }

    /// <inheritdoc />
    public override GeoPosition FromWgs84(GeoPosition position)
    {
        if (IsWgs84Equivalent)
            return position;

        var (lambda, phi, altitude) = FromWgs84Radians(position);
        return new GeoPosition(NormalizeLongitude(lambda) / AngularUnit, phi / AngularUnit, altitude);
    }

    /// <summary>Converts a longitude from the prime meridian and a latitude, in radians, to WGS 84.</summary>
    internal GeoPosition ToWgs84Radians(double lambda, double phi, double? altitude)
    {
        lambda += PrimeMeridian * DegreesToRadians;
        if (ToWgs84Transformation is { } transformation)
        {
            var (x, y, z) = Ellipsoid.ToGeocentric(lambda, phi, altitude ?? 0);
            (x, y, z) = transformation.Apply(x, y, z);
            (lambda, phi, var height) = Ellipsoid.Wgs84.FromGeocentric(x, y, z);
            if (altitude is not null)
            {
                altitude = height;
            }
        }

        return new GeoPosition(NormalizeLongitude(lambda) / DegreesToRadians, phi / DegreesToRadians, altitude);
    }

    /// <summary>Converts a position of WGS 84 to a longitude from the prime meridian and a latitude, in radians.</summary>
    internal (double Lambda, double Phi, double? Altitude) FromWgs84Radians(GeoPosition position)
    {
        var lambda = position.Longitude * DegreesToRadians;
        var phi = position.Latitude * DegreesToRadians;
        var altitude = position.Altitude;
        if (ToWgs84Transformation is { } transformation)
        {
            var (x, y, z) = Ellipsoid.Wgs84.ToGeocentric(lambda, phi, altitude ?? 0);
            (x, y, z) = transformation.Invert(x, y, z);
            (lambda, phi, var height) = Ellipsoid.FromGeocentric(x, y, z);
            if (altitude is not null)
            {
                altitude = height;
            }
        }

        return (lambda - (PrimeMeridian * DegreesToRadians), phi, altitude);
    }

    internal static double NormalizeLongitude(double lambda) => Math.Abs(lambda) <= Math.PI ? lambda : Math.IEEERemainder(lambda, 2 * Math.PI);
}
