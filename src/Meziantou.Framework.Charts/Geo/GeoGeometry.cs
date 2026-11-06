namespace Meziantou.Framework.Charts;

/// <summary>
/// A geometry: polygons (areas such as countries), lines (rivers, routes) and points (cities). A GeoJSON geometry of any
/// type is read into these three lists; a <c>GeometryCollection</c> fills several of them.
/// </summary>
public sealed class GeoGeometry
{
    private GeoPosition? _centroid;

    /// <summary>Initializes a new instance of the <see cref="GeoGeometry"/> class.</summary>
    /// <param name="polygons">The polygons, or <see langword="null"/> for none.</param>
    /// <param name="lines">The lines, or <see langword="null"/> for none.</param>
    /// <param name="points">The points, or <see langword="null"/> for none.</param>
    public GeoGeometry(IEnumerable<GeoPolygon>? polygons = null, IEnumerable<IEnumerable<GeoPosition>>? lines = null, IEnumerable<GeoPosition>? points = null)
    {
        Polygons = polygons is null ? [] : [.. polygons];
        Lines = lines is null ? [] : [.. lines.Select(line => (IReadOnlyList<GeoPosition>)[.. line])];
        Points = points is null ? [] : [.. points];
    }

    /// <summary>Gets the polygons.</summary>
    public IReadOnlyList<GeoPolygon> Polygons { get; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Lines { get; }

    /// <summary>Gets the points.</summary>
    public IReadOnlyList<GeoPosition> Points { get; }

    /// <summary>
    /// Gets a position to label the geometry: the centroid of the exterior ring of its largest polygon (so the islands of a
    /// country do not pull it away), else the mean of the positions of its lines, else the mean of its points. The centroid
    /// is computed on the longitudes and latitudes, as on an equirectangular map. It is <see langword="null"/> for an empty geometry.
    /// </summary>
    public GeoPosition? Centroid => _centroid ??= ComputeCentroid();

    private GeoPosition? ComputeCentroid()
    {
        var bestArea = -1.0;
        GeoPosition? best = null;
        foreach (var polygon in Polygons)
        {
            if (polygon.Rings.Count is 0 || polygon.Rings[0].Count is 0)
                continue;

            var (area, centroid) = GeoPolygon.GetAreaAndCentroid(polygon.Rings[0]);
            if (Math.Abs(area) > bestArea)
            {
                bestArea = Math.Abs(area);
                best = centroid;
            }
        }

        if (best is not null)
            return best;

        var linePositions = Lines.SelectMany(line => line).ToList();
        if (linePositions.Count > 0)
            return GeoPolygon.Mean(linePositions);

        return Points.Count > 0 ? GeoPolygon.Mean(Points) : null;
    }
}
