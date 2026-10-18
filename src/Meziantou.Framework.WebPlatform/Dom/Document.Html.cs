using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The readiness of a document (https://html.spec.whatwg.org/multipage/dom.html#current-document-readiness).</summary>
[IdlInterface("DocumentReadyState", "https://html.spec.whatwg.org/multipage/dom.html#documentreadystate")]
public enum DocumentReadyState
{
    /// <summary>The document is still loading.</summary>
    [IdlMember("loading", "https://html.spec.whatwg.org/multipage/dom.html#dom-documentreadystate-loading")]
    Loading,

    /// <summary>The document has finished parsing but sub-resources are still loading.</summary>
    [IdlMember("interactive", "https://html.spec.whatwg.org/multipage/dom.html#dom-documentreadystate-interactive")]
    Interactive,

    /// <summary>The document and all its sub-resources have finished loading.</summary>
    [IdlMember("complete", "https://html.spec.whatwg.org/multipage/dom.html#dom-documentreadystate-complete")]
    Complete,
}

// Members of the Document interface defined by the HTML specification (https://html.spec.whatwg.org/multipage/dom.html#the-document-object)
public partial class Document
{
    /// <summary>Documents parsed by this library are fully loaded, so the readiness is "complete" unless the host changes it.</summary>
    [IdlMember("readyState", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-readystate")]
    public DocumentReadyState ReadyState { get; internal set; } = DocumentReadyState.Complete;

    /// <summary>Always null, as scripts are not executed, unless the host executes scripts.</summary>
    [IdlMember("currentScript", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-currentscript")]
    public Element? CurrentScript { get; internal set; }

    /// <summary>
    /// The window of the browsing context (https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-document-defaultview).
    /// It is null unless the document was created in a <see cref="Dom.BrowsingContext"/>.
    /// </summary>
    [IdlMember("defaultView", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-document-defaultview")]
    public Window? DefaultView => BrowsingContext?.Window;

    /// <summary>The location of the window, when the document is fully active (https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-document-location).</summary>
    [IdlMember("location", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-document-location")]
    public Location? Location => IsFullyActive ? RelevantWindow!.Location : null;

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#the-html-element-2</summary>
    private Element? HtmlElement => DocumentElement is { } element && element.IsHtml(TagId.Html) ? element : null;

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-document-head</summary>
    [IdlMember("head", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-head")]
    public HtmlElement? Head
    {
        get
        {
            for (var child = HtmlElement?.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is HtmlElement element && element.IsHtml(TagId.Head))
                    return element;
            }

            return null;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-document-body</summary>
    [IdlMember("body", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-body")]
    public HtmlElement? Body
    {
        get
        {
            for (var child = HtmlElement?.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is HtmlElement element && (element.IsHtml(TagId.Body) || element.IsHtml(TagId.Frameset)))
                    return element;
            }

            return null;
        }
        set
        {
            if (value is null || !(value.IsHtml(TagId.Body) || value.IsHtml(TagId.Frameset)))
                throw DomException.HierarchyRequest("The new body must be a body or a frameset element");

            var body = Body;
            if (ReferenceEquals(value, body))
                return;

            if (body is not null)
            {
                body.ParentNode!.Replace(body, value);
                return;
            }

            var documentElement = DocumentElement ?? throw DomException.HierarchyRequest("The document has no document element");
            documentElement.AppendNode(value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#the-title-element-2</summary>
    private Element? TitleElement
    {
        get
        {
            for (var node = FirstChild; node is not null; node = node.GetNextInTreeOrder(this))
            {
                if (node is Element element && element.IsHtml(TagId.Title))
                    return element;
            }

            return null;
        }
    }

    private static Element? SvgTitleChild(Element documentElement)
    {
        for (var child = documentElement.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element element && element.LocalName == "title" && ReferenceEquals(element.NamespaceUri, Namespaces.Svg))
                return element;
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#document.title</summary>
    [IdlMember("title", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-title")]
    public string Title
    {
        get
        {
            string value;
            if (DocumentElement is { LocalName: "svg" } svg && ReferenceEquals(svg.NamespaceUri, Namespaces.Svg))
            {
                value = SvgTitleChild(svg)?.GetChildTextContent() ?? "";
            }
            else
            {
                value = TitleElement?.GetChildTextContent() ?? "";
            }

            return InfraString.StripAndCollapseAsciiWhitespace(value);
        }
        set
        {
            value ??= "";
            var documentElement = DocumentElement;
            Element element;
            if (documentElement is { LocalName: "svg" } && ReferenceEquals(documentElement.NamespaceUri, Namespaces.Svg))
            {
                var title = SvgTitleChild(documentElement);
                if (title is null)
                {
                    title = ElementFactory.CreateElement(this, "title", Namespaces.Svg, prefix: null, isValue: null, synchronousCustomElements: false);
                    documentElement.Insert(title, documentElement.FirstChild);
                }

                element = title;
            }
            else if (documentElement is not null && documentElement.IsHtmlNamespace)
            {
                var title = TitleElement;
                var head = Head;
                if (title is null && head is null)
                    return;

                if (title is null)
                {
                    title = ElementFactory.CreateElement(this, "title", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
                    head!.AppendNode(title);
                }

                element = title;
            }
            else
            {
                return;
            }

            element.StringReplaceAll(value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-document-getelementsbyname</summary>
    [IdlMember("getElementsByName", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-getelementsbyname")]
    public NodeList GetElementsByName(string elementName)
    {
        ArgumentNullException.ThrowIfNull(elementName);
        return new FilteredNodeList(this, node => node is Element element && element.IsHtmlNamespace && element.GetAttributeValue("name") == elementName);
    }
}
