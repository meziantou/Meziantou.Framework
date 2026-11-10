using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.Http.Caching;

/// <summary>A delegating handler that caches HTTP responses following RFC 7234.</summary>
public sealed class HttpCachingDelegateHandler : DelegatingHandler
{
    private readonly HttpCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly Action<Exception>? _onStoreError;
    private readonly bool _isSharedCache;
    private readonly bool _coalesceRequests;
    private readonly ConcurrentDictionary<string, InFlightRequest> _inFlightRequests = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpCachingDelegateHandler"/> class.
    /// </summary>
    /// <param name="store">The cache store used to persist entries.</param>
    /// <param name="options">The caching options.</param>
    public HttpCachingDelegateHandler(IHttpCacheStore store, HttpCachingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var resolvedOptions = options ?? new();

        _timeProvider = resolvedOptions.TimeProvider;
        _onStoreError = resolvedOptions.OnStoreError;
        _isSharedCache = resolvedOptions.IsSharedCache;
        _coalesceRequests = resolvedOptions.CoalesceRequests;
        _cache = new HttpCache(store, resolvedOptions);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpCachingDelegateHandler"/> class with an inner handler.
    /// </summary>
    /// <param name="innerHandler">The inner handler.</param>
    /// <param name="store">The cache store used to persist entries.</param>
    /// <param name="options">The caching options.</param>
    public HttpCachingDelegateHandler(HttpMessageHandler innerHandler, IHttpCacheStore store, HttpCachingOptions? options = null)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(store);
        var resolvedOptions = options ?? new();

        _timeProvider = resolvedOptions.TimeProvider;
        _onStoreError = resolvedOptions.OnStoreError;
        _isSharedCache = resolvedOptions.IsSharedCache;
        _coalesceRequests = resolvedOptions.CoalesceRequests;
        _cache = new HttpCache(store, resolvedOptions);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. The cache is only available on the asynchronous path.</exception>
    /// <remarks>
    /// <see cref="IHttpCacheStore"/> is an asynchronous interface, so there is no synchronous path through
    /// the cache. <see cref="DelegatingHandler"/> forwards this method to the inner handler by default,
    /// which would send every synchronous request to the origin and store nothing, silently: the caller
    /// would get no caching and no indication of it. Failing here surfaces that at the first call instead.
    /// </remarks>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"{nameof(HttpCachingDelegateHandler)} does not support synchronous requests because the cache store is asynchronous. Use HttpClient.SendAsync or one of the GetAsync/PostAsync overloads.");
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestTime = _timeProvider.GetUtcNow();

        // RFC 7234 Section 4: Only cache GET and HEAD methods
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // RFC 7234 Section 4.4: Invalidation on unsafe methods
            if (!IsMethodSafe(request.Method) && IsNonErrorStatusCode(response.StatusCode))
            {
                try
                {
                    await _cache.InvalidateAsync(request.RequestUri, cancellationToken).ConfigureAwait(false);
                    await InvalidateLocationHeadersAsync(response, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsStoreFailure(ex, cancellationToken))
                {
                    ReportStoreError(ex);
                }
            }

            // Set RequestMessage if it's not already set by the base handler
            response.RequestMessage ??= request;

            return response;
        }

        // RFC 9111 Section 4.3.2: the caller is performing its own validation. Evaluating the precondition
        // against the stored response would turn a conditional request into an unconditional 200, and adding
        // our own validators on top of the caller's would send conflicting preconditions. Forward to origin
        // so the caller gets the 304 or 200 it asked for.
        if (HasConditionalHeaders(request.Headers))
        {
            var conditionalPassthroughResponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            conditionalPassthroughResponse.RequestMessage ??= request;
            return conditionalPassthroughResponse;
        }

        // RFC 7234 Section 5.2.1.5: no-store request directive
        if ((request.Headers.CacheControl?.NoStore) is true)
        {
            var noStoreResponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            noStoreResponse.RequestMessage ??= request;
            return noStoreResponse;
        }

        var coalescing = CreateCoalescingState(request);
        if (coalescing is not null)
        {
            try
            {
                var coalescedResponse = await SendThroughCacheAsync(request, requestTime, coalescing, cancellationToken).ConfigureAwait(false);

                // This request has just registered as the one going to the server. A concurrent request may have stored a
                // response between the lookup and the registration, so the cache is looked up again before going.
                if (coalescedResponse is null && coalescing.HasStarted)
                {
                    coalescedResponse = await SendThroughCacheAsync(request, _timeProvider.GetUtcNow(), coalescing, cancellationToken).ConfigureAwait(false);
                }

                if (coalescedResponse is not null)
                    return coalescedResponse;
            }
            finally
            {
                // The response is stored by now, so the requests waiting for this one can look it up
                coalescing.Complete();
            }

            // A concurrent request for the same URL went to the server while this one waited. Its response is now stored,
            // unless it could not be, or varies on a header this request sends differently: look up the cache again, and go
            // to the server if it still has nothing to serve. That second attempt does not wait again, so that requests
            // whose responses are never stored are not serialized behind each other.
            requestTime = _timeProvider.GetUtcNow();
        }

        var cachedResponse = await SendThroughCacheAsync(request, requestTime, coalescing: null, cancellationToken).ConfigureAwait(false);
        return cachedResponse!;
    }

    /// <summary>Returns the state that lets concurrent requests share a single request to the server, or <see langword="null"/> when the request is not coalesced.</summary>
    private CoalescingState? CreateCoalescingState(HttpRequestMessage request)
    {
        if (!_coalesceRequests || request.RequestUri is null)
            return null;

        // A range request asks the server for part of the representation, whose response is not stored: the requests that
        // wait for it would gain nothing.
        if (request.Headers.Range is not null)
            return null;

        return new CoalescingState(_inFlightRequests, HttpCache.ComputePrimaryKey(request.Method, request.RequestUri));
    }

    /// <summary>
    /// Serves the request from the cache, or from the server. Returns <see langword="null"/> when the request had to go to the
    /// server, but waited for a concurrent request to do it instead, or has just registered as the one going to the server
    /// (<see cref="CoalescingState.HasStarted"/>) and must look the cache up again first.
    /// </summary>
    private async Task<HttpResponseMessage?> SendThroughCacheAsync(HttpRequestMessage request, DateTimeOffset requestTime, CoalescingState? coalescing, CancellationToken cancellationToken)
    {
        // Check request-level cache directives
        var requestCacheControl = request.Headers.CacheControl;
        var hasPragmaNoCache = HasPragmaNoCache(request.Headers);

        // Try to get a cached response
        CacheEntry? cacheResult;
        try
        {
            cacheResult = await _cache.TryGetAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreFailure(ex, cancellationToken))
        {
            // A store that cannot be read is a cache miss, not a failed request.
            ReportStoreError(ex);
            cacheResult = null;
        }

        if (cacheResult is not null)
        {
            var currentAge = cacheResult.CalculateCurrentAge(requestTime);
            var freshnessLifetime = cacheResult.GetFreshnessLifetime(_isSharedCache);
            var mustRevalidateWhenStale = cacheResult.MustRevalidateWhenStale(_isSharedCache);
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
            if (requestCacheControl?.MaxAge is not null)
            {
                var maxAgeSeconds = requestCacheControl.MaxAge.Value.TotalSeconds;
                if (currentAge.TotalSeconds >= maxAgeSeconds)
                {
                    isFresh = false;
                }
            }

            // RFC 7234 Section 5.2.1.3: min-fresh request directive
            if (requestCacheControl?.MinFresh is not null && isFresh)
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
            // RFC 9111 Sections 5.2.2.8 and 5.2.2.10: proxy-revalidate and s-maxage mean the same for a shared cache only.
            if (mustRevalidateWhenStale && !isFresh)
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
            if ((requestCacheControl?.MaxStale) is true && !mustRevalidateWhenStale)
            {
                if (requestCacheControl.MaxStaleLimit is not null)
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
                return CreateCachedResponse(request, cacheResult, currentAge);
            }

            // RFC 7234 Section 5.2.1.7: only-if-cached request directive
            if ((requestCacheControl?.OnlyIfCached) is true)
            {
                if (!requiresValidation && (isFresh || allowStale))
                {
                    // RFC 7234 Section 5.5: Add Warning header for stale response
                    var warningHeader = !isFresh ? "110 - \"Response is Stale\"" : null;
                    return CreateCachedResponse(request, cacheResult, currentAge, warningHeader);
                }
                // Return 504 Gateway Timeout if no suitable cached response
                return new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                {
                    RequestMessage = request,
                };
            }

            // Allow stale response if max-stale permits
            if (allowStale && !requiresValidation)
            {
                // RFC 7234 Section 5.5: Add Warning header for stale response
                return CreateCachedResponse(request, cacheResult, currentAge, "110 - \"Response is Stale\"");
            }

            // Attempt conditional validation
            if (cacheResult.ETag is not null || cacheResult.LastModified is not null)
            {
                if (coalescing is not null && !await coalescing.TryStartAsync(cancellationToken).ConfigureAwait(false))
                    return null;

                var conditionalRequest = CloneRequest(request);
                try
                {
                    // RFC 7232 Section 3.2: If-None-Match
                    if (cacheResult.ETag is not null)
                    {
                        conditionalRequest.Headers.TryAddWithoutValidation("If-None-Match", cacheResult.ETag);
                    }

                    // RFC 7232 Section 3.3: If-Modified-Since
                    if (cacheResult.LastModified is not null)
                    {
                        conditionalRequest.Headers.IfModifiedSince = cacheResult.LastModified;
                    }

                    var validationRequestTime = _timeProvider.GetUtcNow();
                    HttpResponseMessage conditionalResponse;
                    try
                    {
                        conditionalResponse = await base.SendAsync(conditionalRequest, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsOriginUnreachable(ex, cancellationToken) && CanServeStaleOnError(cacheResult, currentAge, freshnessLifetime))
                    {
                        // RFC 5861: the origin could not be reached at all, which is the case stale-if-error
                        // exists for. RFC 7234 Section 5.5: Add Warning headers
                        return CreateCachedResponse(request, cacheResult, currentAge, "110 - \"Response is Stale\", 111 - \"Revalidation Failed\"");
                    }

                    var responseTime = _timeProvider.GetUtcNow();

                    // RFC 7234 Section 4.3.3: Handle 304 Not Modified
                    if (conditionalResponse.StatusCode is HttpStatusCode.NotModified)
                    {
                        // RFC 9111 Section 4.3.4 only allows a 304 to update a stored response when its
                        // validators identify that response. A mismatched validator means this 304 cannot
                        // validate the representation whose body we selected.
                        if (HasMismatchedValidator(cacheResult, conditionalResponse))
                        {
                            conditionalResponse.Dispose();

                            var replacementRequestTime = _timeProvider.GetUtcNow();
                            var replacementResponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                            var replacementResponseTime = _timeProvider.GetUtcNow();
                            await StoreAsync(request, replacementResponse, replacementRequestTime, replacementResponseTime, cancellationToken).ConfigureAwait(false);
                            replacementResponse.RequestMessage ??= request;
                            return replacementResponse;
                        }

                        // Update cached entry with new headers from 304 response
                        var isStorable = await cacheResult.UpdateFromValidationResponse(conditionalResponse, validationRequestTime, responseTime, _isSharedCache, cancellationToken).ConfigureAwait(false);

                        // RFC 9111 Sections 3 and 3.5: the updated response may no longer be storable by a shared cache. It
                        // still answers this request, but the stored entry must not answer the next users.
                        isStorable &= !_isSharedCache || request.Headers.Authorization is null || cacheResult.MustRevalidate || cacheResult.Public || cacheResult.SharedMaxAge is not null;
                        try
                        {
                            if (isStorable)
                            {
                                await _cache.PersistEntryAsync(request.Method, request.RequestUri, cacheResult, cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                await _cache.RemoveEntriesAsync(request.Method, request.RequestUri, cacheResult, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex) when (IsStoreFailure(ex, cancellationToken))
                        {
                            // The refreshed entry could not be written back. The response it validated is
                            // still correct, so it is served anyway.
                            ReportStoreError(ex);
                        }

                        conditionalResponse.Dispose();

                        var newAge = cacheResult.CalculateCurrentAge(_timeProvider.GetUtcNow());
                        return CreateCachedResponse(request, cacheResult, newAge);
                    }

                    // RFC 5861: Handle error responses with stale-if-error
                    if (IsStaleIfErrorStatus(conditionalResponse.StatusCode) && CanServeStaleOnError(cacheResult, currentAge, freshnessLifetime))
                    {
                        conditionalResponse.Dispose();

                        // RFC 7234 Section 5.5: Add Warning headers
                        var warnings = "110 - \"Response is Stale\", 111 - \"Revalidation Failed\"";
                        return CreateCachedResponse(request, cacheResult, currentAge, warnings);
                    }

                    // Use fresh response and cache it. The timing of the validation request is used so the
                    // stored entry is not aged by the time the previous entry spent in the cache.
                    await StoreAsync(request, conditionalResponse, validationRequestTime, responseTime, cancellationToken).ConfigureAwait(false);
                    conditionalResponse.RequestMessage ??= request;
                    return conditionalResponse;
                }
                finally
                {
                    // The clone shares the caller's content. Detach it so disposing the clone does not
                    // dispose content that the caller still owns.
                    conditionalRequest.Content = null;
                    conditionalRequest.Dispose();
                }
            }
        }

        // RFC 7234 Section 5.2.1.7: only-if-cached with no cached response
        if ((requestCacheControl?.OnlyIfCached) is true)
        {
            return new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
            {
                RequestMessage = request,
            };
        }

        if (coalescing is not null && !await coalescing.TryStartAsync(cancellationToken).ConfigureAwait(false))
            return null;

        // No suitable cached response, send request to origin
        HttpResponseMessage freshResponse;
        try
        {
            freshResponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (cacheResult is not null &&
                                   IsOriginUnreachable(ex, cancellationToken) &&
                                   CanServeStaleOnError(cacheResult, cacheResult.CalculateCurrentAge(_timeProvider.GetUtcNow()), cacheResult.GetFreshnessLifetime(_isSharedCache)))
        {
            // RFC 5861: serve the stale entry rather than propagating the transport failure.
            var staleAge = cacheResult.CalculateCurrentAge(_timeProvider.GetUtcNow());
            return CreateCachedResponse(request, cacheResult, staleAge, "110 - \"Response is Stale\", 111 - \"Revalidation Failed\"");
        }

        var freshResponseTime = _timeProvider.GetUtcNow();

        // RFC 5861: an error status from the origin is the situation stale-if-error exists for, exactly as
        // it is on the conditional path. An entry without a validator must not lose the fallback merely
        // because the request could not be made conditional.
        if (cacheResult is not null && IsStaleIfErrorStatus(freshResponse.StatusCode))
        {
            var errorAge = cacheResult.CalculateCurrentAge(freshResponseTime);
            if (CanServeStaleOnError(cacheResult, errorAge, cacheResult.GetFreshnessLifetime(_isSharedCache)))
            {
                freshResponse.Dispose();

                // RFC 7234 Section 5.5: Add Warning headers
                var warnings = "110 - \"Response is Stale\", 111 - \"Revalidation Failed\"";
                return CreateCachedResponse(request, cacheResult, errorAge, warnings);
            }
        }

        await StoreAsync(request, freshResponse, requestTime, freshResponseTime, cancellationToken).ConfigureAwait(false);

        // Set RequestMessage if it's not already set by the base handler
        freshResponse.RequestMessage ??= request;
        return freshResponse;
    }

    /// <summary>Stores the response, reporting rather than propagating a store failure.</summary>
    private async ValueTask StoreAsync(HttpRequestMessage request, HttpResponseMessage response, DateTimeOffset requestTime, DateTimeOffset responseTime, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.StoreAsync(request, response, requestTime, responseTime, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreFailure(ex, cancellationToken))
        {
            // The origin answered. Failing to remember the answer must not turn that into a failed request.
            ReportStoreError(ex);
        }
        catch
        {
            // The caller cancelled while the body was being read for storage, so the response is no longer
            // usable and nothing else will dispose it.
            response.Dispose();
            throw;
        }
    }

    private static bool IsStoreFailure(Exception exception, CancellationToken cancellationToken)
    {
        // A cancellation requested by the caller is not a store failure and must propagate.
        return exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
    }

    private void ReportStoreError(Exception exception)
    {
        _onStoreError?.Invoke(exception);
    }

    private static bool IsMethodSafe(HttpMethod method)
    {
        return method == HttpMethod.Get ||
               method == HttpMethod.Head ||
               method == HttpMethod.Options ||
               method == HttpMethod.Trace;
    }

    private static bool IsNonErrorStatusCode(HttpStatusCode statusCode)
    {
        // RFC 9111 Section 4.4: invalidation happens on a non-error response, which includes redirections.
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

    private async ValueTask InvalidateLocationHeadersAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // RFC 7234 Section 4.4: Invalidate Location and Content-Location URIs
        if (response.Headers.Location is not null)
        {
            var locationUri = GetAbsoluteUri(response.RequestMessage?.RequestUri, response.Headers.Location);
            if (locationUri is not null && IsSameHost(response.RequestMessage?.RequestUri, locationUri))
            {
                await _cache.InvalidateAsync(locationUri, cancellationToken).ConfigureAwait(false);
            }
        }

        if (response.Content.Headers.ContentLocation is not null)
        {
            var contentLocationUri = GetAbsoluteUri(response.RequestMessage?.RequestUri, response.Content.Headers.ContentLocation);
            if (contentLocationUri is not null && IsSameHost(response.RequestMessage?.RequestUri, contentLocationUri))
            {
                await _cache.InvalidateAsync(contentLocationUri, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Uri? GetAbsoluteUri(Uri? baseUri, Uri relativeOrAbsolute)
    {
        if (relativeOrAbsolute.IsAbsoluteUri)
            return relativeOrAbsolute;

        if (baseUri is null)
            return null;

        // The header comes from the origin: a reference that cannot be resolved against the request URI, such as
        // "//host:port" with an invalid port, names no URI to invalidate.
        return Uri.TryCreate(baseUri, relativeOrAbsolute, out var absoluteUri) ? absoluteUri : null;
    }

    private static bool IsSameHost(Uri? uri1, Uri? uri2)
    {
        if (uri1 is null || uri2 is null)
            return false;

        return string.Equals(uri1.Host, uri2.Host, StringComparison.OrdinalIgnoreCase);
    }

    private HttpResponseMessage CreateCachedResponse(HttpRequestMessage request, CacheEntry entry, TimeSpan currentAge, string? warningHeaders = null)
    {
        var response = ResponseSerializer.Deserialize(entry.SerializedResponse, out var content);

        // RFC 9110 Section 14.2: a range request is answered from the stored complete response. The Range header only
        // applies to GET, and to a 200 response; otherwise, and when If-Range does not hold, the complete response is sent.
        if (request.Method == HttpMethod.Get &&
            request.Headers.Range is { } range &&
            response.StatusCode is HttpStatusCode.OK &&
            ByteRanges.EvaluateIfRange(request, entry))
        {
            ByteRanges.Apply(response, content ?? [], range);
        }

        // RFC 7234 Section 5.5.4: Add Warning 113 for heuristic expiration exceeding 24 hours
        if (entry.UsesHeuristicExpiration(_isSharedCache) && entry.GetFreshnessLifetime(_isSharedCache) >= TimeSpan.FromHours(24))
        {
            var warning113 = "113 - \"Heuristic Expiration\"";
            warningHeaders = warningHeaders is not null
                ? $"{warningHeaders}, {warning113}"
                : warning113;
        }

        // RFC 7234 Section 5.5: Add Warning headers if provided
        // Remove any existing Warning headers first to avoid duplication
        if (warningHeaders is not null)
        {
            response.Headers.Remove("Warning");
            response.Headers.TryAddWithoutValidation("Warning", warningHeaders);
        }

        // RFC 7234 Section 5.1: Set Age header
        response.Headers.Age = currentAge;

        // Set RequestMessage on the response
        response.RequestMessage = request;
        return response;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
            // The content is shared with the original request. The caller owns it, so the clone must be
            // detached from it before it is disposed.
            Content = original.Content,
        };

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Request options carry per-request state for the inner handlers (resilience policies,
        // IHttpClientFactory metadata, ...) and must survive revalidation.
        IDictionary<string, object?> cloneOptions = clone.Options;
        foreach (var option in original.Options)
        {
            cloneOptions[option.Key] = option.Value;
        }

        return clone;
    }

    private bool CanServeStaleOnError(CacheEntry entry, TimeSpan currentAge, TimeSpan freshnessLifetime)
    {
        // RFC 5861: stale-if-error allows a stale response to be served for the given amount of time past
        // the point at which it became stale.
        if (entry.StaleIfError is null || entry.MustRevalidateWhenStale(_isSharedCache) || entry.ResponseNoCache)
            return false;

        var staleness = currentAge - freshnessLifetime;
        return staleness <= entry.StaleIfError.Value;
    }

    private static bool IsStaleIfErrorStatus(HttpStatusCode statusCode)
    {
        // RFC 5861 Section 4: an error is a situation that would result in a 500, 502, 503 or 504 response.
        // Any other status is the origin answering the request, and must reach the caller: a 404 or a 410
        // means the representation is gone, a 401 or a 403 means it is no longer accessible, and a 3xx is a
        // redirection. Serving the stored body for those would hide the answer the origin just gave.
        return (int)statusCode is 500 or 502 or 503 or 504;
    }

    private static bool IsOriginUnreachable(Exception exception, CancellationToken cancellationToken)
    {
        // A cancellation requested by the caller is not an origin failure and must propagate. A
        // TaskCanceledException without a cancellation request is an HttpClient timeout.
        if (cancellationToken.IsCancellationRequested)
            return false;

        return exception is HttpRequestException or TaskCanceledException;
    }

    private static bool HasConditionalHeaders(HttpRequestHeaders headers)
    {
        // RFC 9110 Section 13.1: preconditions issued by the caller. If-Range is not one of them: it decides between a
        // partial and a complete response, which the cache evaluates against the stored response.
        return headers.IfNoneMatch.Count > 0 ||
               headers.IfMatch.Count > 0 ||
               headers.IfModifiedSince is not null ||
               headers.IfUnmodifiedSince is not null;
    }

    private static bool HasMismatchedValidator(CacheEntry entry, HttpResponseMessage validationResponse)
    {
        var validator = validationResponse.Headers.ETag;
        if (validator is not null)
            return !string.Equals(entry.ETag, validator.ToString(), StringComparison.Ordinal);

        var lastModified = validationResponse.Content.Headers.LastModified;
        if (lastModified is null && validationResponse.Headers.TryGetValues("Last-Modified", out var values))
        {
            var value = values.FirstOrDefault();
            if (value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var parsedDate))
            {
                lastModified = parsedDate;
            }
        }

        return lastModified is not null && entry.LastModified != lastModified;
    }

    /// <summary>A request to the server that the concurrent requests with the same key wait for.</summary>
    private sealed class InFlightRequest
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public void Complete() => _completion.TrySetResult();
    }

    /// <summary>Coalesces the requests to the server of one request with those of the concurrent requests with the same key.</summary>
    private sealed class CoalescingState(ConcurrentDictionary<string, InFlightRequest> inFlightRequests, string key)
    {
        private InFlightRequest? _inFlightRequest;

        /// <summary>Gets a value indicating whether the request is registered as the one the concurrent requests wait for.</summary>
        public bool HasStarted => _inFlightRequest is not null;

        /// <summary>
        /// Returns <see langword="true"/> when the request is registered as the one the concurrent requests wait for, and can go to
        /// the server. Otherwise, returns <see langword="false"/>: after registering it, so that the caller looks the cache up again
        /// first (see <see cref="HasStarted"/>), or after waiting for the concurrent request that is going to the server.
        /// </summary>
        public async ValueTask<bool> TryStartAsync(CancellationToken cancellationToken)
        {
            if (_inFlightRequest is not null)
                return true;

            var inFlightRequest = new InFlightRequest();
            var existing = inFlightRequests.GetOrAdd(key, inFlightRequest);
            if (ReferenceEquals(existing, inFlightRequest))
            {
                _inFlightRequest = inFlightRequest;
                return false;
            }

            await existing.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        public void Complete()
        {
            if (_inFlightRequest is null)
                return;

            inFlightRequests.TryRemove(new KeyValuePair<string, InFlightRequest>(key, _inFlightRequest));
            _inFlightRequest.Complete();
        }
    }
}
