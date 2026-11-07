using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Geo.Maps;

/// <summary>The maps of a folder: a folder or a file per region, in one of the formats of the catalog.</summary>
/// <remarks>
/// By default, a catalog reads the folder formats, <see cref="MapFormat.FlatGeobuf"/> and <see cref="MapFormat.GeoJson"/>:
/// a region is a folder with a file per layer and a <c>metadata.json</c> file. Pass the formats to read other formats,
/// such as the <c>GeoPackageMapFormat</c> of <c>Meziantou.Framework.Geo.Maps.GeoPackage</c>. The
/// Meziantou.Framework.Geo.Maps.Tool .NET tool writes the maps; the packages of map bundles copy them to a <c>maps</c> folder
/// next to the application.
/// </remarks>
public sealed class MapCatalog
{
    private readonly MapFormat[] _formats;

    /// <summary>Initializes a new instance of the <see cref="MapCatalog"/> class that reads the folder formats, <see cref="MapFormat.FlatGeobuf"/> and <see cref="MapFormat.GeoJson"/>.</summary>
    /// <param name="directory">The folder that holds the maps.</param>
    public MapCatalog(string directory)
        : this(directory, DefaultFormats)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MapCatalog"/> class.</summary>
    /// <param name="directory">The folder that holds the maps.</param>
    /// <param name="formats">The formats of the maps, in order of preference: when a region has maps in several formats, the first format wins.</param>
    /// <exception cref="ArgumentException"><paramref name="formats"/> is empty.</exception>
    public MapCatalog(string directory, IEnumerable<MapFormat> formats)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(formats);
        Directory = Path.GetFullPath(directory);
        _formats = [.. formats];
        if (_formats.Length == 0)
            throw new ArgumentException("A catalog needs at least one format.", nameof(formats));
    }

    /// <summary>Gets the formats a catalog reads by default: <see cref="MapFormat.FlatGeobuf"/>, then <see cref="MapFormat.GeoJson"/>.</summary>
    public static IReadOnlyList<MapFormat> DefaultFormats { get; } = [MapFormat.FlatGeobuf, MapFormat.GeoJson];

    /// <summary>Gets the catalog of the <c>maps</c> folder next to the application, where the packages of map bundles copy their regions, with the default formats.</summary>
    public static MapCatalog Default { get; } = new(Path.Combine(AppContext.BaseDirectory, "maps"));

    /// <summary>Gets the full path of the folder of the catalog.</summary>
    public string Directory { get; }

    /// <summary>Gets the formats of the catalog, in order of preference.</summary>
    public IReadOnlyList<MapFormat> Formats => _formats;

    /// <summary>Gets the names of the regions of the catalog, in ordinal order, or an empty list when the folder does not exist.</summary>
    public IReadOnlyList<string> Regions
    {
        get
        {
            if (!System.IO.Directory.Exists(Directory))
                return [];

            var regions = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(Directory))
            {
                foreach (var format in _formats)
                {
                    if (format.TryGetRegion(entry, out var region))
                    {
                        regions.Add(region);
                        break;
                    }
                }
            }

            return [.. regions];
        }
    }

    /// <summary>Gets a value that indicates whether the catalog has the map of a region.</summary>
    /// <param name="region">The name of the region, such as <c>World</c>, <c>Europe</c> or <c>Switzerland</c>.</param>
    /// <returns><see langword="true"/> when the catalog has a map of the region in one of its formats; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="region"/> is empty or is not the name of a file.</exception>
    public bool Contains(string region) => FindFormat(region, out _) is not null;

    /// <summary>Gets the map of a region.</summary>
    /// <param name="region">The name of the region, such as <c>World</c>, <c>Europe</c> or <c>Switzerland</c>.</param>
    /// <returns>The map.</returns>
    /// <exception cref="ArgumentException"><paramref name="region"/> is empty or is not the name of a file.</exception>
    /// <exception cref="KeyNotFoundException">The catalog has no map of this region in its formats.</exception>
    /// <exception cref="FormatException">The metadata of the map is not valid.</exception>
    public Map GetMap(string region)
    {
        if (TryGetMap(region, out var map))
            return map;

        // A map in a format the catalog does not read, such as a GeoPackage file with the default formats
        var other = System.IO.Directory.Exists(Directory) ? System.IO.Directory.EnumerateFileSystemEntries(Directory, region + ".*").Select(Path.GetFileName).Order(StringComparer.Ordinal).FirstOrDefault() : null;
        if (other is not null)
        {
            var hint = other.EndsWith(".gpkg", StringComparison.OrdinalIgnoreCase) ? ": add the GeoPackageMapFormat of Meziantou.Framework.Geo.Maps.GeoPackage to the formats of the catalog" : ": add its format to the formats of the catalog";
            throw new KeyNotFoundException($"The catalog '{Directory}' has no map of the region '{region}' in the formats {string.Join(", ", _formats.Select(format => format.Name))}, but it has '{other}'{hint}.");
        }

        throw new KeyNotFoundException($"The catalog '{Directory}' has no map of the region '{region}'. Add the map bundle of the region, or one of a larger region, such as World.");
    }

    /// <summary>Gets the map of a region, when the catalog has it.</summary>
    /// <param name="region">The name of the region, such as <c>World</c>, <c>Europe</c> or <c>Switzerland</c>.</param>
    /// <param name="map">The map, or <see langword="null"/> when the catalog does not have it.</param>
    /// <returns><see langword="true"/> when the catalog has the map of the region; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="region"/> is empty or is not the name of a file.</exception>
    /// <exception cref="FormatException">The metadata of the map is not valid.</exception>
    public bool TryGetMap(string region, [NotNullWhen(true)] out Map? map)
    {
        var format = FindFormat(region, out var path);
        map = format?.Open(path!);
        return map is not null;
    }

    private MapFormat? FindFormat(string region, out string? path)
    {
        ArgumentException.ThrowIfNullOrEmpty(region);
        if (region is "." or ".." || region.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0 || region.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"'{region}' is not the name of a region: it must be the name of a file or a folder of the catalog.", nameof(region));

        foreach (var format in _formats)
        {
            var candidate = format.GetPath(Directory, region);
            if (format.TryGetRegion(candidate, out _))
            {
                path = candidate;
                return format;
            }
        }

        path = null;
        return null;
    }
}
