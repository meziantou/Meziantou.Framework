using System.Text;

namespace Meziantou.Framework.Geo;

/// <summary>The options of the shapefile reader, <see cref="Shapefile"/>.</summary>
public sealed class ShapefileReaderOptions
{
    /// <summary>
    /// Gets or sets the encoding of the text of the <c>.dbf</c> file, or <see langword="null"/> to get it from the <c>.cpg</c>
    /// file, else from the language driver ID of the <c>.dbf</c> file. The default is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Set it for a file whose encoding is not declared, or declared with a code page that .NET only provides once the
    /// application registers <c>CodePagesEncodingProvider</c>, such as 437, 850 or 1251.
    /// </remarks>
    public Encoding? Encoding { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the coordinates are converted to longitudes and latitudes of WGS 84 from
    /// the coordinate reference system of the <c>.prj</c> file, such as Lambert 93 or UTM. The default is <see langword="false"/>:
    /// the coordinates are those of the file.
    /// </summary>
    /// <remarks>
    /// The <see cref="GeoFeatureCollection.CoordinateReferenceSystem"/> of the features is then <see cref="CoordinateReferenceSystem.Wgs84"/>.
    /// Without a <c>.prj</c> file, the coordinates are not converted. The reader throws a <see cref="FormatException"/>
    /// when the <c>.prj</c> file is not valid, and a <see cref="NotSupportedException"/> when its projection is not supported.
    /// </remarks>
    public bool TransformToWgs84 { get; set; }
}
