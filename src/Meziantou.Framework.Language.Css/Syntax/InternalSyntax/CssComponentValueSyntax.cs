using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>A component value: a token, a function, or a simple block.</summary>
internal abstract class CssComponentValueSyntax : CssSyntaxNode
{
    protected CssComponentValueSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
