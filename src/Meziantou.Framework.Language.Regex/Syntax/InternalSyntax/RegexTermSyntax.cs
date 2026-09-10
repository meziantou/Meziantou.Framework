using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>The base of one element of a sequence.</summary>
internal abstract class RegexTermSyntax : RegexSyntaxNode
{
    protected RegexTermSyntax(SyntaxKind kind, RegexPatternOptions options, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, options, diagnostics, annotations)
    {
    }
}
