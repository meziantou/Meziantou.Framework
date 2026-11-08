namespace Meziantou.Framework.Geo;

/// <summary>How the edges between the consecutive positions of a line or a ring are drawn.</summary>
public enum GeoEdgeType
{
    /// <summary>
    /// The edges are straight lines in degrees, as on an equirectangular map and as in RFC 7946: an edge from the
    /// longitude 179 to the longitude -179 goes the long way, through the Greenwich meridian.
    /// </summary>
    Planar = 0,

    /// <summary>
    /// The edges are the shortest great-circle arcs between their ends, as a plane flies: an edge from the longitude 179
    /// to the longitude -179 crosses the 180th meridian, and an edge between two positions of the same latitude bends
    /// toward the nearest pole.
    /// </summary>
    Geodesic = 1,
}
