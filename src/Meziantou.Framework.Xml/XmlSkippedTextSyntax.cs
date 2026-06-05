namespace Meziantou.Framework.Xml;

/// <summary>Represents source text that could not be parsed into a valid XML construct.</summary>
/// <example>
/// <code>
/// var node = new XmlSkippedTextSyntax("&lt;invalid");
/// var updated = node.WithText("&lt;still-invalid");
/// </code>
/// </example>
public sealed class XmlSkippedTextSyntax : XmlSyntaxNode
{
    public XmlSkippedTextSyntax(string text)
        : base(XmlSyntaxKind.XmlSkippedText, text, [new XmlSyntaxToken(XmlSyntaxKind.SkippedTextToken, text)])
    {
        Text = text;
    }

    public string Text { get; }

    public XmlSkippedTextSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return this;

        return new XmlSkippedTextSyntax(text);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitSkippedText(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitSkippedText(this);
}
