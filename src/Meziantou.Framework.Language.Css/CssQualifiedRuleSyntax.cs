namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a qualified rule: a prelude, then a block. A style rule is one, and so is a keyframe rule.</summary>
public sealed partial class CssQualifiedRuleSyntax
{
    /// <summary>Gets the selectors of a style rule, or <see langword="null"/> when the prelude is not a valid selector list.</summary>
    public CssSelectorListSyntax? Selectors => Prelude as CssSelectorListSyntax;

    public override CssPreludeSyntax? GetPrelude() => Prelude;

    public override CssBlockSyntax GetBlock() => Block;
}
