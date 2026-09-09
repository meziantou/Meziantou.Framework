using System.Diagnostics;

namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents trivia (for example whitespace or line breaks) attached to a token.</summary>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class XmlSyntaxTrivia
{
    public XmlSyntaxTrivia(XmlSyntaxKind kind, string text, int start = 0)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Span = new TextSpan(start, Text.Length);
    }

    public XmlSyntaxKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public TextSpan FullSpan => Span;

    public XmlSyntaxTrivia WithText(string text) => new(Kind, text, Span.Start);
}
