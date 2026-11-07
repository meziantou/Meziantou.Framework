using Meziantou.Framework.Geo.Maps.Internals;

namespace Meziantou.Framework.Geo.Maps;

/// <summary>The map of a region: its layers, read from the files of the map when they are loaded.</summary>
/// <remarks>
/// <para>The positions are longitudes and latitudes, in degrees (WGS 84).</para>
/// <para>
/// The properties that <see cref="MapLayer"/> documents have the same type in every format: a format that does not store
/// the types, such as GeoJSON, where every number is a <see cref="double"/>, gets the documented types, such as an
/// <see cref="int"/> for the <c>rank</c> of a city. The other properties are returned as the format reads them.
/// </para>
/// </remarks>
public abstract class Map
{
    private static readonly MapLayer[] AllLayers = Enum.GetValues<MapLayer>();

    private readonly Lazy<GeoBounds?> _bounds;

    /// <summary>Initializes a new instance of the <see cref="Map"/> class.</summary>
    /// <param name="metadata">The description of the map.</param>
    protected Map(MapMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Metadata = metadata;
        _bounds = metadata.Bounds is { } bounds ? new Lazy<GeoBounds?>(bounds) : new Lazy<GeoBounds?>(() => HasLayer(MapLayer.Borders) ? Load(MapLayer.Borders).GetBounds() : null);
    }

    /// <summary>Gets the description of the map, as its files store it.</summary>
    public MapMetadata Metadata { get; }

    /// <summary>Gets the name of the region in its catalog, such as <c>World</c>, <c>Europe</c> or <c>Switzerland</c>.</summary>
    public string Region => Metadata.Region;

    /// <summary>Gets the name of the region, from the metadata of the map, else <see cref="Region"/>.</summary>
    public string Name => Metadata.Name ?? Region;

    /// <summary>Gets the attribution the licenses of the data require, to show on the maps, or <see langword="null"/> when the metadata of the map has none.</summary>
    public string? Attribution => Metadata.Attribution;

    /// <summary>
    /// Gets the bounds of the region: the box of its borders, which the other layers are cut to. It comes from the
    /// metadata of the map, else from the <see cref="MapLayer.Borders"/> layer; <see langword="null"/> without either.
    /// </summary>
    /// <remarks>Use it as the extent of a map of the region.</remarks>
    public GeoBounds? Bounds => _bounds.Value;

    /// <summary>Gets the layers the map has: the layers whose source has nothing in the region, such as the coastline of a country without sea, are not there.</summary>
    public IReadOnlyList<MapLayer> Layers => [.. AllLayers.Where(HasLayer)];

    /// <summary>Gets a value that indicates whether the map has a layer.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns><see langword="true"/> when the map has the layer; otherwise, <see langword="false"/>.</returns>
    public abstract bool HasLayer(MapLayer layer);

    /// <summary>Reads the features of a layer.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns>The features, in a collection named after the layer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is not a layer.</exception>
    /// <exception cref="KeyNotFoundException">The map does not have the layer.</exception>
    /// <exception cref="FormatException">The layer is not valid.</exception>
    public GeoFeatureCollection Load(MapLayer layer) => LoadCore(layer, bounds: null);

    /// <summary>Reads the features of a layer whose bounds intersect a box, such as the cities of the part of the region a map shows.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="bounds">The box.</param>
    /// <returns>The features, in a collection named after the layer. They are not cut to the box: use <see cref="GeoGeometry.Clip"/> to cut them.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is not a layer.</exception>
    /// <exception cref="KeyNotFoundException">The map does not have the layer.</exception>
    /// <exception cref="FormatException">The layer is not valid.</exception>
    public GeoFeatureCollection Load(MapLayer layer, GeoBounds bounds) => LoadCore(layer, bounds);

    /// <summary>Reads the features of a layer the map has.</summary>
    /// <param name="layer">The layer, one that <see cref="HasLayer"/> reports.</param>
    /// <param name="bounds">
    /// The box the features must intersect, or <see langword="null"/> for every feature. The result can have more features,
    /// such as the features a spatial index finds: <see cref="Load(MapLayer, GeoBounds)"/> keeps those whose bounds intersect the box.
    /// </param>
    /// <returns>The features.</returns>
    /// <exception cref="FormatException">The layer is not valid.</exception>
    protected abstract IEnumerable<GeoFeature> LoadLayer(MapLayer layer, GeoBounds? bounds);

    private GeoFeatureCollection LoadCore(MapLayer layer, GeoBounds? bounds)
    {
        if (layer is < MapLayer.Borders or > MapLayer.PhysicalRegions)
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "The value is not a layer.");

        if (!HasLayer(layer))
            throw new KeyNotFoundException($"The map of the region '{Region}' has no {layer} layer: its source has nothing in the region.");

        var features = LoadLayer(layer, bounds);
        if (bounds is { } box)
        {
            features = features.Where(feature => feature.Geometry?.GetBounds() is { } featureBounds && Intersects(featureBounds, box));
        }

        return new GeoFeatureCollection(features.Select(feature => MapLayerProperties.Normalize(layer, feature))) { Name = layer.ToString() };
    }

    private static bool Intersects(GeoBounds a, GeoBounds b) => a.West <= b.East && a.East >= b.West && a.South <= b.North && a.North >= b.South;
}
