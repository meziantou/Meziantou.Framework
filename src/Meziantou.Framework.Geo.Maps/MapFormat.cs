using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Meziantou.Framework.Geo.Maps.Internals;

namespace Meziantou.Framework.Geo.Maps;

/// <summary>A format of the maps of a <see cref="MapCatalog"/>: how the layers of a region are stored, read and written.</summary>
/// <remarks>
/// <para>
/// <see cref="FlatGeobuf"/> and <see cref="GeoJson"/> store a map as a folder named after the region, with a file per
/// layer and a <c>metadata.json</c> file. Other formats come from other packages, such as the <c>GeoPackageMapFormat</c>
/// of <c>Meziantou.Framework.Geo.Maps.GeoPackage</c>, which stores a map as one GeoPackage file.
/// </para>
/// <para>Derive from this class to read and write another format.</para>
/// </remarks>
public abstract class MapFormat
{
    /// <summary>Initializes a new instance of the <see cref="MapFormat"/> class.</summary>
    protected MapFormat()
    {
    }

    /// <summary>Gets the format that stores a map as a folder with a FlatGeobuf file per layer, such as <c>borders.fgb</c>: compact, with a spatial index. It is the format of the generator by default.</summary>
    public static MapFormat FlatGeobuf { get; } = new FolderMapFormat(FolderMapFormat.Encoding.FlatGeobuf);

    /// <summary>
    /// Gets the format that stores a map as a folder with a GeoJSON file per layer, such as <c>borders.geojson</c>: text
    /// that web libraries read, but several times larger than FlatGeobuf, and without spatial index, so a box query reads
    /// the whole layer.
    /// </summary>
    public static MapFormat GeoJson { get; } = new FolderMapFormat(FolderMapFormat.Encoding.GeoJson);

    /// <summary>Gets the name of the format, such as <c>FlatGeobuf</c>.</summary>
    public abstract string Name { get; }

    /// <summary>Gets the path of the map of a region in the folder of a catalog, such as the folder <c>maps/France</c> or the file <c>maps/France.gpkg</c>.</summary>
    /// <param name="directory">The folder of the catalog.</param>
    /// <param name="region">The name of the region.</param>
    /// <returns>The path; the map may not exist.</returns>
    public abstract string GetPath(string directory, string region);

    /// <summary>Gets the region of the map at a path, when the file or folder at this path is a map of this format.</summary>
    /// <param name="path">The path of a file or a folder of a catalog.</param>
    /// <param name="region">The name of the region, or <see langword="null"/> when the path is not a map of this format.</param>
    /// <returns><see langword="true"/> when the path is a map of this format; otherwise, <see langword="false"/>.</returns>
    public abstract bool TryGetRegion(string path, [NotNullWhen(true)] out string? region);

    /// <summary>Opens the map at a path. The layers are read when they are loaded.</summary>
    /// <param name="path">The path of the map, one that <see cref="TryGetRegion"/> accepts.</param>
    /// <returns>The map.</returns>
    /// <exception cref="FormatException">The metadata of the map is not valid.</exception>
    public abstract Map Open(string path);

    /// <summary>Writes a map: its layers, and its metadata. The layers of a map that was at this path, and that <paramref name="layers"/> does not have, are removed.</summary>
    /// <param name="path">The path of the map, such as the one <see cref="GetPath"/> returns.</param>
    /// <param name="metadata">The description of the map.</param>
    /// <param name="layers">The features of each layer; a layer that is not there is not written.</param>
    public abstract void Write(string path, MapMetadata metadata, IReadOnlyDictionary<MapLayer, GeoFeatureCollection> layers);

    /// <summary>Writes a map in this format, such as a map of another format to convert it: every layer is loaded.</summary>
    /// <param name="path">The path of the map, such as the one <see cref="GetPath"/> returns.</param>
    /// <param name="map">The map.</param>
    public void Write(string path, Map map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Write(path, map.Metadata, map.Layers.ToDictionary(layer => layer, map.Load));
    }

    /// <summary>Gets the name of a layer in the files of a map, such as <c>borders</c>, <c>states-provinces</c> or <c>marine-areas</c>.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns>The name, in lowercase ASCII letters and hyphens.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is not a layer.</exception>
    public static string GetLayerName(MapLayer layer) => layer switch
    {
        MapLayer.Borders => "borders",
        MapLayer.StatesProvinces => "states-provinces",
        MapLayer.Cities => "cities",
        MapLayer.Rivers => "rivers",
        MapLayer.Lakes => "lakes",
        MapLayer.Coastline => "coastline",
        MapLayer.Land => "land",
        MapLayer.Ocean => "ocean",
        MapLayer.MarineAreas => "marine-areas",
        MapLayer.PhysicalRegions => "physical-regions",
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "The value is not a layer."),
    };

    /// <summary>
    /// Writes the metadata of a map as JSON, the content of the <c>metadata.json</c> file of the folder formats:
    /// <c>region</c>, <c>name</c>, <c>attribution</c>, <c>bounds</c> (west, south, east, north), <c>lastModified</c>,
    /// the number of features of each layer, <c>layers</c>, and the <c>sources</c> (<c>name</c>, <c>url</c>, <c>license</c>).
    /// </summary>
    /// <param name="metadata">The description of the map.</param>
    /// <param name="layers">The layers of the map.</param>
    /// <returns>The indented JSON, with <c>\n</c> line endings.</returns>
    protected static string FormatMetadata(MapMetadata metadata, IReadOnlyDictionary<MapLayer, GeoFeatureCollection> layers)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layers);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("region", metadata.Region);
            if (metadata.Name is not null)
            {
                writer.WriteString("name", metadata.Name);
            }

            if (metadata.Attribution is not null)
            {
                writer.WriteString("attribution", metadata.Attribution);
            }

            if (metadata.Bounds is { } bounds)
            {
                writer.WriteStartArray("bounds");
                writer.WriteNumberValue(bounds.West);
                writer.WriteNumberValue(bounds.South);
                writer.WriteNumberValue(bounds.East);
                writer.WriteNumberValue(bounds.North);
                writer.WriteEndArray();
            }

            if (metadata.LastModified is { } lastModified)
            {
                writer.WriteString("lastModified", FormatDate(lastModified));
            }

            writer.WriteStartObject("layers");
            foreach (var (layer, features) in layers.OrderBy(pair => pair.Key))
            {
                writer.WriteNumber(layer.ToString(), features.Count);
            }

            writer.WriteEndObject();
            if (metadata.Sources.Count > 0)
            {
                writer.WriteStartArray("sources");
                foreach (var source in metadata.Sources)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", source.Name);
                    if (source.Url is not null)
                    {
                        writer.WriteString("url", source.Url.OriginalString);
                    }

                    if (source.License is not null)
                    {
                        writer.WriteString("license", source.License);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length) + "\n";
    }

    /// <summary>Reads the metadata of a map written by <see cref="FormatMetadata"/>. The missing members are <see langword="null"/>.</summary>
    /// <param name="region">The name of the region, used when the JSON has no <c>region</c>.</param>
    /// <param name="json">The JSON.</param>
    /// <returns>The description of the map.</returns>
    /// <exception cref="FormatException">The JSON is not valid, or its bounds are not finite numbers.</exception>
    protected static MapMetadata ParseMetadata(string region, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
                throw new FormatException("The metadata of a map must be a JSON object.");

            GeoBounds? bounds = null;
            if (root.TryGetProperty("bounds", out var box) && box.ValueKind is JsonValueKind.Array && box.GetArrayLength() == 4)
            {
                bounds = new GeoBounds(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble());

                // A number out of the range of a double, such as 1e400, reads as an infinity
                if (!double.IsFinite(bounds.Value.West) || !double.IsFinite(bounds.Value.South) || !double.IsFinite(bounds.Value.East) || !double.IsFinite(bounds.Value.North))
                    throw new FormatException("The bounds of the map must be finite numbers.");
            }

            DateTimeOffset? lastModified = null;
            if (GetString(root, "lastModified") is { } date)
            {
                lastModified = DateTimeOffset.Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            }

            var sources = new List<MapSource>();
            if (root.TryGetProperty("sources", out var items) && items.ValueKind is JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object && GetString(item, "name") is { } sourceName)
                    {
                        var url = GetString(item, "url") is { } address && Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null;
                        sources.Add(new MapSource(sourceName, url, GetString(item, "license")));
                    }
                }
            }

            return new MapMetadata(GetString(root, "region") is { Length: > 0 } name ? name : region)
            {
                Sources = sources,
                Name = GetString(root, "name"),
                Attribution = GetString(root, "attribution"),
                Bounds = bounds,
                LastModified = lastModified,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new FormatException("The metadata of the map is not valid JSON.", exception);
        }

        static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Writes a date as the metadata does: in ISO 8601, in UTC, to the millisecond, such as <c>2026-11-06T00:00:00.000Z</c>, the form of the dates of GeoPackage.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The text.</returns>
    protected static string FormatDate(DateTimeOffset date) => date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
