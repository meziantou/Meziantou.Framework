namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The storage of the caches of the Cache API (https://w3c.github.io/ServiceWorker/#cache-objects) of the browsing
/// contexts, e.g. a SQLite database or <see cref="InMemoryCacheStorageBackend"/> (see
/// <see cref="BrowsingContextOptions.CacheStorage"/>). The backend stores the named caches of each origin and their
/// entries (request and response pairs, in insertion order); the library implements the API (the matching of the
/// requests, including the <c>Vary</c> header, and the batch operations) and only reads and writes entries.
/// </summary>
/// <remarks>
/// <para>
/// The methods are called on the threads of the event loops of the windows and the workers that use the caches: an
/// implementation must be thread-safe. The changes of a cache (<see cref="ApplyBatch"/>) must be atomic. The exceptions
/// reject the promises of the API (with a <c>QuotaExceededError</c> when the batch cannot be written, or an <c>UnknownError</c>).
/// </para>
/// <para>
/// The cache names are arbitrary strings (possibly empty, with lone surrogates). A deleted cache is removed with its
/// entries: the <c>Cache</c> objects that still use it keep the entries they had in memory.
/// </para>
/// </remarks>
public abstract class CacheStorageBackend
{
    internal CacheStorageCoordinator Coordinator => field ??= new CacheStorageCoordinator(this);

    /// <summary>Returns the names of the caches of an origin, in creation order (https://w3c.github.io/ServiceWorker/#cache-storage-keys).</summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>).</param>
    public abstract IReadOnlyList<string> GetCacheNames(string origin);

    /// <summary>Creates an empty cache, and returns true, or returns false when the cache exists.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="name">The name of the cache.</param>
    public abstract bool CreateCache(string origin, string name);

    /// <summary>Deletes a cache and its entries, and returns true, or returns false when the cache does not exist.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="name">The name of the cache.</param>
    public abstract bool DeleteCache(string origin, string name);

    /// <summary>
    /// Returns the entries of a cache, in insertion order: all the entries, or the entries whose request URL without its
    /// query and its fragment (see <see cref="CachedRequest.UrlWithoutQuery"/>) is <paramref name="urlWithoutQuery"/>.
    /// The library filters the entries further (the query, the method and the <c>Vary</c> header).
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="name">The name of the cache.</param>
    /// <param name="urlWithoutQuery">The URL of the requests without query and fragment, or null for all the entries.</param>
    public abstract IReadOnlyList<CacheEntry> GetEntries(string origin, string name, string? urlWithoutQuery);

    /// <summary>
    /// Changes the entries of a cache atomically (https://w3c.github.io/ServiceWorker/#batch-cache-operations): removes the
    /// entries with the ids of <paramref name="removedEntryIds"/>, then appends <paramref name="addedEntries"/> (whose ids
    /// are assigned by the backend, greater than the ids of the existing entries).
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="name">The name of the cache.</param>
    /// <param name="removedEntryIds">The ids of the entries to remove.</param>
    /// <param name="addedEntries">The entries to append.</param>
    public abstract void ApplyBatch(string origin, string name, IReadOnlyList<long> removedEntryIds, IReadOnlyList<CacheEntry> addedEntries);

    /// <summary>
    /// Returns an estimation of the number of bytes used by the caches of an origin (<c>navigator.storage.estimate()</c>).
    /// The default is 0: the usage is not reported.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    public virtual long GetUsage(string origin) => 0;
}

/// <summary>An entry of a cache: a request and its response.</summary>
public sealed class CacheEntry
{
    /// <summary>Gets the id of the entry in its cache (assigned by the backend; 0 for a new entry).</summary>
    public long Id { get; init; }

    /// <summary>Gets the request.</summary>
    public required CachedRequest Request { get; init; }

    /// <summary>Gets the response.</summary>
    public required CachedResponse Response { get; init; }
}

/// <summary>A request of a cache entry (https://fetch.spec.whatwg.org/#concept-request).</summary>
public sealed class CachedRequest
{
    /// <summary>Gets the method.</summary>
    public string Method { get; init; } = "GET";

    /// <summary>Gets the URL (with its fragment).</summary>
    public required string Url { get; init; }

    /// <summary>Gets the URL without its query and its fragment, used to look up the entries.</summary>
    public string UrlWithoutQuery => GetUrlWithoutQuery(Url);

    /// <summary>Gets the headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Gets the mode (<c>cors</c>, <c>no-cors</c>, <c>same-origin</c>, <c>navigate</c>).</summary>
    public string Mode { get; init; } = "cors";

    /// <summary>Gets the credentials mode (<c>omit</c>, <c>same-origin</c>, <c>include</c>).</summary>
    public string Credentials { get; init; } = "same-origin";

    /// <summary>Gets the cache mode.</summary>
    public string Cache { get; init; } = "default";

    /// <summary>Gets the redirect mode (<c>follow</c>, <c>error</c>, <c>manual</c>).</summary>
    public string Redirect { get; init; } = "follow";

    /// <summary>Gets the integrity metadata.</summary>
    public string Integrity { get; init; } = "";

    /// <summary>Gets the referrer: <c>client</c>, <c>no-referrer</c> or a URL.</summary>
    public string Referrer { get; init; } = "client";

    /// <summary>Gets the referrer policy.</summary>
    public string ReferrerPolicy { get; init; } = "";

    /// <summary>Returns the URL without its query and its fragment.</summary>
    public static string GetUrlWithoutQuery(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var end = url.IndexOfAny(['?', '#']);
        return end < 0 ? url : url[..end];
    }
}

/// <summary>
/// A response of a cache entry (https://fetch.spec.whatwg.org/#concept-response). The status, the headers, the URL list and
/// the body of an opaque response are the ones of its internal response (the scripts only see its filtered response, e.g.
/// a status of 0 and no body): a service worker can give it to a page (e.g. a cross-origin script).
/// </summary>
public sealed class CachedResponse
{
    /// <summary>Gets the type (<c>basic</c>, <c>cors</c>, <c>default</c>, <c>opaque</c>...).</summary>
    public string Type { get; init; } = "default";

    /// <summary>Gets the status.</summary>
    public int Status { get; init; } = 200;

    /// <summary>Gets the status message.</summary>
    public string StatusText { get; init; } = "";

    /// <summary>Gets the headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Gets the URLs of the response (the last one is its URL), or an empty list.</summary>
    public IReadOnlyList<string> UrlList { get; init; } = [];

    /// <summary>
    /// Gets whether the request of the response included the credentials
    /// (https://fetch.spec.whatwg.org/#response-request-includes-credentials), for the cross-origin resource policy check of
    /// the opaque responses.
    /// </summary>
    public bool RequestIncludesCredentials { get; init; } = true;

    /// <summary>
    /// Gets the <c>Cross-Origin-Resource-Policy</c> header of the response, or null: the header of an opaque response, whose
    /// <see cref="Headers"/> are empty, for the cross-origin resource policy check.
    /// </summary>
    public string? CrossOriginResourcePolicy { get; init; }

    /// <summary>Gets the body, or null for a response without body.</summary>
    public ReadOnlyMemory<byte>? Body { get; init; }
}
