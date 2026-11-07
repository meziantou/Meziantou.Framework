using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Meziantou.Framework.Geo.Maps.Tool;
using Meziantou.Framework.Geo.Maps.Tool.Sources;

namespace Meziantou.Framework.Geo.Maps.Tool.Tests;

/// <summary>
/// Small sources in the formats of the real ones (country-levels, Natural Earth, CLDR), in a cache folder, with the pins
/// of their hashes: the tool builds the bundles of Switzerland, France, Europe and the world without network access.
/// </summary>
internal static class TestSources
{
    public const string NaturalEarthCommit = "test-commit";
    public const string CldrVersion = "test-cldr";
    public const string Date = "2026-01-02";

    public static (FullPath Pins, FullPath Cache) Create(FullPath directory)
    {
        var cache = directory / "cache";
        Directory.CreateDirectory(cache);
        var files = new JsonObject();
        void Add(string url, byte[] content)
        {
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
            File.WriteAllBytes(cache / (sha256 + "-" + Path.GetFileName(new Uri(url).AbsolutePath)), content);
            files[url] = sha256;
        }

        var countryLevels = CreateCountryLevels();
        Add(CountryLevelsSource.BordersUrl, countryLevels);
        Add(CountryLevelsSource.SubdivisionsUrl, countryLevels);
        Add(Regions.ContainmentUrl(CldrVersion), Encoding.UTF8.GetBytes("""{"supplemental":{"territoryContainment":{"001":{"_contains":["150"]},"150":{"_contains":["155"]},"155":{"_contains":["CH","FR"]}}}}"""));
        Add(Regions.NamesUrl(CldrVersion), Encoding.UTF8.GetBytes("""{"main":{"en":{"localeDisplayNames":{"territories":{"CH":"Switzerland","FR":"France"}}}}}"""));

        AddShapefile("10m_cultural", "ne_10m_populated_places",
            City("Bern", "CH", 7.45, 46.95, 140_000, 9, capital: true),
            City("Paris", "FR", 2.35, 48.86, 11_000_000, 14, capital: true),
            City("Lyon", "FR", 4.83, 45.76, 1_600_000, 12, capital: false));
        AddShapefile("10m_physical", "ne_10m_rivers_lake_centerlines", Named("Rhône", new GeoLineString([new(6.5, 46.2), new(4.8, 45.7)])));
        AddShapefile("10m_physical", "ne_10m_lakes", Named("Lake Geneva", Box(6.2, 46.2, 6.9, 46.5)));
        AddShapefile("10m_physical", "ne_10m_land", new GeoFeature(Box(-1, 40, 20, 50)));
        AddShapefile("10m_physical", "ne_10m_ocean", new GeoFeature(Box(-10, 40, -1, 50)));
        AddShapefile("10m_physical", "ne_10m_coastline", new GeoFeature(new GeoLineString([new(-1, 40), new(-1, 50)])));
        AddShapefile("10m_physical", "ne_10m_geography_marine_polys", Classified("Bay of Biscay", "bay", 2, Box(-6, 43, -1.5, 47)));
        AddShapefile("10m_physical", "ne_10m_geography_regions_polys", Classified("ALPS", "Range/mtn", 1, Box(6.5, 45, 12, 47.5)));
        AddShapefile("10m_physical", "ne_10m_geography_regions_points",
            Classified("Pointe du Raz", "cape", 5, new GeoPoint(new(-1.9, 48))),
            Classified("Null Island", "Dragons-be-here", 100, new GeoPoint(new(0, 0))));

        var pins = new JsonObject
        {
            ["date"] = Date,
            ["naturalEarthCommit"] = NaturalEarthCommit,
            ["cldrVersion"] = CldrVersion,
            ["files"] = files,
        };
        var pinsPath = directory / "pins.json";
        File.WriteAllText(pinsPath, pins.ToJsonString());
        return (pinsPath, cache);

        void AddShapefile(string category, string name, params GeoFeature[] features)
        {
            using var shp = new MemoryStream();
            using var shx = new MemoryStream();
            using var dbf = new MemoryStream();
            Shapefile.Write([.. features], shp, shx, dbf);
            var urls = NaturalEarthSource.GetShapefileUrls(NaturalEarthCommit, category, name).ToArray();
            Add(urls[0], shp.ToArray());
            Add(urls[1], dbf.ToArray());
        }
    }

    // The exports of country-levels: a GeoJSON file per country (iso1) and per subdivision (iso2)
    private static byte[] CreateCountryLevels()
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            AddEntry(tar, "geojson/iso1/ch.geojson", "Switzerland", 2, Box(6, 45, 10, 48));
            AddEntry(tar, "geojson/iso1/fr.geojson", "France", 2, Box(-2, 43, 6, 49));
            AddEntry(tar, "geojson/iso2/ch-be.geojson", "Bern", 4, Box(6, 45, 8, 48));
            AddEntry(tar, "geojson/iso2/ch-zh.geojson", "Zürich", 4, Box(8, 45, 10, 48));
            AddEntry(tar, "geojson/iso2/fr-idf.geojson", "Île-de-France", 4, Box(-2, 43, 6, 49));
        }

        return stream.ToArray();

        static void AddEntry(TarWriter tar, string name, string featureName, int adminLevel, GeoGeometry geometry)
        {
            var json = $$"""{"type":"Feature","properties":{"name":"{{featureName}}","admin_level":{{adminLevel}}},"geometry":{{GeoJson.FormatGeometry(geometry)}}}""";
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(json)) });
        }
    }

    private static GeoPolygon Box(double west, double south, double east, double north) => new([[new(west, south), new(east, south), new(east, north), new(west, north), new(west, south)]]);

    private static GeoFeature Named(string name, GeoGeometry geometry) => new(geometry, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name });

    private static GeoFeature Classified(string name, string kind, int rank, GeoGeometry geometry) => new(geometry, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name, ["featurecla"] = kind, ["scalerank"] = rank });

    private static GeoFeature City(string name, string country, double longitude, double latitude, long population, int rank, bool capital) => new(new GeoPoint(new(longitude, latitude)), new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["NAME"] = name,
        ["ISO_A2"] = country,
        ["POP_MAX"] = population,
        ["RANK_MAX"] = rank,
        ["ADM0CAP"] = capital ? 1 : 0,
    });
}
