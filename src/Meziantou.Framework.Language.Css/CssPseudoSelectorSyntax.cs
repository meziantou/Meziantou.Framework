namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a pseudo-class or a pseudo-element without arguments, such as <c>:hover</c> or <c>::before</c>.</summary>
public sealed partial class CssPseudoSelectorSyntax
{
    /// <summary>Gets the name, without the colons and with its escapes resolved, such as <c>hover</c>.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Determines whether this is a pseudo-element: written with two colons, or one of the four CSS 2 wrote with one.</summary>
    public bool IsPseudoElement => Kind() == SyntaxKind.PseudoElementSelector;
}
