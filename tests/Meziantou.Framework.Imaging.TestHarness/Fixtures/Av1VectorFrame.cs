using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>One output frame of an AV1 conformance vector.</summary>
public sealed class Av1VectorFrame
{
    /// <summary>Gets the width of the luma plane of this frame (the frame width after super-resolution upscaling).</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>Gets the height of the luma plane.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>
    /// Gets the lowercase hexadecimal SHA-256 of each plane, in bitstream order Y, U, V (one plane for a monochrome stream),
    /// film grain applied. See <see cref="Av1VectorSet.PlaneDigest"/> for the exact byte layout.
    /// </summary>
    [JsonPropertyName("planes")]
    public required IReadOnlyList<string> Planes { get; init; }
}
