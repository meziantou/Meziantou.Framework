namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents an XML element, including its attributes and child content.</summary>
/// <example>
/// <code>
/// var element = SyntaxFactory.Element("package", [], [new XmlTextSyntax("value")]);
/// var updated = element.WithName("dependency");
/// </code>
/// </example>
public sealed class XmlElementSyntax : XmlSyntaxNode
{
    private readonly IReadOnlyList<XmlSyntaxNode> _childNodes;

    public XmlElementSyntax(
        string name,
        IReadOnlyList<XmlAttributeSyntax> attributes,
        IReadOnlyList<XmlSyntaxNode> content,
        XmlEndTagSyntax? endTag,
        bool isSelfClosing,
        string fullText,
        string startTagText)
        : base(isSelfClosing ? XmlSyntaxKind.XmlEmptyElement : XmlSyntaxKind.XmlElement, fullText, [new XmlSyntaxToken(XmlSyntaxKind.IdentifierToken, name, name)])
    {
        Name = name;
        Attributes = attributes ?? [];
        Content = content ?? [];
        EndTag = endTag;
        IsSelfClosing = isSelfClosing;
        StartTagText = startTagText;

        var nodes = new List<XmlSyntaxNode>(Attributes.Count + Content.Count + 1);
        nodes.AddRange(Attributes);
        nodes.AddRange(Content);
        if (EndTag is not null)
            nodes.Add(EndTag);

        _childNodes = nodes;
    }

    public string Name { get; }
    public IReadOnlyList<XmlAttributeSyntax> Attributes { get; }
    public IReadOnlyList<XmlSyntaxNode> Content { get; }
    public XmlEndTagSyntax? EndTag { get; }
    public bool IsSelfClosing { get; }
    public string StartTagText { get; }
    public override IReadOnlyList<XmlSyntaxNode> ChildNodes => _childNodes;

    public XmlAttributeSyntax? GetAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.Ordinal));
    }

    public string GetInnerText()
    {
        if (IsSelfClosing)
            return string.Empty;

        var endTagText = EndTag?.ToFullString() ?? string.Empty;
        var innerTextLength = ToFullString().Length - StartTagText.Length - endTagText.Length;
        if (innerTextLength <= 0)
            return string.Empty;

        return ToFullString().Substring(StartTagText.Length, innerTextLength);
    }

    public XmlElementSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, Name, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.Element(name, Attributes, Content, IsSelfClosing);
    }

    public XmlElementSyntax WithAttributes(IEnumerable<XmlAttributeSyntax>? attributes)
    {
        var updatedAttributes = attributes?.ToArray() ?? [];
        if (updatedAttributes.SequenceEqual(Attributes))
            return this;

        return SyntaxFactory.Element(Name, updatedAttributes, Content, IsSelfClosing);
    }

    public XmlElementSyntax WithContent(IEnumerable<XmlSyntaxNode>? content)
    {
        var updatedContent = content?.ToArray() ?? [];
        if (updatedContent.SequenceEqual(Content))
            return this;

        return SyntaxFactory.Element(Name, Attributes, updatedContent, IsSelfClosing);
    }

    public XmlElementSyntax WithEndTag(XmlEndTagSyntax? endTag)
    {
        if (ReferenceEquals(endTag, EndTag))
            return this;

        return SyntaxFactory.Element(Name, Attributes, Content, endTag is null);
    }

    public XmlElementSyntax WithInnerText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (IsSelfClosing)
            throw new InvalidOperationException("Cannot set the inner text of a self-closing XML element.");

        if (string.Equals(GetInnerText(), text, StringComparison.Ordinal))
            return this;

        var endTagText = EndTag?.ToFullString() ?? string.Empty;
        return new XmlElementSyntax(
            Name,
            Attributes,
            [new XmlTextSyntax(text)],
            EndTag,
            isSelfClosing: false,
            StartTagText + text + endTagText,
            StartTagText);
    }

    public XmlElementSyntax WithLeadingTrivia(params ReadOnlySpan<XmlSyntaxTrivia> leadingTrivia)
    {
        var triviaText = ConcatenateTrivia(leadingTrivia);
        var nameStart = GetElementNameStart(StartTagText);
        if (nameStart < 0)
            return this;

        var updatedStartTag = "<" + triviaText + StartTagText[nameStart..];
        if (string.Equals(updatedStartTag, StartTagText, StringComparison.Ordinal))
            return this;

        return WithStartTagText(updatedStartTag);
    }

    public XmlElementSyntax WithTrailingTrivia(params ReadOnlySpan<XmlSyntaxTrivia> trailingTrivia)
    {
        var triviaText = ConcatenateTrivia(trailingTrivia);
        var nameStart = GetElementNameStart(StartTagText);
        if (nameStart < 0)
            return this;

        var nameEnd = nameStart + Name.Length;
        if (nameEnd > StartTagText.Length)
            return this;

        var separatorEnd = nameEnd;
        while (separatorEnd < StartTagText.Length && char.IsWhiteSpace(StartTagText[separatorEnd]))
        {
            separatorEnd++;
        }

        var updatedStartTag = StartTagText[..nameEnd] + triviaText + StartTagText[separatorEnd..];
        if (string.Equals(updatedStartTag, StartTagText, StringComparison.Ordinal))
            return this;

        return WithStartTagText(updatedStartTag);
    }

    private XmlElementSyntax WithStartTagText(string startTagText)
    {
        var endTagText = EndTag?.ToFullString() ?? string.Empty;
        var innerTextLength = ToFullString().Length - StartTagText.Length - endTagText.Length;
        var innerText = innerTextLength > 0 ? ToFullString().Substring(StartTagText.Length, innerTextLength) : string.Empty;
        return new XmlElementSyntax(Name, Attributes, Content, EndTag, IsSelfClosing, startTagText + innerText + endTagText, startTagText);
    }

    private static int GetElementNameStart(string startTagText)
    {
        if (string.IsNullOrEmpty(startTagText) || startTagText[0] != '<')
            return -1;

        var current = 1;
        while (current < startTagText.Length && char.IsWhiteSpace(startTagText[current]))
        {
            current++;
        }

        return current < startTagText.Length ? current : -1;
    }

    private static string ConcatenateTrivia(ReadOnlySpan<XmlSyntaxTrivia> trivia)
    {
        if (trivia.IsEmpty)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var currentTrivia in trivia)
        {
            builder.Append(currentTrivia.Text);
        }

        return builder.ToString();
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitElement(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitElement(this);
}
