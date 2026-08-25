namespace Meziantou.Framework.Language.Shell;

/// <summary>Base type for shell constructs that produce a value.</summary>
public abstract class ShellExpressionSyntax : ShellSyntaxNode
{
    protected ShellExpressionSyntax(ShellSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<ShellSyntaxToken>? tokens = null)
        : base(kind, fullText, fullStart, tokens)
    {
    }
}
