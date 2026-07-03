using System.Diagnostics;

namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a lexical token that belongs to a JSON syntax node.</summary>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class JsonSyntaxToken
{
    public JsonSyntaxToken(
        JsonSyntaxKind kind,
        string text,
        string? valueText = null,
        bool isMissing = false,
        IReadOnlyList<JsonSyntaxTrivia>? leadingTrivia = null,
        IReadOnlyList<JsonSyntaxTrivia>? trailingTrivia = null,
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

    public JsonSyntaxKind Kind { get; }
    public string Text { get; }
    public string ValueText { get; }
    public bool IsMissing { get; }
    public TextSpan Span { get; }
    public TextSpan FullSpan { get; }
    public IReadOnlyList<JsonSyntaxTrivia> LeadingTrivia { get; }
    public IReadOnlyList<JsonSyntaxTrivia> TrailingTrivia { get; }
    internal JsonSyntaxNode? Parent { get; set; }

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

    public JsonSyntaxToken WithText(string text)
    {
        return new JsonSyntaxToken(Kind, text, valueText: null, IsMissing, LeadingTrivia, TrailingTrivia, FullSpan.Start);
    }

    public JsonSyntaxToken WithLeadingTrivia(IEnumerable<JsonSyntaxTrivia>? leadingTrivia)
    {
        var trivia = leadingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(LeadingTrivia))
            return this;

        return new JsonSyntaxToken(Kind, Text, ValueText, IsMissing, trivia, TrailingTrivia, FullSpan.Start);
    }

    public JsonSyntaxToken WithTrailingTrivia(IEnumerable<JsonSyntaxTrivia>? trailingTrivia)
    {
        var trivia = trailingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(TrailingTrivia))
            return this;

        return new JsonSyntaxToken(Kind, Text, ValueText, IsMissing, LeadingTrivia, trivia, FullSpan.Start);
    }

    private static int SumTextLength(IReadOnlyList<JsonSyntaxTrivia> trivia)
    {
        var length = 0;
        foreach (var item in trivia)
        {
            length += item.Text.Length;
        }

        return length;
    }
}
