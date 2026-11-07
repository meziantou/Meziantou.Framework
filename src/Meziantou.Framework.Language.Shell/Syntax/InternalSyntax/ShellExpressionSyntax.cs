using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>A construct that produces a value.</summary>
internal abstract class ShellExpressionSyntax : ShellSyntaxNode
{
    protected ShellExpressionSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
