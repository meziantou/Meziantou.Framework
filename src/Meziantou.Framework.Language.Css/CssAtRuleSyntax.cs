namespace Meziantou.Framework.Language.Css;

/// <summary>Represents an at-rule, such as <c>@media</c>: an at-keyword, a prelude, then a block or a semicolon.</summary>
public sealed partial class CssAtRuleSyntax
{
    /// <summary>Gets the name of the rule, without the <c>@</c> and with its escapes resolved, such as <c>media</c>.</summary>
    public string Name => AtKeywordToken.ValueText;

    public override CssPreludeSyntax? GetPrelude() => Prelude;

    public override CssBlockSyntax? GetBlock() => Block;
}
