namespace Meziantou.Framework.Http;

/// <summary>
/// Options for configuring HTTP caching behavior.
/// </summary>
public sealed class CachingOptions
{
    /// <summary>
    /// Gets or sets the maximum size in bytes that a response can be to be cached.
    /// Responses larger than this value will not be cached.
    /// Default is 5 MB (5,242,880 bytes).
    /// Set to null to disable size checking.
    /// </summary>
    public long? MaximumResponseSize { get; set; } = 5 * 1024 * 1024; // 5 MB default

    /// <summary>
    /// Gets or sets a predicate that determines whether a response should be cached.
    /// When the predicate returns true, normal caching logic applies.
    /// When the predicate returns false, the response is not cached.
    /// Default is null (all responses that meet caching requirements are cached).
    /// </summary>
    public Func<HttpResponseMessage, bool>? ShouldCacheResponse { get; set; }
}
