using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Internals;

[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The table and column names come from the GeoPackage tables and are quoted by Quote; the values are parameters")]
internal static class GeoPackageReader
{
    public static List<string> GetTables(SqliteConnection connection)
    {
        if (!TableExists(connection, "gpkg_contents"))
            throw new FormatException("The database is not a GeoPackage: it has no gpkg_contents table.");

        var tables = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM gpkg_contents WHERE data_type = 'features' ORDER BY rowid";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    /// <summary>Prepares the query of the features of a table, in the order of its primary key, and only the ones of a box when the table has an R-tree.</summary>
    public static GeoPackageTable OpenTable(SqliteConnection connection, string? tableName, GeoBounds? bounds)
    {
        var tables = GetTables(connection);
        string table;
        if (tableName is null)
        {
            table = tables.Count switch
            {
                0 => throw new FormatException("The GeoPackage has no feature table."),
                1 => tables[0],
                _ => throw new FormatException($"The GeoPackage has several feature tables ({string.Join(", ", tables)}): give the name of the one to read."),
            };
        }
        else
        {
            table = tables.Find(name => string.Equals(name, tableName, StringComparison.Ordinal))
                ?? tables.Find(name => string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"The GeoPackage has no feature table named '{tableName}'; it has: {string.Join(", ", tables)}.", nameof(tableName));
        }

        string geometryColumn;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT column_name FROM gpkg_geometry_columns WHERE table_name = $table";
            command.Parameters.AddWithValue("$table", table);
            geometryColumn = command.ExecuteScalar() as string ?? throw new FormatException($"The table '{table}' has no geometry column in gpkg_geometry_columns.");
        }

        var columns = new List<(string Name, string Type)>();
        string? primaryKey = null;
        var hasGeometryColumn = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, type, pk FROM pragma_table_info($table) ORDER BY cid";
            command.Parameters.AddWithValue("$table", table);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (name.Equals(geometryColumn, StringComparison.OrdinalIgnoreCase))
                {
                    hasGeometryColumn = true;
                }
                else if (reader.GetInt64(2) == 1 && primaryKey is null)
                {
                    primaryKey = name;
                }
                else
                {
                    columns.Add((name, reader.GetString(1).ToUpperInvariant()));
                }
            }
        }

        if (!hasGeometryColumn)
            throw new FormatException($"The table '{table}' has no column '{geometryColumn}'.");

        var select = "SELECT " + Quote(geometryColumn) + ", " + (primaryKey is null ? "NULL" : Quote(primaryKey)) + string.Concat(columns.Select(column => ", " + Quote(column.Name))) + " FROM " + Quote(table);
        var rtree = "rtree_" + table + "_" + geometryColumn;
        var query = connection.CreateCommand();
        if (bounds is { } box && primaryKey is not null && TableExists(connection, rtree))
        {
            // The R-tree stores 32-bit floats, rounded outward: the features whose bounds intersect the box are kept, maybe a few more
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

        return new GeoPackageTable(table, query, [.. columns], bounds);
    }

    // SQLite stores a value as an integer, a real, a text or a blob: the declared type of the column gives its .NET type
    public static object ReadValue(object value, string type)
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

            case string text when type is "DATE":
                return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : text;

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
