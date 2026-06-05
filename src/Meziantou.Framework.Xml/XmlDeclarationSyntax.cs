namespace Meziantou.Framework.Xml;

/// <summary>
/// Represents an XML declaration (for example <c>&lt;?xml version="1.0"?&gt;</c>).
/// </summary>
/// <example>
/// <code>
/// var declaration = SyntaxFactory.Declaration("1.0", "utf-8", "yes");
/// var updated = declaration.WithEncoding("utf-16");
/// </code>
/// </example>
public sealed class XmlDeclarationSyntax : XmlSyntaxNode
{
    private readonly IReadOnlyList<XmlSyntaxNode> _childNodes;
    private readonly List<DeclarationAttributeSegment> _attributeSegments;

    public XmlDeclarationSyntax(string version, string? encoding, string? standalone, string fullText)
        : base(XmlSyntaxKind.XmlDeclaration, fullText, [new XmlSyntaxToken(XmlSyntaxKind.DeclarationToken, fullText)])
    {
        Version = version;
        Encoding = encoding;
        Standalone = standalone;
        _attributeSegments = ParseAttributeSegments(fullText);
        _childNodes = _attributeSegments.Select(item => (XmlSyntaxNode)item.Attribute).ToArray();
        VersionAttribute = _attributeSegments.FirstOrDefault(item => string.Equals(item.Attribute.Name, "version", StringComparison.Ordinal)).Attribute;
        EncodingAttribute = _attributeSegments.FirstOrDefault(item => string.Equals(item.Attribute.Name, "encoding", StringComparison.Ordinal)).Attribute;
        StandaloneAttribute = _attributeSegments.FirstOrDefault(item => string.Equals(item.Attribute.Name, "standalone", StringComparison.Ordinal)).Attribute;
    }

    public override IReadOnlyList<XmlSyntaxNode> ChildNodes => _childNodes;
    public string Version { get; }
    public string? Encoding { get; }
    public string? Standalone { get; }
    public XmlAttributeSyntax? VersionAttribute { get; }
    public XmlAttributeSyntax? EncodingAttribute { get; }
    public XmlAttributeSyntax? StandaloneAttribute { get; }

    public XmlDeclarationSyntax WithVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (string.Equals(version, Version, StringComparison.Ordinal))
            return this;

        return UpdateRequiredAttributeValue("version", version, version, Encoding, Standalone);
    }

    public XmlDeclarationSyntax WithEncoding(string? encoding)
    {
        if (string.Equals(encoding, Encoding, StringComparison.Ordinal))
            return this;

        return UpdateOptionalAttributeValue("encoding", encoding, Version, encoding, Standalone);
    }

    public XmlDeclarationSyntax WithStandalone(string? standalone)
    {
        if (string.Equals(standalone, Standalone, StringComparison.Ordinal))
            return this;

        return UpdateOptionalAttributeValue("standalone", standalone, Version, Encoding, standalone);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitDeclaration(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitDeclaration(this);

    private XmlDeclarationSyntax UpdateRequiredAttributeValue(string attributeName, string value, string version, string? encoding, string? standalone)
    {
        if (TryGetAttributeSegment(attributeName, out var segment))
        {
            var updatedAttribute = segment.Attribute.WithValue(value);
            return ReplaceSpan(segment.Span, updatedAttribute.ToFullString(), version, encoding, standalone);
        }

        return SyntaxFactory.Declaration(version, encoding, standalone);
    }

    private XmlDeclarationSyntax UpdateOptionalAttributeValue(string attributeName, string? value, string version, string? encoding, string? standalone)
    {
        if (TryGetAttributeSegment(attributeName, out var segment))
        {
            if (value is null)
                return ReplaceSpan(segment.Span, string.Empty, version, encoding, standalone);

            var updatedAttribute = segment.Attribute.WithValue(value);
            return ReplaceSpan(segment.Span, updatedAttribute.ToFullString(), version, encoding, standalone);
        }

        if (value is null)
            return this;

        var fullText = ToFullString();
        var declarationEnd = fullText.LastIndexOf("?>", StringComparison.Ordinal);
        if (declarationEnd < 0)
            return SyntaxFactory.Declaration(version, encoding, standalone);

        var insertionIndex = declarationEnd;
        while (insertionIndex > 0 && char.IsWhiteSpace(fullText[insertionIndex - 1]))
        {
            insertionIndex--;
        }

        var newAttributeText = " " + SyntaxFactory.Attribute(attributeName, value).ToFullString();
        var updatedText = fullText[..insertionIndex] + newAttributeText + fullText[insertionIndex..];
        return new XmlDeclarationSyntax(version, encoding, standalone, updatedText);
    }

    private XmlDeclarationSyntax ReplaceSpan(TextSpan span, string replacement, string version, string? encoding, string? standalone)
    {
        var fullText = ToFullString();
        var builder = new StringBuilder(fullText.Length - span.Length + replacement.Length);
        builder.Append(fullText.AsSpan(0, span.Start));
        builder.Append(replacement);
        builder.Append(fullText.AsSpan(span.End));
        return new XmlDeclarationSyntax(version, encoding, standalone, builder.ToString());
    }

    private bool TryGetAttributeSegment(string attributeName, out DeclarationAttributeSegment segment)
    {
        foreach (var attributeSegment in _attributeSegments)
        {
            if (string.Equals(attributeSegment.Attribute.Name, attributeName, StringComparison.Ordinal))
            {
                segment = attributeSegment;
                return true;
            }
        }

        segment = default;
        return false;
    }

    private static List<DeclarationAttributeSegment> ParseAttributeSegments(string declarationText)
    {
        if (declarationText.Length == 0)
            return [];

        var start = declarationText.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ? 5 : 0;
        var end = declarationText.LastIndexOf("?>", StringComparison.Ordinal);
        if (end < 0 || start >= end)
            return [];

        var result = new List<DeclarationAttributeSegment>();
        var index = start;
        while (index < end)
        {
            var attributeStart = index;
            while (index < end && char.IsWhiteSpace(declarationText[index]))
            {
                index++;
            }

            var nameStart = index;
            while (index < end && !char.IsWhiteSpace(declarationText[index]) && declarationText[index] != '=')
            {
                index++;
            }

            if (index == nameStart)
                break;

            var name = declarationText[nameStart..index];

            while (index < end && char.IsWhiteSpace(declarationText[index]))
            {
                index++;
            }

            if (index >= end || declarationText[index] != '=')
                break;

            index++;
            while (index < end && char.IsWhiteSpace(declarationText[index]))
            {
                index++;
            }

            if (index >= end)
                break;

            string value;
            if (declarationText[index] is '"' or '\'')
            {
                var quote = declarationText[index];
                index++;
                var valueStart = index;
                while (index < end && declarationText[index] != quote)
                {
                    index++;
                }

                value = declarationText[valueStart..Math.Min(index, end)];
                if (index < end && declarationText[index] == quote)
                {
                    index++;
                }
            }
            else
            {
                var valueStart = index;
                while (index < end && !char.IsWhiteSpace(declarationText[index]))
                {
                    index++;
                }

                value = declarationText[valueStart..index];
            }

            var attributeEnd = index;
            if (attributeEnd <= attributeStart)
                continue;

            var attributeText = declarationText[attributeStart..attributeEnd];
            var attribute = new XmlAttributeSyntax(name, value, attributeText);
            result.Add(new DeclarationAttributeSegment(attribute, TextSpan.FromBounds(attributeStart, attributeEnd)));
        }

        return result;
    }

    private readonly record struct DeclarationAttributeSegment(XmlAttributeSyntax Attribute, TextSpan Span);
}
