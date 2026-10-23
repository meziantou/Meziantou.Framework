using System.Net.Http;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Options of a <see cref="BrowsingContext"/>, shared by its nested browsing contexts.</summary>
public sealed partial class BrowsingContextOptions
{
    private readonly bool? _scriptingEnabled;
    private readonly bool? _declarativeRefreshEnabled;
    private SystemClipboard? _defaultClipboard;

    /// <summary>
    /// Gets or sets the loader of the documents of the navigations (e.g. the <c>src</c> of an <c>iframe</c>, or
    /// <c>location.href = url</c>). Nothing is fetched by the library: when this is null, the navigations to URLs other
    /// than <c>about:blank</c> and <c>about:srcdoc</c> result in error documents.
    /// </summary>
    public DocumentLoader? DocumentLoader { get; init; }

    /// <summary>
    /// Gets or sets the cookie store of the browsing contexts: the cookies of <see cref="Document.Cookie"/> and of the Cookie
    /// Store API (<c>window.cookieStore</c>), given to <see cref="DocumentLoader"/> with each request (see
    /// <see cref="DocumentRequest.GetCookieHeader"/> and <see cref="DocumentRequest.StoreCookies"/>). The default is null:
    /// the documents have no cookies. The jar can be shared by several browsing contexts; create it with the
    /// <see cref="TimeProvider"/> of the browsing contexts to measure the expiry times with their clock.
    /// </summary>
    public CookieJar? CookieJar { get; init; }

    /// <summary>
    /// Gets or sets the history of the visited links of the browsing contexts, which the <c>:visited</c> pseudo-class matches
    /// against (https://drafts.csswg.org/selectors-4/#link). The default is null: no link is visited (every link matches
    /// <c>:link</c>), nothing is recorded, and the rendering of a document does not depend on the documents loaded before it.
    /// With a store (e.g. an <see cref="InMemoryVisitedLinkStore"/>, which can be shared by several browsing contexts), the
    /// navigations of the browsing contexts are recorded in it (see <see cref="VisitedLinkStore.Add"/>), and the visited links
    /// are painted with the colors of the <c>:visited</c> rules; the scripts cannot observe which links are visited
    /// (<c>getComputedStyle()</c>, <c>matches()</c> and the other APIs answer as if every link was unvisited).
    /// </summary>
    public VisitedLinkStore? VisitedLinks { get; init; }

    /// <summary>
    /// Gets or sets the local storage areas of the browsing contexts (<c>window.localStorage</c>), keyed by origin. The
    /// default is null: each browsing context created by the application has its own storage shed, shared by its auxiliary
    /// browsing contexts. A shed can be shared by several browsing contexts: the <c>storage</c> events are fired at the
    /// windows of all of them. A shed created with a <see cref="WebStoragePersistence"/> persists the items.
    /// </summary>
    public StorageShed? LocalStorage { get; init; }

    /// <summary>
    /// Gets or sets the storage of the IndexedDB databases of the browsing contexts and of their workers
    /// (<c>indexedDB</c>), e.g. a database or a <see cref="InMemoryIndexedDBBackend"/> shared by several browsing contexts.
    /// The default is null: each browsing context created by the application has its own in-memory databases, shared by
    /// its auxiliary browsing contexts and their workers.
    /// </summary>
    public IndexedDBBackend? IndexedDB { get; init; }

    /// <summary>
    /// Gets or sets the storage of the caches of the Cache API of the browsing contexts and of their workers
    /// (<c>caches</c>), e.g. a database or a <see cref="InMemoryCacheStorageBackend"/> shared by several browsing contexts.
    /// The default is null: each browsing context created by the application has its own in-memory caches, shared by its
    /// auxiliary browsing contexts and their workers.
    /// </summary>
    public CacheStorageBackend? CacheStorage { get; init; }

    /// <summary>
    /// Gets or sets the storage of the origin private file systems of the browsing contexts and of their workers
    /// (<c>navigator.storage.getDirectory()</c>), e.g. a directory, a database or a <see cref="InMemoryFileSystemBackend"/>
    /// shared by several browsing contexts. The default is null: each browsing context created by the application has its
    /// own in-memory file systems, shared by its auxiliary browsing contexts and their workers.
    /// </summary>
    public FileSystemBackend? FileSystem { get; init; }

    /// <summary>
    /// Gets or sets the options of the storage of the origins (<c>navigator.storage</c>): the quota that
    /// <c>estimate()</c> reports and that the origin private file system enforces, and the answer to <c>persist()</c>. The
    /// default is null: the quota is <see cref="StorageOptions.DefaultQuota"/>, and the storage is never persistent.
    /// </summary>
    public StorageOptions? Storage { get; init; }

    /// <summary>
    /// Gets or sets the storage of the service worker registrations of the browsing contexts
    /// (<c>navigator.serviceWorker</c>), e.g. a database or a <see cref="InMemoryServiceWorkerBackend"/> shared by several
    /// browsing contexts: the registrations, and the scripts of their service workers, outlive the browsing contexts. The
    /// default is null: each browsing context created by the application has its own in-memory registrations, shared by its
    /// auxiliary browsing contexts and their workers.
    /// </summary>
    public ServiceWorkerBackend? ServiceWorkers { get; init; }

    /// <summary>
    /// Gets or sets the JavaScript engine that runs the scripts of the documents of the browsing contexts (see
    /// <see cref="Scripting.ScriptEngine"/>). The default is null: the library never executes scripts.
    /// </summary>
    /// <remarks>
    /// When an engine is set, the scripts of the documents run: the <c>script</c> elements, the event handler content
    /// attributes (e.g. <c>onclick</c>), the <c>javascript:</c> URLs and the timers. Do not use an engine with untrusted
    /// documents unless it limits the resources the scripts can use, and the <see cref="DocumentLoader"/> limits the
    /// resources they can request.
    /// </remarks>
    public Scripting.ScriptEngine? ScriptEngine { get; init; }

    /// <summary>
    /// Gets or sets the runtime that compiles and runs the WebAssembly modules of the scripts: it defines the
    /// <c>WebAssembly</c> namespace in the realms (https://webassembly.github.io/spec/js-api/). The default is null: the
    /// namespace is not defined, unless the <see cref="ScriptEngine"/> provides its own (e.g. V8). The
    /// <c>Meziantou.Framework.WebPlatform.Wasmtime</c> package provides a runtime.
    /// </summary>
    /// <remarks>
    /// The WebAssembly code runs with the resources of the process: the limits of the script engine (e.g. its timeout)
    /// interrupt it, but its memories are only limited by the runtime.
    /// </remarks>
    public WebAssembly.WebAssemblyRuntime? WebAssemblyRuntime { get; init; }

    /// <summary>
    /// Gets or sets the rasterizer of the documents of the browsing contexts (see <see cref="Rendering.RenderingBackend"/>,
    /// e.g. the <c>ImagingRenderingBackend</c> of the <c>Meziantou.Framework.WebPlatform.Imaging</c> package): it turns the
    /// display lists the library paints into pixels, decodes the images and draws the canvases. The default is null: nothing
    /// is rasterized (the layout and the geometry are computed as usual), and the APIs that need pixels behave as in a user
    /// agent that does not support them (e.g. <c>canvas.getContext('2d')</c> returns null).
    /// </summary>
    /// <remarks>
    /// With a backend, the encoded bytes of the images are kept in memory so that the backend can decode them when they are
    /// painted. Nothing is painted until something asks for pixels (e.g. a screenshot): the documents are painted lazily, and
    /// painted again only when their layout, their styles or their scroll positions changed.
    /// </remarks>
    public Rendering.RenderingBackend? RenderingBackend { get; init; }

    /// <summary>
    /// Gets or sets the media pipeline of the <c>audio</c> and <c>video</c> elements of the browsing contexts (see
    /// <see cref="Media.MediaBackend"/>): it tells which media types can be played, reads the metadata of the media resources
    /// the elements fetch and plays them. The library implements the processing model of the media elements (the resource
    /// selection, the ready states, the events, <c>play()</c>, seeking...) but never decodes audio or video by itself. The
    /// default is null: the media elements have no media resource (nothing is fetched, no media event is fired,
    /// <c>canPlayType()</c> returns the empty string and <c>play()</c> is rejected with a <c>NotSupportedError</c>).
    /// </summary>
    public Media.MediaBackend? MediaBackend { get; init; }

    /// <summary>
    /// Gets or sets whether the media elements are allowed to play without a user gesture
    /// (https://html.spec.whatwg.org/multipage/media.html#allowed-to-play): <c>play()</c> and the <c>autoplay</c> attribute.
    /// The default is <see cref="Media.MediaAutoplayPolicy.Allowed"/>.
    /// </summary>
    public Media.MediaAutoplayPolicy MediaAutoplayPolicy { get; init; }

    /// <summary>
    /// Gets or sets whether the documents navigate when their declarative refresh comes due: the <c>Refresh</c> header of
    /// their response or a <c>&lt;meta http-equiv="refresh"&gt;</c> element, e.g. <c>5; url=next.html</c>
    /// (https://html.spec.whatwg.org/multipage/semantics.html#shared-declarative-refresh-steps). The refresh comes due the
    /// given number of seconds after the document is completely loaded (measured by the clock of the
    /// <see cref="DomEventLoop"/>), and the navigation replaces the document. The default is true when
    /// <see cref="DocumentLoader"/> is set, false otherwise (the navigation would result in an error document).
    /// </summary>
    public bool DeclarativeRefreshEnabled
    {
        get => _declarativeRefreshEnabled ?? DocumentLoader is not null;
        init => _declarativeRefreshEnabled = value;
    }

    /// <summary>
    /// Gets or sets whether scripting is enabled for the documents loaded in the browsing contexts
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#concept-bc-noscript). The default is true when
    /// <see cref="ScriptEngine"/> is set, false otherwise. Without a script engine, it only changes how <c>&lt;noscript&gt;</c>
    /// is parsed: the scripts never run.
    /// </summary>
    public bool ScriptingEnabled
    {
        get => _scriptingEnabled ?? ScriptEngine is not null;
        init => _scriptingEnabled = value;
    }

    /// <summary>
    /// Gets or sets whether the HTML parser speculatively fetches the resources of the rest of a document while it waits for
    /// a parsing-blocking script (https://html.spec.whatwg.org/multipage/parsing.html#speculative-html-parsing): the
    /// scripts, the images and the other resources the elements would fetch are requested from the
    /// <see cref="DocumentLoader"/> earlier (<see cref="DocumentRequest.IsSpeculative"/> is true), as a browser does. Each
    /// URL is requested speculatively once per document, and the response is not used: the parser requests the resource
    /// again when it reaches the element, so the loader should cache the responses to benefit from it. The documents
    /// whose response has the <c>Document-Policy: expect-no-linked-resources</c> header are not parsed speculatively. The
    /// default is false.
    /// </summary>
    public bool SpeculativeParsing { get; init; }

    /// <summary>
    /// Gets or sets the detector of the encoding of the HTML documents of the navigations that do not declare their
    /// encoding: no byte order mark, no <c>charset</c> in the <c>Content-Type</c> header, no <c>meta</c> element, and no
    /// same-origin container document (https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding).
    /// The default is <see cref="WebPlatform.EncodingDetector.Default"/>, which guesses the legacy encodings from the
    /// content, and UTF-8 for the <c>file:</c> URLs only (as the browsers). Null disables the detection: these documents
    /// are decoded as windows-1252, as the ones whose encoding is not detected. The detector is only used for the HTML
    /// documents of the navigations: the other resources (XML documents, text files, style sheets, scripts, the responses of
    /// <c>XMLHttpRequest</c> and <c>fetch()</c>, <c>TextDecoder</c>) have the fallback encoding of their specification.
    /// </summary>
    public EncodingDetector? EncodingDetector { get; init; } = EncodingDetector.Default;

    /// <summary>
    /// Gets or sets the optional features of the web platform exposed to the scripts of the windows and of the workers, e.g.
    /// <c>new WebPlatformFeatures { WebSocketEnabled = false }</c>: the scripts see the disabled features as unsupported
    /// features, as in a browser that does not implement them. Use it to reduce what the scripts of untrusted documents can
    /// do, or to test the fallbacks of a page. The default is null: <see cref="WebPlatformFeatures.Default"/> (all the
    /// features are enabled).
    /// </summary>
    public WebPlatformFeatures? Features { get; init; }

    /// <summary>
    /// Gets or sets the preferences of the user for the speculative loads requested by the speculation rules of the documents
    /// (https://html.spec.whatwg.org/multipage/speculative-loading.html): the navigational prefetches and the prerendering.
    /// By default, the documents are prefetched and prerendered when a <see cref="DocumentLoader"/> is configured.
    /// </summary>
    public SpeculativeLoadingOptions SpeculativeLoading { get; init; } = SpeculativeLoadingOptions.Default;

    /// <summary>The features of the browsing contexts: <see cref="Features"/>, or the default features.</summary>
    internal WebPlatformFeatures EffectiveFeatures => Features ?? WebPlatformFeatures.Default;

    /// <summary>
    /// Gets or sets the callback invoked when an exception reported to a global object (a window or a worker) is not
    /// handled: an exception thrown by a script or a callback, whose <c>error</c> event was not canceled, or a promise
    /// rejected without handler, whose <c>unhandledrejection</c> event was not canceled
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception). It plays the role of the developer
    /// console of a browser. The callback is invoked on the thread of the event loop of the global object: the workers run
    /// on their own threads.
    /// </summary>
    public Action<GlobalScope, Scripting.ScriptError>? UnhandledScriptError { get; init; }

    /// <summary>
    /// Gets or sets the callback invoked when a script of a global object (a window or a worker) prints a message with the
    /// <c>console</c> namespace (<c>console.log()</c>, <c>console.count()</c>... https://console.spec.whatwg.org/). It plays
    /// the role of the developer console of a browser. The default is null: the messages are not formatted. The callback is
    /// invoked on the thread of the event loop of the global object: the workers run on their own threads.
    /// </summary>
    public Action<GlobalScope, Scripting.ConsoleMessage>? ConsoleMessage { get; init; }

    /// <summary>
    /// Gets or sets the callback invoked when a content security policy is violated (the policies of the
    /// <c>Content-Security-Policy</c> and <c>Content-Security-Policy-Report-Only</c> headers and of the <c>meta</c> elements, including
    /// the Trusted Types directives), whatever the disposition of the policy: see
    /// <see cref="ContentSecurityPolicyViolationReport.Disposition"/>. It is invoked with the <c>securitypolicyviolation</c> event, in the
    /// task that fires it, so it plays the role of the developer console of a browser. The callback is invoked on the thread of the
    /// event loop of the global object: the workers run on their own threads. It is not invoked for a document without browsing context.
    /// </summary>
    public Action<GlobalScope, ContentSecurityPolicyViolationReport>? ContentSecurityPolicyViolation { get; init; }

    /// <summary>
    /// Gets or sets the clock of the timers of the windows (<c>setTimeout()</c>, <c>requestAnimationFrame()</c>...) and of
    /// <c>performance.now()</c>: it is set as the <see cref="DomEventLoop.TimeProvider"/> of the event loop of the thread
    /// when the browsing context is created. Use a fake clock (e.g. <c>FakeTimeProvider</c>) to run the timers without
    /// waiting. The default is null: the clock of the event loop (<see cref="TimeProvider.System"/> by default).
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Gets or sets the virtual viewport and the preferences of the user the media queries and the viewport-relative lengths
    /// of the computed styles (<see cref="Window.GetComputedStyle"/>) are evaluated against. The default is
    /// <see cref="ViewportOptions.Default"/> (800x600 CSS pixels).
    /// </summary>
    public ViewportOptions? Viewport { get; init; }

    /// <summary>
    /// Gets or sets the callback called when the documents of a top-level browsing context are about to be displayed
    /// fullscreen (https://fullscreen.spec.whatwg.org/#dom-element-requestfullscreen, "resize the viewport"), with the
    /// options of <see cref="Element.RequestFullscreen"/>. The host of a window makes it fullscreen and returns the size of
    /// the viewport while fullscreen (the dimensions of the screen), or null to keep the size of the viewport. The default
    /// is null: the viewport keeps its size (<see cref="Viewport"/>), and the fullscreen element fills it (the
    /// <c>:fullscreen</c> rules of the user agent style sheet).
    /// </summary>
    public Func<BrowsingContext, FullscreenOptions, FullscreenViewportSize?>? EnterFullscreen { get; init; }

    /// <summary>
    /// Gets or sets the callback called when the documents of a top-level browsing context are no longer displayed
    /// fullscreen (https://fullscreen.spec.whatwg.org/#exit-fullscreen): the viewport gets the size of
    /// <see cref="Viewport"/> back. The default is null.
    /// </summary>
    public Action<BrowsingContext>? ExitFullscreen { get; init; }

    /// <summary>
    /// Gets or sets the callback called when an element of a document of a top-level browsing context requests the pointer
    /// lock (https://w3c.github.io/pointerlock/#dom-element-requestpointerlock, "process the lock request"), with the options
    /// of <see cref="Element.RequestPointerLock"/>: the host hides its cursor and reads the movements of its mouse
    /// (<see cref="PointerInput.MoveBy"/>), without the adjustments of the platform when
    /// <see cref="PointerLockOptions.UnadjustedMovement"/> is set, and returns true; it returns false when it cannot or does
    /// not want to lock the pointer (the request fails with a <c>NotSupportedError</c>). The default is null: the pointer is
    /// locked (a host without cursor has nothing to do, and the movements it injects are not adjusted).
    /// </summary>
    public Func<BrowsingContext, PointerLockOptions, bool>? RequestPointerLock { get; init; }

    /// <summary>
    /// Gets or sets the callback called when the pointer lock of a top-level browsing context is exited
    /// (https://w3c.github.io/pointerlock/#dfn-exit-pointer-lock): the host displays its cursor again, at the position of the
    /// mouse (<see cref="UserInput.Mouse"/>). The default is null.
    /// </summary>
    public Action<BrowsingContext>? ExitPointerLock { get; init; }

    /// <summary>
    /// Gets or sets the system clipboard of the browsing contexts: the data copied by the copy and cut actions and by
    /// <c>navigator.clipboard</c>, read by the paste action and by <c>navigator.clipboard</c>
    /// (https://w3c.github.io/clipboard-apis/). The default is null: the browsing contexts share an in-memory clipboard,
    /// created with the first browsing context. Set it to observe or simulate what the user copies, or to use the
    /// clipboard of the operating system (see <see cref="SystemClipboard"/>).
    /// </summary>
    public SystemClipboard? Clipboard { get; init; }

    /// <summary>
    /// Gets or sets the callback that decides whether a document may read or write the system clipboard
    /// (https://w3c.github.io/clipboard-apis/#permissions): <c>navigator.clipboard</c> and the copy, cut and paste commands of
    /// <c>document.execCommand()</c>. It plays the role of the permission prompt of a browser; there is no user activation,
    /// so the access never requires a user gesture. The default is null: all the documents are allowed. Deny the access to
    /// untrusted documents when <see cref="Clipboard"/> is the clipboard of the operating system.
    /// </summary>
    /// <remarks>
    /// The asynchronous clipboard API is only available to the documents of a secure context that have the focus, and the
    /// copy, cut and paste actions triggered by the user are always allowed.
    /// </remarks>
    public Func<Document, ClipboardAccess, bool>? ClipboardPermission { get; init; }

    /// <summary>
    /// Gets or sets whether the fonts installed on the machine can be used: a <c>font-family</c> and a <c>local()</c> source
    /// of <c>@font-face</c> can name them, as in a browser. The default is false: the documents only use the fonts bundled
    /// with the library (Tinos, Arimo, Cousine, STIX Two Math and Ahem) and their web fonts, so that the layout is the same
    /// on every machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The installed fonts are found in the font directories of the operating system (Windows, macOS, and the
    /// configuration of fontconfig on Linux), once per process: a font installed later is not seen. Only the names and the
    /// styles of the fonts are read to match them, and a font file is read when one of its faces is used.
    /// </para>
    /// <para>
    /// The bundled fonts keep their names: <c>Arimo</c> and <c>Tinos</c> are always the bundled fonts, and the generic
    /// families (<c>serif</c>, <c>sans-serif</c>, <c>monospace</c>, <c>math</c>) still use them, with the metrics of the
    /// default fonts of the browsers.
    /// </para>
    /// </remarks>
    public bool UseSystemFonts { get; init; }

    /// <summary>
    /// Gets or sets the fonts installed in addition to the bundled fonts: families that <c>font-family</c> and
    /// <c>local()</c> can name, and fonts of the installed font fallback for the scripts that the bundled fonts do not cover
    /// (see <see cref="Css.Fonts.InstalledFontCollection"/>). The default is null: the installed fonts are the bundled fonts,
    /// and the fonts of the machine when <see cref="UseSystemFonts"/> is set. When it is set, it replaces the fonts of the machine.
    /// </summary>
    /// <remarks>
    /// Internal: it is used by the runner of the web-platform-tests, whose tests need the fonts of the CSS test suite to be
    /// installed and fonts with glyphs for the scripts of their texts.
    /// </remarks>
    internal Css.Fonts.InstalledFontCollection? InstalledFonts { get; init; }

    /// <summary>The system clipboard of the browsing contexts: <see cref="Clipboard"/>, or the in-memory clipboard of the options.</summary>
    internal SystemClipboard EffectiveClipboard => Clipboard ?? LazyInitializer.EnsureInitialized(ref _defaultClipboard);
}

/// <summary>
/// Loads the documents of the navigations of the browsing contexts (e.g. the <c>src</c> of an <c>iframe</c>, or
/// <c>location.href = url</c>), the style sheets of the <c>@import</c> rules and, when a
/// <see cref="BrowsingContextOptions.ScriptEngine"/> is set, the external scripts and the HTTP requests of <c>fetch()</c> and
/// <c>XMLHttpRequest</c> (see <see cref="DocumentRequest.Destination"/>). The library never fetches anything by itself: the application provides
/// a loader, for instance the HTTP loader returned by <see cref="Create"/>, which decides which resources are available.
/// </summary>
/// <remarks>
/// The library uses the following headers of the responses (<see cref="DocumentResource.Headers"/>); the other headers
/// are ignored:
/// <list type="bullet">
/// <item><c>Accept-CH</c>, <c>Critical-CH</c> and <c>Clear-Site-Data</c>: the client hints that the origin of a top-level document opts into (see <see cref="BrowsingContextOptions.AcceptCHCache"/>).</item>
/// <item><c>Content-Type</c>: the MIME type and the encoding of the resource.</item>
/// <item><c>Content-Disposition</c>: a navigation to an <c>attachment</c> does not create a document.</item>
/// <item><c>Last-Modified</c>: <see cref="Document.LastModified"/>.</item>
/// <item><c>Content-Language</c>: the default language of the elements (e.g. <c>:lang()</c>): its first language.</item>
/// <item><c>Document-Policy</c>: <c>expect-no-linked-resources</c> disables the speculative parsing of the document (see <see cref="BrowsingContextOptions.SpeculativeParsing"/>).</item>
/// <item><c>Content-Security-Policy</c> and <c>Content-Security-Policy-Report-Only</c>: the fetch directives of the subresources, the <c>sandbox</c> directive and the Trusted Types directives (see <see cref="Document.TrustedTypesDirectives"/>), whose violations are reported (the <c>securitypolicyviolation</c> event and the <c>report-uri</c> reports).</item>
/// <item><c>Referrer-Policy</c>: the referrer policy of the document, which determines <see cref="DocumentRequest.Referrer"/> and <see cref="Document.Referrer"/>.</item>
/// <item><c>Refresh</c>: the declarative refresh of the document (see <see cref="BrowsingContextOptions.DeclarativeRefreshEnabled"/>).</item>
/// <item><c>X-Content-Type-Options</c>: <c>nosniff</c> blocks the scripts whose MIME type is not a JavaScript MIME type, and the style sheets whose MIME type is not <c>text/css</c>.</item>
/// </list>
/// </remarks>
public abstract class DocumentLoader
{
    /// <summary>
    /// Returns a loader that gets the documents with HTTP GET requests (only <c>http:</c> and <c>https:</c> URLs; the other
    /// URLs are network errors). It sends and stores the cookies of <see cref="DocumentRequest.CookieJar"/>, and
    /// follows the redirects itself (up to 20) when the client does not.
    /// </summary>
    /// <param name="httpClient">
    /// The client used to send the requests. Its handler should not use its own cookies (<c>UseCookies = false</c>) when
    /// <see cref="BrowsingContextOptions.CookieJar"/> is set. When null, a shared loader is returned, whose client
    /// does not follow the redirects, does not use cookies, and decompresses the responses.
    /// </param>
    /// <remarks>
    /// Any URL a document navigates to is requested (e.g. the <c>src</c> of its iframes). Do not use this loader with
    /// untrusted documents when the requests can reach private resources (server-side request forgery): use a client
    /// whose handler filters the destinations, or a custom <see cref="DocumentLoader"/>.
    /// </remarks>
    public static DocumentLoader Create(HttpClient? httpClient = null) => httpClient is null ? HttpDocumentLoader.Shared : new HttpDocumentLoader(httpClient);

    /// <summary>
    /// Sends a request of the fetch algorithm of the library (https://fetch.spec.whatwg.org/#http-network-fetch): the document
    /// of a navigation, a subresource of a document (e.g. an external script, a style sheet, an image, see
    /// <see cref="DocumentRequest.Destination"/>), or a request of <c>fetch()</c> or <c>XMLHttpRequest</c>. The library
    /// follows the redirects (<see cref="DocumentRequest.FollowRedirects"/> is false: the loader returns the redirect
    /// responses) and checks each response (CORS, mixed content, content security policies...). Returns null when the
    /// resource is not available: the navigation then results in an error document with an opaque origin, as a network
    /// error (the <c>error</c> event is fired at a <c>script</c> or <c>style</c> element). An exception (including a
    /// cancellation) is also a network error.
    /// </summary>
    /// <remarks>
    /// The method is called from a task of the <see cref="DomEventLoop"/>. When it completes asynchronously, the document
    /// is created by a task queued on completion (from any thread): use <see cref="DomEventLoop.RunUntilIdleAsync"/> to
    /// wait for it. <paramref name="cancellationToken"/> is canceled when the navigation is aborted (e.g. another navigation
    /// of the browsing context starts, or the iframe is removed); the result of an aborted navigation is ignored.
    /// </remarks>
    public abstract ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the connection of a <see cref="WebSocket"/> (https://fetch.spec.whatwg.org/#concept-websocket-connection-obtain)
    /// and sends its opening handshake (https://websockets.spec.whatwg.org/#concept-websocket-establish): an HTTP/1.1 GET
    /// request to <see cref="DocumentRequest.Url"/> (an <c>http:</c> or <c>https:</c> URL, the URL of the WebSocket with
    /// the <c>http</c> or <c>https</c> scheme) whose <see cref="DocumentRequest.Mode"/> is <see cref="RequestMode.WebSocket"/>,
    /// with the headers of <see cref="DocumentRequest.Headers"/> (<c>Upgrade</c>, <c>Connection</c>, <c>Sec-WebSocket-Key</c>,
    /// <c>Sec-WebSocket-Version</c>, <c>Origin</c>...) and the headers of the connection (<c>Host</c>, <c>Cookie</c>...).
    /// Returns the response, or null when the connection cannot be established (a network error). The library validates
    /// the handshake: the response must have the <c>101</c> status, its headers, and the connection on which the frames are
    /// read and written (<see cref="DocumentResource.Connection"/>). The loader must not follow the redirects. The default
    /// implementation returns null: the WebSockets cannot connect.
    /// </summary>
    /// <remarks>
    /// The method is called from a task of the <see cref="DomEventLoop"/> of the global object of the WebSocket (a window or a
    /// worker). <paramref name="cancellationToken"/> is canceled when the WebSocket is closed (or its document is unloaded)
    /// before the connection is established. The library disposes the connection.
    /// </remarks>
    public virtual ValueTask<DocumentResource?> ConnectWebSocketAsync(DocumentRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<DocumentResource?>(null);
}

/// <summary>A request of a document, given to <see cref="DocumentLoader.LoadAsync"/>.</summary>
public sealed class DocumentRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="url">The absolute URL of the document, serialized as specified by the WHATWG URL Standard.</param>
    public DocumentRequest(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Url = url;
    }

    /// <summary>Gets the absolute URL of the document, serialized as specified by the WHATWG URL Standard.</summary>
    public string Url { get; }

    /// <summary>
    /// Gets the cookie store of the browsing context (<see cref="BrowsingContextOptions.CookieJar"/>), or null. Use
    /// <see cref="GetCookieHeader"/> and <see cref="StoreCookies"/> to send and store the cookies of the request.
    /// </summary>
    public CookieJar? CookieJar { get; init; }

    /// <summary>
    /// Gets the "site for cookies" of the document that initiated the request
    /// (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-document-based-requests): the serialized
    /// top-level origin when the document and its ancestors are same-site with it, <c>null</c> (the string) when it is
    /// opaque (the request is cross-site), or null when the request has no client (e.g. <see cref="BrowsingContext.Navigate(string)"/>).
    /// It determines which SameSite cookies are sent and stored. The site for cookies of a top-level navigation
    /// (<see cref="RequestDestination.Document"/>) is its URL.
    /// </summary>
    public string? SiteForCookies { get; init; }

    /// <summary>
    /// Gets the serialized origin of the top-level document of the document that initiated the request, or null when the
    /// request has no client: its site is the partition key of the partitioned cookies (the <c>Partitioned</c> attribute).
    /// </summary>
    public string? TopLevelOrigin { get; init; }

    /// <summary>
    /// The destination of the navigation whose request is fetched again by a service worker (see <see cref="FetchRequest.NavigationDestination"/>),
    /// or null: it determines the context of the cookies instead of <see cref="Destination"/>.
    /// </summary>
    internal RequestDestination? NavigationDestination { get; init; }

    /// <summary>
    /// Gets the URLs of the redirects that led to this request, in order (the URL list of the request without its URL,
    /// https://fetch.spec.whatwg.org/#concept-request-url-list), when the request is created for a redirect: a request is
    /// cross-site when one of them is cross-site. The default is empty.
    /// </summary>
    public IReadOnlyList<string> RedirectUrls
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = [];

    /// <summary>
    /// Returns the value of the <c>Cookie</c> header of the request (the cookies of <see cref="CookieJar"/> for
    /// <see cref="Url"/>, https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-cookie-header-field),
    /// or null when no cookie is sent (e.g. <see cref="IncludeCredentials"/> is false). The value is an isomorphic string
    /// (each character is a byte).
    /// </summary>
    /// <param name="url">The URL of the request, or of a redirect followed by the loader (null for <see cref="Url"/>).</param>
    /// <param name="redirectUrls">The URLs of the redirects that led to <paramref name="url"/> (null for <see cref="RedirectUrls"/>).</param>
    public string? GetCookieHeader(string? url = null, IReadOnlyList<string>? redirectUrls = null)
    {
        if (!IncludeCredentials || CookieJar is not { } cookieJar || GetCookieContext(url, redirectUrls) is not { } context)
            return null;

        return cookieJar.GetCookieString(context);
    }

    /// <summary>
    /// Stores the cookies of the <c>Set-Cookie</c> headers of the response of the request in <see cref="CookieJar"/>
    /// (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-the-set-cookie-header-field), unless
    /// <see cref="IncludeCredentials"/> is false.
    /// </summary>
    /// <param name="setCookieHeaders">The values of the <c>Set-Cookie</c> headers (isomorphic strings: each character is a byte).</param>
    /// <param name="url">The URL of the response: the URL of the request, or of a redirect followed by the loader (null for <see cref="Url"/>).</param>
    /// <param name="redirectUrls">The URLs of the redirects that led to <paramref name="url"/> (null for <see cref="RedirectUrls"/>).</param>
    public void StoreCookies(IEnumerable<string> setCookieHeaders, string? url = null, IReadOnlyList<string>? redirectUrls = null)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeaders);
        if (!IncludeCredentials || CookieJar is not { } cookieJar || GetCookieContext(url, redirectUrls) is not { } context)
            return;

        foreach (var setCookie in setCookieHeaders)
        {
            cookieJar.ReceiveSetCookieString(context, setCookie);
        }
    }

    private CookieRequestContext? GetCookieContext(string? url, IReadOnlyList<string>? redirectUrls)
    {
        if (Urls.UrlParser.Parse(url ?? Url, baseUrl: null) is not { Scheme: "http" or "https" or "ws" or "wss" } parsed)
            return null;

        var previousUrls = new List<Urls.UrlRecord>();
        foreach (var redirectUrl in redirectUrls ?? RedirectUrls)
        {
            if (Urls.UrlParser.Parse(redirectUrl, baseUrl: null) is { } previous)
            {
                previousUrls.Add(previous);
            }
        }

        return CookieContexts.ForRequest(this, parsed, previousUrls);
    }

    /// <summary>
    /// Gets the destination of the request (https://fetch.spec.whatwg.org/#concept-request-destination): a document of a
    /// navigation, the script of a <c>script</c> element, or the style sheet of an <c>@import</c> rule. The default is
    /// <see cref="RequestDestination.Document"/>.
    /// </summary>
    public RequestDestination Destination { get; init; }

    /// <summary>
    /// Gets the element that initiated the request (e.g. the <c>script</c> element, or the <c>style</c> element of the style
    /// sheet that contains the <c>@import</c> rule), or null (e.g. a navigation).
    /// </summary>
    public Element? Initiator { get; init; }

    /// <summary>
    /// Gets the serialized origin of the document that initiated the request (https://fetch.spec.whatwg.org/#concept-request-origin),
    /// e.g. <c>https://example.com</c>, or <c>null</c> (the string) when the origin is opaque. It is null when the request
    /// is not initiated by a document (e.g. <see cref="BrowsingContext.Navigate(string)"/>). A loader can use it to decide
    /// whether the resource is available to the initiator (e.g. to reject cross-origin requests).
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Gets the referrer of the request (https://fetch.spec.whatwg.org/#concept-request-referrer), as determined by
    /// <see cref="ReferrerPolicy"/>: the URL of the document that initiated the request, possibly reduced to its origin,
    /// or null for no referrer (e.g. <see cref="BrowsingContext.Navigate(string)"/>). A loader can send it as the
    /// <c>Referer</c> header.
    /// </summary>
    public string? Referrer { get; init; }

    /// <summary>
    /// Gets the referrer policy of the request (https://w3c.github.io/webappsec-referrer-policy/#referrer-policy), e.g.
    /// <c>strict-origin-when-cross-origin</c>: the <c>referrerpolicy</c> attribute of the initiator, the <c>Referrer-Policy</c>
    /// header or the <c>&lt;meta name="referrer"&gt;</c> of the document, or the default policy. A loader that follows the
    /// redirects should use it to determine the referrer of the next request.
    /// </summary>
    public string ReferrerPolicy { get; init; } = ReferrerPolicies.Default;

    /// <summary>Gets the mode of the request (https://fetch.spec.whatwg.org/#concept-request-mode). A loader can send the <c>Origin</c> header of the CORS requests.</summary>
    public RequestMode Mode { get; init; }

    /// <summary>
    /// Gets the integrity metadata of the request (https://fetch.spec.whatwg.org/#concept-request-integrity-metadata), e.g.
    /// the <c>integrity()</c> modifier of a CSS <c>url()</c>, or the empty string. The library checks that the body of the
    /// response matches it (https://w3c.github.io/webappsec-subresource-integrity/#does-response-match-metadatalist).
    /// </summary>
    public string Integrity
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "";

    /// <summary>
    /// Gets whether the request is a speculative fetch of the HTML parser (see <see cref="BrowsingContextOptions.SpeculativeParsing"/>):
    /// the element that would fetch the resource is not parsed yet (<see cref="Initiator"/> is null), and the response is ignored.
    /// </summary>
    public bool IsSpeculative { get; init; }

    /// <summary>
    /// Gets whether the navigation is initiated with a user activation (https://fetch.spec.whatwg.org/#request-user-activation):
    /// the window of the initiator has transient activation. A loader can send the <c>Sec-Fetch-User</c> header
    /// (https://w3c.github.io/webappsec-fetch-metadata/#sec-fetch-user-header).
    /// </summary>
    public bool UserActivation { get; init; }

    /// <summary>Gets the method of the request (https://fetch.spec.whatwg.org/#concept-request-method): <c>GET</c>, or <c>POST</c> for the submission of a form with the POST method.</summary>
    public string Method { get; init; } = "GET";

    /// <summary>Gets the body of the request (https://fetch.spec.whatwg.org/#concept-request-body), e.g. the entry list of a submitted form, or null.</summary>
    public ReadOnlyMemory<byte>? Body { get; init; }

    /// <summary>Gets the <c>Content-Type</c> of the body of the request (e.g. <c>application/x-www-form-urlencoded</c>), or null when there is no body.</summary>
    public string? BodyContentType { get; init; }

    /// <summary>
    /// Gets the headers of the request, in their order (https://fetch.spec.whatwg.org/#concept-request-header-list): the
    /// headers set by the script (<c>fetch()</c> and <c>XMLHttpRequest</c>), and the <c>Accept</c>, <c>Content-Type</c>,
    /// <c>Content-Length</c>, <c>Origin</c>, <c>Referer</c>, Fetch Metadata (<c>Sec-Fetch-*</c>), client hints
    /// (<c>Sec-CH-*</c>, see <see cref="BrowsingContextOptions.ClientHints"/>), <c>Authorization</c> and
    /// cache headers added by the fetch algorithm of the library (and the <c>Ping-From</c> and <c>Ping-To</c> headers of the
    /// hyperlink auditing requests, <see cref="RequestDestination.Ping"/>). The loader adds the headers of the connection
    /// (e.g. <c>Host</c>, <c>Cookie</c>). It is empty for a request created by the application, whose headers the loader
    /// determines from the other properties (e.g. <see cref="Referrer"/> and <see cref="Destination"/>).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = [];

    /// <summary>
    /// Gets whether the cookies of <see cref="CookieJar"/> are sent with the request, and the cookies of the response
    /// are stored (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, includeCredentials). It is false for a
    /// cross-origin request of <c>fetch()</c> or <c>XMLHttpRequest</c> without credentials. The default is true.
    /// </summary>
    public bool IncludeCredentials { get; init; } = true;

    /// <summary>
    /// Gets whether the loader should follow the redirects. When false, the loader returns the redirect responses (e.g. a
    /// <c>302</c> status with a <c>Location</c> header), and the library follows them as specified
    /// (https://fetch.spec.whatwg.org/#http-redirect-fetch, https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching):
    /// this is the case of all the requests of the library, whose redirect mode and checks (CORS, mixed content, content
    /// security policies...) apply to each response. A loader that follows the redirects anyway should set
    /// <see cref="DocumentResource.Url"/> and <see cref="DocumentResource.RedirectUrls"/>: the library checks these URLs
    /// once the response is received. The default is true (a request created by the application).
    /// </summary>
    public bool FollowRedirects { get; init; } = true;

    /// <summary>
    /// Gets whether the request is synchronous (a synchronous <c>XMLHttpRequest</c>): the event loop is blocked until the
    /// loader completes, so the loader must not wait for a task of the <see cref="DomEventLoop"/>.
    /// </summary>
    public bool IsSynchronous { get; init; }
}

/// <summary>The destination of a <see cref="DocumentRequest"/> (https://fetch.spec.whatwg.org/#concept-request-destination).</summary>
public enum RequestDestination
{
    /// <summary>
    /// The document of the navigation of a top-level browsing context (e.g. <see cref="BrowsingContext.Navigate(string)"/>,
    /// <c>window.open()</c>). The navigations of the nested browsing contexts are <see cref="Iframe"/>, <see cref="Frame"/>,
    /// <see cref="Embed"/> and <see cref="Object"/>.
    /// </summary>
    Document,

    /// <summary>The classic script of a <c>script</c> element.</summary>
    Script,

    /// <summary>A style sheet: the one of a <c>&lt;link rel="stylesheet"&gt;</c> or of an <c>@import</c> rule (https://drafts.csswg.org/css-cascade-5/#fetch-an-import).</summary>
    Style,

    /// <summary>
    /// An image: the one of an <c>img</c> element (https://html.spec.whatwg.org/multipage/images.html#update-the-image-data),
    /// of an SVG <c>image</c> element or the poster of a <c>video</c> element. The resource should be returned as bytes.
    /// </summary>
    Image,

    /// <summary>
    /// A font: the font file of a font face (https://drafts.csswg.org/css-font-loading/#font-face-load) or the resource of a
    /// <c>&lt;link rel="preload" as="font"&gt;</c>. The resource should be returned as bytes.
    /// </summary>
    Font,

    /// <summary>The JSON module of an <c>import</c> with <c>{ type: "json" }</c> (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script).</summary>
    Json,

    /// <summary>The text module of an <c>import</c> with <c>{ type: "text" }</c> (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-destination-from-module-type).</summary>
    Text,

    /// <summary>
    /// The empty destination: a request of <c>fetch()</c> or of <c>XMLHttpRequest</c>. The resource should be returned as
    /// bytes, with its status and its headers.
    /// </summary>
    Empty,

    /// <summary>
    /// A hyperlink auditing ping (https://html.spec.whatwg.org/multipage/links.html#hyperlink-auditing): a POST request
    /// whose body is <c>PING</c>, sent to the URLs of the <c>ping</c> attribute of a followed hyperlink. The response is ignored.
    /// </summary>
    Ping,

    /// <summary>The document of the navigation of an <c>iframe</c> element (e.g. its <c>src</c>).</summary>
    Iframe,

    /// <summary>The document of the navigation of a <c>frame</c> element.</summary>
    Frame,

    /// <summary>The document of the navigation of an <c>embed</c> element.</summary>
    Embed,

    /// <summary>The document of the navigation of an <c>object</c> element.</summary>
#pragma warning disable CA1720 // The name of the destination of the specification
    Object,
#pragma warning restore CA1720

    /// <summary>The script of a dedicated worker.</summary>
    Worker,

    /// <summary>The script of a shared worker.</summary>
    SharedWorker,

    /// <summary>The script of a service worker (https://w3c.github.io/ServiceWorker/#update-algorithm), and the scripts it imports.</summary>
    ServiceWorker,

    /// <summary>
    /// A violation report of a content security policy (https://w3c.github.io/webappsec-csp/#report-violation): a POST
    /// request whose body is the JSON report (<c>application/csp-report</c>), sent to the URLs of the <c>report-uri</c>
    /// directive. The response is ignored.
    /// </summary>
    Report,

    /// <summary>
    /// The media resource of an <c>audio</c> element (https://html.spec.whatwg.org/multipage/media.html#concept-media-load-resource),
    /// fetched when a <see cref="BrowsingContextOptions.MediaBackend"/> is set. The resource should be returned as bytes.
    /// </summary>
    Audio,

    /// <summary>
    /// The media resource of a <c>video</c> element (https://html.spec.whatwg.org/multipage/media.html#concept-media-load-resource),
    /// fetched when a <see cref="BrowsingContextOptions.MediaBackend"/> is set. The resource should be returned as bytes.
    /// </summary>
    Video,

    /// <summary>
    /// The resource of a <c>track</c> element (https://html.spec.whatwg.org/multipage/media.html#start-the-track-processing-model)
    /// or of a <c>&lt;link rel=preload as=track&gt;</c> element: a WebVTT file. The resource should be returned as bytes.
    /// </summary>
    Track,

    /// <summary>
    /// A module script of a paint worklet (<c>CSS.paintWorklet.addModule()</c>, https://html.spec.whatwg.org/multipage/worklets.html#fetch-a-worklet-script-graph),
    /// and the modules it imports.
    /// </summary>
    PaintWorklet,

    /// <summary>
    /// A speculation rule set of the <c>Speculation-Rules</c> header of a document
    /// (https://html.spec.whatwg.org/multipage/speculative-loading.html#the-speculation-rules-header).
    /// </summary>
    SpeculationRules,
}

/// <summary>The mode of a <see cref="DocumentRequest"/> (https://fetch.spec.whatwg.org/#concept-request-mode).</summary>
public enum RequestMode
{
    /// <summary>A request whose response can come from any origin without CORS (e.g. a classic script, an image).</summary>
    NoCors,

    /// <summary>
    /// A CORS request (e.g. a module script, a script with a <c>crossorigin</c> attribute): a response from another origin
    /// must allow the origin of the request with its <c>Access-Control-Allow-Origin</c> header
    /// (https://fetch.spec.whatwg.org/#cors-check), else it is a network error. The library performs the check.
    /// </summary>
    Cors,

    /// <summary>The request of a navigation.</summary>
    Navigate,

    /// <summary>A request whose response must come from the same origin (e.g. <c>fetch(url, { mode: "same-origin" })</c>).</summary>
    SameOrigin,

    /// <summary>The opening handshake of a <see cref="WebSocket"/> (see <see cref="DocumentLoader.ConnectWebSocketAsync"/>).</summary>
    WebSocket,
}

/// <summary>
/// A resource returned by <see cref="DocumentLoader.LoadAsync"/>: the response of a request, with the decoded text or the
/// bytes of its body, its status and its headers.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/ResponseHeadersTests.cs" ref:region="DocumentResource_Example" ref:dedent="true">
/// // A resource created from its decoded content
/// var page = new DocumentResource("&lt;p&gt;Hello&lt;/p&gt;");
/// Assert.Equal("&lt;p&gt;Hello&lt;/p&gt;", page.Content);
///
/// // A resource created from its bytes, decoded using the charset of the Content-Type header
/// var bytes = "console.log('Hello');"u8.ToArray();
/// var script = new DocumentResource(bytes) { Headers = [new("Content-Type", "text/javascript; charset=utf-8")] };
/// Assert.Null(script.Content);
/// Assert.Equal(bytes, script.ContentBytes?.ToArray());
/// </code>
/// </example>
public sealed class DocumentResource
{
    /// <summary>Creates a resource from its decoded content.</summary>
    /// <param name="content">The decoded content of the resource.</param>
    public DocumentResource(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    /// <summary>
    /// Creates a resource from its bytes. The encoding is determined as specified: the byte order mark, the <c>charset</c>
    /// parameter of the <c>Content-Type</c> header, then the <c>&lt;meta charset&gt;</c> of an HTML document (or UTF-8 for
    /// the other documents, and the encoding of the document for the scripts and style sheets).
    /// </summary>
    /// <param name="content">The bytes of the resource.</param>
    public DocumentResource(ReadOnlyMemory<byte> content)
    {
        ContentBytes = content;
    }

    /// <summary>Gets the decoded content of the resource, or null when it was created from bytes.</summary>
    public string? Content { get; }

    /// <summary>Gets the bytes of the resource, or null when it was created from its decoded content.</summary>
    public ReadOnlyMemory<byte>? ContentBytes { get; }

    /// <summary>
    /// Gets or sets the rest of the body of a response that arrives over time (e.g. a chunked response), after the bytes of
    /// <see cref="ContentBytes"/>, or null when the body is complete. The HTML document of a navigation is parsed as its
    /// chunks arrive: the parser waits for the next chunk at the end of the bytes received so far (and, when the response
    /// does not declare the encoding, for the bytes that determine it), and the document is complete at the end of the
    /// sequence. The events of an <see cref="EventSource"/> are dispatched as the chunks of its event stream arrive (the
    /// sequence is enumerated with the cancellation token of the request, which is canceled when the event source is
    /// closed). The other requests, and the navigations to a document that is not an HTML document, wait for the end of
    /// the sequence and get the complete body; an HTML document created from its decoded content ignores the sequence. The
    /// enumerator is disposed when the body is not needed (e.g. a navigation whose response does not create a document).
    /// </summary>
    public IAsyncEnumerable<ReadOnlyMemory<byte>>? RemainingContentBytes { get; init; }

    /// <summary>
    /// Gets or sets the HTTP status code of the response (https://fetch.spec.whatwg.org/#concept-response-status). The
    /// default is 200. The body of a document is displayed whatever its status (e.g. a 404 page), except for the 204 and 205
    /// statuses, which do not create a document. A script or an imported style sheet whose status is not in the range
    /// 200-299 is a network error.
    /// </summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>
    /// Gets or sets the status message of the response (https://fetch.spec.whatwg.org/#concept-response-status-message),
    /// e.g. <c>OK</c> (the <c>statusText</c> of <c>fetch()</c> and <c>XMLHttpRequest</c>). The default is null: the
    /// reason phrase of <see cref="StatusCode"/>.
    /// </summary>
    public string? StatusText { get; init; }

    /// <summary>
    /// Gets or sets the headers of the response, in their order (https://fetch.spec.whatwg.org/#concept-response-header-list).
    /// The names are compared ASCII case-insensitively, and the values of headers with the same name are combined as
    /// specified. The default is no header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>Content-Type</c> header is the MIME type of the resource, possibly with parameters (e.g.
    /// <c>text/html; charset=utf-8</c>). The document of a navigation is created from <c>text/html</c> (the default when
    /// there is no valid <c>Content-Type</c>), an XML MIME type (e.g. <c>application/xhtml+xml</c> or <c>image/svg+xml</c>)
    /// or <c>text/plain</c>; the other types create a document without content.
    /// </para>
    /// <para>The other headers used by the library are listed in the documentation of <see cref="DocumentLoader"/>.</para>
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, string>> Headers
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = [];

    /// <summary>Gets or sets the URL of the response (e.g. after redirects). The default is the requested URL.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// Gets or sets the URLs of the redirects between the requested URL and <see cref="Url"/>, in order (the URL list of the
    /// response without its first and last URLs, https://fetch.spec.whatwg.org/#concept-response-url-list). A redirect to
    /// another origin taints the response of a no-CORS request, e.g. the rules of a style sheet cannot be accessed. The
    /// default is no redirect.
    /// </summary>
    public IReadOnlyList<string> RedirectUrls
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = [];

    /// <summary>
    /// Gets or sets the connection of the response to the opening handshake of a WebSocket, whose status is <c>101</c>
    /// (Switching Protocols): the stream on which the frames of the WebSocket protocol are read and written
    /// (https://www.rfc-editor.org/rfc/rfc6455#section-5), positioned after the headers of the response. See
    /// <see cref="DocumentLoader.ConnectWebSocketAsync"/>. The library disposes it. The default is null.
    /// </summary>
    public Stream? Connection { get; init; }

    /// <summary>
    /// The type of the response of the fetch algorithm whose internal response is this resource (https://fetch.spec.whatwg.org/#concept-response-type):
    /// <see cref="FetchResponseType.Basic"/> or <see cref="FetchResponseType.Cors"/> for a CORS-same-origin response,
    /// <see cref="FetchResponseType.Opaque"/> for a cross-origin response of a no-cors request (see <see cref="FetchResponse.ToDocumentResource"/>).
    /// </summary>
    internal FetchResponseType ResponseType { get; init; }

    /// <summary>
    /// Whether the response is CORS-same-origin (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#cors-same-origin):
    /// its type is "basic", "cors" or "default" (a response created by a service worker).
    /// </summary>
    internal bool IsCorsSameOrigin => ResponseType is FetchResponseType.Basic or FetchResponseType.Cors or FetchResponseType.Default;

    /// <summary>The MIME type of the Content-Type header (https://fetch.spec.whatwg.org/#concept-header-extract-mime-type), or null.</summary>
    internal Infra.MimeType? MimeType
    {
        get
        {
            if (!_mimeTypeExtracted)
            {
                _mimeType = HeaderList.ExtractMimeType(Headers);
                _mimeTypeExtracted = true;
            }

            return _mimeType;
        }
    }

    private Infra.MimeType? _mimeType;
    private bool _mimeTypeExtracted;

    /// <summary>
    /// Whether the computed MIME type of the response in a style context is <c>text/css</c>
    /// (https://mimesniff.spec.whatwg.org/#sniffing-in-a-style-context): the Content-Type is <c>text/css</c>, or it is missing
    /// or not a valid MIME type (the supplied MIME type is undefined), as in the browsers. A response with the
    /// <c>X-Content-Type-Options: nosniff</c> header and no valid <c>text/css</c> Content-Type is blocked (<see cref="IsBlocked(RequestDestination)"/>).
    /// </summary>
    internal bool IsCssInStyleContext => MimeType is not { } mimeType || mimeType.Essence == "text/css";

    /// <summary>Returns whether the status is an ok status (https://fetch.spec.whatwg.org/#ok-status).</summary>
    internal bool IsOkStatus => HeaderList.IsOkStatus(StatusCode);

    /// <summary>
    /// Returns whether the response is blocked, and is then a network error (https://fetch.spec.whatwg.org/#main-fetch):
    /// https://fetch.spec.whatwg.org/#should-response-to-request-be-blocked-due-to-mime-type? and
    /// https://fetch.spec.whatwg.org/#should-response-to-request-be-blocked-due-to-nosniff?.
    /// </summary>
    internal bool IsBlocked(RequestDestination destination) => IsBlocked(Headers, destination);

    /// <inheritdoc cref="IsBlocked(RequestDestination)"/>
    internal static bool IsBlocked(IReadOnlyList<KeyValuePair<string, string>> headers, RequestDestination destination)
    {
        if (destination is RequestDestination.PaintWorklet)
        {
            destination = RequestDestination.Script;
        }

        if (destination is not (RequestDestination.Script or RequestDestination.Style))
            return false;

        var mimeType = HeaderList.ExtractMimeType(headers);
        if (destination == RequestDestination.Script && mimeType is not null &&
            (mimeType.Type is "audio" or "image" or "video" || mimeType.Essence == "text/csv"))
        {
            return true;
        }

        if (!HeaderList.DetermineNosniff(headers))
            return false;

        return destination switch
        {
            RequestDestination.Script => mimeType is not { IsJavaScript: true },
            RequestDestination.Style => mimeType?.Essence != "text/css",
            _ => false,
        };
    }

    /// <summary>
    /// Whether the quirks mode MIME type quirk applies to the style sheet of a request of <paramref name="requestUrl"/>
    /// (https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet): the document is in quirks mode and has the same
    /// origin as the URL of the response (after the redirects). The origin of a <c>data:</c> URL is opaque. As in the
    /// browsers, the response must also be CORS-same-origin (a no-CORS request redirected through another origin is not).
    /// </summary>
    internal bool IsStyleSheetQuirkApplicable(Urls.UrlRecord requestUrl, Document document)
    {
        if (document.Mode != DocumentMode.Quirks)
            return false;

        var responseUrl = Url is { } url ? Urls.UrlParser.Parse(url, baseUrl: null) : requestUrl;
        return responseUrl is not null && responseUrl.Scheme != "data" && responseUrl.SerializeOrigin() is var origin && origin != "null" && origin == document.SerializeOrigin();
    }

    /// <summary>Creates a resource whose Content-Type header is <paramref name="contentType"/>.</summary>
    internal static DocumentResource Create(string content, string contentType) => new(content) { Headers = [new("Content-Type", contentType)] };

    /// <summary>Creates a resource whose Content-Type header is <paramref name="contentType"/>.</summary>
    internal static DocumentResource Create(ReadOnlyMemory<byte> content, string contentType) => new(content) { Headers = [new("Content-Type", contentType)] };
}

/// <summary>
/// The preferences of the user for the speculative loads of the speculation rules (see <see cref="BrowsingContextOptions.SpeculativeLoading"/>),
/// e.g. a data saver mode. The specification lets the user agent decline any speculative load.
/// </summary>
public sealed record SpeculativeLoadingOptions
{
    /// <summary>Gets the default preferences: the documents are prefetched and prerendered, without hiding the IP address of the client.</summary>
    public static SpeculativeLoadingOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets whether the candidates of the speculation rules are prefetched (https://wicg.github.io/nav-speculation/prefetch.html).
    /// When it is false, nothing is prefetched nor prerendered. The default is true.
    /// </summary>
    public bool PrefetchEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>prerender</c> candidates of the speculation rules are prerendered
    /// (https://wicg.github.io/nav-speculation/prerendering.html). When it is false, they are prefetched. The default is true.
    /// </summary>
    public bool PrerenderEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <see cref="DocumentLoader"/> hides the IP address of the client (e.g. with a proxy) for the
    /// requests whose <c>Sec-Purpose</c> header is <c>prefetch;anonymous-client-ip</c>. When it is false (the default), the
    /// cross-origin candidates of the rules that require it (<c>"requires": ["anonymous-client-ip-when-cross-origin"]</c>)
    /// are not prefetched.
    /// </summary>
    public bool AnonymousClientIpSupported { get; init; }
}
