namespace HttpCaching;

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
}
