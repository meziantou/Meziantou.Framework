using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell;

/// <summary>One of the pieces a <see cref="ShellWordSyntax"/> is built from.</summary>
public abstract class ShellWordPartSyntax : ShellSyntaxNode
{
    private protected ShellWordPartSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
