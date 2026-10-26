using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The global object of a service worker (https://w3c.github.io/ServiceWorker/#serviceworkerglobalscope-interface): it
/// receives the lifecycle events (<c>install</c>, <c>activate</c>) and the functional events (<c>fetch</c>,
/// <c>message</c>) of its service worker. It runs on the thread of the service worker (see <see cref="WorkerGlobalScope"/>).
/// </summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#serviceworkerglobalscope"/>
[IdlInterface("ServiceWorkerGlobalScope", "https://w3c.github.io/ServiceWorker/#serviceworkerglobalscope")]
public sealed class ServiceWorkerGlobalScope : WorkerGlobalScope
{
    private Clients? _clients;

    internal ServiceWorkerGlobalScope(WorkerAgent agent, UrlRecord url)
        : base(agent, url, "")
    {
    }

    /// <summary>The service worker of the global object.</summary>
    internal ServiceWorkerRecord ServiceWorkerRecord => Agent.ServiceWorker!;

    internal override ExposureSet Exposure => ExposureSet.ServiceWorker;

    internal override string InterfaceName => "ServiceWorkerGlobalScope";

    /// <summary>Gets the clients of the service worker (https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-clients).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-clients"/>
    [IdlMember("clients", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-clients")]
    public Clients Clients => _clients ??= new Clients(this);

    /// <summary>Gets the registration of the service worker (https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-registration).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-registration"/>
    [IdlMember("registration", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-registration")]
    public ServiceWorkerRegistration Registration => GetServiceWorkerRegistrationObject(ServiceWorkerRecord.Registration);

    /// <summary>Gets the service worker (https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-serviceworker).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-serviceworker"/>
    [IdlMember("serviceWorker", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-serviceworker")]
    public ServiceWorker ServiceWorker => GetServiceWorkerObject(ServiceWorkerRecord);

    /// <summary>
    /// Activates the service worker without waiting for the clients of the active worker to be unloaded
    /// (https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-skipwaiting).
    /// </summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-skipwaiting"/>
    [IdlMember("skipWaiting", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-skipwaiting")]
    public Task SkipWaiting()
    {
        var promise = new TaskCompletionSource();
        var worker = ServiceWorkerRecord;
        var registry = worker.Registration.Registry;
        void Resolve() => QueueGlobalTask(() => promise.TrySetResult());
        lock (registry.Lock)
        {
            worker.SkipWaiting = true;
            worker.SkipWaitingResolvers.Add(Resolve);
        }

        // The promise is resolved once Try Activate ran. The specification resolves it in parallel after the Activate
        // algorithm, without ordering it with the task that sets the "activated" state; as in the browsers, the promise of
        // the worker that is activated is resolved once its activate event is dispatched, before the state changes (see
        // ServiceWorkerRegistry.ResolveSkipWaitingPromises).
        registry.RunInParallel(async () =>
        {
            await registry.TryActivateAsync(worker.Registration).ConfigureAwait(false);
            registry.ResolveSkipWaitingPromises(worker);
        });
        return promise.Task;
    }

    /// <summary>Gets or sets the event handler of the <c>install</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-oninstall"/>
    [IdlMember("oninstall", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-oninstall")]
    public Action<Event>? OnInstall
    {
        get => GetEventHandler("install");
        set => SetEventHandler("install", value);
    }

    /// <summary>Gets or sets the event handler of the <c>activate</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onactivate"/>
    [IdlMember("onactivate", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onactivate")]
    public Action<Event>? OnActivate
    {
        get => GetEventHandler("activate");
        set => SetEventHandler("activate", value);
    }

    /// <summary>Gets or sets the event handler of the <c>fetch</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onfetch"/>
    [IdlMember("onfetch", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onfetch")]
    public Action<Event>? OnFetch
    {
        get => GetEventHandler("fetch");
        set => SetEventHandler("fetch", value);
    }

    /// <summary>Gets or sets the event handler of the <c>message</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onmessage"/>
    [IdlMember("onmessage", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>Gets or sets the event handler of the <c>messageerror</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onmessageerror"/>
    [IdlMember("onmessageerror", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerglobalscope-onmessageerror")]
    public Action<Event>? OnMessageError
    {
        get => GetEventHandler("messageerror");
        set => SetEventHandler("messageerror", value);
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#update-service-worker-extended-events-set, called after an event was dispatched:
    /// an active event is added to the extended events of the service worker until it is no longer active.
    /// </summary>
    internal void UpdateExtendedEvents(ExtendableEvent @event)
    {
        if (@event.IsActive && @event.ExtendedWorker is null)
        {
            @event.ExtendedWorker = ServiceWorkerRecord;
            Interlocked.Increment(ref ServiceWorkerRecord.ActiveExtendedEventCount);
        }

        @event.OnDispatched();
    }

    /// <summary>
    /// The perform the fetch hook of <c>importScripts()</c> (https://w3c.github.io/ServiceWorker/#importscripts): the scripts
    /// are fetched while the service worker is installed and stored in its script resource map; afterwards they are only
    /// read from the map.
    /// </summary>
    private protected override FetchedWorkerScript? FetchImportedScript(UrlRecord url, FileApi.BlobUrlEntry? blobUrlEntry, out bool mutedErrors)
    {
        mutedErrors = false;
        var worker = ServiceWorkerRecord;
        var registration = worker.Registration;
        var registry = registration.Registry;
        var key = url.Serialize();
        StoredServiceWorkerScript? stored;
        bool canFetch;
        bool isStale;
        ServiceWorkerUpdateViaCache updateViaCache;
        lock (registry.Lock)
        {
            worker.ScriptResourceMap.TryGetValue(key, out stored);
            canFetch = worker.State is ServiceWorkerState.Parsed or ServiceWorkerState.Installing;
            if (stored is not null && canFetch)
            {
                worker.UsedScripts.Add(key);
            }

            isStale = registration.IsStale(registry.Now);
            updateViaCache = worker.InstallingUpdateViaCache ?? registration.UpdateViaCache;
        }

        if (stored is not null)
        {
            // https://w3c.github.io/ServiceWorker/#importscripts: a stored bad import script response is a network error
            if (WorkerScriptFetching.IsBadImportScript(stored))
                return null;

            var script = WorkerScriptFetching.FromStoredScript(stored);
            mutedErrors = script.Url.SerializeOrigin() != Url.SerializeOrigin();
            return script;
        }

        if (!canFetch)
            return null;

        var cacheMode = updateViaCache == ServiceWorkerUpdateViaCache.None || isStale ? "no-cache" : "default";
        var response = WorkerScriptFetching.FetchImportedScriptResponse(this, url, blobUrlEntry, ServiceWorkersMode.None, cacheMode);
        lock (registry.Lock)
        {
            registration.LastUpdateCheckTime = registry.Now;
        }

        if (WorkerScriptFetching.IsBadImportScriptResponse(response))
            return null;

        lock (registry.Lock)
        {
            worker.ScriptResourceMap[key] = WorkerScriptFetching.ToStoredScript(key, response);
            worker.UsedScripts.Add(key);
            worker.ClassicScriptsImported = true;
        }

        return WorkerScriptFetching.ProcessImportedScriptResponse(this, response, out mutedErrors);
    }

    /// <summary>
    /// The perform the fetch hook of the static imports of a module service worker (https://w3c.github.io/ServiceWorker/#update-algorithm):
    /// as the scripts of <c>importScripts()</c>, they are fetched while the service worker is installed and stored in its
    /// script resource map; afterwards they are only read from the map. The specification only stores the main script of a
    /// module service worker; the browsers store its imports too, and check them when the registration is updated.
    /// <paramref name="processResponse"/> is called from a task.
    /// </summary>
    internal void FetchStaticImport(FetchRequest request, ResourceTiming? timing, Action<FetchResponse> processResponse)
    {
        var worker = ServiceWorkerRecord;
        var registration = worker.Registration;
        var registry = registration.Registry;
        var key = request.Url.Serialize(excludeFragment: true);
        StoredServiceWorkerScript? stored;
        bool canFetch;
        bool isStale;
        ServiceWorkerUpdateViaCache updateViaCache;
        lock (registry.Lock)
        {
            worker.ScriptResourceMap.TryGetValue(key, out stored);
            canFetch = worker.State is ServiceWorkerState.Parsed or ServiceWorkerState.Installing;
            if (stored is not null && canFetch)
            {
                worker.UsedScripts.Add(key);
            }

            isStale = registration.IsStale(registry.Now);
            updateViaCache = worker.InstallingUpdateViaCache ?? registration.UpdateViaCache;
        }

        if (stored is not null)
        {
            var storedResponse = WorkerScriptFetching.ToResponse(stored);
            EventLoop.QueueTask(() => processResponse(storedResponse));
            return;
        }

        if (!canFetch)
        {
            EventLoop.QueueTask(() => processResponse(FetchResponse.NetworkError()));
            return;
        }

        request.ServiceWorkersMode = ServiceWorkersMode.None;
        request.CacheMode = updateViaCache == ServiceWorkerUpdateViaCache.None || isStale ? "no-cache" : "default";
        Fetching.Fetch(
            request,
            response =>
            {
                if (request.IsSentToLoader && CanRunScript)
                {
                    timing?.Report(response);
                }

                lock (registry.Lock)
                {
                    registration.LastUpdateCheckTime = registry.Now;
                    if (!response.IsNetworkError && response.Status is >= 200 and <= 299)
                    {
                        worker.ScriptResourceMap[key] = WorkerScriptFetching.ToStoredScript(key, response);
                        worker.UsedScripts.Add(key);
                    }
                }

                processResponse(response);
            },
            EventLoop.QueueTask);
    }
}
