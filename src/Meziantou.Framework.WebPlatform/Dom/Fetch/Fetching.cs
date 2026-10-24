using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The fetch algorithm of the Fetch Standard (https://fetch.spec.whatwg.org/#fetching), used by the navigations, the
/// subresources of the documents (<see cref="SubresourceFetching"/>), the scripts of the workers, <c>fetch()</c>,
/// <see cref="XmlHttpRequest"/>, the beacons and the WebSockets: the scheme fetch of the <c>about:</c>, <c>blob:</c> and
/// <c>data:</c> URLs, the checks of each URL of a redirect chain (bad ports, mixed content, content security policies), the
/// CORS protocol (the CORS check, the CORS-preflight requests and their cache), the redirects, the filtered responses, the
/// MIME type and nosniff checks, the integrity metadata and the authentication entries. The HTTP requests are sent by the
/// <see cref="DocumentLoader"/> of the browsing context of the client, which does not follow the redirects
/// (<see cref="DocumentRequest.FollowRedirects"/> is false).
/// </summary>
/// <remarks>
/// The algorithm runs on the event loop: when the loader completes asynchronously, the rest of the algorithm runs in a
/// task queued when it completes. A synchronous request (<see cref="FetchRequest.Synchronous"/>) waits for the loader.
/// The requests whose service-workers mode is "all" are handled by the service workers (<see cref="ServiceWorkerFetchHandling"/>).
/// There is no HTTP cache, and the bodies are not streamed (the response is processed once its body is fully received,
/// except the rest of the body of a navigation, of an event source or of a media resource, see <see cref="DocumentResource.RemainingContentBytes"/>).
/// </remarks>
internal static class Fetching
{
    // https://fetch.spec.whatwg.org/#http-redirect-fetch, step 7
    private const int MaxRedirects = 20;

    // https://fetch.spec.whatwg.org/#bad-port
    private static readonly HashSet<int> BadPorts =
    [
        0, 1, 7, 9, 11, 13, 15, 17, 19, 20, 21, 22, 23, 25, 37, 42, 43, 53, 69, 77, 79, 87, 95, 101, 102, 103, 104, 109, 110, 111,
        113, 115, 117, 119, 123, 135, 137, 139, 143, 161, 179, 389, 427, 465, 512, 513, 514, 515, 526, 530, 531, 532, 540, 548,
        554, 556, 563, 587, 601, 636, 989, 990, 993, 995, 1719, 1720, 1723, 2049, 3659, 4045, 4190, 5060, 5061, 6000, 6566, 6665,
        6666, 6667, 6668, 6669, 6679, 6697, 10080,
    ];

    /// <summary>The CORS-preflight caches and the authentication entries, per browsing context group.</summary>
    private static readonly ConditionalWeakTable<BrowsingContext, FetchCaches> Caches = [];

    /// <summary>
    /// Fetches a request (https://fetch.spec.whatwg.org/#concept-fetch). The task completes with the response, which is a
    /// network error on failure (including when <paramref name="cancellationToken"/> is canceled: the aborted flag is set).
    /// </summary>
    public static Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        // https://fetch.spec.whatwg.org/#fetching, step 12: the Accept header
        if (!FetchHeaders.Contains(request.HeaderList, "Accept"))
        {
            request.HeaderList.Add(new("Accept", GetAcceptHeader(request)));
        }

        if (request.Keepalive)
        {
            request.KeepaliveBrowsingContext ??= request.Client?.BrowsingContext;
        }

        // https://fetch.spec.whatwg.org/#fetching, step 13: a subresource request of a window consumes the preloaded resource
        // of its preload key (https://html.spec.whatwg.org/multipage/links.html#consume-a-preloaded-resource)
        if (!request.IsSpeculative && request.Client is { WorkerGlobal: null } preloadClient && IsSubresourceRequest(request) && preloadClient.TryConsumePreloadedResource(request, out var preloadedResponse))
        {
            request.PreloadedResponseCandidate = preloadedResponse;
        }

        // https://fetch.spec.whatwg.org/#fetching, step 7: the timing info of the fetch
        if (GetTimestamp(request) is { } now)
        {
            var timing = request.Timing;
            timing.IsRecorded = true;
            timing.Start = timing.PostRedirectStart = now;
        }

        return MainFetchAsync(request, recursive: false, cancellationToken);
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#subresource-request: a request of a subresource of a document (the modes of the
    /// navigations and of the WebSockets are not the modes of a preload key).
    /// </summary>
    private static bool IsSubresourceRequest(FetchRequest request)
        => request.Mode is RequestMode.NoCors or RequestMode.Cors or RequestMode.SameOrigin
            && request.Destination is RequestDestination.Font or RequestDestination.Image or RequestDestination.Json or RequestDestination.Script or RequestDestination.Style or RequestDestination.Track or RequestDestination.Empty;

    /// <summary>The current time of the event loop of a request, or null without event loop.</summary>
    private static long? GetTimestamp(FetchRequest request)
        => (request.Navigable?.EventLoop ?? request.Client?.EnvironmentEventLoop ?? request.KeepaliveBrowsingContext?.EventLoop)?.TimeProvider.GetTimestamp();

    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-tao-check: the response has a <c>Timing-Allow-Origin</c> header with <c>*</c> or
    /// the origin of the request, or the response is not tainted.
    /// </summary>
    private static bool TimingAllowCheck(FetchRequest request, FetchResponse response)
    {
        if (request.TimingAllowFailed)
            return false;

        var values = HeaderList.GetDecodeAndSplit(response.HeaderList, "Timing-Allow-Origin") ?? [];
        if (values.Contains("*") || values.Contains(HasRedirectTaintedOrigin(request) ? "null" : request.Origin))
            return true;

        // The response tainting of a navigation is always "basic": as in the browsers, a navigation redirected through another
        // origin needs the header for the next responses too, like the no-cors subresources (whose tainting stays "opaque")
        if (request.Mode == RequestMode.Navigate && (!IsSameOrigin(request.CurrentUrl, request.Origin) || HasRedirectTaintedOrigin(request)))
            return false;

        return request.ResponseTainting == FetchResponseTainting.Basic;
    }

    /// <summary>
    /// Whether the redirect taint of a request is not "same-origin" (https://fetch.spec.whatwg.org/#concept-request-tainted-origin):
    /// a URL of the redirect chain was reached from another origin that is not the origin of the request.
    /// </summary>
    internal static bool HasRedirectTaintedOrigin(FetchRequest request)
    {
        UrlRecord? lastUrl = null;
        foreach (var url in request.RedirectChain)
        {
            if (lastUrl is not null && url.SerializeOrigin() != lastUrl.SerializeOrigin() && !IsSameOrigin(lastUrl, request.Origin))
                return true;

            lastUrl = url;
        }

        return false;
    }

    /// <summary>
    /// Fetches a request and calls <paramref name="processResponse"/> with the response from a task of the event loop: when
    /// the fetch completes synchronously (e.g. a <c>data:</c> URL, or a loader that completes synchronously), the callback is
    /// queued with <paramref name="queueTask"/>; otherwise it is called from the task in which the fetch completes.
    /// </summary>
    public static void Fetch(FetchRequest request, Action<FetchResponse> processResponse, Action<Action> queueTask, CancellationToken cancellationToken = default)
        => WhenCompleted(StartFetch(request, cancellationToken), processResponse, queueTask);

    /// <summary>
    /// Calls <paramref name="processResponse"/> with the response of a fetch: queued with <paramref name="queueTask"/> when
    /// the fetch is completed, otherwise from the task of the event loop in which it completes.
    /// </summary>
    public static void WhenCompleted(Task<FetchResponse> fetch, Action<FetchResponse> processResponse, Action<Action> queueTask)
    {
        if (fetch.IsCompleted)
        {
            queueTask(() => processResponse(GetResult(fetch)));
            return;
        }

        _ = fetch.ContinueWith(completed => processResponse(GetResult(completed)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Starts to fetch a request: an exception (a bug) is a network error.</summary>
    public static Task<FetchResponse> StartFetch(FetchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return FetchAsync(request, cancellationToken);
        }
#pragma warning disable CA1031 // A failure of the fetch is a network error
        catch (Exception)
#pragma warning restore CA1031
        {
            return Task.FromResult(FetchResponse.NetworkError());
        }
    }

    /// <summary>Returns the response of a completed fetch: a failure (a bug, or a cancellation) is a network error.</summary>
    public static FetchResponse GetResult(Task<FetchResponse> completedFetch) => completedFetch.IsCompletedSuccessfully ? completedFetch.Result : FetchResponse.NetworkError(aborted: completedFetch.IsCanceled);

    /// <summary>The value of the Accept header of a request (https://fetch.spec.whatwg.org/#fetching, step 12).</summary>
    private static string GetAcceptHeader(FetchRequest request)
    {
        // The value of a prefetch is the document Accept header value
        if (request.Mode == RequestMode.Navigate || request.IsPrefetch)
            return "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

        return request.Destination switch
        {
            RequestDestination.Image => "image/png,image/svg+xml,image/*;q=0.8,*/*;q=0.5",
            RequestDestination.Json => "application/json,*/*;q=0.5",
            RequestDestination.Text => "text/plain,*/*;q=0.5",
            RequestDestination.Style => "text/css,*/*;q=0.1",
            _ => "*/*",
        };
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-main-fetch</summary>
    private static async Task<FetchResponse> MainFetchAsync(FetchRequest request, bool recursive, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return FetchResponse.NetworkError(aborted: true);

        // Step 4: the violations of the report-only policies, before the URL is upgraded
        if (!request.IsSpeculative)
        {
            ContentSecurityPolicy.ReportRequestViolations(request);
        }

        // Step 5: https://w3c.github.io/webappsec-upgrade-insecure-requests/#upgrade-request
        UpgradeInsecureRequests.UpgradeRequest(request);

        // Step 6: an http: image of a client that prohibits mixed security contexts is fetched over https:, for each URL of a
        // redirect chain (https://w3c.github.io/webappsec-mixed-content/#upgrade-algorithm)
        if (request.Client is { } upgradeClient && request.CurrentUrl.Scheme == "http")
        {
            request.UrlList[^1] = MixedContent.UpgradeRequestUrl(upgradeClient, request.CurrentUrl, request.Destination, request.ImageSetInitiator);
        }

        if (IsBlocked(request))
            return FetchResponse.NetworkError();

        // Steps 8 and 9: the referrer policy and the referrer
        if (request.ReferrerPolicy.Length == 0)
        {
            request.ReferrerPolicy = ReferrerPolicies.GetRequestPolicy("", request.Client);
        }

        if (request.Referrer != "no-referrer")
        {
            string? referrer;
            if (request.Referrer == "client")
            {
                referrer = request.Client is { } client ? ReferrerPolicies.DetermineReferrer(client, request.CurrentUrl, request.ReferrerPolicy) : null;
            }
            else
            {
                referrer = UrlParser.Parse(request.Referrer, baseUrl: null) is { } referrerUrl ? ReferrerPolicies.DetermineReferrer(referrerUrl, request.CurrentUrl, request.ReferrerPolicy) : null;
            }

            request.Referrer = referrer ?? "no-referrer";
        }

        // Step 12
        FetchResponse response;
        var currentUrl = request.CurrentUrl;
        if (!recursive && request.PreloadedResponseCandidate is { } preloadedResponseCandidate)
        {
            // The preloaded response candidate, once the preload has completed. It completes in a task of the event loop.
            request.ConsumedPreload = true;
            try
            {
                response = preloadedResponseCandidate.IsCompleted || !cancellationToken.CanBeCanceled
                    ? await preloadedResponseCandidate.ConfigureAwait(false)
                    : await preloadedResponseCandidate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return FetchResponse.NetworkError(aborted: true);
            }
        }
        else if ((IsSameOriginAsRequest(request, currentUrl) && request.ResponseTainting == FetchResponseTainting.Basic) || currentUrl.Scheme == "data" || request.Mode is RequestMode.Navigate or RequestMode.WebSocket)
        {
            request.ResponseTainting = FetchResponseTainting.Basic;
            response = await SchemeFetchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else if (request.Mode == RequestMode.SameOrigin)
        {
            return FetchResponse.NetworkError();
        }
        else if (request.Mode == RequestMode.NoCors)
        {
            if (request.RedirectMode != FetchRedirectMode.Follow)
                return FetchResponse.NetworkError();

            request.ResponseTainting = FetchResponseTainting.Opaque;
            response = await SchemeFetchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else if (currentUrl.Scheme is not ("http" or "https"))
        {
            return FetchResponse.NetworkError();
        }
        else if (request.UseCorsPreflight || (request.UnsafeRequest && (!FetchHeaders.IsCorsSafelistedMethod(request.Method) || FetchHeaders.GetCorsUnsafeRequestHeaderNames(request.HeaderList).Count > 0)))
        {
            request.ResponseTainting = FetchResponseTainting.Cors;
            response = await HttpFetchAsync(request, makeCorsPreflight: true, cancellationToken).ConfigureAwait(false);
            if (response.IsNetworkError)
            {
                GetCaches(request)?.ClearPreflightEntries(request);
            }
        }
        else
        {
            request.ResponseTainting = FetchResponseTainting.Cors;
            response = await HttpFetchAsync(request, makeCorsPreflight: false, cancellationToken).ConfigureAwait(false);
        }

        // Step 13
        if (recursive)
            return response;

        return FinalizeMainFetch(request, response);
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#fetch-controller-process-the-next-manual-redirect: follows the redirect of a response
    /// of a navigation whose redirect mode is "manual" (https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching):
    /// the response of the location URL (which may be another redirect).
    /// </summary>
    public static Task<FetchResponse> ProcessNextManualRedirectAsync(FetchRequest request, FetchResponse response, CancellationToken cancellationToken)
    {
        try
        {
            return Core();
        }
#pragma warning disable CA1031 // A failure of the fetch is a network error
        catch (Exception)
#pragma warning restore CA1031
        {
            return Task.FromResult(FetchResponse.NetworkError());
        }

        async Task<FetchResponse> Core()
        {
            var redirectResponse = await HttpRedirectFetchAsync(request, response, cancellationToken).ConfigureAwait(false);
            return FinalizeMainFetch(request, redirectResponse);
        }
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-main-fetch, steps 14 to 22: the response of a fetch that is not recursive.</summary>
    private static FetchResponse FinalizeMainFetch(FetchRequest request, FetchResponse response)
    {
        // Step 14: the filtered response
        if (!response.IsNetworkError && response.Type == FetchResponseType.Default)
        {
            if (request.ResponseTainting == FetchResponseTainting.Cors)
            {
                var headerNames = FetchHeaders.ExtractTokenList(response.HeaderList, "Access-Control-Expose-Headers", out _) ?? [];
                if (request.CredentialsMode != FetchCredentialsMode.Include && headerNames.Contains("*", StringComparer.Ordinal))
                {
                    response.CorsExposedHeaderNames = FetchHeaders.ConvertToSortedLowercaseSet(response.HeaderList.Select(header => header.Key));
                }
                else
                {
                    response.CorsExposedHeaderNames = headerNames;
                }
            }

            response = request.ResponseTainting switch
            {
                FetchResponseTainting.Basic => response.ToBasic(),
                FetchResponseTainting.Cors => response.ToCors(),
                _ => response.ToOpaque(),
            };
        }

        // Steps 15 and 16: the URL list of the internal response
        var internalResponse = response.Internal;
        if (internalResponse.UrlList.Count == 0)
        {
            internalResponse.UrlList = [.. request.UrlList];
            if (response.Type is not (FetchResponseType.Opaque or FetchResponseType.Error))
            {
                response.UrlList = internalResponse.UrlList;
            }
        }

        // Step 19: a response blocked by the content security policies (https://w3c.github.io/webappsec-csp/#should-block-response),
        // due to its MIME type or due to nosniff (https://fetch.spec.whatwg.org/#should-response-to-request-be-blocked-due-to-mime-type?)
        if (!response.IsNetworkError && !request.IsSpeculative)
        {
            // The post-request check of the script directives reports the hash of the scripts (https://w3c.github.io/webappsec-csp/#script-post-request)
            // (not for a response that fails its integrity metadata: browsers do not report it)
            if (request.Integrity.Length == 0 || SubresourceIntegrity.Matches(response.Body ?? [], response.Type is FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect, request.Integrity))
            {
                ContentSecurityPolicyReportHash.PotentiallyReportHash(request, response);
            }

            if (ContentSecurityPolicy.ShouldBlockResponse(request, response))
                return FetchResponse.NetworkError();
        }

        if (!response.IsNetworkError && DocumentResource.IsBlocked(internalResponse.HeaderList, request.Destination))
            return FetchResponse.NetworkError();

        // Step 22: the integrity metadata (https://w3c.github.io/webappsec-subresource-integrity/#does-response-match-metadatalist)
        if (!response.IsNetworkError && request.Integrity.Length > 0 && !SubresourceIntegrity.Matches(response.Body ?? [], response.Type is FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect, request.Integrity))
            return FetchResponse.NetworkError();

        // Step 19: a null body status, or a HEAD or CONNECT request, has no body
        if (!response.IsNetworkError && (internalResponse.Status is 101 or 103 or 204 or 205 or 304 || request.Method is "HEAD" or "CONNECT"))
        {
            internalResponse.Body = null;
            response.Body = null;
        }

        return response;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#main-fetch, step 7: whether the current URL of a request is blocked, checked for each
    /// URL of a redirect chain: a bad port, mixed content (https://w3c.github.io/webappsec-mixed-content/#should-block-fetch:
    /// the navigations of the top-level navigables are not mixed content, the navigations of the child navigables are) or
    /// the enforced content security policies of the client, whose violations are reported (https://w3c.github.io/webappsec-csp/#should-block-request;
    /// the report-only policies are checked before the URL is upgraded, step 4; the policies of a speculative fetch are
    /// checked by the speculative parser, without reporting).
    /// </summary>
    private static bool IsBlocked(FetchRequest request)
    {
        // The three checks run: the violations of the content security policies are also reported for a request that is
        // blocked as mixed content (e.g. a ws: WebSocket of a secure context whose policy has connect-src 'none'), as in the browsers
        var blocked = !request.IsSpeculative && ContentSecurityPolicy.ShouldBlockRequest(request);
        var url = request.CurrentUrl;
        if (url.Scheme is "http" or "https")
        {
            if (BadPorts.Contains(url.Port >= 0 ? url.Port : url.Scheme == "https" ? 443 : 80))
                return true;

            if (request.Client is { } mixedContentClient && !(request.Mode == RequestMode.Navigate && request.Destination == RequestDestination.Document) && MixedContent.ShouldBlockFetch(mixedContentClient, url))
                return true;
        }

        return blocked;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-scheme-fetch</summary>
    private static async Task<FetchResponse> SchemeFetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        var url = request.CurrentUrl;
        switch (url.Scheme)
        {
            case "about":
                if (url.OpaquePath == "blank")
                    return FetchResponse.Create(200, "OK", [new("Content-Type", "text/html;charset=utf-8")], []);

                return FetchResponse.NetworkError();

            case "blob":
            {
                var entry = request.UrlList.Count == 1 ? request.BlobUrlEntry : null;
                if (entry is null && request.Client is { } client)
                {
                    entry = FileApi.BlobUrlStore.Get(client).Resolve(url);
                }

                if (request.Method != "GET" || entry is null)
                    return FetchResponse.NetworkError();

                var blob = entry.Object;
                var bytes = blob.Data.ToArray();
                var headers = new List<KeyValuePair<string, string>>();
                var rangeHeader = HeaderList.Get(request.HeaderList, "Range");
                if (rangeHeader is null)
                {
                    headers.Add(new("Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture)));
                    headers.Add(new("Content-Type", GetBlobContentType(blob)));
                    return FetchResponse.Create(200, "OK", headers, bytes);
                }

                // A range request (https://fetch.spec.whatwg.org/#scheme-fetch, blob step 9)
                if (FetchHeaders.ParseSingleRangeHeaderValue(rangeHeader, allowWhitespace: true) is not var (rangeStart, rangeEnd))
                    return FetchResponse.NetworkError();

                long start;
                long end;
                if (rangeStart is null)
                {
                    start = Math.Max(bytes.Length - rangeEnd!.Value, 0);
                    end = bytes.Length - 1;
                }
                else
                {
                    if (rangeStart.Value >= bytes.Length)
                        return FetchResponse.NetworkError();

                    start = rangeStart.Value;
                    end = rangeEnd is { } e ? Math.Min(e, bytes.Length - 1) : bytes.Length - 1;
                }

                var slice = bytes.AsSpan((int)start, (int)(end - start + 1)).ToArray();
                headers.Add(new("Content-Length", slice.Length.ToString(CultureInfo.InvariantCulture)));
                headers.Add(new("Content-Type", GetBlobContentType(blob)));
                headers.Add(new("Content-Range", "bytes " + start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + "/" + bytes.Length.ToString(CultureInfo.InvariantCulture)));
                return FetchResponse.Create(206, "Partial Content", headers, slice);
            }

            case "data":
                if (DataUrl.Process(url) is not var (mimeType, body))
                    return FetchResponse.NetworkError();

                return FetchResponse.Create(200, "OK", [new("Content-Type", mimeType)], body);

            case "http" or "https":
                return await HttpFetchAsync(request, makeCorsPreflight: false, cancellationToken).ConfigureAwait(false);

            // The file: URLs are left to the implementation: the loader gets the resources of the documents (but not the ones
            // of fetch() and XMLHttpRequest), without the HTTP semantics (no credentials, no redirect)
            case "file" when request.Destination != RequestDestination.Empty:
                return await NetworkFetchAsync(request, [.. request.HeaderList], includeCredentials: false, cancellationToken).ConfigureAwait(false);

            default:
                return FetchResponse.NetworkError();
        }
    }

    /// <summary>The Content-Type of the response of a blob: its type, or the empty string when it is not a valid MIME type.</summary>
    private static string GetBlobContentType(FileApi.Blob blob) => MimeType.Parse(blob.Type) is null ? "" : blob.Type;

    /// <summary>https://fetch.spec.whatwg.org/#concept-http-fetch</summary>
    private static async Task<FetchResponse> HttpFetchAsync(FetchRequest request, bool makeCorsPreflight, CancellationToken cancellationToken)
    {
        // Step 5: the service workers (https://w3c.github.io/ServiceWorker/#handle-fetch): the response of a service worker
        // is already filtered, it is not checked by the CORS check and the cross-origin resource policy
        if (request.ServiceWorkersMode == ServiceWorkersMode.All && await ServiceWorkerFetchHandling.HandleFetchAsync(request, cancellationToken).ConfigureAwait(false) is { } serviceWorkerResponse)
        {
            if (serviceWorkerResponse.Type == FetchResponseType.Error
                || (request.Mode == RequestMode.SameOrigin && serviceWorkerResponse.Type == FetchResponseType.Cors)
                || (request.Mode != RequestMode.NoCors && serviceWorkerResponse.Type == FetchResponseType.Opaque)
                || (request.RedirectMode != FetchRedirectMode.Manual && serviceWorkerResponse.Type == FetchResponseType.OpaqueRedirect)
                || (request.RedirectMode != FetchRedirectMode.Follow && serviceWorkerResponse.UrlList.Count > 1))
            {
                return FetchResponse.NetworkError();
            }

            // The timing of a response given by a service worker is reported as the one of a response of the network
            request.IsSentToLoader = true;
            request.IsAnsweredByServiceWorker = true;
            return await ProcessRedirectAsync(request, serviceWorkerResponse, cancellationToken).ConfigureAwait(false);
        }

        // Step 6.1: the CORS-preflight request
        if (makeCorsPreflight)
        {
            var caches = GetCaches(request);
            var needsPreflight = (caches is null || !caches.HasMethodEntry(request, request.Method)) && (!FetchHeaders.IsCorsSafelistedMethod(request.Method) || request.UseCorsPreflight);
            if (!needsPreflight)
            {
                foreach (var name in FetchHeaders.GetCorsUnsafeRequestHeaderNames(request.HeaderList))
                {
                    if (caches is null || !caches.HasHeaderEntry(request, name))
                    {
                        needsPreflight = true;
                        break;
                    }
                }
            }

            if (needsPreflight)
            {
                var preflightResponse = await CorsPreflightFetchAsync(request, cancellationToken).ConfigureAwait(false);
                if (preflightResponse.IsNetworkError)
                    return preflightResponse;
            }
        }

        // Step 6.2: the redirects coming from the network are not exposed to a service worker
        if (request.RedirectMode == FetchRedirectMode.Follow)
        {
            request.ServiceWorkersMode = ServiceWorkersMode.None;
        }

        // Step 6.3
        var response = await HttpNetworkOrCacheFetchAsync(request, isAuthenticationFetch: false, cancellationToken).ConfigureAwait(false);
        if (response.IsNetworkError)
            return response;

        // Step 4.4: the CORS check
        if (request.ResponseTainting == FetchResponseTainting.Cors && !CorsCheck(request, response))
            return FetchResponse.NetworkError();

        // Step 4.5: the timing allow check of each response of the fetch (the redirects included)
        if (!TimingAllowCheck(request, response))
        {
            request.TimingAllowFailed = true;
        }

        // Step 4.5: the cross-origin resource policy check of an opaque response
        if (request.ResponseTainting == FetchResponseTainting.Opaque && request.Client is { } client
            && CrossOriginResourcePolicy.IsBlocked(client, client.SubresourceEmbedderPolicy, request.Destination == RequestDestination.Empty ? "" : FetchMetadata.GetDestinationName(request.Destination), request.Url, request.CurrentUrl, response.HeaderList, forNavigation: false,
                requestIncludesCredentials: request.CredentialsMode == FetchCredentialsMode.Include && client.SubresourceEmbedderPolicy != EmbedderPolicyValue.Credentialless))
        {
            return FetchResponse.NetworkError();
        }

        return await ProcessRedirectAsync(request, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-http-fetch, step 8: the redirects</summary>
    private static async Task<FetchResponse> ProcessRedirectAsync(FetchRequest request, FetchResponse response, CancellationToken cancellationToken)
    {
        if (response.Internal.Status is 301 or 302 or 303 or 307 or 308)
        {
            if (response.Internal.Status != 303 && request.Body is not null && !request.BodyHasSource)
                return FetchResponse.NetworkError();

            switch (request.RedirectMode)
            {
                case FetchRedirectMode.Error:
                    return FetchResponse.NetworkError();
                case FetchRedirectMode.Manual:
                    // A navigation follows the redirects itself (see ProcessNextManualRedirectAsync); the request of a
                    // navigation that a service worker fetches again gets an opaque-redirect filtered response
                    return request.Mode == RequestMode.Navigate && request.Navigable is not null ? response : response.ToOpaqueRedirect();
                default:
                    return await HttpRedirectFetchAsync(request, response, cancellationToken).ConfigureAwait(false);
            }
        }

        return response;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-http-redirect-fetch</summary>
    private static async Task<FetchResponse> HttpRedirectFetchAsync(FetchRequest request, FetchResponse response, CancellationToken cancellationToken)
    {
        var internalResponse = response.Internal;
        var locationUrl = internalResponse.GetLocationUrl(request.CurrentUrl.Fragment, out var failure);
        if (failure)
            return FetchResponse.NetworkError();

        if (locationUrl is null)
            return response;

        if (locationUrl.Scheme is not ("http" or "https"))
            return FetchResponse.NetworkError();

        if (request.RedirectCount == MaxRedirects)
            return FetchResponse.NetworkError();

        request.RedirectCount++;
        if (request.Mode == RequestMode.Cors && locationUrl.IncludesCredentials && !IsSameOrigin(locationUrl, request.Origin))
            return FetchResponse.NetworkError();

        if (request.ResponseTainting == FetchResponseTainting.Cors && locationUrl.IncludesCredentials)
            return FetchResponse.NetworkError();

        if (internalResponse.Status != 303 && request.Body is not null && !request.BodyHasSource)
            return FetchResponse.NetworkError();

        // Step 11: the tainted origin
        if (request.Mode == RequestMode.Cors && locationUrl.SerializeOrigin() != request.CurrentUrl.SerializeOrigin() && !IsSameOrigin(request.CurrentUrl, request.Origin))
        {
            request.TaintedOrigin = true;
        }

        // Step 12: a POST request redirected by a 301 or 302 status, or any request but GET or HEAD redirected by a 303 status, becomes a GET request without body
        if ((internalResponse.Status is 301 or 302 && request.Method == "POST") || (internalResponse.Status == 303 && request.Method is not ("GET" or "HEAD")))
        {
            request.Method = "GET";
            request.Body = null;
            foreach (var name in FetchHeaders.RequestBodyHeaderNames)
            {
                FetchHeaders.Delete(request.HeaderList, name);
            }
        }

        // Step 13: the Authorization header is not sent to another origin
        if (request.CurrentUrl.SerializeOrigin() != locationUrl.SerializeOrigin())
        {
            FetchHeaders.Delete(request.HeaderList, "Authorization");
        }

        // Steps 16 to 18: the timing of the redirects
        if (request.Timing.IsRecorded && GetTimestamp(request) is { } now)
        {
            var timing = request.Timing;
            timing.RedirectEnd = timing.PostRedirectStart = now;
            if (timing.RedirectStart == 0)
            {
                timing.RedirectStart = timing.Start;
            }
        }

        request.UrlList.Add(locationUrl);

        // Step 19: the Referrer-Policy header of the redirect
        if (ReferrerPolicies.ParseHeader(internalResponse.HeaderList) is { Length: > 0 } policy)
        {
            request.ReferrerPolicy = policy;
        }

        return await MainFetchAsync(request, recursive: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-http-network-or-cache-fetch</summary>
    private static async Task<FetchResponse> HttpNetworkOrCacheFetchAsync(FetchRequest request, bool isAuthenticationFetch, CancellationToken cancellationToken)
    {
        var headers = new List<KeyValuePair<string, string>>(request.HeaderList);

        // Steps 8.5 to 8.7: Content-Length
        string? contentLength = null;
        if (request.Body is null && request.Method is "POST" or "PUT")
        {
            contentLength = "0";
        }
        else if (request.Body is { } body)
        {
            contentLength = body.Length.ToString(CultureInfo.InvariantCulture);
        }

        if (contentLength is not null)
        {
            headers.Add(new("Content-Length", contentLength));
        }

        // Step 8.10: the bodies of the inflight keepalive requests of the fetch group are limited to 64 KiB (the request is
        // a record of the fetch group: it is not counted twice)
        if (request.Keepalive && request.Body is { Length: > 0 } keepaliveBody && request.Client is { } keepaliveClient
            && keepaliveBody.Length + keepaliveClient.FetchGroup.GetInflightKeepaliveBytes(except: request) > FetchGroup.KeepaliveQuota)
        {
            return FetchResponse.NetworkError();
        }

        // Step 8.11: Referer
        var referrer = request.Referrer is "no-referrer" or "client" ? null : request.Referrer;
        if (referrer is not null)
        {
            headers.Add(new("Referer", referrer));
        }

        // Step 8.12: Origin (https://fetch.spec.whatwg.org/#append-a-request-origin-header)
        // https://fetch.spec.whatwg.org/#serializing-a-request-origin: "null" when the request has a redirect-tainted origin
        // (e.g. a POST navigation redirected through another origin)
        var serializedOrigin = request.TaintedOrigin || HasRedirectTaintedOrigin(request) ? "null" : request.Origin;
        if (request.ResponseTainting == FetchResponseTainting.Cors || request.Mode == RequestMode.WebSocket)
        {
            headers.Add(new("Origin", serializedOrigin));
        }
        else if (request.Method is not ("GET" or "HEAD"))
        {
            headers.Add(new("Origin", request.Mode == RequestMode.Cors ? serializedOrigin : GetNoCorsOriginHeader(serializedOrigin, request.Origin, request.ReferrerPolicy, request.CurrentUrl)));
        }

        // Step 8.13: the Fetch Metadata headers (https://w3c.github.io/webappsec-fetch-metadata/#abstract-opdef-append-the-fetch-metadata-headers-for-a-request)
        var metadataOrigin = GetLoaderOrigin(request);
        var metadataOriginRecord = metadataOrigin is not null && request.Client?.Origin is { } clientOrigin && clientOrigin.Serialize() == metadataOrigin ? clientOrigin : null;
        FetchMetadata.AppendHeaders(headers, request.Destination, request.Mode, metadataOrigin, request.UserActivation, request.RedirectChain, metadataOriginRecord);

        // Steps 8.16 to 8.18: the cache mode (there is no HTTP cache)
        var cacheMode = request.CacheMode;
        if (cacheMode == "default" && (FetchHeaders.Contains(headers, "If-Modified-Since") || FetchHeaders.Contains(headers, "If-None-Match") || FetchHeaders.Contains(headers, "If-Unmodified-Since") || FetchHeaders.Contains(headers, "If-Match") || FetchHeaders.Contains(headers, "If-Range")))
        {
            cacheMode = "no-store";
        }

        if (cacheMode == "no-cache" && !FetchHeaders.Contains(headers, "Cache-Control"))
        {
            headers.Add(new("Cache-Control", "max-age=0"));
        }

        if (cacheMode is "no-store" or "reload")
        {
            if (!FetchHeaders.Contains(headers, "Pragma"))
            {
                headers.Add(new("Pragma", "no-cache"));
            }

            if (!FetchHeaders.Contains(headers, "Cache-Control"))
            {
                headers.Add(new("Cache-Control", "no-cache"));
            }
        }

        // Step 8.14: the purpose of a prefetch (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch)
        // (https://wicg.github.io/nav-speculation/prerendering.html#interaction-with-fetch: the requests of the documents of a
        // prerendering traversable are prefetches too)
        var isPrerendering = (request.Navigable ?? request.Client?.BrowsingContext)?.Top.IsPrerendering == true;
        if ((request.IsPrefetch || isPrerendering) && !FetchHeaders.Contains(headers, "Sec-Purpose"))
        {
            headers.Add(new("Sec-Purpose", request.SecPurpose ?? (isPrerendering ? "prefetch;prerender" : "prefetch")));
        }

        // Step 8.19: a range request is not compressed
        if (FetchHeaders.Contains(headers, "Range"))
        {
            headers.Add(new("Accept-Encoding", "identity"));
        }

        // Step 8.22: the credentials (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, steps 8.22.2 to 8.22.3)
        var includeCredentials = request.CredentialsMode == FetchCredentialsMode.Include || (request.CredentialsMode == FetchCredentialsMode.SameOrigin && request.ResponseTainting == FetchResponseTainting.Basic);

        // A cross-origin no-cors request of a document whose embedder policy is "credentialless" has no credentials (step 8.11.3)
        if (request.Mode == RequestMode.NoCors && request.ResponseTainting == FetchResponseTainting.Opaque && request.Client?.SubresourceEmbedderPolicy == EmbedderPolicyValue.Credentialless)
        {
            includeCredentials = false;
        }

        var caches = GetCaches(request);
        if (includeCredentials && !request.UseIsolatedCredentials && !FetchHeaders.Contains(headers, "Authorization"))
        {
            string? authorizationValue = null;
            var entry = caches?.GetAuthenticationEntry(request.CurrentUrl);
            if (entry is not null && (!request.UseUrlCredentials || !request.CurrentUrl.IncludesCredentials))
            {
                authorizationValue = GetBasicAuthorization(entry.Value.Username, entry.Value.Password);
            }
            else if (request.CurrentUrl.IncludesCredentials && isAuthenticationFetch)
            {
                authorizationValue = GetBasicAuthorization(Encoding.UTF8.GetString(PercentEncoding.PercentDecode(request.CurrentUrl.Username)), Encoding.UTF8.GetString(PercentEncoding.PercentDecode(request.CurrentUrl.Password)));
            }

            if (authorizationValue is not null)
            {
                headers.Add(new("Authorization", authorizationValue));
            }
        }

        // The end of step 8: the client hints (https://wicg.github.io/client-hints-infrastructure/#fetch)
        ClientHints.AppendToRequest(request, headers);

        // Step 10: there is no cache
        if (cacheMode == "only-if-cached")
            return FetchResponse.NetworkError();

        var response = await NetworkFetchAsync(request, headers, includeCredentials, cancellationToken).ConfigureAwait(false);
        if (response.IsNetworkError)
            return response;

        // https://w3c.github.io/webappsec-clear-site-data/#fetch-integration: the Clear-Site-Data header clears the client hints
        ClientHints.ProcessResponse(request, response);

        // https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, step 8.22.4: the response's request-includes-credentials
        response.RequestIncludesCredentials = includeCredentials;

        // Step 14: an authentication request
        if (response.Status == 401 && request.ResponseTainting != FetchResponseTainting.Cors && includeCredentials && HeaderList.Get(response.HeaderList, "WWW-Authenticate") is not null)
        {
            if (request.Body is not null && !request.BodyHasSource)
                return FetchResponse.NetworkError();

            // The user is not prompted for the credentials: the credentials of the URL are tried once
            if (!request.UseUrlCredentials || isAuthenticationFetch)
                return response;

            return await HttpNetworkOrCacheFetchAsync(request, isAuthenticationFetch: true, cancellationToken).ConfigureAwait(false);
        }

        // The credentials of the URL that were accepted are remembered for the origin
        if (isAuthenticationFetch && response.Status != 401 && request.CurrentUrl.IncludesCredentials)
        {
            caches?.SetAuthenticationEntry(request.CurrentUrl, Encoding.UTF8.GetString(PercentEncoding.PercentDecode(request.CurrentUrl.Username)), Encoding.UTF8.GetString(PercentEncoding.PercentDecode(request.CurrentUrl.Password)));
        }

        return response;
    }

    private static string GetBasicAuthorization(string username, string password) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));

    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-http-network-fetch: the request is sent by the loader of the browsing context.
    /// The opening handshake of a WebSocket (<see cref="RequestMode.WebSocket"/>) is sent by <see cref="DocumentLoader.ConnectWebSocketAsync"/>:
    /// the connection of its response is <see cref="FetchResponse.Connection"/>.
    /// </summary>
    private static async Task<FetchResponse> NetworkFetchAsync(FetchRequest request, List<KeyValuePair<string, string>> headers, bool includeCredentials, CancellationToken cancellationToken)
    {
        // The navigable of a navigation, else the environment of the client (a browsing context or a worker), else the
        // browsing context of a keepalive request whose document was unloaded
        var browsingContext = request.Navigable ?? request.KeepaliveBrowsingContext;
        if ((request.Navigable?.Options ?? request.Client?.EnvironmentOptions ?? browsingContext?.Options)?.DocumentLoader is not { } loader
            || (request.Navigable?.EventLoop ?? request.Client?.EnvironmentEventLoop ?? browsingContext?.EventLoop) is not { } eventLoop)
        {
            return FetchResponse.NetworkError();
        }

        // The values of the headers with the same name are combined when they are sent (e.g. "A: 1" and "a: 2" are sent as "A: 1, 2")
        if (HasDuplicateNames(headers))
        {
            var combinedHeaders = new List<KeyValuePair<string, string>>(headers.Count);
            foreach (var (name, value) in headers)
            {
                FetchHeaders.Combine(combinedHeaders, name, value);
            }

            headers = combinedHeaders;
        }
        var origin = GetLoaderOrigin(request);
        var cookieContext = request.CookieContext;
        var documentRequest = new DocumentRequest(request.CurrentUrl.Serialize())
        {
            CookieJar = cookieContext is null ? request.Client?.CookieJar : cookieContext.CookieJar,
            SiteForCookies = cookieContext is null ? request.Client?.SiteForCookies : cookieContext.SiteForCookies,
            TopLevelOrigin = cookieContext is null ? request.Client?.TopLevelOriginForCookies : cookieContext.TopLevelOrigin,
            RedirectUrls = request.RedirectChain is { Count: > 1 } redirectChain ? [.. redirectChain.Take(redirectChain.Count - 1).Select(url => url.Serialize())] : [],
            Destination = request.Destination,
            NavigationDestination = request.NavigationDestination,
            Mode = request.Mode,
            Initiator = request.Initiator,
            Origin = origin is not null && request.TaintedOrigin ? "null" : origin,
            Referrer = request.Referrer is "no-referrer" or "client" ? null : request.Referrer,
            ReferrerPolicy = request.ReferrerPolicy,
            Method = request.Method,
            Body = request.Body is { } body ? (ReadOnlyMemory<byte>?)body : null,
            BodyContentType = request.Body is null ? null : HeaderList.Get(headers, "Content-Type"),
            Headers = headers,
            IncludeCredentials = includeCredentials,
            FollowRedirects = false,
            IsSynchronous = request.Synchronous,
            IsSpeculative = request.IsSpeculative,
            UserActivation = request.UserActivation,
            Integrity = request.Integrity,
        };

        request.IsSentToLoader = true;
        request.IsAnsweredByServiceWorker = false;
        if (request.Timing.IsRecorded)
        {
            request.Timing.FinalNetworkRequestStart = eventLoop.TimeProvider.GetTimestamp();
        }

        var isWebSocket = request.Mode == RequestMode.WebSocket;
        var resource = await LoadAsync(eventLoop, isWebSocket ? loader.ConnectWebSocketAsync : loader.LoadAsync, documentRequest, request.Synchronous, cancellationToken).ConfigureAwait(false);

        // A body that arrives over time is interpreted as its bytes arrive by the HTML document of a navigation, by an event
        // source and by the media and track elements; the other requests wait for the complete body
        if (resource is { ContentBytes: not null, RemainingContentBytes: not null } streamedResource && !isWebSocket && !request.StreamsBody && (request.Mode != RequestMode.Navigate || (resource.MimeType?.Essence ?? "text/html") != "text/html"))
        {
            resource = await LoadAsync(eventLoop, (_, token) => new ValueTask<DocumentResource?>(ReadCompleteBodyAsync(streamedResource, token)), documentRequest, request.Synchronous, cancellationToken).ConfigureAwait(false);
        }
        if (request.Timing.IsRecorded)
        {
            request.Timing.FinalNetworkResponseStart = eventLoop.TimeProvider.GetTimestamp();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            DisposeConnection(resource);
            return FetchResponse.NetworkError(aborted: true);
        }

        request.LoaderFailed = resource is null;
        if (resource is null)
            return FetchResponse.NetworkError();

        // Only the response of the opening handshake of a WebSocket that switches the protocol has a connection
        if (resource.Connection is not null && (!isWebSocket || resource.StatusCode != 101))
        {
            DisposeConnection(resource);
        }

        // The values of the headers are normalized, as by the HTTP parser; an invalid header is a network error
        var responseHeaders = new List<KeyValuePair<string, string>>(resource.Headers.Count);
        foreach (var (name, value) in resource.Headers)
        {
            var normalizedValue = FetchHeaders.Normalize(value);
            if (!FetchHeaders.IsHeaderName(name) || !FetchHeaders.IsHeaderValue(normalizedValue))
            {
                DisposeConnection(resource);
                return FetchResponse.NetworkError();
            }

            responseHeaders.Add(new(name, normalizedValue));
        }

        var response = FetchResponse.Create(resource.StatusCode, resource.StatusText ?? GetReasonPhrase(resource.StatusCode), responseHeaders, resource);
        if (isWebSocket && resource.StatusCode == 101)
        {
            response.Connection = resource.Connection;
        }

        // A loader that follows the redirects anyway returns the URLs of the redirects and of the last response: they are added
        // to the URL list, as the redirects followed by the library (https://fetch.spec.whatwg.org/#http-redirect-fetch), and
        // the checks of main fetch and the response tainting apply to each of them
        if (resource.Url is { } responseUrl && responseUrl != documentRequest.Url && !ApplyLoaderRedirects(request, [.. resource.RedirectUrls, responseUrl]))
        {
            DisposeConnection(resource);
            return FetchResponse.NetworkError();
        }

        response.UrlList = [.. request.UrlList];
        return response;
    }

    private static bool HasDuplicateNames(List<KeyValuePair<string, string>> headers)
    {
        for (var i = 1; i < headers.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (InfraAscii.EqualsIgnoreAsciiCase(headers[i].Key, headers[j].Key))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds the URLs of the redirects followed by the loader to the URL list of a request. Returns false (a network error)
    /// when one of them is blocked, or when the request of a "same-origin" mode is redirected to another origin. A redirect
    /// to another origin taints the response of a no-cors request (opaque) or of a CORS request (the CORS check applies).
    /// </summary>
    private static bool ApplyLoaderRedirects(FetchRequest request, IEnumerable<string> urls)
    {
        foreach (var value in urls)
        {
            if (UrlParser.Parse(value, baseUrl: null) is not { } url || url.Serialize() == request.CurrentUrl.Serialize())
                continue;

            // https://fetch.spec.whatwg.org/#http-redirect-fetch, step 11: the tainted origin
            if (request.Mode == RequestMode.Cors && url.SerializeOrigin() != request.CurrentUrl.SerializeOrigin() && !IsSameOrigin(request.CurrentUrl, request.Origin))
            {
                request.TaintedOrigin = true;
            }

            request.UrlList.Add(url);
            request.RedirectCount++;
            if (!request.IsSpeculative)
            {
                ContentSecurityPolicy.ReportRequestViolations(request);
            }

            if (IsBlocked(request))
                return false;

            // https://fetch.spec.whatwg.org/#main-fetch, step 12: the response tainting of the URL
            if (request.ResponseTainting == FetchResponseTainting.Basic && url.Scheme != "data" && !IsSameOriginAsRequest(request, url))
            {
                switch (request.Mode)
                {
                    case RequestMode.SameOrigin:
                        return false;
                    case RequestMode.NoCors:
                        request.ResponseTainting = FetchResponseTainting.Opaque;
                        break;
                    case RequestMode.Cors:
                        request.ResponseTainting = FetchResponseTainting.Cors;
                        break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The serialized origin of a request given to the loader and used by the Fetch Metadata headers: null for a navigation
    /// that is not initiated by a document (e.g. <see cref="BrowsingContext.Navigate(string)"/>), whose <c>Sec-Fetch-Site</c> is <c>none</c>.
    /// </summary>
    private static string? GetLoaderOrigin(FetchRequest request) => request.Client is null && request.Mode == RequestMode.Navigate ? null : request.Origin;

    /// <summary>Disposes the connection of a response that is not used (e.g. the fetch was aborted).</summary>
    private static void DisposeConnection(DocumentResource? resource)
    {
        try
        {
            resource?.Connection?.Dispose();
        }
#pragma warning disable CA1031 // The connection is not used
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>
    /// Calls the loader. When it completes asynchronously, the result is returned from a task of the event loop (the rest of
    /// the fetch algorithm runs in that task), except for a synchronous request, which waits for the loader.
    /// </summary>
    private static Task<DocumentResource?> LoadAsync(DomEventLoop eventLoop, Func<DocumentRequest, CancellationToken, ValueTask<DocumentResource?>> load, DocumentRequest request, bool synchronous, CancellationToken cancellationToken)
    {
        ValueTask<DocumentResource?> response;
        try
        {
            response = load(request, cancellationToken);
        }
#pragma warning disable CA1031 // An exception thrown by the loader is a network error
        catch (Exception)
#pragma warning restore CA1031
        {
            return Task.FromResult<DocumentResource?>(null);
        }

        if (response.IsCompleted || synchronous)
            return Task.FromResult(GetResult(response.IsCompleted ? response : new ValueTask<DocumentResource?>(WaitForResult(response.AsTask(), cancellationToken))));

        var completion = new TaskCompletionSource<DocumentResource?>();
        eventLoop.BeginOperation();
        _ = response.AsTask().ContinueWith(
            completed =>
            {
                eventLoop.QueueTask(() => completion.SetResult(GetResult(new ValueTask<DocumentResource?>(completed))));
                eventLoop.EndOperation();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return completion.Task;

        static DocumentResource? WaitForResult(Task<DocumentResource?> task, CancellationToken cancellationToken)
        {
            // A worker that waits for a synchronous request is not running: the host may advance a fake clock. The wait ends
            // when the fetch is terminated (e.g. the timeout of a synchronous XMLHttpRequest), even if the loader ignores it
            using var blockingWait = DomEventLoop.EnterBlockingWait(task, cancellationToken);
            using var registration = blockingWait.Registration;
            try
            {
                task.Wait(cancellationToken);
                return task.GetAwaiter().GetResult();
            }
#pragma warning disable CA1031 // An exception thrown by the loader is a network error
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }

        static DocumentResource? GetResult(ValueTask<DocumentResource?> completedResponse)
        {
            try
            {
                return completedResponse.Result;
            }
#pragma warning disable CA1031 // An exception thrown by the loader (including a cancellation) is a network error
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Waits for the rest of the body of a response that arrives over time (<see cref="DocumentResource.RemainingContentBytes"/>):
    /// the response has the complete body. An error of the sequence is a network error.
    /// </summary>
    private static async Task<DocumentResource?> ReadCompleteBodyAsync(DocumentResource resource, CancellationToken cancellationToken)
    {
        var body = new ArrayBufferWriter<byte>();
        body.Write(resource.ContentBytes!.Value.Span);
        await foreach (var chunk in resource.RemainingContentBytes!.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            body.Write(chunk.Span);
        }

        return new DocumentResource(body.WrittenMemory)
        {
            StatusCode = resource.StatusCode,
            StatusText = resource.StatusText,
            Headers = resource.Headers,
            Url = resource.Url,
            RedirectUrls = resource.RedirectUrls,
        };
    }

    /// <summary>https://fetch.spec.whatwg.org/#cors-preflight-fetch-0</summary>
    private static async Task<FetchResponse> CorsPreflightFetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        var preflight = new FetchRequest(request.CurrentUrl)
        {
            Method = "OPTIONS",
            UrlList = [.. request.UrlList],
            Client = request.Client,
            Destination = request.Destination,
            Origin = request.Origin,
            Referrer = request.Referrer,
            ReferrerPolicy = request.ReferrerPolicy,
            Mode = RequestMode.Cors,
            ResponseTainting = FetchResponseTainting.Cors,
            TaintedOrigin = request.TaintedOrigin,
            Synchronous = request.Synchronous,
            KeepaliveBrowsingContext = request.KeepaliveBrowsingContext,
        };

        preflight.HeaderList.Add(new("Accept", "*/*"));
        preflight.HeaderList.Add(new("Access-Control-Request-Method", request.Method));
        var unsafeHeaderNames = FetchHeaders.GetCorsUnsafeRequestHeaderNames(request.HeaderList);
        if (unsafeHeaderNames.Count > 0)
        {
            preflight.HeaderList.Add(new("Access-Control-Request-Headers", FetchHeaders.Join(unsafeHeaderNames)));
        }

        var response = await HttpNetworkOrCacheFetchAsync(preflight, isAuthenticationFetch: false, cancellationToken).ConfigureAwait(false);
        if (response.IsNetworkError || !CorsCheck(request, response) || !HeaderList.IsOkStatus(response.Status))
            return FetchResponse.NetworkError();

        var methods = FetchHeaders.ExtractTokenList(response.HeaderList, "Access-Control-Allow-Methods", out var methodsFailure);
        var headerNames = FetchHeaders.ExtractTokenList(response.HeaderList, "Access-Control-Allow-Headers", out var headerNamesFailure);
        if (methodsFailure || headerNamesFailure)
            return FetchResponse.NetworkError();

        methods ??= [];
        headerNames ??= [];
        if (methods.Count == 0 && request.UseCorsPreflight)
        {
            methods = [request.Method];
        }

        var credentials = request.CredentialsMode == FetchCredentialsMode.Include;
        if (!methods.Contains(request.Method, StringComparer.Ordinal) && !FetchHeaders.IsCorsSafelistedMethod(request.Method) && (credentials || !methods.Contains("*", StringComparer.Ordinal)))
            return FetchResponse.NetworkError();

        foreach (var (name, _) in request.HeaderList)
        {
            if (FetchHeaders.IsCorsNonWildcardRequestHeaderName(name) && !headerNames.Exists(headerName => InfraAscii.EqualsIgnoreAsciiCase(headerName, name)))
                return FetchResponse.NetworkError();
        }

        foreach (var unsafeName in unsafeHeaderNames)
        {
            if (!headerNames.Exists(headerName => InfraAscii.EqualsIgnoreAsciiCase(headerName, unsafeName)) && (credentials || !headerNames.Contains("*", StringComparer.Ordinal)))
                return FetchResponse.NetworkError();
        }

        // https://fetch.spec.whatwg.org/#http-access-control-max-age: 5 seconds by default, and when the value is not a
        // number (e.g. an empty value). As in the browsers, a negative number is 0 (the specification makes it a failure, i.e.
        // 5 seconds): the response is not cached.
        var maxAge = 5d;
        if (HeaderList.Get(response.HeaderList, "Access-Control-Max-Age") is { } maxAgeValue && long.TryParse(maxAgeValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
        {
            maxAge = Math.Clamp(seconds, 0, 7200);
        }

        if (GetCaches(request) is { } caches && maxAge > 0)
        {
            foreach (var method in methods)
            {
                caches.AddMethodEntry(request, method, maxAge);
            }

            foreach (var headerName in headerNames)
            {
                caches.AddHeaderEntry(request, headerName, maxAge);
            }
        }

        return response;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-cors-check</summary>
    private static bool CorsCheck(FetchRequest request, FetchResponse response)
    {
        var origin = HeaderList.Get(response.HeaderList, "Access-Control-Allow-Origin");
        if (origin is null)
            return false;

        if (request.CredentialsMode != FetchCredentialsMode.Include && origin == "*")
            return true;

        if ((request.TaintedOrigin ? "null" : request.Origin) != origin)
            return false;

        if (request.CredentialsMode != FetchCredentialsMode.Include)
            return true;

        return HeaderList.Get(response.HeaderList, "Access-Control-Allow-Credentials") == "true";
    }

    /// <summary>Whether the origin of a URL is the serialized origin (an opaque origin is not the same origin as any URL).</summary>
    private static bool IsSameOrigin(UrlRecord url, string origin) => origin != "null" && url.SerializeOrigin() == origin;

    /// <summary>
    /// https://fetch.spec.whatwg.org/#append-a-request-origin-header: the value of the <c>Origin</c> header of a request
    /// whose method is neither GET nor HEAD and whose mode is not "cors", given the serialized origin of the request, its
    /// origin, its referrer policy and its current URL.
    /// </summary>
    internal static string GetNoCorsOriginHeader(string serializedOrigin, string origin, string referrerPolicy, UrlRecord currentUrl)
    {
        return referrerPolicy switch
        {
            "no-referrer" => "null",
            "no-referrer-when-downgrade" or "strict-origin" or "strict-origin-when-cross-origin" when origin.StartsWith("https:", StringComparison.Ordinal) && !ReferrerPolicies.IsPotentiallyTrustworthy(currentUrl) => "null",
            "same-origin" when !IsSameOrigin(currentUrl, origin) => "null",
            _ => serializedOrigin,
        };
    }

    /// <summary>
    /// Whether the origin of <paramref name="url"/> is the origin of the request. The origin of a blob: URL is the origin of
    /// the environment of its entry (https://url.spec.whatwg.org/#concept-url-origin), which may be an opaque origin (e.g.
    /// the URL created by a sandboxed document or by a data: worker), same origin with the client that created the URL.
    /// </summary>
    private static bool IsSameOriginAsRequest(FetchRequest request, UrlRecord url)
    {
        if (IsSameOrigin(url, request.Origin))
            return true;

        if (url.Scheme != "blob" || request.UrlList.Count != 1 || request.Client is not { } client)
            return false;

        var entry = request.BlobUrlEntry ?? FileApi.BlobUrlStore.Get(client).Resolve(url);
        return entry is not null && Document.IsSameOrigin(entry.Environment, client);
    }

    private static FetchCaches? GetCaches(FetchRequest request)
    {
        // The workers use the caches of the browsing context group of their creator
        return request.Client?.EnvironmentBrowsingContext?.Top is { } top ? Caches.GetValue(top, static _ => new FetchCaches()) : null;
    }

    /// <summary>The reason phrase of a status (https://httpwg.org/specs/rfc9110.html#overview.of.status.codes), used when the loader does not provide one.</summary>
    internal static string GetReasonPhrase(int status) => status switch
    {
        100 => "Continue",
        101 => "Switching Protocols",
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        203 => "Non-Authoritative Information",
        204 => "No Content",
        205 => "Reset Content",
        206 => "Partial Content",
        300 => "Multiple Choices",
        301 => "Moved Permanently",
        302 => "Found",
        303 => "See Other",
        304 => "Not Modified",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        406 => "Not Acceptable",
        408 => "Request Timeout",
        409 => "Conflict",
        410 => "Gone",
        412 => "Precondition Failed",
        413 => "Content Too Large",
        415 => "Unsupported Media Type",
        416 => "Range Not Satisfiable",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "",
    };

    /// <summary>
    /// The CORS-preflight cache (https://fetch.spec.whatwg.org/#concept-cache) and the authentication entries
    /// (https://fetch.spec.whatwg.org/#authentication-entries) of a browsing context group. The entries expire with the
    /// clock of the event loop. The caches are used by the threads of the workers.
    /// </summary>
    private sealed class FetchCaches
    {
        private readonly Lock _lock = new();
        private readonly List<PreflightEntry> _entries = [];
        private readonly Dictionary<string, (string Username, string Password)> _authenticationEntries = new(StringComparer.Ordinal);

        private sealed record PreflightEntry(string Origin, string Url, bool Credentials, string? Method, string? HeaderName)
        {
            public DateTimeOffset Expires { get; set; }
        }

        private static DateTimeOffset Now(FetchRequest request) => (request.Client?.EnvironmentEventLoop?.TimeProvider ?? TimeProvider.System).GetUtcNow();

        /// <summary>https://fetch.spec.whatwg.org/#concept-cache-match</summary>
        private PreflightEntry? Find(FetchRequest request, Func<PreflightEntry, bool> predicate)
        {
            lock (_lock)
            {
                return FindLocked(request, predicate);
            }
        }

        private PreflightEntry? FindLocked(FetchRequest request, Func<PreflightEntry, bool> predicate)
        {
            var now = Now(request);
            _entries.RemoveAll(entry => entry.Expires <= now);
            var origin = request.TaintedOrigin ? "null" : request.Origin;
            var url = request.CurrentUrl.Serialize();
            var credentials = request.CredentialsMode == FetchCredentialsMode.Include;
            return _entries.Find(entry => entry.Origin == origin && entry.Url == url && entry.Credentials == credentials && predicate(entry));
        }

        /// <summary>https://fetch.spec.whatwg.org/#concept-cache-match-method</summary>
        public bool HasMethodEntry(FetchRequest request, string method)
            => Find(request, entry => entry.Method == method || (entry.Method == "*" && request.CredentialsMode != FetchCredentialsMode.Include)) is not null;

        /// <summary>https://fetch.spec.whatwg.org/#concept-cache-match-header</summary>
        public bool HasHeaderEntry(FetchRequest request, string headerName)
            => Find(request, entry => (entry.HeaderName is not null && InfraAscii.EqualsIgnoreAsciiCase(entry.HeaderName, headerName))
                || (entry.HeaderName == "*" && request.CredentialsMode != FetchCredentialsMode.Include && !FetchHeaders.IsCorsNonWildcardRequestHeaderName(headerName))) is not null;

        public void AddMethodEntry(FetchRequest request, string method, double maxAge) => AddEntry(request, method, headerName: null, maxAge);

        public void AddHeaderEntry(FetchRequest request, string headerName, double maxAge) => AddEntry(request, method: null, headerName, maxAge);

        private void AddEntry(FetchRequest request, string? method, string? headerName, double maxAge)
        {
            var expires = Now(request).AddSeconds(maxAge);
            lock (_lock)
            {
                var existing = FindLocked(request, entry => entry.Method == method && (entry.HeaderName is null ? headerName is null : headerName is not null && InfraAscii.EqualsIgnoreAsciiCase(entry.HeaderName, headerName)));
                if (existing is not null)
                {
                    existing.Expires = expires;
                    return;
                }

                _entries.Add(new PreflightEntry(request.TaintedOrigin ? "null" : request.Origin, request.CurrentUrl.Serialize(), request.CredentialsMode == FetchCredentialsMode.Include, method, headerName) { Expires = expires });
            }
        }

        /// <summary>https://fetch.spec.whatwg.org/#concept-cache-clear</summary>
        public void ClearPreflightEntries(FetchRequest request)
        {
            var origin = request.TaintedOrigin ? "null" : request.Origin;
            var url = request.CurrentUrl.Serialize();
            lock (_lock)
            {
                _entries.RemoveAll(entry => entry.Origin == origin && entry.Url == url);
            }
        }

        public (string Username, string Password)? GetAuthenticationEntry(UrlRecord url)
        {
            lock (_lock)
            {
                return _authenticationEntries.TryGetValue(url.SerializeOrigin(), out var entry) ? entry : null;
            }
        }

        public void SetAuthenticationEntry(UrlRecord url, string username, string password)
        {
            lock (_lock)
            {
                _authenticationEntries[url.SerializeOrigin()] = (username, password);
            }
        }
    }
}
