namespace Meziantou.Framework.Geo;

/// <summary>
/// A projected coordinate reference system: eastings and northings on a plane, such as UTM, Lambert 93 or Web Mercator,
/// made of a <see cref="GeographicCoordinateSystem"/> and the <see cref="CoordinateConversion"/> that projects it.
/// </summary>
public sealed class ProjectedCoordinateSystem : CoordinateReferenceSystem
{
    /// <summary>Initializes a new instance of the <see cref="ProjectedCoordinateSystem"/> class.</summary>
    /// <param name="name">The name of the coordinate reference system.</param>
    /// <param name="geographicCoordinateSystem">The geographic coordinate system that the projection projects.</param>
    /// <param name="conversion">The projection.</param>
    public ProjectedCoordinateSystem(string name, GeographicCoordinateSystem geographicCoordinateSystem, CoordinateConversion conversion)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(geographicCoordinateSystem);
        ArgumentNullException.ThrowIfNull(conversion);
        GeographicCoordinateSystem = geographicCoordinateSystem;
        Conversion = conversion;
    }

    /// <summary>Gets the geographic coordinate system that the projection projects.</summary>
    public GeographicCoordinateSystem GeographicCoordinateSystem { get; }

    /// <summary>Gets the projection.</summary>
    public CoordinateConversion Conversion { get; }

    /// <summary>Gets the linear unit of the coordinates, in meters, such as 0.3048 for the international foot. The default is 1, a meter.</summary>
    public double LinearUnit { get; init; } = 1;

    /// <inheritdoc />
    public override bool IsGeographic => false;

    /// <inheritdoc />
    public override GeoPosition ToWgs84(GeoPosition position)
    {
        var (lambda, phi) = Conversion.Unproject(position.Longitude * LinearUnit, position.Latitude * LinearUnit, GeographicCoordinateSystem.Ellipsoid);
        return GeographicCoordinateSystem.ToWgs84Radians(lambda, phi, position.Altitude);
    }

    /// <inheritdoc />
    public override GeoPosition FromWgs84(GeoPosition position)
    {
        var (lambda, phi, altitude) = GeographicCoordinateSystem.FromWgs84Radians(position);
        var (easting, northing) = Conversion.Project(lambda, phi, GeographicCoordinateSystem.Ellipsoid);
        return new GeoPosition(easting / LinearUnit, northing / LinearUnit, altitude);
    }
}
