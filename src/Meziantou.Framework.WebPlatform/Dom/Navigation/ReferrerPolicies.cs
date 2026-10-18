using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The algorithms of Referrer Policy (https://w3c.github.io/webappsec-referrer-policy/).</summary>
internal static class ReferrerPolicies
{
    /// <summary>https://w3c.github.io/webappsec-referrer-policy/#default-referrer-policy</summary>
    public const string Default = "strict-origin-when-cross-origin";

    /// <summary>
    /// The maximum length of a referrer URL (https://w3c.github.io/webappsec-referrer-policy/#determine-requests-referrer,
    /// step 6): a longer URL is replaced by its origin.
    /// </summary>
    private const int MaxReferrerUrlLength = 4096;

    /// <summary>Returns whether a value is a referrer policy other than the empty string (https://w3c.github.io/webappsec-referrer-policy/#referrer-policy).</summary>
    public static bool IsValid(string value) => value is "no-referrer" or "no-referrer-when-downgrade" or "same-origin" or "origin" or "strict-origin" or "origin-when-cross-origin" or "strict-origin-when-cross-origin" or "unsafe-url";

    /// <summary>https://w3c.github.io/webappsec-referrer-policy/#parse-referrer-policy-from-header: the last valid token, or the empty string.</summary>
    public static string ParseHeader(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var policy = "";
        foreach (var token in HeaderList.GetDecodeAndSplit(headers, "Referrer-Policy") ?? [])
        {
            if (IsValid(token))
            {
                policy = token;
            }
        }

        return policy;
    }

    /// <summary>
    /// The referrer policy of a request: its own policy (e.g. the <c>referrerpolicy</c> attribute of the element), else the
    /// one of the policy container of the document (https://fetch.spec.whatwg.org/#main-fetch, step 8), else the default
    /// referrer policy.
    /// </summary>
    public static string GetRequestPolicy(string requestPolicy, Document? client)
    {
        if (requestPolicy.Length > 0)
            return requestPolicy;

        return client is { ReferrerPolicy.Length: > 0 } ? client.ReferrerPolicy : Default;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-referrer-policy/#determine-requests-referrer for a request whose referrer is "client":
    /// returns the serialized referrer, or null for no referrer.
    /// </summary>
    public static string? DetermineReferrer(Document client, UrlRecord currentUrl, string policy)
    {
        // The referrer source of a window: the URL of its document (or of the container of an iframe srcdoc document)
        if (client.SerializeOrigin() == "null")
            return null;

        var document = client;
        while (document.IsIframeSrcdocDocument && document.BrowsingContext?.Container?.NodeDocument is { } containerDocument)
        {
            document = containerDocument;
        }

        return DetermineReferrer(document.UrlRecord, currentUrl, policy);
    }

    /// <summary>https://w3c.github.io/webappsec-referrer-policy/#determine-requests-referrer, given the referrer source.</summary>
    public static string? DetermineReferrer(UrlRecord referrerSource, UrlRecord currentUrl, string policy)
    {
        if (StripUrl(referrerSource, originOnly: false) is not { } referrerUrl)
            return null;

        var referrerOrigin = StripUrl(referrerSource, originOnly: true)!;
        var serializedReferrerUrl = referrerUrl.Serialize();
        if (serializedReferrerUrl.Length > MaxReferrerUrlLength)
        {
            serializedReferrerUrl = referrerOrigin.Serialize();
        }

        var serializedReferrerOrigin = referrerOrigin.Serialize();
        var isDowngrade = IsPotentiallyTrustworthy(referrerUrl) && !IsPotentiallyTrustworthy(currentUrl);
        var isSameOrigin = IsSameOrigin(referrerUrl, currentUrl);
        return policy switch
        {
            "no-referrer" => null,
            "origin" => serializedReferrerOrigin,
            "unsafe-url" => serializedReferrerUrl,
            "strict-origin" => isDowngrade ? null : serializedReferrerOrigin,
            "same-origin" => isSameOrigin ? serializedReferrerUrl : null,
            "origin-when-cross-origin" => isSameOrigin ? serializedReferrerUrl : serializedReferrerOrigin,
            "no-referrer-when-downgrade" => isDowngrade ? null : serializedReferrerUrl,
            _ => isSameOrigin ? serializedReferrerUrl : isDowngrade ? null : serializedReferrerOrigin,
        };
    }

    /// <summary>https://w3c.github.io/webappsec-referrer-policy/#strip-url</summary>
    private static UrlRecord? StripUrl(UrlRecord url, bool originOnly)
    {
        // The local schemes (https://fetch.spec.whatwg.org/#local-scheme) are not sent
        if (url.Scheme is "about" or "blob" or "data")
            return null;

        var result = url.Clone();
        result.Username = "";
        result.Password = "";
        result.Fragment = null;
        if (originOnly)
        {
            // The path of a special URL is never empty: the origin is serialized with a trailing '/', as the browsers do
            result.Path = result.IsSpecial ? [""] : [];
            result.OpaquePath = result.HasOpaquePath ? "" : null;
            result.Query = null;
        }

        return result;
    }

    private static bool IsSameOrigin(UrlRecord a, UrlRecord b)
    {
        var origin = a.SerializeOrigin();
        return origin != "null" && origin == b.SerializeOrigin();
    }

    /// <summary>https://w3c.github.io/webappsec-secure-contexts/#is-url-trustworthy</summary>
    internal static bool IsPotentiallyTrustworthy(UrlRecord url)
    {
        if (url.Scheme is "data" || (url.Scheme == "about" && url.OpaquePath is "blank" or "srcdoc"))
            return true;

        return IsOriginPotentiallyTrustworthy(url);
    }

    /// <summary>https://w3c.github.io/webappsec-secure-contexts/#is-origin-trustworthy, for the origin of a URL.</summary>
    internal static bool IsOriginPotentiallyTrustworthy(UrlRecord url)
    {
        if (url.Scheme is "https" or "wss" or "file")
            return true;

        // The origin of a blob: URL is the origin of the URL of its path (https://url.spec.whatwg.org/#concept-url-origin)
        if (url.Scheme == "blob")
            return url.OpaquePath is { } path && UrlParser.Parse(path, baseUrl: null) is { Scheme: "http" or "https" } pathUrl && IsOriginPotentiallyTrustworthy(pathUrl);

        if (url.Scheme is not ("http" or "ws") || url.Host is not { } host)
            return false;

        var name = host.Serialized;
        return name is "localhost" or "[::1]" || name.EndsWith(".localhost", StringComparison.Ordinal) || (host.Kind == UrlHostKind.IPv4 && name.StartsWith("127.", StringComparison.Ordinal));
    }

    /// <summary>
    /// The referrer policy set by a <c>&lt;meta name="referrer"&gt;</c> element
    /// (https://html.spec.whatwg.org/multipage/semantics.html#meta-referrer), or null when its content is not a referrer policy.
    /// </summary>
    public static string? ParseMetaContent(string content)
    {
        var value = InfraAscii.ToAsciiLowercase(content) switch
        {
            "never" => "no-referrer",
            "default" => Default,
            "always" => "unsafe-url",
            "origin-when-crossorigin" => "origin-when-cross-origin",
            var other => other,
        };

        return IsValid(value) ? value : null;
    }
}
