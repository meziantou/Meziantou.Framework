namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The mode of a lock (https://w3c.github.io/web-locks/#enumdef-lockmode).</summary>
/// <seealso href="https://w3c.github.io/web-locks/#enumdef-lockmode"/>
[IdlInterface("LockMode", "https://w3c.github.io/web-locks/#enumdef-lockmode")]
public enum LockMode
{
    /// <summary>The lock can be held with the other shared locks of the resource.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockmode-shared"/>
    [IdlMember("shared", "https://w3c.github.io/web-locks/#dom-lockmode-shared")]
    Shared,

    /// <summary>No other lock of the resource can be held.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockmode-exclusive"/>
    [IdlMember("exclusive", "https://w3c.github.io/web-locks/#dom-lockmode-exclusive")]
    Exclusive,
}

/// <summary>The options of <see cref="LockManager.Request(string, LockOptions, LockGrantedCallback)"/> (https://w3c.github.io/web-locks/#dictdef-lockoptions).</summary>
/// <seealso href="https://w3c.github.io/web-locks/#dictdef-lockoptions"/>
[IdlInterface("LockOptions", "https://w3c.github.io/web-locks/#dictdef-lockoptions")]
public sealed class LockOptions
{
    /// <summary>Gets the mode of the lock.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockoptions-mode"/>
    [IdlMember("mode", "https://w3c.github.io/web-locks/#dom-lockoptions-mode")]
    public LockMode Mode { get; init; } = LockMode.Exclusive;

    /// <summary>Gets whether the callback is called with null, instead of waiting, when the lock cannot be granted at once.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockoptions-ifavailable"/>
    [IdlMember("ifAvailable", "https://w3c.github.io/web-locks/#dom-lockoptions-ifavailable")]
    public bool IfAvailable { get; init; }

    /// <summary>Gets whether the held locks of the resource are released (their requests fail with an <c>AbortError</c>) and the lock is granted before the pending requests.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockoptions-steal"/>
    [IdlMember("steal", "https://w3c.github.io/web-locks/#dom-lockoptions-steal")]
    public bool Steal { get; init; }

    /// <summary>Gets the signal that aborts the request while the lock is not granted.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockoptions-signal"/>
    [IdlMember("signal", "https://w3c.github.io/web-locks/#dom-lockoptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>A held lock or a pending request of <see cref="LockManagerSnapshot"/> (https://w3c.github.io/web-locks/#dictdef-lockinfo).</summary>
/// <seealso href="https://w3c.github.io/web-locks/#dictdef-lockinfo"/>
[IdlInterface("LockInfo", "https://w3c.github.io/web-locks/#dictdef-lockinfo")]
public sealed class LockInfo
{
    /// <summary>Gets the name of the resource.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockinfo-name"/>
    [IdlMember("name", "https://w3c.github.io/web-locks/#dom-lockinfo-name")]
    public string? Name { get; init; }

    /// <summary>Gets the mode of the lock.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockinfo-mode"/>
    [IdlMember("mode", "https://w3c.github.io/web-locks/#dom-lockinfo-mode")]
    public LockMode? Mode { get; init; }

    /// <summary>Gets the id of the environment (the window or the worker) that holds or requests the lock: the id of its service worker client.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockinfo-clientid"/>
    [IdlMember("clientId", "https://w3c.github.io/web-locks/#dom-lockinfo-clientid")]
    public string? ClientId { get; init; }
}

/// <summary>The state of a lock manager returned by <see cref="LockManager.Query"/> (https://w3c.github.io/web-locks/#dictdef-lockmanagersnapshot).</summary>
/// <seealso href="https://w3c.github.io/web-locks/#dictdef-lockmanagersnapshot"/>
[IdlInterface("LockManagerSnapshot", "https://w3c.github.io/web-locks/#dictdef-lockmanagersnapshot")]
public sealed class LockManagerSnapshot
{
    /// <summary>Gets the held locks.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockmanagersnapshot-held"/>
    [IdlMember("held", "https://w3c.github.io/web-locks/#dom-lockmanagersnapshot-held")]
    public IReadOnlyList<LockInfo>? Held { get; init; }

    /// <summary>Gets the pending requests: the requests of a resource are in the order they were made.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockmanagersnapshot-pending"/>
    [IdlMember("pending", "https://w3c.github.io/web-locks/#dom-lockmanagersnapshot-pending")]
    public IReadOnlyList<LockInfo>? Pending { get; init; }
}

/// <summary>
/// The callback of <see cref="LockManager.Request(string, LockOptions, LockGrantedCallback)"/>
/// (https://w3c.github.io/web-locks/#callbackdef-lockgrantedcallback): the lock is held until the returned task completes.
/// </summary>
/// <param name="lock">The granted lock; null when the lock is not available and the <see cref="LockOptions.IfAvailable"/> option is set.</param>
/// <seealso href="https://w3c.github.io/web-locks/#callbackdef-lockgrantedcallback"/>
[IdlInterface("LockGrantedCallback", "https://w3c.github.io/web-locks/#callbackdef-lockgrantedcallback")]
#pragma warning disable CA1711 // The name of the IDL callback function
public delegate Task<object?> LockGrantedCallback(WebLock? @lock);
#pragma warning restore CA1711

/// <summary>
/// A granted lock (https://w3c.github.io/web-locks/#api-lock): the <c>Lock</c> interface, named <c>WebLock</c> in .NET as
/// <see cref="System.Threading.Lock"/> has its name.
/// </summary>
/// <seealso href="https://w3c.github.io/web-locks/#lock"/>
[IdlInterface("Lock", "https://w3c.github.io/web-locks/#lock")]
public sealed class WebLock
{
    internal WebLock(string name, LockMode mode)
    {
        Name = name;
        Mode = mode;
    }

    /// <summary>Gets the name of the resource.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lock-name"/>
    [IdlMember("name", "https://w3c.github.io/web-locks/#dom-lock-name")]
    public string Name { get; }

    /// <summary>Gets the mode of the lock.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lock-mode"/>
    [IdlMember("mode", "https://w3c.github.io/web-locks/#dom-lock-mode")]
    public LockMode Mode { get; }
}

/// <summary>
/// The locks of the storage key of a window or of a worker (https://w3c.github.io/web-locks/#api-lock-manager,
/// <see cref="Navigator.Locks"/>): named resources that the windows and the workers of an origin acquire to coordinate
/// their work. The locks are shared by the browsing contexts of a browsing context group and by their workers.
/// </summary>
/// <seealso href="https://w3c.github.io/web-locks/#lockmanager"/>
[IdlInterface("LockManager", "https://w3c.github.io/web-locks/#lockmanager")]
public sealed class LockManager
{
    private readonly GlobalScope _global;

    internal LockManager(GlobalScope global)
    {
        _global = global;
    }

    /// <summary>Requests an exclusive lock: see <see cref="Request(string, LockOptions, LockGrantedCallback)"/>.</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockmanager-request"/>
    [IdlMember("request", "https://w3c.github.io/web-locks/#dom-lockmanager-request")]
    public Task<object?> Request(string name, LockGrantedCallback callback) => Request(name, new LockOptions(), callback);

    /// <summary>
    /// Requests the lock of a resource (https://w3c.github.io/web-locks/#dom-lockmanager-request-name-options-callback):
    /// <paramref name="callback"/> is called from a task of the event loop once the lock is granted, and the lock is held
    /// until the task it returns completes. The returned task is completed with the result of the callback once the lock is
    /// released; it fails with the abort reason when the signal of the options aborts the request, with an
    /// <c>AbortError</c> when the lock is stolen, and with a <see cref="DomException"/> when the request is invalid.
    /// The request of a prerendering document is made when the document is activated.
    /// </summary>
    /// <param name="name">The name of the resource, which does not start with <c>-</c>.</param>
    /// <param name="options">The options.</param>
    /// <param name="callback">The callback, called with the lock (or null when it is not available, with the <see cref="LockOptions.IfAvailable"/> option).</param>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockmanager-request-name-options-callback"/>
    [IdlMember("request", "https://w3c.github.io/web-locks/#dom-lockmanager-request-name-options-callback")]
    public Task<object?> Request(string name, LockOptions options, LockGrantedCallback callback)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);

        // https://wicg.github.io/nav-speculation/prerendering.html#web-locks-patch: [DelayWhilePrerendering], the request of
        // a prerendering document is made when the document is activated
        return Document.DelayWhilePrerendering(_global, () => RequestCore(name, options, callback));
    }

    private Task<object?> RequestCore(string name, LockOptions options, LockGrantedCallback callback)
    {
        if (!_global.CanRunScript)
            return Task.FromException<object?>(DomException.InvalidState("The document is not fully active"));

        if (WebLocks.ObtainLockManager(_global) is not { } manager)
            return Task.FromException<object?>(new DomException("The locks cannot be used by a document whose origin is opaque", "SecurityError"));

        if (name.StartsWith('-', StringComparison.Ordinal))
            return Task.FromException<object?>(DomException.NotSupported("The names that start with '-' are reserved"));

        if (options.Steal && options.IfAvailable)
            return Task.FromException<object?>(DomException.NotSupported("The 'steal' and 'ifAvailable' options cannot be used together"));

        if (options.Steal && options.Mode != LockMode.Exclusive)
            return Task.FromException<object?>(DomException.NotSupported("The 'steal' option needs the 'exclusive' mode"));

        if (options.Signal is not null && (options.Steal || options.IfAvailable))
            return Task.FromException<object?>(DomException.NotSupported("The 'signal' option cannot be used with the 'steal' and 'ifAvailable' options"));

        if (options.Signal is { Aborted: true } aborted)
            return Task.FromException<object?>(aborted.AbortReason as Exception ?? new AbortSignalException(aborted.AbortReason));

        return manager.Request(_global, name, options, callback);
    }

    /// <summary>
    /// Returns the held locks and the pending requests of the lock manager (https://w3c.github.io/web-locks/#dom-lockmanager-query),
    /// of all the windows and workers that share it. As <c>request()</c>, it waits for the activation of a prerendering document.
    /// </summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-lockmanager-query"/>
    [IdlMember("query", "https://w3c.github.io/web-locks/#dom-lockmanager-query")]
    public Task<LockManagerSnapshot> Query() => Document.DelayWhilePrerendering(_global, QueryCore);

    private Task<LockManagerSnapshot> QueryCore()
    {
        if (!_global.CanRunScript)
            return Task.FromException<LockManagerSnapshot>(DomException.InvalidState("The document is not fully active"));

        if (WebLocks.ObtainLockManager(_global) is not { } manager)
            return Task.FromException<LockManagerSnapshot>(new DomException("The locks cannot be used by a document whose origin is opaque", "SecurityError"));

        // The promise is resolved by a task, after the callbacks of the locks that are already granted
        var promise = new TaskCompletionSource<LockManagerSnapshot>(TaskCreationOptions.None);
        var snapshot = manager.Snapshot();
        _global.QueueGlobalTask(() => promise.TrySetResult(snapshot));
        return promise.Task;
    }
}

/// <summary>
/// The key of a lock manager (https://w3c.github.io/web-locks/#lock-managers): the storage key of the environment, the site
/// of its top-level document and whether one of its ancestors is cross-site, so that the locks of the documents embedded
/// by another site are partitioned.
/// </summary>
internal readonly record struct LockManagerKey(StorageKey StorageKey, object TopLevelSite, bool HasCrossSiteAncestor);

/// <summary>The lock managers of a browsing context group, by key. Thread-safe: the workers obtain their lock manager from their threads.</summary>
internal sealed class LockManagerMap
{
    private readonly Dictionary<LockManagerKey, LockManagerState> _managers = [];

    public LockManagerState Get(LockManagerKey key)
    {
        lock (_managers)
        {
            if (!_managers.TryGetValue(key, out var manager))
            {
                manager = new LockManagerState();
                _managers.Add(key, manager);
            }

            return manager;
        }
    }
}

internal static class WebLocks
{
    /// <summary>
    /// https://w3c.github.io/web-locks/#obtain-a-lock-manager: the lock manager of the storage key of the environment in its
    /// browsing context group, or null (failure) when its origin is opaque or when the environment was discarded.
    /// </summary>
    public static LockManagerState? ObtainLockManager(GlobalScope global)
    {
        if (global.LockManagerState is { } existing)
            return existing;

        var environment = global.EnvironmentDocument;
        if (environment.EnvironmentBrowsingContext is not { } browsingContext || ServiceWorkerRegistry.GetStorageKey(environment) is not { } storageKey)
            return null;

        // The site of the top-level document of the window (of the window that created the worker), and whether the
        // document or a document of its ancestor navigables is cross-site with it: the storage of a third-party context
        // is partitioned
        var topLevelSite = environment.Origin.GetSiteKey();
        var hasCrossSiteAncestor = false;
        if (environment.EnvironmentRootDocument is { BrowsingContext: { } navigable } rootDocument && navigable.Top.Document.Origin is { IsOpaque: false } topLevelOrigin)
        {
            topLevelSite = topLevelOrigin.GetSiteKey();
            for (var ancestor = rootDocument; ancestor is not null && !hasCrossSiteAncestor; ancestor = ancestor.BrowsingContext?.Parent?.Document)
            {
                hasCrossSiteAncestor = !OriginRecord.IsSameSite(ancestor.Origin, topLevelOrigin);
            }
        }

        var manager = browsingContext.LockManagers.Get(new LockManagerKey(storageKey, topLevelSite, hasCrossSiteAncestor));
        global.LockManagerState = manager;
        return manager;
    }

    /// <summary>
    /// "Terminate remaining locks and requests" (https://w3c.github.io/web-locks/#lock-termination): the document of the
    /// window is unloaded or destroyed, or the worker ended.
    /// </summary>
    public static void OnEnvironmentDiscarded(GlobalScope global) => global.LockManagerState?.TerminateLocksAndRequests(global);
}

/// <summary>
/// A lock manager (https://w3c.github.io/web-locks/#lock-manager): the held locks and the lock request queues of a storage
/// key. The steps that the specification enqueues on the lock task queue run under a mutex, on the thread of the caller
/// (the thread of the event loop of a window or of a worker); the callbacks are called and the promises are settled by
/// tasks of the event loop of the environment of the request.
/// </summary>
internal sealed class LockManagerState
{
    private readonly Lock _gate = new();

    /// <summary>https://w3c.github.io/web-locks/#lock-manager-held-lock-set</summary>
    private readonly List<HeldLock> _held = [];

    /// <summary>https://w3c.github.io/web-locks/#lock-manager-lock-request-queue-map</summary>
    private readonly Dictionary<string, LinkedList<LockRequest>> _queues = new(StringComparer.Ordinal);

    /// <summary>https://w3c.github.io/web-locks/#request-a-lock</summary>
    public Task<object?> Request(GlobalScope client, string name, LockOptions options, LockGrantedCallback callback)
    {
        var request = new LockRequest(client, client.EnvironmentDocument.EnvironmentId, name, options.Mode, callback, options.Signal);
        if (options.Signal is { } signal)
        {
            // "signal to abort the request request with signal": the request is removed from its queue, and its promise is rejected
            request.AbortAlgorithm = () =>
            {
                lock (_gate)
                {
                    if (_queues.TryGetValue(name, out var queue) && queue.Remove(request))
                    {
                        ProcessQueue(name, queue);
                    }
                }

                request.Promise.TrySetException(signal.AbortReason as Exception ?? new AbortSignalException(signal.AbortReason));
            };
            signal.AddAbortAlgorithm(request.AbortAlgorithm);
        }

        lock (_gate)
        {
            var queue = GetQueue(name);
            if (options.Steal)
            {
                for (var i = _held.Count - 1; i >= 0; i--)
                {
                    var heldLock = _held[i];
                    if (heldLock.Name == name)
                    {
                        _held.RemoveAt(i);
                        heldLock.Client.QueueGlobalTask(() => heldLock.ReleasedPromise.TrySetException(new DomException("The lock was stolen by another request", "AbortError")));
                    }
                }

                queue.AddFirst(request);
            }
            else
            {
                if (options.IfAvailable && !IsGrantable(request, queue, isInQueue: false))
                {
                    RemoveQueueIfEmpty(name, queue);
                    client.QueueGlobalTask(() => InvokeCallback(request, heldLock: null));
                    return request.Promise.Task;
                }

                queue.AddLast(request);
            }

            ProcessQueue(name, queue);
        }

        return request.Promise.Task;
    }

    /// <summary>https://w3c.github.io/web-locks/#snapshot-the-lock-state</summary>
    public LockManagerSnapshot Snapshot()
    {
        lock (_gate)
        {
            var pending = new List<LockInfo>();
            foreach (var queue in _queues.Values)
            {
                foreach (var request in queue)
                {
                    pending.Add(new LockInfo { Name = request.Name, Mode = request.Mode, ClientId = request.ClientId });
                }
            }

            var held = new List<LockInfo>(_held.Count);
            foreach (var heldLock in _held)
            {
                held.Add(new LockInfo { Name = heldLock.Name, Mode = heldLock.Mode, ClientId = heldLock.ClientId });
            }

            return new LockManagerSnapshot { Held = held, Pending = pending };
        }
    }

    /// <summary>
    /// https://w3c.github.io/web-locks/#terminate-remaining-locks-and-requests: the requests of the environment are aborted
    /// and its locks are released (their promises are not settled: the environment is gone).
    /// </summary>
    public void TerminateLocksAndRequests(GlobalScope client)
    {
        lock (_gate)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, queue) in _queues)
            {
                for (var node = queue.First; node is not null;)
                {
                    var next = node.Next;
                    if (ReferenceEquals(node.Value.Client, client))
                    {
                        queue.Remove(node);
                        names.Add(name);
                    }

                    node = next;
                }
            }

            for (var i = _held.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_held[i].Client, client))
                {
                    names.Add(_held[i].Name);
                    _held.RemoveAt(i);
                }
            }

            foreach (var name in names)
            {
                if (_queues.TryGetValue(name, out var queue))
                {
                    ProcessQueue(name, queue);
                }
            }
        }
    }

    private LinkedList<LockRequest> GetQueue(string name)
    {
        if (!_queues.TryGetValue(name, out var queue))
        {
            queue = new LinkedList<LockRequest>();
            _queues.Add(name, queue);
        }

        return queue;
    }

    private void RemoveQueueIfEmpty(string name, LinkedList<LockRequest> queue)
    {
        if (queue.Count == 0)
        {
            _queues.Remove(name);
        }
    }

    /// <summary>https://w3c.github.io/web-locks/#grantable</summary>
    private bool IsGrantable(LockRequest request, LinkedList<LockRequest> queue, bool isInQueue)
    {
        // "If queue is not empty and request is not the first item in queue, then return false"
        if (queue.Count > 0 && !(isInQueue && ReferenceEquals(queue.First!.Value, request)))
            return false;

        foreach (var heldLock in _held)
        {
            if (heldLock.Name == request.Name && (request.Mode == LockMode.Exclusive || heldLock.Mode == LockMode.Exclusive))
                return false;
        }

        return true;
    }

    /// <summary>https://w3c.github.io/web-locks/#process-the-lock-request-queue</summary>
    private void ProcessQueue(string name, LinkedList<LockRequest> queue)
    {
        while (queue.First is { } node)
        {
            var request = node.Value;
            if (!IsGrantable(request, queue, isInQueue: true))
                break;

            queue.RemoveFirst();
            var heldLock = new HeldLock(request.Client, request.ClientId, request.Name, request.Mode, request.Promise);
            _held.Add(heldLock);

            // The task does not run when the document of the window is no longer fully active: its locks are released
            // when it is unloaded
            request.Client.QueueGlobalTask(() =>
            {
                if (request.Signal is { } signal)
                {
                    if (signal.Aborted)
                    {
                        Release(heldLock);
                        return;
                    }

                    signal.RemoveAbortAlgorithm(request.AbortAlgorithm!);
                }

                InvokeCallback(request, heldLock);
            });
        }

        RemoveQueueIfEmpty(name, queue);
    }

    /// <summary>
    /// Invokes the callback of a request with its lock (or with null, for an <c>ifAvailable</c> request that is not
    /// grantable), on the thread of its environment. When the returned promise (the waiting promise of the lock) settles,
    /// the lock is released and the promise of the request (the released promise) is resolved with it.
    /// </summary>
    private void InvokeCallback(LockRequest request, HeldLock? heldLock)
    {
        Task<object?> waiting;
        try
        {
            waiting = request.Callback(heldLock is null ? null : new WebLock(heldLock.Name, heldLock.Mode)) ?? Task.FromResult<object?>(null);
        }
#pragma warning disable CA1031 // The exception of the callback rejects the promise of the request
        catch (Exception ex)
#pragma warning restore CA1031
        {
            waiting = Task.FromException<object?>(ex);
        }

        // The promise of a script settles in a microtask of the thread of its environment
        _ = waiting.ContinueWith(
            completed =>
            {
                if (heldLock is not null)
                {
                    Release(heldLock);
                }

                if (completed.IsCompletedSuccessfully)
                {
                    request.Promise.TrySetResult(completed.Result);
                }
                else
                {
                    request.Promise.TrySetException(completed.Exception?.InnerException ?? new OperationCanceledException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>https://w3c.github.io/web-locks/#release-the-lock (a lock that was stolen is no longer held).</summary>
    private void Release(HeldLock heldLock)
    {
        lock (_gate)
        {
            if (_held.Remove(heldLock) && _queues.TryGetValue(heldLock.Name, out var queue))
            {
                ProcessQueue(heldLock.Name, queue);
            }
        }
    }

    /// <summary>https://w3c.github.io/web-locks/#lock-request</summary>
    private sealed class LockRequest(GlobalScope client, string clientId, string name, LockMode mode, LockGrantedCallback callback, AbortSignal? signal)
    {
        public GlobalScope Client { get; } = client;

        public string ClientId { get; } = clientId;

        public string Name { get; } = name;

        public LockMode Mode { get; } = mode;

        public LockGrantedCallback Callback { get; } = callback;

        public AbortSignal? Signal { get; } = signal;

        public Action? AbortAlgorithm { get; set; }

        public TaskCompletionSource<object?> Promise { get; } = new(TaskCreationOptions.None);
    }

    /// <summary>https://w3c.github.io/web-locks/#lock-concept</summary>
    private sealed class HeldLock(GlobalScope client, string clientId, string name, LockMode mode, TaskCompletionSource<object?> releasedPromise)
    {
        public GlobalScope Client { get; } = client;

        public string ClientId { get; } = clientId;

        public string Name { get; } = name;

        public LockMode Mode { get; } = mode;

        public TaskCompletionSource<object?> ReleasedPromise { get; } = releasedPromise;
    }
}
