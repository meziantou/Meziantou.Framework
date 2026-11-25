namespace Meziantou.Framework.Geo;

/// <summary>A set of polygons, such as a country and its islands.</summary>
public sealed class GeoMultiPolygon : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoMultiPolygon"/> class.</summary>
    /// <param name="polygons">The polygons.</param>
    public GeoMultiPolygon(IEnumerable<GeoPolygon> polygons)
    {
        PolygonArray = ToArray(polygons, nameof(polygons));
    }

    /// <summary>Gets the polygons.</summary>
    public IReadOnlyList<GeoPolygon> Polygons => PolygonArray;

    /// <summary>Gets the array that <see cref="Polygons"/> returns, so that the operations read it without interface calls.</summary>
    internal GeoPolygon[] PolygonArray { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.MultiPolygon;

    /// <summary>Gets a value that indicates whether a position is inside one of the polygons. See <see cref="GeoPolygon.Contains(GeoPosition)"/>.</summary>
    /// <param name="position">The position. The altitude is ignored.</param>
    /// <returns><see langword="true"/> when the position is inside one of the polygons; otherwise, <see langword="false"/>.</returns>
    public bool Contains(GeoPosition position)
    {
        foreach (var polygon in PolygonArray)
        {
            if (polygon.Contains(position))
                return true;
        }

        return false;
    }

    /// <summary>Gets a value that indicates whether a position is inside one of the polygons, whose edges are drawn as an edge type says. See <see cref="GeoPolygon.Contains(GeoPosition, GeoEdgeType)"/>.</summary>
    /// <param name="position">The position. The altitude is ignored.</param>
    /// <param name="edgeType">How the edges are drawn.</param>
    /// <returns><see langword="true"/> when the position is inside one of the polygons; otherwise, <see langword="false"/>.</returns>
    public bool Contains(GeoPosition position, GeoEdgeType edgeType)
    {
        GeoPolygon.ValidateEdgeType(edgeType);
        foreach (var polygon in PolygonArray)
        {
            if (polygon.Contains(position, edgeType))
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => Polygons.SelectMany(polygon => polygon.GetPositions());
}
