using System.Buffers.Binary;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Reads the records of a <c>.shp</c> file, in order, from a stream that may not be seekable.</summary>
internal sealed class ShapefileShapeReader
{
    private const int HeaderLength = 100;
    private const int RecordHeaderLength = 8;

    // The content of a record is read in chunks, so a hostile length does not allocate more than the data the stream has
    private const int ChunkLength = 1 << 20;

    private readonly Stream _stream;
    private readonly long _fileLength;
    private long _position;
    private byte[] _buffer = new byte[256];

    public ShapefileShapeReader(Stream stream)
    {
        _stream = stream;
        var header = new byte[HeaderLength];
        ReadExactly(header, "The .shp file is too short to have a header.");
        if (BinaryPrimitives.ReadInt32BigEndian(header) is not ShapefileShapeType.FileCode)
            throw new FormatException("The .shp file does not start with the file code 9994: it is not a shapefile.");

        _fileLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24)) * 2L;
        if (_fileLength < HeaderLength)
            throw new FormatException("The file length in the header of the .shp file is invalid.");

        _position = HeaderLength;
    }

    /// <summary>Reads the next record.</summary>
    /// <param name="geometry">The geometry, or <see langword="null"/> for a null shape or a shape without positions.</param>
    /// <returns><see langword="true"/> when a record was read; <see langword="false"/> at the end of the file.</returns>
    public bool TryRead(out GeoGeometry? geometry)
    {
        geometry = null;
        if (_position + RecordHeaderLength > _fileLength)
            return false;

        var recordHeader = _buffer.AsSpan(0, RecordHeaderLength);
        ReadExactly(recordHeader, "The .shp file is truncated.");
        var contentLength = BinaryPrimitives.ReadInt32BigEndian(recordHeader[4..]) * 2L;
        _position += RecordHeaderLength;
        if (contentLength < 4 || contentLength > Array.MaxLength || _position + contentLength > _fileLength)
            throw new FormatException($"The record at offset {_position - RecordHeaderLength} of the .shp file has an invalid content length.");

        var length = (int)contentLength;
        ReadContent(length);
        _position += length;
        geometry = ParseShape(_buffer.AsSpan(0, length));
        return true;
    }

    private void ReadContent(int length)
    {
        var read = 0;
        while (read < length)
        {
            var chunk = Math.Min(length - read, ChunkLength);
            if (_buffer.Length < read + chunk)
            {
                Array.Resize(ref _buffer, (int)Math.Min(length, Math.Max(read + chunk, _buffer.Length * 2L)));
            }

            ReadExactly(_buffer.AsSpan(read, chunk), "The .shp file is truncated.");
            read += chunk;
        }
    }

    private void ReadExactly(Span<byte> buffer, string message)
    {
        if (_stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) < buffer.Length)
            throw new FormatException(message);
    }

    private static GeoGeometry? ParseShape(ReadOnlySpan<byte> content)
    {
        var shapeType = BinaryPrimitives.ReadInt32LittleEndian(content);
        return shapeType switch
        {
            ShapefileShapeType.Null => null,
            ShapefileShapeType.Point or ShapefileShapeType.PointM or ShapefileShapeType.PointZ => ParsePoint(content, shapeType is ShapefileShapeType.PointZ),
            ShapefileShapeType.MultiPoint or ShapefileShapeType.MultiPointM or ShapefileShapeType.MultiPointZ => ParseMultiPoint(content, shapeType is ShapefileShapeType.MultiPointZ),
            ShapefileShapeType.PolyLine or ShapefileShapeType.PolyLineM or ShapefileShapeType.PolyLineZ => ParsePolyLine(content, shapeType is ShapefileShapeType.PolyLineZ),
            ShapefileShapeType.Polygon or ShapefileShapeType.PolygonM or ShapefileShapeType.PolygonZ => ParsePolygon(content, shapeType is ShapefileShapeType.PolygonZ),
            ShapefileShapeType.MultiPatch => ParseMultiPatch(content),
            _ => throw new FormatException($"The shape type {shapeType} is not a shapefile shape type."),
        };
    }

    private static GeoPoint? ParsePoint(ReadOnlySpan<byte> content, bool hasZ)
    {
        EnsureLength(content, hasZ ? 28 : 20);
        var x = ReadDouble(content, 4);
        var y = ReadDouble(content, 12);

        // Some writers store a missing point as a point of NaN coordinates
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return null;

        return new GeoPoint(new GeoPosition(x, y, hasZ ? ReadDouble(content, 20) : null));
    }

    private static GeoMultiPoint? ParseMultiPoint(ReadOnlySpan<byte> content, bool hasZ)
    {
        EnsureLength(content, 40);
        var pointCount = ReadCount(content, 36);
        var positions = ReadPositions(content, 40, pointCount, hasZ);
        return positions.Length is 0 ? null : new GeoMultiPoint(positions);
    }

    private static GeoGeometry? ParsePolyLine(ReadOnlySpan<byte> content, bool hasZ)
    {
        var parts = ReadParts(content, hasZ, hasPartTypes: false, out _);
        return parts.Count switch
        {
            0 => null,
            1 => new GeoLineString(parts[0]),
            _ => new GeoMultiLineString(parts.Select(part => new GeoLineString(part))),
        };
    }

    private static GeoGeometry? ParsePolygon(ReadOnlySpan<byte> content, bool hasZ)
    {
        var rings = ReadParts(content, hasZ, hasPartTypes: false, out _);
        return ShapefileRings.Assemble(rings);
    }

    // A multipatch is a set of surfaces: each triangle of a strip or a fan becomes a polygon, as GDAL does. An outer ring
    // or a first ring starts a polygon, and the inner rings and the rings that follow it are its holes.
    private static GeoGeometry? ParseMultiPatch(ReadOnlySpan<byte> content)
    {
        var parts = ReadParts(content, hasZ: true, hasPartTypes: true, out var partTypes);
        var polygons = new List<GeoPolygon>();
        List<GeoPosition[]>? rings = null;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            switch (partTypes[i])
            {
                case 0: // Triangle strip
                case 1: // Triangle fan
                    Flush();
                    for (var j = 2; j < part.Length; j++)
                    {
                        var first = partTypes[i] is 0 ? part[j - 2] : part[0];
                        polygons.Add(new GeoPolygon([[first, part[j - 1], part[j], first]]));
                    }

                    break;

                case 2: // Outer ring
                case 4: // First ring
                    Flush();
                    rings = [part];
                    break;

                case 3: // Inner ring
                case 5: // Ring
                    rings ??= [];
                    rings.Add(part);
                    break;

                default:
                    throw new FormatException($"The multipatch part type {partTypes[i]} is invalid.");
            }
        }

        Flush();
        return polygons.Count switch
        {
            0 => null,
            1 => polygons[0],
            _ => new GeoMultiPolygon(polygons),
        };

        void Flush()
        {
            if (rings is not null)
            {
                polygons.Add(new GeoPolygon(rings));
                rings = null;
            }
        }
    }

    // The layout of a PolyLine, a Polygon and a MultiPatch: box, part count, point count, part starts, [part types], points, [Z range, Z values], [M range, M values]
    private static List<GeoPosition[]> ReadParts(ReadOnlySpan<byte> content, bool hasZ, bool hasPartTypes, out int[] partTypes)
    {
        EnsureLength(content, 44);
        var partCount = ReadCount(content, 36);
        var pointCount = ReadCount(content, 40);
        var partArraysLength = (hasPartTypes ? 8L : 4L) * partCount;
        EnsureLength(content, 44 + partArraysLength);

        var starts = new int[partCount];
        partTypes = hasPartTypes ? new int[partCount] : [];
        for (var i = 0; i < partCount; i++)
        {
            starts[i] = BinaryPrimitives.ReadInt32LittleEndian(content[(44 + (4 * i))..]);
            if (starts[i] < 0 || starts[i] > pointCount || (i > 0 && starts[i] < starts[i - 1]))
                throw new FormatException("The part indexes of a shape are invalid.");

            if (hasPartTypes)
            {
                partTypes[i] = BinaryPrimitives.ReadInt32LittleEndian(content[(44 + (4 * partCount) + (4 * i))..]);
            }
        }

        var positions = ReadPositions(content, (int)(44 + partArraysLength), pointCount, hasZ);
        var parts = new List<GeoPosition[]>(partCount);
        var partTypesList = new List<int>(partCount);
        for (var i = 0; i < partCount; i++)
        {
            var end = i + 1 < partCount ? starts[i + 1] : pointCount;
            if (end > starts[i])
            {
                parts.Add(positions[starts[i]..end]);
                if (hasPartTypes)
                {
                    partTypesList.Add(partTypes[i]);
                }
            }
        }

        // The empty parts are dropped, with their types
        if (hasPartTypes)
        {
            partTypes = [.. partTypesList];
        }

        return parts;
    }

    // The points (X, Y pairs) at an offset, then the Z range and the Z values when the shape has Z; the M values are ignored
    private static GeoPosition[] ReadPositions(ReadOnlySpan<byte> content, int offset, int pointCount, bool hasZ)
    {
        var zOffset = offset + (16L * pointCount) + 16;
        EnsureLength(content, hasZ ? zOffset + (8L * pointCount) : offset + (16L * pointCount));
        var positions = new GeoPosition[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            double? z = hasZ ? ReadDouble(content, (int)zOffset + (8 * i)) : null;
            positions[i] = new GeoPosition(ReadDouble(content, offset + (16 * i)), ReadDouble(content, offset + (16 * i) + 8), z);
        }

        return positions;
    }

    private static int ReadCount(ReadOnlySpan<byte> content, int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(content[offset..]);
        if (count < 0)
            throw new FormatException("A count of a shape is negative.");

        return count;
    }

    private static double ReadDouble(ReadOnlySpan<byte> content, int offset) => BinaryPrimitives.ReadDoubleLittleEndian(content[offset..]);

    private static void EnsureLength(ReadOnlySpan<byte> content, long length)
    {
        if (content.Length < length)
            throw new FormatException("A record of the .shp file is shorter than its shape requires.");
    }
}
