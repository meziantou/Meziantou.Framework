using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>A construct that can stand in a statement list.</summary>
internal abstract class ShellStatementSyntax : ShellSyntaxNode
{
    protected ShellStatementSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
