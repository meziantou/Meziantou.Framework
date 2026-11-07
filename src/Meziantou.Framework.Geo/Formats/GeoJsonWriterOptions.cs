namespace Meziantou.Framework.Geo;

/// <summary>The options of the GeoJSON writer, <see cref="GeoJson"/>.</summary>
public sealed class GeoJsonWriterOptions
{
    /// <summary>Gets or sets a value that indicates whether the JSON is indented. The default is <see langword="false"/>.</summary>
    public bool Indented { get; set; }

    /// <summary>Gets or sets a value that indicates whether the collection, the features and the geometries get a <c>bbox</c> member. The default is <see langword="false"/>.</summary>
    public bool WriteBoundingBoxes { get; set; }
}
