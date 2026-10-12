namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Whether a line that is wider than the layout continues on the next line.</summary>
public enum TextWrapping
{
    /// <summary>A line is broken where the Unicode line breaking algorithm allows it, so that it fits the width of the layout.</summary>
    Wrap = 0,

    /// <summary>Lines are only broken at the line breaks of the text.</summary>
    NoWrap = 1,
}
