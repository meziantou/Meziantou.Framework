namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The type of the navigation that created a document (https://w3c.github.io/navigation-timing/#sec-performance-navigation-types).</summary>
/// <seealso href="https://w3c.github.io/navigation-timing/#dom-navigationtimingtype"/>
[IdlInterface("NavigationTimingType", "https://w3c.github.io/navigation-timing/#dom-navigationtimingtype")]
public enum NavigationTimingType
{
    /// <summary>A navigation that is not a reload nor a traversal of the session history.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-navigationtimingtype-navigate"/>
    [IdlMember("navigate", "https://w3c.github.io/navigation-timing/#dom-navigationtimingtype-navigate")]
    Navigate,

    /// <summary>A reload of the document.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-navigationtimingtype-reload"/>
    [IdlMember("reload", "https://w3c.github.io/navigation-timing/#dom-navigationtimingtype-reload")]
    Reload,

    /// <summary>A traversal of the session history.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-navigationtimingtype-back_forward"/>
    [IdlMember("back_forward", "https://w3c.github.io/navigation-timing/#dom-navigationtimingtype-back_forward")]
    BackForward,
}

/// <summary>
/// The timing of the navigation that created a document and of the loading of the document: the document load timing info
/// (https://html.spec.whatwg.org/multipage/dom.html#document-load-timing-info) and the timing info of the fetch of the
/// navigation (https://fetch.spec.whatwg.org/#fetch-timing-info). The times are timestamps of
/// <see cref="DomEventLoop.TimeProvider"/>, or 0 when the step did not happen (yet).
/// </summary>
internal sealed class DocumentLoadTimingInfo
{
    /// <summary>The time the navigation started: the time origin of the window of the document.</summary>
    public long NavigationStart { get; set; }

    /// <summary>The time the unload event of the previous document started, when the previous document has the origin of the document.</summary>
    public long UnloadEventStart { get; set; }

    public long UnloadEventEnd { get; set; }

    /// <summary>The time the first request of the navigation was sent, when it was redirected.</summary>
    public long RedirectStart { get; set; }

    /// <summary>The time the response of the last redirect was received.</summary>
    public long RedirectEnd { get; set; }

    /// <summary>The time the request of the document (after the redirects) was sent: the time its fetch event was dispatched when a service worker handles it.</summary>
    public long FetchStart { get; set; }

    /// <summary>The time the request of the document was given to a service worker (before the worker is started and activated), or 0.</summary>
    public long WorkerStart { get; set; }

    /// <summary>The time the service worker started to evaluate its router rules, or 0 (see <see cref="FetchTimestamps.ServiceWorkerRouterEvaluationStart"/>).</summary>
    public long WorkerRouterEvaluationStart { get; set; }

    /// <summary>The time the service worker started to look up its caches for a router rule, or 0 (see <see cref="FetchTimestamps.ServiceWorkerCacheLookupStart"/>).</summary>
    public long WorkerCacheLookupStart { get; set; }

    /// <summary>The source of the router rule that matched the request of the document, or the empty string.</summary>
    public string WorkerMatchedRouterSource { get; set; } = "";

    /// <summary>The source that gave the response of the document when the request matched a router rule, or the empty string.</summary>
    public string WorkerFinalRouterSource { get; set; } = "";

    /// <summary>The time the response of the document was received.</summary>
    public long ResponseStart { get; set; }

    /// <summary>The time the body of the response was received.</summary>
    public long ResponseEnd { get; set; }

    /// <summary>The time the document was created (its readiness is <c>loading</c>).</summary>
    public long DomLoading { get; set; }

    public long DomInteractive { get; set; }

    public long DomContentLoadedEventStart { get; set; }

    public long DomContentLoadedEventEnd { get; set; }

    public long DomComplete { get; set; }

    public long LoadEventStart { get; set; }

    public long LoadEventEnd { get; set; }

    /// <summary>The number of redirects of the navigation, or 0 when their timing is not exposed (see <see cref="ExposesRedirects"/>).</summary>
    public int RedirectCount { get; set; }

    /// <summary>The time the navigation was restarted to send the critical client hints (https://wicg.github.io/client-hints-infrastructure/#critical-ch-restart-time).</summary>
    public long CriticalCHRestart { get; set; }

    /// <summary>
    /// Whether the redirect taint of the response is not "same-origin" (https://fetch.spec.whatwg.org/#response-redirect-taint):
    /// the document was created via cross-origin redirects, and the timing of the unload of the previous document is not exposed.
    /// </summary>
    public bool HasCrossOriginRedirects { get; set; }

    /// <summary>Whether the request of the navigation has a client and no referrer: its redirects are not exposed when they are cross-origin.</summary>
    public bool HasNoReferrer { get; set; }

    /// <summary>The redirects of the navigation: the URL that redirected, and the values of the <c>Timing-Allow-Origin</c> header of its response.</summary>
    public List<(Urls.UrlRecord Url, List<string> TimingAllowOrigin)> Redirects { get; } = [];

    public NavigationTimingType Type { get; set; }

    /// <summary>
    /// The delivery type of the response (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-deliverytype):
    /// <c>navigational-prefetch</c> when the navigation used the response of a navigational prefetch, else the empty string.
    /// </summary>
    public string DeliveryType { get; set; } = "";

    /// <summary>
    /// The time the response of the navigational prefetch used by the navigation was received (not earlier than the fetch
    /// start of the navigation), or 0.
    /// </summary>
    public long PrefetchResponseStart { get; set; }

    /// <summary>The time the prerendered document was activated (https://wicg.github.io/nav-speculation/prerendering.html#document-activation-start-time), or 0.</summary>
    public long ActivationStart { get; set; }

    /// <summary>
    /// Whether the timing and the count of the redirects are exposed to a document of an origin
    /// (https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object): every redirect comes
    /// from the origin of the document, or the request has a referrer and the navigation TAO check passes
    /// (https://fetch.spec.whatwg.org/#navigation-tao-check): the response of every redirect has a
    /// <c>Timing-Allow-Origin</c> header that allows the origin of the document. The specification exposes them when the
    /// redirect taint of the response is "same-origin", which also covers the redirects of the origin of the initiator to a
    /// document of another origin: as the browsers, they are not exposed to that document.
    /// </summary>
    public bool ExposesRedirects(OriginRecord origin)
    {
        if (Redirects.Count == 0)
            return false;

        if (Redirects.TrueForAll(redirect => OriginRecord.IsSameOrigin(OriginRecord.FromUrl(redirect.Url), origin)))
            return true;

        if (HasNoReferrer)
            return false;

        var serializedOrigin = origin.Serialize();
        return Redirects.TrueForAll(redirect => redirect.TimingAllowOrigin.Contains("*") || redirect.TimingAllowOrigin.Contains(serializedOrigin));
    }

    /// <summary>The timing of a document that is not fetched (e.g. the initial <c>about:blank</c> document, or the document of a resource given to the navigation): the navigation, the request and the response happen at the same time.</summary>
    public static DocumentLoadTimingInfo CreateImmediate(long timestamp) => new() { NavigationStart = timestamp, FetchStart = timestamp, ResponseStart = timestamp, ResponseEnd = timestamp };
}

/// <summary>
/// The timing of the navigation that created the document of a window
/// (https://w3c.github.io/navigation-timing/#sec-PerformanceNavigationTiming): the entry of the <c>navigation</c> type of the
/// performance timeline of the window, available as soon as the document is created. The times are in milliseconds relative to
/// the time origin of the window (the start of the navigation), and 0 until the step they measure happens.
/// </summary>
/// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming"/>
[IdlInterface("PerformanceNavigationTiming", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming")]
public sealed class PerformanceNavigationTiming : PerformanceResourceTiming
{
    /// <summary>The entry type of the navigation timing entries.</summary>
    internal const string NavigationEntryType = "navigation";

    private readonly DocumentLoadTimingInfo _loadTimingInfo;

    internal PerformanceNavigationTiming(Window window, string url, DocumentLoadTimingInfo loadTimingInfo, FetchTimingInfo timingInfo, ResponseBodyInfo bodyInfo, int responseStatus, string contentEncoding, IReadOnlyList<PerformanceServerTiming> serverTiming)
        : base(window, NavigationEntryType, url, "navigation", timingInfo, bodyInfo, responseStatus, contentEncoding, serverTiming)
    {
        _loadTimingInfo = loadTimingInfo;
    }

    /// <summary>The timing of the navigation and of the loading of the document.</summary>
    internal DocumentLoadTimingInfo LoadTimingInfo => _loadTimingInfo;

    /// <summary>The duration of the entry: the time between the start of the navigation and the end of the load event (https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-duration).</summary>
    private protected override double GetDuration() => LoadEventEnd - StartTime;

    /// <summary>Gets the time the unload event of the previous document started, or 0 when there is no previous document or when it has another origin.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-unloadeventstart"/>
    [IdlMember("unloadEventStart", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-unloadeventstart")]
    public double UnloadEventStart => ToRelativeTime(_loadTimingInfo.UnloadEventStart);

    /// <summary>Gets the time the unload event of the previous document completed, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-unloadeventend"/>
    [IdlMember("unloadEventEnd", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-unloadeventend")]
    public double UnloadEventEnd => ToRelativeTime(_loadTimingInfo.UnloadEventEnd);

    /// <summary>Gets the time the readiness of the document became <c>interactive</c>.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-dominteractive"/>
    [IdlMember("domInteractive", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-dominteractive")]
    public double DomInteractive => ToRelativeTime(_loadTimingInfo.DomInteractive);

    /// <summary>Gets the time the <c>DOMContentLoaded</c> event started.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-domcontentloadedeventstart"/>
    [IdlMember("domContentLoadedEventStart", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-domcontentloadedeventstart")]
    public double DomContentLoadedEventStart => ToRelativeTime(_loadTimingInfo.DomContentLoadedEventStart);

    /// <summary>Gets the time the <c>DOMContentLoaded</c> event completed.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-domcontentloadedeventend"/>
    [IdlMember("domContentLoadedEventEnd", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-domcontentloadedeventend")]
    public double DomContentLoadedEventEnd => ToRelativeTime(_loadTimingInfo.DomContentLoadedEventEnd);

    /// <summary>Gets the time the readiness of the document became <c>complete</c>.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-domcomplete"/>
    [IdlMember("domComplete", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-domcomplete")]
    public double DomComplete => ToRelativeTime(_loadTimingInfo.DomComplete);

    /// <summary>Gets the time the <c>load</c> event started.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-loadeventstart"/>
    [IdlMember("loadEventStart", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-loadeventstart")]
    public double LoadEventStart => ToRelativeTime(_loadTimingInfo.LoadEventStart);

    /// <summary>Gets the time the <c>load</c> event completed.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-loadeventend"/>
    [IdlMember("loadEventEnd", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-loadeventend")]
    public double LoadEventEnd => ToRelativeTime(_loadTimingInfo.LoadEventEnd);

    /// <summary>Gets the type of the navigation.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-type"/>
    [IdlMember("type", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-type")]
    public NavigationTimingType Type => _loadTimingInfo.Type;

    /// <summary>Gets the number of redirects of the navigation, or 0 when one of them is cross-origin.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-redirectcount"/>
    [IdlMember("redirectCount", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-redirectcount")]
    public ushort RedirectCount => (ushort)_loadTimingInfo.RedirectCount;

    /// <summary>Gets the time the navigation was restarted to send the critical client hints (the <c>Critical-CH</c> header of a response), or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-criticalchrestart"/>
    [IdlMember("criticalCHRestart", "https://w3c.github.io/navigation-timing/#dom-performancenavigationtiming-criticalchrestart")]
    public double CriticalCHRestart => ToRelativeTime(_loadTimingInfo.CriticalCHRestart);

    /// <summary>
    /// Gets the time the prerendered document was activated, or 0 for a document that was not prerendered
    /// (https://wicg.github.io/nav-speculation/prerendering.html#dom-performancenavigationtiming-activationstart).
    /// </summary>
    /// <seealso href="https://wicg.github.io/nav-speculation/prerendering.html#dom-performancenavigationtiming-activationstart"/>
    [IdlMember("activationStart", "https://wicg.github.io/nav-speculation/prerendering.html#dom-performancenavigationtiming-activationstart")]
    public double ActivationStart => ToRelativeTime(_loadTimingInfo.ActivationStart);

    private double ToRelativeTime(long timestamp) => timestamp == 0 ? 0 : Math.Max(0, Global.ToRelativeHighResolutionTime(timestamp));
}

/// <summary>
/// The legacy timing of the navigation of the document of a window, in milliseconds since the Unix epoch
/// (https://w3c.github.io/navigation-timing/#sec-navigation-timing-interface, <c>performance.timing</c>): the values of the
/// <see cref="PerformanceNavigationTiming"/> entry of the window.
/// </summary>
/// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming"/>
[IdlInterface("PerformanceTiming", "https://w3c.github.io/navigation-timing/#dom-performancetiming")]
public sealed class PerformanceTiming
{
    /// <summary>The names of the attributes: the names that a user timing mark of a window cannot have (https://w3c.github.io/user-timing/#dom-performancemark-constructor).</summary>
    internal static readonly string[] AttributeNames =
    [
        "navigationStart", "unloadEventStart", "unloadEventEnd", "redirectStart", "redirectEnd", "fetchStart", "domainLookupStart",
        "domainLookupEnd", "connectStart", "connectEnd", "secureConnectionStart", "requestStart", "responseStart", "responseEnd",
        "domLoading", "domInteractive", "domContentLoadedEventStart", "domContentLoadedEventEnd", "domComplete", "loadEventStart",
        "loadEventEnd",
    ];

    private readonly Window _window;

    internal PerformanceTiming(Window window)
    {
        _window = window;
    }

    private PerformanceNavigationTiming? Entry => _window.Performance.NavigationTimingEntry;

    /// <summary>Gets the time the navigation started: the time origin of the window.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-navigationstart"/>
    [IdlMember("navigationStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-navigationstart")]
    public ulong NavigationStart => ToEpochTime(0, zeroWhenNotSet: false);

    /// <summary>Gets the time the unload event of the previous document started, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-unloadeventstart"/>
    [IdlMember("unloadEventStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-unloadeventstart")]
    public ulong UnloadEventStart => ToEpochTime(Entry?.UnloadEventStart);

    /// <summary>Gets the time the unload event of the previous document completed, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-unloadeventend"/>
    [IdlMember("unloadEventEnd", "https://w3c.github.io/navigation-timing/#dom-performancetiming-unloadeventend")]
    public ulong UnloadEventEnd => ToEpochTime(Entry?.UnloadEventEnd);

    /// <summary>Gets the time the first redirect started, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-redirectstart"/>
    [IdlMember("redirectStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-redirectstart")]
    public ulong RedirectStart => Entry is { RedirectCount: > 0 } entry ? ToEpochTime(entry.RedirectStart, zeroWhenNotSet: false) : 0;

    /// <summary>Gets the time the last redirect completed, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-redirectend"/>
    [IdlMember("redirectEnd", "https://w3c.github.io/navigation-timing/#dom-performancetiming-redirectend")]
    public ulong RedirectEnd => Entry is { RedirectCount: > 0 } entry ? ToEpochTime(entry.RedirectEnd, zeroWhenNotSet: false) : 0;

    /// <summary>Gets the time the fetch of the document started.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-fetchstart"/>
    [IdlMember("fetchStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-fetchstart")]
    public ulong FetchStart => ToEpochTime(Entry?.FetchStart, zeroWhenNotSet: false);

    /// <summary>Gets the time the domain name lookup started.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-domainlookupstart"/>
    [IdlMember("domainLookupStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-domainlookupstart")]
    public ulong DomainLookupStart => ToEpochTime(Entry?.DomainLookupStart, zeroWhenNotSet: false);

    /// <summary>Gets the time the domain name lookup completed.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-domainlookupend"/>
    [IdlMember("domainLookupEnd", "https://w3c.github.io/navigation-timing/#dom-performancetiming-domainlookupend")]
    public ulong DomainLookupEnd => ToEpochTime(Entry?.DomainLookupEnd, zeroWhenNotSet: false);

    /// <summary>Gets the time the connection started.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-connectstart"/>
    [IdlMember("connectStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-connectstart")]
    public ulong ConnectStart => ToEpochTime(Entry?.ConnectStart, zeroWhenNotSet: false);

    /// <summary>Gets the time the connection was established.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-connectend"/>
    [IdlMember("connectEnd", "https://w3c.github.io/navigation-timing/#dom-performancetiming-connectend")]
    public ulong ConnectEnd => ToEpochTime(Entry?.ConnectEnd, zeroWhenNotSet: false);

    /// <summary>Gets the time the TLS handshake started, or 0 for a connection that is not secure.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-secureconnectionstart"/>
    [IdlMember("secureConnectionStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-secureconnectionstart")]
    public ulong SecureConnectionStart => Entry is { } entry && entry.IsSecureConnection ? ToEpochTime(entry.SecureConnectionStart, zeroWhenNotSet: false) : 0;

    /// <summary>Gets the time the request was sent.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-requeststart"/>
    [IdlMember("requestStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-requeststart")]
    public ulong RequestStart => ToEpochTime(Entry?.RequestStart, zeroWhenNotSet: false);

    /// <summary>Gets the time the response was received.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-responsestart"/>
    [IdlMember("responseStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-responsestart")]
    public ulong ResponseStart => ToEpochTime(Entry?.ResponseStart, zeroWhenNotSet: false);

    /// <summary>Gets the time the body of the response was received.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-responseend"/>
    [IdlMember("responseEnd", "https://w3c.github.io/navigation-timing/#dom-performancetiming-responseend")]
    public ulong ResponseEnd => ToEpochTime(Entry?.ResponseEnd, zeroWhenNotSet: false);

    /// <summary>Gets the time the document was created (its readiness is <c>loading</c>).</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-domloading"/>
    [IdlMember("domLoading", "https://w3c.github.io/navigation-timing/#dom-performancetiming-domloading")]
    public ulong DomLoading => Entry is { } entry ? ToEpochTime(_window.ToRelativeHighResolutionTime(entry.LoadTimingInfo.DomLoading), zeroWhenNotSet: false) : 0;

    /// <summary>Gets the time the readiness of the document became <c>interactive</c>, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-dominteractive"/>
    [IdlMember("domInteractive", "https://w3c.github.io/navigation-timing/#dom-performancetiming-dominteractive")]
    public ulong DomInteractive => ToEpochTime(Entry?.DomInteractive);

    /// <summary>Gets the time the <c>DOMContentLoaded</c> event started, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-domcontentloadedeventstart"/>
    [IdlMember("domContentLoadedEventStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-domcontentloadedeventstart")]
    public ulong DomContentLoadedEventStart => ToEpochTime(Entry?.DomContentLoadedEventStart);

    /// <summary>Gets the time the <c>DOMContentLoaded</c> event completed, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-domcontentloadedeventend"/>
    [IdlMember("domContentLoadedEventEnd", "https://w3c.github.io/navigation-timing/#dom-performancetiming-domcontentloadedeventend")]
    public ulong DomContentLoadedEventEnd => ToEpochTime(Entry?.DomContentLoadedEventEnd);

    /// <summary>Gets the time the readiness of the document became <c>complete</c>, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-domcomplete"/>
    [IdlMember("domComplete", "https://w3c.github.io/navigation-timing/#dom-performancetiming-domcomplete")]
    public ulong DomComplete => ToEpochTime(Entry?.DomComplete);

    /// <summary>Gets the time the <c>load</c> event started, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-loadeventstart"/>
    [IdlMember("loadEventStart", "https://w3c.github.io/navigation-timing/#dom-performancetiming-loadeventstart")]
    public ulong LoadEventStart => ToEpochTime(Entry?.LoadEventStart);

    /// <summary>Gets the time the <c>load</c> event completed, or 0.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancetiming-loadeventend"/>
    [IdlMember("loadEventEnd", "https://w3c.github.io/navigation-timing/#dom-performancetiming-loadeventend")]
    public ulong LoadEventEnd => ToEpochTime(Entry?.LoadEventEnd);

    /// <summary>
    /// Returns the value of an attribute, relative to the start of the navigation, for the user timing marks named after it
    /// (https://w3c.github.io/user-timing/#dfn-convert-a-name-to-a-timestamp), or null when the name is not the name of an attribute.
    /// </summary>
    internal ulong? GetValue(string name) => name switch
    {
        "navigationStart" => NavigationStart,
        "unloadEventStart" => UnloadEventStart,
        "unloadEventEnd" => UnloadEventEnd,
        "redirectStart" => RedirectStart,
        "redirectEnd" => RedirectEnd,
        "fetchStart" => FetchStart,
        "domainLookupStart" => DomainLookupStart,
        "domainLookupEnd" => DomainLookupEnd,
        "connectStart" => ConnectStart,
        "connectEnd" => ConnectEnd,
        "secureConnectionStart" => SecureConnectionStart,
        "requestStart" => RequestStart,
        "responseStart" => ResponseStart,
        "responseEnd" => ResponseEnd,
        "domLoading" => DomLoading,
        "domInteractive" => DomInteractive,
        "domContentLoadedEventStart" => DomContentLoadedEventStart,
        "domContentLoadedEventEnd" => DomContentLoadedEventEnd,
        "domComplete" => DomComplete,
        "loadEventStart" => LoadEventStart,
        "loadEventEnd" => LoadEventEnd,
        _ => null,
    };

    /// <summary>Converts a time relative to the time origin of the window to milliseconds since the Unix epoch. A time that is not set (0, or no entry) is 0 unless it cannot be unset.</summary>
    private ulong ToEpochTime(double? relativeTime, bool zeroWhenNotSet = true)
    {
        if (zeroWhenNotSet && relativeTime is null or 0)
            return 0;

        return (ulong)Math.Max(0, Math.Round(_window.TimeOrigin + (relativeTime ?? 0), MidpointRounding.AwayFromZero));
    }
}

/// <summary>
/// The legacy description of the navigation of the document of a window
/// (https://w3c.github.io/navigation-timing/#sec-window.performance-attribute, <c>performance.navigation</c>).
/// </summary>
/// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigation"/>
[IdlInterface("PerformanceNavigation", "https://w3c.github.io/navigation-timing/#dom-performancenavigation")]
public sealed class PerformanceNavigation
{
    /// <summary>A navigation that is not a reload nor a traversal of the session history.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_navigate"/>
    [IdlMember("TYPE_NAVIGATE", "https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_navigate")]
    public const ushort TypeNavigate = 0;

    /// <summary>A reload of the document.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_reload"/>
    [IdlMember("TYPE_RELOAD", "https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_reload")]
    public const ushort TypeReload = 1;

    /// <summary>A traversal of the session history.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_back_forward"/>
    [IdlMember("TYPE_BACK_FORWARD", "https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_back_forward")]
    public const ushort TypeBackForward = 2;

    /// <summary>Another type of navigation.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_reserved"/>
    [IdlMember("TYPE_RESERVED", "https://w3c.github.io/navigation-timing/#dom-performancenavigation-type_reserved")]
    public const ushort TypeReserved = 255;

    private readonly Window _window;

    internal PerformanceNavigation(Window window)
    {
        _window = window;
    }

    /// <summary>Gets the type of the navigation: <see cref="TypeNavigate"/>, <see cref="TypeReload"/> or <see cref="TypeBackForward"/>.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigation-type"/>
    [IdlMember("type", "https://w3c.github.io/navigation-timing/#dom-performancenavigation-type")]
    public ushort Type => _window.Performance.NavigationTimingEntry?.Type switch
    {
        NavigationTimingType.Reload => TypeReload,
        NavigationTimingType.BackForward => TypeBackForward,
        _ => TypeNavigate,
    };

    /// <summary>Gets the number of redirects of the navigation, or 0 when one of them is cross-origin.</summary>
    /// <seealso href="https://w3c.github.io/navigation-timing/#dom-performancenavigation-redirectcount"/>
    [IdlMember("redirectCount", "https://w3c.github.io/navigation-timing/#dom-performancenavigation-redirectcount")]
    public ushort RedirectCount => _window.Performance.NavigationTimingEntry?.RedirectCount ?? 0;
}
