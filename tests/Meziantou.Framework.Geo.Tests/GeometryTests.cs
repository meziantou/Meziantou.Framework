namespace Meziantou.Framework.Geo.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void LabelPosition()
    {
        // The centroid of the largest polygon; a hole does not move it
        var square = new GeoPolygon([[new(0, 0), new(2, 0), new(2, 2), new(0, 2), new(0, 0)], [new(0.1, 0.1), new(0.2, 0.1), new(0.2, 0.2)]]);
        var island = new GeoPolygon([[new(10, 10), new(10.5, 10), new(10.5, 10.5)]]);
        Assert.Equal(new GeoPosition(1, 1), new GeoMultiPolygon([island, square]).LabelPosition);

        // A triangle: the mean of its corners
        var (longitude, latitude, _) = new GeoPolygon([[new(0, 0), new(3, 0), new(0, 3)]]).LabelPosition!.Value;
        Assert.Equal(1, longitude, 1e-12);
        Assert.Equal(1, latitude, 1e-12);

        // A flat ring, lines, points, collections, and nothing
        Assert.Equal(new GeoPosition(1, 0), new GeoPolygon([[new(0, 0), new(2, 0)]]).LabelPosition);
        Assert.Equal(new GeoPosition(1, 2), new GeoGeometryCollection([new GeoPolygon([]), new GeoLineString([new(0, 0), new(2, 4)]), new GeoPoint(new(50, 50))]).LabelPosition);
        Assert.Equal(new GeoPosition(2, 3), new GeoMultiPoint([new(1, 2), new(3, 4)]).LabelPosition);
        Assert.Equal(new GeoPosition(1, 1), new GeoGeometryCollection([new GeoPoint(new(9, 9)), square]).LabelPosition);
        Assert.Null(new GeoMultiPoint([]).LabelPosition);
        Assert.Null(new GeoGeometryCollection([]).LabelPosition);
    }

    [Fact]
    public void BoundsAndPositions()
    {
        var geometry = new GeoGeometryCollection([new GeoPoint(new(5, -2)), new GeoMultiLineString([new GeoLineString([new(-1, 3), new(2, 8)])])]);
        Assert.Equal(new GeoBounds(-1, -2, 5, 8), geometry.GetBounds());
        Assert.Equal([new GeoPosition(5, -2), new(-1, 3), new(2, 8)], geometry.GetPositions());
        Assert.False(geometry.IsEmpty);
        Assert.True(new GeoMultiPolygon([]).IsEmpty);
        Assert.Null(new GeoMultiPolygon([]).GetBounds());
        Assert.Equal([GeoGeometryType.Point, GeoGeometryType.LineString], geometry.GetSimpleGeometries().Select(simple => simple.Type));
        Assert.HasCount(2, new GeoMultiPoint([new(0, 0), new(1, 1)]).GetSimpleGeometries());

        var bounds = new GeoBounds(0, 0, 10, 10);
        Assert.True(bounds.Contains(new GeoPosition(10, 0)));
        Assert.False(bounds.Contains(new GeoPosition(10.1, 0)));
        Assert.Equal(new GeoBounds(-5, 0, 10, 20), bounds.Union(new GeoBounds(-5, 5, 0, 20)));

        GeoFeatureCollection features = [new GeoFeature(new GeoPoint(new(1, 2))), new GeoFeature(geometry: null), new GeoFeature(new GeoPoint(new(-3, 4)))];
        Assert.Equal(new GeoBounds(-3, 2, 1, 4), features.GetBounds());
        Assert.Null(new GeoFeatureCollection().GetBounds());
    }

    [Fact]
    public void Contains()
    {
        #region Geo_Contains
        // A square with a square hole
        var polygon = new GeoPolygon(
        [
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)],
            [new(4, 4), new(6, 4), new(6, 6), new(4, 6), new(4, 4)],
        ]);

        Assert.True(polygon.Contains(new GeoPosition(Longitude: 2, Latitude: 2)));
        Assert.False(polygon.Contains(new GeoPosition(Longitude: 5, Latitude: 5))); // in the hole
        Assert.False(polygon.Contains(new GeoPosition(Longitude: 12, Latitude: 5)));
        #endregion

        var multiPolygon = new GeoMultiPolygon([polygon, new GeoPolygon([[new(20, 20), new(30, 20), new(30, 30)]])]);
        Assert.True(multiPolygon.Contains(new GeoPosition(29, 21)));
        Assert.False(multiPolygon.Contains(new GeoPosition(5, 5)));
    }

    [Fact]
    public void Distance()
    {
        #region Geo_Distance
        var paris = new GeoPosition(Longitude: 2.35, Latitude: 48.86);
        var london = new GeoPosition(Longitude: -0.13, Latitude: 51.51);

        // The great-circle distance, in meters
        Assert.Equal(343.5, paris.GetDistance(london) / 1000, 1);
        Assert.Equal(343.5, new GeoLineString([paris, london]).GetLength() / 1000, 1);
        #endregion

        Assert.Equal(0, paris.GetDistance(paris));
        Assert.Equal(Math.PI * GeoPosition.EarthRadius, new GeoPosition(0, 90).GetDistance(new GeoPosition(0, -90)), 1e-6);
        Assert.Equal(Math.PI * GeoPosition.EarthRadius, new GeoPosition(0, 0).GetDistance(new GeoPosition(180, 0)), 1e-6);
    }

    [Fact]
    public void Position_MeasureAndTime()
    {
        #region Geo_PositionMeasureAndTime
        // The measure and the time are optional, set with an object initializer
        var position = new GeoPosition(Longitude: 2.35, Latitude: 48.86, Altitude: 35)
        {
            Measure = 1250,
            Time = new DateTimeOffset(2024, 9, 1, 7, 0, 0, TimeSpan.Zero),
        };

        // A with expression keeps the values it does not set
        var moved = position with { Longitude = 2.36 };
        Assert.Equal(1250, moved.Measure);
        Assert.Equal(position.Time, moved.Time);
        Assert.Null(new GeoPosition(2.35, 48.86).Time);
        #endregion

        Assert.Equal(35, position.Altitude);
        Assert.Equal(new GeoPosition(2.35, 48.86, 35), position with { Measure = null, Time = null });
        Assert.Null((position with { Altitude = null }).Altitude);
        Assert.Equal(1250, (position with { Altitude = null }).Measure);
        Assert.NotEqual(position, position with { Measure = 0 });
        Assert.NotEqual(new GeoPosition(1, 2), new GeoPosition(1, 2) { Measure = 0 });
        Assert.NotEqual(new GeoPosition(1, 2), new GeoPosition(1, 2) { Time = DateTimeOffset.MinValue });
        Assert.NotEqual(new GeoPosition(1, 2), new GeoPosition(1, 2, 0));

        // The times are compared as instants, as DateTimeOffset does, and keep their offset
        var local = position with { Time = new DateTimeOffset(2024, 9, 1, 9, 0, 0, TimeSpan.FromHours(2)) };
        Assert.Equal(position, local);
        Assert.Equal(position.GetHashCode(), local.GetHashCode());
        Assert.Equal(TimeSpan.FromHours(2), local.Time!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2024, 9, 1, 9, 0, 0, TimeSpan.FromHours(2)).Ticks, local.Time.Value.Ticks);

        var (longitude, latitude, altitude) = position;
        Assert.Equal((2.35, 48.86, 35.0), (longitude, latitude, altitude!.Value));
        Assert.Equal("GeoPosition { Longitude = 1, Latitude = 2, Altitude = , Measure = 3, Time =  }", (new GeoPosition(1, 2) { Measure = 3 }).ToString());

        // The optional values do not use Nullable<T> fields, so a position stays small
        Assert.True(System.Runtime.CompilerServices.Unsafe.SizeOf<GeoPosition>() <= 48);
    }

    [Fact]
    public void Operations_KeepMeasuresAndTimes()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var line = new GeoLineString(
        [
            new GeoPosition(0, 0, 0) { Measure = 0, Time = start },
            new GeoPosition(10, 0, 100) { Measure = 10, Time = start.AddMinutes(10) },
            new GeoPosition(10, 0.0001, 100) { Measure = 11, Time = start.AddMinutes(11) },
            new GeoPosition(20, 0) { Measure = 20, Time = start.AddMinutes(20) },
        ]);

        // The position where the line leaves the box gets interpolated values; the altitude needs both ends to have one
        var clipped = Assert.IsType<GeoLineString>(line.Clip(new GeoBounds(-1, -1, 5, 1)));
        Assert.Equal([line.Positions[0], new GeoPosition(5, 0, 50) { Measure = 5, Time = start.AddMinutes(5) }], clipped.Positions);
        var end = Assert.IsType<GeoLineString>(line.Clip(new GeoBounds(15, -1, 25, 1))).Positions[0];
        Assert.Equal(new GeoPosition(15, 0.00005) { Measure = 15.5, Time = start.AddMinutes(15.5) }, end);

        // The positions that are kept keep their values
        Assert.Equal([line.Positions[0], line.Positions[3]], Assert.IsType<GeoLineString>(line.Simplify(0.01)).Positions);
        Assert.Equal(line.Positions, line.Transform(position => position with { Longitude = position.Longitude }).GetPositions());

        // A GPS that does not move records the same location at different times: the first one is kept
        var stop = new GeoLineString([new GeoPosition(0, 0) { Time = start }, new GeoPosition(0, 0) { Time = start.AddMinutes(1) }, new GeoPosition(1, 1) { Time = start.AddMinutes(2) }]);
        Assert.Equal([stop.Positions[0], stop.Positions[2]], Assert.IsType<GeoLineString>(stop.MakeValid()).Positions);
    }

    [Fact]
    public void Constructors_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => new GeoLineString(null!));
        Assert.Throws<ArgumentNullException>(() => new GeoPolygon(null!));
        Assert.Throws<ArgumentNullException>(() => new GeoPolygon([null!]));
        Assert.Throws<ArgumentException>(() => new GeoMultiPolygon([null!]));
        Assert.Throws<ArgumentException>(() => new GeoGeometryCollection([null!]));
        Assert.Throws<ArgumentNullException>(() => new GeoFeatureCollection().Add(null!));
        Assert.Throws<ArgumentNullException>(() => new GeoFeatureCollection(null!));
    }

    [Fact]
    public void BuildFeatures()
    {
        #region Geo_BuildFeatures
        var cities = new GeoFeatureCollection
        {
            new GeoFeature(
                new GeoPoint(new GeoPosition(Longitude: 2.35, Latitude: 48.86)),
                new Dictionary<string, object?> { ["name"] = "Paris", ["population"] = 2_100_000 },
                id: "paris"),
            new GeoFeature(
                new GeoPoint(new GeoPosition(Longitude: -0.13, Latitude: 51.51)),
                new Dictionary<string, object?> { ["name"] = "London", ["population"] = 8_900_000 },
                id: "london"),
        };

        var json = GeoJson.Format(cities);
        Assert.StartsWith("""{"type":"FeatureCollection","features":[{"type":"Feature","id":"paris",""", json);
        #endregion
    }

    [Fact]
    public void MakeValid_BowTie()
    {
        #region GeoGeometry_MakeValid
        // A ring that crosses itself: a bow-tie
        var bowTie = new GeoPolygon([[new(0, 0), new(2, 2), new(2, 0), new(0, 2), new(0, 0)]]);

        // It becomes its two triangles, whose exterior rings turn counter-clockwise
        var repaired = Assert.IsType<GeoMultiPolygon>(bowTie.MakeValid());
        Assert.HasCount(2, repaired.Polygons);
        #endregion

        foreach (var polygon in repaired.Polygons)
        {
            Assert.Equal(1, GetSignedArea(polygon.Rings[0]), 1e-9);
        }
    }

    [Fact]
    public void MakeValid_OrientsRings()
    {
        // A clockwise exterior ring and a counter-clockwise hole: the same area, reoriented
        var polygon = new GeoPolygon(
        [
            [new(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0)],
            [new(4, 4), new(6, 4), new(6, 6), new(4, 6), new(4, 4)],
        ]);
        var repaired = Assert.IsType<GeoPolygon>(polygon.MakeValid());
        Assert.HasCount(2, repaired.Rings);
        Assert.Equal(100, GetSignedArea(repaired.Rings[0]), 1e-9);
        Assert.Equal(-4, GetSignedArea(repaired.Rings[1]), 1e-9);
        Assert.Equal(repaired.Rings[0][0], repaired.Rings[0][^1]);
        Assert.Equal(96, GetArea(repaired), 1e-9);
    }

    [Fact]
    public void MakeValid_HoleCrossingTheExteriorRing()
    {
        // The hole sticks out of the square: the even-odd rule keeps the part of the hole that is outside
        var polygon = new GeoPolygon(
        [
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)],
            [new(8, 4), new(12, 4), new(12, 6), new(8, 6), new(8, 4)],
        ]);
        var repaired = polygon.MakeValid();
        Assert.Equal(100, GetArea(repaired), 1e-9);
        Assert.DoesNotContain(((GeoMultiPolygon)repaired).Polygons, part => part.Contains(new GeoPosition(9, 5)));
        Assert.Contains(((GeoMultiPolygon)repaired).Polygons, part => part.Contains(new GeoPosition(11, 5)));
    }

    [Fact]
    public void MakeValid_MultiPolygonUnion()
    {
        // Two overlapping squares become one polygon; a figure-eight touches itself at a vertex and stays one area
        var multiPolygon = new GeoMultiPolygon(
        [
            new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)]]),
            new GeoPolygon([[new(2, 2), new(6, 2), new(6, 6), new(2, 6), new(2, 2)]]),
        ]);
        var repaired = Assert.IsType<GeoMultiPolygon>(multiPolygon.MakeValid());
        Assert.HasCount(1, repaired.Polygons);
        Assert.Equal(28, GetArea(repaired), 1e-9);

        var figureEight = new GeoPolygon([[new(0, 0), new(1, 1), new(2, 0), new(2, 2), new(1, 1), new(0, 2), new(0, 0)]]);
        Assert.Equal(2, GetArea(figureEight.MakeValid()), 1e-9);

        // Repeated positions are removed from lines; points are unchanged
        Assert.HasCount(2, Assert.IsType<GeoLineString>(new GeoLineString([new(0, 0), new(0, 0), new(1, 1)]).MakeValid()).Positions);
        var point = new GeoPoint(new(1, 2));
        Assert.Same(point, point.MakeValid());
        Assert.True(new GeoPolygon([[new(0, 0), new(1, 1), new(2, 2), new(0, 0)]]).MakeValid().IsEmpty);
        Assert.Throws<InvalidOperationException>(() => new GeoPolygon([[new(0, 0), new(1e6, 0), new(0, 1)]]).MakeValid());
    }

    [Fact]
    public void Clip()
    {
        #region GeoGeometry_Clip
        var square = new GeoPolygon([[new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)]]);
        var bounds = new GeoBounds(West: 5, South: -5, East: 15, North: 5);

        // The quarter of the square that is inside the box
        var clipped = Assert.IsType<GeoPolygon>(square.Clip(bounds));
        Assert.Equal(new GeoBounds(5, 0, 10, 5), clipped.GetBounds());

        // A line that leaves the box and comes back is cut in two
        var line = new GeoLineString([new(6, 1), new(20, 1), new(20, 2), new(6, 2)]);
        var pieces = Assert.IsType<GeoMultiLineString>(line.Clip(bounds));
        Assert.HasCount(2, pieces.LineStrings);
        #endregion

        Assert.Equal([new GeoPosition(6, 1), new(15, 1)], pieces.LineStrings[0].Positions);
        Assert.Equal([new GeoPosition(15, 2), new(6, 2)], pieces.LineStrings[1].Positions);
        Assert.Same(square, square.Clip(new GeoBounds(-1, -1, 11, 11)));
        Assert.Null(square.Clip(new GeoBounds(20, 20, 30, 30)));
        Assert.Null(new GeoPoint(new(0, 0)).Clip(new GeoBounds(1, 1, 2, 2)));
        Assert.HasCount(1, Assert.IsType<GeoMultiPoint>(new GeoMultiPoint([new(0, 0), new(6, 0)]).Clip(bounds)).Positions);
        Assert.Equal(25, GetArea(new GeoMultiPolygon([square]).Clip(bounds)!), 1e-9);
        Assert.IsType<GeoGeometryCollection>(new GeoGeometryCollection([square, new GeoPoint(new(50, 50))]).Clip(bounds));
        Assert.Throws<ArgumentException>("bounds", () => square.Clip(new GeoBounds(10, 0, 0, 10)));

        // A line that only touches the box at a corner, and one that goes along an edge
        Assert.Null(new GeoLineString([new(0, 10), new(5, 5), new(0, 0)]).Clip(bounds));
        Assert.Equal([new GeoPosition(5, -5), new(5, 5)], Assert.IsType<GeoLineString>(new GeoLineString([new(5, -10), new(5, 10)]).Clip(bounds)).Positions);
    }

    [Fact]
    public void Simplify()
    {
        #region GeoGeometry_Simplify
        // A line with a small zigzag, about 100 meters wide
        var line = new GeoLineString([new(0, 0), new(1, 0.0005), new(2, -0.0005), new(3, 0)]);

        // The positions closer than 0.001° to the simplified line are removed
        var simplified = Assert.IsType<GeoLineString>(line.Simplify(tolerance: 0.001));
        Assert.Equal([new GeoPosition(0, 0), new(3, 0)], simplified.Positions);
        #endregion

        Assert.Same(line, line.Simplify(0));
        Assert.HasCount(4, Assert.IsType<GeoLineString>(line.Simplify(0.0001)).Positions);
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => line.Simplify(-1));
        Assert.Throws<ArgumentOutOfRangeException>("tolerance", () => line.Simplify(double.NaN));

        // A polygon smaller than the tolerance collapses; a larger one keeps its corners
        var small = new GeoPolygon([[new(0, 0), new(0.0001, 0), new(0.0001, 0.0001), new(0, 0.0001), new(0, 0)]]);
        Assert.Null(small.Simplify(0.001));
        var square = new GeoPolygon([[new(0, 0), new(5, 0.0001), new(10, 0), new(10, 10), new(0, 10), new(0, 0)]]);
        var simplifiedSquare = Assert.IsType<GeoPolygon>(square.Simplify(0.01));
        Assert.HasCount(5, simplifiedSquare.Rings[0]);
        Assert.Equal(100, GetArea(simplifiedSquare), 1e-9);
        Assert.Null(new GeoMultiPolygon([small]).Simplify(0.001));
        Assert.IsType<GeoMultiLineString>(new GeoMultiLineString([line]).Simplify(0.001));
        var point = new GeoPoint(new(1, 1));
        Assert.Same(point, point.Simplify(1));
    }

    [Fact]
    public void BooleanOperations()
    {
        #region GeoGeometry_BooleanOperations
        var a = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)]]);
        var b = new GeoPolygon([[new(2, 2), new(6, 2), new(6, 6), new(2, 6), new(2, 2)]]);

        var union = a.Union(b);               // an area of 28 square degrees
        var intersection = a.Intersection(b); // the square from (2, 2) to (4, 4)
        var difference = a.Difference(b);     // a with a corner cut away
        #endregion

        Assert.Equal(28, GetArea(union!), 1e-9);
        Assert.Equal(new GeoBounds(2, 2, 4, 4), intersection!.GetBounds());
        Assert.Equal(12, GetArea(difference!), 1e-9);
        Assert.Null(a.Intersection(new GeoPolygon([[new(10, 10), new(11, 10), new(11, 11), new(10, 10)]])));
        Assert.Null(a.Difference(a));

        // A square with a hole minus a square that covers the hole and more: the hole grows
        var withHole = new GeoPolygon([[new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)], [new(4, 4), new(6, 4), new(6, 6), new(4, 6), new(4, 4)]]);
        Assert.Equal(91, GetArea(withHole.Difference(new GeoPolygon([[new(3, 3), new(6, 3), new(6, 6), new(3, 6), new(3, 3)]]))!), 1e-9);

        Assert.Throws<InvalidOperationException>(() => new GeoPoint(new(0, 0)).Union(a));
        Assert.Throws<ArgumentException>("other", () => a.Union(new GeoLineString([new(0, 0), new(1, 1)])));
        Assert.Throws<ArgumentNullException>("other", () => a.Union(null!));
    }

    [Fact]
    public void TransformAndBoundaryLines()
    {
        #region GeoGeometry_Transform
        // A point in Web Mercator (EPSG:3857), in meters, converted to a longitude and a latitude
        var mercator = new GeoPoint(new GeoPosition(261_600, 6_250_600));
        var projection = new MercatorProjection();
        var point = (GeoPoint)mercator.Transform(position => projection.Invert(position.Longitude / 6_378_137, position.Latitude / 6_378_137)!.Value);
        #endregion

        Assert.Equal(2.35, point.Position.Longitude, 0.01);
        Assert.Equal(48.86, point.Position.Latitude, 0.01);

        var square = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4)], [new(1, 1), new(1, 2), new(2, 2), new(1, 1)]]);
        var lines = square.GetBoundaryLines().ToList();
        Assert.HasCount(2, lines);
        Assert.Equal(new GeoPosition(0, 0), lines[0].Positions[^1]);
        Assert.HasCount(5, lines[0].Positions);
        Assert.HasCount(4, lines[1].Positions);
        Assert.Empty(new GeoLineString([new(0, 0), new(1, 1)]).GetBoundaryLines());
        var moved = Assert.IsType<GeoPolygon>(square.Transform(position => position with { Longitude = position.Longitude + 10 }));
        Assert.Equal(new GeoBounds(10, 0, 14, 4), moved.GetBounds());
    }

    [Fact]
    public void ConvexHull()
    {
        #region GeoGeometry_ConvexHull
        // The positions of a few stores: the hull is the polygon of the outer ones
        var stores = new GeoMultiPoint([new(2.35, 48.86), new(2.30, 48.84), new(2.40, 48.88), new(2.33, 48.90), new(2.36, 48.87)]);
        var hull = Assert.IsType<GeoPolygon>(stores.ConvexHull());
        Assert.HasCount(5, hull.Rings[0]); // four corners, and the first one again
        #endregion

        Assert.DoesNotContain(new GeoPosition(2.36, 48.87), hull.Rings[0]);
        Assert.True(GetSignedArea(hull.Rings[0]) > 0);
        Assert.Equal(hull.Rings[0][0], hull.Rings[0][^1]);

        // Positions on a line, a single position, nothing
        Assert.Equal([new GeoPosition(0, 0), new(3, 3)], Assert.IsType<GeoLineString>(new GeoLineString([new(1, 1), new(0, 0), new(3, 3), new(2, 2)]).ConvexHull()).Positions);
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(new GeoMultiPoint([new(1, 2), new(1, 2)]).ConvexHull()).Position);
        Assert.Null(new GeoGeometryCollection([]).ConvexHull());

        // A polygon with a notch, and a collection of parts: the positions on the edges of the hull are not kept
        var notched = new GeoPolygon([[new(0, 0), new(2, 0), new(4, 0), new(4, 4), new(2, 1), new(0, 4), new(0, 0)]]);
        Assert.Equal([new GeoPosition(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)], Assert.IsType<GeoPolygon>(notched.ConvexHull()).Rings[0]);
        var collection = new GeoGeometryCollection([new GeoPoint(new(10, 10)), new GeoLineString([new(0, 0), new(10, 0)])]);
        Assert.HasCount(4, Assert.IsType<GeoPolygon>(collection.ConvexHull()).Rings[0]);
    }

    [Fact]
    public void Distance_Geometries()
    {
        #region GeoGeometry_Distance
        var paris = new GeoPoint(new GeoPosition(2.35, 48.86));
        var route = new GeoLineString([new(-1, 47), new(5, 47.5)]);
        var park = new GeoPolygon([[new(2.2, 48.8), new(2.5, 48.8), new(2.5, 48.95), new(2.2, 48.95), new(2.2, 48.8)]]);

        // The distance on the WGS 84 ellipsoid, in meters, to the nearest position of the line
        Assert.Equal(174_431.755, paris.Distance(route)!.Value, 0.01);

        // 0 for a point inside a polygon
        Assert.Equal(0, paris.Distance(park));
        #endregion

        // Between two positions, as computed by GeographicLib (Karney)
        Assert.Equal(5_551_759.400, new GeoPoint(new(-73.8, 40.6)).Distance(new GeoPoint(new(-0.5, 51.6)))!.Value, 0.001);
        var flindersPeak = new GeoPoint(new(144 + (25 / 60.0) + (29.5244 / 3600), -(37 + (57 / 60.0) + (3.7203 / 3600))));
        var buninyong = new GeoPoint(new(143 + (55 / 60.0) + (35.3839 / 3600), -(37 + (39 / 60.0) + (10.1561 / 3600))));
        Assert.Equal(54_972.271, flindersPeak.Distance(buninyong)!.Value, 0.001);
        Assert.Equal(111_319.491, new GeoPoint(new(0, 0)).Distance(new GeoPoint(new(1, 0)))!.Value, 0.001);

        // A point to a line, two lines, two polygons, a point in the hole of a polygon, a long edge
        Assert.Equal(110_574.389, new GeoPoint(new(0, 1)).Distance(new GeoLineString([new(-1, 0), new(1, 0)]))!.Value, 0.001);
        Assert.Equal(149_139.944, new GeoLineString([new(0, 0), new(1, 1)]).Distance(new GeoLineString([new(2, 0), new(3, 2)]))!.Value, 0.01);
        var first = new GeoPolygon([[new(10, 50), new(11, 50), new(11, 51), new(10, 51), new(10, 50)]]);
        var second = new GeoPolygon([[new(11.5, 50.2), new(12, 50.2), new(12, 50.8), new(11.5, 50.8), new(11.5, 50.2)]]);
        Assert.Equal(35_249.248, first.Distance(second)!.Value, 0.01);
        Assert.Equal(35_249.248, new GeoMultiPolygon([second]).Distance(new GeoGeometryCollection([first]))!.Value, 0.01);
        var withHole = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)], [new(1, 1), new(1, 3), new(3, 3), new(3, 1), new(1, 1)]]);
        Assert.Equal(55_288.419, new GeoPoint(new(2, 2.5)).Distance(withHole)!.Value, 0.01);
        Assert.Equal(1_929_130.373, new GeoPoint(new(10, 60)).Distance(new GeoLineString([new(-20, 40), new(40, 45)]))!.Value, 0.01);

        // The geometries meet: lines that cross or touch, a line inside a polygon, a polygon inside another one
        Assert.Equal(0, new GeoLineString([new(0, 0), new(2, 2)]).Distance(new GeoLineString([new(0, 2), new(2, 0)])));
        Assert.Equal(0, new GeoLineString([new(0, 0), new(2, 0)]).Distance(new GeoLineString([new(2, 0), new(3, 1)])));
        Assert.Equal(0, new GeoLineString([new(10.2, 50.2), new(10.5, 50.5)]).Distance(first));
        Assert.Equal(0, first.Distance(new GeoPolygon([[new(10.2, 50.2), new(10.5, 50.2), new(10.5, 50.5), new(10.2, 50.2)]])));
        Assert.Equal(0, paris.Distance(paris));

        // Empty geometries
        Assert.Null(paris.Distance(new GeoMultiPoint([])));
        Assert.Null(new GeoGeometryCollection([]).Distance(paris));
        Assert.Throws<ArgumentNullException>("other", () => paris.Distance(null!));
    }

    [Fact]
    public void Buffer_Point()
    {
        #region GeoGeometry_Buffer
        // A geofence: the area within 500 meters of a position
        var store = new GeoPoint(new GeoPosition(2.35, 48.86));
        var geofence = Assert.IsType<GeoPolygon>(store.Buffer(500));
        Assert.True(geofence.Contains(new GeoPosition(2.354, 48.86)));  // about 290 meters east
        Assert.False(geofence.Contains(new GeoPosition(2.36, 48.86)));  // about 730 meters east

        // A corridor of 1 kilometer on each side of a route, with flat ends
        var route = new GeoLineString([new(2.35, 48.86), new(2.45, 48.90), new(2.55, 48.88)]);
        var corridor = route.Buffer(1_000, new GeoBufferOptions { EndCapStyle = GeoBufferEndCapStyle.Flat });
        #endregion

        // The vertices of the circle are at the distance, on the ellipsoid, up to the grid of the result (about a centimeter)
        Assert.HasCount(33, geofence.Rings[0]);
        Assert.All(geofence.Rings[0], position => Assert.Equal(500, store.Distance(new GeoPoint(position))!.Value, 0.02));
        Assert.True(GetSignedArea(geofence.Rings[0]) > 0);
        Assert.HasCount(17, Assert.IsType<GeoPolygon>(store.Buffer(500, new GeoBufferOptions { SegmentsPerQuarterCircle = 4 })).Rings[0]);

        // Two circles that overlap become one polygon, and two that do not a multi-polygon
        Assert.IsType<GeoPolygon>(new GeoMultiPoint([new(0, 0), new(0.01, 0)]).Buffer(1_000));
        Assert.HasCount(2, Assert.IsType<GeoMultiPolygon>(new GeoMultiPoint([new(0, 0), new(0.1, 0)]).Buffer(1_000)).Polygons);

        AssertBufferDistance(route, Assert.IsType<GeoPolygon>(corridor), 1_000);

        // A point or a line has no buffer for a distance of 0 or less
        Assert.Null(store.Buffer(0));
        Assert.Null(store.Buffer(-10));
        Assert.Null(route.Buffer(-10));
        Assert.Throws<ArgumentOutOfRangeException>("distance", () => store.Buffer(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("distance", () => store.Buffer(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(GeoBufferJoinStyle.Round, GeoBufferEndCapStyle.Round)]
    [InlineData(GeoBufferJoinStyle.Bevel, GeoBufferEndCapStyle.Flat)]
    [InlineData(GeoBufferJoinStyle.Miter, GeoBufferEndCapStyle.Square)]
    public void Buffer_Line(GeoBufferJoinStyle joinStyle, GeoBufferEndCapStyle endCapStyle)
    {
        // A route with a sharp turn and a long edge that is cut in pieces
        var route = new GeoLineString([new(5, 45), new(5.2, 45.05), new(5.1, 45.1), new(6.5, 46)]);
        var options = new GeoBufferOptions { JoinStyle = joinStyle, EndCapStyle = endCapStyle };
        var buffer = Assert.IsType<GeoPolygon>(route.Buffer(2_000, options));

        Assert.Single(buffer.Rings);
        Assert.Equal(0, route.Distance(buffer));
        if (joinStyle is GeoBufferJoinStyle.Round)
        {
            AssertBufferDistance(route, buffer, 2_000);
        }

        // Inside where the line is closer than the distance, around the long edge
        for (var longitude = 5.5; longitude <= 6; longitude += 0.01)
        {
            for (var latitude = 45.3; latitude <= 45.8; latitude += 0.01)
            {
                var position = new GeoPosition(longitude, latitude);
                var distance = route.Distance(new GeoPoint(position))!.Value;
                if (Math.Abs(distance - 2_000) > 50)
                {
                    Assert.Equal(distance < 2_000, buffer.Contains(position));
                }
            }
        }

        // The round and square caps go beyond the start of the line, the flat cap does not
        var beyond = new GeoPosition(5 - (1_500 / 78_850.0), 45);
        Assert.Equal(endCapStyle is not GeoBufferEndCapStyle.Flat, buffer.Contains(beyond));
    }

    [Fact]
    public void Buffer_Polygon()
    {
        var square = new GeoPolygon([[new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0, 0)]]);
        var squareArea = GetArea(square);

        // Grown: the square and a band of 10 kilometers around it
        var grown = Assert.IsType<GeoPolygon>(square.Buffer(10_000));
        Assert.Single(grown.Rings);
        Assert.True(GetArea(grown) > squareArea);
        Assert.Equal(10_000, new GeoPoint(new(0.5, 0)).Distance(new GeoLineString(grown.Rings[0]))!.Value, 30.0);
        Assert.InRange(new GeoPoint(new(1, 1)).Distance(new GeoLineString(grown.Rings[0]))!.Value, (10_000 * Math.Cos(Math.PI / 32)) - 1, 10_000); // the chords of the arc

        // Mitered: the corners stay square
        var mitered = Assert.IsType<GeoPolygon>(square.Buffer(10_000, new GeoBufferOptions { JoinStyle = GeoBufferJoinStyle.Miter }));
        Assert.Equal(-10_000 / 110_574.0, mitered.GetBounds()!.Value.South, 1e-4);
        Assert.True(mitered.Contains(new GeoPosition(1.085, 1.085)));
        Assert.False(grown.Contains(new GeoPosition(1.085, 1.085)));

        // Shrunk: the square without a band of 10 kilometers along its edges, then nothing
        var shrunk = Assert.IsType<GeoPolygon>(square.Buffer(-10_000));
        Assert.True(GetArea(shrunk) < squareArea);
        Assert.Equal(10_000, new GeoPoint(new(0.5, 0)).Distance(new GeoLineString(shrunk.Rings[0]))!.Value, 30.0);
        Assert.Null(square.Buffer(-60_000));

        // A hole grows smaller when the polygon grows, and disappears
        var withHole = new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)], [new(1, 1), new(1, 3), new(3, 3), new(3, 1), new(1, 1)]]);
        Assert.HasCount(2, Assert.IsType<GeoPolygon>(withHole.Buffer(10_000)).Rings);
        Assert.Single(Assert.IsType<GeoPolygon>(withHole.Buffer(150_000)).Rings);

        // A distance of 0 repairs the polygon; a collection unites its polygons, lines and points
        Assert.Equal(squareArea, GetArea(square.Buffer(0)!), 1e-9);
        var collection = new GeoGeometryCollection([square, new GeoLineString([new(1, 0.5), new(2, 0.5)]), new GeoPoint(new(3, 0.5))]);
        Assert.HasCount(2, Assert.IsType<GeoMultiPolygon>(collection.Buffer(5_000)).Polygons);
        Assert.Equal(squareArea, GetArea(collection.Buffer(0)!), 1e-9);
    }

    [Fact]
    public void BufferOptions()
    {
        var options = new GeoBufferOptions();
        Assert.Equal(8, options.SegmentsPerQuarterCircle);
        Assert.Equal(GeoBufferJoinStyle.Round, options.JoinStyle);
        Assert.Equal(GeoBufferEndCapStyle.Round, options.EndCapStyle);
        Assert.Equal(5, options.MiterLimit);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => options.SegmentsPerQuarterCircle = 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => options.SegmentsPerQuarterCircle = 1001);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => options.MiterLimit = 0.5);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => options.MiterLimit = double.NaN);
    }

    [Fact]
    public void Operations_Countries_DistanceAndHull()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "countries-110m.geojson");
        var countries = GeoJson.Parse(File.ReadAllText(path)).DistinctBy(country => country.Properties["name"]).ToDictionary(country => (string)country.Properties["name"]!, country => country.Geometry!);

        // Neighbors touch; the Strait of Gibraltar is about 14 kilometers wide, but the 1:110m coastlines are simplified
        Assert.Equal(0, countries["France"].Distance(countries["Germany"]));
        Assert.Equal(0, countries["France"].Distance(countries["Spain"]));
        Assert.Equal(26_000, countries["Spain"].Distance(countries["Morocco"])!.Value, 13_000.0);
        Assert.True(countries["Russia"].Distance(countries["Canada"]) > 0);

        // A band of 20 kilometers around France, and France without a band of 20 kilometers along its borders
        var france = countries["France"].MakeValid();
        var grown = france.Buffer(20_000)!;
        var shrunk = france.Buffer(-20_000)!;
        Assert.True(GetArea(grown) > GetArea(france));
        Assert.True(GetArea(shrunk) < GetArea(france));
        Assert.Null(france.Difference(grown));
        Assert.Null(shrunk.Difference(france));
        AssertBufferDistance(france, Assert.IsType<GeoMultiPolygon>(grown), 20_000);

        foreach (var geometry in countries.Values)
        {
            var hull = Assert.IsType<GeoPolygon>(geometry.ConvexHull());
            Assert.True(GetArea(hull) >= GetArea(geometry.MakeValid()) - 1e-9);
        }
    }

    private static void AssertBufferDistance(GeoGeometry geometry, GeoGeometry buffer, double distance)
    {
        // The vertices of the buffer are at the distance, or a little closer where the chords of the arcs meet; the ends of a line with flat ends are on the buffer
        var positions = geometry.GetPositions().ToHashSet();
        foreach (var position in buffer.GetPositions().Where(position => !positions.Contains(position)))
        {
            var actual = geometry.Distance(new GeoPoint(position))!.Value;
            Assert.InRange(actual, distance * 0.98, distance + 1);
        }
    }

    [Fact]
    public void Operations_Countries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "countries-110m.geojson");
        var countries = GeoJson.Parse(File.ReadAllText(path));
        var europe = new GeoBounds(West: -12, South: 35, East: 34, North: 62);
        foreach (var country in countries)
        {
            var geometry = country.Geometry!;
            var repaired = geometry.MakeValid();

            // The ring of Sudan crosses itself near (24.2, 8.7): the repaired area counts the loop it makes
            if (country.Properties["name"] is not "Sudan")
            {
                Assert.Equal(GetArea(geometry), GetArea(repaired), 1e-3);
            }

            Assert.Equal(GetArea(repaired), GetArea(repaired.MakeValid()), 1e-9);

            // The parts of a country in and out of a box make the whole country
            if (repaired.Clip(europe) is { } inside)
            {
                var outside = repaired.Difference(new GeoPolygon([[new(europe.West, europe.South), new(europe.East, europe.South), new(europe.East, europe.North), new(europe.West, europe.North)]]));
                Assert.Equal(GetArea(repaired), GetArea(inside) + (outside is null ? 0 : GetArea(outside)), 1e-3);
            }

            Assert.NotNull(repaired.Simplify(0.01));
        }
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

    private static double GetArea(GeoGeometry geometry) => geometry.GetSimpleGeometries()
        .OfType<GeoPolygon>()
        .Sum(polygon => polygon.Rings.Count == 0 ? 0 : Math.Abs(GetSignedArea(polygon.Rings[0])) - polygon.Rings.Skip(1).Sum(ring => Math.Abs(GetSignedArea(ring))));
}
