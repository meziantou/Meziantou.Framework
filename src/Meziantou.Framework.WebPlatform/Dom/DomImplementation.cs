using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-domimplementation</summary>
/// <seealso href="https://dom.spec.whatwg.org/#domimplementation"/>
[IdlInterface("DOMImplementation", "https://dom.spec.whatwg.org/#domimplementation")]
public sealed class DomImplementation
{
    private readonly Document _document;

    internal DomImplementation(Document document)
    {
        _document = document;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-domimplementation-createdocumenttype</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-domimplementation-createdocumenttype"/>
    [IdlMember("createDocumentType", "https://dom.spec.whatwg.org/#dom-domimplementation-createdocumenttype")]
    public DocumentType CreateDocumentType(string name, string publicId, string systemId)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(publicId);
        ArgumentNullException.ThrowIfNull(systemId);
        if (!XmlNames.IsValidDoctypeName(name))
            throw DomException.InvalidCharacter($"'{name}' is not a valid doctype name");

        return new DocumentType(_document, name, publicId, systemId);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-domimplementation-createdocument</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-domimplementation-createdocument"/>
    [IdlMember("createDocument", "https://dom.spec.whatwg.org/#dom-domimplementation-createdocument")]
    public XmlDocument CreateDocument(string? @namespace, string qualifiedName, DocumentType? doctype = null)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var contentType = @namespace switch
        {
            Namespaces.Html => "application/xhtml+xml",
            Namespaces.Svg => "image/svg+xml",
            _ => "application/xml",
        };

        var document = new XmlDocument(contentType) { OriginDocument = Document.GetOriginDocument(_document), RealmDocument = _document };
        Element? element = null;
        if (qualifiedName.Length > 0)
        {
            element = document.CreateElementNS(@namespace, qualifiedName);
        }

        if (doctype is not null)
        {
            document.AppendChild(doctype);
        }

        if (element is not null)
        {
            document.AppendChild(element);
        }

        return document;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument"/>
    [IdlMember("createHTMLDocument", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument")]
    public Document CreateHtmlDocument(string? title = null)
    {
        var document = Document.CreateHtmlDocument();
        document.OriginDocument = Document.GetOriginDocument(_document);
        document.RealmDocument = _document;
        document.AppendChildCore(new DocumentType(document, "html", "", ""));
        var html = ElementFactory.CreateElement(document, "html", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        document.AppendChildCore(html);
        var head = ElementFactory.CreateElement(document, "head", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        html.AppendChildCore(head);
        if (title is not null)
        {
            var titleElement = ElementFactory.CreateElement(document, "title", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
            head.AppendChildCore(titleElement);
            titleElement.AppendChildCore(new Text(document, title));
        }

        html.AppendChildCore(ElementFactory.CreateElement(document, "body", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false));
        return document;
    }

    /// <summary>Useless; always returns true (https://dom.spec.whatwg.org/#dom-domimplementation-hasfeature).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-domimplementation-hasfeature"/>
    [IdlMember("hasFeature", "https://dom.spec.whatwg.org/#dom-domimplementation-hasfeature")]
    public bool HasFeature() => true;
}
