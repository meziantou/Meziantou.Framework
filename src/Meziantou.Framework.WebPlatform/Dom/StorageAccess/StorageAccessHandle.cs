using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The storages that <see cref="Document.RequestStorageAccess(StorageAccessTypes)"/> requests
/// (https://privacycg.github.io/saa-non-cookie-storage/#dictdef-storageaccesstypes): the members of the returned
/// <see cref="StorageAccessHandle"/> that can be used, and whether the document gets its unpartitioned cookies.
/// </summary>
/// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dictdef-storageaccesstypes"/>
[IdlInterface("StorageAccessTypes", "https://privacycg.github.io/saa-non-cookie-storage/#dictdef-storageaccesstypes")]
public sealed class StorageAccessTypes
{
    /// <summary>Gets whether all the storages are requested, the unpartitioned cookies included.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-all"/>
    [IdlMember("all", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-all")]
    public bool All { get; init; }

    /// <summary>Gets whether the unpartitioned cookies are requested (as <see cref="Document.RequestStorageAccess()"/>).</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-cookies"/>
    [IdlMember("cookies", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-cookies")]
    public bool Cookies { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.SessionStorage"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-sessionstorage"/>
    [IdlMember("sessionStorage", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-sessionstorage")]
    public bool SessionStorage { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.LocalStorage"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-localstorage"/>
    [IdlMember("localStorage", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-localstorage")]
    public bool LocalStorage { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.IndexedDB"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-indexeddb"/>
    [IdlMember("indexedDB", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-indexeddb")]
    public bool IndexedDB { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.Locks"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-locks"/>
    [IdlMember("locks", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-locks")]
    public bool Locks { get; init; }

    /// <summary>Gets whether the <c>caches</c> attribute of the handle (the Cache API of the scripts) is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-caches"/>
    [IdlMember("caches", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-caches")]
    public bool Caches { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.GetDirectory"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-getdirectory"/>
    [IdlMember("getDirectory", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-getdirectory")]
    public bool GetDirectory { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.Estimate"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-estimate"/>
    [IdlMember("estimate", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-estimate")]
    public bool Estimate { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.CreateObjectUrl(Blob)"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-createobjecturl"/>
    [IdlMember("createObjectURL", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-createobjecturl")]
    public bool CreateObjectUrl { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.RevokeObjectUrl"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-revokeobjecturl"/>
    [IdlMember("revokeObjectURL", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-revokeobjecturl")]
    public bool RevokeObjectUrl { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.CreateBroadcastChannel"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-broadcastchannel"/>
    [IdlMember("BroadcastChannel", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-broadcastchannel")]
    public bool BroadcastChannel { get; init; }

    /// <summary>Gets whether <see cref="StorageAccessHandle.CreateSharedWorker(string, SharedWorkerOptions?)"/> is requested.</summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-sharedworker"/>
    [IdlMember("SharedWorker", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesstypes-sharedworker")]
    public bool SharedWorker { get; init; }

    /// <summary>Whether no member is true (https://privacycg.github.io/saa-non-cookie-storage/#dom-document-requeststorageaccess-types, step 2).</summary>
    internal bool IsEmpty => !(All || Cookies || SessionStorage || LocalStorage || IndexedDB || Locks || Caches || GetDirectory || Estimate || CreateObjectUrl || RevokeObjectUrl || BroadcastChannel || SharedWorker);
}

/// <summary>
/// The access of a document of a third-party context to the storage of its origin as a first party (the unpartitioned
/// storage, https://privacycg.github.io/saa-non-cookie-storage/#storageaccesshandle), returned by
/// <see cref="Document.RequestStorageAccess(StorageAccessTypes)"/>: the members give the Web Storage areas, the IndexedDB
/// databases, the locks, the caches, the origin private file system, the usage estimate, the broadcast channels and the
/// shared workers of the storage key of the origin in a top-level document, instead of the ones of the partition of the
/// document. A member that the <see cref="StorageAccessTypes"/> of the request did not include throws an
/// <c>InvalidStateError</c> <see cref="DomException"/> (rejects its task).
/// </summary>
/// <remarks>
/// The blob URLs are not partitioned by the library: <see cref="CreateObjectUrl(Blob)"/> and <see cref="RevokeObjectUrl"/>
/// are <see cref="Url.CreateObjectUrl(Blob)"/> and <see cref="Url.RevokeObjectUrl"/>. A shared worker created by
/// <see cref="CreateSharedWorker(string, SharedWorkerOptions?)"/> is shared with the documents of the origin in their
/// first-party contexts; the <c>sameSiteCookies</c> option of the shared workers is not implemented.
/// </remarks>
/// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#storageaccesshandle"/>
[IdlInterface("StorageAccessHandle", "https://privacycg.github.io/saa-non-cookie-storage/#storageaccesshandle")]
public sealed class StorageAccessHandle
{
    private readonly Window _window;
    private readonly StorageAccessTypes _types;
    private readonly StorageKey _storageKey;

    internal StorageAccessHandle(Window window, StorageAccessTypes types, StorageKey storageKey)
    {
        _window = window;
        _types = types;
        _storageKey = storageKey;
    }

    /// <summary>
    /// Gets the session storage area of the origin in its first-party contexts of the top-level traversable (the area that
    /// the documents of the origin get from <see cref="Window.SessionStorage"/> when they are not embedded by another site).
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the session storage was not requested.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-sessionstorage"/>
    [IdlMember("sessionStorage", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-sessionstorage")]
    public Storage SessionStorage
    {
        get
        {
            CheckType(_types.SessionStorage, "sessionStorage");
            if (field is not null)
                return field;

            var browsingContext = GetBrowsingContext();
            field = CreateStorage(browsingContext.Top.SessionStorageShed.ObtainArea(_storageKey), isSession: true);
            return field;
        }
    }

    /// <summary>Gets the local storage area of the origin in its first-party contexts (see <see cref="Window.LocalStorage"/>).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the local storage was not requested, a <c>SecurityError</c> when the items cannot be loaded.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-localstorage"/>
    [IdlMember("localStorage", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-localstorage")]
    public Storage LocalStorage
    {
        get
        {
            CheckType(_types.LocalStorage, "localStorage");
            if (field is not null)
                return field;

            var browsingContext = GetBrowsingContext();
            StorageArea area;
            try
            {
                area = browsingContext.LocalStorageShed.ObtainArea(_storageKey);
            }
            catch (Exception ex) when (ex is not DomException)
            {
                throw new DomException("The local storage cannot be loaded", "SecurityError");
            }

            field = CreateStorage(area, isSession: false);
            return field;
        }
    }

    /// <summary>Gets the IndexedDB databases of the origin in its first-party contexts.</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when IndexedDB was not requested.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-indexeddb"/>
    [IdlMember("indexedDB", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-indexeddb")]
    public IdbFactory IndexedDB
    {
        get
        {
            CheckType(_types.IndexedDB, "indexedDB");
            return field ??= new IdbFactory(_window, _storageKey);
        }
    }

    /// <summary>Gets the lock manager of the origin in its first-party contexts.</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the locks were not requested.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-locks"/>
    [IdlMember("locks", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-locks")]
    public LockManager Locks
    {
        get
        {
            CheckType(_types.Locks, "locks");
            return field ??= new LockManager(_window, _storageKey);
        }
    }

    /// <summary>
    /// Returns the root directory of the origin private file system of the origin in its first-party contexts (see
    /// <see cref="StorageManager.GetDirectory"/>). The task fails with an <c>InvalidStateError</c> when it was not requested.
    /// </summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-getdirectory"/>
    [IdlMember("getDirectory", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-getdirectory")]
    public Task<FileSystemDirectoryHandle> GetDirectory()
    {
        if (!_types.All && !_types.GetDirectory)
            return Task.FromException<FileSystemDirectoryHandle>(NotRequested("getDirectory"));

        return StorageManager.GetDirectoryCore(_window, _storageKey);
    }

    /// <summary>
    /// Returns an estimation of the usage and of the quota of the storage of the origin in its first-party contexts (see
    /// <see cref="StorageManager.Estimate"/>). The task fails with an <c>InvalidStateError</c> when it was not requested.
    /// </summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-estimate"/>
    [IdlMember("estimate", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-estimate")]
    public Task<StorageEstimate> Estimate()
    {
        if (!_types.All && !_types.Estimate)
            return Task.FromException<StorageEstimate>(NotRequested("estimate"));

        return StorageManager.EstimateCore(_window, _storageKey);
    }

    /// <summary>Creates a <c>blob:</c> URL referencing a blob (see <see cref="Url.CreateObjectUrl(Blob)"/>).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when it was not requested.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-createobjecturl"/>
    [IdlMember("createObjectURL", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-createobjecturl")]
    public string CreateObjectUrl(Blob obj)
    {
        CheckType(_types.CreateObjectUrl, "createObjectURL");
        return Url.CreateObjectUrl(obj);
    }

    /// <summary>Creates a <c>blob:</c> URL referencing a media source (see <see cref="Url.CreateObjectUrl(MediaSource)"/>).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when it was not requested.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-createobjecturl"/>
    [IdlMember("createObjectURL", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-createobjecturl")]
    public string CreateObjectUrl(MediaSource obj)
    {
        CheckType(_types.CreateObjectUrl, "createObjectURL");
        return Url.CreateObjectUrl(obj);
    }

    /// <summary>Revokes a <c>blob:</c> URL (see <see cref="Url.RevokeObjectUrl"/>).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when it was not requested.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-revokeobjecturl"/>
    [IdlMember("revokeObjectURL", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-revokeobjecturl")]
    public void RevokeObjectUrl(string url)
    {
        CheckType(_types.RevokeObjectUrl, "revokeObjectURL");
        Url.RevokeObjectUrl(url);
    }

    /// <summary>
    /// Creates a broadcast channel that exchanges messages with the channels of the origin in its first-party contexts (instead
    /// of the channels of the partition of the document).
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when it was not requested.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-broadcastchannel"/>
    [IdlMember("BroadcastChannel", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-broadcastchannel")]
    public BroadcastChannel CreateBroadcastChannel(string name)
    {
        CheckType(_types.BroadcastChannel, "BroadcastChannel");
        return new BroadcastChannel(name, _storageKey);
    }

    /// <summary>Connects to a shared worker of the origin in its first-party contexts (see <see cref="Dom.SharedWorker"/>).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when it was not requested, a <c>SyntaxError</c> when the URL is invalid.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-sharedworker"/>
    [IdlMember("SharedWorker", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-sharedworker")]
    public SharedWorker CreateSharedWorker(string scriptUrl, string name) => CreateSharedWorker(scriptUrl, new SharedWorkerOptions { Name = name });

    /// <summary>Connects to a shared worker of the origin in its first-party contexts (see <see cref="Dom.SharedWorker"/>).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when it was not requested, a <c>SyntaxError</c> when the URL is invalid.</exception>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-sharedworker"/>
    [IdlMember("SharedWorker", "https://privacycg.github.io/saa-non-cookie-storage/#dom-storageaccesshandle-sharedworker")]
    public SharedWorker CreateSharedWorker(string scriptUrl, SharedWorkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scriptUrl);
        CheckType(_types.SharedWorker, "SharedWorker");
        return new SharedWorker(_window, scriptUrl, options, firstParty: true);
    }

    /// <summary>The storage key of the <c>caches</c> attribute (defined by the realm, as the CacheStorage objects are implemented by CacheStorage.js).</summary>
    internal StorageKey GetCachesStorageKey()
    {
        CheckType(_types.Caches, "caches");
        return _storageKey;
    }

    private Storage CreateStorage(StorageArea area, bool isSession)
    {
        var storage = new Storage(_window, area, isSession, _storageKey);

        // The storage events of the area are fired at the window with this Storage object as storageArea
        var document = _window.Document;
        (document.StorageAccessHandleStorages ??= []).Add(storage);
        return storage;
    }

    private BrowsingContext GetBrowsingContext() => _window.Document.BrowsingContext ?? throw DomException.InvalidState("The document is not fully active");

    private void CheckType(bool requested, string name)
    {
        if (!_types.All && !requested)
            throw NotRequested(name);
    }

    private static DomException NotRequested(string name) => DomException.InvalidState($"The storage access handle was not requested with '{name}'");
}

public partial class Document
{
    /// <summary>The <see cref="Storage"/> objects of the <see cref="StorageAccessHandle"/> objects of the document, which receive the <c>storage</c> events of their areas.</summary>
    internal List<Storage>? StorageAccessHandleStorages { get; set; }

    /// <summary>
    /// Returns whether the document has access to its unpartitioned cookies
    /// (https://privacycg.github.io/saa-non-cookie-storage/#dom-document-hasunpartitionedcookieaccess): the same result as
    /// <see cref="HasStorageAccess"/>.
    /// </summary>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-document-hasunpartitionedcookieaccess"/>
    [IdlMember("hasUnpartitionedCookieAccess", "https://privacycg.github.io/saa-non-cookie-storage/#dom-document-hasunpartitionedcookieaccess")]
    public Task<bool> HasUnpartitionedCookieAccess() => HasStorageAccess();

    /// <summary>
    /// Requests the access to the unpartitioned storage of the origin of the document
    /// (https://privacycg.github.io/saa-non-cookie-storage/#dom-document-requeststorageaccess-types): the request is granted
    /// as the one of <see cref="RequestStorageAccess()"/>, and the task completes with a <see cref="StorageAccessHandle"/>
    /// whose members give the storages that <paramref name="types"/> requests. The document gets its unpartitioned cookies
    /// only when <see cref="StorageAccessTypes.All"/> or <see cref="StorageAccessTypes.Cookies"/> is true. The task fails
    /// with an <c>InvalidStateError</c> <see cref="DomException"/> when no member of <paramref name="types"/> is true, and
    /// with the errors of <see cref="RequestStorageAccess()"/>.
    /// </summary>
    /// <param name="types">The requested storages.</param>
    /// <seealso href="https://privacycg.github.io/saa-non-cookie-storage/#dom-document-requeststorageaccess-types"/>
    [IdlMember("requestStorageAccess", "https://privacycg.github.io/saa-non-cookie-storage/#dom-document-requeststorageaccess-types")]
    public Task<StorageAccessHandle> RequestStorageAccess(StorageAccessTypes types)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (types.IsEmpty)
            return Task.FromException<StorageAccessHandle>(DomException.InvalidState("No storage is requested"));

        var window = RelevantWindow;
        var request = RequestStorageAccessCore(requestUnpartitionedCookieAccess: types.All || types.Cookies);
        return request.ContinueWith(task =>
        {
            if (task.Exception?.InnerException is { } exception)
                throw exception;

            // https://storage.spec.whatwg.org/#obtain-a-storage-key: the key of the origin when it is not embedded by another site
            if (window is null || StorageKey.Obtain(this) is not { } storageKey)
                throw DomException.InvalidState("The document is not fully active");

            return new StorageAccessHandle(window, types, storageKey.ToFirstParty());
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
