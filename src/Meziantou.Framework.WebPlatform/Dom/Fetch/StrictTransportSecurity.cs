using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The settings of HTTP Strict Transport Security (HSTS, https://www.rfc-editor.org/rfc/rfc6797, <see cref="BrowsingContextOptions.Hsts"/>):
/// the <c>http:</c> URLs of the Known HSTS Hosts are fetched over <c>https:</c>, without fallback to <c>http:</c>.
/// </summary>
public sealed class HstsOptions
{
    /// <summary>Gets the settings of HSTS as the browsers apply them, with the HSTS preload list (all the properties have their default value).</summary>
    public static HstsOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets whether the hosts are learned from the <c>Strict-Transport-Security</c> header of the <c>https:</c>
    /// responses (https://www.rfc-editor.org/rfc/rfc6797#section-8.1). The learned hosts are shared by the browsing
    /// contexts that share the <see cref="BrowsingContextOptions"/> instance, and are not persisted. As in Safari, a learned
    /// host does not upgrade a third-party request: a request that is not a top-level navigation, to a URL that is not on
    /// the same site as the top-level document. The default is true, as in the browsers.
    /// </summary>
    public bool LearnFromResponses { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the hosts of the HSTS preload list of Chromium (https://hstspreload.org) are Known HSTS Hosts,
    /// as in the browsers. It is the list of the Meziantou.Framework.Http.Hsts package, of the same version, and it
    /// upgrades every request, including the third-party ones. The list is loaded the first time an <c>http:</c> request
    /// needs it (about 120 KB of memory and a few milliseconds). Setting the
    /// <c>Meziantou.Framework.Http.Hsts.IncludePreloadList</c> feature switch to false turns it off in the whole
    /// application, which lets the trimmer remove the code that reads it. The default is true.
    /// </summary>
    public bool UsePreloadList { get; init; } = true;

    /// <summary>
    /// Gets or sets a function that tells whether the <c>http:</c> requests to a host must be upgraded to <c>https:</c>, in
    /// addition to the hosts learned from the responses and to the preload list: for instance the hosts of an intranet, or
    /// <c>HstsDomainPolicyCollection.MustUpgradeRequest</c> of the Meziantou.Framework.Http.Hsts package to share the
    /// policies that an <see cref="HttpClient"/> learns. Like the preload list, it applies to every request. The host is
    /// the ASCII serialization of a domain (an internationalized domain is in its Punycode form), lowercase and without
    /// a trailing dot; the function matches its superdomains itself (the <c>includeSubDomains</c> directive of a policy). It
    /// is called from the threads of the event loops of the browsing contexts and of their workers, and must be thread-safe.
    /// The default is null.
    /// </summary>
    public Func<string, bool>? IsKnownHost { get; init; }
}

/// <summary>A valid <c>Strict-Transport-Security</c> header (https://www.rfc-editor.org/rfc/rfc6797#section-6.1).</summary>
internal readonly record struct StrictTransportSecurityHeader(long MaxAgeSeconds, bool IncludeSubdomains)
{
    /// <summary>The highest <c>max-age</c> (one year), as in Chromium: a larger value is clamped.</summary>
    public const long MaxMaxAgeSeconds = 365L * 24 * 60 * 60;

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc6797#section-6.1: <c>[ directive ] *( ";" [ directive ] )</c>, where a directive is
    /// <c>token [ "=" ( token / quoted-string ) ]</c>, with optional whitespace around the tokens. As in Chromium, a header
    /// that does not match the grammar, that repeats <c>max-age</c> or <c>includeSubDomains</c>, that has no <c>max-age</c>,
    /// whose <c>max-age</c> is not a number of seconds, or whose <c>includeSubDomains</c> has a value is invalid (null). The
    /// unknown directives are ignored.
    /// </summary>
    public static StrictTransportSecurityHeader? Parse(ReadOnlySpan<char> value)
    {
        long? maxAge = null;
        var includeSubdomains = false;
        var position = 0;
        while (true)
        {
            SkipWhitespace(value, ref position);
            if (position == value.Length)
                break;

            // An empty directive
            if (value[position] == ';')
            {
                position++;
                continue;
            }

            var name = ReadToken(value, ref position);
            if (name.IsEmpty)
                return null;

            SkipWhitespace(value, ref position);
            string? directiveValue = null;
            if (position < value.Length && value[position] == '=')
            {
                position++;
                SkipWhitespace(value, ref position);
                if (position < value.Length && value[position] == '"')
                {
                    if (ReadQuotedString(value, ref position) is not { } quoted)
                        return null;

                    directiveValue = quoted;
                }
                else
                {
                    var token = ReadToken(value, ref position);
                    if (token.IsEmpty)
                        return null;

                    directiveValue = token.ToString();
                }

                SkipWhitespace(value, ref position);
            }

            if (position < value.Length && value[position] != ';')
                return null;

            if (InfraAscii.EqualsIgnoreAsciiCase(name, "max-age"))
            {
                if (maxAge is not null || directiveValue is not { Length: > 0 } || directiveValue.AsSpan().ContainsAnyExceptInRange('0', '9'))
                    return null;

                // delta-seconds has no upper bound: a value too large for a long is clamped too
                var digits = directiveValue.AsSpan().TrimStart('0');
                maxAge = digits.Length > 18 ? MaxMaxAgeSeconds : digits.IsEmpty ? 0 : Math.Min(long.Parse(digits, System.Globalization.CultureInfo.InvariantCulture), MaxMaxAgeSeconds);
            }
            else if (InfraAscii.EqualsIgnoreAsciiCase(name, "includeSubDomains"))
            {
                if (includeSubdomains || directiveValue is not null)
                    return null;

                includeSubdomains = true;
            }
        }

        return maxAge is { } seconds ? new StrictTransportSecurityHeader(seconds, includeSubdomains) : null;
    }

    /// <summary>The serialization of the header, which <see cref="Parse"/> parses as the same header.</summary>
    public string Serialize() => IncludeSubdomains ? $"max-age={MaxAgeSeconds}; includeSubDomains" : $"max-age={MaxAgeSeconds}";

    private static void SkipWhitespace(ReadOnlySpan<char> value, ref int position)
    {
        while (position < value.Length && value[position] is ' ' or '\t')
        {
            position++;
        }
    }

    private static ReadOnlySpan<char> ReadToken(ReadOnlySpan<char> value, ref int position)
    {
        var start = position;
        while (position < value.Length && FetchHeaders.IsTokenCharacter(value[position]))
        {
            position++;
        }

        return value[start..position];
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc9110#section-5.6.4: the unescaped value of a quoted string, or null when it is not terminated.</summary>
    private static string? ReadQuotedString(ReadOnlySpan<char> value, ref int position)
    {
        var builder = new System.Text.StringBuilder();
        position++;
        while (position < value.Length)
        {
            var c = value[position++];
            if (c == '"')
                return builder.ToString();

            if (c == '\\')
            {
                if (position == value.Length)
                    return null;

                c = value[position++];
            }

            builder.Append(c);
        }

        return null;
    }
}

/// <summary>
/// The Known HSTS Hosts learned from the responses (https://www.rfc-editor.org/rfc/rfc6797#section-8.1). It is thread-safe and
/// holds at most <see cref="MaxCount"/> hosts (the entries that expire first are removed).
/// </summary>
internal sealed class HstsHostStore
{
    public const int MaxCount = 10_000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (DateTimeOffset ExpiresAt, bool IncludeSubdomains)> _hosts = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _hosts.Count;
            }
        }
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc6797#section-8.2: whether the host has a congruent match, or a superdomain match whose
    /// policy includes the subdomains. The clock is read only when an entry matches.
    /// </summary>
    public bool Contains(string host, TimeProvider timeProvider)
    {
        lock (_lock)
        {
            if (_hosts.Count == 0)
                return false;

            DateTimeOffset? now = null;
            var lookup = _hosts.GetAlternateLookup<ReadOnlySpan<char>>();
            var suffix = host.AsSpan();
            var isHost = true;
            while (true)
            {
                if (lookup.TryGetValue(suffix, out var entry) && (isHost || entry.IncludeSubdomains))
                {
                    now ??= timeProvider.GetUtcNow();
                    if (entry.ExpiresAt > now)
                        return true;

                    lookup.Remove(suffix);
                }

                var dot = suffix.IndexOf('.');
                if (dot < 0)
                    return false;

                suffix = suffix[(dot + 1)..];
                isHost = false;
            }
        }
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc6797#section-8.1.1: adds or updates the policy of a host; a <c>max-age</c> of 0 removes it.</summary>
    public void Add(string host, StrictTransportSecurityHeader header, TimeProvider timeProvider)
    {
        lock (_lock)
        {
            if (header.MaxAgeSeconds == 0)
            {
                _hosts.Remove(host);
                return;
            }

            var now = timeProvider.GetUtcNow();
            if (_hosts.Count >= MaxCount && !_hosts.ContainsKey(host))
            {
                foreach (var (knownHost, entry) in _hosts)
                {
                    if (entry.ExpiresAt <= now)
                    {
                        _hosts.Remove(knownHost);
                    }
                }

                if (_hosts.Count >= MaxCount)
                {
                    _hosts.Remove(_hosts.MinBy(entry => entry.Value.ExpiresAt).Key);
                }
            }

            _hosts[host] = (now.AddSeconds(header.MaxAgeSeconds), header.IncludeSubdomains);
        }
    }
}

/// <summary>HTTP Strict Transport Security in the fetch algorithm (https://fetch.spec.whatwg.org/#concept-main-fetch, step 11).</summary>
internal static class StrictTransportSecurity
{
    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-main-fetch, step 11, for each URL of a redirect chain (after the referrer is
    /// determined): the <c>http:</c> URL of a request whose host is a domain (not <c>localhost</c>) that is a Known HSTS
    /// Host is fetched over <c>https:</c>; the port of http (80) becomes the port of https (443), the other ports are kept
    /// (https://www.rfc-editor.org/rfc/rfc6797#section-8.3). The opening handshake of a <c>ws:</c> WebSocket is an
    /// <c>http:</c> request: it is upgraded too.
    /// </summary>
    public static void UpgradeRequest(FetchRequest request)
    {
        var url = request.CurrentUrl;
        if (url.Scheme == "http" && IsKnownHost(request, url))
        {
            request.UrlList[^1] = UpgradeInsecureRequests.Upgrade(url);
        }
    }

    /// <summary>
    /// Whether the host of the URL of a request is a Known HSTS Host of the options of its environment. As in Safari and
    /// Firefox, the learned hosts do not apply to a third-party request, so that a third party cannot use them to track the
    /// users across the sites (see <see cref="IsThirdPartyRequest"/>); the preload list and the hosts of
    /// <see cref="HstsOptions.IsKnownHost"/> apply to every request.
    /// </summary>
    public static bool IsKnownHost(FetchRequest request, UrlRecord url)
    {
        if (GetHost(url) is not { } host || GetEnvironment(request) is not ({ Hsts: { } hsts } options, var timeProvider))
            return false;

        return (hsts.LearnFromResponses && options.HstsHosts.Count > 0 && !IsThirdPartyRequest(request, url) && options.HstsHosts.Contains(host, timeProvider))
            || (hsts.UsePreloadList && IsPreloaded(host))
            || hsts.IsKnownHost?.Invoke(host) == true;
    }

    /// <summary>
    /// Whether a host is in the HSTS preload list (https://www.rfc-editor.org/rfc/rfc6797#section-8.2): the host itself, or
    /// one of its superdomains whose entry includes the subdomains. The host is lowercase ASCII, without a trailing dot.
    /// </summary>
    internal static bool IsPreloaded(string host)
    {
        if (!global::Meziantou.Framework.Http.HstsPreloadList.IsSupported)
            return false;

        var preload = global::Meziantou.Framework.Http.HstsPreloadList.Shared;
        var suffix = host.AsSpan();
        var labelCount = suffix.Count('.') + 1;
        var isHost = true;
        while (true)
        {
            if (labelCount <= preload.MaxLabelCount && preload.TryGetValue(suffix, labelCount, out var includeSubdomains) && (isHost || includeSubdomains))
                return true;

            var dot = suffix.IndexOf('.');
            if (dot < 0)
                return false;

            suffix = suffix[(dot + 1)..];
            labelCount--;
            isHost = false;
        }
    }

    /// <summary>
    /// Whether a request is a third-party request: a request that is not a top-level navigation, to a URL that is not
    /// schemelessly same site with the top-level origin of the request (an iframe of another site, a subresource of
    /// another site, or a request of a document embedded by another site). This is the rule of Safari (WebKit ignores the
    /// learned hosts for the requests whose third-party cookies are blocked). A request without top-level origin (no
    /// client), or whose top-level origin is opaque, is not a third-party request.
    /// </summary>
    private static bool IsThirdPartyRequest(FetchRequest request, UrlRecord url)
    {
        if ((request.NavigationDestination ?? request.Destination) == RequestDestination.Document)
            return false;

        var serializedTopLevelOrigin = request.CookieContext is { } cookieContext ? cookieContext.TopLevelOrigin : request.Client?.TopLevelOriginForCookies;
        if (serializedTopLevelOrigin is null or "null" || UrlParser.Parse(serializedTopLevelOrigin, baseUrl: null) is not { } topLevelUrl)
            return false;

        var topLevelOrigin = OriginRecord.FromUrl(topLevelUrl);
        return !topLevelOrigin.IsOpaque && !OriginRecord.IsSchemelesslySameSite(topLevelOrigin, OriginRecord.FromUrl(url));
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc6797#section-8.1, called by HTTP-network fetch for the response of the loader: the
    /// first <c>Strict-Transport-Security</c> header of a response to an <c>https:</c> URL whose host is a domain sets the
    /// policy of the host (the header of an <c>http:</c> response is ignored). An invalid header is ignored.
    /// </summary>
    public static void ProcessResponse(FetchRequest request, List<KeyValuePair<string, string>> headers)
    {
        var url = request.CurrentUrl;
        if (url.Scheme != "https" || GetHost(url) is not { } host)
            return;

        string? value = null;
        foreach (var (name, headerValue) in headers)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(name, "Strict-Transport-Security"))
            {
                value = headerValue;
                break;
            }
        }

        if (value is null || GetEnvironment(request) is not ({ Hsts.LearnFromResponses: true } options, var timeProvider) || StrictTransportSecurityHeader.Parse(value) is not { } header)
            return;

        options.HstsHosts.Add(host, header, timeProvider);
    }

    /// <summary>The host of a URL as the policies store it (without its trailing dot), or null when it cannot be a Known HSTS Host (an IP address, <c>localhost</c>).</summary>
    private static string? GetHost(UrlRecord url)
    {
        if (url.Host is not { Kind: UrlHostKind.Domain } urlHost)
            return null;

        var host = urlHost.Serialized.TrimEnd('.');
        if (host.Length == 0 || host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
            return null;

        return host;
    }

    /// <summary>The options and the clock of the environment of a request: its navigable, else its client (a document or a worker), else the browsing context of a keepalive request.</summary>
    private static (BrowsingContextOptions Options, TimeProvider TimeProvider)? GetEnvironment(FetchRequest request)
    {
        var browsingContext = request.Navigable ?? request.KeepaliveBrowsingContext;
        if ((request.Navigable?.Options ?? request.Client?.EnvironmentOptions ?? browsingContext?.Options) is not { } options
            || (request.Navigable?.EventLoop ?? request.Client?.EnvironmentEventLoop ?? browsingContext?.EventLoop) is not { } eventLoop)
        {
            return null;
        }

        return (options, eventLoop.TimeProvider);
    }
}
