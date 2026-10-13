using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The root of <c>tests/Meziantou.Framework.Imaging.Fixtures/manifest.json</c>. The JSON schema is <c>tests/Meziantou.Framework.Imaging.Fixtures/manifest.schema.json</c>;
/// the conventions are documented in <c>tests/Meziantou.Framework.Imaging.Fixtures/README.md</c>.
/// </summary>
/// <remarks>
/// Schema version 2 adds the expected displayed frames with exact raw-buffer layouts, timing/metadata
/// expectations, reference provenance and cross-checks, comparison policies, and expected errors. Schema version 4 adds
/// the fonts of the text rendering tests (<see cref="Fonts"/>). The manifest is written
/// by the reviewed generator <c>tools/Meziantou.Framework.Imaging.CorpusGenerator</c>; it is authoritative for interpreting raw
/// buffers, which carry no dimensions or timing.
/// </remarks>
public sealed class FixtureManifest
{
    /// <summary>The schema version understood by this harness.</summary>
    public const int CurrentSchemaVersion = 5;

    /// <summary>Gets the optional reference to the JSON schema, for editors.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    /// <summary>Gets the schema version of the manifest.</summary>
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    /// <summary>Gets how the corpus was generated (script, command, tool versions).</summary>
    [JsonPropertyName("generator")]
    public FixtureGeneratorInfo? Generator { get; init; }

    /// <summary>Gets the fixtures.</summary>
    [JsonPropertyName("fixtures")]
    public required IReadOnlyList<FixtureEntry> Fixtures { get; init; }

    /// <summary>
    /// Gets the AV1 conformance vectors: raw OBU streams with plane digests. They are not fixtures (no pixel reference), so
    /// the generic conformance suites never enumerate them.
    /// </summary>
    [JsonPropertyName("av1Vectors")]
    public Av1VectorSet? Av1Vectors { get; init; }

    /// <summary>
    /// Gets the HEVC conformance vectors: raw byte streams with plane digests. They are not fixtures (no pixel reference),
    /// so the generic conformance suites never enumerate them.
    /// </summary>
    [JsonPropertyName("hevcVectors")]
    public HevcVectorSet? HevcVectors { get; init; }

    /// <summary>Gets the ICC profiles used by <see cref="ColorTransforms"/>.</summary>
    [JsonPropertyName("colorProfiles")]
    public IReadOnlyList<ColorProfileEntry>? ColorProfiles { get; init; }

    /// <summary>Gets the reference color conversions between <see cref="ColorProfiles"/>.</summary>
    [JsonPropertyName("colorTransforms")]
    public IReadOnlyList<ColorTransformEntry>? ColorTransforms { get; init; }

    /// <summary>Gets the font files of the text rendering tests, with the reference values recorded from independent font readers.</summary>
    [JsonPropertyName("fonts")]
    public IReadOnlyList<FontEntry>? Fonts { get; init; }

    /// <summary>The third-party suites of test fonts, each a directory stored as published.</summary>
    [JsonPropertyName("fontSuites")]
    public IReadOnlyList<FontSuiteEntry>? FontSuites { get; init; }
}
