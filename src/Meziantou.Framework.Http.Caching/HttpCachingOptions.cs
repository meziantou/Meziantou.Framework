namespace Meziantou.Framework.Http.Caching;

/// <summary>Options for configuring HTTP caching behavior.</summary>
public sealed class HttpCachingOptions
{
    /// <summary>Gets or sets the time provider used for time-based cache operations.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets the maximum size in bytes that a response can be to be cached.
    /// Responses larger than this value will not be cached.
    /// Default is 5 MB (5,242,880 bytes).
    /// Set to null to disable size checking.
    /// </summary>
    public long? MaximumResponseSize { get; set; } = 5 * 1024 * 1024; // 5 MB default

    /// <summary>
    /// Gets or sets a callback invoked when the cache store fails.
    /// A store failure never fails the request: the lookup is treated as a miss and the write is skipped, so
    /// a degraded store degrades to no caching rather than to a broken <see cref="HttpClient"/>. This
    /// callback is the only way to observe that, so set it if you need the failures to be visible.
    /// Default is null (store failures are silently ignored).
    /// </summary>
    public Action<Exception>? OnStoreError { get; set; }

    /// <summary>
    /// Gets or sets a predicate that determines whether a response should be cached.
    /// When the predicate returns true, normal caching logic applies.
    /// When the predicate returns false, the response is not cached.
    /// Default is null (all responses that meet caching requirements are cached).
    /// </summary>
    public Func<HttpResponseMessage, bool>? ShouldCacheResponse { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cache follows the rules of a shared cache (RFC 9111), such as a proxy
    /// serving several users.
    /// </summary>
    /// <remarks>
    /// A shared cache does not store the responses marked <c>private</c>, does not store the responses to requests with an
    /// <c>Authorization</c> header unless the response allows it (<c>public</c>, <c>s-maxage</c> or <c>must-revalidate</c>),
    /// uses <c>s-maxage</c> over <c>max-age</c> and <c>Expires</c>, and honors <c>proxy-revalidate</c>.
    /// Default is <see langword="false"/> (a private cache that belongs to a single user).
    /// </remarks>
    public bool IsSharedCache { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether concurrent requests for the same URL share a single request to the server.
    /// </summary>
    /// <remarks>
    /// When a request must go to the server (no stored response, or a stale one to revalidate), the concurrent requests with
    /// the same method and URL wait for it to complete, then look up the cache again. The ones the stored response can
    /// answer are served from the cache; the others, for instance because the response varies on a header they send
    /// differently, or because it could not be stored, go to the server themselves.
    /// Requests with a <c>Range</c> header do not take part in coalescing.
    /// Default is <see langword="false"/>.
    /// </remarks>
    public bool CoalesceRequests { get; set; }
}
