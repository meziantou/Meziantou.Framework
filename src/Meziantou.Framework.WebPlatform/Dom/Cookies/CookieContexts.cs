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
    /// URL), as in the browsers: the requests of a sandboxed iframe are cross-site.
    /// </summary>
    public static OriginRecord GetSiteForCookies(Document document)
    {
        var topOrigin = GetTopLevelOrigin(document);
        for (var current = document; current is not null; current = GetParentDocument(current))
        {
            if (!OriginRecord.IsSameSite(current.Origin, topOrigin))
                return OriginRecord.CreateOpaque();
        }

        return topOrigin;
    }

    /// <summary>The top-level origin of a document: the origin of the active document of its top-level traversable.</summary>
    public static OriginRecord GetTopLevelOrigin(Document document)
    {
        var top = document;
        while (GetParentDocument(top) is { } parent)
        {
            top = parent;
        }

        return top.Origin;
    }

    private static Document? GetParentDocument(Document document) => document.BrowsingContext?.Parent?.Document;

    /// <summary>
    /// The context of a "non-HTTP" API of a document (https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-non-http-apis),
    /// for <paramref name="url"/>: it is same-site when the site for cookies of the document is same-site with the
    /// top-level origin (it is not opaque).
    /// </summary>
    public static CookieRequestContext ForScript(Document document, UrlRecord url)
    {
        var crossSite = GetSiteForCookies(document).IsOpaque;
        return new CookieRequestContext(url, IsHttp: false, crossSite ? CookieSameSiteStatus.CrossSite : CookieSameSiteStatus.Strict, GetTopLevelOrigin(document), CrossSiteAncestor: crossSite);
    }

    /// <summary>
    /// The context of the cookies of a document for WebDriver (https://w3c.github.io/webdriver/#dfn-associated-cookies): the
    /// cookies of its URL, including the HttpOnly and the SameSite cookies, and the partitioned cookies of its partition.
    /// </summary>
    public static CookieRequestContext ForWebDriver(Document document)
        => new(document.GetCookieUrl(creationUrl: false), IsHttp: true, CookieSameSiteStatus.Strict, GetTopLevelOrigin(document), CrossSiteAncestor: GetSiteForCookies(document).IsOpaque);

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
    {
        var urlOrigin = OriginRecord.FromUrl(url);
        // The request of a navigation that a service worker fetches again is a request of the navigation
        var destination = request.NavigationDestination ?? request.Destination;
        var isTopLevelNavigation = destination == RequestDestination.Document;
        var isNestedNavigation = destination is RequestDestination.Iframe or RequestDestination.Frame or RequestDestination.Embed or RequestDestination.Object;
        var initiator = request.Origin is { } origin ? ParseOrigin(origin) : null;
        var siteForCookies = isTopLevelNavigation ? urlOrigin : request.SiteForCookies is { } site ? ParseOrigin(site) : null;
        var topLevelOrigin = isTopLevelNavigation ? urlOrigin : request.TopLevelOrigin is { } topLevel ? ParseOrigin(topLevel) : null;
        if (siteForCookies is null)
            return new CookieRequestContext(url, IsHttp: true, CookieSameSiteStatus.Strict, topLevelOrigin, isTopLevelNavigation);

        var isSameSiteUrl = !siteForCookies.IsOpaque && OriginRecord.IsSameSite(siteForCookies, urlOrigin);
        var sameSiteUrls = isSameSiteUrl && previousUrls.All(previous => OriginRecord.IsSameSite(siteForCookies, OriginRecord.FromUrl(previous)));
        var sameSiteInitiator = initiator is null || OriginRecord.IsSameSite(initiator, urlOrigin);
        CookieSameSiteStatus status;
        if (isTopLevelNavigation)
        {
            status = sameSiteUrls && sameSiteInitiator ? CookieSameSiteStatus.Strict
                : request.Method is "GET" or "HEAD" or "OPTIONS" or "TRACE" ? CookieSameSiteStatus.Lax : CookieSameSiteStatus.LaxUnsafe;
        }
        else
        {
            // The initiator of a subresource request is the document of its site for cookies (a fetch() whose origin is
            // tainted by a cross-origin redirect is still initiated by the document)
            status = sameSiteUrls && (!isNestedNavigation || sameSiteInitiator) ? CookieSameSiteStatus.Strict : CookieSameSiteStatus.CrossSite;
        }

        // The partitioned cookies of a cross-site request are keyed by the cross-site ancestor bit (CHIPS)
        return new CookieRequestContext(url, IsHttp: true, status, topLevelOrigin, isTopLevelNavigation, CrossSiteAncestor: !isTopLevelNavigation && !isSameSiteUrl);
    }

    /// <summary>Parses a serialized origin (<c>null</c> is an opaque origin).</summary>
    private static OriginRecord ParseOrigin(string origin)
        => origin != "null" && UrlParser.Parse(origin, baseUrl: null) is { } url ? OriginRecord.FromUrl(url) : OriginRecord.CreateOpaque();
}
