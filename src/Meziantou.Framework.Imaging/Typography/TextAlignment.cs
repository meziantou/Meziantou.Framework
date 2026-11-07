namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How the lines of a text are placed in the width of its layout.</summary>
public enum TextAlignment
{
    /// <summary>At the side a line starts from: the left for a left-to-right paragraph, the right for a right-to-left one.</summary>
    Start = 0,

    /// <summary>Centered.</summary>
    Center = 1,

    /// <summary>At the side a line ends at: the right for a left-to-right paragraph, the left for a right-to-left one.</summary>
    End = 2,

    /// <summary>
    /// Each line is stretched to the width of the layout, except the last line of a paragraph and a line that cannot be
    /// stretched, which are aligned as with <see cref="Start"/>. By default, the room is shared by the U+0020 spaces and
    /// the U+00A0 no-break spaces of the line; <see cref="TextLayoutOptions.Justification"/> also adds it between the
    /// characters of Chinese, Japanese, Korean and Thai text, or between every two characters. Arabic words are not
    /// lengthened (kashida).
    /// </summary>
    Justify = 3,
}
