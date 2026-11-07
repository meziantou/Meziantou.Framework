using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The <c>av1Vectors</c> section of the manifest: raw AV1 OBU streams (<c>av1/*.obu</c>) with the SHA-256 of every plane of
/// every output frame. The vectors are kept apart from <see cref="FixtureManifest.Fixtures"/> because they have no pixel
/// reference: they test the AV1 decoder below the AVIF container and the color conversion.
/// </summary>
/// <remarks>
/// Written by <c>tools/Meziantou.Framework.Imaging.CorpusGenerator/AvifCorpus.cs</c>. A digest is recorded only when the
/// independent decoders listed in <see cref="Decoders"/> produce the same bytes.
/// </remarks>
public sealed class Av1VectorSet
{
    /// <summary>Gets how the vectors were generated (script, command, tool versions).</summary>
    [JsonPropertyName("generator")]
    public required FixtureGeneratorInfo Generator { get; init; }

    /// <summary>Gets the independent header parser the feature tags of the vectors are derived from.</summary>
    [JsonPropertyName("headerParser")]
    public required Av1VectorTool HeaderParser { get; init; }

    /// <summary>Gets the independent decoders that produced the plane digests: at least two, which agreed byte for byte.</summary>
    [JsonPropertyName("decoders")]
    public required IReadOnlyList<Av1VectorTool> Decoders { get; init; }

    /// <summary>Gets the definition of a plane digest (sample order, sample size, plane order, plane dimensions).</summary>
    [JsonPropertyName("planeDigest")]
    public required string PlaneDigest { get; init; }

    /// <summary>Gets the vectors.</summary>
    [JsonPropertyName("entries")]
    public required IReadOnlyList<Av1VectorEntry> Entries { get; init; }
}
