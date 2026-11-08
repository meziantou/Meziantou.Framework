namespace Meziantou.Framework.Geo;

/// <summary>The options of the GeoParquet reader, <see cref="GeoParquet"/>.</summary>
public sealed class GeoParquetReaderOptions
{
    /// <summary>
    /// Gets or sets a box to read only the features whose bounds intersect it, or <see langword="null"/> to read every
    /// feature. The default is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// When the file has a bbox covering column (GeoParquet 1.1) with statistics, the row groups outside of the box are
    /// not read. The features are kept whole: use <see cref="GeoGeometry.Clip"/> to cut them to the box.
    /// </remarks>
    public GeoBounds? Bounds { get; set; }

    /// <summary>
    /// Gets or sets the name of the geometry column to read, or <see langword="null"/> to read the primary column of the
    /// file. The default is <see langword="null"/>.
    /// </summary>
    /// <remarks>The other geometry columns of the file are not read.</remarks>
    public string? GeometryColumn { get; set; }
}
