namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a complex selector: compound selectors joined by combinators, such as <c>nav &gt; a:hover</c>.</summary>
public sealed partial class CssComplexSelectorSyntax
{
    /// <summary>Determines whether the selector starts with a combinator, as a selector of a nested rule may: <c>&gt; a</c>.</summary>
    public bool IsRelative => Parts.Count > 0 && Parts[0].Combinator is not null;
}
