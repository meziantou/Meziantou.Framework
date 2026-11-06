namespace Meziantou.Framework.Geo;

/// <summary>A feature: a geometry, its properties and its identifier, such as a country, a road or a city.</summary>
public sealed class GeoFeature
{
    /// <summary>Initializes a new instance of the <see cref="GeoFeature"/> class.</summary>
    /// <param name="geometry">The geometry, or <see langword="null"/> for a feature without a location.</param>
    /// <param name="properties">The properties, or <see langword="null"/> for none.</param>
    /// <param name="id">The identifier, or <see langword="null"/>.</param>
    public GeoFeature(GeoGeometry? geometry, IReadOnlyDictionary<string, object?>? properties = null, string? id = null)
    {
        Geometry = geometry;
        Properties = properties ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        Id = id;
    }

    /// <summary>Gets the identifier, or <see langword="null"/>. A numeric identifier, as GeoJSON allows, is written as text.</summary>
    public string? Id { get; }

    /// <summary>Gets the geometry, or <see langword="null"/> for a feature without a location.</summary>
    public GeoGeometry? Geometry { get; }

    /// <summary>
    /// Gets the properties. The readers give a <see cref="string"/>, a <see cref="double"/>, a <see cref="bool"/> or
    /// <see langword="null"/>; each format documents the other types it gives. The writers accept these types, the other
    /// numeric types, dates and times, and write the other values as their text.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }
}
