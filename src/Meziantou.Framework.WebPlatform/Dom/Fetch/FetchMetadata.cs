using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The Fetch Metadata request headers (https://w3c.github.io/webappsec-fetch-metadata/): <c>Sec-Fetch-Dest</c>,
/// <c>Sec-Fetch-Mode</c>, <c>Sec-Fetch-Site</c> and <c>Sec-Fetch-User</c>, sent with the requests of potentially
/// trustworthy URLs (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, step 8.12).
/// </summary>
internal static class FetchMetadata
{
    /// <summary>
    /// https://w3c.github.io/webappsec-fetch-metadata/#abstract-opdef-append-the-fetch-metadata-headers-for-a-request: appends
    /// the headers of a request whose current URL is the last URL of <paramref name="urlList"/> (the URL of the request,
    /// then the locations of its redirects).
    /// </summary>
    /// <param name="headers">The headers of the request.</param>
    /// <param name="request">The request (its destination, mode, origin and user activation).</param>
    /// <param name="urlList">The URLs of the request and of its redirects (https://fetch.spec.whatwg.org/#concept-request-url-list).</param>
    public static void AppendHeaders(List<KeyValuePair<string, string>> headers, DocumentRequest request, IReadOnlyList<UrlRecord> urlList)
        => AppendHeaders(headers, request.Destination, request.Mode, request.Origin, request.UserActivation, urlList);

    /// <inheritdoc cref="AppendHeaders(List{KeyValuePair{string, string}}, DocumentRequest, IReadOnlyList{UrlRecord})"/>
    /// <param name="headers">The headers of the request.</param>
    /// <param name="destination">The destination of the request.</param>
    /// <param name="mode">The mode of the request.</param>
    /// <param name="origin">The serialized origin of the request, or null for a navigation that is not initiated by a document (e.g. the address bar).</param>
    /// <param name="userActivation">Whether the navigation is initiated with a user activation.</param>
    /// <param name="urlList">The URLs of the request and of its redirects.</param>
    /// <param name="originRecord">The origin of the request, when it is known (else <paramref name="origin"/> is parsed).</param>
    public static void AppendHeaders(List<KeyValuePair<string, string>> headers, RequestDestination destination, RequestMode mode, string? origin, bool userActivation, IReadOnlyList<UrlRecord> urlList, OriginRecord? originRecord = null)
    {
        // https://w3c.github.io/webappsec-fetch-metadata/#abstract-opdef-append-the-fetch-metadata-headers-for-a-request, step 1
        if (urlList.Count == 0 || !ReferrerPolicies.IsPotentiallyTrustworthy(urlList[^1]))
            return;

        SetHeader(headers, "Sec-Fetch-Dest", GetDestinationName(destination));
        SetHeader(headers, "Sec-Fetch-Mode", mode switch
        {
            RequestMode.Navigate => "navigate",
            RequestMode.Cors => "cors",
            RequestMode.SameOrigin => "same-origin",
            RequestMode.WebSocket => "websocket",
            _ => "no-cors",
        });
        SetHeader(headers, "Sec-Fetch-Site", GetSite(origin, originRecord, urlList));

        // https://w3c.github.io/webappsec-fetch-metadata/#abstract-opdef-set-user: the navigations initiated with a user activation
        if (mode == RequestMode.Navigate && userActivation)
        {
            SetHeader(headers, "Sec-Fetch-User", "?1");
        }
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-request-destination, or <c>empty</c> for the empty string.</summary>
    internal static string GetDestinationName(RequestDestination destination) => destination switch
    {
        RequestDestination.Document => "document",
        RequestDestination.Iframe => "iframe",
        RequestDestination.Frame => "frame",
        RequestDestination.Embed => "embed",
        RequestDestination.Object => "object",
        RequestDestination.Script => "script",
        RequestDestination.Style => "style",
        RequestDestination.Image => "image",
        RequestDestination.Font => "font",
        RequestDestination.Json => "json",
        RequestDestination.Text => "text",
        RequestDestination.Worker => "worker",
        RequestDestination.SharedWorker => "sharedworker",
        RequestDestination.ServiceWorker => "serviceworker",
        RequestDestination.Report => "report",
        RequestDestination.Audio => "audio",
        RequestDestination.Video => "video",
        RequestDestination.Track => "track",
        RequestDestination.PaintWorklet => "paintworklet",
        RequestDestination.SpeculationRules => "speculationrules",
        _ => "empty",
    };

    /// <summary>
    /// https://w3c.github.io/webappsec-fetch-metadata/#abstract-opdef-set-site: <c>none</c> for a navigation that is not
    /// initiated by a document, else <c>same-origin</c>, <c>same-site</c> or <c>cross-site</c> for the least related URL of
    /// the URL list.
    /// </summary>
    private static string GetSite(string? origin, OriginRecord? originRecord, IReadOnlyList<UrlRecord> urlList)
    {
        if (origin is null)
            return "none";

        var requestOrigin = originRecord ?? (origin == "null" || UrlParser.Parse(origin, baseUrl: null) is not { } originUrl ? OriginRecord.CreateOpaque() : OriginRecord.FromUrl(originUrl));
        var site = "same-origin";
        foreach (var url in urlList)
        {
            var urlOrigin = OriginRecord.FromUrl(url);
            if (OriginRecord.IsSameOrigin(urlOrigin, requestOrigin))
                continue;

            if (!OriginRecord.IsSameSite(requestOrigin, urlOrigin))
                return "cross-site";

            site = "same-site";
        }

        return site;
    }

    private static void SetHeader(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        FetchHeaders.Delete(headers, name);
        headers.Add(new(name, value));
    }
}
