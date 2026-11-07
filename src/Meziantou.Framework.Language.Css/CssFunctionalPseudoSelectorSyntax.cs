namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a pseudo-class or a pseudo-element with arguments, such as <c>:is(a, b)</c>, <c>:nth-child(2n+1)</c>, or <c>::part(label)</c>.</summary>
/// <remarks>
/// What <see cref="Argument"/> holds depends on the name: a <see cref="CssSelectorListSyntax"/> for <c>:is()</c>,
/// <c>:where()</c>, <c>:not()</c>, and <c>:has()</c>, a <see cref="CssNthArgumentSyntax"/> for <c>:nth-child()</c> and
/// the like, a <see cref="CssCompoundSelectorSyntax"/> for <c>:host()</c> and <c>::slotted()</c>, a
/// <see cref="CssNameListSyntax"/> for the ones that take names, and a <see cref="CssGenericPreludeSyntax"/> for the
/// ones this parser does not know.
/// </remarks>
public sealed partial class CssFunctionalPseudoSelectorSyntax
{
    /// <summary>Gets the name, without the colons and the parenthesis and with its escapes resolved, such as <c>nth-child</c>.</summary>
    public string Name => FunctionToken.ValueText;

    /// <summary>Determines whether this is a pseudo-element, written with two colons.</summary>
    public bool IsPseudoElement => Kind() == SyntaxKind.FunctionalPseudoElementSelector;

    /// <summary>Gets the selector list of <c>:is()</c>, <c>:where()</c>, <c>:not()</c>, <c>:has()</c>, and the like, or <see langword="null"/>.</summary>
    public CssSelectorListSyntax? Selectors => Argument as CssSelectorListSyntax;
}
