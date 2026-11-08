using System.Buffers.Binary;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Writes the geometries of features as the <c>.shp</c> and <c>.shx</c> files of a shapefile.</summary>
internal sealed class ShapefileShapeWriter
{
    private const int HeaderLength = 100;
    private const int RecordHeaderLength = 8;

    // The largest content that fits in an array with its record header
    private const int MaxContentLength = 0x7FFFFFC7 - RecordHeaderLength;

    private readonly Shape?[] _shapes;
    private readonly int _shapeType;
    private readonly bool _hasZ;
    private readonly bool _hasM;
    private readonly GeoBounds? _bounds;
    private readonly double _zMin;
    private readonly double _zMax;
    private readonly double _mMin;
    private readonly double _mMax;
    private readonly long _shpLength;

    private ShapefileShapeWriter(Shape?[] shapes, int shapeType, bool hasZ, bool hasM, string paramName)
    {
        _shapes = shapes;
        _shapeType = shapeType;
        _hasZ = hasZ;
        _hasM = hasM;
        _zMin = double.PositiveInfinity;
        _zMax = double.NegativeInfinity;
        _mMin = double.PositiveInfinity;
        _mMax = double.NegativeInfinity;
        _shpLength = HeaderLength;
        foreach (var shape in shapes)
        {
            var contentLength = GetContentLength(shape);
            if (contentLength > MaxContentLength)
                throw new ArgumentException("A geometry has too many positions for a shapefile record.", paramName);

            _shpLength += RecordHeaderLength + contentLength;
            if (shape is not null)
            {
                _bounds = _bounds is { } bounds ? bounds.Union(shape.Bounds) : shape.Bounds;
                foreach (var position in shape.Points)
                {
                    var z = position.Altitude ?? 0;
                    _zMin = Math.Min(_zMin, z);
                    _zMax = Math.Max(_zMax, z);
                    if (position.Measure is { } m)
                    {
                        _mMin = Math.Min(_mMin, m);
                        _mMax = Math.Max(_mMax, m);
                    }
                }
            }
        }

        if (_zMin > _zMax)
        {
            _zMin = 0;
            _zMax = 0;
        }

        if (_mMin > _mMax)
        {
            _mMin = ShapefileShapeType.NoData;
            _mMax = ShapefileShapeType.NoData;
        }

        // The lengths and the offsets are 32-bit counts of 16-bit words
        if (_shpLength / 2 > int.MaxValue)
            throw new ArgumentException("The geometries are too large for a shapefile: the .shp file would be larger than 4 GB.", paramName);
    }

    /// <summary>Gets the shape type of the features and prepares their shapes.</summary>
    /// <exception cref="ArgumentException">The features mix points, lines and polygons, or have a <see cref="GeoGeometryCollection"/>.</exception>
    public static ShapefileShapeWriter Create(GeoFeatureCollection features, string paramName)
    {
        var family = Family.None;
        var multiPoint = false;
        var hasZ = false;
        var hasM = false;
        foreach (var feature in features)
        {
            if (feature.Geometry is not { } geometry)
                continue;

            var geometryFamily = geometry switch
            {
                GeoPoint => Family.Point,
                GeoMultiPoint => Family.Point,
                GeoLineString or GeoMultiLineString => Family.Line,
                GeoPolygon or GeoMultiPolygon => Family.Polygon,
                _ => throw new ArgumentException("A shapefile cannot store a GeometryCollection: split it into features of points, lines or polygons.", paramName),
            };

            if (family is not Family.None && family != geometryFamily)
                throw new ArgumentException($"A shapefile has one shape type, but the features mix {GetName(family)} and {GetName(geometryFamily)}: write them in separate shapefiles.", paramName);

            family = geometryFamily;
            multiPoint |= geometry is GeoMultiPoint;
            hasZ |= geometry.GetPositions().Any(position => position.Altitude is not null);
            hasM |= geometry.GetPositions().Any(position => position.Measure is not null);
        }

        // The Z shapes hold the M values too, so the M shapes are for the measures without altitudes
        var shapeType = (family, multiPoint, hasZ, hasM) switch
        {
            (Family.None, _, _, _) => ShapefileShapeType.Null,
            (Family.Point, false, true, _) => ShapefileShapeType.PointZ,
            (Family.Point, false, false, true) => ShapefileShapeType.PointM,
            (Family.Point, false, false, false) => ShapefileShapeType.Point,
            (Family.Point, true, true, _) => ShapefileShapeType.MultiPointZ,
            (Family.Point, true, false, true) => ShapefileShapeType.MultiPointM,
            (Family.Point, true, false, false) => ShapefileShapeType.MultiPoint,
            (Family.Line, _, true, _) => ShapefileShapeType.PolyLineZ,
            (Family.Line, _, false, true) => ShapefileShapeType.PolyLineM,
            (Family.Line, _, false, false) => ShapefileShapeType.PolyLine,
            (_, _, true, _) => ShapefileShapeType.PolygonZ,
            (_, _, false, true) => ShapefileShapeType.PolygonM,
            _ => ShapefileShapeType.Polygon,
        };

        var shapes = new Shape?[features.Count];
        for (var i = 0; i < features.Count; i++)
        {
            shapes[i] = CreateShape(features[i].Geometry);
        }

        return new ShapefileShapeWriter(shapes, shapeType, hasZ, hasM, paramName);
    }

    public void WriteShp(Stream stream)
    {
        WriteHeader(stream, _shpLength);
        var buffer = new byte[256];
        for (var i = 0; i < _shapes.Length; i++)
        {
            var shape = _shapes[i];
            var contentLength = (int)GetContentLength(shape);
            var length = RecordHeaderLength + contentLength;
            if (buffer.Length < length)
            {
                buffer = new byte[Math.Max(length, buffer.Length * 2)];
            }

            var record = buffer.AsSpan(0, length);
            record.Clear();
            BinaryPrimitives.WriteInt32BigEndian(record, i + 1);
            BinaryPrimitives.WriteInt32BigEndian(record[4..], contentLength / 2);
            WriteContent(record[RecordHeaderLength..], shape);
            stream.Write(record);
        }
    }

    public void WriteShx(Stream stream)
    {
        WriteHeader(stream, HeaderLength + (8L * _shapes.Length));
        var record = new byte[8];
        long offset = HeaderLength;
        foreach (var shape in _shapes)
        {
            var contentLength = GetContentLength(shape);
            BinaryPrimitives.WriteInt32BigEndian(record, (int)(offset / 2));
            BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(4), (int)(contentLength / 2));
            stream.Write(record);
            offset += RecordHeaderLength + contentLength;
        }
    }

    private void WriteHeader(Stream stream, long fileLength)
    {
        var header = new byte[HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header, ShapefileShapeType.FileCode);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24), (int)(fileLength / 2));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), ShapefileShapeType.Version);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), _shapeType);
        var bounds = _bounds ?? default;
        WriteBounds(header.AsSpan(36), bounds);
        if (_hasZ)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(68), _zMin);
            BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(76), _zMax);
        }

        if (_hasM)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(84), _mMin);
            BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(92), _mMax);
        }

        stream.Write(header);
    }

    private long GetContentLength(Shape? shape)
    {
        if (shape is null)
            return 4;

        long points = shape.Points.Length;
        var zLength = _hasZ ? 16 + (8 * points) : 0;
        var mLength = _hasM ? 16 + (8 * points) : 0;
        return _shapeType switch
        {
            ShapefileShapeType.Point or ShapefileShapeType.PointZ or ShapefileShapeType.PointM => 20 + (_hasZ ? 8 : 0) + (_hasM ? 8 : 0),
            ShapefileShapeType.MultiPoint or ShapefileShapeType.MultiPointZ or ShapefileShapeType.MultiPointM => 40 + (16 * points) + zLength + mLength,
            _ => 44 + (4L * shape.PartStarts.Length) + (16 * points) + zLength + mLength,
        };
    }

    private void WriteContent(Span<byte> content, Shape? shape)
    {
        if (shape is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(content, ShapefileShapeType.Null);
            return;
        }

        BinaryPrimitives.WriteInt32LittleEndian(content, _shapeType);
        var points = shape.Points;
        if (_shapeType is ShapefileShapeType.Point or ShapefileShapeType.PointZ or ShapefileShapeType.PointM)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(content[4..], points[0].Longitude);
            BinaryPrimitives.WriteDoubleLittleEndian(content[12..], points[0].Latitude);
            if (_hasZ)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(content[20..], points[0].Altitude ?? 0);
            }

            if (_hasM)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(content[(_hasZ ? 28 : 20)..], points[0].Measure ?? ShapefileShapeType.NoData);
            }

            return;
        }

        WriteBounds(content[4..], shape.Bounds);
        int offset;
        if (_shapeType is ShapefileShapeType.MultiPoint or ShapefileShapeType.MultiPointZ or ShapefileShapeType.MultiPointM)
        {
            BinaryPrimitives.WriteInt32LittleEndian(content[36..], points.Length);
            offset = 40;
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(content[36..], shape.PartStarts.Length);
            BinaryPrimitives.WriteInt32LittleEndian(content[40..], points.Length);
            offset = 44;
            foreach (var start in shape.PartStarts)
            {
                BinaryPrimitives.WriteInt32LittleEndian(content[offset..], start);
                offset += 4;
            }
        }

        foreach (var position in points)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(content[offset..], position.Longitude);
            BinaryPrimitives.WriteDoubleLittleEndian(content[(offset + 8)..], position.Latitude);
            offset += 16;
        }

        if (_hasZ)
        {
            var zMin = points.Min(position => position.Altitude ?? 0);
            var zMax = points.Max(position => position.Altitude ?? 0);
            BinaryPrimitives.WriteDoubleLittleEndian(content[offset..], zMin);
            BinaryPrimitives.WriteDoubleLittleEndian(content[(offset + 8)..], zMax);
            offset += 16;
            foreach (var position in points)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(content[offset..], position.Altitude ?? 0);
                offset += 8;
            }
        }

        if (_hasM)
        {
            var mMin = double.PositiveInfinity;
            var mMax = double.NegativeInfinity;
            foreach (var position in points)
            {
                if (position.Measure is { } m)
                {
                    mMin = Math.Min(mMin, m);
                    mMax = Math.Max(mMax, m);
                }
            }

            if (mMin > mMax)
            {
                mMin = ShapefileShapeType.NoData;
                mMax = ShapefileShapeType.NoData;
            }

            BinaryPrimitives.WriteDoubleLittleEndian(content[offset..], mMin);
            BinaryPrimitives.WriteDoubleLittleEndian(content[(offset + 8)..], mMax);
            offset += 16;
            foreach (var position in points)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(content[offset..], position.Measure ?? ShapefileShapeType.NoData);
                offset += 8;
            }
        }
    }

    private static void WriteBounds(Span<byte> destination, GeoBounds bounds)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(destination, bounds.West);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[8..], bounds.South);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[16..], bounds.East);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[24..], bounds.North);
    }

    private static Shape? CreateShape(GeoGeometry? geometry)
    {
        var parts = new List<GeoPosition[]>();
        switch (geometry)
        {
            case GeoPoint point:
                parts.Add([point.Position]);
                break;
            case GeoMultiPoint multiPoint:
                parts.Add([.. multiPoint.Positions]);
                break;
            case GeoLineString lineString:
                parts.Add([.. lineString.Positions]);
                break;
            case GeoMultiLineString multiLineString:
                parts.AddRange(multiLineString.LineStrings.Select(line => line.Positions.ToArray()));
                break;
            case GeoPolygon polygon:
                AddPolygon(parts, polygon);
                break;
            case GeoMultiPolygon multiPolygon:
                foreach (var item in multiPolygon.Polygons)
                {
                    AddPolygon(parts, item);
                }

                break;
        }

        parts.RemoveAll(part => part.Length is 0);
        if (parts.Count is 0)
            return null;

        var starts = new int[parts.Count];
        var count = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            starts[i] = count;
            count += parts[i].Length;
        }

        GeoPosition[] points = [.. parts.SelectMany(part => part)];
        return new Shape(starts, points, GeoBounds.FromPositions(points)!.Value);
    }

    // The exterior ring is clockwise and the holes counterclockwise; each ring ends with its first position
    private static void AddPolygon(List<GeoPosition[]> parts, GeoPolygon polygon)
    {
        if (polygon.Rings.Count is 0 || polygon.Rings[0].Count is 0)
            return;

        for (var i = 0; i < polygon.Rings.Count; i++)
        {
            var ring = polygon.Rings[i];
            if (ring.Count is 0)
                continue;

            var positions = new List<GeoPosition>(ring.Count + 1);
            positions.AddRange(ring);
            if (positions[0].Longitude != positions[^1].Longitude || positions[0].Latitude != positions[^1].Latitude || positions.Count is 1)
            {
                positions.Add(positions[0]);
            }

            var orientation = ShapefileRings.GetOrientation(positions);
            if ((i is 0 && orientation > 0) || (i > 0 && orientation < 0))
            {
                positions.Reverse();
            }

            parts.Add([.. positions]);
        }
    }

    private static string GetName(Family family) => family switch
    {
        Family.Point => "points",
        Family.Line => "lines",
        _ => "polygons",
    };

    private enum Family
    {
        None,
        Point,
        Line,
        Polygon,
    }

    private sealed record Shape(int[] PartStarts, GeoPosition[] Points, GeoBounds Bounds);
}
