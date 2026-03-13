using System.Net;

namespace Meziantou.Framework.Http;

internal sealed class HttpCache
{
    private readonly IHttpCachePersistenceProvider _persistenceProvider;
    private readonly CachingOptions _options;

    public HttpCache(CachingOptions? options)
    {
        _options = options ?? new();
        _persistenceProvider = _options.PersistenceProvider ?? new DefaultHttpCachePersistenceProvider();
    }

    private static string ComputePrimaryKey(Uri? uri)
    {
        return uri?.GetLeftPart(UriPartial.Query) ?? string.Empty;
    }

    public async ValueTask<CacheEntry?> TryGetAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri == null)
            return null;

        var primaryKey = ComputePrimaryKey(request.RequestUri);
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
            catch
            {
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

        var primaryKey = ComputePrimaryKey(request.RequestUri);
        var entry = await CacheEntry.CreateAsync(request, response, requestTime, responseTime, cancellationToken).ConfigureAwait(false);

        // Check response size limit if configured
        // We check the serialized size which includes headers and metadata
        if (_options.MaximumResponseSize is not null)
        {
            if (entry.SerializedResponse.Length > _options.MaximumResponseSize.Value)
            {
                return;
            }
        }

        await _persistenceProvider.SetEntryAsync(primaryKey, entry.ToPersistenceEntry(), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PersistEntryAsync(Uri? uri, CacheEntry entry, CancellationToken cancellationToken)
    {
        if (uri == null)
            return ValueTask.CompletedTask;

        var primaryKey = ComputePrimaryKey(uri);
        return _persistenceProvider.SetEntryAsync(primaryKey, entry.ToPersistenceEntry(), cancellationToken);
    }

    public ValueTask InvalidateAsync(Uri? uri, CancellationToken cancellationToken)
    {
        if (uri == null)
            return ValueTask.CompletedTask;

        var primaryKey = ComputePrimaryKey(uri);
        return _persistenceProvider.RemoveEntriesAsync(primaryKey, cancellationToken);
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
            var expires = ParseExpiresHeaderForValidation(response);
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
        return status switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.NonAuthoritativeInformation => true,
            HttpStatusCode.NoContent => true,
            HttpStatusCode.PartialContent => true,
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
            HttpStatusCode.PartialContent => true,
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

    private static DateTimeOffset? ParseExpiresHeaderForValidation(HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues("Expires", out var values))
        {
            if (!response.Headers.TryGetValues("Expires", out values))
                return null;
        }

        var expiresValue = values.FirstOrDefault();
        if (string.IsNullOrEmpty(expiresValue))
            return null;

        // RFC 7234 Section 5.3: Invalid dates (including "0") are treated as past
        if (expiresValue is "0" or "-1")
            return DateTimeOffset.MinValue;

        // RFC 7231 Section 7.1.1.1: Try RFC 1123 format first (preferred)
        if (DateTimeOffset.TryParseExact(expiresValue, "R", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return result;

        // RFC 7231: Try RFC 850 format (obsolete but still used)
        // Format: Sunday, 06-Nov-94 08:49:37 GMT
        if (DateTimeOffset.TryParseExact(expiresValue, "dddd, dd-MMM-yy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // Try without GMT suffix in case it was already parsed
        if (DateTimeOffset.TryParseExact(expiresValue, "dddd, dd-MMM-yy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // RFC 7231: Try asctime format (obsolete but still used)
        // Format: Sun Nov  6 08:49:37 1994
        if (DateTimeOffset.TryParseExact(expiresValue, "ddd MMM  d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // Also try single-digit day format for asctime
        if (DateTimeOffset.TryParseExact(expiresValue, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        // Try general parsing as fallback
        if (DateTimeOffset.TryParse(expiresValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return result;

        return DateTimeOffset.MinValue; // Invalid date treated as expired
    }
}
