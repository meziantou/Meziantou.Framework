namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The local storage areas of Web Storage (<c>window.localStorage</c>): a storage shed
/// (https://storage.spec.whatwg.org/#storage-shed) whose shelves are keyed by origin, each with the <c>localStorage</c>
/// bottle of its origin. The items are kept in memory, and are persisted by the <see cref="WebStoragePersistence"/> given
/// to the constructor, if any.
/// </summary>
/// <remarks>
/// <para>
/// A shed can be shared by several browsing contexts (see <see cref="BrowsingContextOptions.LocalStorage"/>): the
/// documents of the same origin read and write the same items, and the <c>storage</c> events are fired at the windows of
/// the browsing contexts that share the shed. The shed is thread-safe.
/// </para>
/// <para>
/// The items of a storage area are limited by <see cref="Quota"/>: the sum of the lengths of the keys and the values, in
/// UTF-16 code units. <c>setItem()</c> throws a <c>QuotaExceededError</c> when the new item would exceed it.
/// </para>
/// </remarks>
public sealed class StorageShed
{
    /// <summary>The default quota of a storage area: 5 Mi UTF-16 code units (https://storage.spec.whatwg.org/#registered-storage-endpoints: 5 MiB).</summary>
    public const long DefaultQuota = 5 * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<StorageKey, StorageArea> _areas = [];

    // The browsing contexts created by the application whose groups use the shed (the windows that receive the storage events)
    private readonly List<WeakReference<BrowsingContext>> _browsingContexts = [];

    /// <summary>Creates an empty shed whose storage areas have the default quota (<see cref="DefaultQuota"/>).</summary>
    public StorageShed()
        : this(DefaultQuota)
    {
    }

    /// <summary>Creates an empty shed.</summary>
    /// <param name="quota">The maximum size of a storage area: the sum of the lengths of its keys and values, in UTF-16 code units.</param>
    public StorageShed(long quota)
        : this(persistence: null, quota)
    {
    }

    /// <summary>Creates a shed whose storage areas are persisted, with the default quota (<see cref="DefaultQuota"/>).</summary>
    /// <param name="persistence">The persistent storage of the storage areas.</param>
    public StorageShed(WebStoragePersistence persistence)
        : this(persistence ?? throw new ArgumentNullException(nameof(persistence)), DefaultQuota)
    {
    }

    /// <summary>Creates a shed whose storage areas are persisted.</summary>
    /// <param name="persistence">The persistent storage of the storage areas, or null to keep them in memory only.</param>
    /// <param name="quota">The maximum size of a storage area: the sum of the lengths of its keys and values, in UTF-16 code units.</param>
    public StorageShed(WebStoragePersistence? persistence, long quota)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quota);
        Persistence = persistence;
        Quota = quota;
    }

    /// <summary>Gets the maximum size of a storage area: the sum of the lengths of its keys and values, in UTF-16 code units.</summary>
    public long Quota { get; }

    /// <summary>Gets the persistent storage of the storage areas, or null when they are kept in memory only.</summary>
    public WebStoragePersistence? Persistence { get; }

    /// <summary>
    /// Returns the items of the storage area of an origin, in the order of <c>Storage.key()</c>. The items of an origin
    /// that was not used yet are loaded from <see cref="Persistence"/>.
    /// </summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>, or <c>http://example.com:8080</c>).</param>
    public IReadOnlyList<KeyValuePair<string, string>> GetItems(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var key = new StorageKey(origin, PartitionNonce: null);
        StorageArea? area;
        if (Persistence is not null)
        {
            area = ObtainArea(key);
        }
        else
        {
            lock (_lock)
            {
                _areas.TryGetValue(key, out area);
            }
        }

        return area?.GetItems() ?? [];
    }

    /// <summary>
    /// Returns the origins whose storage area has items in memory (the areas loaded from <see cref="Persistence"/> and the
    /// ones the documents used), e.g. to save the storage state of a session.
    /// </summary>
    public IReadOnlyList<string> GetOrigins()
    {
        lock (_lock)
        {
            return [.. _areas.Where(entry => entry.Key.PartitionNonce is null && entry.Key.IsFirstParty && entry.Value.Count > 0).Select(entry => entry.Key.Origin)];
        }
    }

    /// <summary>
    /// Sets an item of the storage area of an origin (e.g. to restore the storage state of a session, as the
    /// <c>storageState</c> option of Playwright). No <c>storage</c> event is fired.
    /// </summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>, or <c>http://example.com:8080</c>).</param>
    /// <param name="key">The key of the item.</param>
    /// <param name="value">The value of the item.</param>
    /// <returns>True when the item is set, false when it exceeds the quota of the storage area (see <see cref="Quota"/>).</returns>
    public bool SetItem(string origin, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            _ = ObtainArea(new StorageKey(origin, PartitionNonce: null)).Set(key, value, out _);
            return true;
        }
        catch (QuotaExceededError)
        {
            return false;
        }
    }

    /// <summary>Removes the items of all the origins, and the persisted items of <see cref="Persistence"/>. No <c>storage</c> event is fired.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Persistence?.ClearAll();
            foreach (var area in _areas.Values)
            {
                area.Clear(persist: false);
            }
        }
    }

    /// <summary>
    /// https://storage.spec.whatwg.org/#obtain-a-storage-bottle-map: the map of the bottle of the storage key, created when
    /// needed (with the items of <see cref="Persistence"/>, except for the partitioned storage keys, the ones of the
    /// credentialless iframes and of the third-party contexts, which are not persisted). The exceptions of the persistence
    /// are not caught, and the map is loaded again by the next call.
    /// </summary>
    internal StorageArea ObtainArea(StorageKey storageKey)
    {
        lock (_lock)
        {
            if (!_areas.TryGetValue(storageKey, out var area))
            {
                area = storageKey.PartitionNonce is null && storageKey.IsFirstParty && Persistence is { } persistence ? new StorageArea(Quota, persistence, storageKey.Origin) : new StorageArea(Quota);
                _areas.Add(storageKey, area);
            }

            return area;
        }
    }

    /// <summary>The number of bytes of the items of the storage key (their UTF-16 code units), for <c>navigator.storage.estimate()</c>: 0 when its area is not loaded.</summary>
    internal long GetUsage(StorageKey storageKey)
    {
        lock (_lock)
        {
            return _areas.TryGetValue(storageKey, out var area) ? area.Size * sizeof(char) : 0;
        }
    }

    /// <summary>
    /// https://storage.spec.whatwg.org/#legacy-clone-a-traversable-storage-shed: copies the session storage areas of a
    /// traversable into the shed of a new auxiliary browsing context.
    /// </summary>
    internal void CopyTo(StorageShed target)
    {
        lock (_lock)
        {
            foreach (var (key, area) in _areas)
            {
                target._areas[key] = area.Clone();
            }
        }
    }

    /// <summary>Adds a browsing context created by the application (the owner of a browsing context group) whose windows receive the storage events of the shed.</summary>
    internal void AddBrowsingContext(BrowsingContext browsingContext)
    {
        lock (_lock)
        {
            _browsingContexts.Add(new WeakReference<BrowsingContext>(browsingContext));
        }
    }

    /// <summary>Removes a browsing context added by <see cref="AddBrowsingContext"/> (when it is disposed).</summary>
    internal void RemoveBrowsingContext(BrowsingContext browsingContext)
    {
        lock (_lock)
        {
            _browsingContexts.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, browsingContext));
        }
    }

    /// <summary>The browsing contexts added by <see cref="AddBrowsingContext"/> that are alive.</summary>
    internal List<BrowsingContext> GetBrowsingContexts()
    {
        lock (_lock)
        {
            var result = new List<BrowsingContext>(_browsingContexts.Count);
            _browsingContexts.RemoveAll(reference =>
            {
                if (!reference.TryGetTarget(out var target))
                    return true;

                result.Add(target);
                return false;
            });
            return result;
        }
    }
}

/// <summary>
/// A storage key (https://storage.spec.whatwg.org/#storage-key): the serialization of the origin, the page credentialless
/// nonce of the documents of the credentialless iframes (https://wicg.github.io/anonymous-iframe/#spec-storage-key), and
/// the top-level site of the environments of a third-party context (https://privacycg.github.io/storage-partitioning/):
/// the storage of an origin embedded by another site is partitioned by the site of the top-level document, and so is the
/// storage of an origin embedded by its own site through a cross-site document (<see cref="HasCrossSiteAncestor"/>). The
/// storage key of a first-party environment (a top-level document, or a document whose ancestors are all same site with the
/// top-level document) has no top-level site.
/// </summary>
internal readonly record struct StorageKey(string Origin, Guid? PartitionNonce, string? TopLevelSite = null, bool HasCrossSiteAncestor = false)
{
    /// <summary>Whether the key is the one of a first-party environment (it has no top-level site).</summary>
    public bool IsFirstParty => TopLevelSite is null;

    /// <summary>
    /// The key of the storage in the backends (IndexedDB, the Cache API, the origin private file system): the serialized origin
    /// of a first-party key, else the origin, the top-level site and the cross-site ancestor bit, separated by <c>^</c>.
    /// </summary>
    public string Serialize() => TopLevelSite is null ? Origin : Origin + "^" + TopLevelSite + (HasCrossSiteAncestor ? "^1" : "^0");

    /// <summary>
    /// https://storage.spec.whatwg.org/#obtain-a-storage-key: the storage key of an environment (the document of a window or
    /// of a worker), or null (failure) when its origin is opaque. A dedicated worker has the partition of the document that
    /// created it, and a service worker the storage key of its registration.
    /// </summary>
    public static StorageKey? Obtain(Document environment)
    {
        var origin = environment.Origin;
        if (origin.IsOpaque)
            return null;

        var root = environment.EnvironmentRootDocument;
        if (root.WorkerGlobal is ServiceWorkerGlobalScope serviceWorker && serviceWorker.Agent.ServiceWorker is { } record)
            return record.Registration.StorageKey;

        var nonce = environment.IsCredentialless && environment.EnvironmentBrowsingContext?.Top.Window is { } topLevelWindow ? topLevelWindow.PageCredentiallessNonce : (Guid?)null;
        var (topLevelSite, hasCrossSiteAncestor) = GetPartition(origin, root.BrowsingContext?.Parent);
        return new StorageKey(origin.Serialize(), nonce, topLevelSite, hasCrossSiteAncestor);
    }

    /// <summary>
    /// The storage key of an environment of origin <paramref name="origin"/> created in <paramref name="navigable"/> (e.g. the
    /// reserved client of a navigation), or of a worker created by the document of <paramref name="navigable"/> when
    /// <paramref name="isNavigable"/> is false.
    /// </summary>
    public static StorageKey Create(OriginRecord origin, Guid? partitionNonce, BrowsingContext? navigable, bool isNavigable)
    {
        if (!isNavigable && navigable?.Document is { } creator && Obtain(creator) is { } creatorKey)
            return creatorKey with { Origin = origin.Serialize(), PartitionNonce = partitionNonce };

        var (topLevelSite, hasCrossSiteAncestor) = GetPartition(origin, navigable?.Parent);
        return new StorageKey(origin.Serialize(), partitionNonce, topLevelSite, hasCrossSiteAncestor);
    }

    /// <summary>
    /// The storage key of the data of an origin in the partition of a request (the data that the <c>Clear-Site-Data</c> header
    /// of its response clears): the key of an environment of the origin created by a navigation request in its navigable, else
    /// the key of an environment of the origin nested in the client of the request (e.g. a frame of a third-party script
    /// whose closest ancestor is the document that fetches it), whose ancestors are the client and its ancestors.
    /// </summary>
    public static StorageKey ForRequest(OriginRecord origin, FetchRequest request)
    {
        BrowsingContext? parent;
        Guid? nonce;
        if (request.Mode == RequestMode.Navigate)
        {
            if (request.Navigable is not { } navigable)
                return new StorageKey(origin.Serialize(), PartitionNonce: null);

            parent = navigable.Parent;
            nonce = navigable.IsCredentiallessNavigable && navigable.Top.Window is { } topLevelWindow ? topLevelWindow.PageCredentiallessNonce : null;
        }
        else
        {
            if (request.Client is not { } client)
                return new StorageKey(origin.Serialize(), PartitionNonce: null);

            // A service worker has the partition of its registration
            var root = client.EnvironmentRootDocument;
            if (root.WorkerGlobal is ServiceWorkerGlobalScope serviceWorker && serviceWorker.Agent.ServiceWorker is { } record)
                return record.Registration.StorageKey with { Origin = origin.Serialize() };

            parent = root.BrowsingContext;
            nonce = Obtain(client)?.PartitionNonce;
        }

        var (topLevelSite, hasCrossSiteAncestor) = GetPartition(origin, parent);
        return new StorageKey(origin.Serialize(), nonce, topLevelSite, hasCrossSiteAncestor);
    }

    /// <summary>
    /// The partition of an origin whose closest ancestor navigable is <paramref name="parent"/>: no top-level site for a
    /// first-party environment, else the site of the top-level document and whether one of the documents of the ancestors
    /// is cross-site with it (only for an origin that is same site with it). The environments of a top-level document whose
    /// origin is opaque are not partitioned.
    /// </summary>
    private static (string? TopLevelSite, bool HasCrossSiteAncestor) GetPartition(OriginRecord origin, BrowsingContext? parent)
    {
        if (parent is null || parent.Top.Document.Origin is not { IsOpaque: false } topLevelOrigin)
            return (null, false);

        var site = GetSite(topLevelOrigin);
        if (!OriginRecord.IsSameSite(origin, topLevelOrigin))
            return (site, false);

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!OriginRecord.IsSameSite(ancestor.Document.Origin, topLevelOrigin))
                return (site, true);
        }

        return (null, false);
    }

    /// <summary>The site of a tuple origin (https://html.spec.whatwg.org/multipage/browsers.html#obtain-a-site), serialized: <c>scheme://registrable-domain</c>.</summary>
    private static string GetSite(OriginRecord origin) => origin.Scheme + "://" + (Urls.PublicSuffixList.GetRegistrableDomain(origin.Host) ?? origin.Host.Serialized);
}

/// <summary>
/// The map of a storage bottle (https://storage.spec.whatwg.org/#storage-bottle) of Web Storage: the items of an origin,
/// in insertion order, limited by a quota, and persisted by a <see cref="WebStoragePersistence"/>. The map is thread-safe,
/// as a <see cref="StorageShed"/> can be shared by the browsing contexts of several threads.
/// </summary>
internal sealed class StorageArea
{
    private readonly Lock _lock = new();
    private readonly OrderedDictionary<string, string> _map = new(StringComparer.Ordinal);
    private readonly long _quota;
    private readonly WebStoragePersistence? _persistence;
    private readonly string _origin = "";
    private long _size;

    public StorageArea(long quota)
    {
        _quota = quota;
    }

    /// <summary>Creates a map with the persisted items of the origin, whose changes are stored by the persistence.</summary>
    public StorageArea(long quota, WebStoragePersistence persistence, string origin)
    {
        _quota = quota;
        _persistence = persistence;
        _origin = origin;
        foreach (var (key, value) in persistence.LoadItems(origin))
        {
            if (key is null || value is null)
                continue;

            if (_map.TryGetValue(key, out var oldValue))
            {
                _size -= oldValue.Length;
            }
            else
            {
                _size += key.Length;
            }

            _map[key] = value;
            _size += value.Length;
        }
    }

    /// <summary>The number of UTF-16 code units of the keys and of the values.</summary>
    public long Size
    {
        get
        {
            lock (_lock)
            {
                return _size;
            }
        }
    }

    /// <summary>The number of items.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>Returns the key at an index of the map, or null.</summary>
    public string? GetKey(uint index)
    {
        lock (_lock)
        {
            return index < (uint)_map.Count ? _map.GetAt((int)index).Key : null;
        }
    }

    /// <summary>Returns the keys of the map.</summary>
    public List<string> GetKeys()
    {
        lock (_lock)
        {
            return [.. _map.Keys];
        }
    }

    /// <summary>Returns the items of the map.</summary>
    public List<KeyValuePair<string, string>> GetItems()
    {
        lock (_lock)
        {
            return [.. _map];
        }
    }

    public bool ContainsKey(string key)
    {
        lock (_lock)
        {
            return _map.ContainsKey(key);
        }
    }

    public string? GetValue(string key)
    {
        lock (_lock)
        {
            return _map.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Sets the value of a key (steps 3 to 5 of https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-setitem).
    /// Returns false when the value is the current value. Throws a <c>QuotaExceededError</c> when the value cannot be stored.
    /// </summary>
    public bool Set(string key, string value, out string? oldValue)
    {
        lock (_lock)
        {
            var newSize = _size + value.Length;
            if (_map.TryGetValue(key, out oldValue))
            {
                if (string.Equals(oldValue, value, StringComparison.Ordinal))
                    return false;

                newSize -= oldValue.Length;
            }
            else
            {
                newSize += key.Length;
            }

            // Loaded items can exceed the quota: the area cannot grow, but its values can be replaced by smaller ones
            if (newSize > _quota && newSize > _size)
                throw new QuotaExceededError("The quota of the storage area is exceeded", quota: null, requested: null);

            if (_persistence is not null)
            {
                try
                {
                    _persistence.SetItem(_origin, key, value);
                }
                catch (Exception ex)
                {
                    throw new QuotaExceededError("The item cannot be stored", ex);
                }
            }

            _map[key] = value;
            _size = newSize;
            return true;
        }
    }

    /// <summary>Removes a key, and returns false when it does not exist.</summary>
    public bool Remove(string key, out string? oldValue)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out oldValue))
                return false;

            _persistence?.RemoveItem(_origin, key);
            _map.Remove(key);
            _size -= key.Length + oldValue.Length;
            return true;
        }
    }

    /// <summary>Removes all the items (and the persisted items, when <paramref name="persist"/> is true), and returns false when the map is empty.</summary>
    public bool Clear(bool persist = true)
    {
        lock (_lock)
        {
            if (_map.Count == 0)
                return false;

            if (persist)
            {
                _persistence?.ClearItems(_origin);
            }

            _map.Clear();
            _size = 0;
            return true;
        }
    }

    /// <summary>Returns a copy of the map in memory (the session storage, which is not persisted).</summary>
    public StorageArea Clone()
    {
        lock (_lock)
        {
            var clone = new StorageArea(_quota) { _size = _size };
            foreach (var (key, value) in _map)
            {
                clone._map.Add(key, value);
            }

            return clone;
        }
    }
}
