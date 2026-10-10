using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>How the expected pixels of a valid fixture were established, and how they were independently verified.</summary>
public sealed class FixtureReference
{
    /// <summary>The expected pixels are hand-defined (pattern or literal frames computed from the specification).</summary>
    public const string HandComputed = "hand-computed";

    /// <summary>The expected pixels were decoded from the same encoded input by an independent reference decoder.</summary>
    public const string Decoded = "decoded";

    /// <summary>Gets <see cref="HandComputed"/> or <see cref="Decoded"/>.</summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>Gets a human-readable description of the reference.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Gets, for decoded references, the reference tool and exact version.</summary>
    [JsonPropertyName("decoder")]
    public string? Decoder { get; init; }

    /// <summary>Gets, for decoded references, the actual decoder implementation and settings behind the tool.</summary>
    [JsonPropertyName("backend")]
    public string? Backend { get; init; }

    /// <summary>Gets, for decoded references, the exact command line.</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Gets the comparisons with other, genuinely independent implementations.</summary>
    [JsonPropertyName("crossChecks")]
    public IReadOnlyList<FixtureCrossCheck>? CrossChecks { get; init; }
}
