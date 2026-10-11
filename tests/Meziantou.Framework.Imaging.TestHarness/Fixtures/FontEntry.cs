using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>A font file of the corpus (<c>fonts</c>). Fonts are not image fixtures: the generic conformance suites never enumerate them.</summary>
public sealed class FontEntry
{
    /// <summary>The accepted values of <see cref="Flavor"/>.</summary>
    public static IReadOnlySet<string> AllowedFlavors { get; } = new HashSet<string>(StringComparer.Ordinal) { "ttf", "otf", "ttc", "woff", "woff2" };

    /// <summary>The accepted values of <see cref="Modification"/>.</summary>
    public static IReadOnlySet<string> AllowedModifications { get; } = new HashSet<string>(StringComparer.Ordinal) { "none", "subset", "container" };

    /// <summary>Gets the identifier, such as <c>fonts/noto-sans-regular</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets the font file.</summary>
    [JsonPropertyName("file")]
    public required FixtureFile File { get; init; }

    /// <summary>Gets the origin and license of the font.</summary>
    [JsonPropertyName("provenance")]
    public required FixtureProvenance Provenance { get; init; }

    /// <summary>Gets the container and outline format: <c>ttf</c> (TrueType outlines), <c>otf</c> (CFF outlines), <c>ttc</c> (collection), <c>woff</c> or <c>woff2</c>.</summary>
    [JsonPropertyName("flavor")]
    public required string Flavor { get; init; }

    /// <summary>Gets the number of faces of the file; more than one for a collection.</summary>
    [JsonPropertyName("faceCount")]
    public int FaceCount { get; init; } = 1;

    /// <summary>
    /// Gets how the file differs from the font its provenance names: <c>none</c> (the original file, or the output of
    /// the generator), <c>subset</c> (glyphs and tables removed by the pinned subsetter) or <c>container</c> (the tables
    /// of <see cref="Original"/> in another container).
    /// </summary>
    [JsonPropertyName("modification")]
    public required string Modification { get; init; }

    /// <summary>Gets the Reserved Font Name of the font, when its license declares one: such a font is only stored unmodified.</summary>
    [JsonPropertyName("reservedFontName")]
    public string? ReservedFontName { get; init; }

    /// <summary>Gets the identifier of the font this file is another container of.</summary>
    [JsonPropertyName("original")]
    public string? Original { get; init; }

    /// <summary>Gets the feature tags, such as <c>font.outlines=glyf</c>.</summary>
    [JsonPropertyName("features")]
    public IReadOnlyList<string>? Features { get; init; }

    /// <summary>Gets the reference values recorded from independent font readers, as a JSON file (<see cref="FontVectors"/>).</summary>
    [JsonPropertyName("vectors")]
    public FixtureFile? Vectors { get; init; }

    /// <summary>Gets free-form notes.</summary>
    /// <summary>The glyphs and positions a reference shaper gives for texts set in the font (JSON, <see cref="ShapingVectors"/>).</summary>
    [JsonPropertyName("shaping")]
    public FixtureFile? Shaping { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
