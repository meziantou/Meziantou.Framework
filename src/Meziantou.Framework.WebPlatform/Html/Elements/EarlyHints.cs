using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The preloads of the early hints of a navigation (https://html.spec.whatwg.org/multipage/semantics.html#early-hints): the
/// <c>preload</c> and <c>modulepreload</c> links of the <c>Link</c> headers of the first <c>103</c> (Early Hints) response
/// of the navigation of a top-level browsing context are fetched before the final response is received ("process early hint
/// headers"), and the new document uses them ("commit early hints"): their entries are added to its map of preloaded
/// resources, and their timing is reported to its performance timeline with the <c>early-hints</c> initiator type.
/// </summary>
/// <remarks>
/// As in Chromium, the requests are checked by the content security policies of the <c>103</c> response and use its referrer
/// policy; the preloads are discarded when the navigation is redirected to another origin, when the document has another origin
/// (e.g. an error document) or another embedder policy than the <c>103</c> response. The links that have a <c>media</c>
/// attribute are ignored (there is no document to evaluate it), the <c>imagesrcset</c> attribute is ignored, and the module
/// preloads are fetched as the module scripts of the document do (a module of a worker must have the origin of the document).
/// </remarks>
internal sealed class EarlyHintPreloads
{
    private readonly List<Preload> _preloads = [];
    private readonly BrowsingContext _browsingContext;
    private readonly OriginRecord _origin;
    private readonly EmbedderPolicyValue _embedderPolicy;
    private bool _done;

    private EarlyHintPreloads(BrowsingContext browsingContext, OriginRecord origin, EmbedderPolicyValue embedderPolicy)
    {
        _browsingContext = browsingContext;
        _origin = origin;
        _embedderPolicy = embedderPolicy;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#process-early-hint-headers: starts the preloads of the <c>Link</c>
    /// headers of a <c>103</c> response of the navigation of <paramref name="browsingContext"/>, whose URL is <paramref name="responseUrl"/>.
    /// The requests have no client: they are sent by the loader of the browsing context.
    /// </summary>
    public static EarlyHintPreloads Process(BrowsingContext browsingContext, UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, CookieJar? cookieJar)
    {
        var origin = OriginRecord.FromUrl(responseUrl);
        var preloads = new EarlyHintPreloads(browsingContext, origin, EmbedderPolicy.Obtain(headers, ReferrerPolicies.IsPotentiallyTrustworthy(responseUrl)).Value);

        // The early policy container: the content security policies and the referrer policy of the 103 response
        var policies = ContentSecurityPolicy.ParseResponsePolicies(headers);
        var referrerPolicy = ReferrerPolicies.ParseHeader(headers);
        foreach (var (name, value) in headers)
        {
            if (!InfraAscii.EqualsIgnoreAsciiCase(name, "Link"))
                continue;

            foreach (var link in LinkHeaderParser.Parse(value))
            {
                if (CreateRequest(link, responseUrl, origin, referrerPolicy) is not { } request)
                    continue;

                if (ContentSecurityPolicy.ShouldBlockRequest(policies, origin, request.Url, request.Destination, nonce: request.Nonce))
                    continue;

                request.KeepaliveBrowsingContext = browsingContext;
                request.CookieContext = new FetchCookieContext(cookieJar, origin.Serialize(), origin.Serialize());
                preloads.Start(request);
            }
        }

        return preloads;
    }

    /// <summary>The request of a preload of an early hint (https://html.spec.whatwg.org/multipage/semantics.html#process-early-hint-headers, step 5), or null.</summary>
    private static FetchRequest? CreateRequest(LinkHeaderValue link, UrlRecord responseUrl, OriginRecord origin, string referrerPolicy)
    {
        var (isPreload, isModulePreload) = LinkPreload.GetPreloadRelations(link);
        if (!isPreload && !isModulePreload)
            return null;

        if (link.GetParameter("media") is not null)
            return null;

        var crossOrigin = link.GetParameter("crossorigin");
        var useCredentials = crossOrigin is not null && InfraAscii.EqualsIgnoreAsciiCase(crossOrigin, "use-credentials");
        RequestDestination destination;
        bool cors;
        var isWorkerModule = false;
        if (isPreload)
        {
            if (LinkPreload.TranslatePreloadDestination(link.GetParameter("as")) is not { } preloadDestination || !LinkPreload.TypeMatchesDestination(link.GetParameter("type") ?? "", preloadDestination))
                return null;

            destination = preloadDestination;
            cors = crossOrigin is not null;
        }
        else
        {
            var (moduleDestination, moduleType) = LinkPreload.TranslateModulePreloadDestination(link.GetParameter("as"));
            if (moduleType is null)
                return null;

            // The module of a worker is fetched as the module script that the document imports
            isWorkerModule = moduleDestination is RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker;
            destination = isWorkerModule ? RequestDestination.Script : moduleDestination;
            cors = true;
        }

        if (link.Target.Length == 0 || UrlParser.Parse(link.Target, responseUrl) is not { Scheme: "http" or "https" } url)
            return null;

        if (isWorkerModule && !OriginRecord.IsSameOrigin(OriginRecord.FromUrl(url), origin))
            return null;

        var linkReferrerPolicy = InfraAscii.ToAsciiLowercase(link.GetParameter("referrerpolicy") ?? "");
        return new FetchRequest(url)
        {
            Origin = origin.Serialize(),
            Destination = destination,
            Mode = cors ? RequestMode.Cors : RequestMode.NoCors,
            CredentialsMode = cors && !useCredentials ? FetchCredentialsMode.SameOrigin : FetchCredentialsMode.Include,
            Referrer = responseUrl.Serialize(),
            ReferrerPolicy = ReferrerPolicies.IsValid(linkReferrerPolicy) ? linkReferrerPolicy : referrerPolicy,
            Integrity = link.GetParameter("integrity") ?? "",
            Nonce = link.GetParameter("nonce") ?? "",
            ServiceWorkersMode = ServiceWorkersMode.None,
        };
    }

    private void Start(FetchRequest request)
    {
        var preload = new Preload(request);
        _preloads.Add(preload);
        Fetching.Fetch(request, response => Complete(preload, response), _browsingContext.EventLoop.QueueTask);
    }

    private void Complete(Preload preload, FetchResponse response)
    {
        preload.Response = response;
        preload.EndTimestamp = _browsingContext.EventLoop.TimeProvider.GetTimestamp();

        // The timing is reported before the fetch that consumed the preload gets its response
        if (preload.Timing is { } timing && preload.Request.IsSentToLoader)
        {
            timing.Report(response, preload.EndTimestamp);
        }

        preload.Entry.SetResponse(response);
    }

    /// <summary>
    /// The "commitEarlyHints" steps (https://html.spec.whatwg.org/multipage/semantics.html#process-early-hint-headers, step 6),
    /// once the document of the navigation is created: the preloads are added to its map of preloaded resources, where its
    /// fetches consume them, and their timing to its performance timeline (once their response is received).
    /// </summary>
    public void Commit(Document document)
    {
        if (_done)
            return;

        _done = true;
        if (!OriginRecord.IsSameOrigin(document.Origin, _origin) || document.EmbedderPolicy.Value != _embedderPolicy)
            return;

        foreach (var preload in _preloads)
        {
            document.SetPreloadedResource(Document.GetPreloadKey(preload.Request), preload.Entry);
            var timing = ResourceTiming.Start(document, preload.Request, "early-hints");
            if (preload.Response is { } response)
            {
                if (preload.Request.IsSentToLoader)
                {
                    timing?.Report(response, preload.EndTimestamp);
                }
            }
            else
            {
                preload.Timing = timing;
            }
        }
    }

    /// <summary>The navigation is redirected to another origin: the preloads are discarded (the document does not use them).</summary>
    public void Discard() => _done = true;

    private sealed class Preload(FetchRequest request)
    {
        public FetchRequest Request => request;

        public PreloadEntry Entry { get; } = new(request.Integrity);

        /// <summary>The response of the preload, or null while it is being fetched.</summary>
        public FetchResponse? Response { get; set; }

        /// <summary>The time the response of the preload was received.</summary>
        public long EndTimestamp { get; set; }

        /// <summary>The timing of the preload in the performance timeline of the document that committed it before its response was received, or null.</summary>
        public ResourceTiming? Timing { get; set; }
    }
}
