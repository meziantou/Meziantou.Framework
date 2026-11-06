namespace Meziantou.Framework.Charts;

/// <summary>
/// A map projection: converts longitudes and latitudes to points of a plane. <see cref="GeoCoordinates"/> scales and
/// centers the projected map to fit the plot area.
/// </summary>
/// <remarks>The formulas are ports of d3-geo and d3-geo-projection (ISC license, Copyright Mike Bostock).</remarks>
public abstract class GeoProjection
{
    private protected GeoProjection()
    {
    }

    /// <summary>The largest latitude the projection draws, in degrees: the latitudes past it are moved to it.</summary>
    internal virtual double MaxLatitude => 90;

    /// <summary>The longitude at the center of the map, in degrees.</summary>
    internal virtual double CenterLongitude => 0;

    /// <summary>A value that indicates whether the meridians are straight lines, so they need no intermediate points.</summary>
    internal virtual bool StraightMeridians => true;

    /// <summary>A value that indicates whether the parallels are straight lines.</summary>
    internal virtual bool StraightParallels => true;

    /// <summary>Projects a position, in degrees, to a point of the plane, in radii of the Earth, with y increasing to the north.</summary>
    internal (double X, double Y) Project(double longitude, double latitude)
    {
        var max = MaxLatitude;
        latitude = Math.Clamp(latitude, -max, max);
        return ProjectRadians(longitude * Math.PI / 180, latitude * Math.PI / 180);
    }

    /// <summary>Projects a position, in radians.</summary>
    private protected abstract (double X, double Y) ProjectRadians(double lambda, double phi);
}
