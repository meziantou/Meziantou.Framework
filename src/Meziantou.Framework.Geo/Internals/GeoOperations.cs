namespace Meziantou.Framework.Geo.Internals;

/// <summary>The geometry operations: repair, clip, simplify, and the boolean operations on polygons.</summary>
internal static class GeoOperations
{
    public static GeoGeometry MakeValid(GeoGeometry geometry)
    {
        ValidateCoordinates(geometry, paramName: null);
        return MakeValidCore(geometry);
    }

    private static GeoGeometry MakeValidCore(GeoGeometry geometry) => geometry switch
    {
        GeoPolygon polygon => ToGeometry(RepairPolygon(polygon), asMultiPolygon: false),
        GeoMultiPolygon multiPolygon => ToGeometry(RepairMultiPolygon(multiPolygon.Polygons), asMultiPolygon: true),
        GeoLineString lineString => new GeoLineString(RemoveDuplicates(lineString.Positions)),
        GeoMultiLineString multiLineString => new GeoMultiLineString(multiLineString.LineStrings.Select(lineString => new GeoLineString(RemoveDuplicates(lineString.Positions)))),
        GeoGeometryCollection collection => new GeoGeometryCollection(collection.Geometries.Select(MakeValidCore)),
        _ => geometry,
    };

    public static GeoGeometry? Clip(GeoGeometry geometry, GeoBounds bounds)
    {
        if (!(bounds.West <= bounds.East && bounds.South <= bounds.North))
            throw new ArgumentException("The box must have its west side before its east side and its south side below its north side.", nameof(bounds));

        if (!IsSupported(bounds.West) || !IsSupported(bounds.East) || !IsSupported(bounds.South) || !IsSupported(bounds.North))
            throw new ArgumentException(UnsupportedCoordinateMessage, nameof(bounds));

        ValidateCoordinates(geometry, paramName: null);
        return ClipCore(geometry, bounds);
    }

    private static GeoGeometry? ClipCore(GeoGeometry geometry, GeoBounds bounds)
    {
        if (geometry.GetBounds() is not { } geometryBounds)
            return null;

        if (Contains(bounds, geometryBounds))
            return geometry;

        if (!Intersects(bounds, geometryBounds))
            return null;

        switch (geometry)
        {
            case GeoPoint point:
                return bounds.Contains(point.Position) ? point : null;

            case GeoMultiPoint multiPoint:
                var positions = multiPoint.Positions.Where(bounds.Contains).ToArray();
                return positions.Length == 0 ? null : new GeoMultiPoint(positions);

            case GeoLineString lineString:
                return ToLineGeometry(ClipLine(lineString.Positions, bounds), asMultiLineString: false);

            case GeoMultiLineString multiLineString:
                return ToLineGeometry(multiLineString.LineStrings.SelectMany(lineString => ClipLine(lineString.Positions, bounds)).ToList(), asMultiLineString: true);

            case GeoPolygon or GeoMultiPolygon:
                var rectangle = new[] { new GeoPosition(bounds.West, bounds.South), new GeoPosition(bounds.East, bounds.South), new GeoPosition(bounds.East, bounds.North), new GeoPosition(bounds.West, bounds.North) };
                var contours = PolygonCombiner.Combine(GetOrientedRings(geometry), [rectangle], PolygonCombineMode.Intersect, PolygonFillRule.CounterClockwise, PolygonFillRule.CounterClockwise);
                return contours.Count == 0 ? null : ToGeometry(contours, asMultiPolygon: geometry is GeoMultiPolygon);

            case GeoGeometryCollection collection:
                var clipped = collection.Geometries.Select(part => ClipCore(part, bounds)).OfType<GeoGeometry>().ToArray();
                return clipped.Length == 0 ? null : new GeoGeometryCollection(clipped);

            default:
                return geometry;
        }
    }

    public static GeoGeometry? Simplify(GeoGeometry geometry, double tolerance)
    {
        if (!(tolerance >= 0) || double.IsInfinity(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be a finite number that is not negative.");

        if (tolerance == 0)
            return geometry;

        ValidateCoordinates(geometry, paramName: null);
        return SimplifyCore(geometry, tolerance);
    }

    public static GeoGeometry? Combine(GeoGeometry first, GeoGeometry second, PolygonCombineMode mode, string paramName)
    {
        if (!IsPolygonal(first))
            throw new InvalidOperationException("The boolean operations only apply to polygons, multi-polygons and collections of polygons.");

        if (!IsPolygonal(second))
            throw new ArgumentException("The boolean operations only apply to polygons, multi-polygons and collections of polygons.", paramName);

        ValidateCoordinates(first, paramName: null);
        ValidateCoordinates(second, paramName);
        var contours = PolygonCombiner.Combine(GetOrientedRings(first), GetOrientedRings(second), mode, PolygonFillRule.CounterClockwise, PolygonFillRule.CounterClockwise);
        return contours.Count == 0 ? null : ToGeometry(contours, asMultiPolygon: false);
    }

    public static GeoGeometry Transform(GeoGeometry geometry, Func<GeoPosition, GeoPosition> transform) => geometry switch
    {
        GeoPoint point => new GeoPoint(transform(point.Position)),
        GeoMultiPoint multiPoint => new GeoMultiPoint(multiPoint.Positions.Select(transform)),
        GeoLineString lineString => new GeoLineString(lineString.Positions.Select(transform)),
        GeoMultiLineString multiLineString => new GeoMultiLineString(multiLineString.LineStrings.Select(lineString => new GeoLineString(lineString.Positions.Select(transform)))),
        GeoPolygon polygon => TransformPolygon(polygon, transform),
        GeoMultiPolygon multiPolygon => new GeoMultiPolygon(multiPolygon.Polygons.Select(polygon => TransformPolygon(polygon, transform))),
        GeoGeometryCollection collection => new GeoGeometryCollection(collection.Geometries.Select(part => Transform(part, transform))),
        _ => geometry,
    };

    public static IEnumerable<GeoLineString> GetBoundaryLines(GeoGeometry geometry)
    {
        foreach (var simple in geometry.GetSimpleGeometries())
        {
            if (simple is not GeoPolygon polygon)
                continue;

            foreach (var ring in polygon.Rings)
            {
                if (ring.Count == 0)
                    continue;

                yield return new GeoLineString(IsClosed(ring) ? ring : [.. ring, ring[0]]);
            }
        }
    }

    private const string UnsupportedCoordinateMessage = "The coordinates must be numbers between -100,000 and 100,000: the operations work on longitudes and latitudes, in degrees.";

    private static bool IsSupported(double value) => Math.Abs(value) <= PolygonCombiner.MaxCoordinate;

    /// <summary>Checks that the polygons of a geometry fit on the grid of the combiner; the other geometries are not moved to it.</summary>
    internal static void ValidateCoordinates(GeoGeometry geometry, string? paramName)
    {
        foreach (var simple in geometry.GetSimpleGeometries())
        {
            if (simple is GeoPolygon polygon && !polygon.GetPositions().All(position => IsSupported(position.Longitude) && IsSupported(position.Latitude)))
            {
                if (paramName is null)
                    throw new InvalidOperationException(UnsupportedCoordinateMessage);

                throw new ArgumentException(UnsupportedCoordinateMessage, paramName);
            }
        }
    }

    private static GeoPolygon TransformPolygon(GeoPolygon polygon, Func<GeoPosition, GeoPosition> transform) => new(polygon.Rings.Select(ring => ring.Select(transform)));

    private static GeoGeometry? SimplifyCore(GeoGeometry geometry, double tolerance)
    {
        switch (geometry)
        {
            case GeoLineString lineString:
                var line = SimplifyLine(lineString.Positions, tolerance);
                return line.Count < 2 ? null : new GeoLineString(line);

            case GeoMultiLineString multiLineString:
                var lines = multiLineString.LineStrings
                    .Select(part => SimplifyLine(part.Positions, tolerance))
                    .Where(part => part.Count >= 2)
                    .Select(part => new GeoLineString(part))
                    .ToArray();
                return lines.Length == 0 ? null : new GeoMultiLineString(lines);

            case GeoPolygon polygon:
                return SimplifyPolygon(polygon, tolerance) is { } simplified ? NullWhenEmpty(MakeValid(simplified)) : null;

            case GeoMultiPolygon multiPolygon:
                var polygons = multiPolygon.Polygons.Select(part => SimplifyPolygon(part, tolerance)).OfType<GeoPolygon>().ToArray();
                return polygons.Length == 0 ? null : NullWhenEmpty(MakeValid(new GeoMultiPolygon(polygons)));

            case GeoGeometryCollection collection:
                var parts = collection.Geometries.Select(part => SimplifyCore(part, tolerance)).OfType<GeoGeometry>().ToArray();
                return parts.Length == 0 ? null : new GeoGeometryCollection(parts);

            default:
                return geometry;
        }

        static GeoGeometry? NullWhenEmpty(GeoGeometry geometry) => geometry.IsEmpty ? null : geometry;
    }

    /// <summary>Simplifies the rings of a polygon, without repairing it: the rings that collapse are dropped, and the polygon when its exterior ring does.</summary>
    private static GeoPolygon? SimplifyPolygon(GeoPolygon polygon, double tolerance)
    {
        var rings = new List<GeoPosition[]>(polygon.Rings.Count);
        for (var i = 0; i < polygon.Rings.Count; i++)
        {
            var ring = SimplifyRing(polygon.Rings[i], tolerance);
            if (ring is null)
            {
                if (i == 0)
                    return null;

                continue;
            }

            rings.Add(ring);
        }

        return new GeoPolygon(rings);
    }

    /// <summary>Simplifies a ring, cut at its first position and at the position that is the furthest from it, so that its two halves are simplified as lines.</summary>
    private static GeoPosition[]? SimplifyRing(IReadOnlyList<GeoPosition> ring, double tolerance)
    {
        var count = IsClosed(ring) ? ring.Count - 1 : ring.Count;
        if (count < 3)
            return null;

        var furthest = 0;
        var distance = -1.0;
        for (var i = 1; i < count; i++)
        {
            var d = SquaredDistance(ring[0], ring[i]);
            if (d > distance)
            {
                distance = d;
                furthest = i;
            }
        }

        var first = SimplifyLine([.. ring.Take(furthest + 1)], tolerance);
        var second = SimplifyLine([.. ring.Skip(furthest).Take(count - furthest), ring[0]], tolerance);

        // Three distinct positions, closed by the first one
        var result = new List<GeoPosition>(first.Count + second.Count);
        result.AddRange(first);
        result.AddRange(second.Skip(1));
        return result.Count < 4 ? null : [.. result];
    }

    /// <summary>The Douglas–Peucker algorithm: keeps the ends, and the positions that are further than the tolerance from the segment between the positions that are kept around them.</summary>
    private static List<GeoPosition> SimplifyLine(IReadOnlyList<GeoPosition> positions, double tolerance)
    {
        var deduplicated = RemoveDuplicates(positions);
        if (deduplicated.Length <= 2)
            return [.. deduplicated];

        var keep = new bool[deduplicated.Length];
        keep[0] = true;
        keep[^1] = true;
        var squaredTolerance = tolerance * tolerance;
        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, deduplicated.Length - 1));
        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();
            var index = -1;
            var max = squaredTolerance;
            for (var i = start + 1; i < end; i++)
            {
                var d = SquaredDistanceToSegment(deduplicated[i], deduplicated[start], deduplicated[end]);
                if (d > max)
                {
                    max = d;
                    index = i;
                }
            }

            if (index < 0)
                continue;

            keep[index] = true;
            stack.Push((start, index));
            stack.Push((index, end));
        }

        var result = new List<GeoPosition>();
        for (var i = 0; i < deduplicated.Length; i++)
        {
            if (keep[i])
            {
                result.Add(deduplicated[i]);
            }
        }

        return result;
    }

    /// <summary>Cuts a line to a box with the Liang–Barsky algorithm: the pieces that are inside the box, in order.</summary>
    private static List<GeoPosition[]> ClipLine(IReadOnlyList<GeoPosition> positions, GeoBounds bounds)
    {
        var pieces = new List<GeoPosition[]>();
        var current = new List<GeoPosition>();
        if (positions.Count == 1)
        {
            if (bounds.Contains(positions[0]))
            {
                pieces.Add([positions[0]]);
            }

            return pieces;
        }

        for (var i = 0; i + 1 < positions.Count; i++)
        {
            var a = positions[i];
            var b = positions[i + 1];
            if (!TryClipSegment(a, b, bounds, out var t0, out var t1))
            {
                Flush();
                continue;
            }

            var start = t0 == 0 ? a : Interpolate(a, b, t0);
            var end = t1 == 1 ? b : Interpolate(a, b, t1);

            // A segment that only touches the box, or a repeated position
            if (IsSameLocation(start, end))
            {
                if (t1 < 1)
                {
                    Flush();
                }

                continue;
            }

            if (current.Count == 0 || !IsSameLocation(current[^1], start))
            {
                Flush();
                current.Add(start);
            }

            current.Add(end);
            if (t1 < 1)
            {
                Flush();
            }
        }

        Flush();
        return pieces;

        void Flush()
        {
            if (current.Count >= 2)
            {
                pieces.Add([.. current]);
            }

            current.Clear();
        }
    }

    private static bool TryClipSegment(GeoPosition a, GeoPosition b, GeoBounds bounds, out double t0, out double t1)
    {
        t0 = 0;
        t1 = 1;
        var dx = b.Longitude - a.Longitude;
        var dy = b.Latitude - a.Latitude;
        return Clip(-dx, a.Longitude - bounds.West, ref t0, ref t1)
            && Clip(dx, bounds.East - a.Longitude, ref t0, ref t1)
            && Clip(-dy, a.Latitude - bounds.South, ref t0, ref t1)
            && Clip(dy, bounds.North - a.Latitude, ref t0, ref t1);

        static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0)
                return q >= 0;

            var t = q / p;
            if (p < 0)
            {
                if (t > t1)
                    return false;

                if (t > t0)
                {
                    t0 = t;
                }
            }
            else
            {
                if (t < t0)
                    return false;

                if (t < t1)
                {
                    t1 = t;
                }
            }

            return true;
        }
    }

    private static GeoPosition Interpolate(GeoPosition a, GeoPosition b, double t)
    {
        double? altitude = a.Altitude is { } altitudeA && b.Altitude is { } altitudeB ? altitudeA + ((altitudeB - altitudeA) * t) : null;
        double? measure = a.Measure is { } measureA && b.Measure is { } measureB ? measureA + ((measureB - measureA) * t) : null;
        DateTimeOffset? time = a.Time is { } timeA && b.Time is { } timeB ? timeA + ((timeB - timeA) * t) : null;
        return new GeoPosition(a.Longitude + ((b.Longitude - a.Longitude) * t), a.Latitude + ((b.Latitude - a.Latitude) * t), altitude) { Measure = measure, Time = time };
    }

    private static GeoGeometry? ToLineGeometry(List<GeoPosition[]> pieces, bool asMultiLineString)
    {
        if (pieces.Count == 0)
            return null;

        if (pieces.Count == 1 && !asMultiLineString)
            return new GeoLineString(pieces[0]);

        return new GeoMultiLineString(pieces.Select(piece => new GeoLineString(piece)));
    }

    /// <summary>Repairs a polygon: the area its rings fill with the even-odd rule.</summary>
    private static List<(long X, long Y)[]> RepairPolygon(GeoPolygon polygon) => PolygonCombiner.Combine(polygon.Rings, second: null, PolygonCombineMode.Union, PolygonFillRule.EvenOdd, PolygonFillRule.EvenOdd);

    /// <summary>Repairs a multi-polygon: the union of the areas its polygons fill, each one with the even-odd rule.</summary>
    private static List<(long X, long Y)[]> RepairMultiPolygon(IReadOnlyList<GeoPolygon> polygons)
    {
        var parts = polygons.Select(RepairPolygon).Where(contours => contours.Count != 0).ToList();
        if (parts.Count <= 1)
            return parts.Count == 0 ? [] : parts[0];

        // The contours of the repaired polygons are oriented: the counter-clockwise rule unites them
        var rings = parts.SelectMany(contours => contours).Select(contour => (IReadOnlyList<GeoPosition>)Array.ConvertAll(contour, PolygonCombiner.ToPosition));
        return PolygonCombiner.Combine(rings, second: null, PolygonCombineMode.Union, PolygonFillRule.CounterClockwise, PolygonFillRule.CounterClockwise);
    }

    /// <summary>Gets the rings of the polygons of a geometry, the exterior rings counter-clockwise and the holes clockwise.</summary>
    internal static IEnumerable<IReadOnlyList<GeoPosition>> GetOrientedRings(GeoGeometry geometry)
    {
        foreach (var simple in geometry.GetSimpleGeometries())
        {
            if (simple is not GeoPolygon polygon)
                continue;

            for (var i = 0; i < polygon.Rings.Count; i++)
            {
                var ring = polygon.Rings[i];
                var isCounterClockwise = GetDoubledArea(ring) > 0;
                yield return isCounterClockwise == (i == 0) ? ring : [.. ring.Reverse()];
            }
        }
    }

    private static bool IsPolygonal(GeoGeometry geometry) => geometry.GetSimpleGeometries().All(simple => simple is GeoPolygon);

    /// <summary>Builds polygons from contours: each counter-clockwise contour is an exterior ring, and each clockwise one a hole of the smallest exterior ring around it.</summary>
    internal static GeoGeometry ToGeometry(List<(long X, long Y)[]> contours, bool asMultiPolygon)
    {
        var shells = new List<(long X, long Y)[]>();
        var holes = new List<(long X, long Y)[]>();
        foreach (var contour in contours)
        {
            if (contour.Length < 3)
                continue;

            if (GetDoubledArea(contour) > 0)
            {
                shells.Add(contour);
            }
            else
            {
                holes.Add(contour);
            }
        }

        var shellAreas = shells.ConvertAll(shell => GetDoubledArea(shell));
        var shellBounds = shells.ConvertAll(GetBounds);
        var shellHoles = shells.ConvertAll(_ => new List<(long X, long Y)[]>());
        foreach (var hole in holes)
        {
            // The middle of an edge of the hole is on no other contour: the contours only meet at vertices
            var x = hole[0].X + hole[1].X;
            var y = hole[0].Y + hole[1].Y;
            var best = -1;
            for (var i = 0; i < shells.Count; i++)
            {
                var (minX, minY, maxX, maxY) = shellBounds[i];
                if (2 * minX > x || 2 * maxX < x || 2 * minY > y || 2 * maxY < y)
                    continue;

                if ((best < 0 || shellAreas[i] < shellAreas[best]) && ContainsDoubled(shells[i], x, y))
                {
                    best = i;
                }
            }

            if (best >= 0)
            {
                shellHoles[best].Add(hole);
            }
        }

        var polygons = new GeoPolygon[shells.Count];
        for (var i = 0; i < shells.Count; i++)
        {
            polygons[i] = new GeoPolygon([ToRing(shells[i]), .. shellHoles[i].Select(ToRing)]);
        }

        if (polygons.Length == 1 && !asMultiPolygon)
            return polygons[0];

        if (polygons.Length == 0 && !asMultiPolygon)
            return new GeoPolygon([]);

        return new GeoMultiPolygon(polygons);

        static GeoPosition[] ToRing((long X, long Y)[] contour)
        {
            var ring = new GeoPosition[contour.Length + 1];
            for (var i = 0; i < contour.Length; i++)
            {
                ring[i] = PolygonCombiner.ToPosition(contour[i]);
            }

            ring[^1] = ring[0];
            return ring;
        }
    }

    /// <summary>Gets a value indicating whether a point, in halves of a unit of the grid, is inside a contour: the even-odd rule, exactly.</summary>
    private static bool ContainsDoubled((long X, long Y)[] contour, long x, long y)
    {
        var inside = false;
        for (int i = 0, j = contour.Length - 1; i < contour.Length; j = i++)
        {
            var ax = 2 * contour[i].X;
            var ay = 2 * contour[i].Y;
            var bx = 2 * contour[j].X;
            var by = 2 * contour[j].Y;
            if ((ay > y) == (by > y))
                continue;

            // x < ax + (y - ay) * (bx - ax) / (by - ay), multiplied by by - ay
            var left = (Int128)(x - ax) * (by - ay);
            var right = (Int128)(y - ay) * (bx - ax);
            if (by > ay ? left < right : left > right)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static (long MinX, long MinY, long MaxX, long MaxY) GetBounds((long X, long Y)[] contour)
    {
        var (minX, minY, maxX, maxY) = (long.MaxValue, long.MaxValue, long.MinValue, long.MinValue);
        foreach (var (x, y) in contour)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return (minX, minY, maxX, maxY);
    }

    private static Int128 GetDoubledArea((long X, long Y)[] contour)
    {
        Int128 area = 0;
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            area += ((Int128)a.X * b.Y) - ((Int128)b.X * a.Y);
        }

        return area;
    }

    private static double GetDoubledArea(IReadOnlyList<GeoPosition> ring)
    {
        var area = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            area += (a.Longitude * b.Latitude) - (b.Longitude * a.Latitude);
        }

        return area;
    }

    private static GeoPosition[] RemoveDuplicates(IReadOnlyList<GeoPosition> positions)
    {
        var result = new List<GeoPosition>(positions.Count);
        foreach (var position in positions)
        {
            if (result.Count == 0 || !IsSameLocation(result[^1], position))
            {
                result.Add(position);
            }
        }

        return [.. result];
    }

    // The measures and the times are not compared: a GPS that does not move records the same location at different times
    private static bool IsSameLocation(GeoPosition a, GeoPosition b) => a.Longitude.Equals(b.Longitude) && a.Latitude.Equals(b.Latitude) && a.Altitude.Equals(b.Altitude);

    private static bool IsClosed(IReadOnlyList<GeoPosition> ring) => ring.Count > 1 && ring[0].Longitude == ring[^1].Longitude && ring[0].Latitude == ring[^1].Latitude;

    private static bool Contains(GeoBounds outer, GeoBounds inner) => inner.West >= outer.West && inner.East <= outer.East && inner.South >= outer.South && inner.North <= outer.North;

    private static bool Intersects(GeoBounds a, GeoBounds b) => b.West <= a.East && b.East >= a.West && b.South <= a.North && b.North >= a.South;

    private static double SquaredDistance(GeoPosition a, GeoPosition b)
    {
        var dx = b.Longitude - a.Longitude;
        var dy = b.Latitude - a.Latitude;
        return (dx * dx) + (dy * dy);
    }

    private static double SquaredDistanceToSegment(GeoPosition point, GeoPosition a, GeoPosition b)
    {
        var dx = b.Longitude - a.Longitude;
        var dy = b.Latitude - a.Latitude;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared == 0)
            return SquaredDistance(point, a);

        var t = Math.Clamp((((point.Longitude - a.Longitude) * dx) + ((point.Latitude - a.Latitude) * dy)) / lengthSquared, 0, 1);
        var x = a.Longitude + (t * dx) - point.Longitude;
        var y = a.Latitude + (t * dy) - point.Latitude;
        return (x * x) + (y * y);
    }
}
