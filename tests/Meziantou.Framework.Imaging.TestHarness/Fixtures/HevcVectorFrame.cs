using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>One output picture of an HEVC conformance vector.</summary>
public sealed class HevcVectorFrame
{
    /// <summary>Gets the width of the luma plane of the output picture: the decoded picture cropped to the conformance window.</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>Gets the height of the luma plane of the output picture.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Gets the picture order count of the picture (<c>PicOrderCntVal</c>), which starts again with each coded video sequence.</summary>
    [JsonPropertyName("poc")]
    public required int Poc { get; init; }

    /// <summary>
    /// Gets the index of the picture among the coded pictures of the stream, in decoding order. The frames are in output
    /// order, which is another order as soon as a stream has B pictures.
    /// </summary>
    [JsonPropertyName("decodeOrder")]
    public required int DecodeOrder { get; init; }

    /// <summary>
    /// Gets the lowercase hexadecimal SHA-256 of each plane of the cropped picture, in bitstream order Y, Cb, Cr (one
    /// plane for a monochrome stream); empty for a vector without digests. See <see cref="HevcVectorSet.PlaneDigest"/>
    /// for the exact byte layout.
    /// </summary>
    [JsonPropertyName("planes")]
    public required IReadOnlyList<string> Planes { get; init; }

    /// <summary>
    /// Gets the decoded picture hash of the picture, read from its SEI message: one lowercase hexadecimal value per colour
    /// component, of the type <see cref="HevcVectorPictureHash.Type"/>. It covers the whole decoded picture, before the
    /// conformance window is applied.
    /// </summary>
    [JsonPropertyName("hash")]
    public required IReadOnlyList<string> Hash { get; init; }
}
