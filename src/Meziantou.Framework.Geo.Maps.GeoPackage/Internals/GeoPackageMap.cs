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
            using var connection = GeoPackageMapFormat.OpenConnection(_path, SqliteOpenMode.ReadOnly);
            return GeoPackageReader.ReadFeatures(connection, _tables[layer], bounds);
        }
        catch (SqliteException exception)
        {
            throw new FormatException($"The {layer} layer of '{_path}' cannot be read.", exception);
        }
    }
}
