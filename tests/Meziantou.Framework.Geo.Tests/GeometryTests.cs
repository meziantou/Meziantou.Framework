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
        Assert.Throws<ArgumentException>("bounds", () => square.Clip(new GeoBounds(0, 10, 10, 0)));
        Assert.Throws<ArgumentException>("bounds", () => square.Clip(new GeoBounds(double.NaN, 0, 10, 10)));

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

    [Fact]
    public void Antimeridian_Fiji()
    {
        #region GeoGeometry_SplitAtAntimeridian
        // Fiji, as a ring whose edges cross the 180th meridian
        var fiji = new GeoPolygon([[new(177, -19), new(-178, -19), new(-178, -16), new(177, -16), new(177, -19)]]);
        Assert.True(fiji.CrossesAntimeridian);

        // Split along the 180th meridian, as RFC 7946 wants it: a polygon on each side
        var split = Assert.IsType<GeoMultiPolygon>(fiji.SplitAtAntimeridian());
        Assert.Equal(new GeoBounds(177, -19, 180, -16), split.Polygons[0].GetBounds());
        Assert.Equal(new GeoBounds(-180, -19, -178, -16), split.Polygons[1].GetBounds());
        Assert.True(split.Contains(new GeoPosition(Longitude: 179, Latitude: -17)));
        Assert.False(split.CrossesAntimeridian);

        // The box of the parts crosses the 180th meridian: its west side is east of its east side
        var bounds = split.GetBounds(GeoEdgeType.Geodesic)!.Value;
        Assert.True(bounds.CrossesAntimeridian);
        Assert.Equal((177, -178), (bounds.West, bounds.East));
        Assert.True(bounds.Contains(new GeoPosition(Longitude: -179, Latitude: -17)));
        #endregion

        // Straight in degrees, the edges of the ring go the long way: the polygon is the rest of the band
        Assert.False(fiji.Contains(new GeoPosition(179, -17)));
        Assert.True(fiji.Contains(new GeoPosition(0, -17)));
        Assert.Equal(new GeoBounds(-178, -19, 177, -16), fiji.GetBounds());

        // As great circles, the edges cross the 180th meridian, and the southern one bends toward the South Pole
        Assert.True(fiji.Contains(new GeoPosition(179, -17), GeoEdgeType.Geodesic));
        Assert.True(fiji.Contains(new GeoPosition(-178.5, -17), GeoEdgeType.Geodesic));
        Assert.False(fiji.Contains(new GeoPosition(0, -17), GeoEdgeType.Geodesic));
        var geodesicBounds = fiji.GetBounds(GeoEdgeType.Geodesic)!.Value;
        Assert.Equal((177, -178, -16), (geodesicBounds.West, geodesicBounds.East, geodesicBounds.North));
        Assert.True(geodesicBounds.South < -19);

        // The label is on the islands, not on the other side of the world
        var (longitude, latitude, _) = fiji.LabelPosition!.Value;
        Assert.Equal(179.5, longitude, 1e-9);
        Assert.Equal(-17.5, latitude, 1e-9);
        Assert.Equal(178.5, split.LabelPosition!.Value.Longitude, 1e-9);

        // Clip to a box that crosses the 180th meridian: a part on each side
        var clipped = Assert.IsType<GeoMultiPolygon>(split.Clip(new GeoBounds(178, -18, -179, -17)));
        Assert.Equal([new GeoBounds(178, -18, 180, -17), new GeoBounds(-180, -18, -179, -17)], clipped.Polygons.Select(polygon => polygon.GetBounds()!.Value));
        Assert.Same(split.Polygons[0], split.Polygons[0].Clip(new GeoBounds(170, -20, -170, -10)));
        Assert.Null(split.Clip(new GeoBounds(170, 0, -170, 10)));
        Assert.Throws<ArgumentException>("bounds", () => split.Clip(new GeoBounds(178, -17, -179, -18)));
    }

    [Fact]
    public void Antimeridian_Chukotka()
    {
        // A part of Russia that crosses the 180th meridian
        var chukotka = new GeoPolygon([[new(160, 60), new(-170, 60), new(-170, 70), new(160, 70), new(160, 60)]]);
        var anadyr = new GeoPosition(177.5, 64.7);
        var lavrentiya = new GeoPosition(-171, 65.6);
        Assert.True(chukotka.Contains(anadyr, GeoEdgeType.Geodesic));
        Assert.True(chukotka.Contains(lavrentiya, GeoEdgeType.Geodesic));
        Assert.False(chukotka.Contains(new GeoPosition(0, 65), GeoEdgeType.Geodesic));
        Assert.False(chukotka.Contains(lavrentiya));
        Assert.True(new GeoMultiPolygon([chukotka]).Contains(lavrentiya, GeoEdgeType.Geodesic));

        var split = Assert.IsType<GeoMultiPolygon>(chukotka.SplitAtAntimeridian());
        Assert.True(split.Contains(anadyr));
        Assert.True(split.Contains(lavrentiya));
        Assert.False(split.Contains(new GeoPosition(0, 65)));

        var (longitude, latitude, _) = chukotka.LabelPosition!.Value;
        Assert.Equal(175, longitude, 1e-9);
        Assert.Equal(65, latitude, 1e-9);

        // A hole on the other side of the 180th meridian stays in its polygon
        var withHole = new GeoPolygon(
        [
            [new(160, 60), new(-170, 60), new(-170, 70), new(160, 70), new(160, 60)],
            [new(-175, 62), new(-172, 62), new(-172, 64), new(-175, 64), new(-175, 62)],
        ]);
        var splitWithHole = withHole.SplitAtAntimeridian();
        Assert.False(Assert.IsType<GeoMultiPolygon>(splitWithHole).Contains(new GeoPosition(-173, 63)));
        Assert.True(((GeoMultiPolygon)splitWithHole).Contains(new GeoPosition(-171, 63)));
    }

    [Fact]
    public void Antimeridian_Route()
    {
        #region Geo_GeodesicBounds
        var tokyo = new GeoPosition(Longitude: 139.69, Latitude: 35.69);
        var sanFrancisco = new GeoPosition(Longitude: -122.42, Latitude: 37.77);
        var route = new GeoLineString([tokyo, sanFrancisco]);

        // Straight in degrees, the edge goes the long way, over Asia, Europe and the Atlantic
        Assert.Equal(new GeoBounds(-122.42, 35.69, 139.69, 37.77), route.GetBounds());

        // As a great circle, it crosses the Pacific and goes north of both cities
        var bounds = route.GetBounds(GeoEdgeType.Geodesic)!.Value;
        Assert.True(bounds.CrossesAntimeridian);
        Assert.Equal((139.69, -122.42), (bounds.West, bounds.East));
        Assert.Equal(48.7, bounds.North, 1);
        #endregion

        Assert.True(route.CrossesAntimeridian);
        Assert.Equal(35.69, bounds.South);

        // Split, the route ends and starts again on the 180th meridian
        var split = Assert.IsType<GeoMultiLineString>(route.SplitAtAntimeridian());
        Assert.HasCount(2, split.LineStrings);
        var (east, west) = (split.LineStrings[0].Positions, split.LineStrings[1].Positions);
        Assert.Equal(tokyo, east[0]);
        Assert.Equal(180, east[^1].Longitude);
        Assert.Equal(-180, west[0].Longitude);
        Assert.Equal(sanFrancisco, west[^1]);
        Assert.Equal(east[^1].Latitude, west[0].Latitude);
        Assert.Equal(36.5465, west[0].Latitude, 4);
        var splitBounds = split.GetBounds(GeoEdgeType.Geodesic)!.Value;
        Assert.Equal((139.69, 35.69, -122.42), (splitBounds.West, splitBounds.South, splitBounds.East));

        // Clipped to a box around the Pacific: the two parts
        Assert.HasCount(2, Assert.IsType<GeoMultiLineString>(split.Clip(new GeoBounds(170, 30, -170, 50))).LineStrings);

        // The label is over the Pacific
        var (longitude, latitude, _) = route.LabelPosition!.Value;
        Assert.Equal(-171.365, longitude, 1e-9);
        Assert.Equal(36.73, latitude, 1e-9);
        Assert.InRange(split.LabelPosition!.Value.Longitude, -180, -170);

        // The box of the cities of the features
        GeoFeatureCollection features = [new GeoFeature(new GeoPoint(tokyo)), new GeoFeature(geometry: null), new GeoFeature(new GeoPoint(sanFrancisco))];
        Assert.Equal(new GeoBounds(139.69, 35.69, -122.42, 37.77), features.GetBounds(GeoEdgeType.Geodesic));
        Assert.Equal(new GeoBounds(-122.42, 35.69, 139.69, 37.77), features.GetBounds(GeoEdgeType.Planar));
        Assert.Null(new GeoFeatureCollection().GetBounds(GeoEdgeType.Geodesic));
        Assert.Throws<ArgumentOutOfRangeException>("edgeType", () => features.GetBounds((GeoEdgeType)2));
    }

    [Fact]
    public void Antimeridian_SplitLinesAndPoints()
    {
        // The positions where a line crosses the meridian are interpolated, altitudes included
        var line = new GeoLineString([new(170, 0, 100), new(-170, 10, 200), new(-160, 10)]);
        var split = Assert.IsType<GeoMultiLineString>(line.SplitAtAntimeridian());
        Assert.Equal([new GeoPosition(170, 0, 100), new(180, 5, 150)], split.LineStrings[0].Positions);
        Assert.Equal([new GeoPosition(-180, 5, 150), new(-170, 10, 200), new(-160, 10)], split.LineStrings[1].Positions);

        // A line that crosses the meridian twice, and one that touches it
        var twice = Assert.IsType<GeoMultiLineString>(new GeoLineString([new(170, 0), new(-170, 0), new(170, 2)]).SplitAtAntimeridian());
        Assert.HasCount(3, twice.LineStrings);
        var touching = new GeoLineString([new(170, 0), new(180, 0), new(180, 1), new(170, 1)]);
        Assert.Same(touching, touching.SplitAtAntimeridian());
        var crossingAtVertex = Assert.IsType<GeoMultiLineString>(new GeoLineString([new(170, 0), new(180, 0), new(-170, 0)]).SplitAtAntimeridian());
        Assert.Equal([new GeoPosition(170, 0), new(180, 0)], crossingAtVertex.LineStrings[0].Positions);
        Assert.Equal([new GeoPosition(-180, 0), new(-170, 0)], crossingAtVertex.LineStrings[1].Positions);

        // The longitudes past 180 move around the world
        var beyond = Assert.IsType<GeoMultiLineString>(new GeoLineString([new(175, 0), new(185, 0)]).SplitAtAntimeridian());
        Assert.Equal([new GeoPosition(-180, 0), new(-175, 0)], beyond.LineStrings[1].Positions);
        Assert.Equal(new GeoPosition(-170, 5), Assert.IsType<GeoPoint>(new GeoPoint(new(190, 5)).SplitAtAntimeridian()).Position);
        Assert.Equal([new GeoPosition(-170, 5), new(10, 5)], Assert.IsType<GeoMultiPoint>(new GeoMultiPoint([new(190, 5), new(10, 5)]).SplitAtAntimeridian()).Positions);
        Assert.Equal([new GeoPosition(-170, 5)], Assert.IsType<GeoLineString>(new GeoLineString([new(190, 5)]).SplitAtAntimeridian()).Positions);

        // The geometries that do not cross the meridian are returned as they are, a box around the whole world included
        var world = new GeoPolygon([[new(-180, -90), new(180, -90), new(180, 90), new(-180, 90), new(-180, -90)]]);
        Assert.False(world.CrossesAntimeridian);
        Assert.Same(world, world.SplitAtAntimeridian());
        GeoGeometry[] unchanged = [new GeoPoint(new(10, 10)), new GeoMultiPoint([new(10, 10)]), new GeoMultiLineString([touching]), new GeoMultiPolygon([world]), new GeoGeometryCollection([world])];
        foreach (var geometry in unchanged)
        {
            Assert.Same(geometry, geometry.SplitAtAntimeridian());
        }

        var multiLineString = Assert.IsType<GeoMultiLineString>(new GeoMultiLineString([touching, line]).SplitAtAntimeridian());
        Assert.HasCount(3, multiLineString.LineStrings);
        var multiPolygon = Assert.IsType<GeoMultiPolygon>(new GeoMultiPolygon([world, new GeoPolygon([[new(177, -19), new(-178, -19), new(-178, -16), new(177, -16)]])]).SplitAtAntimeridian());
        Assert.HasCount(3, multiPolygon.Polygons);
        Assert.Same(world, multiPolygon.Polygons[0]);
        var collection = Assert.IsType<GeoGeometryCollection>(new GeoGeometryCollection([new GeoPoint(new(10, 10)), line]).SplitAtAntimeridian());
        Assert.IsType<GeoMultiLineString>(collection.Geometries[1]);

        // Clip to a box that crosses the 180th meridian
        var points = new GeoMultiPoint([new(175, 0), new(-175, 0), new(0, 0)]);
        Assert.Equal([new GeoPosition(175, 0), new(-175, 0)], Assert.IsType<GeoMultiPoint>(points.Clip(new GeoBounds(170, -10, -170, 10))).Positions);
        var point = new GeoPoint(new(175, 0));
        Assert.Same(point, point.Clip(new GeoBounds(170, -10, -170, 10)));
        Assert.IsType<GeoGeometryCollection>(new GeoGeometryCollection([point, new GeoLineString([new(-175, 0), new(-172, 0)])]).Clip(new GeoBounds(170, -10, -170, 10)));
    }

    [Fact]
    public void Geodesic_ContainsAndBounds()
    {
        #region Geo_GeodesicContains
        var polygon = new GeoPolygon([[new(-60, 70), new(60, 70), new(60, 80), new(-60, 80), new(-60, 70)]]);

        // Straight in degrees, the edges follow the parallels 70 and 80
        Assert.True(polygon.Contains(new GeoPosition(Longitude: 0, Latitude: 75)));
        Assert.False(polygon.Contains(new GeoPosition(Longitude: 0, Latitude: 82)));

        // As great circles, they bend toward the North Pole: the southern edge passes at 79.7° on the meridian 0
        Assert.False(polygon.Contains(new GeoPosition(Longitude: 0, Latitude: 75), GeoEdgeType.Geodesic));
        Assert.True(polygon.Contains(new GeoPosition(Longitude: 0, Latitude: 82), GeoEdgeType.Geodesic));
        #endregion

        Assert.True(polygon.Contains(new GeoPosition(0, 79.5)));
        Assert.True(polygon.Contains(new GeoPosition(0, 79.9), GeoEdgeType.Geodesic));
        Assert.False(polygon.Contains(new GeoPosition(0, 79.5), GeoEdgeType.Geodesic));
        Assert.True(polygon.Contains(new GeoPosition(0, 75), GeoEdgeType.Planar));
        Assert.Throws<ArgumentOutOfRangeException>("edgeType", () => polygon.Contains(new GeoPosition(0, 75), (GeoEdgeType)2));
        Assert.Throws<ArgumentOutOfRangeException>("edgeType", () => new GeoMultiPolygon([]).Contains(new GeoPosition(0, 75), (GeoEdgeType)2));

        var bounds = polygon.GetBounds(GeoEdgeType.Geodesic)!.Value;
        Assert.Equal((-60, 70, 60), (bounds.West, bounds.South, bounds.East));
        Assert.Equal(84.96, bounds.North, 2);
        Assert.Equal(polygon.GetBounds(), polygon.GetBounds(GeoEdgeType.Planar));
        Assert.Throws<ArgumentOutOfRangeException>("edgeType", () => polygon.GetBounds((GeoEdgeType)2));
        Assert.Null(new GeoMultiPolygon([]).GetBounds(GeoEdgeType.Geodesic));

        // Near the equator and for short edges, both give the same answer
        var square = new GeoPolygon([[new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)], [new(4, 4), new(6, 4), new(6, 6), new(4, 6), new(4, 4)]]);
        foreach (var position in new GeoPosition[] { new(2, 2), new(5, 5), new(12, 5), new(5, -1), new(-170, 5), new(9.9, 9.5) })
        {
            Assert.Equal(square.Contains(position), square.Contains(position, GeoEdgeType.Geodesic));
        }

        // A ring that goes around the South Pole contains it, whatever its direction
        GeoPosition[] coast = [new(0, -70), new(90, -70), new(180, -70), new(-90, -70), new(0, -70)];
        foreach (var ring in new[] { coast, coast.Reverse().ToArray() })
        {
            var antarctica = new GeoPolygon([ring]);
            Assert.True(antarctica.Contains(new GeoPosition(0, -80), GeoEdgeType.Geodesic));
            Assert.True(antarctica.Contains(new GeoPosition(-135, -89.9), GeoEdgeType.Geodesic));
            Assert.False(antarctica.Contains(new GeoPosition(45, -60), GeoEdgeType.Geodesic));
            Assert.False(antarctica.Contains(new GeoPosition(45, -73), GeoEdgeType.Geodesic)); // the edges bend toward the pole
            Assert.Equal(new GeoBounds(-180, -90, 180, -70), antarctica.GetBounds(GeoEdgeType.Geodesic));

            var split = Assert.IsType<GeoMultiPolygon>(antarctica.SplitAtAntimeridian());
            Assert.HasCount(2, split.Polygons);
            Assert.True(split.Contains(new GeoPosition(45, -80)));
            Assert.True(split.Contains(new GeoPosition(-45, -80)));
            Assert.False(split.Contains(new GeoPosition(45, -60)));
            Assert.Equal(new GeoBounds(-180, -90, 180, -70), split.GetBounds());
        }

        // A ring around the North Pole
        var arctic = new GeoPolygon([[new(0, 80), new(120, 80), new(-120, 80)]]);
        Assert.True(arctic.Contains(new GeoPosition(50, 89), GeoEdgeType.Geodesic));
        Assert.False(arctic.Contains(new GeoPosition(50, 70), GeoEdgeType.Geodesic));
        Assert.Equal(new GeoBounds(-180, 80, 180, 90), arctic.GetBounds(GeoEdgeType.Geodesic));
    }

    [Fact]
    public void Geodesic_Countries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "countries-110m.geojson");
        var countries = GeoJson.Parse(File.ReadAllText(path)).ToDictionary(country => (string)country.Properties["name"]!, country => country.Geometry!, StringComparer.Ordinal);

        // Natural Earth splits the countries along the 180th meridian, as RFC 7946 wants it
        foreach (var geometry in countries.Values)
        {
            Assert.False(geometry.CrossesAntimeridian);
            Assert.Same(geometry, geometry.SplitAtAntimeridian());
        }

        // The geodesic box of the parts of Fiji and of Russia crosses the 180th meridian
        var fiji = countries["Fiji"].GetBounds(GeoEdgeType.Geodesic)!.Value;
        Assert.True(fiji.CrossesAntimeridian);
        Assert.InRange(fiji.West, 175, 180);
        Assert.InRange(fiji.East, -180, -178);
        var russia = countries["Russia"].GetBounds(GeoEdgeType.Geodesic)!.Value;
        Assert.True(russia.CrossesAntimeridian);
        Assert.InRange(russia.West, 19, 20);
        Assert.InRange(russia.East, -171, -168);
        Assert.Equal(new GeoBounds(-180, -90, 180, 90), GetBoundsWithoutNorth(countries["Antarctica"]));

        // The other countries do not cross it
        var france = countries["France"];
        Assert.False(france.GetBounds(GeoEdgeType.Geodesic)!.Value.CrossesAntimeridian);

        // The country of a few cities
        AssertCountry("France", new GeoPosition(2.35, 48.86));
        AssertCountry("Russia", new GeoPosition(177.5, 64.7));
        AssertCountry("Fiji", new GeoPosition(178.44, -18.14));
        AssertCountry("Antarctica", new GeoPosition(106.8, -78.46));
        AssertCountry("Antarctica", new GeoPosition(-135, -89.9));
        AssertCountry("Brazil", new GeoPosition(-47.88, -15.79));

        void AssertCountry(string name, GeoPosition position)
        {
            var found = countries.Where(country => country.Value switch
            {
                GeoPolygon polygon => polygon.Contains(position, GeoEdgeType.Geodesic),
                GeoMultiPolygon multiPolygon => multiPolygon.Contains(position, GeoEdgeType.Geodesic),
                _ => false,
            }).Select(country => country.Key).ToArray();
            Assert.Equal([name], found);
        }

        static GeoBounds GetBoundsWithoutNorth(GeoGeometry geometry) => geometry.GetBounds(GeoEdgeType.Geodesic)!.Value with { North = 90 };
    }

    [Fact]
    public void Bounds_Antimeridian()
    {
        var pacific = new GeoBounds(West: 170, South: -10, East: -170, North: 10);
        Assert.True(pacific.CrossesAntimeridian);
        Assert.False(new GeoBounds(0, 0, 10, 10).CrossesAntimeridian);
        Assert.True(pacific.Contains(new GeoPosition(175, 0)));
        Assert.True(pacific.Contains(new GeoPosition(-175, 0)));
        Assert.True(pacific.Contains(new GeoPosition(180, 0)));
        Assert.False(pacific.Contains(new GeoPosition(0, 0)));
        Assert.False(pacific.Contains(new GeoPosition(175, 20)));

        Assert.True(pacific.Intersects(new GeoBounds(-175, -5, -160, 5)));
        Assert.True(new GeoBounds(-175, -5, -160, 5).Intersects(pacific));
        Assert.False(pacific.Intersects(new GeoBounds(0, -5, 10, 5)));
        Assert.False(pacific.Intersects(new GeoBounds(175, 20, 176, 30)));
        Assert.True(pacific.Intersects(new GeoBounds(160, -20, -160, 20)));
        Assert.True(new GeoBounds(0, 0, 10, 10).Intersects(new GeoBounds(10, 10, 20, 20)));
        Assert.False(new GeoBounds(0, 0, 10, 10).Intersects(new GeoBounds(11, 0, 20, 10)));

        Assert.Equal(new GeoBounds(170, -20, -160, 10), pacific.Union(new GeoBounds(-175, -20, -160, 0)));
        Assert.Equal(new GeoBounds(0, -10, -170, 10), pacific.Union(new GeoBounds(0, 0, 10, 10)));
        Assert.Equal(new GeoBounds(-180, -10, 180, 10), pacific.Union(new GeoBounds(-170, -10, 170, 10)));
        Assert.Equal(new GeoBounds(160, -10, -170, 10), new GeoBounds(160, 0, 175, 5).Union(pacific));

        // Without a box that crosses the meridian, the union does not cross it either
        Assert.Equal(new GeoBounds(-180, 0, 180, 1), new GeoBounds(170, 0, 180, 1).Union(new GeoBounds(-180, 0, -170, 1)));
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
