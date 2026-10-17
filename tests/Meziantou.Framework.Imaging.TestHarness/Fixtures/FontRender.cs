using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// A line of text drawn with a font by the reference renderer (<c>hb-raster</c>), on a white background: what the
/// library is compared with for the glyphs a font draws with its own colors.
/// </summary>
/// <remarks>
/// The image is the box of the line (the advance of the text, and the ascent and the descent below) with a margin on
/// each side: the origin of the first glyph is at (<see cref="Margin"/>, <see cref="Margin"/> + <see cref="Ascent"/>).
/// </remarks>
public sealed class FontRender
{
    /// <summary>Gets the image, a PNG file.</summary>
    [JsonPropertyName("file")]
    public required FixtureFile File { get; init; }

    /// <summary>Gets the characters of the line, as hexadecimal code points separated by spaces.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Gets the size of the font, in pixels per em.</summary>
    [JsonPropertyName("size")]
    public required int Size { get; init; }

    /// <summary>Gets the margin around the box of the line, in pixels.</summary>
    [JsonPropertyName("margin")]
    public required int Margin { get; init; }

    /// <summary>Gets the distance from the top of the box of the line to its baseline, in pixels.</summary>
    [JsonPropertyName("ascent")]
    public required int Ascent { get; init; }

    /// <summary>Gets the distance from the baseline to the bottom of the box of the line, in pixels.</summary>
    [JsonPropertyName("descent")]
    public required int Descent { get; init; }

    /// <summary>Gets the instance of a variable font, in the syntax of the reference tools; <see langword="null"/> for the default instance.</summary>
    [JsonPropertyName("variations")]
    public string? Variations { get; init; }

    /// <summary>Gets the palette of the font the colors come from; 0 by default.</summary>
    [JsonPropertyName("palette")]
    public int Palette { get; init; }

    /// <summary>Gets the color of the text, as six or eight hexadecimal digits; black by default.</summary>
    [JsonPropertyName("foreground")]
    public string? Foreground { get; init; }

    /// <summary>
    /// Gets the number of pixels, downward, between where the font places the images of its bitmap glyphs and where the
    /// reference renderer drew them: it draws an image one pixel higher than an outline at the same place.
    /// </summary>
    [JsonPropertyName("imageShift")]
    public int ImageShift { get; init; }

    [JsonIgnore]
    public string Characters => string.Concat(Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(codePoint => char.ConvertFromUtf32(Convert.ToInt32(codePoint, 16))));
}
