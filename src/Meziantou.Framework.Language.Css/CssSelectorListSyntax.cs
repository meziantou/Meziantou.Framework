namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a comma-separated list of selectors, such as the prelude of a style rule.</summary>
public sealed partial class CssSelectorListSyntax
{
    /// <summary>Determines whether any of the selectors starts with a combinator, as a selector of a nested rule may: <c>&gt; a</c>.</summary>
    public bool IsRelative => Selectors.OfType<CssComplexSelectorSyntax>().Any(selector => selector.IsRelative);
}
