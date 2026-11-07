using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The <c>hevcVectors</c> section of the manifest: raw HEVC byte streams (<c>hevc/*.hevc</c>, H.265 Annex B) with the
/// SHA-256 of every plane of every output picture. The vectors are kept apart from <see cref="FixtureManifest.Fixtures"/>
/// because they have no pixel reference: they test the HEVC decoder below the HEIF container and the color conversion.
/// </summary>
/// <remarks>
/// Written by <c>tools/Meziantou.Framework.Imaging.CorpusGenerator/HevcCorpus.cs</c>. A digest is recorded only when at
/// least two of the independent decoders listed in <see cref="Decoders"/> produce the same bytes, and each vector records
/// which ones (<see cref="HevcVectorEntry.Decoders"/>).
/// </remarks>
public sealed class HevcVectorSet
{
    /// <summary>Gets how the vectors were generated (script, command, tool versions).</summary>
    [JsonPropertyName("generator")]
    public required FixtureGeneratorInfo Generator { get; init; }

    /// <summary>Gets the independent header parser the feature tags of the vectors are derived from.</summary>
    [JsonPropertyName("headerParser")]
    public required HevcVectorTool HeaderParser { get; init; }

    /// <summary>Gets the independent decoders: three, of which at least two agree byte for byte on a vector that has digests.</summary>
    [JsonPropertyName("decoders")]
    public required IReadOnlyList<HevcVectorTool> Decoders { get; init; }

    /// <summary>Gets the definition of a plane digest (sample order, sample size, plane order, plane dimensions).</summary>
    [JsonPropertyName("planeDigest")]
    public required string PlaneDigest { get; init; }

    /// <summary>Gets the definition of the picture hashes recorded for each output picture (<see cref="HevcVectorFrame.Hash"/>).</summary>
    [JsonPropertyName("pictureHash")]
    public required string PictureHash { get; init; }

    /// <summary>Gets the vectors.</summary>
    [JsonPropertyName("entries")]
    public required IReadOnlyList<HevcVectorEntry> Entries { get; init; }
}
