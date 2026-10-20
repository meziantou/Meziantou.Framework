using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The navigable containers (https://html.spec.whatwg.org/multipage/document-sequences.html#navigable-container):
/// <c>iframe</c> and <c>frame</c> elements have a content navigable when their document has a browsing context, and the
/// <c>embed</c> and <c>object</c> elements when they display a document (see <see cref="EmbeddedResources"/>).
/// </summary>
internal static class NavigableContainers
{
    public static BrowsingContext? GetContentNavigable(Element element) => element switch
    {
        HtmlIFrameElement iframe => iframe.ContentNavigable,
        HtmlFrameElement frame => frame.ContentNavigable,
        HtmlEmbedElement embed => embed.ContentNavigable,
        HtmlObjectElement obj => obj.ContentNavigable,
        _ => null,
    };

    public static void SetContentNavigable(Element element, BrowsingContext? navigable)
    {
        switch (element)
        {
            case HtmlIFrameElement iframe:
                iframe.ContentNavigable = navigable;
                break;
            case HtmlFrameElement frame:
                frame.ContentNavigable = navigable;
                break;
            case HtmlEmbedElement embed:
                embed.ContentNavigable = navigable;
                break;
            case HtmlObjectElement obj:
                obj.ContentNavigable = navigable;
                break;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#concept-bcc-content-document: the document, when it is same origin-domain with the container document.</summary>
    public static Document? GetContentDocument(Element container)
    {
        if (GetContentNavigable(container) is not { } navigable)
            return null;

        var document = navigable.Document;
        return Document.IsSameOriginDomain(document, container.NodeDocument) ? document : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-getsvgdocument</summary>
    public static Document? GetSvgDocument(Element container)
    {
        var document = GetContentDocument(container);
        return document is { ContentType: "image/svg+xml", IsHtmlDocument: false } ? document : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#shared-attribute-processing-steps-for-iframe-and-frame-elements</summary>
    public static UrlRecord? SharedAttributeProcessingSteps(Element element, bool initialInsertion)
    {
        var document = element.NodeDocument;
        var url = UrlParser.Parse("about:blank", baseUrl: null)!;
        if (element.GetAttributeValue("src") is { Length: > 0 } src && UrlParser.Parse(src, document.DocumentBaseUrl, document.Encoding) is { } maybeUrl)
        {
            url = maybeUrl;
        }

        // Prevent recursive nesting. The specification returns null as soon as an inclusive ancestor navigable displays the
        // URL, but the browsers allow one level of self-reference (e.g. A -> B -> B, which location-ancestor-origins.sub.html
        // relies on) and only block the next one
        if (BrowsingContext.IsUrlRepeatedInInclusiveAncestors(document.BrowsingContext, url))
            return null;

        if (BrowsingContext.MatchesAboutBlank(url) && initialInsertion && GetContentNavigable(element) is { } contentNavigable && !BrowsingContext.UrlEquals(url, contentNavigable.Document.UrlRecord, excludeFragments: false))
        {
            // e.g. about:blank?foo
            contentNavigable.UpdateUrlAndHistory(url, serializedData: null, hasSerializedData: false, NavigationHistoryBehavior.Replace);
        }

        return url;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#navigate-an-iframe-or-frame</summary>
    public static void NavigateIframeOrFrame(Element element, UrlRecord url, bool initialInsertion, string? srcdoc = null, string? referrerPolicy = null)
    {
        if (GetContentNavigable(element) is not { } navigable)
            return;

        var historyHandling = navigable.Document.CompletelyLoaded ? NavigationHistoryBehavior.Auto : NavigationHistoryBehavior.Replace;
        referrerPolicy ??= (element as HtmlIFrameElement)?.ReferrerPolicy ?? "";

        // The pending resource-timing start time of the element: the timing of the navigation is added to the performance
        // timeline of the document of the element (https://w3c.github.io/resource-timing/#resources-included-in-the-performanceresourcetiming-interface)
        navigable.StartContainerResourceTiming(element, srcdoc is null ? url : null);
        navigable.Navigate(url, element.NodeDocument, historyHandling, srcdoc, initialInsertion, referrerPolicy);
        navigable.DiscardUnstartedContainerResourceTiming();
    }
}

public partial class HtmlIFrameElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#content-navigable</summary>
    internal BrowsingContext? ContentNavigable { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-contentdocument</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-contentdocument"/>
    [IdlMember("contentDocument", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-contentdocument")]
    public Document? ContentDocument => NavigableContainers.GetContentDocument(this);

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-contentwindow</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-contentwindow"/>
    [IdlMember("contentWindow", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-contentwindow")]
    public Window? ContentWindow => ContentNavigable?.Window;

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-getsvgdocument</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-getsvgdocument"/>
    [IdlMember("getSVGDocument", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-getsvgdocument")]
    public Document? GetSvgDocument() => NavigableContainers.GetSvgDocument(this);

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-iframe-element:html-element-post-connection-steps</summary>
    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        if (NodeDocument.BrowsingContext is null || ContentNavigable is not null)
            return;

        BrowsingContext.CreateChildNavigable(this);
        ProcessIframeAttributes(initialInsertion: true);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-iframe-element:html-element-removing-steps</summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        BrowsingContext.DestroyChildNavigable(this);
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        // The name attribute only names the content navigable when it is created
        // (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#attr-iframe-name)
        if (@namespace is not null || ContentNavigable is null)
            return;

        switch (localName)
        {
            case "srcdoc":
            case "src" when !HasAttributeWithLocalName("srcdoc"):
                ProcessIframeAttributes(initialInsertion: false);
                break;
            case "loading":
                // https://html.spec.whatwg.org/multipage/iframe-embed-object.html#attr-iframe-loading
                LazyLoading.OnLoadingAttributeChanged(this, oldValue, value);
                break;
        }
    }

    /// <summary>
    /// The current navigation was lazy loaded boolean (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#current-navigation-was-lazy-loaded):
    /// the iframe does not delay the load event of its document.
    /// </summary>
    internal bool CurrentNavigationWasLazyLoaded { get; private set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/iframe-embed-object.html#process-the-iframe-attributes: a lazy iframe navigates
    /// when it is about to intersect the viewport (its lazy load resumption steps).
    /// </summary>
    private void ProcessIframeAttributes(bool initialInsertion)
    {
        LazyLoading.ClearResumptionSteps(this);
        if (GetAttributeValue("srcdoc") is { } srcdoc)
        {
            CurrentNavigationWasLazyLoaded = false;
            if (LazyLoading.WillLazyLoad(this))
            {
                CurrentNavigationWasLazyLoaded = true;
                LazyLoading.Defer(this, () => NavigableContainers.NavigateIframeOrFrame(this, UrlParser.Parse("about:srcdoc", baseUrl: null)!, initialInsertion, srcdoc));
                return;
            }

            NavigableContainers.NavigateIframeOrFrame(this, UrlParser.Parse("about:srcdoc", baseUrl: null)!, initialInsertion, srcdoc);
            return;
        }

        var url = NavigableContainers.SharedAttributeProcessingSteps(this, initialInsertion);
        if (url is null)
            return;

        if (BrowsingContext.MatchesAboutBlank(url) && initialInsertion)
        {
            RunIframeLoadEventSteps();
            return;
        }

        var referrerPolicy = ReferrerPolicy;
        CurrentNavigationWasLazyLoaded = false;
        if (LazyLoading.WillLazyLoad(this))
        {
            CurrentNavigationWasLazyLoaded = true;
            LazyLoading.Defer(this, () => NavigableContainers.NavigateIframeOrFrame(this, url, initialInsertion, referrerPolicy: referrerPolicy));
            return;
        }

        NavigableContainers.NavigateIframeOrFrame(this, url, initialInsertion, referrerPolicy: referrerPolicy);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#iframe-load-event-steps</summary>
    internal void RunIframeLoadEventSteps()
    {
        if (ContentNavigable?.Document is not { } childDocument || childDocument.MuteIframeLoad)
            return;

        ContentNavigable.ReportContainerResourceTimingFallback();
        childDocument.IframeLoadInProgress = true;
        FireEvent(new Event("load"));
        childDocument.IframeLoadInProgress = false;

        // https://w3c.github.io/webappsec-permissions-policy/#changes-to-html: the features that the allow attribute delegates
        // although the policy of the document disables them are reported
        PermissionsPolicyAlgorithms.CheckPotentialViolationInContainer(this);
    }
}

public partial class HtmlFrameElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#content-navigable</summary>
    internal BrowsingContext? ContentNavigable { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-frame-contentdocument</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-frame-contentdocument"/>
    [IdlMember("contentDocument", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-frame-contentdocument")]
    public Document? ContentDocument => NavigableContainers.GetContentDocument(this);

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-frame-contentwindow</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-frame-contentwindow"/>
    [IdlMember("contentWindow", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-frame-contentwindow")]
    public Window? ContentWindow => ContentNavigable?.Window;

    /// <summary>
    /// The frame HTML element post-connection steps (https://html.spec.whatwg.org/multipage/obsolete.html#frames): the
    /// element is connected (in a document tree or in a shadow tree) to a document that has a browsing context.
    /// </summary>
    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        if (NodeDocument.BrowsingContext is null || ContentNavigable is not null)
            return;

        BrowsingContext.CreateChildNavigable(this);
        ProcessFrameAttributes(initialInsertion: true);
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        BrowsingContext.DestroyChildNavigable(this);
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null || ContentNavigable is null)
            return;

        // The name attribute only names the content navigable when it is created
        if (localName == "src")
        {
            ProcessFrameAttributes(initialInsertion: false);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#process-the-frame-attributes</summary>
    private void ProcessFrameAttributes(bool initialInsertion)
    {
        var url = NavigableContainers.SharedAttributeProcessingSteps(this, initialInsertion);
        if (url is null)
            return;

        if (BrowsingContext.MatchesAboutBlank(url) && initialInsertion)
        {
            FireEvent(new Event("load"));
            return;
        }

        NavigableContainers.NavigateIframeOrFrame(this, url, initialInsertion);
    }
}
