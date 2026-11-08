namespace Meziantou.Framework.Geo;

/// <summary>The options of the GeoPackage reader, <see cref="GeoPackage"/>.</summary>
public sealed class GeoPackageReaderOptions
{
    /// <summary>
    /// Gets or sets a box to read only the features whose bounds intersect it, or <see langword="null"/> to read every
    /// feature. The default is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// When the table has an R-tree spatial index, only the rows that the index finds are read. The features are kept
    /// whole: use <see cref="GeoGeometry.Clip"/> to cut them to the box.
    /// </remarks>
    public GeoBounds? Bounds { get; set; }
}
