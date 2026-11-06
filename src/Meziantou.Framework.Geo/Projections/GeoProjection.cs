namespace Meziantou.Framework.Geo;

/// <summary>
/// A map projection: converts longitudes and latitudes to points of a plane, in radii of the Earth, with y increasing to
/// the north. Scale and translate the result to draw a map.
/// </summary>
/// <remarks>The formulas are ports of d3-geo and d3-geo-projection (ISC license, Copyright Mike Bostock).</remarks>
public abstract class GeoProjection
{
    /// <summary>Initializes a new instance of the <see cref="GeoProjection"/> class.</summary>
    protected GeoProjection()
    {
    }

    /// <summary>Gets the largest latitude the projection draws, in degrees: <see cref="Project(double, double)"/> moves the latitudes past it to it. The default is 90.</summary>
    public virtual double MaxLatitude => 90;

    /// <summary>Gets the longitude at the center of the map, in degrees. The default is 0.</summary>
    public virtual double CenterLongitude => 0;

    /// <summary>Gets a value that indicates whether the meridians are straight lines, so a line along a meridian needs no intermediate points. The default is <see langword="true"/>.</summary>
    public virtual bool HasStraightMeridians => true;

    /// <summary>Gets a value that indicates whether the parallels are straight lines. The default is <see langword="true"/>.</summary>
    public virtual bool HasStraightParallels => true;

    /// <summary>Projects a position to a point of the plane.</summary>
    /// <param name="position">The position. The altitude is ignored.</param>
    /// <returns>The point, in radii of the Earth, with y increasing to the north.</returns>
    public (double X, double Y) Project(GeoPosition position) => Project(position.Longitude, position.Latitude);

    /// <summary>Projects a position to a point of the plane.</summary>
    /// <param name="longitude">The longitude, in degrees.</param>
    /// <param name="latitude">The latitude, in degrees.</param>
    /// <returns>The point, in radii of the Earth, with y increasing to the north.</returns>
    public (double X, double Y) Project(double longitude, double latitude)
    {
        var max = MaxLatitude;
        latitude = Math.Clamp(latitude, -max, max);
        return ProjectRadians(longitude * Math.PI / 180, latitude * Math.PI / 180);
    }

    /// <summary>Converts a point of the plane back to a position: the inverse of <see cref="Project(double, double)"/>.</summary>
    /// <param name="x">The x coordinate, in radii of the Earth.</param>
    /// <param name="y">The y coordinate, in radii of the Earth, increasing to the north.</param>
    /// <returns>The position, with a longitude from -180 to 180, or <see langword="null"/> when the point is outside of the map or the projection has no inverse.</returns>
    public GeoPosition? Invert(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || InvertRadians(x, y) is not var (lambda, phi))
            return null;

        var longitude = lambda * 180 / Math.PI;
        var latitude = phi * 180 / Math.PI;
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude) || Math.Abs(latitude) > 90 + 1e-9)
            return null;

        // Back to -180..180, as the longitudes of the formulas are relative to the center of the map
        longitude = Math.IEEERemainder(longitude, 360);
        return new GeoPosition(longitude, Math.Clamp(latitude, -90, 90));
    }

    /// <summary>Projects a position, in radians.</summary>
    /// <param name="lambda">The longitude, in radians.</param>
    /// <param name="phi">The latitude, in radians, already limited to <see cref="MaxLatitude"/>.</param>
    /// <returns>The point, in radii of the Earth, with y increasing to the north.</returns>
    protected abstract (double X, double Y) ProjectRadians(double lambda, double phi);

    /// <summary>Converts a point of the plane back to a position, in radians.</summary>
    /// <param name="x">The x coordinate, in radii of the Earth.</param>
    /// <param name="y">The y coordinate, in radii of the Earth.</param>
    /// <returns>The longitude and the latitude, in radians, or <see langword="null"/> when the point is outside of the map or the projection has no inverse. The default returns <see langword="null"/>.</returns>
    protected virtual (double Lambda, double Phi)? InvertRadians(double x, double y) => null;
}
