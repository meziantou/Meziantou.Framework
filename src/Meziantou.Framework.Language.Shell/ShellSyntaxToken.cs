using System.Diagnostics;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a lexical token that belongs to a shell syntax node.</summary>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class ShellSyntaxToken
{
    public ShellSyntaxToken(
        ShellSyntaxKind kind,
        string text,
        string? valueText = null,
        bool isMissing = false,
        IReadOnlyList<ShellSyntaxTrivia>? leadingTrivia = null,
        IReadOnlyList<ShellSyntaxTrivia>? trailingTrivia = null,
        int fullStart = 0)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        ValueText = valueText ?? Text;
        IsMissing = isMissing;
        LeadingTrivia = leadingTrivia ?? [];
        TrailingTrivia = trailingTrivia ?? [];

        var leadingLength = SumTextLength(LeadingTrivia);
        Span = new TextSpan(fullStart + leadingLength, Text.Length);
        FullSpan = new TextSpan(fullStart, leadingLength + Text.Length + SumTextLength(TrailingTrivia));
    }

    public ShellSyntaxKind Kind { get; }

    /// <summary>The exact source text of the token, excluding trivia.</summary>
    public string Text { get; }

    /// <summary>The token text with escapes and quoting resolved. Equal to <see cref="Text"/> when there is nothing to resolve.</summary>
    public string ValueText { get; }

    /// <summary>Returns <see langword="true"/> when the parser synthesized this token because the source was missing it.</summary>
    public bool IsMissing { get; }

    public TextSpan Span { get; }
    public TextSpan FullSpan { get; }
    public IReadOnlyList<ShellSyntaxTrivia> LeadingTrivia { get; }
    public IReadOnlyList<ShellSyntaxTrivia> TrailingTrivia { get; }
    public ShellSyntaxNode? Parent { get; internal set; }

    public string ToFullString()
    {
        if (LeadingTrivia.Count == 0 && TrailingTrivia.Count == 0)
            return Text;

        var buffer = new StringBuilder();
        foreach (var trivia in LeadingTrivia)
        {
            buffer.Append(trivia.Text);
        }

        buffer.Append(Text);

        foreach (var trivia in TrailingTrivia)
        {
            buffer.Append(trivia.Text);
        }

        return buffer.ToString();
    }

    public ShellSyntaxToken WithText(string text) => new(Kind, text, valueText: null, IsMissing, LeadingTrivia, TrailingTrivia, FullSpan.Start);

    public ShellSyntaxToken WithLeadingTrivia(IEnumerable<ShellSyntaxTrivia>? leadingTrivia)
    {
        var trivia = leadingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(LeadingTrivia))
            return this;

        return new ShellSyntaxToken(Kind, Text, ValueText, IsMissing, trivia, TrailingTrivia, FullSpan.Start);
    }

    public ShellSyntaxToken WithTrailingTrivia(IEnumerable<ShellSyntaxTrivia>? trailingTrivia)
    {
        var trivia = trailingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(TrailingTrivia))
            return this;

        return new ShellSyntaxToken(Kind, Text, ValueText, IsMissing, LeadingTrivia, trivia, FullSpan.Start);
    }

    public override string ToString() => Text;

    private static int SumTextLength(IReadOnlyList<ShellSyntaxTrivia> trivia)
    {
        var length = 0;
        foreach (var item in trivia)
        {
            length += item.Text.Length;
        }

        return length;
    }
}
