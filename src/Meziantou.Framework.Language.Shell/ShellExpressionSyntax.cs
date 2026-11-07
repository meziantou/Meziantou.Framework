using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell;

/// <summary>A construct that produces a value.</summary>
public abstract class ShellExpressionSyntax : ShellSyntaxNode
{
    private protected ShellExpressionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
