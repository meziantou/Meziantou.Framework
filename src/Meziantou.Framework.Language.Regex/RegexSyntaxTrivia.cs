using System.Diagnostics;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents pattern trivia: whitespace, an extended-mode <c>#</c> comment, or a <c>(?#…)</c> comment.</summary>
/// <remarks>
/// A pattern has no trivia at all unless <see cref="RegexPatternOptions.IgnorePatternWhitespace"/> is in effect, which
/// inline options can switch on and off part-way through. A <c>(?#…)</c> comment is the exception: it is trivia in
/// every mode.
/// </remarks>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class RegexSyntaxTrivia
{
    public RegexSyntaxTrivia(RegexSyntaxKind kind, string text, int start = 0)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Span = new TextSpan(start, Text.Length);
    }

    public RegexSyntaxKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public TextSpan FullSpan => Span;

    /// <summary>Returns <see langword="true"/> when the trivia is a comment.</summary>
    public bool IsComment => Kind is RegexSyntaxKind.PatternCommentTrivia or RegexSyntaxKind.InlineCommentTrivia;

    public RegexSyntaxTrivia WithText(string text) => new(Kind, text, Span.Start);

    public override string ToString() => Text;
}
