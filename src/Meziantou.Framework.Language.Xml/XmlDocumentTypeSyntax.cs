namespace Meziantou.Framework.Language.Xml;

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
    public XmlDocumentTypeSyntax(string name, string? value, string fullText, int fullStart = 0)
        : base(XmlSyntaxKind.XmlDocumentType, fullText, [new XmlSyntaxToken(XmlSyntaxKind.DocumentTypeToken, fullText, fullStart: fullStart)], fullStart)
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

        // Value holds the whole internal content, which starts with the name, so the name has to be swapped inside
        // it too. Passing the old value through would keep rendering the old name while Name reported the new one.
        return SyntaxFactory.DocumentType(name, ReplaceLeadingName(Value, Name, name));
    }

    public XmlDocumentTypeSyntax WithValue(string? value)
    {
        if (string.Equals(value, Value, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.DocumentType(Name, value);
    }

    /// <summary>Returns whether <paramref name="value"/> begins with <paramref name="name"/> as a whole token.</summary>
    internal static bool StartsWithName(string value, string name)
    {
        if (name.Length == 0 || !value.StartsWith(name, StringComparison.Ordinal))
            return false;

        return value.Length == name.Length || char.IsWhiteSpace(value[name.Length]);
    }

    private static string? ReplaceLeadingName(string? value, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(value) || !StartsWithName(value, oldName))
            return value;

        return string.Concat(newName, value.AsSpan(oldName.Length));
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitDocumentType(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitDocumentType(this);
}
