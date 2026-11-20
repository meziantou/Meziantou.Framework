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
/// A partial response (<c>206</c>) with a single range is stored (https://httpwg.org/specs/rfc9111.html#incomplete.responses):
/// it answers the range requests whose range it contains, and a request without range completes it with a range request of
/// the rest of the representation. A stored complete response answers the range requests too. A <c>HEAD</c> request does not
/// use the stored response of a <c>GET</c> request. The cache is used by the threads of the workers.
/// </remarks>
internal sealed class HttpCache
{
    /// <summary>The longest body that is stored.</summary>
    internal const int MaxBodyLength = 16 * 1024 * 1024;

    // https://httpwg.org/specs/rfc9110.html#overview.of.status.codes: the statuses that are heuristically cacheable
    private static readonly HashSet<int> HeuristicallyCacheableStatuses = [200, 203, 204, 206, 300, 301, 308, 404, 405, 410, 414, 501];

    // https://httpwg.org/specs/rfc9111.html#update: the headers of a 304 response that do not update the stored response
    private static readonly HashSet<string> NotUpdatedHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length", "Content-Encoding", "Content-Range", "Transfer-Encoding", "TE", "Connection", "Keep-Alive", "Proxy-Connection", "Trailer", "Upgrade",
    };

    private static readonly ConditionalWeakTable<DocumentLoader, HttpCache> Caches = [];

    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<HttpCacheEntry>> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revalidations = new(StringComparer.Ordinal);

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

            // A partial response does not replace a stored complete response of the same request
            if (entry.Status == 206 && entries.Exists(existing => existing.Status != 206 && existing.IncludeCredentials == entry.IncludeCredentials && MatchesVary(existing, entry.RequestHeaders)))
                return;

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
    /// Starts the background revalidation of a stored response used within its <c>stale-while-revalidate</c> window
    /// (https://httpwg.org/specs/rfc5861.html#n-the-stale-while-revalidate-cache-control-extension). Returns false when a
    /// revalidation of the same request is already in progress: the stored response is used without another one.
    /// </summary>
    public bool TryBeginRevalidation(string key, bool includeCredentials)
    {
        lock (_lock)
        {
            return _revalidations.Add(GetRevalidationKey(key, includeCredentials));
        }
    }

    /// <summary>Ends a background revalidation started by <see cref="TryBeginRevalidation"/>.</summary>
    public void EndRevalidation(string key, bool includeCredentials)
    {
        lock (_lock)
        {
            _revalidations.Remove(GetRevalidationKey(key, includeCredentials));
        }
    }

    private static string GetRevalidationKey(string key, bool includeCredentials) => (includeCredentials ? "1" : "0") + key;

    /// <summary>The partition of a key (<see cref="GetKey"/>): the top-level site and the cross-site bit.</summary>
    public static string GetPartition(string key) => key[..key.IndexOf('\n', StringComparison.Ordinal)];

    /// <summary>
    /// Removes the stored responses of the URLs of an origin in a partition (<see cref="GetPartition"/>,
    /// https://w3c.github.io/webappsec-clear-site-data/#clear-cache). Returns the number of URLs whose responses were removed.
    /// </summary>
    public int ClearOrigin(string origin, string partition)
    {
        lock (_lock)
        {
            var removed = 0;
            foreach (var key in _entries.Keys.ToArray())
            {
                var separator = key.IndexOf('\n', StringComparison.Ordinal);
                if (separator < 0 || !key.AsSpan(0, separator).SequenceEqual(partition))
                    continue;

                if (UrlParser.Parse(key[(separator + 1)..], baseUrl: null) is not { } url || url.SerializeOrigin() != origin)
                    continue;

                _entries.Remove(key);
                removed++;
            }

            return removed;
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
        if (status is < 200 or 304 || status > 999)
            return false;

        // A partial response is stored when it is the response of a GET request and has a single byte range of a
        // representation whose length is known
        if (status == 206 && (method != "GET" || ParseContentRange(responseHeaders) is null))
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
    /// The byte range of the representation that a stored response contains: all the representation for a <c>200</c>
    /// response, the range of the <c>Content-Range</c> header of a <c>206</c> response (an incomplete partial response,
    /// whose body is shorter than its range, contains the bytes of its body), or null.
    /// </summary>
    public static (long First, long Last, long CompleteLength)? GetStoredRange(HttpCacheEntry entry)
    {
        if (entry.Status == 200)
            return entry.Body.Length == 0 ? null : (0, entry.Body.Length - 1, entry.Body.Length);

        if (entry.Status != 206 || ParseContentRange(entry.Headers) is not var (first, last, completeLength) || entry.Body.Length == 0)
            return null;

        return (first, Math.Min(last, first + entry.Body.Length - 1), completeLength);
    }

    /// <summary>
    /// The <c>206</c> response that a stored response (<c>200</c> or <c>206</c>) gives to a request whose <c>Range</c> header has
    /// a single byte range (https://httpwg.org/specs/rfc9110.html#field.range), or null when the stored response does not
    /// contain all the bytes of the range (or when the range cannot be satisfied): the request is sent to the network.
    /// </summary>
    public static HttpCacheEntry? GetRangeEntry(HttpCacheEntry entry, string rangeHeader)
    {
        if (GetStoredRange(entry) is not var (first, last, completeLength) || FetchHeaders.ParseSingleRangeHeaderValue(rangeHeader, allowWhitespace: true) is not var (rangeStart, rangeEnd))
            return null;

        long start;
        long end;
        if (rangeStart is null)
        {
            if (rangeEnd is 0)
                return null;

            start = Math.Max(completeLength - rangeEnd!.Value, 0);
            end = completeLength - 1;
        }
        else
        {
            start = rangeStart.Value;
            end = rangeEnd is { } value ? Math.Min(value, completeLength - 1) : completeLength - 1;
        }

        if (start > end || start < first || end > last)
            return null;

        var headers = new List<KeyValuePair<string, string>>(entry.Headers.Count + 2);
        foreach (var header in entry.Headers)
        {
            if (!header.Key.Equals("Content-Range", StringComparison.OrdinalIgnoreCase) && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                headers.Add(header);
            }
        }

        var body = entry.Body.AsSpan((int)(start - first), (int)(end - start + 1)).ToArray();
        headers.Add(new("Content-Range", FormatContentRange(start, end, completeLength)));
        headers.Add(new("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)));
        return entry with { Status = 206, StatusMessage = "Partial Content", Headers = headers, Body = body };
    }

    /// <summary>The value of a <c>Content-Range</c> header of a byte range (https://httpwg.org/specs/rfc9110.html#field.content-range).</summary>
    public static string FormatContentRange(long first, long last, long completeLength)
        => "bytes " + first.ToString(CultureInfo.InvariantCulture) + "-" + last.ToString(CultureInfo.InvariantCulture) + "/" + completeLength.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses the <c>Content-Range</c> header of a <c>206</c> response (https://httpwg.org/specs/rfc9110.html#field.content-range):
    /// a single byte range <c>bytes first-last/complete-length</c> whose complete length is known, or null.
    /// </summary>
    public static (long First, long Last, long CompleteLength)? ParseContentRange(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (HeaderList.Get(headers, "Content-Range") is not { } value)
            return null;

        var span = value.AsSpan().Trim(" \t");
        if (!span.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
            return null;

        span = span[6..].TrimStart(' ');
        var dash = span.IndexOf('-');
        var slash = span.IndexOf('/');
        if (dash <= 0 || slash <= dash + 1)
            return null;

        if (!TryParseDigits(span[..dash], out var first) || !TryParseDigits(span[(dash + 1)..slash], out var last) || !TryParseDigits(span[(slash + 1)..], out var completeLength))
            return null;

        if (first > last || last >= completeLength)
            return null;

        return (first, last, completeLength);

        static bool TryParseDigits(ReadOnlySpan<char> digits, out long result)
        {
            result = 0;
            if (digits.IsEmpty || digits.Length > 18)
                return false;

            foreach (var c in digits)
            {
                if (c is < '0' or > '9')
                    return false;

                result = (result * 10) + (c - '0');
            }

            return true;
        }
    }

    /// <summary>Whether a stored response can be used without validation (<see cref="GetFreshness"/> is <see cref="HttpCacheFreshness.Fresh"/>).</summary>
    public static bool IsFresh(HttpCacheEntry entry, IReadOnlyList<KeyValuePair<string, string>> requestHeaders, DateTimeOffset now)
        => GetFreshness(entry, requestHeaders, now) == HttpCacheFreshness.Fresh;

    /// <summary>
    /// Whether a stored response can be used without validation (https://httpwg.org/specs/rfc9111.html#expiration.model),
    /// given the directives of the request (https://httpwg.org/specs/rfc9111.html#cache-request-directive): its current age
    /// is less than its freshness lifetime, and the <c>max-age</c>, <c>min-fresh</c> and <c>max-stale</c> directives of the
    /// request are satisfied. A <c>no-cache</c> directive (or <c>Pragma: no-cache</c>) always requires a validation. A
    /// stale response whose staleness is within its <c>stale-while-revalidate</c> window
    /// (https://httpwg.org/specs/rfc5861.html#n-the-stale-while-revalidate-cache-control-extension) can be used while it is
    /// revalidated in the background, unless it must be revalidated or the request constrains its age.
    /// </summary>
    public static HttpCacheFreshness GetFreshness(HttpCacheEntry entry, IReadOnlyList<KeyValuePair<string, string>> requestHeaders, DateTimeOffset now)
    {
        var requestDirectives = ParseCacheControl(requestHeaders);
        if (requestDirectives.ContainsKey("no-cache") || (requestDirectives.Count == 0 && HeaderList.Get(requestHeaders, "Pragma") is { } pragma && pragma.Contains("no-cache", StringComparison.OrdinalIgnoreCase)))
            return HttpCacheFreshness.Stale;

        // As in the browsers, a response with Pragma: no-cache is always validated, unless it is immutable
        var responseDirectives = ParseCacheControl(entry.Headers);
        if (responseDirectives.ContainsKey("no-cache") || (!responseDirectives.ContainsKey("immutable") && HeaderList.Get(entry.Headers, "Pragma") is { } responsePragma && responsePragma.Contains("no-cache", StringComparison.OrdinalIgnoreCase)))
            return HttpCacheFreshness.Stale;

        var lifetime = GetFreshnessLifetime(entry, responseDirectives);
        var age = GetCurrentAge(entry, now);
        if (GetDeltaSeconds(requestDirectives, "max-age") is { } maxAge && age > maxAge)
            return HttpCacheFreshness.Stale;

        if (GetDeltaSeconds(requestDirectives, "min-fresh") is { } minFresh && lifetime - age < minFresh)
            return HttpCacheFreshness.Stale;

        if (lifetime > age)
            return HttpCacheFreshness.Fresh;

        if (responseDirectives.ContainsKey("must-revalidate"))
            return HttpCacheFreshness.Stale;

        // A stale response is used with max-stale
        if (requestDirectives.TryGetValue("max-stale", out var maxStaleValue) && (maxStaleValue is null || (TryParseDeltaSeconds(maxStaleValue, out var maxStale) && age - lifetime <= maxStale)))
            return HttpCacheFreshness.Fresh;

        // As in the browsers, a request that sets a maximum age (e.g. the "no-cache" mode of fetch()) waits for the validation
        if (!requestDirectives.ContainsKey("max-age") && GetDeltaSeconds(responseDirectives, "stale-while-revalidate") is { } staleWhileRevalidate && age - lifetime <= staleWhileRevalidate)
            return HttpCacheFreshness.StaleWhileRevalidate;

        return HttpCacheFreshness.Stale;
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

/// <summary>Whether a stored response of the <see cref="HttpCache"/> can be used (<see cref="HttpCache.GetFreshness"/>).</summary>
internal enum HttpCacheFreshness
{
    /// <summary>The stored response must be validated before it is used.</summary>
    Stale,

    /// <summary>The stored response is used without validation.</summary>
    Fresh,

    /// <summary>The stored response is used, and it is revalidated in the background (<c>stale-while-revalidate</c>).</summary>
    StaleWhileRevalidate,
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
