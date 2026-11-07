using System.Text;

namespace Meziantou.Framework.Geo.Tests;

public sealed class GpxTests
{
    private static string GetTestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "gpx", fileName);

    private static GeoFeatureCollection ReadTestData(string fileName)
    {
        using var stream = File.OpenRead(GetTestDataPath(fileName));
        return Gpx.Read(stream);
    }

    [Fact]
    public void Read_Gdal()
    {
        // Written by GDAL 3.10.3 from a GeoJSON file split into a layer per geometry type (a VRT), with trailing spaces trimmed:
        // ogr2ogr -f GPX -dsco GPX_USE_EXTENSIONS=YES gdal.gpx in.vrt
        var features = ReadTestData("gdal.gpx");
        Assert.Null(features.Name);
        Assert.HasCount(4, features);

        Assert.Equal(new GeoPosition(2.2945, 48.8584), Assert.IsType<GeoPoint>(features[0].Geometry).Position);
        Assert.Equal(["gpxType", "time", "name", "desc", "sym"], features[0].Properties.Keys);
        Assert.Equal("wpt", features[0].Properties[Gpx.TypePropertyName]);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 30, 0, TimeSpan.Zero), features[0].Properties["time"]);
        Assert.Equal("Eiffel Tower", features[0].Properties["name"]);
        Assert.Equal("Paris, France", features[0].Properties["desc"]);
        Assert.Equal("Flag, Blue", features[0].Properties["sym"]);
        Assert.Null(features[0].Id);
        Assert.Equal("Museum", features[1].Properties["cmt"]);

        Assert.Equal("rte", features[2].Properties[Gpx.TypePropertyName]);
        Assert.Equal("Walk", features[2].Properties["name"]);
        Assert.Equal(1.0, features[2].Properties["number"]);
        Assert.Equal([new GeoPosition(2.2945, 48.8584, 35), new(2.3376, 48.8606, 40), new(2.3499, 48.853, 36)], Assert.IsType<GeoLineString>(features[2].Geometry).Positions);

        Assert.Equal("trk", features[3].Properties[Gpx.TypePropertyName]);
        Assert.Equal("running", features[3].Properties["type"]);
        var track = Assert.IsType<GeoMultiLineString>(features[3].Geometry);
        Assert.Equal([2, 3], track.LineStrings.Select(line => line.Positions.Count));
        Assert.Equal(new GeoPosition(2.33, 48.89, 34), track.LineStrings[1].Positions[2]);
    }

    [Fact]
    public void Read_Garmin()
    {
        var features = ReadTestData("garmin.gpx");
        Assert.Equal("Morning Ride", features.Name);
        Assert.HasCount(2, features);

        var waypoint = features[0];
        Assert.Equal(new GeoPosition(5.7245, 45.1885, 212.4), Assert.IsType<GeoPoint>(waypoint.Geometry).Position);
        Assert.Equal(["gpxType", "time", "name", "sym", "type"], waypoint.Properties.Keys);
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 6, 12, 3, TimeSpan.Zero), waypoint.Properties["time"]);
        Assert.Equal("Flag, Green", waypoint.Properties["sym"]);
        Assert.Equal("user", waypoint.Properties["type"]);

        // The extensions of the track and of its points (heart rate, cadence…) are ignored
        var track = features[1];
        Assert.Equal(["gpxType", "name", "type"], track.Properties.Keys);
        Assert.Equal("cycling", track.Properties["type"]);
        var segments = Assert.IsType<GeoMultiLineString>(track.Geometry).LineStrings;
        Assert.Equal([3, 2], segments.Select(segment => segment.Positions.Count));
        Assert.Equal(new GeoPosition(5.72449999954551458358764648437, 45.18850000016391277313232421875, 212.39999389648438), segments[0].Positions[0]);
        Assert.Equal(new GeoPosition(5.7322, 45.1931, 221.8), segments[1].Positions[1]);
    }

    [Fact]
    public void Read_Strava()
    {
        var features = ReadTestData("strava.gpx");
        Assert.Null(features.Name);
        var track = Assert.Single(features);
        Assert.Equal("Lunch Run", track.Properties["name"]);
        Assert.Equal("9", track.Properties["type"]);
        var segment = Assert.Single(Assert.IsType<GeoMultiLineString>(track.Geometry).LineStrings);
        Assert.Equal(new GeoPosition(-0.124625, 51.500729, 11.2), segment.Positions[0]);
        Assert.HasCount(4, segment.Positions);
    }

    [Fact]
    public void Read_Gpx10()
    {
        // The name is a child of the gpx element, the elements of other namespaces (topografix:color) are ignored, and the
        // encoding comes from the XML declaration
        var features = ReadTestData("gpx10.gpx");
        Assert.Equal("Boston Freedom Trail", features.Name);
        Assert.Equal(["wpt", "wpt", "rte", "trk"], features.Select(feature => feature.Properties[Gpx.TypePropertyName]));
        Assert.Equal(["gpxType", "time", "name", "cmt", "desc", "sym", "type"], features[0].Properties.Keys);
        Assert.Equal(new GeoPosition(-71.063353, 42.355001, 12.192), Assert.IsType<GeoPoint>(features[0].Geometry).Position);
        Assert.Equal("Park", features[0].Properties["sym"]);
        Assert.Equal(["gpxType", "name", "desc", "number"], features[2].Properties.Keys);
        Assert.Equal(1.0, features[2].Properties["number"]);
        Assert.Equal([new GeoPosition(-71.063353, 42.355001, 12.192), new(-71.0622, 42.3578, 16.764), new(-71.06027, 42.35834)], Assert.IsType<GeoLineString>(features[2].Geometry).Positions);
        Assert.Equal(2.0, features[3].Properties["number"]);
        Assert.HasCount(2, Assert.Single(Assert.IsType<GeoMultiLineString>(features[3].Geometry).LineStrings).Positions);
    }

    [Fact]
    public void Parse_EdgeCases()
    {
        var features = Gpx.Parse("""
            <!DOCTYPE gpx>
            <gpx><!-- no namespace -->
              <wpt lat="-90" lon="180"><ele> </ele><time>2024-01-02T03:04:05</time><name><![CDATA[A & B]]></name></wpt>
              <wpt lat="1e1" lon="-0.5"/>
              <rte/>
              <trk><trkseg/><trkseg><trkpt lat="1" lon="2"><name>ignored</name></trkpt></trkseg></trk>
              <unknown><wpt lat="5" lon="5"/></unknown>
            </gpx>
            """);
        Assert.HasCount(4, features);
        Assert.Equal(new GeoPosition(180, -90), Assert.IsType<GeoPoint>(features[0].Geometry).Position);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), features[0].Properties["time"]);
        Assert.Equal("A & B", features[0].Properties["name"]);
        Assert.Equal(new GeoPosition(-0.5, 10), Assert.IsType<GeoPoint>(features[1].Geometry).Position);
        Assert.Empty(Assert.IsType<GeoLineString>(features[2].Geometry).Positions);
        Assert.Equal([0, 1], Assert.IsType<GeoMultiLineString>(features[3].Geometry).LineStrings.Select(line => line.Positions.Count));
        Assert.Equal(["gpxType"], features[3].Properties.Keys);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), Gpx.Parse("""<gpx><wpt lat="1" lon="2"><time>2024-01-02T03:04:05+02:00</time></wpt></gpx>""")[0].Properties["time"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<gpx>")]
    [InlineData("<kml/>")]
    [InlineData("<gpx><wpt lon='1'/></gpx>")]
    [InlineData("<gpx><wpt lat='1'/></gpx>")]
    [InlineData("<gpx><wpt lat='a' lon='1'/></gpx>")]
    [InlineData("<gpx><wpt lat='90.5' lon='1'/></gpx>")]
    [InlineData("<gpx><wpt lat='1' lon='NaN'/></gpx>")]
    [InlineData("<gpx><rte><rtept lat='1' lon='-181'/></rte></gpx>")]
    [InlineData("<gpx><trk><trkseg><trkpt lat='1'/></trkseg></trk></gpx>")]
    [InlineData("<gpx><wpt lat='1' lon='2'><ele>high</ele></wpt></gpx>")]
    [InlineData("<gpx><wpt lat='1' lon='2'><time>yesterday</time></wpt></gpx>")]
    [InlineData("<gpx><rte><number>one</number></rte></gpx>")]
    [InlineData("<gpx><wpt lat='1' lon='2'><name><b>bold</b></name></wpt></gpx>")]
    [InlineData("<!DOCTYPE gpx [<!ENTITY a 'b'>]><gpx><wpt lat='1' lon='2'><name>&a;</name></wpt></gpx>")]
    public void Parse_Invalid(string gpx)
    {
        Assert.Throws<FormatException>(() => Gpx.Parse(gpx));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(gpx));
        Assert.Throws<FormatException>(() => Gpx.Read(stream));
    }

    [Fact]
    public void Format_Elements()
    {
        var waypointProperties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "summit",
            ["sym"] = "Summit",
            ["name"] = "Mont Blanc <&>",
            ["time"] = new DateTimeOffset(2024, 7, 1, 14, 30, 0, 500, TimeSpan.FromHours(2)),
            ["population"] = 0,
            [Gpx.TypePropertyName] = "trk",
        };
        GeoFeatureCollection features =
        [
            new GeoFeature(new GeoMultiLineString([new GeoLineString([new(6, 45, 1000)]), new GeoLineString([])]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Track", ["number"] = 3 }),
            new GeoFeature(new GeoLineString([new(1, 2), new(3, 4, 5.5)]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["number"] = 2.0, ["desc"] = "Route", ["cmt"] = 42 }),
            new GeoFeature(new GeoPoint(new(6.8652, 45.8326, 4808.73)), waypointProperties, id: "ignored"),
            new GeoFeature(new GeoMultiPoint([new(1, 2), new(3, 4)]), new Dictionary<string, object?>(StringComparer.Ordinal) { ["time"] = "2024-01-01T00:00:00+01:00", ["src"] = null }),
            new GeoFeature(new GeoLineString([new(7, 8)]), new Dictionary<string, object?>(StringComparer.Ordinal) { [Gpx.TypePropertyName] = "trk", ["number"] = -1 }),
            new GeoFeature(new GeoMultiLineString([new GeoLineString([new(9, 10)]), new GeoLineString([new(11, 12)])]), new Dictionary<string, object?>(StringComparer.Ordinal) { [Gpx.TypePropertyName] = "rte", ["number"] = "4", ["name"] = "Split" }),
        ];
        features.Name = "Alps";

        Assert.Equal(
            """<?xml version="1.0" encoding="utf-8"?><gpx version="1.1" creator="Meziantou.Framework.Geo" xmlns="http://www.topografix.com/GPX/1/1"><metadata><name>Alps</name></metadata>"""
            + """<wpt lat="45.8326" lon="6.8652"><ele>4808.73</ele><time>2024-07-01T12:30:00.5Z</time><name>Mont Blanc &lt;&amp;&gt;</name><sym>Summit</sym><type>summit</type></wpt>"""
            + """<wpt lat="2" lon="1"><time>2023-12-31T23:00:00Z</time></wpt><wpt lat="4" lon="3"><time>2023-12-31T23:00:00Z</time></wpt>"""
            + """<rte><cmt>42</cmt><desc>Route</desc><number>2</number><rtept lat="2" lon="1" /><rtept lat="4" lon="3"><ele>5.5</ele></rtept></rte>"""
            + """<rte><name>Split</name><number>4</number><rtept lat="10" lon="9" /></rte><rte><name>Split</name><number>4</number><rtept lat="12" lon="11" /></rte>"""
            + """<trk><name>Track</name><number>3</number><trkseg><trkpt lat="45" lon="6"><ele>1000</ele></trkpt></trkseg><trkseg /></trk>"""
            + """<trk><trkseg><trkpt lat="8" lon="7" /></trkseg></trk></gpx>""",
            Gpx.Format(features));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        foreach (var fileName in new[] { "gdal.gpx", "garmin.gpx", "strava.gpx", "gpx10.gpx" })
        {
            var features = ReadTestData(fileName);
            var gpx = Gpx.Format(features);
            var copy = Gpx.Parse(gpx);
            Assert.Equal(gpx, Gpx.Format(copy));
            Assert.Equal(features.Name, copy.Name);
            Assert.Equal(features.Select(feature => feature.Geometry?.Type), copy.Select(feature => feature.Geometry?.Type));
            Assert.Equal(features.SelectMany(feature => feature.Geometry!.GetPositions()), copy.SelectMany(feature => feature.Geometry!.GetPositions()));
            for (var i = 0; i < features.Count; i++)
            {
                Assert.Equal(features[i].Properties.OrderBy(property => property.Key, StringComparer.Ordinal), copy[i].Properties.OrderBy(property => property.Key, StringComparer.Ordinal));
            }
        }
    }

    [Fact]
    public void Format_Options()
    {
        GeoFeatureCollection features = [new GeoFeature(new GeoPoint(new(1, 2)))];
        var gpx = Gpx.Format(features, new GpxWriterOptions { Creator = "My app", Indented = true });
        Assert.Contains("creator=\"My app\"", gpx);
        Assert.Contains("\n  <wpt", gpx);
        Assert.Throws<ArgumentNullException>("value", () => new GpxWriterOptions().Creator = null!);

        using var stream = new MemoryStream();
        Gpx.Write(features, stream);
        Assert.Equal((byte)'<', stream.ToArray()[0]);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void Format_Unsupported()
    {
        var polygon = new GeoPolygon([[new(0, 0), new(1, 0), new(1, 1), new(0, 0)]]);
        Assert.Throws<ArgumentException>("features", () => Gpx.Format([new GeoFeature(polygon)]));
        Assert.Throws<ArgumentException>("features", () => Gpx.Format([new GeoFeature(new GeoMultiPolygon([polygon]))]));
        Assert.Throws<ArgumentException>("features", () => Gpx.Format([new GeoFeature(new GeoGeometryCollection([new GeoPoint(new(1, 2))]))]));
        Assert.Throws<ArgumentException>("features", () => Gpx.Format([new GeoFeature(geometry: null)]));
        Assert.Throws<ArgumentException>("features", () => Gpx.Format([new GeoFeature(new GeoPoint(new(double.NaN, 2)))]));

        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>("features", () => Gpx.Write([new GeoFeature(new GeoPoint(new(1, 2))), new GeoFeature(polygon)], stream));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void TrackLength_Example()
    {
        File.Copy(GetTestDataPath("strava.gpx"), "activity.gpx", overwrite: true);

        #region Geo_GpxTrackLength
        GeoFeatureCollection features;
        using (var stream = File.OpenRead("activity.gpx"))
        {
            features = Gpx.Read(stream);
        }

        // A track is a multi-line string, with one line per segment
        var track = features.Single(feature => (string?)feature.Properties[Gpx.TypePropertyName] is "trk");
        var segments = ((GeoMultiLineString)track.Geometry!).LineStrings;
        var length = segments.Sum(segment => segment.GetLength());

        Assert.Equal("Lunch Run", track.Properties["name"]);
        Assert.Equal(173, Math.Round(length, MidpointRounding.AwayFromZero));
        #endregion
    }

    [Fact]
    public void Write_Example()
    {
        #region Geo_GpxWrite
        GeoFeatureCollection features =
        [
            // A point is a waypoint, and a multi-line string is a track with one segment per line
            new GeoFeature(new GeoPoint(new GeoPosition(6.8652, 45.8326, 4808)), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "Mont Blanc",
                ["time"] = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero),
            }),
            new GeoFeature(new GeoMultiLineString([new GeoLineString([new(6.8693, 45.9237, 1035), new(6.8814, 45.8775, 2372)])]), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "Ascent",
            }),
        ];
        features.Name = "Mont Blanc";

        using (var stream = File.Create("mont-blanc.gpx"))
        {
            Gpx.Write(features, stream, new GpxWriterOptions { Creator = "My application", Indented = true });
        }
        #endregion

        var copy = Gpx.Parse(File.ReadAllText("mont-blanc.gpx"));
        Assert.Equal("Mont Blanc", copy.Name);
        Assert.Equal(["wpt", "trk"], copy.Select(feature => feature.Properties[Gpx.TypePropertyName]));
    }

    [Fact]
    public void Read_InvalidCharacterInDetectedEncoding()
    {
        // "<" in UTF-32, then a surrogate (U+DB00), which UTF-32 cannot hold: creating the XML reader fails
        Assert.Throws<FormatException>(() => Gpx.Read(new MemoryStream([0x3C, 0, 0, 0, 0, 0xDB, 0, 0])));
    }

    [Fact]
    public void Read_NonAsciiBytesInDeclaration()
    {
        // A byte above 0x7F is not valid in an XML declaration; when switching to the declared encoding, .NET 10 counts each
        // one as several bytes, past the bytes read so far
        var document = "<?xml version=\"1.0"u8.ToArray().Concat(Enumerable.Repeat((byte)0xFF, 141)).Concat("\" encoding=\"UTF-8\"?>\n<a>"u8.ToArray()).Concat(Enumerable.Repeat((byte)'a', 80)).ToArray();
        Assert.Throws<FormatException>(() => Gpx.Read(new MemoryStream(document)));
    }
}
