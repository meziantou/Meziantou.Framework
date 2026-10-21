using System.Xml;
using System.Xml.Linq;

namespace Meziantou.Framework.Language.Xml;

/// <summary>
/// Formats XML syntax trees/documents using <see cref="XmlFormattingOptions"/>.
/// </summary>
/// <remarks>
/// Each element whose content is only elements, comments and processing instructions gets each child on its own line,
/// indented one level deeper. Text is never changed: an element that contains text or CDATA, its descendants, and
/// elements under <c>xml:space="preserve"</c> are written as they are.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs" ref:region="Formatter_Format" ref:dedent="true">
/// var tree = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;item/&gt;&lt;/root&gt;");
/// var formatted = Formatter.Format(tree);
/// Assert.Equal("&lt;root&gt;\n  &lt;item /&gt;\n&lt;/root&gt;", formatted.ToFullString());
/// </code>
/// </example>
public static class Formatter
{
    private static readonly XName XmlSpaceName = XNamespace.Xml + "space";

    /// <summary>Marks the nodes this formatter produced, so a later pass can find them again.</summary>
    public static SyntaxAnnotation Annotation { get; } = new("Formatter");

    public static XmlDocumentSyntax Format(XmlSyntaxTree syntaxTree)
    {
        ArgumentNullException.ThrowIfNull(syntaxTree);
        return Format(syntaxTree.GetRoot(), XmlFormattingOptions.Default);
    }

    public static XmlDocumentSyntax Format(XmlDocumentSyntax document)
    {
        return Format(document, XmlFormattingOptions.Default);
    }

    public static XmlDocumentSyntax Format(XmlDocumentSyntax document, XmlFormattingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= XmlFormattingOptions.Default;
        var text = document.ToFullString();
        if (text.Length == 0)
            return document;

        try
        {
            var loadOptions = options.PreserveSingleLineElements ? LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo : LoadOptions.PreserveWhitespace;
            var source = XDocument.Parse(text, loadOptions);
            var layout = new Layout(options, options.PreserveSingleLineElements ? FindSingleLineElements(document, text) : null);
            layout.LayOutDocument(source);

            // The layout pass already wrote the line breaks and the indentation, so XmlWriter must not add its own.
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = source.Declaration is null,
                Indent = false,
                NewLineHandling = NewLineHandling.None,
                Encoding = Encoding.UTF8,
            };

            using var writer = new Utf8StringWriter();
            using (var xmlWriter = XmlWriter.Create(writer, settings))
            {
                source.Save(xmlWriter);
            }

            var formatted = XmlSyntaxTree.ParseText(writer.ToString()).GetRoot();
            if (options.SpaceAroundEquals || !options.SpaceBeforeSelfClosingSlash || options.AttributeWrapping || HasAttributeValueToEscape(source))
            {
                formatted = FormatTags(formatted, options, layout.GetAttributeIndentations(source));
            }

            return formatted;
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException)
        {
            // XmlWriter refuses some documents XDocument reads, such as an element whose prefix is the reserved
            // "xmlns", and reports them with an ArgumentException.
            return document;
        }
    }

    /// <summary>Finds the elements written on a single line, by the line and column of their name as <see cref="IXmlLineInfo"/> reports them.</summary>
    private static HashSet<(int Line, int Column)> FindSingleLineElements(XmlDocumentSyntax document, string text)
    {
        var result = new HashSet<(int Line, int Column)>();
        List<int>? lineStarts = null;
        var offset = document.FullSpan.Start;
        foreach (var node in document.DescendantNodes())
        {
            if (node is not XmlElementSyntax element)
                continue;

            var span = element.Span;
            if (text.AsSpan(span.Start - offset, span.Length).ContainsAny('\r', '\n'))
                continue;

            lineStarts ??= GetLineStarts(text);
            var position = element.StartTag.NameToken.SpanStart - offset;
            var line = lineStarts.BinarySearch(position);
            if (line < 0)
            {
                line = ~line - 1;
            }

            result.Add((line + 1, position - lineStarts[line] + 1));
        }

        return result;
    }

    // XmlReader counts "\r\n", "\r" and "\n" as one line break each, and columns in UTF-16 code units.
    private static List<int> GetLineStarts(string text)
    {
        var result = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\r' && i + 1 < text.Length && text[i + 1] is '\n')
            {
                i++;
            }

            if (c is '\r' or '\n')
            {
                result.Add(i + 1);
            }
        }

        return result;
    }

    // NewLineHandling.None writes a tab or a line break of an attribute value as it is, and reading it back turns it into
    // a space. The other modes would also change the line breaks of the text content and of the indentation.
    private static bool HasAttributeValueToEscape(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Value.AsSpan().ContainsAny('\t', '\r', '\n'))
                    return true;
            }
        }

        return false;
    }

    // A tab or a line break written as it is in an attribute value can only come from a character reference, as the
    // parser turns the other ones into spaces. Writing the reference again keeps the value.
    private static void AppendAttributeValue(StringBuilder result, string value)
    {
        foreach (var c in value)
        {
            _ = c switch
            {
                '\t' => result.Append("&#x9;"),
                '\n' => result.Append("&#xA;"),
                '\r' => result.Append("&#xD;"),
                _ => result.Append(c),
            };
        }
    }

    /// <summary>Rewrites every tag for the options XmlWriter has no setting for, and writes the tabs and line breaks of attribute values as character references.</summary>
    /// <param name="document">A document XmlWriter wrote, so each tag is written as <c>&lt;name a="v" b="v"&gt;</c> or <c>&lt;name a="v" /&gt;</c>.</param>
    /// <param name="attributeIndentations">For each element in document order, the indentation of its attributes when they go on their own line, or <see langword="null"/>.</param>
    private static XmlDocumentSyntax FormatTags(XmlDocumentSyntax document, XmlFormattingOptions options, List<string?> attributeIndentations)
    {
        var text = document.ToFullString();
        var result = new StringBuilder(text.Length);
        var copied = 0;
        var elementIndex = 0;
        foreach (var node in document.DescendantNodes())
        {
            var (tag, nameToken, attributes, isEmpty) = node switch
            {
                XmlElementStartTagSyntax startTag => ((XmlSyntaxNode)startTag, startTag.NameToken, startTag.Attributes, false),
                XmlEmptyElementSyntax emptyElement => (emptyElement, emptyElement.NameToken, emptyElement.Attributes, true),
                _ => default,
            };

            if (tag is null)
                continue;

            // The output elements match the XDocument elements one to one, as they were written from them.
            var indentation = elementIndex < attributeIndentations.Count && attributes.Count > 1 ? attributeIndentations[elementIndex] : null;
            elementIndex++;

            result.Append(text, copied, tag.SpanStart - copied);
            result.Append('<').Append(nameToken.Text);
            foreach (var attribute in attributes)
            {
                if (indentation is null)
                {
                    result.Append(' ');
                }
                else
                {
                    result.Append(options.NewLine).Append(indentation);
                }

                result.Append(attribute.NameToken.Text)
                    .Append(options.SpaceAroundEquals ? " = " : "=")
                    .Append(attribute.StartQuoteToken.Text);
                AppendAttributeValue(result, attribute.ValueToken.Text);
                result.Append(attribute.EndQuoteToken.Text);
            }

            result.Append(isEmpty ? (options.SpaceBeforeSelfClosingSlash ? " />" : "/>") : ">");
            copied = tag.Span.End;
        }

        result.Append(text, copied, text.Length - copied);
        return XmlSyntaxTree.ParseText(result.ToString()).GetRoot();
    }

    private sealed class Layout(XmlFormattingOptions options, HashSet<(int Line, int Column)>? singleLineElements)
    {
        private readonly List<string> _indentations = [string.Empty];
        private readonly Dictionary<XElement, string> _attributeIndentations = [];

        public void LayOutDocument(XDocument document)
        {
            var endsWithLineBreak = document.LastNode is XText last && last.Value.AsSpan().ContainsAny('\r', '\n');
            RemoveWhitespace(document);

            var isFirst = document.Declaration is null;
            foreach (var node in document.Nodes().ToList())
            {
                if (!isFirst)
                {
                    node.AddBeforeSelf(new XText(options.NewLine));
                }

                isFirst = false;
                if (node is XElement element)
                {
                    LayOutElement(element, depth: 0);
                }
            }

            if (endsWithLineBreak)
            {
                document.Add(new XText(options.NewLine));
            }
        }

        public List<string?> GetAttributeIndentations(XDocument document)
        {
            var result = new List<string?>();
            foreach (var element in document.Descendants())
            {
                result.Add(_attributeIndentations.GetValueOrDefault(element));
            }

            return result;
        }

        /// <summary>Lays out the content of an element that starts on its own line.</summary>
        private void LayOutElement(XElement element, int depth)
        {
            if (singleLineElements is not null && IsSingleLine(element))
                return;

            if (options.AttributeWrapping)
            {
                _attributeIndentations[element] = GetIndentation(depth + 1);
            }

            if (!HasElementOnlyContent(element) || string.Equals((string?)element.Attribute(XmlSpaceName), "preserve", StringComparison.Ordinal))
                return;

            RemoveWhitespace(element);
            var childIndentation = options.NewLine + GetIndentation(depth + 1);
            foreach (var child in element.Nodes().ToList())
            {
                child.AddBeforeSelf(new XText(childIndentation));
                if (child is XElement childElement)
                {
                    LayOutElement(childElement, depth + 1);
                }
            }

            element.Add(new XText(options.NewLine + GetIndentation(depth)));
        }

        private bool IsSingleLine(XElement element)
        {
            var lineInfo = (IXmlLineInfo)element;
            return lineInfo.HasLineInfo() && singleLineElements!.Contains((lineInfo.LineNumber, lineInfo.LinePosition));
        }

        private string GetIndentation(int depth)
        {
            while (_indentations.Count <= depth)
            {
                _indentations.Add(options.UseTabs
                    ? new string('\t', _indentations.Count)
                    : new string(' ', Math.Max(0, options.IndentationSize) * _indentations.Count));
            }

            return _indentations[depth];
        }

        // Whitespace between child elements only lays them out. Text or CDATA makes it part of the content.
        private static bool HasElementOnlyContent(XElement element)
        {
            var hasNonText = false;
            foreach (var node in element.Nodes())
            {
                if (node is XText text)
                {
                    if (node is XCData || !IsWhitespace(text.Value))
                        return false;
                }
                else
                {
                    hasNonText = true;
                }
            }

            return hasNonText;
        }

        private static void RemoveWhitespace(XContainer container)
        {
            foreach (var node in container.Nodes().ToList())
            {
                if (node is XText text and not XCData && IsWhitespace(text.Value))
                {
                    node.Remove();
                }
            }
        }

        private static bool IsWhitespace(string value) => !value.AsSpan().ContainsAnyExcept(" \t\r\n");
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public Utf8StringWriter()
            : base(CultureInfo.InvariantCulture)
        {
        }
    }
}
