using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Compression;
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
/// The responses are stored in the HTTP cache (<see cref="HttpCache"/>). The response is processed once its body is fully received, except the rest of the body of a
/// navigation, of an event source, of a media resource and of <c>fetch()</c> (see <see cref="FetchRequest.StreamsBody"/>
/// and <see cref="DocumentResource.RemainingContentBytes"/>).
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
        // As in Chromium, the requests of a frozen document (https://wicg.github.io/page-lifecycle/) wait until it is resumed,
        // except the keepalive requests (e.g. navigator.sendBeacon()), which outlive the document
        if (!request.Keepalive && request.Mode != RequestMode.Navigate && request.Client is { IsFrozen: true, WorkerGlobal: null } frozenClient)
        {
            var completion = new TaskCompletionSource<FetchResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            frozenClient.DeferTaskUntilReactivated(() => _ = ForwardAsync(FetchAsync(request, cancellationToken), completion));
            return completion.Task;
        }

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

        // https://privacycg.github.io/storage-access/#fetching: whether the request can use the storage access of its client
        request.StorageAccessEligibility = StorageAccess.DetermineInitialEligibility(request);

        return MainFetchAsync(request, recursive: false, cancellationToken);
    }

    private static async Task ForwardAsync(Task<FetchResponse> task, TaskCompletionSource<FetchResponse> completion)
    {
        try
        {
            completion.TrySetResult(await task.ConfigureAwait(false));
        }
#pragma warning disable CA1031 // The exception is given to the deferred task
        catch (Exception ex)
#pragma warning restore CA1031
        {
            completion.TrySetException(ex);
        }
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
            internalResponse.HasRewrittenUrl = true;
            internalResponse.UrlList = [.. request.UrlList];
            if (response.Type is not (FetchResponseType.Opaque or FetchResponseType.Error))
            {
                response.UrlList = internalResponse.UrlList;
            }
        }

        // https://wicg.github.io/signature-based-sri/#verify-signature-assertions: the signatures of the response
        var signatures = response.IsNetworkError ? null : (IntegritySignatureResult?)IntegritySignatures.Verify(new IntegritySignatureContext(request.CurrentUrl, request.Method, request.HeaderList, internalResponse.Status, internalResponse.HeaderList), GetHttpCacheTime(request));

        // Step 19: a response blocked by the content security policies (https://w3c.github.io/webappsec-csp/#should-block-response),
        // due to its MIME type or due to nosniff (https://fetch.spec.whatwg.org/#should-response-to-request-be-blocked-due-to-mime-type?)
        if (!response.IsNetworkError && !request.IsSpeculative)
        {
            // The post-request check of the script directives reports the hash of the scripts (https://w3c.github.io/webappsec-csp/#script-post-request)
            // (not for a response that fails its integrity metadata: browsers do not report it)
            if (request.Integrity.Length == 0 || SubresourceIntegrity.Matches(response.Body ?? [], response.Type is FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect, request.Integrity, signatures))
            {
                ContentSecurityPolicyReportHash.PotentiallyReportHash(request, response);
            }

            if (ContentSecurityPolicy.ShouldBlockResponse(request, response))
                return FetchResponse.NetworkError();
        }

        if (!response.IsNetworkError && DocumentResource.IsBlocked(internalResponse.HeaderList, request.Destination))
            return FetchResponse.NetworkError();

        // https://wicg.github.io/signature-based-sri/#server-initiated-integrity-checks: a response whose signature is not valid
        // is a network error
        if (signatures is { Failed: true })
            return FetchResponse.NetworkError();

        // Step 22: the integrity metadata (https://w3c.github.io/webappsec-subresource-integrity/#does-response-match-metadatalist),
        // and the public keys of the signatures (https://wicg.github.io/signature-based-sri/#client-initiated-integrity-checks)
        if (!response.IsNetworkError && request.Integrity.Length > 0 && !SubresourceIntegrity.Matches(response.Body ?? [], response.Type is FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect, request.Integrity, signatures))
            return FetchResponse.NetworkError();

        // The digests of the Unencoded-Digest header of the response (https://wicg.github.io/signature-based-sri/#verify-unencoded-digest-assertions):
        // a response whose body arrives over time is not checked (the body of the resource of the loader is only read when
        // the response has the header)
        if (!response.IsNetworkError && HeaderList.Get(internalResponse.HeaderList, "Unencoded-Digest") is not null && internalResponse.Resource?.RemainingContentBytes is null
            && !SubresourceIntegrity.MatchesUnencodedDigest(internalResponse.HeaderList, internalResponse.Body ?? []))
        {
            return FetchResponse.NetworkError();
        }

        // Step 19: a null body status, or a HEAD or CONNECT request, has no body
        if (!response.IsNetworkError && (internalResponse.Status is 101 or 103 or 204 or 205 or 304 || request.Method is "HEAD" or "CONNECT"))
        {
            internalResponse.Body = null;
            response.Body = null;
        }

        // As in Chromium and WebKit, the Link headers of the response of a subresource preload their resources
        if (!response.IsNetworkError)
        {
            LinkPreload.ProcessSubresourceLinkHeaders(request, internalResponse);
        }

        return response;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#main-fetch, step 7: whether the current URL of a request is blocked, checked for each
    /// URL of a redirect chain: a bad port, mixed content (https://w3c.github.io/webappsec-mixed-content/#should-block-fetch:
    /// the navigations of the top-level navigables are not mixed content, the navigations of the child navigables are) or
    /// the enforced content security policies of the client, whose violations are reported (https://w3c.github.io/webappsec-csp/#should-block-request;
    /// the report-only policies are checked before the URL is upgraded, step 4; the policies of a speculative fetch are
    /// checked by the speculative parser, without reporting), and the integrity policies of the client.
    /// </summary>
    private static bool IsBlocked(FetchRequest request)
    {
        // The three checks run: the violations of the content security policies are also reported for a request that is
        // blocked as mixed content (e.g. a ws: WebSocket of a secure context whose policy has connect-src 'none'), as in the browsers
        var blocked = !request.IsSpeculative && ContentSecurityPolicy.ShouldBlockRequest(request);

        // https://w3c.github.io/webappsec-subresource-integrity/#should-request-be-blocked-by-integrity-policy: the violations
        // are reported (a speculative fetch is checked when its response is used)
        blocked |= !request.IsSpeculative && IntegrityPolicy.ShouldBlockRequest(request);

        var url = request.CurrentUrl;
        if (url.Scheme is "http" or "https")
        {
            if (BadPorts.Contains(url.Port >= 0 ? url.Port : url.Scheme == "https" ? 443 : 80))
                return true;

            // The dangling markup mitigation (https://github.com/whatwg/fetch/pull/519): a URL whose input had a tab or a
            // newline and a "<" is likely made of the markup that follows an attribute whose quote is not closed
            if (url.PotentiallyDanglingMarkup)
                return true;

            if (request.Client is { } mixedContentClient && !(request.Mode == RequestMode.Navigate && request.Destination == RequestDestination.Document) && MixedContent.ShouldBlockFetch(mixedContentClient, url))
                return true;
        }
        else if (url.Scheme == "blob" && request.Client is { } blobClient && !(request.Mode == RequestMode.Navigate && request.Destination == RequestDestination.Document)
            && url.OpaquePath is { } blobPath && UrlParser.Parse(blobPath, baseUrl: null) is { Scheme: "http" } blobOriginUrl && MixedContent.ShouldBlockFetch(blobClient, blobOriginUrl))
        {
            // https://w3c.github.io/webappsec-mixed-content/#should-block-fetch: a blob: URL whose origin is a non-trustworthy
            // tuple origin (a blob created by an http: document) is mixed content in a secure context. As in the browsers, a blob
            // created by an opaque origin (e.g. a sandboxed frame of the secure context) is not.
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

                // https://w3c.github.io/FileAPI/#blob-url-obtain-object: the blob URL of another storage key (the origin of the
                // environment that created it, https://storage.spec.whatwg.org/#obtain-a-storage-key-for-non-storage-purposes) is
                // not authorized for the request of an environment (e.g. a cross-origin blob: URL loaded by a script element).
                // The navigations are not checked: the environment of their reserved client has no origin yet.
                if (request.Mode != RequestMode.Navigate && (request.Client is not { } blobRequestClient || !OriginRecord.IsSameOrigin(entry.Environment.Origin, blobRequestClient.Origin)))
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
                return await NetworkFetchAsync(request, [.. request.HeaderList], includeCredentials: false, dictionary: null, cancellationToken).ConfigureAwait(false);

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
        // is already filtered, it is not checked by the CORS check. The cross-origin resource policy check of an opaque
        // response uses the embedder policy of the client (the service worker can have another one)
        // The request of a service worker to the URL of a request whose race-network-and-fetch-handler router rule is
        // running (e.g. fetch(event.request)) gets the response of the network request of the race
        if (request is { Method: "GET", Body: null, Client.WorkerGlobal: ServiceWorkerGlobalScope serviceWorkerGlobal }
            && serviceWorkerGlobal.ServiceWorkerRecord.GetRaceResponse(request.CurrentUrl.Serialize()) is { } raceResponse)
        {
            await ((Task)raceResponse).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (GetResult(raceResponse) is { IsNetworkError: false } raceNetworkResponse)
                return raceNetworkResponse.Clone();
        }

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

            // Step 7: the cross-origin resource policy check of an opaque response
            if ((request.ResponseTainting == FetchResponseTainting.Opaque || serviceWorkerResponse.Type == FetchResponseType.Opaque) && request.Client is { } serviceWorkerClient
                && IsBlockedByCrossOriginResourcePolicy(request, serviceWorkerClient, serviceWorkerResponse.Internal.UrlList is [.., var serviceWorkerResponseUrl] ? serviceWorkerResponseUrl : request.CurrentUrl, serviceWorkerResponse.Internal.HeaderList, serviceWorkerResponse.Internal.RequestIncludesCredentials))
            {
                return FetchResponse.NetworkError();
            }

            // The timing allow check of a cross-origin response that the service worker fetched (its CORS or opaque filtered
            // response, https://fetch.spec.whatwg.org/#concept-response-timing-allow-passed) fails for the request too, as in
            // the browsers: the worker has the origin of the client
            if (serviceWorkerResponse.Type is FetchResponseType.Cors or FetchResponseType.Opaque
                && !(HeaderList.GetDecodeAndSplit(serviceWorkerResponse.Internal.HeaderList, "Timing-Allow-Origin") is { } timingAllowOrigins && (timingAllowOrigins.Contains("*") || timingAllowOrigins.Contains(request.Origin))))
            {
                request.TimingAllowFailed = true;
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
        request.SingleHopCacheMode = null;
        if (response.IsNetworkError)
            return response;

        // https://privacycg.github.io/storage-access-headers/#http-fetch: the response asks to retry the request with the
        // unpartitioned cookies (Activate-Storage-Access: retry), which the storage-access permission allows
        if (StorageAccess.PerformRetryCheck(request, request.StorageAccessStatus, response.Internal.HeaderList))
            return await HttpStorageAccessRetryFetchAsync(request, cancellationToken).ConfigureAwait(false);

        // Step 4.4: the CORS check
        if (request.ResponseTainting == FetchResponseTainting.Cors && !CorsCheck(request, response))
            return FetchResponse.NetworkError();

        // Step 4.5: the timing allow check of each response of the fetch (the redirects included)
        if (!TimingAllowCheck(request, response))
        {
            request.TimingAllowFailed = true;
        }

        // Step 4.5: the cross-origin resource policy check of an opaque response
        if (request.ResponseTainting == FetchResponseTainting.Opaque && request.Client is { } client && IsBlockedByCrossOriginResourcePolicy(request, client, request.CurrentUrl, response.HeaderList,
                requestIncludesCredentials: request.CredentialsMode == FetchCredentialsMode.Include && client.SubresourceEmbedderPolicy != EmbedderPolicyValue.Credentialless))
        {
            return FetchResponse.NetworkError();
        }

        return await ProcessRedirectAsync(request, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>https://privacycg.github.io/storage-access-headers/#http-storage-access-retry-fetch</summary>
    private static async Task<FetchResponse> HttpStorageAccessRetryFetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        if (request.RedirectCount == MaxRedirects)
            return FetchResponse.NetworkError();

        request.RedirectCount++;
        request.UrlList.Add(request.CurrentUrl);
        request.SingleHopCacheMode = "reload";
        request.StorageAccessEligibility = StorageAccessEligibility.Eligible;
        return await MainFetchAsync(request, recursive: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>https://fetch.spec.whatwg.org/#cross-origin-resource-policy-check of a response of the request of a client.</summary>
    private static bool IsBlockedByCrossOriginResourcePolicy(FetchRequest request, Document client, UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, bool requestIncludesCredentials)
        => CrossOriginResourcePolicy.IsBlocked(client, client.SubresourceEmbedderPolicy, request.Destination == RequestDestination.Empty ? "" : FetchMetadata.GetDestinationName(request.Destination), request.Url, responseUrl, headers, forNavigation: false, requestIncludesCredentials);

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

        // https://privacycg.github.io/storage-access/#http-redirect-fetch: a redirect to another origin is not eligible for storage access
        StorageAccess.ProcessRedirect(request, locationUrl);

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

        // https://privacycg.github.io/storage-access-headers/#set-storage-access: the storage access status of the request
        // (the Sec-Fetch-Storage-Access header is appended with the Fetch Metadata headers)
        request.StorageAccessStatus = GetStorageAccessStatus(request);

        // Step 8.12: Origin (https://fetch.spec.whatwg.org/#append-a-request-origin-header)
        // https://fetch.spec.whatwg.org/#serializing-a-request-origin: "null" when the request has a redirect-tainted origin
        // (e.g. a POST navigation redirected through another origin)
        var serializedOrigin = request.TaintedOrigin || HasRedirectTaintedOrigin(request) ? "null" : request.Origin;
        if (request.ResponseTainting == FetchResponseTainting.Cors || request.Mode == RequestMode.WebSocket)
        {
            headers.Add(new("Origin", serializedOrigin));
        }
        else if (request.Method is not ("GET" or "HEAD") || request.StorageAccessStatus == StorageAccessStatus.Inactive)
        {
            headers.Add(new("Origin", request.Mode == RequestMode.Cors ? serializedOrigin : GetNoCorsOriginHeader(serializedOrigin, request.Origin, request.ReferrerPolicy, request.CurrentUrl)));
        }

        // Step 8.13: the Fetch Metadata headers (https://w3c.github.io/webappsec-fetch-metadata/#abstract-opdef-append-the-fetch-metadata-headers-for-a-request)
        var metadataOrigin = GetLoaderOrigin(request);
        var metadataOriginRecord = metadataOrigin is not null && request.Client?.Origin is { } clientOrigin && clientOrigin.Serialize() == metadataOrigin ? clientOrigin : null;
        FetchMetadata.AppendHeaders(headers, request.Destination, request.Mode, metadataOrigin, request.UserActivation, request.RedirectChain, metadataOriginRecord);
        if (request.StorageAccessStatus is { } storageAccessStatus)
        {
            FetchHeaders.Delete(headers, "Sec-Fetch-Storage-Access");
            headers.Add(new("Sec-Fetch-Storage-Access", StorageAccess.Serialize(storageAccessStatus)));
        }

        // Step 8.15: the default User-Agent value, when the browsing contexts have one (BrowsingContextOptions.UserAgent; the
        // loader may add its own otherwise)
        var environmentOptions = request.Navigable?.Options ?? request.Client?.EnvironmentOptions ?? request.KeepaliveBrowsingContext?.Options;
        if (environmentOptions?.UserAgent is { } userAgent && !FetchHeaders.Contains(headers, "User-Agent"))
        {
            headers.Add(new("User-Agent", userAgent));
        }

        // The Accept-Language header of the preferred languages of the user, when the browsing contexts have some
        // (BrowsingContextOptions.Languages; the loader may add its own otherwise)
        if (environmentOptions?.Languages is { Count: > 0 } languages && !FetchHeaders.Contains(headers, "Accept-Language"))
        {
            headers.Add(new("Accept-Language", BrowsingContextOptions.GetAcceptLanguage(languages)));
        }

        // Steps 8.16 to 8.18: the cache mode
        var cacheMode = request.SingleHopCacheMode ?? request.CacheMode;
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

        // https://wicg.github.io/signature-based-sri/#abstract-opdef-append-the-accept-signature-header: the public keys of the
        // integrity metadata
        if (request.Integrity.Length > 0 && !FetchHeaders.Contains(headers, "Accept-Signature") && IntegritySignatures.GetAcceptSignature(request.Integrity) is { } acceptSignature)
        {
            headers.Add(new("Accept-Signature", acceptSignature));
        }

        // https://w3c.github.io/gpc/#the-sec-gpc-header-field-for-http-requests
        GlobalPrivacyControlOptions.AppendToRequest(request, headers);

        // Steps 9 and 10: the stored response of the HTTP cache, used as is when it is fresh (or with the "force-cache" and
        // "only-if-cached" modes), else validated with the validators of the stored response
        string? cacheKey = null;
        var cache = cacheMode == "no-store" ? null : GetHttpCache(request, out cacheKey);
        HttpCacheEntry? storedResponse = null;
        var revalidating = false;
        var requestHeaders = headers;
        var now = GetHttpCacheTime(request);
        var requestDirectives = HttpCache.ParseCacheControl(headers);
        if (cache is not null && cacheMode != "reload" && request.Method == "GET" && !requestDirectives.ContainsKey("no-store"))
        {
            storedResponse = cache.Select(cacheKey!, includeCredentials, headers);
            if (storedResponse is not null)
            {
                var freshness = cacheMode is "force-cache" or "only-if-cached" ? HttpCacheFreshness.Fresh : HttpCache.GetFreshness(storedResponse, headers, now);
                if (freshness == HttpCacheFreshness.Fresh)
                    return CreateCachedResponse(request, storedResponse, includeCredentials);

                // https://httpwg.org/specs/rfc5861.html#n-the-stale-while-revalidate-cache-control-extension: the stale
                // response is used, and it is revalidated in the background
                if (freshness == HttpCacheFreshness.StaleWhileRevalidate && cacheMode == "default" && !requestDirectives.ContainsKey("only-if-cached"))
                {
                    RevalidateInBackground(request, cache, cacheKey!, storedResponse, headers, includeCredentials);
                    return CreateCachedResponse(request, storedResponse, includeCredentials);
                }

                // A stale response is validated with its validators (https://httpwg.org/specs/rfc9111.html#validation.sent);
                // a response without validator is fetched again. A request whose Cache-Control has only-if-cached does not
                // validate it.
                var etag = HeaderList.Get(storedResponse.Headers, "ETag");
                var lastModified = HeaderList.Get(storedResponse.Headers, "Last-Modified");
                if (requestDirectives.ContainsKey("only-if-cached"))
                {
                    storedResponse = null;
                }
                else if (etag is not null || lastModified is not null)
                {
                    revalidating = true;
                    headers = [.. headers];
                    if (etag is not null)
                    {
                        headers.Add(new("If-None-Match", etag));
                    }

                    if (lastModified is not null)
                    {
                        headers.Add(new("If-Modified-Since", lastModified));
                    }
                }
                else
                {
                    storedResponse = null;
                }
            }
        }

        // Step 10.1: a request of the "only-if-cached" mode without stored response is a network error
        if (cacheMode == "only-if-cached")
            return FetchResponse.NetworkError();

        // https://httpwg.org/specs/rfc9111.html#cache-request-directive.only-if-cached: a request whose Cache-Control has
        // only-if-cached gets a 504 response without a usable stored response
        if (cache is not null && requestDirectives.ContainsKey("only-if-cached") && !revalidating)
        {
            var gatewayTimeout = FetchResponse.Create(504, "Gateway Timeout", [], []);
            gatewayTimeout.UrlList = [.. request.UrlList];
            return gatewayTimeout;
        }

        // https://www.rfc-editor.org/rfc/rfc9842#section-2.2: the best compression dictionary of the request is announced to
        // the server. As in the browsers, the headers are added below the HTTP cache: they are not the headers of the stored
        // request (a response whose Vary header has Available-Dictionary is used whatever the dictionaries).
        var cachePartition = (cacheKey ?? GetHttpCacheKey(request)) is { } partitionKey ? HttpCache.GetPartition(partitionKey) : null;
        var dictionary = FindCompressionDictionary(request, cachePartition, headers, now);
        var networkHeaders = headers;
        if (dictionary is not null)
        {
            networkHeaders = [.. headers, new("Accept-Encoding", CompressionDictionaries.AcceptEncoding), new("Available-Dictionary", CompressionDictionaries.GetAvailableDictionaryValue(dictionary))];
            if (dictionary.Options.Id.Length > 0)
            {
                networkHeaders.Add(new("Dictionary-ID", CompressionDictionaries.GetDictionaryIdValue(dictionary.Options.Id)));
            }
        }

        var response = await NetworkFetchAsync(request, networkHeaders, includeCredentials, dictionary, cancellationToken).ConfigureAwait(false);
        if (response.IsNetworkError)
            return response;

        // https://w3c.github.io/webappsec-clear-site-data/#fetch-integration: the Clear-Site-Data header clears the data of
        // the origin once the cookies of the response are stored, before the response is stored in the HTTP cache
        // (as in the browsers, the response that clears the cache is not stored either)
        var clearedCache = ClearSiteData.ProcessResponse(request, response, includeCredentials, cachePartition);

        var responseTime = GetHttpCacheTime(request);
        if (cache is not null && (!clearedCache || response.Status == 304))
        {
            response = UpdateHttpCache(request, response, cache, cacheKey!, requestHeaders, includeCredentials, revalidating ? storedResponse : null, now, responseTime);
        }

        // https://www.rfc-editor.org/rfc/rfc9842#section-2.1: the response registers a compression dictionary
        response = RegisterCompressionDictionary(request, response, cachePartition, now, responseTime);

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

    /// <summary>
    /// The background revalidation of a stored response used within its <c>stale-while-revalidate</c> window
    /// (https://httpwg.org/specs/rfc5861.html#n-the-stale-while-revalidate-cache-control-extension): a copy of the request is
    /// sent to the network with the validators of the stored response, and its response updates the HTTP cache once its
    /// <c>Clear-Site-Data</c> header is processed, as the response of the network of a fetch. As in the
    /// browsers, the revalidation is not handled by the service workers, is not checked by the content security policies and
    /// has no resource timing; a single revalidation of a request is in progress at a time.
    /// </summary>
    private static void RevalidateInBackground(FetchRequest request, HttpCache cache, string key, HttpCacheEntry storedResponse, List<KeyValuePair<string, string>> requestHeaders, bool includeCredentials)
    {
        if (!cache.TryBeginRevalidation(key, includeCredentials))
            return;

        var revalidationRequest = request.Clone();
        revalidationRequest.Synchronous = false;
        revalidationRequest.StreamsBody = false;
        revalidationRequest.ProcessEarlyHintsResponse = null;
        _ = RevalidateAsync(revalidationRequest, cache, key, storedResponse, requestHeaders, includeCredentials);

        static async Task RevalidateAsync(FetchRequest request, HttpCache cache, string key, HttpCacheEntry storedResponse, List<KeyValuePair<string, string>> requestHeaders, bool includeCredentials)
        {
            try
            {
                var headers = new List<KeyValuePair<string, string>>(requestHeaders);
                var etag = HeaderList.Get(storedResponse.Headers, "ETag");
                var lastModified = HeaderList.Get(storedResponse.Headers, "Last-Modified");
                if (etag is not null)
                {
                    headers.Add(new("If-None-Match", etag));
                }

                if (lastModified is not null)
                {
                    headers.Add(new("If-Modified-Since", lastModified));
                }

                var requestTime = GetHttpCacheTime(request);
                var response = await NetworkFetchAsync(request, headers, includeCredentials, dictionary: null, CancellationToken.None).ConfigureAwait(false);
                if (!response.IsNetworkError)
                {
                    // As the response of the network of HttpNetworkOrCacheFetchAsync (the loader stored its cookies, and the
                    // continuation runs on the event loop of the request): the Clear-Site-Data header is processed before
                    // the HTTP cache is updated, and a response that clears the cache is not stored
                    var clearedCache = ClearSiteData.ProcessResponse(request, response, includeCredentials, HttpCache.GetPartition(key));
                    if (!clearedCache || response.Status == 304)
                    {
                        UpdateHttpCache(request, response, cache, key, requestHeaders, includeCredentials, etag is not null || lastModified is not null ? storedResponse : null, requestTime, GetHttpCacheTime(request));
                    }

                    ClientHints.ProcessResponse(request, response);
                }
            }
#pragma warning disable CA1031 // A failure of the revalidation keeps the stored response
            catch (Exception)
#pragma warning restore CA1031
            {
            }
            finally
            {
                cache.EndRevalidation(key, includeCredentials);
            }
        }
    }

    /// <summary>
    /// The HTTP cache of a request (https://fetch.spec.whatwg.org/#determine-the-http-cache-partition), or null when it has
    /// none (no loader, an opaque top-level origin, a URL that is not an HTTP(S) URL, or a request with isolated credentials,
    /// e.g. a cross-site prefetch, whose network context is not the one of the user agent): the cache of the loader, and the
    /// key of the URL in the partition of the request.
    /// </summary>
    private static HttpCache? GetHttpCache(FetchRequest request, out string? key)
    {
        key = null;
        if (request.CurrentUrl.Scheme is not ("http" or "https") || request.Mode == RequestMode.WebSocket || request.IsSpeculative || request.UseIsolatedCredentials || GetDocumentLoader(request) is not { } loader)
            return null;

        // The top-level site of the request, and whether it comes from a cross-site frame: the ones of the cookies (a
        // top-level navigation is its own top-level site)
        string? topLevelOrigin;
        string? siteForCookies;
        if (request.CookieContext is { } cookieContext)
        {
            topLevelOrigin = cookieContext.TopLevelOrigin;
            siteForCookies = cookieContext.SiteForCookies;
        }
        else if (request.Client is { } client && request.Mode != RequestMode.Navigate)
        {
            topLevelOrigin = client.TopLevelOriginForCookies;
            siteForCookies = client.SiteForCookies;
        }
        else
        {
            topLevelOrigin = null;
            siteForCookies = null;
        }

        key = HttpCache.GetKey(topLevelOrigin ?? request.CurrentUrl.SerializeOrigin(), crossSite: siteForCookies == "null", request.CurrentUrl);
        return key is null ? null : HttpCache.Get(loader);
    }

    /// <summary>The key of the URL of a request in its partition of the HTTP cache, or null when it has none.</summary>
    private static string? GetHttpCacheKey(FetchRequest request)
    {
        _ = GetHttpCache(request, out var key);
        return key;
    }

    /// <summary>The current time of the HTTP cache: the time of the event loop of the request.</summary>
    private static DateTimeOffset GetHttpCacheTime(FetchRequest request)
        => ((request.Navigable?.EventLoop ?? request.Client?.EnvironmentEventLoop ?? request.KeepaliveBrowsingContext?.EventLoop)?.TimeProvider ?? TimeProvider.System).GetUtcNow();

    /// <summary>The loader of a request: the one of its navigable, of the environment of its client, or of the browsing context of a keepalive request.</summary>
    private static DocumentLoader? GetDocumentLoader(FetchRequest request)
        => (request.Navigable?.Options ?? request.Client?.EnvironmentOptions ?? (request.Navigable ?? request.KeepaliveBrowsingContext)?.Options)?.DocumentLoader;

    /// <summary>The response constructed from a stored response (https://httpwg.org/specs/rfc9111.html#constructing.responses.from.caches).</summary>
    private static FetchResponse CreateCachedResponse(FetchRequest request, HttpCacheEntry entry, bool includeCredentials, string cacheState = "local")
    {
        var response = FetchResponse.Create(entry.Status, entry.StatusMessage, [.. entry.Headers], entry.Body);
        response.CacheState = cacheState;
        response.UrlList = [.. request.UrlList];
        response.RequestIncludesCredentials = includeCredentials;
        request.LoaderFailed = false;

        // A response of the HTTP cache has a resource timing, as one of the network (e.g. a cached @import child)
        request.IsSentToLoader = true;
        return response;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, step 10: the response of the network updates the HTTP
    /// cache. A successful response of an unsafe method invalidates the stored responses of the URL and of the URLs of its
    /// <c>Location</c> and <c>Content-Location</c> headers (https://httpwg.org/specs/rfc9111.html#invalidation); a <c>304</c>
    /// response that validates the stored response updates it, and the stored response is returned; a response that can be
    /// stored is stored once its body is received (a body read as it arrives is stored when it is fully read).
    /// </summary>
    private static FetchResponse UpdateHttpCache(FetchRequest request, FetchResponse response, HttpCache cache, string key, List<KeyValuePair<string, string>> requestHeaders, bool includeCredentials, HttpCacheEntry? storedResponse, DateTimeOffset requestTime, DateTimeOffset responseTime)
    {
        var url = request.CurrentUrl;
        if (request.Method is not ("GET" or "HEAD" or "OPTIONS" or "TRACE") && response.Status is >= 200 and < 400)
        {
            cache.Invalidate(key);
            foreach (var name in (ReadOnlySpan<string>)["Location", "Content-Location"])
            {
                if (HeaderList.Get(response.HeaderList, name) is { } location && UrlParser.Parse(location, url) is { } locationUrl && locationUrl.SerializeOrigin() == url.SerializeOrigin())
                {
                    cache.Invalidate(HttpCache.GetKeyInPartition(key, locationUrl));
                }
            }
        }

        if (storedResponse is not null && response.Status == 304)
        {
            return CreateCachedResponse(request, cache.Freshen(key, storedResponse, response.HeaderList, requestTime, responseTime), includeCredentials, cacheState: "validated");
        }

        if (!HttpCache.IsStorable(request.Method, url, requestHeaders, response.Status, response.HeaderList))
            return response;

        // As in the browsers, whose opaque response blocking (https://github.com/annevk/orb) blocks them before they are
        // stored, the HTML, JSON and XML responses of a cross-origin no-cors request are not stored
        if (request.ResponseTainting == FetchResponseTainting.Opaque && HeaderList.ExtractMimeType(response.HeaderList) is { } mimeType
            && (mimeType.Essence == "text/html" || mimeType.Essence == "application/json" || mimeType.Essence == "text/json" || mimeType.Subtype.EndsWith("+json", StringComparison.Ordinal) || mimeType.IsXml))
        {
            return response;
        }

        HttpCacheEntry CreateEntry(byte[] body) => new(includeCredentials, requestHeaders, response.Status, response.StatusMessage, response.HeaderList, body, requestTime, responseTime);
        if (response.Resource is { RemainingContentBytes: { } remaining } resource)
        {
            // The body that arrives over time is stored once it is fully received
            var first = resource.ContentBytes ?? ReadOnlyMemory<byte>.Empty;
            var cachedResource = new DocumentResource(first)
            {
                StatusCode = resource.StatusCode,
                StatusText = resource.StatusText,
                Headers = resource.Headers,
                Url = resource.Url,
                RedirectUrls = resource.RedirectUrls,
                RemainingContentBytes = new HttpCacheBody(first, remaining, body => cache.Store(key, CreateEntry(body))),
            };
            var streamedResponse = FetchResponse.Create(response.Status, response.StatusMessage, response.HeaderList, cachedResource);
            streamedResponse.UrlList = response.UrlList;
            return streamedResponse;
        }

        var bytes = response.Resource is { } completeResource ? completeResource.ContentBytes?.ToArray() ?? Encoding.UTF8.GetBytes(completeResource.Content ?? "") : response.Body ?? [];
        if (bytes.Length <= HttpCache.MaxBodyLength)
        {
            cache.Store(key, CreateEntry(bytes));
        }

        return response;
    }

    private static string GetBasicAuthorization(string username, string password) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));

    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-http-network-fetch: the request is sent by the loader of the browsing context.
    /// The opening handshake of a WebSocket (<see cref="RequestMode.WebSocket"/>) is sent by <see cref="DocumentLoader.ConnectWebSocketAsync"/>:
    /// the connection of its response is <see cref="FetchResponse.Connection"/>.
    /// </summary>
    private static async Task<FetchResponse> NetworkFetchAsync(FetchRequest request, List<KeyValuePair<string, string>> headers, bool includeCredentials, CompressionDictionary? dictionary, CancellationToken cancellationToken)
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
        var interimResponses = new InterimResponses(request, eventLoop);
        var documentRequest = new DocumentRequest(request.CurrentUrl.Serialize())
        {
            InterimResponseHandler = interimResponses.Process,
            CookieJar = cookieContext is null ? request.Client?.CookieJar : cookieContext.CookieJar,
            SiteForCookies = cookieContext is null ? request.Client?.SiteForCookies : cookieContext.SiteForCookies,
            TopLevelOrigin = cookieContext is null ? request.Client?.TopLevelOriginForCookies : cookieContext.TopLevelOrigin,
            RedirectUrls = request.RedirectChain is { Count: > 1 } redirectChain ? [.. redirectChain.Take(redirectChain.Count - 1).Select(url => url.Serialize())] : [],
            Destination = request.Destination,
            NavigationDestination = request.NavigationDestination,
            StorageAccessEligibility = request.StorageAccessEligibility,
            StorageAccessPermissions = GetStorageAccessPermissions(request),
            Mode = request.Mode,
            Initiator = request.Initiator,
            BrowsingContext = request.Navigable ?? request.Client?.BrowsingContext ?? request.KeepaliveBrowsingContext,
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
            StreamsResponseBody = request.StreamsBody && !request.Synchronous,
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

        // https://fetch.spec.whatwg.org/#fetch-timing-info-final-network-response-start-time: the headers of the final response
        // are received (the rest of a body that arrives over time is received later)
        interimResponses.Complete();
        if (request.Timing.IsRecorded)
        {
            request.Timing.FinalNetworkResponseStart = eventLoop.TimeProvider.GetTimestamp();
        }

        // https://www.rfc-editor.org/rfc/rfc9842#section-4: the body of a response compressed with a dictionary is decoded with
        // the dictionary announced by the request, as the loader decodes the other content codings (the headers are kept: the
        // Content-Length is the length of the encoded body). A body that cannot be decoded is an error of the body. As in the
        // browsers, the body of a request without dictionary is not decoded, as the bodies of the unknown content codings.
        if (resource is not null && dictionary is not null && !isWebSocket && CanHaveBody(request, resource.StatusCode) && GetDictionaryContentCoding(resource.Headers) is { } dictionaryCoding)
        {
            resource = DecodeDictionaryCompressedResource(resource, dictionaryCoding, dictionary);
        }

        // A body that arrives over time is interpreted as its bytes arrive by the HTML document of a navigation, by an event
        // source, by the media and track elements and by fetch() (a ReadableStream); the other requests, the redirects and
        // the responses without body wait for the complete body
        if (resource is { ContentBytes: not null, RemainingContentBytes: not null } streamedResource && !isWebSocket
            && (!request.StreamsBody || !CanHaveBody(request, resource.StatusCode))
            && (request.Mode != RequestMode.Navigate || (resource.MimeType?.Essence ?? "text/html") != "text/html"))
        {
            resource = await LoadAsync(eventLoop, (_, token) => new ValueTask<DocumentResource?>(ReadCompleteBodyAsync(streamedResource, token)), documentRequest, request.Synchronous, cancellationToken).ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            DisposeConnection(resource);
            return FetchResponse.NetworkError(aborted: true);
        }

        // An interim response whose headers are not valid is a network error (the HTTP parser fails), and so is a final
        // response whose status is the one of an interim response: the loader reported it as the final response, and the
        // connection ended before the final response (https://fetch.spec.whatwg.org/#http-network-fetch)
        if (resource is not null && (interimResponses.Failed || resource.StatusCode is >= 100 and <= 199 and not 101))
        {
            DisposeConnection(resource);
            resource = null;
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

    /// <summary>
    /// The interim responses of a request sent to the loader (https://fetch.spec.whatwg.org/#http-network-fetch, step 17):
    /// the time of the first one is the first interim network-response start time of the timing info, and the first
    /// <c>103</c> response is given to the process early hints response steps of the request (a navigation), from a task of
    /// the event loop. The loader reports them from any thread, before the final response.
    /// </summary>
    private sealed class InterimResponses(FetchRequest request, DomEventLoop eventLoop)
    {
        private readonly Lock _lock = new();
        private bool _completed;

        /// <summary>Whether an interim response had an invalid header: the request is a network error.</summary>
        public bool Failed { get; private set; }

        public void Process(int status, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            var normalizedHeaders = new List<KeyValuePair<string, string>>(headers.Count);
            var valid = true;
            foreach (var (name, value) in headers)
            {
                var normalizedValue = FetchHeaders.Normalize(value);
                if (!FetchHeaders.IsHeaderName(name) || !FetchHeaders.IsHeaderValue(normalizedValue))
                {
                    valid = false;
                    break;
                }

                normalizedHeaders.Add(new(name, normalizedValue));
            }

            lock (_lock)
            {
                if (_completed || Failed)
                    return;

                if (!valid)
                {
                    Failed = true;
                    return;
                }

                if (request.Timing.IsRecorded && request.Timing.FirstInterimNetworkResponseStart == 0)
                {
                    request.Timing.FirstInterimNetworkResponseStart = eventLoop.TimeProvider.GetTimestamp();
                }
            }

            if (status == 103 && request.ProcessEarlyHintsResponse is not null)
            {
                request.AddPendingEarlyHints(normalizedHeaders);
                eventLoop.QueueTask(request.ProcessPendingEarlyHints);
            }
        }

        /// <summary>The final response is received: the interim responses reported later are ignored.</summary>
        public void Complete()
        {
            lock (_lock)
            {
                _completed = true;
            }
        }
    }

    /// <summary>
    /// Whether the response of a request can have a body that is read as it arrives: not a redirect, not a null body status
    /// and not the response of a HEAD or CONNECT request (https://fetch.spec.whatwg.org/#main-fetch, step 19).
    /// </summary>
    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc9842#section-2.2: the compression dictionary announced by a request, or null: the
    /// best dictionary of the partition of the request (see <see cref="CompressionDictionaries.FindBestMatch"/>). A request
    /// to a URL that is not potentially trustworthy, a cross-origin <c>no-cors</c> request (whose response is opaque), a
    /// WebSocket handshake and a request whose <c>Accept-Encoding</c> is set (a range request) have no dictionary.
    /// </summary>
    private static CompressionDictionary? FindCompressionDictionary(FetchRequest request, string? partition, List<KeyValuePair<string, string>> headers, DateTimeOffset now)
    {
        if (partition is null || request.Mode == RequestMode.WebSocket || request.ResponseTainting == FetchResponseTainting.Opaque || FetchHeaders.Contains(headers, "Accept-Encoding")
            || request.CurrentUrl.Scheme is not ("http" or "https") || !ReferrerPolicies.IsPotentiallyTrustworthy(request.CurrentUrl) || GetDocumentLoader(request) is not { } loader)
        {
            return null;
        }

        var destination = FetchMetadata.GetDestinationName(request.Destination);
        return CompressionDictionaries.Get(loader).FindBestMatch(partition, request.CurrentUrl, destination == "empty" ? "" : destination, now);
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc9842#section-2.1: registers the dictionary of a response whose
    /// <c>Use-As-Dictionary</c> header is valid, once its body is received: the response of a request to a potentially
    /// trustworthy URL, which is not opaque (a cross-origin <c>no-cors</c> request), with an ok status.
    /// </summary>
    private static FetchResponse RegisterCompressionDictionary(FetchRequest request, FetchResponse response, string? partition, DateTimeOffset requestTime, DateTimeOffset responseTime)
    {
        if (partition is null || response.Status is < 200 or > 299 || request.ResponseTainting == FetchResponseTainting.Opaque || request.Mode == RequestMode.WebSocket
            || request.CurrentUrl.Scheme is not ("http" or "https") || !ReferrerPolicies.IsPotentiallyTrustworthy(request.CurrentUrl)
            || HeaderList.Get(response.HeaderList, "Use-As-Dictionary") is not { } value || GetDocumentLoader(request) is not { } loader
            || CompressionDictionaries.ParseUseAsDictionary(value, request.CurrentUrl) is not { } options)
        {
            return response;
        }

        var dictionaries = CompressionDictionaries.Get(loader);
        var url = request.CurrentUrl;
        var status = response.Status;
        var headers = response.HeaderList.ToArray();
        void Register(byte[] content) => dictionaries.Register(partition, url, options, content, status, headers, requestTime, responseTime);
        if (response.Resource is { RemainingContentBytes: { } remaining } resource)
        {
            // The body that arrives over time is registered once it is fully received
            var first = resource.ContentBytes ?? ReadOnlyMemory<byte>.Empty;
            var registeredResource = new DocumentResource(first)
            {
                StatusCode = resource.StatusCode,
                StatusText = resource.StatusText,
                Headers = resource.Headers,
                Url = resource.Url,
                RedirectUrls = resource.RedirectUrls,
                RemainingContentBytes = new HttpCacheBody(first, remaining, Register),
            };
            var streamedResponse = FetchResponse.Create(response.Status, response.StatusMessage, response.HeaderList, registeredResource);
            streamedResponse.UrlList = response.UrlList;
            return streamedResponse;
        }

        Register(response.Resource is { } completeResource ? completeResource.ContentBytes?.ToArray() ?? Encoding.UTF8.GetBytes(completeResource.Content ?? "") : response.Body ?? []);
        return response;
    }

    /// <summary>The content coding of a response when it is a dictionary content coding (<c>dcb</c> or <c>dcz</c>, https://www.rfc-editor.org/rfc/rfc9842#section-4), else null.</summary>
    private static string? GetDictionaryContentCoding(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (HeaderList.GetDecodeAndSplit(headers, "Content-Encoding") is not [var coding])
            return null;

        coding = InfraAscii.ToAsciiLowercase(coding.Trim(' ', '\t'));
        return DictionaryDecompressor.IsDictionaryCoding(coding) ? coding : null;
    }

    /// <summary>
    /// The resource whose body is the decoded body of a resource compressed with a dictionary. The body is decoded once it
    /// is received: it arrives over time, and an error of the decoder is an error of the body.
    /// </summary>
    private static DocumentResource DecodeDictionaryCompressedResource(DocumentResource resource, string coding, CompressionDictionary? dictionary)
    {
        return new DocumentResource(ReadOnlyMemory<byte>.Empty)
        {
            StatusCode = resource.StatusCode,
            StatusText = resource.StatusText,
            Headers = resource.Headers,
            Url = resource.Url,
            RedirectUrls = resource.RedirectUrls,
            RemainingContentBytes = DecodeAsync(resource, coding, dictionary),
        };

        static async IAsyncEnumerable<ReadOnlyMemory<byte>> DecodeAsync(DocumentResource resource, string coding, CompressionDictionary? dictionary, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var body = new ArrayBufferWriter<byte>();
            body.Write(resource.ContentBytes is { } bytes ? bytes.Span : Encoding.UTF8.GetBytes(resource.Content ?? ""));
            if (resource.RemainingContentBytes is { } remaining)
            {
                await foreach (var chunk in remaining.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    body.Write(chunk.Span);
                }
            }

            byte[] decoded;
            try
            {
                decoded = CompressionDictionaries.Decode(coding, body.WrittenSpan, dictionary);
            }
            catch (InvalidDataException ex)
            {
                throw new IOException(ex.Message, ex);
            }

            yield return decoded;
        }
    }

    private static bool CanHaveBody(FetchRequest request, int status) => status is not (101 or 103 or 204 or 205 or 304 or 301 or 302 or 303 or 307 or 308) && request.Method is not ("HEAD" or "CONNECT");

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

    /// <summary>The permission store of the <c>storage-access</c> permission of a request: the one of its client, or of its navigable.</summary>
    private static PermissionStore? GetStorageAccessPermissions(FetchRequest request)
        => (request.Client?.EnvironmentOptions ?? request.Navigable?.Options ?? request.KeepaliveBrowsingContext?.Options)?.EffectivePermissions;

    /// <summary>
    /// https://privacycg.github.io/storage-access-headers/#request-storage-access-status: the storage access status of a
    /// request whose credentials mode is "include" and whose current URL is potentially trustworthy, or null when the
    /// <c>Sec-Fetch-Storage-Access</c> header is not sent.
    /// </summary>
    private static StorageAccessStatus? GetStorageAccessStatus(FetchRequest request)
    {
        if (request.CredentialsMode != FetchCredentialsMode.Include || !ReferrerPolicies.IsPotentiallyTrustworthy(request.CurrentUrl) || request.IsSpeculative || request.UseIsolatedCredentials)
            return null;

        var cookieContext = request.CookieContext;
        var siteForCookies = cookieContext is null ? request.Client?.SiteForCookies : cookieContext.SiteForCookies;
        var topLevelOrigin = cookieContext is null ? request.Client?.TopLevelOriginForCookies : cookieContext.TopLevelOrigin;
        var cookieJar = cookieContext is null ? request.Client?.CookieJar : cookieContext.CookieJar;
        var origin = GetLoaderOrigin(request);
        var previousUrls = request.RedirectChain is { Count: > 1 } redirectChain ? redirectChain.Take(redirectChain.Count - 1).ToArray() : [];
        var permissions = GetStorageAccessPermissions(request);
        var context = CookieContexts.ForRequest(request.NavigationDestination ?? request.Destination, origin is not null && request.TaintedOrigin ? "null" : origin, siteForCookies, topLevelOrigin, request.Method, request.StorageAccessEligibility, permissions, request.CurrentUrl, previousUrls);
        return StorageAccess.GetStatus(request, siteForCookies is null ? null : context, cookieJar, permissions);
    }

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
