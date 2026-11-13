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

#if NET11_0_OR_GREATER
    [Fact]
    public void Format_Ieee754Values()
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["bfloat16"] = (System.Numerics.BFloat16)0.5f,
            ["decimal32"] = System.Numerics.Decimal32.Parse("1.10", CultureInfo.InvariantCulture),
            ["decimal64"] = System.Numerics.Decimal64.Parse("1E+20", CultureInfo.InvariantCulture),
            ["decimal128"] = System.Numerics.Decimal128.Parse("0.1000000000000000000000000000000001", CultureInfo.InvariantCulture),
            ["nan"] = System.Numerics.Decimal64.NaN,
        };

        var json = GeoJson.Format([new GeoFeature(geometry: null, properties)]);

        Assert.Equal("""{"type":"FeatureCollection","features":[{"type":"Feature","geometry":null,"properties":{"bfloat16":0.5,"decimal32":1.10,"decimal64":1E+20,"decimal128":0.1000000000000000000000000000000001,"nan":null}}]}""", json);
        Assert.Equal(1e20, GeoJson.Parse(json)[0].Properties["decimal64"]);
    }
#endif

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

    [Fact]
    public void Parse_UnpairedSurrogate()
    {
        // UTF-8 cannot encode an unpaired surrogate, so JSON cannot represent it
        Assert.Throws<FormatException>(() => GeoJson.Parse("{\"type\":\"Feature\",\"properties\":{\"name\":\"\uD800\"}}"));
        Assert.Throws<FormatException>(() => GeoJson.ParseGeometry("{\"type\":\"\uDC00\"}"));

        // The same character, escaped in the JSON text
        Assert.Throws<FormatException>(() => GeoJson.Parse("""{"type":"Feature","properties":{"name":"\ud800"}}"""));
        Assert.Throws<FormatException>(() => GeoJson.Read(new MemoryStream(Encoding.UTF8.GetBytes("""{"type":"Point\udc00","coordinates":[1,2]}"""))));
        Assert.Throws<FormatException>(() => GeoJson.Parse("""{"type":"Feature","properties":{"list":[{"a":"\ud800"}]}}"""));
        Assert.Throws<FormatException>(() => GeoJson.Parse("""{"type":"Feature","properties":{"object":{"\udc00":1}}}"""));
    }

    [Fact]
    public void Parse_CoordinateTooLarge()
    {
        Assert.Throws<FormatException>(() => GeoJson.ParseGeometry("""{"type":"Point","coordinates":[1,1e999]}"""));
        Assert.Throws<FormatException>(() => GeoJson.ParseGeometry("""{"type":"Point","coordinates":[1,2,-1e999]}"""));

        // A property is not a coordinate: the writer writes null for it
        var feature = Assert.Single(GeoJson.Parse("""{"type":"Feature","properties":{"value":1e999}}"""));
        Assert.Equal(double.PositiveInfinity, feature.Properties["value"]);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    [InlineData(7)]
    public async Task ReadFeaturesAsync_AllTypes(int maxReadLength)
    {
        // Reading a few bytes at a time ends the buffer in the middle of every token and feature
        var expected = GeoJson.Parse(AllTypes);
        expected.Name = null;
        using var stream = new CountingStream(Encoding.UTF8.GetBytes(AllTypes), canSeek: false, maxReadLength);
        var features = await GeoJson.ReadFeaturesAsync(stream, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GeoJson.Format(expected), GeoJson.Format(new GeoFeatureCollection(features)));
        Assert.Equal("42", features[0].Id);
    }

    [Theory]
    [InlineData("""{"features":[{"type":"Feature","geometry":{"type":"Point","coordinates":[1,2]}}],"bbox":[1,2,1,2],"type":"FeatureCollection"}""")]
    [InlineData("""{"name":{"a":[1,{"b":2}]},"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"Point","coordinates":[1,2]}}],"other":[]}""")]
    [InlineData("""  {"type":"Feature","geometry":{"type":"Point","coordinates":[1,2]}}  """)]
    [InlineData("""{"geometry":{"type":"Point","coordinates":[1,2]},"type":"Feature","features":[1]}""")]
    [InlineData("""{"coordinates":[1,2],"type":"Point"}""")]
    [InlineData("""{"type":"Feature","name":"\ud800","geometry":{"type":"Point","coordinates":[1,2]}}""")]
    public async Task ReadFeaturesAsync_OneFeature(string json)
    {
        byte[][] documents = [Encoding.UTF8.GetBytes(json), [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(json)]];
        foreach (var document in documents)
        {
            foreach (var maxReadLength in new[] { 1, int.MaxValue })
            {
                using var stream = new CountingStream(document, canSeek: false, maxReadLength);
                var feature = Assert.Single(await GeoJson.ReadFeaturesAsync(stream, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
                Assert.Equal(new GeoPosition(1, 2), Assert.IsType<GeoPoint>(feature.Geometry).Position);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("""{"type":1}""")]
    [InlineData("""{"type":"FeatureCollection"}""")]
    [InlineData("""{"type":"FeatureCollection","features":[{"type":"Point","coordinates":[1,2]}]}""")]
    [InlineData("""{"type":"FeatureCollection","features":[1]}""")]
    [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","geometry":null}]""")]
    [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","geometry":null}]} 1""")]
    [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"name":"\ud800"}}]}""")]
    [InlineData("""{"features":[{"type":"Feature","geometry":null}]}""")]
    [InlineData("""{"features":[{"type":"Feature","geometry":null}],"type":"Feature"}""")]
    [InlineData("""{"type":"Point","coordinates":[1]}""")]
    [InlineData("""{"type":"FeatureCollection","name":"\ud800","features":[]}""")]
    [InlineData("""{"features":[],"name":"\ud800","type":"FeatureCollection"}""")]
    public async Task ReadFeaturesAsync_Invalid(string json)
    {
        using var stream = new CountingStream(Encoding.UTF8.GetBytes(json), canSeek: false);
        await Assert.ThrowsAsync<FormatException>(async () => await GeoJson.ReadFeaturesAsync(stream, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadFeaturesAsync_LargeDocument_ReadsAsTheFeaturesAreEnumerated()
    {
        // 200,000 features, about 20 MB, generated as the stream is read
        const int Count = 200_000;
        var feature = Encoding.UTF8.GetBytes("""{"type":"Feature","properties":{"name":"feature","value":42},"geometry":{"type":"Point","coordinates":[2.35,48.86]}}""");
        using var stream = new RepeatedRecordStream(
            [.. """{"type":"FeatureCollection","name":"large","features":["""u8, .. feature],
            [(byte)',', .. feature],
            Count - 1,
            [.. "]}"u8]);

        var index = 0;
        await foreach (var item in GeoJson.ReadFeaturesAsync(stream, TestContext.Current.CancellationToken))
        {
            // When a feature is returned, the reader is at most a buffer ahead of it: it does not read the whole document first
            Assert.True(stream.Position < ((index + 1L) * (feature.Length + 1)) + (1024 * 1024), $"Feature {index} was returned after reading {stream.Position} bytes");
            Assert.Equal("feature", item.Properties["name"]);
            index++;
        }

        Assert.Equal(Count, index);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task Documentation_ReadFeaturesAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".geojson");
        await File.WriteAllTextAsync(path, AllTypes, TestContext.Current.CancellationToken);
        try
        {
            #region Geo_GeoJsonReadFeaturesAsync
            // The features are read one at a time: a large export is processed without loading it in memory
            await using var stream = File.OpenRead(path);
            var points = 0;
            await foreach (var feature in GeoJson.ReadFeaturesAsync(stream))
            {
                if (feature.Geometry is GeoPoint)
                {
                    points++;
                }
            }

            Assert.Equal(1, points);
            #endregion
        }
        finally
        {
            File.Delete(path);
        }
    }
}
