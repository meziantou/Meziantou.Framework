namespace Meziantou.Framework.Geo.Maps;

/// <summary>The description of the map of a region: its name, its bounds, and the attribution of its data.</summary>
public sealed class MapMetadata
{
    /// <summary>Initializes a new instance of the <see cref="MapMetadata"/> class.</summary>
    /// <param name="region">The name of the region in its catalog, such as <c>World</c>, <c>Europe</c> or <c>Switzerland</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="region"/> is empty.</exception>
    public MapMetadata(string region)
    {
        ArgumentException.ThrowIfNullOrEmpty(region);
        Region = region;
    }

    /// <summary>Gets the name of the region in its catalog, such as <c>World</c>, <c>Europe</c> or <c>Switzerland</c>.</summary>
    public string Region { get; }

    /// <summary>Gets the name of the region to show, such as <c>Switzerland</c>, or <see langword="null"/> to use <see cref="Region"/>.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the attribution the licenses of the data require, to show on the maps.</summary>
    public string? Attribution { get; init; }

    /// <summary>Gets the bounds of the region: the box of its borders, which the other layers are cut to.</summary>
    public GeoBounds? Bounds { get; init; }

    /// <summary>Gets the sources of the data of the map, with their licenses.</summary>
    public IReadOnlyList<MapSource> Sources { get; init; } = [];

    /// <summary>Gets the date of the data of the map: the formats that store a date, such as GeoPackage, write it.</summary>
    public DateTimeOffset? LastModified { get; init; }
}
