using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.Charts;

/// <summary>Reads GeoJSON (RFC 7946): a <c>FeatureCollection</c>, a <c>Feature</c> or a geometry.</summary>
public static class GeoJson
{
    /// <summary>Reads the features of a GeoJSON document.</summary>
    /// <param name="json">The GeoJSON text.</param>
    /// <returns>The features of a <c>FeatureCollection</c>, the feature of a <c>Feature</c>, or a feature without properties for a geometry.</returns>
    /// <exception cref="FormatException">The text is not valid JSON or not GeoJSON.</exception>
    public static IReadOnlyList<GeoFeature> ParseFeatures(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
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
    public static IReadOnlyList<GeoFeature> ParseFeatures(Stream utf8Json)
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

    private static List<GeoFeature> ReadFeatures(JsonElement root)
    {
        switch (GetType(root))
        {
            case "FeatureCollection":
                if (!root.TryGetProperty("features", out var features) || features.ValueKind is not JsonValueKind.Array)
                    throw new FormatException("A FeatureCollection must have a 'features' array.");

                return [.. features.EnumerateArray().Select(ReadFeature)];

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

        string? id = null;
        if (element.TryGetProperty("id", out var idElement))
        {
            id = idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString(),
                JsonValueKind.Number => idElement.GetDouble().ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        return new GeoFeature(geometry, properties, id);
    }

    private static object? ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.Clone(),
    };

    private static GeoGeometry ReadGeometry(JsonElement element)
    {
        var polygons = new List<GeoPolygon>();
        var lines = new List<IReadOnlyList<GeoPosition>>();
        var points = new List<GeoPosition>();
        AddGeometry(element, polygons, lines, points);
        return new GeoGeometry(polygons, lines, points);
    }

    private static void AddGeometry(JsonElement element, List<GeoPolygon> polygons, List<IReadOnlyList<GeoPosition>> lines, List<GeoPosition> points)
    {
        var type = GetType(element);
        if (type is "GeometryCollection")
        {
            if (!element.TryGetProperty("geometries", out var geometries) || geometries.ValueKind is not JsonValueKind.Array)
                throw new FormatException("A GeometryCollection must have a 'geometries' array.");

            foreach (var geometry in geometries.EnumerateArray())
            {
                AddGeometry(geometry, polygons, lines, points);
            }

            return;
        }

        if (!element.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind is not JsonValueKind.Array)
            throw new FormatException($"A {type} must have a 'coordinates' array.");

        switch (type)
        {
            case "Point":
                points.Add(ReadPosition(coordinates));
                break;
            case "MultiPoint":
                points.AddRange(ReadPositions(coordinates));
                break;
            case "LineString":
                lines.Add(ReadPositions(coordinates));
                break;
            case "MultiLineString":
                lines.AddRange(ReadArray(coordinates).Select(ReadPositions));
                break;
            case "Polygon":
                polygons.Add(ReadPolygon(coordinates));
                break;
            case "MultiPolygon":
                polygons.AddRange(ReadArray(coordinates).Select(ReadPolygon));
                break;
            default:
                throw new FormatException($"'{type}' is not a GeoJSON type.");
        }
    }

    private static GeoPolygon ReadPolygon(JsonElement rings) => new(ReadArray(rings).Select(ReadPositions));

    private static List<GeoPosition> ReadPositions(JsonElement positions) => [.. ReadArray(positions).Select(ReadPosition)];

    private static JsonElement.ArrayEnumerator ReadArray(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Array)
            throw new FormatException("The coordinates of a GeoJSON geometry must be nested arrays.");

        return element.EnumerateArray();
    }

    // A position is [longitude, latitude], possibly followed by an altitude, which is ignored
    private static GeoPosition ReadPosition(JsonElement position)
    {
        if (position.ValueKind is not JsonValueKind.Array || position.GetArrayLength() < 2 || position[0].ValueKind is not JsonValueKind.Number || position[1].ValueKind is not JsonValueKind.Number)
            throw new FormatException("A GeoJSON position must be an array of at least two numbers.");

        return new GeoPosition(position[0].GetDouble(), position[1].GetDouble());
    }

    private static string GetType(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String)
            throw new FormatException("A GeoJSON object must have a 'type' member.");

        return type.GetString()!;
    }
}
