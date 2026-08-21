using System.Security.Cryptography;

namespace Meziantou.Framework.Http.Caching;

/// <summary>Represents a persisted HTTP cache entry.</summary>
/// <remarks>
/// This type is part of <see cref="IHttpCacheStore"/> public contract.
/// It allows custom providers to persist cache metadata without exposing internal cache implementation details.
/// </remarks>
public sealed class HttpCachePersistenceEntry
{
    /// <summary>
    /// Gets or sets a value indicating whether this entry should never match requests.
    /// Corresponds to the <c>Vary: *</c> behavior.
    /// </summary>
    public bool SecondaryKeyMatchNone { get; set; }

    /// <summary>Gets or sets the normalized vary-based secondary key headers.</summary>
    public Dictionary<string, string>? SecondaryKeyHeaders { get; set; }

    /// <summary>Gets or sets the request time.</summary>
    public DateTimeOffset RequestTime { get; set; }

    /// <summary>Gets or sets the response time.</summary>
    public DateTimeOffset ResponseTime { get; set; }

    /// <summary>Gets or sets the response date.</summary>
    public DateTimeOffset ResponseDate { get; set; }

    /// <summary>Gets or sets the age value.</summary>
    public TimeSpan AgeValue { get; set; }

    /// <summary>
    /// Gets or sets the <c>max-age</c> directive value.
    /// </summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>
    /// Gets or sets the <c>s-maxage</c> directive value.
    /// </summary>
    public TimeSpan? SharedMaxAge { get; set; }

    /// <summary>Gets or sets the Expires header value.</summary>
    public DateTimeOffset? Expires { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>must-revalidate</c> is set.
    /// </summary>
    public bool MustRevalidate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>proxy-revalidate</c> is set.
    /// </summary>
    public bool ProxyRevalidate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>no-cache</c> is set.
    /// </summary>
    public bool ResponseNoCache { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>public</c> is set.
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>private</c> is set.
    /// </summary>
    public bool Private { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>no-transform</c> is set.
    /// </summary>
    public bool NoTransform { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>immutable</c> is set.
    /// </summary>
    public bool Immutable { get; set; }

    /// <summary>
    /// Gets or sets the <c>stale-if-error</c> value.
    /// </summary>
    public TimeSpan? StaleIfError { get; set; }

    /// <summary>Gets or sets the ETag validator.</summary>
    public string? ETag { get; set; }

    /// <summary>Gets or sets the Last-Modified validator.</summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>Gets or sets the serialized HTTP response payload.</summary>
    public ReadOnlyMemory<byte> SerializedResponse { get; set; }

    /// <summary>Creates a deep clone of the current instance.</summary>
    public HttpCachePersistenceEntry Clone()
    {
        return new HttpCachePersistenceEntry
        {
            SecondaryKeyMatchNone = SecondaryKeyMatchNone,
            SecondaryKeyHeaders = SecondaryKeyHeaders is null ? null : new Dictionary<string, string>(SecondaryKeyHeaders, StringComparer.OrdinalIgnoreCase),
            RequestTime = RequestTime,
            ResponseTime = ResponseTime,
            ResponseDate = ResponseDate,
            AgeValue = AgeValue,
            MaxAge = MaxAge,
            SharedMaxAge = SharedMaxAge,
            Expires = Expires,
            MustRevalidate = MustRevalidate,
            ProxyRevalidate = ProxyRevalidate,
            ResponseNoCache = ResponseNoCache,
            Public = Public,
            Private = Private,
            NoTransform = NoTransform,
            Immutable = Immutable,
            StaleIfError = StaleIfError,
            ETag = ETag,
            LastModified = LastModified,
            SerializedResponse = SerializedResponse.ToArray(),
        };
    }

    /// <summary>Computes the freshness lifetime of the entry, as defined by RFC 7234 Section 4.2.1.</summary>
    public TimeSpan GetFreshnessLifetime()
    {
        return CacheFreshness.GetFreshnessLifetime(SharedMaxAge, MaxAge, Expires, ResponseDate, LastModified);
    }

    /// <summary>Computes the age of the entry at the specified time, as defined by RFC 7234 Section 4.2.3.</summary>
    /// <param name="now">The time at which the age is evaluated.</param>
    public TimeSpan GetCurrentAge(DateTimeOffset now)
    {
        return CacheFreshness.GetCurrentAge(RequestTime, ResponseTime, ResponseDate, AgeValue, now);
    }

    /// <summary>Gets the time at which the entry becomes stale.</summary>
    /// <remarks>
    /// The result is clamped to the range of <see cref="DateTimeOffset"/>. Stores can index this value to
    /// remove obsolete entries without deserializing them.
    /// </remarks>
    public DateTimeOffset GetStaleTime()
    {
        var correctedInitialAge = CacheFreshness.GetCorrectedInitialAge(RequestTime, ResponseTime, ResponseDate, AgeValue);
        var freshnessLifetime = GetFreshnessLifetime();

        long ticks;
        try
        {
            ticks = checked(ResponseTime.UtcDateTime.Ticks + freshnessLifetime.Ticks - correctedInitialAge.Ticks);
        }
        catch (OverflowException)
        {
            ticks = freshnessLifetime >= correctedInitialAge ? DateTime.MaxValue.Ticks : DateTime.MinValue.Ticks;
        }

        if (ticks < DateTime.MinValue.Ticks)
            return DateTimeOffset.MinValue;

        if (ticks > DateTime.MaxValue.Ticks)
            return DateTimeOffset.MaxValue;

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>Gets a value indicating whether the entry becomes unusable as soon as it is stale.</summary>
    /// <remarks>
    /// An entry that may be served stale, or that carries a validator allowing it to be revalidated, remains
    /// usable after it expires and must be kept.
    /// </remarks>
    public bool IsUnusableWhenStale
    {
        get
        {
            if (!MustRevalidate && !ProxyRevalidate && !ResponseNoCache)
                return false;

            return string.IsNullOrEmpty(ETag) && LastModified is null;
        }
    }

    /// <summary>
    /// Indicates whether the entry is stale and cannot be reused, and can therefore be removed by a store.
    /// </summary>
    /// <param name="now">The time at which staleness is evaluated.</param>
    public bool IsObsolete(DateTimeOffset now)
    {
        return IsUnusableWhenStale && GetCurrentAge(now) >= GetFreshnessLifetime();
    }

    /// <summary>Computes a stable hash of the secondary key, suitable for use as a storage key.</summary>
    public string ComputeSecondaryKeyHash()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(SecondaryKeyMatchNone ? '1' : '0');

        if (SecondaryKeyHeaders is not null)
        {
            foreach (var (key, value) in SecondaryKeyHeaders.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                stringBuilder.Append('\u001f');
                stringBuilder.Append(key);
                stringBuilder.Append('\u001e');
                stringBuilder.Append(value);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash);
    }

    /// <summary>Indicates whether two entries share the same secondary key.</summary>
    public static bool HasSameSecondaryKey(HttpCachePersistenceEntry left, HttpCachePersistenceEntry right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.SecondaryKeyMatchNone != right.SecondaryKeyMatchNone)
            return false;

        var leftHeaders = left.SecondaryKeyHeaders;
        var rightHeaders = right.SecondaryKeyHeaders;

        var leftCount = leftHeaders?.Count ?? 0;
        var rightCount = rightHeaders?.Count ?? 0;
        if (leftCount != rightCount)
            return false;

        if (leftCount is 0)
            return true;

        foreach (var header in leftHeaders!)
        {
            if (!rightHeaders!.TryGetValue(header.Key, out var value))
                return false;

            if (!string.Equals(header.Value, value, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
