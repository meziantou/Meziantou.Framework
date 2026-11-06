namespace Meziantou.Framework.Geo;

/// <summary>The type of a <see cref="GeoGeometry"/>. The values are the geometry type codes of WKB.</summary>
public enum GeoGeometryType
{
    /// <summary>A <see cref="GeoPoint"/>.</summary>
    Point = 1,

    /// <summary>A <see cref="GeoLineString"/>.</summary>
    LineString = 2,

    /// <summary>A <see cref="GeoPolygon"/>.</summary>
    Polygon = 3,

    /// <summary>A <see cref="GeoMultiPoint"/>.</summary>
    MultiPoint = 4,

    /// <summary>A <see cref="GeoMultiLineString"/>.</summary>
    MultiLineString = 5,

    /// <summary>A <see cref="GeoMultiPolygon"/>.</summary>
    MultiPolygon = 6,

    /// <summary>A <see cref="GeoGeometryCollection"/>.</summary>
    GeometryCollection = 7,
}
