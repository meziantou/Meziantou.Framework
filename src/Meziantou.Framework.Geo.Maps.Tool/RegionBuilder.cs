using Meziantou.Framework.Geo.Maps.GeoPackage;
using Meziantou.Framework.Geo.Maps.Tool.Sources;

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>Builds the layers of a region and writes its map in one of the formats of Meziantou.Framework.Geo.Maps.</summary>
internal static class RegionBuilder
{
    public static readonly MapFormat[] AllFormats = [MapFormat.FlatGeobuf, MapFormat.GeoJson, GeoPackageMapFormat.Instance];

    public const string Attribution = "© OpenStreetMap contributors (ODbL): borders and subdivisions via country-levels. Cities, rivers, lakes, land, ocean, coastline, marine areas and physical regions: Natural Earth (public domain).";

    /// <summary>Builds the selected layers of a region. The borders are read even when they are not selected: they give the extent of the region.</summary>
    public static Dictionary<MapLayer, GeoFeatureCollection> BuildLayers(Region region, SourceSet sources, IReadOnlySet<MapLayer> selected, out GeoBounds? extent)
    {
        // The ISO codes in a fixed order, so that the bundles are the same from one run to the next
        var members = Regions.GetMembers(region, sources.Regions);
        var iso = members
            .SelectMany(member => member.Iso)
            .Select(code => code.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var isoSet = iso.ToHashSet(StringComparer.Ordinal);

        var layers = new Dictionary<MapLayer, GeoFeatureCollection>();
        var borders = iso.Select(sources.CountryLevels.GetBorder).OfType<GeoFeature>().ToList();
        if (selected.Contains(MapLayer.Borders))
        {
            Add(layers, MapLayer.Borders, borders);
        }

        if (selected.Contains(MapLayer.StatesProvinces))
        {
            Add(layers, MapLayer.StatesProvinces, iso.SelectMany(code => GetSubdivisions(code, sources)));
        }

        if (selected.Contains(MapLayer.Cities))
        {
            Add(layers, MapLayer.Cities, sources.NaturalEarth.GetCities(isoSet));
        }

        // The other layers are cut to the extent of the borders, or to the fixed extent of a region without border (Antarctica)
        var exclusions = region.ExtentExclusions ?? [];
        extent = GeoBoxes.GetMainExtent(borders.Where(border => !exclusions.Contains((string?)border.Properties["iso"], StringComparer.OrdinalIgnoreCase)));

        // A member without border, such as Antarctica, has a fixed extent
        foreach (var member in members)
        {
            if (member.Extent is { } memberExtent)
            {
                extent = extent is { } current ? current.Union(memberExtent) : memberExtent;
            }
        }

        if (extent is { } bounds)
        {
            if (selected.Contains(MapLayer.Rivers))
            {
                Add(layers, MapLayer.Rivers, sources.NaturalEarth.GetRivers(bounds));
            }

            if (selected.Contains(MapLayer.Lakes))
            {
                Add(layers, MapLayer.Lakes, sources.NaturalEarth.GetLakes(bounds));
            }

            if (selected.Contains(MapLayer.MarineAreas))
            {
                Add(layers, MapLayer.MarineAreas, sources.NaturalEarth.GetMarineAreas(bounds));
            }

            if (selected.Contains(MapLayer.PhysicalRegions))
            {
                Add(layers, MapLayer.PhysicalRegions, sources.NaturalEarth.GetPhysicalRegions(bounds));
            }

            // Without ocean in the box, the land is the whole box: it is left out, as the coastline. The ocean is cut even
            // when it is not selected, to know whether the land and the coastline are
            if (selected.Contains(MapLayer.Ocean) || selected.Contains(MapLayer.Land) || selected.Contains(MapLayer.Coastline))
            {
                var ocean = sources.BaseLayers.GetOcean(bounds);
                if (ocean.Count > 0)
                {
                    if (selected.Contains(MapLayer.Ocean))
                    {
                        Add(layers, MapLayer.Ocean, ocean);
                    }

                    if (selected.Contains(MapLayer.Land))
                    {
                        Add(layers, MapLayer.Land, sources.BaseLayers.GetLand(bounds));
                    }

                    if (selected.Contains(MapLayer.Coastline))
                    {
                        Add(layers, MapLayer.Coastline, sources.BaseLayers.GetCoastline(bounds));
                    }
                }
            }
        }

        return layers;
    }

    /// <summary>Writes the map of a region in a format, and returns its path: a folder for the folder formats, a file for GeoPackage.</summary>
    public static string Write(Region region, Dictionary<MapLayer, GeoFeatureCollection> layers, GeoBounds? bounds, MapFormat format, string directory, DateTimeOffset? date)
    {
        // A map of the region in another format is replaced too, so that the folder holds one map per region
        foreach (var other in AllFormats)
        {
            var otherPath = other.GetPath(directory, region.Key);
            if (Directory.Exists(otherPath))
            {
                Directory.Delete(otherPath, recursive: true);
            }
            else if (File.Exists(otherPath))
            {
                File.Delete(otherPath);
            }
        }

        var path = format.GetPath(directory, region.Key);
        var metadata = new MapMetadata(region.Key)
        {
            Name = region.Name,
            Attribution = Attribution,
            Bounds = bounds,
            LastModified = date,
            Sources =
            [
                new MapSource("country-levels", new Uri("https://github.com/hyperknot/country-levels"), "ODbL-1.0"),
                new MapSource("Nominatim", new Uri("https://nominatim.openstreetmap.org/"), "ODbL-1.0"),
                new MapSource("Natural Earth", new Uri("https://www.naturalearthdata.com/"), "public domain"),
            ],
        };
        format.Write(path, metadata, layers);
        return path;
    }

    // The subdivisions of country-levels and of the backfill: a code that is in both would be drawn twice, so the build fails
    private static List<GeoFeature> GetSubdivisions(string country, SourceSet sources)
    {
        var subdivisions = sources.CountryLevels.GetSubdivisions(country).Concat(sources.Backfill.GetSubdivisions(country)).ToList();
        var duplicates = subdivisions.GroupBy(feature => (string?)feature.Properties["iso"], StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => $"{group.Key} ({string.Join(", ", group.Select(feature => feature.Properties["name"]))})").ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"The subdivisions of {country} have the same code twice: {string.Join("; ", duplicates)}.");

        return subdivisions;
    }

    private static void Add(Dictionary<MapLayer, GeoFeatureCollection> layers, MapLayer layer, IEnumerable<GeoFeature> features)
    {
        var collection = new GeoFeatureCollection(features) { Name = layer.ToString() };
        if (collection.Count > 0)
        {
            layers[layer] = collection;
        }
    }
}
