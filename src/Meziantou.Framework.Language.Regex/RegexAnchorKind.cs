namespace Meziantou.Framework.Language.Regex;

/// <summary>Identifies which zero-width assertion an anchor is.</summary>
public enum RegexAnchorKind
{
    /// <summary><c>^</c>.</summary>
    Caret,

    /// <summary><c>$</c>.</summary>
    Dollar,

    /// <summary><c>\A</c>.</summary>
    StartOfInput,

    /// <summary><c>\Z</c>.</summary>
    EndOfInputBeforeFinalLineBreak,

    /// <summary><c>\z</c>.</summary>
    EndOfInput,

    /// <summary><c>\G</c>.</summary>
    ContiguousMatch,

    /// <summary><c>\b</c>.</summary>
    WordBoundary,

    /// <summary><c>\B</c>.</summary>
    NonWordBoundary,

    /// <summary><c>\K</c>.</summary>
    KeepOut,
}
