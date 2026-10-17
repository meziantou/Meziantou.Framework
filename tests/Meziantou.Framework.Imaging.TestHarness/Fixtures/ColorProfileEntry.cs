using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>An ICC profile of the corpus (<c>colorProfiles</c>), used by the color conversion vectors.</summary>
public sealed class ColorProfileEntry
{
    /// <summary>Gets the identifier, such as <c>icc/srgb-v4</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets the profile file.</summary>
    [JsonPropertyName("file")]
    public required FixtureFile File { get; init; }

    /// <summary>Gets the origin and license of the profile.</summary>
    [JsonPropertyName("provenance")]
    public required FixtureProvenance Provenance { get; init; }

    /// <summary>Gets the feature tags, such as <c>icc.model=matrix</c>.</summary>
    [JsonPropertyName("features")]
    public IReadOnlyList<string>? Features { get; init; }

    /// <summary>Gets free-form notes.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
