using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Tests;

public sealed class GeoJsonTests
{
    private const string AllTypes = """
        {"type":"FeatureCollection","name":"sample","features":[
          {"type":"Feature","id":42,"properties":{"name":"A","value":1.5,"flag":true,"off":false,"none":null,"list":[1,2]},"geometry":{"type":"Point","coordinates":[1,2,300]}},
          {"type":"Feature","id":"b","geometry":{"type":"MultiPoint","coordinates":[[1,2],[3,4]]}},
          {"type":"Feature","id":true,"properties":null,"geometry":{"type":"LineString","coordinates":[[0,0],[1,1]]}},
          {"type":"Feature","geometry":{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3]]]}},
          {"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[0,0],[4,0],[4,4],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}},
          {"type":"Feature","geometry":{"type":"MultiPolygon","coordinates":[[[[0,0],[1,0],[1,1],[0,0]]],[[[5,5],[6,5],[6,6],[5,5]]]]}},
          {"type":"Feature","geometry":{"type":"GeometryCollection","geometries":[{"type":"Point","coordinates":[9,9]},{"type":"LineString","coordinates":[[0,0],[1,0]]}]}},
          {"type":"Feature","geometry":null}
        ]}
        """;

    [Fact]
    public void Parse_AllTypes()
    {
        var features = GeoJson.Parse(AllTypes);
        Assert.Equal("sample", features.Name);
        Assert.HasCount(8, features);
        Assert.Equal("42", features[0].Id);
        Assert.Equal("A", features[0].Properties["name"]);
        Assert.Equal(1.5, features[0].Properties["value"]);
        Assert.Equal(true, features[0].Properties["flag"]);
        Assert.Equal(false, features[0].Properties["off"]);
        Assert.Null(features[0].Properties["none"]);
        Assert.Equal("[1,2]", ((JsonElement)features[0].Properties["list"]!).GetRawText());
        Assert.Equal(new GeoPosition(1, 2, 300), Assert.IsType<GeoPoint>(features[0].Geometry).Position);
        Assert.Equal("b", features[1].Id);
        Assert.Equal([new GeoPosition(1, 2), new(3, 4)], Assert.IsType<GeoMultiPoint>(features[1].Geometry).Positions);
        Assert.Null(features[2].Id);
        Assert.Empty(features[2].Properties);
        Assert.Equal([new GeoPosition(0, 0), new(1, 1)], Assert.IsType<GeoLineString>(features[2].Geometry).Positions);
        Assert.HasCount(2, Assert.IsType<GeoMultiLineString>(features[3].Geometry).LineStrings);
        Assert.HasCount(2, Assert.IsType<GeoPolygon>(features[4].Geometry).Rings);
        Assert.HasCount(2, Assert.IsType<GeoMultiPolygon>(features[5].Geometry).Polygons);
        var collection = Assert.IsType<GeoGeometryCollection>(features[6].Geometry);
        Assert.Equal([GeoGeometryType.Point, GeoGeometryType.LineString], collection.Geometries.Select(geometry => geometry.Type));
        Assert.Null(features[7].Geometry);
    }

    [Fact]
    public void Parse_FeatureGeometryAndStream()
    {
        Assert.Equal("x", Assert.Single(GeoJson.Parse("""{"type":"Feature","id":"x","geometry":null}""")).Id);
        Assert.Equal("1.50", Assert.Single(GeoJson.Parse("""{"type":"Feature","id":1.50}""")).Id);
        Assert.Equal(new GeoPosition(3, 4), Assert.IsType<GeoPoint>(Assert.Single(GeoJson.Parse("""{"type":"Point","coordinates":[3,4]}""")).Geometry).Position);
        Assert.Equal(new GeoPosition(3, 4), Assert.IsType<GeoPoint>(GeoJson.ParseGeometry("""{"type":"Point","coordinates":[3,4,"x",5]}""")).Position);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(AllTypes));
        Assert.HasCount(8, GeoJson.Read(stream));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"type":1}""")]
    [InlineData("""{"type":"Circle","coordinates":[]}""")]
    [InlineData("""{"type":"FeatureCollection"}""")]
    [InlineData("""{"type":"FeatureCollection","features":[{"type":"Point","coordinates":[1,2]}]}""")]
    [InlineData("""{"type":"Point"}""")]
    [InlineData("""{"type":"Point","coordinates":[]}""")]
    [InlineData("""{"type":"GeometryCollection"}""")]
    [InlineData("""{"type":"Point","coordinates":[1]}""")]
    [InlineData("""{"type":"Point","coordinates":["a","b"]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[1,2]}""")]
    public void Parse_Invalid(string json)
    {
        Assert.Throws<FormatException>(() => GeoJson.Parse(json));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<FormatException>(() => GeoJson.Read(stream));
    }

    [Fact]
    public void ParseGeometry_Invalid()
    {
        Assert.Throws<FormatException>(() => GeoJson.ParseGeometry("""{"type":"Feature","geometry":null}"""));
        Assert.Throws<FormatException>(() => GeoJson.ParseGeometry("{"));
    }

    [Fact]
    public void Parse_UnpairedSurrogate()
    {
        // The text cannot be encoded as UTF-8 to be parsed
        var surrogate = (char)0xD800;
        var exception = Assert.Throws<FormatException>(() => GeoJson.Parse("{\"type\":\"Feature\",\"id\":\"" + surrogate + "\",\"geometry\":null}"));
        Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Throws<FormatException>(() => GeoJson.ParseGeometry("{\"type\":\"Point\",\"coordinates\":[1,2]," + surrogate + "}"));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var features = GeoJson.Parse(AllTypes);
        var json = GeoJson.Format(features);
        var copy = GeoJson.Parse(json);
        Assert.Equal(json, GeoJson.Format(copy));
        Assert.Equal("sample", copy.Name);
        Assert.Equal(features.Select(feature => feature.Geometry?.Type), copy.Select(feature => feature.Geometry?.Type));
        Assert.Equal(features.SelectMany(feature => feature.Geometry?.GetPositions() ?? []), copy.SelectMany(feature => feature.Geometry?.GetPositions() ?? []));
        Assert.Equal("42", copy[0].Id);
        Assert.Equal("[1,2]", ((JsonElement)copy[0].Properties["list"]!).GetRawText());
    }

    [Fact]
    public void Format_Values()
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["int"] = 3,
            ["long"] = long.MaxValue,
            ["decimal"] = 1.10m,
            ["float"] = 0.5f,
            ["nan"] = double.NaN,
            ["date"] = new DateOnly(2024, 2, 29),
            ["time"] = new DateTimeOffset(2024, 2, 29, 13, 45, 0, TimeSpan.FromHours(1)),
            ["list"] = new[] { 1, 2 },
            ["object"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "b" },
            ["guid"] = Guid.Empty,
            ["text"] = "Côte d'Ivoire <&>",
        };
        var json = GeoJson.Format([new GeoFeature(geometry: null, properties)]);
        Assert.Equal("""{"type":"FeatureCollection","features":[{"type":"Feature","geometry":null,"properties":{"int":3,"long":9223372036854775807,"decimal":1.10,"float":0.5,"nan":null,"date":"2024-02-29","time":"2024-02-29T13:45:00.0000000+01:00","list":[1,2],"object":{"a":"b"},"guid":"00000000-0000-0000-0000-000000000000","text":"Côte d'Ivoire <&>"}}]}""", json);
    }

    [Fact]
    public void Format_Options()
    {
        GeoFeatureCollection features = [new GeoFeature(new GeoLineString([new(0, 1), new(2, 3, 4)]), id: "line")];
        Assert.Equal(
            """{"type":"FeatureCollection","bbox":[0,1,2,3],"features":[{"type":"Feature","id":"line","bbox":[0,1,2,3],"geometry":{"type":"LineString","bbox":[0,1,2,3],"coordinates":[[0,1],[2,3,4]]},"properties":{}}]}""",
            GeoJson.Format(features, new GeoJsonWriterOptions { WriteBoundingBoxes = true }));
        Assert.Contains("\n", GeoJson.Format(features, new GeoJsonWriterOptions { Indented = true }));
        Assert.Equal("""{"type":"Point","coordinates":[1,2]}""", GeoJson.FormatGeometry(new GeoPoint(new(1, 2))));
    }

    [Fact]
    public void Format_NotFinite()
    {
        Assert.Throws<ArgumentException>(() => GeoJson.FormatGeometry(new GeoPoint(new(double.NaN, 2))));
        Assert.Throws<ArgumentException>(() => GeoJson.Format([new GeoFeature(new GeoPoint(new(1, 2, double.PositiveInfinity)))]));
    }

    [Fact]
    public void ReadAndWrite_Example()
    {
        File.WriteAllText("countries.geojson", """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"name":"France","population":68.4},"geometry":{"type":"Point","coordinates":[2.35,48.86]}},
              {"type":"Feature","properties":{"name":"Spain","population":48.6},"geometry":{"type":"Point","coordinates":[-3.70,40.42]}}
            ]}
            """);

        #region Geo_GeoJsonReadWrite
        GeoFeatureCollection countries;
        using (var stream = File.OpenRead("countries.geojson"))
        {
            countries = GeoJson.Read(stream);
        }

        // A JSON number is a double, and a string is a string
        var large = countries.Where(country => (double)country.Properties["population"]! > 50).ToList();
        Assert.Equal("France", Assert.Single(large).Properties["name"]);

        using (var stream = File.Create("large-countries.geojson"))
        {
            GeoJson.Write(new GeoFeatureCollection(large), stream, new GeoJsonWriterOptions { Indented = true });
        }
        #endregion

        Assert.Single(GeoJson.Parse(File.ReadAllText("large-countries.geojson")));
    }
}
