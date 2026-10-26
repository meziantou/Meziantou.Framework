using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The credentials mode of a request (https://fetch.spec.whatwg.org/#concept-request-credentials-mode).</summary>
internal enum FetchCredentialsMode
{
    SameOrigin,
    Omit,
    Include,
}

/// <summary>The redirect mode of a request (https://fetch.spec.whatwg.org/#concept-request-redirect-mode).</summary>
internal enum FetchRedirectMode
{
    Follow,
    Error,
    Manual,
}

/// <summary>The response tainting of a request (https://fetch.spec.whatwg.org/#concept-request-response-tainting).</summary>
internal enum FetchResponseTainting
{
    Basic,
    Cors,
    Opaque,
}

/// <summary>The type of a response (https://fetch.spec.whatwg.org/#concept-response-type).</summary>
internal enum FetchResponseType
{
    Default,
    Basic,
    Cors,
    Error,
    Opaque,
    OpaqueRedirect,
}

/// <summary>The service-workers mode of a request (https://fetch.spec.whatwg.org/#request-service-workers-mode).</summary>
internal enum ServiceWorkersMode
{
    /// <summary>The relevant service workers get a fetch event.</summary>
    All,

    /// <summary>No service worker gets a fetch event.</summary>
    None,
}

/// <summary>
/// The cookies of a request whose cookies are not the ones of its client (a navigation): the cookie store, the site for
/// cookies and the top-level origin given to the loader (see <see cref="DocumentRequest.CookieJar"/>).
/// </summary>
internal sealed record FetchCookieContext(CookieJar? CookieJar, string? SiteForCookies, string? TopLevelOrigin);

/// <summary>
/// The data of a navigation request that the <c>Request</c> object of its fetch event keeps (it is not exposed to the
/// scripts): when the service worker fetches the request again, the cookies are the ones of the navigation.
/// </summary>
internal sealed record FetchEventNavigationContext(FetchCookieContext? CookieContext, RequestDestination Destination);

/// <summary>
/// A request of the fetch algorithm (https://fetch.spec.whatwg.org/#concept-request), created by <c>fetch()</c>,
/// <see cref="XmlHttpRequest"/>, the subresources of the documents (see <see cref="SubresourceFetching"/>) and the
/// navigations. The body is a byte sequence (the bodies whose source is a stream are read before the request is fetched,
/// and have no source).
/// </summary>
/// <summary>
/// The times of a fetch (https://fetch.spec.whatwg.org/#fetch-timing-info), as timestamps of the time provider of the event
/// loop of the request (see <see cref="DomEventLoop.TimeProvider"/>), or 0 when the step did not happen. The loader does not
/// expose the timing of the connection nor the time of the first byte: the request is sent when the loader is called, and
/// the response is received when the loader completes.
/// </summary>
internal sealed class FetchTimestamps
{
    /// <summary>Whether the times are recorded (the request has an event loop).</summary>
    public bool IsRecorded { get; set; }

    public long Start { get; set; }

    public long RedirectStart { get; set; }

    public long RedirectEnd { get; set; }

    public long PostRedirectStart { get; set; }

    public long FinalNetworkRequestStart { get; set; }

    public long FinalNetworkResponseStart { get; set; }

    /// <summary>
    /// The time the request was given to a service worker (https://fetch.spec.whatwg.org/#fetch-timing-info-final-service-worker-start-time),
    /// before the worker is started (and activated), or 0.
    /// </summary>
    public long ServiceWorkerStart { get; set; }

    /// <summary>The time the fetch event of the request was queued on the event loop of the service worker, once it is running and activated, or 0.</summary>
    public long ServiceWorkerReady { get; set; }
}

internal sealed partial class FetchRequest
{
    public FetchRequest(UrlRecord url)
    {
        UrlList = [url];
    }

    public string Method { get; set; } = "GET";

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-url-list</summary>
    public List<UrlRecord> UrlList { get; set; }

    public UrlRecord Url => UrlList[0];

    public UrlRecord CurrentUrl => UrlList[^1];

    /// <summary>
    /// The URLs of the redirects of a navigation whose request is fetched again by a service worker (<c>fetch(event.request)</c>).
    /// The request of a fetch event has a single URL, the current URL of the request of the client (as in the browsers: the
    /// specification gives it the whole URL list, so its <c>url</c> would be the URL before the redirects, and the response of
    /// its fetch would be a redirected response, which a navigation rejects). The previous URLs are kept here: they are part
    /// of the redirect chain that taints the origin of the request (the <c>Origin</c> and <c>Sec-Fetch-Site</c> headers, the
    /// SameSite cookies).
    /// </summary>
    public List<UrlRecord> NavigationRedirectUrls { get; set; } = [];

    /// <summary>The URL list of the request, after the URLs of <see cref="NavigationRedirectUrls"/>.</summary>
    public IReadOnlyList<UrlRecord> RedirectChain => NavigationRedirectUrls.Count == 0 ? UrlList : [.. NavigationRedirectUrls, .. UrlList];

    /// <summary>
    /// The destination of the navigation whose request is fetched again by a service worker, or null: the destination of
    /// the request of the service worker is the empty string, but its cookies are the ones of the navigation (a top-level
    /// navigation sends the Lax cookies, see <see cref="CookieContexts.ForRequest"/>).
    /// </summary>
    public RequestDestination? NavigationDestination { get; set; }

    public List<KeyValuePair<string, string>> HeaderList { get; set; } = [];

    /// <summary>Whether the headers were set by the author (https://fetch.spec.whatwg.org/#unsafe-request-flag).</summary>
    public bool UnsafeRequest { get; set; }

    public byte[]? Body { get; set; }

    /// <summary>Whether the body has a source (https://fetch.spec.whatwg.org/#concept-body-source): false for a stream.</summary>
    public bool BodyHasSource { get; set; } = true;

    /// <summary>The document of the environment settings object of the request (https://fetch.spec.whatwg.org/#concept-request-client).</summary>
    public Document? Client { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-origin: the serialized origin of the client, or <c>null</c>.</summary>
    public string Origin { get; set; } = "null";

    /// <summary>The referrer: "client", "no-referrer" or a URL (https://fetch.spec.whatwg.org/#concept-request-referrer).</summary>
    public string Referrer { get; set; } = "client";

    /// <summary>The referrer policy, or the empty string (https://fetch.spec.whatwg.org/#concept-request-referrer-policy).</summary>
    public string ReferrerPolicy { get; set; } = "";

    public RequestMode Mode { get; set; } = RequestMode.NoCors;

    public bool UseCorsPreflight { get; set; }

    public FetchCredentialsMode CredentialsMode { get; set; }

    public bool UseUrlCredentials { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-cache-mode</summary>
    public string CacheMode { get; set; } = "default";

    /// <summary>https://fetch.spec.whatwg.org/#request-service-workers-mode</summary>
    public ServiceWorkersMode ServiceWorkersMode { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-reserved-client: the environment created by the response of a navigation or of a worker script, or null.</summary>
    public ReservedClient? ReservedClient { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-replaces-client-id</summary>
    public string? ReplacesClientId { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-reload-navigation-flag</summary>
    public bool IsReloadNavigation { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-history-navigation-flag</summary>
    public bool IsHistoryNavigation { get; set; }

    public FetchRedirectMode RedirectMode { get; set; }

    public RequestDestination Destination { get; set; } = RequestDestination.Empty;

    /// <summary>
    /// The blob URL entry of the URL of the request (https://url.spec.whatwg.org/#concept-url-blob-entry), resolved when the
    /// URL is parsed (e.g. by <c>XMLHttpRequest.open()</c>), so that the blob is fetched even if the URL is revoked
    /// afterwards; null when it is resolved by the fetch.
    /// </summary>
    public FileApi.BlobUrlEntry? BlobUrlEntry { get; set; }

    /// <summary>The integrity metadata (https://fetch.spec.whatwg.org/#concept-request-integrity-metadata), or the empty string.</summary>
    public string Integrity { get; set; } = "";

    public FetchResponseTainting ResponseTainting { get; set; }

    public int RedirectCount { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-tainted-origin</summary>
    public bool TaintedOrigin { get; set; }

    /// <summary>Whether the request blocks the event loop (a synchronous <c>XMLHttpRequest</c>).</summary>
    public bool Synchronous { get; set; }

    /// <summary>Whether the request may outlive its client (https://fetch.spec.whatwg.org/#request-keepalive-flag).</summary>
    public bool Keepalive { get; set; }

    /// <summary>
    /// The browsing context whose loader sends a keepalive request, set when the fetch starts: the request is still sent
    /// when the browsing context of its client is discarded (e.g. a removed iframe).
    /// </summary>
    public BrowsingContext? KeepaliveBrowsingContext { get; set; }

    /// <summary>
    /// The navigable of a navigation request: its loader sends the request, and its event loop runs the fetch (the
    /// request has no client when the navigation is not initiated by a document, e.g. <see cref="BrowsingContext.Navigate(string)"/>).
    /// </summary>
    public BrowsingContext? Navigable { get; set; }

    /// <summary>The cookies of the request, when they are not the ones of the client (a navigation), or null.</summary>
    public FetchCookieContext? CookieContext { get; set; }

    /// <summary>
    /// The element that initiated the request (e.g. the <c>script</c> element), or null: its nonce is the cryptographic nonce
    /// metadata (https://fetch.spec.whatwg.org/#concept-request-nonce-metadata) checked by the content security policies.
    /// </summary>
    public Element? Initiator { get; set; }

    /// <summary>The cryptographic nonce metadata, or null for the nonce of <see cref="Initiator"/> (e.g. the fetch options of a module script).</summary>
    public string? Nonce { get; set; }

    /// <summary>Whether the parser metadata is "parser-inserted" (https://fetch.spec.whatwg.org/#concept-request-parser-metadata).</summary>
    public bool ParserInserted { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#request-render-blocking: the element that initiated the request was render-blocking when the request was created.</summary>
    public bool RenderBlocking { get; set; }

    /// <summary>
    /// Whether the request is a speculative fetch of the HTML parser (see <see cref="DocumentRequest.IsSpeculative"/>): its
    /// content security policies are the ones of the copy of the document, checked by the speculative parser.
    /// </summary>
    public bool IsSpeculative { get; set; }

    /// <summary>
    /// Whether the initiator type is "imageset" (an image of an element that uses <c>srcset</c> or <c>picture</c>): its
    /// <c>http:</c> URLs are not upgraded (https://w3c.github.io/webappsec-mixed-content/#upgrade-algorithm).
    /// </summary>
    public bool ImageSetInitiator { get; set; }

    /// <summary>
    /// The environment whose CSP list applies to the request and receives its violations when it is not the client: the
    /// environment that created a module worker, for the static imports of the worker, which are fetched with the outside
    /// settings (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-worker-script-tree). The library keeps
    /// the worker as the client of these requests (its event loop, its loader and its service worker), or null.
    /// </summary>
    public Document? PolicyClient { get; set; }

    /// <summary>
    /// Whether the navigation is a form submission: the "form-submission" navigation type of the content security policies
    /// (https://w3c.github.io/webappsec-csp/#should-block-navigation-request), and its <c>http:</c> URL is upgraded
    /// (https://w3c.github.io/webappsec-upgrade-insecure-requests/#upgrade-request).
    /// </summary>
    public bool IsFormSubmission { get; set; }

    /// <summary>Whether the navigation is initiated with a user activation (https://fetch.spec.whatwg.org/#request-user-activation).</summary>
    public bool UserActivation { get; set; }

    /// <summary>
    /// The timing info of the fetch (https://fetch.spec.whatwg.org/#fetch-timing-info), recorded by the fetch algorithm: it
    /// is exposed by the resource timing entry of the request when the timing allow check passes.
    /// </summary>
    public FetchTimestamps Timing { get; private set; } = new();

    /// <summary>
    /// The timing allow failed flag (https://fetch.spec.whatwg.org/#timing-allow-failed): a response of the fetch (the
    /// redirects included) failed the timing allow check, so the timing of the fetch is not exposed.
    /// </summary>
    public bool TimingAllowFailed { get; set; }

    /// <summary>Whether the request was sent to the loader (once at least): a request blocked before (e.g. by a content security policy) has no resource timing.</summary>
    public bool IsSentToLoader { get; set; }

    /// <summary>Whether the last response of the request was given by a service worker (it was not received from the network).</summary>
    public bool IsAnsweredByServiceWorker { get; set; }

    /// <summary>
    /// The preloaded response candidate of the fetch (https://fetch.spec.whatwg.org/#fetch-params-preloaded-response-candidate):
    /// the response of the preload that the request consumed (see <see cref="Document.TryConsumePreloadedResource"/>), which
    /// completes when the preload does, or null. It is the response of main fetch, instead of a fetch of the URL.
    /// </summary>
    public Task<FetchResponse>? PreloadedResponseCandidate { get; set; }

    /// <summary>
    /// Whether the response of the fetch is a preloaded response: the request was not sent, and the preload reported the
    /// timing of the resource, so the fetch does not report it again.
    /// </summary>
    public bool ConsumedPreload { get; set; }

    /// <summary>
    /// Whether the initiator of the request is "prefetch" (https://fetch.spec.whatwg.org/#concept-request-initiator): the
    /// request has the <c>Sec-Purpose: prefetch</c> header.
    /// </summary>
    public bool IsPrefetch { get; set; }

    /// <summary>
    /// The value of the <c>Sec-Purpose</c> header of a prefetch (<see cref="IsPrefetch"/>): <c>prefetch</c> by default,
    /// <c>prefetch;anonymous-client-ip</c> for a navigational prefetch that hides the IP address of the client
    /// (https://wicg.github.io/nav-speculation/prefetch.html), or <c>prefetch;prerender</c> for the requests of a prerendering
    /// navigable (https://wicg.github.io/nav-speculation/prerendering.html#interaction-with-fetch).
    /// </summary>
    public string? SecPurpose { get; set; }

    /// <summary>
    /// Whether the request uses the isolated partition of a cross-site navigational prefetch: the cookies are the ones of
    /// <see cref="CookieContext"/>, and the authentication entries of the user agent are not used.
    /// </summary>
    public bool UseIsolatedCredentials { get; set; }

    /// <summary>
    /// Whether the body of the response is interpreted as its bytes arrive (an event source, the resource of a media element
    /// or of a track element, the <c>ReadableStream</c> of the body of <c>fetch()</c>): the response is returned when its
    /// headers are received, with the rest of its body (<see cref="DocumentResource.RemainingContentBytes"/>, see
    /// <see cref="FetchBodyReader"/>). The navigations do it too; the other requests get the complete body.
    /// </summary>
    public bool StreamsBody { get; set; }

    /// <summary>
    /// Whether the loader returned no response for the last URL of the request (the server cannot be reached): a failure of the
    /// network, as opposed to a request or a response that the fetch algorithm blocked.
    /// </summary>
    public bool LoaderFailed { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-clone (the body is immutable, so it is shared).</summary>
    public FetchRequest Clone()
    {
        var clone = (FetchRequest)MemberwiseClone();
        clone.UrlList = [.. UrlList];
        clone.NavigationRedirectUrls = [.. NavigationRedirectUrls];
        clone.HeaderList = [.. HeaderList];
        clone.Timing = new FetchTimestamps();
        clone.PreloadedResponseCandidate = null;
        clone.ConsumedPreload = false;
        return clone;
    }
}

/// <summary>A response of the fetch algorithm (https://fetch.spec.whatwg.org/#concept-response).</summary>
internal sealed class FetchResponse
{
    public FetchResponseType Type { get; set; } = FetchResponseType.Default;

    public bool Aborted { get; set; }

    public List<UrlRecord> UrlList { get; set; } = [];

    public int Status { get; set; } = 200;

    public string StatusMessage { get; set; } = "";

    public List<KeyValuePair<string, string>> HeaderList { get; set; } = [];

    /// <summary>
    /// The body of the response. The body of a response of the loader is its resource (<see cref="Resource"/>), copied as
    /// bytes when it is read.
    /// </summary>
    public byte[]? Body
    {
        get
        {
            if (_bodyResource is { } resource)
            {
                _body = resource.ContentBytes is { } bytes ? bytes.ToArray() : System.Text.Encoding.UTF8.GetBytes(resource.Content!);
                _bodyResource = null;
            }

            return _body;
        }
        set
        {
            _body = value;
            _bodyResource = null;
        }
    }

    private byte[]? _body;
    private DocumentResource? _bodyResource;

    /// <summary>The length of the body, without reading the body of a resource of the loader.</summary>
    public int BodyLength => _bodyResource is { } resource ? resource.ContentBytes?.Length ?? System.Text.Encoding.UTF8.GetByteCount(resource.Content!) : _body?.Length ?? 0;

    /// <summary>
    /// The resource returned by the loader, whose content is the body while <see cref="Body"/> is not changed (e.g. its decoded
    /// content, or the rest of a body that arrives over time), or null.
    /// </summary>
    public DocumentResource? Resource => _bodyResource;

    /// <summary>
    /// The connection of the response to the opening handshake of a WebSocket (https://fetch.spec.whatwg.org/#concept-websocket-connection),
    /// whose status is 101, or null. It is a property of the internal response.
    /// </summary>
    public Stream? Connection { get; set; }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-response-cache-state: <c>local</c> for a stored response of the HTTP cache,
    /// <c>validated</c> for a stored response validated by a <c>304</c> response, else the empty string.
    /// </summary>
    public string CacheState { get; set; } = "";

    /// <summary>https://fetch.spec.whatwg.org/#response-request-includes-credentials (a property of the internal response)</summary>
    public bool RequestIncludesCredentials { get; set; } = true;

    /// <summary>https://fetch.spec.whatwg.org/#concept-response-cors-exposed-header-name-list</summary>
    public List<string> CorsExposedHeaderNames { get; set; } = [];

    /// <summary>The internal response of a filtered response (https://fetch.spec.whatwg.org/#concept-internal-response), or this response.</summary>
    public FetchResponse Internal { get; private set; } = null!;

    public bool IsNetworkError => Type == FetchResponseType.Error;

    public UrlRecord? Url => UrlList.Count == 0 ? null : UrlList[^1];

    /// <summary>The location URL of a redirect response (https://fetch.spec.whatwg.org/#concept-response-location-url): null when there is no Location header.</summary>
    public UrlRecord? GetLocationUrl(string? requestFragment, out bool failure)
    {
        failure = false;
        if (!HeaderList.Exists(header => header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase)) || Status is not (301 or 302 or 303 or 307 or 308))
            return null;

        // The bytes of the header are decoded as UTF-8; the bytes of an invalid UTF-8 sequence are percent-encoded
        var location = HeaderList.Find(header => header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase)).Value;
        var locationUrl = UrlParser.Parse(DecodeLocation(FetchHeaders.IsomorphicEncode(location)), Url);
        if (locationUrl is null)
        {
            failure = true;
            return null;
        }

        if (locationUrl.Fragment is null)
        {
            locationUrl.Fragment = requestFragment;
        }

        return locationUrl;
    }

    private static string DecodeLocation(byte[] bytes)
    {
        try
        {
            return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            var result = new System.Text.StringBuilder();
            foreach (var b in bytes)
            {
                if (b < 0x80)
                {
                    result.Append((char)b);
                }
                else
                {
                    result.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            return result.ToString();
        }
    }

    public static FetchResponse NetworkError(bool aborted = false)
    {
        var response = new FetchResponse { Type = FetchResponseType.Error, Status = 0, Aborted = aborted };
        response.Internal = response;
        return response;
    }

    public static FetchResponse Create(int status, string statusMessage, List<KeyValuePair<string, string>> headers, byte[]? body)
    {
        var response = new FetchResponse { Status = status, StatusMessage = statusMessage, HeaderList = headers, Body = body };
        response.Internal = response;
        return response;
    }

    /// <summary>Creates a response whose body is the content of a resource of the loader (see <see cref="Resource"/>).</summary>
    public static FetchResponse Create(int status, string statusMessage, List<KeyValuePair<string, string>> headers, DocumentResource resource)
    {
        var response = new FetchResponse { Status = status, StatusMessage = statusMessage, HeaderList = headers, _bodyResource = resource };
        response.Internal = response;
        return response;
    }

    /// <summary>
    /// Returns the response as a <see cref="DocumentResource"/> for the subresources and the navigations, or null for a network
    /// error: the status, the headers, the URLs and the body of the internal response (e.g. the image of an opaque response is
    /// decoded), and its type (<see cref="DocumentResource.ResponseType"/>), which tells whether it is CORS-same-origin.
    /// </summary>
    public DocumentResource? ToDocumentResource()
    {
        if (IsNetworkError)
            return null;

        var internalResponse = Internal;
        var urlList = internalResponse.UrlList;
        var url = urlList.Count == 0 ? null : urlList[^1].Serialize();
        var redirectUrls = urlList.Count <= 2 ? [] : urlList.Skip(1).Take(urlList.Count - 2).Select(item => item.Serialize()).ToArray();
        var headers = internalResponse.HeaderList;
        var statusText = internalResponse.StatusMessage;
        if (internalResponse._bodyResource is { } resource)
        {
            return resource.Content is { } content
                ? new DocumentResource(content) { StatusCode = internalResponse.Status, StatusText = statusText, Headers = headers, Url = url, RedirectUrls = redirectUrls, ResponseType = Type }
                : new DocumentResource(resource.ContentBytes!.Value) { StatusCode = internalResponse.Status, StatusText = statusText, Headers = headers, Url = url, RedirectUrls = redirectUrls, RemainingContentBytes = resource.RemainingContentBytes, ResponseType = Type };
        }

        return new DocumentResource(internalResponse._body ?? []) { StatusCode = internalResponse.Status, StatusText = statusText, Headers = headers, Url = url, RedirectUrls = redirectUrls, ResponseType = Type };
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-filtered-response-basic</summary>
    public FetchResponse ToBasic() => Filter(FetchResponseType.Basic, HeaderList.Exists(static header => FetchHeaders.IsForbiddenResponseHeaderName(header.Key))
        ? HeaderList.Where(header => !FetchHeaders.IsForbiddenResponseHeaderName(header.Key)).ToList()
        : HeaderList);

    /// <summary>https://fetch.spec.whatwg.org/#concept-filtered-response-cors</summary>
    public FetchResponse ToCors() => Filter(FetchResponseType.Cors, HeaderList.Where(header => FetchHeaders.IsCorsSafelistedResponseHeaderName(header.Key, CorsExposedHeaderNames)).ToList());

    /// <summary>https://fetch.spec.whatwg.org/#concept-filtered-response-opaque</summary>
    public FetchResponse ToOpaque()
    {
        var filtered = Filter(FetchResponseType.Opaque, []);
        filtered.UrlList = [];
        filtered.Status = 0;
        filtered.StatusMessage = "";
        filtered.Body = null;
        return filtered;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-filtered-response-opaque-redirect</summary>
    public FetchResponse ToOpaqueRedirect()
    {
        var filtered = Filter(FetchResponseType.OpaqueRedirect, []);
        filtered.Status = 0;
        filtered.StatusMessage = "";
        filtered.Body = null;
        return filtered;
    }

    private FetchResponse Filter(FetchResponseType type, List<KeyValuePair<string, string>> headers) => new()
    {
        Type = type,
        Aborted = Aborted,
        UrlList = UrlList,
        Status = Status,
        StatusMessage = StatusMessage,
        HeaderList = headers,
        _body = _body,
        _bodyResource = _bodyResource,
        CorsExposedHeaderNames = CorsExposedHeaderNames,
        Internal = Internal,
    };
}
