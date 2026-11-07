namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a term followed by a quantifier, such as <c>a*</c> or <c>(ab){2,3}?</c>.</summary>
public sealed partial class RegexQuantifiedSyntax : RegexTermSyntax
{
    /// <summary>How the quantifier backtracks.</summary>
    public RegexQuantifierMode Mode => Quantifier.Mode;
}
