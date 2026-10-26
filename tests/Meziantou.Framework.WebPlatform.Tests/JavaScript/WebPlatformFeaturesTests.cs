using System.Reflection;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The features disabled by <see cref="BrowsingContextOptions.Features"/> are not exposed to the scripts.</summary>
public sealed class WebPlatformFeaturesTests : IDisposable
{
    private const string ProbeScript = """
        const probes = {
          WebSocketEnabled: typeof WebSocket,
          EventSourceEnabled: typeof EventSource,
          BeaconEnabled: typeof navigator.sendBeacon + ('sendBeacon' in navigator),
          ClientHintsEnabled: typeof navigator.userAgentData + typeof NavigatorUAData + ('userAgentData' in navigator),
          WebCryptoEnabled: typeof crypto + typeof Crypto + typeof SubtleCrypto + typeof CryptoKey + ('crypto' in self),
          FetchEnabled: typeof fetch + typeof Request + typeof Response + typeof Headers,
          FetchLaterEnabled: typeof fetchLater + typeof FetchLaterResult,
          XmlHttpRequestEnabled: typeof XMLHttpRequest + typeof XMLHttpRequestUpload,
          DedicatedWorkersEnabled: typeof Worker,
          SharedWorkersEnabled: typeof SharedWorker,
          ServiceWorkersEnabled: typeof navigator.serviceWorker + typeof ServiceWorker + typeof ServiceWorkerRegistration + typeof ServiceWorkerContainer + ('serviceWorker' in navigator),
          AsyncClipboardEnabled: typeof navigator.clipboard + typeof Clipboard + typeof ClipboardItem,
          CookieStoreEnabled: typeof cookieStore + typeof CookieStore + typeof CookieChangeEvent,
          IndexedDBEnabled: typeof indexedDB + typeof IDBFactory + typeof IDBKeyRange + ('indexedDB' in self),
          CacheStorageEnabled: typeof caches + typeof Cache + typeof CacheStorage + ('caches' in self),
          WebStorageEnabled: typeof localStorage + typeof sessionStorage + typeof Storage + typeof StorageEvent + ('localStorage' in window),
          BroadcastChannelEnabled: typeof BroadcastChannel,
          CompressionStreamsEnabled: typeof CompressionStream + typeof DecompressionStream,
          NavigationApiEnabled: typeof navigation + typeof Navigation + typeof NavigateEvent + ('navigation' in window),
          UrlPatternEnabled: typeof URLPattern,
          ObservableEnabled: typeof Observable + typeof Subscriber + typeof EventTarget.prototype.when + ('when' in document),
          WebAssemblyEnabled: typeof WebAssembly + ('WebAssembly' in self),
          ResizeObserverEnabled: typeof ResizeObserver + typeof ResizeObserverEntry + typeof ResizeObserverSize,
          IntersectionObserverEnabled: typeof IntersectionObserver + typeof IntersectionObserverEntry,
          LayoutInstabilityEnabled: typeof LayoutShift + typeof LayoutShiftAttribution + PerformanceObserver.supportedEntryTypes.includes('layout-shift'),
          PaintTimingEnabled: typeof PerformancePaintTiming + PerformanceObserver.supportedEntryTypes.includes('paint'),
          LargestContentfulPaintEnabled: typeof LargestContentfulPaint + PerformanceObserver.supportedEntryTypes.includes('largest-contentful-paint'),
          ElementTimingEnabled: typeof PerformanceElementTiming + PerformanceObserver.supportedEntryTypes.includes('element') + ('elementTiming' in Element.prototype),
          EventTimingEnabled: typeof PerformanceEventTiming + typeof EventCounts + PerformanceObserver.supportedEntryTypes.includes('event') + PerformanceObserver.supportedEntryTypes.includes('first-input') + ('eventCounts' in performance) + ('interactionCount' in performance),
          UserTimingEnabled: typeof PerformanceMark + typeof PerformanceMeasure + typeof performance.mark + typeof performance.measure + PerformanceObserver.supportedEntryTypes.includes('mark'),
          NavigationTimingEnabled: typeof PerformanceNavigationTiming + typeof PerformanceTiming + typeof PerformanceNavigation + typeof performance.timing + PerformanceObserver.supportedEntryTypes.includes('navigation') + ('navigation' in Performance.prototype),
          ScrollDrivenAnimationsEnabled: typeof ScrollTimeline + typeof ViewTimeline + ('rangeStart' in Animation.prototype),
          ReportingObserverEnabled: typeof ReportingObserver,
          DeclarativePartialUpdatesEnabled: String('buffer' in HTMLTemplateElement.prototype),
          SvgAnimationsEnabled: typeof TimeEvent,
          MediaSourceEnabled: typeof MediaSource + typeof MediaSourceHandle + typeof SourceBuffer + typeof SourceBufferList + typeof ManagedMediaSource + typeof ManagedSourceBuffer + typeof BufferedChangeEvent + ('sourceBuffer' in AudioTrack.prototype) + ('sourceBuffer' in VideoTrack.prototype),
          MediaPlaybackQualityEnabled: typeof VideoPlaybackQuality + ('getVideoPlaybackQuality' in HTMLVideoElement.prototype),
          TextTracksEnabled: typeof TextTrack + typeof TextTrackList + typeof TextTrackCue + typeof TextTrackCueList + typeof VTTCue + typeof VTTRegion + ('textTracks' in HTMLMediaElement.prototype) + ('addTextTrack' in HTMLMediaElement.prototype) + ('track' in HTMLTrackElement.prototype),
          ViewTransitionsEnabled: typeof ViewTransition + typeof ViewTransitionTypeSet + typeof CSSViewTransitionRule + typeof document.startViewTransition + ("activeViewTransition" in document),
          PermissionsPolicyEnabled: typeof PermissionsPolicy + typeof document.permissionsPolicy + ('permissionsPolicy' in HTMLIFrameElement.prototype),
          TouchEventsEnabled: typeof Touch + typeof TouchList + typeof TouchEvent,
          SpeculationRulesEnabled: HTMLScriptElement.supports('speculationrules') + String('prerendering' in document) + ('onprerenderingchange' in document),
          PaintWorkletEnabled: typeof CSS.paintWorklet + ('paintWorklet' in CSS),
          LayoutWorkletEnabled: typeof CSS.layoutWorklet + ('layoutWorklet' in CSS),
          AnimationWorkletEnabled: typeof CSS.animationWorklet + ('animationWorklet' in CSS) + typeof WorkletAnimation,
          CloseWatcherEnabled: typeof CloseWatcher,
          TextDirectivesEnabled: typeof FragmentDirective + typeof document.fragmentDirective,
          FullscreenEnabled: typeof Element.prototype.requestFullscreen + typeof document.exitFullscreen + ('fullscreenElement' in document) + ('fullscreenEnabled' in document) + ('fullscreen' in document) + ('onfullscreenchange' in document) + ('onfullscreenerror' in Element.prototype) + ('fullscreenElement' in ShadowRoot.prototype),
          PointerLockEnabled: typeof Element.prototype.requestPointerLock + typeof document.exitPointerLock + ('pointerLockElement' in document) + ('onpointerlockchange' in document) + ('onpointerlockerror' in document) + ('pointerLockElement' in ShadowRoot.prototype),
          PrioritizedTaskSchedulingEnabled: typeof scheduler + typeof Scheduler + typeof TaskController + typeof TaskSignal + typeof TaskPriorityChangeEvent,
          WebLocksEnabled: typeof navigator.locks + typeof LockManager + typeof Lock,
          StorageManagerEnabled: typeof navigator.storage + typeof StorageManager,
          FileSystemEnabled: typeof FileSystemHandle + typeof FileSystemFileHandle + typeof FileSystemDirectoryHandle + typeof FileSystemWritableFileStream + String(typeof StorageManager !== 'undefined' && 'getDirectory' in StorageManager.prototype),
          VisibilityStateEntryEnabled: typeof VisibilityStateEntry + PerformanceObserver.supportedEntryTypes.includes('visibility-state'),
          CssParserApiEnabled: typeof CSS.parseStylesheet + typeof CSS.parseRuleList + typeof CSS.parseRule + typeof CSS.parseDeclarationList + typeof CSS.parseDeclaration + typeof CSS.parseValue + typeof CSS.parseValueList + typeof CSS.parseCommaValueList + ('parseValue' in CSS) + typeof CSSParserRule + typeof CSSParserAtRule + typeof CSSParserQualifiedRule + typeof CSSParserDeclaration + typeof CSSParserValue + typeof CSSParserBlock + typeof CSSParserFunction,
          CanvasEnabled: typeof CanvasRenderingContext2D + typeof CanvasGradient + typeof CanvasPattern + typeof TextMetrics + typeof Path2D + typeof OffscreenCanvas + typeof OffscreenCanvasRenderingContext2D + ('transferControlToOffscreen' in HTMLCanvasElement.prototype),
        };
        document.title = JSON.stringify(probes);
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly Wasmtime.WasmtimeRuntime WebAssemblyRuntime = new();

    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, WebPlatformFeatures? features)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            WebAssemblyRuntime = WebAssemblyRuntime,
            DocumentLoader = _loader,
            Features = features,
            UnhandledScriptError = (_, error) =>
            {
                lock (_errors)
                {
                    _errors.Add(error);
                }
            },
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static IEnumerable<PropertyInfo> FeatureProperties => typeof(WebPlatformFeatures).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    public static TheoryData<string> FeatureNames => [.. FeatureProperties.Select(property => property.Name)];

    /// <summary>Returns features where the given features are disabled.</summary>
    private static WebPlatformFeatures Disable(params string[] names)
    {
        var features = new WebPlatformFeatures();
        foreach (var name in names)
        {
            typeof(WebPlatformFeatures).GetProperty(name)!.SetValue(features, false);
        }

        return features;
    }

    private Dictionary<string, string> Probe(WebPlatformFeatures? features)
    {
        var document = Load("<script>" + ProbeScript + "</script>", features);
        Assert.Empty(_errors);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(document.Title)!;
    }

    [Fact]
    public void AllFeaturesAreEnabledByDefault()
    {
        var probes = Probe(features: null);

        Assert.Equal(FeatureProperties.Select(property => property.Name).Order(StringComparer.Ordinal), probes.Keys.Order(StringComparer.Ordinal));
        Assert.All(FeatureProperties, property => Assert.True((bool)property.GetValue(WebPlatformFeatures.Default)!));
        Assert.All(probes, probe => Assert.DoesNotMatch("undefined|false", probe.Value));
    }

    [Fact]
    public void DisabledFeaturesAreNotExposed()
    {
        var probes = Probe(Disable([.. FeatureProperties.Select(property => property.Name)]));

        Assert.All(probes, probe => Assert.DoesNotMatch("function|object|true", probe.Value));
    }

    [Theory]
    [MemberData(nameof(FeatureNames))]
    public void DisabledFeature_DoesNotAffectTheOtherFeatures(string feature)
    {
        var probes = Probe(Disable(feature));

        foreach (var (name, value) in probes)
        {
            if (name == feature)
            {
                Assert.DoesNotMatch("function|object|true", value);
            }
            else if (feature == nameof(WebPlatformFeatures.FetchEnabled) && name == nameof(WebPlatformFeatures.CacheStorageEnabled))
            {
                // The Cache API stores the Request and Response objects of fetch
                Assert.DoesNotMatch("function|object|true", value);
            }
            else if (feature == nameof(WebPlatformFeatures.FetchEnabled) && name == nameof(WebPlatformFeatures.FetchLaterEnabled))
            {
                // fetchLater() defers a request of fetch
                Assert.DoesNotMatch("function|object|true", value);
            }
            else if (feature == nameof(WebPlatformFeatures.StorageManagerEnabled) && name == nameof(WebPlatformFeatures.FileSystemEnabled))
            {
                // The origin private file system is obtained from the storage manager
                Assert.DoesNotMatch("function|object|true", value);
            }
            else
            {
                Assert.DoesNotMatch("undefined|false", value);
            }
        }
    }

    [Fact]
    public void DisabledFeatures_AreNotExposedInTheWorkers()
    {
        _loader.Resources["https://example.com/worker.js"] = new DocumentResource("""
            postMessage([typeof fetch, typeof Request, typeof Headers, typeof crypto, 'crypto' in self, typeof WebSocket, typeof CompressionStream, typeof TextEncoder].join());
            """)
        { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<script>new Worker('worker.js').onmessage = e => document.title = e.data;</script>", new WebPlatformFeatures { FetchEnabled = false, WebCryptoEnabled = false, WebSocketEnabled = false, CompressionStreamsEnabled = false });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (document.Title.Length == 0 && stopwatch.Elapsed < Timeout)
        {
            _contexts.EventLoop.RunUntilIdle();
            Thread.Sleep(1);
        }

        Assert.Equal("undefined,undefined,undefined,undefined,false,undefined,undefined,function", document.Title);
    }
}
