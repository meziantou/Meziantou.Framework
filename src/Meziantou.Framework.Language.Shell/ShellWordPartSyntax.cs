namespace Meziantou.Framework.Language.Shell;

/// <summary>Base type for the pieces a <see cref="ShellWordSyntax"/> is built from.</summary>
public abstract class ShellWordPartSyntax : ShellSyntaxNode
{
    protected ShellWordPartSyntax(ShellSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<ShellSyntaxToken>? tokens = null)
        : base(kind, fullText, fullStart, tokens)
    {
    }
}
