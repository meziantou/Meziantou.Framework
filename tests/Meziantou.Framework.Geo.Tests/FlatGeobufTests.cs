using System.Buffers.Binary;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Tests;

// The files of TestData/flatgeobuf come from the test data of the reference implementation,
// https://github.com/flatgeobuf/flatgeobuf/tree/d69dc74c898068237fcf98cf3a92f82180b6b3c8/test/data (BSD 2-Clause, see LICENSE.txt).
public sealed class FlatGeobufTests
{
    private static string GetPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "flatgeobuf", name);

    [Fact]
    public void Read_Countries_MatchesGeoJson()
    {
        var features = FlatGeobuf.Read(GetPath("countries.fgb"));
        Assert.Equal("countries", features.Name);
        Assert.HasCount(179, features);

        var expected = GeoJson.Parse(File.ReadAllText(GetPath("countries.geojson"))).ToDictionary(feature => (string)feature.Properties["name"]!, StringComparer.Ordinal);
        foreach (var feature in features)
        {
            // The rings of the FlatGeobuf file turn the other way: compare their positions
            var multiPolygon = Assert.IsType<GeoMultiPolygon>(feature.Geometry);
            var geometry = expected[(string)feature.Properties["name"]!].Geometry!;
            var polygons = geometry is GeoMultiPolygon multi ? multi.Polygons : [(GeoPolygon)geometry];
            Assert.HasCount(polygons.Count, multiPolygon.Polygons);
            for (var i = 0; i < polygons.Count; i++)
            {
                Assert.HasCount(polygons[i].Rings.Count, multiPolygon.Polygons[i].Rings);
                for (var j = 0; j < polygons[i].Rings.Count; j++)
                {
                    Assert.True(polygons[i].Rings[j].ToHashSet().SetEquals(multiPolygon.Polygons[i].Rings[j]));
                }
            }
        }
    }

    [Fact]
    public void Read_AllDataTypes()
    {
        var feature = Assert.Single(FlatGeobuf.Read(GetPath("alldatatypes.fgb")));
        Assert.Equal((sbyte)-1, feature.Properties["byte"]);
        Assert.Equal((byte)255, feature.Properties["ubyte"]);
        Assert.Equal(true, feature.Properties["bool"]);
        Assert.Equal((short)-1, feature.Properties["short"]);
        Assert.Equal(ushort.MaxValue, feature.Properties["ushort"]);
        Assert.Equal(-1, feature.Properties["int"]);
        Assert.Equal(uint.MaxValue, feature.Properties["uint"]);
        Assert.Equal(-1L, feature.Properties["long"]);
        Assert.Equal(ulong.MaxValue, feature.Properties["ulong"]);
        Assert.Equal(0f, feature.Properties["float"]);
        Assert.Equal(0d, feature.Properties["double"]);
        Assert.Equal("X", feature.Properties["string"]);

        // "X" is not valid JSON: the text is kept
        Assert.Equal("X", feature.Properties["json"]);
        Assert.Equal(new DateTimeOffset(2020, 2, 29, 12, 34, 56, TimeSpan.Zero), feature.Properties["datetime"]);
        Assert.Equal("X"u8.ToArray(), (byte[])feature.Properties["binary"]!);
    }

    [Fact]
    public void Read_OtherFiles()
    {
        // A collection of different geometry types, without index
        var heterogeneous = FlatGeobuf.Read(GetPath("heterogeneous.fgb"));
        Assert.Equal("L1", heterogeneous.Name);
        Assert.HasCount(3, heterogeneous);

        // No feature, and features whose number the header does not give
        Assert.Empty(FlatGeobuf.Read(GetPath("empty.fgb")));
        Assert.NotEmpty(FlatGeobuf.Read(GetPath("unknown_feature_count.fgb")));

        var point = Assert.Single(FlatGeobuf.Read(GetPath("no_properties.fgb")));
        Assert.IsType<GeoPoint>(point.Geometry);
        Assert.Empty(point.Properties);

        Assert.HasCount(2, FlatGeobuf.Read(GetPath("mp_overlapping.fgb")));
        var polygons = FlatGeobuf.Read(GetPath("poly00.fgb"));
        Assert.HasCount(10, polygons);
        Assert.IsType<double>(polygons[0].Properties["AREA"]);
        Assert.IsType<long>(polygons[0].Properties["EAS_ID"]);
        Assert.IsType<string>(polygons[0].Properties["PRFEDEA"]);

        // A file the fuzzer of the reference implementation found, and data that is not FlatGeobuf
        Assert.Throws<FormatException>(() => FlatGeobuf.Read(GetPath("fuzz-4642513358553088.fgb")));
        Assert.Throws<FormatException>(() => FlatGeobuf.Read(new ReadOnlyMemory<byte>([1, 2, 3])));

        // A feature whose table is at an offset above int.MaxValue: the signature, the size-prefixed header, then the size of the feature
        var data = FlatGeobuf.Write([new GeoFeature(new GeoPoint(new(1, 2)))], new FlatGeobufWriterOptions { IncludeSpatialIndex = false });
        var feature = 12 + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)) + 4;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(feature), uint.MaxValue);
        Assert.Throws<FormatException>(() => FlatGeobuf.Read(data));
    }

    [Fact]
    public void Read_Bounds()
    {
        #region Geo_FlatGeobufReadBounds
        // With a spatial index, only the features whose bounds intersect the box are decoded
        var europe = new GeoBounds(West: -10, South: 35, East: 30, North: 60);
        var features = FlatGeobuf.Read("TestData/flatgeobuf/countries.fgb", new FlatGeobufReaderOptions { Bounds = europe });
        Assert.Contains("France", features.Select(feature => (string?)feature.Properties["name"]));
        Assert.DoesNotContain("Japan", features.Select(feature => (string?)feature.Properties["name"]));
        #endregion

        // Without the index, the features are filtered as they are read: the same features
        var all = FlatGeobuf.Read(GetPath("countries.fgb"));
        var withoutIndex = FlatGeobuf.Read(FlatGeobuf.Write(all, new FlatGeobufWriterOptions { IncludeSpatialIndex = false }), new FlatGeobufReaderOptions { Bounds = europe });
        Assert.Equal(Names(features), Names(withoutIndex));

        var expected = all.Where(feature => Intersects(feature.Geometry!.GetBounds()!.Value, europe));
        Assert.Equal(Names(expected), Names(features));

        static string[] Names(IEnumerable<GeoFeature> features) => [.. features.Select(feature => (string)feature.Properties["name"]!).Order(StringComparer.Ordinal)];

        static bool Intersects(GeoBounds a, GeoBounds b) => a.West <= b.East && a.East >= b.West && a.South <= b.North && a.North >= b.South;
    }

    [Fact]
    public void WriteRead()
    {
        #region Geo_FlatGeobufReadWrite
        var features = new GeoFeatureCollection
        {
            new GeoFeature(
                new GeoPoint(new GeoPosition(Longitude: 2.35, Latitude: 48.86)),
                new Dictionary<string, object?> { ["name"] = "Paris", ["population"] = 2_100_000 }),
            new GeoFeature(
                new GeoPoint(new GeoPosition(Longitude: -0.13, Latitude: 51.51)),
                new Dictionary<string, object?> { ["name"] = "London", ["population"] = 8_900_000 }),
        };
        features.Name = "cities";

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".fgb");
        FlatGeobuf.Write(features, path);
        var cities = FlatGeobuf.Read(path);
        Assert.Equal("cities", cities.Name);
        Assert.HasCount(2, cities);
        #endregion

        File.Delete(path);
        Assert.Equal(["London", "Paris"], cities.Select(city => (string)city.Properties["name"]!).Order(StringComparer.Ordinal));
        Assert.Equal(2_100_000, cities.Single(city => city.Properties["name"] is "Paris").Properties["population"]);
    }

    [Fact]
    public void Read_Span_DoesNotKeepTheData()
    {
        #region Geo_FlatGeobufReadSpan
        var buffer = File.ReadAllBytes("TestData/flatgeobuf/countries.fgb");
        var europe = new GeoBounds(West: -10, South: 35, East: 30, North: 60);
        var features = FlatGeobuf.Read(buffer.AsSpan(), new FlatGeobufReaderOptions { Bounds = europe });

        // The features are copies: the memory of the data can be reused or freed
        buffer.AsSpan().Clear();
        Assert.Contains("France", features.Select(feature => (string?)feature.Properties["name"]));
        #endregion

        var expected = FlatGeobuf.Read(GetPath("countries.fgb"), new FlatGeobufReaderOptions { Bounds = europe });
        Assert.HasCount(expected.Count, features);
        Assert.Equal(GeoJson.Format(expected), GeoJson.Format(features));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteRead_RoundTrip(bool includeSpatialIndex)
    {
        using var document = JsonDocument.Parse("""{"a":[1,2]}""");
        var square = new GeoPolygon([[new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)], [new(4, 4), new(6, 4), new(6, 6), new(4, 4)]]);
        var features = new GeoFeatureCollection
        {
            new GeoFeature(square, new Dictionary<string, object?> { ["text"] = "a", ["number"] = 1, ["flag"] = true, ["json"] = document.RootElement.Clone() }),
            new GeoFeature(new GeoMultiPolygon([square, new GeoPolygon([[new(20, 20), new(30, 20), new(30, 30), new(20, 20)]])]), new Dictionary<string, object?> { ["number"] = 2L, ["date"] = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), ["bytes"] = new byte[] { 1, 2 } }),
            new GeoFeature(new GeoLineString([new(1, 1, 100), new(2, 2)]), new Dictionary<string, object?> { ["number"] = 2.5, ["text"] = null }),
            new GeoFeature(new GeoMultiLineString([new GeoLineString([new(1, 1), new(2, 2)]), new GeoLineString([new(3, 3), new(4, 4)])])),
            new GeoFeature(new GeoMultiPoint([new(5, 5), new(6, 6)])),
            new GeoFeature(new GeoGeometryCollection([new GeoPoint(new(7, 7)), square])),
        };

        var data = FlatGeobuf.Write(features, new FlatGeobufWriterOptions { IncludeSpatialIndex = includeSpatialIndex, Name = "layer" });
        Assert.Equal(0, data.Length % 8);
        var read = FlatGeobuf.Read(data);
        Assert.Equal("layer", read.Name);
        Assert.HasCount(features.Count, read);

        // The index reorders the features: match them by their geometries
        foreach (var feature in features)
        {
            var match = Assert.Single(read, candidate => candidate.Geometry!.Type == feature.Geometry!.Type && candidate.Geometry.GetPositions().Select(position => (position.Longitude, position.Latitude)).SequenceEqual(feature.Geometry.GetPositions().Select(position => (position.Longitude, position.Latitude))));
            foreach (var (name, value) in feature.Properties)
            {
                switch (name, value)
                {
                    case (_, null):
                        Assert.False(match.Properties.ContainsKey(name));
                        break;
                    case ("number", _):
                        Assert.Equal(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), match.Properties[name]);
                        break;
                    case ("json", JsonElement element):
                        Assert.Equal(element.GetRawText(), ((JsonElement)match.Properties[name]!).GetRawText());
                        break;
                    case ("bytes", byte[] bytes):
                        Assert.Equal(bytes, (byte[])match.Properties[name]!);
                        break;
                    default:
                        Assert.Equal(value, match.Properties[name]);
                        break;
                }
            }
        }

        // The positions without altitude get 0 when another one has an altitude
        var line = Assert.IsType<GeoLineString>(read.Single(feature => feature.Geometry is GeoLineString).Geometry);
        Assert.Equal([new GeoPosition(1, 1, 100), new(2, 2, 0)], line.Positions);
    }

    [Fact]
    public void WriteRead_Measures()
    {
        // The measures are the M values; a missing one is written as NaN, which reads back as no measure, and the times are not written
        var features = new GeoFeatureCollection
        {
            new GeoFeature(new GeoLineString([new GeoPosition(0, 0) { Measure = 1 }, new GeoPosition(1, 1) { Time = DateTimeOffset.UnixEpoch }])),
            new GeoFeature(new GeoPolygon([[new GeoPosition(0, 0, 5) { Measure = 1 }, new GeoPosition(1, 0, 5) { Measure = 2 }, new GeoPosition(1, 1, 5) { Measure = 3 }, new GeoPosition(0, 0, 5) { Measure = 1 }]])),
            new GeoFeature(new GeoGeometryCollection([new GeoPoint(new GeoPosition(7, 7) { Measure = -7 })])),
        };

        var read = FlatGeobuf.Read(FlatGeobuf.Write(features, new FlatGeobufWriterOptions { IncludeSpatialIndex = false }));
        Assert.Equal([new GeoPosition(0, 0, 0) { Measure = 1 }, new(1, 1, 0)], read[0].Geometry!.GetPositions());
        Assert.Equal(features[1].Geometry!.GetPositions(), read[1].Geometry!.GetPositions());
        Assert.Equal([new GeoPosition(7, 7, 0) { Measure = -7 }], read[2].Geometry!.GetPositions());

        // Without measures, the file has no M values
        var withoutMeasures = FlatGeobuf.Read(FlatGeobuf.Write([new GeoFeature(new GeoPoint(new(1, 2)))]));
        Assert.Null(withoutMeasures[0].Geometry!.GetPositions().Single().Measure);
    }

    [Fact]
    public void Write_ColumnsInIndexOrder()
    {
        // The index reorders the features: the columns follow the written order, so writing what is read gives the same bytes
        foreach (var (first, second) in new[] { (0.0, 50.0), (50.0, 0.0) })
        {
            var features = new GeoFeatureCollection
            {
                new GeoFeature(new GeoPoint(new(first, first)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = 1 }),
                new GeoFeature(new GeoPoint(new(second, second)), new Dictionary<string, object?>(StringComparer.Ordinal) { ["b"] = "text" }),
            };

            var data = FlatGeobuf.Write(features);
            Assert.Equal(data, FlatGeobuf.Write(FlatGeobuf.Read(data)));
        }
    }

    [Fact]
    public void Write_FeaturesWithoutGeometry()
    {
        // The index needs a box for each feature: it is not written
        var features = new GeoFeatureCollection { new GeoFeature(new GeoPoint(new(1, 2))), new GeoFeature(geometry: null), new GeoFeature(new GeoPolygon([])) };
        var read = FlatGeobuf.Read(FlatGeobuf.Write(features));
        Assert.Equal([GeoGeometryType.Point, null, null], read.Select(feature => feature.Geometry?.Type));
        Assert.Empty(FlatGeobuf.Read(FlatGeobuf.Write([])));
        Assert.HasCount(1, FlatGeobuf.Read(FlatGeobuf.Write(features), new FlatGeobufReaderOptions { Bounds = new GeoBounds(0, 0, 5, 5) }));

        Assert.Throws<ArgumentException>("features", () => FlatGeobuf.Write([new GeoFeature(new GeoPoint(new(double.NaN, 0)))]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FlatGeobufWriterOptions { IndexNodeSize = 1 });
    }

    [Fact]
    public void Write_DateTimes()
    {
        // ISO 8601 to the millisecond, which GDAL reads: at most 32 characters
        var offset = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)).AddTicks(1_234_567);
        var features = new GeoFeatureCollection
        {
            new GeoFeature(new GeoPoint(new(0, 0)), new Dictionary<string, object?>
            {
                ["offset"] = offset,
                ["utc"] = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
                ["date"] = new DateOnly(2024, 5, 6),
            }),
        };
        var feature = Assert.Single(FlatGeobuf.Read(FlatGeobuf.Write(features)));
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 7, 8, 9, 123, TimeSpan.FromHours(2)), feature.Properties["offset"]);
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero), feature.Properties["utc"]);
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero), feature.Properties["date"]);
    }

    [Fact]
    public void Write_LargeIndex()
    {
        // Enough features for an index of three levels
        var features = new GeoFeatureCollection();
        for (var i = 0; i < 1000; i++)
        {
            features.Add(new GeoFeature(new GeoPoint(new(i % 40, i / 40)), new Dictionary<string, object?> { ["i"] = i }));
        }

        var data = FlatGeobuf.Write(features, new FlatGeobufWriterOptions { IndexNodeSize = 4 });
        var read = FlatGeobuf.Read(data, new FlatGeobufReaderOptions { Bounds = new GeoBounds(10, 10, 12, 11) });
        Assert.Equal([410, 411, 412, 450, 451, 452], read.Select(feature => (int)feature.Properties["i"]!).Order());
        Assert.HasCount(1000, FlatGeobuf.Read(data));
    }

    [Theory]
    [InlineData("countries.fgb")]
    [InlineData("alldatatypes.fgb")]
    [InlineData("heterogeneous.fgb")]
    [InlineData("unknown_feature_count.fgb")]
    [InlineData("empty.fgb")]
    [InlineData("no_properties.fgb")]
    [InlineData("poly00.fgb")]
    [InlineData("mp_overlapping.fgb")]
    [InlineData("fuzz-4642513358553088.fgb")]
    public async Task ReadFeaturesAsync_MatchesRead(string name)
    {
        var data = File.ReadAllBytes(GetPath(name));
        GeoBounds?[] boxes = [null, new GeoBounds(West: -10, South: 35, East: 30, North: 60)];
        foreach (var bounds in boxes)
        {
            var options = new FlatGeobufReaderOptions { Bounds = bounds };
            string? expected;
            try
            {
                var features = FlatGeobuf.Read(data, options);
                features.Name = null;
                expected = GeoJson.Format(features);
            }
            catch (FormatException)
            {
                expected = null;
            }

            foreach (var canSeek in new[] { true, false })
            {
                using var stream = new CountingStream(data, canSeek, maxReadLength: 5);
                if (expected is null)
                {
                    await Assert.ThrowsAsync<FormatException>(async () => await FlatGeobuf.ReadFeaturesAsync(stream, options, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
                }
                else
                {
                    var features = await FlatGeobuf.ReadFeaturesAsync(stream, options, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
                    Assert.Equal(expected, GeoJson.Format(new GeoFeatureCollection(features)));
                }
            }
        }
    }

    [Fact]
    public async Task ReadFeaturesAsync_File()
    {
        var features = await FlatGeobuf.ReadFeaturesAsync(GetPath("countries.fgb"), cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.HasCount(179, features);
    }

    [Fact]
    public async Task ReadFeaturesAsync_Invalid()
    {
        var data = File.ReadAllBytes(GetPath("countries.fgb"));
        foreach (var length in new[] { 0, 5, 12, 100, data.Length - 10 })
        {
            using var stream = new MemoryStream(data, 0, length);
            await Assert.ThrowsAsync<FormatException>(async () => await FlatGeobuf.ReadFeaturesAsync(stream, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ReadFeaturesAsync_SpatialIndex_ReadsOnlyTheNodesAndFeaturesOfTheBox()
    {
        var data = FlatGeobuf.Write(CreateGrid(250_000), new FlatGeobufWriterOptions());
        var bounds = new GeoBounds(West: 10, South: 10, East: 12, North: 11);
        using var stream = new CountingStream(data);
        var features = await FlatGeobuf.ReadFeaturesAsync(stream, new FlatGeobufReaderOptions { Bounds = bounds }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([5010, 5011, 5012, 5510, 5511, 5512], features.Select(feature => (int)feature.Properties["i"]!).Order());

        // The file is about 20 MB: the search reads a few nodes of each level of the index, and the 6 features
        Assert.True(stream.BytesRead < 64 * 1024, $"{stream.BytesRead} bytes read out of {data.Length}");
    }

    [Fact]
    public async Task ReadFeaturesAsync_LargeFile_ReadsAsTheFeaturesAreEnumerated()
    {
        var data = FlatGeobuf.Write(CreateGrid(250_000), new FlatGeobufWriterOptions { IncludeSpatialIndex = false });
        using var stream = new CountingStream(data, canSeek: false);
        var index = 0;
        long previous = 0;
        await foreach (var feature in FlatGeobuf.ReadFeaturesAsync(stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Only the feature being returned is read, after the header
            Assert.True(index is 0 || stream.BytesRead - previous < 1024, $"Feature {index} was returned after reading {stream.BytesRead - previous} bytes");
            Assert.Equal(index, (int)feature.Properties["i"]!);
            previous = stream.BytesRead;
            index++;
        }

        Assert.Equal(250_000, index);
        Assert.Equal(data.Length, stream.BytesRead);
    }

    [Fact]
    public async Task Documentation_ReadFeaturesAsync()
    {
        #region Geo_FlatGeobufReadFeaturesAsync
        // The features are read one at a time; with a box and a spatial index, only the nodes of the index that the
        // search visits and the features it finds are read from the file
        var europe = new GeoBounds(West: -10, South: 35, East: 30, North: 60);
        var names = new List<string?>();
        await foreach (var feature in FlatGeobuf.ReadFeaturesAsync("TestData/flatgeobuf/countries.fgb", new FlatGeobufReaderOptions { Bounds = europe }))
        {
            names.Add((string?)feature.Properties["name"]);
        }

        Assert.Contains("France", names);
        Assert.DoesNotContain("Japan", names);
        #endregion
    }

    // Points on a grid of 500 columns: the feature i is at (i % 500, i / 500)
    private static GeoFeatureCollection CreateGrid(int count)
    {
        var features = new GeoFeatureCollection();
        for (var i = 0; i < count; i++)
        {
            features.Add(new GeoFeature(new GeoPoint(new(i % 500, i / 500)), new Dictionary<string, object?> { ["i"] = i }));
        }

        return features;
    }
}
