namespace Meziantou.Framework.Http.Caching;

/// <summary>Bounds the content of a cache store. When a write exceeds a bound, the store evicts the least recently used entries.</summary>
/// <remarks>
/// <para>
/// An entry is one stored response: a URL with several variants (see the <c>Vary</c> header) has one entry per variant.
/// Reading the entries of a URL and writing an entry make them the most recently used.
/// </para>
/// <para>
/// The size of an entry is the length of its <see cref="HttpCachePersistenceEntry.SerializedResponse"/>, which holds the
/// headers and the body of the response. The metadata of the entry and the overhead of the store are not counted.
/// </para>
/// </remarks>
public sealed class HttpCacheStoreLimits
{
    /// <summary>
    /// Gets or sets the maximum number of entries the store keeps, or <see langword="null"/> for no limit.
    /// Default is <see langword="null"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public long? MaximumEntryCount
    {
        get;
        set
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum entry count must be positive.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum total size, in bytes, of the entries the store keeps, or <see langword="null"/> for no limit.
    /// Default is <see langword="null"/>. An entry larger than this value is not kept.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public long? MaximumTotalSize
    {
        get;
        set
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum total size must be positive.");

            field = value;
        }
    }
}
