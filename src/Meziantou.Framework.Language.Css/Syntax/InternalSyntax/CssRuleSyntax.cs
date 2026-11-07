using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>A qualified rule or an at-rule.</summary>
internal abstract class CssRuleSyntax : CssStatementSyntax
{
    protected CssRuleSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
