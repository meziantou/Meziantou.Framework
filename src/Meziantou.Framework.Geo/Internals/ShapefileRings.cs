namespace Meziantou.Framework.Geo.Internals;

/// <summary>Groups the rings of a shapefile polygon into polygons, and computes the orientation of a ring.</summary>
internal static class ShapefileRings
{
    // The number of vertices of a hole tested against an exterior ring: a vertex on the edge of the exterior ring gives an
    // unspecified result, so the majority of a few vertices decides
    private const int SampleCount = 9;

    /// <summary>
    /// Groups rings into a <see cref="GeoPolygon"/> or a <see cref="GeoMultiPolygon"/>. The clockwise rings are exterior
    /// rings and the counterclockwise rings are holes, each assigned to the smallest exterior ring that contains it; a hole
    /// in no exterior ring becomes a polygon. When no ring is clockwise, the orientation is ignored: a ring is a hole when
    /// it is in an odd number of rings.
    /// </summary>
    public static GeoGeometry? Assemble(List<GeoPosition[]> rings)
    {
        if (rings.Count is 0)
            return null;

        if (rings.Count is 1)
            return new GeoPolygon(rings);

        var infos = new RingInfo[rings.Count];
        for (var i = 0; i < rings.Count; i++)
        {
            infos[i] = new RingInfo(rings[i], GetSignedArea(rings[i]), GetOrientation(rings[i]), GetBounds(rings[i]));
        }

        // A ring whose orientation cannot be computed (NaN, when the coordinates overflow) is an exterior ring, so it is never dropped
        var polygons = infos.Any(info => !IsHole(info)) ? AssembleByOrientation(infos) : AssembleByNesting(infos);
        if (polygons.Count is 1)
            return new GeoPolygon(polygons[0]);

        return new GeoMultiPolygon(polygons.Select(polygon => new GeoPolygon(polygon)));
    }

    /// <summary>
    /// Gets the orientation of a ring: negative when it is clockwise, positive when it is counterclockwise, 0 when it is
    /// flat. It is twice the signed area, adding the terms of the edges in one order then in the other: reversing the ring
    /// negates each term exactly and swaps the two sums, so it negates the result exactly, and the reader and the writer
    /// agree on the orientation of a ring whatever the magnitude of its coordinates. The signed area alone does not, as the
    /// rounding of its sum depends on the order of its terms.
    /// </summary>
    public static double GetOrientation(IReadOnlyList<GeoPosition> ring)
    {
        var forward = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            forward += GetTerm(ring, i);
        }

        var backward = 0.0;
        for (var i = ring.Count - 1; i >= 0; i--)
        {
            backward += GetTerm(ring, i);
        }

        return forward + backward;

        static double GetTerm(IReadOnlyList<GeoPosition> ring, int index)
        {
            var a = ring[index];
            var b = ring[(index + 1) % ring.Count];
            return (a.Longitude * b.Latitude) - (b.Longitude * a.Latitude);
        }
    }

    /// <summary>Gets the signed area of a ring in square degrees: negative when the ring is clockwise.</summary>
    public static double GetSignedArea(IReadOnlyList<GeoPosition> ring)
    {
        var area = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            area += (a.Longitude * b.Latitude) - (b.Longitude * a.Latitude);
        }

        return area / 2;
    }

    private static List<List<GeoPosition[]>> AssembleByOrientation(RingInfo[] infos)
    {
        var polygons = new List<List<GeoPosition[]>>();
        var exteriors = new List<(RingInfo Ring, List<GeoPosition[]> Polygon)>();
        foreach (var info in infos)
        {
            if (!IsHole(info))
            {
                var polygon = new List<GeoPosition[]> { info.Positions };
                polygons.Add(polygon);
                exteriors.Add((info, polygon));
            }
        }

        foreach (var info in infos)
        {
            if (IsHole(info))
            {
                List<GeoPosition[]>? best = null;
                var bestArea = double.PositiveInfinity;
                foreach (var (exterior, polygon) in exteriors)
                {
                    var area = Math.Abs(exterior.SignedArea);
                    if (area < bestArea && Contains(exterior, info))
                    {
                        best = polygon;
                        bestArea = area;
                    }
                }

                if (best is null)
                {
                    polygons.Add([info.Positions]);
                }
                else
                {
                    best.Add(info.Positions);
                }
            }
        }

        return polygons;
    }

    private static List<List<GeoPosition[]>> AssembleByNesting(RingInfo[] infos)
    {
        var polygons = new List<List<GeoPosition[]>>();
        var placed = new List<(RingInfo Ring, int Depth, List<GeoPosition[]> Polygon)>();
        foreach (var info in infos.OrderByDescending(info => Math.Abs(info.SignedArea)))
        {
            // The rings are placed from the largest, so the last ring that contains this one is the smallest
            var parentIndex = placed.FindLastIndex(item => Contains(item.Ring, info));
            if (parentIndex >= 0 && placed[parentIndex].Depth % 2 is 0)
            {
                var parent = placed[parentIndex];
                parent.Polygon.Add(info.Positions);
                placed.Add((info, parent.Depth + 1, parent.Polygon));
            }
            else
            {
                var polygon = new List<GeoPosition[]> { info.Positions };
                polygons.Add(polygon);
                placed.Add((info, parentIndex >= 0 ? placed[parentIndex].Depth + 1 : 0, polygon));
            }
        }

        return polygons;
    }

    private static bool Contains(RingInfo outer, RingInfo inner)
    {
        if (!outer.Bounds.Contains(new GeoPosition(inner.Bounds.West, inner.Bounds.South)) || !outer.Bounds.Contains(new GeoPosition(inner.Bounds.East, inner.Bounds.North)))
            return false;

        // The samples do not depend on the direction of the ring, so neither does the result
        var positions = inner.Positions.Order(PositionComparer.Instance).ToArray();
        var count = Math.Min(SampleCount, positions.Length);
        var inside = 0;
        for (var i = 0; i < count; i++)
        {
            if (ContainsPosition(outer.Positions, positions[(int)((long)i * positions.Length / count)]))
            {
                inside++;
            }
        }

        return inside * 2 > count;
    }

    // Even-odd rule, as GeoPolygon.Contains
    private static bool ContainsPosition(GeoPosition[] ring, GeoPosition position)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            // Each edge from its lower end, so that the rounding does not depend on the direction of the ring
            var (a, b) = ring[i].Latitude <= ring[j].Latitude ? (ring[i], ring[j]) : (ring[j], ring[i]);
            if ((a.Latitude > position.Latitude) != (b.Latitude > position.Latitude) &&
                position.Longitude < ((b.Longitude - a.Longitude) * (position.Latitude - a.Latitude) / (b.Latitude - a.Latitude)) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool IsHole(RingInfo info) => info.Orientation > 0;

    private static GeoBounds GetBounds(GeoPosition[] ring) => GeoBounds.FromPositions(ring) ?? default;

    private sealed class PositionComparer : IComparer<GeoPosition>
    {
        public static PositionComparer Instance { get; } = new();

        public int Compare(GeoPosition x, GeoPosition y)
        {
            var result = x.Longitude.CompareTo(y.Longitude);
            return result is 0 ? x.Latitude.CompareTo(y.Latitude) : result;
        }
    }

    private readonly record struct RingInfo(GeoPosition[] Positions, double SignedArea, double Orientation, GeoBounds Bounds);
}
