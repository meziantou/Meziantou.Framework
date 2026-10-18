using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>The HTMLHyperlinkElementUtils mixin (https://html.spec.whatwg.org/multipage/links.html#htmlhyperlinkelementutils).</summary>
internal static class HyperlinkElementUtils
{
    /// <summary>The url of the element: the href content attribute parsed relative to the node document (https://html.spec.whatwg.org/multipage/links.html#reinitialise-url).</summary>
    internal static UrlRecord? GetUrl(Element element)
    {
        // SVG elements use the deprecated xlink:href attribute when there is no href attribute
        var href = element.GetAttributeValue("href");
        if (href is null && element is SvgElement)
        {
            href = element.GetAttributeNS("http://www.w3.org/1999/xlink", "href");
        }

        if (href is null)
            return null;

        var document = element.NodeDocument;
        return UrlParser.Parse(href, document.DocumentBaseUrl, WebEncodingRegistryHelper.GetUrlEncoding(document));
    }

    /// <summary>Returns a component of the url, or <paramref name="nullValue"/> when the url is null.</summary>
    public static string Get(Element element, Func<Dom.Url, string> getter, string nullValue)
    {
        var url = GetUrl(element);
        return url is null ? nullValue : getter(new Dom.Url(url));
    }

    /// <summary>Sets a component of the url and updates the href content attribute (https://html.spec.whatwg.org/multipage/links.html#update-href).</summary>
    public static void Set(Element element, string value, Action<Dom.Url, string> setter)
    {
        ArgumentNullException.ThrowIfNull(value);
        var url = GetUrl(element);
        if (url is null)
            return;

        var wrapper = new Url(url);
        setter(wrapper, value);
        element.SetAttributeValue("href", wrapper.Href);
    }
    /// <summary>
    /// The activation behavior of the <c>a</c> and <c>area</c> elements
    /// (https://html.spec.whatwg.org/multipage/links.html#links-created-by-a-and-area-elements) and of the SVG and MathML
    /// <c>a</c> elements: follows the hyperlink. The hyperlinks with a <c>download</c> attribute are downloaded: the download
    /// request navigate event is fired, but nothing is downloaded (downloads are not supported).
    /// </summary>
    public static void ActivationBehavior(Element element)
    {
        if (!element.HasActivationBehavior || CannotNavigate(element))
            return;

        if (element.HasAttribute("download"))
        {
            DownloadHyperlink(element);
            return;
        }

        FollowHyperlink(element);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/links.html#downloading-hyperlinks</summary>
    private static void DownloadHyperlink(Element element)
    {
        var document = element.NodeDocument;
        if ((document.ActiveSandboxingFlags & SandboxingFlags.Downloads) != SandboxingFlags.None || GetUrl(element) is not { } url || document.RelevantWindow is not { } window)
            return;

        var userInvolvement = window.HasTransientActivation ? UserNavigationInvolvement.Activation : UserNavigationInvolvement.None;
        if (!window.Navigation.FireDownloadRequestNavigateEvent(url, userInvolvement, element, element.GetAttributeValue("download") ?? ""))
            return;

        window.Navigation.InformAboutAbortingNavigation();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#hyperlink-referrer-policy: <c>no-referrer</c> for a <c>noreferrer</c>
    /// link, else the state of the <c>referrerpolicy</c> attribute.
    /// </summary>
    internal static string GetReferrerPolicy(Element element)
        => HasLinkType(element, "noreferrer") ? "no-referrer" : Reflection.GetEnumerated(element, "referrerpolicy", EnumeratedAttributes.Referrerpolicy);

    /// <summary>https://html.spec.whatwg.org/multipage/links.html#cannot-navigate</summary>
    private static bool CannotNavigate(Element element) => !element.NodeDocument.IsFullyActive || (element is not HtmlAnchorElement && !element.IsConnected);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#following-hyperlinks-2: navigates the navigable chosen by the target
    /// of the element (a new auxiliary browsing context for <c>_blank</c> and the unknown names).
    /// </summary>
    private static void FollowHyperlink(Element element)
    {
        var document = element.NodeDocument;
        if (document.BrowsingContext is not { } currentNavigable)
            return;

        var target = GetElementTarget(element);
        if (GetUrl(element) is not { } url)
            return;

        if (currentNavigable.ChooseNavigable(target, GetNoopener(element, target), out _) is not { } targetNavigable)
            return;

        // The ping URLs are parsed before the navigation, which may unload the document of the element
        var pingUrls = GetPingUrls(element);
        var referrerPolicy = HasLinkType(element, "noreferrer") ? "no-referrer" : element.GetAttributeValue("referrerpolicy") ?? "";
        // The navigation is initiated by the user when the window has transient activation (a trusted activation event)
        var userInvolvement = document.RelevantWindow?.HasTransientActivation == true ? UserNavigationInvolvement.Activation : UserNavigationInvolvement.None;
        targetNavigable.Navigate(url, document, NavigationHistoryBehavior.Auto, referrerPolicy: referrerPolicy, userInvolvement: userInvolvement, sourceElement: element);
        SendPings(currentNavigable, document, url, pingUrls);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#hyperlink-auditing: the tokens of the <c>ping</c> attribute of an
    /// <c>a</c> or <c>area</c> element (and of the MathML <c>a</c> element), encoding-parsed relative to the node document. The failures, the URLs whose scheme
    /// is not an HTTP(S) scheme and the mixed content URLs are ignored (the requests blocked by the content security policies,
    /// connect-src, are blocked and reported by the fetch).
    /// </summary>
    private static List<UrlRecord> GetPingUrls(Element element)
    {
        var urls = new List<UrlRecord>();
        if (element is not (HtmlAnchorElement or HtmlAreaElement or MathMLAnchorElement) || element.GetAttributeValue("ping") is not { Length: > 0 } ping)
            return urls;

        foreach (var token in Infra.InfraString.SplitOnAsciiWhitespace(ping))
        {
            // A ping is blockable mixed content (https://w3c.github.io/webappsec-mixed-content/#should-block-fetch), checked once
            // upgraded by the upgrade-insecure-requests directive
            if (Reflection.EncodingParseUrl(element.NodeDocument, token) is { Scheme: "http" or "https" } url
                && !MixedContent.ShouldBlockFetch(element.NodeDocument, UpgradeInsecureRequests.UpgradeSubresourceUrl(element.NodeDocument, url)))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    /// <summary>
    /// Sends the hyperlink auditing requests (https://html.spec.whatwg.org/multipage/links.html#hyperlink-auditing): POST
    /// requests whose body is <c>PING</c> (text/ping), without referrer, with the <c>Ping-To</c> header (the URL of the
    /// hyperlink), the <c>Ping-From</c> header (the URL of the document) when the ping URL is same-origin with the document or
    /// the document is not <c>https:</c>, and the <c>Origin</c> header (https://fetch.spec.whatwg.org/#append-a-request-origin-header).
    /// The responses are ignored.
    /// </summary>
    private static void SendPings(BrowsingContext browsingContext, Document document, UrlRecord targetUrl, List<UrlRecord> urls)
    {
        if (urls.Count == 0 || browsingContext.Options.DocumentLoader is null)
            return;

        var referrerPolicy = ReferrerPolicies.GetRequestPolicy("", document);
        foreach (var url in urls)
        {
            var request = new FetchRequest(url)
            {
                Method = "POST",
                Body = "PING"u8.ToArray(),
                Client = browsingContext.Document,
                Origin = document.SerializeOrigin(),
                Referrer = "no-referrer",
                ReferrerPolicy = referrerPolicy,
                Destination = RequestDestination.Ping,
                Mode = RequestMode.NoCors,
                CredentialsMode = FetchCredentialsMode.Include,
            };

            request.HeaderList.Add(new("Content-Type", "text/ping"));
            if (url.SerializeOrigin() == document.UrlRecord.SerializeOrigin() || document.UrlRecord.Scheme != "https")
            {
                request.HeaderList.Add(new("Ping-From", document.UrlRecord.Serialize()));
            }

            request.HeaderList.Add(new("Ping-To", targetUrl.Serialize()));

            // The response is ignored, including the network errors. The timing of the ping is added to the performance
            // timeline of the document (its initiator type is "ping").
            var timing = ResourceTiming.Start(document, request, "ping");
            if (timing is null)
            {
                _ = Fetching.StartFetch(request, CancellationToken.None);
            }
            else
            {
                Fetching.Fetch(request, response =>
                {
                    if (request.IsSentToLoader && document.IsFullyActive)
                    {
                        timing.Report(response);
                    }
                }, document.QueueElementTask);
            }
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#get-an-element's-noopener: the noopener and noreferrer link types,
    /// and the <c>_blank</c> target without the opener link type, do not set the opener of a new browsing context.
    /// </summary>
    internal static bool GetNoopener(Element element, string target)
    {
        if (HasLinkType(element, "noopener") || HasLinkType(element, "noreferrer"))
            return true;

        return !HasLinkType(element, "opener") && Infra.InfraAscii.EqualsIgnoreAsciiCase(target, "_blank");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/links.html#get-an-element's-target</summary>
    private static string GetElementTarget(Element element)
    {
        // An empty target attribute uses the target of the base element, as in the browsers (the specification uses the
        // empty string, which chooses the current navigable)
        var target = element.GetAttributeValue("target");
        if (string.IsNullOrEmpty(target))
        {
            foreach (var descendant in element.NodeDocument.Descendants())
            {
                if (descendant is HtmlBaseElement baseElement && baseElement.GetAttributeValue("target") is { } baseTarget)
                {
                    target = baseTarget;
                    break;
                }
            }
        }

        if (target is null)
            return "";

        if (target.AsSpan().IndexOfAny("\t\n\r") >= 0 && target.Contains('<', StringComparison.Ordinal))
            return "_blank";

        return target;
    }

    internal static bool HasLinkType(Element element, string linkType)
    {
        var rel = element.GetAttributeValue("rel");
        if (rel is null)
            return false;

        foreach (var token in rel.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(token, linkType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
