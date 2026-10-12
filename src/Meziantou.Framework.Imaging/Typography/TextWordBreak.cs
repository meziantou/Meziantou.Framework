namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Where a line can be broken inside a word.</summary>
public enum TextWordBreak
{
    /// <summary>The rules of each script: between the ideographs of Chinese and Japanese, not inside the words of other scripts.</summary>
    Normal = 0,

    /// <summary>A line can also be broken between any two letters.</summary>
    BreakAll = 1,

    /// <summary>A line is not broken between ideographs either: words are only separated by spaces and punctuation.</summary>
    KeepAll = 2,
}
