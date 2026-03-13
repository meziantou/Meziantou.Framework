namespace Meziantou.Framework.Http;

/// <summary>
/// Represents a persisted HTTP cache entry.
/// </summary>
/// <remarks>
/// This type is part of <see cref="IHttpCachePersistenceProvider"/> public contract.
/// It allows custom providers to persist cache metadata without exposing internal cache implementation details.
/// </remarks>
public sealed class HttpCachePersistenceEntry
{
    /// <summary>
    /// Gets or sets a value indicating whether this entry should never match requests.
    /// Corresponds to the <c>Vary: *</c> behavior.
    /// </summary>
    public bool SecondaryKeyMatchNone { get; set; }

    /// <summary>
    /// Gets or sets the normalized vary-based secondary key headers.
    /// </summary>
    public Dictionary<string, string>? SecondaryKeyHeaders { get; set; }

    /// <summary>
    /// Gets or sets the request time.
    /// </summary>
    public DateTimeOffset RequestTime { get; set; }

    /// <summary>
    /// Gets or sets the response time.
    /// </summary>
    public DateTimeOffset ResponseTime { get; set; }

    /// <summary>
    /// Gets or sets the response date.
    /// </summary>
    public DateTimeOffset ResponseDate { get; set; }

    /// <summary>
    /// Gets or sets the age value.
    /// </summary>
    public TimeSpan AgeValue { get; set; }

    /// <summary>
    /// Gets or sets the <c>max-age</c> directive value.
    /// </summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>
    /// Gets or sets the <c>s-maxage</c> directive value.
    /// </summary>
    public TimeSpan? SharedMaxAge { get; set; }

    /// <summary>
    /// Gets or sets the Expires header value.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the ETag validator.
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// Gets or sets the Last-Modified validator.
    /// </summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>
    /// Gets or sets the serialized HTTP response payload.
    /// </summary>
    public ReadOnlyMemory<byte> SerializedResponse { get; set; }

    /// <summary>
    /// Creates a deep clone of the current instance.
    /// </summary>
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

    /// <summary>
    /// Indicates whether two entries share the same secondary key.
    /// </summary>
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
