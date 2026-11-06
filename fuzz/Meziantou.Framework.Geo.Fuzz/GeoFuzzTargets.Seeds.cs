using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Geo.Fuzz;

public static partial class GeoFuzzTargets
{
    private static readonly string[] GeoJsonTexts =
    [
        """{"type":"Point","coordinates":[2.35,48.86,35]}""",
        """{"type":"Feature","id":42,"properties":{"name":"Paris","list":[1,2],"nested":{"a":null}},"geometry":{"type":"LineString","coordinates":[[0,0],[1,1]]}}""",
        """{"type":"FeatureCollection","name":"sample","features":[{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[0,0],[4,0],[4,4],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}},{"type":"Feature","geometry":{"type":"GeometryCollection","geometries":[{"type":"MultiPoint","coordinates":[[1,2]]},{"type":"MultiPolygon","coordinates":[[[[0,0],[1,0],[1,1],[0,0]]]]}]}},{"type":"Feature","geometry":null}]}""",
    ];

    private static readonly string[] PolylineTexts = ["_p~iF~ps|U_ulLnnqC_mqNvxq`@", "_izlhA~rlgdF_{geC~ywl@_kwzCn`{nI", "??", ""];

    private static IEnumerable<FuzzSeed> GetGeoJsonSeeds()
    {
        for (var i = 0; i < GeoJsonTexts.Length; i++)
        {
            yield return FuzzSeed.FromText("inline-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), GeoJsonTexts[i]);
        }

        foreach (var (name, data) in GetTestFiles(".geojson"))
        {
            yield return new FuzzSeed(name, data);
        }
    }

    private static IEnumerable<FuzzSeed> GetTopoJsonSeeds()
    {
        foreach (var (name, data) in GetTestFiles(".topojson"))
        {
            yield return new FuzzSeed(name, (byte[])[0, .. data]);
            yield return new FuzzSeed(name + "-quantized", (byte[])[100, .. data]);
        }
    }

    private static IEnumerable<FuzzSeed> GetWktSeeds()
    {
        var index = 0;
        foreach (var columns in GetWktTable())
        {
            yield return FuzzSeed.FromText("wkt-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), columns[0]);
            index++;
        }

        yield return FuzzSeed.FromText("ewkt", "SRID=4326;MULTIPOINT Z ((1 2 3), (4 5 6))");
        yield return FuzzSeed.FromText("empty", "GEOMETRYCOLLECTION (POINT EMPTY, POLYGON (EMPTY, (0 0, 1 0, 1 1, 0 0)))");
    }

    private static IEnumerable<FuzzSeed> GetWkbSeeds()
    {
        var index = 0;
        foreach (var columns in GetWktTable())
        {
            // The legacy WKB of OGR is "-" for the geometries it cannot write
            foreach (var column in new[] { 1, 2, 3 }.Where(column => columns[column] is not "-"))
            {
                yield return new FuzzSeed("wkb-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + column.ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.FromHexString(columns[column]));
            }

            index++;
        }

        // EWKB with the Z flag and the SRID 4326, as PostGIS writes it
        yield return new FuzzSeed("ewkb", Convert.FromHexString("01010000A0E6100000000000000000F03F00000000000000400000000000000840"));
    }

    private static IEnumerable<FuzzSeed> GetPolylineSeeds()
    {
        for (var i = 0; i < PolylineTexts.Length; i++)
        {
            yield return FuzzSeed.FromText("polyline-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), [i is 1 ? (byte)6 : (byte)5], PolylineTexts[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetKmlSeeds()
    {
        foreach (var (name, data) in GetTestFiles(".kml"))
        {
            yield return new FuzzSeed(name, (byte[])[0, .. data]);
        }

        foreach (var (name, data) in GetTestFiles(".kmz"))
        {
            yield return new FuzzSeed(name, (byte[])[1, .. data]);
        }
    }

    private static IEnumerable<FuzzSeed> GetGpxSeeds()
    {
        foreach (var (name, data) in GetTestFiles(".gpx"))
        {
            yield return new FuzzSeed(name, data);
        }
    }

    private static IEnumerable<FuzzSeed> GetShapefileSeeds()
    {
        var files = GetTestFiles("").ToList();
        foreach (var (name, shp) in files.Where(file => file.Name.EndsWith(".shp", StringComparison.OrdinalIgnoreCase)))
        {
            var dbf = files.FirstOrDefault(file => string.Equals(file.Name, name[..^4] + ".dbf", StringComparison.OrdinalIgnoreCase)).Data ?? [];
            byte[] length = [(byte)shp.Length, (byte)(shp.Length >> 8)];
            yield return new FuzzSeed(name, (byte[])[1, .. length, .. shp, .. dbf]);
            yield return new FuzzSeed(name + "-zip", (byte[])[3, .. length, .. shp, .. dbf]);
            yield return new FuzzSeed(name + "-nodbf", (byte[])[0, .. length, .. shp]);
        }
    }

    private static IEnumerable<string[]> GetWktTable()
    {
        var (_, data) = GetTestFiles(".tsv").Single();
        foreach (var line in Encoding.UTF8.GetString(data).Split('\n'))
        {
            if (line.Length is 0 || line[0] is '#')
                continue;

            yield return line.TrimEnd('\r').Split('\t');
        }
    }

    // The files of tests/Meziantou.Framework.Geo.Tests/TestData with an extension, named "<folder>-<file>"
    private static IEnumerable<(string Name, byte[] Data)> GetTestFiles(string extension)
    {
        var assembly = typeof(GeoFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(TestDataPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var name = resourceName[TestDataPrefix.Length..].Replace('\\', '/');
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (name.Replace('/', '-'), memory.ToArray());
        }
    }
}
