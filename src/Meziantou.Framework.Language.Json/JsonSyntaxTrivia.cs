using System.Diagnostics;

namespace Meziantou.Framework.Language.Json;

/// <summary>Represents JSON trivia such as whitespace, line breaks, or comments.</summary>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class JsonSyntaxTrivia
{
    public JsonSyntaxTrivia(JsonSyntaxKind kind, string text, int start = 0)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Span = new TextSpan(start, Text.Length);
    }

    public JsonSyntaxKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public TextSpan FullSpan => Span;

    public JsonSyntaxTrivia WithText(string text)
    {
        return new JsonSyntaxTrivia(Kind, text, Span.Start);
    }
}
