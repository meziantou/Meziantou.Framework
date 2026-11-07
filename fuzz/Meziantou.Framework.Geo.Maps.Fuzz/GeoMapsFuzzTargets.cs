using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Geo.Maps.GeoPackage;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Maps.Fuzz;

/// <summary>
/// A map opens or throws a <see cref="FormatException"/>, and its layers load or throw a <see cref="FormatException"/>:
/// nothing else escapes for an invalid file. What is read can be written in the same format, and the writer is stable:
/// opening what it wrote and writing it again gives the same map.
/// </summary>
[SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The table and column names are constants of the fuzz project; the fuzzed values are parameters")]
public static class GeoMapsFuzzTargets
{
    private const string TemplateTable = "borders";

    // The columns of the template GeoPackage, with each declared type the reader converts
    private static readonly string[] TemplateColumns = ["name", "flag", "count", "big", "ratio", "date", "data", "tiny", "small", "single", "real"];

    private static readonly string[] JsonTokens =
    [
        "{", "}", "[", "]", ":", ",", "\"", "null", "true", "false", "-0", "1e308", "1e400", "-1e400", "\\ud800", "\\u0000", "\"region\"", "\"name\"", "\"attribution\"", "\"bounds\"", "\"lastModified\"", "\"layers\"",
        "\"sources\"", "\"url\"", "\"license\"", "\"2024-02-29T13:45:00.123Z\"", "\"0001-01-01T00:00:00+14:00\"", "\"9999-12-31T23:59:59-14:00\"", "\"https://example.com/\"", "\"Borders\"",
    ];

    private static readonly string[] GeoPackageTokens =
    [
        "GP\u0000\u0001æ\u0010\u0000\u0000", "GP\u0000\u0003æ\u0010\u0000\u0000", "GP\u0000\u0011", "GP\u0000!", "\u0001\u0001\u0000\u0000\u0000", "\u0001\u0003\u0000\u0000\u0000", "\u0001\u0007\u0000\u0000\u0000",
        "\u0000\u0000\u0000\u0000\u0000\u0000ø\u007F", "ÿÿÿ\u007F",
    ];

    private static readonly Lazy<byte[]> Template = new(CreateTemplate);

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("geo-maps-metadata", MetadataTarget) { Seeds = GetMetadataSeeds, Dictionary = JsonTokens },
        new("geo-maps-geopackage", GeoPackageTarget) { Seeds = GetGeoPackageSeeds, Dictionary = [.. GeoPackageTokens, .. JsonTokens] },
    ];

    /// <summary>
    /// <c>data</c>: the <c>metadata.json</c> file of a folder map. The map opens or throws a <see cref="FormatException"/>,
    /// and the metadata written back reads as the same metadata.
    /// </summary>
    private static void MetadataTarget(ReadOnlySpan<byte> data)
    {
        using var directory = new TemporaryFolder();
        var path = Path.Combine(directory.Path, "region");
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "metadata.json"), data.ToArray());

        Map map;
        try
        {
            map = MapFormat.GeoJson.Open(path);
        }
        catch (FormatException)
        {
            return;
        }

        FuzzAssert.Equal(0, map.Layers.Count, "A folder without layer files has layers");
        _ = map.Bounds;
        _ = map.Name;
        AssertStable(map, MapFormat.GeoJson, Path.Combine(directory.Path, "first"), Path.Combine(directory.Path, "second"));
    }

    /// <summary>
    /// <c>geometryKind, column, valueKind, metadataLength (2 bytes, little-endian), metadata, valueLength, value, geometry</c>:
    /// a GeoPackage map written by <see cref="GeoPackageMapFormat"/>, whose metadata, geometry of its first feature and
    /// value of the column <c>TemplateColumns[column]</c> of that feature are replaced (an empty metadata keeps the metadata
    /// of the template). <c>geometryKind</c> stores the
    /// geometry as a blob (0), a text, an integer or NULL; <c>valueKind</c> stores the value as a text (0), an integer, a
    /// real (NULL for NaN, which SQLite does not store), a blob or NULL. The map opens and its layers load, or they throw a <see cref="FormatException"/>. A bounded
    /// load gives the features of a full load that intersect the box, and the map written back is stable.
    /// </summary>
    private static void GeoPackageTarget(ReadOnlySpan<byte> data)
    {
        var geometryKind = FuzzInput.ReadByte(ref data) % 4;
        var column = FuzzInput.Pick(TemplateColumns, FuzzInput.ReadByte(ref data));
        var valueKind = FuzzInput.ReadByte(ref data) % 5;
        var metadataLength = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var metadata = Take(ref data, metadataLength);
        var value = Take(ref data, FuzzInput.ReadByte(ref data));
        var geometry = data.ToArray();

        using var directory = new TemporaryFolder();
        var path = Path.Combine(directory.Path, "region.gpkg");
        File.WriteAllBytes(path, Template.Value);
        using (var connection = Open(path))
        {
            // Without metadata, the map keeps the metadata of the template
            if (!metadata.IsEmpty)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE gpkg_metadata SET metadata = $metadata";
                command.Parameters.AddWithValue("$metadata", FuzzInput.ReadText(metadata));
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"UPDATE \"{TemplateTable}\" SET geom = $geometry, \"{column}\" = $value WHERE fid = (SELECT MIN(fid) FROM \"{TemplateTable}\")";
                command.Parameters.AddWithValue("$geometry", geometryKind switch
                {
                    0 => (object)geometry,
                    1 => FuzzInput.ReadText(geometry),
                    2 => DBNull.Value,
                    _ => ReadInt64(geometry),
                });
                command.Parameters.AddWithValue("$value", valueKind switch
                {
                    0 => (object)FuzzInput.ReadText(value),
                    1 => ReadInt64(value),
                    2 => BitConverter.Int64BitsToDouble(ReadInt64(value)) is var number && !double.IsNaN(number) ? number : DBNull.Value,
                    3 => value.ToArray(),
                    _ => DBNull.Value,
                });
                command.ExecuteNonQuery();
            }
        }

        Map map;
        try
        {
            map = GeoPackageMapFormat.Instance.Open(path);
        }
        catch (FormatException)
        {
            return;
        }

        var layers = new Dictionary<MapLayer, GeoFeatureCollection>();
        try
        {
            foreach (var layer in map.Layers)
            {
                layers[layer] = map.Load(layer);
            }

            _ = map.Bounds;
        }
        catch (FormatException)
        {
            return;
        }

        var box = new GeoBounds(0, 0, 10, 10);
        foreach (var (layer, features) in layers)
        {
            foreach (var feature in features)
            {
                Exercise(feature);
            }

            var expected = features.Where(feature => feature.Geometry?.GetBounds() is { } bounds && Intersects(box, bounds)).Select(Describe).Order(StringComparer.Ordinal);
            var actual = map.Load(layer, box).Select(Describe).Order(StringComparer.Ordinal);
            FuzzAssert.Equal(string.Join('\n', expected), string.Join('\n', actual), $"A bounded load of the {layer} layer does not give the features that intersect the box");
        }

        if (!layers.Values.SelectMany(features => features).All(feature => feature.Geometry is null || IsFinite(feature.Geometry)))
            return;

        AssertStable(map, GeoPackageMapFormat.Instance, Path.Combine(directory.Path, "first.gpkg"), Path.Combine(directory.Path, "second.gpkg"));
    }

    /// <summary>Writes the map, opens it and writes it again: both maps must be the same.</summary>
    private static void AssertStable(Map map, MapFormat format, string firstPath, string secondPath)
    {
        format.Write(firstPath, map);
        var first = format.Open(firstPath);
        FuzzAssert.Equal(DescribeMetadata(map.Metadata, withDate: false), DescribeMetadata(first.Metadata, withDate: false), "The metadata written is not read back as the metadata");
        FuzzAssert.Equal(string.Join(',', map.Layers), string.Join(',', first.Layers), "The layers written are not read back");
        if (map.Metadata.LastModified is { } date)
        {
            FuzzAssert.True(first.Metadata.LastModified is { } read && Math.Abs((read - date).TotalMilliseconds) < 1, "The date written is not read back to the millisecond");
        }

        format.Write(secondPath, first);
        var second = format.Open(secondPath);
        FuzzAssert.Equal(DescribeMetadata(first.Metadata, withDate: true), DescribeMetadata(second.Metadata, withDate: true), "Writing the metadata read from the output of the writer gives other metadata");
        foreach (var layer in first.Layers)
        {
            FuzzAssert.Equal(Describe(first.Load(layer)), Describe(second.Load(layer)), $"Writing the {layer} layer read from the output of the writer gives another layer");
        }
    }

    private static byte[] CreateTemplate()
    {
        using var directory = new TemporaryFolder();
        var path = Path.Combine(directory.Path, "template.gpkg");
        var square = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)]]);
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = "Square",
            ["flag"] = true,
            ["count"] = 42,
            ["big"] = long.MaxValue,
            ["ratio"] = 0.5,
            ["date"] = new DateTimeOffset(2024, 2, 29, 13, 45, 0, TimeSpan.Zero),
            ["data"] = new byte[] { 1, 2, 3 },
        };
        var borders = new GeoFeatureCollection
        {
            new GeoFeature(square, properties),
            new GeoFeature(new GeoPolygon([[new(20, 20), new(30, 20), new(30, 30), new(20, 20)]]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Triangle" }),
        };
        var cities = new GeoFeatureCollection { new GeoFeature(new GeoPoint(new GeoPosition(2, 2)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "City" }) };
        var metadata = new MapMetadata("template") { Name = "Template", Attribution = "None", Sources = [new MapSource("Test", new Uri("https://example.com/"), "CC0")] };
        GeoPackageMapFormat.Instance.Write(path, metadata, new Dictionary<MapLayer, GeoFeatureCollection> { [MapLayer.Borders] = borders, [MapLayer.Cities] = cities });

        // The triggers of the R-tree call the functions of GeoPackage (ST_IsEmpty, ...), which SQLite does not have; the
        // other declared types are those of files written by other tools
        using (var connection = Open(path))
        {
            var triggers = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger'";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    triggers.Add(reader.GetString(0));
                }
            }

            foreach (var statement in triggers.Select(trigger => $"DROP TRIGGER \"{trigger}\"").Concat(new[] { "tiny TINYINT", "small SMALLINT", "single FLOAT", "real REAL" }.Select(definition => $"ALTER TABLE \"{TemplateTable}\" ADD COLUMN {definition}")))
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }

        return File.ReadAllBytes(path);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static ReadOnlySpan<byte> Take(ref ReadOnlySpan<byte> data, int length)
    {
        var result = data[..Math.Min(length, data.Length)];
        data = data[result.Length..];
        return result;
    }

    private static long ReadInt64(ReadOnlySpan<byte> data)
    {
        Span<byte> bytes = stackalloc byte[8];
        data[..Math.Min(8, data.Length)].CopyTo(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    private static bool Intersects(GeoBounds a, GeoBounds b) => b.West <= a.East && b.East >= a.West && b.South <= a.North && b.North >= a.South;

    private static bool IsFinite(GeoGeometry geometry) => geometry.GetPositions().All(position => double.IsFinite(position.Longitude) && double.IsFinite(position.Latitude) && (position.Altitude is not { } altitude || double.IsFinite(altitude)));

    private static void Exercise(GeoFeature feature)
    {
        if (feature.Geometry is not { } geometry)
            return;

        var positions = geometry.GetPositions().ToList();
        FuzzAssert.Equal(positions.Count is 0, geometry.IsEmpty, "IsEmpty does not match the positions of the geometry");
        FuzzAssert.Equal(positions.Count is 0, geometry.GetBounds() is null, "GetBounds does not match the positions of the geometry");
        _ = geometry.LabelPosition;
    }

    private static string DescribeMetadata(MapMetadata metadata, bool withDate)
    {
        var builder = new StringBuilder();
        builder.Append("Region: ").Append(FuzzAssert.Format(metadata.Region)).Append('\n');
        builder.Append("Name: ").Append(FuzzAssert.Format(metadata.Name)).Append('\n');
        builder.Append("Attribution: ").Append(FuzzAssert.Format(metadata.Attribution)).Append('\n');
        builder.Append("Bounds: ").Append(metadata.Bounds is { } bounds ? string.Create(CultureInfo.InvariantCulture, $"{bounds.West:R} {bounds.South:R} {bounds.East:R} {bounds.North:R}") : "none").Append('\n');
        if (withDate)
        {
            builder.Append("LastModified: ").Append(metadata.LastModified?.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (var source in metadata.Sources)
        {
            builder.Append("Source: ").Append(FuzzAssert.Format(source.Name)).Append(' ').Append(FuzzAssert.Format(source.Url?.OriginalString)).Append(' ').Append(FuzzAssert.Format(source.License)).Append('\n');
        }

        return builder.ToString();
    }

    private static string Describe(IEnumerable<GeoFeature> features) => string.Join('\n', features.Select(Describe));

    private static string Describe(GeoFeature feature)
    {
        var builder = new StringBuilder();
        builder.Append(feature.Geometry is null ? "null" : WellKnownText.Format(feature.Geometry));
        foreach (var (name, value) in feature.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append("; ").Append(name).Append(" = ").Append(value?.GetType().Name).Append(' ').Append(FuzzAssert.Format(value switch
            {
                byte[] bytes => Convert.ToHexString(bytes),
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                float number => number.ToString("R", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
                _ => value?.ToString(),
            }));
        }

        return builder.ToString();
    }

    private static IEnumerable<FuzzSeed> GetMetadataSeeds()
    {
        yield return FuzzSeed.FromText("full", """{"region":"CH","name":"Switzerland","attribution":"© OpenStreetMap contributors","bounds":[5.9,45.8,10.5,47.8],"lastModified":"2024-02-29T13:45:00.123Z","layers":{"Borders":1},"sources":[{"name":"country-levels","url":"https://example.com/","license":"ODbL"}]}""");
        yield return FuzzSeed.FromText("minimal", """{"region":"world"}""");
        yield return FuzzSeed.FromText("empty", "{}");
        yield return FuzzSeed.FromText("offset", """{"lastModified":"2024-02-29T13:45:00+05:30","sources":[{"name":"a"},{"url":"relative"},null]}""");
    }

    private static IEnumerable<FuzzSeed> GetGeoPackageSeeds()
    {
        var metadata = Encoding.UTF8.GetBytes("""{"region":"region","name":"Region","bounds":[0,0,40,40],"lastModified":"2024-02-29T13:45:00.123Z","sources":[{"name":"Test"}]}""");
        byte[] length = [(byte)metadata.Length, (byte)(metadata.Length >> 8)];

        var polygon = ToGeoPackage(new GeoPolygon([[new(0, 0), new(5, 0), new(5, 5), new(0, 0)]]), envelope: true);
        var point = ToGeoPackage(new GeoPoint(new GeoPosition(1, 1)), envelope: false);
        byte[] text = Encoding.UTF8.GetBytes("text");
        yield return new FuzzSeed("polygon", (byte[])[0, 0, 0, .. length, .. metadata, (byte)text.Length, .. text, .. polygon]);
        yield return new FuzzSeed("point-integer", (byte[])[0, 2, 1, .. length, .. metadata, 8, 1, 2, 3, 4, 5, 6, 7, 8, .. point]);
        yield return new FuzzSeed("real", (byte[])[0, 4, 2, .. length, .. metadata, 8, 0, 0, 0, 0, 0, 0, 0xF0, 0x3F, .. polygon]);
        yield return new FuzzSeed("tiny", (byte[])[0, 7, 1, .. length, .. metadata, 1, 0x80, .. polygon]);
        yield return new FuzzSeed("date", (byte[])[0, 5, 0, .. length, .. metadata, 10, .. "2024-02-29"u8, .. polygon]);
        yield return new FuzzSeed("blob", (byte[])[0, 6, 3, .. length, .. metadata, 2, 0xFF, 0x00, .. polygon]);
        yield return new FuzzSeed("null", (byte[])[2, 0, 4, 0, 0, 0]);
        yield return new FuzzSeed("empty-flag", (byte[])[0, 0, 4, 0, 0, 0, .. "GP\u0000\u0011"u8, 0xE6, 0x10, 0, 0]);
    }

    // GP, version 0, little-endian with an optional envelope [minx, maxx, miny, maxy], SRS 4326, then the WKB
    private static byte[] ToGeoPackage(GeoGeometry geometry, bool envelope)
    {
        var bounds = geometry.GetBounds()!.Value;
        var header = new List<byte> { (byte)'G', (byte)'P', 0, (byte)(envelope ? 0b11 : 0b01), 0xE6, 0x10, 0, 0 };
        if (envelope)
        {
            foreach (var value in new[] { bounds.West, bounds.East, bounds.South, bounds.North })
            {
                header.AddRange(BitConverter.GetBytes(value));
            }
        }

        return [.. header, .. WellKnownBinary.Write(geometry)];
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder() => Path = Directory.CreateTempSubdirectory("mfw-geo-maps-fuzz-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
