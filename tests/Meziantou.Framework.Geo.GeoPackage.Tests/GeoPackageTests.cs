using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Geo.Tests;

// TestData/places.gpkg was written by GDAL 3.10.3 (docker image ghcr.io/osgeo/gdal:alpine-small-3.10.3) from three GeoJSON files:
//   ogr2ogr -f GPKG places.gpkg cities.geojson -nln cities
//   ogr2ogr -f GPKG -update places.gpkg regions.geojson -nln regions
//   ogr2ogr -f GPKG -update places.gpkg lines.geojson -nln lines3d   (the "id" property became the FID column)
// then compacted with "sqlite3 places.gpkg VACUUM". The values the tests expect are the ones "ogrinfo -al" prints.
public sealed class GeoPackageTests
{
    private static string GetPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [Fact]
    public void GetTables()
    {
        Assert.Equal(["cities", "regions", "lines3d"], GeoPackage.GetTables(GetPath("places.gpkg")));
    }

    [Fact]
    public void Read_Points()
    {
        var cities = GeoPackage.Read(GetPath("places.gpkg"), "cities");
        Assert.Equal("cities", cities.Name);
        Assert.HasCount(4, cities);
        Assert.Equal(["1", "2", "3", "4"], cities.Select(city => city.Id));

        var paris = cities[0];
        Assert.Equal(new GeoPosition(2.3522, 48.8566), Assert.IsType<GeoPoint>(paris.Geometry).Position);
        Assert.Equal("Paris", paris.Properties["name"]);
        Assert.Equal(2102650, paris.Properties["population"]); // MEDIUMINT
        Assert.Equal(105.4, paris.Properties["area"]); // REAL
        Assert.Equal(true, paris.Properties["capital"]); // BOOLEAN
        Assert.Equal(new DateOnly(52, 1, 1), paris.Properties["founded"]); // DATE
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero), paris.Properties["updated"]); // DATETIME

        Assert.Equal("Zürich – 日本", cities[1].Properties["name"]);
        Assert.Equal(false, cities[1].Properties["capital"]);
        Assert.False(cities[1].Properties.ContainsKey("founded"));

        // A feature without geometry, and without the NULL values
        Assert.Null(cities[2].Geometry);
        Assert.Equal(["name"], cities[2].Properties.Keys);
    }

    [Fact]
    public void Read_PolygonsAndLines()
    {
        var regions = GeoPackage.Read(GetPath("places.gpkg"), "REGIONS");
        var square = Assert.IsType<GeoPolygon>(regions[0].Geometry);
        Assert.HasCount(2, square.Rings);
        Assert.False(square.Contains(new GeoPosition(5, 5)));
        Assert.True(square.Contains(new GeoPosition(1, 1)));
        Assert.HasCount(2, Assert.IsType<GeoMultiPolygon>(regions[1].Geometry).Polygons);

        var line = Assert.Single(GeoPackage.Read(GetPath("places.gpkg"), "lines3d"));
        Assert.Equal("1", line.Id);
        Assert.Empty(line.Properties);
        Assert.Equal([new GeoPosition(0, 0, 100), new GeoPosition(1, 1, 200)], Assert.IsType<GeoLineString>(line.Geometry).Positions);
    }

    [Fact]
    public void Read_Bounds()
    {
        var europe = new GeoBounds(West: -10, South: 35, East: 30, North: 60);
        var cities = GeoPackage.Read(GetPath("places.gpkg"), "cities", new GeoPackageReaderOptions { Bounds = europe });
        Assert.Equal(["Paris", "Zürich – 日本"], cities.Select(city => (string?)city.Properties["name"]));

        var regions = GeoPackage.Read(GetPath("places.gpkg"), "regions", new GeoPackageReaderOptions { Bounds = new GeoBounds(20.5, 20.5, 25, 25) });
        Assert.Equal("islands", Assert.Single(regions).Properties["name"]);
    }

    [Fact]
    public void Read_Invalid()
    {
        Assert.Throws<FormatException>(() => GeoPackage.Read(GetPath("places.gpkg")));
        Assert.Throws<ArgumentException>(() => GeoPackage.Read(GetPath("places.gpkg"), "roads"));
        Assert.Throws<FileNotFoundException>(() => GeoPackage.Read(GetPath("missing.gpkg")));

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gpkg");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            Assert.Throws<FormatException>(() => GeoPackage.Read(path));
            Assert.Throws<FormatException>(() => GeoPackage.GetTables(path));
        }
        finally
        {
            File.Delete(path);
        }

        // A SQLite database that is not a GeoPackage, and a geometry that is not a GeoPackage geometry
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Assert.Throws<FormatException>(() => GeoPackage.Read(connection));
        Execute(connection, """
            CREATE TABLE gpkg_contents (table_name TEXT NOT NULL PRIMARY KEY, data_type TEXT NOT NULL);
            CREATE TABLE gpkg_geometry_columns (table_name TEXT NOT NULL, column_name TEXT NOT NULL);
            CREATE TABLE points (fid INTEGER PRIMARY KEY, geom BLOB);
            INSERT INTO gpkg_contents VALUES ('points', 'features');
            INSERT INTO gpkg_geometry_columns VALUES ('points', 'geom');
            INSERT INTO points VALUES (1, x'0102030405060708');
            """);
        Assert.Throws<FormatException>(() => GeoPackage.Read(connection));
    }

    [Fact]
    public async Task ReadFeaturesAsync_MatchesRead()
    {
        foreach (var table in GeoPackage.GetTables(GetPath("places.gpkg")))
        {
            var expected = GeoPackage.Read(GetPath("places.gpkg"), table);
            expected.Name = null;
            var features = await GeoPackage.ReadFeaturesAsync(GetPath("places.gpkg"), table, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(GeoJson.Format(expected), GeoJson.Format(new GeoFeatureCollection(features)));
        }

        await Assert.ThrowsAsync<FormatException>(async () => await GeoPackage.ReadFeaturesAsync(GetPath("places.gpkg"), cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadFeaturesAsync_LargeTable_ReadsAsTheFeaturesAreEnumerated()
    {
        // A view of 1,000,000 rows generated by SQLite as they are read: each geometry is built by a function that counts the rows
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var generated = 0;
        connection.CreateFunction("make_point", (long i) =>
        {
            generated++;
            return CreatePoint(i % 360 - 180, 0);
        });
        Execute(connection, """
            CREATE TABLE gpkg_contents (table_name TEXT NOT NULL PRIMARY KEY, data_type TEXT NOT NULL);
            CREATE TABLE gpkg_geometry_columns (table_name TEXT NOT NULL, column_name TEXT NOT NULL);
            CREATE VIEW points AS WITH RECURSIVE r(i) AS (SELECT 0 UNION ALL SELECT i + 1 FROM r WHERE i < 999999) SELECT i AS n, make_point(i) AS geom FROM r;
            INSERT INTO gpkg_contents VALUES ('points', 'features');
            INSERT INTO gpkg_geometry_columns VALUES ('points', 'geom');
            """);

        var index = 0;
        await foreach (var feature in GeoPackage.ReadFeaturesAsync(connection, cancellationToken: TestContext.Current.CancellationToken))
        {
            // The rows are read when the features are returned, not before
            Assert.Equal(index + 1, generated);
            Assert.Equal((long)index, feature.Properties["n"]);
            index++;
            if (index == 10_000)
                break;
        }

        Assert.Equal(10_000, generated);
    }

    [Fact]
    public async Task Documentation_ReadFeaturesAsync()
    {
        #region GeoGeoPackage_ReadFeaturesAsync
        // The layers of a GeoPackage file are its feature tables
        var tables = GeoPackage.GetTables("TestData/places.gpkg");
        Assert.Contains("cities", tables);

        // The rows are read one at a time; with a box, the R-tree spatial index of the table finds the rows to read
        var europe = new GeoBounds(West: -10, South: 35, East: 30, North: 60);
        var names = new List<string?>();
        await foreach (var city in GeoPackage.ReadFeaturesAsync("TestData/places.gpkg", "cities", new GeoPackageReaderOptions { Bounds = europe }))
        {
            names.Add((string?)city.Properties["name"]);
        }

        Assert.Contains("Paris", names);
        Assert.DoesNotContain("Tokyo", names);
        #endregion
    }

    [Fact]
    public void Documentation_Read()
    {
        #region GeoGeoPackage_Read
        var cities = GeoPackage.Read("TestData/places.gpkg", "cities");
        var paris = cities[0];
        Assert.Equal("1", paris.Id); // The primary key of the table
        Assert.Equal(2_102_650, paris.Properties["population"]); // A MEDIUMINT column is an int
        Assert.Equal(new GeoPosition(2.3522, 48.8566), ((GeoPoint)paris.Geometry!).Position);

        // Write the features in another format, such as GeoJSON
        var geoJson = GeoJson.Format(cities);
        #endregion

        Assert.Contains("Paris", geoJson, StringComparison.Ordinal);
    }

    // A GeoPackage geometry without envelope: "GP", version 0, little-endian flags, the SRS id, then the WKB of the point
    private static byte[] CreatePoint(double longitude, double latitude)
    {
        var wkb = WellKnownBinary.Write(new GeoPoint(new GeoPosition(longitude, latitude)));
        var result = new byte[8 + wkb.Length];
        result[0] = (byte)'G';
        result[1] = (byte)'P';
        result[3] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 4326);
        wkb.CopyTo(result.AsSpan(8));
        return result;
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The SQL comes from the tests")]
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
