using System.Buffers;
using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Converts a node to HTML markup. <see cref="Default"/> uses the HTML fragment serialization algorithm
/// (https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments); <see cref="HtmlMinifyFormatter"/>
/// and <see cref="HtmlPrettyFormatter"/> produce a minified or an indented output.
/// </summary>
/// <remarks>
/// <para>
/// A formatter is immutable and can be used concurrently: each call creates a <see cref="HtmlMarkupWriter"/> that holds
/// the state of the conversion. To customize the output, derive from <see cref="HtmlMarkupWriter"/> and return it from
/// <see cref="CreateWriter"/>.
/// </para>
/// <para>
/// The content of the <c>style</c> elements and the value of the <c>style</c> attributes are written as is, unless
/// <see cref="StyleElementFormatter"/> or <see cref="StyleAttributeFormatter"/> is set.
/// </para>
/// </remarks>
public abstract class HtmlFormatter
{
    private static readonly SearchValues<char> TextEscapeChars = SearchValues.Create("&<>\U000000A0");
    private static readonly SearchValues<char> AttributeEscapeChars = SearchValues.Create("&<>\"\U000000A0");

    /// <summary>
    /// The formatter that uses the HTML fragment serialization algorithm: the output is the same as
    /// <see cref="Element.OuterHtml"/>, <see cref="Element.GetHtml"/> and <see cref="Document.GetHtml"/>.
    /// </summary>
    public static HtmlFormatter Default { get; } = new DefaultHtmlFormatter();

    /// <summary>
    /// The formatter of the CSS text of the HTML <c>style</c> elements whose type is CSS (no <c>type</c> attribute, or
    /// <c>text/css</c>), e.g. a <see cref="CssMinifyFormatter"/>; null (the default) to write their text as is.
    /// </summary>
    /// <remarks>
    /// The text is parsed and the resulting style sheet is formatted: as in the browsers, the comments, the invalid rules and
    /// the declarations of unsupported properties are not written (see <see cref="CssFormatter"/>). The text is written as is
    /// when the element has children other than text nodes, or when the formatted text contains <c>&lt;/style</c>, which
    /// would end the element.
    /// </remarks>
    public CssFormatter? StyleElementFormatter { get; init; }

    /// <summary>
    /// The formatter of the declarations of the <c>style</c> attributes, e.g. a <see cref="CssMinifyFormatter"/>; null (the
    /// default) to write their value as is.
    /// </summary>
    /// <remarks>
    /// The value is parsed as a declaration block and the resulting declarations are formatted: as in the browsers, the
    /// invalid declarations and the declarations of unsupported properties are not written (see <see cref="CssFormatter"/>).
    /// The attribute is written even when no declaration is left (<c>style=""</c>), as the selectors and the scripts can
    /// test its presence. The <c>style</c> attributes of the elements of all namespaces are formatted, but not the
    /// namespaced attributes whose local name is <c>style</c>.
    /// </remarks>
    public CssFormatter? StyleAttributeFormatter { get; init; }

    /// <summary>Returns the markup of <paramref name="node"/> (see <see cref="HtmlMarkupWriter.Write"/>).</summary>
    public string Format(Node node)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Format(node, writer);
        return writer.ToString();
    }

    /// <summary>Writes the markup of <paramref name="node"/> to <paramref name="output"/> (see <see cref="HtmlMarkupWriter.Write"/>).</summary>
    public void Format(Node node, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(output);
        var writer = CreateWriter(output);
        writer.StyleElementFormatter ??= StyleElementFormatter;
        writer.StyleAttributeFormatter ??= StyleAttributeFormatter;
        writer.Write(node);
    }

    /// <summary>Creates the writer that converts one node to markup.</summary>
    protected abstract HtmlMarkupWriter CreateWriter(TextWriter output);

    /// <summary>
    /// Returns whether the element is serialized without children and end tag
    /// (https://html.spec.whatwg.org/multipage/parsing.html#serializes-as-void).
    /// </summary>
    public static bool IsVoidElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.IsHtmlNamespace &&
            element.TagId is TagId.Area or TagId.Base or TagId.Br or TagId.Col or TagId.Embed or TagId.Hr or TagId.Img or TagId.Input or TagId.Link or
                TagId.Meta or TagId.Source or TagId.Track or TagId.Wbr or TagId.Basefont or TagId.Bgsound or TagId.Frame or TagId.Keygen or TagId.Param;
    }

    /// <summary>
    /// Returns whether the text children of the element are serialized without escaping: style, script, xmp, iframe,
    /// noembed, noframes, plaintext, and noscript when scripting is enabled
    /// (https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments).
    /// </summary>
    public static bool HasRawTextContent(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.IsHtmlNamespace &&
            (element.TagId is TagId.Style or TagId.Script or TagId.Xmp or TagId.Iframe or TagId.Noembed or TagId.Noframes or TagId.Plaintext ||
             (element.TagId == TagId.Noscript && element.NodeDocument.ScriptingEnabled));
    }

    /// <summary>Returns the tag name written in the start and end tags of the element: its local name in the HTML, MathML and SVG namespaces, its qualified name otherwise.</summary>
    public static string GetSerializedTagName(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.NamespaceUri is Namespaces.Html or Namespaces.MathML or Namespaces.Svg ? element.LocalName : element.QualifiedName;
    }

    /// <summary>Returns the serialized name of the attribute, with the <c>xml:</c>, <c>xmlns:</c> or <c>xlink:</c> prefix of its namespace.</summary>
    public static string GetSerializedAttributeName(Attr attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        return GetSerializedAttributeName(attribute.NamespaceUri, attribute.LocalName, attribute.Name);
    }

    internal static string GetSerializedAttributeName(Element element, int index)
    {
        var @namespace = element.GetAttributeNamespace(index);
        var localName = element.GetAttributeLocalName(index);
        return @namespace is null ? localName : GetSerializedAttributeName(@namespace, localName, element.GetAttributeQualifiedName(index));
    }

    private static string GetSerializedAttributeName(string? @namespace, string localName, string qualifiedName)
    {
        return @namespace switch
        {
            null => localName,
            Namespaces.Xml => "xml:" + localName,
            Namespaces.Xmlns when localName == "xmlns" => "xmlns",
            Namespaces.Xmlns => "xmlns:" + localName,
            Namespaces.XLink => "xlink:" + localName,
            _ => qualifiedName,
        };
    }

    /// <summary>
    /// Writes <paramref name="value"/> escaped as defined by the HTML serialization
    /// (https://html.spec.whatwg.org/multipage/parsing.html#escapingString): <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> and
    /// U+00A0 are replaced by character references, and <c>"</c> too in attribute mode.
    /// </summary>
    public static void WriteEscaped(TextWriter output, ReadOnlySpan<char> value, bool attributeMode)
    {
        ArgumentNullException.ThrowIfNull(output);
        var searchValues = attributeMode ? AttributeEscapeChars : TextEscapeChars;
        while (true)
        {
            var index = value.IndexOfAny(searchValues);
            if (index < 0)
            {
                output.Write(value);
                return;
            }

            output.Write(value[..index]);
            output.Write(value[index] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => "&nbsp;",
            });
            value = value[(index + 1)..];
        }
    }

    private sealed class DefaultHtmlFormatter : HtmlFormatter
    {
        protected override HtmlMarkupWriter CreateWriter(TextWriter output) => new(output);
    }
}
