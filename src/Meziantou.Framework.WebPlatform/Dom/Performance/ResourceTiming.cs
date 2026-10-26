using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The timing of a fetch (https://fetch.spec.whatwg.org/#fetch-timing-info). The times are relative to the time origin of
/// the global object that started the fetch (0 when the time is not exposed).
/// </summary>
internal sealed class FetchTimingInfo
{
    public double StartTime { get; init; }

    public double RedirectStartTime { get; init; }

    public double RedirectEndTime { get; init; }

    public double PostRedirectStartTime { get; init; }

    public double FinalNetworkRequestStartTime { get; init; }

    public double FinalNetworkResponseStartTime { get; init; }

    /// <summary>https://fetch.spec.whatwg.org/#fetch-timing-info-final-service-worker-start-time, or 0 when no fetch event was dispatched for the request.</summary>
    public double FinalServiceWorkerStartTime { get; init; }

    /// <summary>https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerrouterevaluationstart, or 0 when the service worker has no router rule.</summary>
    public double WorkerRouterEvaluationStartTime { get; init; }

    /// <summary>https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workercachelookupstart, or 0 when the request was not routed to the cache.</summary>
    public double WorkerCacheLookupStartTime { get; init; }

    /// <summary>https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workermatchedroutersource</summary>
    public string WorkerMatchedRouterSource { get; init; } = "";

    /// <summary>https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workerfinalroutersource</summary>
    public string WorkerFinalRouterSource { get; init; } = "";

    public double EndTime { get; set; }

    /// <summary>The timing of the connection of the final response (https://fetch.spec.whatwg.org/#connection-timing-info), or null when it is not exposed.</summary>
    public ConnectionTimingInfo? FinalConnectionTimingInfo { get; init; }

    /// <summary>Whether the timing allow check passed (https://fetch.spec.whatwg.org/#concept-response-timing-allow-passed).</summary>
    public bool TimingAllowPassed { get; init; }
}

/// <summary>https://fetch.spec.whatwg.org/#connection-timing-info</summary>
internal sealed record ConnectionTimingInfo(double DomainLookupStartTime, double DomainLookupEndTime, double ConnectionStartTime, double ConnectionEndTime, double SecureConnectionStartTime, string AlpnNegotiatedProtocol)
{
    /// <summary>Whether the connection is secure (its secure connection start time may be 0: the start of the navigation).</summary>
    public bool IsSecure { get; init; }
}

/// <summary>https://fetch.spec.whatwg.org/#response-body-info</summary>
internal readonly record struct ResponseBodyInfo(ulong EncodedSize, ulong DecodedSize, string ContentType);

/// <summary>
/// Reports the timing of a fetch started by a global object to its performance timeline when the fetch completes
/// (https://fetch.spec.whatwg.org/#finalize-and-report-timing, https://w3c.github.io/resource-timing/#dfn-mark-resource-timing).
/// The times are recorded by the fetch algorithm (<see cref="FetchRequest.Timing"/>). The loader does not expose the timing
/// of the connection: the connection is established when the request is sent to the loader, and the response is received
/// when the loader completes.
/// </summary>
internal sealed class ResourceTiming
{
    private readonly GlobalScope _global;
    private readonly FetchRequest _request;
    private readonly string _requestedUrl;
    private readonly string _initiatorType;
    private readonly long _startTimestamp;
    private bool _reported;

    private ResourceTiming(GlobalScope global, FetchRequest request, string initiatorType)
    {
        _global = global;
        _request = request;
        _requestedUrl = request.Url.Serialize();
        _initiatorType = initiatorType;
        _startTimestamp = global.EventLoop.TimeProvider.GetTimestamp();
    }

    /// <summary>
    /// Starts to record the timing of a fetch of a document (the relevant global object is the window of the document). Returns
    /// null when the fetch is not reported: the document has no window, the request is speculative, or the URL is not an
    /// HTTP(S) URL (https://fetch.spec.whatwg.org/#finalize-and-report-timing, step 2).
    /// </summary>
    public static ResourceTiming? Start(Document document, FetchRequest request, string initiatorType)
    {
        if (request.IsSpeculative || document.DefaultView is not { } window || !ReferenceEquals(window.AssociatedDocument, document))
            return null;

        return Start(window, request, initiatorType);
    }

    /// <summary>Starts to record the timing of a fetch of a global object. Returns null when the URL is not an HTTP(S) URL.</summary>
    public static ResourceTiming? Start(GlobalScope global, FetchRequest request, string initiatorType)
    {
        if (request.Url.Scheme is not ("http" or "https"))
            return null;

        return new ResourceTiming(global, request, initiatorType);
    }

    /// <summary>
    /// Reports the timing of the fetch, once, when its response is received (a network error, or the response of the fetch
    /// algorithm). It must be called from a task of the event loop of the global object.
    /// </summary>
    public void Report(FetchResponse response) => Report(response, _global.EventLoop.TimeProvider.GetTimestamp());

    /// <summary>
    /// Reports the timing of a fetch whose response is processed synchronously (the loader completed synchronously): the entry
    /// is queued by a task of the global object, as the response of a network is, so that a script that starts the fetch and
    /// then observes the performance timeline sees it. The end time is the current time.
    /// </summary>
    public void ReportFromTask(FetchResponse response)
    {
        var endTimestamp = _global.EventLoop.TimeProvider.GetTimestamp();
        _global.QueueGlobalTask(() => Report(response, endTimestamp));
    }

    private void Report(FetchResponse response, long endTimestamp)
    {
        // The timing of a preloaded response was reported by its preload
        if (_reported || _request.ConsumedPreload)
            return;

        _reported = true;
        var timing = _request.Timing;
        var startTime = ToRelativeTime(timing.IsRecorded ? timing.Start : _startTimestamp);
        var endTime = Math.Max(startTime, ToRelativeTime(endTimestamp));

        // https://fetch.spec.whatwg.org/#concept-response-timing-allow-passed: a network error, or a fetch one of whose
        // responses (the redirects included) failed the timing allow check, has an opaque timing info
        // (https://fetch.spec.whatwg.org/#create-an-opaque-timing-info)
        var internalResponse = response.Internal ?? response;
        var timingAllowPassed = !response.IsNetworkError && !_request.TimingAllowFailed;

        // The fetch event of the request is dispatched to the service worker before the request is sent to the network (or
        // answered by the worker): the times of the network are not earlier than the time the worker started
        var workerStartTime = timing.ServiceWorkerStart == 0 ? 0 : Math.Min(endTime, Math.Max(startTime, ToRelativeTime(timing.ServiceWorkerStart)));
        var workerReadyTime = timing.ServiceWorkerReady == 0 ? workerStartTime : Math.Min(endTime, Math.Max(workerStartTime, ToRelativeTime(timing.ServiceWorkerReady)));
        FetchTimingInfo timingInfo;
        if (timingAllowPassed)
        {
            var postRedirectStartTime = Math.Max(workerReadyTime, ToRelativeTime(timing.PostRedirectStart == 0 ? _startTimestamp : timing.PostRedirectStart));
            var requestStartTime = timing.FinalNetworkRequestStart == 0 ? postRedirectStartTime : ToRelativeTime(timing.FinalNetworkRequestStart);
            var isSecure = internalResponse.Url is { Scheme: "https" } || (internalResponse.Url is null && _request.CurrentUrl.Scheme == "https");
            timingInfo = new FetchTimingInfo
            {
                StartTime = startTime,
                RedirectStartTime = timing.RedirectStart == 0 ? 0 : Math.Max(workerStartTime, ToRelativeTime(timing.RedirectStart)),
                RedirectEndTime = ToRelativeTime(timing.RedirectEnd),
                PostRedirectStartTime = postRedirectStartTime,
                FinalNetworkRequestStartTime = requestStartTime,
                FinalNetworkResponseStartTime = timing.FinalNetworkResponseStart == 0 ? endTime : Math.Min(endTime, ToRelativeTime(timing.FinalNetworkResponseStart)),
                FinalServiceWorkerStartTime = workerStartTime,
                WorkerRouterEvaluationStartTime = timing.ServiceWorkerRouterEvaluationStart == 0 ? 0 : Math.Min(endTime, Math.Max(startTime, ToRelativeTime(timing.ServiceWorkerRouterEvaluationStart))),
                WorkerCacheLookupStartTime = timing.ServiceWorkerCacheLookupStart == 0 ? 0 : Math.Min(endTime, Math.Max(startTime, ToRelativeTime(timing.ServiceWorkerCacheLookupStart))),
                WorkerMatchedRouterSource = timing.ServiceWorkerMatchedRouterSource,
                WorkerFinalRouterSource = timing.ServiceWorkerFinalRouterSource,
                EndTime = endTime,

                // The connection is reused: its times are the time the fetch started (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-connectstart).
                // A response given by a service worker has no connection: its ALPN negotiated protocol is the empty string.
                FinalConnectionTimingInfo = new ConnectionTimingInfo(postRedirectStartTime, postRedirectStartTime, postRedirectStartTime, postRedirectStartTime, isSecure ? postRedirectStartTime : 0, _request.IsAnsweredByServiceWorker ? "" : "http/1.1") { IsSecure = isSecure },
                TimingAllowPassed = true,
            };
        }
        else
        {
            // https://fetch.spec.whatwg.org/#create-an-opaque-timing-info: the time the service worker started is kept
            timingInfo = new FetchTimingInfo { StartTime = startTime, PostRedirectStartTime = Math.Max(startTime, workerStartTime), FinalServiceWorkerStartTime = workerStartTime, EndTime = endTime };
        }

        // The body info and the status of the filtered response: an opaque filtered response (a cross-origin no-cors
        // response) and a network error have no status and no body (https://fetch.spec.whatwg.org/#concept-filtered-response-opaque)
        var isOpaque = response.IsNetworkError || response.Type is FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect;
        var bodyInfo = default(ResponseBodyInfo);
        var contentEncoding = "";
        if (!isOpaque)
        {
            var decodedBodySize = (ulong)internalResponse.BodyLength;
            bodyInfo = new ResponseBodyInfo(GetEncodedBodySize(internalResponse.HeaderList, decodedBodySize), decodedBodySize, MinimizeSupportedMimeType(HeaderList.ExtractMimeType(internalResponse.HeaderList)));
            contentEncoding = GetContentEncoding(internalResponse.HeaderList);
        }

        // https://fetch.spec.whatwg.org/#fetch-finale: the status and the content type of the response of a navigation (an
        // iframe, an object...) are only exposed to its container when no URL of the redirect chain is cross-origin
        var status = isOpaque ? 0 : internalResponse.Status;
        if (_request.Mode == RequestMode.Navigate && !_request.UrlList.TrueForAll(url => url.SerializeOrigin() == _request.Origin))
        {
            status = 0;
            bodyInfo = bodyInfo with { ContentType = "" };
        }

        // https://fetch.spec.whatwg.org/#fetch-finale: the Server-Timing headers of a response fetched by a secure context,
        // which an opaque timing info and an opaque filtered response do not have
        var serverTiming = timingAllowPassed && !isOpaque && _global.IsSecureContext ? PerformanceServerTiming.Parse(internalResponse.HeaderList) : [];
        Mark(timingInfo, bodyInfo, status, contentEncoding, serverTiming);
    }

    private double ToRelativeTime(long timestamp) => timestamp == 0 ? 0 : Math.Max(0, _global.ToRelativeHighResolutionTime(timestamp));

    /// <summary>
    /// Marks the resource timing of a navigation of an <c>iframe</c> or <c>frame</c> element whose timing is not exposed to
    /// the document of the element (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#iframe-load-event-steps):
    /// the fallback timing info only has the time the element started the navigation and the current time.
    /// </summary>
    public static void MarkFallback(GlobalScope global, string url, string initiatorType, long startTimestamp)
    {
        var startTime = Math.Max(0, global.ToRelativeHighResolutionTime(startTimestamp));
        var timingInfo = new FetchTimingInfo { StartTime = startTime, PostRedirectStartTime = startTime, EndTime = Math.Max(startTime, global.GetCurrentHighResolutionTime()) };
        var entry = new PerformanceResourceTiming(global, url, initiatorType, timingInfo, default, responseStatus: 0);
        var performance = global.Performance;
        performance.QueueEntry(entry);
        performance.AddResourceTimingEntry(entry);
    }

    /// <summary>https://w3c.github.io/resource-timing/#dfn-mark-resource-timing</summary>
    private void Mark(FetchTimingInfo timingInfo, ResponseBodyInfo bodyInfo, int responseStatus, string contentEncoding, IReadOnlyList<PerformanceServerTiming> serverTiming)
    {
        var entry = new PerformanceResourceTiming(_global, _requestedUrl, _initiatorType, timingInfo, bodyInfo, responseStatus, contentEncoding, serverTiming) { IsRenderBlocking = _request.RenderBlocking };
        var performance = _global.Performance;
        performance.QueueEntry(entry);
        performance.AddResourceTimingEntry(entry);
    }

    /// <summary>
    /// The content encoding of a response (https://fetch.spec.whatwg.org/#response-body-info, "content encoding"): the value of
    /// its <c>Content-Encoding</c> header when it is a content coding supported by the browsers, <c>multiple</c> for several
    /// codings, <c>@unknown</c> for another value, or the empty string.
    /// </summary>
    internal static string GetContentEncoding(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (HeaderList.GetDecodeAndSplit(headers, "Content-Encoding") is not { Count: > 0 } codings)
            return "";

        if (codings.Count > 1)
            return "multiple";

        // The content codings supported by the browsers (the registry also has "identity", which is not a content coding of a response)
        var coding = InfraAscii.ToAsciiLowercase(codings[0]);
        return coding is "" or "gzip" or "x-gzip" or "deflate" or "br" or "zstd" or "dcb" or "dcz" ? coding : "@unknown";
    }

    /// <summary>
    /// The encoded size of the body of a response: the loader gives the decoded body, so the encoded size of a response with
    /// a content coding is the value of its <c>Content-Length</c> header (the decoded size when there is none).
    /// </summary>
    internal static ulong GetEncodedBodySize(IReadOnlyList<KeyValuePair<string, string>> headers, ulong decodedBodySize)
    {
        if (HeaderList.Get(headers, "Content-Encoding") is { Length: > 0 } && HeaderList.Get(headers, "Content-Length") is { } contentLength
            && ulong.TryParse(contentLength, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var encodedBodySize))
        {
            return encodedBodySize;
        }

        return decodedBodySize;
    }

    /// <summary>
    /// https://mimesniff.spec.whatwg.org/#minimize-a-supported-mime-type: the JavaScript, JSON and XML MIME types are
    /// replaced by a generic one, and the MIME types that the library does not support are not exposed (the empty string).
    /// </summary>
    internal static string MinimizeSupportedMimeType(MimeType? mimeType)
    {
        if (mimeType is null)
            return "";

        if (mimeType.IsJavaScript)
            return "text/javascript";

        if (mimeType.Subtype.EndsWith("+json", StringComparison.Ordinal) || mimeType.Essence is "application/json" or "text/json")
            return "application/json";

        if (mimeType.Essence == "image/svg+xml")
            return "image/svg+xml";

        if (mimeType.IsXml)
            return "application/xml";

        return IsSupportedMimeType(mimeType.Essence) ? mimeType.Essence : "";
    }

    /// <summary>
    /// Whether a MIME type is supported by the user agent (https://mimesniff.spec.whatwg.org/#supported-by-the-user-agent): the
    /// documents, the style sheets, the images and the fonts that the library reads, and the audio and video types of the
    /// browsers (the media resources are fetched, although they are not decoded).
    /// </summary>
    private static bool IsSupportedMimeType(string essence) => essence
        is "text/html" or "text/plain" or "text/css" or "text/vtt" or "text/event-stream"
        or "image/png" or "image/x-png" or "image/apng" or "image/jpeg" or "image/jpg" or "image/pjpeg" or "image/gif" or "image/webp" or "image/avif" or "image/jxl" or "image/bmp"
        or "image/x-icon" or "image/vnd.microsoft.icon"
        or "font/ttf" or "font/otf" or "font/woff" or "font/woff2" or "font/collection" or "font/truetype" or "font/opentype" or "application/font-woff" or "application/font-woff2"
        or "application/x-font-ttf" or "application/x-font-opentype" or "application/vnd.ms-fontobject"
        or "audio/mpeg" or "audio/mp4" or "audio/ogg" or "audio/wav" or "audio/x-wav" or "audio/webm" or "audio/flac" or "audio/aac"
        or "video/mp4" or "video/ogg" or "video/webm";
}
