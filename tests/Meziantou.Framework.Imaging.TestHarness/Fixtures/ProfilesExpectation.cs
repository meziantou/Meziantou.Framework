using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The exact identity of the metadata payloads a decoder must preserve byte for byte: the uncompressed ICC profile, the
/// EXIF data starting at the TIFF header (no <c>Exif\0\0</c> prefix), and the XMP packet. <see langword="null"/> means absent.
/// </summary>
public sealed class ProfilesExpectation
{
    /// <summary>Gets the expected ICC profile, or <see langword="null"/>.</summary>
    [JsonPropertyName("icc")]
    public required ProfileExpectation? Icc { get; init; }

    /// <summary>Gets the expected EXIF data, or <see langword="null"/>.</summary>
    [JsonPropertyName("exif")]
    public required ProfileExpectation? Exif { get; init; }

    /// <summary>Gets the expected XMP packet, or <see langword="null"/>.</summary>
    [JsonPropertyName("xmp")]
    public required ProfileExpectation? Xmp { get; init; }
}
