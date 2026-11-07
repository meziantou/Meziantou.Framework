using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>An item of a selector list.</summary>
internal abstract class CssSelectorSyntax : CssSyntaxNode
{
    protected CssSelectorSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
