namespace Meziantou.Framework.Imaging.Typography;

/// <summary>What is made from the glyphs of a font when the font of the style wanted does not exist.</summary>
/// <remarks>
/// A bold or an italic text is normally set in the bold or the italic font of a family, or in an instance of a variable
/// font: these are drawn by a type designer. The values of this enumeration make a replacement from the glyphs of any
/// font, by geometry alone; see <see cref="TextStyle.Synthesis"/>.
/// </remarks>
[Flags]
public enum FontSynthesis
{
    /// <summary>The glyphs are drawn as the font has them.</summary>
    None = 0,

    /// <summary>
    /// The glyphs are thickened: every edge of an outline moves outward by 1/48 of the size of the text, so a stem is
    /// thicker by 1/24 of the size, and each glyph that takes room takes 1/24 of the size more.
    /// </summary>
    Bold = 1,

    /// <summary>The glyphs are slanted to the right: a point moves right by a quarter of its height above the baseline, a slant of about 14 degrees.</summary>
    Oblique = 2,
}
