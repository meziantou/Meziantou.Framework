namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The optional features of the web platform exposed to the scripts of the windows and of the workers (see
/// <see cref="BrowsingContextOptions.Features"/>), as the runtime flags of a browser. All the features are enabled by
/// default. A disabled feature is not exposed: its interface objects and its members are not defined, so the scripts see it
/// as an unsupported feature (e.g. <c>typeof WebSocket === "undefined"</c>, <c>"sendBeacon" in navigator === false</c>).
/// The .NET API is not affected.
/// </summary>
public sealed record WebPlatformFeatures
{
    /// <summary>Gets the default features: all the features are enabled.</summary>
    public static WebPlatformFeatures Default { get; } = new();

    /// <summary>Gets or sets whether the <c>WebSocket</c> interface is exposed (https://websockets.spec.whatwg.org/). The default is true.</summary>
    public bool WebSocketEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>EventSource</c> interface is exposed: the server-sent events
    /// (https://html.spec.whatwg.org/multipage/server-sent-events.html). The default is true.
    /// </summary>
    public bool EventSourceEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>PermissionsPolicy</c> interface, <c>document.permissionsPolicy</c> and
    /// <c>iframe.permissionsPolicy</c> are exposed (https://w3c.github.io/webappsec-permissions-policy/#introspection). The
    /// permissions policies of the documents are enforced either way. The default is true.
    /// </summary>
    public bool PermissionsPolicyEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Touch Events are enabled (https://w3c.github.io/touch-events/): the <c>Touch</c>,
    /// <c>TouchList</c> and <c>TouchEvent</c> interfaces, and the <c>touchstart</c>, <c>touchmove</c>, <c>touchend</c> and
    /// <c>touchcancel</c> events of the touch pointers of the user (see <see cref="UserInput"/>). The default is true.
    /// </summary>
    public bool TouchEventsEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the speculation rules are supported (https://html.spec.whatwg.org/multipage/speculative-loading.html):
    /// the <c>speculationrules</c> script type (<c>HTMLScriptElement.supports("speculationrules")</c>), the
    /// <c>Speculation-Rules</c> header, and the prerendering API (<c>document.prerendering</c>, the <c>prerenderingchange</c>
    /// event and <c>PerformanceNavigationTiming.activationStart</c>). When it is false, nothing is prefetched nor prerendered.
    /// The default is true.
    /// </summary>
    public bool SpeculationRulesEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>CloseWatcher</c> interface is exposed
    /// (https://html.spec.whatwg.org/multipage/interaction.html#the-closewatcher-interface). The close requests of the user
    /// still close the dialogs and the popovers. The default is true.
    /// </summary>
    public bool CloseWatcherEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the URL text directives are enabled (https://wicg.github.io/scroll-to-text-fragment/): the
    /// <c>FragmentDirective</c> interface and <c>document.fragmentDirective</c>. When disabled, the fragment directive
    /// (<c>#:~:text=...</c>) is a part of the fragment of the URL as any other text: it is neither removed nor searched. The
    /// default is true.
    /// </summary>
    public bool TextDirectivesEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Fullscreen API is exposed (https://fullscreen.spec.whatwg.org/):
    /// <c>Element.requestFullscreen()</c>, <c>document.exitFullscreen()</c>, <c>fullscreenElement</c>,
    /// <c>fullscreenEnabled</c>, <c>fullscreen</c>, and the <c>onfullscreenchange</c> and <c>onfullscreenerror</c> event
    /// handlers. The default is true.
    /// </summary>
    public bool FullscreenEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Pointer Lock API is exposed (https://w3c.github.io/pointerlock/):
    /// <c>Element.requestPointerLock()</c>, <c>document.exitPointerLock()</c>, <c>pointerLockElement</c>, and the
    /// <c>onpointerlockchange</c> and <c>onpointerlockerror</c> event handlers. The default is true.
    /// </summary>
    public bool PointerLockEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether Prioritized Task Scheduling is exposed (https://wicg.github.io/scheduling-apis/):
    /// <c>scheduler</c> (<c>postTask()</c>, <c>yield()</c>), <c>Scheduler</c>, <c>TaskController</c>, <c>TaskSignal</c> and
    /// <c>TaskPriorityChangeEvent</c>. The default is true.
    /// </summary>
    public bool PrioritizedTaskSchedulingEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Web Locks API is exposed (https://w3c.github.io/web-locks/): <c>navigator.locks</c>,
    /// <c>LockManager</c> and <c>Lock</c>. The default is true.
    /// </summary>
    public bool WebLocksEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the storage manager of the Storage Standard is exposed (https://storage.spec.whatwg.org/):
    /// <c>navigator.storage</c> and <c>StorageManager</c>. When it is false, the origin private file system is not exposed
    /// either (see <see cref="FileSystemEnabled"/>). The default is true.
    /// </summary>
    public bool StorageManagerEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the origin private file system is exposed (https://fs.spec.whatwg.org/):
    /// <c>navigator.storage.getDirectory()</c>, <c>FileSystemHandle</c>, <c>FileSystemFileHandle</c>,
    /// <c>FileSystemDirectoryHandle</c>, <c>FileSystemWritableFileStream</c> and <c>FileSystemSyncAccessHandle</c>. The
    /// default is true.
    /// </summary>
    public bool FileSystemEnabled { get; init; } = true;

    /// <summary>Gets or sets whether <c>navigator.sendBeacon()</c> is exposed (https://w3c.github.io/beacon/). The default is true.</summary>
    public bool BeaconEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the client hints are enabled (https://wicg.github.io/client-hints-infrastructure/,
    /// https://wicg.github.io/ua-client-hints/): <c>navigator.userAgentData</c> and the <c>NavigatorUAData</c> interface. When
    /// disabled, no client hint is sent with the requests either (the <c>Sec-CH-*</c> headers), and the <c>Accept-CH</c> and
    /// <c>Critical-CH</c> headers and the <c>&lt;meta http-equiv="delegate-ch"&gt;</c> elements are ignored. The default is true.
    /// </summary>
    public bool ClientHintsEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Web Cryptography API is exposed (https://w3c.github.io/webcrypto/): <c>crypto</c>,
    /// <c>Crypto</c>, <c>SubtleCrypto</c> and <c>CryptoKey</c>. The default is true.
    /// </summary>
    public bool WebCryptoEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether <c>fetch()</c> and the <c>Request</c>, <c>Response</c> and <c>Headers</c> interfaces are exposed
    /// (https://fetch.spec.whatwg.org/#fetch-api). The default is true.
    /// </summary>
    public bool FetchEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the deferred fetching is exposed (https://fetch.spec.whatwg.org/#deferred-fetch):
    /// <c>fetchLater()</c> and <c>FetchLaterResult</c>, in the windows. They are also hidden when <see cref="FetchEnabled"/>
    /// is false. The default is true.
    /// </summary>
    public bool FetchLaterEnabled { get; init; } = true;

    /// <summary>Gets or sets whether the <c>XMLHttpRequest</c> interface is exposed (https://xhr.spec.whatwg.org/). The default is true.</summary>
    public bool XmlHttpRequestEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>Worker</c> interface is exposed: the dedicated workers
    /// (https://html.spec.whatwg.org/multipage/workers.html#dedicated-workers-and-the-worker-interface). The default is true.
    /// </summary>
    public bool DedicatedWorkersEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>SharedWorker</c> interface is exposed: the shared workers
    /// (https://html.spec.whatwg.org/multipage/workers.html#shared-workers-and-the-sharedworker-interface). The default is true.
    /// </summary>
    public bool SharedWorkersEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the service workers are exposed (https://w3c.github.io/ServiceWorker/): <c>navigator.serviceWorker</c>
    /// and the interfaces of the service workers, in the secure contexts. When it is false, no service worker is registered
    /// or run, and the requests are not intercepted. The default is true.
    /// </summary>
    public bool ServiceWorkersEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the asynchronous clipboard API is exposed (https://w3c.github.io/clipboard-apis/#async-clipboard-api):
    /// <c>navigator.clipboard</c> and the <c>Clipboard</c>, <c>ClipboardItem</c> and <c>ClipboardChangeEvent</c> interfaces.
    /// The copy, cut and paste actions are not affected (see <see cref="BrowsingContextOptions.ClipboardPermission"/>). The
    /// default is true.
    /// </summary>
    public bool AsyncClipboardEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Cookie Store API is exposed (https://cookiestore.spec.whatwg.org/): <c>cookieStore</c>,
    /// <c>CookieStore</c> and <c>CookieChangeEvent</c>. <c>document.cookie</c> is not affected. The default is true.
    /// </summary>
    public bool CookieStoreEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Web Storage API is exposed (https://html.spec.whatwg.org/multipage/webstorage.html):
    /// <c>localStorage</c>, <c>sessionStorage</c>, <c>Storage</c> and <c>StorageEvent</c>. The default is true.
    /// </summary>
    public bool WebStorageEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether IndexedDB is exposed (https://w3c.github.io/IndexedDB/): <c>indexedDB</c> and the <c>IDB*</c>
    /// interfaces. The default is true.
    /// </summary>
    public bool IndexedDBEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Cache API is exposed (https://w3c.github.io/ServiceWorker/#cache-objects): <c>caches</c>,
    /// <c>Cache</c> and <c>CacheStorage</c>, in the secure contexts, when <see cref="FetchEnabled"/> is true. The default is true.
    /// </summary>
    public bool CacheStorageEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>BroadcastChannel</c> interface is exposed
    /// (https://html.spec.whatwg.org/multipage/web-messaging.html#broadcasting-to-other-browsing-contexts). The default is true.
    /// </summary>
    public bool BroadcastChannelEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>CompressionStream</c> and <c>DecompressionStream</c> interfaces are exposed
    /// (https://compression.spec.whatwg.org/). The default is true.
    /// </summary>
    public bool CompressionStreamsEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the navigation API is exposed (https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api):
    /// <c>navigation</c> and the <c>Navigation*</c> interfaces. The default is true.
    /// </summary>
    public bool NavigationApiEnabled { get; init; } = true;

    /// <summary>Gets or sets whether the <c>URLPattern</c> interface is exposed (https://urlpattern.spec.whatwg.org/). The default is true.</summary>
    public bool UrlPatternEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>WebAssembly</c> namespace is exposed (https://webassembly.github.io/spec/js-api/), when
    /// <see cref="BrowsingContextOptions.WebAssemblyRuntime"/> is set. The default is true.
    /// </summary>
    public bool WebAssemblyEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>ResizeObserver</c>, <c>ResizeObserverEntry</c> and <c>ResizeObserverSize</c> interfaces are
    /// exposed (https://drafts.csswg.org/resize-observer-1/). Without resize observers, a document whose scripts never read the
    /// geometry is never laid out. The default is true.
    /// </summary>
    public bool ResizeObserverEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>IntersectionObserver</c> and <c>IntersectionObserverEntry</c> interfaces are exposed
    /// (https://w3c.github.io/IntersectionObserver/), and whether the <c>loading="lazy"</c> images and iframes are loaded lazily
    /// (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-loading-attributes): when disabled, they are loaded at
    /// once as with <c>loading="eager"</c>, so a document whose scripts never read the geometry is never laid out. The default
    /// is true.
    /// </summary>
    public bool IntersectionObserverEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the 2D contexts of the canvases are exposed (https://html.spec.whatwg.org/multipage/canvas.html#2dcontext):
    /// the <c>CanvasRenderingContext2D</c>, <c>CanvasGradient</c>, <c>CanvasPattern</c>, <c>TextMetrics</c>, <c>Path2D</c>,
    /// <c>OffscreenCanvas</c> and <c>OffscreenCanvasRenderingContext2D</c> interfaces; when disabled, <c>getContext("2d")</c>
    /// returns null. The 2D contexts also need a rendering backend (<see cref="BrowsingContextOptions.RenderingBackend"/>). The
    /// default is true.
    /// </summary>
    public bool CanvasEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the CSS Painting API is enabled (https://drafts.css-houdini.org/css-paint-api-1/): <c>CSS.paintWorklet</c>
    /// (and the <c>Worklet</c> interface), and the <c>paint()</c> images, which are invalid images when disabled. The paint
    /// worklets also need a script engine that supports the module scripts and a rendering backend
    /// (<see cref="BrowsingContextOptions.RenderingBackend"/>). The default is true.
    /// </summary>
    public bool PaintWorkletEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the CSS Layout API is enabled (https://drafts.css-houdini.org/css-layout-api-1/): <c>CSS.layoutWorklet</c>
    /// (and the <c>Worklet</c> interface), and the layout of the <c>display: layout()</c> boxes by the registered layout
    /// classes; when disabled, these boxes are flow-root block containers. The layout worklets also need a script engine that
    /// supports the module scripts. The default is true.
    /// </summary>
    public bool LayoutWorkletEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the CSS Animation Worklet API is exposed (https://drafts.css-houdini.org/css-animation-worklet-1/):
    /// <c>CSS.animationWorklet</c> (and the <c>Worklet</c> interface) and the <c>WorkletAnimation</c> interface. The
    /// animation worklets also need a script engine that supports the module scripts. The default is true.
    /// </summary>
    public bool AnimationWorkletEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Layout Instability API is exposed (https://wicg.github.io/layout-instability/): the
    /// <c>LayoutShift</c> and <c>LayoutShiftAttribution</c> interfaces and the <c>layout-shift</c> entry type of the
    /// performance observers. The layout shifts of a document are only computed once a performance observer of its window
    /// observes them, so a document whose scripts never read the geometry is never laid out. The default is true.
    /// </summary>
    public bool LayoutInstabilityEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the visibility state entries are exposed
    /// (https://html.spec.whatwg.org/multipage/interaction.html#the-visibilitystateentry-interface): the
    /// <c>VisibilityStateEntry</c> interface and the <c>visibility-state</c> entry type. <c>document.visibilityState</c> and the
    /// <c>visibilitychange</c> event are not affected. The default is true.
    /// </summary>
    public bool VisibilityStateEntryEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Paint Timing API is exposed (https://w3c.github.io/paint-timing/): the <c>PerformancePaintTiming</c>
    /// interface and the <c>paint</c> entry type (the first paint and the first contentful paint). The paints of a document are only
    /// computed once a script of its window observes or reads the paint timing entries, so a document whose scripts never do it is
    /// never laid out nor painted for them. The default is true.
    /// </summary>
    public bool PaintTimingEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Largest Contentful Paint API is exposed (https://w3c.github.io/largest-contentful-paint/): the
    /// <c>LargestContentfulPaint</c> interface and the <c>largest-contentful-paint</c> entry type. The default is true.
    /// </summary>
    public bool LargestContentfulPaintEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Element Timing API is exposed (https://w3c.github.io/element-timing/): the
    /// <c>PerformanceElementTiming</c> interface, the <c>element</c> entry type and <c>Element.elementTiming</c>. The default is true.
    /// </summary>
    public bool ElementTimingEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Event Timing API is exposed (https://w3c.github.io/event-timing/): the <c>PerformanceEventTiming</c>
    /// and <c>EventCounts</c> interfaces, <c>performance.eventCounts</c>, <c>performance.interactionCount</c> and the <c>event</c>
    /// and <c>first-input</c> entry types. When it is disabled, the trusted events of the user input are not timed (and they
    /// schedule no rendering update of the event loop). The default is true.
    /// </summary>
    public bool EventTimingEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the User Timing API is exposed (https://w3c.github.io/user-timing/): the <c>PerformanceMark</c> and
    /// <c>PerformanceMeasure</c> interfaces, <c>performance.mark()</c>, <c>measure()</c>, <c>clearMarks()</c> and
    /// <c>clearMeasures()</c>, and the <c>mark</c> and <c>measure</c> entry types. The default is true.
    /// </summary>
    public bool UserTimingEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Navigation Timing API is exposed (https://w3c.github.io/navigation-timing/): the
    /// <c>PerformanceNavigationTiming</c>, <c>PerformanceTiming</c> and <c>PerformanceNavigation</c> interfaces,
    /// <c>performance.timing</c> and <c>performance.navigation</c>, and the <c>navigation</c> entry type. The default is true.
    /// </summary>
    public bool NavigationTimingEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the scroll-driven animations are enabled (https://drafts.csswg.org/scroll-animations-1/): the
    /// <c>ScrollTimeline</c> and <c>ViewTimeline</c> interfaces and the <c>rangeStart</c> and <c>rangeEnd</c> members of
    /// <c>Animation</c>. When disabled, the scroll progress and view progress timelines of the styles
    /// (<c>animation-timeline: scroll()</c>, <c>view()</c> and the named timelines) are inactive, so a document is never laid
    /// out for them. The default is true.
    /// </summary>
    public bool ScrollDrivenAnimationsEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the SVG animations run (SMIL, https://svgwg.org/specs/animations/): the <c>animate</c>, <c>set</c>,
    /// <c>animateMotion</c> and <c>animateTransform</c> elements, and the <c>TimeEvent</c> interface. When disabled, the
    /// animation elements have no effect and fire no event. The default is true.
    /// </summary>
    public bool SvgAnimationsEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the view transitions are enabled (https://drafts.csswg.org/css-view-transitions-1/):
    /// <c>document.startViewTransition()</c>, <c>document.activeViewTransition</c>, <c>element.startViewTransition()</c>,
    /// <c>element.activeViewTransition</c> and the <c>ViewTransition</c> and <c>ViewTransitionTypeSet</c> interfaces. When
    /// disabled, the members and the interfaces are not exposed to the scripts (the <c>view-transition-*</c> properties and
    /// the pseudo-elements are still parsed). The default is true.
    /// </summary>
    public bool ViewTransitionsEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the text tracks of the media elements are enabled (https://html.spec.whatwg.org/multipage/media.html#timed-text-tracks,
    /// https://w3c.github.io/webvtt/): the <c>TextTrack</c>, <c>TextTrackList</c>, <c>TextTrackCue</c>, <c>TextTrackCueList</c>,
    /// <c>VTTCue</c> and <c>VTTRegion</c> interfaces, <c>HTMLMediaElement.textTracks</c>, <c>HTMLMediaElement.addTextTrack()</c>
    /// and <c>HTMLTrackElement.track</c>. When disabled, the resources of the <c>track</c> elements are not fetched and no
    /// track is enabled automatically, so no cue is rendered. The default is true.
    /// </summary>
    public bool TextTracksEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the Media Source Extensions are exposed (https://w3c.github.io/media-source/): the
    /// <c>MediaSource</c>, <c>MediaSourceHandle</c>, <c>SourceBuffer</c>, <c>SourceBufferList</c>, <c>ManagedMediaSource</c>,
    /// <c>ManagedSourceBuffer</c> and <c>BufferedChangeEvent</c> interfaces and the <c>sourceBuffer</c> attribute of the
    /// audio, video and text tracks. They need a media backend that parses the byte
    /// streams (<see cref="Media.MediaBackend.CreateSegmentParser"/>): without one, no type is supported. The default is
    /// true.
    /// </summary>
    public bool MediaSourceEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether <c>HTMLVideoElement.getVideoPlaybackQuality()</c> and the <c>VideoPlaybackQuality</c> interface
    /// are exposed (https://w3c.github.io/media-playback-quality/). The default is true.
    /// </summary>
    public bool MediaPlaybackQualityEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the <c>ReportingObserver</c> interface is exposed (https://w3c.github.io/reporting/#reportingobserver).
    /// The reports are still generated and delivered to the endpoints of the <c>Reporting-Endpoints</c> header. The default is true.
    /// </summary>
    public bool ReportingObserverEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the tentative extensions of the template elements that patch content are supported (the declarative
    /// partial updates, https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md): the
    /// <c>buffer</c>, <c>sanitize</c> and <c>src</c> attributes, and <c>HTMLTemplateElement.buffer</c>. The patching of the targeted markers
    /// (https://html.spec.whatwg.org/multipage/scripting.html#prepare-content-patching) is supported either way. The default is true.
    /// </summary>
    [Experimental(ExperimentalApis.DeclarativePartialUpdates, UrlFormat = ExperimentalApis.UrlFormat)]
    public bool DeclarativePartialUpdatesEnabled { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the CSS Parser API is exposed (https://wicg.github.io/css-parser-api/, a draft that no browser
    /// implements): <c>CSS.parseStylesheet()</c>, <c>CSS.parseRuleList()</c>, <c>CSS.parseRule()</c>,
    /// <c>CSS.parseDeclarationList()</c>, <c>CSS.parseDeclaration()</c>, <c>CSS.parseValue()</c>, <c>CSS.parseValueList()</c>,
    /// <c>CSS.parseCommaValueList()</c> and the <c>CSSParserRule</c>, <c>CSSParserAtRule</c>, <c>CSSParserQualifiedRule</c>,
    /// <c>CSSParserDeclaration</c>, <c>CSSParserValue</c>, <c>CSSParserBlock</c> and <c>CSSParserFunction</c> interfaces. The
    /// default is true.
    /// </summary>
    public bool CssParserApiEnabled { get; init; } = true;
}
