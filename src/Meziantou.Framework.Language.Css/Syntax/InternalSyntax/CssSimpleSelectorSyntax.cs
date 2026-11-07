using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>One of the simple selectors a compound selector is made of.</summary>
internal abstract class CssSimpleSelectorSyntax : CssSyntaxNode
{
    protected CssSimpleSelectorSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
