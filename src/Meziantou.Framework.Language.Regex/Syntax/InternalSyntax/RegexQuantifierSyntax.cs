using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>The base of the operator part of a quantified term.</summary>
internal abstract class RegexQuantifierSyntax : RegexSyntaxNode
{
    protected RegexQuantifierSyntax(SyntaxKind kind, RegexPatternOptions options, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, options, diagnostics, annotations)
    {
    }
}
