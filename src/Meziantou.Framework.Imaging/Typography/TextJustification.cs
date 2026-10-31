namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Where a justified line (<see cref="TextAlignment.Justify"/>) takes the room it is stretched by.</summary>
/// <remarks>
/// The room is shared equally by the places a line can be stretched at (its justification opportunities). The scripts
/// are those of the CSS Text Module: the block scripts are Han, Hiragana, Katakana, Bopomofo, Hangul and Yi (with the
/// characters that are wide or fullwidth, such as the ideographic punctuation), the clustered scripts are Thai, Lao,
/// Khmer, Myanmar, Tai Le, New Tai Lue, Tai Tham and Tai Viet, and the cursive scripts, whose letters join, are Arabic,
/// Syriac, Mongolian, N'Ko, Phags-pa, Mandaic and Hanifi Rohingya. Arabic words are never lengthened (kashida), and the
/// space is not distributed by priority (spaces first, then the characters) as some typographic traditions do.
/// </remarks>
public enum TextJustification
{
    /// <summary>
    /// Only the spaces between words are widened: U+0020 spaces and U+00A0 no-break spaces. A line without such a space
    /// is aligned at its start. The default.
    /// </summary>
    InterWord = 0,

    /// <summary>
    /// The spaces between words are widened, the U+3000 ideographic spaces too, and room is added between two
    /// characters when one of them is of a block script (Chinese, Japanese, Korean) or of a clustered script (Thai, Lao,
    /// Khmer, Burmese) and neither is white space.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Room is added between every two characters of the line (each grapheme cluster with its marks, or a ligature),
    /// white space included, except between two letters of a cursive script.
    /// </summary>
    InterCharacter = 2,
}
