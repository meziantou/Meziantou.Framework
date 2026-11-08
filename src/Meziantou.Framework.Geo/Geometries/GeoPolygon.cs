using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>A polygon, such as a country or a lake: an exterior ring, then the rings of its holes, as in GeoJSON.</summary>
public sealed class GeoPolygon : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoPolygon"/> class.</summary>
    /// <param name="rings">The rings: the exterior ring first, then the holes. A ring may repeat its first position at its end.</param>
    public GeoPolygon(IEnumerable<IEnumerable<GeoPosition>> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        Rings = [.. rings.Select(ring => (IReadOnlyList<GeoPosition>)ToArray(ring, nameof(rings)))];
    }

    /// <summary>Gets the rings: the exterior ring first, then the holes.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Rings { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.Polygon;

    /// <summary>Gets a value that indicates whether a position is inside the polygon and outside its holes.</summary>
    /// <param name="position">The position. The altitude is ignored.</param>
    /// <returns><see langword="true"/> when the position is inside the polygon; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// The test is made on the longitudes and latitudes, as on an equirectangular map: the edges are straight in degrees,
    /// not great circles, as <see cref="GeoEdgeType.Planar"/> says. A polygon that crosses the 180th meridian must be split
    /// along it first, with <see cref="GeoGeometry.SplitAtAntimeridian"/>. The result is unspecified for a position on an edge.
    /// </remarks>
    public bool Contains(GeoPosition position)
    {
        // Even-odd rule over every ring: a position in a hole crosses the edges of the exterior ring and of the hole
        var inside = false;
        foreach (var ring in Rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                // Each edge from its lower end, so that the rounding does not depend on the direction of the ring
                var (a, b) = ring[i].Latitude <= ring[j].Latitude ? (ring[i], ring[j]) : (ring[j], ring[i]);
                if ((a.Latitude > position.Latitude) != (b.Latitude > position.Latitude) &&
                    position.Longitude < ((b.Longitude - a.Longitude) * (position.Latitude - a.Latitude) / (b.Latitude - a.Latitude)) + a.Longitude)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <summary>Gets a value that indicates whether a position is inside the polygon and outside its holes, whose edges are drawn as an edge type says.</summary>
    /// <param name="position">The position. The altitude is ignored.</param>
    /// <param name="edgeType">How the edges are drawn.</param>
    /// <returns><see langword="true"/> when the position is inside the polygon; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <para>
    /// With <see cref="GeoEdgeType.Geodesic"/>, each edge is the shortest great-circle arc between its ends, on a sphere:
    /// a polygon may cross the 180th meridian without being split. The rings may turn in either direction: the polygon is
    /// the side of its exterior ring that does not contain the poles, or, for a ring that goes around a pole, such as the
    /// coast of Antarctica, the side that contains the pole it is nearest to.
    /// </para>
    /// <para>The result is unspecified for a position on an edge.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeType"/> is not a value of <see cref="GeoEdgeType"/>.</exception>
    public bool Contains(GeoPosition position, GeoEdgeType edgeType) => edgeType switch
    {
        GeoEdgeType.Planar => Contains(position),
        GeoEdgeType.Geodesic => SphericalGeometry.Contains(Rings, position),
        _ => throw new ArgumentOutOfRangeException(nameof(edgeType), edgeType, message: null),
    };

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => Rings.SelectMany(ring => ring);

    internal static void ValidateEdgeType(GeoEdgeType edgeType)
    {
        if (edgeType is not GeoEdgeType.Planar and not GeoEdgeType.Geodesic)
            throw new ArgumentOutOfRangeException(nameof(edgeType), edgeType, message: null);
    }

    /// <summary>The signed area of a ring in square degrees (shoelace formula) and its centroid, or a zero area and the mean of its positions when it is flat.</summary>
    internal static (double Area, GeoPosition Centroid) GetAreaAndCentroid(IReadOnlyList<GeoPosition> ring)
    {
        var area = 0.0;
        var cx = 0.0;
        var cy = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var cross = (a.Longitude * b.Latitude) - (b.Longitude * a.Latitude);
            area += cross;
            cx += (a.Longitude + b.Longitude) * cross;
            cy += (a.Latitude + b.Latitude) * cross;
        }

        area /= 2;
        if (Math.Abs(area) < 1e-12)
            return (0, new GeoPosition(ring.Average(position => position.Longitude), ring.Average(position => position.Latitude)));

        return (area, new GeoPosition(cx / (6 * area), cy / (6 * area)));
    }
}
