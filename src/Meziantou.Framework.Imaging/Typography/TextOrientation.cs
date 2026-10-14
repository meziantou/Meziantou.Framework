namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How the characters of a vertical line are set.</summary>
/// <remarks>See <see cref="TextLayoutOptions.Orientation"/>. A horizontal line has no orientation: its characters are upright.</remarks>
public enum TextOrientation
{
    /// <summary>
    /// Each character is set as the Unicode Standard says (Vertical_Orientation, UAX #50): the ideographs, the
    /// syllables of kana and of Hangul and the symbols are upright; the letters of the scripts written in horizontal
    /// lines (Latin, Cyrillic, Arabic, Devanagari) are on their side, turned a quarter of a turn clockwise, as a
    /// horizontal line read from the top. This is the default.
    /// </summary>
    Mixed = 0,

    /// <summary>Every character is upright, one under the other, whatever its script; the text is laid out from the top, without right-to-left text.</summary>
    Upright = 1,

    /// <summary>Every character is on its side: the line is a horizontal line turned a quarter of a turn clockwise.</summary>
    Sideways = 2,
}
