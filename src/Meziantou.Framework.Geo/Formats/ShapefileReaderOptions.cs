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
}
