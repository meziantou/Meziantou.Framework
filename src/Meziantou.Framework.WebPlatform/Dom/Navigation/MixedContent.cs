using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Mixed Content (https://w3c.github.io/webappsec-mixed-content/): the subresources of a document that restricts mixed
/// security contexts (e.g. an <c>https:</c> page) whose URL is not potentially trustworthy (e.g. an <c>http:</c> URL) are
/// upgraded to <c>https:</c> (the images), or blocked (the other destinations: a network error). The navigations are not
/// subresources: they are not checked.
/// </summary>
internal static class MixedContent
{
    /// <summary>
    /// https://w3c.github.io/webappsec-mixed-content/#categorize-settings-object: the origin of the document, or of the
    /// document of an ancestor navigable, is potentially trustworthy. The settings document of a worker also checks the
    /// document (or worker) that created it, as browsers do: a worker created from a <c>data:</c> URL has an opaque origin.
    /// A document with an opaque origin (e.g. sandboxed) whose URL is <c>https:</c> prohibits mixed content too.
    /// </summary>
    public static bool ProhibitsMixedSecurityContexts(Document document)
    {
        for (var current = document; current is not null; current = current.WorkerGlobal is { } worker ? worker.Agent.CreatorDocument : current.BrowsingContext?.Container?.NodeDocument)
        {
            // A sandboxed document (opaque origin) whose URL is https: also prohibits mixed content, as in the browsers
            if (current.IsPotentiallyTrustworthy() || (Document.GetOriginDocument(current).OpaqueOrigin && current.UrlRecord.Scheme is "https" or "wss"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-mixed-content/#upgrade-algorithm: returns the URL to fetch, the <c>https:</c> URL of
    /// an <c>http:</c> image, audio or video resource (unless its host is an IP address, or the element uses <c>srcset</c> or
    /// <c>picture</c>: the "imageset" initiator), else <paramref name="url"/>.
    /// </summary>
    public static UrlRecord UpgradeRequestUrl(Document document, UrlRecord url, RequestDestination destination, bool imageSet = false)
    {
        if (destination is not (RequestDestination.Image or RequestDestination.Audio or RequestDestination.Video) || imageSet || url.Scheme != "http")
            return url;

        if (url.Host is not { Kind: UrlHostKind.Domain } || ReferrerPolicies.IsPotentiallyTrustworthy(url) || !ProhibitsMixedSecurityContexts(document))
            return url;

        // Setting the scheme resets the port when it is the default port of https (443)
        var upgraded = url.Clone();
        return UrlParser.ParseWithStateOverride("https:", upgraded, UrlParserState.SchemeStart) ? upgraded : url;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-mixed-content/#should-block-fetch: whether the request of a subresource is a network
    /// error because its URL is not potentially trustworthy in a document that prohibits mixed security contexts.
    /// </summary>
    public static bool ShouldBlockFetch(Document document, UrlRecord url) => !ReferrerPolicies.IsPotentiallyTrustworthy(url) && ProhibitsMixedSecurityContexts(document);
}
