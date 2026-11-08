using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>The query of the features of a table, and how to read its rows.</summary>
internal sealed class GeoPackageTable(string name, SqliteCommand command, (string Name, string Type)[] columns, GeoBounds? bounds)
{
    public string Name => name;

    public SqliteCommand Command => command;

    /// <summary>Reads the feature of the current row: the geometry, the primary key, then the properties.</summary>
    /// <returns>The feature, or <see langword="null"/> when the bounds of its geometry do not intersect the box of the query.</returns>
    public GeoFeature? ReadFeature(SqliteDataReader reader)
    {
        var geometry = reader.GetValue(0) switch
        {
            DBNull => null,
            byte[] bytes => GeoPackageGeometry.Read(bytes),
            _ => throw new FormatException($"A geometry of the table '{name}' is not a blob."),
        };

        if (bounds is { } box && (geometry?.GetBounds() is not { } geometryBounds || geometryBounds.West > box.East || geometryBounds.East < box.West || geometryBounds.South > box.North || geometryBounds.North < box.South))
            return null;

        var id = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture);
        var properties = new Dictionary<string, object?>(columns.Length, StringComparer.Ordinal);
        for (var i = 0; i < columns.Length; i++)
        {
            if (!reader.IsDBNull(i + 2))
            {
                properties[columns[i].Name] = GeoPackageReader.ReadValue(reader.GetValue(i + 2), columns[i].Type);
            }
        }

        return new GeoFeature(geometry, properties, id);
    }
}
