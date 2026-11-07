using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>A decoder that does not take part in the digests of an HEVC conformance vector.</summary>
public sealed class HevcVectorExclusion
{
    /// <summary>The decoder does not decode the stream.</summary>
    public const string Rejects = "rejects";

    /// <summary>The decoder decodes the stream and outputs other samples, or other pictures.</summary>
    public const string Differs = "differs";

    /// <summary>Gets the identifier of the decoder (<see cref="HevcVectorTool.Id"/>).</summary>
    [JsonPropertyName("decoder")]
    public required string Decoder { get; init; }

    /// <summary>Gets what the decoder does with the stream: <see cref="Rejects"/> or <see cref="Differs"/>.</summary>
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    /// <summary>Gets why the decoder is left out: the known behavior of that version of the decoder, which the recipe of the vector declares.</summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>Gets what the generator observed: the message of the decoder, or the pictures and the planes that differ.</summary>
    [JsonPropertyName("observed")]
    public required string Observed { get; init; }
}
