using System.Net;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.Xslt;

namespace Meziantou.Framework.WebPlatform.Dom;

// The XSLT stylesheet of the xml-stylesheet processing instruction of an XML document loaded by a navigation, as in the
// browsers (https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-xml, https://www.w3.org/TR/xml-stylesheet/):
// the document is transformed, and the result of the transformation replaces the document, in the same window.
public sealed partial class BrowsingContext
{
    /// <summary>
    /// Starts the transformation of an XML document whose prolog has an xml-stylesheet processing instruction of an XSLT
    /// stylesheet. Returns false when the document has none (or is not well-formed): it is loaded as an XML document.
    /// </summary>
    /// <remarks>
    /// The document is created (its window, its content security policies), but not parsed: its scripts do not run, as in the
    /// browsers. The stylesheet is fetched by the document (a same-origin request of destination <c>xslt</c>); when it is
    /// loaded, the document is transformed, and the serialization of the result tree is loaded as a document of the type of
    /// its output method (HTML, XML or text) that replaces the document in the same window. When the stylesheet cannot be
    /// loaded or the transformation fails, the document is parsed as an XML document.
    /// </remarks>
    private bool TryLoadXsltDocument(UrlRecord url, string content, string contentType, WebEncodingId encoding)
    {
        if (!content.Contains("xml-stylesheet", StringComparison.Ordinal))
            return false;

        var serializedUrl = url.Serialize();
        if (XsltDocumentLoader.Parse(content, serializedUrl) is not { } source || FindXsltStylesheetHref(source) is not { } href)
            return false;

        var parameters = _pendingDocumentParameters;
        var document = XmlDocumentParser.CreateDocument(new XmlParserOptions { Url = serializedUrl, ContentType = contentType, BrowsingContext = this });
        document.Encoding = encoding;
        if (parameters is null || !ReferenceEquals(document.BrowsingContext, this))
        {
            ParseXmlDocument(document, content);
            return true;
        }

        // The load event of the document is delayed until the result replaces it
        document.StyleSheetImportsDelayingTheLoadEvent++;
        void Complete(XsltStylesheetSource? stylesheet)
        {
            document.StyleSheetImportsDelayingTheLoadEvent--;
            if (!ReferenceEquals(Document, document))
                return;

            if (stylesheet is null || !TryTransformXmlDocument(document, source, stylesheet, parameters, url, encoding))
            {
                ParseXmlDocument(document, content);
            }
        }

        // A stylesheet embedded in the document is referenced by the id of its element (https://www.w3.org/TR/xml-stylesheet/#the-xml-stylesheet-processing-instruction)
        if (href[0] == '#')
        {
            var id = href[1..];
            Complete(FindElementById(source, id) is { } element ? new XsltStylesheetSource(element) : null);
            return true;
        }

        if (UrlParser.Parse(href, document.DocumentBaseUrl) is not { } stylesheetUrl)
        {
            Complete(stylesheet: null);
            return true;
        }

        var request = SubresourceFetching.CreateRequest(document, stylesheetUrl, RequestDestination.Xslt, cors: false, referrerPolicy: "", initiator: null);
        request.Mode = RequestMode.SameOrigin;
        request.CredentialsMode = FetchCredentialsMode.SameOrigin;
        request.ParserInserted = true;
        var started = SubresourceFetching.Fetch(document, request, resource =>
        {
            if (resource is not { IsOkStatus: true })
            {
                Complete(stylesheet: null);
                return;
            }

            string text;
            try
            {
                text = resource.Content ?? Xml.XmlByteDecoder.Decode(resource.ContentBytes!.Value.Span);
            }
            catch (System.Xml.XmlException)
            {
                Complete(stylesheet: null);
                return;
            }

            Complete(XsltDocumentLoader.Parse(text, resource.Url ?? stylesheetUrl.Serialize()) is { } stylesheetDocument ? new XsltStylesheetSource(stylesheetDocument) : null);
        }, initiatorType: "other");

        if (!started)
        {
            Complete(stylesheet: null);
        }

        return true;
    }

    private sealed record XsltStylesheetSource(Node Node);

    /// <summary>The href of the first xml-stylesheet processing instruction of the prolog whose type is an XSLT type, as in the browsers.</summary>
    private static string? FindXsltStylesheetHref(Document document)
    {
        for (var child = document.FirstChild; child is not null and not Element; child = child.NextSibling)
        {
            if (child is ProcessingInstruction { Target: "xml-stylesheet" } instruction
                && instruction.GetAttribute("type") is { } type
                && MimeType.Parse(type)?.Essence is "text/xsl" or "application/xslt+xml" or "text/xml" or "application/xml"
                && instruction.GetAttribute("alternate") != "yes"
                && instruction.GetAttribute("href") is { Length: > 0 } href)
            {
                return href;
            }
        }

        return null;
    }

    private static Element? FindElementById(Document document, string id)
    {
        for (var node = document.FirstChild; node is not null; node = node.GetNextInTreeOrder(document))
        {
            if (node is Element element && (element.GetAttributeValue(Namespaces.Xml, "id") == id || element.GetAttributeValue("id") == id))
                return element;
        }

        return null;
    }

    private static void ParseXmlDocument(Document document, string content) => new Html.Parsing.XmlScriptingParser(document, content, runTheEnd: true).Start();

    /// <summary>
    /// Transforms the source document, and loads the result as the document of the navigation: the result of the html output
    /// method is parsed by the HTML parser (in quirks mode without doctype, as in the browsers), the result of the xml output
    /// method by the XML parser, and the result of the text output method is a text document.
    /// </summary>
    private bool TryTransformXmlDocument(Document document, Document source, XsltStylesheetSource stylesheetSource, DocumentParameters parameters, UrlRecord url, WebEncodingId encoding)
    {
        string markup;
        XsltOutputMethod method;
        XsltOutputSettings settings;
        try
        {
            var loader = new XsltDocumentLoader(document, parserInserted: true);
            var stylesheet = XsltCompiler.Compile(stylesheetSource.Node, loader);
            var result = XsltExecution.Transform(stylesheet, source, new Dictionary<XsltQName, object>(), loader, documentFunctionEnabled: true);
            settings = stylesheet.Output;
            method = XsltOutput.GetMethod(settings, result);
            markup = XsltOutput.Serialize(settings, result, method);
        }
        catch (XsltException ex)
        {
            document.RelevantWindow?.ReportConsoleMessage(new ConsoleMessage("error", ConsoleMessageLevel.Error, "XSLT transformation failed: " + ex.Message));
            return false;
        }

        // The result replaces the document in its window (the session history entry is replaced), from a new task: the
        // violations of the content security policies reported while the document was transformed are reported first
        EventLoop.QueueTask(() => ReplaceWithXsltResult(document, parameters, url, encoding, settings, method, markup));
        return true;
    }

    private void ReplaceWithXsltResult(Document document, DocumentParameters parameters, UrlRecord url, WebEncodingId encoding, XsltOutputSettings settings, XsltOutputMethod method, string markup)
    {
        if (!ReferenceEquals(Document, document))
            return;

        parameters.XsltSourceDocument = document;
        _pendingDocumentParameters = parameters;
        try
        {
            switch (method)
            {
                case XsltOutputMethod.Html:
                    parameters.ContentType = "text/html";
                    _ = new HtmlDocumentParser(new HtmlParserOptions
                    {
                        BrowsingContext = this,
                        Url = url.Serialize(),
                        ScriptingEnabled = Options.ScriptingEnabled,
                        EncodingDetector = Options.EncodingDetector,
                    }).Parse(markup);
                    break;

                case XsltOutputMethod.Text:
                    parameters.ContentType = "text/plain";
                    parameters.IsTextDocument = true;
                    var textDocument = new HtmlDocumentParser(new HtmlParserOptions
                    {
                        BrowsingContext = this,
                        Url = url.Serialize(),
                        ScriptingEnabled = Options.ScriptingEnabled,
                        EncodingDetector = Options.EncodingDetector,
                    }).Parse("<pre style=\"word-wrap: break-word; white-space: pre-wrap;\">\n" + WebUtility.HtmlEncode(markup));
                    textDocument.Encoding = encoding;
                    break;

                default:
                    var resultDocument = XmlDocumentParser.CreateDocument(new XmlParserOptions { Url = url.Serialize(), ContentType = settings.MediaType is { } mediaType && MimeType.Parse(mediaType) is { IsXml: true } xmlType ? xmlType.Essence : "application/xml", BrowsingContext = this });
                    resultDocument.Encoding = encoding;
                    ParseXmlDocument(resultDocument, markup);
                    break;
            }
        }
        finally
        {
            _pendingDocumentParameters = null;
        }
    }
}
