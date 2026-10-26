using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of the external resource of a link element that the library fetches.</summary>
internal enum LinkResourceKind
{
    /// <summary>https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet</summary>
    StyleSheet,

    /// <summary>https://html.spec.whatwg.org/multipage/links.html#link-type-preload</summary>
    Preload,

    /// <summary>https://html.spec.whatwg.org/multipage/links.html#link-type-modulepreload</summary>
    ModulePreload,

    /// <summary>https://html.spec.whatwg.org/multipage/links.html#link-type-prefetch</summary>
    Prefetch,

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#rel-icon, and its <c>apple-touch-icon</c> and
    /// <c>apple-touch-icon-precomposed</c> variants of the browsers: only checked against the content security policies of the
    /// document (the library does not render the icons).
    /// </summary>
    Icon,

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#link-type-preconnect, and the hint of
    /// https://html.spec.whatwg.org/multipage/links.html#link-type-next: nothing is fetched (the loader has no connection pool).
    /// </summary>
    Preconnect,

    /// <summary>https://html.spec.whatwg.org/multipage/links.html#link-type-dns-prefetch: nothing is fetched (the loader has no DNS cache).</summary>
    DnsPrefetch,
}

/// <summary>
/// The external resource of a link element: its URL, the destination and mode of its request, and whether it is an
/// alternative style sheet that is not explicitly enabled (https://html.spec.whatwg.org/multipage/links.html#the-link-is-an-alternative-stylesheet).
/// <paramref name="ModuleType"/> is the type of the module of a modulepreload, or null when its <c>as</c> attribute is not a
/// module preload destination (the element then fires an <c>error</c> event).
/// </summary>
internal readonly record struct LinkResource(LinkResourceKind Kind, UrlRecord Url, RequestDestination Destination, bool Cors, bool Alternate = false, ModuleType? ModuleType = null);

// The external resource links of the link elements (https://html.spec.whatwg.org/multipage/semantics.html#the-link-element):
// the style sheets, the preloads, the module preloads and the prefetches are fetched; the preconnects and the DNS prefetches
// are processed without fetching anything.
public partial class HtmlLinkElement
{
    private string? _resourceKey;
    private int _fetchGeneration;
    private Document? _documentWhoseLoadEventIsDelayed;
    private Document? _documentWhoseScriptsAreBlocked;
    private Document? _documentWhoseRenderingIsBlocked;
    private CssStyleSheet? _styleSheet;

    /// <summary>
    /// Whether the style sheet link is implicitly potentially render-blocking (https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet):
    /// the element was created by the parser of its document (<see cref="CreatedByParser"/> is reset once it is inserted).
    /// </summary>
    private bool _implicitlyRenderBlocking;

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#explicitly-enabled: the <c>disabled</c> attribute was removed.</summary>
    private bool _explicitlyEnabled;

    /// <summary>
    /// Whether the element was created by the parser of its document and is not connected yet: the style sheet fetched when
    /// it is inserted blocks the scripts of the parser (https://html.spec.whatwg.org/multipage/semantics.html#contributes-a-script-blocking-style-sheet).
    /// </summary>
    internal bool CreatedByParser { get; set; }

    /// <summary>https://drafts.csswg.org/cssom/#associated-css-style-sheet</summary>
    internal CssStyleSheet? AssociatedStyleSheet => _styleSheet;

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        UpdateLinkedResource();
        UpdateInternalResourceLink();
        CreatedByParser = false;
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        UpdateLinkedResource();
        UpdateInternalResourceLink();
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is null && localName == "disabled" && value is null && oldValue is not null)
        {
            _explicitlyEnabled = true;
        }

        if (@namespace is null && localName is "rel" or "href" or "crossorigin" or "type" or "disabled" or "as" or "media" or "referrerpolicy" or "title" or "imagesrcset" or "imagesizes")
        {
            UpdateLinkedResource();
        }

        if (@namespace is null && localName == "blocking" && !IsPotentiallyRenderBlocking)
        {
            // https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism: the blocking attribute "is changed so
            // that el is no longer potentially render-blocking"
            StopBlockingRendering();
        }

        // https://html.spec.whatwg.org/multipage/links.html#link-type-expect: the link is processed when its href or media
        // attribute changes; as in the browsers, also when its rel or blocking attribute changes
        if (@namespace is null && localName is "rel" or "href" or "media" or "blocking")
        {
            UpdateInternalResourceLink();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#potentially-render-blocking: the blocking attribute of the
    /// element contains "render", or its style sheet is implicitly potentially render-blocking.
    /// </summary>
    private bool IsPotentiallyRenderBlocking => _implicitlyRenderBlocking || Document.HasRenderBlockingToken(this);

    // ----------------------------------------------------------------------------------------------------------------
    // Link type "expect" (https://html.spec.whatwg.org/multipage/links.html#link-type-expect)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether the processing of the internal resource link blocked the rendering of the document.</summary>
    private bool _expectBlocksRendering;

    /// <summary>The fragment of the URL of the expected element, and its percent-decoded value, while the link blocks the rendering.</summary>
    private string? _expectedFragment;
    private string? _expectedDecodedFragment;

    /// <summary>Whether the rel attribute of the element contains the <c>expect</c> keyword: the element creates an internal resource link.</summary>
    internal bool IsExpectLink
    {
        get
        {
            if (GetAttributeValue("rel") is not { Length: > 0 } rel)
                return false;

            foreach (var token in rel.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (InfraAscii.EqualsIgnoreAsciiCase(token, "expect"))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Whether the element may be the element that the link expects: its id, or its name for an a element, is the fragment of the link.</summary>
    internal bool MayExpect(Element element)
    {
        if (_expectedFragment is not { } fragment)
            return false;

        if (element.IdValue is { } id && (id == fragment || id == _expectedDecodedFragment))
            return true;

        return element.IsHtml(TagId.A) && element.GetAttributeValue("name") is { } name && (name == fragment || name == _expectedDecodedFragment);
    }

    private void UpdateInternalResourceLink()
    {
        if (_expectBlocksRendering || (IsConnected && NodeDocument.ReadyState == DocumentReadyState.Loading && IsExpectLink))
        {
            ProcessInternalResourceLink();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#process-internal-resource-link: the link blocks the rendering of its
    /// document while the document is loading and the element indicated by its URL is not found, or is still on the stack of
    /// open elements of the parser (its content is not completely parsed). As in the browsers, a URL without fragment does not
    /// block (the specification would block until the document is parsed, as its indicated part is not an element).
    /// </summary>
    internal void ProcessInternalResourceLink()
    {
        var document = NodeDocument;
        _expectedFragment = null;
        _expectedDecodedFragment = null;
        var blocks = false;
        if (document.ReadyState == DocumentReadyState.Loading && IsConnected && document.BrowsingContext is not null && IsExpectLink
            && GetAttributeValue("href") is { } href && Reflection.EncodingParseUrl(document, href) is { Fragment: { Length: > 0 } fragment } url
            && BrowsingContext.UrlEquals(url, document.UrlRecord, excludeFragments: true)
            && Document.HasRenderBlockingToken(this)
            && (GetAttributeValue("media") is not { } media || MediaQueryEvaluator.Matches(media, StyleEngine.CreateEnvironment(document))))
        {
            _expectedFragment = fragment;
            _expectedDecodedFragment = System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(fragment));
            blocks = Css.Selectors.HtmlElementStates.GetIndicatedElement(document, fragment) is not { } indicatedElement || document.IsOnStackOfOpenElements(indicatedElement);
        }

        if (blocks)
        {
            _expectBlocksRendering = true;
            document.BlockRendering(this);
        }
        else if (_expectBlocksRendering)
        {
            _expectBlocksRendering = false;
            _expectedFragment = null;
            _expectedDecodedFragment = null;
            document.UnblockRendering(this);
        }
    }

    /// <summary>
    /// Returns the external resource that the element would fetch (whether it is connected or not), or null: a style sheet
    /// (<c>rel</c> contains <c>stylesheet</c>, no <c>disabled</c> attribute, a <c>type</c> that is missing or <c>text/css</c>,
    /// and a <c>title</c> for an alternative style sheet), a preload whose <c>as</c> attribute is a preload destination
    /// (<c>fetch</c>, <c>font</c>, <c>image</c>, <c>script</c>, <c>style</c> or <c>track</c>) that its <c>type</c> attribute
    /// matches, a module preload, a prefetch (or a prerender, which the browsers process as a prefetch), the icon of a document
    /// with content security policies, a preconnect (or a <c>next</c> link), or a DNS prefetch. <paramref name="mediaMatches"/> is whether the <c>media</c> attribute matches the
    /// environment of <paramref name="environmentDocument"/>: only a style sheet is fetched when its media does not match (as in
    /// browsers, it applies when the environment changes), but it does not block the scripts and the speculative parser
    /// does not fetch it.
    /// </summary>
    internal LinkResource? GetLinkResource(Document environmentDocument, out bool mediaMatches)
    {
        mediaMatches = true;
        var href = GetAttributeValue("href") ?? "";
        var isStyleSheet = false;
        var isAlternate = false;
        var isPreload = false;
        var isModulePreload = false;
        var isPrefetch = false;
        var isIcon = false;
        var isPreconnect = false;
        var isDnsPrefetch = false;
        foreach (var token in (GetAttributeValue("rel") ?? "").Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            isStyleSheet |= InfraAscii.EqualsIgnoreAsciiCase(token, "stylesheet");
            isAlternate |= InfraAscii.EqualsIgnoreAsciiCase(token, "alternate");
            isPreload |= InfraAscii.EqualsIgnoreAsciiCase(token, "preload");
            isModulePreload |= InfraAscii.EqualsIgnoreAsciiCase(token, "modulepreload");

            // The browsers process the prerender links (removed from the HTML Standard) as prefetches: Chromium's NoState
            // Prefetch fetches the resource without rendering it
            isPrefetch |= InfraAscii.EqualsIgnoreAsciiCase(token, "prefetch") || InfraAscii.EqualsIgnoreAsciiCase(token, "prerender");
            isIcon |= InfraAscii.EqualsIgnoreAsciiCase(token, "icon") || InfraAscii.EqualsIgnoreAsciiCase(token, "apple-touch-icon") || InfraAscii.EqualsIgnoreAsciiCase(token, "apple-touch-icon-precomposed");

            // https://html.spec.whatwg.org/multipage/links.html#link-type-next: "user agents should process such links as if
            // they were using one of the dns-prefetch, preconnect, or prefetch keywords", which one being implementation-
            // dependent: the library preconnects (it does not fetch the next document of the series)
            isPreconnect |= InfraAscii.EqualsIgnoreAsciiCase(token, "preconnect") || InfraAscii.EqualsIgnoreAsciiCase(token, "next");
            isDnsPrefetch |= InfraAscii.EqualsIgnoreAsciiCase(token, "dns-prefetch");
        }

        LinkResourceKind kind;
        var destination = RequestDestination.Empty;
        var alternate = false;
        ModuleType? moduleType = null;
        if (isStyleSheet)
        {
            // https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet: the linked resource fetch setup steps. An
            // alternative style sheet that is not explicitly enabled is only fetched when it has a title, as in browsers.
            var type = GetAttributeValue("type") is { Length: > 0 } typeValue ? MimeType.Parse(typeValue)?.Essence ?? "" : null;
            if (HasAttribute("disabled") || type is not (null or "text/css"))
                return null;

            alternate = isAlternate && !_explicitlyEnabled;
            if (alternate && string.IsNullOrEmpty(GetAttributeValue("title")))
                return null;

            kind = LinkResourceKind.StyleSheet;
            destination = RequestDestination.Style;
        }
        else if (isPreload)
        {
            // https://html.spec.whatwg.org/multipage/links.html#link-type-preload: the preload destination of the as attribute,
            // which the type attribute must match
            kind = LinkResourceKind.Preload;
            if (LinkPreload.TranslatePreloadDestination(GetAttributeValue("as")) is not { } preloadDestination || !LinkPreload.TypeMatchesDestination(GetAttributeValue("type") ?? "", preloadDestination))
                return null;

            // https://html.spec.whatwg.org/multipage/links.html#preload, step 2: the source selected from the source set of an image
            destination = preloadDestination;
            if (destination == RequestDestination.Image && GetAttributeValue("imagesrcset") is { } imageSrcset && HtmlImageElement.SelectImageSource(href, imageSrcset, GetAttributeValue("imagesizes"), environmentDocument) is { } source)
            {
                href = source;
            }
        }
        else if (isModulePreload)
        {
            // https://html.spec.whatwg.org/multipage/links.html#link-type-modulepreload: the destination is "script" when the as
            // attribute is in no state
            kind = LinkResourceKind.ModulePreload;
            (destination, moduleType) = LinkPreload.TranslateModulePreloadDestination(GetAttributeValue("as"));
        }
        else if (isPrefetch)
        {
            // https://html.spec.whatwg.org/multipage/links.html#link-type-prefetch: the destination is the empty string
            kind = LinkResourceKind.Prefetch;
        }
        else if (isIcon && environmentDocument.ContentSecurityPoliciesOrNull is { Count: > 0 })
        {
            // The icon of a document with content security policies: its fetch is blocked by img-src (nothing is rendered, so
            // the icon is not fetched when it is allowed)
            kind = LinkResourceKind.Icon;
            destination = RequestDestination.Image;
        }
        else if (isPreconnect)
        {
            kind = LinkResourceKind.Preconnect;
        }
        else if (isDnsPrefetch)
        {
            kind = LinkResourceKind.DnsPrefetch;
        }
        else
        {
            return null;
        }

        if (href.Length == 0 || Reflection.EncodingParseUrl(NodeDocument, href) is not { } url)
            return null;

        mediaMatches = GetAttributeValue("media") is not { } media || MediaQueryEvaluator.Matches(media, StyleEngine.CreateEnvironment(environmentDocument));

        // https://html.spec.whatwg.org/multipage/semantics.html#create-a-link-request: a potential-CORS request, whose mode is
        // "cors" when the element has a crossorigin attribute (a font preload without the attribute does not match the
        // request of the font, which is always a CORS request)
        return new LinkResource(kind, url, destination, Cors: HasAttribute("crossorigin"), alternate, moduleType);
    }

    /// <summary>
    /// Fetches and processes the linked resource (https://html.spec.whatwg.org/multipage/semantics.html#fetch-and-process-the-linked-resource)
    /// when the element becomes browsing-context connected or when the attributes that determine the resource change. The
    /// resource is not fetched again when the attributes still designate the same request (e.g. <c>href</c> set to the
    /// same value). The style sheet is removed when the element no longer links to a style sheet.
    /// </summary>
    private void UpdateLinkedResource()
    {
        var document = NodeDocument;
        var mediaMatches = true;
        var resource = IsConnected && document.BrowsingContext is not null ? GetLinkResource(document, out mediaMatches) : null;

        // The attributes whose change fetches the resource again: the CORS state and the referrer policy of a style sheet, the
        // destination of a preload (https://html.spec.whatwg.org/multipage/links.html#link-type-preload), and only the URL of
        // a module preload or of a prefetch
        var key = resource switch
        {
            { Kind: LinkResourceKind.StyleSheet } value => string.Join(' ', value.Kind, value.Destination, value.Cors, value.Alternate, ReferrerPolicy, value.Url.Serialize()),
            { Kind: LinkResourceKind.Preload } value => string.Join(' ', value.Kind, value.Destination, value.Url.Serialize()),
            { } value => string.Join(' ', value.Kind, value.Url.Serialize()),
            _ => null,
        };

        if (key is not null && key == _resourceKey)
        {
            // The media and the title of the style sheet are references to the attributes: the style sheet is not fetched again
            if (_styleSheet is { } sheet)
            {
                if (sheet.Media.MediaText != (GetAttributeValue("media") ?? ""))
                {
                    sheet.Media.MediaText = GetAttributeValue("media") ?? "";
                }

                var title = GetRoot() is Document && GetAttributeValue("title") is { Length: > 0 } titleValue ? titleValue : null;
                if (sheet.Title != title)
                {
                    sheet.Title = title;
                    OnStyleSheetChanged();
                }
            }

            return;
        }

        _fetchGeneration++;
        StopDelayingTheLoadEvent();
        StopBlockingScripts();
        StopBlockingRendering();

        // https://html.spec.whatwg.org/multipage/semantics.html#attr-link-media: only a style sheet whose media does not match is fetched
        if (resource is not { } linkResource || (!mediaMatches && linkResource.Kind != LinkResourceKind.StyleSheet))
        {
            _resourceKey = null;
            RemoveStyleSheet();
            return;
        }

        _resourceKey = key;
        var generation = _fetchGeneration;
        if (linkResource.Kind != LinkResourceKind.StyleSheet)
        {
            // The preloads, the module preloads and the prefetches do not delay the load event of the document
            RemoveStyleSheet();
            FetchHint(document, linkResource, generation);
            return;
        }

        // https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet: "If el's media attribute's value matches the
        // environment and el is potentially render-blocking, then block rendering on el", before the request is created. As in
        // the browsers, an alternative style sheet that is not enabled does not block the rendering (it does not block the
        // scripts either).
        _implicitlyRenderBlocking = CreatedByParser;
        if (mediaMatches && !linkResource.Alternate && IsPotentiallyRenderBlocking)
        {
            _documentWhoseRenderingIsBlocked = document;
            document.BlockRendering(this);
        }

        // The fetch consumes the response of a preload of the same request (https://html.spec.whatwg.org/multipage/links.html#consume-a-preloaded-resource),
        // without fetching it again
        var request = SubresourceFetching.CreateRequest(document, linkResource.Url, linkResource.Destination, linkResource.Cors, ReferrerPolicy, this, useCredentials: SubresourceFetching.UsesCredentials(this), integrity: GetAttributeValue("integrity") ?? "");
        request.RenderBlocking = document.IsRenderBlocking(this);
        var preloadKey = Document.GetPreloadKey(request);
        if (document.TryGetCachedStyleSheet(preloadKey, out var cachedResponse))
        {
            // As in the browsers (memory cache), a style sheet already fetched by the document is not fetched again: it is
            // processed synchronously below, and the events are fired by a task
        }
        else if (!SubresourceFetching.Fetch(document, request, response => OnFetched(linkResource, response)))
        {
            StopBlockingRendering();
            RemoveStyleSheet();
            return;
        }

        _documentWhoseLoadEventIsDelayed = document;
        document.LinksDelayingTheLoadEvent++;

        // https://html.spec.whatwg.org/multipage/semantics.html#contributes-a-script-blocking-style-sheet: an enabled style
        // sheet of an element created by the parser, whose media matches, blocks the scripts until it is processed
        if (CreatedByParser && !linkResource.Alternate && mediaMatches)
        {
            _documentWhoseScriptsAreBlocked = document;
            document.SetStyleSheetBlockingScripts(true);
        }

        if (cachedResponse is not null)
        {
            OnFetched(linkResource, cachedResponse, synchronous: true);
        }

        void OnFetched(LinkResource linkResource, DocumentResource? response, bool synchronous = false)
        {
            if (generation != _fetchGeneration)
                return;

            // The fetch algorithm performs the CORS check and blocks a response due to its MIME type or due to nosniff
            var success = response is not null && response.IsOkStatus;

            // https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet: process the linked resource. As in
            // browsers, the element has an empty style sheet when the response is not a style sheet (the error event is fired).
            RemoveStyleSheet();
            var sheet = CreateStyleSheet(document, linkResource, success ? response : null, ref success);
            _styleSheet = sheet;
            if (!sheet.Alternate)
            {
                SetPreferredStyleSheetSetName();
            }

            OnStyleSheetChanged();
            CssImportFetching.FetchImports(sheet);
            if (success && !synchronous)
            {
                document.AddCachedStyleSheet(preloadKey, response!);
            }

            if (synchronous)
            {
                var sheetSuccess = success;
                document.QueueElementTask(() =>
                {
                    if (generation == _fetchGeneration)
                    {
                        OnStyleSheetProcessed(sheet, sheetSuccess);
                    }
                });
            }
            else
            {
                OnStyleSheetProcessed(sheet, success);
            }
        }

        void OnStyleSheetProcessed(CssStyleSheet sheet, bool success)
        {
            StopDelayingTheLoadEvent();

            // The load event is fired once the critical subresources (the imported style sheets) are fetched
            if (sheet.ImportLoadState is { Pending: > 0 } state)
            {
                state.Completed += () =>
                {
                    if (ReferenceEquals(_styleSheet, sheet))
                    {
                        FireEvent(new Event(success && !state.Failed ? "load" : "error"));
                        StopBlockingScripts();
                        StopBlockingRendering();
                    }
                };
            }
            else
            {
                FireEvent(new Event(success && sheet.ImportLoadState is not { Failed: true } ? "load" : "error"));
                StopBlockingScripts();
                StopBlockingRendering();
            }
        }
    }

    /// <summary>
    /// Fetches the resource of a preload, of a module preload or of a prefetch: the element fires a <c>load</c> event, or an
    /// <c>error</c> event for a network error (a response whose status is not an ok status is not a network error).
    /// </summary>
    private void FetchHint(Document document, LinkResource linkResource, int generation)
    {
        void FireLoadOrError(bool success)
        {
            if (generation == _fetchGeneration)
            {
                FireEvent(new Event(success ? "load" : "error"));
            }
        }

        if (linkResource.Kind == LinkResourceKind.Icon)
        {
            ContentSecurityPolicy.ShouldBlockRequest(document, linkResource.Url, RequestDestination.Image);
            return;
        }

        // https://html.spec.whatwg.org/multipage/links.html#link-type-preconnect: "The user agent should obtain a connection
        // given partitionKey, url's origin, and useCredentials" (for an HTTP(S) URL), and
        // https://html.spec.whatwg.org/multipage/links.html#link-type-dns-prefetch: "The user agent should resolve an origin
        // given partitionKey and url's origin". The loader of the library has no connection pool nor DNS cache that it could
        // fill in advance (each request opens its own connections), so the hints are satisfied by doing nothing (they are
        // recommendations: a preconnect can be partial or skipped). They fire neither load nor error events.
        if (linkResource.Kind is LinkResourceKind.Preconnect or LinkResourceKind.DnsPrefetch)
            return;

        if (linkResource.Kind == LinkResourceKind.ModulePreload)
        {
            // https://html.spec.whatwg.org/multipage/links.html#link-type-modulepreload: the module script is fetched into the
            // module map of the window; its descendants are not fetched (the specification makes it optional)
            if (document.BrowsingContext?.Options is not { ScriptingEnabled: true, ScriptEngine: { SupportsModuleScripts: true } engine } || document.DefaultView is not { } window || !ReferenceEquals(window.Document, document))
                return;

            if (linkResource.ModuleType is not { } moduleType)
            {
                document.QueueElementTask(() => FireLoadOrError(success: false));
                return;
            }

            var options = new ModuleFetchOptions
            {
                IncludeCredentials = SubresourceFetching.UsesCredentials(this),
                ReferrerPolicy = ReferrerPolicy,
                Nonce = CryptographicNonce,
            };
            engine.PreloadModuleScript(window, linkResource.Url.Serialize(), moduleType, linkResource.Destination, options, this, success => document.QueueElementTask(() => FireLoadOrError(success)));
            return;
        }

        var request = SubresourceFetching.CreateRequest(document, linkResource.Url, linkResource.Destination, linkResource.Cors, ReferrerPolicy, this, useCredentials: SubresourceFetching.UsesCredentials(this), integrity: GetAttributeValue("integrity") ?? "");

        // As in the browsers, the request of a link element created by the parser is "parser-inserted": a script preload
        // without nonce is blocked by 'strict-dynamic' (the specification gives no parser metadata to the links)
        request.ParserInserted = CreatedByParser;
        if (linkResource.Kind == LinkResourceKind.Prefetch)
        {
            // https://html.spec.whatwg.org/multipage/links.html#link-type-prefetch: the initiator of the request is "prefetch".
            // The response is used by the next requests of the URL through the HTTP cache, when it can be stored
            request.IsPrefetch = true;
            // As in the browsers, a prefetch blocked by a content security policy has a resource timing entry
            SubresourceFetching.FetchResponse(document, request, response => FireLoadOrError(!response.IsNetworkError), reportBlockedRequest: true);
            return;
        }

        LinkPreload.Preload(document, request, response => FireLoadOrError(!response.IsNetworkError));
    }

    /// <summary>
    /// Creates the style sheet of a response (https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet). The
    /// style sheet is empty when <paramref name="response"/> is null (a network error, or a response that is not ok), or when
    /// its MIME type is not <c>text/css</c> (a response without a valid Content-Type is sniffed as a style sheet, and a
    /// CORS-same-origin style sheet whose response URL is same-origin, except a <c>data:</c> URL, is processed whatever its
    /// MIME type in quirks mode): <paramref name="success"/> is then false. The rules of the style sheet of a network error cannot be
    /// accessed.
    /// </summary>
    private CssStyleSheet CreateStyleSheet(Document document, LinkResource linkResource, DocumentResource? response, ref bool success)
    {
        var url = linkResource.Url.Serialize();
        var text = "";
        var originClean = false;
        if (response is not null)
        {
            originClean = response.IsCorsSameOrigin;
            var mimeType = response.MimeType;
            if (response.IsCssInStyleContext || (originClean && response.IsStyleSheetQuirkApplicable(linkResource.Url, document)))
            {
                // The CSS environment encoding is the encoding of the charset attribute, or the encoding of the document
                var environmentEncoding = GetAttributeValue("charset") is { } charset && WebEncodingRegistry.TryGetEncoding(charset, out var charsetEncoding) ? charsetEncoding : document.Encoding;
                text = response.Content ?? CssImportFetching.Decode(response.ContentBytes!.Value.Span, mimeType, environmentEncoding);
            }
            else
            {
                success = false;
            }
        }

        // The title is only used for the style sheets of the document tree
        var title = GetRoot() is Document ? GetAttributeValue("title") : null;
        return new CssStyleSheet(this, text, GetAttributeValue("media"), title, url, response?.Url ?? url, linkResource.Alternate, originClean)
        {
            ResourceReferrer = CssResourceReferrer.Create(response?.Url ?? url, response),
        };
    }

    private void RemoveStyleSheet()
    {
        if (_styleSheet is not { } sheet)
            return;

        // https://drafts.csswg.org/cssom/#remove-a-css-style-sheet
        _styleSheet = null;
        sheet.OwnerNode = null;
        sheet.ImportLoadState?.Cancellation.Cancel();
        OnStyleSheetChanged();
    }

    private void OnStyleSheetChanged()
    {
        // The style sheet list of the document is recomputed when the version changes (the tree does not change)
        CssStyleVersion.Increment(NodeDocument);
        NodeDocument.RequestCssResourcesUpdate();
    }

    private void StopBlockingScripts()
    {
        if (_documentWhoseScriptsAreBlocked is not { } document)
            return;

        _documentWhoseScriptsAreBlocked = null;
        document.SetStyleSheetBlockingScripts(false);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#unblock-rendering, for the style sheet of the element.</summary>
    private void StopBlockingRendering()
    {
        if (_documentWhoseRenderingIsBlocked is not { } document)
            return;

        _documentWhoseRenderingIsBlocked = null;
        document.UnblockRendering(this);
    }

    private void StopDelayingTheLoadEvent()
    {
        if (_documentWhoseLoadEventIsDelayed is not { } document)
            return;

        _documentWhoseLoadEventIsDelayed = null;
        document.LinksDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }
}

public partial class Document
{
    /// <summary>The preload key of a request (https://html.spec.whatwg.org/multipage/links.html#preload-key): its URL, destination, mode and credentials mode.</summary>
    internal static string GetPreloadKey(FetchRequest request) => string.Join(' ', request.Destination, request.Mode, request.CredentialsMode, request.Url.Serialize());

    /// <summary>
    /// The style sheets fetched by the link elements of the document, by preload key: as in the browsers (memory cache), a
    /// link element whose style sheet was already fetched does not fetch it again, and its style sheet applies synchronously.
    /// </summary>
    private Dictionary<string, DocumentResource>? _cachedStyleSheets;

    internal void AddCachedStyleSheet(string key, DocumentResource response) => (_cachedStyleSheets ??= new(StringComparer.Ordinal))[key] = response;

    internal bool TryGetCachedStyleSheet(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DocumentResource? response)
    {
        response = null;
        return _cachedStyleSheets is not null && _cachedStyleSheets.TryGetValue(key, out response);
    }
}
