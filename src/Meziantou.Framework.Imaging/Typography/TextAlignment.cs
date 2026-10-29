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
    /// Each line is stretched to the width of the layout by widening U+0020 spaces and U+00A0 no-break spaces, except the
    /// last line of a paragraph and a line without expandable spaces, which are aligned as with <see cref="Start"/>.
    /// Does not add space between CJK characters or lengthen Arabic words (kashida).
    /// </summary>
    Justify = 3,
}
