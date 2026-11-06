using System.Collections;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>Reads and writes GeoJSON (RFC 7946): a <c>FeatureCollection</c>, a <c>Feature</c> or a geometry.</summary>
/// <remarks>
/// <para>
/// The reader keeps the altitudes, ignores the <c>bbox</c> and the foreign members, except the <c>name</c> of a
/// <c>FeatureCollection</c>, which becomes <see cref="GeoFeatureCollection.Name"/>. The properties are a
/// <see cref="string"/>, a <see cref="double"/>, a <see cref="bool"/>, <see langword="null"/>, or a
/// <see cref="JsonElement"/> for an object or an array. A numeric <c>id</c> becomes its JSON text.
/// </para>
/// <para>
/// The writer writes the identifiers as strings, the numbers as JSON numbers (<see langword="null"/> when they are not
/// finite), a <see cref="JsonElement"/> as it is, a dictionary as an object, a collection as an array, and the other
/// values as their text.
/// </para>
/// </remarks>
public static class GeoJson
{
    /// <summary>Reads the features of a GeoJSON document.</summary>
    /// <param name="json">The GeoJSON text.</param>
    /// <returns>The features of a <c>FeatureCollection</c>, the feature of a <c>Feature</c>, or a feature without properties for a geometry.</returns>
    /// <exception cref="FormatException">The text is not valid JSON or not GeoJSON.</exception>
    public static GeoFeatureCollection Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonText.Parse(json);
            return ReadFeatures(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The text is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Reads the features of a GeoJSON document.</summary>
    /// <param name="utf8Json">The GeoJSON document, encoded in UTF-8. It is left open.</param>
    /// <returns>The features of a <c>FeatureCollection</c>, the feature of a <c>Feature</c>, or a feature without properties for a geometry.</returns>
    /// <exception cref="FormatException">The document is not valid JSON or not GeoJSON.</exception>
    public static GeoFeatureCollection Read(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return ReadFeatures(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The document is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Reads a GeoJSON geometry, such as <c>{"type":"Point","coordinates":[2.35,48.86]}</c>.</summary>
    /// <param name="json">The GeoJSON text of a geometry.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="FormatException">The text is not valid JSON or not a GeoJSON geometry.</exception>
    public static GeoGeometry ParseGeometry(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonText.Parse(json);
            return ReadGeometry(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The text is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Writes features as a GeoJSON <c>FeatureCollection</c>.</summary>
    /// <param name="features">The features.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The GeoJSON text.</returns>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static string Format(GeoFeatureCollection features, GeoJsonWriterOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(features, stream, options);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Writes features as a GeoJSON <c>FeatureCollection</c>, encoded in UTF-8.</summary>
    /// <param name="features">The features.</param>
    /// <param name="utf8Json">The stream to write to. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static void Write(GeoFeatureCollection features, Stream utf8Json, GeoJsonWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(utf8Json);
        GeoValidation.EnsureFinite(features, nameof(features));
        options ??= new GeoJsonWriterOptions();
        using var writer = new Utf8JsonWriter(utf8Json, CreateWriterOptions(options));
        WriteFeatures(writer, features, options);
    }

    /// <summary>Writes a geometry as GeoJSON.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The GeoJSON text of the geometry.</returns>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static string FormatGeometry(GeoGeometry geometry, GeoJsonWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        GeoValidation.EnsureFinite(geometry, nameof(geometry));
        options ??= new GeoJsonWriterOptions();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CreateWriterOptions(options)))
        {
            WriteGeometry(writer, geometry, options);
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    // The text of a file: only the characters JSON requires are escaped, so the names in other scripts stay readable
    private static JsonWriterOptions CreateWriterOptions(GeoJsonWriterOptions options) => new() { Indented = options.Indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static GeoFeatureCollection ReadFeatures(JsonElement root)
    {
        switch (GetType(root))
        {
            case "FeatureCollection":
                if (!root.TryGetProperty("features", out var features) || features.ValueKind is not JsonValueKind.Array)
                    throw new FormatException("A FeatureCollection must have a 'features' array.");

                var collection = new GeoFeatureCollection(features.EnumerateArray().Select(ReadFeature));
                if (root.TryGetProperty("name", out var name) && name.ValueKind is JsonValueKind.String)
                {
                    collection.Name = name.GetString();
                }

                return collection;

            case "Feature":
                return [ReadFeature(root)];

            default:
                return [new GeoFeature(ReadGeometry(root))];
        }
    }

    private static GeoFeature ReadFeature(JsonElement element)
    {
        if (GetType(element) is not "Feature")
            throw new FormatException("The items of a FeatureCollection must be features.");

        var geometry = element.TryGetProperty("geometry", out var geometryElement) && geometryElement.ValueKind is not JsonValueKind.Null ? ReadGeometry(geometryElement) : null;
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (element.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in propertiesElement.EnumerateObject())
            {
                properties[property.Name] = ReadValue(property.Value);
            }
        }

        return new GeoFeature(geometry, properties, element.TryGetProperty("id", out var id) ? ReadId(id) : null);
    }

    /// <summary>Reads an identifier: a string, or the JSON text of a number.</summary>
    internal static string? ReadId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => id.GetString(),
        JsonValueKind.Number => id.GetRawText(),
        _ => null,
    };

    /// <summary>Reads the value of a property.</summary>
    internal static object? ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.Clone(),
    };

    /// <summary>Reads a GeoJSON geometry.</summary>
    internal static GeoGeometry ReadGeometry(JsonElement element)
    {
        var type = GetType(element);
        if (type is "GeometryCollection")
        {
            if (!element.TryGetProperty("geometries", out var geometries) || geometries.ValueKind is not JsonValueKind.Array)
                throw new FormatException("A GeometryCollection must have a 'geometries' array.");

            return new GeoGeometryCollection([.. geometries.EnumerateArray().Select(ReadGeometry)]);
        }

        if (!element.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind is not JsonValueKind.Array)
            throw new FormatException($"A {type} must have a 'coordinates' array.");

        return type switch
        {
            "Point" => new GeoPoint(ReadPosition(coordinates)),
            "MultiPoint" => new GeoMultiPoint(ReadPositions(coordinates)),
            "LineString" => new GeoLineString(ReadPositions(coordinates)),
            "MultiLineString" => new GeoMultiLineString([.. ReadArray(coordinates).Select(line => new GeoLineString(ReadPositions(line)))]),
            "Polygon" => ReadPolygon(coordinates),
            "MultiPolygon" => new GeoMultiPolygon([.. ReadArray(coordinates).Select(ReadPolygon)]),
            _ => throw new FormatException($"'{type}' is not a GeoJSON type."),
        };
    }

    private static GeoPolygon ReadPolygon(JsonElement rings) => new([.. ReadArray(rings).Select(ReadPositions)]);

    private static List<GeoPosition> ReadPositions(JsonElement positions) => [.. ReadArray(positions).Select(ReadPosition)];

    private static JsonElement.ArrayEnumerator ReadArray(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Array)
            throw new FormatException("The coordinates of a GeoJSON geometry must be nested arrays.");

        return element.EnumerateArray();
    }

    // A position is [longitude, latitude], possibly followed by an altitude; the other values are ignored
    private static GeoPosition ReadPosition(JsonElement position)
    {
        if (position.ValueKind is not JsonValueKind.Array || position.GetArrayLength() < 2 || position[0].ValueKind is not JsonValueKind.Number || position[1].ValueKind is not JsonValueKind.Number)
            throw new FormatException("A GeoJSON position must be an array of at least two numbers.");

        double? altitude = position.GetArrayLength() > 2 && position[2].ValueKind is JsonValueKind.Number ? position[2].GetDouble() : null;
        return new GeoPosition(position[0].GetDouble(), position[1].GetDouble(), altitude);
    }

    private static string GetType(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String)
            throw new FormatException("A GeoJSON object must have a 'type' member.");

        return type.GetString()!;
    }

    private static void WriteFeatures(Utf8JsonWriter writer, GeoFeatureCollection features, GeoJsonWriterOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "FeatureCollection");
        if (features.Name is not null)
        {
            writer.WriteString("name", features.Name);
        }

        if (options.WriteBoundingBoxes && features.GetBounds() is { } bounds)
        {
            WriteBounds(writer, bounds);
        }

        writer.WriteStartArray("features");
        foreach (var feature in features)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "Feature");
            if (feature.Id is not null)
            {
                writer.WriteString("id", feature.Id);
            }

            if (options.WriteBoundingBoxes && feature.Geometry?.GetBounds() is { } featureBounds)
            {
                WriteBounds(writer, featureBounds);
            }

            writer.WritePropertyName("geometry");
            if (feature.Geometry is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WriteGeometry(writer, feature.Geometry, options);
            }

            writer.WriteStartObject("properties");
            foreach (var (name, value) in feature.Properties)
            {
                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteGeometry(Utf8JsonWriter writer, GeoGeometry geometry, GeoJsonWriterOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", geometry.Type.ToString());
        if (options.WriteBoundingBoxes && geometry.GetBounds() is { } bounds)
        {
            WriteBounds(writer, bounds);
        }

        if (geometry is GeoGeometryCollection collection)
        {
            writer.WriteStartArray("geometries");
            foreach (var item in collection.Geometries)
            {
                WriteGeometry(writer, item, options);
            }

            writer.WriteEndArray();
        }
        else
        {
            writer.WritePropertyName("coordinates");
            switch (geometry)
            {
                case GeoPoint point:
                    WritePosition(writer, point.Position);
                    break;
                case GeoMultiPoint multiPoint:
                    WritePositions(writer, multiPoint.Positions);
                    break;
                case GeoLineString lineString:
                    WritePositions(writer, lineString.Positions);
                    break;
                case GeoMultiLineString multiLineString:
                    writer.WriteStartArray();
                    foreach (var line in multiLineString.LineStrings)
                    {
                        WritePositions(writer, line.Positions);
                    }

                    writer.WriteEndArray();
                    break;
                case GeoPolygon polygon:
                    WritePolygon(writer, polygon);
                    break;
                case GeoMultiPolygon multiPolygon:
                    writer.WriteStartArray();
                    foreach (var item in multiPolygon.Polygons)
                    {
                        WritePolygon(writer, item);
                    }

                    writer.WriteEndArray();
                    break;
            }
        }

        writer.WriteEndObject();
    }

    private static void WritePolygon(Utf8JsonWriter writer, GeoPolygon polygon)
    {
        writer.WriteStartArray();
        foreach (var ring in polygon.Rings)
        {
            WritePositions(writer, ring);
        }

        writer.WriteEndArray();
    }

    private static void WritePositions(Utf8JsonWriter writer, IReadOnlyList<GeoPosition> positions)
    {
        writer.WriteStartArray();
        foreach (var position in positions)
        {
            WritePosition(writer, position);
        }

        writer.WriteEndArray();
    }

    private static void WritePosition(Utf8JsonWriter writer, GeoPosition position)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(position.Longitude);
        writer.WriteNumberValue(position.Latitude);
        if (position.Altitude is { } value)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteBounds(Utf8JsonWriter writer, GeoBounds bounds)
    {
        writer.WriteStartArray("bbox");
        writer.WriteNumberValue(bounds.West);
        writer.WriteNumberValue(bounds.South);
        writer.WriteNumberValue(bounds.East);
        writer.WriteNumberValue(bounds.North);
        writer.WriteEndArray();
    }

    /// <summary>Writes the value of a property.</summary>
    internal static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case var _ when GeoPropertyValue.TryGetNumber(value, out var number):
                if (double.IsFinite(number))
                {
                    if (value is decimal decimalValue)
                    {
                        writer.WriteNumberValue(decimalValue);
                    }
                    else if (value is long or int or short or sbyte or byte or ulong or uint or ushort)
                    {
                        writer.WriteRawValue(GeoPropertyValue.ToText(value)!);
                    }
                    else
                    {
                        writer.WriteNumberValue(number);
                    }
                }
                else
                {
                    writer.WriteNullValue();
                }

                break;
            case IEnumerable<KeyValuePair<string, object?>> dictionary:
                writer.WriteStartObject();
                foreach (var (name, item) in dictionary)
                {
                    writer.WritePropertyName(name);
                    WriteValue(writer, item);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(GeoPropertyValue.ToText(value));
                break;
        }
    }
}
