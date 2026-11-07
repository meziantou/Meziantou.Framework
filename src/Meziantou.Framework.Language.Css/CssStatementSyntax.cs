using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>Something that can stand in a style sheet or a block: a rule, a declaration, or text a browser drops.</summary>
public abstract class CssStatementSyntax : CssSyntaxNode
{
    private protected CssStatementSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
