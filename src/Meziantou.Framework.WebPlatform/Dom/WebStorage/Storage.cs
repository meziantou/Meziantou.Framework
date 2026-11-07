namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A storage area of Web Storage (https://html.spec.whatwg.org/multipage/webstorage.html#the-storage-interface):
/// <c>window.localStorage</c> or <c>window.sessionStorage</c>. Its items are the items of the storage area of the origin
/// of the document, shared by the documents of the same origin (and, for the session storage, of the same top-level
/// browsing context).
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#storage-2"/>
[IdlInterface("Storage", "https://html.spec.whatwg.org/multipage/webstorage.html#storage-2")]
public sealed class Storage : ISupportedPropertyNames, IRealmBoundPlatformObject
{
    private readonly Window _window;
    private readonly StorageArea _map;

    internal Storage(Window window, StorageArea map, bool isSession)
    {
        _window = window;
        _map = map;
        IsSession = isSession;
    }

    /// <summary>The type of the storage: "session" (true) or "local" (false).</summary>
    internal bool IsSession { get; }

    Window? IRealmBoundPlatformObject.RealmWindow => _window;

    /// <summary>Gets the number of items (https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-length).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-length")]
    public uint Length => (uint)_map.Count;

    /// <summary>Returns the key at an index, or null (https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-key).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-key"/>
    [IdlMember("key", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-key")]
    public string? Key(uint index) => _map.GetKey(index);

    /// <summary>Returns the value of a key, or null (https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-getitem).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-getitem"/>
    [IdlMember("getItem", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-getitem")]
    public string? GetItem(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _map.GetValue(key);
    }

    /// <summary>
    /// Sets the value of a key (https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-setitem), and fires a
    /// <c>storage</c> event at the other windows that use the storage area. Throws a <c>QuotaExceededError</c> when the
    /// quota of the storage area is exceeded.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-setitem"/>
    [IdlMember("setItem", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-setitem")]
    public void SetItem(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (_map.Set(key, value, out var oldValue))
        {
            Broadcast(key, oldValue, value);
        }
    }

    /// <summary>Removes a key (https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-removeitem).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-removeitem"/>
    [IdlMember("removeItem", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-removeitem")]
    public void RemoveItem(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_map.Remove(key, out var oldValue))
        {
            Broadcast(key, oldValue, newValue: null);
        }
    }

    /// <summary>Removes all the items (https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-clear).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-clear"/>
    [IdlMember("clear", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storage-clear")]
    public void Clear()
    {
        if (_map.Clear())
        {
            Broadcast(key: null, oldValue: null, newValue: null);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webstorage.html#the-storage-interface:supported-property-names</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames() => _map.GetKeys();

    bool ISupportedPropertyNames.IsSupportedPropertyName(string name) => _map.ContainsKey(name);

    /// <summary>https://html.spec.whatwg.org/multipage/webstorage.html#concept-storage-broadcast</summary>
    private void Broadcast(string? key, string? oldValue, string? newValue)
    {
        var thisDocument = _window.Document;
        if (thisDocument.BrowsingContext is not { } navigable)
            return;

        var url = thisDocument.Url;
        if (Window.GetStorageKey(thisDocument) is not { } storageKey)
            return;
        if (IsSession)
        {
            // The windows of the same top-level traversable
            BroadcastToGroup(navigable.Top, [navigable.Top], thisDocument, storageKey, key, oldValue, newValue, url, isSession: true);
            return;
        }

        // The windows of the browsing context groups that use the storage shed: the tasks of the groups of another event loop
        // are queued on their event loop (the shed can be shared by browsing contexts of several threads)
        var currentLoop = navigable.EventLoop;
        foreach (var owner in navigable.LocalStorageShed.GetBrowsingContexts())
        {
            if (ReferenceEquals(owner.EventLoop, currentLoop))
            {
                BroadcastToGroup(navigable.Top, owner.GetGroupTopLevelBrowsingContexts(), thisDocument, storageKey, key, oldValue, newValue, url, isSession: false);
            }
            else
            {
                owner.EventLoop.QueueTask(() => BroadcastToGroup(navigable.Top, owner.GetGroupTopLevelBrowsingContexts(), thisDocument, storageKey, key, oldValue, newValue, url, isSession: false));
            }
        }
    }

    private static void BroadcastToGroup(BrowsingContext sourceTop, IEnumerable<BrowsingContext> topLevelBrowsingContexts, Document thisDocument, StorageKey storageKey, string? key, string? oldValue, string? newValue, string url, bool isSession)
    {
        foreach (var topLevel in topLevelBrowsingContexts)
        {
            if (topLevel.IsDestroyed || (isSession && !ReferenceEquals(topLevel, sourceTop)))
                continue;

            foreach (var navigable in topLevel.GetInclusiveDescendantNavigables())
            {
                var document = navigable.Document;
                // The Storage objects of the same storage key: the same origin (and partition nonce)
                if (ReferenceEquals(document, thisDocument) || document.RelevantWindow is not { } window || Window.GetStorageKey(document) != storageKey)
                    continue;

                // Queue a global task on the DOM manipulation task source given the relevant global object of the remote storage
                document.QueueGlobalTask(() =>
                {
                    Storage storageArea;
                    try
                    {
                        storageArea = isSession ? window.SessionStorage : window.LocalStorage;
                    }
                    catch (DomException)
                    {
                        return;
                    }

                    window.FireEvent(new StorageEvent("storage", new StorageEventInit
                    {
                        Key = key,
                        OldValue = oldValue,
                        NewValue = newValue,
                        Url = url,
                        StorageArea = storageArea,
                    })
                    {
                        RealmWindow = window,
                    });
                });
            }
        }
    }
}
