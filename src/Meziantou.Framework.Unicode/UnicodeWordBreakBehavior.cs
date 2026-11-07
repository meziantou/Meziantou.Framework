namespace Meziantou.Framework;

/// <summary>Specifies whether a line can break inside a word.</summary>
public enum UnicodeWordBreakBehavior
{
    /// <summary>The default rules of the Unicode Line Breaking Algorithm.</summary>
    Normal = 0,

    /// <summary>A line can break between any two letters or digits: they behave as ideographs.</summary>
    BreakAll = 1,

    /// <summary>A line does not break between two letters, digits or ideographs: text written without spaces, such as Chinese, Japanese and Korean, only breaks at punctuation and spaces.</summary>
    KeepAll = 2,
}
