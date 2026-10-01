using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>What comes before the block of a rule: a selector list, a media query list, or the prelude of another at-rule.</summary>
public abstract class CssPreludeSyntax : CssSyntaxNode
{
    private protected CssPreludeSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
