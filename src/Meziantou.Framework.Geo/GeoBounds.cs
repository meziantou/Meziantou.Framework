using System.Runtime.InteropServices;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>
/// A box of longitudes and latitudes, in degrees. A box that crosses the 180th meridian has its west side east of its
/// east side, as RFC 7946 allows: <c>new GeoBounds(177, -21, -178, -12)</c> goes from the longitude 177 to 180, then
/// from -180 to -178.
/// </summary>
/// <param name="West">The westernmost longitude.</param>
/// <param name="South">The smallest latitude.</param>
/// <param name="East">The easternmost longitude. It is smaller than <paramref name="West"/> when the box crosses the 180th meridian.</param>
/// <param name="North">The largest latitude.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoBounds(double West, double South, double East, double North)
{
    /// <summary>Gets a value that indicates whether the box crosses the 180th meridian: its west side is east of its east side.</summary>
    public bool CrossesAntimeridian => West > East;

    /// <summary>Gets a value that indicates whether a position is in the box, edges included. The altitude is ignored.</summary>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> when the position is in the box; otherwise, <see langword="false"/>.</returns>
    public bool Contains(GeoPosition position)
    {
        if (!(position.Latitude >= South && position.Latitude <= North))
            return false;

        if (CrossesAntimeridian)
            return position.Longitude >= West || position.Longitude <= East;

        return position.Longitude >= West && position.Longitude <= East;
    }

    /// <summary>Gets a value that indicates whether this box and another one have a part in common, edges included.</summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> when the boxes intersect; otherwise, <see langword="false"/>.</returns>
    public bool Intersects(GeoBounds other)
    {
        if (!(other.South <= North && other.North >= South))
            return false;

        foreach (var (west, east) in GetLongitudeIntervals())
        {
            foreach (var (otherWest, otherEast) in other.GetLongitudeIntervals())
            {
                if (otherWest <= east && otherEast >= west)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Gets the smallest box that contains this box and another one.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>
    /// The box that contains both boxes. When neither box crosses the 180th meridian, the result does not cross it either:
    /// it goes from the westernmost to the easternmost side. Otherwise, the result is the shortest box that contains both,
    /// and it can cross the 180th meridian.
    /// </returns>
    public GeoBounds Union(GeoBounds other)
    {
        if (!CrossesAntimeridian && !other.CrossesAntimeridian)
            return new(Math.Min(West, other.West), Math.Min(South, other.South), Math.Max(East, other.East), Math.Max(North, other.North));

        var longitudes = new LongitudeRange();
        longitudes.Add(West, GetWidth());
        longitudes.Add(other.West, other.GetWidth());
        var (west, east) = longitudes.GetRange();
        return new(west, Math.Min(South, other.South), east, Math.Max(North, other.North));
    }

    /// <summary>Gets the smallest box that contains positions.</summary>
    /// <param name="positions">The positions.</param>
    /// <returns>The box, or <see langword="null"/> when there is no position. It does not cross the 180th meridian.</returns>
    public static GeoBounds? FromPositions(IEnumerable<GeoPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var (west, south, east, north) = (double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var position in positions)
        {
            west = Math.Min(west, position.Longitude);
            south = Math.Min(south, position.Latitude);
            east = Math.Max(east, position.Longitude);
            north = Math.Max(north, position.Latitude);
        }

        if (west > east)
            return null;

        return new GeoBounds(west, south, east, north);
    }

    private double GetWidth() => CrossesAntimeridian ? East - West + 360 : East - West;

    private (double West, double East)[] GetLongitudeIntervals() => CrossesAntimeridian ? [(West, 180), (-180, East)] : [(West, East)];
}
