namespace Meziantou.Framework.Imaging.Typography;

/// <summary>The lines drawn with a text.</summary>
[Flags]
public enum TextDecorations
{
    /// <summary>No line.</summary>
    None = 0,

    /// <summary>A line under the baseline, at the position and with the thickness the font gives.</summary>
    Underline = 1,

    /// <summary>A line through the text, at the position and with the thickness the font gives.</summary>
    Strikethrough = 2,

    /// <summary>A line at the ascent of the font, with the thickness of its underline.</summary>
    Overline = 4,
}
