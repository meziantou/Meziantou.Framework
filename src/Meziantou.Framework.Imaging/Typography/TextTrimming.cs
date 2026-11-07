namespace Meziantou.Framework.Imaging.Typography;

/// <summary>What replaces the text that does not fit the layout.</summary>
public enum TextTrimming
{
    /// <summary>Nothing: the text that does not fit is not part of the layout, or overflows its width when lines are not wrapped.</summary>
    None = 0,

    /// <summary>The last line that fits ends with the ellipsis, after the last character that leaves room for it.</summary>
    Character = 1,

    /// <summary>The last line that fits ends with the ellipsis, after the last word that leaves room for it.</summary>
    Word = 2,
}
