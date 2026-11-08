using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Geo;
using Microsoft.Data.Sqlite;
using static Meziantou.Framework.GeoPackageFuzz.GeoFeatureDescriber;

namespace Meziantou.Framework.GeoPackageFuzz;

/// <summary>
/// The GeoPackage reader rejects an invalid file or geometry with a <see cref="FormatException"/> and never throws anything
/// else. The streaming reader returns the features of the reader of a whole table, and a box selects the features whose
/// bounds intersect it.
/// </summary>
public static class GeoPackageFuzzTargets
{
    private const string TestDataPrefix = "TestData/";

    private const int MaxTables = 4;

    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), $"meziantou-geopackage-fuzz-{Environment.ProcessId}.gpkg");

    private static readonly string[] Tokens =
    [
        "SQLite format 3\u0000", "gpkg_contents", "gpkg_geometry_columns", "features", "rtree_", "table_name", "column_name", "data_type", "GP\u0000\u0001", "GP\u0000\u0003", "GP\u0000\u0005", "GP\u0000\u0011", "GP\u0000!",
        "æ\u0010\u0000\u0000", "\u0001\u0001\u0000\u0000\u0000", "\u0001é\u0003\u0000\u0000", "\u0001¹\u000B\u0000\u0000", "\u0000\u0000\u0000\u0000\u0000\u0000ø\u007F",
        "BOOLEAN", "TINYINT", "SMALLINT", "MEDIUMINT", "INTEGER", "FLOAT", "DOUBLE", "REAL", "TEXT", "BLOB", "DATE", "DATETIME", "2024-02-29", "2024-02-29T13:45:00Z",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("geo-geopackage", FileTarget) { Seeds = GetFileSeeds, Dictionary = Tokens },
        new("geo-geopackage-geometry", GeometryTarget) { Seeds = GetGeometrySeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// <c>data</c>: a GeoPackage file. <see cref="GeoPackage.GetTables(string)"/> and <see cref="GeoPackage.Read(string, string?, GeoPackageReaderOptions?)"/>
    /// reject an invalid file with a <see cref="FormatException"/>, and <see cref="GeoPackage.ReadFeaturesAsync(string, string?, GeoPackageReaderOptions?, CancellationToken)"/>
    /// returns the features of <c>Read</c>, or fails when it fails.
    /// </summary>
    private static void FileTarget(ReadOnlySpan<byte> data)
    {
        File.WriteAllBytes(FilePath, data);
        IReadOnlyList<string> tables;
        try
        {
            tables = GeoPackage.GetTables(FilePath);
        }
        catch (FormatException)
        {
            return;
        }

        foreach (var table in tables.Take(MaxTables))
        {
            GeoFeatureCollection? expected;
            try
            {
                expected = GeoPackage.Read(FilePath, table);
            }
            catch (FormatException)
            {
                expected = null;
            }

            var (actual, error) = ReadAll(() => GeoPackage.ReadFeaturesAsync(FilePath, table));
            FuzzAssert.Equal(expected is null, error is not null, $"GeoPackage.Read and GeoPackage.ReadFeaturesAsync disagree on whether the table '{table}' is valid: {error?.Message}");
            if (expected is not null)
            {
                FuzzAssert.Equal(table, expected.Name, "The collection is not named after its table");
                Exercise(expected);
                FuzzAssert.Equal(Describe(expected), Describe(actual), "GeoPackage.ReadFeaturesAsync returns other features than GeoPackage.Read");
            }
        }
    }

    /// <summary>
    /// <c>west, south, east, north, geometry</c>: a GeoPackage geometry (its header, then the WKB), in a table of a database
    /// in memory. A geometry that is not valid throws a <see cref="FormatException"/>; a read with the box returns the
    /// feature exactly when the bounds of its geometry intersect the box (the table has no spatial index).
    /// </summary>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Constant SQL, the geometry is a parameter")]
    private static void GeometryTarget(ReadOnlySpan<byte> data)
    {
        var box = ReadBox(ref data);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE gpkg_contents (table_name TEXT NOT NULL PRIMARY KEY, data_type TEXT NOT NULL);
                CREATE TABLE gpkg_geometry_columns (table_name TEXT NOT NULL, column_name TEXT NOT NULL);
                CREATE TABLE shapes (fid INTEGER PRIMARY KEY, geom BLOB, name TEXT);
                INSERT INTO gpkg_contents VALUES ('shapes', 'features');
                INSERT INTO gpkg_geometry_columns VALUES ('shapes', 'geom');
                INSERT INTO shapes VALUES (1, $geometry, 'shape');
                """;
            command.Parameters.AddWithValue("$geometry", data.ToArray());
            command.ExecuteNonQuery();
        }

        GeoFeatureCollection features;
        try
        {
            features = GeoPackage.Read(connection);
        }
        catch (FormatException)
        {
            return;
        }

        var feature = features.Single();
        FuzzAssert.Equal("1", feature.Id, "The identifier of the feature is not its primary key");
        FuzzAssert.Equal("shape", feature.Properties["name"], "The property of the feature is not its column");
        Exercise(features);

        var bounded = GeoPackage.Read(connection, options: new GeoPackageReaderOptions { Bounds = box });
        var intersects = feature.Geometry?.GetBounds() is { } bounds && Intersects(box, bounds);
        FuzzAssert.Equal(intersects ? 1 : 0, bounded.Count, "A read with a box does not return the feature exactly when its bounds intersect the box");
        var (streamed, error) = ReadAll(() => GeoPackage.ReadFeaturesAsync(connection));
        FuzzAssert.True(error is null, $"GeoPackage.ReadFeaturesAsync fails where GeoPackage.Read succeeds: {error?.Message}");
        FuzzAssert.Equal(Describe(features), Describe(streamed), "GeoPackage.ReadFeaturesAsync returns another feature than GeoPackage.Read");
    }

    private static IEnumerable<FuzzSeed> GetFileSeeds()
    {
        foreach (var (name, data) in GetTestFiles())
        {
            yield return new FuzzSeed(name, data);
        }
    }

    private static IEnumerable<FuzzSeed> GetGeometrySeeds()
    {
        byte[] box = [0x80, 0x80, 0x7F, 0x7F];
        yield return new FuzzSeed("point", (byte[])[.. box, .. Header(envelope: 0), .. WellKnownBinary.Write(new GeoPoint(new GeoPosition(2.35, 48.86)))]);
        yield return new FuzzSeed("point-z", (byte[])[.. box, .. Header(envelope: 0), .. WellKnownBinary.Write(new GeoPoint(new GeoPosition(2.35, 48.86, 35)))]);
        yield return new FuzzSeed("empty", (byte[])[.. box, (byte)'G', (byte)'P', 0, 0x11, 0xE6, 0x10, 0, 0]);
        yield return new FuzzSeed("polygon-envelope", (byte[])[1, 1, 3, 3, .. Header(envelope: 1), .. new byte[32], .. WellKnownBinary.Write(new GeoPolygon([[new(0, 0), new(4, 0), new(4, 4), new(0, 0)]]))]);
        yield return new FuzzSeed("line-envelope-zm", (byte[])[.. box, .. Header(envelope: 4), .. new byte[64], .. WellKnownBinary.Write(new GeoLineString([new(0, 0) { Measure = 1 }, new(1, 1) { Measure = 2 }]))]);

        // "GP", version 0, the flags (little-endian, the envelope contents indicator), the SRS id 4326
        static byte[] Header(int envelope)
        {
            var header = new byte[8];
            header[0] = (byte)'G';
            header[1] = (byte)'P';
            header[3] = (byte)(1 | (envelope << 1));
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 4326);
            return header;
        }
    }

    private static IEnumerable<(string Name, byte[] Data)> GetTestFiles()
    {
        var assembly = typeof(GeoPackageFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(TestDataPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (resourceName[TestDataPrefix.Length..], memory.ToArray());
        }
    }
}
