using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The reference values of one face of a font of the corpus.</summary>
public sealed class FontFaceVectors
{
    /// <summary>Gets the family name.</summary>
    [JsonPropertyName("family")]
    public required string Family { get; init; }

    /// <summary>Gets the subfamily name.</summary>
    [JsonPropertyName("subfamily")]
    public required string Subfamily { get; init; }

    /// <summary>Gets the PostScript name, when the font has one.</summary>
    [JsonPropertyName("postScriptName")]
    public string? PostScriptName { get; init; }

    /// <summary>Gets the number of font units per em.</summary>
    [JsonPropertyName("unitsPerEm")]
    public required int UnitsPerEm { get; init; }

    /// <summary>Gets the number of glyphs.</summary>
    [JsonPropertyName("glyphCount")]
    public required int GlyphCount { get; init; }

    /// <summary>
    /// Gets the metrics the font declares, in font units, by OpenType metric tag: <c>hasc</c>, <c>hdsc</c>, <c>hlgp</c>
    /// (horizontal ascender, descender and line gap), <c>xhgt</c>, <c>cpht</c> (x-height and cap height), <c>unds</c>,
    /// <c>undo</c> (underline size and offset), <c>strs</c>, <c>stro</c> (strikeout size and offset). A metric the font
    /// does not declare is absent.
    /// </summary>
    [JsonPropertyName("metrics")]
    public required IReadOnlyDictionary<string, int> Metrics { get; init; }

    /// <summary>Gets the character map: the glyph of each code point, keyed by the code point in uppercase hexadecimal.</summary>
    [JsonPropertyName("characterMap")]
    public required IReadOnlyDictionary<string, int> CharacterMap { get; init; }

    /// <summary>Gets the advance width of each glyph, in font units.</summary>
    [JsonPropertyName("advances")]
    public required IReadOnlyList<int> Advances { get; init; }

    /// <summary>
    /// Gets the outline of each glyph as SVG path data in font units, with the y axis up: <c>M</c>, <c>L</c>, <c>Q</c>
    /// (quadratic), <c>C</c> (cubic) and <c>Z</c> commands with absolute coordinates. A glyph without outline is an
    /// empty string.
    /// </summary>
    [JsonPropertyName("outlines")]
    public required IReadOnlyList<string> Outlines { get; init; }
}
