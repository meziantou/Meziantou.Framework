using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The state of a document related to its browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html)
// and to its loading (https://html.spec.whatwg.org/multipage/document-lifecycle.html). Documents that are not created in a
// BrowsingContext have no browsing context: they are not fully active and they are never loaded.
public partial class Document
{
    private bool _parsingFinished;
    private List<HtmlScriptElement>? _scriptsToExecuteAsSoonAsPossible;
    private List<HtmlScriptElement>? _scriptsToExecuteInOrderAsSoonAsPossible;
    private List<HtmlScriptElement>? _scriptsToExecuteWhenParsingFinished;
    private bool _loadEventQueued;

    /// <summary>The media elements that play a media resource for the document: their players are released when the document is destroyed.</summary>
    internal HashSet<HtmlMediaElement>? MediaElementsWithPlayer { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#concept-document-bc: null when the document was not created in a browsing context, or was destroyed.</summary>
    internal BrowsingContext? BrowsingContext { get; set; }

    /// <summary>The window of the document (https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-document-window), if it was created in a browsing context.</summary>
    internal Window? RelevantWindow { get; set; }

    /// <summary>
    /// The event loop of the relevant agent of the document: the event loop of the browsing context group of its window, or
    /// of the window that created it (e.g. a document created by <c>DOMParser</c> or <c>createHTMLDocument()</c>). It is null
    /// for the documents created outside a browsing context: their tasks and microtasks are not queued.
    /// </summary>
    internal DomEventLoop? EventLoop => WorkerGlobal?.EventLoop ?? (RelevantWindow ?? OriginDocument?.RelevantWindow)?.EventLoop;

    /// <summary>
    /// The document whose relevant realm is the relevant realm of this document, for a document created without a browsing
    /// context (e.g. by <c>createHTMLDocument()</c>, the fragment parsing algorithm, or for the template contents).
    /// </summary>
    internal Document? RealmDocument { get; set; }

    /// <summary>
    /// The global object of the relevant realm of the document, if known: its window, or the one of the document that created
    /// it (<see cref="RealmDocument"/>).
    /// </summary>
    internal Window? RealmWindow => RelevantWindow ?? RealmDocument?.RealmWindow;

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#is-initial-about:blank</summary>
    internal bool IsInitialAboutBlank { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#concept-document-about-base-url</summary>
    internal UrlRecord? AboutBaseUrl { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#an-iframe-srcdoc-document: a document whose URL matches about:srcdoc.</summary>
    internal bool IsIframeSrcdocDocument => BrowsingContext.MatchesAboutSrcdoc(UrlRecord);

    /// <summary>Whether the origin of the document is a new opaque origin (e.g. an error document).</summary>
    internal bool OpaqueOrigin { get; set; }

    /// <summary>Whether the document is the error document of a navigation whose response is a network error (e.g. a blocked request).</summary>
    internal bool IsNetworkErrorDocument { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#active-sandboxing-flag-set (only the flags that change the origin are modeled).</summary>
    internal SandboxingFlags ActiveSandboxingFlags { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#completely-loaded</summary>
    internal bool CompletelyLoaded { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#ready-for-post-load-tasks</summary>
    internal bool ReadyForPostLoadTasks { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#page-showing</summary>
    internal bool PageShowing { get; set; }

    /// <summary>Whether the unload events of the document were fired (a document is unloaded once, see <see cref="BrowsingContext.UnloadRemovedChildNavigables"/>).</summary>
    internal bool IsUnloaded { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#iframe-load-in-progress</summary>
    internal bool IframeLoadInProgress { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#mute-iframe-load</summary>
    internal bool MuteIframeLoad { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#latest-entry</summary>
    internal SessionHistoryEntry? LatestEntry { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#doc-history (created on first use by <see cref="Window.History"/>).</summary>
    internal History? HistoryObject { get; set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#concept-document-internal-ancestor-origin-objects-list: the origins of
    /// the container documents, possibly masked by the referrer policy of the iframe elements (see <see cref="BrowsingContext.CreateInternalAncestorOriginObjectsList"/>).
    /// </summary>
    internal IReadOnlyList<OriginRecord> InternalAncestorOriginObjects { get; set; } = [];

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#concept-document-ancestor-origins-list (created on first use, as the internal list does not change).</summary>
    internal DomStringList AncestorOriginsList => field ??= new DomStringList([.. InternalAncestorOriginObjects.Select(origin => origin.Serialize())]);

    /// <summary>https://html.spec.whatwg.org/multipage/webstorage.html#local-storage-holder</summary>
    internal Storage? LocalStorageHolder { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/webstorage.html#session-storage-holder</summary>
    internal Storage? SessionStorageHolder { get; set; }

    /// <summary>The navigables whose container is an element of this document (including the shadow trees).</summary>
    internal List<BrowsingContext>? ChildNavigables { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#fully-active</summary>
    internal bool IsFullyActive
    {
        get
        {
            if (BrowsingContext is not { } browsingContext || !ReferenceEquals(browsingContext.Document, this))
                return false;

            return browsingContext.Container is not { } container || container.NodeDocument.IsFullyActive;
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#document-tree-child-navigables: the content navigables
    /// of the navigable containers of the document, in tree order, or in the order of their creation when
    /// <paramref name="creationOrder"/> is true (the order of the indexed properties of the WindowProxy).
    /// </summary>
    internal List<BrowsingContext> GetDocumentTreeChildNavigables(bool creationOrder = false)
    {
        if (ChildNavigables is not { Count: > 0 } children)
            return [];

        var result = new List<BrowsingContext>(children.Count);
        foreach (var child in children)
        {
            if (child.Container is { } container && container.IsConnected && ReferenceEquals(container.GetRoot(), this))
            {
                result.Add(child);
            }
        }

        if (result.Count > 1 && !creationOrder)
        {
            result.Sort((a, b) => ReferenceEquals(a.Container, b.Container) ? 0 : a.Container!.IsPreceding(b.Container!) ? -1 : 1);
        }

        return result;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#fallback-base-url</summary>
    internal UrlRecord FallbackBaseUrl
    {
        get
        {
            if (IsIframeSrcdocDocument && AboutBaseUrl is { } srcdocBaseUrl)
                return srcdocBaseUrl;

            if (AboutBaseUrl is { } aboutBaseUrl && BrowsingContext.MatchesAboutBlank(UrlRecord))
                return aboutBaseUrl;

            return UrlRecord;
        }
    }

    /// <summary>
    /// Sets the URL of the document (https://html.spec.whatwg.org/multipage/browsing-the-web.html#set-the-url): a URL
    /// change of the same document (history.pushState(), a fragment navigation, document.open()) keeps its origin.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="keepTargetElement">
    /// Whether the target element is kept (https://html.spec.whatwg.org/multipage/browsing-the-web.html#url-and-history-update-steps:
    /// <c>history.pushState()</c> and the navigations intercepted by the navigation API change the URL without scrolling to the
    /// fragment), until the document scrolls to the fragment.
    /// </param>
    internal void SetUrl(UrlRecord url, bool keepTargetElement = false)
    {
        var origin = ReferenceEquals(GetOriginDocument(this), this) && !OpaqueOrigin ? Origin : null;
        _creationUrl ??= UrlRecord;
        var targetFragment = TargetFragment;
        UrlRecord = url;
        if (keepTargetElement)
        {
            _keptTargetFragment = targetFragment;
            _isTargetFragmentKept = true;
        }
        else
        {
            _isTargetFragmentKept = false;
        }

        var fragmentChanged = !string.Equals(targetFragment, TargetFragment, StringComparison.Ordinal);
        _baseUrlCache = null;
        InvalidateSpeculationLinks();

        // The fragment gives the target element (:target), which is not a mutation of the tree
        if (fragmentChanged)
        {
            IncrementElementStateVersion();
        }

        if (origin is not null)
        {
            _origin = origin;
            _originUrl = url;
        }

        // The fragment navigations and the URL changes of history.pushState() and history.replaceState() are visits
        if (BrowsingContext is { Top.IsPrerendering: false })
        {
            RecordVisitedLink(url);
        }
    }

    private string? _keptTargetFragment;
    private bool _isTargetFragmentKept;

    /// <summary>
    /// The fragment that gives the target element of the document (https://html.spec.whatwg.org/multipage/browsing-the-web.html#target-element):
    /// the fragment of the URL, or the fragment the document last scrolled to when the URL changed without scrolling to the
    /// fragment (see <see cref="SetUrl"/>).
    /// </summary>
    internal string? TargetFragment => _isTargetFragmentKept ? _keptTargetFragment : UrlRecord.Fragment;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#scroll-to-the-fragment-identifier: the target element
    /// becomes the indicated part of the document, given by the fragment of its URL.
    /// </summary>
    internal void UpdateTargetFragment()
    {
        if (!_isTargetFragmentKept)
            return;

        _isTargetFragmentKept = false;
        if (!string.Equals(_keptTargetFragment, UrlRecord.Fragment, StringComparison.Ordinal))
        {
            IncrementElementStateVersion();
        }

        _keptTargetFragment = null;
    }

    /// <summary>
    /// The creation URL of the environment of the document (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-creation-url):
    /// its URL when it was loaded, which a URL change of the same document (<see cref="SetUrl"/>) does not change.
    /// </summary>
    internal UrlRecord CreationUrl => _creationUrl ?? UrlRecord;

    /// <summary>
    /// The URL of the cookies of the document (<c>document.cookie</c> uses its URL, the Cookie Store API its creation URL):
    /// the URL of the document whose origin it has when it inherits its origin (e.g. an <c>about:blank</c> or
    /// <c>about:srcdoc</c> iframe), as the browsers do.
    /// </summary>
    internal UrlRecord GetCookieUrl(bool creationUrl)
    {
        var originDocument = GetOriginDocument(this);
        if (!ReferenceEquals(originDocument, this))
            return originDocument.GetCookieUrl(creationUrl);

        return creationUrl ? CreationUrl : UrlRecord;
    }

    private UrlRecord? _creationUrl;

    /// <summary>The fetch group of the document (https://fetch.spec.whatwg.org/#concept-fetch-group): its ongoing fetch() and XMLHttpRequest fetches.</summary>
    internal FetchGroup FetchGroup => field ??= new FetchGroup();

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-lifecycle.html#abort-a-document-and-its-descendants: the active parsers of
    /// the documents are aborted (they do not complete their loading), and their fetches are canceled.
    /// </summary>
    /// <param name="abortFetches">
    /// Whether the fetches are canceled. A navigation does not cancel them, as the browsers do: the fetches of a document
    /// are terminated when it is destroyed, without firing events (e.g. no abort event of XMLHttpRequest).
    /// </param>
    internal void AbortDocumentAndDescendants(bool abortFetches = true)
    {
        if (ChildNavigables is { } children)
        {
            foreach (var child in children.ToArray())
            {
                child.Document.AbortDocumentAndDescendants(abortFetches);
            }
        }

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#abort-a-document
        if (abortFetches)
        {
            FetchGroup.Abort();
        }

        if (ActiveParser is { } parser)
        {
            ActiveParserWasAborted = true;
            parser.Abort();
        }
    }

    /// <summary>
    /// The origin of the document (https://dom.spec.whatwg.org/#concept-document-origin): the origin of the document it was
    /// created by (<see cref="OriginDocument"/>), else a new opaque origin (<see cref="OpaqueOrigin"/>), else the origin of
    /// its URL. The origin record is kept while its URL has the same origin, so that its domain (set by <c>document.domain</c>)
    /// is preserved.
    /// </summary>
    internal OriginRecord Origin
    {
        get
        {
            var originDocument = GetOriginDocument(this);
            if (!ReferenceEquals(originDocument, this))
                return originDocument.Origin;

            if (OpaqueOrigin)
            {
                if (_origin is not { IsOpaque: true })
                {
                    _origin = OriginRecord.CreateOpaque();
                    _originUrl = null;
                }

                return _origin;
            }

            if (_origin is null || !ReferenceEquals(_originUrl, UrlRecord))
            {
                var origin = OriginRecord.FromUrl(UrlRecord);
                if (_origin is null || _origin.IsOpaque || origin.IsOpaque || !OriginRecord.IsSameOrigin(_origin, origin))
                {
                    _origin = origin;
                }

                _originUrl = UrlRecord;
            }

            return _origin;
        }
    }

    private OriginRecord? _origin;
    private UrlRecord? _originUrl;

    /// <summary>
    /// The top-level origin of the environment of the document (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-top-level-origin):
    /// the origin of the active document of the top-level browsing context, or the origin of the document when it has no browsing context.
    /// </summary>
    /// <summary>
    /// The value of the <c>Document-Isolation-Policy</c> header (https://wicg.github.io/document-isolation-policy/), whose
    /// "isolate-and-require-corp" and "isolate-and-credentialless" values apply the rules of the corresponding embedder
    /// policies to the subresources of the document, or <see cref="EmbedderPolicyValue.UnsafeNone"/>.
    /// </summary>
    internal EmbedderPolicyValue DocumentIsolationPolicy { get; set; }

    /// <summary>The embedder policy that applies to the subresource requests of the document: its embedder policy, else its document isolation policy.</summary>
    internal EmbedderPolicyValue SubresourceEmbedderPolicy => EmbedderPolicy.Value != EmbedderPolicyValue.UnsafeNone ? EmbedderPolicy.Value : DocumentIsolationPolicy;

    /// <summary>
    /// Whether the document is loaded in a credentialless iframe, or in one of its descendants (the "is credentialless" flag
    /// of the policy container, https://wicg.github.io/anonymous-iframe/#spec-policy-container).
    /// </summary>
    internal bool IsCredentialless { get; set; }

    /// <summary>
    /// The cookie jar of the requests of the document: the one of the browsing context, or, for a credentialless document,
    /// an ephemeral one shared by the credentialless documents of the top-level document (https://wicg.github.io/anonymous-iframe/#spec-cookies).
    /// </summary>
    internal CookieJar? CookieJar => WorkerGlobal is { } worker ? worker.Agent.CookieJar : BrowsingContext?.GetCookieJar(IsCredentialless);

    /// <summary>The cookie jar of the credentialless documents embedded by this top-level document.</summary>
    internal CookieJar CredentiallessCookieJar => field ??= new CookieJar(BrowsingContext?.Options.TimeProvider);

    /// <summary>The serialized "site for cookies" of the requests of the document (see <see cref="DocumentRequest.SiteForCookies"/>).</summary>
    internal string SiteForCookies => CookieContexts.GetSiteForCookies(this).Serialize();

    /// <summary>The serialized top-level origin of the requests of the document (see <see cref="DocumentRequest.TopLevelOrigin"/>).</summary>
    internal string TopLevelOriginForCookies => CookieContexts.GetTopLevelOrigin(this).Serialize();

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#concept-document-coop</summary>
    internal OpenerPolicy OpenerPolicy { get; set; } = OpenerPolicy.Default;

    /// <summary>The embedder policy of the policy container of the document (https://html.spec.whatwg.org/multipage/browsers.html#policy-container-embedder-policy).</summary>
    internal EmbedderPolicy EmbedderPolicy { get; set; } = EmbedderPolicy.Default;

    /// <summary>
    /// The permissions policy of the document (https://html.spec.whatwg.org/multipage/dom.html#concept-document-permissions-policy):
    /// the policy inherited from the document of its container, and the declarations of its <c>Permissions-Policy</c> header.
    /// See <see cref="PermissionsPolicyAlgorithms.IsAllowedToUse"/>.
    /// </summary>
    internal PermissionsPolicyRecord PermissionsPolicyRecord { get; set; } = PermissionsPolicyRecord.Empty;

    /// <summary>
    /// The report-only permissions policy of the document (https://w3c.github.io/webappsec-permissions-policy/#document-report-only-permissions-policy):
    /// the declarations of its <c>Permissions-Policy-Report-Only</c> header, whose violations are only reported.
    /// </summary>
    internal PermissionsPolicyRecord ReportOnlyPermissionsPolicyRecord { get; set; } = PermissionsPolicyRecord.Empty;

    /// <summary>
    /// Gets the policy object of the document (https://w3c.github.io/webappsec-permissions-policy/#dom-document-permissionspolicy):
    /// the features that its permissions policy allows.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-permissions-policy/#dom-document-permissionspolicy"/>
    [IdlMember("permissionsPolicy", "https://w3c.github.io/webappsec-permissions-policy/#dom-document-permissionspolicy")]
    public PermissionsPolicy PermissionsPolicy => field ??= new PermissionsPolicy(this);

    internal OriginRecord TopLevelOrigin => BrowsingContext?.Top.Document.Origin ?? Origin;

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#ascii-serialisation-of-an-origin</summary>
    internal string SerializeOrigin() => Origin.Serialize();

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#same-origin</summary>
    internal static bool IsSameOrigin(Document a, Document b) => ReferenceEquals(GetOriginDocument(a), GetOriginDocument(b)) || OriginRecord.IsSameOrigin(a.Origin, b.Origin);

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#same-origin-domain: the domains set by <c>document.domain</c> are compared.</summary>
    internal static bool IsSameOriginDomain(Document a, Document b) => ReferenceEquals(GetOriginDocument(a), GetOriginDocument(b)) || OriginRecord.IsSameOriginDomain(a.Origin, b.Origin);

    /// <summary>
    /// Whether the document is potentially trustworthy for the secure context of its environment
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#secure-context): a document with an opaque origin (e.g. a
    /// sandboxed or data: document) is trustworthy when its URL is (e.g. an <c>about:srcdoc</c> document), as the browsers
    /// check the origin it would have had (its ancestors are checked too).
    /// </summary>
    internal bool IsPotentiallyTrustworthyForSecureContext()
        => GetOriginDocument(this).OpaqueOrigin ? ReferrerPolicies.IsPotentiallyTrustworthy(UrlRecord) : IsPotentiallyTrustworthy();

    /// <summary>https://w3c.github.io/webappsec-secure-contexts/#is-origin-trustworthy</summary>
    internal bool IsPotentiallyTrustworthy()
    {
        var originDocument = GetOriginDocument(this);
        if (originDocument.OpaqueOrigin)
            return false;

        return ReferrerPolicies.IsOriginPotentiallyTrustworthy(originDocument.UrlRecord);
    }

    /// <summary>Queues a task that runs only if the document is fully active (https://html.spec.whatwg.org/multipage/webappapis.html#queue-a-global-task).</summary>
    internal void QueueGlobalTask(Action task)
    {
        // The document of a worker queues the tasks on the event loop of the worker
        if (WorkerGlobal is { } worker)
        {
            worker.QueueGlobalTask(task);
            return;
        }

        // The task of a document that is not fully active would never run
        if (BrowsingContext is not { } browsingContext)
            return;

        browsingContext.EventLoop.QueueTask(() =>
        {
            if (IsFullyActive)
            {
                task();
            }
        });
    }

    /// <summary>
    /// Queues an element task of an element of this document (https://html.spec.whatwg.org/multipage/webappapis.html#queue-an-element-task).
    /// The task runs only while the document of the relevant global object is fully active. The relevant global object of
    /// a document created by a script without a browsing context (<c>DOMParser</c>, the fragment parsing algorithm...) is
    /// the window that created it, so its tasks run in the event loop of that window. The tasks of the other documents
    /// without a browsing context (e.g. parsed documents) are not queued.
    /// </summary>
    /// <summary>
    /// The document of the environment of the fetches and tasks of the document: the document itself when it has a browsing
    /// context, otherwise the document of the window that created it (e.g. by <c>createHTMLDocument()</c>), or null.
    /// </summary>
    internal Document? EnvironmentDocument => BrowsingContext is not null ? this : OriginDocument?.RelevantWindow?.Document is { BrowsingContext: not null } globalDocument ? globalDocument : null;

    internal void QueueElementTask(Action task)
    {
        if (BrowsingContext is null && OriginDocument?.RelevantWindow?.Document is { } globalDocument && !ReferenceEquals(globalDocument, this))
        {
            globalDocument.QueueGlobalTask(task);
            return;
        }

        QueueGlobalTask(task);
    }

    /// <summary>
    /// The response of the navigation that created a media document (https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-media):
    /// the element for the media uses it instead of fetching the resource again (the content security policy of the document
    /// does not apply to it).
    /// </summary>
    internal DocumentResource? MediaDocumentResource { get; set; }

    /// <summary>Returns the response of the navigation of a media document for the request of its element for the media (once), or null.</summary>
    internal DocumentResource? TakeMediaDocumentResource(Urls.UrlRecord url)
    {
        if (MediaDocumentResource is not { } resource || url.Serialize() != UrlRecord.Serialize())
            return null;

        MediaDocumentResource = null;
        return resource;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#update-the-current-document-readiness</summary>
    internal void UpdateReadiness(DocumentReadyState readiness)
    {
        if (ReadyState == readiness)
            return;

        ReadyState = readiness;
        if (readiness == DocumentReadyState.Interactive)
        {
            MarkDomInteractive();
        }
        else if (readiness == DocumentReadyState.Complete)
        {
            MarkDomComplete();
        }

        // https://html.spec.whatwg.org/multipage/links.html#process-internal-resource-link: the link rel=expect elements only
        // block the rendering while the readiness is "loading". A document that is completely loaded is never render-blocked.
        if (readiness != DocumentReadyState.Loading)
        {
            ProcessInternalResourceLinks();
            if (readiness == DocumentReadyState.Complete)
            {
                EndRenderBlockingPeriod();
            }
        }

        if (!HasBeenRevealed)
        {
            EventLoop?.OnDocumentParsed(this);
        }

        FireEvent(new Event("readystatechange"));
        if (readiness == DocumentReadyState.Complete)
        {
            OnFontEnvironmentChanged();
        }
    }

    /// <summary>
    /// The steps run when the parser of a document of a browsing context stops (https://html.spec.whatwg.org/multipage/parsing.html#the-end):
    /// the DOMContentLoaded event, then the load events once nothing delays the load event.
    /// </summary>
    internal void FinishParsing()
    {
        UpdateReadiness(DocumentReadyState.Interactive);
        FinishParsingAfterDeferredScripts();
    }

    /// <summary>
    /// The steps of https://html.spec.whatwg.org/multipage/parsing.html#the-end run once the deferred scripts are executed:
    /// the DOMContentLoaded event, then the load events once nothing delays the load event.
    /// </summary>
    /// <remarks>
    /// Like the browsers (FrameLoader::finishedParsing in WebKit and Blink), the document scrolls to its fragment again
    /// after DOMContentLoaded, so the ancestor revealing algorithm runs again on the target of a fragment navigation done
    /// while parsing: it reveals the ancestors that a beforematch event handler prevented from being revealed.
    /// </remarks>
    internal void FinishParsingAfterDeferredScripts()
    {
        var parsingSession = _parsingSession;
        QueueGlobalTask(() =>
        {
            var marked = MarkDomContentLoadedEventStart();
            FireEvent(new Event("DOMContentLoaded", new EventInit { Bubbles = true }));
            if (marked)
            {
                MarkDomContentLoadedEventEnd();
            }

            // https://html.spec.whatwg.org/multipage/parsing.html#the-end: the client message queue of the
            // ServiceWorkerContainer of the document is enabled
            IsDomContentLoadedFired = true;
            if (RelevantWindow is { } window && ReferenceEquals(window.AssociatedDocument, this))
            {
                window.ServiceWorkerObjectsOrNull?.Container?.EnableClientMessageQueue();
            }

            // https://wicg.github.io/scroll-to-text-fragment/#restricting-scroll-on-load: a document whose force-load-at-top
            // policy is set does not scroll to its fragment when it is loaded. The pending text directives are searched for
            // the last time.
            if (BrowsingContext is not null && ReferenceEquals(BrowsingContext.Document, this) && !ForceLoadAtTop)
            {
                Dom.BrowsingContext.ScrollToTheFragment(this, AllowTextDirectiveScroll);
            }

            PendingTextDirectives = null;

            // https://html.spec.whatwg.org/multipage/parsing.html#the-end, steps 7 to 9: the event loop spins until nothing
            // delays the load event, so the tasks queued while parsing (e.g. a planned form navigation of a child navigable,
            // which delays the load event) run before the load event is queued
            if (parsingSession == _parsingSession)
            {
                _parsingFinished = true;
                TryQueueLoadEvent();
            }
        });
    }

    // Incremented when the script-created parser of document.open() replaces the parser (see ResetParsingState)
    private int _parsingSession;

    /// <summary>Whether the DOMContentLoaded event of the document was fired (the end of https://html.spec.whatwg.org/multipage/parsing.html#the-end, step 6).</summary>
    internal bool IsDomContentLoadedFired { get; private set; }

    /// <summary>The steps of "the end" run again when the script-created parser of document.open() stops.</summary>
    internal void ResetParsingState()
    {
        _parsingSession++;
        _parsingFinished = false;
        _loadEventQueued = false;
    }

    /// <summary>The parser of the document while it is parsing (https://html.spec.whatwg.org/multipage/dom.html#active-parser).</summary>
    internal Html.Parsing.ScriptingParser? ActiveParser
    {
        get => _activeParser;
        set
        {
            _activeParser = value;
            if (value is null)
            {
                // A document whose parser stopped before it inserted the body element may no longer be render-blocked
                RequestSkippedRenderingUpdate();
            }
        }
    }

    private Html.Parsing.ScriptingParser? _activeParser;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#pending-parsing-blocking-script</summary>
    internal HtmlScriptElement? PendingParsingBlockingScript { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#set-of-scripts-that-will-execute-as-soon-as-possible</summary>
    internal List<HtmlScriptElement> ScriptsToExecuteAsSoonAsPossible => _scriptsToExecuteAsSoonAsPossible ??= [];

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#list-of-scripts-that-will-execute-in-order-as-soon-as-possible</summary>
    internal List<HtmlScriptElement> ScriptsToExecuteInOrderAsSoonAsPossible => _scriptsToExecuteInOrderAsSoonAsPossible ??= [];

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#list-of-scripts-that-will-execute-when-the-document-has-finished-parsing</summary>
    internal List<HtmlScriptElement> ScriptsToExecuteWhenParsingFinished => _scriptsToExecuteWhenParsingFinished ??= [];

    /// <summary>The number of script elements whose "delaying the load event" is true (https://html.spec.whatwg.org/multipage/scripting.html#concept-script-delay-load).</summary>
    internal int ScriptsDelayingTheLoadEvent { get; set; }

    /// <summary>
    /// The number of fetches of the style sheets of @import rules in progress: the elements delay the load event until the
    /// critical subresources of their style sheet are fetched (https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block).
    /// </summary>
    internal int StyleSheetImportsDelayingTheLoadEvent { get; set; }

    /// <summary>The number of img elements whose image is being fetched: they delay the load event (https://html.spec.whatwg.org/multipage/images.html#update-the-image-data).</summary>
    internal int ImagesDelayingTheLoadEvent { get; set; }

    /// <summary>
    /// The number of images of the computed styles (see <see cref="Css.Om.CssImageFetching"/>) whose fetch started before the load
    /// event and is in progress: as in the browsers, they delay the load event (the fetches of a document delay its load event,
    /// https://html.spec.whatwg.org/multipage/parsing.html#delay-the-load-event), so that the layout at the load event uses them
    /// (the natural size of the image of a generated content or of a list marker, the float area of a shape from an image).
    /// </summary>
    internal int CssImagesDelayingTheLoadEvent { get; set; }

    /// <summary>
    /// The number of link elements whose linked resource is being fetched: they delay the load event until the resource and
    /// its critical subresources are fetched (https://html.spec.whatwg.org/multipage/semantics.html#the-link-element).
    /// </summary>
    internal int LinksDelayingTheLoadEvent { get; set; }

    /// <summary>
    /// The number of embed and object elements whose task is queued or whose resource is being fetched: they delay the load
    /// event (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element), and then their content navigable does.
    /// </summary>
    internal int EmbeddedContentDelayingTheLoadEvent { get; set; }

    /// <summary>Whether the scripts delay the load event: they are being fetched, or will execute as soon as possible (https://html.spec.whatwg.org/multipage/parsing.html#the-end, steps 7 and 8).</summary>
    private bool AreScriptsDelayingTheLoadEvent => ScriptsDelayingTheLoadEvent > 0 || _scriptsToExecuteAsSoonAsPossible is { Count: > 0 } || _scriptsToExecuteInOrderAsSoonAsPossible is { Count: > 0 };

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#delay-the-load-event: the navigable containers delay the load
    /// event of their document while their content navigable is loading, i.e. while it is delaying load events or its
    /// active document is not ready for post-load tasks. The fetches started by a child document after its load event
    /// (e.g. an image created by a load event handler) do not delay the load event of the parent.
    /// </summary>
    private bool IsLoadEventDelayed()
    {
        if (AreScriptsDelayingTheLoadEvent || StyleSheetImportsDelayingTheLoadEvent > 0 || ImagesDelayingTheLoadEvent > 0 || LinksDelayingTheLoadEvent > 0 || EmbeddedContentDelayingTheLoadEvent > 0 || CssImagesDelayingTheLoadEvent > 0)
            return true;

        if (ChildNavigables is not { Count: > 0 } children)
            return false;

        foreach (var child in children)
        {
            // An iframe whose current navigation was lazy loaded does not delay the load event
            // (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#current-navigation-was-lazy-loaded)
            if ((child.IsDelayingLoadEvents || !child.Document.ReadyForPostLoadTasks) && child.Container is not HtmlIFrameElement { CurrentNavigationWasLazyLoaded: true })
                return true;
        }

        return false;
    }

    /// <summary>Queues the load task once the parsing is finished and nothing delays the load event (https://html.spec.whatwg.org/multipage/parsing.html#the-end).</summary>
    internal void TryQueueLoadEvent()
    {
        if (!_parsingFinished || _loadEventQueued || IsLoadEventDelayed())
            return;

        // The images of the computed styles are fetched when the rendering is updated: the ones of the styles that changed since
        // the last rendering opportunity are fetched now, and delay the load event
        if (FetchPendingCssImages() && IsLoadEventDelayed())
            return;

        _loadEventQueued = true;
        var childNavigables = ChildNavigables is { Count: > 0 } children ? new HashSet<BrowsingContext>(children) : null;
        QueueGlobalTask(() =>
        {
            // https://html.spec.whatwg.org/multipage/parsing.html#the-end (steps 8–9): the load event is delayed by the
            // navigables inserted since the task was queued (e.g. an iframe inserted by a DOMContentLoaded listener): the
            // task is queued again once they no longer delay it
            if (ChildNavigables is { Count: > 0 } currentChildren)
            {
                foreach (var child in currentChildren)
                {
                    if (childNavigables?.Contains(child) != true && (child.IsDelayingLoadEvents || !child.Document.ReadyForPostLoadTasks) && child.Container is not HtmlIFrameElement { CurrentNavigationWasLazyLoaded: true })
                    {
                        _loadEventQueued = false;
                        return;
                    }
                }
            }

            UpdateReadiness(DocumentReadyState.Complete);
            if (BrowsingContext is null || RelevantWindow is not { } window)
                return;

            FireSvgLoadEvents();
            var marked = MarkLoadEventStart();
            window.FireLegacyTargetOverrideEvent(new Event("load"));
            if (marked)
            {
                MarkLoadEventEnd();
            }

            OnLoadEventFired();

            // All the browsers fire the load event of the container right after the load event of the window, before the
            // pageshow event (the specification fires pageshow first, then queues a task that fires the load event of the
            // container, https://html.spec.whatwg.org/multipage/document-lifecycle.html#completely-finish-loading). While
            // the navigable is navigating away from the document, the event is fired by the task, as specified: the new
            // document may be the active one by then, and the container does not get the load event of this document.
            // The page is showing from then on: a container that is removed by its load event listener unloads the document
            // (pagehide), as in the browsers.
            PageShowing = true;
            var isContainerLoadEventFired = BrowsingContext is { OngoingNavigationId: 0, Container.NodeDocument.IsFullyActive: true };
            if (isContainerLoadEventFired)
            {
                FireContainerLoadEvent();
                if (BrowsingContext is null || !ReferenceEquals(RelevantWindow, window) || !PageShowing)
                    return;
            }

            // https://html.spec.whatwg.org/multipage/nav-history-apis.html#fire-a-page-transition-event
            window.FireLegacyTargetOverrideEvent(new PageTransitionEvent("pageshow", new PageTransitionEventInit { Persisted = false, Bubbles = true, Cancelable = true }));
            CompletelyFinishLoading(isContainerLoadEventFired);
        });
    }

    /// <summary>Whether an svg element was inserted in the document (see <see cref="FireSvgLoadEvents"/>).</summary>
    internal bool HasSvgSvgElements { get; set; }

    /// <summary>
    /// https://svgwg.org/svg2-draft/interact.html#LoadEvent: as in the browsers, the load event of the outermost svg elements of
    /// the document is fired before the load event of the window (it does not bubble).
    /// </summary>
    private void FireSvgLoadEvents()
    {
        if (!HasSvgSvgElements)
            return;

        List<SvgSvgElement>? elements = null;
        for (Node? node = DocumentElement; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is SvgSvgElement svg && Svg.SvgUserSpace.IsOutermost(svg))
            {
                (elements ??= []).Add(svg);
            }
        }

        if (elements is null)
            return;

        foreach (var element in elements)
        {
            if (element.IsConnected && ReferenceEquals(element.NodeDocument, this))
            {
                element.FireEvent(new Event("load"));
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#completely-finish-loading</summary>
    private void CompletelyFinishLoading(bool isContainerLoadEventFired)
    {
        CompletelyLoaded = true;

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#completely-finish-loading: the navigation timing
        // entry is queued to the performance observers once the load event completed
        if (NavigationTimingEntry is { } navigationTimingEntry && RelevantWindow is { } relevantWindow && ReferenceEquals(relevantWindow.Performance.NavigationTimingEntry, navigationTimingEntry) && !_navigationTimingEntryQueued)
        {
            _navigationTimingEntryQueued = true;
            relevantWindow.Performance.QueueNavigationTimingEntry();
        }

        RunCompletelyLoadedCallbacks();
        if (BrowsingContext?.Container is not { } container)
        {
            ReadyForPostLoadTasks = true;
            return;
        }

        // The load event of the container that was not fired yet is fired by an element task. The document is ready for
        // post-load tasks afterwards, so that the load event of the container document is fired after the one of the container.
        container.NodeDocument.QueueGlobalTask(() =>
        {
            if (!isContainerLoadEventFired)
            {
                // As in the browsers, the container does not get the load event of a document that its navigable is navigating
                // away from (e.g. a form submitted while the document was parsed replaces it before its load event). The event
                // is fired if the navigation ends without replacing the document.
                if (BrowsingContext is { IsCrossDocumentNavigationOngoing: true })
                {
                    IsContainerLoadEventPending = true;
                }
                else
                {
                    FireContainerLoadEvent();
                }
            }

            ReadyForPostLoadTasks = true;
            container.NodeDocument.TryQueueLoadEvent();
        });
    }

    /// <summary>
    /// Whether the load event of the container was not fired because the navigable was navigating away from the document
    /// (see <see cref="FireSuppressedContainerLoadEvent"/>).
    /// </summary>
    private bool IsContainerLoadEventPending { get; set; }

    /// <summary>Fires the load event of the container that was not fired while the navigable was navigating away from the document.</summary>
    internal void FireSuppressedContainerLoadEvent()
    {
        if (!IsContainerLoadEventPending || BrowsingContext?.Container is not { } container)
            return;

        IsContainerLoadEventPending = false;
        container.NodeDocument.QueueGlobalTask(FireContainerLoadEvent);
    }

    /// <summary>Fires the load event of the navigable container of the document (the iframe load event steps).</summary>
    private void FireContainerLoadEvent()
    {
        if (BrowsingContext?.Container is not { } container)
            return;

        if (container is HtmlIFrameElement iframe)
        {
            if (ReferenceEquals(iframe.ContentNavigable?.Document, this))
            {
                iframe.RunIframeLoadEventSteps();
            }
        }
        else if (ReferenceEquals(NavigableContainers.GetContentNavigable(container)?.Document, this))
        {
            BrowsingContext?.ReportContainerResourceTimingFallback();
            container.FireEvent(new Event("load"));
        }
    }
}
