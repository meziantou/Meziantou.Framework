using System.Net;
using System.Text.Json;

namespace Meziantou.Framework.Http.Caching;

internal sealed class HttpCache
{
    private readonly IHttpCacheStore _persistenceProvider;
    private readonly HttpCachingOptions _options;

    public HttpCache(IHttpCacheStore persistenceProvider, HttpCachingOptions options)
    {
        ArgumentNullException.ThrowIfNull(persistenceProvider);
        ArgumentNullException.ThrowIfNull(options);

        _persistenceProvider = persistenceProvider;
        _options = options;
    }

    // RFC 9111 Section 2: the primary cache key is composed of the request method and the target URI.
    // GET and HEAD must not share a key, otherwise a stored HEAD response (which has no body) could be
    // served for a subsequent GET.
    private static string ComputePrimaryKey(HttpMethod method, Uri uri)
    {
        return method.Method + " " + uri.GetLeftPart(UriPartial.Query);
    }

    // draft-ietf-httpbis-no-vary-search Section 7: a response that declares a URL variation config can be
    // reused for any equivalent URL, so it cannot be stored under a key that contains the query. Those
    // responses get their own key, built from the URL without its query. The marker holds a space, which a
    // URI never does because Uri escapes it, so the key can never collide with that of a query-less URL.
    private static string ComputeNoVarySearchKey(HttpMethod method, Uri uri)
    {
        return method.Method + " " + uri.GetLeftPart(UriPartial.Path) + " no-vary-search";
    }

    public async ValueTask<CacheEntry?> TryGetAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null)
            return null;

        // An entry stored under the exact target URI always matches, so it is looked up first. Section 7 of
        // the No-Vary-Search draft allows preferring it over an entry that only matches modulo its config,
        // and it keeps the cost of a cache hit to a single lookup for responses without the header.
        var exactMatch = await TryGetAsync(ComputePrimaryKey(request.Method, uri), request, uri, matchQuery: false, cancellationToken).ConfigureAwait(false);
        if (exactMatch is not null)
            return exactMatch;

        return await TryGetAsync(ComputeNoVarySearchKey(request.Method, uri), request, uri, matchQuery: true, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CacheEntry?> TryGetAsync(string key, HttpRequestMessage request, Uri uri, bool matchQuery, CancellationToken cancellationToken)
    {
        var persistedEntries = await _persistenceProvider.GetEntriesAsync(key, cancellationToken).ConfigureAwait(false);
        if (persistedEntries.Count is 0)
            return null;

        // Find the best matching entry considering Vary headers
        CacheEntry? bestMatch = null;
        DateTimeOffset latestDate = DateTimeOffset.MinValue;

        foreach (var persistedEntry in persistedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CacheEntry entry;
            try
            {
                entry = CacheEntry.FromPersistenceEntry(persistedEntry);
            }
            catch (JsonException)
            {
                // The stored payload is corrupted: ignore the entry and treat it as a miss.
                continue;
            }

            // Check secondary key (Vary headers) match
            if (!entry.SecondaryKey.MatchRequest(request))
                continue;

            // draft-ietf-httpbis-no-vary-search Section 6: the entries stored under this key were kept for
            // other queries of the same path, and only answer requests for an equivalent URL.
            if (matchQuery && !entry.MatchesQuery(uri))
                continue;

            // RFC 7234 Section 4: Use most recent response by Date header
            // draft-ietf-httpbis-no-vary-search Section 7: preferring the most recent Date also makes caches
            // converge on the latest config when several of them are stored.
            if (entry.ResponseDate > latestDate)
            {
                latestDate = entry.ResponseDate;
                bestMatch = entry;
            }
        }

        return bestMatch;
    }

    public async ValueTask StoreAsync(HttpRequestMessage request, HttpResponseMessage response, DateTimeOffset requestTime, DateTimeOffset responseTime, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
            return;

        // RFC 7234 Section 3: Determine if response is cacheable
        if (!IsCacheable(request, response))
            return;

        // Check if response should be cached based on custom predicate
        if (_options.ShouldCacheResponse is not null && !_options.ShouldCacheResponse(response))
            return;

        // Check the announced size before reading anything. Buffering a multi-gigabyte response only to
        // discard it is a needless allocation, and serialization would grow it further. A response that
        // announces no Content-Length is bounded while it is read instead, by BoundedContentReader.
        var maximumResponseSize = _options.MaximumResponseSize;
        if (maximumResponseSize is not null && response.Content?.Headers.ContentLength > maximumResponseSize.GetValueOrDefault())
            return;

        // The size limit applies to the serialized entry, which includes headers and metadata.
        var entry = await CacheEntry.CreateAsync(request, response, requestTime, responseTime, maximumResponseSize, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return;

        await _persistenceProvider.SetEntryAsync(ComputeStorageKey(request.Method, request.RequestUri, entry), entry.ToPersistenceEntry(), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PersistEntryAsync(HttpMethod method, Uri? uri, CacheEntry entry, CancellationToken cancellationToken)
    {
        if (uri is null)
            return ValueTask.CompletedTask;

        return _persistenceProvider.SetEntryAsync(ComputeStorageKey(method, uri, entry), entry.ToPersistenceEntry(), cancellationToken);
    }

    private static string ComputeStorageKey(HttpMethod method, Uri uri, CacheEntry entry)
    {
        return entry.VariationConfig.IsDefault ? ComputePrimaryKey(method, uri) : ComputeNoVarySearchKey(method, uri);
    }

    public async ValueTask InvalidateAsync(Uri? uri, CancellationToken cancellationToken)
    {
        if (uri is null)
            return;

        // The primary key includes the request method, so every cacheable method must be invalidated.
        await _persistenceProvider.RemoveEntriesAsync(ComputePrimaryKey(HttpMethod.Get, uri), cancellationToken).ConfigureAwait(false);
        await _persistenceProvider.RemoveEntriesAsync(ComputePrimaryKey(HttpMethod.Head, uri), cancellationToken).ConfigureAwait(false);

        // draft-ietf-httpbis-no-vary-search Section 7: invalidating the URLs that are only equivalent modulo
        // a variation config is optional. Dropping the whole key is the conservative choice: the entries it
        // holds are keyed on the URL without its query, so the query of the invalidated URL says nothing
        // about which of them are still valid.
        await _persistenceProvider.RemoveEntriesAsync(ComputeNoVarySearchKey(HttpMethod.Get, uri), cancellationToken).ConfigureAwait(false);
        await _persistenceProvider.RemoveEntriesAsync(ComputeNoVarySearchKey(HttpMethod.Head, uri), cancellationToken).ConfigureAwait(false);
    }

    private static bool IsCacheable(HttpRequestMessage request, HttpResponseMessage response)
    {
        // RFC 7234 Section 3: A cache MUST NOT store a response unless:

        // The request method is GET or HEAD (cacheable methods)
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return false;

        // The response status code is cacheable
        if (!IsCacheableStatusCode(response.StatusCode))
            return false;

        // RFC 9111 Section 4.1: a stored response with "Vary: *" can never be selected for any request, so
        // storing it is pure waste. This is checked before the body is read rather than after.
        var vary = response.Headers.Vary;
        if (vary.Count > 0 && vary.Contains("*"))
            return false;

        var requestCacheControl = request.Headers.CacheControl;
        var responseCacheControl = response.Headers.CacheControl;

        // no-store directive in request
        if ((requestCacheControl?.NoStore) is true)
            return false;

        // no-store directive in response
        if ((responseCacheControl?.NoStore) is true)
            return false;

        // RFC 9111 Section 3.5: the restriction on responses to a request carrying an Authorization header
        // applies to shared caches only. This is a private cache attached to a single HttpClient, so such a
        // response is stored under the same rules as any other. It remains the caller's responsibility not
        // to share one HttpClient, and therefore one cache, between users.

        // Check if response has explicit freshness information or is cacheable by default
        var hasExplicitFreshness = responseCacheControl?.MaxAge is not null ||
                                   responseCacheControl?.SharedMaxAge is not null ||
                                   responseCacheControl?.Public is true;

        // Validate Expires header if present and no Cache-Control freshness
        if (!hasExplicitFreshness && HasExpiresHeader(response))
        {
            var expires = CacheEntry.ParseExpiresHeader(response);
            var date = response.Headers.Date ?? DateTimeOffset.UtcNow;

            // If Expires is valid and in the future, it counts as explicit freshness
            if (expires.HasValue && expires.Value > date)
            {
                hasExplicitFreshness = true;
            }
            // If Expires is expired or invalid, don't cache unless status is cacheable by default
        }

        if (!hasExplicitFreshness && !HasDefaultCacheableStatusCode(response.StatusCode))
            return false;

        return true;
    }

    private static bool IsCacheableStatusCode(HttpStatusCode status)
    {
        // RFC 7234 Section 3: Cacheable status codes
        // RFC 7231 Section 6.1: These can be cached with explicit caching directives
        // 206 is excluded: Range requests bypass the cache, so a stored partial response could only ever
        // be replayed to a request that asked for the full representation.
        return status switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.NonAuthoritativeInformation => true,
            HttpStatusCode.NoContent => true,
            HttpStatusCode.MultipleChoices => true,
            HttpStatusCode.MovedPermanently => true,
            HttpStatusCode.NotFound => true,
            HttpStatusCode.MethodNotAllowed => true,
            HttpStatusCode.Gone => true,
            HttpStatusCode.RequestUriTooLong => true,
            HttpStatusCode.NotImplemented => true,
            HttpStatusCode.InternalServerError => true,
            _ => false,
        };
    }

    private static bool HasDefaultCacheableStatusCode(HttpStatusCode status)
    {
        // RFC 7231 Section 6.1: Status codes that are cacheable by default
        return status switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.NonAuthoritativeInformation => true,
            HttpStatusCode.NoContent => true,
            HttpStatusCode.MultipleChoices => true,
            HttpStatusCode.MovedPermanently => true,
            HttpStatusCode.NotFound => true,
            HttpStatusCode.MethodNotAllowed => true,
            HttpStatusCode.Gone => true,
            HttpStatusCode.RequestUriTooLong => true,
            HttpStatusCode.NotImplemented => true,
            _ => false,
        };
    }

    private static bool HasExpiresHeader(HttpResponseMessage response)
    {
        // Expires can be on content headers or response headers depending on how it was added
        return response.Content.Headers.TryGetValues("Expires", out _) ||
               response.Headers.TryGetValues("Expires", out _);
    }
}
