using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The settings of the HTTPS upgrades (https://github.com/whatwg/fetch/pull/1655, <see cref="BrowsingContextOptions.HttpsUpgrades"/>):
/// the <c>http:</c> URL of a top-level navigation is fetched over <c>https:</c> first, and over <c>http:</c> when the
/// <c>https:</c> request fails.
/// </summary>
public sealed class HttpsUpgradeOptions
{
    /// <summary>Gets the settings of the HTTPS upgrades as the browsers apply them (all the properties have their default value).</summary>
    public static HttpsUpgradeOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets whether the URLs whose port is not the default port of <c>http:</c> (80) are upgraded, keeping their
    /// port (<c>http://example.com:8080/</c> becomes <c>https://example.com:8080/</c>). A server rarely serves HTTP and HTTPS
    /// on the same port, so the default is false, as in the specification and the browsers. Test servers that serve HTTPS on
    /// the port of the <c>http:</c> URL (as the web-platform-tests expect) need it.
    /// </summary>
    public bool UpgradeNonDefaultPorts { get; init; }

    /// <summary>
    /// Gets or sets whether the hosts that are not unique are upgraded: the single-label hosts (<c>http://intranet/</c>) and
    /// the domains whose top-level domain is not in the public suffix list (<c>.test</c>, <c>.local</c>, <c>.internal</c>,
    /// <c>.lan</c>...), which resolve in a private network only. The default is false, as in Chromium. The IP addresses and
    /// the potentially trustworthy hosts (<c>localhost</c>) are never upgraded.
    /// </summary>
    public bool UpgradeNonUniqueHosts { get; init; }

    /// <summary>
    /// Gets or sets the time after which an upgraded request that has no response yet is canceled, so that the navigation
    /// falls back to <c>http:</c> without waiting for the timeout of the network (a server that drops the connections to the
    /// port of HTTPS). The time is measured with <see cref="BrowsingContextOptions.TimeProvider"/>. The default is null: the
    /// navigation waits for the response or the error of the <see cref="DocumentLoader"/>. Chromium uses 3 seconds.
    /// </summary>
    public TimeSpan? FallbackTimeout { get; init; }
}

/// <summary>
/// HTTPS upgrades (https://github.com/whatwg/fetch/pull/1655, rendered at https://whatpr.org/fetch/1655.html#https-upgrading):
/// when <see cref="BrowsingContextOptions.HttpsUpgrades"/> is set, the <c>http:</c> URL of a top-level navigation is fetched
/// over <c>https:</c>, and the navigation falls back to the <c>http:</c> URL when the upgraded request fails (a network error:
/// the loader returns no response, e.g. the connection or the TLS handshake fails, or the fallback timeout expires) or when
/// the <c>https:</c> URL redirects back to the <c>http:</c> URL. Each URL of a redirect chain is upgraded, as in the browsers
/// (the specification only upgrades the first one; the tests of https-upgrades/ expect the browsers' behavior). After a
/// fallback, the host is not upgraded again for 7 days (the allowlist of the example of the specification).
/// </summary>
internal static class HttpsUpgrading
{
    /// <summary>
    /// https://whatpr.org/fetch/1655.html#upgrade-an-http-request, called by main fetch after the mixed content upgrade
    /// (step 6), for each URL of a redirect chain, before the referrer is determined: the referrer of an upgraded request is
    /// computed for the <c>https:</c> URL (a <c>no-referrer-when-downgrade</c> policy keeps it).
    /// </summary>
    public static void UpgradeRequest(FetchRequest request)
    {
        if (request.Navigable?.Options is not { HttpsUpgrades: { } options } browsingContextOptions)
            return;

        var url = request.CurrentUrl;

        // A fallback request is not upgraded again: the next URL is the fallback URL after a network error of the upgraded
        // request (a 307 redirect, see Fallback), or a redirect of the https: URL back to the http: URL (a redirect loop:
        // the upgrade failed too). Another URL (the location of a redirect of the upgraded request) is upgraded again.
        if (request.HttpsUpgradeFallbackUrl is { } fallbackUrl)
        {
            request.HttpsUpgradeFallbackUrl = null;
            request.IsHttpsUpgrade = false;
            if (fallbackUrl.Serialize(excludeFragment: true) == url.Serialize(excludeFragment: true))
            {
                if (url.Host is { } fallbackHost)
                {
                    browsingContextOptions.HttpsUpgradeAllowlist.Add(fallbackHost.Serialized, request.Navigable.EventLoop.TimeProvider);
                }

                return;
            }
        }

        if (!IsEligible(request, url, options) || browsingContextOptions.HttpsUpgradeAllowlist.Contains(url.Host!.Value.Serialized, request.Navigable.EventLoop.TimeProvider))
            return;

        request.HttpsUpgradeFallbackUrl = url;
        request.UrlList[^1] = UpgradeInsecureRequests.Upgrade(url);
        request.IsHttpsUpgrade = true;
    }

    /// <summary>
    /// https://whatpr.org/fetch/1655.html#upgrade-an-http-request, step 1: a <c>GET</c> request of a top-level navigation
    /// (destination "document") to an <c>http:</c> URL on the default port, whose host is not exempted. The prefetches and
    /// the speculative requests are not upgraded: their response is matched with the URL of the navigation.
    /// </summary>
    private static bool IsEligible(FetchRequest request, UrlRecord url, HttpsUpgradeOptions options)
    {
        if (request.Destination != RequestDestination.Document || request.Method != "GET" || url.Scheme != "http" || request.IsPrefetch || request.IsSpeculative)
            return false;

        if (url.Port != -1 && !options.UpgradeNonDefaultPorts)
            return false;

        // The implementation-defined exemptions, as in Chromium: the IP addresses, the potentially trustworthy hosts
        // (localhost), and the hosts that are not unique (they resolve in a private network only)
        return url.Host is { Kind: UrlHostKind.Domain } host
            && !ReferrerPolicies.IsPotentiallyTrustworthy(url)
            && (options.UpgradeNonUniqueHosts || PublicSuffixList.IsUniqueDomain(host));
    }

    /// <summary>
    /// https://whatpr.org/fetch/1655.html#https-upgrade-fallback, called by HTTP-network fetch when the connection fails: a
    /// network error of an upgraded request is a <c>307</c> redirect to its <c>http:</c> URL, which main fetch does not
    /// upgrade (see <see cref="UpgradeRequest"/>).
    /// </summary>
    public static FetchResponse Fallback(FetchRequest request, FetchResponse response)
    {
        if (!request.IsHttpsUpgrade || request.HttpsUpgradeFallbackUrl is not { } fallbackUrl || !response.IsNetworkError || response.Aborted)
            return response;

        request.IsHttpsUpgrade = false;
        var fallbackResponse = FetchResponse.Create(307, "Temporary Redirect", [new("Location", fallbackUrl.Serialize())], body: null);
        fallbackResponse.UrlList = [.. request.UrlList];
        return fallbackResponse;
    }
}

/// <summary>
/// The hosts whose HTTPS upgrade failed (the allowlist of https://whatpr.org/fetch/1655.html#example-https-upgrade-allowlist):
/// their navigations are not upgraded until the entry expires, 7 days after the last failure. It is thread-safe and holds at
/// most <see cref="MaxCount"/> hosts (the entries that expire first are removed).
/// </summary>
internal sealed class HttpsUpgradeAllowlist
{
    public const int MaxCount = 1000;

    private static readonly TimeSpan Duration = TimeSpan.FromDays(7);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _expirations = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _expirations.Count;
            }
        }
    }

    /// <summary>Whether a host is in the allowlist. As in the example of the specification, its entry is extended for 7 days.</summary>
    public bool Contains(string host, TimeProvider timeProvider)
    {
        lock (_lock)
        {
            // The clock is read only when the host has an entry
            if (!_expirations.TryGetValue(host, out var expiration))
                return false;

            var now = timeProvider.GetUtcNow();
            if (expiration <= now)
            {
                _expirations.Remove(host);
                return false;
            }

            _expirations[host] = now + Duration;
            return true;
        }
    }

    /// <summary>Adds a host whose upgrade failed.</summary>
    public void Add(string host, TimeProvider timeProvider)
    {
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            if (_expirations.Count >= MaxCount && !_expirations.ContainsKey(host))
            {
                foreach (var (expiredHost, expiration) in _expirations)
                {
                    if (expiration <= now)
                    {
                        _expirations.Remove(expiredHost);
                    }
                }

                if (_expirations.Count >= MaxCount)
                {
                    _expirations.Remove(_expirations.MinBy(entry => entry.Value).Key);
                }
            }

            _expirations[host] = now + Duration;
        }
    }
}
