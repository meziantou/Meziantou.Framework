namespace Meziantou.Framework.Charts;

/// <summary>A GeoJSON feature: a geometry, its properties and its identifier.</summary>
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

    /// <summary>Gets the identifier: the <c>id</c> member of the feature, a number written as text, or <see langword="null"/>.</summary>
    public string? Id { get; }

    /// <summary>Gets the geometry, or <see langword="null"/> for a feature without a location.</summary>
    public GeoGeometry? Geometry { get; }

    /// <summary>
    /// Gets the properties. A JSON string is a <see cref="string"/>, a number a <see cref="double"/>, <c>true</c> and
    /// <c>false</c> a <see cref="bool"/>, <c>null</c> a <see langword="null"/>, and an object or an array a
    /// <see cref="System.Text.Json.JsonElement"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }
}
