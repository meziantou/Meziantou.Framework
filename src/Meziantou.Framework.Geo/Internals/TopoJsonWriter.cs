using System.Collections;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// Writes a TopoJSON topology. The arcs are found as topojson-server does: the positions where the lines and rings meet or
/// part (the junctions) cut them into arcs, and the identical arcs, in either direction, are kept once.
/// </summary>
internal sealed class TopoJsonWriter
{
    private readonly List<KeyValuePair<string, GeoFeatureCollection>> _objects;
    private readonly GeoBounds? _bounds;
    private readonly bool _quantized;
    private readonly double _scaleX;
    private readonly double _scaleY;

    // The distinct positions, quantized when there is a transform, and their index
    private readonly Dictionary<GeoPosition, int> _positionIds = [];
    private readonly List<GeoPosition> _positions = [];

    // The lines and rings, in the order of the geometries, and the arcs they are made of
    private readonly List<TopoPath> _paths = [];
    private readonly List<int[]> _arcs = [];
    private readonly Dictionary<int[], int> _arcIndexes = new(ArcComparer.Instance);
    private int _nextPath;

    public TopoJsonWriter(List<KeyValuePair<string, GeoFeatureCollection>> objects, int? quantization)
    {
        _objects = objects;
        _bounds = GeoBounds.FromPositions(objects.SelectMany(item => item.Value).Where(feature => feature.Geometry is not null).SelectMany(feature => feature.Geometry!.GetPositions()));
        if (quantization is { } n && _bounds is { } bounds)
        {
            _quantized = true;
            _scaleX = bounds.East > bounds.West ? (bounds.East - bounds.West) / (n - 1) : 1;
            _scaleY = bounds.North > bounds.South ? (bounds.North - bounds.South) / (n - 1) : 1;
        }

        foreach (var (_, features) in objects)
        {
            foreach (var feature in features)
            {
                if (feature.Geometry is not null)
                {
                    CollectPaths(feature.Geometry);
                }
            }
        }

        var junctions = FindJunctions();
        foreach (var path in _paths)
        {
            path.Arcs = Cut(path, junctions);
        }
    }

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Topology");
        if (_bounds is { } bounds)
        {
            writer.WriteStartArray("bbox");
            writer.WriteNumberValue(bounds.West);
            writer.WriteNumberValue(bounds.South);
            writer.WriteNumberValue(bounds.East);
            writer.WriteNumberValue(bounds.North);
            writer.WriteEndArray();
            if (_quantized)
            {
                writer.WriteStartObject("transform");
                writer.WriteStartArray("scale");
                writer.WriteNumberValue(_scaleX);
                writer.WriteNumberValue(_scaleY);
                writer.WriteEndArray();
                writer.WriteStartArray("translate");
                writer.WriteNumberValue(bounds.West);
                writer.WriteNumberValue(bounds.South);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }

        writer.WriteStartObject("objects");
        _nextPath = 0;
        foreach (var (name, features) in _objects)
        {
            writer.WriteStartObject(name);
            writer.WriteString("type", "GeometryCollection");
            writer.WriteStartArray("geometries");
            foreach (var feature in features)
            {
                writer.WriteStartObject();
                WriteGeometryMembers(writer, feature.Geometry);
                if (feature.Id is not null)
                {
                    writer.WriteString("id", feature.Id);
                }

                if (feature.Properties.Count > 0)
                {
                    writer.WriteStartObject("properties");
                    foreach (var (propertyName, value) in feature.Properties)
                    {
                        writer.WritePropertyName(propertyName);
                        GeoJson.WriteValue(writer, value);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteStartArray("arcs");
        foreach (var arc in _arcs)
        {
            writer.WriteStartArray();
            var previous = new GeoPosition(0, 0);
            foreach (var id in arc)
            {
                var position = _positions[id];
                if (_quantized)
                {
                    // Delta encoding: each position is the difference from the previous one of the arc
                    WritePosition(writer, new GeoPosition(position.Longitude - previous.Longitude, position.Latitude - previous.Latitude, position.Altitude));
                    previous = position;
                }
                else
                {
                    WritePosition(writer, position);
                }
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private GeoPosition Transform(GeoPosition position)
    {
        if (!_quantized)
            return position;

        var bounds = _bounds!.Value;
        return new GeoPosition(Math.Round((position.Longitude - bounds.West) / _scaleX, MidpointRounding.AwayFromZero), Math.Round((position.Latitude - bounds.South) / _scaleY, MidpointRounding.AwayFromZero), position.Altitude);
    }

    private void CollectPaths(GeoGeometry geometry)
    {
        switch (geometry)
        {
            case GeoLineString lineString:
                AddPath(lineString.Positions, isRing: false);
                break;
            case GeoMultiLineString multiLineString:
                foreach (var line in multiLineString.LineStrings)
                {
                    AddPath(line.Positions, isRing: false);
                }

                break;
            case GeoPolygon polygon:
                AddRings(polygon);
                break;
            case GeoMultiPolygon multiPolygon:
                foreach (var item in multiPolygon.Polygons)
                {
                    AddRings(item);
                }

                break;
            case GeoGeometryCollection collection:
                foreach (var item in collection.Geometries)
                {
                    CollectPaths(item);
                }

                break;
        }
    }

    private void AddRings(GeoPolygon polygon)
    {
        foreach (var ring in polygon.Rings)
        {
            AddPath(ring, isRing: true);
        }
    }

    private void AddPath(IReadOnlyList<GeoPosition> positions, bool isRing)
    {
        var ids = new List<int>(positions.Count + 1);
        foreach (var position in positions)
        {
            var id = GetPositionId(Transform(position));

            // The positions that round to the same value are merged, as each arc of a quantized topology must move
            if (_quantized && ids.Count > 0 && ids[^1] == id)
                continue;

            ids.Add(id);
        }

        if (_quantized && ids.Count is 1 && positions.Count > 1)
        {
            ids.Add(ids[0]);
        }

        if (isRing && ids.Count > 0 && ids[0] != ids[^1])
        {
            ids.Add(ids[0]);
        }

        _paths.Add(new TopoPath([.. ids], isRing));
    }

    private int GetPositionId(GeoPosition position)
    {
        if (!_positionIds.TryGetValue(position, out var id))
        {
            id = _positions.Count;
            _positions.Add(position);
            _positionIds.Add(position, id);
        }

        return id;
    }

    // A position is a junction when it starts or ends a line or a ring, or when its neighbors are not the same everywhere it appears
    private BitArray FindJunctions()
    {
        const int Unset = -2;
        var junctions = new BitArray(_positions.Count);
        var left = new int[_positions.Count];
        var right = new int[_positions.Count];
        Array.Fill(left, Unset);
        foreach (var path in _paths)
        {
            if (IsDegenerate(path))
                continue;

            var ids = path.Ids;
            if (path.IsRing)
            {
                // The first position of a ring is a junction too, so that the ring read back starts at the same position
                junctions[ids[0]] = true;
                var count = ids.Length - 1;
                for (var i = 0; i < count; i++)
                {
                    Visit(ids[i], ids[(i + count - 1) % count], ids[i + 1]);
                }
            }
            else
            {
                junctions[ids[0]] = true;
                junctions[ids[^1]] = true;
                for (var i = 1; i < ids.Length - 1; i++)
                {
                    Visit(ids[i], ids[i - 1], ids[i + 1]);
                }
            }
        }

        return junctions;

        void Visit(int id, int previous, int next)
        {
            if (left[id] is Unset)
            {
                left[id] = previous;
                right[id] = next;
            }
            else if (!(left[id] == previous && right[id] == next) && !(left[id] == next && right[id] == previous))
            {
                junctions[id] = true;
            }
        }
    }

    private static bool IsDegenerate(TopoPath path) => path.Ids.Length < (path.IsRing ? 4 : 2);

    private int[] Cut(TopoPath path, BitArray junctions)
    {
        var ids = path.Ids;
        if (ids.Length is 0)
            return [];

        if (IsDegenerate(path))
            return [AddArc(ids)];

        var arcs = new List<int>();
        var arcStart = 0;
        for (var i = 1; i < ids.Length; i++)
        {
            if (i == ids.Length - 1 || junctions[ids[i]])
            {
                arcs.Add(AddArc(ids[arcStart..(i + 1)]));
                arcStart = i;
            }
        }

        return [.. arcs];
    }

    // An arc already written is referenced by its index, or by its one's complement when it is reversed
    private int AddArc(int[] arc)
    {
        if (_arcIndexes.TryGetValue(arc, out var index))
            return index;

        var reversed = (int[])arc.Clone();
        Array.Reverse(reversed);
        if (_arcIndexes.TryGetValue(reversed, out index))
            return ~index;

        index = _arcs.Count;
        _arcs.Add(arc);
        _arcIndexes.Add(arc, index);
        return index;
    }

    private void WriteGeometryMembers(Utf8JsonWriter writer, GeoGeometry? geometry)
    {
        if (geometry is null)
        {
            writer.WriteNull("type");
            return;
        }

        writer.WriteString("type", geometry.Type.ToString());
        switch (geometry)
        {
            case GeoPoint point:
                writer.WritePropertyName("coordinates");
                WritePosition(writer, Transform(point.Position));
                break;

            case GeoMultiPoint multiPoint:
                writer.WriteStartArray("coordinates");
                foreach (var position in multiPoint.Positions)
                {
                    WritePosition(writer, Transform(position));
                }

                writer.WriteEndArray();
                break;

            case GeoLineString:
                writer.WritePropertyName("arcs");
                WriteNextPath(writer);
                break;

            case GeoMultiLineString multiLineString:
                writer.WriteStartArray("arcs");
                for (var i = 0; i < multiLineString.LineStrings.Count; i++)
                {
                    WriteNextPath(writer);
                }

                writer.WriteEndArray();
                break;

            case GeoPolygon polygon:
                writer.WritePropertyName("arcs");
                WriteRings(writer, polygon);
                break;

            case GeoMultiPolygon multiPolygon:
                writer.WriteStartArray("arcs");
                foreach (var item in multiPolygon.Polygons)
                {
                    WriteRings(writer, item);
                }

                writer.WriteEndArray();
                break;

            case GeoGeometryCollection collection:
                writer.WriteStartArray("geometries");
                foreach (var item in collection.Geometries)
                {
                    writer.WriteStartObject();
                    WriteGeometryMembers(writer, item);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
        }
    }

    private void WriteRings(Utf8JsonWriter writer, GeoPolygon polygon)
    {
        writer.WriteStartArray();
        for (var i = 0; i < polygon.Rings.Count; i++)
        {
            WriteNextPath(writer);
        }

        writer.WriteEndArray();
    }

    // The paths are written in the order they were collected
    private void WriteNextPath(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        foreach (var arc in _paths[_nextPath++].Arcs)
        {
            writer.WriteNumberValue(arc);
        }

        writer.WriteEndArray();
    }

    private void WritePosition(Utf8JsonWriter writer, GeoPosition position)
    {
        writer.WriteStartArray();
        if (_quantized)
        {
            writer.WriteNumberValue((long)position.Longitude);
            writer.WriteNumberValue((long)position.Latitude);
        }
        else
        {
            writer.WriteNumberValue(position.Longitude);
            writer.WriteNumberValue(position.Latitude);
        }

        if (position.Altitude is { } altitude)
        {
            writer.WriteNumberValue(altitude);
        }

        writer.WriteEndArray();
    }

    private sealed class TopoPath(int[] ids, bool isRing)
    {
        public int[] Ids { get; } = ids;

        public bool IsRing { get; } = isRing;

        public int[] Arcs { get; set; } = [];
    }

    private sealed class ArcComparer : IEqualityComparer<int[]>
    {
        public static ArcComparer Instance { get; } = new();

        public bool Equals(int[]? x, int[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(int[] obj)
        {
            var hash = new HashCode();
            foreach (var item in obj)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }
    }
}
