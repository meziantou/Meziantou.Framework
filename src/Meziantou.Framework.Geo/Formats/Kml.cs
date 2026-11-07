using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>Reads and writes KML 2.2 (OGC 12-007r2), the format of Google Earth and Google My Maps.</summary>
/// <remarks>
/// <para>
/// The reader returns the <c>Placemark</c> elements of the <c>Document</c> and <c>Folder</c> elements at any depth, in
/// document order. The <c>name</c> of the top-level <c>Document</c> or <c>Folder</c> becomes
/// <see cref="GeoFeatureCollection.Name"/>, and the <c>id</c> attribute of a placemark becomes <see cref="GeoFeature.Id"/>.
/// The properties are the <c>name</c> and the <c>description</c> of the placemark (the text of a CDATA section, so HTML
/// is kept as it is), the <c>Data</c> and <c>SchemaData</c>/<c>SimpleData</c> items of its <c>ExtendedData</c> (the
/// first one wins when a name repeats, and an element wins over a data item of the same name), and <c>folder</c>, the
/// names of the folders that contain the placemark, separated by <c>/</c> (the top-level <c>Document</c> or
/// <c>Folder</c> excluded, and the unnamed folders skipped). KML has no types: the values are strings, except the
/// <c>SimpleData</c> items whose <c>SimpleField</c> in the <c>Schema</c> of the document is numeric (a
/// <see cref="double"/>) or <c>bool</c> (a <see cref="bool"/>). A <c>Data</c> item without a <c>value</c> is
/// <see langword="null"/>.
/// </para>
/// <para>
/// The geometries are <c>Point</c>, <c>LineString</c>, <c>LinearRing</c> (a <see cref="GeoLineString"/>, as KML draws a
/// ring alone as a line), <c>Polygon</c>, <c>MultiGeometry</c> (a <see cref="GeoMultiPoint"/>,
/// <see cref="GeoMultiLineString"/> or <see cref="GeoMultiPolygon"/> when its items are all points, all lines or all
/// polygons; otherwise, a <see cref="GeoGeometryCollection"/>), and <c>gx:Track</c> and <c>gx:MultiTrack</c> (lines
/// through their <c>gx:coord</c> positions). The altitudes are kept as they are written, whatever the
/// <c>altitudeMode</c>. The styles, the time primitives, the <c>Model</c> geometries, the overlays and the network links
/// are ignored, and nothing is downloaded. The elements are matched by their local name, so KML 2.0, 2.1 and 2.2 and
/// the Google extensions are read. A document type definition is rejected and no external entity is resolved.
/// </para>
/// <para>
/// The writer writes a KML 2.2 <c>Document</c>, named after <see cref="GeoFeatureCollection.Name"/>, with a
/// <c>Placemark</c> per feature. Its <c>id</c> is <see cref="GeoFeature.Id"/> when it is a valid XML identifier (an
/// NCName, so not a number); otherwise, the identifier is not written. The <c>name</c> and <c>description</c> come from
/// the string properties named by <see cref="KmlWriterOptions"/>, and the other properties go in <c>ExtendedData</c>
/// as <c>Data</c> items with their text, independent of the culture (the <see langword="null"/> values are skipped).
/// The multi-geometries and the collections become <c>MultiGeometry</c> elements, the rings of the polygons are closed,
/// and a geometry with altitudes gets the <c>absolute</c> altitude mode.
/// </para>
/// </remarks>
public static class Kml
{
    /// <summary>The namespace of KML 2.2.</summary>
    internal const string Namespace = "http://www.opengis.net/kml/2.2";

    private const string FolderProperty = "folder";

    /// <summary>Reads the placemarks of a KML document.</summary>
    /// <param name="kml">The KML text.</param>
    /// <returns>The placemarks.</returns>
    /// <exception cref="FormatException">The text is not well-formed XML or not KML.</exception>
    public static GeoFeatureCollection Parse(string kml)
    {
        ArgumentNullException.ThrowIfNull(kml);
        using var textReader = new StringReader(kml);
        using var reader = XmlReader.Create(textReader, CreateReaderSettings());
        return Read(reader);
    }

    /// <summary>Reads the placemarks of a KML document.</summary>
    /// <param name="stream">The KML document. Its encoding is read from its XML declaration. It is left open.</param>
    /// <returns>The placemarks.</returns>
    /// <exception cref="FormatException">The document is not well-formed XML or not KML.</exception>
    public static GeoFeatureCollection Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using (var reader = XmlStreamReader.Open(stream, CreateReaderSettings(), "The document is not well-formed XML: "))
        {
            return Read(reader);
        }
    }

    /// <summary>Writes features as a KML document.</summary>
    /// <param name="features">The features.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The KML text.</returns>
    /// <exception cref="ArgumentException">A position is not finite, or a text contains a character that XML cannot represent.</exception>
    public static string Format(GeoFeatureCollection features, KmlWriterOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(features, stream, options);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Writes features as a KML document, encoded in UTF-8 without a byte order mark.</summary>
    /// <param name="features">The features.</param>
    /// <param name="stream">The stream to write to. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A position is not finite, or a text contains a character that XML cannot represent.</exception>
    public static void Write(GeoFeatureCollection features, Stream stream, KmlWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(stream);
        GeoValidation.EnsureFinite(features, nameof(features));
        options ??= new KmlWriterOptions();

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = options.Indented,
            NewLineChars = "\n",
        };

        using var writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("kml", Namespace);
        writer.WriteStartElement("Document", Namespace);
        if (features.Name is not null)
        {
            writer.WriteElementString("name", Namespace, features.Name);
        }

        foreach (var feature in features)
        {
            WritePlacemark(writer, feature, options);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    // KML never uses a DTD: rejecting it prevents the entity expansion attacks, and no resolver means nothing is downloaded
    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    private static GeoFeatureCollection Read(XmlReader reader)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException("The document is not well-formed XML: " + ex.Message, ex);
        }

        var root = document.Root!;
        var context = new ReadContext(ReadSchemas(root));
        switch (root.Name.LocalName)
        {
            case "kml":
                foreach (var child in root.Elements())
                {
                    ReadTopLevelFeature(child, context);
                }

                break;

            case "Document" or "Folder" or "Placemark":
                ReadTopLevelFeature(root, context);
                break;

            default:
                throw new FormatException($"The root element '{root.Name.LocalName}' is not a KML element.");
        }

        return context.Features;
    }

    private static void ReadTopLevelFeature(XElement element, ReadContext context)
    {
        switch (element.Name.LocalName)
        {
            case "Document" or "Folder":
                context.Features.Name ??= GetChild(element, "name")?.Value;
                ReadContainer(element, context);
                break;

            case "Placemark":
                context.Features.Add(ReadPlacemark(element, context));
                break;
        }
    }

    private static void ReadContainer(XElement container, ReadContext context)
    {
        foreach (var child in container.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Placemark":
                    context.Features.Add(ReadPlacemark(child, context));
                    break;

                case "Document" or "Folder":
                    var name = GetChild(child, "name")?.Value;
                    if (name is not null)
                    {
                        context.Folders.Add(name);
                    }

                    ReadContainer(child, context);
                    if (name is not null)
                    {
                        context.Folders.RemoveAt(context.Folders.Count - 1);
                    }

                    break;
            }
        }
    }

    private static GeoFeature ReadPlacemark(XElement placemark, ReadContext context)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (GetChild(placemark, "name") is { } name)
        {
            properties["name"] = name.Value;
        }

        if (GetChild(placemark, "description") is { } description)
        {
            properties["description"] = description.Value;
        }

        if (GetChild(placemark, "ExtendedData") is { } extendedData)
        {
            ReadExtendedData(extendedData, properties, context);
        }

        if (context.Folders.Count > 0)
        {
            properties.TryAdd(FolderProperty, string.Join('/', context.Folders));
        }

        var geometry = placemark.Elements().Select(ReadGeometry).FirstOrDefault(geometry => geometry is not null);
        return new GeoFeature(geometry, properties, placemark.Attribute("id")?.Value);
    }

    private static void ReadExtendedData(XElement extendedData, Dictionary<string, object?> properties, ReadContext context)
    {
        foreach (var item in extendedData.Elements())
        {
            switch (item.Name.LocalName)
            {
                case "Data" when item.Attribute("name")?.Value is { } name:
                    properties.TryAdd(name, GetChild(item, "value")?.Value);
                    break;

                case "SchemaData":
                    var schemaUrl = item.Attribute("schemaUrl")?.Value;
                    Dictionary<string, string>? fields = null;
                    if (schemaUrl is not null)
                    {
                        var index = schemaUrl.LastIndexOf('#', StringComparison.Ordinal);
                        context.Schemas.TryGetValue(index >= 0 ? schemaUrl[(index + 1)..] : schemaUrl, out fields);
                    }

                    foreach (var simpleData in item.Elements())
                    {
                        if (simpleData.Name.LocalName is "SimpleData" && simpleData.Attribute("name")?.Value is { } simpleName)
                        {
                            var type = fields is not null && fields.TryGetValue(simpleName, out var fieldType) ? fieldType : null;
                            properties.TryAdd(simpleName, ConvertSimpleData(simpleData.Value, type));
                        }
                    }

                    break;
            }
        }
    }

    // The types of the SimpleField elements of the schemas, by identifier, or by name as KML 2.1 referenced them
    private static Dictionary<string, Dictionary<string, string>> ReadSchemas(XElement root)
    {
        var schemas = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var schema in root.Descendants())
        {
            if (schema.Name.LocalName is not "Schema")
                continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in schema.Elements())
            {
                if (field.Name.LocalName is "SimpleField" && field.Attribute("name")?.Value is { } name && field.Attribute("type")?.Value is { } type)
                {
                    fields.TryAdd(name, type);
                }
            }

            if (schema.Attribute("id")?.Value is { } id)
            {
                schemas.TryAdd(id, fields);
            }

            if (schema.Attribute("name")?.Value is { } schemaName)
            {
                schemas.TryAdd(schemaName, fields);
            }
        }

        return schemas;
    }

    private static object ConvertSimpleData(string value, string? type)
    {
        switch (type)
        {
            case "int" or "uint" or "short" or "ushort" or "float" or "double":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return number;

                break;

            case "bool":
                switch (value.Trim())
                {
                    case "1" or "true":
                        return true;
                    case "0" or "false":
                        return false;
                }

                break;
        }

        return value;
    }

    private static GeoGeometry? ReadGeometry(XElement element) => element.Name.LocalName switch
    {
        "Point" => ReadPoint(element),
        "LineString" or "LinearRing" => new GeoLineString(ReadCoordinates(element)),
        "Polygon" => ReadPolygon(element),
        "MultiGeometry" => ReadMultiGeometry(element),
        "Track" => new GeoLineString(ReadTrack(element)),
        "MultiTrack" => new GeoMultiLineString([.. element.Elements().Where(track => track.Name.LocalName is "Track").Select(track => new GeoLineString(ReadTrack(track)))]),
        _ => null,
    };

    private static GeoPoint ReadPoint(XElement element)
    {
        var positions = ReadCoordinates(element);
        if (positions.Count is 0)
            throw new FormatException("A KML Point must have a position.");

        return new GeoPoint(positions[0]);
    }

    private static GeoPolygon ReadPolygon(XElement element)
    {
        var rings = new List<List<GeoPosition>>();
        if (GetChild(element, "outerBoundaryIs") is { } outer)
        {
            rings.Add(GetChild(outer, "LinearRing") is { } ring ? ReadCoordinates(ring) : []);
        }

        foreach (var inner in element.Elements())
        {
            if (inner.Name.LocalName is not "innerBoundaryIs")
                continue;

            if (rings.Count is 0)
                throw new FormatException("A KML Polygon with an innerBoundaryIs must have an outerBoundaryIs.");

            // KML 2.0 allows several rings in an innerBoundaryIs
            foreach (var ring in inner.Elements())
            {
                if (ring.Name.LocalName is "LinearRing")
                {
                    rings.Add(ReadCoordinates(ring));
                }
            }
        }

        return new GeoPolygon(rings);
    }

    private static GeoGeometry ReadMultiGeometry(XElement element)
    {
        List<GeoGeometry> geometries = [.. element.Elements().Select(ReadGeometry).OfType<GeoGeometry>()];
        if (geometries.Count > 0)
        {
            if (geometries.All(geometry => geometry is GeoPoint))
                return new GeoMultiPoint(geometries.Select(geometry => ((GeoPoint)geometry).Position));

            if (geometries.All(geometry => geometry is GeoLineString))
                return new GeoMultiLineString(geometries.Cast<GeoLineString>());

            if (geometries.All(geometry => geometry is GeoPolygon))
                return new GeoMultiPolygon(geometries.Cast<GeoPolygon>());
        }

        return new GeoGeometryCollection(geometries);
    }

    // A gx:coord is "longitude latitude altitude", separated by spaces
    private static List<GeoPosition> ReadTrack(XElement track)
    {
        var positions = new List<GeoPosition>();
        foreach (var coord in track.Elements())
        {
            if (coord.Name.LocalName is not "coord")
                continue;

            var values = coord.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length is < 2 or > 3)
                throw new FormatException($"'{coord.Value}' is not a KML track position.");

            positions.Add(new GeoPosition(ParseNumber(values[0]), ParseNumber(values[1]), values.Length > 2 ? ParseNumber(values[2]) : null));
        }

        return positions;
    }

    private static List<GeoPosition> ReadCoordinates(XElement element) => ParseCoordinates(GetChild(element, "coordinates")?.Value ?? "");

    /// <summary>Parses the tuples <c>longitude,latitude[,altitude]</c> of a <c>coordinates</c> element.</summary>
    /// <remarks>The tuples are separated by whitespace. A space after or before a comma is tolerated, so <c>1, 2</c> is one tuple.</remarks>
    internal static List<GeoPosition> ParseCoordinates(string text)
    {
        var positions = new List<GeoPosition>();
        var values = new double?[3];
        var count = 0;
        var inTuple = false;
        var afterComma = false;
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (c is ',')
            {
                if (!inTuple)
                    throw new FormatException("A KML coordinate tuple cannot start with a comma.");

                if (afterComma)
                {
                    AddValue(null);
                }

                afterComma = true;
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && text[index] is not ',' && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            // Two numbers separated by whitespace only belong to two tuples
            if (inTuple && !afterComma)
            {
                AddPosition();
            }

            AddValue(ParseNumber(text.AsSpan(start, index - start)));
            inTuple = true;
            afterComma = false;
        }

        if (inTuple)
        {
            AddPosition();
        }

        return positions;

        void AddValue(double? value)
        {
            if (count is 3)
                throw new FormatException("A KML coordinate tuple has at most three values: longitude, latitude and altitude.");

            values[count++] = value;
        }

        void AddPosition()
        {
            if (count < 2 || values[0] is not { } longitude || values[1] is not { } latitude)
                throw new FormatException("A KML coordinate tuple must have a longitude and a latitude.");

            positions.Add(new GeoPosition(longitude, latitude, count > 2 ? values[2] : null));
            count = 0;
        }
    }

    private static double ParseNumber(ReadOnlySpan<char> text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new FormatException($"'{text}' is not a valid KML coordinate.");

        return value;
    }

    private static XElement? GetChild(XElement element, string localName)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == localName)
                return child;
        }

        return null;
    }

    private static void WritePlacemark(XmlWriter writer, GeoFeature feature, KmlWriterOptions options)
    {
        writer.WriteStartElement("Placemark", Namespace);
        if (feature.Id is not null && IsNCName(feature.Id))
        {
            writer.WriteAttributeString("id", feature.Id);
        }

        var nameProperty = GetStringProperty(feature, options.NameProperty);
        var descriptionProperty = GetStringProperty(feature, options.DescriptionProperty);
        if (nameProperty is not null)
        {
            writer.WriteElementString("name", Namespace, (string)feature.Properties[nameProperty]!);
        }

        if (descriptionProperty is not null)
        {
            var description = (string)feature.Properties[descriptionProperty]!;
            writer.WriteStartElement("description", Namespace);

            // HTML is easier to read in a CDATA section, as Google Earth writes it
            if (description.Contains('<', StringComparison.Ordinal) && !description.Contains("]]>", StringComparison.Ordinal))
            {
                writer.WriteCData(description);
            }
            else
            {
                writer.WriteString(description);
            }

            writer.WriteEndElement();
        }

        var data = feature.Properties
            .Where(property => property.Key != nameProperty && property.Key != descriptionProperty)
            .Select(property => (property.Key, Value: GeoPropertyValue.ToText(property.Value)))
            .Where(property => property.Value is not null)
            .ToList();
        if (data.Count > 0)
        {
            writer.WriteStartElement("ExtendedData", Namespace);
            foreach (var (name, value) in data)
            {
                writer.WriteStartElement("Data", Namespace);
                writer.WriteAttributeString("name", name);
                writer.WriteElementString("value", Namespace, value);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        if (feature.Geometry is not null)
        {
            WriteGeometry(writer, feature.Geometry);
        }

        writer.WriteEndElement();
    }

    private static string? GetStringProperty(GeoFeature feature, string? name) => name is not null && feature.Properties.TryGetValue(name, out var value) && value is string ? name : null;

    private static bool IsNCName(string value)
    {
        if (value.Length is 0 || !XmlConvert.IsStartNCNameChar(value[0]))
            return false;

        foreach (var c in value)
        {
            if (!XmlConvert.IsNCNameChar(c))
                return false;
        }

        return true;
    }

    private static void WriteGeometry(XmlWriter writer, GeoGeometry geometry)
    {
        switch (geometry)
        {
            case GeoPoint point:
                writer.WriteStartElement("Point", Namespace);
                WriteAltitudeMode(writer, geometry);
                WriteCoordinates(writer, [point.Position]);
                writer.WriteEndElement();
                break;

            case GeoLineString lineString:
                writer.WriteStartElement("LineString", Namespace);
                WriteAltitudeMode(writer, geometry);
                WriteCoordinates(writer, lineString.Positions);
                writer.WriteEndElement();
                break;

            case GeoPolygon polygon:
                writer.WriteStartElement("Polygon", Namespace);
                WriteAltitudeMode(writer, geometry);
                for (var i = 0; i < polygon.Rings.Count; i++)
                {
                    writer.WriteStartElement(i is 0 ? "outerBoundaryIs" : "innerBoundaryIs", Namespace);
                    writer.WriteStartElement("LinearRing", Namespace);
                    WriteCoordinates(writer, CloseRing(polygon.Rings[i]));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                break;

            default:
                writer.WriteStartElement("MultiGeometry", Namespace);
                IEnumerable<GeoGeometry> items = geometry switch
                {
                    GeoMultiPoint multiPoint => multiPoint.Positions.Select(position => new GeoPoint(position)),
                    GeoMultiLineString multiLineString => multiLineString.LineStrings,
                    GeoMultiPolygon multiPolygon => multiPolygon.Polygons,
                    GeoGeometryCollection collection => collection.Geometries,
                    _ => throw new ArgumentException($"The geometry type '{geometry.Type}' is not supported.", nameof(geometry)),
                };

                foreach (var item in items)
                {
                    WriteGeometry(writer, item);
                }

                writer.WriteEndElement();
                break;
        }
    }

    // Without an altitude mode, KML clamps the geometries to the ground and ignores their altitudes
    private static void WriteAltitudeMode(XmlWriter writer, GeoGeometry geometry)
    {
        if (geometry.GetPositions().Any(position => position.Altitude is not null))
        {
            writer.WriteElementString("altitudeMode", Namespace, "absolute");
        }
    }

    private static IReadOnlyList<GeoPosition> CloseRing(IReadOnlyList<GeoPosition> ring) => ring.Count > 0 && ring[0] != ring[^1] ? [.. ring, ring[0]] : ring;

    private static void WriteCoordinates(XmlWriter writer, IReadOnlyList<GeoPosition> positions)
    {
        var text = new StringBuilder();
        foreach (var position in positions)
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(CultureInfo.InvariantCulture, $"{position.Longitude},{position.Latitude}");
            if (position.Altitude is { } altitude)
            {
                text.Append(CultureInfo.InvariantCulture, $",{altitude}");
            }
        }

        writer.WriteElementString("coordinates", Namespace, text.ToString());
    }

    private sealed class ReadContext(Dictionary<string, Dictionary<string, string>> schemas)
    {
        public GeoFeatureCollection Features { get; } = [];

        public Dictionary<string, Dictionary<string, string>> Schemas { get; } = schemas;

        public List<string> Folders { get; } = [];
    }
}
