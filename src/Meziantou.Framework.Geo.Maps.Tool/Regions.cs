using System.Text;
using System.Text.Json;
using Meziantou.Framework.Geo.Maps.Tool.Sources;

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>
/// The regions: the world, the continents, and a region per country or territory that country-levels has a border for.
/// The countries are grouped into continents by the territory containment of Unicode CLDR (the UN M49 regions), and
/// named after their English name in CLDR.
/// </summary>
internal static class Regions
{
    public static readonly Region World = new("world", Parent: null, "World", [], IsWorld: true);

    // The continents, from the regions of CLDR: North America is CLDR's grouping of Northern America, Central America and
    // the Caribbean. Antarctica is in Outlying Oceania for CLDR, but is a continent of its own on a map.
    private static readonly (string Code, string Id, string Name)[] Continents =
    [
        ("002", "africa", "Africa"),
        ("AQ", "antarctica", "Antarctica"),
        ("142", "asia", "Asia"),
        ("150", "europe", "Europe"),
        ("003", "north-america", "North America"),
        ("009", "oceania", "Oceania"),
        ("005", "south-america", "South America"),
    ];

    // The members that do not extend the extent of their continent: Russia, in Eastern Europe for CLDR, would stretch the
    // map of Europe to the Pacific, and remote islands the maps of Africa (the British Indian Ocean Territory, the French
    // Southern Territories) and of South America (Bouvet Island, South Georgia). Their borders are still in the layers.
    private static readonly Dictionary<string, string[]> ExtentExclusions = new(StringComparer.Ordinal)
    {
        ["africa"] = ["IO", "TF"],
        ["europe"] = ["RU"],
        ["south-america"] = ["BV", "GS"],
    };

    // The extents of the continents that have no border to give one: Antarctica, from the limit of the Antarctic Treaty, 60°S,
    // to the pole, so that its land, ice, seas and physical regions are cut to it, and that the world includes it
    private static readonly Dictionary<string, GeoBounds> Extents = new(StringComparer.Ordinal)
    {
        ["antarctica"] = new GeoBounds(West: -180, South: -90, East: 180, North: -60),
    };

    // The codes of country-levels that are part of another country: FX is metropolitan France, which FR covers
    private static readonly HashSet<string> IgnoredCodes = new(StringComparer.Ordinal) { "FX" };

    // The English names of CLDR that are longer than the name a map shows
    private static readonly HashSet<string> ShortNames = new(StringComparer.Ordinal) { "HK", "MM", "MO", "PS" };

    public static string ContainmentUrl(string version) => $"https://raw.githubusercontent.com/unicode-org/cldr-json/{version}/cldr-json/cldr-core/supplemental/territoryContainment.json";

    public static string NamesUrl(string version) => $"https://raw.githubusercontent.com/unicode-org/cldr-json/{version}/cldr-json/cldr-localenames-full/main/en/territories.json";

    public static async Task<IReadOnlyList<Region>> LoadAsync(Downloader downloader, string cldrVersion, CountryLevelsSource countryLevels)
    {
        using var containment = JsonDocument.Parse(await downloader.GetAsync(ContainmentUrl(cldrVersion)).ConfigureAwait(false));
        using var names = JsonDocument.Parse(await downloader.GetAsync(NamesUrl(cldrVersion)).ConfigureAwait(false));

        var tree = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var region in containment.RootElement.GetProperty("supplemental").GetProperty("territoryContainment").EnumerateObject())
        {
            if (region.Value.TryGetProperty("_contains", out var children))
            {
                tree[region.Name] = [.. children.EnumerateArray().Select(child => child.GetString()!)];
            }
        }

        var territories = names.RootElement.GetProperty("main").GetProperty("en").GetProperty("localeDisplayNames").GetProperty("territories");
        var englishNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in territories.EnumerateObject())
        {
            englishNames[name.Name] = name.Value.GetString()!;
        }

        return Build(tree, englishNames, countryLevels.Countries);
    }

    /// <summary>Builds the regions from the territory containment of CLDR, its English names, and the countries that have a border.</summary>
    public static IReadOnlyList<Region> Build(IReadOnlyDictionary<string, string[]> tree, IReadOnlyDictionary<string, string> names, IEnumerable<string> countries)
    {
        // The continent of each country: the first continent whose tree contains it
        var continentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, id, _) in Continents)
        {
            foreach (var country in GetCountries(tree, code))
            {
                continentOf.TryAdd(country, id);
            }
        }

        var regions = new List<Region> { World };
        regions.AddRange(Continents.Select(continent => new Region(continent.Id, Parent: null, continent.Name, tree.ContainsKey(continent.Code) ? [] : [continent.Code], ExtentExclusions: ExtentExclusions.GetValueOrDefault(continent.Id) ?? [], Extent: Extents.TryGetValue(continent.Id, out var extent) ? extent : null)));

        var unplaced = new List<string>();
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var code in countries.Select(code => code.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (IgnoredCodes.Contains(code) || Continents.Any(continent => continent.Code == code))
                continue;

            if (!continentOf.TryGetValue(code, out var continent))
            {
                unplaced.Add(code);
                continue;
            }

            var name = (ShortNames.Contains(code) ? names.GetValueOrDefault(code + "-alt-short") : null) ?? names.GetValueOrDefault(code) ?? throw new InvalidOperationException($"CLDR has no English name for {code}.");
            var id = GetId(name);
            if (!byId.TryAdd(id, code))
                throw new InvalidOperationException($"{byId[id]} and {code} have the same identifier, {id}.");

            regions.Add(new Region(id, continent, name, [code]));
        }

        // A country that is in no continent would be in no map but its own: the build fails instead
        if (unplaced.Count > 0)
            throw new InvalidOperationException($"CLDR places {string.Join(", ", unplaced)} in no continent: add them to the continents, or to the ignored codes when another country covers them.");

        return regions;
    }

    /// <summary>Gets the regions whose ISO codes make up a region: every country for the world, the countries of a continent (and the continent when it has a code of its own, such as Antarctica), or the country itself.</summary>
    public static IReadOnlyList<Region> GetMembers(Region region, IReadOnlyList<Region> regions)
    {
        if (region.IsWorld)
            return [.. regions.Where(candidate => candidate.Iso.Length > 0)];

        if (region.IsContinent)
        {
            var children = regions.Where(candidate => string.Equals(candidate.Parent, region.Id, StringComparison.Ordinal) && candidate.Iso.Length > 0);
            return region.Iso.Length > 0 ? [region, .. children] : [.. children];
        }

        return region.Iso.Length > 0 ? [region] : [];
    }

    /// <summary>Gets the identifier of a region from its English name: lowercase ASCII words joined by hyphens, such as <c>cote-divoire</c> or <c>bosnia-and-herzegovina</c>.</summary>
    public static string GetId(string name) => string.Join('-', GetWords(name)).ToLowerInvariant();

    /// <summary>Gets the key of a region from its English name: its ASCII words in PascalCase, such as <c>CoteDIvoire</c> or <c>USVirginIslands</c>.</summary>
    public static string GetKey(string name) => string.Concat(GetWords(name).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    // The letters U+00C0 to U+00FF without their accents: the English names of CLDR only use these, such as Côte d’Ivoire or
    // São Tomé & Príncipe. A table, rather than the decomposition of Unicode, does not depend on ICU
    private static readonly string[] Latin1Letters =
    [
        "A", "A", "A", "A", "A", "A", "AE", "C", "E", "E", "E", "E", "I", "I", "I", "I",
        "D", "N", "O", "O", "O", "O", "O", "", "O", "U", "U", "U", "U", "Y", "Th", "ss",
        "a", "a", "a", "a", "a", "a", "ae", "c", "e", "e", "e", "e", "i", "i", "i", "i",
        "d", "n", "o", "o", "o", "o", "o", "", "o", "u", "u", "u", "u", "y", "th", "y",
    ];

    // The words of a name, without accents and punctuation: "St. Kitts & Nevis" is St, Kitts, and, Nevis
    private static List<string> GetWords(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in name.Replace("&", " and ", StringComparison.Ordinal))
        {
            if (c is '.' or '\'' or '’')
                continue;

            if (char.IsAsciiLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (c is >= '\u00C0' and <= '\u00FF' && Latin1Letters[c - '\u00C0'] is { Length: > 0 } letters)
            {
                word.Append(letters);
            }
            else if (char.IsLetter(c))
            {
                throw new InvalidOperationException($"The name '{name}' has the letter '{c}', which has no ASCII equivalent in the table of the identifiers of the regions.");
            }
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static IEnumerable<string> GetCountries(IReadOnlyDictionary<string, string[]> tree, string code)
    {
        if (!tree.TryGetValue(code, out var children))
        {
            yield return code;
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var country in GetCountries(tree, child))
            {
                yield return country;
            }
        }
    }
}
