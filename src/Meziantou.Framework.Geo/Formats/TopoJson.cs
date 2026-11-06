using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>
/// Reads and writes TopoJSON, the format of map files such as <c>world-atlas</c> and <c>us-atlas</c>: a <c>Topology</c>
/// whose named objects reference arcs, so that a border shared by two polygons is stored once.
/// </summary>
/// <remarks>
/// <para>
/// Each object of the topology is a collection of features: a <c>GeometryCollection</c> object gives a feature for each of
/// its geometries, with their <c>id</c> and <c>properties</c>, and another object gives one feature. A
/// <c>GeometryCollection</c> inside a feature becomes a <see cref="GeoGeometryCollection"/>, and a geometry whose type is
/// <see langword="null"/> becomes a feature without geometry (or is skipped inside a <see cref="GeoGeometryCollection"/>).
/// </para>
/// <para>
/// The reader stitches the arcs, reversing those referenced by a negative index (<c>~i</c>), applies the <c>transform</c>
/// of a quantized topology to the arcs (delta-encoded) and to the positions of the points (not delta-encoded), and keeps a
/// third value of a position, which is never transformed, as the altitude. It ignores the <c>bbox</c>, the foreign members,
/// and the <c>id</c> and <c>properties</c> of a <c>GeometryCollection</c> object. The properties are a <see cref="string"/>,
/// a <see cref="double"/>, a <see cref="bool"/>, <see langword="null"/>, or a <see cref="JsonElement"/> for an object or an
/// array. A numeric <c>id</c> becomes its JSON text.
/// </para>
/// <para>
/// The writer writes each collection as a <c>GeometryCollection</c> object, and finds the arcs as <c>topojson-server</c>
/// does: the lines and rings are cut at the junctions, the positions where they meet or part, and the identical arcs,
/// in either direction, are written once. The rings are also cut at their first position, so they are read back
/// starting at the same position. It closes the rings that are not, writes the altitudes as a third value (which GDAL
/// does not read), the identifiers as strings, and the properties as <see cref="GeoJson"/> does. Without
/// <see cref="TopoJsonWriterOptions.Quantization"/>, the features are read back as they were written; with it, the
/// positions are rounded to a grid and the arcs delta-encoded.
/// </para>
/// </remarks>
public static class TopoJson
{
    /// <summary>Reads the features of all the objects of a TopoJSON topology.</summary>
    /// <param name="json">The TopoJSON text.</param>
    /// <returns>The features of the objects, in the order of the document. The <see cref="GeoFeatureCollection.Name"/> is the name of the object when the topology has exactly one.</returns>
    /// <exception cref="FormatException">The text is not valid JSON or not a TopoJSON topology.</exception>
    public static GeoFeatureCollection Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Merge(ParseObjects(json));
    }

    /// <summary>Reads the features of all the objects of a TopoJSON topology.</summary>
    /// <param name="utf8Json">The TopoJSON document, encoded in UTF-8. It is left open.</param>
    /// <returns>The features of the objects, in the order of the document. The <see cref="GeoFeatureCollection.Name"/> is the name of the object when the topology has exactly one.</returns>
    /// <exception cref="FormatException">The document is not valid JSON or not a TopoJSON topology.</exception>
    public static GeoFeatureCollection Read(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        return Merge(ReadObjects(utf8Json));
    }

    /// <summary>Reads the features of an object of a TopoJSON topology, such as the <c>countries</c> of <c>world-atlas</c>.</summary>
    /// <param name="json">The TopoJSON text.</param>
    /// <param name="objectName">The name of the object.</param>
    /// <returns>The features of the object. The <see cref="GeoFeatureCollection.Name"/> is the name of the object.</returns>
    /// <exception cref="FormatException">The text is not valid JSON or not a TopoJSON topology.</exception>
    /// <exception cref="ArgumentException">The topology has no object named <paramref name="objectName"/>.</exception>
    public static GeoFeatureCollection Parse(string json, string objectName)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(objectName);
        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadObject(document.RootElement, objectName);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The text is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Reads the features of an object of a TopoJSON topology, such as the <c>countries</c> of <c>world-atlas</c>.</summary>
    /// <param name="utf8Json">The TopoJSON document, encoded in UTF-8. It is left open.</param>
    /// <param name="objectName">The name of the object.</param>
    /// <returns>The features of the object. The <see cref="GeoFeatureCollection.Name"/> is the name of the object.</returns>
    /// <exception cref="FormatException">The document is not valid JSON or not a TopoJSON topology.</exception>
    /// <exception cref="ArgumentException">The topology has no object named <paramref name="objectName"/>.</exception>
    public static GeoFeatureCollection Read(Stream utf8Json, string objectName)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ArgumentNullException.ThrowIfNull(objectName);
        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return ReadObject(document.RootElement, objectName);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The document is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Reads the objects of a TopoJSON topology, such as the <c>countries</c> and the <c>land</c> of <c>world-atlas</c>.</summary>
    /// <param name="json">The TopoJSON text.</param>
    /// <returns>The features of each object by name, in the order of the document. The <see cref="GeoFeatureCollection.Name"/> of each collection is the name of its object.</returns>
    /// <exception cref="FormatException">The text is not valid JSON or not a TopoJSON topology.</exception>
    public static IReadOnlyDictionary<string, GeoFeatureCollection> ParseObjects(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadObjects(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The text is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Reads the objects of a TopoJSON topology, such as the <c>countries</c> and the <c>land</c> of <c>world-atlas</c>.</summary>
    /// <param name="utf8Json">The TopoJSON document, encoded in UTF-8. It is left open.</param>
    /// <returns>The features of each object by name, in the order of the document. The <see cref="GeoFeatureCollection.Name"/> of each collection is the name of its object.</returns>
    /// <exception cref="FormatException">The document is not valid JSON or not a TopoJSON topology.</exception>
    public static IReadOnlyDictionary<string, GeoFeatureCollection> ReadObjects(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return ReadObjects(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The document is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Writes features as a TopoJSON topology with one object, named after <see cref="GeoFeatureCollection.Name"/>, or <c>features</c> when it is <see langword="null"/>.</summary>
    /// <param name="features">The features.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The TopoJSON text.</returns>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static string Format(GeoFeatureCollection features, TopoJsonWriterOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(features, stream, options);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Writes features as a TopoJSON topology with one object, named after <see cref="GeoFeatureCollection.Name"/>, or <c>features</c> when it is <see langword="null"/>, encoded in UTF-8.</summary>
    /// <param name="features">The features.</param>
    /// <param name="utf8Json">The stream to write to. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static void Write(GeoFeatureCollection features, Stream utf8Json, TopoJsonWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(utf8Json);
        GeoValidation.EnsureFinite(features, nameof(features));
        WriteCore([new(features.Name ?? "features", features)], utf8Json, options ?? new TopoJsonWriterOptions());
    }

    /// <summary>Writes collections of features as the objects of a TopoJSON topology, which share their arcs.</summary>
    /// <param name="objects">The features of each object by name, in the order to write them.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The TopoJSON text.</returns>
    /// <exception cref="ArgumentException">A collection is <see langword="null"/> or a position is not finite.</exception>
    public static string FormatObjects(IReadOnlyDictionary<string, GeoFeatureCollection> objects, TopoJsonWriterOptions? options = null)
    {
        using var stream = new MemoryStream();
        WriteObjects(objects, stream, options);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Writes collections of features as the objects of a TopoJSON topology, which share their arcs, encoded in UTF-8.</summary>
    /// <param name="objects">The features of each object by name, in the order to write them.</param>
    /// <param name="utf8Json">The stream to write to. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A collection is <see langword="null"/> or a position is not finite.</exception>
    public static void WriteObjects(IReadOnlyDictionary<string, GeoFeatureCollection> objects, Stream utf8Json, TopoJsonWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(utf8Json);
        var list = objects.ToList();
        foreach (var (_, features) in list)
        {
            if (features is null)
                throw new ArgumentException("The collections must not be null.", nameof(objects));

            GeoValidation.EnsureFinite(features, nameof(objects));
        }

        WriteCore(list, utf8Json, options ?? new TopoJsonWriterOptions());
    }

    private static GeoFeatureCollection Merge(IReadOnlyDictionary<string, GeoFeatureCollection> objects)
    {
        var result = new GeoFeatureCollection(objects.Values.SelectMany(features => features));
        if (objects.Count is 1)
        {
            result.Name = objects.Keys.First();
        }

        return result;
    }

    private static OrderedDictionary<string, GeoFeatureCollection> ReadObjects(JsonElement root)
    {
        var reader = new TopoJsonReader(root);
        var result = new OrderedDictionary<string, GeoFeatureCollection>(StringComparer.Ordinal);
        foreach (var property in reader.Objects.EnumerateObject())
        {
            result[property.Name] = reader.ReadObject(property.Name, property.Value);
        }

        return result;
    }

    private static GeoFeatureCollection ReadObject(JsonElement root, string objectName)
    {
        var reader = new TopoJsonReader(root);
        if (!reader.Objects.TryGetProperty(objectName, out var element))
            throw new ArgumentException($"The topology has no object named '{objectName}'.", nameof(objectName));

        return reader.ReadObject(objectName, element);
    }

    private static void WriteCore(List<KeyValuePair<string, GeoFeatureCollection>> objects, Stream utf8Json, TopoJsonWriterOptions options)
    {
        var jsonOptions = new JsonWriterOptions { Indented = options.Indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var writer = new Utf8JsonWriter(utf8Json, jsonOptions);
        new TopoJsonWriter(objects, options.Quantization).Write(writer);
    }
}
