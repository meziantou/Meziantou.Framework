using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>A component value: a token, a function, or a simple block.</summary>
public abstract class CssComponentValueSyntax : CssSyntaxNode
{
    private protected CssComponentValueSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
