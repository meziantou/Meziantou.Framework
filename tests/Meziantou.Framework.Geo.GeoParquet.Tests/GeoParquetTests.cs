using System.Globalization;
using Parquet;
using Parquet.Schema;

namespace Meziantou.Framework.Geo.Tests;

// The files of TestData come from the GeoParquet specification,
// https://github.com/opengeospatial/geoparquet/tree/76e3219b1cdf0250f87e62b2cdffd824163025f4 (Apache 2.0, see LICENSE.txt):
// test_data/data-<type>-encoding_wkb.parquet with the WKT of their geometries in data-<type>-wkt.csv, and examples/example.parquet,
// five countries of Natural Earth written by GeoPandas.
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
        using var plain = new MemoryStream();
        var id = new DataField<int>("id");
        await using (var writer = await ParquetWriter.CreateAsync(new ParquetSchema(id), plain, cancellationToken: TestContext.Current.CancellationToken))
        {
            using var rowGroup = writer.CreateRowGroup();
            await rowGroup.WriteAsync(id, new ReadOnlyMemory<int>([1]), cancellationToken: TestContext.Current.CancellationToken);
        }

        plain.Position = 0;
        await Assert.ThrowsAsync<FormatException>(() => GeoParquet.ReadAsync(plain, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadFeaturesAsync_MatchesReadAsync()
    {
        var expected = await GeoParquet.ReadAsync(GetPath("example.parquet"), cancellationToken: TestContext.Current.CancellationToken);
        expected.Name = null;
        var features = await GeoParquet.ReadFeaturesAsync(GetPath("example.parquet"), cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GeoJson.Format(expected), GeoJson.Format(new GeoFeatureCollection(features)));
    }

    [Fact]
    public async Task ReadFeaturesAsync_LargeFile_ReadsOneRowGroupAtATime()
    {
        const int RowGroups = 40;
        const int Rows = 5_000;
        var data = await CreateLargeFileAsync(RowGroups, Rows);
        using var stream = new CountingStream(data);
        var index = 0;
        await foreach (var feature in GeoParquet.ReadFeaturesAsync(stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            // The columns of a row group are read when its first feature is returned, not before
            var rowGroup = index / Rows;
            Assert.True(stream.BytesRead < (rowGroup + 2L) * data.Length / RowGroups, $"Feature {index} was returned after reading {stream.BytesRead} bytes out of {data.Length}");
            Assert.Equal(index, feature.Properties["id"]);
            index++;
        }

        Assert.Equal(RowGroups * Rows, index);
    }

    [Fact]
    public async Task ReadFeaturesAsync_BboxCovering_SkipsTheRowGroupsOutsideOfTheBox()
    {
        const int RowGroups = 40;
        const int Rows = 5_000;
        var data = await CreateLargeFileAsync(RowGroups, Rows);

        // The row group 10 covers the longitudes -140 to -139
        using var stream = new CountingStream(data);
        var features = await GeoParquet.ReadFeaturesAsync(stream, new GeoParquetReaderOptions { Bounds = new GeoBounds(-139.9, 0, -139.5, 1) }, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(features);
        Assert.All(features, feature => Assert.Equal(10, (int)feature.Properties["id"]! / Rows));
        Assert.True(stream.BytesRead < data.Length / 10, $"{stream.BytesRead} bytes read out of {data.Length}");
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

    // A GeoParquet 1.1 file of points, with a bbox covering column: the row group g has the longitudes -180 + g * 4 to -179 + g * 4
    private static async Task<byte[]> CreateLargeFileAsync(int rowGroups, int rows)
    {
        var id = new DataField<int>("id");
        var geometry = new DataField<byte[]>("geometry");
        var xmin = new DataField<double>("xmin");
        var ymin = new DataField<double>("ymin");
        var xmax = new DataField<double>("xmax");
        var ymax = new DataField<double>("ymax");
        var schema = new ParquetSchema(id, geometry, new StructField("bbox", xmin, ymin, xmax, ymax));
        var dataFields = schema.GetDataFields();

        using var stream = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            writer.CustomMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["geo"] = """{"version":"1.1.0","primary_column":"geometry","columns":{"geometry":{"encoding":"WKB","geometry_types":["Point"],"covering":{"bbox":{"xmin":["bbox","xmin"],"ymin":["bbox","ymin"],"xmax":["bbox","xmax"],"ymax":["bbox","ymax"]}}}}}""",
            };

            for (var g = 0; g < rowGroups; g++)
            {
                var ids = new int[rows];
                var geometries = new byte[rows][];
                var longitudes = new double[rows];
                var latitudes = new double[rows];
                for (var i = 0; i < rows; i++)
                {
                    ids[i] = (g * rows) + i;
                    longitudes[i] = -180 + (g * 4) + (i % 100 * 0.01);
                    latitudes[i] = i / 100 * 0.01;
                    geometries[i] = WellKnownBinary.Write(new GeoPoint(new GeoPosition(longitudes[i], latitudes[i])));
                }

                // The columns of the bbox structure, which can be null, are written with nullable values
                static ReadOnlyMemory<double?> ToNullable(double[] values) => Array.ConvertAll(values, value => (double?)value);

                using var rowGroup = writer.CreateRowGroup();
                await rowGroup.WriteAsync(id, new ReadOnlyMemory<int>(ids), cancellationToken: TestContext.Current.CancellationToken);
                await rowGroup.WriteAsync(geometry, geometries);
                await rowGroup.WriteAsync(dataFields[2], ToNullable(longitudes), cancellationToken: TestContext.Current.CancellationToken);
                await rowGroup.WriteAsync(dataFields[3], ToNullable(latitudes), cancellationToken: TestContext.Current.CancellationToken);
                await rowGroup.WriteAsync(dataFields[4], ToNullable(longitudes), cancellationToken: TestContext.Current.CancellationToken);
                await rowGroup.WriteAsync(dataFields[5], ToNullable(latitudes), cancellationToken: TestContext.Current.CancellationToken);
            }
        }

        return stream.ToArray();
    }
}
