using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell;

/// <summary>A construct that can stand in a statement list.</summary>
public abstract class ShellStatementSyntax : ShellSyntaxNode
{
    private protected ShellStatementSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
