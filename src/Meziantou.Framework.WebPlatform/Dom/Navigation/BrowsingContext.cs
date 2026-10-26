using System.Net;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.Xml;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The window type of the rules for choosing a navigable (https://html.spec.whatwg.org/multipage/document-sequences.html#the-rules-for-choosing-a-navigable).</summary>
internal enum WindowType
{
    ExistingOrNone,
    NewAndUnrestricted,
    NewWithNoOpener,
}

/// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigationhistorybehavior</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationhistorybehavior"/>
[IdlInterface("NavigationHistoryBehavior", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationhistorybehavior")]
public enum NavigationHistoryBehavior
{
    /// <summary>
    /// Lets the user agent choose: a new entry is pushed, unless the navigation targets the current URL, in which case
    /// the current entry is replaced.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistorybehavior-auto"/>
    [IdlMember("auto", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistorybehavior-auto")]
    Auto,

    /// <summary>Adds a new entry to the session history.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistorybehavior-push"/>
    [IdlMember("push", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistorybehavior-push")]
    Push,

    /// <summary>Replaces the current entry of the session history.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistorybehavior-replace"/>
    [IdlMember("replace", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistorybehavior-replace")]
    Replace,
}

/// <summary>
/// A navigable and its active browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#navigables).
/// A <see cref="BrowsingContext"/> created by the application is a top-level traversable; the <c>iframe</c> and
/// <c>frame</c> elements of its documents create nested browsing contexts.
/// </summary>
/// <remarks>
/// <para>
/// Documents are loaded in a browsing context by navigating it: <see cref="Navigate(string, Func{Url, string})"/> and
/// <see cref="Navigate(string, Func{Url, DocumentResource})"/> load content provided by the application, and
/// <see cref="Navigate(string)"/> loads the document provided by <see cref="BrowsingContextOptions.DocumentLoader"/>. The
/// loaded document becomes the active document of the browsing context: it has a <see cref="Document.DefaultView"/>, and
/// the load events are fired by the tasks of the <see cref="DomEventLoop"/>.
/// </para>
/// <para>
/// Nothing is fetched: the documents of the navigations to other URLs than <c>about:blank</c> and <c>about:srcdoc</c> are
/// provided by <see cref="BrowsingContextOptions.DocumentLoader"/>. No script is executed unless
/// <see cref="BrowsingContextOptions.ScriptEngine"/> is set.
/// </para>
/// <para>
/// A top-level browsing context is the root of a browsing context group, which has its own <see cref="DomEventLoop"/>.
/// <see cref="Dispose"/> discards its documents and the pending tasks of its event loop.
/// </para>
/// </remarks>
public sealed partial class BrowsingContext : IDisposable
{
    private const string AboutBlank = "about:blank";

    /// <summary>The maximum number of ancestors of a navigable that navigates (Firefox allows 10 nested frames).</summary>
    private const int MaxNestingDepth = 20;
    private static readonly UrlRecord AboutSrcdocUrl = UrlParser.Parse("about:srcdoc", baseUrl: null)!;
    private static int s_nextNavigationId;

    private int _ongoingNavigation;

    /// <summary>
    /// The identifier of the ongoing navigation, or 0. The identifiers increase: a navigation whose identifier is greater
    /// than a given value started after it.
    /// </summary>
    internal int OngoingNavigationId => _ongoingNavigation;

    /// <summary>
    /// Gets whether a navigation of the browsing context is ongoing: it started (e.g. <c>location.href</c> was set, or a link
    /// was followed), and its document is not active yet. It becomes false when the new document is active, and when the
    /// navigation ends without a new document (e.g. a 204 response, or a navigation canceled by another one).
    /// </summary>
    public bool IsNavigating => _ongoingNavigation != 0;

    /// <summary>The identifier that the next navigation will exceed (see <see cref="OngoingNavigationId"/>).</summary>
    internal static int LastNavigationId => Volatile.Read(ref s_nextNavigationId);
#pragma warning disable CA2213 // The cancellation of an aborted navigation is not disposed, as the loader may still use its token
    private CancellationTokenSource? _loaderCancellation;
#pragma warning restore CA2213
    private DocumentParameters? _pendingDocumentParameters;
    private BlobUrlStore? _blobUrls;
    private List<BroadcastChannel>? _broadcastChannels;
    private AgentClusterMap? _agentClusters;
    private StorageShed? _localStorageShed;
    private IndexedDBBackend? _indexedDBBackend;
    private CacheStorageBackend? _cacheStorageBackend;
    private FileSystemBackend? _fileSystemBackend;
    private StoragePersistenceState? _storagePersistence;
    private StorageShed? _sessionStorageShed;
    private ServiceWorkerBackend? _serviceWorkerBackend;

    /// <summary>Creates a top-level browsing context, whose active document is an initial <c>about:blank</c> document.</summary>
    public BrowsingContext(BrowsingContextOptions? options = null)
        : this(options ?? new BrowsingContextOptions(), parent: null, container: null, groupOwner: null, creator: null, name: "")
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context</summary>
    /// <param name="options">The options.</param>
    /// <param name="parent">The parent of a nested browsing context.</param>
    /// <param name="container">The container of a nested browsing context.</param>
    /// <param name="groupOwner">The browsing context created by the application that owns the group of an auxiliary browsing context.</param>
    /// <param name="creator">The document that creates an auxiliary browsing context (its opener), or null.</param>
    /// <param name="name">The target name of an auxiliary browsing context.</param>
    /// <param name="popupSandboxingFlags">The popup sandboxing flag set of an auxiliary browsing context.</param>
    private BrowsingContext(BrowsingContextOptions options, BrowsingContext? parent, Element? container, BrowsingContext? groupOwner, Document? creator, string name, SandboxingFlags popupSandboxingFlags = SandboxingFlags.None, bool prerendering = false)
    {
        IsPrerendering = prerendering;
        _hasSystemFocus = !prerendering;
        _popupSandboxingFlags = popupSandboxingFlags;
        Options = options;
        Parent = parent;
        Container = container;
        _groupOwner = groupOwner;
        EventLoop = parent?.EventLoop ?? groupOwner?.EventLoop ?? new DomEventLoop();
        Name = container?.GetAttributeValue("name") ?? name;
        if (options.ScriptEngine is { } scriptEngine)
        {
            EventLoop.AttachScriptEngine(scriptEngine);
        }

        if (parent is null && groupOwner is null && options.TimeProvider is { } timeProvider)
        {
            EventLoop.TimeProvider = timeProvider;
        }

        // The windows of the browsing context group receive the storage events of the shared local storage areas
        if (parent is null && groupOwner is null)
        {
            options.LocalStorage?.AddBrowsingContext(this);
        }

        // Create a new browsing context and document: the initial about:blank document has the origin and base URL of its creator
        creator ??= container?.NodeDocument;
        var document = Document.CreateHtmlDocument();
        document.Mode = DocumentMode.Quirks;
        document.IsInitialAboutBlank = true;
        document.OriginDocument = creator is null ? null : Document.GetOriginDocument(creator);

        // The sandboxing flags of the iframe or of the popup, and the CSP-derived sandboxing flags of the policy container
        // inherited from the creator (https://w3c.github.io/webappsec-csp/#sandbox-init), as for the about:blank documents of a
        // navigation: a popup that escapes the sandbox of its opener still inherits its CSP (WPT sandbox-inherit-to-blank-document-unsandboxed.html).
        // As in Chromium, the sandbox directive of the required CSP of the iframe (its csp attribute) applies too.
        var policyContainer = creator is null ? null : PolicyContainer.Clone(creator);
        var sandboxingFlags = GetIframeSandboxingFlags();
        if (policyContainer is not null)
        {
            sandboxingFlags |= GetContentSecurityPolicySandboxingFlags(policyContainer);
        }

        if (GetRequiredContentSecurityPolicy() is { } requiredPolicy && ContentSecurityPolicy.Parse(requiredPolicy, reportOnly: false, fromMeta: false).GetDirectiveValue("sandbox") is { } requiredSandbox)
        {
            sandboxingFlags |= Sandboxing.ParseSandboxingDirective(string.Join(' ', requiredSandbox));
        }

        document.ActiveSandboxingFlags = sandboxingFlags;
        document.ScriptingEnabled = options.ScriptingEnabled && (document.ActiveSandboxingFlags & SandboxingFlags.Scripts) == SandboxingFlags.None;
        if ((document.ActiveSandboxingFlags & SandboxingFlags.Origin) != SandboxingFlags.None)
        {
            document.OriginDocument = null;
            document.OpaqueOrigin = true;
        }

        document.AboutBaseUrl = creator?.DocumentBaseUrl;

        // The policy container of the creator, and the opener policy of the top-level document of a same-origin creator
        // (https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context, steps 6 and 15)
        document.EmbedderPolicy = creator?.EmbedderPolicy ?? EmbedderPolicy.Default;
        document.DocumentIsolationPolicy = creator?.DocumentIsolationPolicy ?? EmbedderPolicyValue.UnsafeNone;
        document.IsCredentialless = IsCredentiallessNavigable;

        // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context: the permissions
        // policy of the navigable (the container policy of the iframe and the policy of its document)
        document.PermissionsPolicyRecord = PermissionsPolicyAlgorithms.CreateForNavigable(container, document.Origin);
        document.ReportOnlyPermissionsPolicyRecord = document.PermissionsPolicyRecord;
        if (parent is null && creator is not null && OriginRecord.IsSameOrigin(creator.Origin, creator.TopLevelOrigin) && creator.BrowsingContext is { } creatorBrowsingContext)
        {
            document.OpenerPolicy = creatorBrowsingContext.Top.Document.OpenerPolicy;
        }

        document.Referrer = creator?.Url ?? "";
        document.ReferrerPolicy = creator?.ReferrerPolicy ?? "";

        // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context, step 15: a clone of the
        // policy container of the creator (its CSP list applies to the initial about:blank document of an iframe or a popup)
        if (policyContainer is not null)
        {
            document.SetPolicyContainer(policyContainer);
        }

        UpgradeInsecureRequests.Inherit(document, container?.NodeDocument);

        // https://w3c.github.io/ServiceWorker/#control-and-use-window-client: the initial about:blank document is controlled by
        // the service worker of its creator when it has its origin (see InitializeServiceWorkerClient)
        if (creator is { ActiveServiceWorker: { } creatorController } && OriginRecord.IsSameOrigin(creator.Origin, document.Origin))
        {
            document.ActiveServiceWorker = creatorController;
        }

        document.InternalAncestorOriginObjects = CreateInternalAncestorOriginObjectsList(document, GetIframeElementReferrerPolicy());
        document.ReadyForPostLoadTasks = true;
        document.CustomElementRegistryStorage = new CustomElementRegistry(document);

        // Populate with html/head/body (https://html.spec.whatwg.org/multipage/document-lifecycle.html#populate-with-html/head/body)
        var html = document.CreateElementNS(Namespaces.Html, "html");
        html.AppendNode(document.CreateElementNS(Namespaces.Html, "head"));
        html.AppendNode(document.CreateElementNS(Namespaces.Html, "body"));
        document.AppendNode(html);

        // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context: the agent of the creator's origin.
        // The group of an auxiliary browsing context (the group of its opener) is set after the constructor.
        var group = parent is not null ? Group : groupOwner is not null ? creator?.BrowsingContext?.Group : null;
        var window = new Window(document, options, EventLoop) { AgentCluster = AgentClusters.Obtain(document.Origin, requestsOriginKeyed: false, group is not null && group.CrossOriginIsolationMode != CrossOriginIsolationMode.None) };
        CreateNavigationTimingEntry(document, window, DocumentLoadTimingInfo.CreateImmediate(window.TimeOriginTimestamp), bodySize: 0, status: 200, headers: []);
        Activate(document, window, NavigationHistoryBehavior.Push, srcdoc: null);

        // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context: "Completely finish
        // loading document" (the navigable has no container yet, so no load event is fired): e.g. the refresh of a meta
        // element inserted in the initial about:blank document comes due
        document.CompletelyLoaded = true;
    }

    /// <summary>Gets the options of the browsing context.</summary>
    public BrowsingContextOptions Options { get; }

    /// <summary>Gets the parent browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#nav-parent), or null for a top-level browsing context.</summary>
    public BrowsingContext? Parent { get; private set; }

    /// <summary>Gets the element that contains this browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#nav-container), or null for a top-level browsing context.</summary>
    public Element? Container { get; }

    /// <summary>The broadcast channels of the windows and the workers of the browsing context group (used by the group owner), in creation order. The list is locked: the workers use it from their threads.</summary>
    private LockManagerMap? _lockManagers;

    /// <summary>The lock managers of the browsing context group (https://w3c.github.io/web-locks/#lock-managers), shared by its windows and their workers.</summary>
    internal LockManagerMap LockManagers => ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _lockManagers) : GroupOwner.LockManagers;

    internal List<BroadcastChannel> BroadcastChannels => ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _broadcastChannels) : GroupOwner.BroadcastChannels;

    /// <summary>The blob URL store of the browsing context group (used by the group owner).</summary>
    internal BlobUrlStore BlobUrls => ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _blobUrls) : GroupOwner.BlobUrls;

    /// <summary>The agent clusters of the browsing context group (used by the group owner).</summary>
    internal AgentClusterMap AgentClusters => ReferenceEquals(GroupOwner, this) ? _agentClusters ??= new AgentClusterMap() : GroupOwner.AgentClusters;

    /// <summary>
    /// The storage shed of the local storage areas (https://storage.spec.whatwg.org/#user-agent-storage-shed):
    /// <see cref="BrowsingContextOptions.LocalStorage"/>, or the shed of the browsing context created by the application
    /// that owns the group.
    /// </summary>
    internal StorageShed LocalStorageShed => Options.LocalStorage ?? (ReferenceEquals(GroupOwner, this) ? _localStorageShed ??= CreateLocalStorageShed() : GroupOwner.LocalStorageShed);

    /// <summary>
    /// The storage of the IndexedDB databases: <see cref="BrowsingContextOptions.IndexedDB"/>, or the in-memory backend of the
    /// browsing context created by the application that owns the group.
    /// </summary>
    internal IndexedDBBackend IndexedDBBackend => Options.IndexedDB ?? (ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _indexedDBBackend, static () => new InMemoryIndexedDBBackend()) : GroupOwner.IndexedDBBackend);

    /// <summary>
    /// The storage of the caches of the Cache API: <see cref="BrowsingContextOptions.CacheStorage"/>, or the in-memory
    /// backend of the browsing context created by the application that owns the group.
    /// </summary>
    internal CacheStorageBackend CacheStorageBackend => Options.CacheStorage ?? (ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _cacheStorageBackend, static () => new InMemoryCacheStorageBackend()) : GroupOwner.CacheStorageBackend);

    /// <summary>
    /// The storage of the origin private file systems: <see cref="BrowsingContextOptions.FileSystem"/>, or the in-memory
    /// backend of the browsing context created by the application that owns the group.
    /// </summary>
    internal FileSystemBackend FileSystemBackend => Options.FileSystem ?? (ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _fileSystemBackend, static () => new InMemoryFileSystemBackend()) : GroupOwner.FileSystemBackend);

    /// <summary>The storage keys whose local storage bucket is persistent (https://storage.spec.whatwg.org/#bucket-mode), for the browsing context group.</summary>
    internal StoragePersistenceState StoragePersistence => ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _storagePersistence, static () => new StoragePersistenceState()) : GroupOwner.StoragePersistence;

    /// <summary>
    /// The storage of the service worker registrations: <see cref="BrowsingContextOptions.ServiceWorkers"/>, or the in-memory
    /// backend of the browsing context created by the application that owns the group.
    /// </summary>
    internal ServiceWorkerBackend ServiceWorkerBackend => Options.ServiceWorkers ?? (ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _serviceWorkerBackend, static () => new InMemoryServiceWorkerBackend()) : GroupOwner.ServiceWorkerBackend);

    /// <summary>The storage shed of the session storage areas of a top-level traversable (https://storage.spec.whatwg.org/#traversable-navigable-storage-shed).</summary>
    internal StorageShed SessionStorageShed => Parent is null ? _sessionStorageShed ??= new StorageShed() : Top.SessionStorageShed;

    private StorageShed CreateLocalStorageShed()
    {
        var shed = new StorageShed();
        shed.AddBrowsingContext(this);
        return shed;
    }

    /// <summary>
    /// The sandboxing flags of the documents of the navigable (https://html.spec.whatwg.org/multipage/browsers.html#determining-the-creation-sandboxing-flags):
    /// the iframe sandboxing flag set of an iframe container (its sandbox attribute) and the flags of the active document of
    /// the parent, or the popup sandboxing flag set of a top-level browsing context (the flags of the document that opened it,
    /// when they propagate to the auxiliary browsing contexts).
    /// </summary>
    /// <summary>Whether the documents of the navigable get an opaque origin (the sandboxed origin browsing context flag, e.g. an iframe sandboxed without allow-same-origin).</summary>
    internal bool HasSandboxedOriginFlag => (GetIframeSandboxingFlags() & SandboxingFlags.Origin) != SandboxingFlags.None;

    private SandboxingFlags GetIframeSandboxingFlags()
    {
        var flags = Container is HtmlIFrameElement iframe && iframe.GetAttributeValue("sandbox") is { } sandbox ? Sandboxing.ParseSandboxingDirective(sandbox) : SandboxingFlags.None;
        if (Parent is { } parent)
        {
            flags |= parent.Document.ActiveSandboxingFlags;
        }
        else
        {
            flags |= _popupSandboxingFlags;
        }

        return flags;
    }

    // The browsing context group (https://html.spec.whatwg.org/multipage/document-sequences.html#browsing-context-group): the
    // top-level browsing context created by the application owns the group, and the auxiliary browsing contexts (window.open,
    // the _blank targets) belong to its group: they share its event loop, and are destroyed with it.
    private readonly BrowsingContext? _groupOwner;
    private readonly SandboxingFlags _popupSandboxingFlags;
    private List<BrowsingContext>? _auxiliaryBrowsingContexts;

    /// <summary>The top-level browsing context created by the application that owns the browsing context group.</summary>
    internal BrowsingContext GroupOwner => Top._groupOwner ?? Top;

    /// <summary>Gets whether the browsing context is an auxiliary browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#auxiliary-browsing-context), created by <c>window.open()</c> or a <c>_blank</c> target.</summary>
    public bool IsAuxiliary => _groupOwner is not null;

    /// <summary>
    /// Gets whether the browsing context was requested as a popup by the features of <c>window.open()</c>
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#is-popup). It only changes the <c>visible</c> attribute
    /// of the <see cref="BarProp"/> objects of the windows (e.g. <c>window.menubar.visible</c>).
    /// </summary>
    public bool IsPopup { get; internal set; }

    /// <summary>The smallest width and height of the viewport of a popup, in CSS pixels (as in the browsers).</summary>
    private const int MinimumPopupViewportSize = 100;

    /// <summary>
    /// The size of the viewport and the position on the screen of the client window of a top-level browsing context created
    /// by <c>window.open()</c> with the <c>width</c>, <c>height</c>, <c>left</c> and <c>top</c> features, or null: the
    /// viewport of the options, at the origin of the screen.
    /// </summary>
    internal int? ClientWindowViewportWidth { get; private set; }

    internal int? ClientWindowViewportHeight { get; private set; }

    internal int ClientWindowLeft { get; private set; }

    internal int ClientWindowTop { get; private set; }

    /// <summary>
    /// https://drafts.csswg.org/cssom-view/#set-up-browsing-context-features: sizes and moves the client window of a new
    /// top-level browsing context. Nothing is displayed: the screen is the viewport of the options, and the client window
    /// has no decoration. The values are clamped so that the viewport is not smaller than the one of the popups of the
    /// browsers (100 CSS pixels) and the position is not negative; the window is not kept inside the screen.
    /// </summary>
    internal void SetUpFeatures(Dictionary<string, string> tokenizedFeatures)
    {
        if (tokenizedFeatures.TryGetValue("left", out var left))
        {
            ClientWindowLeft = Math.Max(0, WindowFeatures.ParseInteger(left));
        }

        if (tokenizedFeatures.TryGetValue("top", out var top))
        {
            ClientWindowTop = Math.Max(0, WindowFeatures.ParseInteger(top));
        }

        // A width or a height of 0 (also an error) keeps the default size
        if (tokenizedFeatures.TryGetValue("width", out var width) && WindowFeatures.ParseInteger(width) is not 0 and var parsedWidth)
        {
            ClientWindowViewportWidth = Math.Max(MinimumPopupViewportSize, parsedWidth);
        }

        if (tokenizedFeatures.TryGetValue("height", out var height) && WindowFeatures.ParseInteger(height) is not 0 and var parsedHeight)
        {
            ClientWindowViewportHeight = Math.Max(MinimumPopupViewportSize, parsedHeight);
        }
    }

    /// <summary>
    /// The size of the viewport while the documents of the top-level browsing context are displayed fullscreen (the answer of
    /// <see cref="BrowsingContextOptions.EnterFullscreen"/>), or null: the viewport of the options.
    /// </summary>
    internal FullscreenViewportSize? FullscreenViewportSize { get; private set; }

    private bool _isFullscreen;

    /// <summary>The timestamp of the first keydown event of the Escape key that is held while the keyboard lock of the fullscreen element is active, or null.</summary>
    internal long? EscapeKeyHeldSince { get; set; }

    /// <summary>
    /// https://fullscreen.spec.whatwg.org/#dom-element-requestfullscreen: "resize pendingDoc's node navigable's top-level
    /// traversable's active document's viewport's dimensions". Nothing is displayed: the host is asked for the size of the
    /// screen (<see cref="BrowsingContextOptions.EnterFullscreen"/>), and the viewport keeps its size by default.
    /// </summary>
    internal void EnterFullscreen(FullscreenOptions options)
    {
        if (_isFullscreen || IsDestroyed)
            return;

        _isFullscreen = true;
        SetFullscreenViewportSize(Options.EnterFullscreen?.Invoke(this, options));
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#exit-fullscreen: "resize doc's viewport to its "normal" dimensions".</summary>
    internal void ExitFullscreen()
    {
        if (!_isFullscreen)
            return;

        _isFullscreen = false;
        if (!IsDestroyed)
        {
            Options.ExitFullscreen?.Invoke(this);
        }

        SetFullscreenViewportSize(null);
    }

    private ViewportOptions? _viewport;

    /// <summary>
    /// Gets or sets the virtual viewport of the top-level browsing context and the preferences of the user: the size of the
    /// viewport, the device pixel ratio, the media type and the user preference media features (e.g.
    /// <c>prefers-color-scheme</c>). It is <see cref="BrowsingContextOptions.Viewport"/> until it is set (e.g. as
    /// <c>page.setViewportSize()</c> and <c>page.emulateMedia()</c> of Playwright). A new value applies to the documents of the
    /// browsing context and of its nested browsing contexts: the media queries are evaluated again, the documents are laid
    /// out again, and the <c>resize</c> event is fired at the next rendering opportunity.
    /// </summary>
    /// <exception cref="InvalidOperationException">The browsing context is a nested browsing context: its viewport is the content box of its container.</exception>
    public ViewportOptions Viewport
    {
        get
        {
            var top = Top;
            return top._viewport ?? top.Options.Viewport ?? ViewportOptions.Default;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Parent is not null)
                throw new InvalidOperationException("The viewport of a nested browsing context is the content box of its container");

            if (value == Viewport)
                return;

            // The environment of the documents is evaluated again (see StyleEngine.CreateEnvironment), and the resize steps of the
            // next rendering update fire the resize event
            _viewport = value;
            if (!IsDestroyed)
            {
                foreach (var navigable in GetInclusiveDescendantNavigables())
                {
                    navigable.Document.InvalidateLayout();
                }

                EventLoop.RequestRenderingOpportunity(Document);
            }
        }
    }

    private bool _isOffline;

    /// <summary>
    /// Gets or sets whether the user agent is offline (as the <c>offline</c> option of Playwright): <c>navigator.onLine</c>
    /// returns false, and the <c>offline</c> event (or <c>online</c> when it changes back) is fired at the windows of the
    /// browsing context and of its nested browsing contexts. The requests are not blocked: a <see cref="DocumentLoader"/> fails
    /// them (returns null). The value is the one of the top-level browsing context.
    /// </summary>
    public bool IsOffline
    {
        get => Top._isOffline;
        set
        {
            var top = Top;
            if (top._isOffline == value)
                return;

            top._isOffline = value;
            if (top.IsDestroyed)
                return;

            // https://html.spec.whatwg.org/multipage/system-state.html#navigator.online: "the user agent must queue a global task on
            // the networking task source given the global object to fire an event named online or offline"
            var type = value ? "offline" : "online";
            foreach (var navigable in top.GetInclusiveDescendantNavigables())
            {
                if (navigable.Document.RelevantWindow is { } window)
                {
                    navigable.Document.QueueGlobalTask(() => window.FireEvent(new Event(type)));
                }
            }
        }
    }

    private void SetFullscreenViewportSize(FullscreenViewportSize? size)
    {
        if (FullscreenViewportSize == size)
            return;

        // The environment of the documents is evaluated again (see StyleEngine.CreateEnvironment), and the resize steps of the
        // next rendering update fire the resize event
        FullscreenViewportSize = size;
        if (!IsDestroyed)
        {
            Document.InvalidateLayout();
            EventLoop.RequestRenderingOpportunity(Document);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#one-permitted-sandboxed-navigator: the sandboxed navigable that opened this popup, or null.</summary>
    internal BrowsingContext? OnePermittedSandboxedNavigator { get; private set; }

    /// <summary>Gets the opener browsing context of an auxiliary browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#opener-browsing-context), or null.</summary>
    public BrowsingContext? Opener { get; internal set; }

    /// <summary>
    /// Gets the browsing context whose document created this auxiliary browsing context (<c>window.open()</c>, a link or a form
    /// with a <c>_blank</c> target), even when it has no opener (<c>noopener</c>, the default of the <c>_blank</c> links): the
    /// tab that opened the popup, as the browsers show it. It is null for the browsing contexts created by the application.
    /// </summary>
    public BrowsingContext? CreatorBrowsingContext { get; private set; }

    /// <summary>The descendant navigables of the navigable, in tree order (https://html.spec.whatwg.org/multipage/document-sequences.html#descendant-navigables).</summary>
    internal IEnumerable<BrowsingContext> GetDescendantNavigables()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var descendant in child.GetDescendantNavigables())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>The navigable and its descendant navigables, in tree order.</summary>
    internal IEnumerable<BrowsingContext> GetInclusiveDescendantNavigables()
    {
        yield return this;
        foreach (var descendant in GetDescendantNavigables())
        {
            yield return descendant;
        }
    }

    /// <summary>The top-level browsing contexts of the group: the group owner and its auxiliary browsing contexts.</summary>
    internal IEnumerable<BrowsingContext> GetGroupTopLevelBrowsingContexts()
    {
        var owner = GroupOwner;
        yield return owner;
        if (owner._auxiliaryBrowsingContexts is { } auxiliaries)
        {
            foreach (var auxiliary in auxiliaries.ToArray())
            {
                yield return auxiliary;
            }
        }
    }

    /// <summary>
    /// Creates an auxiliary browsing context in the group of this browsing context
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-auxiliary-browsing-context): its
    /// initial about:blank document has the origin of the opener, or an opaque origin without opener.
    /// </summary>
    internal BrowsingContext CreateAuxiliaryBrowsingContext(string name, Document? opener, SandboxingFlags popupSandboxingFlags = SandboxingFlags.None, BrowsingContext? creator = null)
    {
        var owner = GroupOwner;
        var context = new BrowsingContext(Options, parent: null, container: null, owner, opener, name, popupSandboxingFlags)
        {
            Opener = opener?.BrowsingContext,
            CreatorBrowsingContext = creator ?? opener?.BrowsingContext,

            // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-auxiliary-browsing-context: the
            // group of the opener (a browsing context without opener is in a new group)
            _group = opener is null ? new BrowsingContextGroup() : Group,
        };
        (owner._auxiliaryBrowsingContexts ??= []).Add(context);

        // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-top-level-traversable: the session
        // storage of the top-level traversable of the opener is copied (legacy-clone a traversable storage shed)
        if (opener?.BrowsingContext is { } openerNavigable)
        {
            openerNavigable.Top.SessionStorageShed.CopyTo(context.SessionStorageShed);
        }

        Options.BrowsingContextCreated?.Invoke(context);
        return context;
    }

    /// <summary>The number of ancestors of the navigable.</summary>
    private int GetNestingDepth()
    {
        var depth = 0;
        for (var current = Parent; current is not null; current = current.Parent)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// Whether the navigation of a child navigable to <paramref name="url"/> would nest a document in itself more than once.
    /// The specification only prevents an iframe from loading the URL of an ancestor through its src attribute, but the
    /// browsers allow one level of self-reference and block the next one for any navigation started by a document (e.g. an
    /// about:blank iframe that a document navigates to a relative URL, such as "#", resolved against its own URL).
    /// </summary>
    private bool IsSelfReferentialNavigation(UrlRecord url, Document? sourceDocument, PostResource? postResource)
    {
        // As in the browsers, the about: URLs cannot nest endlessly, the frames loaded with POST requests are exempted, and
        // so are the navigations started by the user agent
        if (Parent is null || sourceDocument is null || postResource is not null || url.Scheme == "about")
            return false;

        return IsUrlRepeatedInInclusiveAncestors(Parent, url);
    }

    /// <summary>
    /// Whether more than one of the inclusive ancestor navigables of <paramref name="navigable"/> display
    /// <paramref name="url"/>, ignoring the fragments. A child navigable of <paramref name="navigable"/> that loads
    /// <paramref name="url"/> would nest the document in itself more than once, which the browsers block.
    /// </summary>
    internal static bool IsUrlRepeatedInInclusiveAncestors(BrowsingContext? navigable, UrlRecord url)
    {
        var foundSelfReference = false;
        for (var ancestor = navigable; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (UrlEquals(ancestor.Document.UrlRecord, url, excludeFragments: true))
            {
                if (foundSelfReference)
                    return true;

                foundSelfReference = true;
            }
        }

        return false;
    }

    /// <summary>Gets the top-level browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#nav-top).</summary>
    public BrowsingContext Top
    {
        get
        {
            var result = this;
            while (result.Parent is not null)
            {
                result = result.Parent;
            }

            return result;
        }
    }

    private UserInput? _input;

    /// <summary>
    /// Gets the input devices of the user of the top-level browsing context: the entry point of a host application that
    /// injects the pointer and wheel input of the user (see <see cref="UserInput"/>). The nested browsing contexts share
    /// the input devices of their top-level browsing context.
    /// </summary>
    public UserInput Input
    {
        get
        {
            var top = Top;
            return top._input ??= new UserInput(top);
        }
    }

    /// <summary>The input devices of the top-level browsing context once they were used, else null.</summary>
    internal UserInput? InputOrNull => _input;

    /// <summary>Gets the active document (https://html.spec.whatwg.org/multipage/document-sequences.html#nav-document).</summary>
    public Document Document { get; private set; } = null!;

    /// <summary>Gets the active window (https://html.spec.whatwg.org/multipage/document-sequences.html#nav-window).</summary>
    public Window Window => Document.RelevantWindow!;

    /// <summary>Gets or sets the target name (https://html.spec.whatwg.org/multipage/document-sequences.html#nav-target).</summary>
    public string Name
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>Gets the browsing contexts of the <c>iframe</c> and <c>frame</c> elements of the active document, in tree order (https://html.spec.whatwg.org/multipage/document-sequences.html#document-tree-child-navigables).</summary>
    public IReadOnlyList<BrowsingContext> Children => Document.GetDocumentTreeChildNavigables();

    /// <summary>Gets whether the browsing context was destroyed (e.g. its <c>iframe</c> was removed from the document).</summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#is-closing</summary>
    internal bool IsClosing { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#is-delaying-load-events</summary>
    internal bool IsDelayingLoadEvents { get; private set; }

    /// <summary>
    /// Gets or sets whether the top-level browsing context is visible to the user: its system visibility state
    /// (https://html.spec.whatwg.org/multipage/interaction.html#system-visibility-state). The host sets it to false when its
    /// window is minimized, when its tab is in the background or when another window covers it: the visibility state of the
    /// documents of the top-level browsing context and of its nested browsing contexts (<c>document.visibilityState</c>,
    /// <c>document.hidden</c>) is then updated from tasks of the event loop, which fire <c>visibilitychange</c>. The nested
    /// browsing contexts share the state of their top-level browsing context. The default is true. A prerendering traversable
    /// is never visible (https://wicg.github.io/nav-speculation/prerendering.html#prerendering-traversable): its documents get
    /// the state of the navigable that activates them.
    /// </summary>
    public bool IsVisible
    {
        get => Top is { _isVisible: true, IsPrerendering: false };
        set
        {
            var top = Top;
            if (top._isVisible == value)
                return;

            top._isVisible = value;
            if (top.IsPrerendering)
                return;

            if (!value)
            {
                // https://w3c.github.io/pointerlock/#requirements: "Pointer lock must be exited if [...] the user agent,
                // window, or tab loses focus"
                PointerLockModel.ExitByUserAgent(top);
            }

            foreach (var navigable in top.GetDescendantNavigables().Prepend(top))
            {
                // Queue a global task on the user interaction task source to update the visibility state of the document
                var document = navigable.Document;
                document.RelevantWindow?.QueueGlobalTask(() =>
                {
                    if (ReferenceEquals(navigable.Document, document) && !document.IsUnloaded)
                    {
                        document.UpdateVisibilityState(value);
                    }
                });
            }
        }
    }

    private bool _isVisible = true;

    /// <summary>
    /// Gets or sets whether the top-level browsing context has the system focus
    /// (https://html.spec.whatwg.org/multipage/interaction.html#tlbc-system-focus): whether its window is the active window of
    /// the user. The host sets it when its window is activated or deactivated: the focused element and the window of its
    /// document get <c>blur</c> when the focus is lost and <c>focus</c> when it comes back, <c>document.hasFocus()</c> is false
    /// meanwhile, and the scripts still move the focus, without focus events. One top-level browsing context of a group has
    /// the system focus at a time: a new auxiliary browsing context (<c>window.open()</c>) takes it, and gives it back to its
    /// opener when it is closed. The nested browsing contexts share the state of their top-level browsing context. The
    /// default is true. A prerendering traversable never has the system focus
    /// (https://wicg.github.io/nav-speculation/prerendering.html#interaction-with-system-focus).
    /// </summary>
    public bool HasSystemFocus
    {
        get => Top._hasSystemFocus;
        set => Top.SetSystemFocus(value);
    }

    private bool _hasSystemFocus = true;

    private void SetSystemFocus(bool value)
    {
        if (_hasSystemFocus == value || IsDestroyed || IsPrerendering)
            return;

        if (value)
        {
            // The window of another top-level browsing context of the group is no longer the active window
            foreach (var other in GetGroupTopLevelBrowsingContexts())
            {
                if (!ReferenceEquals(other, this))
                {
                    other.SetSystemFocus(false);
                }
            }
        }

        _hasSystemFocus = value;

        // The caret is only painted in a window that has the system focus
        var focusedArea = FocusModel.GetCurrentlyFocusedArea(Document);
        (focusedArea as Document ?? focusedArea.NodeDocument).OnCaretChanged();
        if (!value)
        {
            // https://w3c.github.io/pointerlock/#requirements: the pointer lock is exited when the window loses the focus
            PointerLockModel.ExitByUserAgent(this);
        }

        FocusModel.OnSystemFocusChanged(Document, gained: value);
    }

    /// <summary>
    /// Gets the event loop of the browsing context group: each top-level browsing context creates its own event loop, and
    /// the nested browsing contexts share the event loop of their top-level browsing context. The tasks of the documents
    /// (e.g. the <c>load</c> events, the timers and the scripts) are queued in it.
    /// </summary>
    public DomEventLoop EventLoop { get; }

    internal void QueueTask(Action task) => EventLoop.QueueTask(task);

    /// <summary>
    /// Navigates the browsing context to <paramref name="url"/> (resolved against the URL of the active document). The
    /// document is created by a task of the <see cref="DomEventLoop"/>, from the resource returned by
    /// <see cref="BrowsingContextOptions.DocumentLoader"/>. A navigation to a fragment of the active document is synchronous.
    /// </summary>
    public void Navigate(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var urlRecord = UrlParser.Parse(url, Document.DocumentBaseUrl, Document.Encoding) ?? throw new ArgumentException($"'{url}' is not a valid URL", nameof(url));
        Navigate(urlRecord, sourceDocument: null, NavigationHistoryBehavior.Auto, userInvolvement: UserNavigationInvolvement.BrowserUI);
    }

    /// <summary>
    /// Navigates the browsing context to <c>about:blank</c>, and loads the HTML document returned by <paramref name="html"/>
    /// (see <see cref="Navigate(string, Func{Url, string})"/>).
    /// </summary>
    /// <param name="html">Returns the HTML content of the document, given its URL (<c>about:blank</c>).</param>
    /// <returns>The new active document.</returns>
    public Document Navigate(Func<Url, string> html) => Navigate(AboutBlank, html);

    /// <summary>
    /// Navigates the browsing context to <c>about:blank</c>, and loads the document returned by <paramref name="resource"/>
    /// (see <see cref="Navigate(string, Func{Url, DocumentResource})"/>).
    /// </summary>
    /// <param name="resource">Returns the resource of the document, given its URL (<c>about:blank</c>), or null for a network error.</param>
    /// <returns>The new active document.</returns>
    public Document Navigate(Func<Url, DocumentResource?> resource) => Navigate(AboutBlank, resource);

    /// <summary>
    /// Navigates the browsing context to <paramref name="url"/> (resolved against the URL of the active document), and
    /// loads the HTML document returned by <paramref name="html"/>. The document is created synchronously and becomes the
    /// active document: when the method returns, its <c>readyState</c> is <c>interactive</c>, and the
    /// <c>DOMContentLoaded</c> and <c>load</c> events are fired by the tasks of the <see cref="DomEventLoop"/> (see
    /// <see cref="DomEventLoop.RunUntilIdle"/>). <see cref="BrowsingContextOptions.DocumentLoader"/> is not used.
    /// </summary>
    /// <param name="url">The URL of the document, resolved against the URL of the active document.</param>
    /// <param name="html">Returns the HTML content of the document, given the resolved URL.</param>
    /// <returns>The new active document.</returns>
    /// <remarks>
    /// When a script engine runs a parser-blocking script that is not available yet (the
    /// <see cref="BrowsingContextOptions.DocumentLoader"/> completes asynchronously), the method returns once the parser
    /// pauses: the rest of the document is parsed by the tasks of the <see cref="DomEventLoop"/>, or use
    /// <see cref="NavigateAsync(string, Func{Url, string}, CancellationToken)"/>.
    /// </remarks>
    public Document Navigate(string url, Func<Url, string> html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return Navigate(url, resolvedUrl => new DocumentResource(html(resolvedUrl) ?? throw new InvalidOperationException("The HTML content of the document is null")));
    }

    /// <summary>
    /// Navigates the browsing context to <paramref name="url"/> (resolved against the URL of the active document), and
    /// loads the document returned by <paramref name="resource"/>, as if it was returned by
    /// <see cref="BrowsingContextOptions.DocumentLoader"/>: HTML, XML or text, from its decoded content or its bytes (see
    /// <see cref="DocumentResource"/>). The document is created synchronously and becomes the active document (see
    /// <see cref="Navigate(string, Func{Url, string})"/>).
    /// </summary>
    /// <param name="url">The URL of the document, resolved against the URL of the active document.</param>
    /// <param name="resource">
    /// Returns the resource of the document, given the resolved URL. When it returns null, the document is an error document
    /// whose origin is opaque, as for a network error. A response whose status is 204 or 205, or whose
    /// <c>Content-Disposition</c> is <c>attachment</c>, does not create a document: the active document stays.
    /// </param>
    /// <returns>The new active document.</returns>
    public Document Navigate(string url, Func<Url, DocumentResource?> resource)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(resource);
        if (IsDestroyed)
            throw new InvalidOperationException("The browsing context is destroyed");

        var urlRecord = UrlParser.Parse(url, Document.DocumentBaseUrl, Document.Encoding) ?? throw new ArgumentException($"'{url}' is not a valid URL", nameof(url));
        if (urlRecord.Scheme == "javascript")
            throw new ArgumentException("A javascript: URL cannot be navigated to with a document", nameof(url));

        // https://wicg.github.io/scroll-to-text-fragment/#fragment-directive: the fragment directive is removed from the URL
        var fragmentDirective = AreTextDirectivesEnabled ? TextDirectives.RemoveFragmentDirective(urlRecord) : null;

        // The timing of the navigation: the resource is requested when the navigation starts
        var timing = new DocumentLoadTimingInfo { NavigationStart = EventLoop.TimeProvider.GetTimestamp() };
        timing.FetchStart = EventLoop.TimeProvider.GetTimestamp();
        var documentResource = resource(new Url(urlRecord));
        if (IsDestroyed)
            throw new InvalidOperationException("The browsing context is destroyed");

        // The new navigation aborts the ongoing one. The history handling is the one of an "auto" navigation.
        CancelOngoingNavigation();
        var activeDocument = Document;
        var parameters = new DocumentParameters
        {
            HistoryHandling = activeDocument.IsInitialAboutBlank || UrlEquals(urlRecord, activeDocument.UrlRecord, excludeFragments: false)
                ? NavigationHistoryBehavior.Replace
                : NavigationHistoryBehavior.Push,
            Timing = timing,

            // The application navigates the browsing context, as the user of a browser does with its user interface
            FragmentDirective = fragmentDirective,
            UserInvolvement = UserNavigationInvolvement.BrowserUI,
        };

        if (documentResource is null)
        {
            // A network error: an error document, whose origin is opaque
            documentResource = new DocumentResource("");
            parameters.OpaqueOrigin = true;
        }
        else if (HasNoDocument(documentResource))
        {
            return Document;
        }
        else if (documentResource.Url is not null)
        {
            urlRecord = UrlParser.Parse(documentResource.Url, baseUrl: null) ?? urlRecord;
        }

        if (!parameters.OpaqueOrigin && !ApplyOpenerPolicy(urlRecord, parameters, documentResource.Headers))
        {
            // A network error: a sandboxed document cannot have an opener policy
            documentResource = new DocumentResource("");
            parameters.OpaqueOrigin = true;
        }

        ApplyNavigationResponseClientHints(urlRecord, documentResource.Headers);
        return LoadDocument(urlRecord, parameters, documentResource);
    }

    /// <summary>
    /// Navigates the browsing context to <paramref name="url"/> and loads the HTML document returned by
    /// <paramref name="html"/> (see <see cref="Navigate(string, Func{Url, string})"/>), and waits until the parser has
    /// finished: while the parser waits for a script, the tasks of the <see cref="EventLoop"/> run.
    /// </summary>
    /// <param name="url">The URL of the document, resolved against the URL of the active document.</param>
    /// <param name="html">Returns the HTML content of the document, given the resolved URL.</param>
    /// <param name="cancellationToken">A token to stop waiting for the parser.</param>
    /// <returns>The new active document.</returns>
    public Task<Document> NavigateAsync(string url, Func<Url, string> html, CancellationToken cancellationToken = default)
    {
        return WaitForParserAsync(Navigate(url, html), cancellationToken);
    }

    /// <summary>
    /// Navigates the browsing context to <paramref name="url"/> and loads the document returned by
    /// <paramref name="resource"/> (see <see cref="Navigate(string, Func{Url, DocumentResource})"/>), and waits until the
    /// parser has finished: while the parser waits for a script, the tasks of the <see cref="EventLoop"/> run.
    /// </summary>
    /// <param name="url">The URL of the document, resolved against the URL of the active document.</param>
    /// <param name="resource">Returns the resource of the document, given the resolved URL (null for a network error).</param>
    /// <param name="cancellationToken">A token to stop waiting for the parser.</param>
    /// <returns>The new active document.</returns>
    public Task<Document> NavigateAsync(string url, Func<Url, DocumentResource?> resource, CancellationToken cancellationToken = default)
    {
        return WaitForParserAsync(Navigate(url, resource), cancellationToken);
    }

    /// <summary>
    /// Navigates the browsing context to <c>about:blank</c>, loads the HTML document returned by <paramref name="html"/>,
    /// and waits until the parser has finished (see <see cref="NavigateAsync(string, Func{Url, string}, CancellationToken)"/>).
    /// </summary>
    /// <param name="html">Returns the HTML content of the document, given its URL (<c>about:blank</c>).</param>
    /// <param name="cancellationToken">A token to stop waiting for the parser.</param>
    /// <returns>The new active document.</returns>
    public Task<Document> NavigateAsync(Func<Url, string> html, CancellationToken cancellationToken = default) => NavigateAsync(AboutBlank, html, cancellationToken);

    /// <summary>
    /// Navigates the browsing context to <c>about:blank</c>, loads the document returned by <paramref name="resource"/>,
    /// and waits until the parser has finished (see <see cref="NavigateAsync(string, Func{Url, DocumentResource}, CancellationToken)"/>).
    /// </summary>
    /// <param name="resource">Returns the resource of the document, given its URL (<c>about:blank</c>), or null for a network error.</param>
    /// <param name="cancellationToken">A token to stop waiting for the parser.</param>
    /// <returns>The new active document.</returns>
    public Task<Document> NavigateAsync(Func<Url, DocumentResource?> resource, CancellationToken cancellationToken = default) => NavigateAsync(AboutBlank, resource, cancellationToken);

    private static async Task<Document> WaitForParserAsync(Document document, CancellationToken cancellationToken)
    {
        await HtmlDocumentParser.WaitForParserAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#the-rules-for-choosing-a-navigable: returns the navigable
    /// that a hyperlink, a form or <c>window.open()</c> targeting <paramref name="name"/> navigates. For <c>_blank</c> and the
    /// names that are not found, a new auxiliary browsing context is created (with this browsing context as opener, unless
    /// <paramref name="noopener"/> is true).
    /// </summary>
    internal BrowsingContext? ChooseNavigable(string name, bool noopener, out WindowType windowType)
    {
        windowType = WindowType.ExistingOrNone;
        if (name.Length == 0 || Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "_self"))
            return this;

        if (Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "_parent"))
            return Parent ?? this;

        if (Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "_top"))
            return Top;

        if (!Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "_blank") && FindNavigableByName(name) is { } existing)
            return existing;

        if (IsDestroyed)
            return null;

        // A new top-level traversable is requested: a document sandboxed without allow-popups cannot create it
        var currentDocument = Document;
        if ((currentDocument.ActiveSandboxingFlags & SandboxingFlags.AuxiliaryNavigation) != SandboxingFlags.None)
            return null;

        // https://html.spec.whatwg.org/multipage/document-sequences.html#the-rules-for-choosing-a-navigable, step 8.2.1: a new
        // top-level traversable consumes the user activation of the window
        currentDocument.RelevantWindow?.ConsumeUserActivation();

        // In the presence of an opener policy, the nested documents that are cross-origin with their top-level document open
        // their popups without opener. The opener policy is the one of the top-level document: the nested documents have none.
        // The popups of the credentialless iframes have no opener (https://wicg.github.io/anonymous-iframe/#spec-window-open).
        if ((Top.Document.OpenerPolicy.Value is OpenerPolicyValue.SameOrigin or OpenerPolicyValue.SameOriginPlusCoep && !OriginRecord.IsSameOrigin(currentDocument.Origin, currentDocument.TopLevelOrigin))
            || currentDocument.IsCredentialless)
        {
            noopener = true;
            name = "_blank";
        }

        // A new top-level traversable, whose target name is the name (except _blank)
        windowType = noopener ? WindowType.NewWithNoOpener : WindowType.NewAndUnrestricted;
        var targetName = Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "_blank") ? "" : name;
        // https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-auxiliary-browsing-context: the sandboxing
        // flags of the document propagate to the popup, unless it uses allow-popups-to-escape-sandbox
        var popupSandboxingFlags = (currentDocument.ActiveSandboxingFlags & SandboxingFlags.PropagatesToAuxiliaryBrowsingContexts) != SandboxingFlags.None ? currentDocument.ActiveSandboxingFlags : SandboxingFlags.None;
        var popup = CreateAuxiliaryBrowsingContext(targetName, noopener ? null : Document, popupSandboxingFlags, creator: this);
        popup._isNewTopLevelTraversable = true;

        // The window of the new top-level browsing context is the active window of the user: it takes the system focus from
        // the window of the group that has it (from a task: the focus events are not fired while the script that opens it runs)
        popup._hasSystemFocus = false;
        popup.QueueTask(() =>
        {
            if (popup.GetGroupTopLevelBrowsingContexts().Any(static context => context._hasSystemFocus))
            {
                popup.SetSystemFocus(true);
            }
        });

        // https://html.spec.whatwg.org/multipage/document-sequences.html#the-rules-for-choosing-a-navigable, step 8.2.9: a
        // sandboxed document can navigate the popups it opens
        if ((currentDocument.ActiveSandboxingFlags & SandboxingFlags.Navigation) != SandboxingFlags.None)
        {
            popup.OnePermittedSandboxedNavigator = this;
        }

        return popup;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#find-a-navigable-by-target-name</summary>
    private BrowsingContext? FindNavigableByName(string name)
    {
        // The subtrees to search are « traversable, this navigable » (the first implementation-defined choice, as Gecko and
        // Blink), searched in reverse order: the descendants of this navigable first, then the other navigables of its page
        if (Find(this, name) is { } result)
            return result;

        if (!ReferenceEquals(Top, this) && Find(Top, name) is { } inPage)
            return inPage;

        // The other top-level browsing contexts of the group, whose navigables this browsing context is familiar with
        foreach (var topLevel in GetGroupTopLevelBrowsingContexts())
        {
            if (ReferenceEquals(topLevel, Top) || topLevel.IsDestroyed || topLevel.IsClosing || IsInOtherGroup(topLevel))
                continue;

            foreach (var navigable in topLevel.GetInclusiveDescendantNavigables())
            {
                if (navigable.Name == name && IsFamiliarWith(navigable))
                    return navigable;
            }
        }

        return null;

        static BrowsingContext? Find(BrowsingContext navigable, string name)
        {
            foreach (var candidate in navigable.GetInclusiveDescendantNavigables())
            {
                if (candidate.Name == name)
                    return candidate;
            }

            return null;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#familiar-with</summary>
    internal bool IsFamiliarWith(BrowsingContext other)
    {
        var origin = Document.Origin;
        if (OriginRecord.IsSameOrigin(origin, other.Document.Origin))
            return true;

        if (ReferenceEquals(Top, other))
            return true;

        if (other.Parent is null && other.IsAuxiliary && other.Opener is { IsDestroyed: false } opener && !ReferenceEquals(opener, other) && IsFamiliarWith(opener))
            return true;

        for (var ancestor = other.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (OriginRecord.IsSameOrigin(origin, ancestor.Document.Origin))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a document of <paramref name="sourceNavigable"/> can navigate this navigable of its frame tree, beyond the
    /// sandboxing flags. As in the browsers:
    /// <list type="bullet">
    /// <item>A document can navigate its top-level traversable only when it is same origin with the top-level document, or
    /// with transient activation (the framebusting intervention, WPT <c>cross-origin-top-navigation-without-user-activation.window.js</c>).
    /// A sandboxed document allowed to navigate it (<c>allow-top-navigation</c>) can when its parent can. As in Safari, the
    /// navigations to a URL that is same site with the top-level document are allowed (WPT <c>location-replace-cross-site.html</c>).</item>
    /// <item>A document can navigate another navigable of the tree only when it is same origin with the document of the
    /// navigable or of one of its ancestors (the "familiar with" relationship, WPT <c>child-navigates-parent-cross-origin.window.js</c>).</item>
    /// </list>
    /// The navigables it contains, and the navigables of other trees (popups, openers), are not restricted here.
    /// </summary>
    private bool IsAllowedToBeNavigatedBy(BrowsingContext sourceNavigable, Document sourceDocument, bool hasTransientActivation, UrlRecord url)
    {
        if (!ReferenceEquals(sourceNavigable.Top, Top))
            return true;

        for (BrowsingContext? current = this; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, sourceNavigable))
                return true;
        }

        if (Parent is null)
            return hasTransientActivation || OriginRecord.IsSameSite(OriginRecord.FromUrl(url), Document.Origin) || IsFramebustingAllowed(sourceNavigable, sourceDocument, Document.Origin);

        for (BrowsingContext? current = this; current is not null; current = current.Parent)
        {
            if (OriginRecord.IsSameOrigin(sourceDocument.Origin, current.Document.Origin))
                return true;
        }

        return false;

        // As in Firefox: a document that is same origin with the top-level document, or a sandboxed document allowed to
        // navigate the top-level traversable (allow-top-navigation) whose parent can, can navigate it without activation
        static bool IsFramebustingAllowed(BrowsingContext navigable, Document document, OriginRecord topOrigin)
        {
            while (navigable.Parent is { } parent)
            {
                if (OriginRecord.IsSameOrigin(document.Origin, topOrigin))
                    return true;

                var flags = document.ActiveSandboxingFlags;
                if (flags == SandboxingFlags.None || (flags & SandboxingFlags.TopLevelNavigationWithoutUserActivation) != SandboxingFlags.None)
                    return false;

                navigable = parent;
                document = parent.Document;
            }

            return true;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate</summary>
    /// <param name="url">The URL to navigate to.</param>
    /// <param name="sourceDocument">The document that initiates the navigation, whose URL is the referrer.</param>
    /// <param name="historyHandling">The history handling.</param>
    /// <param name="srcdoc">The content of the iframe srcdoc document, or null.</param>
    /// <param name="initialInsertion">Whether the navigation is the one of the insertion of an iframe.</param>
    /// <param name="referrerPolicy">The referrer policy of the request (e.g. the <c>referrerpolicy</c> attribute of the iframe), or the empty string.</param>
    /// <param name="postResource">The body of a POST request (the submission of a form), or null for a GET request.</param>
    /// <param name="traversalEntry">The session history entry whose document a history traversal (or a reload) loads again, or null.</param>
    /// <param name="exceptionsEnabled">Whether a navigation that the source document is not allowed to perform throws a "SecurityError" DOMException (e.g. a Location navigation).</param>
    /// <param name="hasTransientActivation">Whether the window of the source document has transient activation (the source snapshot params), or null to get it now. <c>window.open()</c> gets it before the creation of the popup consumes it.</param>
    /// <param name="navigationApiState">The navigation API state of the new entry (<c>navigation.navigate()</c>), or null.</param>
    /// <param name="apiMethodTracker">The navigation API method tracker of <c>navigation.navigate()</c>, or null.</param>
    /// <param name="userInvolvement">The user navigation involvement (e.g. a hyperlink activated by a trusted event).</param>
    /// <param name="sourceElement">The element responsible for the navigation (a hyperlink, a submit button or a form), or null.</param>
    /// <param name="formData">The entry list of the submitted form of a form submission (the navigate event gets the one of a POST navigation), or null.</param>
    /// <param name="response">The response of the navigation, already fetched by the container (the <c>embed</c> and <c>object</c> elements), or null to fetch it.</param>
    internal void Navigate(UrlRecord url, Document? sourceDocument, NavigationHistoryBehavior historyHandling, string? srcdoc = null, bool initialInsertion = false, string referrerPolicy = "", PostResource? postResource = null, SessionHistoryEntry? traversalEntry = null, bool exceptionsEnabled = false, bool? hasTransientActivation = null, NavigationApiStateValue? navigationApiState = null, NavigationApiMethodTracker? apiMethodTracker = null, UserNavigationInvolvement userInvolvement = UserNavigationInvolvement.None, Element? sourceElement = null, FormData? formData = null, DocumentResource? response = null, bool checkNavigationRateLimit = false)
    {
        if (IsDestroyed)
            return;

        // As in the browsers, the navigables nested too deeply do not navigate (e.g. a document that loads itself in an
        // iframe with a relative URL would nest documents endlessly)
        if (GetNestingDepth() >= MaxNestingDepth && traversalEntry is null)
            return;

        if (traversalEntry is null && IsSelfReferentialNavigation(url, sourceDocument, postResource))
            return;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#snapshotting-source-snapshot-params
        var sourceHasTransientActivation = hasTransientActivation ?? sourceDocument?.RelevantWindow?.HasTransientActivation ?? false;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, step 5: the sandboxing flags of the source document
        if (traversalEntry is null && sourceDocument?.BrowsingContext is { } sourceNavigable
            && !Sandboxing.IsAllowedToNavigate(sourceNavigable, this, sourceDocument.ActiveSandboxingFlags, sourceHasTransientActivation))
        {
            if (exceptionsEnabled)
                throw new DomException("The document is not allowed to navigate this browsing context", "SecurityError");

            return;
        }

        if (traversalEntry is null && sourceDocument?.BrowsingContext is { } initiatorNavigable && !IsAllowedToBeNavigatedBy(initiatorNavigable, sourceDocument, sourceHasTransientActivation, url))
        {
            if (exceptionsEnabled)
                throw new DomException("The document is not allowed to navigate this browsing context", "SecurityError");

            return;
        }

        // As in the browsers, a document of the web cannot navigate to a local file: the navigation is blocked before the
        // navigate event is fired (the promises of navigation.navigate() are rejected with an "AbortError" DOMException)
        if (traversalEntry is null && url.Scheme == "file" && sourceDocument is { Origin.Scheme: "http" or "https" })
        {
            // The navigation ends without document (e.g. the promise of WindowClient.navigate() is rejected)
            NavigationEndedWithoutDocument?.Invoke();
            return;
        }

        // https://wicg.github.io/scroll-to-text-fragment/#fragment-directive: the fragment directive (the part of the fragment
        // after ":~:") is removed from the URL; its text directives are searched in the document once it is navigated
        string? fragmentDirective = null;
        if (traversalEntry is null && AreTextDirectivesEnabled && TextDirectives.HasFragmentDirective(url))
        {
            url = url.Clone();
            fragmentDirective = TextDirectives.RemoveFragmentDirective(url);
        }

        var activeDocument = Document;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, steps 11 to 13: a navigation (not a reload nor a
        // traversal) cancels the lazy loading of a lazy iframe container (e.g. a fragment navigation of its initial document)
        if (traversalEntry is null && activeDocument.UnloadCounter == 0 && Container is HtmlIFrameElement iframe && LazyLoading.WillLazyLoad(iframe))
        {
            LazyLoading.ClearResumptionSteps(iframe);
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, step 14: the navigation is ignored when the
        // navigable is not allowed to perform a navigation (the rate limit), after the lazy loading of the container is canceled
        if (checkNavigationRateLimit && traversalEntry is null && !IsAllowedToPerformNavigationOrHistoryUpdate())
            return;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate-to-a-javascript:-url (ignored without a script engine)
        if (url.Scheme == "javascript")
        {
            if (Options.ScriptEngine is { } engine && Document.ScriptingEnabled)
            {
                var initiatorOrigin = sourceDocument is null ? null : Document.GetOriginDocument(sourceDocument);
                var generation = _javaScriptUrlNavigationGeneration;
                var isFormSubmission = formData is not null;

                // The location of the script that started the navigation, for the violations of the content security policies
                // (the navigation runs in a task)
                var scriptLocation = sourceDocument is { ContentSecurityPoliciesOrNull: not null, RelevantWindow.ScriptEngine: { } sourceEngine } ? sourceEngine.GetCurrentScriptLocation() : null;

                // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, steps 18 and 19: the navigation is the
                // ongoing navigation of the navigable, which a later navigation cancels, and a navigation of a child navigable
                // delays the load event of its parent. As in the browsers, a javascript: URL of a cross origin-domain document,
                // which does nothing, does not abort the ongoing navigation.
                var javaScriptNavigationId = 0;
                if (sourceDocument is null || Document.IsSameOriginDomain(sourceDocument, activeDocument))
                {
                    CancelOngoingNavigation();
                    javaScriptNavigationId = Interlocked.Increment(ref s_nextNavigationId);
                    _ongoingNavigation = javaScriptNavigationId;
                    _javaScriptUrlNavigationId = javaScriptNavigationId;
                    if (Parent is not null)
                    {
                        IsDelayingLoadEvents = true;
                    }
                }

                activeDocument.QueueGlobalTask(() =>
                {
                    // As in the browsers, a later javascript: URL navigation does not cancel this one: both scripts run (WPT
                    // url/javascript-urls.window.js); another navigation cancels it (WPT navigating-across-documents/007.html)
                    var supersededByJavaScriptUrl = false;
                    if (javaScriptNavigationId != 0 && _ongoingNavigation != javaScriptNavigationId)
                    {
                        if (!IsJavaScriptUrlNavigationOngoing || javaScriptNavigationId > _javaScriptUrlNavigationId)
                            return;

                        supersededByJavaScriptUrl = true;
                    }

                    if (generation != _javaScriptUrlNavigationGeneration)
                    {
                        if (javaScriptNavigationId != 0)
                        {
                            EndNavigationWithoutDocument();
                        }

                        return;
                    }

                    NavigateToJavaScriptUrl(engine, url, sourceDocument, initiatorOrigin, initialInsertion, sourceElement, scriptLocation, isFormSubmission, isOngoingNavigation: javaScriptNavigationId != 0 && !supersededByJavaScriptUrl, supersededByJavaScriptUrl);
                });
            }

            return;
        }

        if (historyHandling == NavigationHistoryBehavior.Auto)
        {
            // As in the browsers, a POST request to the URL of the document is not converted to a replace (the
            // specification converts every navigation to the URL of the document)
            historyHandling = postResource is null && UrlEquals(url, activeDocument.UrlRecord, excludeFragments: false) && (sourceDocument is null || Document.IsSameOrigin(sourceDocument, activeDocument))
                ? NavigationHistoryBehavior.Replace
                : NavigationHistoryBehavior.Push;
        }

        // The navigation must be a replace: the initial about:blank document, and the navigables of a prerendering
        // traversable, whose session history has a single entry (https://wicg.github.io/nav-speculation/prerendering.html#navigation-patches)
        if (activeDocument.IsInitialAboutBlank || Top.IsPrerendering)
        {
            historyHandling = NavigationHistoryBehavior.Replace;
        }

        if (srcdoc is null && postResource is null && traversalEntry is null && UrlEquals(url, ActiveEntry.Url, excludeFragments: true) && url.Fragment is not null)
        {
            NavigateToFragment(url, historyHandling, userInvolvement, sourceElement, navigationApiState, apiMethodTracker, fragmentDirective, sourceDocument?.Origin);
            return;
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, step 21: a navigation is ignored while a traversal is ongoing
        if (_isTraversalOngoing && traversalEntry is null)
            return;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, step 14: a navigation started by the
        // beforeunload, pagehide or unload event handlers of the document is ignored
        if (activeDocument.UnloadCounter > 0)
            return;

        // The new navigation aborts the ongoing one (and the ongoing navigate event). The ongoing navigation of a traversal or
        // a reload is "traversal", set without informing the navigation API (the navigate event was fired by the traversal).
        CancelOngoingNavigation(informNavigationApi: traversalEntry is null);
        _isTraversalOngoing = traversalEntry is not null;
        var navigationId = Interlocked.Increment(ref s_nextNavigationId);
        _ongoingNavigation = navigationId;
        _ongoingNavigationSourceDocument = traversalEntry is null ? sourceDocument : null;
        if (_containerResourceTiming is { NavigationId: 0 } containerResourceTiming)
        {
            containerResourceTiming.NavigationId = navigationId;
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, step 20: the navigate event of a navigation
        // initiated by a same origin-domain document, which can cancel or intercept it
        if (traversalEntry is null && userInvolvement != UserNavigationInvolvement.BrowserUI && sourceDocument is not null && Document.IsSameOriginDomain(activeDocument, sourceDocument)
            && !activeDocument.IsInitialAboutBlank && url.Scheme is "about" or "blob" or "data" or "file" or "http" or "https" && activeDocument.RelevantWindow is { } activeWindow)
        {
            var navigationType = historyHandling == NavigationHistoryBehavior.Push ? NavigationType.Push : NavigationType.Replace;
            if (!activeWindow.Navigation.FirePushReplaceReloadNavigateEvent(navigationType, url, isSameDocument: false, userInvolvement, sourceElement, postResource is null ? null : formData, navigationApiState?.Value, apiMethodTracker: apiMethodTracker))
                return;

            if (IsDestroyed || _ongoingNavigation != navigationId)
                return;
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#checking-if-unloading-is-canceled: the beforeunload
        // event is fired at the documents that would be unloaded (synchronously, as the browsers do). The navigation is
        // canceled when the user does not confirm the unload prompt.
        if ((traversalEntry is null || !ReferenceEquals(_beforeUnloadFiredForEntry, traversalEntry)) && FireBeforeUnload())
        {
            if (_ongoingNavigation == navigationId)
            {
                EndNavigationWithoutDocument();
            }

            return;
        }

        if (IsDestroyed || _ongoingNavigation != navigationId)
            return;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, step 22: a navigation of a child navigable
        // delays the load event of its parent; a reload or a traversal does not, except the restoration of the entry of a
        // child navigable inserted in a document loaded again by a traversal
        if (Parent is not null && (traversalEntry is null || initialInsertion))
        {
            IsDelayingLoadEvents = true;
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate, step 23.3: the active document stops loading
        if (traversalEntry is null)
        {
            activeDocument.QueueGlobalTask(() =>
            {
                if (_ongoingNavigation == navigationId && !IsDestroyed)
                {
                    Document.AbortDocumentAndDescendants(abortFetches: false);
                }
            });
        }

        // The blob URL entry is resolved when the URL is parsed (https://url.spec.whatwg.org/#concept-url-blob-entry): the
        // navigation loads the blob even if the URL is revoked before the document is loaded
        var blobUrlEntry = url.Scheme == "blob" ? BlobUrls.Resolve(url) : null;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#snapshotting-target-snapshot-params: the referrer policy
        // of the iframe, which masks the ancestor origins of the new document
        var iframeElementReferrerPolicy = GetIframeElementReferrerPolicy();

        // The sandboxing flags of the new document are the ones of the container when the navigation starts: a later
        // change of the sandbox attribute applies to the next navigation
        var targetSandboxingFlags = GetIframeSandboxingFlags();

        // https://w3c.github.io/webappsec-cspee/#html-integration: the required CSP of the target snapshot params (the csp
        // attribute of the iframe when the navigation starts)
        var requiredPolicy = GetRequiredContentSecurityPolicy();
        QueueTask(() =>
        {
            if (_ongoingNavigation != navigationId || IsDestroyed)
                return;

            StartLoadingDocument(navigationId, url, sourceDocument, sourceHasTransientActivation, historyHandling, srcdoc, referrerPolicy, iframeElementReferrerPolicy, targetSandboxingFlags, requiredPolicy, blobUrlEntry, postResource, traversalEntry, response, isFormSubmission: formData is not null, fragmentDirective, userInvolvement);
        });
    }

    /// <summary>
    /// The entry of the cross-document traversal that already fired beforeunload at the documents of the navigable (before
    /// its navigate event, see <see cref="ApplyTraverseHistoryStep"/>), while the navigation that loads the entry starts: it
    /// does not fire the event again.
    /// </summary>
    private SessionHistoryEntry? _beforeUnloadFiredForEntry;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#checking-if-unloading-is-canceled: fires beforeunload at
    /// the active documents of the navigable and of its descendant navigables
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#fire-beforeunload). When a document that has sticky
    /// activation cancels the event, the user is asked to confirm the unloading, once, with
    /// <see cref="BrowsingContextOptions.UserPromptHandler"/> (without handler, the unloading is never canceled).
    /// </summary>
    /// <returns>Whether the user canceled the unloading.</returns>
    private bool FireBeforeUnload()
    {
        var unloadPromptShown = false;
        foreach (var navigable in GetDescendantNavigables().Prepend(this).ToList())
        {
            // A document whose beforeunload, pagehide or unload event handlers are running does not get the event again
            var document = navigable.Document;
            if (document.UnloadCounter > 0 || document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document))
                continue;

            document.UnloadCounter++;
            EventLoop.TerminationNestingLevel++;
            try
            {
                var beforeUnloadEvent = BeforeUnloadEvent.CreateUninitializedBeforeUnloadEvent();
                beforeUnloadEvent.InitEvent("beforeunload", bubbles: false, cancelable: true);
                window.FireEvent(beforeUnloadEvent);

                // "If unloadPromptShown is false, document's active sandboxing flag set does not have its sandboxed modals flag
                // set, document's relevant global object has sticky activation, and beforeunloadEvent's canceled flag is set or
                // its returnValue attribute is not the empty string, then [...] ask the user to confirm that they wish to unload
                // the document, and pause while waiting for the user's response"
                if (!unloadPromptShown && window.HasStickyActivation && (beforeUnloadEvent.DefaultPrevented || beforeUnloadEvent.ReturnValue.Length > 0)
                    && window.ShowUserPrompt(UserPromptType.BeforeUnload, message: "", defaultValue: "") is { } result)
                {
                    unloadPromptShown = true;
                    if (!result.Accepted)
                        return true;
                }
            }
            finally
            {
                EventLoop.TerminationNestingLevel--;
                document.UnloadCounter--;
            }
        }

        return false;
    }

    // Incremented to cancel the pending javascript: URL navigations
    private int _javaScriptUrlNavigationGeneration;

    // The identifier of the last javascript: URL navigation (see IsJavaScriptUrlNavigationOngoing)
    private int _javaScriptUrlNavigationId;

    /// <summary>
    /// Whether the ongoing navigation is a javascript: URL navigation, whose script has not run yet: as in the browsers, a
    /// form submission planned before it still navigates the navigable (the script may not create a document).
    /// </summary>
    internal bool IsJavaScriptUrlNavigationOngoing => _ongoingNavigation != 0 && _ongoingNavigation == _javaScriptUrlNavigationId;

    // The source document of the ongoing navigation (null for a traversal or a reload): the client of its request
    private Document? _ongoingNavigationSourceDocument;

    /// <summary>
    /// Cancels the ongoing navigation when its request was made by a document whose fetches are canceled
    /// (https://html.spec.whatwg.org/multipage/document-lifecycle.html#abort-a-document: the fetches in the context of the
    /// document, e.g. the navigation of an iframe by its parent document stopped by <c>window.stop()</c>), as in Chrome.
    /// </summary>
    internal void CancelNavigationFetchedBy(Document document)
    {
        if (!IsCrossDocumentNavigationOngoing || !ReferenceEquals(_ongoingNavigationSourceDocument, document) || _isTraversalOngoing)
            return;

        CancelOngoingNavigation();
        EndNavigationWithoutDocument();
    }

    /// <summary>Whether the navigable is navigating to another document (the ongoing navigation is not a javascript: URL navigation).</summary>
    internal bool IsCrossDocumentNavigationOngoing => _ongoingNavigation != 0 && _ongoingNavigation != _javaScriptUrlNavigationId;

    /// <summary>
    /// Whether the text directives are enabled (<see cref="WebPlatformFeatures.TextDirectivesEnabled"/>): the fragment
    /// directive is then removed from the URLs the navigable is navigated to.
    /// </summary>
    private bool AreTextDirectivesEnabled => (Options.Features ?? WebPlatformFeatures.Default).TextDirectivesEnabled;

    /// <summary>
    /// Whether the top-level browsing context is the only one of its browsing context group
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#browsing-context-group-set): no other top-level browsing
    /// context has script access to its documents (e.g. a popup opened with <c>noopener</c>).
    /// </summary>
    internal bool IsAloneInItsGroup
    {
        get
        {
            foreach (var topLevel in GetGroupTopLevelBrowsingContexts())
            {
                if (!ReferenceEquals(topLevel, Top) && !topLevel.IsDestroyed && !IsInOtherGroup(topLevel))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Cancels the javascript: URL navigations of the navigable that are queued or running: as in the browsers (unspecified), a form
    /// submission planned to navigate the navigable takes precedence over them, so that the navigable does not load two documents.
    /// </summary>
    internal void CancelPendingJavaScriptUrlNavigations() => _javaScriptUrlNavigationGeneration++;

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate-to-a-javascript:-url</summary>
    private void NavigateToJavaScriptUrl(Scripting.ScriptEngine engine, UrlRecord url, Document? sourceDocument, Document? initiatorOrigin, bool initialInsertion, Element? sourceElement, Scripting.ScriptLocation? scriptLocation, bool isFormSubmission, bool isOngoingNavigation, bool supersededByJavaScriptUrl = false)
    {
        if (IsDestroyed)
            return;

        // The ongoing navigation is set to null. The canceled navigation no longer delays the load event of the parent document
        // (e.g. when the javascript: URL is blocked, or evaluates to a value that is not a string).
        if (isOngoingNavigation)
        {
            CancelOngoingNavigation();
            EndNavigationWithoutDocument();
        }

        // The navigation of a cross origin-domain document is not the ongoing navigation: it is canceled after the origin
        // check (the browsers do not abort a navigation of a document because a cross-origin document tried to run a
        // javascript: URL in it)
        var document = Document;
        if (sourceDocument is not null && !Document.IsSameOriginDomain(sourceDocument, document))
            return;

        // As in the browsers (the specification only checks the target), a document sandboxed without allow-scripts cannot
        // run a script through a javascript: URL in another navigable (e.g. in a popup that escapes its sandbox)
        if (sourceDocument is not null && (sourceDocument.ActiveSandboxingFlags & SandboxingFlags.Scripts) != SandboxingFlags.None)
            return;

        // A navigation superseded by a later javascript: URL navigation leaves the ongoing navigation to it
        if (!isOngoingNavigation && !supersededByJavaScriptUrl)
        {
            CancelOngoingNavigation();
            EndNavigationWithoutDocument();
        }

        // https://w3c.github.io/webappsec-csp/#should-block-navigation-request: the pre-navigation checks (the Trusted Types
        // default policy may convert the script, and the form-action directive of a form submission), then the inline checks of
        // the script directives, with the policies of the source of the navigation. The violation of an inline check is fired
        // at the element that started the navigation (a hyperlink, or the navigable container), as in the browsers.
        var urlString = url.Serialize();
        var policyDocument = sourceDocument ?? document;
        var violationElement = sourceElement is not null && ReferenceEquals(sourceElement.NodeDocument, policyDocument) ? sourceElement
            : Container is { } violationContainer && ReferenceEquals(violationContainer.NodeDocument, policyDocument) ? violationContainer
            : null;
        if (ContentSecurityPolicy.ShouldBlockJavaScriptUrlNavigation(policyDocument, ref urlString, isFormSubmission, violationElement, scriptLocation))
            return;

        var encodedScriptSource = urlString["javascript:".Length..];
        if (IsDestroyed || !ReferenceEquals(Document, document))
            return;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#evaluate-a-javascript:-url
        var scriptSource = WebEncodingRegistry.CreateDecoder(WebEncodingId.Utf8).DecodeToString(PercentEncoding.PercentDecode(encodedScriptSource));
        var window = document.RelevantWindow!;
        var script = new Scripting.ClassicScript(window, scriptSource, document.BaseUri) { Url = document.Url };
        var generation = _javaScriptUrlNavigationGeneration;
        var result = Scripting.ScriptExecution.RunClassicScript(engine, script) as string;

        // As in the browsers, a form submission planned by the script (e.g. form.submit() in the iframe) takes precedence over the result
        if (generation != _javaScriptUrlNavigationGeneration)
            return;

        if (result is null)
        {
            // The load event of an iframe inserted with a javascript: URL is fired even when no document is created
            if (initialInsertion && !IsDestroyed && Document.IsInitialAboutBlank && Container is { } container)
            {
                if (container is HtmlIFrameElement iframe)
                {
                    iframe.RunIframeLoadEventSteps();
                }
                else
                {
                    container.FireEvent(new Event("load"));
                }
            }

            return;
        }

        if (IsDestroyed || !ReferenceEquals(Document, document))
            return;

        // The result is the body of an HTML response, whose URL is the URL of the active document and whose origin is the origin of the initiator
        var parameters = new DocumentParameters
        {
            HistoryHandling = NavigationHistoryBehavior.Replace,

            // The navigation params have no request (https://html.spec.whatwg.org/multipage/browsing-the-web.html#evaluate-a-javascript:-url
            // questions the resulting null referrer): as in Chrome and Safari, the document keeps the referrer of the previous document
            Referrer = document.Referrer,
            ReferrerPolicy = document.ReferrerPolicy,
            OriginDocument = initiatorOrigin ?? Document.GetOriginDocument(document),
            AboutBaseUrl = document.AboutBaseUrl,
            OpenerPolicy = document.OpenerPolicy,
            EmbedderPolicy = document.EmbedderPolicy,
            DocumentIsolationPolicy = document.DocumentIsolationPolicy,

            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#evaluate-a-javascript:-url: the policy container of
            // the active document
            PolicyContainer = PolicyContainer.Clone(document),
        };

        // The body of the response is the UTF-8 encoding of the result: its lone surrogates become U+FFFD
        LoadDocument(document.UrlRecord, parameters, DocumentResource.Create(Infra.InfraString.ToScalarValueString(result), "text/html;charset=utf-8"));
    }

    /// <summary>
    /// The destination of the request of a navigation (https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching,
    /// step 3): the element of the container of a nested browsing context, else <c>document</c>.
    /// </summary>
    private RequestDestination GetNavigationDestination() => Container?.LocalName switch
    {
        "iframe" => RequestDestination.Iframe,
        "frame" => RequestDestination.Frame,
        "embed" => RequestDestination.Embed,
        "object" => RequestDestination.Object,
        _ => RequestDestination.Document,
    };

    /// <summary>
    /// Aborts the ongoing navigation: its document is not loaded, and the <see cref="DocumentLoader.LoadAsync"/> call is canceled.
    /// The navigation API is informed about it (https://html.spec.whatwg.org/multipage/browsing-the-web.html#set-the-ongoing-navigation),
    /// except by a traversal, which sets the ongoing navigation directly.
    /// </summary>
    private void CancelOngoingNavigation(bool informNavigationApi = true)
    {
        if (informNavigationApi && Document?.RelevantWindow?.NavigationIfCreated is { } navigation)
        {
            navigation.InformAboutAbortingNavigation();
        }

        _ongoingNavigation = 0;
        _ongoingNavigationSourceDocument = null;
        _isTraversalOngoing = false;
        if (_loaderCancellation is { } cancellation)
        {
            _loaderCancellation = null;
            cancellation.Cancel();
        }
    }

    /// <summary>
    /// Creates the navigation params (https://html.spec.whatwg.org/multipage/browsing-the-web.html#populating-a-session-history-entry):
    /// the srcdoc and about:blank documents are created immediately, the other documents are provided by
    /// <see cref="BrowsingContextOptions.DocumentLoader"/>, possibly asynchronously.
    /// </summary>
    private void StartLoadingDocument(int navigationId, UrlRecord url, Document? sourceDocument, bool sourceHasTransientActivation, NavigationHistoryBehavior historyHandling, string? srcdoc, string referrerPolicy, string iframeElementReferrerPolicy, SandboxingFlags targetSandboxingFlags, string? requiredPolicy, FileApi.BlobUrlEntry? blobUrlEntry, PostResource? postResource, SessionHistoryEntry? traversalEntry, DocumentResource? fetchedResponse, bool isFormSubmission, string? fragmentDirective = null, UserNavigationInvolvement userInvolvement = UserNavigationInvolvement.None)
    {
        var initiatorOrigin = sourceDocument is null ? null : Document.GetOriginDocument(sourceDocument);

        // A traversal to an about: document gives it the initiator origin of its document state, not the origin of the active document
        if (traversalEntry?.DocumentState is { HasInitiatorOrigin: true } storedState && url.Scheme == "about")
        {
            initiatorOrigin = storedState.InitiatorOriginDocument;
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate: "If sourceDocument is navigable's container
        // document, then reserve deferred fetch quota for navigable's container given url's origin"
        // (https://fetch.spec.whatwg.org/#reserve-deferred-fetch-quota). As in Chromium (the only implementation, and the
        // web-platform-tests), the origin of the document of a sandboxed iframe (without allow-same-origin) is opaque: the
        // iframe is cross-origin, and gets the minimal quota.
        if (traversalEntry is null && Container is { } navigableContainer && sourceDocument is not null && ReferenceEquals(sourceDocument, navigableContainer.NodeDocument))
        {
            var sandboxed = (GetIframeSandboxingFlags() & SandboxingFlags.Origin) != SandboxingFlags.None;
            DeferredFetch.ReserveQuota(navigableContainer, sandboxed ? OriginRecord.CreateOpaque() : OriginRecord.FromUrl(url));
        }

        // https://wicg.github.io/scroll-to-text-fragment/#restricting-the-text-fragment: the text directive user activation of
        // the active document is transferred to the request (a client-side redirect of a navigation the user is involved in).
        // As in the browsers, a navigation started by a script while its window has transient activation (e.g. window.open()
        // in a click event handler) has it too.
        var textDirectiveUserActivation = traversalEntry is null && (Document.TextDirectiveUserActivation || sourceHasTransientActivation);
        Document.TextDirectiveUserActivation = false;

        // https://w3c.github.io/webappsec-referrer-policy/#determine-requests-referrer: the referrer of the request is "client"
        referrerPolicy = ReferrerPolicies.GetRequestPolicy(referrerPolicy, sourceDocument);
        var referrer = sourceDocument is null ? null : ReferrerPolicies.DetermineReferrer(sourceDocument, url, referrerPolicy);

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching: a traversal that
        // fetches a document again uses the initiator origin and the referrer of its document state, not the ones of the
        // active document that initiates it. As in the browsers, a reload is initiated by the document it reloads (its
        // SameSite=Strict cookies are sent, WPT cookies/samesite/*-reload.https.html).
        OriginRecord? storedInitiatorOrigin = null;
        if (traversalEntry?.DocumentState is { RequestInitiatorOrigin: { } initiatorOfDocumentState } documentState && !ReferenceEquals(traversalEntry, ActiveEntry) && url.Scheme is "http" or "https")
        {
            storedInitiatorOrigin = initiatorOfDocumentState;
            referrer = documentState.RequestReferrer;
        }

        var navigationStart = EventLoop.TimeProvider.GetTimestamp();
        var parameters = new DocumentParameters
        {
            HistoryHandling = historyHandling,
            Referrer = referrer ?? "",
            RequestReferrerPolicy = referrerPolicy,
            TraversalEntry = traversalEntry,
            IframeElementReferrerPolicy = iframeElementReferrerPolicy,
            TargetSandboxingFlags = targetSandboxingFlags,
            SourceAllowsDownloading = sourceDocument is null || (sourceDocument.ActiveSandboxingFlags & SandboxingFlags.Downloads) == SandboxingFlags.None,
            FragmentDirective = fragmentDirective,
            UserInvolvement = userInvolvement,
            InitiatorOrigin = storedInitiatorOrigin ?? sourceDocument?.Origin,
            InitiatorTopLevelOrigin = sourceDocument?.TopLevelOrigin,
            TextDirectiveUserActivation = textDirectiveUserActivation,
            IsPostNavigation = postResource is not null,
            Timing = new DocumentLoadTimingInfo
            {
                NavigationStart = navigationStart,
                FetchStart = navigationStart,
                Type = traversalEntry is null ? NavigationTimingType.Navigate : ReferenceEquals(traversalEntry, ActiveEntry) ? NavigationTimingType.Reload : NavigationTimingType.BackForward,
            },
        };

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#determining-navigation-params-policy-container: the
        // documents of the local schemes get a clone of the policy container stored in the session history entry, else of
        // the one of their initiator (the parent for a srcdoc document)
        var historyPolicyContainer = traversalEntry?.DocumentState.HistoryPolicyContainer;

        // A document of a local scheme loaded again from the session history keeps the required CSP it enforced (as in
        // Chromium): it is in its history policy container, or in the document state of a blob: document. A later change
        // of the csp attribute applies to the documents that are fetched again.
        if (traversalEntry is not null && url.Scheme is "about" or "blob" or "data")
        {
            requiredPolicy = historyPolicyContainer is null ? traversalEntry.DocumentState.HistoryRequiredContentSecurityPolicy : null;
        }

        parameters.RequiredContentSecurityPolicy = requiredPolicy;

        DocumentResource resource;
        if (srcdoc is null && IsNavigationRequestBlockedByContentSecurityPolicy(url, sourceDocument, isFormSubmission))
        {
            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching: a navigation
            // blocked by a content security policy is a network error (an error document, whose origin is opaque)
            resource = new DocumentResource("");
            parameters.OpaqueOrigin = true;
        }
        else if (srcdoc is not null)
        {
            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-from-a-srcdoc-resource: the
            // URL is about:srcdoc; a traversal keeps the fragment of the entry (e.g. about:srcdoc#yo)
            url = traversalEntry is not null && MatchesAboutSrcdoc(url) ? url : AboutSrcdocUrl;
            parameters.PolicyContainer = historyPolicyContainer ?? (Container?.NodeDocument is { } parentDocument ? PolicyContainer.Clone(parentDocument) : PolicyContainer.Empty);
            resource = new DocumentResource(srcdoc);
            parameters.Srcdoc = srcdoc;
            parameters.Referrer = sourceDocument?.Url ?? "";
            parameters.ReferrerPolicy = sourceDocument?.ReferrerPolicy ?? "";
            parameters.OriginDocument = initiatorOrigin;
            parameters.OpaqueOrigin = initiatorOrigin is null;
            parameters.AboutBaseUrl = sourceDocument?.DocumentBaseUrl;
            parameters.EmbedderPolicy = Container?.NodeDocument.EmbedderPolicy;
            parameters.DocumentIsolationPolicy = Container?.NodeDocument.DocumentIsolationPolicy ?? EmbedderPolicyValue.UnsafeNone;
        }
        else if (url.Scheme == "about")
        {
            // about:blank documents have the origin of the initiator; the other about: URLs are network errors
            resource = new DocumentResource("");
            parameters.ReferrerPolicy = sourceDocument?.ReferrerPolicy ?? "";
            parameters.PolicyContainer = historyPolicyContainer ?? PolicyContainer.CloneOrNull(sourceDocument) ?? PolicyContainer.Empty;
            parameters.OriginDocument = MatchesAboutBlank(url) ? initiatorOrigin : null;
            parameters.OpaqueOrigin = initiatorOrigin is null || !MatchesAboutBlank(url);
            parameters.AboutBaseUrl = sourceDocument?.DocumentBaseUrl;
            parameters.EmbedderPolicy = sourceDocument?.EmbedderPolicy;
            parameters.DocumentIsolationPolicy = sourceDocument?.DocumentIsolationPolicy ?? EmbedderPolicyValue.UnsafeNone;

            if (MatchesAboutBlank(url))
            {
                InheritOpenerPolicy(sourceDocument, parameters);
            }
        }
        else if (url.Scheme == "blob")
        {
            // https://fetch.spec.whatwg.org/#scheme-fetch: the content of the blob, whose type is the Content-Type
            if (blobUrlEntry is { } entry)
            {
                resource = new DocumentResource(entry.Object.Data) { Headers = [new("Content-Type", entry.Object.Type)] };

                // The policy container of the initiator, else of the environment that created the blob URL
                // (https://html.spec.whatwg.org/multipage/browsers.html#creating-a-policy-container-from-a-fetch-response).
                // The policy container of a blob: URL is not stored in the session history: as in the browsers, a traversal
                // gets the one of the environment that created the blob URL, not the one of the active document.
                parameters.PolicyContainer = (traversalEntry is null ? PolicyContainer.CloneOrNull(sourceDocument) : null) ?? PolicyContainer.Clone(entry.Environment);
                parameters.EmbedderPolicy = sourceDocument?.EmbedderPolicy;
                parameters.DocumentIsolationPolicy = sourceDocument?.DocumentIsolationPolicy ?? EmbedderPolicyValue.UnsafeNone;
                if (sourceDocument is not null && OriginRecord.IsSameOrigin(OriginRecord.FromUrl(url), sourceDocument.Origin))
                {
                    InheritOpenerPolicy(sourceDocument, parameters);
                }
            }
            else
            {
                resource = new DocumentResource("");
                parameters.OpaqueOrigin = true;
            }
        }
        else if (url.Scheme == "data")
        {
            // https://fetch.spec.whatwg.org/#scheme-fetch: the body of the data: URL, whose type is the Content-Type. The
            // origin of the document is opaque.
            resource = Urls.DataUrl.Process(url) is var (mimeType, body) ? DocumentResource.Create(body, mimeType) : new DocumentResource("");
            parameters.PolicyContainer = historyPolicyContainer ?? PolicyContainer.CloneOrNull(sourceDocument) ?? PolicyContainer.Empty;
            parameters.OpaqueOrigin = true;
            parameters.EmbedderPolicy = sourceDocument?.EmbedderPolicy;
            parameters.DocumentIsolationPolicy = sourceDocument?.DocumentIsolationPolicy ?? EmbedderPolicyValue.UnsafeNone;
        }
        else if (url.Scheme is not ("http" or "https" or "file"))
        {
            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#attempt-to-create-a-non-fetch-scheme-document:
            // the URLs of the schemes that are not fetch schemes (e.g. mailto:, or an unknown scheme) are handed off to
            // external software: the navigation ends, and the active document stays
            EndNavigationWithoutDocument();
            return;
        }
        else if (fetchedResponse is not null)
        {
            // The response fetched by the container is processed as the response of the loader
            QueueTask(() => ProcessResponse(navigationId, url, parameters, fetchedResponse));
            return;
        }
        else if (Options.DocumentLoader is not null)
        {
            // https://wicg.github.io/nav-speculation/prerendering.html#navigate-activation: the navigation activates the
            // document that was prerendered for its URL
            if (TryActivatePrerender(url, sourceDocument, historyHandling, postResource, traversalEntry, parameters))
                return;

            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching: a request
            // whose redirect mode is "manual", whose redirects are followed by the navigation. Its cookies are the ones of
            // the navigable; the site for cookies of a nested navigation is the one of its container document
            // (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-document-based-requests). The
            // navigation is initiated with a user activation when the window of the initiator has transient activation
            // (the "activation" user involvement of https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate).
            var cancellation = new CancellationTokenSource();
            _loaderCancellation = cancellation;
            var request = new FetchRequest(url)
            {
                Method = postResource is null ? "GET" : "POST",
                Body = postResource?.Body,
                Client = sourceDocument,
                Navigable = this,
                Origin = storedInitiatorOrigin?.Serialize() ?? sourceDocument?.SerializeOrigin() ?? "null",
                Referrer = storedInitiatorOrigin is null ? "client" : referrer is { Length: > 0 } ? referrer : "no-referrer",
                ReferrerPolicy = referrerPolicy,
                Destination = GetNavigationDestination(),
                Mode = RequestMode.Navigate,
                CredentialsMode = FetchCredentialsMode.Include,
                UseUrlCredentials = true,
                RedirectMode = FetchRedirectMode.Manual,
                UserActivation = sourceHasTransientActivation,
                IsFormSubmission = isFormSubmission,
                CookieContext = new FetchCookieContext(GetCookieJar(IsCredentiallessNavigable), Parent?.Document.SiteForCookies, Parent?.Document.TopLevelOriginForCookies),

                // https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching: the reserved
                // environment of the new document, which a service worker may control
                ReservedClient = new ReservedClient(url.SerializeOrigin()),
                ReplacesClientId = Document.IsInitialAboutBlank ? null : Document.EnvironmentId,
                // A reload loads the active entry again (https://fetch.spec.whatwg.org/#concept-request-reload-navigation-flag),
                // a traversal another entry of the session history
                IsReloadNavigation = traversalEntry is not null && ReferenceEquals(traversalEntry, ActiveEntry),
                IsHistoryNavigation = traversalEntry is not null && !ReferenceEquals(traversalEntry, ActiveEntry),

                // As in the browsers, a reload validates the document with the server (the cache mode of the request that a
                // service worker gets is "no-cache")
                CacheMode = traversalEntry is not null && ReferenceEquals(traversalEntry, ActiveEntry) ? "no-cache" : "default",
            };
            PrepareRequestOfPrerenderingNavigable(request);
            parameters.ReservedClient = request.ReservedClient;
            parameters.Timing.FetchStart = EventLoop.TimeProvider.GetTimestamp();
            if (_containerResourceTiming is { } containerResourceTiming && containerResourceTiming.NavigationId == navigationId)
            {
                containerResourceTiming.Request = request;
            }

            if (postResource is not null)
            {
                request.HeaderList.Add(new("Content-Type", postResource.ContentType));
            }

            // https://w3c.github.io/webappsec-cspee/#abstract-opdef-set-the-sec-required-csp-header
            if (requiredPolicy is not null)
            {
                request.HeaderList.Add(new("Sec-Required-CSP", requiredPolicy));
            }

            // https://wicg.github.io/nav-speculation/prefetch.html#wait-for-a-matching-prefetch-record: the navigation uses the
            // response of a navigational prefetch of the source document, or waits for the one that is ongoing
            if (TryUsePrefetchRecord(navigationId, url, sourceDocument, postResource, parameters, StartNavigationFetch, cancellation.Token))
                return;

            // The response is processed by a task, even when the fetch completes synchronously: as with a network response,
            // the tasks already queued (e.g. a traversal, another navigation) run before
            StartNavigationFetch();
            return;

            void StartNavigationFetch() => Fetching.WhenCompleted(Fetching.StartFetch(request, cancellation.Token), response => ProcessNavigationResponse(navigationId, url, request, parameters, response, cancellation.Token), QueueTask);
        }
        else
        {
            // Nothing can be fetched: a network error
            resource = new DocumentResource("");
            parameters.OpaqueOrigin = true;
        }

        _ongoingNavigation = 0;
        if (srcdoc is null && !ApplyOpenerPolicy(url, parameters, resource.Headers))
        {
            resource = new DocumentResource("");
            parameters.OpaqueOrigin = true;
        }

        LoadDocument(url, parameters, resource);
    }

    /// <summary>
    /// Processes a response of the fetch of a navigation (https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching),
    /// unless the navigation was aborted: each response, including the redirects, is checked by the opener policy (of a
    /// top-level navigable) and by the cross-origin resource policy (of a child navigable). The redirects to an HTTP(S) URL
    /// are followed; a redirect to a URL whose scheme is not a fetch scheme is handed off to external software (the
    /// navigation ends), and a redirect to another fetch scheme (e.g. <c>data:</c>) is a network error.
    /// </summary>
    private void ProcessNavigationResponse(int navigationId, UrlRecord url, FetchRequest request, DocumentParameters parameters, FetchResponse response, CancellationToken cancellationToken)
    {
        if (_ongoingNavigation != navigationId || IsDestroyed)
            return;

        // The client hints of the response; the navigation is restarted when its request did not have the critical ones
        if (ProcessNavigationResponseClientHints(navigationId, url, request, parameters, response, cancellationToken))
            return;

        var internalResponse = response.Internal;
        var blockedByOpenerPolicy = false;
        if (!response.IsNetworkError)
        {
            var responseUrl = internalResponse.Url ?? request.CurrentUrl;
            Html.NavigationalPrefetch.ProcessClearSiteData(this, responseUrl, internalResponse.HeaderList);
            parameters.OpenerPolicy = null;
            if (!ApplyOpenerPolicy(responseUrl, parameters, internalResponse.HeaderList))
            {
                // The opener policy of the response cannot be combined with the sandboxing flags: the error document keeps
                // the policy, so that a popup is still cut from its opener
                blockedByOpenerPolicy = true;
                response = FetchResponse.NetworkError();
            }
            else if (IsBlockedByResourcePolicy(responseUrl, internalResponse.HeaderList))
            {
                response = FetchResponse.NetworkError();
            }
            else if (internalResponse.Status is 301 or 302 or 303 or 307 or 308)
            {
                var locationUrl = internalResponse.GetLocationUrl(request.CurrentUrl.Fragment, out var failure);
                if (failure)
                {
                    response = FetchResponse.NetworkError();
                }
                else if (locationUrl is { Scheme: "http" or "https" } && request.Client is { ContentSecurityPoliciesOrNull: not null } sourceDocument
                    && ContentSecurityPolicy.ShouldBlockNavigationRequest(sourceDocument, request.Url, locationUrl, request.IsFormSubmission, request.RedirectCount + 1))
                {
                    // https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching: the
                    // pre-navigation checks of the policies of the source run for each URL of the redirect chain
                    response = FetchResponse.NetworkError();
                }
                else if (locationUrl is { Scheme: "http" or "https" })
                {
                    // https://wicg.github.io/scroll-to-text-fragment/#extracting-the-fragment-directive: the fragment directive
                    // of the location URL replaces the one of the navigation. A location URL without fragment inherits the
                    // fragment of the request, and keeps its fragment directive, as in the browsers (the specification
                    // removes it).
                    if (AreTextDirectivesEnabled && internalResponse.GetLocationUrl(requestFragment: null, out _) is { Fragment: not null } locationWithFragment)
                    {
                        parameters.FragmentDirective = TextDirectives.RemoveFragmentDirective(locationWithFragment);
                    }

                    // The reserved environment of another origin is discarded: a new one is created for the location URL
                    if (request.ReservedClient is { } reservedClient && reservedClient.Origin != locationUrl.SerializeOrigin())
                    {
                        request.ReservedClient = new ReservedClient(locationUrl.SerializeOrigin());
                        parameters.ReservedClient = request.ReservedClient;
                    }

                    // https://fetch.spec.whatwg.org/#http-redirect-fetch: the timing of the redirects, which is not exposed
                    // when a redirect is cross-origin
                    if (parameters.Timing is { } redirectTiming)
                    {
                        if (redirectTiming.RedirectStart == 0)
                        {
                            redirectTiming.RedirectStart = redirectTiming.FetchStart;
                        }

                        redirectTiming.RedirectCount++;
                        redirectTiming.Redirects.Add((request.CurrentUrl, HeaderList.GetDecodeAndSplit(internalResponse.HeaderList, "Timing-Allow-Origin") ?? []));
                        redirectTiming.RedirectEnd = redirectTiming.FetchStart = EventLoop.TimeProvider.GetTimestamp();
                    }

                    // The redirects are followed in parallel: the next response is processed without queuing a task
                    Fetching.WhenCompleted(Fetching.ProcessNextManualRedirectAsync(request, response, cancellationToken), next => ProcessNavigationResponse(navigationId, url, request, parameters, next, cancellationToken), static task => task());
                    return;
                }
                else if (locationUrl is not null)
                {
                    if (locationUrl.Scheme is not ("about" or "blob" or "data" or "file"))
                    {
                        _loaderCancellation?.Dispose();
                        _loaderCancellation = null;
                        EndNavigationWithoutDocument();
                        return;
                    }

                    response = FetchResponse.NetworkError();
                }
            }
        }

        if (response.IsNetworkError && !blockedByOpenerPolicy)
        {
            // The document of a network error has no opener policy
            parameters.OpenerPolicy = null;
            parameters.OpenerPolicyEnforcementResult = null;
        }

        // The referrer of the document is the referrer of the request, which is determined again for each URL of the redirect
        // chain (https://fetch.spec.whatwg.org/#main-fetch, step 9)
        parameters.Referrer = request.Referrer is "no-referrer" or "client" ? "" : request.Referrer;
        if (parameters.Timing is { } timing)
        {
            // The request was handled by a service worker: its fetch event is dispatched once the worker is running and activated
            if (request.Timing.ServiceWorkerStart != 0)
            {
                timing.WorkerStart = request.Timing.ServiceWorkerStart;
                timing.FetchStart = Math.Max(timing.FetchStart, Math.Max(request.Timing.ServiceWorkerStart, request.Timing.ServiceWorkerReady));
            }

            // The router rules of the service worker (https://w3c.github.io/ServiceWorker/#handle-fetch, step 15)
            timing.WorkerRouterEvaluationStart = request.Timing.ServiceWorkerRouterEvaluationStart;
            timing.WorkerCacheLookupStart = request.Timing.ServiceWorkerCacheLookupStart;
            timing.WorkerMatchedRouterSource = request.Timing.ServiceWorkerMatchedRouterSource;
            timing.WorkerFinalRouterSource = request.Timing.ServiceWorkerFinalRouterSource;

            timing.ResponseStart = timing.PrefetchResponseStart != 0 ? timing.PrefetchResponseStart : EventLoop.TimeProvider.GetTimestamp();
            timing.HasCrossOriginRedirects = Fetching.HasRedirectTaintedOrigin(request);
            timing.HasNoReferrer = request.Client is not null && request.Referrer == "no-referrer";
        }

        var resource = response.ToDocumentResource();

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object: the URL of the document
        // is the current URL of the request, not the URL of the response (a service worker may respond with the response of
        // another URL)
        if (!response.IsNetworkError && internalResponse.Url is { } finalResponseUrl && !UrlEquals(finalResponseUrl, request.CurrentUrl, excludeFragments: true))
        {
            parameters.PrefetchNavigationUrl ??= request.CurrentUrl;
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching: the timing of the
        // navigation started by an iframe or frame element is reported to the document of the element when the timing allow
        // check passed. Otherwise the element reports a fallback timing when its load event is fired.
        if (_containerResourceTiming is { } containerResourceTiming && ReferenceEquals(containerResourceTiming.Request, request)
            && resource is not null && !request.TimingAllowFailed && !HasNoDocument(resource))
        {
            _containerResourceTiming = null;
            ResourceTiming.Start(containerResourceTiming.Window, request, containerResourceTiming.InitiatorType)?.Report(response);
        }

        ProcessResponse(navigationId, url, parameters, resource, checkedByNavigationFetch: true);
    }

    /// <summary>
    /// Processes the response of a navigation, unless the navigation was aborted: the response of the fetch of the navigation
    /// (<paramref name="checkedByNavigationFetch"/>), or the response fetched by the container (an <c>embed</c> or
    /// <c>object</c> element). A null response is a network error.
    /// </summary>
    private void ProcessResponse(int navigationId, UrlRecord url, DocumentParameters parameters, DocumentResource? resource, bool checkedByNavigationFetch = false, bool resumedAfterPrerendering = false)
    {
        if (_ongoingNavigation != navigationId || IsDestroyed)
            return;

        // https://wicg.github.io/nav-speculation/prerendering.html#navigate-delay-cross-origin-iframes: the document of a
        // child navigable of a prerendering traversable that is cross-origin with the prerendered document is created when
        // the page is activated (its request is sent), unless its container document opted in
        if (!resumedAfterPrerendering && IsPrerenderingChildResponseDelayed(url, resource, out var prerenderingDocument))
        {
            prerenderingDocument.RunAfterPrerendering(() => ProcessResponse(navigationId, url, parameters, resource, checkedByNavigationFetch, resumedAfterPrerendering: true));
            return;
        }

        _ongoingNavigation = 0;
        _loaderCancellation?.Dispose();
        _loaderCancellation = null;

        // https://wicg.github.io/nav-speculation/prerendering.html#navigation-patches: a response that cannot be prerendered
        // cancels the prerendering
        if (Parent is null && IsPrerendering && !IsPrerenderingResponseAllowed(url, resource))
        {
            CancelPrerendering();
            return;
        }

        if (Parent is null && IsPrerendering && PrerenderRecord is { ResponseSearchVariance: null } prerenderRecord && resource is not null)
        {
            prerenderRecord.ResponseSearchVariance = Html.UrlSearchVariance.Obtain(resource.Headers);
        }

        if (resource is null)
        {
            // A network error: an error document, whose origin is opaque
            resource = new DocumentResource("");
            parameters.OpaqueOrigin = true;
            parameters.IsNetworkError = true;
        }
        else if (HasNoDocument(resource))
        {
            // The navigation ends: the active document stays. The container reports the timing of a response without content
            // (its load event is not fired); a download is not reported. The rest of a body that arrives over time is not read,
            // unless the response is handled as a download.
            _isTraversalOngoing = false;
            if (!(resource.StatusCode is not (204 or 205) && TryHandleNavigationAsDownload(url, parameters, resource)) && resource.RemainingContentBytes is { } remainingBytes)
            {
                _ = remainingBytes.GetAsyncEnumerator().DisposeAsync().AsTask();
            }

            if (resource.StatusCode is 204 or 205)
            {
                ReportContainerResourceTimingFallback();
            }
            else
            {
                _containerResourceTiming = null;
            }

            EndNavigationWithoutDocument();
            return;
        }
        else if (resource.Url is not null)
        {
            url = UrlParser.Parse(resource.Url, baseUrl: null) ?? url;

            // The response fetched by a container followed redirects: the referrer is determined again for each URL of the
            // redirect chain (https://fetch.spec.whatwg.org/#main-fetch, step 9), which is the referrer of the document
            foreach (var redirectUrl in checkedByNavigationFetch ? [] : resource.RedirectUrls.Append(resource.Url))
            {
                if (parameters.Referrer.Length == 0 || UrlParser.Parse(parameters.Referrer, baseUrl: null) is not { } referrer || UrlParser.Parse(redirectUrl, baseUrl: null) is not { } currentUrl)
                    break;

                parameters.Referrer = ReferrerPolicies.DetermineReferrer(referrer, currentUrl, parameters.RequestReferrerPolicy) ?? "";
            }
        }

        // The document of a navigational prefetch that was not redirected has the URL of the navigation, which may differ from
        // the prefetched URL by its fragment or by its query (No-Vary-Search); the document of a response that a service
        // worker fetched from another URL has the URL of the request
        if (parameters.PrefetchNavigationUrl is { } prefetchNavigationUrl)
        {
            url = prefetchNavigationUrl;
        }

        // The opener policy and the cross-origin resource policy of the responses of the fetch of a navigation are checked by
        // ProcessNavigationResponse. The response of a child navigable is checked by the frame-ancestors directives of its
        // policies, by the embedder policy of its container document and by its X-Frame-Options header.
        if (!parameters.OpaqueOrigin && (IsNavigationResponseBlocked(url, resource.Headers, resource.StatusCode, checkResourcePolicy: !checkedByNavigationFetch, parameters) || (!checkedByNavigationFetch && !ApplyOpenerPolicy(url, parameters, resource.Headers))))
        {
            // A network error: an error document, whose origin is opaque
            resource = new DocumentResource("");
            parameters.OpaqueOrigin = true;
            parameters.IsNetworkError = true;
            parameters.Headers = [];
        }

        LoadDocument(url, parameters, resource);
    }

    /// <summary>
    /// The pending resource timing of the navigation started by the container of the navigable (the pending resource-timing
    /// start time of an <c>iframe</c> element, https://html.spec.whatwg.org/multipage/iframe-embed-object.html#iframe-pending-resource-timing-start-time,
    /// which the browsers also apply to the <c>frame</c> elements).
    /// </summary>
    private ContainerResourceTiming? _containerResourceTiming;

    private sealed class ContainerResourceTiming(Window window, string url, string initiatorType, long startTimestamp)
    {
        public Window Window { get; } = window;

        public string Url { get; } = url;

        public string InitiatorType { get; } = initiatorType;

        public long StartTimestamp { get; } = startTimestamp;

        /// <summary>The navigation started by the container, or 0 until it starts.</summary>
        public int NavigationId { get; set; }

        /// <summary>The request of the navigation, whose timing info is reported when the timing allow check passes.</summary>
        public FetchRequest? Request { get; set; }
    }

    /// <summary>
    /// Sets the pending resource timing of a navigation started by the container (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#navigate-an-iframe-or-frame):
    /// the navigations to the URLs that are not HTTP(S) URLs are not reported.
    /// </summary>
    internal void StartContainerResourceTiming(Element container, UrlRecord? url)
    {
        _containerResourceTiming = null;
        if (url is { Scheme: "http" or "https" } && container.NodeDocument.RelevantWindow is { } window && ReferenceEquals(window.AssociatedDocument, container.NodeDocument))
        {
            _containerResourceTiming = new ContainerResourceTiming(window, url.Serialize(), container.LocalName, EventLoop.TimeProvider.GetTimestamp());
        }
    }

    /// <summary>Discards the pending resource timing of the container when its navigation did not start (e.g. a navigation to a fragment).</summary>
    internal void DiscardUnstartedContainerResourceTiming()
    {
        if (_containerResourceTiming is { NavigationId: 0 })
        {
            _containerResourceTiming = null;
        }
    }

    /// <summary>
    /// Reports the fallback timing of the navigation started by the container, when its timing was not reported (the timing
    /// allow check failed, or the navigation failed): https://html.spec.whatwg.org/multipage/iframe-embed-object.html#iframe-load-event-steps.
    /// </summary>
    internal void ReportContainerResourceTimingFallback()
    {
        if (_containerResourceTiming is not { } timing)
            return;

        _containerResourceTiming = null;
        if (ReferenceEquals(timing.Window.AssociatedDocument?.RelevantWindow, timing.Window) && timing.Window.Document.IsFullyActive)
        {
            ResourceTiming.MarkFallback(timing.Window, timing.Url, timing.InitiatorType, timing.StartTimestamp);
        }
    }

    /// <summary>The navigation ends without a new document: the active document stays.</summary>
    private void EndNavigationWithoutDocument()
    {
        _ongoingNavigation = 0;
        Document.FireSuppressedContainerLoadEvent();
        if (IsDelayingLoadEvents)
        {
            IsDelayingLoadEvents = false;
            Parent?.Document.TryQueueLoadEvent();
        }

        NavigationEndedWithoutDocument?.Invoke();
    }

    /// <summary>
    /// Handles the response of a navigation whose <c>Content-Disposition</c> is <c>attachment</c> as a download
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#populating-a-session-history-entry) when the source
    /// document, the final sandboxing flags of the navigation and the application (<see cref="BrowsingContextOptions.DownloadStarted"/>)
    /// allow it.
    /// </summary>
    /// <returns>Whether the response was handed to the application, which then reads the rest of its body.</returns>
    private bool TryHandleNavigationAsDownload(UrlRecord url, DocumentParameters parameters, DocumentResource resource)
    {
        if (Options.DownloadStarted is not { } downloadStarted || !parameters.SourceAllowsDownloading)
            return false;

        if ((GetFinalSandboxingFlags(resource.Headers, parameters.TargetSandboxingFlags) & SandboxingFlags.Downloads) != SandboxingFlags.None)
            return false;

        downloadStarted(this, new Download(resource.Url ?? url.Serialize(), filename: null, resource));
        return true;
    }

    /// <summary>
    /// Returns whether a response does not create a document
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#populating-a-session-history-entry): a 204 or 205
    /// status, or a download (a <c>Content-Disposition</c> header whose type is <c>attachment</c>, which is not saved).
    /// </summary>
    private static bool HasNoDocument(DocumentResource resource)
    {
        if (resource.StatusCode is 204 or 205)
            return true;

        // https://httpwg.org/specs/rfc6266.html#header.field.definition: the disposition type is the token before the parameters
        if (HeaderList.Get(resource.Headers, "Content-Disposition") is not { } contentDisposition)
            return false;

        var semicolon = contentDisposition.IndexOf(';', StringComparison.Ordinal);
        return Infra.InfraAscii.EqualsIgnoreAsciiCase(Infra.MimeType.TrimHttpWhitespace(semicolon < 0 ? contentDisposition : contentDisposition.AsSpan(0, semicolon)), "attachment");
    }

    /// <summary>
    /// Creates the document of a navigation and loads it (https://html.spec.whatwg.org/multipage/document-lifecycle.html#loading-a-document).
    /// </summary>
    /// <returns>The new active document.</returns>
    private Document LoadDocument(UrlRecord url, DocumentParameters parameters, DocumentResource resource)
    {
        // The URL of a redirect has its fragment directive (see ProcessNavigationResponse), which is not part of the URL of the
        // document
        if (AreTextDirectivesEnabled && TextDirectives.HasFragmentDirective(url))
        {
            url = url.Clone();
            _ = TextDirectives.RemoveFragmentDirective(url);
        }

        // https://mimesniff.spec.whatwg.org/#mime-type-sniffing-algorithm: a Content-Type that gives no MIME type (e.g. */*) is
        // sniffed from the bytes of the resource (an HTML document, else a text or binary resource). A response without
        // Content-Type is an HTML document (the handlers of the tests often omit it).
        var mimeType = resource.MimeType;
        if (mimeType is null && HeaderList.Get(resource.Headers, "Content-Type") is not null)
        {
            var sniffedType = EmbeddedResources.SniffType(resource.ContentBytes is { } contentBytes ? contentBytes.Span : System.Text.Encoding.UTF8.GetBytes(resource.Content ?? ""));
            mimeType = Infra.MimeType.Parse(sniffedType == "image/*" ? "application/octet-stream" : sniffedType);
        }

        var contentType = mimeType?.Essence ?? "text/html";
        var charset = mimeType?.GetParameter("charset");
        if (HeaderList.Get(resource.Headers, "Last-Modified") is { } lastModified)
        {
            parameters.LastModified = HeaderList.ParseHttpDate(lastModified);
        }

        parameters.Headers = resource.Headers;
        parameters.Status = resource.StatusCode;

        // The timing of the response: a body that arrives over time ends later (see Document.MarkNavigationResponseEnd)
        var responseTime = EventLoop.TimeProvider.GetTimestamp();
        var loadTiming = parameters.Timing ??= DocumentLoadTimingInfo.CreateImmediate(responseTime);
        if (loadTiming.ResponseStart == 0)
        {
            loadTiming.ResponseStart = responseTime;
        }

        var isStreamed = contentType == "text/html" && resource is { ContentBytes: not null, RemainingContentBytes: not null };
        loadTiming.ResponseEnd = isStreamed ? 0 : responseTime;
        parameters.BodySize = isStreamed ? null : (ulong)(resource.ContentBytes?.Length ?? System.Text.Encoding.UTF8.GetByteCount(resource.Content ?? ""));
        if (ReferrerPolicies.ParseHeader(resource.Headers) is { Length: > 0 } referrerPolicy)
        {
            parameters.ReferrerPolicy = referrerPolicy;
        }

        // The spec makes the language unknown when the header lists several languages, but the browsers use the first one
        if (HeaderList.GetDecodeAndSplit(resource.Headers, "Content-Language") is [{ Length: > 0 } contentLanguage, ..])
        {
            parameters.ContentLanguage = contentLanguage;
        }

        _pendingDocumentParameters = parameters;
        try
        {
            if (mimeType is { IsXml: true })
            {
                LoadXmlDocument(url, resource.Content ?? Decode(resource.ContentBytes!.Value.Span, charset), contentType);
                return Document;
            }

            var parser = new HtmlDocumentParser(new HtmlParserOptions
            {
                BrowsingContext = this,
                Url = url.Serialize(),
                ScriptingEnabled = Options.ScriptingEnabled,
                TransportEncoding = charset,
                EncodingDetector = Options.EncodingDetector,
            });

            if (contentType == "text/html" && resource.ContentBytes is { } streamedBytes && resource.RemainingContentBytes is { } remainingBytes)
            {
                // A response that arrives over time is parsed as its chunks arrive
                parser.Parse(streamedBytes, remainingBytes).AddNavigationResponseBytes(streamedBytes.Length);
            }
            else if (contentType == "text/html" && resource.ContentBytes is { } bytes)
            {
                // https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding
                parser.Parse(bytes.Span);
            }
            else if (contentType == "text/html")
            {
                parser.Parse(resource.Content!);
            }
            else if (contentType is "text/plain" or "text/css" or "text/vtt" || mimeType is { IsJavaScript: true } || (mimeType is not null && (mimeType.Subtype.EndsWith("+json", StringComparison.Ordinal) || mimeType.Essence is "application/json" or "text/json")))
            {
                // https://html.spec.whatwg.org/multipage/browsing-the-web.html#loading-a-document: the text documents (plain text,
                // style sheets, scripts, JSON, WebVTT).
                // https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-text: the text is in a pre element, and the
                // encoding of the document is the one of the bytes. As in the browsers, the lines wrap.
                var encoding = WebEncodingId.Utf8;
                var text = resource.Content ?? Decode(resource.ContentBytes!.Value.Span, charset, out encoding);
                parameters.ContentType = contentType;
                parameters.IsTextDocument = true;
                var textDocument = parser.Parse("<pre style=\"word-wrap: break-word; white-space: pre-wrap;\">\n" + WebUtility.HtmlEncode(text));
                textDocument.Encoding = encoding;
            }
            else if (GetMediaDocumentHostElement(contentType) is { } hostElement)
            {
                // https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-media: a document in no-quirks mode whose
                // body contains an element for the media, which fetches the resource; the media plays automatically, with controls
                // The element uses the response of the navigation instead of fetching the resource again
                var attributes = hostElement == "img" ? "" : " controls autoplay";
                parameters.MediaDocumentResource = resource;
                parameters.ContentType = contentType;
                _ = parser.Parse($"<!DOCTYPE html><html><head></head><body><{hostElement} src=\"{WebUtility.HtmlEncode(url.Serialize())}\"{attributes}></body></html>");
            }
            else
            {
                // https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-ua-inline: there is no rendering of the other types
                parser.Parse("");
            }
        }
        finally
        {
            _pendingDocumentParameters = null;
        }

        return Document;
    }

    /// <summary>
    /// The element for the media of a media document (https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-media):
    /// <c>img</c> for a supported image type, <c>video</c> or <c>audio</c> for a type the media backend of the browsing context
    /// can play (<see cref="BrowsingContextOptions.MediaBackend"/>), else null (the type has no rendering).
    /// </summary>
    private string? GetMediaDocumentHostElement(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.Ordinal))
            return HtmlImageElement.IsSupportedImageType(contentType) ? "img" : null;

        if (Options.MediaBackend is not { } backend || !(contentType.StartsWith("video/", StringComparison.Ordinal) || contentType.StartsWith("audio/", StringComparison.Ordinal) || contentType == "application/ogg"))
            return null;

        if (backend.CanPlayType(contentType, []) == Media.MediaTypeSupport.NotSupported)
            return null;

        return contentType.StartsWith("audio/", StringComparison.Ordinal) ? "audio" : "video";
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-xml</summary>
    private void LoadXmlDocument(UrlRecord url, string content, string contentType)
    {
        var document = XmlDocumentParser.CreateDocument(new XmlParserOptions { Url = url.Serialize(), ContentType = contentType, BrowsingContext = this });
        new Html.Parsing.XmlScriptingParser(document, content, runTheEnd: ReferenceEquals(document.BrowsingContext, this)).Start();
    }

    /// <summary>Decodes a resource that is not HTML: the byte order mark, then the charset parameter, then UTF-8.</summary>
    private static string Decode(ReadOnlySpan<byte> bytes, string? charset) => Decode(bytes, charset, out _);

    private static string Decode(ReadOnlySpan<byte> bytes, string? charset, out WebEncodingId encoding)
    {
        var bomLength = WebEncodingRegistry.SniffBom(bytes, out encoding);
        if (bomLength == 0 && (charset is null || !WebEncodingRegistry.TryGetEncoding(charset, out encoding)))
        {
            encoding = WebEncodingId.Utf8;
        }

        return WebEncodingRegistry.CreateDecoder(encoding).DecodeToString(bytes[bomLength..]);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#determining-the-iframe-element-referrer-policy</summary>
    private string GetIframeElementReferrerPolicy() => (Container as HtmlIFrameElement)?.ReferrerPolicy ?? "";

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#internal-ancestor-origin-objects-list-creation-steps: the origin of the
    /// container document, followed by its own list. The origin of the container document is masked (a new opaque origin) when
    /// the referrer policy of the iframe is "no-referrer", or "same-origin" and the documents are cross-origin; the ancestors that
    /// follow it and are same origin with it are masked as well.
    /// </summary>
    internal IReadOnlyList<OriginRecord> CreateInternalAncestorOriginObjectsList(Document document, string referrerPolicy)
    {
        if (Container?.NodeDocument is not { } parentDocument)
            return [];

        var parentOrigin = parentDocument.Origin;
        var masked = referrerPolicy == "no-referrer" || (referrerPolicy == "same-origin" && !OriginRecord.IsSameOrigin(parentOrigin, document.Origin));
        var output = new List<OriginRecord>(parentDocument.InternalAncestorOriginObjects.Count + 1) { masked ? OriginRecord.CreateOpaque() : parentOrigin };
        foreach (var ancestorOrigin in parentDocument.InternalAncestorOriginObjects)
        {
            if (masked && OriginRecord.IsSameOrigin(ancestorOrigin, parentOrigin))
            {
                output.Add(OriginRecord.CreateOpaque());
                continue;
            }

            output.Add(ancestorOrigin);
            masked = false;
        }

        return output;
    }

    /// <summary>
    /// Whether the environment of a new document of this browsing context is a secure context
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#secure-context): the document and the documents of the
    /// ancestors are potentially trustworthy.
    /// </summary>
    private bool IsSecureContext(Document document)
    {
        for (var current = document; current is not null; current = ReferenceEquals(current, document) ? Container?.NodeDocument : current.BrowsingContext?.Container?.NodeDocument)
        {
            if (!current.IsPotentiallyTrustworthyForSecureContext())
                return false;
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#matches-about:blank</summary>
    internal static bool MatchesAboutBlank(UrlRecord url) => url.Scheme == "about" && url.OpaquePath == "blank" && url.Username.Length == 0 && url.Password.Length == 0 && url.Host is null;

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#matches-about:srcdoc</summary>
    internal static bool MatchesAboutSrcdoc(UrlRecord url) => url.Scheme == "about" && url.OpaquePath == "srcdoc" && url.Query is null && url.Username.Length == 0 && url.Password.Length == 0 && url.Host is null;

    /// <summary>https://url.spec.whatwg.org/#concept-url-equals</summary>
    internal static bool UrlEquals(UrlRecord a, UrlRecord b, bool excludeFragments) => a.Serialize(excludeFragments) == b.Serialize(excludeFragments);

    /// <summary>
    /// Initializes a document created by a parser for this browsing context ("create and initialize a Document object"),
    /// and makes it the active document.
    /// </summary>
    /// <param name="document">The new document.</param>
    internal void InitializeDocument(Document document)
    {
        var activeDocument = Document;

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object: a new registry
        document.CustomElementRegistryStorage = new CustomElementRegistry(document);
        var parameters = _pendingDocumentParameters ?? new DocumentParameters
        {
            HistoryHandling = activeDocument.IsInitialAboutBlank ? NavigationHistoryBehavior.Replace : NavigationHistoryBehavior.Push,
        };

        if (parameters.ContentType is { } contentType)
        {
            document.ContentType = contentType;
        }

        document.AboutBaseUrl = parameters.AboutBaseUrl;
        document.OriginDocument = parameters.OriginDocument;
        document.OpaqueOrigin = parameters.OpaqueOrigin;
        document.IsNetworkErrorDocument = parameters.IsNetworkError;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#determining-the-origin: a sandboxed document has a new
        // opaque origin. The sandboxing flags are the ones of the iframe container and of the CSP of the response.
        var sandboxingFlags = GetFinalSandboxingFlags(parameters.Headers, parameters.TargetSandboxingFlags);
        if (parameters.PolicyContainer is { IsEmpty: false } inheritedPolicyContainer)
        {
            sandboxingFlags |= GetContentSecurityPolicySandboxingFlags(inheritedPolicyContainer);
        }

        // https://w3c.github.io/webappsec-cspee/#html-integration: the required CSP is enforced by the document, unless the
        // policies of its response subsume it (its sandbox directive applies to the document)
        var enforcedRequiredPolicy = GetEnforcedRequiredContentSecurityPolicy(document, parameters);
        if (enforcedRequiredPolicy?.GetDirectiveValue("sandbox") is { } requiredSandbox)
        {
            sandboxingFlags |= Sandboxing.ParseSandboxingDirective(string.Join(' ', requiredSandbox));
        }

        document.ActiveSandboxingFlags = sandboxingFlags;

        // https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-script: scripting is disabled when the
        // sandboxed scripts browsing context flag is set (the scripts do not run, and the parser parses noscript elements)
        if ((sandboxingFlags & SandboxingFlags.Scripts) != SandboxingFlags.None)
        {
            document.ScriptingEnabled = false;
        }

        if ((sandboxingFlags & SandboxingFlags.Origin) != SandboxingFlags.None)
        {
            document.OriginDocument = null;
            document.OpaqueOrigin = true;
        }

        document.Referrer = parameters.Referrer;
        document.ReferrerPolicy = parameters.ReferrerPolicy;
        document.ResponseStatus = parameters.Status;
        document.ResponseBackForwardCacheBlockingReasons = GetResponseBackForwardCacheBlockingReasons(document, parameters);
        document.MediaDocumentResource = parameters.MediaDocumentResource;

        // The text documents have no doctype but are in no-quirks mode, as in the browsers
        document.ParserCannotChangeTheMode |= parameters.IsTextDocument;

        // https://w3c.github.io/webappsec-csp/#parse-response-csp: the self-origin of the CSP list is the origin of the URL of
        // the response, also when the document is sandboxed (the documents of the local schemes keep their origin)
        if (document.UrlRecord.Scheme is "http" or "https" or "file")
        {
            document.ContentSecurityPolicySelfOrigin = OriginRecord.FromUrl(document.UrlRecord);
        }
        document.InternalAncestorOriginObjects = CreateInternalAncestorOriginObjectsList(document, parameters.IframeElementReferrerPolicy ?? GetIframeElementReferrerPolicy());
        document.OpenerPolicy = Parent is null ? parameters.OpenerPolicy ?? OpenerPolicy.Default : OpenerPolicy.Default;
        document.EmbedderPolicy = parameters.EmbedderPolicy ?? EmbedderPolicy.Obtain(parameters.Headers, IsSecureContext(document));
        document.DocumentIsolationPolicy = parameters.EmbedderPolicy is not null ? parameters.DocumentIsolationPolicy : GetDocumentIsolationPolicy(parameters.Headers, IsSecureContext(document));
        document.IsCredentialless = IsCredentiallessNavigable;

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object (and the report-only
        // policy of https://w3c.github.io/webappsec-permissions-policy/#changes-to-html): the permissions policy of the
        // navigable for the origin of the document, with the declarations of the response
        var inheritedPermissionsPolicy = PermissionsPolicyAlgorithms.CreateForNavigable(Container, document.Origin);
        document.PermissionsPolicyRecord = PermissionsPolicyAlgorithms.CreateFromResponse(inheritedPermissionsPolicy, document.Origin, parameters.Headers, reportOnly: false);
        document.ReportOnlyPermissionsPolicyRecord = PermissionsPolicyAlgorithms.CreateFromResponse(inheritedPermissionsPolicy, document.Origin, parameters.Headers, reportOnly: true);
        UpgradeInsecureRequests.Inherit(document, Container?.NodeDocument);

        // https://html.spec.whatwg.org/multipage/browsers.html#obtain-browsing-context-navigation
        var switchesGroup = Parent is null && parameters.OpenerPolicyEnforcementResult is { NeedsBrowsingContextGroupSwitch: true };
        if (switchesGroup)
        {
            SwitchBrowsingContextGroup(document.OpenerPolicy);
        }
        document.LastModifiedTime = parameters.LastModified;
        document.ProtocolLanguage = parameters.ContentLanguage;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#determining-navigation-params-policy-container: the
        // policies of the response, or the inherited policy container of a document of a local scheme
        if (parameters.PolicyContainer is { } policyContainer)
        {
            document.SetPolicyContainer(policyContainer);
        }
        else
        {
            document.AddContentSecurityPolicies(parameters.Headers);
        }

        if (enforcedRequiredPolicy is not null)
        {
            document.AddContentSecurityPolicy(enforcedRequiredPolicy.Serialized, reportOnly: false);
        }

        if (parameters.RequiredContentSecurityPolicy is { } requiredPolicy && !parameters.IsNetworkError)
        {
            document.RequiredContentSecurityPolicy = requiredPolicy;
        }

        foreach (var (name, value) in parameters.Headers)
        {
            if (Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "Document-Policy"))
            {
                document.ExpectsNoLinkedResources |= Html.Parsing.SpeculativeHtmlParser.HasExpectNoLinkedResources(value);
                document.ForceLoadAtTop |= Html.Parsing.SpeculativeHtmlParser.HasDocumentPolicyFeature(value, "force-load-at-top");
            }
        }

        document.ReadyState = DocumentReadyState.Loading;
        document.StartRenderBlockingPeriod();
        InitializeServiceWorkerClient(document, parameters.ReservedClient);

        // The window of an initial about:blank document is reused by a same origin-domain document
        Window window;
        if (!switchesGroup && activeDocument.IsInitialAboutBlank && Document.IsSameOriginDomain(activeDocument, document))
        {
            window = activeDocument.RelevantWindow!;
        }
        else
        {
            // The agent of the origin of the document: the Origin-Agent-Cluster header of a secure context may request an
            // origin-keyed agent cluster (https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object)
            var requestsOriginKeyed = AgentClusterMap.RequestsOriginKeyed(parameters.Headers) && IsSecureContext(document);
            window = new Window(document, Options, EventLoop) { AgentCluster = AgentClusters.Obtain(document.Origin, requestsOriginKeyed, Group.CrossOriginIsolationMode != CrossOriginIsolationMode.None) };
        }

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object: the load timing info
        // of the document, whose navigation start time is the time origin of the window, and its navigation timing entry
        CreateNavigationTimingEntry(document, window, parameters.Timing ?? DocumentLoadTimingInfo.CreateImmediate(EventLoop.TimeProvider.GetTimestamp()), parameters.BodySize, parameters.Status, parameters.Headers);

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object: the reporting endpoints of the response
        window.InitializeReportingEndpoints(parameters.Headers, document.UrlRecord);

        Activate(document, window, parameters.HistoryHandling, parameters.Srcdoc, parameters.TraversalEntry, parameters.RequestReferrerPolicy, parameters);

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object: the Refresh header
        if (HeaderList.Get(parameters.Headers, "Refresh") is { } refresh)
        {
            document.RunSharedDeclarativeRefreshSteps(refresh);
        }

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#loading-a-document: the Link headers of the response
        LinkPreload.ProcessLinkHeaders(document, parameters.Headers);

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object: the Speculation-Rules header
        document.ProcessSpeculationRulesHeader(parameters.Headers);
        document.SupportsPrerenderingCrossOriginFrames = Html.Prerendering.HasLoadingMode(parameters.Headers, "prerender-cross-origin-frames");
    }

    /// <summary>
    /// Sets the document load timing info of a new document, the time origin of its window (the start of the navigation) and
    /// its navigation timing entry (https://w3c.github.io/navigation-timing/#dfn-create-the-navigation-timing-entry). The
    /// loader does not expose the timing of the connection: the connection is established when the request is sent.
    /// </summary>
    private static void CreateNavigationTimingEntry(Document document, Window window, DocumentLoadTimingInfo timing, ulong? bodySize, int status, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        timing.DomLoading = window.EventLoop.TimeProvider.GetTimestamp();
        document.LoadTimingInfo = timing;
        window.SetTimeOrigin(timing.NavigationStart);

        var exposesRedirects = timing.ExposesRedirects(OriginRecord.FromUrl(document.UrlRecord));
        if (!exposesRedirects)
        {
            timing.RedirectCount = 0;
        }

        var fetchStart = ToRelativeTime(timing.FetchStart);
        var isFetched = document.UrlRecord.Scheme is "http" or "https";
        var isSecure = document.UrlRecord.Scheme == "https";
        var timingInfo = new FetchTimingInfo
        {
            StartTime = 0,
            RedirectStartTime = exposesRedirects ? ToRelativeTime(timing.RedirectStart) : 0,
            RedirectEndTime = exposesRedirects ? ToRelativeTime(timing.RedirectEnd) : 0,
            PostRedirectStartTime = fetchStart,
            FinalNetworkRequestStartTime = fetchStart,
            FinalNetworkResponseStartTime = ToRelativeTime(timing.ResponseStart),
            FinalServiceWorkerStartTime = Math.Min(fetchStart, ToRelativeTime(timing.WorkerStart)),
            WorkerRouterEvaluationStartTime = ToRelativeTime(timing.WorkerRouterEvaluationStart),
            WorkerCacheLookupStartTime = ToRelativeTime(timing.WorkerCacheLookupStart),
            WorkerMatchedRouterSource = timing.WorkerMatchedRouterSource,
            WorkerFinalRouterSource = timing.WorkerFinalRouterSource,
            EndTime = ToRelativeTime(timing.ResponseEnd),
            FinalConnectionTimingInfo = new ConnectionTimingInfo(fetchStart, fetchStart, fetchStart, fetchStart, isSecure ? fetchStart : 0, isFetched ? "http/1.1" : "") { IsSecure = isSecure },
            TimingAllowPassed = true,
        };

        var bodyInfo = new ResponseBodyInfo(ResourceTiming.GetEncodedBodySize(headers, bodySize ?? 0), bodySize ?? 0, ResourceTiming.MinimizeSupportedMimeType(HeaderList.ExtractMimeType(headers)));
        var entry = new PerformanceNavigationTiming(window, document.Url, timing, timingInfo, bodyInfo, status, ResourceTiming.GetContentEncoding(headers), isSecure ? PerformanceServerTiming.Parse(headers) : []);
        document.NavigationTimingEntry = entry;
        window.Performance.SetNavigationTimingEntry(entry);

        double ToRelativeTime(long timestamp) => timestamp == 0 ? 0 : Math.Max(0, window.ToRelativeHighResolutionTime(timestamp));
    }

    /// <summary>
    /// The environment of a new document as a service worker client (https://w3c.github.io/ServiceWorker/#control-and-use-window-client):
    /// the document of a navigation gets the id and the active service worker of its reserved client when it has its origin;
    /// an <c>about:blank</c>, <c>about:srcdoc</c> or <c>blob:</c> document is controlled by the service worker of its parent
    /// (or creator) when it has its origin.
    /// </summary>
    private void InitializeServiceWorkerClient(Document document, ReservedClient? reservedClient)
    {
        if (reservedClient is not null && !document.Origin.IsOpaque && reservedClient.Origin == document.Origin.Serialize())
        {
            document.EnvironmentId = reservedClient.Id;
            document.ActiveServiceWorker = reservedClient.ActiveServiceWorker;
            return;
        }

        if (document.UrlRecord.Scheme is "about" or "blob" && (Parent?.Document ?? Opener?.Document) is { ActiveServiceWorker: { } parentController } parentDocument && OriginRecord.IsSameOrigin(parentDocument.Origin, document.Origin))
        {
            document.ActiveServiceWorker = parentController;
        }
    }

    /// <summary>
    /// Makes a document the active document (https://html.spec.whatwg.org/multipage/browsing-the-web.html#finalize-a-cross-document-navigation):
    /// its entry is added to the session history (<see cref="AddSessionHistoryEntry"/>), or it becomes the document of the
    /// entry that a traversal or a reload loads again.
    /// </summary>
    private void Activate(Document document, Window window, NavigationHistoryBehavior historyHandling, string? srcdoc, SessionHistoryEntry? traversalEntry = null, string requestReferrerPolicy = "", DocumentParameters? parameters = null, bool prerenderActivation = false)
    {
        var oldDocument = Document;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#finalize-a-cross-document-navigation, step 4 (and the
        // same step of a traversal): the target name of a top-level traversable that is not an auxiliary browsing context with
        // an opener is cleared when the origin changes. As in the browsers, the navigation away from the initial about:blank
        // document keeps it (its origin is opaque without opener, e.g. a popup opened with noopener).
        if (Parent is null && oldDocument is not null && !oldDocument.IsInitialAboutBlank && !(IsAuxiliary && Opener is not null)
            && !OriginRecord.IsSameOrigin(document.Origin, oldDocument.Origin))
        {
            Name = "";
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#deactivate-a-document-for-a-cross-document-navigation:
        // the previous document and its descendants are unloaded while they are still active. An eligible document is kept
        // in the back/forward cache (its pagehide event is persisted, and its unload event is not fired).
        var keepOldDocument = false;
        if (oldDocument is not null)
        {
            FirePageSwap(oldDocument, document, historyHandling, traversalEntry);
            var blockingReasons = GetBackForwardCacheBlockingReasons(oldDocument, historyHandling, traversalEntry);
            keepOldDocument = blockingReasons.Count == 0;
            UnloadDocumentAndDescendants(oldDocument, includeInitialAboutBlank: true, persisted: keepOldDocument);
            if (keepOldDocument)
            {
                AddUnloadBlockingReasons(oldDocument, blockingReasons);
                keepOldDocument = blockingReasons.Count == 0;
            }

            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigation-params-unload-timing: the times of the unload
            // event of the previous document are exposed to a document of the same origin, when no redirect is cross-origin. As in
            // the browsers, the initial about:blank document is not a previous document (its unload event times are 0).
            if (!oldDocument.IsInitialAboutBlank && document.LoadTimingInfo is { HasCrossOriginRedirects: false } loadTiming && oldDocument.UnloadEventTimes is { Start: not 0 } unloadTimes
                && OriginRecord.IsSameOrigin(oldDocument.Origin, document.Origin))
            {
                loadTiming.UnloadEventStart = unloadTimes.Start;
                loadTiming.UnloadEventEnd = unloadTimes.End;
            }
        }

        var isNewWindow = !ReferenceEquals(oldDocument?.RelevantWindow, window);
        var previousEntry = ActiveEntry;
        IsDelayingLoadEvents = false;
        _isTraversalOngoing = false;
        window.Document = document;
        document.RelevantWindow = window;
        document.BrowsingContext = this;

        // https://fetch.spec.whatwg.org/#potentially-free-deferred-fetch-quota: a document that is same origin with its
        // container document shares its deferred-fetch quota
        DeferredFetch.PotentiallyFreeQuota(document);

        SessionHistoryEntry entry;
        if (ActiveEntry is null)
        {
            entry = new SessionHistoryEntry(document.UrlRecord, new DocumentState(document) { SrcdocResource = srcdoc });
            SetInitialSessionHistoryEntry(entry);
        }
        else if (traversalEntry is not null && GetSessionHistoryEntries()?.Contains(traversalEntry) == true)
        {
            // A traversal (or a reload) to an entry whose document was not kept: the entry gets the new document
            entry = traversalEntry;
            entry.DocumentState.Document = document;
            entry.DocumentState.Origin = document.Origin;
            SetActiveEntry(entry);

            // The child navigables of the new document take over the nested histories of the previous document, and a
            // traversal restores their entries and the state of the form controls of the document
            entry.DocumentState.NestedHistoriesToTakeOver = entry.DocumentState.NestedHistories.Count > 0 ? [.. entry.DocumentState.NestedHistories] : null;
            entry.DocumentState.RestoresNestedHistories = !ReferenceEquals(traversalEntry, previousEntry);
            document.FormControlStateToRestore = ReferenceEquals(traversalEntry, previousEntry) ? null : entry.PersistedFormControlState;
            entry.PersistedFormControlState = null;
        }
        else
        {
            entry = new SessionHistoryEntry(document.UrlRecord, new DocumentState(document) { SrcdocResource = srcdoc });
            AddSessionHistoryEntry(entry, historyHandling);
        }

        // https://wicg.github.io/scroll-to-text-fragment/#extracting-the-fragment-directive: the fragment directive of the
        // URL is the directive state of the new entry; a traversal or a reload keeps the one of its entry. The entry of a
        // prerendered document that is activated gets the directive state of the activating navigation in ActivatePrerender.
        if (traversalEntry is null)
        {
            entry.DirectiveState = new DirectiveState(parameters?.FragmentDirective);
        }

        // https://wicg.github.io/scroll-to-text-fragment/#applying-directives-to-a-document: the text directives of the entry
        // are searched once the document is parsed (see Document.FinishParsingAfterDeferredScripts). The allow text directive
        // scroll flag is not set for the traversals and the reloads: the text is highlighted, and the scroll position restored.
        if (!prerenderActivation)
        {
            document.PendingTextDirectives = entry.DirectiveState.Value is { } directiveStateValue ? TextDirectives.Parse(directiveStateValue) : null;
            var userInvolvement = parameters?.UserInvolvement ?? UserNavigationInvolvement.None;
            document.TextDirectiveUserActivation = traversalEntry is null && (userInvolvement is UserNavigationInvolvement.Activation or UserNavigationInvolvement.BrowserUI || parameters is { TextDirectiveUserActivation: true });
            document.AllowTextDirectiveScroll = traversalEntry is null && document.CheckIfTextDirectiveCanBeScrolled(parameters?.InitiatorOrigin, userInvolvement, isSameDocumentNavigation: false);
        }

        if (requestReferrerPolicy.Length > 0)
        {
            entry.DocumentState.RequestReferrerPolicy = requestReferrerPolicy;
        }

        // The initiator origin and the referrer of the navigation are stored in the document state of a new entry: a
        // traversal that fetches the document again uses them (WPT history-traversal/document-state.https.html)
        if (!ReferenceEquals(entry, traversalEntry) && parameters is not null)
        {
            entry.DocumentState.RequestInitiatorOrigin = parameters.InitiatorOrigin;
            entry.DocumentState.RequestReferrer = parameters.Referrer;
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#attempt-to-populate-the-history-entry's-document: the
        // policy container of a document whose URL requires storing the policy container in history (a local URL that is not a
        // blob: URL). The document has not been parsed yet: its policies are the ones of its navigation params.
        if (document.UrlRecord.Scheme is "about" or "data")
        {
            entry.DocumentState.HistoryPolicyContainer = PolicyContainer.Clone(document);
            if (document.UrlRecord.Scheme == "about" && parameters is not null)
            {
                entry.DocumentState.HasInitiatorOrigin = true;
                entry.DocumentState.InitiatorOriginDocument = parameters.OpaqueOrigin ? null : parameters.OriginDocument;
            }
        }
        else if (document.UrlRecord.Scheme == "blob")
        {
            entry.DocumentState.HistoryRequiredContentSecurityPolicy = parameters?.RequiredContentSecurityPolicy;
        }

        document.LatestEntry = entry;
        Document = document;

        // The navigation is recorded in the history of the visited links (see Document.RecordVisitedLink); a traversal or a
        // reload is not a new visit, and a prerendered document is visited when it is activated
        if (traversalEntry is null && Options.VisitedLinks is not null && !Top.IsPrerendering)
        {
            document.RecordVisitedLink(document.UrlRecord, parameters?.InitiatorOrigin, parameters?.InitiatorTopLevelOrigin);
        }

        // The natural size of an embed or an object element is the one of the SVG document it displays (see ReplacedContent)
        if (Container is HtmlEmbedElement or HtmlObjectElement)
        {
            Container.NodeDocument.InvalidateLayout();
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#update-document-for-history-step-application, step 6.3:
        // the state of the history object of the new document (e.g. the document of an entry added by pushState() loaded again)
        if (entry.ClassicHistoryApiState is not null)
        {
            window.History.RestoreState(entry);
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#update-document-for-history-step-application: the
        // navigation API entries of the new document and its activation. The navigation of the previous document no longer
        // tracks its ongoing navigation (a successful cross-document navigation does not fire navigateerror).
        if (isNewWindow && oldDocument?.RelevantWindow?.NavigationIfCreated is { } previousNavigation)
        {
            previousNavigation.ClearOngoingNavigation();
        }

        if (!document.IsInitialAboutBlank)
        {
            var navigation = window.Navigation;
            navigation.InitializeEntriesForNewDocument(GetSessionHistoryEntriesForNavigationApi(entry), entry);
            var navigationType = traversalEntry is null
                ? (historyHandling == NavigationHistoryBehavior.Push ? NavigationType.Push : NavigationType.Replace)
                : (ReferenceEquals(traversalEntry, previousEntry) ? NavigationType.Reload : NavigationType.Traverse);
            navigation.UpdateActivation(previousEntry, navigationType, document);
        }

        if (oldDocument is not null)
        {
            if (keepOldDocument && IsDocumentInSessionHistory(oldDocument))
            {
                StoreInBackForwardCache(oldDocument);
            }
            else
            {
                DestroyDocument(oldDocument);
            }
        }

        // https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object: the initial visibility
        // state of the document is the system visibility state of the traversable ("hidden" for a prerendering traversable).
        // A prerendered document that is activated gets the state of this navigable once it is active (see ActivatePrerender).
        if (!prerenderActivation)
        {
            document.InitializeVisibilityState(IsVisible);

            // https://w3c.github.io/ServiceWorker/#dfn-service-worker-client: the window of the document is a service worker client
            ServiceWorkerRegistry.OnEnvironmentCreated(window, ClientType.Window);
        }

        // The prerender record is completed once the prerendering traversable has its document
        if (Parent is null && IsPrerendering && !document.IsInitialAboutBlank && PrerenderRecord is { State: Html.PrefetchState.Ongoing } prerenderRecord)
        {
            prerenderRecord.SetState(Html.PrefetchState.Completed);
        }

        if (Options.ScriptEngine is { } engine)
        {
            if (isNewWindow && !prerenderActivation)
            {
                engine.CreateRealm(window);
            }

            engine.OnDocumentActivated(document);
        }

        DocumentActivated?.Invoke(document);
        if (!document.IsInitialAboutBlank)
        {
            Options.DocumentActivated?.Invoke(this, document);
        }

        // https://html.spec.whatwg.org/multipage/dom.html#reveal: the document of a navigation with a view transition is revealed
        // at its first rendering opportunity (the other documents are revealed when the pagereveal event is observed)
        if (document.InboundViewTransitionParams is not null)
        {
            EventLoop.RequestReveal(document);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#deactivate-a-document-for-a-cross-document-navigation: fires
    /// the <c>pageswap</c> event at the window of the document that is replaced (https://html.spec.whatwg.org/multipage/nav-history-apis.html#fire-the-pageswap-event),
    /// with the activation of the navigation when the new document has the same origin, and with the outbound view transition
    /// when the navigation can trigger a cross-document view transition
    /// (https://drafts.csswg.org/css-view-transitions-2/#navigation-can-trigger-a-cross-document-view-transition: the old
    /// document opts in with <c>@view-transition { navigation: auto }</c>, the navigation is not a reload and the documents have
    /// the same origin). The old state is then captured, and given to the new document (its inbound view transition params).
    /// </summary>
    private void FirePageSwap(Document oldDocument, Document newDocument, NavigationHistoryBehavior historyHandling, SessionHistoryEntry? traversalEntry)
    {
        if (oldDocument.IsUnloaded || oldDocument.RelevantWindow is not { } oldWindow || !ReferenceEquals(oldWindow.Document, oldDocument))
            return;

        var navigationType = traversalEntry is null
            ? (historyHandling == NavigationHistoryBehavior.Push ? NavigationType.Push : NavigationType.Replace)
            : (ReferenceEquals(traversalEntry, ActiveEntry) ? NavigationType.Reload : NavigationType.Traverse);
        var sameOrigin = OriginRecord.IsSameOrigin(oldDocument.Origin, newDocument.Origin);

        ViewTransition? transition = null;
        if (sameOrigin && navigationType != NavigationType.Reload)
        {
            transition = oldDocument.SetupOutboundViewTransition();
        }

        // Nothing observes the event (and there is no transition to capture): the activation is not created
        if (transition is null && !oldWindow.HasEventListener("pageswap"))
            return;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#fire-the-pageswap-event: no activation for a new document
        // that was created via cross-origin redirects (a document restored from the session history has a latest entry)
        NavigationActivation? activation = null;
        var createdViaCrossOriginRedirects = newDocument.LoadTimingInfo is { HasCrossOriginRedirects: true } && newDocument.LatestEntry is null;
        if (sameOrigin && !createdViaCrossOriginRedirects && Options.EffectiveFeatures.NavigationApiEnabled)
        {
            var navigation = oldWindow.Navigation;
            var destinationEntry = navigationType switch
            {
                NavigationType.Reload => navigation.CurrentEntryInternal,
                NavigationType.Traverse => navigation.FindEntry(traversalEntry!),
                _ => new NavigationHistoryEntry(navigation, new SessionHistoryEntry(newDocument.UrlRecord, new DocumentState(newDocument))),
            };
            activation = new NavigationActivation { From = navigation.CurrentEntryInternal, Entry = destinationEntry, NavigationType = navigationType };
        }

        var start = EventLoop.TimeProvider.GetTimestamp();
        using (oldWindow.ScriptEngine?.PrepareToRunScript(oldWindow))
        {
            oldWindow.FireEvent(new PageSwapEvent("pageswap", new PageSwapEventInit { Activation = activation, ViewTransition = transition }));
        }

        // "To skip a transition after a timeout": a navigation delayed for too long by the pageswap event has no transition
        if (transition is not null && EventLoop.TimeProvider.GetElapsedTime(start) > ViewTransition.UpdateCallbackTimeout)
        {
            transition.SkipOutbound(new DomException("The pageswap event delayed the navigation for too long.", "TimeoutError"));
        }

        if (transition is not null && !IsDestroyed)
        {
            newDocument.InboundViewTransitionParams = oldDocument.CaptureOutboundViewTransition(transition);
        }
    }

    /// <summary>Called when a document becomes the active document of the browsing context (e.g. the end of a navigation).</summary>
    internal Action<Document>? DocumentActivated { get; set; }

    /// <summary>Called when a navigation ends without creating a document (e.g. a URL handed off to external software, a 204 response): the active document stays.</summary>
    internal Action? NavigationEndedWithoutDocument { get; set; }

    /// <summary>
    /// Unloads the active document (https://html.spec.whatwg.org/multipage/document-lifecycle.html#unload-a-document) and
    /// destroys it (the traversable is closed).
    /// </summary>
    private static void UnloadAndDestroy(Document document)
    {
        UnloadDocumentAndDescendants(document, includeInitialAboutBlank: true);
        DestroyDocument(document);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-lifecycle.html#unload-a-document-and-its-descendants: the documents of
    /// the child navigables are unloaded first, then the pagehide and unload events are fired at the window of the document.
    /// </summary>
    /// <param name="document">The document to unload.</param>
    /// <param name="includeInitialAboutBlank">
    /// Whether <paramref name="document"/> is unloaded when it is an initial about:blank document. As in the browsers, a
    /// navigation unloads it (before the new document reuses its window, if it does), and so does the closing of its
    /// top-level traversable: the pagehide and unload events are fired at it (its page showing flag is not set, so the
    /// specification fires no pagehide). The initial about:blank documents of the descendant navigables are always unloaded,
    /// as in the browsers (e.g. <c>visibilitychange</c> is fired at the empty frames of a removed frame). The removal of a
    /// container does not unload it (see <see cref="UnloadRemovedChildNavigables"/>).
    /// </param>
    /// <param name="persisted">
    /// Whether the documents are kept in the back/forward cache ("intend to keep in bfcache", their salvageable state): the
    /// <c>pagehide</c> event is fired with <c>persisted</c> set to true, the <c>unload</c> event is not fired, and the
    /// unloading document cleanup steps of an unsalvageable document (e.g. closing the event sources) do not run.
    /// </param>
    private static void UnloadDocumentAndDescendants(Document document, bool includeInitialAboutBlank = false, bool persisted = false)
    {
        if (document.IsUnloaded)
            return;

        if (document.ChildNavigables is { } children)
        {
            foreach (var child in children.ToArray())
            {
                if (!child.IsDestroyed)
                {
                    UnloadDocumentAndDescendants(child.Document, includeInitialAboutBlank: true, persisted);
                }
            }
        }

        if ((includeInitialAboutBlank || !document.IsInitialAboutBlank) && document.RelevantWindow is { } oldWindow && ReferenceEquals(oldWindow.Document, document))
        {
            document.IsUnloaded = true;

            // document.open() and document.write() are ignored by the pagehide, visibilitychange and unload event handlers (the
            // unload counter), and window.open() opens nothing (the termination nesting level)
            document.UnloadCounter++;
            oldWindow.EventLoop.TerminationNestingLevel++;
            try
            {
                if (document.PageShowing || document.IsInitialAboutBlank)
                {
                    document.PageShowing = false;
                    oldWindow.FireLegacyTargetOverrideEvent(new PageTransitionEvent("pagehide", new PageTransitionEventInit { Persisted = persisted, Bubbles = true, Cancelable = true }));
                }

                // https://html.spec.whatwg.org/multipage/document-lifecycle.html#unload-a-document, step 7
                document.UpdateVisibilityState(visible: false);

                // The "unload" policy-controlled feature (https://github.com/fergald/docs/blob/master/explainers/permissions-policy-unload.md,
                // implemented by Chromium): the unload event is not fired at a document whose permissions policy disables it.
                // The unload event of a salvageable document (kept in the back/forward cache) is not fired.
                var unloadEventStart = oldWindow.EventLoop.TimeProvider.GetTimestamp();
                if (!persisted && PermissionsPolicyAlgorithms.IsFeatureEnabled(PolicyControlledFeature.Unload, document, document.Origin, report: oldWindow.HasEventListener("unload")))
                {
                    oldWindow.FireLegacyTargetOverrideEvent(new Event("unload"));
                }

                document.UnloadEventTimes = (unloadEventStart, oldWindow.EventLoop.TimeProvider.GetTimestamp());
            }
            finally
            {
                oldWindow.EventLoop.TerminationNestingLevel--;
                document.UnloadCounter--;
            }

            // https://html.spec.whatwg.org/multipage/document-lifecycle.html#unloading-document-cleanup-steps: the WebSockets of the window disappear,
            // and its event sources are forcibly closed
            // (https://fullscreen.spec.whatwg.org/#unloading-document-cleanup-steps: the document fully exits fullscreen; the
            // pointer lock of one of its elements is exited; https://w3c.github.io/web-locks/#lock-termination: its locks are
            // released and its lock requests are aborted)
            FullscreenModel.FullyExit(document);
            PointerLockModel.OnDocumentUnloaded(document);
            if (persisted)
                return;

            WebLocks.OnEnvironmentDiscarded(oldWindow);
            oldWindow.ReleaseFileSystemResources();
            WebSocket.MakeDisappear(oldWindow);
            EventSource.ForciblyClose(oldWindow);
            IdbFactory.CloseConnections(oldWindow);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#destroy-a-document-and-its-descendants</summary>
    private static void DestroyDocument(Document document)
    {
        // The pointer lock of an element of the document is exited (https://w3c.github.io/pointerlock/#requirements), and
        // the locks of its window are released (https://w3c.github.io/web-locks/#lock-termination)
        PointerLockModel.OnDocumentUnloaded(document);
        if (document.RelevantWindow is { } destroyedWindow && ReferenceEquals(destroyedWindow.Document, document))
        {
            WebLocks.OnEnvironmentDiscarded(destroyedWindow);
            destroyedWindow.ReleaseFileSystemResources();
        }
        if (document.ChildNavigables is { } children)
        {
            foreach (var child in children.ToArray())
            {
                if (child.Container is { } container)
                {
                    NavigableContainers.SetContentNavigable(container, null);
                }

                child.DestroyDocuments();
            }

            children.Clear();
        }

        // The navigational prefetches and the prerendering navigables of the speculation rules of the document
        Html.NavigationalPrefetch.DiscardAll(document);

        // A traversal to an entry of a destroyed document loads it again, and restores the state of its form controls
        // (https://html.spec.whatwg.org/multipage/browsing-the-web.html#persisted-user-state-restoration)
        if (document.LatestEntry is { DocumentState: { } documentState } latestEntry && ReferenceEquals(documentState.Document, document))
        {
            documentState.Document = null;
            documentState.DocumentReferrerPolicy = document.ReferrerPolicy;
            latestEntry.PersistedFormControlState = FormControlState.Save(document);
        }

        // https://fetch.spec.whatwg.org/#concept-fetch-group-terminate: the fetches of the document are terminated, and its
        // deferred fetches are processed
        document.FetchGroup.Abort();
        DeferredFetch.ProcessAll(document);

        // The media elements of a document that is no longer fully active stop playing: their players are released
        if (document.MediaElementsWithPlayer is { Count: > 0 } mediaElements)
        {
            foreach (var mediaElement in mediaElements.ToArray())
            {
                mediaElement.OnDocumentDestroyed();
            }
        }

        // https://w3c.github.io/ServiceWorker/#on-client-unload-algorithm: the document is no longer a service worker client
        ServiceWorkerRegistry.OnEnvironmentDiscarded(document);

        // https://html.spec.whatwg.org/multipage/workers.html#the-worker's-lifetime: the dedicated workers of the window of
        // the document are terminated (their owner set is empty)
        if (document.RelevantWindow is { } window && ReferenceEquals(window.Document, document))
        {
            window.TerminateWorkers();
        }

        // https://html.spec.whatwg.org/multipage/worklets.html#worklets-lifetime: the global scopes of the worklets of the
        // document are terminated
        document.TerminateWorklets();

        // The realtime audio contexts of the document are closed
        document.CloseAudioContexts();

        // https://w3c.github.io/FileAPI/#lifeTime: the blob URLs created by the document are revoked
        document.BrowsingContext?.BlobUrls.RemoveEntries(document);

        // The broadcast channels of the window of the document can no longer receive messages
        if (document.RelevantWindow is { } documentWindow && ReferenceEquals(documentWindow.Document, document))
        {
            BroadcastChannel.RemoveChannels(documentWindow);
            WebSocket.MakeDisappear(documentWindow);
            EventSource.ForciblyClose(documentWindow);
            IdbFactory.CloseConnections(documentWindow);
        }

        // The WindowProxy of the browsing context keeps the window of its last active document (e.g. the contentWindow of a removed iframe)
        if (document.BrowsingContext is { } browsingContext && ReferenceEquals(browsingContext.Document, document)
            && document.RelevantWindow is { } activeWindow && ReferenceEquals(activeWindow.Document, document))
        {
            activeWindow.DiscardedBrowsingContext = browsingContext.ActiveBrowsingContext;
        }

        document.BrowsingContext = null;
    }

    /// <summary>Destroys the documents of this browsing context.</summary>
    private void DestroyDocuments()
    {
        // https://html.spec.whatwg.org/multipage/document-sequences.html#destroy-a-child-navigable, step 4
        if (Parent is not null)
        {
            Document.RelevantWindow?.NavigationIfCreated?.InformAboutChildNavigableDestruction();
        }

        IsDestroyed = true;
        CancelOngoingNavigation();
        IsDelayingLoadEvents = false;
        ClearBackForwardCache();
        var window = Document.RelevantWindow;
        DestroyDocument(Document);
        if (window is not null)
        {
            Options.ScriptEngine?.DestroyRealm(window);
        }

        Options.BrowsingContextDestroyed?.Invoke(this);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#create-a-new-child-navigable</summary>
    internal static void CreateChildNavigable(Element element)
    {
        var parentDocument = element.NodeDocument;
        var parent = parentDocument.BrowsingContext!;
        var navigable = new BrowsingContext(parent.Options, parent, element, groupOwner: null, creator: null, name: "");
        if (!navigable.TryTakeOverNestedHistory())
        {
            navigable.AddChildNavigableHistory();
        }

        NavigableContainers.SetContentNavigable(element, navigable);
        (parentDocument.ChildNavigables ??= []).Add(navigable);
        parent.Options.ScriptEngine?.OnChildNavigablesChanged(parent.Window);
        parent.Options.BrowsingContextCreated?.Invoke(navigable);
    }

    /// <summary>
    /// Unloads the documents of the child navigables whose container is a shadow-including inclusive descendant of
    /// <paramref name="node"/> (or a descendant when <paramref name="descendantsOnly"/> is true), before the node is removed:
    /// the HTML Standard destroys the child navigable of a removed container without unloading its document
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#destroy-a-child-navigable), but the browsers fire the
    /// pagehide and unload events before they remove the node, so the event handlers may change the tree (the callers check
    /// the tree again). The initial about:blank documents are not unloaded, as tested by WPT
    /// dom/nodes/insertion-removing-steps/insertion-removing-steps-iframe.window.js (WPT
    /// html/syntax/parsing/adoption-agency-reparenting-pagehide.tentative.html expects the opposite).
    /// </summary>
    internal static void UnloadRemovedChildNavigables(Node node, bool descendantsOnly = false)
    {
        if (node.NodeDocument.ChildNavigables is not { Count: > 0 } navigables || !node.IsConnected)
            return;

        foreach (var navigable in navigables.ToArray())
        {
            if (!navigable.IsDestroyed && navigable.Container is { } container && node.IsShadowIncludingInclusiveAncestorOf(container) && !(descendantsOnly && ReferenceEquals(container, node)))
            {
                UnloadDocumentAndDescendants(navigable.Document);
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#destroy-a-child-navigable</summary>
    internal static void DestroyChildNavigable(Element container)
    {
        if (NavigableContainers.GetContentNavigable(container) is not { } navigable)
            return;

        NavigableContainers.SetContentNavigable(container, null);
        navigable.DestroyDocuments();
        navigable.RemoveChildNavigableHistory();
        var parentDocument = navigable.Parent?.Document;
        parentDocument?.ChildNavigables?.Remove(navigable);
        if (navigable.Parent is { Options.ScriptEngine: { } engine } parent)
        {
            engine.OnChildNavigablesChanged(parent.Window);
        }

        // The navigable no longer delays the load event of the parent document
        parentDocument?.TryQueueLoadEvent();
    }

    /// <summary>
    /// Discards a top-level browsing context: destroys its documents and the nested browsing contexts
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#destroy-a-top-level-traversable), aborts the
    /// ongoing navigations, and disposes the event loop: the pending tasks, microtasks and timers are discarded, and the
    /// ones queued afterwards are ignored. No event is fired (use <see cref="Window.Close"/> to unload the document first).
    /// </summary>
    /// <exception cref="InvalidOperationException">The browsing context is a nested browsing context: it is destroyed when its container element is removed.</exception>
    /// <remarks>The auxiliary browsing contexts of the group are discarded too. Disposing an auxiliary browsing context only discards it.</remarks>
    public void Dispose()
    {
        if (Parent is not null)
            throw new InvalidOperationException("A nested browsing context is destroyed when its container element is removed from the document");

        Options.LocalStorage?.RemoveBrowsingContext(this);

        if (_auxiliaryBrowsingContexts is { } auxiliaries)
        {
            foreach (var auxiliary in auxiliaries.ToArray())
            {
                if (!auxiliary.IsDestroyed)
                {
                    auxiliary.DestroyDocuments();
                }
            }

            auxiliaries.Clear();
        }

        if (!IsDestroyed)
        {
            DestroyDocuments();
        }

        if (_groupOwner is { } owner)
        {
            owner._auxiliaryBrowsingContexts?.Remove(this);
            return;
        }

        EventLoop.Dispose();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#close-a-top-level-traversable: fires beforeunload at
    /// the documents of the traversable (there is no prompt: the unloading is never canceled), then unloads and destroys them.
    /// </summary>
    internal void Close()
    {
        if (IsDestroyed)
            return;

        if (FireBeforeUnload())
            return;

        Destroy();
    }

    /// <summary>
    /// Closes a top-level browsing context as the user closes the tab of a browser
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#close-a-top-level-traversable): the <c>beforeunload</c> event
    /// is fired (when <paramref name="runBeforeUnload"/> is true; the user can cancel the closing, see
    /// <see cref="BrowsingContextOptions.UserPromptHandler"/>), then the documents are unloaded (<c>pagehide</c> and <c>unload</c>)
    /// and destroyed (<see cref="BrowsingContextOptions.BrowsingContextDestroyed"/> is invoked). Unlike <see cref="Dispose"/>, the
    /// event loop of the browsing context group is not disposed: the other top-level browsing contexts of the group (the
    /// auxiliary browsing contexts) keep running.
    /// </summary>
    /// <param name="runBeforeUnload">Whether the <c>beforeunload</c> event is fired first.</param>
    /// <returns>False when the user canceled the closing, else true.</returns>
    /// <exception cref="InvalidOperationException">The browsing context is a nested browsing context.</exception>
    public bool TryClose(bool runBeforeUnload = false)
    {
        if (Parent is not null)
            throw new InvalidOperationException("A nested browsing context is destroyed when its container element is removed from the document");

        if (IsDestroyed)
            return true;

        if (runBeforeUnload && FireBeforeUnload())
            return false;

        Destroy();
        return true;
    }

    /// <summary>Destroys a top-level browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#destroy-a-top-level-traversable).</summary>
    internal void Destroy()
    {
        if (IsDestroyed)
            return;

        var window = Document.RelevantWindow;
        ClearBackForwardCache();
        UnloadAndDestroy(Document);
        IsDestroyed = true;
        _groupOwner?._auxiliaryBrowsingContexts?.Remove(this);

        // The window of the opener (else the first window of the group) is the active window again
        if (_hasSystemFocus && _groupOwner is { } groupOwner)
        {
            _hasSystemFocus = false;
            var next = Opener?.Top is { IsDestroyed: false } opener ? opener : groupOwner;
            if (!next.IsDestroyed)
            {
                next.SetSystemFocus(true);
            }
        }
        if (window is not null)
        {
            Options.ScriptEngine?.DestroyRealm(window);
        }

        Options.BrowsingContextDestroyed?.Invoke(this);
    }

    /// <summary>The number of times the loading was stopped (window.stop()): a planned navigation of a form is canceled by a stop (https://github.com/whatwg/html/issues/3730).</summary>
    internal int StopCount { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#nav-stop</summary>
    internal void StopLoading()
    {
        StopCount++;
        CancelOngoingNavigation();
        Document.AbortDocumentAndDescendants();
        if (IsDelayingLoadEvents)
        {
            IsDelayingLoadEvents = false;
            Parent?.Document.TryQueueLoadEvent();
        }
    }

    /// <summary>
    /// Reloads the active document, as the reload button of a browser
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#reload, with the "browser UI" user navigation involvement:
    /// the <c>navigate</c> event of the navigation API cannot cancel it). The document is loaded again by
    /// <see cref="BrowsingContextOptions.DocumentLoader"/>, from a task of the event loop.
    /// </summary>
    public void Reload() => ReloadNavigable(userInvolvement: UserNavigationInvolvement.BrowserUI);

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#reload</summary>
    /// <param name="navigationApiState">The navigation API state of the entry (<c>navigation.reload()</c>), or null.</param>
    /// <param name="apiMethodTracker">The navigation API method tracker of <c>navigation.reload()</c>, or null.</param>
    /// <param name="userInvolvement">The user navigation involvement.</param>
    internal void ReloadNavigable(NavigationApiStateValue? navigationApiState = null, NavigationApiMethodTracker? apiMethodTracker = null, UserNavigationInvolvement userInvolvement = UserNavigationInvolvement.None)
    {
        if (Document.UnloadCounter > 0)
            return;

        // The navigate event of the reload, which can cancel or intercept it
        var entry = ActiveEntry;
        if (userInvolvement != UserNavigationInvolvement.BrowserUI && Document.RelevantWindow is { } window)
        {
            var destinationNavigationApiState = navigationApiState is null ? entry.NavigationApiState : navigationApiState.Value;
            if (!window.Navigation.FirePushReplaceReloadNavigateEvent(NavigationType.Reload, entry.Url, isSameDocument: false, userInvolvement, navigationApiState: destinationNavigationApiState, apiMethodTracker: apiMethodTracker))
                return;

            if (IsDestroyed || !ReferenceEquals(entry, ActiveEntry))
                return;
        }

        if (navigationApiState is not null)
        {
            entry.NavigationApiState = navigationApiState.Value;
        }

        // The active entry is populated again (its document state is reload pending)
        Navigate(entry.Url, Document, NavigationHistoryBehavior.Replace, entry.DocumentState.SrcdocResource, traversalEntry: entry);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate-to-a-fragment</summary>
    private void NavigateToFragment(UrlRecord url, NavigationHistoryBehavior historyHandling, UserNavigationInvolvement userInvolvement = UserNavigationInvolvement.None, Element? sourceElement = null, NavigationApiStateValue? navigationApiState = null, NavigationApiMethodTracker? apiMethodTracker = null, string? fragmentDirective = null, OriginRecord? initiatorOrigin = null)
    {
        var document = Document;
        var window = Window;

        // The navigate event of the fragment navigation, which can cancel or intercept it
        var destinationNavigationApiState = navigationApiState is null ? ActiveEntry.NavigationApiState : navigationApiState.Value;
        var navigationType = historyHandling == NavigationHistoryBehavior.Push ? NavigationType.Push : NavigationType.Replace;
        if (!window.Navigation.FirePushReplaceReloadNavigateEvent(navigationType, url, isSameDocument: true, userInvolvement, sourceElement, navigationApiState: destinationNavigationApiState, apiMethodTracker: apiMethodTracker))
            return;

        if (IsDestroyed || !ReferenceEquals(document, Document))
            return;

        // https://wicg.github.io/scroll-to-text-fragment/#extracting-the-fragment-directive: "when only the fragment has changed
        // and it did not specify a directive, the active entry's directive state is reused. This prevents a fragment change from
        // clobbering highlights"
        var entry = new SessionHistoryEntry(url, ActiveEntry.DocumentState)
        {
            ScrollRestoration = ActiveEntry.ScrollRestoration,
            NavigationApiState = destinationNavigationApiState,
            DirectiveState = fragmentDirective is null ? ActiveEntry.DirectiveState : new DirectiveState(fragmentDirective),
        };
        AddSessionHistoryEntry(entry, historyHandling, isSameDocument: true);
        document.SetUrl(url);
        UpdateDocumentForHistoryStepApplication(document, entry, navigationType);

        // The indicated part of the document is determined again
        document.IsTargetTextIndicated = false;
        var allowTextDirectiveScroll = document.CheckIfTextDirectiveCanBeScrolled(initiatorOrigin, userInvolvement, isSameDocumentNavigation: true);
        ScrollToTheFragment(document, allowTextDirectiveScroll);

        // "Set navigable's active document's pending text directives to null". As in Firefox, the text directives that are not
        // found in a document that is still being parsed stay pending: they are searched again when its parsing ends.
        if (document.PendingTextDirectives is not null && document.TargetTextRanges is null && document.ActiveParser is not null)
        {
            document.AllowTextDirectiveScroll = allowTextDirectiveScroll;
        }
        else
        {
            document.PendingTextDirectives = null;
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#scroll-to-the-fragment-identifier: the target element
    /// is the indicated part (<see cref="Css.Selectors.HtmlElementStates.GetTargetElement"/>), its hidden ancestors are
    /// revealed, it is scrolled into view (block start, inline nearest; once the document is laid out, see
    /// <see cref="Css.Layout.LayoutEngine.ScrollIntoViewWhenLaidOut"/>), it is focused (or the viewport when it is not
    /// focusable), and it becomes the sequential focus navigation starting point. The <c>top</c> fragment scrolls to the
    /// beginning of the document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="allowTextDirectiveScroll">Whether the document can scroll to the range of a text directive (https://wicg.github.io/scroll-to-text-fragment/#check-if-a-text-directive-can-be-scrolled).</param>
    /// <param name="scrollToBeginning">Whether the document scrolls to its beginning when its indicated part is null (the scroll behavior of the navigation API).</param>
    internal static void ScrollToTheFragment(Document document, bool allowTextDirectiveScroll = false, bool scrollToBeginning = false)
    {
        document.UpdateTargetFragment();

        // https://wicg.github.io/scroll-to-text-fragment/#invoking-text-directives: the first range of the pending text
        // directives is the indicated part of the document, and all the ranges are its target text (::target-text). Its target
        // element is the first common ancestor of its start and its end that is an element, whose hidden ancestors are
        // revealed, and the range is scrolled into view, centered in the block axis. As in the browsers, the target element is not focused (a link would be followed by the
        // Enter key): it only is the sequential focus navigation starting point. When the document is not allowed to scroll
        // to a text directive, the text is only highlighted, and the indicated part is the one of the fragment (the
        // specification returns: as in the browsers, the element of the fragment is still scrolled to).
        if (document.InvokePendingTextDirectives() is { } targetText && allowTextDirectiveScroll)
        {
            document.IsTargetTextIndicated = true;
            if (Css.Selectors.HtmlElementStates.GetTargetElement(document) is not { } targetTextElement)
                return;

            HtmlElement.RunAncestorRevealingAlgorithm(targetTextElement);
            if (!targetTextElement.IsConnected || !ReferenceEquals(targetTextElement.NodeDocument, document))
                return;

            if (Css.Cascade.ComputedStyleDeclarations.IsBeingRendered(targetTextElement))
            {
                document.GetLayoutEngine().ScrollIntoViewWhenLaidOut(targetTextElement, ScrollLogicalPosition.Center, ScrollLogicalPosition.Nearest, targetText);
            }

            if (targetText.StartContainer is { IsConnected: true } startContainer)
            {
                document.SequentialFocusNavigationStartingPoint = new(startContainer);
            }

            return;
        }

        // The indicated part is still the range of the text directives that were found before (the document scrolls to its
        // fragment again when its parsing ends)
        if (document.IsTargetTextIndicated && document.PendingTextDirectives is null)
            return;

        document.IsTargetTextIndicated = false;
        if (Css.Selectors.HtmlElementStates.GetTargetElement(document) is not { } target)
        {
            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#the-indicated-part-of-the-document: "If fragment is the
            // empty string or an ASCII case-insensitive match for "top", then return the top of the document", which "scrolls to
            // the beginning of the document" (https://drafts.csswg.org/cssom-view/#scroll-to-the-beginning-of-the-document)
            if ((scrollToBeginning || (document.UrlRecord.Fragment is { } fragment && (fragment.Length == 0 || fragment.Equals("top", StringComparison.OrdinalIgnoreCase))))
                && document.LayoutEngineOrNull is { IsLayoutRequested: true } engine && engine.EnsureLayout() is not null)
            {
                _ = engine.PerformScroll(engine.ViewportScrollState, Css.Layout.PhysicalOffset.Zero, ScrollBehavior.Auto, document.DocumentElement);
            }

            return;
        }

        HtmlElement.RunAncestorRevealingAlgorithm(target);
        if (!target.IsConnected || !ReferenceEquals(target.NodeDocument, document))
            return;

        // "Scroll target into view, with behavior "auto", block "start", and inline "nearest""
        if (Css.Cascade.ComputedStyleDeclarations.IsBeingRendered(target))
        {
            document.GetLayoutEngine().ScrollIntoViewWhenLaidOut(target, ScrollLogicalPosition.Start, ScrollLogicalPosition.Nearest, snapsToTarget: true);
        }

        FocusModel.RunFocusingSteps(target, fallbackTarget: document);
        document.SequentialFocusNavigationStartingPoint = new(target);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#update-document-for-history-step-application (for same-document navigations).</summary>
    private void UpdateDocumentForHistoryStepApplication(Document document, SessionHistoryEntry entry, NavigationType navigationType)
    {
        var oldUrl = document.LatestEntry?.Url ?? document.UrlRecord;
        if (ReferenceEquals(document.LatestEntry, entry))
            return;

        // https://wicg.github.io/scroll-to-text-fragment/#applying-directives-to-a-document: the text directives of an entry
        // whose directive state differs. A traversal does not scroll to the fragment: the text is only highlighted.
        if (!ReferenceEquals(document.LatestEntry?.DirectiveState, entry.DirectiveState))
        {
            document.PendingTextDirectives = TextDirectives.Parse(entry.DirectiveState.Value);
            if (navigationType == NavigationType.Traverse)
            {
                _ = document.InvokePendingTextDirectives();
                document.PendingTextDirectives = null;
                document.IsTargetTextIndicated = false;
            }
        }

        document.LatestEntry = entry;
        var window = document.RelevantWindow!;
        window.History.RestoreState(entry);
        window.Navigation.UpdateEntriesForSameDocumentNavigation(entry, navigationType);
        window.FireEvent(new PopStateEvent("popstate", new PopStateEventInit { State = window.History.StateValue }));
        if (navigationType == NavigationType.Traverse)
        {
            RestorePersistedState(entry);
        }

        if (oldUrl.Fragment != entry.Url.Fragment)
        {
            var oldUrlString = oldUrl.Serialize();
            var newUrlString = entry.Url.Serialize();
            document.QueueGlobalTask(() => window.FireEvent(new HashChangeEvent("hashchange", new HashChangeEventInit { OldUrl = oldUrlString, NewUrl = newUrlString })));
        }

        Options.SameDocumentNavigated?.Invoke(this);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#url-and-history-update-steps</summary>
    internal void UpdateUrlAndHistory(UrlRecord newUrl, object? serializedData, bool hasSerializedData, NavigationHistoryBehavior historyHandling)
    {
        var document = Document;
        var activeEntry = ActiveEntry;
        var newEntry = new SessionHistoryEntry(newUrl, activeEntry.DocumentState)
        {
            ClassicHistoryApiState = hasSerializedData ? serializedData : activeEntry.ClassicHistoryApiState,
            ScrollRestoration = activeEntry.ScrollRestoration,
        };

        if (document.IsInitialAboutBlank || Top.IsPrerendering)
        {
            historyHandling = NavigationHistoryBehavior.Replace;
        }

        AddSessionHistoryEntry(newEntry, historyHandling, isSameDocument: true);
        if (hasSerializedData)
        {
            document.RelevantWindow!.History.RestoreState(newEntry);
        }

        document.SetUrl(newUrl, keepTargetElement: true);
        document.LatestEntry = newEntry;
        document.RelevantWindow?.Navigation.UpdateEntriesForSameDocumentNavigation(newEntry, historyHandling == NavigationHistoryBehavior.Push ? NavigationType.Push : NavigationType.Replace);
        Options.SameDocumentNavigated?.Invoke(this);
    }

    /// <summary>The parameters of the document being created by a navigation.</summary>
    private sealed class DocumentParameters
    {
        /// <summary>The response of the navigation, for a media document (see <see cref="Document.MediaDocumentResource"/>).</summary>
        public DocumentResource? MediaDocumentResource { get; set; }

        /// <summary>Whether the document shows a text resource (https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-text).</summary>
        public bool IsTextDocument { get; set; }

        /// <summary>
        /// The URL of the document when it is not the URL of the response: the URL of a navigation that uses the response of a
        /// navigational prefetch that was not redirected, or the current URL of the request when a service worker responded
        /// with the response of another URL.
        /// </summary>
        public UrlRecord? PrefetchNavigationUrl { get; set; }

        public NavigationHistoryBehavior HistoryHandling { get; init; }

        /// <summary>The timing of the navigation (https://w3c.github.io/navigation-timing/), or null for a document that is loaded at once.</summary>
        public DocumentLoadTimingInfo? Timing { get; set; }

        /// <summary>The size of the body of the response, or null when it arrives after the document is created.</summary>
        public ulong? BodySize { get; set; }

        public string Referrer { get; set; } = "";

        /// <summary>The referrer policy of the request of the document, which determines the referrer after a redirect.</summary>
        public string RequestReferrerPolicy { get; init; } = "";

        /// <summary>The referrer policy of the policy container of the document (the <c>Referrer-Policy</c> header, or the one of the initiator of an about:blank or srcdoc document).</summary>
        public string ReferrerPolicy { get; set; } = "";

        /// <summary>The <c>Last-Modified</c> header of the response (https://html.spec.whatwg.org/multipage/dom.html#dom-document-lastmodified).</summary>
        public DateTimeOffset? LastModified { get; set; }

        /// <summary>The language of the <c>Content-Language</c> header of the response (https://html.spec.whatwg.org/multipage/dom.html#language).</summary>
        public string? ContentLanguage { get; set; }

        /// <summary>The HTTP status code of the response (the status of the violations of the content security policies, https://w3c.github.io/webappsec-csp/#violation-status).</summary>
        public int Status { get; set; } = 200;

        /// <summary>The headers of the response, whose content security policies apply to the document (https://w3c.github.io/webappsec-csp/#parse-response-csp).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; set; } = [];

        public string? Srcdoc { get; set; }

        /// <summary>The session history entry whose document a history traversal or a reload loads again, or null.</summary>
        public SessionHistoryEntry? TraversalEntry { get; set; }

        /// <summary>Whether the request of the navigation is a <c>POST</c> request (a form submission).</summary>
        public bool IsPostNavigation { get; init; }

        /// <summary>The content type of a document that the HTML parser creates for another type (a text document or a media document), or null.</summary>
        public string? ContentType { get; set; }

        /// <summary>The fragment directive removed from the URL of the navigation (or of its last redirect), or null (https://wicg.github.io/scroll-to-text-fragment/#fragment-directive).</summary>
        public string? FragmentDirective { get; set; }

        /// <summary>https://wicg.github.io/scroll-to-text-fragment/#navigation-params-user-involvement</summary>
        public UserNavigationInvolvement UserInvolvement { get; init; }

        /// <summary>The origin of the document that initiated the navigation, or null.</summary>
        public OriginRecord? InitiatorOrigin { get; init; }

        /// <summary>The origin of the top-level document of the document that started the navigation, or null (the partition of the visited links).</summary>
        public OriginRecord? InitiatorTopLevelOrigin { get; init; }

        /// <summary>https://wicg.github.io/scroll-to-text-fragment/#request-text-directive-user-activation (of the request of the navigation)</summary>
        public bool TextDirectiveUserActivation { get; init; }

        /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigation-params-iframe-referrer-policy: the referrer policy of the iframe container when the navigation started, or null to get it now.</summary>
        public string? IframeElementReferrerPolicy { get; init; }

        /// <summary>The sandboxing flags of the navigable when the navigation started (the target snapshot params), or null to get them when the document is created.</summary>
        public SandboxingFlags? TargetSandboxingFlags { get; init; }

        /// <summary>Whether the source document allows downloading (the source snapshot params): it is not sandboxed without allow-downloads.</summary>
        public bool SourceAllowsDownloading { get; init; } = true;

        public Document? OriginDocument { get; set; }

        public bool OpaqueOrigin { get; set; }

        /// <summary>Whether the document is the error document of a navigation whose response is a network error.</summary>
        public bool IsNetworkError { get; set; }

        public UrlRecord? AboutBaseUrl { get; set; }

        /// <summary>The opener policy of the response of a top-level navigation, or null for the default policy.</summary>
        public OpenerPolicy? OpenerPolicy { get; set; }

        /// <summary>The result of the enforcement of the opener policy of the response, which tells whether the navigation switches browsing context group.</summary>
        public OpenerPolicyEnforcementResult? OpenerPolicyEnforcementResult { get; set; }

        /// <summary>The reserved client of the fetch of the navigation (its id and its active service worker), or null.</summary>
        public ReservedClient? ReservedClient { get; set; }

        /// <summary>The embedder policy inherited by a document of a local scheme (the one of its initiator or its parent), or null to obtain it from the headers.</summary>
        public EmbedderPolicy? EmbedderPolicy { get; set; }

        /// <summary>The document isolation policy inherited with <see cref="EmbedderPolicy"/>.</summary>
        public EmbedderPolicyValue DocumentIsolationPolicy { get; set; }

        /// <summary>
        /// The policy container of a document that does not get the policies of its response
        /// (https://html.spec.whatwg.org/multipage/browsers.html#determining-navigation-params-policy-container): the one of
        /// the initiator of a document of a local scheme, of the parent of a srcdoc document, of the session history entry, or
        /// of the document replaced by the result of a <c>javascript:</c> URL. Null for the policies of the response headers.
        /// </summary>
        public PolicyContainer? PolicyContainer { get; set; }

        /// <summary>
        /// The required CSP of the navigation params (https://w3c.github.io/webappsec-cspee/#navigation-params-required-csp):
        /// the serialized policy that the document must enforce, or null.
        /// </summary>
        public string? RequiredContentSecurityPolicy { get; set; }
    }
}
