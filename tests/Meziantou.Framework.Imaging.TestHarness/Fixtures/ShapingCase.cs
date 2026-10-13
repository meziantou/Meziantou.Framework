using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>One text shaped by the reference shaper.</summary>
public sealed class ShapingCase
{
    /// <summary>The text, as the hexadecimal code points of its characters separated by spaces: no character is lost in an editor.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>The features given to the shaper, in its syntax (<c>-liga,smcp</c>), or <see langword="null"/> for the default ones.</summary>
    [JsonPropertyName("features")]
    public string? Features { get; init; }

    /// <summary>
    /// The glyphs, in visual order. Each one is six numbers: the glyph identifier, the cluster (the index of a
    /// character of the text), the horizontal and vertical offsets, the horizontal and vertical advances, in font units.
    /// </summary>
    /// <summary>Gets the instance of a variable font the text is shaped with, in the syntax of the reference shaper; <see langword="null"/> for the default instance.</summary>
    [JsonPropertyName("variations")]
    public string? Variations { get; init; }

    /// <summary>Gets the language the text is shaped in, as a BCP 47 language tag; <see langword="null"/> when the shaper is given none.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("glyphs")]
    public required IReadOnlyList<IReadOnlyList<int>> Glyphs { get; init; }

    /// <summary>Gets the text as a string.</summary>
    [JsonIgnore]
    public string Characters => string.Concat(Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(codePoint => char.ConvertFromUtf32(Convert.ToInt32(codePoint, 16))));
}
