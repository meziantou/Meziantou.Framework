namespace Meziantou.Framework.Geo.Internals;

/// <summary>The constants of the ESRI Shapefile Technical Description (July 1998).</summary>
internal static class ShapefileShapeType
{
    public const int FileCode = 9994;
    public const int Version = 1000;

    public const int Null = 0;
    public const int Point = 1;
    public const int PolyLine = 3;
    public const int Polygon = 5;
    public const int MultiPoint = 8;
    public const int PointZ = 11;
    public const int PolyLineZ = 13;
    public const int PolygonZ = 15;
    public const int MultiPointZ = 18;
    public const int PointM = 21;
    public const int PolyLineM = 23;
    public const int PolygonM = 25;
    public const int MultiPointM = 28;
    public const int MultiPatch = 31;
}
