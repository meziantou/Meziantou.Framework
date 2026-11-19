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
        if (GetRegistry(out var registry, out var storageKey) is { } error)
            return Task.FromException<StorageBucket>(error);

        if (!IsValidName(name))
            return Task.FromException<StorageBucket>(new TypeErrorException($"The bucket name '{name}' is not valid"));

        if (options.Quota is { } quota && quota is 0 or > MaxQuota)
            return Task.FromException<StorageBucket>(new TypeErrorException("The quota must be between 1 and Number.MAX_SAFE_INTEGER"));

        var record = registry.Open(_global, storageKey, name, options);
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
        if (GetRegistry(out var registry, out var storageKey) is { } error)
            return Task.FromException<IReadOnlyList<string>>(error);

        return Complete(registry.Keys(_global, storageKey));
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
        if (GetRegistry(out var registry, out var storageKey) is { } error)
            return Task.FromException(error);

        if (!IsValidName(name))
            return Task.FromException(new TypeErrorException($"The bucket name '{name}' is not valid"));

        registry.Delete(_global, storageKey, name);
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

    /// <summary>Gets the registry of the buckets and the storage key of the environment, or returns the error of the methods.</summary>
    private Exception? GetRegistry(out StorageBucketRegistry registry, out StorageKey storageKey)
    {
        registry = null!;
        storageKey = default;
        var environment = _global.EnvironmentDocument;
        if (!_global.CanRunScript || environment.EnvironmentBrowsingContext is not { } browsingContext)
            return new TypeErrorException("The document is not fully active");

        if (StorageKey.Obtain(environment) is not { } key)
            return new DomException("The storage buckets cannot be used by a document whose origin is opaque", "SecurityError");

        registry = browsingContext.StorageBuckets;
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

/// <summary>A storage bucket of a storage shelf (https://wicg.github.io/storage-buckets/#storage-bucket): its storage key (whose <see cref="StorageKey.Bucket"/> is its name and the identifier of its lifetime) and its metadata.</summary>
internal sealed class StorageBucketRecord(long id, string name, StorageKey key)
{
    /// <summary>The identifier of the bucket: a bucket created again with the same name is another bucket.</summary>
    public long Id { get; } = id;

    public string Name { get; } = name;

    public StorageKey Key { get; } = key;

    public ulong? Quota { get; init; }

    public double? Expires { get; set; }

    public bool IsDeleted { get; set; }
}

/// <summary>
/// The storage buckets of the storage keys of a browsing context group (https://wicg.github.io/storage-buckets/#storage-bucket-manager),
/// shared by its windows and their workers. Thread-safe: the workers use it from their threads.
/// </summary>
internal sealed class StorageBucketRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<StorageKey, SortedDictionary<string, StorageBucketRecord>> _buckets = [];
    private long _nextId;

    public StorageBucketRecord Open(GlobalScope global, StorageKey storageKey, string name, StorageBucketOptions options)
    {
        StorageBucketRecord? expired = null;
        StorageBucketRecord record;
        lock (_lock)
        {
            var buckets = GetBuckets(storageKey);
            if (buckets.TryGetValue(name, out var existing))
            {
                if (!IsExpired(global, existing))
                    return existing;

                // https://wicg.github.io/storage-buckets/#storage-bucket-expiration: an expired bucket is deleted
                expired = existing;
                buckets.Remove(name);
            }

            // Each lifetime of a bucket has its own storage: the objects of a deleted bucket (an open database, a file handle) and
            // the storage that its deletion keeps (a database with an open connection) are not the storage of a new bucket
            // of the same name
            var id = ++_nextId;
            record = new StorageBucketRecord(id, name, storageKey with { Bucket = name + "~" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            {
                Quota = options.Quota,
                Expires = options.Expires,
            };
            buckets.Add(name, record);
        }

        if (expired is not null)
        {
            ClearStorage(global, expired);
        }

        return record;
    }

    public IReadOnlyList<string> Keys(GlobalScope global, StorageKey storageKey)
    {
        List<StorageBucketRecord> expired = [];
        List<string> names = [];
        lock (_lock)
        {
            var buckets = GetBuckets(storageKey);
            foreach (var (name, record) in buckets.ToArray())
            {
                if (IsExpired(global, record))
                {
                    buckets.Remove(name);
                    expired.Add(record);
                }
                else
                {
                    names.Add(name);
                }
            }
        }

        foreach (var record in expired)
        {
            ClearStorage(global, record);
        }

        return names;
    }

    public void Delete(GlobalScope global, StorageKey storageKey, string name)
    {
        StorageBucketRecord? record;
        lock (_lock)
        {
            var buckets = GetBuckets(storageKey);
            if (!buckets.Remove(name, out record))
                return;
        }

        ClearStorage(global, record);
    }

    /// <summary>Deletes the buckets of a storage key (the <c>"storage"</c> type of <c>Clear-Site-Data</c>).</summary>
    public void DeleteAll(BrowsingContext browsingContext, StorageKey storageKey)
    {
        StorageBucketRecord[] records;
        lock (_lock)
        {
            if (!_buckets.Remove(storageKey, out var buckets))
                return;

            records = [.. buckets.Values];
        }

        foreach (var record in records)
        {
            ClearStorage(browsingContext, record);
        }
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

    private static void ClearStorage(GlobalScope global, StorageBucketRecord record)
    {
        if (global.EnvironmentDocument.EnvironmentBrowsingContext is { } browsingContext)
        {
            ClearStorage(browsingContext, record);
        }
        else
        {
            record.IsDeleted = true;
        }
    }

    /// <summary>
    /// Marks a removed bucket as deleted and removes its databases (except the ones with an open connection), its caches and
    /// its file system, in the backends that store them: the backends of the partition of the bucket (a bucket of a
    /// credentialless iframe is stored in the in-memory backends of its page, see <see cref="StorageEndpoints.GetBackend"/>).
    /// </summary>
    private static void ClearStorage(BrowsingContext browsingContext, StorageBucketRecord record)
    {
        record.IsDeleted = true;
        var origin = record.Key.Serialize();
        if (StorageEndpoints.GetBackend(browsingContext, record.Key, static context => context.IndexedDBBackend, static () => new InMemoryIndexedDBBackend()) is { } indexedDB)
        {
            ClearIndexedDB(indexedDB, origin);
        }

        if (StorageEndpoints.GetBackend(browsingContext, record.Key, static context => context.CacheStorageBackend, static () => new InMemoryCacheStorageBackend()) is { } cacheStorage)
        {
            var caches = cacheStorage.Coordinator;
            foreach (var name in caches.Keys(origin).ToArray())
            {
                caches.Delete(origin, name);
            }
        }

        if (StorageEndpoints.GetBackend(browsingContext, record.Key, static context => context.FileSystemBackend, static () => new InMemoryFileSystemBackend()) is { } fileSystem
            && fileSystem.GetEntries(origin, []) is { } entries)
        {
            foreach (var entry in entries)
            {
                fileSystem.RemoveEntry(origin, [entry.Name]);
            }
        }
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
