namespace Meziantou.Framework.Geo.Benchmarks;

internal static class BenchmarkData
{
    /// <summary>The borders of 179 countries, a GeoJSON file of 256 KB with polygons and multi-polygons.</summary>
    public static string CountriesJson { get; } = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "countries.geojson"));

    public static GeoFeatureCollection Countries { get; } = GeoJson.Parse(CountriesJson);
}
