using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>A qualified rule, such as a style rule, or an at-rule.</summary>
public abstract class CssRuleSyntax : CssStatementSyntax
{
    private protected CssRuleSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets what comes before the block, or <see langword="null"/> when there is nothing there.</summary>
    /// <remarks>
    /// A prelude the rule has a grammar for is parsed with it, such as a <see cref="CssSelectorListSyntax"/> for a style
    /// rule. A <see cref="CssGenericPreludeSyntax"/> stands for a prelude that has no grammar, or that does not match it;
    /// in the latter case, a browser drops the rule, and the tree reports why.
    /// </remarks>
    public abstract CssPreludeSyntax? GetPrelude();

    /// <summary>Gets the block of the rule, or <see langword="null"/> for an at-rule that ends with a semicolon.</summary>
    public abstract CssBlockSyntax? GetBlock();
}
