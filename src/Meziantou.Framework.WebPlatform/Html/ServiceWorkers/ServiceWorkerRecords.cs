using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A service worker registration (https://w3c.github.io/ServiceWorker/#dfn-service-worker-registration): its scope, and
/// its installing, waiting and active service workers. The members are guarded by the lock of its <see cref="ServiceWorkerRegistry"/>.
/// </summary>
internal sealed class ServiceWorkerRegistrationRecord
{
    public ServiceWorkerRegistrationRecord(ServiceWorkerRegistry registry, StorageKey storageKey, UrlRecord scopeUrl, ServiceWorkerUpdateViaCache updateViaCache)
    {
        Registry = registry;
        StorageKey = storageKey;
        ScopeUrl = scopeUrl;
        Scope = scopeUrl.Serialize();
        UpdateViaCache = updateViaCache;
    }

    public ServiceWorkerRegistry Registry { get; }

    /// <summary>The storage key (https://w3c.github.io/ServiceWorker/#service-worker-registration-storage-key).</summary>
    public StorageKey StorageKey { get; }

    public UrlRecord ScopeUrl { get; }

    /// <summary>The serialized scope URL.</summary>
    public string Scope { get; }

    public ServiceWorkerUpdateViaCache UpdateViaCache { get; set; }

    public DateTimeOffset? LastUpdateCheckTime { get; set; }

    public ServiceWorkerRecord? Installing { get; set; }

    public ServiceWorkerRecord? Waiting { get; set; }

    public ServiceWorkerRecord? Active { get; set; }

    public bool NavigationPreloadEnabled { get; set; }

    public string NavigationPreloadHeaderValue { get; set; } = "true";

    /// <summary>Whether the Activate algorithm runs for the registration.</summary>
    public bool ActivationInProgress { get; set; }

    /// <summary>Whether a soft update of the registration is scheduled (<see cref="ServiceWorkerRegistry.ScheduleSoftUpdate"/>).</summary>
    public bool IsSoftUpdateScheduled { get; set; }

    /// <summary>Whether the registration was removed from the registration map (https://w3c.github.io/ServiceWorker/#dfn-service-worker-registration-unregistered).</summary>
    public bool IsUnregistered { get; set; }

    /// <summary>https://w3c.github.io/ServiceWorker/#get-newest-worker</summary>
    public ServiceWorkerRecord? NewestWorker => Installing ?? Waiting ?? Active;

    /// <summary>https://w3c.github.io/ServiceWorker/#service-worker-registration-stale: the last update check is older than a day.</summary>
    public bool IsStale(DateTimeOffset now) => LastUpdateCheckTime is { } time && now - time > TimeSpan.FromDays(1);

    public StoredServiceWorkerRegistration ToStored() => new()
    {
        ScopeUrl = Scope,
        UpdateViaCache = UpdateViaCache switch
        {
            ServiceWorkerUpdateViaCache.All => "all",
            ServiceWorkerUpdateViaCache.None => "none",
            _ => "imports",
        },
        LastUpdateCheckTime = LastUpdateCheckTime,
        NavigationPreloadEnabled = NavigationPreloadEnabled,
        NavigationPreloadHeaderValue = NavigationPreloadHeaderValue,
        Waiting = Waiting?.ToStored(),
        Active = Active?.ToStored(),
    };

    public static ServiceWorkerRegistrationRecord? FromStored(ServiceWorkerRegistry registry, StorageKey storageKey, StoredServiceWorkerRegistration stored)
    {
        if (UrlParser.Parse(stored.ScopeUrl, baseUrl: null) is not { } scopeUrl)
            return null;

        var registration = new ServiceWorkerRegistrationRecord(registry, storageKey, scopeUrl, stored.UpdateViaCache switch
        {
            "all" => ServiceWorkerUpdateViaCache.All,
            "none" => ServiceWorkerUpdateViaCache.None,
            _ => ServiceWorkerUpdateViaCache.Imports,
        })
        {
            LastUpdateCheckTime = stored.LastUpdateCheckTime,
            NavigationPreloadEnabled = stored.NavigationPreloadEnabled,
            NavigationPreloadHeaderValue = stored.NavigationPreloadHeaderValue,
        };

        registration.Waiting = stored.Waiting is { } waiting ? ServiceWorkerRecord.FromStored(registration, waiting) : null;
        registration.Active = stored.Active is { } active ? ServiceWorkerRecord.FromStored(registration, active) : null;
        if (registration.Waiting is null && registration.Active is null)
            return null;

        return registration;
    }
}

/// <summary>
/// A service worker (https://w3c.github.io/ServiceWorker/#dfn-service-worker): its script and the responses of the scripts it
/// imports, its state, and its agent while it runs. The members are guarded by the lock of the registry, except the members
/// used by the thread of the service worker.
/// </summary>
internal sealed class ServiceWorkerRecord
{
    public ServiceWorkerRecord(ServiceWorkerRegistrationRecord registration, string id, UrlRecord scriptUrl, WorkerType type)
    {
        Registration = registration;
        Id = id;
        ScriptUrl = scriptUrl;
        Type = type;
    }

    /// <summary>The containing service worker registration (https://w3c.github.io/ServiceWorker/#dfn-containing-service-worker-registration).</summary>
    public ServiceWorkerRegistrationRecord Registration { get; }

    public string Id { get; }

    public UrlRecord ScriptUrl { get; }

    public WorkerType Type { get; }

    public ServiceWorkerState State { get; set; } = ServiceWorkerState.Parsed;

    /// <summary>https://w3c.github.io/ServiceWorker/#dfn-script-resource-map: the responses of the scripts, by URL (in fetch order).</summary>
    public OrderedDictionary<string, StoredServiceWorkerScript> ScriptResourceMap { get; } = new(StringComparer.Ordinal);

    /// <summary>https://w3c.github.io/ServiceWorker/#dfn-set-of-used-scripts</summary>
    public HashSet<string> UsedScripts { get; } = new(StringComparer.Ordinal);

    /// <summary>https://w3c.github.io/ServiceWorker/#dfn-classic-scripts-imported-flag</summary>
    public bool ClassicScriptsImported { get; set; }

    /// <summary>
    /// The environment of the client of the job that installs the worker (https://w3c.github.io/ServiceWorker/#update-algorithm):
    /// the fetch client of the static imports of a module script, whose content security policies apply to them, or null.
    /// </summary>
    public Document? UpdateJobClient { get; set; }

    /// <summary>https://w3c.github.io/ServiceWorker/#dfn-skip-waiting-flag</summary>
    public bool SkipWaiting { get; set; }

    /// <summary>The callbacks that resolve the pending promises of <c>skipWaiting()</c> (they queue a task on the event loop of the worker).</summary>
    public List<Action> SkipWaitingResolvers { get; } = [];

    /// <summary>
    /// Whether the Activate algorithm queued the task that dispatches the activate event of the worker (or skipped the
    /// event): the promises of <c>skipWaiting()</c> are resolved from then on. Guarded by the lock of the registry.
    /// </summary>
    public bool IsActivateEventQueued { get; set; }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#dfn-set-of-event-types-to-handle: the event types of the listeners of the global
    /// object after the first evaluation of the script, or null when it was never evaluated.
    /// </summary>
    public HashSet<string>? EventTypesToHandle { get; set; }

    /// <summary>https://w3c.github.io/ServiceWorker/#all-fetch-listeners-are-empty-flag</summary>
    public bool AllFetchListenersAreEmpty { get; set; }

    /// <summary>The agent of the running service worker, or null (https://w3c.github.io/ServiceWorker/#running).</summary>
    public WorkerAgent? Agent { get; set; }

    /// <summary>The start status of the running service worker: completed with false when its script threw an exception.</summary>
    public Task<ServiceWorkerStartStatus>? StartStatus { get; set; }

    /// <summary>The callbacks of the tasks queued on the event loop of the running worker, called when the tasks are discarded.</summary>
    public List<Action> PendingTaskDiscards { get; } = [];

    /// <summary>The tasks waiting for the activation of the worker (e.g. a fetch event of an activating worker).</summary>
    public List<ServiceWorkerWait<bool>> ActivationWaiters { get; } = [];

    /// <summary>https://w3c.github.io/ServiceWorker/#service-worker-set-of-extended-events (used on the thread of the service worker).</summary>
    public List<ExtendableEvent> ExtendedEvents { get; } = [];

    /// <summary>The number of extended events that are active (read by the other threads, see <see cref="HasNoPendingEvents"/>).</summary>
    public int ActiveExtendedEventCount;

    /// <summary>The number of functional events (fetch, message) being dispatched or waiting for the worker to start.</summary>
    public int PendingFunctionalEvents;

    /// <summary>
    /// The end of the last step that queued (or discarded) the event of a message posted to the worker: the events of the
    /// messages are queued in the order of the calls of <c>postMessage()</c>. It is read and replaced with the lock of the registry.
    /// </summary>
    public Task LastMessageQueued { get; set; } = Task.CompletedTask;

    /// <summary>https://w3c.github.io/ServiceWorker/#service-worker-has-no-pending-events</summary>
    public bool HasNoPendingEvents => Volatile.Read(ref ActiveExtendedEventCount) == 0 && Volatile.Read(ref PendingFunctionalEvents) == 0;

    /// <summary>The main script: the response of <see cref="ScriptUrl"/>.</summary>
    public StoredServiceWorkerScript? MainScript => ScriptResourceMap.TryGetValue(ScriptUrl.Serialize(), out var script) ? script : null;

    public StoredServiceWorker ToStored() => new()
    {
        Id = Id,
        ScriptUrl = ScriptUrl.Serialize(),
        Type = Type == WorkerType.Module ? "module" : "classic",
        State = State == ServiceWorkerState.Activated ? "activated" : "installed",
        ClassicScriptsImported = ClassicScriptsImported,
        EventTypesToHandle = EventTypesToHandle is { } types ? [.. types] : [],
        Scripts = [.. ScriptResourceMap.Values],
        UsedScriptUrls = [.. UsedScripts],
    };

    public static ServiceWorkerRecord? FromStored(ServiceWorkerRegistrationRecord registration, StoredServiceWorker stored)
    {
        if (UrlParser.Parse(stored.ScriptUrl, baseUrl: null) is not { } scriptUrl)
            return null;

        var worker = new ServiceWorkerRecord(registration, stored.Id, scriptUrl, stored.Type == "module" ? WorkerType.Module : WorkerType.Classic)
        {
            State = stored.State == "activated" ? ServiceWorkerState.Activated : ServiceWorkerState.Installed,
            ClassicScriptsImported = stored.ClassicScriptsImported,
            EventTypesToHandle = [.. stored.EventTypesToHandle],
        };
        worker.AllFetchListenersAreEmpty = !worker.EventTypesToHandle.Contains("fetch");
        foreach (var script in stored.Scripts)
        {
            worker.ScriptResourceMap[script.Url] = script;
        }

        foreach (var url in stored.UsedScriptUrls)
        {
            worker.UsedScripts.Add(url);
        }

        return worker.MainScript is null ? null : worker;
    }
}

/// <summary>The kind of a job (https://w3c.github.io/ServiceWorker/#dfn-job-type).</summary>
internal enum ServiceWorkerJobType
{
    Register,
    Update,
    Unregister,
}

/// <summary>
/// A job of a job queue (https://w3c.github.io/ServiceWorker/#dfn-job): its promise is a task completed on the thread of its
/// client (a <see cref="TaskCompletionSource{ServiceWorkerRegistration}"/> or, for an unregister job, a
/// <see cref="TaskCompletionSource{Boolean}"/>), or null for a job started by the user agent (a soft update).
/// </summary>
internal sealed class ServiceWorkerJob
{
    public required ServiceWorkerJobType Type { get; init; }

    public required StorageKey StorageKey { get; init; }

    public required UrlRecord ScopeUrl { get; init; }

    public UrlRecord? ScriptUrl { get; init; }

    public WorkerType WorkerType { get; init; }

    public ServiceWorkerUpdateViaCache UpdateViaCache { get; init; }

    /// <summary>The client of the job (https://w3c.github.io/ServiceWorker/#dfn-job-client), or null.</summary>
    public GlobalScope? Client { get; init; }

    /// <summary>The referrer: the creation URL of the client, or null.</summary>
    public UrlRecord? Referrer { get; init; }

    public object? Promise { get; init; }

    public bool ForceBypassCache { get; init; }

    /// <summary>https://w3c.github.io/ServiceWorker/#dfn-job-list-of-equivalent-jobs</summary>
    public List<ServiceWorkerJob> EquivalentJobs { get; } = [];

    /// <summary>Whether the promise of the job was resolved or rejected.</summary>
    public bool IsSettled { get; set; }

    /// <summary>Completed by Finish Job (https://w3c.github.io/ServiceWorker/#finish-job): the next job of the queue runs.</summary>
    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>https://w3c.github.io/ServiceWorker/#dfn-job-equivalent</summary>
    public bool IsEquivalentTo(ServiceWorkerJob other) => Type == other.Type && Type switch
    {
        ServiceWorkerJobType.Unregister => ScopeUrl.Serialize() == other.ScopeUrl.Serialize(),
        _ => ScopeUrl.Serialize() == other.ScopeUrl.Serialize() && ScriptUrl?.Serialize() == other.ScriptUrl?.Serialize() && WorkerType == other.WorkerType && UpdateViaCache == other.UpdateViaCache,
    };
}
