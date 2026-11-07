using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>The base of a single unquantified unit of a pattern.</summary>
internal abstract class RegexAtomSyntax : RegexTermSyntax
{
    protected RegexAtomSyntax(SyntaxKind kind, RegexPatternOptions options, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, options, diagnostics, annotations)
    {
    }
}
