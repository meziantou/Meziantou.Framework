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
}
