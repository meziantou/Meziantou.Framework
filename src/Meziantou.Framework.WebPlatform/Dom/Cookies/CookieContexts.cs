using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The contexts of the retrievals and the receptions of cookies (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-same-site-and-cross-site-requ):
/// the "site for cookies" of the documents and the same-site status of the requests.
/// </summary>
internal static class CookieContexts
{
    /// <summary>
    /// The "site for cookies" of a document (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-document-based-requests):
    /// the top-level origin when it is same-site with the origins of the document and of its ancestors, an opaque origin
    /// otherwise. Unlike the specification, the origin of a sandboxed document is its opaque origin (not the origin of its
    /// URL), as in the browsers: the requests of a sandboxed iframe are cross-site. The site for cookies of a service worker
    /// is its origin when its storage key is first-party, an opaque origin otherwise.
    /// </summary>
    public static OriginRecord GetSiteForCookies(Document document)
    {
        // A service worker of a partitioned storage key (registered by a third-party context) is cross-site: its requests
        // do not send the SameSite cookies (https://privacycg.github.io/storage-partitioning/)
        if (GetServiceWorkerStorageKey(document) is { } storageKey)
            return storageKey.IsFirstParty ? document.Origin : OriginRecord.CreateOpaque();

        var topOrigin = GetTopLevelOrigin(document);
        for (var current = document; current is not null; current = GetParentDocument(current))
        {
            if (!OriginRecord.IsSameSite(current.Origin, topOrigin))
                return OriginRecord.CreateOpaque();
        }

        return topOrigin;
    }

    /// <summary>
    /// The top-level origin of a document: the origin of the active document of its top-level traversable, or for a service
    /// worker registered by a third-party context, the top-level site of its storage key.
    /// </summary>
    public static OriginRecord GetTopLevelOrigin(Document document)
    {
        // The top-level site of the storage key of a service worker (its partitioned cookies are the ones of that site)
        if (GetServiceWorkerStorageKey(document) is { TopLevelSite: { } topLevelSite })
            return ParseOrigin(topLevelSite);

        var top = document;
        while (GetParentDocument(top) is { } parent)
        {
            top = parent;
        }

        return top.Origin;
    }

    private static Document? GetParentDocument(Document document) => document.BrowsingContext?.Parent?.Document;

    /// <summary>The storage key of the registration of the service worker of a document, or null when the document is not the one of a service worker.</summary>
    private static StorageKey? GetServiceWorkerStorageKey(Document document)
        => document.WorkerGlobal is ServiceWorkerGlobalScope { Agent.ServiceWorker: { } serviceWorker } ? serviceWorker.Registration.StorageKey : null;

    /// <summary>
    /// The context of a "non-HTTP" API of a document (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-non-http-apis),
    /// for <paramref name="url"/>: it is same-site when the site for cookies of the document is same-site with the
    /// top-level origin (it is not opaque). The unpartitioned cookies of a third-party context are the ones of the Storage
    /// Access API when the document has storage access (https://privacycg.github.io/storage-access/#cookies).
    /// </summary>
    public static CookieRequestContext ForScript(Document document, UrlRecord url)
    {
        var crossSite = GetSiteForCookies(document).IsOpaque;
        return new CookieRequestContext(url, IsHttp: false, crossSite ? CookieSameSiteStatus.CrossSite : CookieSameSiteStatus.Strict, GetTopLevelOrigin(document), CrossSiteAncestor: crossSite,
            HasStorageAccess: crossSite && StorageAccess.HasUnpartitionedCookieAccess(document));
    }

    /// <summary>
    /// The context of the cookies of a document for WebDriver (https://w3c.github.io/webdriver/#dfn-associated-cookies): the
    /// cookies of its URL, including the HttpOnly and the SameSite cookies, and the partitioned cookies of its partition.
    /// </summary>
    public static CookieRequestContext ForWebDriver(Document document)
        => new(document.GetCookieUrl(creationUrl: false), IsHttp: true, CookieSameSiteStatus.Strict, GetTopLevelOrigin(document), CrossSiteAncestor: GetSiteForCookies(document).IsOpaque, HasStorageAccess: true);

    /// <summary>
    /// The context of an HTTP request (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-same-site-and-cross-site-requ)
    /// to <paramref name="url"/>, after the redirects of <paramref name="previousUrls"/>:
    /// <list type="bullet">
    /// <item>A top-level navigation is same-site with its URL (its site for cookies): it is strict when the initiator and the URLs of the redirects are same-site with the URL, lax otherwise.</item>
    /// <item>
    /// Another request is cross-site when its site for cookies is opaque or is not same-site with the URL or one of the URLs
    /// of the redirects, or when it is the navigation of an iframe initiated by a document that is not same-site with the URL.
    /// It is strict otherwise.
    /// </item>
    /// <item>A request without client (e.g. <see cref="BrowsingContext.Navigate(string)"/>) is strict.</item>
    /// </list>
    /// The lax requests with an unsafe method (e.g. <c>POST</c>) do not send the Lax cookies.
    /// </summary>
    public static CookieRequestContext ForRequest(DocumentRequest request, UrlRecord url, IReadOnlyList<UrlRecord> previousUrls)
        => ForRequest(request.NavigationDestination ?? request.Destination, request.Origin, request.SiteForCookies, request.TopLevelOrigin, request.Method, request.StorageAccessEligibility, request.StorageAccessPermissions, url, previousUrls);

    /// <inheritdoc cref="ForRequest(DocumentRequest, UrlRecord, IReadOnlyList{UrlRecord})"/>
    /// <param name="destination">The destination of the request (the destination of the navigation of a request that a service worker fetches again).</param>
    /// <param name="origin">The serialized origin of the request, or null when it has no client.</param>
    /// <param name="serializedSiteForCookies">The serialized site for cookies of the client, or null when the request has no client.</param>
    /// <param name="serializedTopLevelOrigin">The serialized top-level origin of the client, or null.</param>
    /// <param name="method">The method of the request.</param>
    /// <param name="eligibility">Whether the request is eligible for storage access.</param>
    /// <param name="permissions">The permission store of the <c>storage-access</c> permission, or null.</param>
    /// <param name="url">The URL of the request.</param>
    /// <param name="previousUrls">The URLs of the redirects that led to <paramref name="url"/>.</param>
    public static CookieRequestContext ForRequest(RequestDestination destination, string? origin, string? serializedSiteForCookies, string? serializedTopLevelOrigin, string method, StorageAccessEligibility eligibility, PermissionStore? permissions, UrlRecord url, IReadOnlyList<UrlRecord> previousUrls)
    {
        var urlOrigin = OriginRecord.FromUrl(url);

        // The request of a navigation that a service worker fetches again is a request of the navigation
        var isTopLevelNavigation = destination == RequestDestination.Document;
        var isNestedNavigation = destination is RequestDestination.Iframe or RequestDestination.Frame or RequestDestination.Embed or RequestDestination.Object;
        var initiator = origin is not null ? ParseOrigin(origin) : null;
        var siteForCookies = isTopLevelNavigation ? urlOrigin : serializedSiteForCookies is { } site ? ParseOrigin(site) : null;
        var topLevelOrigin = isTopLevelNavigation ? urlOrigin : serializedTopLevelOrigin is { } topLevel ? ParseOrigin(topLevel) : null;
        if (siteForCookies is null)
            return new CookieRequestContext(url, IsHttp: true, CookieSameSiteStatus.Strict, topLevelOrigin, isTopLevelNavigation);

        var isSameSiteUrl = !siteForCookies.IsOpaque && OriginRecord.IsSameSite(siteForCookies, urlOrigin);
        var sameSiteUrls = isSameSiteUrl && previousUrls.All(previous => OriginRecord.IsSameSite(siteForCookies, OriginRecord.FromUrl(previous)));
        var sameSiteInitiator = initiator is null || OriginRecord.IsSameSite(initiator, urlOrigin);
        CookieSameSiteStatus status;
        if (isTopLevelNavigation)
        {
            status = sameSiteUrls && sameSiteInitiator ? CookieSameSiteStatus.Strict
                : method is "GET" or "HEAD" or "OPTIONS" or "TRACE" ? CookieSameSiteStatus.Lax : CookieSameSiteStatus.LaxUnsafe;
        }
        else
        {
            // The initiator of a subresource request is the document of its site for cookies (a fetch() whose origin is
            // tainted by a cross-origin redirect is still initiated by the document)
            status = sameSiteUrls && (!isNestedNavigation || sameSiteInitiator) ? CookieSameSiteStatus.Strict : CookieSameSiteStatus.CrossSite;
        }

        // The partitioned cookies of a cross-site request are keyed by the cross-site ancestor bit (CHIPS), and its
        // unpartitioned cookies are the ones of the Storage Access API when it is eligible for storage access
        // (https://privacycg.github.io/storage-access/#cookies)
        var crossSiteAncestor = !isTopLevelNavigation && !isSameSiteUrl;
        var hasStorageAccess = crossSiteAncestor && StorageAccess.HasStorageAccess(eligibility, permissions, topLevelOrigin, url);
        return new CookieRequestContext(url, IsHttp: true, status, topLevelOrigin, isTopLevelNavigation, CrossSiteAncestor: crossSiteAncestor, HasStorageAccess: hasStorageAccess);
    }

    /// <summary>Parses a serialized origin (<c>null</c> is an opaque origin).</summary>
    private static OriginRecord ParseOrigin(string origin)
        => origin != "null" && UrlParser.Parse(origin, baseUrl: null) is { } url ? OriginRecord.FromUrl(url) : OriginRecord.CreateOpaque();
}
