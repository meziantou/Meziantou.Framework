using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>Reads and writes the Well-Known Binary (WKB) representation of a geometry, as defined by the OGC Simple Features and ISO 19125, and the extended WKB of PostGIS (EWKB).</summary>
/// <remarks>
/// <para>
/// The reader accepts both byte orders, for each nested geometry, the ISO type codes of the geometries with an altitude
/// or a measure (1001 for a point with Z, 2001 with M, 3001 with Z and M), and the flags of EWKB (Z, M, and SRID
/// followed by the SRID, which is ignored). It keeps the Z values as <see cref="GeoPosition.Altitude"/>, a NaN Z value
/// becoming no altitude, and drops the M values (measures). An empty point, written with NaN coordinates as GEOS and
/// PostGIS do, becomes an empty <see cref="GeoMultiPoint"/>, as a <see cref="GeoPoint"/> always has a position, and the
/// empty points of a multipoint are skipped. The other types are rejected, such as a circular string, a triangle or a TIN.
/// </para>
/// <para>
/// The writer writes ISO WKB in little-endian order. A geometry has a single number of dimensions: when a position of
/// the geometry has an altitude, every position is written with one (type codes 1001 to 1007), and a missing altitude
/// is written as 0. PostGIS reads this WKB with <c>ST_GeomFromWKB</c>, and its hexadecimal text with a cast to
/// <c>geometry</c>.
/// </para>
/// </remarks>
public static class WellKnownBinary
{
    // GEOMETRYCOLLECTION can nest: limit the depth so a hostile input cannot overflow the stack
    private const int MaxDepth = 100;

    private const uint EwkbZFlag = 0x80000000;
    private const uint EwkbMFlag = 0x40000000;
    private const uint EwkbSridFlag = 0x20000000;

    /// <summary>Reads the WKB, or the EWKB, of a geometry.</summary>
    /// <param name="data">The WKB.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="FormatException">The data is not the WKB of a supported geometry, is truncated, or has bytes after the geometry.</exception>
    public static GeoGeometry Read(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        var geometry = reader.ReadGeometry(depth: 0, expectedType: null) ?? new GeoMultiPoint([]);
        if (reader.Offset != data.Length)
            throw new FormatException($"The WKB has {(data.Length - reader.Offset).ToString(CultureInfo.InvariantCulture)} bytes after the geometry, at offset {reader.Offset.ToString(CultureInfo.InvariantCulture)}.");

        return geometry;
    }

    /// <summary>Reads the hexadecimal text of the WKB, or the EWKB, of a geometry, as PostGIS writes a <c>geometry</c> value.</summary>
    /// <param name="hex">The hexadecimal text, in any case, such as <c>0101000000000000000000F03F0000000000000040</c>.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="FormatException">The text is not hexadecimal, or not the WKB of a supported geometry.</exception>
    public static GeoGeometry ParseHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        byte[] data;
        try
        {
            data = Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new FormatException("The text is not hexadecimal: " + ex.Message, ex);
        }

        return Read(data);
    }

    /// <summary>Writes the WKB of a geometry, in little-endian order.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The WKB of the geometry.</returns>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static byte[] Write(GeoGeometry geometry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Write(geometry, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Writes the WKB of a geometry, in little-endian order.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="writer">The writer to write the WKB to.</param>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static void Write(GeoGeometry geometry, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(writer);
        GeoValidation.EnsureFinite(geometry, nameof(geometry));
        var hasAltitude = geometry.GetPositions().Any(position => position.Altitude is not null);
        WriteGeometry(writer, geometry, hasAltitude);
    }

    /// <summary>Writes the hexadecimal text of the WKB of a geometry, in little-endian order and in uppercase, as PostGIS writes a <c>geometry</c> value.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The hexadecimal text of the WKB of the geometry.</returns>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static string FormatHex(GeoGeometry geometry) => Convert.ToHexString(Write(geometry));

    private static void WriteGeometry(IBufferWriter<byte> writer, GeoGeometry geometry, bool hasAltitude)
    {
        var header = writer.GetSpan(5);
        header[0] = 1; // Little-endian
        BinaryPrimitives.WriteUInt32LittleEndian(header[1..], (uint)geometry.Type + (hasAltitude ? 1000u : 0u));
        writer.Advance(5);
        switch (geometry)
        {
            case GeoPoint point:
                WritePosition(writer, point.Position, hasAltitude);
                break;

            case GeoLineString lineString:
                WritePositions(writer, lineString.Positions, hasAltitude);
                break;

            case GeoPolygon polygon:
                WritePolygon(writer, polygon, hasAltitude);
                break;

            case GeoMultiPoint multiPoint:
                WriteCount(writer, multiPoint.Positions.Count);
                foreach (var position in multiPoint.Positions)
                {
                    WriteGeometry(writer, new GeoPoint(position), hasAltitude);
                }

                break;

            case GeoMultiLineString multiLineString:
                WriteParts(writer, multiLineString.LineStrings, hasAltitude);
                break;

            case GeoMultiPolygon multiPolygon:
                WriteParts(writer, multiPolygon.Polygons, hasAltitude);
                break;

            case GeoGeometryCollection collection:
                WriteParts(writer, collection.Geometries, hasAltitude);
                break;
        }
    }

    private static void WriteParts(IBufferWriter<byte> writer, IReadOnlyList<GeoGeometry> parts, bool hasAltitude)
    {
        WriteCount(writer, parts.Count);
        foreach (var part in parts)
        {
            WriteGeometry(writer, part, hasAltitude);
        }
    }

    private static void WritePolygon(IBufferWriter<byte> writer, GeoPolygon polygon, bool hasAltitude)
    {
        WriteCount(writer, polygon.Rings.Count);
        foreach (var ring in polygon.Rings)
        {
            WritePositions(writer, ring, hasAltitude);
        }
    }

    private static void WritePositions(IBufferWriter<byte> writer, IReadOnlyList<GeoPosition> positions, bool hasAltitude)
    {
        WriteCount(writer, positions.Count);
        foreach (var position in positions)
        {
            WritePosition(writer, position, hasAltitude);
        }
    }

    private static void WriteCount(IBufferWriter<byte> writer, int count)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(4), (uint)count);
        writer.Advance(4);
    }

    private static void WritePosition(IBufferWriter<byte> writer, GeoPosition position, bool hasAltitude)
    {
        var size = hasAltitude ? 24 : 16;
        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteDoubleLittleEndian(span, position.Longitude);
        BinaryPrimitives.WriteDoubleLittleEndian(span[8..], position.Latitude);
        if (hasAltitude)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span[16..], position.Altitude ?? 0);
        }

        writer.Advance(size);
    }

    private ref struct Reader
    {
        // The smallest geometry: a byte order, a type and an empty count
        private const int MinGeometrySize = 9;

        private readonly ReadOnlySpan<byte> _data;

        // The byte order of the geometry being read. A parent never reads after its parts, so a part can overwrite it.
        private bool _littleEndian;

        public Reader(ReadOnlySpan<byte> data)
        {
            _data = data;
        }

        public int Offset { get; private set; }

        /// <summary>Reads a geometry. Returns <see langword="null"/> for an empty point.</summary>
        public GeoGeometry? ReadGeometry(int depth, GeoGeometryType? expectedType)
        {
            var start = Offset;
            _littleEndian = ReadByte() switch
            {
                0 => false,
                1 => true,
                var value => throw Error($"The byte order {value.ToString(CultureInfo.InvariantCulture)} is not valid, expected 0 or 1", start),
            };

            var code = ReadUInt32();
            var hasZ = (code & EwkbZFlag) is not 0;
            var hasM = (code & EwkbMFlag) is not 0;
            var hasSrid = (code & EwkbSridFlag) is not 0;
            var isoCode = code & 0x0FFFFFFF;
            switch (isoCode / 1000)
            {
                case 0:
                    break;
                case 1:
                    hasZ = true;
                    break;
                case 2:
                    hasM = true;
                    break;
                case 3:
                    hasZ = true;
                    hasM = true;
                    break;
                default:
                    throw Error($"The geometry type {code.ToString(CultureInfo.InvariantCulture)} is not supported", start + 1);
            }

            var typeCode = isoCode % 1000;
            if (typeCode is < 1 or > 7)
                throw Error($"The geometry type {code.ToString(CultureInfo.InvariantCulture)} is not supported", start + 1);

            var type = (GeoGeometryType)typeCode;
            if (expectedType is not null && type != expectedType)
                throw Error($"A {expectedType} was expected but the geometry is a {type}", start + 1);

            if (hasSrid)
            {
                _ = ReadUInt32();
            }

            var dimensions = 2 + (hasZ ? 1 : 0) + (hasM ? 1 : 0);
            switch (type)
            {
                case GeoGeometryType.Point:
                    var position = ReadPosition(hasZ, hasM);
                    return double.IsNaN(position.Longitude) && double.IsNaN(position.Latitude) ? null : new GeoPoint(position);

                case GeoGeometryType.LineString:
                    return new GeoLineString(ReadPositions(hasZ, hasM, dimensions));

                case GeoGeometryType.Polygon:
                    return ReadPolygon(hasZ, hasM, dimensions);

                case GeoGeometryType.MultiPoint:
                    var count = ReadCount(MinGeometrySize);
                    var positions = new List<GeoPosition>(count);
                    for (var i = 0; i < count; i++)
                    {
                        if (ReadGeometry(depth + 1, GeoGeometryType.Point) is GeoPoint point)
                        {
                            positions.Add(point.Position);
                        }
                    }

                    return new GeoMultiPoint(positions);

                case GeoGeometryType.MultiLineString:
                    return new GeoMultiLineString(ReadParts<GeoLineString>(depth, GeoGeometryType.LineString));

                case GeoGeometryType.MultiPolygon:
                    return new GeoMultiPolygon(ReadParts<GeoPolygon>(depth, GeoGeometryType.Polygon));

                default:
                    if (depth >= MaxDepth)
                        throw Error($"The geometry collections are nested more than {MaxDepth.ToString(CultureInfo.InvariantCulture)} levels deep", start);

                    var geometryCount = ReadCount(MinGeometrySize);
                    var geometries = new List<GeoGeometry>(geometryCount);
                    for (var i = 0; i < geometryCount; i++)
                    {
                        geometries.Add(ReadGeometry(depth + 1, expectedType: null) ?? new GeoMultiPoint([]));
                    }

                    return new GeoGeometryCollection(geometries);
            }
        }

        private List<T> ReadParts<T>(int depth, GeoGeometryType type)
            where T : GeoGeometry
        {
            var count = ReadCount(MinGeometrySize);
            var parts = new List<T>(count);
            for (var i = 0; i < count; i++)
            {
                parts.Add((T)ReadGeometry(depth + 1, type)!);
            }

            return parts;
        }

        private GeoPolygon ReadPolygon(bool hasZ, bool hasM, int dimensions)
        {
            var count = ReadCount(4);
            var rings = new GeoPosition[count][];
            for (var i = 0; i < count; i++)
            {
                rings[i] = ReadPositions(hasZ, hasM, dimensions);
            }

            return new GeoPolygon(rings);
        }

        private GeoPosition[] ReadPositions(bool hasZ, bool hasM, int dimensions)
        {
            var count = ReadCount(dimensions * 8);
            var positions = new GeoPosition[count];
            for (var i = 0; i < count; i++)
            {
                positions[i] = ReadPosition(hasZ, hasM);
            }

            return positions;
        }

        private GeoPosition ReadPosition(bool hasZ, bool hasM)
        {
            var x = ReadDouble();
            var y = ReadDouble();
            double? z = null;
            if (hasZ)
            {
                var value = ReadDouble();
                if (!double.IsNaN(value))
                {
                    z = value;
                }
            }

            if (hasM)
            {
                _ = ReadDouble();
            }

            return new GeoPosition(x, y, z);
        }

        /// <summary>Reads a count, and checks the remaining data can hold that many items, so a hostile count does not allocate a huge array.</summary>
        private int ReadCount(int minItemSize)
        {
            var start = Offset;
            var count = ReadUInt32();
            if (count > (uint)((_data.Length - Offset) / minItemSize))
                throw Error($"The count {count.ToString(CultureInfo.InvariantCulture)} exceeds the size of the data", start);

            return (int)count;
        }

        private byte ReadByte()
        {
            EnsureAvailable(1);
            return _data[Offset++];
        }

        private uint ReadUInt32()
        {
            EnsureAvailable(4);
            var span = _data.Slice(Offset, 4);
            Offset += 4;
            return _littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span);
        }

        private double ReadDouble()
        {
            EnsureAvailable(8);
            var span = _data.Slice(Offset, 8);
            Offset += 8;
            return _littleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(span) : BinaryPrimitives.ReadDoubleBigEndian(span);
        }

        private readonly void EnsureAvailable(int size)
        {
            if (_data.Length - Offset < size)
                throw Error("The WKB is truncated", Offset);
        }

        private static FormatException Error(string message, int offset) => new($"{message} at offset {offset.ToString(CultureInfo.InvariantCulture)} of the WKB.");
    }
}
