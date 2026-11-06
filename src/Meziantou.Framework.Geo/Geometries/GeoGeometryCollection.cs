namespace Meziantou.Framework.Geo;

/// <summary>A set of geometries of any types.</summary>
public sealed class GeoGeometryCollection : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoGeometryCollection"/> class.</summary>
    /// <param name="geometries">The geometries.</param>
    public GeoGeometryCollection(IEnumerable<GeoGeometry> geometries)
    {
        Geometries = ToArray(geometries, nameof(geometries));
    }

    /// <summary>Gets the geometries.</summary>
    public IReadOnlyList<GeoGeometry> Geometries { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.GeometryCollection;

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => Geometries.SelectMany(geometry => geometry.GetPositions());
}
