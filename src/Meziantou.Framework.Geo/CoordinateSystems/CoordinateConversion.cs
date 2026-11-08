namespace Meziantou.Framework.Geo;

/// <summary>
/// The projection of a <see cref="ProjectedCoordinateSystem"/>: converts the longitudes and latitudes of an ellipsoid to
/// eastings and northings in meters, as the map projection methods of the EPSG dataset define them.
/// </summary>
/// <remarks>
/// Derive from this class to support another projection method. The angles are in radians, the longitudes measured from
/// the prime meridian of the geographic coordinate system; the eastings and the northings are in meters, the false
/// easting and the false northing included.
/// </remarks>
public abstract class CoordinateConversion
{
    /// <summary>Initializes a new instance of the <see cref="CoordinateConversion"/> class.</summary>
    protected CoordinateConversion()
    {
    }

    /// <summary>Gets the latitude of the origin of the northings (the latitude of the false origin of a Lambert conformal conic projection), in degrees. The default is 0.</summary>
    public double LatitudeOfOrigin { get; init; }

    /// <summary>Gets the longitude of the origin (the central meridian), in degrees from the prime meridian. The default is 0.</summary>
    public double CentralMeridian { get; init; }

    /// <summary>Gets the easting of the origin, in meters. The default is 0.</summary>
    public double FalseEasting { get; init; }

    /// <summary>Gets the northing of the origin, in meters. The default is 0.</summary>
    public double FalseNorthing { get; init; }

    /// <summary>Projects a longitude and a latitude.</summary>
    /// <param name="lambda">The longitude from the prime meridian, in radians.</param>
    /// <param name="phi">The latitude, in radians.</param>
    /// <param name="ellipsoid">The ellipsoid of the longitudes and the latitudes.</param>
    /// <returns>The easting and the northing, in meters.</returns>
    public abstract (double Easting, double Northing) Project(double lambda, double phi, Ellipsoid ellipsoid);

    /// <summary>Converts an easting and a northing back to a longitude and a latitude: the inverse of <see cref="Project"/>.</summary>
    /// <param name="easting">The easting, in meters.</param>
    /// <param name="northing">The northing, in meters.</param>
    /// <param name="ellipsoid">The ellipsoid of the longitudes and the latitudes.</param>
    /// <returns>The longitude from the prime meridian and the latitude, in radians.</returns>
    public abstract (double Lambda, double Phi) Unproject(double easting, double northing, Ellipsoid ellipsoid);

    /// <summary>Gets the longitude from the central meridian, from -π to π.</summary>
    /// <param name="lambda">The longitude from the prime meridian, in radians.</param>
    /// <returns>The longitude from the central meridian, in radians.</returns>
    private protected double GetLongitudeFromCentralMeridian(double lambda) => GeographicCoordinateSystem.NormalizeLongitude(lambda - (CentralMeridian * Math.PI / 180));
}
