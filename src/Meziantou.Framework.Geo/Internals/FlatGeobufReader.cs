using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Reads FlatGeobuf data: the signature, the header, the optional index, then the size-prefixed features.</summary>
internal static class FlatGeobufReader
{
    public static GeoFeatureCollection Read(ReadOnlySpan<byte> data, GeoBounds? bounds)
    {
        var span = data;
        if (span.Length < 12 || span[0] != 'f' || span[1] != 'g' || span[2] != 'b' || span[4] != 'f' || span[5] != 'g' || span[6] != 'b')
            throw new FormatException("The data is not FlatGeobuf: it does not start with the signature of the format.");

        if (span[3] != FlatGeobufFormat.MajorVersion)
            throw new FormatException($"The version {span[3]} of FlatGeobuf is not supported: only version {FlatGeobufFormat.MajorVersion} is.");

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        if (headerSize > span.Length - 12)
            throw new FormatException("The header of the FlatGeobuf data is truncated.");

        var header = FlatBufferTable.GetRoot(data.Slice(12, (int)headerSize));
        var geometryType = header.GetByte(FlatGeobufFormat.HeaderGeometryType);
        var dimensions = new FlatGeobufDimensions(header.GetBool(FlatGeobufFormat.HeaderHasZ), header.GetBool(FlatGeobufFormat.HeaderHasM));
        var columns = ReadColumns(header);
        var count = header.GetUInt64(FlatGeobufFormat.HeaderFeaturesCount);
        var nodeSize = header.GetUInt16(FlatGeobufFormat.HeaderIndexNodeSize, defaultValue: 16);

        var position = 12L + headerSize;
        var index = ReadOnlySpan<byte>.Empty;
        if (nodeSize > 0 && count > 0)
        {
            var indexSize = PackedRTree.GetSize(count, nodeSize);
            if (indexSize > span.Length - position)
                throw new FormatException("The index of the FlatGeobuf data is truncated.");

            index = span.Slice((int)position, (int)indexSize);
            position += indexSize;
        }

        var features = new GeoFeatureCollection { Name = header.GetString(FlatGeobufFormat.HeaderName) };
        var dataStart = (int)position;
        if (bounds is { } box && !index.IsEmpty)
        {
            foreach (var offset in PackedRTree.Search(index, count, nodeSize, box))
            {
                if (offset < 0 || offset > span.Length - dataStart)
                    throw new FormatException("The index of the FlatGeobuf data refers to a feature outside of the data.");

                features.Add(ReadFeature(data, dataStart + (int)offset, geometryType, dimensions, columns, out _));
            }

            return features;
        }

        var read = 0UL;
        var current = dataStart;
        while (current < span.Length && (count == 0 || read < count))
        {
            var feature = ReadFeature(data, current, geometryType, dimensions, columns, out var next);
            current = next;
            read++;
            if (bounds is { } filter && (feature.Geometry?.GetBounds() is not { } featureBounds || !Intersects(filter, featureBounds)))
                continue;

            features.Add(feature);
        }

        if (count != 0 && read < count)
            throw new FormatException("The FlatGeobuf data has fewer features than its header declares.");

        return features;
    }

    private static bool Intersects(GeoBounds a, GeoBounds b) => b.West <= a.East && b.East >= a.West && b.South <= a.North && b.North >= a.South;

    private static GeoFeature ReadFeature(ReadOnlySpan<byte> data, int position, byte geometryType, FlatGeobufDimensions dimensions, Column[] columns, out int next)
    {
        var span = data;
        if (position > span.Length - 4)
            throw new FormatException("A feature of the FlatGeobuf data is truncated.");

        var size = BinaryPrimitives.ReadUInt32LittleEndian(span[position..]);
        if (size > span.Length - position - 4)
            throw new FormatException("A feature of the FlatGeobuf data is truncated.");

        next = position + 4 + (int)size;
        var feature = FlatBufferTable.GetRoot(data.Slice(position + 4, (int)size));
        var featureColumns = feature.Has(FlatGeobufFormat.FeatureColumns) ? ReadColumns(feature, FlatGeobufFormat.FeatureColumns) : columns;
        GeoGeometry? geometry = null;
        if (feature.TryGetTable(FlatGeobufFormat.FeatureGeometry, out var table))
        {
            geometry = ReadGeometry(table, geometryType, dimensions, depth: 0);
        }

        var properties = ReadProperties(feature.GetVectorBytes(FlatGeobufFormat.FeatureProperties, elementSize: 1, out _), featureColumns);
        return new GeoFeature(geometry, properties);
    }

    private static Column[] ReadColumns(FlatBufferTable table, int field = FlatGeobufFormat.HeaderColumns)
    {
        var vector = table.GetTableVector(field);
        var columns = new Column[vector.Count];
        for (var i = 0; i < columns.Length; i++)
        {
            var column = vector[i];
            columns[i] = new Column(
                column.GetString(FlatGeobufFormat.ColumnName) ?? throw new FormatException("A column of the FlatGeobuf data has no name."),
                column.GetByte(FlatGeobufFormat.ColumnType));
        }

        return columns;
    }

    private static Dictionary<string, object?> ReadProperties(ReadOnlySpan<byte> bytes, Column[] columns)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        var position = 0;
        // A trailing byte, too short for the index of a column, is padding, as GDAL reads it
        while (position + 2 <= bytes.Length)
        {
            var index = BinaryPrimitives.ReadUInt16LittleEndian(Take(bytes, ref position, 2));
            if (index >= columns.Length)
                throw new FormatException("A property of the FlatGeobuf data refers to a column that does not exist.");

            var column = columns[index];
            properties[column.Name] = column.Type switch
            {
                FlatGeobufFormat.ColumnByte => (sbyte)Take(bytes, ref position, 1)[0],
                FlatGeobufFormat.ColumnUByte => Take(bytes, ref position, 1)[0],
                FlatGeobufFormat.ColumnBool => Take(bytes, ref position, 1)[0] != 0,
                FlatGeobufFormat.ColumnShort => BinaryPrimitives.ReadInt16LittleEndian(Take(bytes, ref position, 2)),
                FlatGeobufFormat.ColumnUShort => BinaryPrimitives.ReadUInt16LittleEndian(Take(bytes, ref position, 2)),
                FlatGeobufFormat.ColumnInt => BinaryPrimitives.ReadInt32LittleEndian(Take(bytes, ref position, 4)),
                FlatGeobufFormat.ColumnUInt => BinaryPrimitives.ReadUInt32LittleEndian(Take(bytes, ref position, 4)),
                FlatGeobufFormat.ColumnLong => BinaryPrimitives.ReadInt64LittleEndian(Take(bytes, ref position, 8)),
                FlatGeobufFormat.ColumnULong => BinaryPrimitives.ReadUInt64LittleEndian(Take(bytes, ref position, 8)),
                FlatGeobufFormat.ColumnFloat => BinaryPrimitives.ReadSingleLittleEndian(Take(bytes, ref position, 4)),
                FlatGeobufFormat.ColumnDouble => BinaryPrimitives.ReadDoubleLittleEndian(Take(bytes, ref position, 8)),
                FlatGeobufFormat.ColumnString => Encoding.UTF8.GetString(TakeSized(bytes, ref position)),
                FlatGeobufFormat.ColumnJson => ParseJson(Encoding.UTF8.GetString(TakeSized(bytes, ref position))),
                FlatGeobufFormat.ColumnDateTime => ParseDateTime(Encoding.UTF8.GetString(TakeSized(bytes, ref position))),
                FlatGeobufFormat.ColumnBinary => TakeSized(bytes, ref position).ToArray(),
                _ => throw new FormatException($"The type {column.Type} of the column '{column.Name}' of the FlatGeobuf data is not valid."),
            };
        }

        return properties;

        static ReadOnlySpan<byte> TakeSized(ReadOnlySpan<byte> bytes, ref int position)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(Take(bytes, ref position, 4));
            if (length > bytes.Length - position)
                throw new FormatException("A property of the FlatGeobuf data is truncated.");

            return Take(bytes, ref position, (int)length);
        }

        static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> bytes, ref int position, int length)
        {
            if (length > bytes.Length - position)
                throw new FormatException("A property of the FlatGeobuf data is truncated.");

            var result = bytes.Slice(position, length);
            position += length;
            return result;
        }
    }

    private static object ParseJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static object ParseDateTime(string text)
    {
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            return value;

        return text;
    }

    private static GeoGeometry? ReadGeometry(FlatBufferTable table, byte type, FlatGeobufDimensions dimensions, int depth)
    {
        if (depth > 64)
            throw new FormatException("The geometries of the FlatGeobuf data are nested too deeply.");

        if (type == FlatGeobufFormat.GeometryUnknown || table.Has(FlatGeobufFormat.GeometryType))
        {
            type = table.GetByte(FlatGeobufFormat.GeometryType, type);
        }

        switch (type)
        {
            case FlatGeobufFormat.GeometryPoint:
                var point = ReadPositions(table, dimensions);
                return point.Length == 0 ? null : new GeoPoint(point[0]);

            case FlatGeobufFormat.GeometryMultiPoint:
                return new GeoMultiPoint(ReadPositions(table, dimensions));

            case FlatGeobufFormat.GeometryLineString:
                return new GeoLineString(ReadPositions(table, dimensions));

            case FlatGeobufFormat.GeometryMultiLineString:
                if (table.Has(FlatGeobufFormat.GeometryParts))
                    return new GeoMultiLineString(ReadParts(table, FlatGeobufFormat.GeometryLineString, dimensions, depth).Cast<GeoLineString>());

                return new GeoMultiLineString(Split(table, dimensions).Select(part => new GeoLineString(part)));

            case FlatGeobufFormat.GeometryPolygon:
                return new GeoPolygon(Split(table, dimensions));

            case FlatGeobufFormat.GeometryMultiPolygon:
                return new GeoMultiPolygon(ReadParts(table, FlatGeobufFormat.GeometryPolygon, dimensions, depth).Cast<GeoPolygon>());

            case FlatGeobufFormat.GeometryCollection:
                return new GeoGeometryCollection(ReadParts(table, FlatGeobufFormat.GeometryUnknown, dimensions, depth));

            default:
                throw new FormatException($"The geometry type {type} of the FlatGeobuf data is not supported: only points, lines, polygons, their multi-geometries and collections are.");
        }
    }

    private static List<GeoGeometry> ReadParts(FlatBufferTable table, byte partType, FlatGeobufDimensions dimensions, int depth)
    {
        var parts = new List<GeoGeometry>();
        foreach (var part in table.GetTableVector(FlatGeobufFormat.GeometryParts))
        {
            var geometry = ReadGeometry(part, partType, dimensions, depth + 1);
            var isExpected = partType switch
            {
                FlatGeobufFormat.GeometryLineString => geometry is GeoLineString,
                FlatGeobufFormat.GeometryPolygon => geometry is GeoPolygon,
                _ => true,
            };

            if (!isExpected)
                throw new FormatException("A part of a multi-geometry of the FlatGeobuf data does not have the type of the multi-geometry.");

            if (geometry is not null)
            {
                parts.Add(geometry);
            }
        }

        return parts;
    }

    private static GeoPosition[] ReadPositions(FlatBufferTable table, FlatGeobufDimensions dimensions)
    {
        var xy = table.GetDoubleVector(FlatGeobufFormat.GeometryXY);
        if ((xy.Length & 1) != 0)
            throw new FormatException("The coordinates of a geometry of the FlatGeobuf data are not pairs.");

        var count = xy.Length / 2;
        var z = dimensions.HasZ ? table.GetDoubleVector(FlatGeobufFormat.GeometryZ) : [];
        if (z.Length != 0 && z.Length != count)
            throw new FormatException("A geometry of the FlatGeobuf data does not have an altitude for each position.");

        var m = dimensions.HasM ? table.GetDoubleVector(FlatGeobufFormat.GeometryM) : [];
        if (m.Length != 0 && m.Length != count)
            throw new FormatException("A geometry of the FlatGeobuf data does not have a measure for each position.");

        var positions = new GeoPosition[count];
        for (var i = 0; i < count; i++)
        {
            positions[i] = new GeoPosition(xy[2 * i], xy[(2 * i) + 1], z.Length == 0 ? null : z[i])
            {
                Measure = m.Length == 0 || double.IsNaN(m[i]) ? null : m[i],
            };
        }

        return positions;
    }

    /// <summary>Splits the positions of a geometry at the ends of its parts: the lines of a multi-line or the rings of a polygon.</summary>
    private static List<GeoPosition[]> Split(FlatBufferTable table, FlatGeobufDimensions dimensions)
    {
        var positions = ReadPositions(table, dimensions);
        var ends = table.GetUInt32Vector(FlatGeobufFormat.GeometryEnds);
        if (ends.Length == 0)
            return positions.Length == 0 ? [] : [positions];

        var parts = new List<GeoPosition[]>(ends.Length);
        var start = 0;
        foreach (var end in ends)
        {
            if (end < start || end > positions.Length)
                throw new FormatException("The ends of the parts of a geometry of the FlatGeobuf data are not valid.");

            parts.Add(positions[start..(int)end]);
            start = (int)end;
        }

        return parts;
    }

    private readonly record struct Column(string Name, byte Type);
}
