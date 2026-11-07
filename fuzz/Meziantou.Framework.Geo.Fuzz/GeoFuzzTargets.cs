using System.Globalization;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Geo.Fuzz;

/// <summary>
/// The readers of <c>Meziantou.Framework.Geo</c> reject invalid data with a <see cref="FormatException"/> and never throw
/// anything else. What a reader returns can be written in the same format, and the writer is stable: reading what it wrote
/// and writing it again gives the same output. The geometries the readers return support every operation of the model.
/// </summary>
public static partial class GeoFuzzTargets
{
    private const string TestDataPrefix = "TestData/";

    private const int MaxShapefileWrites = 5;

    private static readonly string[] JsonTokens =
    [
        "{", "}", "[", "]", ":", ",", "\"", "null", "true", "false", "-0", "1e308", "1e-324", "\"type\"", "\"coordinates\"", "\"geometries\"", "\"features\"", "\"properties\"", "\"geometry\"", "\"id\"", "\"bbox\"", "\"name\"",
        "\"Feature\"", "\"FeatureCollection\"", "\"Point\"", "\"MultiPoint\"", "\"LineString\"", "\"MultiLineString\"", "\"Polygon\"", "\"MultiPolygon\"", "\"GeometryCollection\"",
        "\"Topology\"", "\"objects\"", "\"arcs\"", "\"transform\"", "\"scale\"", "\"translate\"", "\\ud800", "\\u0000",
    ];

    private static readonly string[] WktTokens =
    [
        "POINT", "LINESTRING", "POLYGON", "MULTIPOINT", "MULTILINESTRING", "MULTIPOLYGON", "GEOMETRYCOLLECTION", "EMPTY", " Z ", " M ", " ZM ", "POINTZ", "POINTM", "SRID=4326;", "(", ")", ",", " ", "-", ".", "e", "E+", "1e308", "NaN", "Infinity",
        "CIRCULARSTRING", "TRIANGLE",
    ];

    private static readonly string[] WkbTokens =
    [
        "\u0001\u0001\u0000\u0000\u0000", "\u0000\u0000\u0000\u0000\u0001", "\u0001é\u0003\u0000\u0000", "\u0001\u0007\u0000\u0000\u0000", "\u0001\u0001\u0000\u0000 æ\u0010\u0000\u0000",
        "\u0000\u0000\u0000\u0000\u0000\u0000ø\u007F", "ÿÿÿ\u007F",
    ];

    private static readonly string[] XmlTokens =
    [
        "<", ">", "</", "/>", "=\"", "\"", "<?xml version=\"1.0\"?>", "<![CDATA[", "]]>", "<!--", "-->", "<!DOCTYPE x [<!ENTITY a \"b\">]>", "&amp;", "&#x10FFFF;", "&a;", " xmlns=\"http://www.opengis.net/kml/2.2\"", " xmlns=\"http://www.topografix.com/GPX/1/1\"",
        "kml", "Document", "Folder", "Placemark", "name", "description", "ExtendedData", "Data", "value", "Schema", "SimpleField", "SchemaData", "SimpleData", "schemaUrl", "type=\"int\"", "type=\"bool\"", "Point", "LineString", "LinearRing",
        "Polygon", "outerBoundaryIs", "innerBoundaryIs", "MultiGeometry", "coordinates", "gx:Track", "gx:MultiTrack", "gx:coord", "altitudeMode", " id=\"",
        "gpx", "metadata", "wpt", "rte", "rtept", "trk", "trkseg", "trkpt", " lat=\"", " lon=\"", "ele", "time", "cmt", "desc", "src", "sym", "number", "extensions", "2024-02-29T13:45:00Z", "0001-01-01T00:00:00+14:00",
        "9999-12-31T23:59:59-14:00",
    ];

    private static readonly string[] PolylineTokens = ["?", "@", "_", "~", "`", "_p~iF~ps|U_ulLnnqC_mqNvxq`@", "~~~~~~~", "??"];

    private static readonly string[] ShapefileTokens =
    [
        "\u0000\u0000'\u000A", "è\u0003\u0000\u0000", "\u0001\u0000\u0000\u0000", "\u0003\u0000\u0000\u0000", "\u0005\u0000\u0000\u0000", "\u0008\u0000\u0000\u0000", "\u000B\u0000\u0000\u0000", "\u000D\u0000\u0000\u0000",
        "\u000F\u0000\u0000\u0000", "\u001F\u0000\u0000\u0000", "ÿÿÿ\u007F", "\u0003", "\u000D", "\u001A", "*", " ", "C", "N", "F", "L", "D", "M", "?", "T", "19991231", "00000000",
    ];

    private static readonly string[] FlatGeobufTokens =
    [
        "fgb\u0003fgb\u0000", "fgb\u0003fgb\u0001", "\u0000\u0000\u0000\u0000", "\u0001\u0000\u0000\u0000", "\u0010\u0000", "\u0002\u0000", "ÿÿÿÿ", "ÿÿÿ\u007F", "\u0000\u0000\u0000\u0000\u0000\u0000ð?", "\u0000\u0000\u0000\u0000\u0000\u0000ø\u007F",
        "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007", "\u000B", "\u000C", "\u000D", "\u000E", "{}", "[1]", "2024-02-29T13:45:00Z", "EPSG", "WGS 84",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("geo-geojson", GeoJsonTarget) { Seeds = GetGeoJsonSeeds, Dictionary = JsonTokens },
        new("geo-topojson", TopoJsonTarget) { Seeds = GetTopoJsonSeeds, Dictionary = JsonTokens },
        new("geo-wkt", WktTarget) { Seeds = GetWktSeeds, Dictionary = WktTokens },
        new("geo-wkb", WkbTarget) { Seeds = GetWkbSeeds, Dictionary = WkbTokens },
        new("geo-polyline", PolylineTarget) { Seeds = GetPolylineSeeds, Dictionary = PolylineTokens },
        new("geo-kml", KmlTarget) { Seeds = GetKmlSeeds, Dictionary = XmlTokens },
        new("geo-gpx", GpxTarget) { Seeds = GetGpxSeeds, Dictionary = XmlTokens },
        new("geo-shapefile", ShapefileTarget) { Seeds = GetShapefileSeeds, Dictionary = ShapefileTokens },
        new("geo-flatgeobuf", FlatGeobufTarget) { Seeds = GetFlatGeobufSeeds, Dictionary = FlatGeobufTokens },
        new("geo-operations", OperationsTarget) { Seeds = GetOperationsSeeds },
        new("geo-projection", ProjectionTarget) { Seeds = GetProjectionSeeds },
    ];

    /// <summary>
    /// <c>text</c>: the GeoJSON text. Reading it from a string and from its UTF-8 bytes gives the same features, and
    /// <see cref="GeoJson.Format"/> is stable.
    /// </summary>
    private static void GeoJsonTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        var bytes = data.ToArray();
        var features = Read(() => GeoJson.Parse(text));
        if (IsStrictUtf8(data))
        {
            var fromStream = Read(() => GeoJson.Read(new MemoryStream(bytes, writable: false)));
            FuzzAssert.Equal(Describe(features), Describe(fromStream), "Reading the UTF-8 bytes gives other features than reading the text");
        }

        if (features is null)
            return;

        Exercise(features);
        AssertStable(features, collection => GeoJson.Format(collection), GeoJson.Parse, "GeoJSON");
    }

    /// <summary>
    /// <c>options, text</c>: <c>options</c> 0 writes without quantization, any other value with a quantization of
    /// <c>2 + options * 97</c>. The objects read from the topology are written back, and the writer is stable.
    /// </summary>
    private static void TopoJsonTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(data);
        var objects = Read(() => TopoJson.ParseObjects(text));
        var all = Read(() => TopoJson.Parse(text));
        FuzzAssert.Equal(objects is null, all is null, "ParseObjects and Parse disagree on whether the topology is valid");
        if (objects is null || all is null)
            return;

        FuzzAssert.Equal(objects.Values.Sum(collection => collection.Count), all.Count, "Parse does not return the features of every object");
        foreach (var (name, collection) in objects)
        {
            FuzzAssert.Equal(name, collection.Name, "The name of a collection is not the name of its object");
            Exercise(collection);
        }

        if (!objects.Values.All(IsFinite))
            return;

        var writerOptions = new TopoJsonWriterOptions { Quantization = options is 0 ? null : 2 + (options * 97) };
        var first = TopoJson.FormatObjects(objects, writerOptions);
        var read = TopoJson.ParseObjects(first);
        FuzzAssert.Equal(string.Join(',', objects.Keys), string.Join(',', read.Keys), "The objects written are not read back");
        if (writerOptions.Quantization is null)
        {
            // Without quantization, the features are read back as they were written, the rings once closed
            var second = TopoJson.FormatObjects(read, writerOptions);
            FuzzAssert.Equal(first, second, "Writing the topology read from the output of the writer gives another topology");
        }
    }

    /// <summary><c>text</c>: the WKT. What is read is written back to WKT and to WKB, and both writers are stable.</summary>
    private static void WktTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        var geometry = Read(() => WellKnownText.Parse(text));
        if (geometry is null)
            return;

        Exercise(geometry);
        FuzzAssert.True(IsFinite(geometry), "The WKT reader returned a position that is not finite");
        var wkt = WellKnownText.Format(geometry);
        var reread = WellKnownText.Parse(wkt);
        FuzzAssert.Equal(wkt, WellKnownText.Format(reread), "Writing the WKT read from the output of the writer gives another WKT");
        FuzzAssert.Equal(Describe(Normalize(geometry)), Describe(reread), "The WKT written is not read back as the geometry");

        var wkb = WellKnownBinary.Write(geometry);
        FuzzAssert.Equal(wkt, WellKnownText.Format(WellKnownBinary.Read(wkb)), "The WKB written is not read back as the geometry");
        FuzzAssert.Equal(wkt, WellKnownText.Format(WellKnownBinary.ParseHex(WellKnownBinary.FormatHex(geometry))), "The hexadecimal WKB written is not read back as the geometry");
    }

    /// <summary><c>wkb</c>: the WKB, also read as hexadecimal text. What is read is written back, and the writer is stable.</summary>
    private static void WkbTarget(ReadOnlySpan<byte> data)
    {
        var geometry = Read(data.ToArray(), static bytes => WellKnownBinary.Read(bytes));
        var hex = FuzzInput.ReadText(data);
        _ = Read(() => WellKnownBinary.ParseHex(hex));
        if (geometry is null)
            return;

        Exercise(geometry);
        if (!IsFinite(geometry))
            return;

        var wkb = WellKnownBinary.Write(geometry);
        var reread = WellKnownBinary.Read(wkb);
        FuzzAssert.Equal(Convert.ToHexString(wkb), Convert.ToHexString(WellKnownBinary.Write(reread)), "Writing the WKB read from the output of the writer gives another WKB");
        FuzzAssert.Equal(Describe(Normalize(geometry)), Describe(reread), "The WKB written is not read back as the geometry");
        FuzzAssert.Equal(Describe(reread), Describe(WellKnownText.Parse(WellKnownText.Format(geometry))), "The WKT written is not read back as the geometry");
    }

    /// <summary>
    /// <c>precision, text</c>: <c>precision</c> modulo 11 is the number of decimal digits. The decoded positions encode to a
    /// polyline that decodes to the same positions.
    /// </summary>
    private static void PolylineTarget(ReadOnlySpan<byte> data)
    {
        var precision = FuzzInput.ReadByte(ref data) % 11;
        var text = FuzzInput.ReadText(data);
        IReadOnlyList<GeoPosition> positions;
        try
        {
            positions = EncodedPolyline.Decode(text, precision);
        }
        catch (FormatException)
        {
            return;
        }

        FuzzAssert.True(positions.All(position => double.IsFinite(position.Longitude) && double.IsFinite(position.Latitude) && position.Altitude is null), "A decoded position is not finite or has an altitude");
        var encoded = EncodedPolyline.Encode(positions, precision);
        FuzzAssert.Equal(Describe(positions), Describe(EncodedPolyline.Decode(encoded, precision)), "The encoded positions do not decode to the same positions");
        FuzzAssert.Equal(encoded, EncodedPolyline.Encode(EncodedPolyline.Decode(encoded, precision), precision), "Encoding the decoded polyline gives another polyline");
    }

    /// <summary>
    /// <c>mode, data</c>: <c>mode</c> 0 reads the data as KML, any other value as KMZ. The features are written back as KML
    /// and KMZ, and the writers are stable.
    /// </summary>
    private static void KmlTarget(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        var bytes = data.ToArray();
        var text = FuzzInput.ReadText(data);
        var features = mode is 0 ? Read(() => Kml.Read(new MemoryStream(bytes, writable: false))) : Read(() => Kmz.Read(new MemoryStream(bytes, writable: false)));
        if (mode is 0)
        {
            _ = Read(() => Kml.Parse(text));
        }

        if (features is null)
            return;

        Exercise(features);
        var kml = AssertStable(features, collection => Kml.Format(collection), Kml.Parse, "KML");

        using var kmz = new MemoryStream();
        Kmz.Write(features, kmz);
        kmz.Position = 0;
        FuzzAssert.Equal(kml, Kml.Format(Kmz.Read(kmz)), "The KMZ written is not read back as the KML");
    }

    /// <summary><c>data</c>: the GPX. The features are written back, and the writer is stable.</summary>
    private static void GpxTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var text = FuzzInput.ReadText(data);
        var features = Read(() => Gpx.Read(new MemoryStream(bytes, writable: false)));
        _ = Read(() => Gpx.Parse(text));
        if (features is null)
            return;

        Exercise(features);
        AssertStable(features, collection => Gpx.Format(collection), Gpx.Parse, "GPX");
    }

    /// <summary>
    /// <c>flags, length (2 bytes, little-endian), shp, dbf</c>: <c>length</c> is the length of the <c>.shp</c> file; the
    /// rest is the <c>.dbf</c> file when bit 0 of <c>flags</c> is set. When bit 1 is set, the files are read from a zip
    /// file instead, and bit 2 reads the <c>.dbf</c> file as ISO-8859-1. The features are written back when they have a
    /// single type of geometry, and the writer settles: after a few writes, reading what it wrote and writing it again gives
    /// the same files.
    /// </summary>
    private static void ShapefileTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var length = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var shp = data[..Math.Min(length, data.Length)].ToArray();
        var dbf = (flags & 1) is 0 ? null : data[shp.Length..].ToArray();
        var options = new ShapefileReaderOptions { Encoding = (flags & 4) is 0 ? null : Encoding.Latin1 };

        GeoFeatureCollection? features;
        if ((flags & 2) is 0)
        {
            features = Read(() => Shapefile.Read(new MemoryStream(shp, writable: false), dbf is null ? null : new MemoryStream(dbf, writable: false), options));
        }
        else
        {
            features = Read(() => Shapefile.ReadZip(new MemoryStream(Zip(shp, dbf), writable: false), options));
        }

        if (features is null)
            return;

        Exercise(features);
        if (!IsFinite(features))
            return;

        // A shapefile has a single type of geometry, but its records may not have the type of its header
        byte[] firstShp, firstDbf;
        try
        {
            (firstShp, firstDbf) = WriteShapefile(features);
        }
        catch (ArgumentException) when (GetFamilies(features).Count > 1)
        {
            return;
        }

        // The polygons of a shapefile are rings that the reader groups by orientation and containment, so an invalid polygon
        // (a hole outside of its exterior ring, as a multipatch or a hand-made file can give) is read back as other polygons,
        // whose rings can group again differently: the output of the writer settles after a few writes. The attributes do
        // not depend on the geometries, so they are the same from the first write.
        var reread = ReadShapefile(firstShp, firstDbf);
        FuzzAssert.Equal(features.Count, reread.Count, "The shapefile written does not have a record per feature");
        var (currentShp, currentDbf) = (firstShp, firstDbf);
        for (var write = 2; ; write++)
        {
            var (nextShp, nextDbf) = WriteShapefile(ReadShapefile(currentShp, currentDbf));
            FuzzAssert.Equal(Convert.ToHexString(firstDbf), Convert.ToHexString(nextDbf), "Writing the shapefile read from the output of the writer gives another .dbf file");
            if (nextShp.AsSpan().SequenceEqual(currentShp))
                break;

            FuzzAssert.True(write < MaxShapefileWrites, $"The .shp file written still changes after {MaxShapefileWrites} writes");
            (currentShp, currentDbf) = (nextShp, nextDbf);
        }
    }

    private static GeoFeatureCollection ReadShapefile(byte[] shp, byte[] dbf) => Shapefile.Read(new MemoryStream(shp), new MemoryStream(dbf), new ShapefileReaderOptions { Encoding = Encoding.UTF8 });

    private static (byte[] Shp, byte[] Dbf) WriteShapefile(GeoFeatureCollection features)
    {
        using var shp = new MemoryStream();
        using var shx = new MemoryStream();
        using var dbf = new MemoryStream();
        Shapefile.Write(features, shp, shx, dbf);
        return (shp.ToArray(), dbf.ToArray());
    }

    private static byte[] Zip(byte[] shp, byte[]? dbf)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry("layer.shp").Open())
            {
                entry.Write(shp);
            }

            if (dbf is not null)
            {
                using var entry = archive.CreateEntry("layer.dbf").Open();
                entry.Write(dbf);
            }
        }

        return memory.ToArray();
    }

    private static HashSet<string> GetFamilies(GeoFeatureCollection features)
    {
        return [.. features.Where(feature => feature.Geometry is { IsEmpty: false }).Select(feature => feature.Geometry switch
        {
            GeoPoint or GeoMultiPoint => "point",
            GeoLineString or GeoMultiLineString => "line",
            GeoPolygon or GeoMultiPolygon => "polygon",
            _ => "other",
        })];
    }

    /// <summary>Writes the features, reads them back and writes them again: both outputs must be the same.</summary>
    private static string AssertStable(GeoFeatureCollection features, Func<GeoFeatureCollection, string> format, Func<string, GeoFeatureCollection> parse, string formatName)
    {
        var first = format(features);
        var reread = parse(first);
        Exercise(reread);
        var second = format(reread);
        FuzzAssert.Equal(first, second, $"Writing the {formatName} read from the output of the writer gives another {formatName}");
        return first;
    }

    /// <summary>Runs a reader: it returns a value, or throws a <see cref="FormatException"/> for invalid data.</summary>
    private static T? Read<T>(Func<T> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static T? Read<T>(byte[] data, Func<byte[], T> read)
        where T : class
    {
        try
        {
            return read(data);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsStrictUtf8(ReadOnlySpan<byte> data) => System.Text.Unicode.Utf8.IsValid(data);

    private static bool IsFinite(GeoFeatureCollection features) => features.All(feature => feature.Geometry is null || IsFinite(feature.Geometry));

    private static bool IsFinite(GeoGeometry geometry) => geometry.GetPositions().All(position => double.IsFinite(position.Longitude) && double.IsFinite(position.Latitude) && (position.Altitude is not { } altitude || double.IsFinite(altitude)));

    /// <summary>
    /// The geometry the WKT and WKB writers give: a geometry has a single number of dimensions, so when a position has an
    /// altitude, the positions without one get an altitude of 0.
    /// </summary>
    private static GeoGeometry Normalize(GeoGeometry geometry)
    {
        if (!geometry.GetPositions().Any(position => position.Altitude is not null))
            return geometry;

        return Map(geometry);

        static GeoPosition Fix(GeoPosition position) => position with { Altitude = position.Altitude ?? 0 };

        static GeoGeometry Map(GeoGeometry geometry) => geometry switch
        {
            GeoPoint point => new GeoPoint(Fix(point.Position)),
            GeoMultiPoint multiPoint => new GeoMultiPoint(multiPoint.Positions.Select(Fix)),
            GeoLineString lineString => new GeoLineString(lineString.Positions.Select(Fix)),
            GeoMultiLineString multiLineString => new GeoMultiLineString(multiLineString.LineStrings.Select(line => (GeoLineString)Map(line))),
            GeoPolygon polygon => new GeoPolygon(polygon.Rings.Select(ring => ring.Select(Fix))),
            GeoMultiPolygon multiPolygon => new GeoMultiPolygon(multiPolygon.Polygons.Select(polygon => (GeoPolygon)Map(polygon))),
            GeoGeometryCollection collection => new GeoGeometryCollection(collection.Geometries.Select(Map)),
            _ => throw new InvalidOperationException("Unknown geometry"),
        };
    }

    /// <summary>Calls the operations of the model on the features: none of them throws, and the bounds contain the positions.</summary>
    private static void Exercise(GeoFeatureCollection features)
    {
        foreach (var feature in features)
        {
            FuzzAssert.True(feature.Properties is not null, "A feature has no property dictionary");
            if (feature.Geometry is not null)
            {
                Exercise(feature.Geometry);
            }
        }

        _ = features.GetBounds();
    }

    private static void Exercise(GeoGeometry geometry)
    {
        var positions = geometry.GetPositions().ToList();
        FuzzAssert.Equal(positions.Count is 0, geometry.IsEmpty, "IsEmpty does not match the positions of the geometry");
        var bounds = geometry.GetBounds();
        FuzzAssert.Equal(positions.Count is 0, bounds is null, "GetBounds does not match the positions of the geometry");
        if (bounds is { } box && positions.All(position => double.IsFinite(position.Longitude) && double.IsFinite(position.Latitude)))
        {
            FuzzAssert.True(positions.All(box.Contains), "The bounds of the geometry do not contain its positions");
        }

        var label = geometry.LabelPosition;
        FuzzAssert.False(label is not null && positions.Count is 0, "An empty geometry has a label position");
        _ = geometry.GetSimpleGeometries().Count();
        if (label is { } position)
        {
            switch (geometry)
            {
                case GeoPolygon polygon:
                    _ = polygon.Contains(position);
                    break;
                case GeoMultiPolygon multiPolygon:
                    _ = multiPolygon.Contains(position);
                    break;
                case GeoLineString lineString:
                    _ = lineString.GetLength();
                    break;
            }
        }
    }

    private static string Describe(IEnumerable<GeoPosition> positions) => string.Join(", ", positions.Select(Describe));

    private static string Describe(GeoPosition position)
    {
        return position.Altitude is { } altitude
            ? string.Create(CultureInfo.InvariantCulture, $"{position.Longitude:R} {position.Latitude:R} {altitude:R}")
            : string.Create(CultureInfo.InvariantCulture, $"{position.Longitude:R} {position.Latitude:R}");
    }

    private static string Describe(GeoGeometry? geometry) => geometry switch
    {
        null => "null",
        GeoPoint point => "Point(" + Describe(point.Position) + ")",
        GeoMultiPoint multiPoint => "MultiPoint(" + Describe(multiPoint.Positions) + ")",
        GeoLineString lineString => "LineString(" + Describe(lineString.Positions) + ")",
        GeoMultiLineString multiLineString => "MultiLineString(" + string.Join("; ", multiLineString.LineStrings.Select(Describe)) + ")",
        GeoPolygon polygon => "Polygon(" + string.Join("; ", polygon.Rings.Select(Describe)) + ")",
        GeoMultiPolygon multiPolygon => "MultiPolygon(" + string.Join("; ", multiPolygon.Polygons.Select(Describe)) + ")",
        GeoGeometryCollection collection => "GeometryCollection(" + string.Join("; ", collection.Geometries.Select(Describe)) + ")",
        _ => geometry.GetType().Name,
    };

    private static string Describe(GeoFeatureCollection? features)
    {
        if (features is null)
            return "invalid";

        var builder = new StringBuilder();
        builder.Append("Name: ").Append(features.Name).Append('\n');
        foreach (var feature in features)
        {
            builder.Append("Feature ").Append(feature.Id).Append(": ").Append(Describe(feature.Geometry)).Append('\n');
            foreach (var (name, value) in feature.Properties)
            {
                builder.Append("  ").Append(name).Append(" = ").Append(FuzzAssert.Format(value is byte[] binary ? Convert.ToHexString(binary) : value is IFormattable formattable ? formattable.ToString(format: null, CultureInfo.InvariantCulture) : value?.ToString())).Append('\n');
            }
        }

        return builder.ToString();
    }
}
