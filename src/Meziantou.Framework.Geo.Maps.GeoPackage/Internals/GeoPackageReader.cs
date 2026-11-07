using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Maps.GeoPackage.Internals;

[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The table and column names come from the GeoPackage tables and are quoted by Quote; the values are parameters")]
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

    public static List<GeoFeature> ReadFeatures(SqliteConnection connection, string table, GeoBounds? bounds)
    {
        string geometryColumn;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT column_name FROM gpkg_geometry_columns WHERE table_name = $table";
            command.Parameters.AddWithValue("$table", table);
            geometryColumn = command.ExecuteScalar() as string ?? throw new FormatException($"The table '{table}' has no geometry column in gpkg_geometry_columns.");
        }

        var columns = new List<(string Name, string Type)>();
        string? primaryKey = null;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, type, pk FROM pragma_table_info($table) ORDER BY cid";
            command.Parameters.AddWithValue("$table", table);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (reader.GetInt64(2) == 1)
                {
                    primaryKey = name;
                }
                else if (!name.Equals(geometryColumn, StringComparison.OrdinalIgnoreCase))
                {
                    columns.Add((name, reader.GetString(1).ToUpperInvariant()));
                }
            }
        }

        var select = "SELECT " + Quote(geometryColumn) + string.Concat(columns.Select(column => ", " + Quote(column.Name))) + " FROM " + Quote(table);
        var rtree = "rtree_" + table + "_" + geometryColumn;
        using var query = connection.CreateCommand();
        if (bounds is { } box && primaryKey is not null && TableExists(connection, rtree))
        {
            // The R-tree stores 32-bit floats, rounded outward: the map keeps the features whose bounds intersect the box
            query.CommandText = select + $" WHERE {Quote(primaryKey)} IN (SELECT id FROM {Quote(rtree)} WHERE minx <= $east AND maxx >= $west AND miny <= $north AND maxy >= $south)";
            query.Parameters.AddWithValue("$west", box.West);
            query.Parameters.AddWithValue("$south", box.South);
            query.Parameters.AddWithValue("$east", box.East);
            query.Parameters.AddWithValue("$north", box.North);
        }
        else
        {
            query.CommandText = select;
        }

        if (primaryKey is not null)
        {
            query.CommandText += " ORDER BY " + Quote(primaryKey);
        }

        var features = new List<GeoFeature>();
        using var rows = query.ExecuteReader();
        while (rows.Read())
        {
            var geometry = rows.GetValue(0) switch
            {
                DBNull => null,
                byte[] bytes => GeoPackageGeometry.Read(bytes),
                _ => throw new FormatException($"A geometry of the table '{table}' is not a blob."),
            };
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Count; i++)
            {
                if (!rows.IsDBNull(i + 1))
                {
                    properties[columns[i].Name] = ReadValue(rows.GetValue(i + 1), columns[i].Type);
                }
            }

            features.Add(new GeoFeature(geometry, properties));
        }

        return features;
    }

    // SQLite stores a value as an integer, a real, a text or a blob: the declared type of the column gives its .NET type
    private static object ReadValue(object value, string type)
    {
        switch (value)
        {
            case long integer:
                return type switch
                {
                    "BOOLEAN" => integer != 0,
                    "TINYINT" when integer is >= sbyte.MinValue and <= sbyte.MaxValue => (sbyte)integer,
                    "SMALLINT" when integer is >= short.MinValue and <= short.MaxValue => (short)integer,
                    "MEDIUMINT" when integer is >= int.MinValue and <= int.MaxValue => (int)integer,
                    "FLOAT" => (float)integer,
                    "DOUBLE" or "REAL" => (double)integer,
                    _ => integer,
                };

            case double real:
                return type is "FLOAT" ? (float)real : real;

            case string text when type is "DATETIME":
                return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : text;

            default:
                return value;
        }
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE name = $name AND type IN ('table', 'view')";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
