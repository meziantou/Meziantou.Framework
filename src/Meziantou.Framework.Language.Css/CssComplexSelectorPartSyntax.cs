namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a compound selector of a complex selector, together with the combinator before it.</summary>
/// <remarks>
/// Whitespace is trivia, so the descendant combinator has no node: a part after the first with no
/// <see cref="Combinator"/> is a descendant of the part before it.
/// </remarks>
public sealed partial class CssComplexSelectorPartSyntax
{
    /// <summary>Gets how this part relates to the one before it.</summary>
    public CssCombinatorKind CombinatorKind => Combinator?.Kind() switch
    {
        SyntaxKind.ChildCombinator => CssCombinatorKind.Child,
        SyntaxKind.NextSiblingCombinator => CssCombinatorKind.NextSibling,
        SyntaxKind.SubsequentSiblingCombinator => CssCombinatorKind.SubsequentSibling,
        SyntaxKind.ColumnCombinator => CssCombinatorKind.Column,
        _ => Parent is CssComplexSelectorSyntax selector && selector.Parts.Count > 0 && selector.Parts[0] == this ? CssCombinatorKind.None : CssCombinatorKind.Descendant,
    };
}
