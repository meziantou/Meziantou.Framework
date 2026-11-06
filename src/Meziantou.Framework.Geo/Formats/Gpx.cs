using System.Globalization;
using System.Text;
using System.Xml;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>Reads and writes GPX, the GPS Exchange Format: the waypoints, routes and tracks of a GPS device or of a sports application.</summary>
/// <remarks>
/// <para>
/// The reader reads GPX 1.1 and GPX 1.0, and returns one feature per <c>wpt</c>, <c>rte</c> and <c>trk</c> element, in
/// the order of the document. A waypoint is a <see cref="GeoPoint"/>, a route is a <see cref="GeoLineString"/> of its
/// <c>rtept</c>, and a track is a <see cref="GeoMultiLineString"/> with one line per <c>trkseg</c>, even when it has a
/// single segment, so the type of the geometry tells a track from a route. The <c>ele</c> of a point becomes its
/// <see cref="GeoPosition.Altitude"/>. The <c>name</c> of the <c>metadata</c> (or of the <c>gpx</c> element in GPX 1.0)
/// becomes <see cref="GeoFeatureCollection.Name"/>.
/// </para>
/// <para>
/// The properties of a feature are the <see cref="TypePropertyName"/> property, <c>wpt</c>, <c>rte</c> or <c>trk</c>,
/// and the elements <c>name</c>, <c>cmt</c>, <c>desc</c>, <c>src</c> and <c>type</c> as a <see cref="string"/>, the
/// <c>sym</c> of a waypoint as a <see cref="string"/>, the <c>number</c> of a route or a track as a <see cref="double"/>,
/// and the <c>time</c> of a waypoint as a <see cref="DateTimeOffset"/> (UTC when the text has no offset). The other
/// elements are ignored: <c>link</c> (<c>url</c> in GPX 1.0), <c>extensions</c> (such as the heart rate or the cadence of
/// a Garmin device), the other data of a waypoint (<c>magvar</c>, <c>fix</c>, <c>hdop</c>…), the metadata other than its
/// name, and every element of the points of the routes and tracks except <c>ele</c>, such as their <c>time</c>. The
/// features have no <see cref="GeoFeature.Id"/>.
/// </para>
/// <para>
/// The writer writes GPX 1.1. A <see cref="GeoPoint"/> is a <c>wpt</c>, a <see cref="GeoMultiPoint"/> is one <c>wpt</c>
/// per point, a <see cref="GeoLineString"/> is a <c>rte</c>, and a <see cref="GeoMultiLineString"/> is a <c>trk</c> with
/// one <c>trkseg</c> per line, as the reader returns them and as GDAL writes them. When the <see cref="TypePropertyName"/>
/// property is <c>trk</c>, a <see cref="GeoLineString"/> is a track of one segment; when it is <c>rte</c>, a
/// <see cref="GeoMultiLineString"/> is one route per line. As GPX requires, the waypoints come first, then the routes, then
/// the tracks. The writer writes the properties named after the elements the reader reads, as their text; the
/// <c>time</c> is written in UTC when it is a <see cref="DateTimeOffset"/>, a <see cref="DateTime"/> or a text that is a
/// date, and the <c>number</c> when it is a non-negative integer. The other properties and the identifiers are not
/// written, as GPX has no element for them.
/// </para>
/// </remarks>
public static class Gpx
{
    /// <summary>The name of the property that holds the GPX element of a feature: <c>wpt</c>, <c>rte</c> or <c>trk</c>.</summary>
    public const string TypePropertyName = "gpxType";

    private const string Gpx11Namespace = "http://www.topografix.com/GPX/1/1";

    /// <summary>Reads the waypoints, routes and tracks of a GPX document.</summary>
    /// <param name="gpx">The GPX text.</param>
    /// <returns>One feature per waypoint, route and track, in the order of the document.</returns>
    /// <exception cref="FormatException">The text is not valid XML or not GPX.</exception>
    public static GeoFeatureCollection Parse(string gpx)
    {
        ArgumentNullException.ThrowIfNull(gpx);
        using var textReader = new StringReader(gpx);
        using var reader = XmlReader.Create(textReader, CreateReaderSettings());
        return ReadDocument(reader);
    }

    /// <summary>Reads the waypoints, routes and tracks of a GPX document.</summary>
    /// <param name="stream">The GPX document. Its encoding is read from the XML declaration, UTF-8 by default. It is left open.</param>
    /// <returns>One feature per waypoint, route and track, in the order of the document.</returns>
    /// <exception cref="FormatException">The document is not valid XML or not GPX.</exception>
    public static GeoFeatureCollection Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = XmlReader.Create(stream, CreateReaderSettings());
        return ReadDocument(reader);
    }

    /// <summary>Writes features as a GPX 1.1 document.</summary>
    /// <param name="features">The features: points and lines.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The GPX text.</returns>
    /// <exception cref="ArgumentException">A feature has no geometry, or a polygon or a geometry collection, or a position is not finite.</exception>
    public static string Format(GeoFeatureCollection features, GpxWriterOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(features, stream, options);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Writes features as a GPX 1.1 document, encoded in UTF-8.</summary>
    /// <param name="features">The features: points and lines.</param>
    /// <param name="stream">The stream to write to. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentException">A feature has no geometry, or a polygon or a geometry collection, or a position is not finite.</exception>
    public static void Write(GeoFeatureCollection features, Stream stream, GpxWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(stream);
        GeoValidation.EnsureFinite(features, nameof(features));
        options ??= new GpxWriterOptions();

        // Sort the features before writing anything, so an invalid feature leaves the stream untouched
        var waypoints = new List<(GeoPosition Position, GeoFeature Feature)>();
        var routes = new List<(GeoLineString Line, GeoFeature Feature)>();
        var tracks = new List<(IReadOnlyList<GeoLineString> Segments, GeoFeature Feature)>();
        foreach (var feature in features)
        {
            var type = feature.Properties.TryGetValue(TypePropertyName, out var value) ? GeoPropertyValue.ToText(value) : null;
            switch (feature.Geometry)
            {
                case GeoPoint point:
                    waypoints.Add((point.Position, feature));
                    break;
                case GeoMultiPoint multiPoint:
                    waypoints.AddRange(multiPoint.Positions.Select(position => (position, feature)));
                    break;
                case GeoLineString line when type is "trk":
                    tracks.Add(([line], feature));
                    break;
                case GeoLineString line:
                    routes.Add((line, feature));
                    break;
                case GeoMultiLineString multiLine when type is "rte":
                    routes.AddRange(multiLine.LineStrings.Select(line => (line, feature)));
                    break;
                case GeoMultiLineString multiLine:
                    tracks.Add((multiLine.LineStrings, feature));
                    break;
                case null:
                    throw new ArgumentException("GPX cannot hold a feature without a geometry.", nameof(features));
                default:
                    throw new ArgumentException($"GPX cannot hold a {feature.Geometry.Type}: only points, multi-points, line strings and multi-line strings.", nameof(features));
            }
        }

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = options.Indented,
            CloseOutput = false,
        };
        using var writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("gpx", Gpx11Namespace);
        writer.WriteAttributeString("version", "1.1");
        writer.WriteAttributeString("creator", options.Creator);
        if (features.Name is not null)
        {
            writer.WriteStartElement("metadata", Gpx11Namespace);
            writer.WriteElementString("name", Gpx11Namespace, features.Name);
            writer.WriteEndElement();
        }

        foreach (var (position, feature) in waypoints)
        {
            WriteWaypoint(writer, position, feature.Properties);
        }

        foreach (var (line, feature) in routes)
        {
            writer.WriteStartElement("rte", Gpx11Namespace);
            WriteDescription(writer, feature.Properties);
            foreach (var position in line.Positions)
            {
                WritePoint(writer, "rtept", position);
            }

            writer.WriteEndElement();
        }

        foreach (var (segments, feature) in tracks)
        {
            writer.WriteStartElement("trk", Gpx11Namespace);
            WriteDescription(writer, feature.Properties);
            foreach (var segment in segments)
            {
                writer.WriteStartElement("trkseg", Gpx11Namespace);
                foreach (var position in segment.Positions)
                {
                    WritePoint(writer, "trkpt", position);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    // The DTD is ignored rather than prohibited, so a document with a DOCTYPE is still read; it is never processed, so
    // no entity is expanded and no external resource is loaded
    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
    };

    private static GeoFeatureCollection ReadDocument(XmlReader reader)
    {
        try
        {
            if (reader.MoveToContent() is not XmlNodeType.Element || reader.LocalName is not "gpx")
                throw new FormatException("The root element of a GPX document must be 'gpx'.");

            // The elements of GPX 1.0 and 1.1 have the same names; the elements of other namespaces are extensions
            var ns = reader.NamespaceURI;
            var collection = new GeoFeatureCollection();
            for (var found = MoveToFirstChild(reader); found; found = MoveToNextChild(reader))
            {
                switch (reader.NamespaceURI == ns ? reader.LocalName : null)
                {
                    case "metadata":
                        for (var child = MoveToFirstChild(reader); child; child = MoveToNextChild(reader))
                        {
                            if (reader.NamespaceURI == ns && reader.LocalName is "name")
                            {
                                collection.Name = reader.ReadElementContentAsString();
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }

                        break;

                    case "name": // GPX 1.0
                        collection.Name = reader.ReadElementContentAsString();
                        break;

                    case "wpt":
                        var properties = CreateProperties("wpt");
                        var position = ReadPoint(reader, ns, properties);
                        collection.Add(new GeoFeature(new GeoPoint(position), properties));
                        break;

                    case "rte":
                        collection.Add(ReadRoute(reader, ns));
                        break;

                    case "trk":
                        collection.Add(ReadTrack(reader, ns));
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }

            return collection;
        }
        catch (XmlException ex)
        {
            throw new FormatException("The document is not valid XML: " + ex.Message, ex);
        }
    }

    private static Dictionary<string, object?> CreateProperties(string type) => new(StringComparer.Ordinal) { [TypePropertyName] = type };

    private static GeoFeature ReadRoute(XmlReader reader, string ns)
    {
        var properties = CreateProperties("rte");
        var positions = new List<GeoPosition>();
        for (var found = MoveToFirstChild(reader); found; found = MoveToNextChild(reader))
        {
            if (reader.NamespaceURI == ns && reader.LocalName is "rtept")
            {
                positions.Add(ReadPoint(reader, ns, properties: null));
            }
            else
            {
                ReadDescription(reader, ns, properties);
            }
        }

        return new GeoFeature(new GeoLineString(positions), properties);
    }

    private static GeoFeature ReadTrack(XmlReader reader, string ns)
    {
        var properties = CreateProperties("trk");
        var segments = new List<GeoLineString>();
        for (var found = MoveToFirstChild(reader); found; found = MoveToNextChild(reader))
        {
            if (reader.NamespaceURI == ns && reader.LocalName is "trkseg")
            {
                var positions = new List<GeoPosition>();
                for (var point = MoveToFirstChild(reader); point; point = MoveToNextChild(reader))
                {
                    if (reader.NamespaceURI == ns && reader.LocalName is "trkpt")
                    {
                        positions.Add(ReadPoint(reader, ns, properties: null));
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                segments.Add(new GeoLineString(positions));
            }
            else
            {
                ReadDescription(reader, ns, properties);
            }
        }

        return new GeoFeature(new GeoMultiLineString(segments), properties);
    }

    // Reads an element of a route or a track other than its points, and skips the elements that are not kept
    private static void ReadDescription(XmlReader reader, string ns, Dictionary<string, object?> properties)
    {
        switch (reader.NamespaceURI == ns ? reader.LocalName : null)
        {
            case "name" or "cmt" or "desc" or "src" or "type":
                var name = reader.LocalName;
                properties[name] = reader.ReadElementContentAsString();
                break;
            case "number":
                properties["number"] = ParseNumber(reader.ReadElementContentAsString(), "number");
                break;
            default:
                reader.Skip();
                break;
        }
    }

    // Reads a wpt, rtept or trkpt; the properties are null for the points of a route or a track, whose data is not kept
    private static GeoPosition ReadPoint(XmlReader reader, string ns, Dictionary<string, object?>? properties)
    {
        var name = reader.LocalName;
        var latitude = ParseCoordinate(reader.GetAttribute("lat"), name, "lat", 90);
        var longitude = ParseCoordinate(reader.GetAttribute("lon"), name, "lon", 180);
        double? altitude = null;
        for (var found = MoveToFirstChild(reader); found; found = MoveToNextChild(reader))
        {
            switch (reader.NamespaceURI == ns ? reader.LocalName : null)
            {
                case "ele":
                    var text = reader.ReadElementContentAsString();
                    altitude = string.IsNullOrWhiteSpace(text) ? null : ParseNumber(text, "ele");
                    break;
                case "time" when properties is not null:
                    properties["time"] = ParseTime(reader.ReadElementContentAsString());
                    break;
                case "name" or "cmt" or "desc" or "src" or "sym" or "type" when properties is not null:
                    var property = reader.LocalName;
                    properties[property] = reader.ReadElementContentAsString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new GeoPosition(longitude, latitude, altitude);
    }

    private static double ParseCoordinate(string? text, string element, string attribute, double limit)
    {
        if (text is null)
            throw new FormatException($"A '{element}' element must have a '{attribute}' attribute.");

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !(Math.Abs(value) <= limit))
            throw new FormatException($"The '{attribute}' attribute of a '{element}' element must be a number from -{limit} to {limit}: '{text}'.");

        return value;
    }

    private static double ParseNumber(string text, string element)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new FormatException($"The '{element}' element must be a number: '{text}'.");

        return value;
    }

    private static DateTimeOffset ParseTime(string text)
    {
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var value))
            throw new FormatException($"The 'time' element must be a date and a time: '{text}'.");

        return value;
    }

    // Moves to the first child element of the current element, or after the element when it has none
    private static bool MoveToFirstChild(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return false;
        }

        reader.Read();
        return MoveToNextChild(reader);
    }

    // Moves to the next child element, or after the end of the parent element when there is none
    private static bool MoveToNextChild(XmlReader reader)
    {
        while (true)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    return true;
                case XmlNodeType.EndElement:
                    reader.Read();
                    return false;
                default:
                    if (!reader.Read())
                        throw new FormatException("The GPX document ends before the end of an element.");

                    break;
            }
        }
    }

    private static void WriteWaypoint(XmlWriter writer, GeoPosition position, IReadOnlyDictionary<string, object?> properties)
    {
        writer.WriteStartElement("wpt", Gpx11Namespace);
        WriteCoordinates(writer, position);
        if (properties.TryGetValue("time", out var value) && FormatTime(value) is { } time)
        {
            writer.WriteElementString("time", Gpx11Namespace, time);
        }

        WriteText(writer, properties, "name");
        WriteText(writer, properties, "cmt");
        WriteText(writer, properties, "desc");
        WriteText(writer, properties, "src");
        WriteText(writer, properties, "sym");
        WriteText(writer, properties, "type");
        writer.WriteEndElement();
    }

    // The elements of a route or a track before its points, in the order of the schema
    private static void WriteDescription(XmlWriter writer, IReadOnlyDictionary<string, object?> properties)
    {
        WriteText(writer, properties, "name");
        WriteText(writer, properties, "cmt");
        WriteText(writer, properties, "desc");
        WriteText(writer, properties, "src");
        if (properties.TryGetValue("number", out var value) && FormatNumber(value) is { } number)
        {
            writer.WriteElementString("number", Gpx11Namespace, number);
        }

        WriteText(writer, properties, "type");
    }

    private static void WritePoint(XmlWriter writer, string name, GeoPosition position)
    {
        writer.WriteStartElement(name, Gpx11Namespace);
        WriteCoordinates(writer, position);
        writer.WriteEndElement();
    }

    // The attributes and the ele element, which comes first in the schema
    private static void WriteCoordinates(XmlWriter writer, GeoPosition position)
    {
        writer.WriteAttributeString("lat", position.Latitude.ToString("R", CultureInfo.InvariantCulture));
        writer.WriteAttributeString("lon", position.Longitude.ToString("R", CultureInfo.InvariantCulture));
        if (position.Altitude is { } altitude)
        {
            writer.WriteElementString("ele", Gpx11Namespace, altitude.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private static void WriteText(XmlWriter writer, IReadOnlyDictionary<string, object?> properties, string name)
    {
        if (properties.TryGetValue(name, out var value) && GeoPropertyValue.ToText(value) is { } text)
        {
            writer.WriteElementString(name, Gpx11Namespace, text);
        }
    }

    private static string? FormatTime(object? value)
    {
        DateTime? utc = value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            DateTime { Kind: DateTimeKind.Local } dateTime => dateTime.ToUniversalTime(),
            DateTime dateTime => dateTime,
            _ when GeoPropertyValue.ToText(value) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed.UtcDateTime,
            _ => null,
        };

        return utc?.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }

    // The number of a route or a track is a non-negative integer
    private static string? FormatNumber(object? value)
    {
        if (GeoPropertyValue.TryGetNumber(value, out var number))
            return number is >= 0 and < 1e18 && Math.Floor(number) == number ? ((ulong)number).ToString(CultureInfo.InvariantCulture) : null;

        return GeoPropertyValue.ToText(value) is { } text && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed.ToString(CultureInfo.InvariantCulture) : null;
    }
}
