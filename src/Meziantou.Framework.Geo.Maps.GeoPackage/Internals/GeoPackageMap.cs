using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Maps.GeoPackage.Internals;

internal sealed class GeoPackageMap : Map
{
    private readonly string _path;
    private readonly Dictionary<MapLayer, string> _tables;

    public GeoPackageMap(string path, MapMetadata metadata, Dictionary<MapLayer, string> tables)
        : base(metadata)
    {
        _path = path;
        _tables = tables;
    }

    public override bool HasLayer(MapLayer layer) => _tables.ContainsKey(layer);

    protected override IEnumerable<GeoFeature> LoadLayer(MapLayer layer, GeoBounds? bounds)
    {
        try
        {
            // The features of the layer, read by the GeoPackage reader of Meziantou.Framework.Geo.GeoPackage, without the
            // identifier it gives them (the primary key of the table): the features of a map have none, in every format
            using var connection = GeoPackageMapFormat.OpenConnection(_path, SqliteOpenMode.ReadOnly);
            var features = global::Meziantou.Framework.Geo.GeoPackage.Read(connection, _tables[layer], new GeoPackageReaderOptions { Bounds = bounds });
            return [.. features.Select(feature => new GeoFeature(feature.Geometry, feature.Properties))];
        }
        catch (SqliteException exception)
        {
            throw new FormatException($"The {layer} layer of '{_path}' cannot be read.", exception);
        }
    }
}
