namespace Meziantou.Framework.Geo;

/// <summary>The options of the KML writer, <see cref="Kml"/>, also used by the KMZ writer, <see cref="Kmz"/>.</summary>
public sealed class KmlWriterOptions
{
    /// <summary>Gets or sets a value that indicates whether the XML is indented. The default is <see langword="false"/>.</summary>
    public bool Indented { get; set; }

    /// <summary>
    /// Gets or sets the property whose value, when it is a <see cref="string"/>, becomes the <c>name</c> of the placemark,
    /// or <see langword="null"/> to write every property in the <c>ExtendedData</c>. The default is <c>name</c>.
    /// </summary>
    public string? NameProperty { get; set; } = "name";

    /// <summary>
    /// Gets or sets the property whose value, when it is a <see cref="string"/>, becomes the <c>description</c> of the
    /// placemark, or <see langword="null"/> to write every property in the <c>ExtendedData</c>. The default is <c>description</c>.
    /// </summary>
    public string? DescriptionProperty { get; set; } = "description";
}
