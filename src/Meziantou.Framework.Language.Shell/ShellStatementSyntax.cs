namespace Meziantou.Framework.Language.Shell;

/// <summary>Base type for shell constructs that can appear in a statement list.</summary>
public abstract class ShellStatementSyntax : ShellSyntaxNode
{
    protected ShellStatementSyntax(ShellSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<ShellSyntaxToken>? tokens = null)
        : base(kind, fullText, fullStart, tokens)
    {
    }
}
