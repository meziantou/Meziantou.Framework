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
}
