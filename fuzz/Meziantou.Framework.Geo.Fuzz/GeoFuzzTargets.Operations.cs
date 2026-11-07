using System.Globalization;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Geo.Fuzz;

public static partial class GeoFuzzTargets
{
    private const byte RingSeparator = 0xFF;
    private const byte GeometrySeparator = 0xFE;

    // The coordinates are signed bytes times a scale: whole degrees give collinear and overlapping edges, the grid of the
    // polygon combiner (1e-7) gives positions that snap together, and the large scale reaches the limit of the coordinates
    // Shapes a few units of the grid wide are cut at their own vertices again and again: their vertices drift by several
    // units, so the positions of the results are only checked for larger shapes
    private const double MinPositionCheckScale = 0.001;

    private static readonly double[] OperationScales = [1, 0.25, 1e-7, 3e-7, 0.001, 1.37, 700];

    private static readonly GeoProjection[] Projections =
    [
        new AzimuthalEquidistantProjection(),
        new AzimuthalEquidistantProjection { SouthPole = true },
        new AzimuthalEquidistantProjection { CentralMeridian = 135 },
        new AzimuthalEquidistantProjection { SouthPole = true, CentralMeridian = -60.5 },
        new EquirectangularProjection(),
        new MercatorProjection(),
        new EqualEarthProjection(),
        new AlbersProjection(),
    ];

    /// <summary>
    /// <c>scale, tolerance, west, south, east, north, positions</c>: the positions are pairs of signed bytes, times the
    /// scale <c>OperationScales[scale]</c>, with <c>0xFF</c> between the rings and <c>0xFE</c> between the two polygons.
    /// The operations do not throw for coordinates in their range. Repairing a repaired geometry keeps its area, and
    /// the areas of the boolean operations of the repaired polygons add up: |A ∪ B| + |A ∩ B| = |A| + |B| and
    /// |A \ B| = |A| - |A ∩ B|. A clipped geometry is in its box (to about two units of the grid of the polygon combiner,
    /// for shapes larger than a few units), and a simplified line keeps a subset of the positions.
    /// </summary>
    private static void OperationsTarget(ReadOnlySpan<byte> data)
    {
        var scale = FuzzInput.Pick(OperationScales, FuzzInput.ReadByte(ref data));
        var tolerance = FuzzInput.ReadByte(ref data) * scale / 16;
        var box = ReadBox(ref data);
        box = new GeoBounds(box.West * scale, box.South * scale, box.East * scale, box.North * scale);
        var (firstRings, secondRings) = ReadPolygons(data, scale);
        if (firstRings.Count is 0)
            return;

        var first = new GeoPolygon(firstRings);
        var second = new GeoPolygon(secondRings);

        // The positions as a line, and the rings as a multi-line
        var line = new GeoLineString(firstRings.SelectMany(ring => ring));
        var lines = new GeoMultiLineString(firstRings.Select(ring => new GeoLineString(ring)));
        foreach (var geometry in new GeoGeometry[] { first, second, line, lines, new GeoMultiPoint(line.Positions), new GeoMultiPolygon([first, second]), new GeoGeometryCollection([first, line]) })
        {
            Exercise(geometry);
            FuzzAssert.Equal(Describe(geometry), Describe(geometry.Transform(position => position)), "The identity transform changes the geometry");
            AssertBoundaryLines(geometry);
            AssertClip(geometry, box, checkPositions: scale >= MinPositionCheckScale);
            AssertSimplify(geometry, tolerance, checkPositions: scale >= MinPositionCheckScale);
            var valid = geometry.MakeValid();
            Exercise(valid);

            // The combiner cuts the edges at the vertices within 1.5 units of the grid: a sliver a unit or two wide can
            // disappear when a repaired polygon is repaired again, which hardly changes its area
            var revalid = valid.MakeValid();
            var validMargin = 1e-6 * (1 + GetPerimeter(valid)) * Math.Max(1, scale);
            FuzzAssert.True(Math.Abs(GetArea(valid) - GetArea(revalid)) <= validMargin, $"Repairing a repaired geometry changes its area: {Describe(valid)} becomes {Describe(revalid)}");
        }

        AssertSimplifiedLine(line, tolerance);

        var validFirst = first.MakeValid();
        var validSecond = second.MakeValid();
        var union = validFirst.Union(validSecond);
        var intersection = validFirst.Intersection(validSecond);
        var difference = validFirst.Difference(validSecond);
        foreach (var result in new[] { union, intersection, difference })
        {
            if (result is not null)
            {
                Exercise(result);
                FuzzAssert.True(result is GeoPolygon or GeoMultiPolygon, "A boolean operation does not return polygons");
                FuzzAssert.False(result.IsEmpty, "A boolean operation returns an empty geometry instead of null");
            }
        }

        var areaFirst = GetArea(validFirst);
        var areaSecond = GetArea(validSecond);
        var areaUnion = GetArea(union);
        var areaIntersection = GetArea(intersection);
        var areaDifference = GetArea(difference);
        var margin = 1e-6 * (1 + GetPerimeter(validFirst) + GetPerimeter(validSecond)) * Math.Max(1, scale);
        FuzzAssert.True(areaFirst >= -margin && areaSecond >= -margin, $"A repaired polygon has a negative area: {Format(areaFirst)}, {Format(areaSecond)}");
        FuzzAssert.True(Math.Abs(areaUnion + areaIntersection - areaFirst - areaSecond) <= margin, $"|A ∪ B| + |A ∩ B| ≠ |A| + |B|: {Format(areaUnion)} + {Format(areaIntersection)} ≠ {Format(areaFirst)} + {Format(areaSecond)}");
        FuzzAssert.True(Math.Abs(areaDifference - (areaFirst - areaIntersection)) <= margin, $"|A \\ B| ≠ |A| - |A ∩ B|: {Format(areaDifference)} ≠ {Format(areaFirst)} - {Format(areaIntersection)}");
        FuzzAssert.True(areaIntersection <= Math.Min(areaFirst, areaSecond) + margin, "The intersection is larger than a polygon");
    }

    /// <summary>
    /// <c>projection, longitude (8 bytes), latitude (8 bytes)</c>: the doubles are little-endian. Projecting a position and
    /// inverting the result gives the position back, and inverting any point never throws.
    /// </summary>
    private static void ProjectionTarget(ReadOnlySpan<byte> data)
    {
        var projection = FuzzInput.Pick(Projections, FuzzInput.ReadByte(ref data));
        var x = ReadDouble(ref data);
        var y = ReadDouble(ref data);

        if (projection.Invert(x, y) is { } inverted)
        {
            FuzzAssert.True(double.IsFinite(inverted.Longitude) && Math.Abs(inverted.Longitude) <= 180 && double.IsFinite(inverted.Latitude) && Math.Abs(inverted.Latitude) <= 90, "Invert returns a position outside of the globe");
        }

        // The same bytes, as a position on the globe
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return;

        var longitude = Math.IEEERemainder(x, 360);
        var latitude = Math.Clamp(Math.IEEERemainder(y, 180), -projection.MaxLatitude, projection.MaxLatitude);
        var (px, py) = projection.Project(longitude, latitude);
        FuzzAssert.True(double.IsFinite(px) && double.IsFinite(py), "A position of the globe projects to a point that is not finite");
        if (projection.Invert(px, py) is not { } back)
        {
            // Only the projections without an inverse may return null
            FuzzAssert.True(projection is AlbersProjection, $"The projected position ({Format(longitude)}, {Format(latitude)}) cannot be inverted");
            return;
        }

        // The formulas lose precision near the poles: 1e-5° is about a meter
        FuzzAssert.True(Math.Abs(back.Latitude - latitude) <= 1e-5, $"Inverting the projected position ({Format(longitude)}, {Format(latitude)}) gives the latitude {Format(back.Latitude)}");

        // At a pole, every longitude is the same position
        if (Math.Abs(latitude) < 90 - 1e-6 && !(projection is AzimuthalEquidistantProjection && Math.Abs(latitude) > 89.9))
        {
            var difference = Math.Abs(Math.IEEERemainder(back.Longitude - longitude, 360));
            FuzzAssert.True(difference <= 1e-5, $"Inverting the projected position ({Format(longitude)}, {Format(latitude)}) gives the longitude {Format(back.Longitude)}");
        }
    }

    private static double ReadDouble(ref ReadOnlySpan<byte> data)
    {
        Span<byte> bytes = stackalloc byte[8];
        for (var i = 0; i < 8; i++)
        {
            bytes[i] = FuzzInput.ReadByte(ref data);
        }

        return BitConverter.ToDouble(bytes);
    }

    private static (List<GeoPosition[]> First, List<GeoPosition[]> Second) ReadPolygons(ReadOnlySpan<byte> data, double scale)
    {
        var first = new List<GeoPosition[]>();
        var second = new List<GeoPosition[]>();
        var rings = first;
        var ring = new List<GeoPosition>();
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] is RingSeparator or GeometrySeparator)
            {
                if (ring.Count > 0)
                {
                    rings.Add([.. ring]);
                    ring.Clear();
                }

                if (data[i] is GeometrySeparator)
                {
                    rings = second;
                }

                continue;
            }

            if (i + 1 >= data.Length || data[i + 1] is RingSeparator or GeometrySeparator)
                continue;

            ring.Add(new GeoPosition((sbyte)data[i] * scale, (sbyte)data[i + 1] * scale));
            i++;
        }

        if (ring.Count > 0)
        {
            rings.Add([.. ring]);
        }

        return (first, second);
    }

    private static void AssertBoundaryLines(GeoGeometry geometry)
    {
        var rings = geometry.GetSimpleGeometries().OfType<GeoPolygon>().SelectMany(polygon => polygon.Rings).Count(ring => ring.Count > 0);
        var boundaries = geometry.GetBoundaryLines().ToList();
        FuzzAssert.Equal(rings, boundaries.Count, "GetBoundaryLines does not return a line per ring");
        FuzzAssert.True(boundaries.All(boundary => boundary.Positions[0] == boundary.Positions[^1]), "A boundary line is not closed");
    }

    private static void AssertClip(GeoGeometry geometry, GeoBounds box, bool checkPositions)
    {
        var clipped = geometry.Clip(box);
        if (clipped is null)
            return;

        Exercise(clipped);
        FuzzAssert.False(clipped.IsEmpty, "Clip returns an empty geometry instead of null");
        if (!checkPositions)
            return;

        // The polygons are snapped to the grid of 1e-7°: the combiner cuts a segment at the points within 1.5 units of
        // it, so a vertex can move by about two units. The lines are cut by interpolation.
        const double Margin = 2.5e-7;
        foreach (var position in clipped.GetPositions())
        {
            var inside = position.Longitude >= box.West - Margin && position.Longitude <= box.East + Margin && position.Latitude >= box.South - Margin && position.Latitude <= box.North + Margin;
            FuzzAssert.True(inside, $"Clip returns the position {Describe(position)} outside of the box");
        }
    }

    private static void AssertSimplify(GeoGeometry geometry, double tolerance, bool checkPositions)
    {
        var simplified = geometry.Simplify(tolerance);
        if (simplified is null)
            return;

        Exercise(simplified);
        if (simplified is GeoPoint or GeoMultiPoint)
        {
            FuzzAssert.Equal(Describe(geometry), Describe(simplified), "Simplify changes the points");
        }

        if (checkPositions && geometry.GetBounds() is { } bounds && simplified.GetBounds() is { } simplifiedBounds)
        {
            const double Margin = 2.5e-7;
            FuzzAssert.True(simplifiedBounds.West >= bounds.West - Margin && simplifiedBounds.East <= bounds.East + Margin && simplifiedBounds.South >= bounds.South - Margin && simplifiedBounds.North <= bounds.North + Margin, "The simplified geometry is outside of the bounds of the geometry");
        }
    }

    /// <summary>The Douglas–Peucker algorithm keeps the ends of the line and a subset of its positions, in order.</summary>
    private static void AssertSimplifiedLine(GeoLineString line, double tolerance)
    {
        if (line.Simplify(tolerance) is not GeoLineString simplified)
            return;

        var positions = line.Positions;
        FuzzAssert.Equal(Describe(positions[0]), Describe(simplified.Positions[0]), "The simplified line does not start at the start of the line");
        FuzzAssert.Equal(Describe(positions[^1]), Describe(simplified.Positions[^1]), "The simplified line does not end at the end of the line");
        var index = 0;
        foreach (var position in simplified.Positions)
        {
            while (index < positions.Count && positions[index] != position)
            {
                index++;
            }

            FuzzAssert.True(index < positions.Count, $"The simplified line has the position {Describe(position)} that is not a position of the line, in order");
        }
    }

    /// <summary>The area of polygons on a plane, in square degrees: the exterior rings add theirs, the holes remove theirs.</summary>
    private static double GetArea(GeoGeometry? geometry)
    {
        if (geometry is null)
            return 0;

        var area = 0.0;
        foreach (var polygon in geometry.GetSimpleGeometries().OfType<GeoPolygon>())
        {
            for (var i = 0; i < polygon.Rings.Count; i++)
            {
                var ring = Math.Abs(GetSignedArea(polygon.Rings[i]));
                area += i is 0 ? ring : -ring;
            }
        }

        return area;
    }

    private static double GetSignedArea(IReadOnlyList<GeoPosition> ring)
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

    private static double GetPerimeter(GeoGeometry geometry)
    {
        var perimeter = 0.0;
        foreach (var polygon in geometry.GetSimpleGeometries().OfType<GeoPolygon>())
        {
            foreach (var ring in polygon.Rings)
            {
                for (var i = 0; i < ring.Count; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Count];
                    perimeter += Math.Abs(a.Longitude - b.Longitude) + Math.Abs(a.Latitude - b.Latitude);
                }
            }
        }

        return perimeter;
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static IEnumerable<FuzzSeed> GetOperationsSeeds()
    {
        // Two overlapping squares, a square with a hole, a bow-tie, a hole outside its exterior, collinear edges, spikes
        yield return new FuzzSeed("overlapping-squares", (byte[])[0, 8, 0, 0, 0, 0, 10, 0, 10, 10, 0, 10, GeometrySeparator, 5, 5, 15, 5, 15, 15, 5, 15]);
        yield return new FuzzSeed("hole", (byte[])[0, 4, 0xF6, 0xF6, 5, 5, 0, 0, 20, 0, 20, 20, 0, 20, 0, 0, RingSeparator, 5, 5, 5, 10, 10, 10, 10, 5, 5, 5, GeometrySeparator, 10, 0, 30, 0, 30, 30, 10, 30]);
        yield return new FuzzSeed("bow-tie", (byte[])[1, 16, 0, 0, 8, 8, 0, 0, 10, 10, 10, 0, 0, 10, GeometrySeparator, 0, 0, 10, 0, 0, 10]);
        yield return new FuzzSeed("hole-outside", (byte[])[0, 1, 0, 0, 50, 50, 0, 0, 4, 0, 4, 4, 0, 4, RingSeparator, 10, 10, 12, 10, 12, 12, 10, 12, GeometrySeparator, 2, 2, 11, 2, 11, 11, 2, 11]);
        yield return new FuzzSeed("collinear", (byte[])[0, 2, 0, 0, 3, 3, 0, 0, 1, 0, 2, 0, 3, 0, 3, 3, 2, 3, 0, 3, 0, 0, GeometrySeparator, 1, 0, 2, 0, 2, 3, 1, 3]);
        yield return new FuzzSeed("grid", (byte[])[2, 3, 0, 0, 2, 2, 0, 0, 3, 0, 3, 3, 0, 3, GeometrySeparator, 1, 1, 4, 1, 4, 4, 1, 4]);
        yield return new FuzzSeed("spike", (byte[])[4, 40, 0x9C, 0x9C, 100, 100, 0, 0, 100, 0, 50, 1, 50, 100, 49, 1, 0, 0, GeometrySeparator, 0x9C, 0x9C, 0x7F, 0x9C, 0x7F, 0x7F]);
        yield return new FuzzSeed("large", (byte[])[6, 1, 0x81, 0x81, 0x7F, 0x7F, 0x81, 0x81, 0x7F, 0x81, 0x7F, 0x7F, 0x81, 0x7F, GeometrySeparator, 0, 0, 0x7F, 0, 0, 0x7F]);
    }

    private static IEnumerable<FuzzSeed> GetProjectionSeeds()
    {
        (double Longitude, double Latitude)[] positions = [(0, 0), (2.35, 48.86), (-180, 90), (179.999, -89.999), (135, 0), (-60.5, -90), (45, 85.05112878), (-96, 29.5)];
        for (var projection = 0; projection < Projections.Length; projection++)
        {
            for (var i = 0; i < positions.Length; i++)
            {
                yield return new FuzzSeed(
                    string.Create(CultureInfo.InvariantCulture, $"projection-{projection}-{i}"),
                    (byte[])[(byte)projection, .. BitConverter.GetBytes(positions[i].Longitude), .. BitConverter.GetBytes(positions[i].Latitude)]);
            }
        }
    }
}
