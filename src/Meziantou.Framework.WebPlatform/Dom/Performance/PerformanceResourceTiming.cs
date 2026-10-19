namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The timing of the fetch of a resource (https://w3c.github.io/resource-timing/#sec-performanceresourcetiming), added to
/// the performance timeline of the global object that fetched it. The times are in milliseconds relative to the time origin
/// of the global object, or 0 when they are not exposed (e.g. the timing allow check failed for a cross-origin resource).
/// </summary>
/// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming"/>
[IdlInterface("PerformanceResourceTiming", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming")]
public class PerformanceResourceTiming : PerformanceEntry
{
    /// <summary>The entry type of the resource timing entries.</summary>
    internal const string ResourceEntryType = "resource";

    private readonly FetchTimingInfo _timingInfo;
    private ResponseBodyInfo _bodyInfo;

    internal PerformanceResourceTiming(GlobalScope global, string requestedUrl, string initiatorType, FetchTimingInfo timingInfo, ResponseBodyInfo bodyInfo, int responseStatus, string contentEncoding = "", IReadOnlyList<PerformanceServerTiming>? serverTiming = null)
        : this(global, ResourceEntryType, requestedUrl, initiatorType, timingInfo, bodyInfo, responseStatus, contentEncoding, serverTiming)
    {
    }

    private protected PerformanceResourceTiming(GlobalScope global, string entryType, string requestedUrl, string initiatorType, FetchTimingInfo timingInfo, ResponseBodyInfo bodyInfo, int responseStatus, string contentEncoding, IReadOnlyList<PerformanceServerTiming>? serverTiming)
        : base(global, entryType, requestedUrl, timingInfo.StartTime, timingInfo.EndTime - timingInfo.StartTime)
    {
        InitiatorType = initiatorType;
        _timingInfo = timingInfo;
        _bodyInfo = bodyInfo;
        ResponseStatus = (ushort)responseStatus;
        ContentEncoding = contentEncoding;
        ServerTiming = serverTiming ?? [];
    }

    /// <summary>Whether the connection of the final response is secure (the scheme of its URL is <c>https</c>).</summary>
    internal bool IsSecureConnection => _timingInfo.FinalConnectionTimingInfo is { SecureConnectionStartTime: > 0 } or { IsSecure: true };

    /// <summary>Sets the end time and the sizes of the body of a response that is received after the entry is created (the document of a navigation).</summary>
    internal void SetResponseEnd(double endTime, ulong encodedBodySize, ulong decodedBodySize)
    {
        _timingInfo.EndTime = endTime;
        _bodyInfo = _bodyInfo with { EncodedSize = encodedBodySize, DecodedSize = decodedBodySize };
    }

    /// <summary>
    /// Gets the type of the initiator of the fetch (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-initiatortype):
    /// <c>css</c> for the resources of the style sheets (<c>@import</c>, fonts, images), the local name of the element that
    /// fetched the resource (e.g. <c>img</c>, <c>script</c>, <c>link</c>), <c>fetch</c>, <c>xmlhttprequest</c>, or <c>other</c>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-initiatortype"/>
    [IdlMember("initiatorType", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-initiatortype")]
    public string InitiatorType { get; }

    /// <summary>
    /// Gets the delivery type of the resource: <c>navigational-prefetch</c> for the document of a navigation that used a
    /// navigational prefetch, else the empty string (there is no HTTP cache).
    /// </summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-deliverytype"/>
    [IdlMember("deliveryType", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-deliverytype")]
    public string DeliveryType => (this as PerformanceNavigationTiming)?.LoadTimingInfo.DeliveryType ?? "";

    /// <summary>Gets the ALPN protocol of the connection (e.g. <c>http/1.1</c>), or the empty string when it is not exposed.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-nexthopprotocol"/>
    [IdlMember("nextHopProtocol", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-nexthopprotocol")]
    public string NextHopProtocol => _timingInfo.FinalConnectionTimingInfo?.AlpnNegotiatedProtocol ?? "";

    /// <summary>Gets the time the fetch event of the request was dispatched to a service worker, or 0 when no service worker handled the request.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerstart"/>
    [IdlMember("workerStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerstart")]
    public double WorkerStart => _timingInfo.FinalServiceWorkerStartTime;

    /// <summary>Gets the start time of the first redirect, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-redirectstart"/>
    [IdlMember("redirectStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-redirectstart")]
    public double RedirectStart => _timingInfo.RedirectStartTime;

    /// <summary>Gets the time the last redirect completed, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-redirectend"/>
    [IdlMember("redirectEnd", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-redirectend")]
    public double RedirectEnd => _timingInfo.RedirectEndTime;

    /// <summary>Gets the time the fetch of the resource (after the redirects) started.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-fetchstart"/>
    [IdlMember("fetchStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-fetchstart")]
    public double FetchStart => _timingInfo.PostRedirectStartTime;

    /// <summary>Gets the time the domain name lookup started, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-domainlookupstart"/>
    [IdlMember("domainLookupStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-domainlookupstart")]
    public double DomainLookupStart => _timingInfo.FinalConnectionTimingInfo?.DomainLookupStartTime ?? 0;

    /// <summary>Gets the time the domain name lookup completed, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-domainlookupend"/>
    [IdlMember("domainLookupEnd", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-domainlookupend")]
    public double DomainLookupEnd => _timingInfo.FinalConnectionTimingInfo?.DomainLookupEndTime ?? 0;

    /// <summary>Gets the time the connection started, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-connectstart"/>
    [IdlMember("connectStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-connectstart")]
    public double ConnectStart => _timingInfo.FinalConnectionTimingInfo?.ConnectionStartTime ?? 0;

    /// <summary>Gets the time the connection was established, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-connectend"/>
    [IdlMember("connectEnd", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-connectend")]
    public double ConnectEnd => _timingInfo.FinalConnectionTimingInfo?.ConnectionEndTime ?? 0;

    /// <summary>Gets the time the TLS handshake started, or 0 for a connection that is not secure.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-secureconnectionstart"/>
    [IdlMember("secureConnectionStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-secureconnectionstart")]
    public double SecureConnectionStart => _timingInfo.FinalConnectionTimingInfo?.SecureConnectionStartTime ?? 0;

    /// <summary>Gets the time the request was sent, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-requeststart"/>
    [IdlMember("requestStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-requeststart")]
    public double RequestStart => _timingInfo.FinalNetworkRequestStartTime;

    /// <summary>Gets the time the headers of the final response were received, or 0.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-finalresponseheadersstart"/>
    [IdlMember("finalResponseHeadersStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-finalresponseheadersstart")]
    public double FinalResponseHeadersStart => _timingInfo.FinalNetworkResponseStartTime;

    /// <summary>Gets the time the first interim response (1xx) was received: 0, as the interim responses are not exposed by the loader.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-firstinterimresponsestart"/>
    [IdlMember("firstInterimResponseStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-firstinterimresponsestart")]
    public double FirstInterimResponseStart => 0;

    /// <summary>Gets the time the response started: <see cref="FirstInterimResponseStart"/> if it is not 0, otherwise <see cref="FinalResponseHeadersStart"/>.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-responsestart"/>
    [IdlMember("responseStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-responsestart")]
    public double ResponseStart => FirstInterimResponseStart != 0 ? FirstInterimResponseStart : FinalResponseHeadersStart;

    /// <summary>Gets the time the response was received.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-responseend"/>
    [IdlMember("responseEnd", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-responseend")]
    public double ResponseEnd => _timingInfo.EndTime;

    /// <summary>Gets the time the service worker started to evaluate its router rules: 0, as there is no service worker.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerrouterevaluationstart"/>
    [IdlMember("workerRouterEvaluationStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerrouterevaluationstart")]
    public double WorkerRouterEvaluationStart => 0;

    /// <summary>Gets the time the service worker started to look up its cache: 0, as there is no service worker.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workercachelookupstart"/>
    [IdlMember("workerCacheLookupStart", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workercachelookupstart")]
    public double WorkerCacheLookupStart => 0;

    /// <summary>Gets the source of the matched router rule of the service worker: the empty string, as there is no service worker.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workermatchedroutersource"/>
    [IdlMember("workerMatchedRouterSource", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workermatchedroutersource")]
    public string WorkerMatchedRouterSource => "";

    /// <summary>Gets the final source of the router of the service worker: the empty string, as there is no service worker.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerfinalroutersource"/>
    [IdlMember("workerFinalRouterSource", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerfinalroutersource")]
    public string WorkerFinalRouterSource => "";

    /// <summary>Gets the size of the response: the encoded size of its body plus 300 (in place of the size of the headers), or 0 when the timing allow check failed.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-transfersize"/>
    [IdlMember("transferSize", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-transfersize")]
    public ulong TransferSize => _timingInfo.TimingAllowPassed ? EncodedBodySize + 300 : 0;

    /// <summary>Gets the size of the body of the response, before its content codings are removed, or 0 for a CORS-cross-origin response.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-encodedbodysize"/>
    [IdlMember("encodedBodySize", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-encodedbodysize")]
    public ulong EncodedBodySize => _bodyInfo.EncodedSize;

    /// <summary>Gets the size of the body of the response, or 0 for a CORS-cross-origin response.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-decodedbodysize"/>
    [IdlMember("decodedBodySize", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-decodedbodysize")]
    public ulong DecodedBodySize => _bodyInfo.DecodedSize;

    /// <summary>Gets the status of the response, or 0 for a network error or an opaque response.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-responsestatus"/>
    [IdlMember("responseStatus", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-responsestatus")]
    public ushort ResponseStatus { get; }

    /// <summary>
    /// Gets whether the resource blocked the rendering: <c>blocking</c> when the element that fetched it (a script, a style
    /// sheet link) was render-blocking when the fetch started, else <c>non-blocking</c>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-renderblockingstatus"/>
    [IdlMember("renderBlockingStatus", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-renderblockingstatus")]
    public string RenderBlockingStatus => IsRenderBlocking ? "blocking" : "non-blocking";

    /// <summary>https://fetch.spec.whatwg.org/#request-render-blocking of the request of the resource.</summary>
    internal bool IsRenderBlocking { get; init; }

    /// <summary>Gets the minimized MIME type of the response (e.g. <c>text/css</c>), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-contenttype"/>
    [IdlMember("contentType", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-contenttype")]
    public string ContentType => _bodyInfo.ContentType ?? "";

    /// <summary>
    /// Gets the metrics of the <c>Server-Timing</c> headers of the response (https://w3c.github.io/server-timing/#dom-performanceresourcetiming-servertiming):
    /// empty when the global object is not a secure context or when the timing allow check failed.
    /// </summary>
    /// <seealso href="https://w3c.github.io/server-timing/#dom-performanceresourcetiming-servertiming"/>
    [IdlMember("serverTiming", "https://w3c.github.io/server-timing/#dom-performanceresourcetiming-servertiming")]
    public IReadOnlyList<PerformanceServerTiming> ServerTiming { get; }

    /// <summary>
    /// Gets the content coding of the response (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-contentencoding):
    /// the value of its <c>Content-Encoding</c> header when it is a content coding supported by the browsers, <c>multiple</c>
    /// for several codings, <c>@unknown</c> for another value, or the empty string (no content coding, or a CORS-cross-origin response).
    /// </summary>
    /// <seealso href="https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-contentencoding"/>
    [IdlMember("contentEncoding", "https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-contentencoding")]
    public string ContentEncoding { get; }
}
