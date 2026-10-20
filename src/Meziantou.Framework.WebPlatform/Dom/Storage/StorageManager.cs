namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The result of <see cref="StorageManager.Estimate"/> (https://storage.spec.whatwg.org/#dictdef-storageestimate).</summary>
/// <seealso href="https://storage.spec.whatwg.org/#dictdef-storageestimate"/>
[IdlInterface("StorageEstimate", "https://storage.spec.whatwg.org/#dictdef-storageestimate")]
public sealed class StorageEstimate
{
    /// <summary>Gets an estimation of the number of bytes used by the storage of the origin.</summary>
    /// <seealso href="https://storage.spec.whatwg.org/#dom-storageestimate-usage"/>
    [IdlMember("usage", "https://storage.spec.whatwg.org/#dom-storageestimate-usage")]
    public ulong? Usage { get; init; }

    /// <summary>Gets the number of bytes that the origin can store.</summary>
    /// <seealso href="https://storage.spec.whatwg.org/#dom-storageestimate-quota"/>
    [IdlMember("quota", "https://storage.spec.whatwg.org/#dom-storageestimate-quota")]
    public ulong? Quota { get; init; }
}

/// <summary>
/// The options of the storage of the origins (https://storage.spec.whatwg.org/), see <see cref="BrowsingContextOptions.Storage"/>:
/// what <c>navigator.storage</c> (<see cref="StorageManager"/>) reports and enforces.
/// </summary>
public sealed record StorageOptions
{
    /// <summary>The default quota of an origin: 1 GiB.</summary>
    public const long DefaultQuota = 1024L * 1024 * 1024;

    internal static StorageOptions Default { get; } = new();

    /// <summary>
    /// Gets the quota of the storage of an origin, in bytes (https://storage.spec.whatwg.org/#storage-quota): the quota that
    /// <see cref="StorageManager.Estimate"/> reports, enforced by the origin private file system (a write that makes the
    /// usage of the file system exceed it fails with a <c>QuotaExceededError</c>). The other storage endpoints have their
    /// own limits (e.g. <see cref="StorageShed.Quota"/>). The default is <see cref="DefaultQuota"/>.
    /// </summary>
    public long Quota
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultQuota;

    /// <summary>
    /// Gets the callback called by <see cref="StorageManager.Persist"/> with the serialization of an origin, when the storage
    /// of the origin is not persistent yet: the host returns whether the "persistent-storage" permission is granted (a
    /// browser decides with its own heuristics, or asks the user). The default is null: the permission is denied, and the
    /// storage of the origins stays "best-effort".
    /// </summary>
    public Func<string, bool>? RequestPersistentStorage { get; init; }
}

/// <summary>
/// The storage manager of a window or of a worker (https://storage.spec.whatwg.org/#storagemanager,
/// <see cref="Navigator.Storage"/>): the estimation of the usage of the storage of its origin, the persistence of that
/// storage, and the root directory of its origin private file system (https://fs.spec.whatwg.org/).
/// </summary>
/// <seealso href="https://storage.spec.whatwg.org/#storagemanager"/>
[IdlInterface("StorageManager", "https://storage.spec.whatwg.org/#storagemanager")]
public sealed class StorageManager
{
    private readonly GlobalScope _global;

    internal StorageManager(GlobalScope global)
    {
        _global = global;
    }

    /// <summary>
    /// Returns whether the storage of the origin is persistent (https://storage.spec.whatwg.org/#dom-storagemanager-persisted):
    /// the mode of its local storage bucket is "persistent", so the user agent does not clear it under storage pressure. The
    /// task fails with a <see cref="TypeErrorException"/> when the origin is opaque.
    /// </summary>
    /// <seealso href="https://storage.spec.whatwg.org/#dom-storagemanager-persisted"/>
    [IdlMember("persisted", "https://storage.spec.whatwg.org/#dom-storagemanager-persisted")]
    public Task<bool> Persisted()
    {
        if (ObtainLocalStorageShelf() is not var (persistence, key))
            return Task.FromException<bool>(new TypeErrorException("The storage cannot be used by a document whose origin is opaque"));

        return Complete(persistence.IsPersistent(key));
    }

    /// <summary>
    /// Requests the storage of the origin to be persistent (https://storage.spec.whatwg.org/#dom-storagemanager-persist), and
    /// returns whether it is: the host grants the "persistent-storage" permission with
    /// <see cref="StorageOptions.RequestPersistentStorage"/> (it is denied by default). The request of a prerendering document
    /// is made when the document is activated.
    /// </summary>
    /// <seealso href="https://storage.spec.whatwg.org/#dom-storagemanager-persist"/>
    [IdlMember("persist", "https://storage.spec.whatwg.org/#dom-storagemanager-persist")]
    public Task<bool> Persist()
    {
        // https://wicg.github.io/nav-speculation/prerendering.html#storage-api-patch: [DelayWhilePrerendering]
        return Document.DelayWhilePrerendering(_global, () =>
        {
            if (ObtainLocalStorageShelf() is not var (persistence, key))
                return Task.FromException<bool>(new TypeErrorException("The storage cannot be used by a document whose origin is opaque"));

            if (!persistence.IsPersistent(key) && (_global.Options.Storage ?? StorageOptions.Default).RequestPersistentStorage is { } request && request(key.Origin))
            {
                persistence.SetPersistent(key);
            }

            return Complete(persistence.IsPersistent(key));
        });
    }

    /// <summary>
    /// Returns an estimation of the usage and of the quota of the storage of the origin
    /// (https://storage.spec.whatwg.org/#dom-storagemanager-estimate). The usage is the sum of what the storages of the origin
    /// report: the local storage, and the backends of IndexedDB (<see cref="IndexedDBBackend.GetUsage"/>), of the Cache API
    /// (<see cref="CacheStorageBackend.GetUsage"/>) and of the file system (<see cref="FileSystemBackend.GetUsage"/>). The
    /// quota is <see cref="StorageOptions.Quota"/>.
    /// </summary>
    /// <seealso href="https://storage.spec.whatwg.org/#dom-storagemanager-estimate"/>
    [IdlMember("estimate", "https://storage.spec.whatwg.org/#dom-storagemanager-estimate")]
    public Task<StorageEstimate> Estimate()
    {
        if (_global.EnvironmentDocument.Origin.IsOpaque)
            return Task.FromException<StorageEstimate>(new TypeErrorException("The storage cannot be used by a document whose origin is opaque"));

        StorageEstimate estimate;
        try
        {
            var quota = (_global.Options.Storage ?? StorageOptions.Default).Quota;
            estimate = new StorageEstimate { Usage = (ulong)Math.Max(0, GetUsage(_global)), Quota = (ulong)quota };
        }
#pragma warning disable CA1031 // "If ... failure, reject promise with a TypeError": the errors of the backends
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Task.FromException<StorageEstimate>(new TypeErrorException("The usage of the storage could not be estimated: " + ex.Message));
        }

        return Complete(estimate);
    }

    /// <summary>
    /// Returns the root directory of the origin private file system (https://fs.spec.whatwg.org/#dom-storagemanager-getdirectory),
    /// the bucket file system of the storage key of the window or of the worker, stored by
    /// <see cref="BrowsingContextOptions.FileSystem"/>. The task fails with a <c>SecurityError</c> when the origin is opaque.
    /// </summary>
    /// <seealso href="https://fs.spec.whatwg.org/#dom-storagemanager-getdirectory"/>
    [IdlMember("getDirectory", "https://fs.spec.whatwg.org/#dom-storagemanager-getdirectory")]
    public Task<FileSystemDirectoryHandle> GetDirectory()
    {
        if (ResolveFileSystem(_global) is not var (backend, origin))
            return Task.FromException<FileSystemDirectoryHandle>(new DomException("The file system cannot be used by a document whose origin is opaque", "SecurityError"));

        return Task.FromResult(new FileSystemDirectoryHandle(_global, backend, origin, []));
    }

    /// <summary>The promises of the storage manager are settled by tasks (the algorithms run in parallel, then queue a storage task).</summary>
    private Task<T> Complete<T>(T value)
    {
        var completion = new TaskCompletionSource<T>();
        _global.QueueGlobalTask(() => completion.TrySetResult(value));
        return completion.Task;
    }

    /// <summary>
    /// https://storage.spec.whatwg.org/#obtain-a-local-storage-shelf: the persistence state of the buckets of the browsing
    /// context group and the storage key of the global object, or null (failure) when its origin is opaque.
    /// </summary>
    private (StoragePersistenceState Persistence, StorageKey Key)? ObtainLocalStorageShelf()
    {
        var environment = _global.EnvironmentDocument;
        if (ServiceWorkerRegistry.GetStorageKey(environment) is not { } key)
            return null;

        var state = environment.EnvironmentBrowsingContext?.StoragePersistence ?? DetachedPersistence;
        return (state, key);
    }

    // The documents without browsing context have no persistent storage
    private static readonly StoragePersistenceState DetachedPersistence = new(readOnly: true);

    internal static (FileSystemBackend Backend, string Origin)? ResolveFileSystem(GlobalScope global)
        => StorageEndpoints.Resolve(global, global.Options.FileSystem, static context => context.FileSystemBackend, static () => new InMemoryFileSystemBackend());

    /// <summary>The usage of the storage endpoints of the storage key of a global object, as their storages report it.</summary>
    internal static long GetUsage(GlobalScope global)
    {
        var options = global.Options;
        long usage = 0;
        if (StorageEndpoints.Resolve(global, options.IndexedDB, static context => context.IndexedDBBackend, static () => new InMemoryIndexedDBBackend()) is var (indexedDB, indexedDBOrigin))
        {
            usage += indexedDB.GetUsage(indexedDBOrigin);
        }

        if (StorageEndpoints.Resolve(global, options.CacheStorage, static context => context.CacheStorageBackend, static () => new InMemoryCacheStorageBackend()) is var (caches, cachesOrigin))
        {
            usage += caches.GetUsage(cachesOrigin);
        }

        if (ResolveFileSystem(global) is var (fileSystem, fileSystemOrigin))
        {
            usage += fileSystem.GetUsage(fileSystemOrigin);
        }

        var environment = global.EnvironmentDocument;
        if (environment.EnvironmentBrowsingContext is { } browsingContext && ServiceWorkerRegistry.GetStorageKey(environment) is { } key)
        {
            usage += browsingContext.LocalStorageShed.GetUsage(key);
        }

        return usage;
    }

    /// <summary>
    /// Throws a <see cref="QuotaExceededError"/> when the file system of the origin cannot grow by <paramref name="additionalBytes"/>
    /// (https://storage.spec.whatwg.org/#storage-quota): its usage would exceed <see cref="StorageOptions.Quota"/>.
    /// </summary>
    internal static void CheckQuota(GlobalScope global, FileSystemBackend backend, string origin, long additionalBytes)
    {
        var quota = (global.Options.Storage ?? StorageOptions.Default).Quota;
        var usage = FileSystemHandle.Run(() => backend.GetUsage(origin));
        if (usage + additionalBytes > quota)
            throw new QuotaExceededError("The quota of the storage is exceeded", quota, usage + additionalBytes);
    }
}

/// <summary>
/// The mode of the local storage buckets of a browsing context group (https://storage.spec.whatwg.org/#bucket-mode): the
/// storage keys whose bucket is "persistent" (the other buckets are "best-effort"). Thread-safe: the workers read it from
/// their threads.
/// </summary>
internal sealed class StoragePersistenceState(bool readOnly = false)
{
    private readonly HashSet<StorageKey> _persistent = [];

    public bool IsPersistent(StorageKey key)
    {
        lock (_persistent)
        {
            return _persistent.Contains(key);
        }
    }

    public void SetPersistent(StorageKey key)
    {
        if (readOnly)
            return;

        lock (_persistent)
        {
            _persistent.Add(key);
        }
    }
}
