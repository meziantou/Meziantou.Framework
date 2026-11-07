using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>An item of a media query list.</summary>
public abstract class CssMediaQuerySyntax : CssSyntaxNode
{
    private protected CssMediaQuerySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
