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
    public XmlEndTagSyntax(string name, string fullText, int fullStart = 0)
        : base(XmlSyntaxKind.XmlEndTag, fullText, fullStart, [new XmlSyntaxToken(XmlSyntaxKind.IdentifierToken, name, fullStart: fullStart + GetNameOffset(fullText))])
    {
        Name = name;
    }

    /// <summary>The offset of the name inside <paramref name="fullText"/>, which opens with <c>&lt;/</c> and may then hold whitespace.</summary>
    private static int GetNameOffset(string fullText)
    {
        if (fullText.Length < 2 || fullText[0] != '<' || fullText[1] != '/')
            return 0;

        var current = 2;
        while (current < fullText.Length && char.IsWhiteSpace(fullText[current]))
        {
            current++;
        }

        return current;
    }

    public string Name { get; }

    public XmlEndTagSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, Name, StringComparison.Ordinal))
            return this;

        return new XmlEndTagSyntax(name, $"</{name}>", FullSpan.Start);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitEndTag(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitEndTag(this);
}
