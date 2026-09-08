namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents plain text content inside an XML element.</summary>
/// <example>
/// <code>
/// var text = new XmlTextSyntax("hello");
/// var updated = text.WithText("world");
/// </code>
/// </example>
public sealed class XmlTextSyntax : XmlSyntaxNode
{
    public XmlTextSyntax(string text, int fullStart = 0)
        : base(XmlSyntaxKind.XmlText, text, fullStart, [new XmlSyntaxToken(XmlSyntaxKind.TextToken, text, fullStart: fullStart)])
    {
        Text = text;
    }

    public string Text { get; }

    public XmlTextSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return this;

        return new XmlTextSyntax(text, FullSpan.Start);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitText(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitText(this);
}
