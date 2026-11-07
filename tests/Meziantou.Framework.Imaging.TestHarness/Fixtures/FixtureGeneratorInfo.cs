using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Describes the reviewed generator that produced the corpus. Regeneration is never run by the tests.</summary>
public sealed class FixtureGeneratorInfo
{
    /// <summary>Gets the generator script path, relative to the repository root.</summary>
    [JsonPropertyName("script")]
    public required string Script { get; init; }

    /// <summary>Gets the explicit regeneration command.</summary>
    [JsonPropertyName("command")]
    public required string Command { get; init; }

    /// <summary>Gets the pinned tools (name and exact version) used for the whole corpus.</summary>
    [JsonPropertyName("tools")]
    public required IReadOnlyList<string> Tools { get; init; }
}
