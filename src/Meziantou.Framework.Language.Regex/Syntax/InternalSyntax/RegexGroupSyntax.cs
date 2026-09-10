using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>The base of every parenthesized construct.</summary>
internal abstract class RegexGroupSyntax : RegexAtomSyntax
{
    protected RegexGroupSyntax(SyntaxKind kind, RegexPatternOptions options, RegexPatternOptions innerOptions, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, options, diagnostics, annotations)
        => InnerOptions = innerOptions;

    /// <summary>The options in effect inside the group, after any inline options in its header were applied.</summary>
    public RegexPatternOptions InnerOptions { get; }
}
