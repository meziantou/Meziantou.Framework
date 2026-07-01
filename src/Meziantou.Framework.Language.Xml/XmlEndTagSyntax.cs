namespace Meziantou.Framework.Language.Xml;

/// <summary>
/// Represents an XML end tag (for example <c>&lt;/item&gt;</c>).
/// </summary>
/// <example>
/// <code>
/// var endTag = new XmlEndTagSyntax("item", "&lt;/item&gt;");
/// var updated = endTag.WithName("other");
/// </code>
/// </example>
public sealed class XmlEndTagSyntax : XmlSyntaxNode
{
    public XmlEndTagSyntax(string name, string fullText)
        : base(XmlSyntaxKind.XmlEndTag, fullText, [new XmlSyntaxToken(XmlSyntaxKind.IdentifierToken, name)])
    {
        Name = name;
    }

    public string Name { get; }

    public XmlEndTagSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, Name, StringComparison.Ordinal))
            return this;

        return new XmlEndTagSyntax(name, $"</{name}>");
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitEndTag(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitEndTag(this);
}
