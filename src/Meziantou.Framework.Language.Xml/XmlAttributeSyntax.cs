namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents an XML attribute in a start tag.</summary>
/// <example>
/// <code>
/// var attribute = SyntaxFactory.Attribute("version", "1.0.0");
/// var updated = attribute.WithValue("2.0.0");
/// </code>
/// </example>
public sealed class XmlAttributeSyntax : XmlSyntaxNode
{
    public XmlAttributeSyntax(string name, string value, string fullText)
        : base(XmlSyntaxKind.XmlAttribute, fullText, [new XmlSyntaxToken(XmlSyntaxKind.IdentifierToken, name), new XmlSyntaxToken(XmlSyntaxKind.AttributeValueToken, value)])
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string Value { get; }

    public XmlAttributeSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, Name, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.Attribute(name, Value);
    }

    public XmlAttributeSyntax WithValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.Equals(value, Value, StringComparison.Ordinal))
            return this;

        var fullText = ToFullString();
        if (TryGetAttributeValueSpan(fullText, out var valueSpan, out var quoteCharacter))
        {
            var escapedValue = EscapeAttributeValue(value, quoteCharacter);
            var builder = new StringBuilder(fullText.Length - valueSpan.Length + escapedValue.Length);
            builder.Append(fullText.AsSpan(0, valueSpan.Start));
            builder.Append(escapedValue);
            builder.Append(fullText.AsSpan(valueSpan.End));
            return new XmlAttributeSyntax(Name, value, builder.ToString());
        }

        return SyntaxFactory.Attribute(Name, value);
    }

    public XmlAttributeSyntax WithLeadingTrivia(IEnumerable<XmlSyntaxTrivia>? leadingTrivia)
    {
        var triviaText = ConcatenateTrivia(leadingTrivia);
        var fullText = ToFullString();
        var nameStart = GetAttributeNameStart(fullText);
        if (nameStart < 0)
            return this;

        var updated = triviaText + fullText[nameStart..];
        if (string.Equals(updated, fullText, StringComparison.Ordinal))
            return this;

        return new XmlAttributeSyntax(Name, Value, updated);
    }

    public XmlAttributeSyntax WithTrailingTrivia(IEnumerable<XmlSyntaxTrivia>? trailingTrivia)
    {
        var triviaText = ConcatenateTrivia(trailingTrivia);
        var fullText = ToFullString();
        var nameStart = GetAttributeNameStart(fullText);
        if (nameStart < 0)
            return this;

        var nameEnd = nameStart + Name.Length;
        var separatorStart = nameEnd;
        while (separatorStart < fullText.Length && char.IsWhiteSpace(fullText[separatorStart]))
        {
            separatorStart++;
        }

        var equalsIndex = fullText.IndexOf('=', separatorStart, StringComparison.Ordinal);
        if (equalsIndex < 0)
            return this;

        var updated = fullText[..nameEnd] + triviaText + fullText[equalsIndex..];
        if (string.Equals(updated, fullText, StringComparison.Ordinal))
            return this;

        return new XmlAttributeSyntax(Name, Value, updated);
    }

    private static bool TryGetAttributeValueSpan(string attributeText, out TextSpan span, out char quoteCharacter)
    {
        var index = attributeText.IndexOf('=', StringComparison.Ordinal);
        if (index < 0)
        {
            span = default;
            quoteCharacter = '\0';
            return false;
        }

        var current = index + 1;
        while (current < attributeText.Length && char.IsWhiteSpace(attributeText[current]))
        {
            current++;
        }

        if (current >= attributeText.Length)
        {
            span = default;
            quoteCharacter = '\0';
            return false;
        }

        if (attributeText[current] is '"' or '\'')
        {
            var quote = attributeText[current];
            var valueStart = current + 1;
            var valueEnd = attributeText.IndexOf(quote, valueStart, StringComparison.Ordinal);
            if (valueEnd < 0)
            {
                span = default;
                quoteCharacter = '\0';
                return false;
            }

            span = TextSpan.FromBounds(valueStart, valueEnd);
            quoteCharacter = quote;
            return true;
        }

        var unquotedValueStart = current;
        while (current < attributeText.Length && !char.IsWhiteSpace(attributeText[current]) && attributeText[current] != '>')
        {
            current++;
        }

        span = TextSpan.FromBounds(unquotedValueStart, current);
        quoteCharacter = '\0';
        return true;
    }

    private static string EscapeAttributeValue(string value, char quoteCharacter)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"' when quoteCharacter is '"' or '\0':
                    builder.Append("&quot;");
                    break;
                case '\'' when quoteCharacter is '\'' or '\0':
                    builder.Append("&apos;");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    private static int GetAttributeNameStart(string attributeText)
    {
        var current = 0;
        while (current < attributeText.Length && char.IsWhiteSpace(attributeText[current]))
        {
            current++;
        }

        return current < attributeText.Length ? current : -1;
    }

    private static string ConcatenateTrivia(IEnumerable<XmlSyntaxTrivia>? trivia)
    {
        if (trivia is null)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var currentTrivia in trivia)
        {
            builder.Append(currentTrivia.Text);
        }

        return builder.ToString();
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitAttribute(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitAttribute(this);
}
