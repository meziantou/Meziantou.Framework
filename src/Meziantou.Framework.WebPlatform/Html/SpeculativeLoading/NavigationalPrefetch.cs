using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-state</summary>
internal enum PrefetchState
{
    Ongoing,
    Completed,
    Canceled,
}

/// <summary>
/// A prefetch record (https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record): a navigational prefetch started
/// by the speculation rules of a document, whose response is used by a navigation of the document to its URL. A prerender
/// record (<see cref="IsPrerender"/>) loads the document in a prerendering navigable instead
/// (https://wicg.github.io/nav-speculation/prerendering.html#prerender-record).
/// </summary>
internal sealed class PrefetchRecord
{
    private List<Action>? _waiters;

    public PrefetchRecord(SpeculativeLoadCandidate candidate, List<string?> tags)
    {
        Url = candidate.Url;
        NoVarySearchHint = candidate.Rule.NoVarySearchHint;
        ReferrerPolicy = candidate.ReferrerPolicy;
        RequiresAnonymousClientIp = candidate.Rule.RequiresAnonymousClientIp;
        IsPrerender = candidate.IsPrerender;
        TargetHint = candidate.Rule.TargetHint;
        Tags = tags;
    }

    public UrlRecord Url { get; }

    public UrlSearchVariance NoVarySearchHint { get; }

    /// <summary>The referrer policy of the candidate (an empty string for the policy of the document).</summary>
    public string ReferrerPolicy { get; }

    /// <summary>Whether the anonymization policy is not null: the cross-origin requests must hide the IP address of the client.</summary>
    public bool RequiresAnonymousClientIp { get; }

    public List<string?> Tags { get; }

    public bool IsPrerender { get; }

    public string? TargetHint { get; }

    public PrefetchState State { get; private set; }

    /// <summary>The request of the prefetch, whose URL list has the URLs of the redirects.</summary>
    public FetchRequest? Request { get; set; }

    /// <summary>The response of the last exchange of the redirect chain, once the prefetch is completed.</summary>
    public FetchResponse? Response { get; set; }

#pragma warning disable CA2213 // The cancellation is not disposed, as the loader may still use its token
    public CancellationTokenSource Cancellation { get; } = new();
#pragma warning restore CA2213

    /// <summary>The time the prefetch started.</summary>
    public long StartTime { get; set; }

    /// <summary>The time after which a completed prefetch is not used (https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-expiry-time).</summary>
    public long ExpiryTime { get; set; }

    /// <summary>The time the response was received.</summary>
    public long ResponseTime { get; set; }

    /// <summary>The redirects of the prefetch: their URL, the time their response was received and their <c>Timing-Allow-Origin</c> values.</summary>
    public List<(UrlRecord Url, long Time, List<string> TimingAllowOrigin)> Redirects { get; } = [];

    /// <summary>
    /// The cookies of the isolated partition of a cross-site prefetch (https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-isolated-partition-key),
    /// which are copied to the cookies of the navigable when the prefetch is used, or null when no request was isolated.
    /// </summary>
    public CookieJar? IsolatedCookieJar { get; set; }

    /// <summary>Whether a service worker controlled a URL of the redirect chain when it was requested.</summary>
    public bool ServiceWorkerMatched { get; set; }

    /// <summary>The prerendering navigable of a prerender record.</summary>
    public BrowsingContext? PrerenderingNavigable { get; set; }

    /// <summary>https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-still-being-speculated</summary>
    public bool IsStillBeingSpeculated(SpeculativeLoadCandidate candidate)
        => candidate.IsPrerender == IsPrerender && BrowsingContext.UrlEquals(candidate.Url, Url, excludeFragments: false)
            && candidate.Rule.RequiresAnonymousClientIp == RequiresAnonymousClientIp && candidate.Rule.NoVarySearchHint.Equals(NoVarySearchHint)
            && candidate.ReferrerPolicy == ReferrerPolicy;

    /// <summary>https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-matches-a-url</summary>
    public bool Matches(UrlRecord url)
    {
        if (BrowsingContext.UrlEquals(url, Url, excludeFragments: true))
            return true;

        return ResponseSearchVariance is { } variance && variance.AreEquivalent(Url, url);
    }

    /// <summary>https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-is-expected-to-match-a-url</summary>
    public bool IsExpectedToMatch(UrlRecord url) => Matches(url) || (ResponseSearchVariance is null && NoVarySearchHint.AreEquivalent(Url, url));

    /// <summary>
    /// The URL search variance of the response (its <c>No-Vary-Search</c> header), once it is received: the response of the
    /// prefetch, or the response of the document of the prerendering traversable.
    /// </summary>
    public UrlSearchVariance? ResponseSearchVariance { get; set; }

    /// <summary>Runs <paramref name="callback"/> when the state of the record changes.</summary>
    public void WhenStateChanges(Action callback) => (_waiters ??= []).Add(callback);

    public void SetState(PrefetchState state)
    {
        if (State == state)
            return;

        State = state;
        var waiters = _waiters;
        _waiters = null;
        if (waiters is not null)
        {
            foreach (var waiter in waiters)
            {
                waiter();
            }
        }
    }
}

/// <summary>
/// The navigational prefetches of the speculation rules (https://wicg.github.io/nav-speculation/prefetch.html): the fetch of
/// a prefetch record, and the use of its response by a navigation.
/// </summary>
internal static class NavigationalPrefetch
{
    /// <summary>The lifetime of a completed prefetch (5 minutes).</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>The maximum number of prefetch records of a document (the "immediate" candidates beyond the limit are not enacted).</summary>
    private const int MaxRecords = 50;

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prefetch.html#list-of-sufficiently-strict-speculative-navigation-referrer-policies:
    /// the referrer policies that a cross-site prefetch may use.
    /// </summary>
    private static bool IsSufficientlyStrict(string referrerPolicy) => referrerPolicy is "" or "strict-origin-when-cross-origin" or "strict-origin" or "same-origin" or "no-referrer";

    /// <summary>
    /// Whether the documents of the navigable of <paramref name="document"/> can be replaced by a prerendered document:
    /// only the auxiliary browsing contexts, which the application does not hold (see <see cref="Prerendering"/>).
    /// </summary>
    public static bool CanPrerender(Document document)
        => document.BrowsingContext is { Parent: null, Options: { } options } navigable && options.SpeculativeLoading.PrerenderEnabled && Prerendering.CanActivateIn(navigable);

    /// <summary>https://wicg.github.io/nav-speculation/prefetch.html#start-a-referrer-initiated-navigational-prefetch</summary>
    public static void Start(Document document, PrefetchRecord record)
    {
        if (document.BrowsingContext is not { Parent: null, Options: { DocumentLoader: not null } options } navigable || !options.SpeculativeLoading.PrefetchEnabled)
            return;

        // Only the potentially trustworthy HTTP(S) URLs are prefetched (a same-site http: URL can be prerendered, as in Chromium)
        var url = record.Url;
        if (url.Scheme is not ("http" or "https") || (!record.IsPrerender && !ReferrerPolicies.IsPotentiallyTrustworthy(url)))
            return;

        // A prefetch record of the same URL is already ongoing or completed
        if (document.PrefetchRecords is { } records)
        {
            if (records.Count >= MaxRecords)
                return;

            foreach (var existing in records)
            {
                if (existing.IsPrerender == record.IsPrerender && existing.NoVarySearchHint.Equals(record.NoVarySearchHint) && record.NoVarySearchHint.AreEquivalent(existing.Url, url)
                    && (existing.State == PrefetchState.Ongoing || !IsExpired(existing, navigable)))
                {
                    return;
                }
            }
        }

        var referrerPolicy = ReferrerPolicies.GetRequestPolicy(record.ReferrerPolicy, document);
        if (IsCrossSite(document, url) && !IsSufficientlyStrict(record.ReferrerPolicy.Length == 0 ? document.ReferrerPolicy : record.ReferrerPolicy))
            return;

        if (record.RequiresAnonymousClientIp && !IsSameOrigin(document, url) && !options.SpeculativeLoading.AnonymousClientIpSupported)
            return;

        if (record.IsPrerender)
        {
            Prerendering.Start(document, record);
            return;
        }

        // A link to the document itself with a fragment is a fragment navigation: nothing is fetched
        if (url.Fragment is not null && BrowsingContext.UrlEquals(url, document.UrlRecord, excludeFragments: true))
            return;

        // A cross-site prefetch is not sent when the user agent has credentials for the URL: its response would differ from
        // the one of the navigation
        if (HasConflictingCredentials(document, navigable, url) || IsIsolatedUrlWithServiceWorker(document, navigable, url))
            return;

        document.AddPrefetchRecord(record);
        record.StartTime = navigable.EventLoop.TimeProvider.GetTimestamp();
        var request = new FetchRequest(url)
        {
            Client = document,
            Navigable = navigable,
            Origin = document.SerializeOrigin(),
            ReferrerPolicy = referrerPolicy,
            Destination = RequestDestination.Document,
            Mode = RequestMode.Navigate,
            CredentialsMode = FetchCredentialsMode.Include,
            UseUrlCredentials = true,
            RedirectMode = FetchRedirectMode.Manual,
            IsPrefetch = true,
            ReservedClient = new ReservedClient(url.SerializeOrigin()),
        };
        record.Request = request;
        PrepareExchange(document, navigable, record, request, url);
        Fetching.WhenCompleted(Fetching.StartFetch(request, record.Cancellation.Token), response => ProcessResponse(document, navigable, record, request, response), navigable.QueueTask);
    }

    /// <summary>
    /// Sets what depends on the URL of an exchange of the redirect chain: the cookies of the request (the ones of the navigable
    /// for a same-site URL, else the ones of the isolated partition of the record), and the <c>Sec-Purpose</c> header.
    /// </summary>
    private static void PrepareExchange(Document document, BrowsingContext navigable, PrefetchRecord record, FetchRequest request, UrlRecord url)
    {
        var isCrossSite = IsCrossSite(document, url);
        if (isCrossSite)
        {
            // The isolated partition has no service worker
            record.IsolatedCookieJar ??= navigable.Options.CookieJar is null ? null : new CookieJar(navigable.EventLoop.TimeProvider);
            request.CookieContext = new FetchCookieContext(record.IsolatedCookieJar, SiteForCookies: null, TopLevelOrigin: null);
            request.UseIsolatedCredentials = true;
            request.ServiceWorkersMode = ServiceWorkersMode.None;
        }
        else
        {
            request.CookieContext = new FetchCookieContext(navigable.GetCookieJar(credentialless: false), SiteForCookies: null, TopLevelOrigin: null);
            request.UseIsolatedCredentials = false;
            request.ServiceWorkersMode = ServiceWorkersMode.All;
        }

        // The headers of the speculative navigation request are headers of the request (as in Chromium, a service worker sees
        // them, and they are sent again by fetch(event.request)): Sec-Purpose, and the tags of a same-site request
        // (https://wicg.github.io/nav-speculation/prefetch.html#sec-speculation-tags-header)
        request.SecPurpose = record.RequiresAnonymousClientIp && !IsSameOrigin(document, url) ? "prefetch;anonymous-client-ip" : "prefetch";
        request.HeaderList.RemoveAll(static header => header.Key is "Sec-Purpose" or "Sec-Speculation-Tags");
        request.HeaderList.Add(new("Sec-Purpose", request.SecPurpose));
        if (!isCrossSite)
        {
            request.HeaderList.Add(new("Sec-Speculation-Tags", SerializeTags(record.Tags)));
        }

        record.ServiceWorkerMatched |= ServiceWorkerFetchHandling.HasActiveWorker(navigable, url);
    }

    private static void ProcessResponse(Document document, BrowsingContext navigable, PrefetchRecord record, FetchRequest request, FetchResponse response)
    {
        if (record.State != PrefetchState.Ongoing)
            return;

        if (response.IsNetworkError || navigable.IsDestroyed)
        {
            CancelAndDiscard(document, record);
            return;
        }

        var internalResponse = response.Internal;
        var now = navigable.EventLoop.TimeProvider.GetTimestamp();
        if (internalResponse.Status is 301 or 302 or 303 or 307 or 308)
        {
            var locationUrl = internalResponse.GetLocationUrl(request.CurrentUrl.Fragment, out var failure);
            if (failure || locationUrl is not { Scheme: "http" or "https" } || !ReferrerPolicies.IsPotentiallyTrustworthy(locationUrl)
                || (IsCrossSite(document, locationUrl) && !IsSufficientlyStrict(record.ReferrerPolicy.Length == 0 ? document.ReferrerPolicy : record.ReferrerPolicy))
                || HasConflictingCredentials(document, navigable, locationUrl)
                // As in Chromium, a prefetch fails when it is redirected from or to a URL that a service worker controls
                || record.ServiceWorkerMatched || ServiceWorkerFetchHandling.HasActiveWorker(navigable, locationUrl)
                || (document.ContentSecurityPoliciesOrNull is not null && ContentSecurityPolicy.ShouldBlockNavigationRequest(document, request.Url, locationUrl, isFormSubmission: false, request.RedirectCount + 1)))
            {
                CancelAndDiscard(document, record);
                return;
            }

            if (request.ReservedClient is { } reservedClient && reservedClient.Origin != locationUrl.SerializeOrigin())
            {
                request.ReservedClient = new ReservedClient(locationUrl.SerializeOrigin());
            }

            record.Redirects.Add((request.CurrentUrl, now, HeaderList.GetDecodeAndSplit(internalResponse.HeaderList, "Timing-Allow-Origin") ?? []));
            PrepareExchange(document, navigable, record, request, locationUrl);
            Fetching.WhenCompleted(Fetching.ProcessNextManualRedirectAsync(request, response, record.Cancellation.Token), next => ProcessResponse(document, navigable, record, request, next), static task => task());
            return;
        }

        // Only the successful responses are kept (https://wicg.github.io/nav-speculation/prefetch.html#create-navigation-params-by-fetching)
        if (internalResponse.Status is < 200 or > 299)
        {
            CancelAndDiscard(document, record);
            return;
        }

        record.Response = response;
        record.ResponseSearchVariance = UrlSearchVariance.Obtain(internalResponse.HeaderList);
        record.ResponseTime = now;
        record.ExpiryTime = now + (long)(Lifetime.TotalSeconds * navigable.EventLoop.TimeProvider.TimestampFrequency);
        record.SetState(PrefetchState.Completed);
    }

    /// <summary>https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-cancel-and-discard</summary>
    public static void CancelAndDiscard(Document document, PrefetchRecord record)
    {
        document.PrefetchRecords?.Remove(record);
        if (record.State == PrefetchState.Ongoing)
        {
            record.Cancellation.Cancel();
        }

        if (record.PrerenderingNavigable is { } prerenderingNavigable)
        {
            record.PrerenderingNavigable = null;
            Prerendering.Cancel(prerenderingNavigable);
        }

        record.SetState(PrefetchState.Canceled);
    }

    /// <summary>
    /// The <c>Clear-Site-Data</c> header of a response of a navigation (https://w3c.github.io/webappsec-clear-site-data/), for
    /// the speculative loads: the <c>"prefetchCache"</c> and <c>"prerenderCache"</c> types (and <c>"cache"</c> and <c>"*"</c>,
    /// which include them) discard the prefetch records and the prerender records of the documents of the origin of the
    /// response (https://wicg.github.io/nav-speculation/prefetch.html#clear-site-data-patches). The other types are not
    /// implemented.
    /// </summary>
    public static void ProcessClearSiteData(BrowsingContext navigable, UrlRecord url, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (HeaderList.Get(headers, "Clear-Site-Data") is not { } value || !ReferrerPolicies.IsPotentiallyTrustworthy(url))
            return;

        var prefetch = false;
        var prerender = false;
        foreach (var type in value.Split(','))
        {
            switch (type.Trim(' ', '\t'))
            {
                case "\"*\"" or "\"cache\"":
                    prefetch = prerender = true;
                    break;
                case "\"prefetchCache\"":
                    prefetch = true;
                    break;
                case "\"prerenderCache\"":
                    prerender = true;
                    break;
            }
        }

        if (!prefetch && !prerender)
            return;

        var origin = OriginRecord.FromUrl(url);
        foreach (var topLevel in navigable.GetGroupTopLevelBrowsingContexts().ToArray())
        {
            if (topLevel.IsDestroyed)
                continue;

            foreach (var descendant in topLevel.GetInclusiveDescendantNavigables().ToArray())
            {
                var document = descendant.Document;
                if (document.PrefetchRecords is not { Count: > 0 } records || !OriginRecord.IsSameOrigin(document.Origin, origin))
                    continue;

                foreach (var record in records.ToArray())
                {
                    if (record.IsPrerender ? prerender : prefetch)
                    {
                        CancelAndDiscard(document, record);
                    }
                }
            }
        }
    }

    /// <summary>Cancels and discards the prefetch records of a document that is destroyed.</summary>
    public static void DiscardAll(Document document)
    {
        if (document.PrefetchRecords is not { Count: > 0 } records)
            return;

        foreach (var record in records.ToArray())
        {
            CancelAndDiscard(document, record);
        }
    }

    // A prerender record has no expiry time: its prerendering traversable lives until the record is canceled or activated
    private static bool IsExpired(PrefetchRecord record, BrowsingContext navigable)
        => record.State == PrefetchState.Completed && !record.IsPrerender && navigable.EventLoop.TimeProvider.GetTimestamp() > record.ExpiryTime;

    private static bool IsCrossSite(Document document, UrlRecord url) => !OriginRecord.IsSameSite(document.Origin, OriginRecord.FromUrl(url));

    private static bool IsSameOrigin(Document document, UrlRecord url) => OriginRecord.IsSameOrigin(document.Origin, OriginRecord.FromUrl(url));

    /// <summary>
    /// Whether the user agent has cookies for a cross-site URL (https://wicg.github.io/nav-speculation/prefetch.html#prefetch-record-had-conflicting-credentials):
    /// the prefetch, which is sent without them, would not get the response of the navigation.
    /// </summary>
    private static bool HasConflictingCredentials(Document document, BrowsingContext navigable, UrlRecord url)
        => IsCrossSite(document, url) && navigable.GetCookieJar(credentialless: false) is { } cookieJar && cookieJar.GetCookies(url.Serialize()).Count > 0;

    /// <summary>
    /// Whether a service worker controls a cross-site URL: as in Chromium, it is not prefetched (the isolated partition of the
    /// prefetch has no service worker, so its response would not be the one of the navigation).
    /// </summary>
    private static bool IsIsolatedUrlWithServiceWorker(Document document, BrowsingContext navigable, UrlRecord url)
        => IsCrossSite(document, url) && ServiceWorkerFetchHandling.HasActiveWorker(navigable, url);

    /// <summary>
    /// The value of the <c>Sec-Speculation-Tags</c> header (https://html.spec.whatwg.org/multipage/speculative-loading.html#the-sec-speculation-tags-header):
    /// a structured field list of strings, where the null tag is the <c>null</c> token.
    /// </summary>
    internal static string SerializeTags(List<string?> tags)
    {
        var builder = new StringBuilder();
        foreach (var tag in tags)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            if (tag is null)
            {
                builder.Append("null");
            }
            else
            {
                builder.Append('"').Append(tag.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prefetch.html#wait-for-a-matching-prefetch-record: finds the prefetch record of
    /// <paramref name="sourceDocument"/> that a navigation to <paramref name="url"/> uses. Returns false when there is none.
    /// Otherwise <paramref name="continuation"/> is called with the completed record, or with null when the ongoing prefetches
    /// that were expected to match failed: at once, or when the prefetches complete.
    /// </summary>
    public static bool WaitForMatchingRecord(BrowsingContext navigable, Document? sourceDocument, UrlRecord url, Action<PrefetchRecord?> continuation)
    {
        if (sourceDocument?.PrefetchRecords is not { Count: > 0 } records || url.Scheme is not ("http" or "https"))
            return false;

        PrefetchRecord? ongoing = null;
        foreach (var record in records.ToArray())
        {
            if (record.IsPrerender)
                continue;

            if (record.State == PrefetchState.Completed)
            {
                if (IsExpired(record, navigable))
                {
                    CancelAndDiscard(sourceDocument, record);
                }
                else if (record.Matches(url) && record.Response is not null && record.ServiceWorkerMatched != ServiceWorkerFetchHandling.HasActiveWorker(navigable, record.Request?.CurrentUrl ?? record.Url))
                {
                    // As in Chromium, a prefetch is not used when a service worker started or stopped controlling its URL
                    // since: the response of the navigation would come from another source. The URL is the one of the request
                    // (after its redirects): the response of a service worker may have another URL (e.g. fetch() of a modified URL)
                    CancelAndDiscard(sourceDocument, record);
                }
                else if (record.Matches(url))
                {
                    // The record is used once
                    records.Remove(record);
                    continuation(record);
                    return true;
                }
            }
            else if (record.State == PrefetchState.Ongoing && record.IsExpectedToMatch(url))
            {
                ongoing ??= record;
            }
        }

        if (ongoing is null)
            return false;

        ongoing.WhenStateChanges(() =>
        {
            if (!WaitForMatchingRecord(navigable, sourceDocument, url, continuation))
            {
                continuation(null);
            }
        });
        return true;
    }

    /// <summary>
    /// The navigation of <paramref name="navigable"/> uses the response of a prefetch record
    /// (https://wicg.github.io/nav-speculation/prefetch.html#create-navigation-params-from-a-prefetch-record): the cookies of
    /// its isolated partition are copied to the cookies of the navigable (https://wicg.github.io/nav-speculation/prefetch.html#copy-prefetch-cookies),
    /// and the timing of the navigation gets the redirects and the response of the prefetch, which are not earlier than the
    /// start of the navigation.
    /// </summary>
    public static void Activate(PrefetchRecord record, BrowsingContext navigable, DocumentLoadTimingInfo? timing)
    {
        if (record.IsolatedCookieJar is { } isolatedCookieJar && navigable.GetCookieJar(credentialless: false) is { } cookieJar)
        {
            cookieJar.CopyFrom(isolatedCookieJar);
            record.IsolatedCookieJar = null;
        }

        if (timing is null)
            return;

        timing.DeliveryType = "navigational-prefetch";
        foreach (var (redirectUrl, time, timingAllowOrigin) in record.Redirects)
        {
            if (timing.RedirectStart == 0)
            {
                timing.RedirectStart = timing.FetchStart;
            }

            timing.RedirectCount++;
            timing.Redirects.Add((redirectUrl, timingAllowOrigin));
            timing.RedirectEnd = timing.FetchStart = Math.Max(timing.FetchStart, time);
        }

        timing.PrefetchResponseStart = Math.Max(timing.FetchStart, record.ResponseTime);
    }
}
