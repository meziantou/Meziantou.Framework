using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The values the reference font readers give for an instance of a variable font.</summary>
public sealed class FontInstanceVectors
{
    /// <summary>Gets the values of the axes, in the syntax of the reference tools: <c>wght=650,wdth=80</c>.</summary>
    [JsonPropertyName("variations")]
    public required string Variations { get; init; }

    /// <summary>Gets the metrics of the instance, by OpenType metric tag, in font units.</summary>
    [JsonPropertyName("metrics")]
    public required IReadOnlyDictionary<string, int> Metrics { get; init; }

    /// <summary>Gets the advance width of each glyph, in font units, on which both readers agree.</summary>
    [JsonPropertyName("advances")]
    public required IReadOnlyList<int> Advances { get; init; }

    /// <summary>
    /// Gets the outline of each glyph as an SVG path in font units, to three decimals, as HarfBuzz draws it; FreeType
    /// draws the same segments within one unit.
    /// </summary>
    [JsonPropertyName("outlines")]
    public required IReadOnlyList<string> Outlines { get; init; }
}
