using System.Diagnostics;

namespace Meziantou.Framework.Xml;

/// <summary>Represents trivia (for example whitespace or line breaks) attached to a token.</summary>
[DebuggerDisplay("{Kind}: '{Text}'")]
public sealed class XmlSyntaxTrivia
{
    public XmlSyntaxTrivia(XmlSyntaxKind kind, string text)
    {
        Kind = kind;
        Text = text ?? string.Empty;
    }

    public XmlSyntaxKind Kind { get; }
    public string Text { get; }

    public XmlSyntaxTrivia WithText(string text) => new(Kind, text);
}
