using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Reads the objects of a TopoJSON topology: decodes its arcs once and stitches them into lines and rings.</summary>
internal sealed class TopoJsonReader
{
    private readonly JsonElement[] _arcs;
    private readonly GeoPosition[]?[] _decodedArcs;
    private readonly bool _hasTransform;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private readonly double _translateX;
    private readonly double _translateY;

    public TopoJsonReader(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String || type.GetString() is not "Topology")
            throw new FormatException("A TopoJSON document must be an object whose 'type' is 'Topology'.");

        if (!root.TryGetProperty("objects", out var objects) || objects.ValueKind is not JsonValueKind.Object)
            throw new FormatException("A TopoJSON topology must have an 'objects' object.");

        Objects = objects;
        if (root.TryGetProperty("arcs", out var arcs))
        {
            if (arcs.ValueKind is not JsonValueKind.Array)
                throw new FormatException("The 'arcs' of a TopoJSON topology must be an array.");

            // The indexer of a JsonElement is linear: it would make the decoding of a large topology quadratic
            _arcs = [.. arcs.EnumerateArray()];
        }
        else
        {
            _arcs = [];
        }

        _decodedArcs = new GeoPosition[_arcs.Length][];

        if (root.TryGetProperty("transform", out var transform) && transform.ValueKind is not JsonValueKind.Null)
        {
            if (transform.ValueKind is not JsonValueKind.Object || !transform.TryGetProperty("scale", out var scale) || !transform.TryGetProperty("translate", out var translate))
                throw new FormatException("The 'transform' of a TopoJSON topology must have a 'scale' and a 'translate'.");

            (_scaleX, _scaleY) = ReadPair(scale, "scale");
            (_translateX, _translateY) = ReadPair(translate, "translate");
            _hasTransform = true;
        }
    }

    public JsonElement Objects { get; }

    public GeoFeatureCollection ReadObject(string name, JsonElement element)
    {
        var features = new GeoFeatureCollection { Name = name };
        if (GetType(element) is "GeometryCollection")
        {
            foreach (var item in GetArray(element, "geometries").EnumerateArray())
            {
                features.Add(ReadFeature(item));
            }
        }
        else
        {
            features.Add(ReadFeature(element));
        }

        return features;
    }

    private static (double X, double Y) ReadPair(JsonElement element, string name)
    {
        if (element.ValueKind is not JsonValueKind.Array || element.GetArrayLength() is not 2 || element[0].ValueKind is not JsonValueKind.Number || element[1].ValueKind is not JsonValueKind.Number)
            throw new FormatException($"The '{name}' of a TopoJSON transform must be an array of two numbers.");

        return (GeoJson.ReadCoordinate(element[0]), GeoJson.ReadCoordinate(element[1]));
    }

    private static string? GetType(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
            throw new FormatException("A TopoJSON geometry must be an object.");

        if (!element.TryGetProperty("type", out var type) || type.ValueKind is JsonValueKind.Null)
            return null;

        if (type.ValueKind is not JsonValueKind.String)
            throw new FormatException("The 'type' of a TopoJSON geometry must be a string or null.");

        return type.GetString();
    }

    private static JsonElement GetArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is not JsonValueKind.Array)
            throw new FormatException($"A TopoJSON {GetType(element)} must have a '{name}' array.");

        return value;
    }

    private static JsonElement.ArrayEnumerator EnumerateArray(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Array)
            throw new FormatException("The arcs and the coordinates of a TopoJSON geometry must be nested arrays.");

        return element.EnumerateArray();
    }

    private GeoFeature ReadFeature(JsonElement element)
    {
        var geometry = ReadGeometry(element);
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (element.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in propertiesElement.EnumerateObject())
            {
                properties[property.Name] = GeoJson.ReadValue(property.Value);
            }
        }

        return new GeoFeature(geometry, properties, element.TryGetProperty("id", out var id) ? GeoJson.ReadId(id) : null);
    }

    private GeoGeometry? ReadGeometry(JsonElement element)
    {
        var type = GetType(element);
        return type switch
        {
            null => null,
            "GeometryCollection" => new GeoGeometryCollection([.. GetArray(element, "geometries").EnumerateArray().Select(ReadGeometry).OfType<GeoGeometry>()]),
            "Point" => new GeoPoint(ReadPoint(GetArray(element, "coordinates"))),
            "MultiPoint" => new GeoMultiPoint([.. GetArray(element, "coordinates").EnumerateArray().Select(ReadPoint)]),
            "LineString" => new GeoLineString(Stitch(GetArray(element, "arcs"))),
            "MultiLineString" => new GeoMultiLineString([.. GetArray(element, "arcs").EnumerateArray().Select(line => new GeoLineString(Stitch(line)))]),
            "Polygon" => ReadPolygon(GetArray(element, "arcs")),
            "MultiPolygon" => new GeoMultiPolygon([.. GetArray(element, "arcs").EnumerateArray().Select(ReadPolygon)]),
            _ => throw new FormatException($"'{type}' is not a TopoJSON geometry type."),
        };
    }

    private GeoPolygon ReadPolygon(JsonElement rings) => new([.. EnumerateArray(rings).Select(Stitch)]);

    // The arcs of a line or a ring follow each other: the last position of an arc is the first of the next one
    private List<GeoPosition> Stitch(JsonElement arcIndexes)
    {
        var positions = new List<GeoPosition>();
        foreach (var item in EnumerateArray(arcIndexes))
        {
            if (item.ValueKind is not JsonValueKind.Number || !item.TryGetInt32(out var index))
                throw new FormatException("An arc index of a TopoJSON geometry must be an integer.");

            var reversed = index < 0;
            var arc = GetArc(reversed ? ~index : index);
            if (positions.Count > 0)
            {
                positions.RemoveAt(positions.Count - 1);
            }

            if (reversed)
            {
                for (var i = arc.Length - 1; i >= 0; i--)
                {
                    positions.Add(arc[i]);
                }
            }
            else
            {
                positions.AddRange(arc);
            }
        }

        return positions;
    }

    private GeoPosition[] GetArc(int index)
    {
        if ((uint)index >= (uint)_decodedArcs.Length)
            throw new FormatException($"The arc {index} does not exist in the TopoJSON topology.");

        return _decodedArcs[index] ??= DecodeArc(_arcs[index]);
    }

    // The positions of the arcs of a quantized topology are the differences from the previous position
    private GeoPosition[] DecodeArc(JsonElement arc)
    {
        if (arc.ValueKind is not JsonValueKind.Array)
            throw new FormatException("A TopoJSON arc must be an array of positions.");

        var result = new GeoPosition[arc.GetArrayLength()];
        var x = 0.0;
        var y = 0.0;
        var i = 0;
        foreach (var position in arc.EnumerateArray())
        {
            var (px, py, altitude) = ReadValues(position);
            if (_hasTransform)
            {
                x += px;
                y += py;
                result[i++] = new GeoPosition((x * _scaleX) + _translateX, (y * _scaleY) + _translateY, altitude);
            }
            else
            {
                result[i++] = new GeoPosition(px, py, altitude);
            }
        }

        return result;
    }

    private GeoPosition ReadPoint(JsonElement position)
    {
        var (x, y, altitude) = ReadValues(position);
        return _hasTransform ? new GeoPosition((x * _scaleX) + _translateX, (y * _scaleY) + _translateY, altitude) : new GeoPosition(x, y, altitude);
    }

    // A position is [x, y], possibly followed by an altitude; the other values are ignored
    private static (double X, double Y, double? Altitude) ReadValues(JsonElement position)
    {
        if (position.ValueKind is not JsonValueKind.Array || position.GetArrayLength() < 2 || position[0].ValueKind is not JsonValueKind.Number || position[1].ValueKind is not JsonValueKind.Number)
            throw new FormatException("A TopoJSON position must be an array of at least two numbers.");

        double? altitude = position.GetArrayLength() > 2 && position[2].ValueKind is JsonValueKind.Number ? GeoJson.ReadCoordinate(position[2]) : null;
        return (GeoJson.ReadCoordinate(position[0]), GeoJson.ReadCoordinate(position[1]), altitude);
    }
}
