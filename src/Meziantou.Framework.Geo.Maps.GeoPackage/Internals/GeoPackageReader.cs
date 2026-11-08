using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Maps.GeoPackage.Internals;

/// <summary>Reads the metadata of a map and its layers; the features of a layer are read by Meziantou.Framework.Geo.GeoPackage.</summary>
internal static class GeoPackageReader
{
    public static string? ReadMetadata(SqliteConnection connection)
    {
        if (!TableExists(connection, "gpkg_metadata") || !TableExists(connection, "gpkg_metadata_reference"))
            return null;

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.metadata FROM gpkg_metadata m JOIN gpkg_metadata_reference r ON r.md_file_id = m.id
            WHERE r.reference_scope = 'geopackage' AND m.md_standard_uri = $uri AND m.mime_type = 'application/json'
            ORDER BY m.id LIMIT 1
            """;
        command.Parameters.AddWithValue("$uri", GeoPackageMapFormat.MetadataStandardUri);
        return command.ExecuteScalar() as string;
    }

    public static Dictionary<MapLayer, string> ReadLayers(SqliteConnection connection)
    {
        if (!TableExists(connection, "gpkg_contents"))
            throw new FormatException("The file has no gpkg_contents table.");

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT table_name FROM gpkg_contents WHERE data_type = 'features'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var layers = new Dictionary<MapLayer, string>();
        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            if (tables.TryGetValue(GeoPackageMapFormat.GetTableName(layer), out var table))
            {
                layers[layer] = table;
            }
        }

        return layers;
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE name = $name AND type IN ('table', 'view')";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }
}
