#pragma warning disable MA0004 // Use Task.ConfigureAwait
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Geo.Maps.Tool.Sources;

/// <summary>
/// The administrative boundaries of country-levels, derived from OpenStreetMap (ODbL): the borders of the countries
/// (iso1, ISO 3166-1) from the detailed export, and their subdivisions (iso2, ISO 3166-2) from the medium export, which
/// keeps the bundles small. The geometries are repaired once, when they are loaded.
/// </summary>
internal sealed class CountryLevelsSource
{
    public const string BordersUrl = "https://github.com/hyperknot/country-levels/releases/download/v2.2.0/export_high.tgz";
    public const string SubdivisionsUrl = "https://github.com/hyperknot/country-levels/releases/download/v2.2.0/export_medium.tgz";

    // Subdivisions whose ISO 3166-2 code is wrong in country-levels: two of the wilayas Algeria created in 2019 have the
    // code of another new wilaya, which the backfill of OpenStreetMap adds. The build fails when the name no longer
    // matches, so that a fixed export of country-levels is reviewed.
    private static readonly Dictionary<string, (string Name, string Iso)> IsoCorrections = new(StringComparer.Ordinal)
    {
        ["DZ-49"] = ("El M'Ghair", "DZ-57"),
        ["DZ-58"] = ("In Guezzam", "DZ-54"),
    };

    private readonly Dictionary<string, GeoFeature> _borders;
    private readonly Dictionary<string, List<GeoFeature>> _subdivisions;

    private CountryLevelsSource(Dictionary<string, GeoFeature> borders, Dictionary<string, List<GeoFeature>> subdivisions)
    {
        _borders = borders;
        _subdivisions = subdivisions;
    }

    /// <summary>Gets the border of a country, with its <c>name</c> and <c>iso</c> code.</summary>
    public GeoFeature? GetBorder(string iso) => _borders.GetValueOrDefault(iso);

    /// <summary>Gets the ISO 3166-1 alpha-2 codes of the countries and territories that have a border.</summary>
    public IEnumerable<string> Countries => _borders.Keys;

    /// <summary>Gets the subdivisions of a country, ordered by ISO 3166-2 code, with their <c>name</c>, <c>iso</c>, <c>country</c> and <c>admin_level</c>.</summary>
    public IReadOnlyList<GeoFeature> GetSubdivisions(string iso) => _subdivisions.GetValueOrDefault(iso) ?? [];

    public static async Task<CountryLevelsSource> LoadAsync(Downloader downloader)
    {
        var borders = Read(await downloader.GetAsync(BordersUrl), "iso1")
            .ToDictionary(feature => (string)feature.Properties["iso"]!, StringComparer.OrdinalIgnoreCase);
        var subdivisions = Read(await downloader.GetAsync(SubdivisionsUrl), "iso2")
            .GroupBy(feature => (string)feature.Properties["country"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(feature => (string)feature.Properties["iso"]!, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);
        return new CountryLevelsSource(borders, subdivisions);
    }

    /// <summary>Reads the GeoJSON files of a folder of the export, in parallel, in the order of their ISO codes so that the bundles are the same from one run to the next.</summary>
    private static List<GeoFeature> Read(byte[] archive, string level)
    {
        var files = new List<(string Code, string Json)>();
        using (var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress))
        using (var tar = new TarReader(gzip))
        {
            while (tar.GetNextEntry() is { } entry)
            {
                var segments = entry.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (entry.DataStream is null || segments.Length < 3 || segments[^1].EndsWith(".geojson", StringComparison.Ordinal) is false || !segments.Contains(level, StringComparer.Ordinal))
                    continue;

                using var reader = new StreamReader(entry.DataStream, Encoding.UTF8);
                files.Add((Path.GetFileNameWithoutExtension(segments[^1]), reader.ReadToEnd()));
            }
        }

        return [.. files
            .OrderBy(file => file.Code, StringComparer.Ordinal)
            .AsParallel()
            .AsOrdered()
            .Select(file => ReadFeature(file.Code, file.Json, level))
            .OfType<GeoFeature>()];
    }

    private static GeoFeature? ReadFeature(string code, string json, string level)
    {
        var feature = GeoJson.Parse(json).FirstOrDefault();
        if (feature?.Geometry is not { } geometry)
            return null;

        var repaired = geometry.MakeValid();
        if (repaired.IsEmpty)
            return null;

        var country = code.Split('-')[0].ToUpperInvariant();
        var name = feature.Properties.GetValueOrDefault("name") as string;
        var iso = code.ToUpperInvariant();
        if (string.Equals(level, "iso2", StringComparison.Ordinal) && IsoCorrections.TryGetValue(iso, out var correction))
        {
            if (!string.Equals(name, correction.Name, StringComparison.Ordinal))
                throw new InvalidOperationException($"The correction of {iso} expects {correction.Name}, but country-levels names it {name}: check whether the export now has the right code.");

            iso = correction.Iso;
        }

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["iso"] = iso,
        };

        if (string.Equals(level, "iso2", StringComparison.Ordinal))
        {
            properties["country"] = country;
            properties["admin_level"] = GetInteger(feature.Properties.GetValueOrDefault("admin_level"));
        }

        return new GeoFeature(repaired, properties);
    }

    private static int? GetInteger(object? value) => value switch
    {
        double number => (int)number,
        JsonElement { ValueKind: JsonValueKind.Number } element => element.GetInt32(),
        _ => null,
    };
}
