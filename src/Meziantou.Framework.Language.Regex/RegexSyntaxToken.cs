using System.Diagnostics;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a lexical token that belongs to a regular-expression syntax node.</summary>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class RegexSyntaxToken
{
    public RegexSyntaxToken(
        RegexSyntaxKind kind,
        string text,
        string? valueText = null,
        bool isMissing = false,
        IReadOnlyList<RegexSyntaxTrivia>? leadingTrivia = null,
        IReadOnlyList<RegexSyntaxTrivia>? trailingTrivia = null,
        int fullStart = 0)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        ValueText = valueText ?? Text;
        IsMissing = isMissing;
        // Copied rather than kept by reference: the spans below are measured now, and a caller who mutated their list
        // afterwards would change ToFullString() while Span and FullSpan went on describing the old text.
        LeadingTrivia = leadingTrivia is { Count: > 0 } ? [.. leadingTrivia] : [];
        TrailingTrivia = trailingTrivia is { Count: > 0 } ? [.. trailingTrivia] : [];

        var leadingLength = SumTextLength(LeadingTrivia);
        Span = new TextSpan(fullStart + leadingLength, Text.Length);
        FullSpan = new TextSpan(fullStart, leadingLength + Text.Length + SumTextLength(TrailingTrivia));
    }

    public RegexSyntaxKind Kind { get; }

    /// <summary>The exact source text of the token, excluding trivia.</summary>
    public string Text { get; }

    /// <summary>The token text with escapes and quoting resolved. Equal to <see cref="Text"/> when there is nothing to resolve.</summary>
    public string ValueText { get; }

    /// <summary>Returns <see langword="true"/> when the parser synthesized this token because the source was missing it.</summary>
    public bool IsMissing { get; }

    public TextSpan Span { get; }
    public TextSpan FullSpan { get; }
    public IReadOnlyList<RegexSyntaxTrivia> LeadingTrivia { get; }
    public IReadOnlyList<RegexSyntaxTrivia> TrailingTrivia { get; }
    public RegexSyntaxNode? Parent { get; internal set; }

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

    public RegexSyntaxToken WithText(string text) => new(Kind, text, valueText: null, IsMissing, LeadingTrivia, TrailingTrivia, FullSpan.Start);

    public RegexSyntaxToken WithLeadingTrivia(IEnumerable<RegexSyntaxTrivia>? leadingTrivia)
    {
        var trivia = leadingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(LeadingTrivia))
            return this;

        return new RegexSyntaxToken(Kind, Text, ValueText, IsMissing, trivia, TrailingTrivia, FullSpan.Start);
    }

    public RegexSyntaxToken WithTrailingTrivia(IEnumerable<RegexSyntaxTrivia>? trailingTrivia)
    {
        var trivia = trailingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(TrailingTrivia))
            return this;

        return new RegexSyntaxToken(Kind, Text, ValueText, IsMissing, LeadingTrivia, trivia, FullSpan.Start);
    }

    public override string ToString() => Text;

    private static int SumTextLength(IReadOnlyList<RegexSyntaxTrivia> trivia)
    {
        var length = 0;
        foreach (var item in trivia)
        {
            length += item.Text.Length;
        }

        return length;
    }
}
