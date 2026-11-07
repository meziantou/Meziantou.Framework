using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>Something that can stand in a style sheet or a block: a rule, a declaration, or text a browser drops.</summary>
internal abstract class CssStatementSyntax : CssSyntaxNode
{
    protected CssStatementSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
