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
    private static string ComputePrimaryKey(HttpMethod method, Uri? uri)
    {
        return method.Method + " " + (uri?.GetLeftPart(UriPartial.Query) ?? string.Empty);
    }

    public async ValueTask<CacheEntry?> TryGetAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri == null)
            return null;

        var primaryKey = ComputePrimaryKey(request.Method, request.RequestUri);
        var persistedEntries = await _persistenceProvider.GetEntriesAsync(primaryKey, cancellationToken).ConfigureAwait(false);
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

            // RFC 7234 Section 4: Use most recent response by Date header
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
        if (request.RequestUri == null)
            return;

        // RFC 7234 Section 3: Determine if response is cacheable
        if (!IsCacheable(request, response))
            return;

        // Check if response should be cached based on custom predicate
        if (_options.ShouldCacheResponse is not null && !_options.ShouldCacheResponse(response))
            return;

        // Check the announced size before reading anything. Buffering a multi-gigabyte response only to
        // discard it is a needless allocation, and serialization would grow it further.
        var maximumResponseSize = _options.MaximumResponseSize;
        if (maximumResponseSize is not null && response.Content?.Headers.ContentLength > maximumResponseSize.GetValueOrDefault())
            return;

        var primaryKey = ComputePrimaryKey(request.Method, request.RequestUri);

        // The size limit applies to the serialized entry, which includes headers and metadata.
        var entry = await CacheEntry.CreateAsync(request, response, requestTime, responseTime, maximumResponseSize, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return;

        await _persistenceProvider.SetEntryAsync(primaryKey, entry.ToPersistenceEntry(), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PersistEntryAsync(HttpMethod method, Uri? uri, CacheEntry entry, CancellationToken cancellationToken)
    {
        if (uri is null)
            return ValueTask.CompletedTask;

        var primaryKey = ComputePrimaryKey(method, uri);
        return _persistenceProvider.SetEntryAsync(primaryKey, entry.ToPersistenceEntry(), cancellationToken);
    }

    public async ValueTask InvalidateAsync(Uri? uri, CancellationToken cancellationToken)
    {
        if (uri is null)
            return;

        // The primary key includes the request method, so every cacheable method must be invalidated.
        await _persistenceProvider.RemoveEntriesAsync(ComputePrimaryKey(HttpMethod.Get, uri), cancellationToken).ConfigureAwait(false);
        await _persistenceProvider.RemoveEntriesAsync(ComputePrimaryKey(HttpMethod.Head, uri), cancellationToken).ConfigureAwait(false);
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

        var requestCacheControl = request.Headers.CacheControl;
        var responseCacheControl = response.Headers.CacheControl;

        // no-store directive in request
        if ((requestCacheControl?.NoStore) is true)
            return false;

        // no-store directive in response
        if ((responseCacheControl?.NoStore) is true)
            return false;

        // Authorization header without explicit cacheable directive
        if (request.Headers.Authorization is not null)
        {
            // RFC 7234 Section 3.2: Must have must-revalidate, public, or s-maxage
            if (responseCacheControl is null)
                return false;

            if (!responseCacheControl.MustRevalidate &&
                !responseCacheControl.Public &&
                responseCacheControl.SharedMaxAge == null)
                return false;
        }

        // Check if response has explicit freshness information or is cacheable by default
        var hasExplicitFreshness = responseCacheControl?.MaxAge != null ||
                                   responseCacheControl?.SharedMaxAge != null ||
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
