namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The Accept-CH cache of the user agent (https://wicg.github.io/client-hints-infrastructure/#accept-ch-cache): the client
/// hints that each origin opted into with the <c>Accept-CH</c> header of the response of a top-level navigation, which are
/// sent with the next requests to the origin (see <see cref="BrowsingContextOptions.AcceptCHCache"/>).
/// </summary>
/// <remarks>
/// <para>
/// The cache is keyed by the serialization of the origins (e.g. <c>https://example.com</c>), and its values are client hints
/// tokens: the names of the request headers in lowercase (e.g. <c>sec-ch-ua-arch</c>). The names that are not client hints
/// tokens are ignored. An origin that opted out (an empty <c>Accept-CH</c> header) has an empty entry.
/// </para>
/// <para>
/// The cache acts as a cookie store (a site can use the hints as bits of an identifier): clear it when the cookies are
/// cleared. It is kept in memory: to persist it, save its entries (<see cref="GetOrigins"/> and <see cref="GetClientHints"/>)
/// and restore them with <see cref="SetClientHints"/>. The cache is thread-safe: it can be shared by several browsing contexts.
/// </para>
/// </remarks>
public sealed class AcceptCHCache
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ClientHintsSet> _entries = new(StringComparer.Ordinal);

    /// <summary>Returns the serialized origins that have an entry in the cache.</summary>
    public IReadOnlyList<string> GetOrigins()
    {
        lock (_lock)
        {
            return [.. _entries.Keys];
        }
    }

    /// <summary>Returns the client hints tokens of an origin (e.g. <c>sec-ch-ua-arch</c>): empty when the origin has no entry.</summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>).</param>
    public IReadOnlyList<string> GetClientHints(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return Get(origin).ToNames();
    }

    /// <summary>Creates or overrides the entry of an origin.</summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>).</param>
    /// <param name="clientHints">The client hints tokens (ASCII case-insensitive); the other names are ignored.</param>
    public void SetClientHints(string origin, IEnumerable<string> clientHints)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(clientHints);
        Set(origin, ClientHintsSet.Create(clientHints));
    }

    /// <summary>Removes the entry of an origin, as the <c>Clear-Site-Data</c> header does. Returns whether the origin had an entry.</summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>).</param>
    public bool Remove(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _entries.Remove(origin);
        }
    }

    /// <summary>Removes all the entries.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    /// <summary>
    /// The cache of the browsing context group, for the cache of a prerendering traversable: the hints of an origin are the
    /// ones of both caches, and the entries that the prerendered documents write stay in the cache of the traversable until
    /// the page is activated (as in Chromium; see <see cref="BrowsingContext.AcceptCHCache"/>).
    /// </summary>
    internal AcceptCHCache? Fallback { get; init; }

    internal ClientHintsSet Get(string origin)
    {
        ClientHintsSet entry;
        lock (_lock)
        {
            entry = _entries.GetValueOrDefault(origin);
        }

        return Fallback is null ? entry : entry.Union(Fallback.Get(origin));
    }

    /// <summary>Copies the entries of this cache to another one (the cache of a prerendering traversable that is activated).</summary>
    internal void CopyTo(AcceptCHCache target)
    {
        KeyValuePair<string, ClientHintsSet>[] entries;
        lock (_lock)
        {
            entries = [.. _entries];
        }

        foreach (var (origin, clientHints) in entries)
        {
            target.Set(origin, clientHints.Union(target.Get(origin)));
        }
    }

    internal void Set(string origin, ClientHintsSet clientHints)
    {
        lock (_lock)
        {
            _entries[origin] = clientHints;
        }
    }
}
