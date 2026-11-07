using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class BrowsingContext
{
    private AcceptCHCache? _acceptCHCache;

    // The origins whose critical client hints restarted the ongoing navigation
    private int _criticalClientHintsNavigation;
    private HashSet<string>? _criticalClientHintsRestarts;

    /// <summary>
    /// The Accept-CH cache of the browsing context group (https://wicg.github.io/client-hints-infrastructure/#accept-ch-cache):
    /// the one of the options (see <see cref="BrowsingContextOptions.AcceptCHCache"/>), else the cache of the browsing context
    /// created by the application.
    /// </summary>
    /// <remarks>
    /// A prerendering traversable has its own cache over the one of the group: the hints that its documents opt into are not
    /// sent to the other pages until it is activated (see <see cref="AcceptCHCache.Fallback"/>).
    /// </remarks>
    internal AcceptCHCache AcceptCHCache
    {
        get
        {
            var top = Top;
            if (top.IsPrerendering)
                return top._prerenderingAcceptCHCache ??= new AcceptCHCache { Fallback = top.GroupAcceptCHCache };

            return GroupAcceptCHCache;
        }
    }

    private AcceptCHCache? _prerenderingAcceptCHCache;

    private AcceptCHCache GroupAcceptCHCache => Options.AcceptCHCache ?? (ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _acceptCHCache) : GroupOwner.GroupAcceptCHCache);

    /// <summary>The client hints that the documents of a prerendering traversable opted into are stored in the cache of the group when the page is activated.</summary>
    private void CommitPrerenderingAcceptCHCache()
    {
        var cache = _prerenderingAcceptCHCache;
        _prerenderingAcceptCHCache = null;
        cache?.CopyTo(GroupAcceptCHCache);
    }

    /// <summary>
    /// The client hints of a response of a navigation (https://wicg.github.io/client-hints-infrastructure/#navigation-response):
    /// the <c>Accept-CH</c> header of a response of a top-level navigation whose URL is potentially trustworthy creates or
    /// overrides the entry of the origin of the URL in the Accept-CH cache
    /// (https://wicg.github.io/client-hints-infrastructure/#abstract-opdef-create-or-override-the-cached-client-hints-set),
    /// then the <c>Clear-Site-Data</c> header clears it (see <see cref="ClientHints.ProcessClearSiteData"/>). The cache is
    /// only written by the top-level documents, so it is partitioned: the response of a child navigable only clears the
    /// entry of its origin when it is the origin of the top-level document.
    /// </summary>
    /// <returns>
    /// Whether the user agent should restart the navigation for the critical client hints
    /// (https://wicg.github.io/client-hints-infrastructure/#should-restart-page-for-critical-client-hints): a hint of the
    /// <c>Critical-CH</c> header is in the <c>Accept-CH</c> header, and was not in the cache (the request did not have it).
    /// </returns>
    private bool ApplyNavigationResponseClientHints(UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (!ClientHints.IsEnabled(Options) || responseUrl.Scheme is not ("http" or "https") || !ReferrerPolicies.IsPotentiallyTrustworthy(responseUrl))
            return false;

        var cache = AcceptCHCache;
        var origin = responseUrl.SerializeOrigin();
        if (Parent is not null)
        {
            if (Top.Document.Origin is { IsOpaque: false } topOrigin && topOrigin.Serialize() == origin)
            {
                ClientHints.ProcessClearSiteData(cache, responseUrl, headers);
            }

            return false;
        }

        // The documents of a sandboxed navigable have an opaque origin: they do not opt into client hints
        var missesCriticalHints = false;
        if (!HasSandboxedOriginFlag && ClientHints.ParseHeader(headers, "Accept-CH") is { } acceptedHints)
        {
            if (ClientHints.ParseHeader(headers, "Critical-CH") is { } criticalHints)
            {
                missesCriticalHints = acceptedHints.Except(cache.Get(origin)).Overlaps(criticalHints);
            }

            cache.Set(origin, acceptedHints);
        }

        ClientHints.ProcessClearSiteData(cache, responseUrl, headers);
        return missesCriticalHints && !cache.Get(origin).IsEmpty;
    }

    /// <summary>
    /// Processes the client hints of a response of the fetch of a navigation, including the redirects (see
    /// <see cref="ApplyNavigationResponseClientHints"/>), and restarts the navigation, before any redirect, when the response
    /// asks for critical client hints that the request did not have (the <c>Critical-CH</c> header): the new requests have
    /// the hints that the response opted into. The time of the restart is the <c>criticalCHRestart</c> of the navigation
    /// timing entry of the document.
    /// </summary>
    /// <remarks>
    /// As in Chromium, a navigation is restarted once per origin of its redirect chain at most (the specification restarts a
    /// navigable once), and only when the method of its request is <c>GET</c>.
    /// </remarks>
    /// <returns>Whether the navigation was restarted: the response is ignored.</returns>
    private bool ProcessNavigationResponseClientHints(int navigationId, UrlRecord url, FetchRequest request, DocumentParameters parameters, FetchResponse response, CancellationToken cancellationToken)
    {
        request.NavigationInitialMethod ??= request.Method;
        if (response.IsNetworkError)
            return false;

        var internalResponse = response.Internal;
        var responseUrl = internalResponse.Url ?? request.CurrentUrl;
        if (!ApplyNavigationResponseClientHints(responseUrl, internalResponse.HeaderList) || request.NavigationInitialMethod != "GET")
            return false;

        if (_criticalClientHintsNavigation != navigationId)
        {
            _criticalClientHintsNavigation = navigationId;
            _criticalClientHintsRestarts = null;
        }

        if (!(_criticalClientHintsRestarts ??= new HashSet<string>(StringComparer.Ordinal)).Add(responseUrl.SerializeOrigin()))
            return false;

        // The timing of the redirects is the one of the restarted navigation
        if (parameters.Timing is { } timing)
        {
            timing.CriticalCHRestart = EventLoop.TimeProvider.GetTimestamp();
            timing.RedirectStart = 0;
            timing.RedirectEnd = 0;
            timing.RedirectCount = 0;
            timing.Redirects.Clear();
        }

        var restartedRequest = new FetchRequest(url)
        {
            Client = request.Client,
            Navigable = this,
            Origin = request.Origin,
            ReferrerPolicy = parameters.RequestReferrerPolicy,
            Destination = request.Destination,
            Mode = RequestMode.Navigate,
            CredentialsMode = request.CredentialsMode,
            UseUrlCredentials = request.UseUrlCredentials,
            RedirectMode = FetchRedirectMode.Manual,
            UserActivation = request.UserActivation,
            IsFormSubmission = request.IsFormSubmission,
            CookieContext = request.CookieContext,
            ReservedClient = new ReservedClient(url.SerializeOrigin()),
            ReplacesClientId = request.ReplacesClientId,
            IsReloadNavigation = request.IsReloadNavigation,
            IsHistoryNavigation = request.IsHistoryNavigation,
            NavigationInitialMethod = "GET",
        };
        PrepareRequestOfPrerenderingNavigable(restartedRequest);
        parameters.ReservedClient = restartedRequest.ReservedClient;
        if (parameters.Timing is { } restartedTiming)
        {
            restartedTiming.FetchStart = EventLoop.TimeProvider.GetTimestamp();
        }

        Fetching.WhenCompleted(Fetching.StartFetch(restartedRequest, cancellationToken), next => ProcessNavigationResponse(navigationId, url, restartedRequest, parameters, next, cancellationToken), QueueTask);
        return true;
    }
}
