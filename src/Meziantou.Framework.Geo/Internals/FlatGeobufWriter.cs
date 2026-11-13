using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Writes FlatGeobuf data: the signature, the header, the optional index, then the size-prefixed features.</summary>
internal static class FlatGeobufWriter
{
    public static void Write(GeoFeatureCollection features, Stream stream, FlatGeobufWriterOptions options)
    {
        var geometryTypes = features
            .Where(feature => feature.Geometry is { IsEmpty: false })
            .Select(feature => GetGeometryType(feature.Geometry!))
            .Distinct()
            .ToList();
        var headerType = geometryTypes.Count == 1 ? geometryTypes[0] : FlatGeobufFormat.GeometryUnknown;
        var positions = features.Where(feature => feature.Geometry is not null).SelectMany(feature => feature.Geometry!.GetPositions());
        var dimensions = new FlatGeobufDimensions(positions.Any(position => position.Altitude is not null), positions.Any(position => position.Measure is not null));

        // The index requires the features in its order, along a Hilbert curve, and a box for each of them
        var boxes = features.Select(feature => feature.Geometry is { IsEmpty: false } geometry ? geometry.GetBounds() : null).ToArray();
        var hasIndex = options.IncludeSpatialIndex && features.Count > 0 && boxes.All(box => box is not null);
        int[] order = hasIndex ? PackedRTree.GetHilbertOrder([.. boxes.Select(box => box!.Value)]) : [.. Enumerable.Range(0, features.Count)];

        // The columns in the order the written features have them, so that writing what is read gives the same columns
        var columns = GetColumns(order.Select(i => features[i]));

        var buffers = new byte[features.Count][];
        var offsets = new long[features.Count];
        var offset = 0L;
        for (var i = 0; i < order.Length; i++)
        {
            buffers[i] = WriteFeature(features[order[i]], columns, headerType, dimensions);
            offsets[i] = offset;
            offset += buffers[i].Length;
        }

        var header = new FlatBufferTableBuilder();
        if ((options.Name ?? features.Name) is { } name)
        {
            header.AddString(FlatGeobufFormat.HeaderName, name);
        }

        if (features.GetBounds() is { } envelope)
        {
            header.AddDoubles(FlatGeobufFormat.HeaderEnvelope, [envelope.West, envelope.South, envelope.East, envelope.North]);
        }

        header.AddByte(FlatGeobufFormat.HeaderGeometryType, headerType);
        if (dimensions.HasZ)
        {
            header.AddBool(FlatGeobufFormat.HeaderHasZ, value: true);
        }

        if (dimensions.HasM)
        {
            header.AddBool(FlatGeobufFormat.HeaderHasM, value: true);
        }

        if (columns.Count > 0)
        {
            header.AddTables(FlatGeobufFormat.HeaderColumns, [.. columns.Select(column =>
            {
                var table = new FlatBufferTableBuilder();
                table.AddString(FlatGeobufFormat.ColumnName, column.Name);
                table.AddByte(FlatGeobufFormat.ColumnType, column.Type);
                return table;
            })]);
        }

        header.AddUInt64(FlatGeobufFormat.HeaderFeaturesCount, (ulong)features.Count);
        header.AddUInt16(FlatGeobufFormat.HeaderIndexNodeSize, hasIndex ? options.IndexNodeSize : (ushort)0);

        var crs = new FlatBufferTableBuilder();
        crs.AddString(FlatGeobufFormat.CrsOrganization, "EPSG");
        crs.AddInt32(FlatGeobufFormat.CrsCode, 4326);
        crs.AddString(FlatGeobufFormat.CrsName, "WGS 84");
        header.AddTable(FlatGeobufFormat.HeaderCrs, crs);
        if (options.Title is { } title)
        {
            header.AddString(FlatGeobufFormat.HeaderTitle, title);
        }

        if (options.Description is { } description)
        {
            header.AddString(FlatGeobufFormat.HeaderDescription, description);
        }

        stream.Write([(byte)'f', (byte)'g', (byte)'b', FlatGeobufFormat.MajorVersion, (byte)'f', (byte)'g', (byte)'b', FlatGeobufFormat.PatchVersion]);
        stream.Write(FlatBufferBuilder.BuildSizePrefixed(header));
        if (hasIndex)
        {
            stream.Write(PackedRTree.Write([.. order.Select(i => boxes[i]!.Value)], offsets, options.IndexNodeSize));
        }

        foreach (var buffer in buffers)
        {
            stream.Write(buffer);
        }
    }

    private static byte[] WriteFeature(GeoFeature feature, List<Column> columns, byte headerType, FlatGeobufDimensions dimensions)
    {
        var table = new FlatBufferTableBuilder();
        if (feature.Geometry is { IsEmpty: false } geometry)
        {
            table.AddTable(FlatGeobufFormat.FeatureGeometry, WriteGeometry(geometry, dimensions, writeType: headerType == FlatGeobufFormat.GeometryUnknown));
        }

        var properties = WriteProperties(feature, columns);
        if (properties.Length > 0)
        {
            table.AddBytes(FlatGeobufFormat.FeatureProperties, properties);
        }

        return FlatBufferBuilder.BuildSizePrefixed(table);
    }

    private static FlatBufferTableBuilder WriteGeometry(GeoGeometry geometry, FlatGeobufDimensions dimensions, bool writeType)
    {
        var table = new FlatBufferTableBuilder();
        var type = GetGeometryType(geometry);
        switch (geometry)
        {
            case GeoPoint point:
                AddPositions(table, [point.Position], dimensions);
                break;

            case GeoMultiPoint multiPoint:
                AddPositions(table, multiPoint.Positions, dimensions);
                break;

            case GeoLineString lineString:
                AddPositions(table, lineString.Positions, dimensions);
                break;

            case GeoMultiLineString multiLineString:
                AddParts(table, multiLineString.LineStrings.Select(lineString => lineString.Positions).ToList(), dimensions);
                break;

            case GeoPolygon polygon:
                AddParts(table, polygon.Rings, dimensions);
                break;

            case GeoMultiPolygon multiPolygon:
                table.AddTables(FlatGeobufFormat.GeometryParts, [.. multiPolygon.Polygons.Select(polygon => WriteGeometry(polygon, dimensions, writeType: true))]);
                break;

            case GeoGeometryCollection collection:
                table.AddTables(FlatGeobufFormat.GeometryParts, [.. collection.Geometries.Select(part => WriteGeometry(part, dimensions, writeType: true))]);
                break;
        }

        if (writeType)
        {
            table.AddByte(FlatGeobufFormat.GeometryType, type);
        }

        return table;
    }

    private static void AddParts(FlatBufferTableBuilder table, IReadOnlyList<IReadOnlyList<GeoPosition>> parts, FlatGeobufDimensions dimensions)
    {
        AddPositions(table, [.. parts.SelectMany(part => part)], dimensions);

        // One part needs no ends
        if (parts.Count > 1)
        {
            var ends = new uint[parts.Count];
            var end = 0u;
            for (var i = 0; i < parts.Count; i++)
            {
                end += (uint)parts[i].Count;
                ends[i] = end;
            }

            table.AddUInt32s(FlatGeobufFormat.GeometryEnds, ends);
        }
    }

    private static void AddPositions(FlatBufferTableBuilder table, IReadOnlyList<GeoPosition> positions, FlatGeobufDimensions dimensions)
    {
        var xy = new double[positions.Count * 2];
        for (var i = 0; i < positions.Count; i++)
        {
            xy[2 * i] = positions[i].Longitude;
            xy[(2 * i) + 1] = positions[i].Latitude;
        }

        table.AddDoubles(FlatGeobufFormat.GeometryXY, xy);
        if (dimensions.HasZ)
        {
            table.AddDoubles(FlatGeobufFormat.GeometryZ, [.. positions.Select(position => position.Altitude ?? 0)]);
        }

        if (dimensions.HasM)
        {
            table.AddDoubles(FlatGeobufFormat.GeometryM, [.. positions.Select(position => position.Measure ?? double.NaN)]);
        }
    }

    private static byte GetGeometryType(GeoGeometry geometry) => geometry switch
    {
        GeoPoint => FlatGeobufFormat.GeometryPoint,
        GeoMultiPoint => FlatGeobufFormat.GeometryMultiPoint,
        GeoLineString => FlatGeobufFormat.GeometryLineString,
        GeoMultiLineString => FlatGeobufFormat.GeometryMultiLineString,
        GeoPolygon => FlatGeobufFormat.GeometryPolygon,
        GeoMultiPolygon => FlatGeobufFormat.GeometryMultiPolygon,
        _ => FlatGeobufFormat.GeometryCollection,
    };

    private static byte[] WriteProperties(GeoFeature feature, List<Column> columns)
    {
        using var stream = new MemoryStream();
        Span<byte> scratch = stackalloc byte[8];
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (!feature.Properties.TryGetValue(column.Name, out var value) || GetKind(value) is null)
                continue;

            BinaryPrimitives.WriteUInt16LittleEndian(scratch, (ushort)index);
            stream.Write(scratch[..2]);
            switch (column.Type)
            {
                case FlatGeobufFormat.ColumnBool:
                    stream.WriteByte(value is true or JsonElement { ValueKind: JsonValueKind.True } ? (byte)1 : (byte)0);
                    break;
                case FlatGeobufFormat.ColumnByte:
                    stream.WriteByte((byte)(sbyte)value!);
                    break;
                case FlatGeobufFormat.ColumnUByte:
                    stream.WriteByte((byte)value!);
                    break;
                case FlatGeobufFormat.ColumnShort:
                    BinaryPrimitives.WriteInt16LittleEndian(scratch, (short)value!);
                    stream.Write(scratch[..2]);
                    break;
                case FlatGeobufFormat.ColumnUShort:
                    BinaryPrimitives.WriteUInt16LittleEndian(scratch, (ushort)value!);
                    stream.Write(scratch[..2]);
                    break;
                case FlatGeobufFormat.ColumnInt:
                    BinaryPrimitives.WriteInt32LittleEndian(scratch, (int)value!);
                    stream.Write(scratch[..4]);
                    break;
                case FlatGeobufFormat.ColumnUInt:
                    BinaryPrimitives.WriteUInt32LittleEndian(scratch, (uint)value!);
                    stream.Write(scratch[..4]);
                    break;
                case FlatGeobufFormat.ColumnLong:
                    BinaryPrimitives.WriteInt64LittleEndian(scratch, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    stream.Write(scratch[..8]);
                    break;
                case FlatGeobufFormat.ColumnULong:
                    BinaryPrimitives.WriteUInt64LittleEndian(scratch, (ulong)value!);
                    stream.Write(scratch[..8]);
                    break;
                case FlatGeobufFormat.ColumnFloat:
                    BinaryPrimitives.WriteSingleLittleEndian(scratch, value switch
                    {
                        Half half => (float)half,
#if NET11_0_OR_GREATER
                        System.Numerics.BFloat16 bfloat16 => (float)bfloat16,
#endif
                        _ => (float)value!,
                    });
                    stream.Write(scratch[..4]);
                    break;
                case FlatGeobufFormat.ColumnDouble:
                    GeoPropertyValue.TryGetNumber(value, out var number);
                    BinaryPrimitives.WriteDoubleLittleEndian(scratch, number);
                    stream.Write(scratch[..8]);
                    break;
                case FlatGeobufFormat.ColumnBinary:
                    WriteSized(stream, (byte[])value!);
                    break;
                case FlatGeobufFormat.ColumnJson:
                    WriteSized(stream, Encoding.UTF8.GetBytes(((JsonElement)value!).GetRawText()));
                    break;
                case FlatGeobufFormat.ColumnDateTime:
                    WriteSized(stream, Encoding.UTF8.GetBytes(FormatDateTime(value!)));
                    break;
                default:
                    WriteSized(stream, Encoding.UTF8.GetBytes(GeoPropertyValue.ToText(value) ?? ""));
                    break;
            }
        }

        return stream.ToArray();

        // ISO 8601 to the millisecond: GDAL reads at most 32 characters
        static string FormatDateTime(object value) => value switch
        {
            DateTimeOffset { Offset.Ticks: 0 } dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFzzz", CultureInfo.InvariantCulture),
            DateTime { Kind: DateTimeKind.Utc } dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", CultureInfo.InvariantCulture),
            DateTime { Kind: DateTimeKind.Local } dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFzzz", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFF", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => GeoPropertyValue.ToText(value) ?? "",
        };

        static void WriteSized(Stream stream, byte[] bytes)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }
    }

    /// <summary>Gets the columns: the property names in the order they are first seen, each typed by its non-null values.</summary>
    private static List<Column> GetColumns(IEnumerable<GeoFeature> features)
    {
        var kinds = new Dictionary<string, byte?>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var feature in features)
        {
            foreach (var (name, value) in feature.Properties)
            {
                if (!kinds.TryGetValue(name, out var current))
                {
                    names.Add(name);
                    current = null;
                }

                kinds[name] = Merge(current, GetKind(value));
            }
        }

        return [.. names.Select(name => new Column(name, kinds[name] ?? FlatGeobufFormat.ColumnString))];

        static byte? Merge(byte? current, byte? kind)
        {
            if (kind is null)
                return current;

            if (current is null || current == kind)
                return kind;

            if (IsInteger(current.Value) && IsInteger(kind.Value) && current != FlatGeobufFormat.ColumnULong && kind != FlatGeobufFormat.ColumnULong)
                return FlatGeobufFormat.ColumnLong;

            if (IsNumber(current.Value) && IsNumber(kind.Value))
                return FlatGeobufFormat.ColumnDouble;

            return FlatGeobufFormat.ColumnString;
        }

        static bool IsInteger(byte kind) => kind is >= FlatGeobufFormat.ColumnByte and <= FlatGeobufFormat.ColumnULong and not FlatGeobufFormat.ColumnBool;

        static bool IsNumber(byte kind) => IsInteger(kind) || kind is FlatGeobufFormat.ColumnFloat or FlatGeobufFormat.ColumnDouble;
    }

    /// <summary>Gets the column type of a value, or <see langword="null"/> for a value that is not written.</summary>
    private static byte? GetKind(object? value) => value switch
    {
        null => null,
        bool => FlatGeobufFormat.ColumnBool,
        sbyte => FlatGeobufFormat.ColumnByte,
        byte => FlatGeobufFormat.ColumnUByte,
        short => FlatGeobufFormat.ColumnShort,
        ushort => FlatGeobufFormat.ColumnUShort,
        int => FlatGeobufFormat.ColumnInt,
        uint => FlatGeobufFormat.ColumnUInt,
        long => FlatGeobufFormat.ColumnLong,
        ulong => FlatGeobufFormat.ColumnULong,
        float or Half => FlatGeobufFormat.ColumnFloat,
        double or decimal => FlatGeobufFormat.ColumnDouble,
#if NET11_0_OR_GREATER
        System.Numerics.BFloat16 => FlatGeobufFormat.ColumnFloat,
        System.Numerics.Decimal32 or System.Numerics.Decimal64 or System.Numerics.Decimal128 => FlatGeobufFormat.ColumnDouble,
#endif
        DateTime or DateTimeOffset or DateOnly => FlatGeobufFormat.ColumnDateTime,
        byte[] => FlatGeobufFormat.ColumnBinary,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement { ValueKind: JsonValueKind.Number } => FlatGeobufFormat.ColumnDouble,
        JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } => FlatGeobufFormat.ColumnBool,
        JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array } => FlatGeobufFormat.ColumnJson,
        _ => FlatGeobufFormat.ColumnString,
    };

    private readonly record struct Column(string Name, byte Type);
}
