namespace Meziantou.Framework.Language.Regex;

/// <summary>Base visitor for walking regular-expression syntax trees without returning a value.</summary>
public abstract class RegexSyntaxVisitor
{
    public virtual void Visit(RegexSyntaxNode? node)
    {
        if (node is null)
            return;

        node.Accept(this);
    }

    protected virtual void DefaultVisit(RegexSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (var child in node.ChildNodes)
        {
            Visit(child);
        }
    }

    public virtual void VisitPattern(RegexPatternSyntax node) => DefaultVisit(node);
    public virtual void VisitAlternation(RegexAlternationSyntax node) => DefaultVisit(node);
    public virtual void VisitSequence(RegexSequenceSyntax node) => DefaultVisit(node);
    public virtual void VisitQuantified(RegexQuantifiedSyntax node) => DefaultVisit(node);
    public virtual void VisitSimpleQuantifier(RegexSimpleQuantifierSyntax node) => DefaultVisit(node);
    public virtual void VisitRangeQuantifier(RegexRangeQuantifierSyntax node) => DefaultVisit(node);
    public virtual void VisitAnchor(RegexAnchorSyntax node) => DefaultVisit(node);
    public virtual void VisitAnyCharacter(RegexAnyCharacterSyntax node) => DefaultVisit(node);
    public virtual void VisitAtomicGroup(RegexAtomicGroupSyntax node) => DefaultVisit(node);
    public virtual void VisitBackreference(RegexBackreferenceSyntax node) => DefaultVisit(node);
    public virtual void VisitBacktrackingVerb(RegexBacktrackingVerbSyntax node) => DefaultVisit(node);
    public virtual void VisitBalancingGroup(RegexBalancingGroupSyntax node) => DefaultVisit(node);
    public virtual void VisitBranchResetGroup(RegexBranchResetGroupSyntax node) => DefaultVisit(node);
    public virtual void VisitCapturingGroup(RegexCapturingGroupSyntax node) => DefaultVisit(node);
    public virtual void VisitCharacterClass(RegexCharacterClassSyntax node) => DefaultVisit(node);
    public virtual void VisitCharacterClassEscape(RegexCharacterClassEscapeSyntax node) => DefaultVisit(node);
    public virtual void VisitCharacterEscape(RegexCharacterEscapeSyntax node) => DefaultVisit(node);
    public virtual void VisitCharacterRange(RegexCharacterRangeSyntax node) => DefaultVisit(node);
    public virtual void VisitClassSubtraction(RegexClassSubtractionSyntax node) => DefaultVisit(node);
    public virtual void VisitCollatingElement(RegexCollatingElementSyntax node) => DefaultVisit(node);
    public virtual void VisitConditional(RegexConditionalSyntax node) => DefaultVisit(node);
    public virtual void VisitConditionalReference(RegexConditionalReferenceSyntax node) => DefaultVisit(node);
    public virtual void VisitInlineOptions(RegexInlineOptionsSyntax node) => DefaultVisit(node);
    public virtual void VisitLiteral(RegexLiteralSyntax node) => DefaultVisit(node);
    public virtual void VisitLookaround(RegexLookaroundSyntax node) => DefaultVisit(node);
    public virtual void VisitNamedBackreference(RegexNamedBackreferenceSyntax node) => DefaultVisit(node);
    public virtual void VisitNamedGroup(RegexNamedGroupSyntax node) => DefaultVisit(node);
    public virtual void VisitNonCapturingGroup(RegexNonCapturingGroupSyntax node) => DefaultVisit(node);
    public virtual void VisitOptionsGroup(RegexOptionsGroupSyntax node) => DefaultVisit(node);
    public virtual void VisitPosixCharacterClass(RegexPosixCharacterClassSyntax node) => DefaultVisit(node);
    public virtual void VisitQuotedLiteral(RegexQuotedLiteralSyntax node) => DefaultVisit(node);
    public virtual void VisitRecursion(RegexRecursionSyntax node) => DefaultVisit(node);
    public virtual void VisitSkippedText(RegexSkippedTextSyntax node) => DefaultVisit(node);
    public virtual void VisitUnicodeCategory(RegexUnicodeCategorySyntax node) => DefaultVisit(node);
}

/// <summary>Base visitor for walking regular-expression syntax trees and returning a value.</summary>
public abstract class RegexSyntaxVisitor<TResult>
{
    public virtual TResult Visit(RegexSyntaxNode? node)
    {
        if (node is null)
            return default!;

        return node.Accept(this);
    }

    protected virtual TResult DefaultVisit(RegexSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return default!;
    }

    public virtual TResult VisitPattern(RegexPatternSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAlternation(RegexAlternationSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSequence(RegexSequenceSyntax node) => DefaultVisit(node);
    public virtual TResult VisitQuantified(RegexQuantifiedSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSimpleQuantifier(RegexSimpleQuantifierSyntax node) => DefaultVisit(node);
    public virtual TResult VisitRangeQuantifier(RegexRangeQuantifierSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAnchor(RegexAnchorSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAnyCharacter(RegexAnyCharacterSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAtomicGroup(RegexAtomicGroupSyntax node) => DefaultVisit(node);
    public virtual TResult VisitBackreference(RegexBackreferenceSyntax node) => DefaultVisit(node);
    public virtual TResult VisitBacktrackingVerb(RegexBacktrackingVerbSyntax node) => DefaultVisit(node);
    public virtual TResult VisitBalancingGroup(RegexBalancingGroupSyntax node) => DefaultVisit(node);
    public virtual TResult VisitBranchResetGroup(RegexBranchResetGroupSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCapturingGroup(RegexCapturingGroupSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCharacterClass(RegexCharacterClassSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCharacterClassEscape(RegexCharacterClassEscapeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCharacterEscape(RegexCharacterEscapeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCharacterRange(RegexCharacterRangeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitClassSubtraction(RegexClassSubtractionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCollatingElement(RegexCollatingElementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitConditional(RegexConditionalSyntax node) => DefaultVisit(node);
    public virtual TResult VisitConditionalReference(RegexConditionalReferenceSyntax node) => DefaultVisit(node);
    public virtual TResult VisitInlineOptions(RegexInlineOptionsSyntax node) => DefaultVisit(node);
    public virtual TResult VisitLiteral(RegexLiteralSyntax node) => DefaultVisit(node);
    public virtual TResult VisitLookaround(RegexLookaroundSyntax node) => DefaultVisit(node);
    public virtual TResult VisitNamedBackreference(RegexNamedBackreferenceSyntax node) => DefaultVisit(node);
    public virtual TResult VisitNamedGroup(RegexNamedGroupSyntax node) => DefaultVisit(node);
    public virtual TResult VisitNonCapturingGroup(RegexNonCapturingGroupSyntax node) => DefaultVisit(node);
    public virtual TResult VisitOptionsGroup(RegexOptionsGroupSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPosixCharacterClass(RegexPosixCharacterClassSyntax node) => DefaultVisit(node);
    public virtual TResult VisitQuotedLiteral(RegexQuotedLiteralSyntax node) => DefaultVisit(node);
    public virtual TResult VisitRecursion(RegexRecursionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSkippedText(RegexSkippedTextSyntax node) => DefaultVisit(node);
    public virtual TResult VisitUnicodeCategory(RegexUnicodeCategorySyntax node) => DefaultVisit(node);
}
