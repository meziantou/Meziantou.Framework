using System.Net;
using System.Net.Http.Headers;

namespace HttpCaching;

/// <summary>A delegating handler that caches HTTP responses following RFC 7234.</summary>
public sealed class CachingDelegateHandler : DelegatingHandler
{
    private readonly HttpCache _cache = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingDelegateHandler"/> class.
    /// </summary>
    public CachingDelegateHandler()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingDelegateHandler"/> class with a time provider.
    /// </summary>
    /// <param name="timeProvider">The time provider to use for time-based operations.</param>
    public CachingDelegateHandler(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingDelegateHandler"/> class with an inner handler.
    /// </summary>
    /// <param name="innerHandler">The inner handler.</param>
    public CachingDelegateHandler(HttpMessageHandler innerHandler)
        : this(innerHandler, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingDelegateHandler"/> class with an inner handler and time provider.
    /// </summary>
    /// <param name="innerHandler">The inner handler.</param>
    /// <param name="timeProvider">The time provider to use for time-based operations.</param>
    public CachingDelegateHandler(HttpMessageHandler innerHandler, TimeProvider timeProvider)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    // TODO set response.RequestMessage to the original request
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestTime = _timeProvider.GetUtcNow();

        // RFC 7234 Section 4: Only cache GET and HEAD methods
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // RFC 7234 Section 4.4: Invalidation on unsafe methods
            if (!IsMethodSafe(request.Method) && IsSuccessStatusCode(response.StatusCode))
            {
                _cache.Invalidate(request.RequestUri);
                InvalidateLocationHeaders(response);
            }

            return response;
        }

        // Check request-level cache directives
        var requestCacheControl = request.Headers.CacheControl;
        var hasPragmaNoCache = HasPragmaNoCache(request.Headers);

        // RFC 7234 Section 5.2.1.5: no-store request directive
        if ((requestCacheControl?.NoStore) is true)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // Try to get a cached response
        var cacheResult = _cache.TryGet(request);

        if (cacheResult != null)
        {
            var currentAge = cacheResult.CalculateCurrentAge(requestTime);
            var freshnessLifetime = cacheResult.FreshnessLifetime;
            var isFresh = freshnessLifetime > currentAge;

            // RFC 8246: immutable directive indicates response will not change
            // When response is fresh and immutable, skip validation even if no-cache is present
            var isImmutableAndFresh = cacheResult.Immutable && isFresh;

            // RFC 7234 Section 5.2.1.4 & 5.4: no-cache directive or Pragma: no-cache
            // RFC 8246: immutable overrides no-cache when response is still fresh
            var requiresValidation = !isImmutableAndFresh && 
                                     ((requestCacheControl?.NoCache) is true ||
                                      (hasPragmaNoCache && requestCacheControl is null));

            // RFC 7234 Section 5.2.1.1: max-age request directive
            if (requestCacheControl?.MaxAge != null)
            {
                var maxAgeSeconds = requestCacheControl.MaxAge.Value.TotalSeconds;
                if (currentAge.TotalSeconds > maxAgeSeconds)
                {
                    isFresh = false;
                }
            }

            // RFC 7234 Section 5.2.1.3: min-fresh request directive
            if (requestCacheControl?.MinFresh != null && isFresh)
            {
                var minFresh = requestCacheControl.MinFresh.Value;
                var remainingFreshness = freshnessLifetime - currentAge;
                if (remainingFreshness < minFresh)
                {
                    isFresh = false;
                }
            }

            // RFC 7234 Section 5.2.2.1: must-revalidate response directive
            // RFC 8246: must-revalidate takes precedence over immutable when stale
            if (cacheResult.MustRevalidate && !isFresh)
            {
                requiresValidation = true;
            }

            // RFC 7234 Section 5.2.2.2: no-cache response directive
            // RFC 8246: immutable prevents validation only when fresh
            if (cacheResult.ResponseNoCache && !isImmutableAndFresh)
            {
                requiresValidation = true;
            }

            // RFC 7234 Section 5.2.1.2: max-stale request directive allows stale responses
            var allowStale = false;
            if ((requestCacheControl?.MaxStale) is true && !cacheResult.MustRevalidate)
            {
                if (requestCacheControl.MaxStaleLimit != null)
                {
                    var staleness = currentAge - freshnessLifetime;
                    if (staleness <= requestCacheControl.MaxStaleLimit.Value)
                    {
                        allowStale = true;
                    }
                }
                else
                {
                    allowStale = true; // Accept any staleness
                }
            }

            // Return cached response if fresh and doesn't require validation
            if (isFresh && !requiresValidation)
            {
                return CreateCachedResponse(cacheResult, currentAge);
            }

            // RFC 7234 Section 5.2.1.7: only-if-cached request directive
            if ((requestCacheControl?.OnlyIfCached) is true)
            {
                if (isFresh || allowStale)
                {
                    return CreateCachedResponse(cacheResult, currentAge);
                }
                // Return 504 Gateway Timeout if no suitable cached response
                return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
            }

            // Allow stale response if max-stale permits
            if (allowStale && !requiresValidation)
            {
                return CreateCachedResponse(cacheResult, currentAge);
            }

            // Attempt conditional validation
            if (cacheResult.ETag != null || cacheResult.LastModified != null)
            {
                var conditionalRequest = CloneRequest(request);

                // RFC 7232 Section 3.2: If-None-Match
                if (cacheResult.ETag != null)
                {
                    conditionalRequest.Headers.TryAddWithoutValidation("If-None-Match", cacheResult.ETag);
                }

                // RFC 7232 Section 3.3: If-Modified-Since
                if (cacheResult.LastModified != null && cacheResult.ETag is null)
                {
                    conditionalRequest.Headers.IfModifiedSince = cacheResult.LastModified;
                }

                var validationRequestTime = _timeProvider.GetUtcNow();
                var conditionalResponse = await base.SendAsync(conditionalRequest, cancellationToken).ConfigureAwait(false);
                var responseTime = _timeProvider.GetUtcNow();

                // RFC 7234 Section 4.3.3: Handle 304 Not Modified
                if (conditionalResponse.StatusCode is HttpStatusCode.NotModified)
                {
                    // Update cached entry with new headers from 304 response
                    await cacheResult.UpdateFromValidationResponse(conditionalResponse, validationRequestTime, responseTime, cancellationToken).ConfigureAwait(false);

                    conditionalResponse.Dispose();

                    var newAge = cacheResult.CalculateCurrentAge(_timeProvider.GetUtcNow());
                    return CreateCachedResponse(cacheResult, newAge);
                }

                // RFC 5861: Handle error responses with stale-if-error
                if (cacheResult.StaleIfError is not null && !conditionalResponse.IsSuccessStatusCode)
                {
                    var staleness = currentAge - freshnessLifetime;
                    if (staleness <= cacheResult.StaleIfError.Value)
                    {
                        conditionalResponse.Dispose();
                        
                        // RFC 7234 Section 5.5: Add Warning headers
                        var warnings = "110 - \"Response is Stale\", 111 - \"Revalidation Failed\"";
                        return CreateCachedResponse(cacheResult, currentAge, warnings);
                    }
                }

                // Use fresh response and cache it
                await _cache.StoreAsync(request, conditionalResponse, requestTime, responseTime, cancellationToken).ConfigureAwait(false);
                return conditionalResponse;
            }
        }

        // RFC 7234 Section 5.2.1.7: only-if-cached with no cached response
        if ((requestCacheControl?.OnlyIfCached) is true)
        {
            return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
        }

        // No suitable cached response, send request to origin
        var freshResponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var freshResponseTime = _timeProvider.GetUtcNow();

        await _cache.StoreAsync(request, freshResponse, requestTime, freshResponseTime, cancellationToken).ConfigureAwait(false);
        return freshResponse;
    }

    private static bool IsMethodSafe(HttpMethod method)
    {
        return method == HttpMethod.Get ||
               method == HttpMethod.Head ||
               method == HttpMethod.Options ||
               method == HttpMethod.Trace;
    }

    private static bool IsSuccessStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code >= 200 && code < 400;
    }

    private static bool HasPragmaNoCache(HttpRequestHeaders headers)
    {
        // RFC 7234 Section 5.4: Pragma: no-cache
        if (headers.Pragma is null)
            return false;

        foreach (var pragma in headers.Pragma)
        {
            if (string.Equals(pragma.Name, "no-cache", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void InvalidateLocationHeaders(HttpResponseMessage response)
    {
        // RFC 7234 Section 4.4: Invalidate Location and Content-Location URIs
        if (response.Headers.Location != null)
        {
            var locationUri = GetAbsoluteUri(response.RequestMessage?.RequestUri, response.Headers.Location);
            if (locationUri != null && IsSameHost(response.RequestMessage?.RequestUri, locationUri))
            {
                _cache.Invalidate(locationUri);
            }
        }

        if (response.Content.Headers.ContentLocation != null)
        {
            var contentLocationUri = GetAbsoluteUri(response.RequestMessage?.RequestUri, response.Content.Headers.ContentLocation);
            if (contentLocationUri != null && IsSameHost(response.RequestMessage?.RequestUri, contentLocationUri))
            {
                _cache.Invalidate(contentLocationUri);
            }
        }
    }

    private static Uri? GetAbsoluteUri(Uri? baseUri, Uri relativeOrAbsolute)
    {
        if (relativeOrAbsolute.IsAbsoluteUri)
            return relativeOrAbsolute;

        if (baseUri == null)
            return null;

        return new Uri(baseUri, relativeOrAbsolute);
    }

    private static bool IsSameHost(Uri? uri1, Uri? uri2)
    {
        if (uri1 == null || uri2 == null)
            return false;

        return string.Equals(uri1.Host, uri2.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage CreateCachedResponse(CacheEntry entry, TimeSpan currentAge, string? warningHeaders = null)
    {
        var response = ResponseSerializer.Deserialize(entry.SerializedResponse);

        // RFC 7234 Section 5.5: Add Warning headers if provided
        // Remove any existing Warning headers first to avoid duplication
        if (warningHeaders is not null)
        {
            response.Headers.Remove("Warning");
            response.Headers.TryAddWithoutValidation("Warning", warningHeaders);
        }

        // RFC 7234 Section 5.1: Set Age header
        response.Headers.Age = currentAge;

        // Rebuild Cache-Control header from CacheEntry properties
        // This ensures that updates from 304 responses are reflected
        if (entry.MaxAge is not null || entry.SharedMaxAge is not null || entry.MustRevalidate || entry.ResponseNoCache || entry.Immutable || entry.StaleIfError is not null)
        {
            response.Headers.Remove("Cache-Control");
            var cacheControl = new CacheControlHeaderValue();
            
            if (entry.MaxAge is not null)
            {
                cacheControl.MaxAge = entry.MaxAge;
            }

            if (entry.SharedMaxAge is not null)
            {
                cacheControl.SharedMaxAge = entry.SharedMaxAge;
            }
            
            if (entry.MustRevalidate)
            {
                cacheControl.MustRevalidate = true;
            }
            
            if (entry.ResponseNoCache)
            {
                cacheControl.NoCache = true;
            }
            
            if (entry.Immutable)
            {
                cacheControl.Extensions.Add(new NameValueHeaderValue("immutable"));
            }

            if (entry.StaleIfError is not null)
            {
                cacheControl.Extensions.Add(new NameValueHeaderValue("stale-if-error", ((int)entry.StaleIfError.Value.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
            }
            
            response.Headers.CacheControl = cacheControl;
        }

        return response;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content != null)
        {
            clone.Content = original.Content;
        }

        return clone;
    }
}
