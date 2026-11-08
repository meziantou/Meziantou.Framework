namespace Meziantou.Framework.Geo;

/// <summary>A set of polygons, such as a country and its islands.</summary>
public sealed class GeoMultiPolygon : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoMultiPolygon"/> class.</summary>
    /// <param name="polygons">The polygons.</param>
    public GeoMultiPolygon(IEnumerable<GeoPolygon> polygons)
    {
        Polygons = ToArray(polygons, nameof(polygons));
    }

    /// <summary>Gets the polygons.</summary>
    public IReadOnlyList<GeoPolygon> Polygons { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.MultiPolygon;

    /// <summary>Gets a value that indicates whether a position is inside one of the polygons. See <see cref="GeoPolygon.Contains(GeoPosition)"/>.</summary>
    /// <param name="position">The position. The altitude is ignored.</param>
    /// <returns><see langword="true"/> when the position is inside one of the polygons; otherwise, <see langword="false"/>.</returns>
    public bool Contains(GeoPosition position) => Polygons.Any(polygon => polygon.Contains(position));

    /// <summary>Gets a value that indicates whether a position is inside one of the polygons, whose edges are drawn as an edge type says. See <see cref="GeoPolygon.Contains(GeoPosition, GeoEdgeType)"/>.</summary>
    /// <param name="position">The position. The altitude is ignored.</param>
    /// <param name="edgeType">How the edges are drawn.</param>
    /// <returns><see langword="true"/> when the position is inside one of the polygons; otherwise, <see langword="false"/>.</returns>
    public bool Contains(GeoPosition position, GeoEdgeType edgeType)
    {
        GeoPolygon.ValidateEdgeType(edgeType);
        return Polygons.Any(polygon => polygon.Contains(position, edgeType));
    }

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => Polygons.SelectMany(polygon => polygon.GetPositions());
}
