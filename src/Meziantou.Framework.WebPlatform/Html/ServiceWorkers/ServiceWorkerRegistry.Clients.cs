namespace Meziantou.Framework.WebPlatform.Dom;

// The service worker clients (https://w3c.github.io/ServiceWorker/#dfn-service-worker-client) of a browsing context group:
// the windows and the dedicated and shared workers, registered when they are created and removed when they are discarded.
internal sealed partial class ServiceWorkerRegistry
{
    private readonly List<ServiceWorkerClientEntry> _clients = [];
    private long _nextClientOrder;

    /// <summary>
    /// Registers a new environment (the window of a document, or a dedicated or shared worker) as a service worker client
    /// of its browsing context group. The clients are only registered when the scripts run and the service workers are
    /// enabled (only a service worker uses them).
    /// </summary>
    internal static void OnEnvironmentCreated(GlobalScope global, ClientType type)
    {
        if (global.Options.ScriptEngine is null || !global.Options.EffectiveFeatures.ServiceWorkersEnabled)
            return;

        var environment = global.EnvironmentDocument;
        if (environment.ServiceWorkerClient is not null || Get(environment) is not { } registry)
            return;

        var frameType = FrameType.None;
        if (global is Window { BrowsingContext: { } browsingContext })
        {
            frameType = GetFrameType(browsingContext);
        }

        var entry = new ServiceWorkerClientEntry(registry, global, type, frameType, Interlocked.Increment(ref registry._nextClientOrder));
        environment.ServiceWorkerClient = entry;
        lock (registry._lock)
        {
            registry._clients.Add(entry);
        }
    }

    /// <summary>
    /// The environment is discarded (its document is unloaded, or its worker ends): it is no longer a client, and the
    /// registration it used may be activated or cleared (https://w3c.github.io/ServiceWorker/#on-client-unload-algorithm).
    /// </summary>
    internal static void OnEnvironmentDiscarded(Document environment)
    {
        if (environment.ServiceWorkerClient is not { } entry)
            return;

        var registry = entry.Registry;
        lock (registry._lock)
        {
            if (entry.IsDiscarded)
                return;

            entry.IsDiscarded = true;
            registry._clients.Remove(entry);
        }

        if (environment.ActiveServiceWorker?.Registration is { } registration)
        {
            registry.OnClientStoppedUsing(registration);
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#on-client-unload-algorithm: a client no longer uses the registration.</summary>
    internal void OnClientStoppedUsing(ServiceWorkerRegistrationRecord registration)
    {
        lock (_lock)
        {
            if (IsUsedLocked(registration))
                return;
        }

        TryClearOrActivateInParallel(registration);
    }

    /// <summary>The clients of the group, in creation order.</summary>
    internal List<ServiceWorkerClientEntry> GetClients()
    {
        lock (_lock)
        {
            return [.. _clients];
        }
    }

    /// <summary>The client whose id is <paramref name="id"/>, or null.</summary>
    internal ServiceWorkerClientEntry? GetClient(string id)
    {
        lock (_lock)
        {
            return _clients.Find(client => client.Id == id);
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#get-frame-type</summary>
    internal static FrameType GetFrameType(BrowsingContext browsingContext)
    {
        if (browsingContext.Parent is not null)
            return FrameType.Nested;

        return browsingContext.IsAuxiliary ? FrameType.Auxiliary : FrameType.TopLevel;
    }
}

/// <summary>
/// A service worker client of a browsing context group (https://w3c.github.io/ServiceWorker/#dfn-service-worker-client): the
/// environment of a window or of a dedicated or shared worker. Its members are read by the threads of the service workers;
/// its global object must only be used on its own thread.
/// </summary>
internal sealed class ServiceWorkerClientEntry
{
    private readonly WeakReference<GlobalScope> _global;

    public ServiceWorkerClientEntry(ServiceWorkerRegistry registry, GlobalScope global, ClientType type, FrameType frameType, long creationOrder)
    {
        Registry = registry;
        _global = new WeakReference<GlobalScope>(global);
        var environment = global.EnvironmentDocument;
        Id = environment.EnvironmentId;
        Type = type;
        FrameType = frameType;
        CreationOrder = creationOrder;
        CreationUrl = environment.Url;
        StorageKey = ServiceWorkerRegistry.GetStorageKey(environment);
        IsSecureContext = global.IsSecureContext;
    }

    public ServiceWorkerRegistry Registry { get; }

    /// <summary>The id of the environment (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-id).</summary>
    public string Id { get; }

    public ClientType Type { get; }

    public FrameType FrameType { get; }

    public long CreationOrder { get; }

    /// <summary>The serialized creation URL (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-creation-url).</summary>
    public string CreationUrl { get; }

    /// <summary>The storage key, or null for an opaque origin.</summary>
    public StorageKey? StorageKey { get; }

    public bool IsSecureContext { get; }

    /// <summary>Whether the environment was discarded (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-discarded). Guarded by the lock of the registry.</summary>
    public bool IsDiscarded { get; set; }

    /// <summary>The global object of the environment (a <see cref="Window"/> or a <see cref="WorkerGlobalScope"/>), or null when it was collected.</summary>
    public GlobalScope? Global => _global.TryGetTarget(out var global) ? global : null;

    /// <summary>The document that represents the environment, or null.</summary>
    public Document? Environment => Global?.EnvironmentDocument;

    /// <summary>Queues a task on the event loop of the environment (the task is dropped when it cannot run).</summary>
    public void QueueTask(Action task) => Global?.QueueGlobalTask(task);
}
