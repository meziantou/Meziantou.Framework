namespace Meziantou.Framework.Geo.Tests;

public sealed class WellKnownTextTests
{
    [Theory]
    [InlineData("POINT (1 2)")]
    [InlineData("POINT Z (1 2 3)")]
    [InlineData("POINT (-0.5 1E-07)")]
    [InlineData("LINESTRING (0 0, 1 1, 2 0.5)")]
    [InlineData("LINESTRING EMPTY")]
    [InlineData("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))")]
    [InlineData("POLYGON EMPTY")]
    [InlineData("MULTIPOINT ((1 2), (3 4))")]
    [InlineData("MULTIPOINT EMPTY")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), EMPTY)")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")]
    [InlineData("MULTIPOLYGON Z (((0 0 1, 1 0 1, 1 1 1, 0 0 1)))")]
    [InlineData("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1), GEOMETRYCOLLECTION EMPTY)")]
    [InlineData("GEOMETRYCOLLECTION Z (POINT Z (1 2 3), GEOMETRYCOLLECTION Z (LINESTRING Z (0 0 1, 1 1 2)))")]
    [InlineData("GEOMETRYCOLLECTION EMPTY")]
    public void Parse_RoundTrips(string wkt)
    {
        Assert.Equal(wkt, WellKnownText.Format(WellKnownText.Parse(wkt)));
    }

    [Fact]
    public void Parse_Types()
    {
        Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(WellKnownText.Parse("POINT (1 2)")).Position);
        Assert.Equal([new GeoPosition(0, 0), new(1, 1)], Assert.IsType<GeoLineString>(WellKnownText.Parse("LINESTRING (0 0, 1 1)")).Positions);
        var polygon = Assert.IsType<GeoPolygon>(WellKnownText.Parse("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))"));
        Assert.HasCount(2, polygon.Rings);
        Assert.Equal([new GeoPosition(1, 1), new(2, 1), new(2, 2), new(1, 1)], polygon.Rings[1]);
        Assert.Equal([new GeoPosition(1, 2), new(3, 4)], Assert.IsType<GeoMultiPoint>(WellKnownText.Parse("MULTIPOINT ((1 2), (3 4))")).Positions);
        Assert.HasCount(2, Assert.IsType<GeoMultiLineString>(WellKnownText.Parse("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")).LineStrings);
        Assert.HasCount(2, Assert.IsType<GeoMultiPolygon>(WellKnownText.Parse("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")).Polygons);
        var collection = Assert.IsType<GeoGeometryCollection>(WellKnownText.Parse("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))"));
        Assert.Equal([GeoGeometryType.Point, GeoGeometryType.LineString], collection.Geometries.Select(geometry => geometry.Type));
    }

    [Fact]
    public void Parse_Syntax()
    {
        Assert.Equal(new GeoPosition(1.5, -2), Assert.IsType<GeoPoint>(WellKnownText.Parse("  point(1.5   -2.0)\r\n")).Position);
        Assert.Equal(new GeoPosition(100, 0.025), Assert.IsType<GeoPoint>(WellKnownText.Parse("Point\t(1e2 2.5E-2)")).Position);
        Assert.Equal(new GeoPosition(.5, -.5), Assert.IsType<GeoPoint>(WellKnownText.Parse("POINT(+.5 -.5)")).Position);
        Assert.Equal([new GeoPosition(1, 2), new(3, 4)], Assert.IsType<GeoMultiPoint>(WellKnownText.Parse("MULTIPOINT (1 2, 3 4)")).Positions);
        Assert.Equal([new GeoPosition(1, 2), new(3, 4)], Assert.IsType<GeoMultiPoint>(WellKnownText.Parse("multipoint((1 2),3 4,EMPTY)")).Positions);
        Assert.Equal("POLYGON (EMPTY, (0 0, 1 0, 0 0))", WellKnownText.Format(WellKnownText.Parse("POLYGON (EMPTY, (0 0, 1 0, 0 0))")));
    }

    [Fact]
    public void Parse_Dimensions()
    {
        Assert.Equal(new GeoPosition(1, 2, 3), Assert.IsType<GeoPoint>(WellKnownText.Parse("POINT Z (1 2 3)")).Position);
        Assert.Equal(new GeoPosition(1, 2, 3), Assert.IsType<GeoPoint>(WellKnownText.Parse("POINTZ(1 2 3)")).Position);
        Assert.Equal(new GeoPosition(1, 2, 3), Assert.IsType<GeoPoint>(WellKnownText.Parse("POINT (1 2 3)")).Position);
        Assert.Equal(new GeoPosition(1, 2) { Measure = 4 }, Assert.IsType<GeoPoint>(WellKnownText.Parse("POINT M (1 2 4)")).Position);
        Assert.Equal(new GeoPosition(1, 2) { Measure = 4 }, Assert.IsType<GeoPoint>(WellKnownText.Parse("POINTM(1 2 4)")).Position);
        Assert.Equal(new GeoPosition(1, 2, 3) { Measure = 4 }, Assert.IsType<GeoPoint>(WellKnownText.Parse("POINT ZM (1 2 3 4)")).Position);
        Assert.Equal(new GeoPosition(1, 2, 3) { Measure = 4 }, Assert.IsType<GeoPoint>(WellKnownText.Parse("pointzm (1 2 3 4)")).Position);
        Assert.Equal(new GeoPosition(1, 2, 3) { Measure = 4 }, Assert.IsType<GeoPoint>(WellKnownText.Parse("POINT (1 2 3 4)")).Position);
        Assert.Equal([new GeoPosition(0, 0) { Measure = 5 }, new(1, 1) { Measure = 6 }], Assert.IsType<GeoLineString>(WellKnownText.Parse("LINESTRING M (0 0 5, 1 1 6)")).Positions);

        // The children of a tagged collection use its tag
        var collection = Assert.IsType<GeoGeometryCollection>(WellKnownText.Parse("GEOMETRYCOLLECTION M (POINT (1 2 3), POINT Z (4 5 6))"));
        Assert.Equal([new GeoPosition(1, 2) { Measure = 3 }, new(4, 5, 6)], collection.GetPositions());
        Assert.Equal("LINESTRING EMPTY", WellKnownText.Format(WellKnownText.Parse("LINESTRING Z EMPTY")));
    }

    [Theory]
    [InlineData("POINT M (1 2 3)")]
    [InlineData("POINT ZM (1 2 3 4)")]
    [InlineData("LINESTRING M (0 0 1.5, 1 1 -2)")]
    [InlineData("POLYGON ZM ((0 0 1 0, 1 0 1 1, 1 1 1 2, 0 0 1 0))")]
    [InlineData("MULTIPOINT M ((1 2 3), (4 5 6))")]
    [InlineData("MULTILINESTRING ZM ((0 0 1 2, 1 1 3 4), (2 2 5 6, 3 3 7 8))")]
    [InlineData("MULTIPOLYGON M (((0 0 1, 1 0 2, 1 1 3, 0 0 1)))")]
    [InlineData("GEOMETRYCOLLECTION ZM (POINT ZM (1 2 3 4), LINESTRING ZM (0 0 0 0, 1 1 1 1))")]
    public void Format_Measures_RoundTrip(string wkt)
    {
        var geometry = WellKnownText.Parse(wkt);
        Assert.All(geometry.GetPositions(), position => Assert.NotNull(position.Measure));
        Assert.Equal(wkt, WellKnownText.Format(geometry));
        Assert.Equal(geometry.GetPositions(), WellKnownText.Parse(WellKnownText.Format(geometry)).GetPositions());
    }

    [Fact]
    public void Format_Measures()
    {
        // As for the altitudes, a geometry has a single number of dimensions: a missing measure is written as 0, and the times are not written
        var line = new GeoLineString([new GeoPosition(0, 0) { Measure = 5 }, new GeoPosition(1, 1, 2) { Time = DateTimeOffset.UnixEpoch }]);
        Assert.Equal("LINESTRING ZM (0 0 0 5, 1 1 2 0)", WellKnownText.Format(line));
    }

    [Fact]
    public void Parse_EmptyPoint()
    {
        Assert.Empty(Assert.IsType<GeoMultiPoint>(WellKnownText.Parse("POINT EMPTY")).Positions);
        var collection = Assert.IsType<GeoGeometryCollection>(WellKnownText.Parse("GEOMETRYCOLLECTION (POINT EMPTY, POINT (1 2))"));
        Assert.Empty(Assert.IsType<GeoMultiPoint>(collection.Geometries[0]).Positions);
        Assert.IsType<GeoPoint>(collection.Geometries[1]);
    }

    [Fact]
    public void Parse_ExtendedWkt()
    {
        Assert.Equal(new GeoPosition(2.35, 48.86), Assert.IsType<GeoPoint>(WellKnownText.Parse("SRID=4326;POINT(2.35 48.86)")).Position);
        Assert.Equal([new GeoPosition(0, 0, 10), new(1, 1, 20)], Assert.IsType<GeoLineString>(WellKnownText.Parse(" srid = 3857 ; LINESTRING Z (0 0 10, 1 1 20)")).Positions);
    }

    [Theory]
    [InlineData("", "Expected a geometry type at position 0")]
    [InlineData("POINT", "Expected '(' but the text ends at position 5")]
    [InlineData("POINT (1 2", "Expected ')' but the text ends at position 10")]
    [InlineData("POINT (1 2))", "Unexpected text after the geometry at position 11")]
    [InlineData("POINT (1 2) POINT (3 4)", "Unexpected text after the geometry at position 12")]
    [InlineData("POINT (1)", "Expected a number at position 8")]
    [InlineData("POINT (1 a)", "Expected a number at position 9")]
    [InlineData("POINT (1 2 3 4 5)", "A position has more than 4 numbers at position 7")]
    [InlineData("POINT (1,2)", "Expected a number at position 8")]
    [InlineData("POINT (1-2 3)", "'1-2' is not a valid number at position 7")]
    [InlineData("POINT (1e999 2)", "'1e999' is not a valid number at position 7")]
    [InlineData("POINT Z (1 2)", "A position of a geometry tagged Z must have 3 numbers at position 9")]
    [InlineData("POINT ZM (1 2 3)", "A position of a geometry tagged ZM must have 4 numbers at position 10")]
    [InlineData("LINESTRING ((0 0, 1 1))", "Expected a number at position 12")]
    [InlineData("LINESTRING (0 0 1 1 1)", "A position has more than 4 numbers at position 12")]
    [InlineData("POLYGON (0 0, 1 1)", "Expected '(' but found '0' at position 9")]
    [InlineData("CIRCULARSTRING (0 0, 1 1, 2 0)", "'CIRCULARSTRING' is not a supported geometry type at position 0")]
    [InlineData("SRID=abc;POINT (1 2)", "Expected the SRID number at position 5")]
    [InlineData("SRID=4326 POINT (1 2)", "Expected ';' but found 'P' at position 10")]
    [InlineData("GEOMETRYCOLLECTION (POINT (1 2), )", "Expected a geometry type at position 33")]
    public void Parse_Invalid(string wkt, string message)
    {
        var exception = Assert.Throws<FormatException>(() => WellKnownText.Parse(wkt));
        Assert.StartsWith(message, exception.Message);
    }

    [Fact]
    public void Parse_NestingLimit()
    {
        var shallow = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION (", 50)) + "POINT (1 2)" + new string(')', 50);
        Assert.Equal([new GeoPosition(1, 2)], WellKnownText.Parse(shallow).GetPositions());
        var deep = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION (", 10_000)) + "POINT (1 2)" + new string(')', 10_000);
        Assert.Throws<FormatException>(() => WellKnownText.Parse(deep));
    }

    [Fact]
    public void Format_Altitudes()
    {
        // A geometry has a single number of dimensions: a missing altitude is written as 0
        Assert.Equal("LINESTRING Z (0 1 0, 2 3 4)", WellKnownText.Format(new GeoLineString([new(0, 1), new(2, 3, 4)])));
        Assert.Equal("GEOMETRYCOLLECTION Z (POINT Z (0 1 0), POINT Z (2 3 4))", WellKnownText.Format(new GeoGeometryCollection([new GeoPoint(new(0, 1)), new GeoPoint(new(2, 3, 4))])));
        Assert.Equal("POINT (0.1 -123456789.123)", WellKnownText.Format(new GeoPoint(new(0.1, -123456789.123))));
        Assert.Equal("POINT (1E+20 -0)", WellKnownText.Format(new GeoPoint(new(1e20, -0.0))));
    }

    [Fact]
    public void Format_RoundTripsNumbers()
    {
        var position = new GeoPosition(Math.PI, -Math.E, 1.0 / 3);
        Assert.Equal(position, Assert.IsType<GeoPoint>(WellKnownText.Parse(WellKnownText.Format(new GeoPoint(position)))).Position);
    }

    [Fact]
    public void Format_NotFinite()
    {
        Assert.Throws<ArgumentException>(() => WellKnownText.Format(new GeoPoint(new(double.NaN, 2))));
        Assert.Throws<ArgumentException>(() => WellKnownText.Format(new GeoLineString([new(1, 2, double.PositiveInfinity)])));
        Assert.Throws<ArgumentNullException>(() => WellKnownText.Format(null!));
        Assert.Throws<ArgumentNullException>(() => WellKnownText.Parse(null!));
    }

    [Fact]
    public void Parse_Example()
    {
        #region Geo_WellKnownTextParse
        // A column of a CSV export, or the result of ST_AsText in a database
        var geometry = WellKnownText.Parse("POLYGON ((2.25 48.82, 2.42 48.82, 2.42 48.90, 2.25 48.90, 2.25 48.82))");

        var polygon = Assert.IsType<GeoPolygon>(geometry);
        Assert.True(polygon.Contains(new GeoPosition(2.35, 48.86)));
        Assert.Equal(new GeoBounds(2.25, 48.82, 2.42, 48.90), geometry.GetBounds());
        #endregion
    }

    [Fact]
    public void Format_Example()
    {
        #region Geo_WellKnownTextFormat
        var route = new GeoLineString([new GeoPosition(2.35, 48.86, 35), new GeoPosition(4.84, 45.76, 170)]);

        var wkt = WellKnownText.Format(route);

        Assert.Equal("LINESTRING Z (2.35 48.86 35, 4.84 45.76 170)", wkt);
        #endregion
    }
}
