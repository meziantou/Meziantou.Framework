namespace Meziantou.Framework.Xml;

/// <summary>
/// Represents a document type declaration (<c>&lt;!DOCTYPE ...&gt;</c>).
/// </summary>
/// <example>
/// <code>
/// var doctype = SyntaxFactory.DocumentType("html", null);
/// var updated = doctype.WithName("root");
/// </code>
/// </example>
public sealed class XmlDocumentTypeSyntax : XmlSyntaxNode
{
    public XmlDocumentTypeSyntax(string name, string? value, string fullText)
        : base(XmlSyntaxKind.XmlDocumentType, fullText, [new XmlSyntaxToken(XmlSyntaxKind.DocumentTypeToken, fullText)])
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string? Value { get; }

    public XmlDocumentTypeSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, Name, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.DocumentType(name, Value);
    }

    public XmlDocumentTypeSyntax WithValue(string? value)
    {
        if (string.Equals(value, Value, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.DocumentType(Name, value);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitDocumentType(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitDocumentType(this);
}
