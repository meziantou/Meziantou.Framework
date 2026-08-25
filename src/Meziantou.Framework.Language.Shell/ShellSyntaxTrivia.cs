using System.Diagnostics;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents shell trivia such as whitespace, line breaks, comments, or line continuations.</summary>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class ShellSyntaxTrivia
{
    public ShellSyntaxTrivia(ShellSyntaxKind kind, string text, int start = 0)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Span = new TextSpan(start, Text.Length);
    }

    public ShellSyntaxKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public TextSpan FullSpan => Span;

    /// <summary>Returns <see langword="true"/> when the trivia is a comment.</summary>
    public bool IsComment => Kind is ShellSyntaxKind.SingleLineCommentTrivia;

    public ShellSyntaxTrivia WithText(string text) => new(Kind, text, Span.Start);

    public override string ToString() => Text;
}
