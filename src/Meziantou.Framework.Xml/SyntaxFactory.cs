using System.Security;

namespace Meziantou.Framework.Xml;

/// <summary>Creates syntax nodes, tokens, and trivia programmatically.</summary>
/// <example>
/// <code>
/// var node = SyntaxFactory.Element("package", [SyntaxFactory.Attribute("version", "1.0.0")], [], isSelfClosing: true);
/// var document = SyntaxFactory.Document(node);
/// </code>
/// </example>
public static class SyntaxFactory
{
    public static XmlSyntaxTree ParseText(string text) => XmlSyntaxTree.ParseText(text);
    public static XmlSyntaxToken Token(XmlSyntaxKind kind, string text, string? valueText = null, bool isMissing = false, IReadOnlyList<XmlSyntaxTrivia>? leadingTrivia = null, IReadOnlyList<XmlSyntaxTrivia>? trailingTrivia = null) => new(kind, text, valueText, isMissing, leadingTrivia, trailingTrivia);
    public static XmlSyntaxTrivia Trivia(XmlSyntaxKind kind, string text) => new(kind, text);
    public static XmlSyntaxToken Identifier(string value) => new(XmlSyntaxKind.IdentifierToken, value, value);

    public static XmlDocumentSyntax Document(params XmlSyntaxNode[] childNodes)
    {
        ArgumentNullException.ThrowIfNull(childNodes);
        return new XmlDocumentSyntax(childNodes, XmlSyntaxNode.BuildFullText(childNodes));
    }

    public static XmlElementSyntax Element(string name, params XmlSyntaxNode[] content)
    {
        ArgumentNullException.ThrowIfNull(name);
        content ??= [];
        return Element(name, attributes: [], content, isSelfClosing: false);
    }

    public static XmlElementSyntax Element(
        string name,
        IEnumerable<XmlAttributeSyntax> attributes,
        IEnumerable<XmlSyntaxNode> content,
        bool isSelfClosing)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(content);

        var attributeList = attributes.ToArray();
        var contentList = content.ToArray();
        var startTag = BuildStartTag(name, attributeList, isSelfClosing);
        XmlEndTagSyntax? endTag = isSelfClosing ? null : EndTag(name);
        var builder = new StringBuilder(startTag);
        foreach (var node in contentList)
        {
            builder.Append(node.ToFullString());
        }

        if (endTag is not null)
        {
            builder.Append(endTag.ToFullString());
        }

        return new XmlElementSyntax(name, attributeList, contentList, endTag, isSelfClosing, builder.ToString(), startTag);
    }

    public static XmlEndTagSyntax EndTag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new XmlEndTagSyntax(name, $"</{name}>");
    }

    public static XmlAttributeSyntax Attribute(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        var escapedValue = SecurityElement.Escape(value) ?? string.Empty;
        return new XmlAttributeSyntax(name, value, $"{name}=\"{escapedValue}\"");
    }

    public static XmlTextSyntax Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new XmlTextSyntax(text);
    }

    public static XmlCommentSyntax Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new XmlCommentSyntax(text, $"<!--{text}-->");
    }

    public static XmlCDataSectionSyntax CDataSection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new XmlCDataSectionSyntax(text, $"<![CDATA[{text}]]>");
    }

    public static XmlDeclarationSyntax Declaration(string version = "1.0", string? encoding = null, string? standalone = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        var builder = new StringBuilder("<?xml version=\"");
        builder.Append(version);
        builder.Append('"');
        if (!string.IsNullOrEmpty(encoding))
        {
            builder.Append(" encoding=\"");
            builder.Append(encoding);
            builder.Append('"');
        }

        if (!string.IsNullOrEmpty(standalone))
        {
            builder.Append(" standalone=\"");
            builder.Append(standalone);
            builder.Append('"');
        }

        builder.Append("?>");
        return new XmlDeclarationSyntax(version, encoding, standalone, builder.ToString());
    }

    public static XmlDeclarationSyntax XmlDeclaration(string version = "1.0", string? encoding = null, string? standalone = null)
    {
        return Declaration(version, encoding, standalone);
    }

    public static XmlProcessingInstructionSyntax ProcessingInstruction(string target, string? data)
    {
        ArgumentNullException.ThrowIfNull(target);
        var fullText = data is null ? $"<?{target}?>" : $"<?{target} {data}?>";
        return new XmlProcessingInstructionSyntax(target, data, fullText);
    }

    public static XmlDocumentTypeSyntax DocumentType(string name, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = string.IsNullOrEmpty(value) ? $"<!DOCTYPE {name}>" : $"<!DOCTYPE {value}>";
        return new XmlDocumentTypeSyntax(name, value, text);
    }

    public static XmlSkippedTextSyntax SkippedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new XmlSkippedTextSyntax(text);
    }

    private static string BuildStartTag(string name, IReadOnlyList<XmlAttributeSyntax> attributes, bool isSelfClosing)
    {
        var builder = new StringBuilder();
        builder.Append('<');
        builder.Append(name);
        foreach (var attribute in attributes)
        {
            builder.Append(' ');
            builder.Append(attribute.ToFullString());
        }

        builder.Append(isSelfClosing ? "/>" : ">");
        return builder.ToString();
    }
}
