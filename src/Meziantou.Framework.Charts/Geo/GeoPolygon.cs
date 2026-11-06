namespace Meziantou.Framework.Charts;

/// <summary>A polygon: an exterior ring, then the rings of its holes, as in GeoJSON.</summary>
public sealed class GeoPolygon
{
    /// <summary>Initializes a new instance of the <see cref="GeoPolygon"/> class.</summary>
    /// <param name="rings">The rings: the exterior ring first, then the holes. A ring may repeat its first position at its end.</param>
    public GeoPolygon(IEnumerable<IEnumerable<GeoPosition>> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        Rings = [.. rings.Select(ring => (IReadOnlyList<GeoPosition>)[.. ring])];
    }

    /// <summary>Gets the rings: the exterior ring first, then the holes.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Rings { get; }

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
            return (0, Mean(ring));

        return (area, new GeoPosition(cx / (6 * area), cy / (6 * area)));
    }

    internal static GeoPosition Mean(IReadOnlyList<GeoPosition> positions)
    {
        if (positions.Count is 0)
            return new GeoPosition(double.NaN, double.NaN);

        return new GeoPosition(positions.Average(position => position.Longitude), positions.Average(position => position.Latitude));
    }
}
