namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of <see cref="StorageBucketManager.Open"/> (https://wicg.github.io/storage-buckets/#dictdef-storagebucketoptions).</summary>
/// <seealso href="https://wicg.github.io/storage-buckets/#dictdef-storagebucketoptions"/>
[IdlInterface("StorageBucketOptions", "https://wicg.github.io/storage-buckets/#dictdef-storagebucketoptions")]
public sealed class StorageBucketOptions
{
    /// <summary>Gets whether the bucket is requested to be persistent (see <see cref="StorageBucket.Persist"/>).</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucketoptions-persisted"/>
    [IdlMember("persisted", "https://wicg.github.io/storage-buckets/#dom-storagebucketoptions-persisted")]
    public bool Persisted { get; init; }

    /// <summary>Gets the quota of the bucket, in bytes (between 1 and 2^53 - 1), or null for the quota of the origin.</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucketoptions-quota"/>
    [IdlMember("quota", "https://wicg.github.io/storage-buckets/#dom-storagebucketoptions-quota")]
    public ulong? Quota { get; init; }

    /// <summary>Gets the expiration time of the bucket, in milliseconds since the Unix epoch, or null.</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucketoptions-expires"/>
    [IdlMember("expires", "https://wicg.github.io/storage-buckets/#dom-storagebucketoptions-expires")]
    public double? Expires { get; init; }
}

/// <summary>
/// The storage buckets of the origin of a window or a worker (https://wicg.github.io/storage-buckets/#storagebucketmanager,
/// <see cref="Navigator.StorageBuckets"/>): named buckets whose IndexedDB databases, caches and origin private file system
/// are kept apart from the ones of the origin (its default bucket) and of the other buckets, and are removed with the
/// bucket. The buckets of an origin are shared by the windows and the workers of a browsing context group (in the
/// partition of the storage key of the environment).
/// </summary>
/// <seealso href="https://wicg.github.io/storage-buckets/#storagebucketmanager"/>
[IdlInterface("StorageBucketManager", "https://wicg.github.io/storage-buckets/#storagebucketmanager")]
public sealed class StorageBucketManager
{
    /// <summary>The maximum quota of a bucket: <c>Number.MAX_SAFE_INTEGER</c>.</summary>
    private const ulong MaxQuota = (1UL << 53) - 1;

    private readonly GlobalScope _global;

    internal StorageBucketManager(GlobalScope global)
    {
        _global = global;
    }

    /// <summary>
    /// Opens a bucket, created if needed (https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-open). The name
    /// starts with a lowercase ASCII letter or a digit, and has at most 64 lowercase ASCII letters, digits, <c>-</c> and
    /// <c>_</c>. The options are used when the bucket is created; an expired bucket is replaced by a new one. The task fails
    /// with a <see cref="TypeErrorException"/> when the name or the quota is invalid or the document is not fully active,
    /// and with a <c>SecurityError</c> <see cref="DomException"/> when the origin is opaque.
    /// </summary>
    /// <param name="name">The name of the bucket.</param>
    /// <param name="options">The options of a new bucket.</param>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-open"/>
    [IdlMember("open", "https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-open")]
    public Task<StorageBucket> Open(string name, StorageBucketOptions? options = null)
    {
        options ??= new StorageBucketOptions();
        if (GetRegistry(out var registry, out var browsingContext, out var storageKey) is { } error)
            return Task.FromException<StorageBucket>(error);

        if (!IsValidName(name))
            return Task.FromException<StorageBucket>(new TypeErrorException($"The bucket name '{name}' is not valid"));

        if (options.Quota is { } quota && quota is 0 or > MaxQuota)
            return Task.FromException<StorageBucket>(new TypeErrorException("The quota must be between 1 and Number.MAX_SAFE_INTEGER"));

        var record = registry.Open(_global, browsingContext, storageKey, name, options);
        if (options.Persisted)
        {
            StorageBucket.RequestPersistence(_global, record);
        }

        return Complete(new StorageBucket(_global, record));
    }

    /// <summary>
    /// Returns the names of the buckets of the origin, in lexicographic order (https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-keys).
    /// The task fails as <see cref="Open"/>'s.
    /// </summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-keys"/>
    [IdlMember("keys", "https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-keys")]
    public Task<IReadOnlyList<string>> Keys()
    {
        if (GetRegistry(out var registry, out var browsingContext, out var storageKey) is { } error)
            return Task.FromException<IReadOnlyList<string>>(error);

        return Complete(registry.Keys(_global, browsingContext, storageKey));
    }

    /// <summary>
    /// Deletes a bucket and its storage (https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-delete): its
    /// databases, its caches and its file system. The <see cref="StorageBucket"/> objects of the bucket then fail with an
    /// <c>UnknownError</c>. Deleting a bucket that does not exist does nothing. The task fails as <see cref="Open"/>'s.
    /// </summary>
    /// <param name="name">The name of the bucket.</param>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-delete"/>
    [IdlMember("delete", "https://wicg.github.io/storage-buckets/#dom-storagebucketmanager-delete")]
    public Task Delete(string name)
    {
        if (GetRegistry(out var registry, out var browsingContext, out var storageKey) is { } error)
            return Task.FromException(error);

        if (!IsValidName(name))
            return Task.FromException(new TypeErrorException($"The bucket name '{name}' is not valid"));

        registry.Delete(browsingContext, storageKey, name);
        return Complete<object?>(null);
    }

    /// <summary>https://wicg.github.io/storage-buckets/#valid-bucket-name</summary>
    internal static bool IsValidName(string name)
    {
        if (name.Length is 0 or > 64 || !IsLowerAlphanumeric(name[0]))
            return false;

        foreach (var c in name)
        {
            if (!IsLowerAlphanumeric(c) && c is not ('-' or '_'))
                return false;
        }

        return true;

        static bool IsLowerAlphanumeric(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
    }

    /// <summary>Gets the registry of the buckets, the browsing context and the storage key of the environment, or returns the error of the methods.</summary>
    private Exception? GetRegistry(out StorageBucketRegistry registry, out BrowsingContext browsingContext, out StorageKey storageKey)
    {
        registry = null!;
        browsingContext = null!;
        storageKey = default;
        var environment = _global.EnvironmentDocument;
        if (!_global.CanRunScript || environment.EnvironmentBrowsingContext is not { } context)
            return new TypeErrorException("The document is not fully active");

        if (StorageKey.Obtain(environment) is not { } key)
            return new DomException("The storage buckets cannot be used by a document whose origin is opaque", "SecurityError");

        registry = context.StorageBuckets;
        browsingContext = context;
        storageKey = key;
        return null;
    }

    private Task<T> Complete<T>(T value)
    {
        var completion = new TaskCompletionSource<T>();
        _global.QueueGlobalTask(() => completion.TrySetResult(value));
        return completion.Task;
    }
}

/// <summary>
/// A storage bucket (https://wicg.github.io/storage-buckets/#storagebucket): the IndexedDB databases
/// (<see cref="IndexedDB"/>), the caches and the origin private file system (<see cref="GetDirectory"/>) of a named bucket
/// of the origin, with its persistence, its quota and its expiration time. Once the bucket is deleted, the methods fail
/// with an <c>UnknownError</c> <see cref="DomException"/>.
/// </summary>
/// <seealso href="https://wicg.github.io/storage-buckets/#storagebucket"/>
[IdlInterface("StorageBucket", "https://wicg.github.io/storage-buckets/#storagebucket")]
public sealed class StorageBucket
{
    private readonly GlobalScope _global;
    private readonly StorageBucketRecord _record;

    internal StorageBucket(GlobalScope global, StorageBucketRecord record)
    {
        _global = global;
        _record = record;
    }

    /// <summary>Gets the name of the bucket.</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-name"/>
    [IdlMember("name", "https://wicg.github.io/storage-buckets/#dom-storagebucket-name")]
    public string Name => _record.Name;

    /// <summary>Gets the IndexedDB databases of the bucket.</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-indexeddb"/>
    [IdlMember("indexedDB", "https://wicg.github.io/storage-buckets/#dom-storagebucket-indexeddb")]
    public IdbFactory IndexedDB => field ??= new IdbFactory(_global, _record.Key, _record);

    /// <summary>
    /// Requests the bucket to be persistent (https://wicg.github.io/storage-buckets/#dom-storagebucket-persist), as
    /// <see cref="StorageManager.Persist"/>, and returns whether it is.
    /// </summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-persist"/>
    [IdlMember("persist", "https://wicg.github.io/storage-buckets/#dom-storagebucket-persist")]
    public Task<bool> Persist() => Run(() => RequestPersistence(_global, _record));

    /// <summary>Returns whether the bucket is persistent (https://wicg.github.io/storage-buckets/#dom-storagebucket-persisted).</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-persisted"/>
    [IdlMember("persisted", "https://wicg.github.io/storage-buckets/#dom-storagebucket-persisted")]
    public Task<bool> Persisted() => Run(() => GetPersistence(_global).IsPersistent(_record.Key));

    /// <summary>
    /// Returns an estimation of the usage of the bucket (its databases, caches and file system) and its quota: the quota of
    /// the bucket, or the quota of the origin (<see cref="StorageOptions.Quota"/>).
    /// </summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-estimate"/>
    [IdlMember("estimate", "https://wicg.github.io/storage-buckets/#dom-storagebucket-estimate")]
    public Task<StorageEstimate> Estimate()
    {
        if (_record.IsDeleted)
            return Task.FromException<StorageEstimate>(Deleted());

        return StorageManager.EstimateCore(_global, _record.Key, (long?)_record.Quota);
    }

    /// <summary>Sets the expiration time of the bucket, in milliseconds since the Unix epoch (https://wicg.github.io/storage-buckets/#dom-storagebucket-setexpires).</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-setexpires"/>
    [IdlMember("setExpires", "https://wicg.github.io/storage-buckets/#dom-storagebucket-setexpires")]
    public Task SetExpires(double expires) => Run<object?>(() =>
    {
        _record.Expires = expires;
        return null;
    });

    /// <summary>Returns the expiration time of the bucket, in milliseconds since the Unix epoch, or null (https://wicg.github.io/storage-buckets/#dom-storagebucket-expires).</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-expires"/>
    [IdlMember("expires", "https://wicg.github.io/storage-buckets/#dom-storagebucket-expires")]
    public Task<double?> Expires() => Run(() => _record.Expires);

    /// <summary>
    /// Returns the root directory of the origin private file system of the bucket (https://wicg.github.io/storage-buckets/#dom-storagebucket-getdirectory),
    /// whose writes are limited by the quota of the bucket. The task fails with an <c>InvalidStateError</c> once the
    /// bucket is deleted (as in Chromium).
    /// </summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-storagebucket-getdirectory"/>
    [IdlMember("getDirectory", "https://wicg.github.io/storage-buckets/#dom-storagebucket-getdirectory")]
    public Task<FileSystemDirectoryHandle> GetDirectory()
    {
        if (_record.IsDeleted)
            return Task.FromException<FileSystemDirectoryHandle>(DomException.InvalidState("The storage bucket was deleted"));

        return StorageManager.GetDirectoryCore(_global, _record.Key);
    }

    /// <summary>
    /// The storage key of the bucket, for its <c>caches</c> attribute (defined by the realm, as the CacheStorage objects are
    /// implemented by CacheStorage.js): the operations of the CacheStorage object of a deleted bucket fail with an <c>UnknownError</c>.
    /// </summary>
    internal StorageKey StorageKey => _record.Key;

    /// <summary>The token of the CacheStorage object of the bucket: a bucket created again after its deletion gets another CacheStorage object.</summary>
    internal string CacheStorageToken => _record.Key.Serialize();

    /// <summary>Whether the bucket of the CacheStorage object is deleted: its operations fail with an <c>UnknownError</c>.</summary>
    internal bool IsDeleted => _record.IsDeleted;

    internal static DomException Deleted() => new("The storage bucket was deleted", "UnknownError");

    /// <summary>Requests the persistence of a bucket (<see cref="StorageOptions.RequestPersistentStorage"/>) and returns whether it is persistent.</summary>
    internal static bool RequestPersistence(GlobalScope global, StorageBucketRecord record)
    {
        var persistence = GetPersistence(global);
        if (!persistence.IsPersistent(record.Key) && (global.Options.Storage ?? StorageOptions.Default).RequestPersistentStorage is { } request && request(record.Key.Origin))
        {
            persistence.SetPersistent(record.Key);
        }

        return persistence.IsPersistent(record.Key);
    }

    private static StoragePersistenceState GetPersistence(GlobalScope global) => global.EnvironmentDocument.EnvironmentBrowsingContext?.StoragePersistence ?? StorageManager.DetachedPersistence;

    /// <summary>Runs the steps of a method in parallel: the task is settled by a task, and fails with an <c>UnknownError</c> when the bucket is deleted.</summary>
    private Task<T> Run<T>(Func<T> steps)
    {
        if (_record.IsDeleted)
            return Task.FromException<T>(Deleted());

        var value = steps();
        var completion = new TaskCompletionSource<T>();
        _global.QueueGlobalTask(() => completion.TrySetResult(value));
        return completion.Task;
    }
}

/// <summary>
/// A storage bucket of a storage shelf (https://wicg.github.io/storage-buckets/#storage-bucket): its storage key (whose
/// <see cref="StorageKey.Bucket"/> is the identity of its lifetime, see <see cref="StorageBucketBackends"/>) and its metadata.
/// </summary>
internal sealed class StorageBucketRecord(string name, StorageKey key)
{
    public string Name { get; } = name;

    public StorageKey Key { get; } = key;

    /// <summary>The identity of the lifetime of the bucket: a bucket created again with the same name is another bucket.</summary>
    public string Identity => Key.Bucket!;

    public ulong? Quota { get; init; }

    public double? Expires { get; set; }

    public bool IsDeleted { get; set; }
}

/// <summary>
/// The storage buckets of the storage keys of a browsing context group (https://wicg.github.io/storage-buckets/#storage-bucket-manager),
/// shared by its windows and their workers. The buckets that exist are the ones whose identity is stored in the backends
/// (see <see cref="StorageBucketBackends"/>): the groups that share a backend, or that use a persistent backend again, see
/// the same buckets. The registry keeps the metadata of the buckets that the group opened (their quota and their expiration
/// time). Thread-safe: the workers use it from their threads.
/// </summary>
internal sealed class StorageBucketRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<StorageKey, SortedDictionary<string, StorageBucketRecord>> _buckets = [];

    public StorageBucketRecord Open(GlobalScope global, BrowsingContext browsingContext, StorageKey storageKey, string name, StorageBucketOptions options)
    {
        var backends = new StorageBucketBackends(browsingContext, storageKey);
        List<string> deleted = [];
        StorageBucketRecord record;
        lock (_lock)
        {
            var buckets = GetBuckets(storageKey);
            buckets.TryGetValue(name, out var existing);

            // https://wicg.github.io/storage-buckets/#storage-bucket-expiration: an expired bucket is deleted
            var expired = existing is not null && IsExpired(global, existing) ? existing.Identity : null;
            string identity;
            if (backends.HasBackend)
            {
                identity = backends.Open(name, expired, deleted);
            }
            else
            {
                if (existing is not null && expired is null)
                    return existing;

                if (expired is not null)
                {
                    deleted.Add(expired);
                }

                identity = StorageBucketBackends.CreateIdentity(name);
            }

            if (existing is not null)
            {
                if (existing.Identity == identity)
                    return existing;

                // The bucket expired, or was deleted by another browsing context group that shares the backends
                existing.IsDeleted = true;
                buckets.Remove(name);
            }

            record = new StorageBucketRecord(name, storageKey with { Bucket = identity })
            {
                Quota = options.Quota,
                Expires = options.Expires,
            };
            buckets.Add(name, record);
        }

        backends.Clear(deleted);
        return record;
    }

    public IReadOnlyList<string> Keys(GlobalScope global, BrowsingContext browsingContext, StorageKey storageKey)
    {
        var backends = new StorageBucketBackends(browsingContext, storageKey);
        List<string> deleted = [];
        IReadOnlyList<string> names;
        lock (_lock)
        {
            var buckets = GetBuckets(storageKey);
            if (backends.HasBackend)
            {
                var existing = backends.List((name, identity) => buckets.TryGetValue(name, out var record) && record.Identity == identity && IsExpired(global, record), deleted);
                foreach (var (name, record) in buckets.ToArray())
                {
                    // Expired, or deleted (and possibly created again) by another browsing context group that shares the backends
                    if (!existing.TryGetValue(name, out var identity) || identity != record.Identity)
                    {
                        record.IsDeleted = true;
                        buckets.Remove(name);
                    }
                }

                names = [.. existing.Keys];
            }
            else
            {
                foreach (var (name, record) in buckets.ToArray())
                {
                    if (IsExpired(global, record))
                    {
                        record.IsDeleted = true;
                        buckets.Remove(name);
                        deleted.Add(record.Identity);
                    }
                }

                names = [.. buckets.Keys];
            }
        }

        backends.Clear(deleted);
        return names;
    }

    public void Delete(BrowsingContext browsingContext, StorageKey storageKey, string name)
    {
        var backends = new StorageBucketBackends(browsingContext, storageKey);
        List<string> deleted = [];
        lock (_lock)
        {
            if (GetBuckets(storageKey).Remove(name, out var record))
            {
                record.IsDeleted = true;
                if (!backends.HasBackend)
                {
                    deleted.Add(record.Identity);
                }
            }

            // The bucket may have been created by another browsing context group that shares the backends, or before the
            // backends were used again
            backends.Delete(name, identity: null, deleted);
        }

        backends.Clear(deleted);
    }

    /// <summary>Deletes the buckets of a storage key (the <c>"storage"</c> type of <c>Clear-Site-Data</c>).</summary>
    public void DeleteAll(BrowsingContext browsingContext, StorageKey storageKey)
    {
        var backends = new StorageBucketBackends(browsingContext, storageKey);
        List<string> deleted = [];
        lock (_lock)
        {
            if (_buckets.Remove(storageKey, out var buckets))
            {
                foreach (var record in buckets.Values)
                {
                    record.IsDeleted = true;
                    if (!backends.HasBackend)
                    {
                        deleted.Add(record.Identity);
                    }
                }
            }

            backends.DeleteAll(deleted);
        }

        backends.Clear(deleted);
    }

    /// <summary>The quota of the bucket whose serialized storage key is <paramref name="serializedKey"/>, or null when it is not a bucket or has no quota.</summary>
    public long? GetQuota(string serializedKey)
    {
        lock (_lock)
        {
            foreach (var buckets in _buckets.Values)
            {
                foreach (var record in buckets.Values)
                {
                    if (record.Quota is { } quota && record.Key.Serialize() == serializedKey)
                        return (long)Math.Min(quota, long.MaxValue);
                }
            }
        }

        return null;
    }

    /// <summary>Deletes the databases of a serialized storage key that have no open connection.</summary>
    internal static void ClearIndexedDB(IndexedDBBackend indexedDB, string origin)
    {
        foreach (var database in indexedDB.GetDatabases(origin))
        {
            var state = indexedDB.Coordinator.GetDatabase(origin, database.Name);
            if (state.GetOpenConnections(except: null).Count > 0)
                continue;

            state.CallBackend(backend =>
            {
                backend.DeleteDatabase(origin, database.Name);
                return true;
            });
        }
    }

    private SortedDictionary<string, StorageBucketRecord> GetBuckets(StorageKey storageKey)
    {
        if (!_buckets.TryGetValue(storageKey, out var buckets))
        {
            buckets = new SortedDictionary<string, StorageBucketRecord>(StringComparer.Ordinal);
            _buckets.Add(storageKey, buckets);
        }

        return buckets;
    }

    private static bool IsExpired(GlobalScope global, StorageBucketRecord record)
        => record.Expires is { } expires && expires <= global.EventLoop.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();
}

/// <summary>
/// The backends that store the buckets of a storage key (IndexedDB, the Cache API and the origin private file system, see
/// <see cref="StorageEndpoints.GetBackend"/>), and the identities of the lifetimes of the buckets.
/// </summary>
/// <remarks>
/// <para>
/// The identity of a bucket is created with the bucket: its name, <c>~</c>, its order (16 hexadecimal digits) <c>~</c> and a
/// GUID. Its order is greater than the order of every identity and deletion of the bucket that the backends store, so a
/// bucket created again after its deletion has a greater order: the objects and the storage kept by the deleted one (an
/// open database, a file handle) do not reach it. The deletion of a bucket is stored as a tombstone (its identity followed
/// by <c>~deleted</c>), so that the bucket stays deleted for the browsing context groups that share a backend.
/// </para>
/// <para>
/// Each backend stores the identities and the latest tombstone of the buckets of a storage key in a namespace of its own:
/// the serialized storage key prefixed by <c>storage-buckets </c> (an origin has no space, so it is not the serialization
/// of a storage key), as the names of empty databases, of empty caches and of empty directories. The backends that share
/// the same data with other browsing context groups, or that are persistent, can disagree (a backend that another group
/// did not use when it deleted or created the bucket): the state of a bucket is its latest tombstone and its latest
/// identity, if this identity is greater than the tombstone (the bucket exists). Each operation reads and writes this state
/// in all the backends at once (under a lock shared by the groups), so two groups that create a bucket at the same time get
/// the same identity. The storage of an identity is only removed when the identity is deleted (it is not greater than the
/// latest tombstone): an older identity that is not deleted (two groups that created the bucket with backends that they did
/// not share) only loses its place in the backends, and its storage is kept.
/// </para>
/// </remarks>
internal sealed class StorageBucketBackends
{
    private const string TombstoneSuffix = "~deleted";

    // The browsing context groups that share a backend have their own registry
    private static readonly Lock IdentitiesLock = new();

    private readonly StorageKey _storageKey;
    private readonly IdentityStore[] _stores;

    public StorageBucketBackends(BrowsingContext browsingContext, StorageKey storageKey)
    {
        _storageKey = storageKey;
        var origin = "storage-buckets " + storageKey.Serialize();
        List<IdentityStore> stores = [];
        if (StorageEndpoints.GetBackend(browsingContext, storageKey, static context => context.IndexedDBBackend, static () => new InMemoryIndexedDBBackend()) is { } indexedDB)
        {
            stores.Add(new IndexedDBIdentityStore(indexedDB, origin));
        }

        if (StorageEndpoints.GetBackend(browsingContext, storageKey, static context => context.CacheStorageBackend, static () => new InMemoryCacheStorageBackend()) is { } cacheStorage)
        {
            stores.Add(new CacheStorageIdentityStore(cacheStorage, origin));
        }

        if (StorageEndpoints.GetBackend(browsingContext, storageKey, static context => context.FileSystemBackend, static () => new InMemoryFileSystemBackend()) is { } fileSystem)
        {
            stores.Add(new FileSystemIdentityStore(fileSystem, origin));
        }

        _stores = [.. stores];
    }

    /// <summary>Whether a backend stores the buckets: false for a credentialless iframe whose page is gone.</summary>
    public bool HasBackend => _stores.Length > 0;

    /// <summary>A hook of the tests: called by <see cref="Open"/> with the name of the bucket, between its lookup and the storage of its identity.</summary>
    internal static Action<string>? LookedUp { get; set; }

    /// <summary>Creates the identity of a bucket that no backend stores.</summary>
    public static string CreateIdentity(string name) => new Marker(name, 0, Guid.NewGuid(), IsTombstone: false).Identity;

    /// <summary>
    /// Returns the identities of the buckets that exist, by name in lexicographic order, after deleting the expired ones.
    /// </summary>
    /// <param name="isExpired">Returns whether the bucket of a name and an identity is expired (it is then deleted).</param>
    /// <param name="deleted">The identities whose storage is to be removed (see <see cref="Clear"/>).</param>
    public SortedDictionary<string, string> List(Func<string, string, bool> isExpired, List<string> deleted)
    {
        var buckets = new SortedDictionary<string, string>(StringComparer.Ordinal);
        lock (IdentitiesLock)
        {
            foreach (var (name, markers) in ReadMarkers())
            {
                var state = GetState(markers);
                if (state.Identity is { } identity && isExpired(name, identity.Identity))
                {
                    Store(markers, state with { Identity = null, Tombstone = identity with { IsTombstone = true } }, deleted);
                }
                else if (state.Identity is { } existing)
                {
                    buckets.Add(name, existing.Identity);
                }
            }
        }

        return buckets;
    }

    /// <summary>
    /// Returns the identity of a bucket, created when the bucket does not exist or when its identity is
    /// <paramref name="expired"/> (the bucket is then deleted first), and stores it in the backends.
    /// </summary>
    /// <param name="name">The name of the bucket.</param>
    /// <param name="expired">The identity of the bucket if it is expired, or null.</param>
    /// <param name="deleted">The identities whose storage is to be removed (see <see cref="Clear"/>).</param>
    public string Open(string name, string? expired, List<string> deleted)
    {
        lock (IdentitiesLock)
        {
            var markers = ReadMarkers().GetValueOrDefault(name);
            var state = GetState(markers);
            LookedUp?.Invoke(name);
            if (state.Identity is { } identity && identity.Identity == expired)
            {
                state = state with { Identity = null, Tombstone = identity with { IsTombstone = true } };
            }

            if (state.Identity is null)
            {
                state = state with { Identity = new Marker(name, state.MaxOrder + 1, Guid.NewGuid(), IsTombstone: false) };
            }

            Store(markers, state, deleted);
            return state.Identity.Value.Identity;
        }
    }

    /// <summary>
    /// Deletes a bucket (when its identity is <paramref name="identity"/>, or whatever its identity when it is null): stores
    /// its tombstone in the backends.
    /// </summary>
    /// <param name="name">The name of the bucket.</param>
    /// <param name="identity">The identity of the bucket to delete, or null.</param>
    /// <param name="deleted">The identities whose storage is to be removed (see <see cref="Clear"/>).</param>
    public void Delete(string name, string? identity, List<string> deleted)
    {
        lock (IdentitiesLock)
        {
            DeleteCore(ReadMarkers().GetValueOrDefault(name), identity, deleted);
        }
    }

    /// <summary>Deletes all the buckets.</summary>
    public void DeleteAll(List<string> deleted)
    {
        lock (IdentitiesLock)
        {
            foreach (var markers in ReadMarkers().Values)
            {
                DeleteCore(markers, identity: null, deleted);
            }
        }
    }

    /// <summary>
    /// Removes the storage of deleted buckets from the backends: their databases (except the ones with an open connection),
    /// their caches and their file systems.
    /// </summary>
    public void Clear(IEnumerable<string> identities)
    {
        foreach (var identity in identities.Distinct(StringComparer.Ordinal))
        {
            var origin = (_storageKey with { Bucket = identity }).Serialize();
            foreach (var store in _stores)
            {
                store.Clear(origin);
            }
        }
    }

    private void DeleteCore(List<StoredMarker>? markers, string? identity, List<string> deleted)
    {
        var state = GetState(markers);
        if (state.Identity is { } current && (identity is null || current.Identity == identity))
        {
            state = state with { Identity = null, Tombstone = current with { IsTombstone = true } };
        }

        Store(markers, state, deleted);
    }

    /// <summary>Reads the markers of the backends (once each), by bucket name.</summary>
    private Dictionary<string, List<StoredMarker>> ReadMarkers()
    {
        var markers = new Dictionary<string, List<StoredMarker>>(StringComparer.Ordinal);
        for (var i = 0; i < _stores.Length; i++)
        {
            foreach (var value in _stores[i].GetMarkers())
            {
                if (Marker.Parse(value) is not { } marker)
                    continue;

                if (!markers.TryGetValue(marker.Name, out var list))
                {
                    list = [];
                    markers.Add(marker.Name, list);
                }

                list.Add(new StoredMarker(i, value, marker));
            }
        }

        return markers;
    }

    /// <summary>
    /// The state of a bucket in the backends (from its markers, see <see cref="ReadMarkers"/>): its latest tombstone, and its
    /// latest identity if it is greater than the tombstone.
    /// </summary>
    private static BucketState GetState(List<StoredMarker>? markers)
    {
        Marker? identity = null;
        Marker? tombstone = null;
        long maxOrder = 0;
        foreach (var (_, _, marker) in markers ?? [])
        {
            maxOrder = Math.Max(maxOrder, marker.Order);
            if (marker.IsTombstone)
            {
                if (tombstone is null || marker.IsGreaterThan(tombstone.Value))
                {
                    tombstone = marker;
                }
            }
            else if (identity is null || marker.IsGreaterThan(identity.Value))
            {
                identity = marker;
            }
        }

        if (identity is { } latest && tombstone is { } deletion && latest.Order <= deletion.Order)
        {
            identity = null;
        }

        return new BucketState(identity, tombstone, maxOrder);
    }

    /// <summary>
    /// Stores the state of a bucket in all the backends (whose markers of the bucket are <paramref name="markers"/>, read by
    /// <see cref="ReadMarkers"/> in the same lock): its identity and its tombstone replace the other ones. The other
    /// identities that the tombstone deletes (not greater than it) are added to <paramref name="deleted"/>; the storage of
    /// the other ones is kept.
    /// </summary>
    private void Store(List<StoredMarker>? markers, BucketState state, List<string> deleted)
    {
        var identity = state.Identity?.Value;
        var tombstone = state.Tombstone?.Value;
        for (var i = 0; i < _stores.Length; i++)
        {
            var store = _stores[i];
            var hasIdentity = false;
            var hasTombstone = false;
            foreach (var (storeIndex, value, marker) in markers ?? [])
            {
                if (storeIndex != i)
                    continue;

                if (value == identity)
                {
                    hasIdentity = true;
                    continue;
                }

                if (value == tombstone)
                {
                    hasTombstone = true;
                    continue;
                }

                store.Remove(value);
                if (!marker.IsTombstone && state.Tombstone is { } deletion && marker.Order <= deletion.Order)
                {
                    deleted.Add(marker.Identity);
                }
            }

            if (identity is not null && !hasIdentity)
            {
                store.Add(identity);
            }

            if (tombstone is not null && !hasTombstone)
            {
                store.Add(tombstone);
            }
        }
    }

    private readonly record struct BucketState(Marker? Identity, Marker? Tombstone, long MaxOrder);

    /// <summary>A marker read from a backend: the index of the backend, the stored value and the parsed marker.</summary>
    private readonly record struct StoredMarker(int Store, string Value, Marker Marker);

    /// <summary>An identity of a bucket, or the tombstone of a deleted identity, stored in a backend.</summary>
    private readonly record struct Marker(string Name, long Order, Guid Guid, bool IsTombstone)
    {
        public string Identity => Name + "~" + Order.ToString("x16", CultureInfo.InvariantCulture) + "~" + Guid.ToString("N");

        public string Value => IsTombstone ? Identity + TombstoneSuffix : Identity;

        public static Marker? Parse(string value)
        {
            var isTombstone = value.EndsWith(TombstoneSuffix, StringComparison.Ordinal);
            var parts = (isTombstone ? value[..^TombstoneSuffix.Length] : value).Split('~');
            if (parts.Length != 3
                || !StorageBucketManager.IsValidName(parts[0])
                || parts[1].Length != 16
                || !long.TryParse(parts[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var order)
                || !Guid.TryParseExact(parts[2], "N", out var guid))
            {
                return null;
            }

            return new Marker(parts[0], order, guid, isTombstone);
        }

        /// <summary>Whether the marker is greater than another one: its order is greater, or its GUID for the same order.</summary>
        public bool IsGreaterThan(Marker other) => Order != other.Order ? Order > other.Order : Guid.CompareTo(other.Guid) > 0;
    }

    /// <summary>A backend: the identities and the tombstones of the buckets that it stores, and the storage of the buckets.</summary>
    private abstract class IdentityStore
    {
        public abstract IReadOnlyList<string> GetMarkers();

        public abstract void Add(string marker);

        public abstract void Remove(string marker);

        /// <summary>Removes the storage of a serialized storage key.</summary>
        public abstract void Clear(string origin);
    }

    /// <summary>The markers are the names of empty databases (of version 1).</summary>
    private sealed class IndexedDBIdentityStore(IndexedDBBackend backend, string markersOrigin) : IdentityStore
    {
        public override IReadOnlyList<string> GetMarkers() => [.. backend.GetDatabases(markersOrigin).Select(database => database.Name)];

        public override void Add(string marker)
        {
            using var transaction = backend.BeginTransaction(markersOrigin, marker, IdbTransactionMode.VersionChange, []);
            transaction.SetVersion(1);
            transaction.Commit();
        }

        public override void Remove(string marker) => backend.DeleteDatabase(markersOrigin, marker);

        public override void Clear(string origin) => StorageBucketRegistry.ClearIndexedDB(backend, origin);
    }

    /// <summary>The markers are the names of empty caches.</summary>
    private sealed class CacheStorageIdentityStore(CacheStorageBackend backend, string markersOrigin) : IdentityStore
    {
        public override IReadOnlyList<string> GetMarkers() => backend.GetCacheNames(markersOrigin);

        public override void Add(string marker) => backend.CreateCache(markersOrigin, marker);

        public override void Remove(string marker) => backend.DeleteCache(markersOrigin, marker);

        public override void Clear(string origin)
        {
            var caches = backend.Coordinator;
            foreach (var name in caches.Keys(origin).ToArray())
            {
                caches.Delete(origin, name);
            }
        }
    }

    /// <summary>The markers are the names of empty directories of the root directory.</summary>
    private sealed class FileSystemIdentityStore(FileSystemBackend backend, string markersOrigin) : IdentityStore
    {
        public override IReadOnlyList<string> GetMarkers() => [.. (backend.GetEntries(markersOrigin, []) ?? []).Select(entry => entry.Name)];

        public override void Add(string marker) => backend.CreateEntry(markersOrigin, [marker], FileSystemHandleKind.Directory, modificationTime: 0);

        public override void Remove(string marker) => backend.RemoveEntry(markersOrigin, [marker]);

        public override void Clear(string origin)
        {
            if (backend.GetEntries(origin, []) is { } entries)
            {
                foreach (var entry in entries)
                {
                    backend.RemoveEntry(origin, [entry.Name]);
                }
            }
        }
    }
}

public sealed partial class Navigator
{
    /// <summary>Gets the storage buckets of the origin of the window (https://wicg.github.io/storage-buckets/#dom-navigatorstoragebuckets-storagebuckets).</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-navigatorstoragebuckets-storagebuckets"/>
    [IdlMember("storageBuckets", "https://wicg.github.io/storage-buckets/#dom-navigatorstoragebuckets-storagebuckets")]
    public StorageBucketManager StorageBuckets => field ??= new StorageBucketManager(_window);
}

public sealed partial class WorkerNavigator
{
    /// <summary>Gets the storage buckets of the origin of the worker (https://wicg.github.io/storage-buckets/#dom-navigatorstoragebuckets-storagebuckets).</summary>
    /// <seealso href="https://wicg.github.io/storage-buckets/#dom-navigatorstoragebuckets-storagebuckets"/>
    [IdlMember("storageBuckets", "https://wicg.github.io/storage-buckets/#dom-navigatorstoragebuckets-storagebuckets")]
    public StorageBucketManager StorageBuckets => field ??= new StorageBucketManager(_global);
}

public sealed partial class BrowsingContext
{
    private StorageBucketRegistry? _storageBuckets;

    /// <summary>The storage buckets of the browsing context group, shared by its windows and their workers.</summary>
    internal StorageBucketRegistry StorageBuckets => ReferenceEquals(GroupOwner, this) ? LazyInitializer.EnsureInitialized(ref _storageBuckets) : GroupOwner.StorageBuckets;
}
