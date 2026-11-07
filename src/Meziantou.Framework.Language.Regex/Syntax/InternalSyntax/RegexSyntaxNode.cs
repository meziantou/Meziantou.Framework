using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>The base of every immutable regular-expression node.</summary>
/// <remarks>
/// A regex node carries the options in effect where it starts, because inline constructs such as <c>(?i)</c> change
/// them part-way through a pattern. That makes the options part of what a node is: two spellings of the same text
/// under different options are genuinely different nodes, and keeping the options here is what stops one being shared
/// where the other belongs.
/// </remarks>
internal abstract class RegexSyntaxNode : GreenNode
{
    protected RegexSyntaxNode(SyntaxKind kind, RegexPatternOptions options, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base((int)kind, diagnostics, annotations)
        => Options = options;

    public SyntaxKind Kind => (SyntaxKind)RawKind;

    /// <summary>The options in effect at the first character of this node.</summary>
    public RegexPatternOptions Options { get; }

    public override string KindText => Kind.ToString();

    /// <summary>A regular expression separates the branches of an alternation with a bar.</summary>
    internal override GreenNode? CreateSeparator() => SyntaxFactory.Token(SyntaxKind.BarToken, "|");

    /// <remarks>
    /// A pattern has no end-of-line trivia of its own; a line break only becomes trivia in extended mode, as part of
    /// a run of whitespace. One made up entirely of line breaks counts.
    /// </remarks>
    internal override bool IsEndOfLineTrivia(GreenNode trivia)
    {
        if (trivia.RawKind != (int)SyntaxKind.WhitespaceTrivia)
            return false;

        var text = trivia.ToString();

        return text.Length > 0 && text.AsSpan().IndexOfAnyExcept('\r', '\n') < 0;
    }
}
