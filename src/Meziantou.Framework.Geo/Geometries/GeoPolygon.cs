using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>A polygon, such as a country or a lake: an exterior ring, then the rings of its holes, as in GeoJSON.</summary>
public sealed class GeoPolygon : GeoGeometry
{
    // The lowest and the highest latitudes of each ring, computed on the first call to Contains: the polygon is immutable
    private (double Min, double Max)[]? _ringLatitudeRanges;

    /// <summary>Initializes a new instance of the <see cref="GeoPolygon"/> class.</summary>
    /// <param name="rings">The rings: the exterior ring first, then the holes. A ring may repeat its first position at its end.</param>
    public GeoPolygon(IEnumerable<IEnumerable<GeoPosition>> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        RingArrays = [.. rings.Select(ring => ToArray(ring, nameof(rings)))];
        Rings = [.. RingArrays];
    }

    private GeoPolygon(GeoPosition[][] rings)
    {
        RingArrays = rings;
        Rings = [.. rings];
    }

    /// <summary>Gets the rings: the exterior ring first, then the holes.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Rings { get; }

    /// <summary>Gets the arrays that <see cref="Rings"/> returns, so that the operations read them without interface calls.</summary>
    internal GeoPosition[][] RingArrays { get; }

    /// <summary>Creates a polygon that keeps arrays the caller no longer uses, instead of copies of them.</summary>
    internal static GeoPolygon FromOwnedArrays(GeoPosition[][] rings) => new(rings);

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
        var rings = RingArrays;
        var latitudeRanges = _ringLatitudeRanges ??= GetLatitudeRanges(rings);
        for (var r = 0; r < rings.Length; r++)
        {
            // No edge of a ring crosses the latitude of a position that is not between the lowest and the highest
            // latitudes of the ring, as the test below compares the latitudes exactly. A NaN never skips the ring.
            if (position.Latitude < latitudeRanges[r].Min || position.Latitude >= latitudeRanges[r].Max)
                continue;

            var ring = rings[r].AsSpan();
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                // Each edge from its lower end, so that the rounding does not depend on the direction of the ring
                ref readonly var p = ref ring[i];
                ref readonly var q = ref ring[j];
                var isLower = p.Latitude <= q.Latitude;
                ref readonly var a = ref isLower ? ref p : ref q;
                ref readonly var b = ref isLower ? ref q : ref p;
                if ((a.Latitude > position.Latitude) != (b.Latitude > position.Latitude) &&
                    position.Longitude < ((b.Longitude - a.Longitude) * (position.Latitude - a.Latitude) / (b.Latitude - a.Latitude)) + a.Longitude)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static (double Min, double Max)[] GetLatitudeRanges(GeoPosition[][] rings)
    {
        var ranges = new (double Min, double Max)[rings.Length];
        for (var r = 0; r < rings.Length; r++)
        {
            var (min, max) = (double.PositiveInfinity, double.NegativeInfinity);
            foreach (ref readonly var position in rings[r].AsSpan())
            {
                min = Math.Min(min, position.Latitude);
                max = Math.Max(max, position.Latitude);
            }

            ranges[r] = (min, max);
        }

        return ranges;
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
        GeoEdgeType.Geodesic => SphericalGeometry.Contains(RingArrays, position),
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
