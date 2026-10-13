namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>
/// The library-independent description of decoded image metadata (<c>Image.Metadata</c>), compared with the manifest by
/// <see cref="GoldenAssert.ImageMatches"/>: exact resolution, byte-exact ICC/EXIF/XMP payloads (hash and length) and
/// text entries in file order.
/// </summary>
public sealed class DecodedMetadataSnapshot
{
    /// <summary>Gets the horizontal resolution in dots per inch, or <see langword="null"/> when the image has none.</summary>
    public double? HorizontalDpi { get; init; }

    /// <summary>Gets the vertical resolution in dots per inch, or <see langword="null"/> when the image has none.</summary>
    public double? VerticalDpi { get; init; }

    /// <summary>Gets the transfer function label: <c>srgb</c> or <c>linear</c>.</summary>
    public string TransferFunction { get; init; } = "srgb";

    /// <summary>
    /// Gets the CICP color description as <c>primaries/transfer/matrix full</c> (or <c>limited</c>), or <see langword="null"/>
    /// when the image has none.
    /// </summary>
    public string? Cicp { get; init; }

    /// <summary>Gets the content light level as <c>MaxCLL content, MaxPALL picture</c>, or <see langword="null"/> when the image has none.</summary>
    public string? ContentLightLevel { get; init; }

    /// <summary>Gets the mastering display color volume as <c>R x,y G x,y B x,y W x,y L min..max</c> (stored integers), or <see langword="null"/> when the image has none.</summary>
    public string? MasteringDisplay { get; init; }

    /// <summary>Gets the pixel aspect ratio as <c>horizontal:vertical</c>, or <see langword="null"/> when the image has none.</summary>
    public string? PixelAspectRatio { get; init; }

    /// <summary>Gets the ICC profile bytes, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? IccProfile { get; init; }

    /// <summary>Gets the EXIF data (from the TIFF header), or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? ExifProfile { get; init; }

    /// <summary>Gets the XMP packet, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? XmpProfile { get; init; }

    /// <summary>Gets the text entries in order.</summary>
    public IReadOnlyList<DecodedTextEntry> TextEntries { get; init; } = [];
}
