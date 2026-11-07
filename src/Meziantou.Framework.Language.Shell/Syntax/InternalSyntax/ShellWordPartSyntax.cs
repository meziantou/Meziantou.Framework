using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>One of the pieces a word is built from.</summary>
internal abstract class ShellWordPartSyntax : ShellSyntaxNode
{
    protected ShellWordPartSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
