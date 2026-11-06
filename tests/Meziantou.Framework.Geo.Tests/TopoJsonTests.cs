using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Tests;

public sealed class TopoJsonTests
{
    // Two objects; the arcs 0 and 1 are shared, reversed, and stitched; the arc 2 has altitudes
    private const string Sample = """
        {"type":"Topology","bbox":[0,0,2,2],"objects":{
          "areas":{"type":"GeometryCollection","geometries":[
            {"type":"Polygon","id":"a","properties":{"name":"A","value":1.5,"flag":true,"none":null,"list":[1,2]},"arcs":[[0,1]]},
            {"type":"Polygon","id":12,"arcs":[[-1,3]]},
            {"type":"MultiPolygon","arcs":[[[0,1]],[[-1,3],[4]]]},
            {"type":"LineString","arcs":[2]},
            {"type":"MultiLineString","arcs":[[0],[-3]]},
            {"type":"Point","coordinates":[5,6,7]},
            {"type":"MultiPoint","coordinates":[[1,2],[3,4]]},
            {"type":"GeometryCollection","id":"nested","geometries":[{"type":"Point","coordinates":[1,1]},{"type":null},{"type":"LineString","arcs":[0]}]},
            {"type":null,"id":"empty","properties":{"name":"Nowhere"}}
          ]},
          "border":{"type":"LineString","arcs":[0],"properties":{"name":"Border"}}
        },"arcs":[
          [[1,0],[1,1],[1,2]],
          [[1,2],[0,2],[0,0],[1,0]],
          [[0,0,10],[2,2,20.5,99]],
          [[1,0],[2,0],[2,2],[1,2]],
          [[1.5,0.5],[1.5,1],[1.75,1],[1.5,0.5]]
        ]}
        """;

    [Fact]
    public void ParseObjects_AllTypes()
    {
        var objects = TopoJson.ParseObjects(Sample);
        Assert.Equal(["areas", "border"], objects.Keys);
        var areas = objects["areas"];
        Assert.Equal("areas", areas.Name);
        Assert.HasCount(9, areas);
        Assert.Equal("a", areas[0].Id);
        Assert.Equal("A", areas[0].Properties["name"]);
        Assert.Equal(1.5, areas[0].Properties["value"]);
        Assert.Equal(true, areas[0].Properties["flag"]);
        Assert.Null(areas[0].Properties["none"]);
        Assert.Equal("[1,2]", ((JsonElement)areas[0].Properties["list"]!).GetRawText());
        GeoPosition[] west = [new(1, 0), new(1, 1), new(1, 2), new(0, 2), new(0, 0), new(1, 0)];
        GeoPosition[] east = [new(1, 2), new(1, 1), new(1, 0), new(2, 0), new(2, 2), new(1, 2)];
        Assert.Equal(west, Assert.Single(Assert.IsType<GeoPolygon>(areas[0].Geometry).Rings));
        Assert.Equal("12", areas[1].Id);
        Assert.Empty(areas[1].Properties);
        Assert.Equal(east, Assert.Single(Assert.IsType<GeoPolygon>(areas[1].Geometry).Rings));
        var multiPolygon = Assert.IsType<GeoMultiPolygon>(areas[2].Geometry);
        Assert.Equal(west, Assert.Single(multiPolygon.Polygons[0].Rings));
        Assert.Equal(east, multiPolygon.Polygons[1].Rings[0]);
        Assert.Equal([new GeoPosition(1.5, 0.5), new(1.5, 1), new(1.75, 1), new(1.5, 0.5)], multiPolygon.Polygons[1].Rings[1]);
        Assert.Equal([new GeoPosition(0, 0, 10), new(2, 2, 20.5)], Assert.IsType<GeoLineString>(areas[3].Geometry).Positions);
        var multiLineString = Assert.IsType<GeoMultiLineString>(areas[4].Geometry);
        Assert.Equal([new GeoPosition(1, 0), new(1, 1), new(1, 2)], multiLineString.LineStrings[0].Positions);
        Assert.Equal([new GeoPosition(2, 2, 20.5), new(0, 0, 10)], multiLineString.LineStrings[1].Positions);
        Assert.Equal(new GeoPosition(5, 6, 7), Assert.IsType<GeoPoint>(areas[5].Geometry).Position);
        Assert.Equal([new GeoPosition(1, 2), new(3, 4)], Assert.IsType<GeoMultiPoint>(areas[6].Geometry).Positions);
        Assert.Equal("nested", areas[7].Id);
        var collection = Assert.IsType<GeoGeometryCollection>(areas[7].Geometry);
        Assert.Equal([GeoGeometryType.Point, GeoGeometryType.LineString], collection.Geometries.Select(geometry => geometry.Type));
        Assert.Equal("empty", areas[8].Id);
        Assert.Null(areas[8].Geometry);
        Assert.Equal("Nowhere", areas[8].Properties["name"]);
        var border = Assert.Single(objects["border"]);
        Assert.Equal("Border", border.Properties["name"]);
        Assert.Equal(GeoGeometryType.LineString, border.Geometry!.Type);
    }

    [Fact]
    public void Parse_ObjectsAndStream()
    {
        var all = TopoJson.Parse(Sample);
        Assert.Null(all.Name);
        Assert.HasCount(10, all);
        Assert.Equal("Border", all[9].Properties["name"]);

        var border = TopoJson.Parse(Sample, "border");
        Assert.Equal("border", border.Name);
        Assert.Single(border);
        Assert.Equal("areas", TopoJson.Parse("""{"type":"Topology","objects":{"areas":{"type":"GeometryCollection","geometries":[]}},"arcs":[]}""").Name);
        Assert.Empty(TopoJson.Parse("""{"type":"Topology","objects":{}}"""));

        var exception = Assert.Throws<ArgumentException>(() => TopoJson.Parse(Sample, "unknown"));
        Assert.Equal("objectName", exception.ParamName);

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(Sample)))
        {
            Assert.HasCount(10, TopoJson.Read(stream));
        }

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(Sample)))
        {
            Assert.HasCount(9, TopoJson.Read(stream, "areas"));
        }

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(Sample)))
        {
            Assert.HasCount(2, TopoJson.ReadObjects(stream));
        }
    }

    [Fact]
    public void Parse_Transform()
    {
        // The arcs are delta-encoded, the points are not, and the third value is never transformed
        var features = TopoJson.Parse("""
            {"type":"Topology","transform":{"scale":[0.5,0.25],"translate":[10,20]},"objects":{"shapes":{"type":"GeometryCollection","geometries":[
              {"type":"LineString","arcs":[0,-2]},
              {"type":"Point","coordinates":[4,4,100]},
              {"type":"MultiPoint","coordinates":[[2,2],[2,2]]}
            ]}},"arcs":[[[0,0,1],[2,4,2],[2,0,3]],[[6,4],[-2,0]]]}
            """);
        Assert.Equal([new GeoPosition(10, 20, 1), new(11, 21, 2), new(12, 21), new(13, 21)], Assert.IsType<GeoLineString>(features[0].Geometry).Positions);
        Assert.Equal(new GeoPosition(12, 21, 100), Assert.IsType<GeoPoint>(features[1].Geometry).Position);
        Assert.Equal([new GeoPosition(11, 20.5), new(11, 20.5)], Assert.IsType<GeoMultiPoint>(features[2].Geometry).Positions);
    }

    // The reference files: regions.topojson and regions-quantized.topojson are written by topojson-server 3.0.1 from regions.geojson
    // ("geo2topo regions=regions.geojson" and "geo2topo -q 10000 regions=regions.geojson"), and the *.gdal.geojson files by GDAL 3.10.3
    // ("ogr2ogr -f GeoJSON -lco RFC7946=NO -lco COORDINATE_PRECISION=15 regions.gdal.geojson regions.topojson").
    [Theory]
    [InlineData("regions.topojson")]
    [InlineData("regions-quantized.topojson")]
    public void Read_TopojsonServerFiles_AsGdal(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "topojson", fileName);
        GeoFeatureCollection features;
        using (var stream = File.OpenRead(path))
        {
            features = TopoJson.Read(stream);
        }

        Assert.Equal("regions", features.Name);
        Assert.HasCount(8, features);
        Assert.Equal(["west", "east", "7", null, null, null, null, null], features.Select(feature => feature.Id));
        Assert.Equal(980.0, features[1].Properties["population"]);
        Assert.Equal(false, features[1].Properties["capital"]);
        Assert.Null(features[7].Geometry);

        // GDAL skips the features without geometry, and moves the identifiers to the properties
        var gdal = GeoJson.Parse(File.ReadAllText(Path.ChangeExtension(path, ".gdal.geojson")));
        var withGeometry = features.Where(feature => feature.Geometry is not null).ToList();
        Assert.HasCount(gdal.Count, withGeometry);
        for (var i = 0; i < gdal.Count; i++)
        {
            Assert.Equal(gdal[i].Properties["name"], withGeometry[i].Properties["name"]);
            AssertSameGeometry(gdal[i].Geometry!, withGeometry[i].Geometry!, tolerance: 1e-9);
        }
    }

    [Fact]
    public void Read_TopojsonServerFile_AsSource()
    {
        // Without quantization, topojson-server keeps the positions, but starts some rings at another position
        var source = GeoJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "topojson", "regions.geojson")));
        var features = TopoJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "topojson", "regions.topojson")));
        for (var i = 0; i < source.Count; i++)
        {
            Assert.Equal(source[i].Geometry?.Type, features[i].Geometry?.Type);
            Assert.Equal(SortedPositions(source[i].Geometry), SortedPositions(features[i].Geometry));
        }
    }

    private static List<GeoPosition> SortedPositions(GeoGeometry? geometry) => [.. (geometry?.GetPositions() ?? []).Distinct().OrderBy(position => position.Longitude).ThenBy(position => position.Latitude)];

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"type":"FeatureCollection","features":[]}""")]
    [InlineData("""{"type":"Topology"}""")]
    [InlineData("""{"type":"Topology","objects":[]}""")]
    [InlineData("""{"type":"Topology","objects":{},"arcs":{}}""")]
    [InlineData("""{"type":"Topology","objects":{},"transform":{"scale":[1,1]}}""")]
    [InlineData("""{"type":"Topology","objects":{},"transform":{"scale":[1],"translate":[0,0]}}""")]
    [InlineData("""{"type":"Topology","objects":{"a":1}}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":1}}}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"Circle"}}}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"GeometryCollection"}}}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"Point"}}}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"Point","coordinates":[1]}}}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"LineString","arcs":[0]}},"arcs":[]}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"LineString","arcs":[-2]}},"arcs":[[[0,0],[1,1]]]}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"LineString","arcs":[0.5]}},"arcs":[[[0,0],[1,1]]]}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"LineString","arcs":[0]}},"arcs":[[[0,"a"]]]}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"LineString","arcs":[0]}},"arcs":[1]}""")]
    [InlineData("""{"type":"Topology","objects":{"a":{"type":"Polygon","arcs":[0]}},"arcs":[[[0,0],[1,1]]]}""")]
    public void Parse_Invalid(string json)
    {
        Assert.Throws<FormatException>(() => TopoJson.Parse(json));
        Assert.Throws<FormatException>(() => TopoJson.ParseObjects(json));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<FormatException>(() => TopoJson.Read(stream));
    }

    [Fact]
    public void Parse_UnpairedSurrogate()
    {
        // The text cannot be encoded as UTF-8 to be parsed
        var json = "{\"type\":\"Topology\",\"objects\":{\"" + (char)0xD800 + "\":{\"type\":\"GeometryCollection\",\"geometries\":[]}},\"arcs\":[]}";
        Assert.Throws<FormatException>(() => TopoJson.Parse(json));
        Assert.Throws<FormatException>(() => TopoJson.ParseObjects(json));
    }

    [Fact]
    public void Format_SharedBorder()
    {
        GeoFeatureCollection features =
        [
            new GeoFeature(new GeoPolygon([[new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0, 0)]]), id: "a"),
            new GeoFeature(new GeoPolygon([[new(1, 0), new(2, 0), new(2, 1), new(1, 1), new(1, 0)]]), id: "b"),
        ];

        // The border of the two squares, from (1,0) to (1,1), is the arc 1, reversed in "b"
        Assert.Equal(
            """{"type":"Topology","bbox":[0,0,2,1],"objects":{"features":{"type":"GeometryCollection","geometries":[{"type":"Polygon","arcs":[[0,1,2]],"id":"a"},{"type":"Polygon","arcs":[[3,-2]],"id":"b"}]}},"arcs":[[[0,0],[1,0]],[[1,0],[1,1]],[[1,1],[0,1],[0,0]],[[1,0],[2,0],[2,1],[1,1]]]}""",
            TopoJson.Format(features));
        Assert.Equal(
            """{"type":"Topology","bbox":[0,0,2,1],"transform":{"scale":[1,0.5],"translate":[0,0]},"objects":{"features":{"type":"GeometryCollection","geometries":[{"type":"Polygon","arcs":[[0,1,2]],"id":"a"},{"type":"Polygon","arcs":[[3,-2]],"id":"b"}]}},"arcs":[[[0,0],[1,0]],[[1,0],[0,2]],[[1,2],[-1,0],[0,-2]],[[1,0],[1,0],[0,2],[-1,0]]]}""",
            TopoJson.Format(features, new TopoJsonWriterOptions { Quantization = 3 }));
        AssertSameFeatures(features, TopoJson.Parse(TopoJson.Format(features, new TopoJsonWriterOptions { Quantization = 3 })), tolerance: 0);
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var objects = TopoJson.ParseObjects(Sample);
        var json = TopoJson.FormatObjects(objects);
        var copy = TopoJson.ParseObjects(json);
        Assert.Equal(json, TopoJson.FormatObjects(copy));
        Assert.Equal(objects.Keys, copy.Keys);
        foreach (var (name, features) in objects)
        {
            AssertSameFeatures(features, copy[name], tolerance: 0);
        }

        Assert.Equal("12", copy["areas"][1].Id);
        Assert.Equal("[1,2]", ((JsonElement)copy["areas"][0].Properties["list"]!).GetRawText());
        Assert.Equal(true, copy["areas"][0].Properties["flag"]);
        Assert.Null(copy["areas"][8].Geometry);

        // The arcs shared by the objects are written once
        using var document = JsonDocument.Parse(json);
        Assert.Equal(5, document.RootElement.GetProperty("arcs").GetArrayLength());
    }

    [Fact]
    public void Format_Map()
    {
        // A map of 20x10 countries whose borders zigzag, as a GeoJSON file of 200 polygons, and the outline of the land
        var countries = new GeoFeatureCollection { Name = "countries" };
        for (var x = 0; x < 20; x++)
        {
            for (var y = 0; y < 10; y++)
            {
                var ring = new List<GeoPosition>();
                ring.AddRange(Edge(new(x, y), new(x + 1, y)));
                ring.AddRange(Edge(new(x + 1, y), new(x + 1, y + 1)));
                ring.AddRange(Edge(new(x + 1, y + 1), new(x, y + 1)));
                ring.AddRange(Edge(new(x, y + 1), new(x, y)));
                ring.Add(ring[0]);
                countries.Add(new GeoFeature(new GeoPolygon([ring]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = $"{x}-{y}" }, id: $"{x * 10 + y}"));
            }
        }

        var outline = new List<GeoPosition>();
        for (var x = 0; x < 20; x++)
        {
            outline.AddRange(Edge(new(x, 0), new(x + 1, 0)));
        }

        for (var y = 0; y < 10; y++)
        {
            outline.AddRange(Edge(new(20, y), new(20, y + 1)));
        }

        for (var x = 20; x > 0; x--)
        {
            outline.AddRange(Edge(new(x, 10), new(x - 1, 10)));
        }

        for (var y = 10; y > 0; y--)
        {
            outline.AddRange(Edge(new(0, y), new(0, y - 1)));
        }

        outline.Add(outline[0]);
        var land = new GeoFeatureCollection([new GeoFeature(new GeoMultiPolygon([new GeoPolygon([outline])]))]);
        var objects = new Dictionary<string, GeoFeatureCollection>(StringComparer.Ordinal) { ["countries"] = countries, ["land"] = land };

        var topoJson = TopoJson.FormatObjects(objects);
        var copy = TopoJson.ParseObjects(topoJson);
        AssertSameFeatures(countries, copy["countries"], tolerance: 0);
        AssertSameFeatures(land, copy["land"], tolerance: 0);

        // Each border between two countries is stored once, and the land reuses the borders of the coast
        var geoJsonLength = GeoJson.Format(countries).Length + GeoJson.Format(land).Length;
        Assert.True(topoJson.Length < geoJsonLength * 0.6, $"{topoJson.Length} / {geoJsonLength}");
        using (var document = JsonDocument.Parse(topoJson))
        {
            // The sides of the cells, but 3 corners of the map are not junctions: their 2 sides are 1 arc
            var sides = (20 * 11) + (21 * 10);
            Assert.Equal(sides - 3, document.RootElement.GetProperty("arcs").GetArrayLength());
        }

        var quantized = TopoJson.FormatObjects(objects, new TopoJsonWriterOptions { Quantization = 10_000 });
        Assert.True(quantized.Length < topoJson.Length * 0.6, $"{quantized.Length} / {topoJson.Length}");
        AssertSameFeatures(countries, TopoJson.Parse(quantized, "countries"), tolerance: 0.002);

        // A side of a cell, without its last corner: 3 positions between the corners, moved away from the straight line,
        // the same whatever the direction
        static IEnumerable<GeoPosition> Edge(GeoPosition from, GeoPosition to)
        {
            var reversed = (from.Longitude, from.Latitude).CompareTo((to.Longitude, to.Latitude)) > 0;
            var (a, b) = reversed ? (to, from) : (from, to);
            var positions = new List<GeoPosition>();
            for (var i = 0; i <= 4; i++)
            {
                var t = i / 4.0;
                var offset = i is 0 or 4 ? 0 : 0.05 * Math.Sin(((a.Longitude * 3) + (a.Latitude * 7) + b.Longitude + b.Latitude + i) * 1.7);
                positions.Add(new GeoPosition(a.Longitude + ((b.Longitude - a.Longitude) * t) + offset, a.Latitude + ((b.Latitude - a.Latitude) * t) + offset));
            }

            if (reversed)
            {
                positions.Reverse();
            }

            return positions.Take(4);
        }
    }

    [Fact]
    public void Format_Values()
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["int"] = 3,
            ["long"] = long.MaxValue,
            ["decimal"] = 1.10m,
            ["nan"] = double.NaN,
            ["date"] = new DateOnly(2024, 2, 29),
            ["list"] = new[] { 1, 2 },
            ["object"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "b" },
            ["text"] = "Côte d'Ivoire <&>",
        };
        var json = TopoJson.Format([new GeoFeature(geometry: null, properties)]);
        Assert.Equal("""{"type":"Topology","objects":{"features":{"type":"GeometryCollection","geometries":[{"type":null,"properties":{"int":3,"long":9223372036854775807,"decimal":1.10,"nan":null,"date":"2024-02-29","list":[1,2],"object":{"a":"b"},"text":"Côte d'Ivoire <&>"}}]}},"arcs":[]}""", json);
    }

    [Fact]
    public void Format_EdgeCases()
    {
        GeoFeatureCollection features =
        [
            new GeoFeature(new GeoPolygon([[new(0, 0), new(1, 0), new(1, 1)]])),
            new GeoFeature(new GeoLineString([new(5, 5)])),
            new GeoFeature(new GeoLineString([])),
            new GeoFeature(new GeoPolygon([[new(0, 0), new(0, 0)]])),
            new GeoFeature(new GeoPoint(new(1, 2, 3))),
            new GeoFeature(new GeoLineString([new(0, 0), new(0.0001, 0), new(1, 1)])),
        ];
        var copy = TopoJson.Parse(TopoJson.Format(features));

        // The rings are closed
        Assert.Equal([new GeoPosition(0, 0), new(1, 0), new(1, 1), new(0, 0)], Assert.IsType<GeoPolygon>(copy[0].Geometry).Rings[0]);
        Assert.Equal([new GeoPosition(5, 5)], Assert.IsType<GeoLineString>(copy[1].Geometry).Positions);
        Assert.Empty(Assert.IsType<GeoLineString>(copy[2].Geometry).Positions);
        Assert.Equal([new GeoPosition(0, 0), new(0, 0)], Assert.IsType<GeoPolygon>(copy[3].Geometry).Rings[0]);
        Assert.Equal(new GeoPosition(1, 2, 3), Assert.IsType<GeoPoint>(copy[4].Geometry).Position);

        // The positions that round to the same value are merged
        var quantized = TopoJson.Parse(TopoJson.Format(features, new TopoJsonWriterOptions { Quantization = 6 }));
        Assert.Equal([new GeoPosition(0, 0), new(1, 1)], Assert.IsType<GeoLineString>(quantized[5].Geometry).Positions);
        Assert.Equal(new GeoPosition(1, 2, 3), Assert.IsType<GeoPoint>(quantized[4].Geometry).Position);

        Assert.Equal("""{"type":"Topology","objects":{"features":{"type":"GeometryCollection","geometries":[]}},"arcs":[]}""", TopoJson.Format([], new TopoJsonWriterOptions { Quantization = 100 }));
        Assert.Contains("\"named\":", TopoJson.Format(new GeoFeatureCollection { Name = "named" }));
        Assert.Contains("\n", TopoJson.Format(features, new TopoJsonWriterOptions { Indented = true }));
    }

    [Fact]
    public void Format_Invalid()
    {
        Assert.Throws<ArgumentException>(() => TopoJson.Format([new GeoFeature(new GeoPoint(new(double.NaN, 2)))]));
        Assert.Throws<ArgumentException>(() => TopoJson.FormatObjects(new Dictionary<string, GeoFeatureCollection>(StringComparer.Ordinal) { ["a"] = [new GeoFeature(new GeoPoint(new(1, 2, double.PositiveInfinity)))] }));
        Assert.Throws<ArgumentException>(() => TopoJson.FormatObjects(new Dictionary<string, GeoFeatureCollection>(StringComparer.Ordinal) { ["a"] = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TopoJsonWriterOptions { Quantization = 1 });
    }

    [Fact]
    public void ReadAndWrite_Example()
    {
        File.WriteAllText("countries-110m.json", """
            {"type":"Topology","transform":{"scale":[0.001,0.001],"translate":[-10,36]},
             "objects":{"countries":{"type":"GeometryCollection","geometries":[
               {"type":"Polygon","arcs":[[0,1]],"id":"250","properties":{"name":"France"}},
               {"type":"Polygon","arcs":[[-1,2]],"id":"724","properties":{"name":"Spain"}}]}},
             "arcs":[
               [[8000,7000],[4000,-1000]],
               [[12000,6000],[0,6000],[-6000,0],[2000,-5000]],
               [[8000,7000],[-8000,-500],[0,-6500],[13000,0],[-1000,6000]]]}
            """);

        #region Geo_TopoJsonRead
        // A file of world-atlas: its "countries" object is a collection of polygons that share their borders
        GeoFeatureCollection countries;
        using (var stream = File.OpenRead("countries-110m.json"))
        {
            countries = TopoJson.Read(stream, "countries");
        }

        var france = countries.Single(country => country.Id == "250");
        Assert.Equal("France", france.Properties["name"]);
        Assert.True(Assert.IsType<GeoPolygon>(france.Geometry).Contains(new GeoPosition(-1.5, 43)));
        #endregion

        #region Geo_TopoJsonWrite
        // The border of France and Spain is written once, and the positions are rounded to a grid of 10,000x10,000
        var options = new TopoJsonWriterOptions { Quantization = 10_000 };
        using (var stream = File.Create("countries.topojson"))
        {
            TopoJson.Write(countries, stream, options);
        }

        var copy = TopoJson.Parse(File.ReadAllText("countries.topojson"), "countries");
        Assert.Equal(["France", "Spain"], copy.Select(country => country.Properties["name"]));
        #endregion
    }

    private static void AssertSameFeatures(GeoFeatureCollection expected, GeoFeatureCollection actual, double tolerance)
    {
        Assert.HasCount(expected.Count, actual);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Id, actual[i].Id);
            Assert.Equal(expected[i].Properties.Keys, actual[i].Properties.Keys);
            if (expected[i].Geometry is null)
            {
                Assert.Null(actual[i].Geometry);
            }
            else
            {
                AssertSameGeometry(expected[i].Geometry!, actual[i].Geometry!, tolerance);
            }
        }
    }

    private static void AssertSameGeometry(GeoGeometry expected, GeoGeometry actual, double tolerance)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.GetSimpleGeometries().Select(Shape), actual.GetSimpleGeometries().Select(Shape));
        var expectedPositions = expected.GetPositions().ToList();
        var actualPositions = actual.GetPositions().ToList();
        Assert.HasCount(expectedPositions.Count, actualPositions);
        for (var i = 0; i < expectedPositions.Count; i++)
        {
            Assert.True(Math.Abs(expectedPositions[i].Longitude - actualPositions[i].Longitude) <= tolerance && Math.Abs(expectedPositions[i].Latitude - actualPositions[i].Latitude) <= tolerance, $"{expectedPositions[i]} != {actualPositions[i]}");
            Assert.Equal(expectedPositions[i].Altitude, actualPositions[i].Altitude);
        }

        static string Shape(GeoGeometry geometry) => geometry switch
        {
            GeoPolygon polygon => "Polygon:" + string.Join(',', polygon.Rings.Select(ring => ring.Count)),
            GeoLineString lineString => "LineString:" + lineString.Positions.Count,
            _ => geometry.Type.ToString(),
        };
    }
}
