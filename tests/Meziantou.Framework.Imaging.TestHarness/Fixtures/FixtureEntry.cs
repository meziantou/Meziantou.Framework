using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>One corpus entry: an encoded input, its provenance, and its expectations (pixels and metadata, or an error).</summary>
public sealed class FixtureEntry
{
    /// <summary>Gets the stable identifier, e.g. <c>png/gray8-odd-width</c>. Lowercase letters, digits, '-', '_', '.' and '/' only.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets the kind of fixture: one of <see cref="FixtureKinds"/>.</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>Gets the container format of the input (<c>png</c> for PNG and APNG, <c>gif</c>, <c>jpeg</c>).</summary>
    [JsonPropertyName("format")]
    public required string Format { get; init; }

    /// <summary>Gets the encoded input file.</summary>
    [JsonPropertyName("input")]
    public required FixtureFile Input { get; init; }

    /// <summary>Gets where the input (and its references) come from and under which license.</summary>
    [JsonPropertyName("provenance")]
    public required FixtureProvenance Provenance { get; init; }

    /// <summary>
    /// Gets the encoding features actually present in the input, parsed from the encoded bytes by the generator
    /// (e.g. <c>png.interlace=adam7</c>, <c>apng.poster=separate</c>, <c>gif.lzw.deferredClear</c>, <c>jpeg.sampling=4:2:0</c>).
    /// </summary>
    [JsonPropertyName("features")]
    public IReadOnlyList<string>? Features { get; init; }

    /// <summary>Gets how the expected pixels were established (required for valid fixtures).</summary>
    [JsonPropertyName("reference")]
    public FixtureReference? Reference { get; init; }

    /// <summary>Gets the expected decoded image (required for valid fixtures, forbidden otherwise).</summary>
    [JsonPropertyName("expected")]
    public FixtureExpectation? Expected { get; init; }

    /// <summary>Gets the pixel comparison policy (required for valid fixtures).</summary>
    [JsonPropertyName("comparison")]
    public ComparisonPolicyDefinition? Comparison { get; init; }

    /// <summary>Gets, for invalid/unsupported/limit fixtures, the expected failure. Fabricated expected pixels are not allowed.</summary>
    [JsonPropertyName("expectedError")]
    public ExpectedErrorDefinition? ExpectedError { get; init; }

    /// <summary>Gets the decode options (resource limits, frame limit) the fixture must be decoded with.</summary>
    [JsonPropertyName("decodeOptions")]
    public DecodeOptionsDefinition? DecodeOptions { get; init; }

    /// <summary>Gets free-form notes for reviewers.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>Gets a value indicating whether the entry declares the specified encoding feature.</summary>
    /// <param name="feature">The feature, e.g. <c>png.interlace=adam7</c>.</param>
    /// <returns><see langword="true"/> if the feature is declared.</returns>
    public bool HasFeature(string feature) => Features?.Contains(feature, StringComparer.Ordinal) == true;

    /// <inheritdoc/>
    public override string ToString() => Id;
}
