#pragma warning disable MA0004 // Use Task.ConfigureAwait
using System.Text;

namespace Meziantou.Framework.Geo.Maps.Tool.Sources;

/// <summary>
/// The populated places, rivers, lakes, marine areas and physical regions of Natural Earth at 1:10m (public domain), from
/// the mirror of the Natural Earth repository on GitHub, at a pinned commit. The cities are selected by the ISO code of
/// their country, so that a small country keeps its capital even when the point is just outside its border; the other
/// layers are cut to the bounds of the region.
/// </summary>
internal sealed class NaturalEarthSource
{
    // 1:10m data is already generalized: 0.0005° (about 50 meters) removes positions without a visible change
    private const double Tolerance = 0.0005;

    private readonly GeoFeatureCollection _places;
    private readonly List<(GeoFeature Feature, GeoBounds Bounds)> _rivers;
    private readonly List<(GeoFeature Feature, GeoBounds Bounds)> _lakes;
    private readonly List<(GeoFeature Feature, GeoBounds Bounds)> _marineAreas;
    private readonly List<(GeoFeature Feature, GeoBounds Bounds)> _physicalRegions;

    private NaturalEarthSource(GeoFeatureCollection places, GeoFeatureCollection rivers, GeoFeatureCollection lakes, GeoFeatureCollection marineAreas, IEnumerable<GeoFeature> physicalRegions)
    {
        _places = places;
        _rivers = Prepare(rivers, repair: false, GetName);
        _lakes = Prepare(lakes, repair: true, GetName);
        _marineAreas = Prepare(marineAreas, repair: true, GetClassification);
        _physicalRegions = Prepare(physicalRegions, repair: true, GetClassification);
    }

    public static IEnumerable<string> GetUrls(string commit) =>
    [
        .. GetShapefileUrls(commit, "10m_cultural", "ne_10m_populated_places"),
        .. GetShapefileUrls(commit, "10m_physical", "ne_10m_rivers_lake_centerlines"),
        .. GetShapefileUrls(commit, "10m_physical", "ne_10m_lakes"),
        .. GetShapefileUrls(commit, "10m_physical", "ne_10m_geography_marine_polys"),
        .. GetShapefileUrls(commit, "10m_physical", "ne_10m_geography_regions_polys"),
        .. GetShapefileUrls(commit, "10m_physical", "ne_10m_geography_regions_points"),
    ];

    public static async Task<NaturalEarthSource> LoadAsync(Downloader downloader, string commit)
    {
        var places = await ReadAsync(downloader, commit, "10m_cultural", "ne_10m_populated_places");
        var rivers = await ReadAsync(downloader, commit, "10m_physical", "ne_10m_rivers_lake_centerlines");
        var lakes = await ReadAsync(downloader, commit, "10m_physical", "ne_10m_lakes");
        var marineAreas = await ReadAsync(downloader, commit, "10m_physical", "ne_10m_geography_marine_polys");
        var regions = await ReadAsync(downloader, commit, "10m_physical", "ne_10m_geography_regions_polys");
        var regionPoints = await ReadAsync(downloader, commit, "10m_physical", "ne_10m_geography_regions_points");

        // Null Island, at 0°, 0°, is a placeholder of Natural Earth, not a place
        var physicalRegions = regions.Concat(regionPoints).Where(feature => !string.Equals(GetText(feature, "featurecla"), "Dragons-be-here", StringComparison.OrdinalIgnoreCase));
        return new NaturalEarthSource(places, rivers, lakes, marineAreas, physicalRegions);
    }

    /// <summary>Gets the populated places of countries, in the order of the source, with their <c>name</c>, <c>country</c>, <c>population</c>, <c>rank</c> and <c>capital</c>.</summary>
    public IReadOnlyList<GeoFeature> GetCities(IReadOnlySet<string> iso) =>
    [
        .. _places
            .Where(place => GetText(place, "ISO_A2") is { } country && iso.Contains(country))
            .Select(place => new GeoFeature(place.Geometry, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = GetText(place, "NAME"),
                ["country"] = GetText(place, "ISO_A2"),
                ["population"] = (long)GetNumber(place, "POP_MAX"),
                ["rank"] = (int)GetNumber(place, "RANK_MAX"),
                ["capital"] = GetNumber(place, "ADM0CAP") == 1,
            })),
    ];

    /// <summary>Gets the named rivers, cut to a box and simplified.</summary>
    public IReadOnlyList<GeoFeature> GetRivers(GeoBounds bounds) => Clip(_rivers, bounds);

    /// <summary>Gets the named lakes, cut to a box and simplified.</summary>
    public IReadOnlyList<GeoFeature> GetLakes(GeoBounds bounds) => Clip(_lakes, bounds);

    /// <summary>Gets the named seas, gulfs, bays, straits and other areas of the oceans, cut to a box and simplified, with their <c>name</c>, <c>kind</c> and <c>scale_rank</c>.</summary>
    public IReadOnlyList<GeoFeature> GetMarineAreas(GeoBounds bounds) => Clip(_marineAreas, bounds);

    /// <summary>Gets the named physical regions (polygons) and features (points), cut to a box and simplified, with their <c>name</c>, <c>kind</c> and <c>scale_rank</c>.</summary>
    public IReadOnlyList<GeoFeature> GetPhysicalRegions(GeoBounds bounds) => Clip(_physicalRegions, bounds);

    private static List<GeoFeature> Clip(List<(GeoFeature Feature, GeoBounds Bounds)> source, GeoBounds bounds)
    {
        var result = new List<GeoFeature>();
        foreach (var (feature, featureBounds) in source)
        {
            if (!GeoBoxes.Intersects(bounds, featureBounds) || feature.Geometry!.Clip(bounds)?.Simplify(Tolerance) is not { } geometry)
                continue;

            result.Add(new GeoFeature(geometry, feature.Properties));
        }

        return result;
    }

    private static List<(GeoFeature Feature, GeoBounds Bounds)> Prepare(IEnumerable<GeoFeature> source, bool repair, Func<GeoFeature, Dictionary<string, object?>> getProperties) =>
    [
        .. source
            .Where(feature => feature.Geometry is { IsEmpty: false } && GetText(feature, "name") is { Length: > 0 })
            .AsParallel()
            .AsOrdered()
            .Select(feature =>
            {
                var geometry = repair ? feature.Geometry!.MakeValid() : feature.Geometry!;
                return (new GeoFeature(geometry, getProperties(feature)), geometry.GetBounds()!.Value);
            }),
    ];

    private static Dictionary<string, object?> GetName(GeoFeature feature) => new(StringComparer.Ordinal) { ["name"] = GetText(feature, "name") };

    // The class of Natural Earth in lowercase (sea, range/mtn, cape...), and its scale rank, to choose the features to label
    private static Dictionary<string, object?> GetClassification(GeoFeature feature) => new(StringComparer.Ordinal)
    {
        ["name"] = GetText(feature, "name"),
        ["kind"] = GetText(feature, "featurecla")?.ToLowerInvariant(),
        ["scale_rank"] = (int)GetNumber(feature, "scalerank"),
    };

    public static async Task<GeoFeatureCollection> ReadAsync(Downloader downloader, string commit, string category, string name)
    {
        var urls = GetShapefileUrls(commit, category, name).ToArray();
        var shp = await downloader.GetAsync(urls[0]);
        var dbf = await downloader.GetAsync(urls[1]);
        return Shapefile.Read(new MemoryStream(shp), new MemoryStream(dbf), new ShapefileReaderOptions { Encoding = Encoding.UTF8 });
    }

    internal static IEnumerable<string> GetShapefileUrls(string commit, string category, string name) =>
    [
        $"https://raw.githubusercontent.com/nvkelso/natural-earth-vector/{commit}/{category}/{name}.shp",
        $"https://raw.githubusercontent.com/nvkelso/natural-earth-vector/{commit}/{category}/{name}.dbf",
    ];

    /// <summary>Gets a property, ignoring the case of its name: the columns of the dBASE files of Natural Earth are not all in the same case.</summary>
    private static object? GetProperty(GeoFeature feature, string name)
    {
        foreach (var (key, value) in feature.Properties)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static string? GetText(GeoFeature feature, string name) => GetProperty(feature, name) is string { Length: > 0 } text ? text : null;

    private static double GetNumber(GeoFeature feature, string name) => GetProperty(feature, name) is double number ? number : 0;
}
