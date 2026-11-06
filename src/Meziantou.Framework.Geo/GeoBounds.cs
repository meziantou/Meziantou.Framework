using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo;

/// <summary>A box of longitudes and latitudes, in degrees.</summary>
/// <param name="West">The smallest longitude.</param>
/// <param name="South">The smallest latitude.</param>
/// <param name="East">The largest longitude.</param>
/// <param name="North">The largest latitude.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoBounds(double West, double South, double East, double North)
{
    /// <summary>Gets a value that indicates whether a position is in the box, edges included. The altitude is ignored.</summary>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> when the position is in the box; otherwise, <see langword="false"/>.</returns>
    public bool Contains(GeoPosition position) => position.Longitude >= West && position.Longitude <= East && position.Latitude >= South && position.Latitude <= North;

    /// <summary>Gets the smallest box that contains this box and another one.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>The box that contains both boxes.</returns>
    public GeoBounds Union(GeoBounds other) => new(Math.Min(West, other.West), Math.Min(South, other.South), Math.Max(East, other.East), Math.Max(North, other.North));

    /// <summary>Gets the smallest box that contains positions.</summary>
    /// <param name="positions">The positions.</param>
    /// <returns>The box, or <see langword="null"/> when there is no position.</returns>
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
}
