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
    public XmlTextSyntax(string text)
        : base(XmlSyntaxKind.XmlText, text, [new XmlSyntaxToken(XmlSyntaxKind.TextToken, text)])
    {
        Text = text;
    }

    public string Text { get; }

    public XmlTextSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return this;

        return new XmlTextSyntax(text);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitText(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitText(this);
}
