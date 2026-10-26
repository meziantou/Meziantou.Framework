using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The HTTP cache (https://httpwg.org/specs/rfc9111.html) used by the fetch algorithm (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch):
/// a private cache of the responses of the GET requests, shared by the browsing contexts and the workers that use the same
/// <see cref="DocumentLoader"/>. It is partitioned by the top-level site of the request and whether the request comes from a
/// cross-site frame (https://fetch.spec.whatwg.org/#http-cache-partitions), and the requests with and without credentials
/// have their own entries. The freshness is computed with the clock of the event loop of the request.
/// </summary>
/// <remarks>
/// The partial responses (<c>206</c>) are not stored, the <c>stale-while-revalidate</c> directive is not supported, and a
/// <c>HEAD</c> request does not use the stored response of a <c>GET</c> request. The cache is used by the threads of the workers.
/// </remarks>
internal sealed class HttpCache
{
    /// <summary>The longest body that is stored.</summary>
    internal const int MaxBodyLength = 16 * 1024 * 1024;

    // https://httpwg.org/specs/rfc9110.html#overview.of.status.codes: the statuses that are heuristically cacheable
    private static readonly HashSet<int> HeuristicallyCacheableStatuses = [200, 203, 204, 300, 301, 308, 404, 405, 410, 414, 501];

    // https://httpwg.org/specs/rfc9111.html#update: the headers of a 304 response that do not update the stored response
    private static readonly HashSet<string> NotUpdatedHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length", "Content-Encoding", "Content-Range", "Transfer-Encoding", "TE", "Connection", "Keep-Alive", "Proxy-Connection", "Trailer", "Upgrade",
    };

    private static readonly ConditionalWeakTable<DocumentLoader, HttpCache> Caches = [];

    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<HttpCacheEntry>> _entries = new(StringComparer.Ordinal);

    /// <summary>The HTTP cache of the requests sent by a loader.</summary>
    public static HttpCache Get(DocumentLoader loader) => Caches.GetValue(loader, static _ => new HttpCache());

    /// <summary>
    /// The key of the entries of a URL in a partition (https://fetch.spec.whatwg.org/#determine-the-http-cache-partition):
    /// the top-level site, whether the request comes from a cross-site frame, and the URL without its fragment. Null when
    /// the top-level origin is opaque: the response is not cached.
    /// </summary>
    public static string? GetKey(string? topLevelOrigin, bool crossSite, UrlRecord url)
    {
        if (topLevelOrigin is null || topLevelOrigin == "null" || UrlParser.Parse(topLevelOrigin, baseUrl: null) is not { } topLevelUrl)
            return null;

        var origin = OriginRecord.FromUrl(topLevelUrl);
        if (origin.IsOpaque)
            return null;

        var site = origin.Scheme + "://" + (PublicSuffixList.GetRegistrableDomain(origin.Host) ?? origin.Host.Serialized);
        return site + " " + (crossSite ? "cross-site" : "same-site") + "\n" + GetUrlKey(url);
    }

    /// <summary>The key of the entries of a URL in the same partition as <paramref name="key"/>.</summary>
    public static string GetKeyInPartition(string key, UrlRecord url) => key[..(key.IndexOf('\n', StringComparison.Ordinal) + 1)] + GetUrlKey(url);

    private static string GetUrlKey(UrlRecord url) => url.Serialize(excludeFragment: true);

    /// <summary>
    /// Selects the most recent stored response for a request (https://httpwg.org/specs/rfc9111.html#constructing.responses.from.caches):
    /// the request has the credentials mode of the stored request, and the headers nominated by its <c>Vary</c> header match.
    /// </summary>
    public HttpCacheEntry? Select(string key, bool includeCredentials, IReadOnlyList<KeyValuePair<string, string>> requestHeaders)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var entries))
                return null;

            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.IncludeCredentials == includeCredentials && MatchesVary(entry, requestHeaders))
                    return entry;
            }

            return null;
        }
    }

    /// <summary>
    /// Stores a response (https://httpwg.org/specs/rfc9111.html#response.cacheability): it replaces the stored responses of
    /// the same request (the same credentials mode and the same values of the headers nominated by <c>Vary</c>).
    /// </summary>
    public void Store(string key, HttpCacheEntry entry)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var entries))
            {
                entries = [];
                _entries[key] = entries;
            }

            entries.RemoveAll(existing => existing.IncludeCredentials == entry.IncludeCredentials && MatchesVary(existing, entry.RequestHeaders));
            entries.Add(entry);
        }
    }

    /// <summary>Removes the stored responses of a URL (https://httpwg.org/specs/rfc9111.html#invalidation).</summary>
    public void Invalidate(string key)
    {
        lock (_lock)
        {
            _entries.Remove(key);
        }
    }

    /// <summary>
    /// Updates a stored response with the headers of a <c>304</c> response that validates it
    /// (https://httpwg.org/specs/rfc9111.html#freshening.responses): the headers of the stored response are replaced by the
    /// headers with the same name, and the entry is fresh again from the time of the new response.
    /// </summary>
    public HttpCacheEntry Freshen(string key, HttpCacheEntry entry, IReadOnlyList<KeyValuePair<string, string>> headers, DateTimeOffset requestTime, DateTimeOffset responseTime)
    {
        var updatedHeaders = new List<KeyValuePair<string, string>>(entry.Headers.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in headers)
        {
            if (!NotUpdatedHeaderNames.Contains(name))
            {
                names.Add(name);
            }
        }

        foreach (var header in entry.Headers)
        {
            if (!names.Contains(header.Key))
            {
                updatedHeaders.Add(header);
            }
        }

        foreach (var header in headers)
        {
            if (names.Contains(header.Key))
            {
                updatedHeaders.Add(header);
            }
        }

        var updated = entry with { Headers = updatedHeaders, RequestTime = requestTime, ResponseTime = responseTime };
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var entries) && entries.IndexOf(entry) is var index and >= 0)
            {
                entries[index] = updated;
            }
        }

        return updated;
    }

    /// <summary>https://httpwg.org/specs/rfc9111.html#caching.negotiated.responses</summary>
    private static bool MatchesVary(HttpCacheEntry entry, IReadOnlyList<KeyValuePair<string, string>> requestHeaders)
    {
        foreach (var name in GetVaryNames(entry.Headers))
        {
            if (name == "*")
                return false;

            if (!string.Equals(NormalizeVaryValue(HeaderList.Get(entry.RequestHeaders, name)), NormalizeVaryValue(HeaderList.Get(requestHeaders, name)), StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static IEnumerable<string> GetVaryNames(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (!InfraAscii.EqualsIgnoreAsciiCase(name, "Vary"))
                continue;

            foreach (var part in value.Split(','))
            {
                var trimmed = part.Trim(' ', '\t');
                if (trimmed.Length > 0)
                    yield return trimmed;
            }
        }
    }

    /// <summary>The value of a header compared by <c>Vary</c>: the values of the list are separated by a comma without whitespace.</summary>
    private static string? NormalizeVaryValue(string? value)
    {
        if (value is null)
            return null;

        return string.Join(',', value.Split(',').Select(part => part.Trim(' ', '\t')));
    }

    /// <summary>
    /// Whether a response to a request can be stored (https://httpwg.org/specs/rfc9111.html#response.cacheability): the
    /// request is a GET request (or a POST or PATCH request whose response has explicit freshness and whose
    /// <c>Content-Location</c> is the URL of the request, https://httpwg.org/specs/rfc9110.html#POST), the status is final
    /// and is not <c>206</c>, there is no <c>no-store</c> directive, no <c>Vary: *</c>, and the response has explicit
    /// freshness or a status that is heuristically cacheable.
    /// </summary>
    public static bool IsStorable(string method, UrlRecord url, IReadOnlyList<KeyValuePair<string, string>> requestHeaders, int status, IReadOnlyList<KeyValuePair<string, string>> responseHeaders)
    {
        if (status is < 200 or 206 or 304 || status > 999)
            return false;

        var requestDirectives = ParseCacheControl(requestHeaders);
        var responseDirectives = ParseCacheControl(responseHeaders);
        if (requestDirectives.ContainsKey("no-store") || responseDirectives.ContainsKey("no-store"))
            return false;

        if (GetVaryNames(responseHeaders).Contains("*", StringComparer.Ordinal))
            return false;

        var hasExplicitFreshness = GetDeltaSeconds(responseDirectives, "max-age") is not null || HeaderList.Get(responseHeaders, "Expires") is not null;
        if (method != "GET")
        {
            return method is "POST" or "PATCH" && hasExplicitFreshness
                && HeaderList.Get(responseHeaders, "Content-Location") is { } contentLocation
                && UrlParser.Parse(contentLocation, url) is { } contentLocationUrl && GetUrlKey(contentLocationUrl) == GetUrlKey(url);
        }

        return hasExplicitFreshness || responseDirectives.ContainsKey("public") || responseDirectives.ContainsKey("private") || HeuristicallyCacheableStatuses.Contains(status);
    }

    /// <summary>
    /// Whether a stored response can be used without validation (https://httpwg.org/specs/rfc9111.html#expiration.model),
    /// given the directives of the request (https://httpwg.org/specs/rfc9111.html#cache-request-directive): its current age
    /// is less than its freshness lifetime, and the <c>max-age</c>, <c>min-fresh</c> and <c>max-stale</c> directives of the
    /// request are satisfied. A <c>no-cache</c> directive (or <c>Pragma: no-cache</c>) always requires a validation.
    /// </summary>
    public static bool IsFresh(HttpCacheEntry entry, IReadOnlyList<KeyValuePair<string, string>> requestHeaders, DateTimeOffset now)
    {
        var requestDirectives = ParseCacheControl(requestHeaders);
        if (requestDirectives.ContainsKey("no-cache") || (requestDirectives.Count == 0 && HeaderList.Get(requestHeaders, "Pragma") is { } pragma && pragma.Contains("no-cache", StringComparison.OrdinalIgnoreCase)))
            return false;

        // As in the browsers, a response with Pragma: no-cache is always validated, unless it is immutable
        var responseDirectives = ParseCacheControl(entry.Headers);
        if (responseDirectives.ContainsKey("no-cache") || (!responseDirectives.ContainsKey("immutable") && HeaderList.Get(entry.Headers, "Pragma") is { } responsePragma && responsePragma.Contains("no-cache", StringComparison.OrdinalIgnoreCase)))
            return false;

        var lifetime = GetFreshnessLifetime(entry, responseDirectives);
        var age = GetCurrentAge(entry, now);
        if (GetDeltaSeconds(requestDirectives, "max-age") is { } maxAge && age > maxAge)
            return false;

        if (GetDeltaSeconds(requestDirectives, "min-fresh") is { } minFresh && lifetime - age < minFresh)
            return false;

        if (lifetime > age)
            return true;

        // A stale response is used with max-stale, unless the response must be revalidated
        if (!requestDirectives.TryGetValue("max-stale", out var maxStaleValue) || responseDirectives.ContainsKey("must-revalidate"))
            return false;

        return maxStaleValue is null || (TryParseDeltaSeconds(maxStaleValue, out var maxStale) && age - lifetime <= maxStale);
    }

    /// <summary>https://httpwg.org/specs/rfc9111.html#calculating.freshness.lifetime (a private cache), in seconds.</summary>
    private static double GetFreshnessLifetime(HttpCacheEntry entry, Dictionary<string, string?> responseDirectives)
    {
        if (responseDirectives.ContainsKey("max-age"))
            return GetDeltaSeconds(responseDirectives, "max-age") ?? 0;

        var date = ParseHttpDate(HeaderList.Get(entry.Headers, "Date")) ?? entry.ResponseTime;
        if (HeaderList.Get(entry.Headers, "Expires") is { } expiresValue)
        {
            // An invalid date (e.g. "0") is in the past
            return ParseHttpDate(expiresValue) is { } expires ? (expires - date).TotalSeconds : 0;
        }

        // https://httpwg.org/specs/rfc9111.html#heuristic.freshness: 10% of the time since the last modification
        if ((HeuristicallyCacheableStatuses.Contains(entry.Status) || responseDirectives.ContainsKey("public")) && ParseHttpDate(HeaderList.Get(entry.Headers, "Last-Modified")) is { } lastModified && lastModified < date)
            return (date - lastModified).TotalSeconds / 10;

        return 0;
    }

    /// <summary>https://httpwg.org/specs/rfc9111.html#age.calculations, in seconds.</summary>
    internal static double GetCurrentAge(HttpCacheEntry entry, DateTimeOffset now)
    {
        var dateValue = ParseHttpDate(HeaderList.Get(entry.Headers, "Date")) ?? entry.ResponseTime;
        var ageValue = TryParseDeltaSeconds(HeaderList.Get(entry.Headers, "Age"), out var age) ? age : 0;
        var apparentAge = Math.Max(0, (entry.ResponseTime - dateValue).TotalSeconds);
        var responseDelay = (entry.ResponseTime - entry.RequestTime).TotalSeconds;
        var correctedInitialAge = Math.Max(apparentAge, ageValue + responseDelay);
        var residentTime = (now - entry.ResponseTime).TotalSeconds;
        return correctedInitialAge + residentTime;
    }

    /// <summary>The directives of the <c>Cache-Control</c> headers (https://httpwg.org/specs/rfc9111.html#field.cache-control): the lowercase names and their values (unquoted), or null.</summary>
    internal static Dictionary<string, string?> ParseCacheControl(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in headers)
        {
            if (!InfraAscii.EqualsIgnoreAsciiCase(name, "Cache-Control"))
                continue;

            foreach (var part in SplitDirectives(value))
            {
                var equals = part.IndexOf('=', StringComparison.Ordinal);
                var directive = InfraAscii.ToAsciiLowercase((equals < 0 ? part : part[..equals]).Trim(' ', '\t'));
                if (directive.Length == 0)
                    continue;

                string? argument = null;
                if (equals >= 0)
                {
                    argument = part[(equals + 1)..].Trim(' ', '\t');
                    if (argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"')
                    {
                        argument = argument[1..^1];
                    }
                }

                // The first occurrence of a directive is used
                result.TryAdd(directive, argument);
            }
        }

        return result;
    }

    /// <summary>The directives of a <c>Cache-Control</c> value, separated by the commas that are not in a quoted string.</summary>
    private static List<string> SplitDirectives(string value)
    {
        var result = new List<string>();
        var start = 0;
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '"':
                    quoted = !quoted;
                    break;
                case '\\' when quoted:
                    i++;
                    break;
                case ',' when !quoted:
                    result.Add(value[start..i]);
                    start = i + 1;
                    break;
            }
        }

        result.Add(value[start..]);
        return result;
    }

    /// <summary>The delta-seconds value of a directive (https://httpwg.org/specs/rfc9111.html#delta-seconds), or null when it is missing or invalid.</summary>
    private static double? GetDeltaSeconds(Dictionary<string, string?> directives, string name)
        => directives.TryGetValue(name, out var value) && TryParseDeltaSeconds(value, out var seconds) ? seconds : null;

    /// <summary>A non-negative integer of digits; a value too large is the greatest value (https://httpwg.org/specs/rfc9111.html#delta-seconds).</summary>
    private static bool TryParseDeltaSeconds(string? value, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        seconds = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;
        return true;
    }

    private static readonly string[] HttpDateFormats =
    [
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        "dddd, dd-MMM-yy HH:mm:ss 'GMT'",
        "ddd MMM d HH:mm:ss yyyy",
        "ddd MMM dd HH:mm:ss yyyy",
        "ddd, d MMM yyyy HH:mm:ss 'GMT'",
    ];

    /// <summary>An HTTP-date (https://httpwg.org/specs/rfc9110.html#http.date): the IMF-fixdate, RFC 850 and asctime formats, or null.</summary>
    internal static DateTimeOffset? ParseHttpDate(string? value)
    {
        if (value is null)
            return null;

        return DateTimeOffset.TryParseExact(value.Trim(), HttpDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowInnerWhite, out var date) ? date : null;
    }
}

/// <summary>A stored response of the <see cref="HttpCache"/>, with the headers of its request (for <c>Vary</c>) and the times of the request and of the response.</summary>
internal sealed record HttpCacheEntry(
    bool IncludeCredentials,
    IReadOnlyList<KeyValuePair<string, string>> RequestHeaders,
    int Status,
    string StatusMessage,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body,
    DateTimeOffset RequestTime,
    DateTimeOffset ResponseTime);

/// <summary>
/// The rest of a body that arrives over time (<see cref="DocumentResource.RemainingContentBytes"/>) of a response that the
/// <see cref="HttpCache"/> stores: the chunks are kept as they are read, and the complete body is given to the callback at
/// the end of the body (a body that fails, that is not fully read or that is too long is not stored).
/// </summary>
internal sealed class HttpCacheBody(ReadOnlyMemory<byte> first, IAsyncEnumerable<ReadOnlyMemory<byte>> remaining, Action<byte[]> store) : IAsyncEnumerable<ReadOnlyMemory<byte>>
{
    public async IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var body = new ArrayBufferWriter<byte>();
        body.Write(first.Span);
        var tooLong = false;
        await foreach (var chunk in remaining.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!tooLong)
            {
                tooLong = body.WrittenCount + chunk.Length > HttpCache.MaxBodyLength;
                if (!tooLong)
                {
                    body.Write(chunk.Span);
                }
            }

            yield return chunk;
        }

        if (!tooLong)
        {
            store(body.WrittenSpan.ToArray());
        }
    }
}
