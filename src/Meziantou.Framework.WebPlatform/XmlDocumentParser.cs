using System.Xml;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.Xml;

namespace Meziantou.Framework.WebPlatform;

/// <summary>Options of <see cref="XmlDocumentParser"/>.</summary>
public sealed class XmlParserOptions
{
    /// <summary>Gets the URL of the document.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the content type of the document: "application/xml", "text/xml", "application/xhtml+xml" or "image/svg+xml".</summary>
    public string ContentType { get; init; } = "application/xml";

    // The browsing context in which the document is loaded (see HtmlParserOptions.BrowsingContext). The default is null:
    // the document has no browsing context.
    internal BrowsingContext? BrowsingContext { get; init; }

    /// <summary>
    /// Gets or sets the custom element registry of the parsed document (see <see cref="HtmlParserOptions.CustomElementRegistry"/>).
    /// </summary>
    public CustomElementRegistry? CustomElementRegistry { get; init; }
}

/// <summary>
/// Parses XML documents (https://html.spec.whatwg.org/multipage/xhtml.html#parsing-xhtml-documents). External entities
/// are never loaded; the XHTML DOCTYPEs declare the HTML named character references.
/// </summary>
public static class XmlDocumentParser
{
    /// <summary>
    /// Parses an XML document. Throws <see cref="XmlException"/> when the document is not namespace-well-formed. The document
    /// has no browsing context: use <see cref="BrowsingContext.Navigate(string, Func{Url, DocumentResource})"/> to load a
    /// document in a browsing context.
    /// </summary>
    public static Document ParseDocument(string xml, XmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var document = CreateDocument(options);

        // The steps of "the end" run for the documents of a browsing context (https://html.spec.whatwg.org/multipage/parsing.html#the-end)
        var runTheEnd = options?.BrowsingContext is { } browsingContext && ReferenceEquals(document.BrowsingContext, browsingContext);
        new Html.Parsing.XmlScriptingParser(document, xml, runTheEnd) { ThrowOnError = true }.Start();
        return document;
    }

    /// <summary>
    /// Parses XML bytes, determining the encoding from the byte order mark, byte signature and XML declaration, or UTF-8
    /// by default. Throws <see cref="XmlException"/> for malformed bytes, conflicting encodings or invalid XML.
    /// </summary>
    public static Document ParseDocument(ReadOnlySpan<byte> xml, XmlParserOptions? options = null) => ParseDocument(XmlByteDecoder.Decode(xml), options);

    /// <summary>
    /// Parses XML from the current position of a stream, determining its encoding as for byte inputs.
    /// Buffers the input and leaves the caller-owned stream open.
    /// </summary>
    public static Document ParseDocument(Stream stream, XmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ParseDocument(memory.GetBuffer().AsSpan(0, (int)memory.Length), options);
    }

    /// <summary>
    /// Parses an XML document, and waits until the parser has finished: while the parser waits for a script, the tasks of
    /// the <see cref="DomEventLoop"/> of the browsing context run. Throws <see cref="XmlException"/> when the document is
    /// not namespace-well-formed (before the parser waits for a script; afterwards, the document shows the error).
    /// </summary>
    public static async Task<Document> ParseDocumentAsync(string xml, XmlParserOptions? options = null, CancellationToken cancellationToken = default)
    {
        var document = ParseDocument(xml, options);
        await HtmlDocumentParser.WaitForParserAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>
    /// Asynchronously reads XML from the current position of a stream and waits until the parser has finished.
    /// Buffers the input and leaves the caller-owned stream open. Cancellation interrupts reading and waiting for the parser.
    /// </summary>
    public static async Task<Document> ParseDocumentAsync(Stream stream, XmlParserOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var document = ParseDocument(memory.GetBuffer().AsSpan(0, (int)memory.Length), options);
        await HtmlDocumentParser.WaitForParserAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>Creates a Document whose type is "xml" (DOMParser and navigation create a Document, not an XMLDocument).</summary>
    internal static Document CreateDocument(XmlParserOptions? options)
    {
        var document = new Document(isHtmlDocument: false, options?.ContentType ?? "application/xml");
        if (options?.Url is { } url)
        {
            document.UrlRecord = UrlParser.Parse(url, baseUrl: null) ?? throw new ArgumentException($"'{url}' is not a valid URL", nameof(options));
        }

        if (options?.BrowsingContext is { } browsingContext)
        {
            document.ScriptingEnabled = browsingContext.Options.ScriptingEnabled;
            browsingContext.InitializeDocument(document);
        }
        else
        {
            document.CustomElementRegistryStorage = options?.CustomElementRegistry;
        }

        return document;
    }

    /// <summary>The XML fragment parsing algorithm (https://html.spec.whatwg.org/multipage/xhtml.html#xml-fragment-parsing-algorithm).</summary>
    internal static DocumentFragment ParseFragment(Node target, string input)
    {
        var context = target as Element ?? ((DocumentFragment)target).Host ?? throw new InvalidOperationException("The target has no context element");

        // The start tag of the context declares the namespace prefixes in scope and the default namespace
        var qualifiedName = context.Prefix is null ? context.LocalName : context.Prefix + ":" + context.LocalName;
        var startTag = new System.Text.StringBuilder();
        startTag.Append('<').Append(qualifiedName);
        foreach (var (prefix, ns) in GetNamespacesInScope(context))
        {
            startTag.Append(prefix is null ? " xmlns" : " xmlns:" + prefix).Append("=\"");
            XmlSerialization.AppendEscapedAttributeValue(startTag, ns);
            startTag.Append('"');
        }

        startTag.Append('>');
        var markup = startTag + input + "</" + qualifiedName + ">";

        // The document has no custom element registry: the elements (including the copy of the context element) are
        // not custom elements, and they are adopted by the document of the target
        var document = new Dom.XmlDocument("application/xml");
        var error = XmlTreeBuilder.Parse(document, document, markup, fragment: true);
        if (error is not null)
            throw DomException.Syntax(error.Message);

        var root = document.DocumentElement!;
        if (root.PreviousSibling is not null || root.NextSibling is not null)
            throw DomException.Syntax("The markup contains nodes outside of the context element");

        // The copy of an XHTML template context element holds the parsed children in its template contents
        Node parent = root is HtmlTemplateElement template ? template.Content : root;
        var fragment = new DocumentFragment(context.NodeDocument);
        while (parent.FirstChild is { } child)
        {
            fragment.AppendNode(child);
        }

        return fragment;
    }

    /// <summary>
    /// Returns the namespace prefixes in scope on an element (the prefixes for which lookupNamespaceURI() returns a
    /// non-null value), and the default namespace (null prefix) if any.
    /// </summary>
    private static List<(string? Prefix, string Namespace)> GetNamespacesInScope(Element context)
    {
        var result = new List<(string? Prefix, string Namespace)>();
        if (context.LookupNamespaceUri(null) is { Length: > 0 } defaultNamespace)
        {
            result.Add((null, defaultNamespace));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var element = context; element is not null; element = element.ParentElement)
        {
            if (element.Prefix is { } elementPrefix)
            {
                TryAdd(elementPrefix);
            }

            for (var i = 0; i < element.AttributeCount; i++)
            {
                if (ReferenceEquals(element.GetAttributeNamespace(i), Namespaces.Xmlns) && element.GetAttributePrefix(i) == "xmlns")
                {
                    TryAdd(element.GetAttributeLocalName(i));
                }
            }
        }

        return result;

        void TryAdd(string prefix)
        {
            if (seen.Add(prefix) && prefix != "xml" && context.LookupNamespaceUri(prefix) is { } ns)
            {
                result.Add((prefix, ns));
            }
        }
    }
}
