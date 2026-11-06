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
    /// not great circles. The result is unspecified for a position on an edge.
    /// </remarks>
    public bool Contains(GeoPosition position)
    {
        // Even-odd rule over every ring: a position in a hole crosses the edges of the exterior ring and of the hole
        var inside = false;
        foreach (var ring in Rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Latitude > position.Latitude) != (b.Latitude > position.Latitude) &&
                    position.Longitude < ((b.Longitude - a.Longitude) * (position.Latitude - a.Latitude) / (b.Latitude - a.Latitude)) + a.Longitude)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => Rings.SelectMany(ring => ring);

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
