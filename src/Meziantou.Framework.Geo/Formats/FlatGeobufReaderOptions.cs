namespace Meziantou.Framework.Geo;

/// <summary>The options of the FlatGeobuf reader, <see cref="FlatGeobuf"/>.</summary>
public sealed class FlatGeobufReaderOptions
{
    /// <summary>
    /// Gets or sets a box to read only the features whose bounds intersect it, or <see langword="null"/> to read every
    /// feature. The default is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// When the file has a spatial index, the other features are not decoded. The features are kept whole: use
    /// <see cref="GeoGeometry.Clip"/> to cut them to the box.
    /// </remarks>
    public GeoBounds? Bounds { get; set; }
}
