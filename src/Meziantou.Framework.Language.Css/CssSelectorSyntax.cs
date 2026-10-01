using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>An item of a selector list: a complex selector, or one a forgiving list keeps without understanding it.</summary>
public abstract class CssSelectorSyntax : CssSyntaxNode
{
    private protected CssSelectorSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
