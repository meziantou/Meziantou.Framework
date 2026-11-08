using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Tests;

// The files of TestData come from the GeoParquet specification,
// https://github.com/opengeospatial/geoparquet/tree/76e3219b1cdf0250f87e62b2cdffd824163025f4 (Apache 2.0, see LICENSE.txt):
// test_data/data-<type>-encoding_wkb.parquet with the WKT of their geometries in data-<type>-wkt.csv, and examples/example.parquet,
// five countries of Natural Earth written by GeoPandas.
// The files of TestData/generated are written by pyarrow, the reference implementation of Parquet, with generate.py:
// every compression codec, data pages of versions 1 and 2, and the dictionary, delta and BYTE_STREAM_SPLIT encodings, with
// the values pyarrow reads from them in expected.json.
public sealed class GeoParquetTests
{
    private static string GetPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [Theory]
    [InlineData("point")]
    [InlineData("linestring")]
    [InlineData("polygon")]
    [InlineData("multipoint")]
    [InlineData("multilinestring")]
    [InlineData("multipolygon")]
    public async Task ReadAsync_MatchesWkt(string type)
    {
        var features = await GeoParquet.ReadAsync(GetPath($"data-{type}-encoding_wkb.parquet"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal($"data-{type}-encoding_wkb", features.Name);

        // "col","geometry": the index of the row, then the WKT of the geometry, EMPTY, or nothing for null
        var rows = File.ReadAllLines(GetPath($"data-{type}-wkt.csv")).Skip(1).ToArray();
        Assert.HasCount(rows.Length, features);
        for (var i = 0; i < rows.Length; i++)
        {
            var separator = rows[i].IndexOf(',', StringComparison.Ordinal);
            Assert.Equal(long.Parse(rows[i][..separator], CultureInfo.InvariantCulture), Convert.ToInt64(features[i].Properties["col"], CultureInfo.InvariantCulture));
            var wkt = rows[i][(separator + 1)..].Trim('"');
            if (wkt.Length is 0 || wkt.EndsWith("EMPTY", StringComparison.Ordinal))
            {
                Assert.Null(features[i].Geometry);
            }
            else
            {
                Assert.Equal(WellKnownText.Format(WellKnownText.Parse(wkt)), WellKnownText.Format(features[i].Geometry!));
            }
        }
    }

    [Fact]
    public async Task ReadAsync_Example()
    {
        var countries = await GeoParquet.ReadAsync(GetPath("example.parquet"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["Fiji", "Tanzania", "W. Sahara", "Canada", "United States of America"], countries.Select(country => (string?)country.Properties["name"]));
        var canada = countries[3];
        Assert.Equal("North America", canada.Properties["continent"]);
        Assert.Equal("CAN", canada.Properties["iso_a3"]);
        Assert.Equal(37_589_262.0, canada.Properties["pop_est"]); // A DOUBLE column
        Assert.Equal(1_736_425L, canada.Properties["gdp_md_est"]); // An INT64 column
        Assert.IsType<GeoMultiPolygon>(canada.Geometry);
        Assert.True(canada.Geometry.GetBounds()!.Value.North > 80);
        Assert.Null(canada.Id);
    }

    [Fact]
    public async Task ReadAsync_Bounds()
    {
        var northAmerica = new GeoBounds(West: -130, South: 25, East: -60, North: 60);
        var countries = await GeoParquet.ReadAsync(GetPath("example.parquet"), new GeoParquetReaderOptions { Bounds = northAmerica }, TestContext.Current.CancellationToken);
        Assert.Equal(["Canada", "United States of America"], countries.Select(country => (string?)country.Properties["name"]));
    }

    [Fact]
    public async Task ReadAsync_Invalid()
    {
        using var notParquet = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        await Assert.ThrowsAsync<FormatException>(() => GeoParquet.ReadAsync(notParquet, cancellationToken: TestContext.Current.CancellationToken));

        var data = File.ReadAllBytes(GetPath("data-point-encoding_wkb.parquet"));
        using var truncated = new MemoryStream(data, 0, data.Length / 2);
        await Assert.ThrowsAsync<FormatException>(() => GeoParquet.ReadAsync(truncated, cancellationToken: TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => GeoParquet.ReadAsync(GetPath("example.parquet"), new GeoParquetReaderOptions { GeometryColumn = "name" }, TestContext.Current.CancellationToken));

        // A Parquet file without the "geo" metadata
        await Assert.ThrowsAsync<FormatException>(() => GeoParquet.ReadAsync(GetPath("generated/no-geo.parquet"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadFeaturesAsync_MatchesReadAsync()
    {
        var expected = await GeoParquet.ReadAsync(GetPath("example.parquet"), cancellationToken: TestContext.Current.CancellationToken);
        expected.Name = null;
        var features = await GeoParquet.ReadFeaturesAsync(GetPath("example.parquet"), cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GeoJson.Format(expected), GeoJson.Format(new GeoFeatureCollection(features)));
    }

    [Theory]
    [InlineData("uncompressed-v1")]
    [InlineData("snappy-v1")]
    [InlineData("snappy-v2")]
    [InlineData("gzip-v2")]
    [InlineData("brotli-v1")]
    [InlineData("lz4raw-v2")]
    [InlineData("zstd-v2")]
    [InlineData("plain-v1")]
    [InlineData("delta-v2")]
    [InlineData("split-v1")]
    [InlineData("pages-v1")]
    public async Task ReadAsync_MatchesPyArrow(string name)
    {
#if !NET11_0_OR_GREATER
        if (name is "zstd-v2")
        {
            // Zstandard is part of .NET from .NET 11
            await Assert.ThrowsAsync<NotSupportedException>(() => GeoParquet.ReadAsync(GetPath($"generated/{name}.parquet"), cancellationToken: TestContext.Current.CancellationToken));
            return;
        }
#endif

        var features = await GeoParquet.ReadAsync(GetPath($"generated/{name}.parquet"), cancellationToken: TestContext.Current.CancellationToken);
        using var expected = JsonDocument.Parse(File.ReadAllBytes(GetPath("generated/expected.json")));
        var rows = expected.RootElement.EnumerateArray().ToList();
        Assert.HasCount(rows.Count, features);
        for (var i = 0; i < rows.Count; i++)
        {
            var feature = features[i];
            foreach (var property in rows[i].EnumerateObject())
            {
                if (property.Name is "geometry")
                {
                    if (property.Value.ValueKind is JsonValueKind.Null)
                    {
                        Assert.Null(feature.Geometry);
                    }
                    else
                    {
                        Assert.Equal(WellKnownText.Format(WellKnownText.Parse(property.Value.GetString()!)), WellKnownText.Format(feature.Geometry!));
                    }
                }
                else if (property.Value.ValueKind is JsonValueKind.Null)
                {
                    Assert.False(feature.Properties.ContainsKey(property.Name), $"Row {i}, column {property.Name}");
                }
                else
                {
                    AssertValue(property.Value.GetString()!, feature.Properties[property.Name], $"Row {i}, column {property.Name}");
                }
            }

            // The bbox structure and the list of strings are not properties
            Assert.False(feature.Properties.ContainsKey("bbox"));
            Assert.False(feature.Properties.ContainsKey("tags"));
        }

        Assert.IsType<sbyte>(features[0].Properties["i8"]);
        Assert.IsType<ulong>(features[0].Properties["u64"]);
        Assert.IsType<DateOnly>(features[0].Properties["day"]);
        Assert.Equal(DateTimeKind.Unspecified, ((DateTime)features[0].Properties["ts_ms"]!).Kind);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)features[0].Properties["ts_us_utc"]!).Kind);
        Assert.IsType<TimeOnly>(features[0].Properties["time_ms"]);
        Assert.IsType<decimal>(features[0].Properties["dec_wide"]);
    }

    [Fact]
    public async Task ReadAsync_CorruptedData_ThrowsFormatException()
    {
        // Every byte of the metadata and of the first pages of the file is changed in turn: the reader reports invalid data
        // as a FormatException (or a NotSupportedException for an unknown feature), never as another exception
        var data = File.ReadAllBytes(GetPath("generated/snappy-v2.parquet"));
        var offsets = Enumerable.Range(0, 2048).Concat(Enumerable.Range(data.Length - 4096, 4096));
        foreach (var offset in offsets)
        {
            var corrupted = (byte[])data.Clone();
            corrupted[offset] ^= 0x5A;
            using var stream = new MemoryStream(corrupted);
            try
            {
                await GeoParquet.ReadAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException)
            {
            }
        }

        foreach (var length in new[] { 0, 4, 8, 12, 100, data.Length / 2, data.Length - 9, data.Length - 1 })
        {
            using var truncated = new MemoryStream(data, 0, length);
            await Assert.ThrowsAsync<FormatException>(() => GeoParquet.ReadAsync(truncated, cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ReadFeaturesAsync_LargeFile_ReadsOneRowGroupAtATime()
    {
        // 24 row groups of 500 rows
        const int RowGroups = 24;
        const int Rows = 500;
        var data = File.ReadAllBytes(GetPath("generated/rowgroups.parquet"));
        var metadataLength = GetMetadataLength(data);
        var rowGroupLength = (data.Length - metadataLength) / RowGroups;
        using var stream = new CountingStream(data);
        var index = 0;
        await foreach (var feature in GeoParquet.ReadFeaturesAsync(stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            // The columns of a row group are read when its first feature is returned, not before
            var rowGroup = index / Rows;
            Assert.True(stream.BytesRead < metadataLength + ((rowGroup + 2L) * rowGroupLength), $"Feature {index} was returned after reading {stream.BytesRead} bytes out of {data.Length}");
            Assert.Equal(index, feature.Properties["id"]);
            index++;
        }

        Assert.Equal(RowGroups * Rows, index);
    }

    [Fact]
    public async Task ReadFeaturesAsync_BboxCovering_SkipsTheRowGroupsOutsideOfTheBox()
    {
        var data = File.ReadAllBytes(GetPath("generated/rowgroups.parquet"));
        var metadataLength = GetMetadataLength(data);

        // The row group 10 has the longitudes from -30 to -25.1
        using var stream = new CountingStream(data);
        var features = await GeoParquet.ReadFeaturesAsync(stream, new GeoParquetReaderOptions { Bounds = new GeoBounds(-29.95, 0, -29.5, 1) }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(features);
        Assert.All(features, feature => Assert.Equal(10, (int)feature.Properties["id"]! / 500));
        Assert.True(stream.BytesRead < metadataLength + ((data.Length - metadataLength) / 10), $"{stream.BytesRead} bytes read out of {data.Length}");
    }

    [Fact]
    public async Task Documentation_ReadFeaturesAsync()
    {
        #region GeoGeoParquet_ReadFeaturesAsync
        // The file is read one row group at a time; with a box, the row groups outside of it are skipped when the file
        // has a bbox covering column
        var northAmerica = new GeoBounds(West: -130, South: 25, East: -60, North: 60);
        var names = new List<string?>();
        await foreach (var country in GeoParquet.ReadFeaturesAsync("TestData/example.parquet", new GeoParquetReaderOptions { Bounds = northAmerica }))
        {
            names.Add((string?)country.Properties["name"]);
        }

        Assert.Equal(["Canada", "United States of America"], names);
        #endregion
    }

    [Fact]
    public async Task Documentation_ReadAsync()
    {
        #region GeoGeoParquet_ReadAsync
        var countries = await GeoParquet.ReadAsync("TestData/example.parquet");
        var canada = countries.First(country => (string?)country.Properties["name"] is "Canada");
        Assert.Equal("North America", canada.Properties["continent"]);

        // Write the features in another format, such as FlatGeobuf
        var flatGeobuf = FlatGeobuf.Write(countries);
        #endregion

        Assert.NotEmpty(flatGeobuf);
    }

    // The metadata at the end of the file, with its length and the "PAR1" signature, which the reader reads first
    private static int GetMetadataLength(byte[] data) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(data.Length - 8)) + 8;

    private static void AssertValue(string expected, object? actual, string message)
    {
        switch (actual)
        {
            case float value:
                Assert.Equal(double.Parse(expected, CultureInfo.InvariantCulture), value, message);
                break;
            case double value:
                Assert.Equal(double.Parse(expected, CultureInfo.InvariantCulture), value, message);
                break;
            case decimal value:
                Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), value, message);
                break;
            case bool value:
                Assert.Equal(expected, value ? "true" : "false", message);
                break;
            case byte[] value:
                Assert.Equal(expected, Convert.ToHexStringLower(value), message);
                break;
            case DateTime value:
                Assert.Equal(expected, value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture), message);
                break;
            case DateOnly value:
                Assert.Equal(expected, value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), message);
                break;
            case TimeOnly value:
                Assert.Equal(expected, value.ToString(value.Ticks % TimeSpan.TicksPerSecond is 0 ? "HH:mm:ss" : "HH:mm:ss.ffffff", CultureInfo.InvariantCulture), message);
                break;
            default:
                Assert.Equal(expected, Convert.ToString(actual, CultureInfo.InvariantCulture), message);
                break;
        }
    }
}
