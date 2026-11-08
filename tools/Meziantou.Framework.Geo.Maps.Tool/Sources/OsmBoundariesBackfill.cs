namespace Meziantou.Framework.Geo.Maps.Tool.Sources;

/// <summary>
/// Subdivisions that country-levels misses, because its ISO 3166-2 data predates a reform: the wilayas Algeria created in
/// 2019, and the provinces of the DR Congo of 2015 that country-levels omits. Their boundaries are OpenStreetMap
/// relations (ODbL), embedded in the tool as the answers of the lookup of Nominatim, so that the tool does not query
/// Nominatim and that its output does not change when the relations are edited.
/// </summary>
internal sealed class OsmBoundariesBackfill
{
    private static readonly (string Country, string Iso, string Name, long Relation)[] Entries =
    [
        ("DZ", "DZ-49", "Timimoun", 6528164),
        ("DZ", "DZ-50", "Bordj Badji Mokhtar", 6528163),
        ("DZ", "DZ-52", "Béni Abbès", 6824843),
        ("DZ", "DZ-53", "In Salah", 6824900),
        ("DZ", "DZ-55", "Touggourt", 6822397),
        ("DZ", "DZ-56", "Djanet", 6825876),
        ("DZ", "DZ-58", "El Meniaa", 6825901),
        ("CD", "CD-EQ", "Équateur", 193084),
        ("CD", "CD-MN", "Mai-Ndombe", 5647368),
    ];

    private readonly Dictionary<string, List<GeoFeature>> _byCountry;

    private OsmBoundariesBackfill(Dictionary<string, List<GeoFeature>> byCountry) => _byCountry = byCountry;

    public static OsmBoundariesBackfill Load()
    {
        var byCountry = new Dictionary<string, List<GeoFeature>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (country, iso, name, relation) in Entries)
        {
            using var stream = typeof(OsmBoundariesBackfill).Assembly.GetManifestResourceStream(GetResourceName(relation))
                ?? throw new InvalidOperationException($"The boundary of the relation {relation} ({name}) is not embedded in the tool.");
            if (GeoJson.Read(stream).FirstOrDefault()?.Geometry is not { } geometry)
                throw new InvalidOperationException($"The boundary of the relation {relation} ({name}) has no geometry.");

            var feature = new GeoFeature(geometry.MakeValid(), new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["iso"] = iso,
                ["country"] = country,
                ["admin_level"] = 4,
            });

            if (!byCountry.TryGetValue(country, out var list))
            {
                list = [];
                byCountry[country] = list;
            }

            list.Add(feature);
        }

        return new OsmBoundariesBackfill(byCountry);
    }

    public IReadOnlyList<GeoFeature> GetSubdivisions(string iso) => _byCountry.GetValueOrDefault(iso) ?? [];

    /// <summary>Gets the address of the answer of Nominatim the tool embeds for a relation, to refresh it.</summary>
    public static string GetLookupUrl(long relation) => $"https://nominatim.openstreetmap.org/lookup?osm_ids=R{relation}&format=geojson&polygon_geojson=1";

    private static string GetResourceName(long relation) => "Boundaries.R" + relation.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".geojson";
}
