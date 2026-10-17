using Meziantou.Framework.WebPlatform.Xml;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#domparsersupportedtype</summary>
[IdlInterface("DOMParserSupportedType", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#domparsersupportedtype")]
public enum DomParserSupportedType
{
    /// <summary>The <c>text/html</c> type, parsed as HTML.</summary>
    [IdlMember("text/html", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparsersupportedtype-texthtml")]
    TextHtml,

    /// <summary>The <c>text/xml</c> type, parsed as XML.</summary>
    [IdlMember("text/xml", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparsersupportedtype-otherwise")]
    TextXml,

    /// <summary>The <c>application/xml</c> type, parsed as XML.</summary>
    [IdlMember("application/xml", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparsersupportedtype-otherwise")]
    ApplicationXml,

    /// <summary>The <c>application/xhtml+xml</c> type, parsed as XML.</summary>
    [IdlMember("application/xhtml+xml", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparsersupportedtype-otherwise")]
    ApplicationXhtmlXml,

    /// <summary>The <c>image/svg+xml</c> type, parsed as XML.</summary>
    [IdlMember("image/svg+xml", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparsersupportedtype-otherwise")]
    ImageSvgXml,
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#domparser</summary>
[IdlInterface("DOMParser", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#domparser")]
public sealed class DomParser
{
    /// <summary>The namespace of the root element of the document returned when the XML is not well-formed.</summary>
    internal const string ParserErrorNamespace = "http://www.mozilla.org/newlayout/xml/parsererror.xml";

    // The associated document of the relevant global object of the parser
    private readonly Document _relevantDocument;

    /// <summary>Creates a parser associated with the current global document.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparser-constructor")]
    public DomParser()
    {
        _relevantDocument = Document.CurrentGlobalDocument;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparser-parsefromstring</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    [IdlMember("parseFromString", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparser-parsefromstring")]
    public Document ParseFromString(string markup, DomParserSupportedType type)
    {
        ArgumentNullException.ThrowIfNull(markup);
        return ParseFromStringCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, _relevantDocument, markup, "DOMParser parseFromString"), type);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparser-parsefromstring</summary>
    [IdlMember("parseFromString", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-domparser-parsefromstring")]
    public Document ParseFromString(TrustedHtml markup, DomParserSupportedType type)
    {
        ArgumentNullException.ThrowIfNull(markup);
        return ParseFromStringCore(TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, _relevantDocument, markup, "DOMParser parseFromString"), type);
    }

    private Document ParseFromStringCore(string markup, DomParserSupportedType type)
    {
        // The URL of the document is the URL of the relevant global object's associated document
        var url = _relevantDocument.Url;
        if (type == DomParserSupportedType.TextHtml)
        {
            // Documents created by DOMParser have no browsing context, so scripting is disabled
            return new HtmlDocumentParser(new HtmlParserOptions { Url = url, ScriptingEnabled = false, AllowDeclarativeShadowRoots = false, OriginDocument = Document.GetOriginDocument(_relevantDocument) }).Parse(markup);
        }

        var contentType = type switch
        {
            DomParserSupportedType.TextXml => "text/xml",
            DomParserSupportedType.ApplicationXhtmlXml => "application/xhtml+xml",
            DomParserSupportedType.ImageSvgXml => "image/svg+xml",
            _ => "application/xml",
        };

        var document = XmlDocumentParser.CreateDocument(new XmlParserOptions { Url = url, ContentType = contentType });
        document.OriginDocument = Document.GetOriginDocument(_relevantDocument);
        var error = XmlTreeBuilder.Parse(document, document, markup);
        if (error is not null)
        {
            while (document.FirstChild is { } child)
            {
                document.RemoveChildCore(child, suppressObservers: false);
            }

            var root = document.CreateElementNS(ParserErrorNamespace, "parsererror");
            root.AppendNode(new Text(document, error.Message));
            document.AppendNode(root);
        }

        return document;
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#xmlserializer</summary>
[IdlInterface("XMLSerializer", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#xmlserializer")]
public sealed class XmlSerializer
{
    /// <summary>Creates an XML serializer.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-xmlserializer-constructor")]
    public XmlSerializer()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-xmlserializer-serializetostring</summary>
    [IdlMember("serializeToString", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-xmlserializer-serializetostring")]
    public string SerializeToString(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return XmlSerialization.Serialize(root, requireWellFormed: false);
    }
}
