using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The origin and license of a fixture. Required for every entry (see tests/Meziantou.Framework.Imaging.Fixtures/README.md).</summary>
public sealed class FixtureProvenance
{
    /// <summary>Gets how the fixture was obtained: <c>hand-authored</c>, <c>generated</c> (by a committed script) or <c>external</c>.</summary>
    [JsonPropertyName("origin")]
    public required string Origin { get; init; }

    /// <summary>Gets the SPDX license identifier of the fixture content. Must be in <see cref="FixtureManifestValidator.AllowedLicenses"/>.</summary>
    [JsonPropertyName("license")]
    public required string License { get; init; }

    /// <summary>Gets the author or copyright holder.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>Gets, for external fixtures, the URL or citation of the original source.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>Gets, for generated and hand-authored fixtures, the generator script path (relative to the repository root) and its function.</summary>
    [JsonPropertyName("generator")]
    public string? Generator { get; init; }

    /// <summary>Gets the pinned tools (name and exact version) used to produce the input or the references.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>Gets the synthetic pattern parameters (sizes, seeds, palettes...).</summary>
    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; init; }

    /// <summary>Gets the exact external-tool command lines used to produce the input (symbolic file names).</summary>
    [JsonPropertyName("commands")]
    public IReadOnlyList<string>? Commands { get; init; }
}
