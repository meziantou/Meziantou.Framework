using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>The <c>geo</c> metadata of a GeoParquet file: the geometry column to read, and its bbox covering.</summary>
internal sealed record GeoParquetMetadata(string GeometryColumn, GeoParquetCovering? Covering)
{
    public static GeoParquetMetadata Read(IReadOnlyDictionary<string, string> metadata, string? geometryColumn)
    {
        if (!metadata.TryGetValue("geo", out var json))
            throw new FormatException("The Parquet file is not a GeoParquet file: it has no 'geo' metadata.");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("columns", out var columns) || columns.ValueKind is not JsonValueKind.Object)
                throw new FormatException("The 'geo' metadata of the GeoParquet file has no 'columns' object.");

            var name = geometryColumn
                ?? (root.TryGetProperty("primary_column", out var primary) && primary.ValueKind is JsonValueKind.String ? primary.GetString() : null)
                ?? throw new FormatException("The 'geo' metadata of the GeoParquet file has no 'primary_column'.");

            if (!columns.TryGetProperty(name, out var column) || column.ValueKind is not JsonValueKind.Object)
            {
                if (geometryColumn is not null)
                    throw new ArgumentException($"The GeoParquet file has no geometry column named '{geometryColumn}'.", nameof(geometryColumn));

                throw new FormatException($"The 'geo' metadata of the GeoParquet file does not describe its primary column '{name}'.");
            }

            var encoding = column.TryGetProperty("encoding", out var encodingElement) && encodingElement.ValueKind is JsonValueKind.String ? encodingElement.GetString() : null;
            if (!string.Equals(encoding, "WKB", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"The encoding '{encoding}' of the geometry column '{name}' of the GeoParquet file is not supported: only WKB is.");

            return new GeoParquetMetadata(name, ReadCovering(column));
        }
        catch (JsonException ex)
        {
            throw new FormatException("The 'geo' metadata of the GeoParquet file is not valid JSON: " + ex.Message, ex);
        }
    }

    // GeoParquet 1.1: "covering": {"bbox": {"xmin": ["bbox", "xmin"], "ymin": [...], "xmax": [...], "ymax": [...]}}
    private static GeoParquetCovering? ReadCovering(JsonElement column)
    {
        if (!column.TryGetProperty("covering", out var covering) || covering.ValueKind is not JsonValueKind.Object ||
            !covering.TryGetProperty("bbox", out var bbox) || bbox.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var xmin = ReadPath(bbox, "xmin");
        var ymin = ReadPath(bbox, "ymin");
        var xmax = ReadPath(bbox, "xmax");
        var ymax = ReadPath(bbox, "ymax");
        if (xmin is null || ymin is null || xmax is null || ymax is null)
            return null;

        return new GeoParquetCovering(xmin[0], xmin, ymin, xmax, ymax);

        static string[]? ReadPath(JsonElement bbox, string name)
        {
            if (!bbox.TryGetProperty(name, out var path) || path.ValueKind is not JsonValueKind.Array || path.GetArrayLength() is 0)
                return null;

            var result = new string[path.GetArrayLength()];
            var i = 0;
            foreach (var part in path.EnumerateArray())
            {
                if (part.ValueKind is not JsonValueKind.String)
                    return null;

                result[i++] = part.GetString()!;
            }

            return result;
        }
    }
}
