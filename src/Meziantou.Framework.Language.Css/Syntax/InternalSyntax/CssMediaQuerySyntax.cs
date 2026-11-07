using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>An item of a media query list.</summary>
internal abstract class CssMediaQuerySyntax : CssSyntaxNode
{
    protected CssMediaQuerySyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
