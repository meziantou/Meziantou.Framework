namespace Meziantou.Framework.Language.Css;

/// <summary>How a compound selector relates to the one before it.</summary>
public enum CssCombinatorKind
{
    /// <summary>The compound selector is the first of its complex selector, and has no combinator.</summary>
    None,

    /// <summary>Whitespace: a descendant of the element the selector before it matches.</summary>
    Descendant,

    /// <summary><c>&gt;</c>: a child.</summary>
    Child,

    /// <summary><c>+</c>: the next sibling.</summary>
    NextSibling,

    /// <summary><c>~</c>: any later sibling.</summary>
    SubsequentSibling,

    /// <summary><c>||</c>: a cell of the column.</summary>
    Column,
}
