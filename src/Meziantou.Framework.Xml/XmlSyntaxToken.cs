using System.Diagnostics;

namespace Meziantou.Framework.Xml;

/// <summary>Represents a lexical token that belongs to a syntax node.</summary>
/// <example>
/// <code>
/// var token = element.Tokens.First();
/// var text = token.ToFullString();
/// </code>
/// </example>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class XmlSyntaxToken
{
    public XmlSyntaxToken(
        XmlSyntaxKind kind,
        string text,
        string? valueText = null,
        bool isMissing = false,
        IReadOnlyList<XmlSyntaxTrivia>? leadingTrivia = null,
        IReadOnlyList<XmlSyntaxTrivia>? trailingTrivia = null)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        ValueText = valueText ?? Text;
        IsMissing = isMissing;
        LeadingTrivia = leadingTrivia ?? [];
        TrailingTrivia = trailingTrivia ?? [];
    }

    public XmlSyntaxKind Kind { get; }
    public string Text { get; }
    public string ValueText { get; }
    public bool IsMissing { get; }
    public TextSpan Span => new(0, Text.Length);
    public IReadOnlyList<XmlSyntaxTrivia> LeadingTrivia { get; }
    public IReadOnlyList<XmlSyntaxTrivia> TrailingTrivia { get; }
    internal XmlSyntaxNode? Parent { get; set; }

    public string ToFullString()
    {
        if (LeadingTrivia.Count == 0 && TrailingTrivia.Count == 0)
            return Text;

        var buffer = new System.Text.StringBuilder();
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

    public XmlSyntaxToken WithText(string text) => new(Kind, text, valueText: null, IsMissing, LeadingTrivia, TrailingTrivia);

    public XmlSyntaxToken WithLeadingTrivia(IEnumerable<XmlSyntaxTrivia>? leadingTrivia)
    {
        var trivia = leadingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(LeadingTrivia))
            return this;

        return new XmlSyntaxToken(Kind, Text, ValueText, IsMissing, trivia, TrailingTrivia);
    }

    public XmlSyntaxToken WithTrailingTrivia(IEnumerable<XmlSyntaxTrivia>? trailingTrivia)
    {
        var trivia = trailingTrivia?.ToArray() ?? [];
        if (trivia.SequenceEqual(TrailingTrivia))
            return this;

        return new XmlSyntaxToken(Kind, Text, ValueText, IsMissing, LeadingTrivia, trivia);
    }
}
