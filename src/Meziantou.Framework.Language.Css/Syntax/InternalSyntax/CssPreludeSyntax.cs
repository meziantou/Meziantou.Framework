using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>What comes before the block of a rule.</summary>
internal abstract class CssPreludeSyntax : CssSyntaxNode
{
    protected CssPreludeSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
