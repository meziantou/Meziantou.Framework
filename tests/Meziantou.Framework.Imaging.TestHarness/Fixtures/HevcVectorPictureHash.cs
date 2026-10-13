using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The decoded picture hash SEI messages of an HEVC conformance vector (H.265, clause D.3.19): every picture of every
/// vector carries one, so that a decoder can be checked with nothing but the stream.
/// </summary>
public sealed class HevcVectorPictureHash
{
    /// <summary>The MD5 of each colour component: 16 bytes.</summary>
    public const string Md5 = "md5";

    /// <summary>The CRC of each colour component: 2 bytes.</summary>
    public const string Crc = "crc";

    /// <summary>The checksum of each colour component: 4 bytes.</summary>
    public const string Checksum = "checksum";

    /// <summary>Gets the type of the hashes of the stream: <see cref="Md5"/>, <see cref="Crc"/> or <see cref="Checksum"/>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Gets the identifiers of the agreeing decoders that computed the hash of every picture they decoded and found the
    /// values of the stream: at least one. FFmpeg only checks the MD5; dec265 only compares the hash of the last picture
    /// of a stream, so it is only named for a stream of one picture, and never for a checksum above 8 bits.
    /// </summary>
    [JsonPropertyName("verifiedBy")]
    public required IReadOnlyList<string> VerifiedBy { get; init; }

    /// <summary>Gets the number of hexadecimal characters of the hash of one colour component.</summary>
    [JsonIgnore]
    public int HexLength => Type switch
    {
        Md5 => 32,
        Crc => 4,
        Checksum => 8,
        _ => 0,
    };
}
