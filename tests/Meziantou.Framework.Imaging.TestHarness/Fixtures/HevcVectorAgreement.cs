using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Which of the decoders of <see cref="HevcVectorSet.Decoders"/> produced the digests of a vector, and which did not.</summary>
public sealed class HevcVectorAgreement
{
    /// <summary>
    /// Gets the identifiers of the decoders that produced the same bytes for every plane of every output picture. Two or
    /// three for a vector that has digests; for a vector without digests, the one decoder whose output the picture hashes
    /// of the stream confirm.
    /// </summary>
    [JsonPropertyName("agree")]
    public required IReadOnlyList<string> Agree { get; init; }

    /// <summary>Gets the decoders that are left out: every decoder of the section is either in <see cref="Agree"/> or here.</summary>
    [JsonPropertyName("excluded")]
    public required IReadOnlyList<HevcVectorExclusion> Excluded { get; init; }
}
