using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The service worker registrations of a browsing context group (https://w3c.github.io/ServiceWorker/#dfn-scope-to-registration-map)
/// and their job queues (https://w3c.github.io/ServiceWorker/#dfn-scope-to-job-queue-map), the service worker clients of
/// the group, and the algorithms of the registrations (https://w3c.github.io/ServiceWorker/#algorithms).
/// </summary>
/// <remarks>
/// <para>
/// The registrations are stored by the <see cref="ServiceWorkerBackend"/> of the group: the registrations of an origin are
/// loaded the first time they are used, and each change of a registration is saved. The service workers run on their own
/// thread (see <see cref="WorkerAgent"/>), and their event loop is a child of the event loop of the group, which terminates
/// them when it is disposed.
/// </para>
/// <para>
/// The jobs run "in parallel" on the thread pool: the event loop of the group considers that an operation is in progress
/// while a job runs (e.g. <c>BrowsingContext.WaitUntilIdleAsync</c> waits for a registration). The
/// state of the registrations is guarded by a lock, as it is read by the threads of the clients and of the service workers.
/// The promises and the objects of a client are only used on the thread of the client: the algorithms queue tasks on it.
/// </para>
/// </remarks>
internal sealed partial class ServiceWorkerRegistry
{
    private static readonly ConditionalWeakTable<BrowsingContext, ServiceWorkerRegistry> Registries = [];

    private readonly Lock _lock = new();
    private readonly Dictionary<StorageKey, OrderedDictionary<string, ServiceWorkerRegistrationRecord>> _registrations = [];
    private readonly Dictionary<(StorageKey StorageKey, string Scope), List<ServiceWorkerJob>> _jobQueues = [];
    private readonly List<WeakReference<GlobalScope>> _objectHolders = [];
    private long _nextWorkerId;

    // The number of changes of the workers of the registrations (see ServiceWorkerRegistrationSnapshot); guarded by the lock
    private long _registrationStateVersion;

    private ServiceWorkerRegistry(BrowsingContext groupOwner)
    {
        GroupOwner = groupOwner;
        _parallelOperations = DomEventLoop.CreateOperationTracker(groupOwner.EventLoop);
    }

    // The algorithms that run in parallel (the jobs, the dispatch of the fetch events): the event loop of the group is busy,
    // and a host that advances a fake clock waits for them
    private readonly DomEventLoop _parallelOperations;

    /// <summary>The browsing context created by the application that owns the browsing context group.</summary>
    public BrowsingContext GroupOwner { get; }

    public ServiceWorkerBackend Backend => GroupOwner.ServiceWorkerBackend;

    /// <summary>The event loop of the browsing context group.</summary>
    public DomEventLoop EventLoop => GroupOwner.EventLoop;

    internal Lock Lock => _lock;

    public DateTimeOffset Now => EventLoop.TimeProvider.GetUtcNow();

    /// <summary>The registry of the browsing context group of an environment (a document, or the document of a worker), or null when the environment has no browsing context group.</summary>
    public static ServiceWorkerRegistry? Get(Document environment) => environment.EnvironmentBrowsingContext?.GroupOwner is { } groupOwner ? Get(groupOwner) : null;

    public static ServiceWorkerRegistry Get(BrowsingContext groupOwner) => Registries.GetValue(groupOwner.GroupOwner, static owner => new ServiceWorkerRegistry(owner));

    /// <summary>The registry of a browsing context group, or null when no service worker was used in the group.</summary>
    public static ServiceWorkerRegistry? GetIfCreated(BrowsingContext? browsingContext) => browsingContext is not null && Registries.TryGetValue(browsingContext.GroupOwner, out var registry) ? registry : null;

    /// <summary>
    /// https://storage.spec.whatwg.org/#obtain-a-storage-key: the serialized origin of the environment, or null for an opaque
    /// origin. The environments of the credentialless iframes are partitioned by the page credentialless nonce of their
    /// top-level window (https://wicg.github.io/anonymous-iframe/#spec-storage-key).
    /// </summary>
    public static StorageKey? GetStorageKey(Document environment) => environment.Origin is { IsOpaque: false } origin ? GetStorageKey(origin.Serialize(), environment.IsCredentialless, environment.EnvironmentBrowsingContext) : null;

    /// <summary>The storage key of an environment of a browsing context, whose origin is <paramref name="origin"/>.</summary>
    public static StorageKey GetStorageKey(string origin, bool credentialless, BrowsingContext? browsingContext)
        => new(origin, credentialless && browsingContext?.Top.Window is { } topLevelWindow ? topLevelWindow.PageCredentiallessNonce : null);

    // ----------------------------------------------------------------------------------------------------------------
    // The registration map
    // ----------------------------------------------------------------------------------------------------------------

    private OrderedDictionary<string, ServiceWorkerRegistrationRecord> GetRegistrationsLocked(StorageKey storageKey)
    {
        if (!_registrations.TryGetValue(storageKey, out var registrations))
        {
            registrations = new OrderedDictionary<string, ServiceWorkerRegistrationRecord>(StringComparer.Ordinal);
            IReadOnlyList<StoredServiceWorkerRegistration> stored;
            try
            {
                // The registrations of the credentialless iframes are not persisted: they live as long as their page
                stored = IsPersisted(storageKey) ? Backend.GetRegistrations(storageKey.Origin) : [];
            }
#pragma warning disable CA1031 // A backend that cannot be read has no registration
            catch (Exception)
#pragma warning restore CA1031
            {
                stored = [];
            }

            foreach (var item in stored)
            {
                if (ServiceWorkerRegistrationRecord.FromStored(this, storageKey, item) is not { } registration)
                    continue;

                foreach (var worker in (ReadOnlySpan<ServiceWorkerRecord?>)[registration.Waiting, registration.Active])
                {
                    if (worker is not null && long.TryParse(worker.Id, System.Globalization.CultureInfo.InvariantCulture, out var id))
                    {
                        _nextWorkerId = Math.Max(_nextWorkerId, id);
                    }
                }

                registrations[registration.Scope] = registration;
            }

            _registrations.Add(storageKey, registrations);
        }

        return registrations;
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#get-registration-algorithm</summary>
    public ServiceWorkerRegistrationRecord? GetRegistration(StorageKey storageKey, UrlRecord scopeUrl)
    {
        var scope = scopeUrl.Serialize(excludeFragment: true);
        lock (_lock)
        {
            return GetRegistrationsLocked(storageKey).TryGetValue(scope, out var registration) ? registration : null;
        }
    }

    /// <summary>The registrations of a storage key, in creation order.</summary>
    public List<ServiceWorkerRegistrationRecord> GetRegistrations(StorageKey storageKey)
    {
        lock (_lock)
        {
            return [.. GetRegistrationsLocked(storageKey).Values];
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#set-registration-algorithm</summary>
    private ServiceWorkerRegistrationRecord SetRegistration(StorageKey storageKey, UrlRecord scopeUrl, ServiceWorkerUpdateViaCache updateViaCache)
    {
        var scope = scopeUrl.Serialize(excludeFragment: true);
        var registration = new ServiceWorkerRegistrationRecord(this, storageKey, UrlParser.Parse(scope, baseUrl: null)!, updateViaCache);
        lock (_lock)
        {
            GetRegistrationsLocked(storageKey)[scope] = registration;
        }

        return registration;
    }

    /// <summary>Removes a registration from the registration map: it is unregistered.</summary>
    private void RemoveRegistration(ServiceWorkerRegistrationRecord registration)
    {
        lock (_lock)
        {
            var registrations = GetRegistrationsLocked(registration.StorageKey);
            if (registrations.TryGetValue(registration.Scope, out var existing) && ReferenceEquals(existing, registration))
            {
                registrations.Remove(registration.Scope);
            }

            registration.IsUnregistered = true;
        }

        try
        {
            if (IsPersisted(registration.StorageKey))
            {
                Backend.DeleteRegistration(registration.StorageKey.Origin, registration.Scope);
            }
        }
#pragma warning disable CA1031 // The registration is removed from memory anyway
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Saves a registration in the backend (only the installed and activated workers are stored).</summary>
    internal void Persist(ServiceWorkerRegistrationRecord registration)
    {
        StoredServiceWorkerRegistration stored;
        lock (_lock)
        {
            if (registration.IsUnregistered || !IsPersisted(registration.StorageKey))
                return;

            if (registration.Waiting is null && registration.Active is null)
            {
                stored = null!;
            }
            else
            {
                stored = registration.ToStored();
            }
        }

        try
        {
            if (stored is null)
            {
                Backend.DeleteRegistration(registration.StorageKey.Origin, registration.Scope);
            }
            else
            {
                Backend.SaveRegistration(registration.StorageKey.Origin, stored);
            }
        }
#pragma warning disable CA1031 // The registration is kept in memory anyway
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Whether the registrations of a storage key are stored by the backend (not partitioned by a credentialless nonce).</summary>
    private static bool IsPersisted(StorageKey storageKey) => storageKey.PartitionNonce is null;

    /// <summary>https://w3c.github.io/ServiceWorker/#scope-match-algorithm</summary>
    public ServiceWorkerRegistrationRecord? Match(StorageKey storageKey, UrlRecord clientUrl)
    {
        var clientUrlString = clientUrl.Serialize();
        lock (_lock)
        {
            ServiceWorkerRegistrationRecord? result = null;
            foreach (var (scope, registration) in GetRegistrationsLocked(storageKey))
            {
                if (clientUrlString.StartsWith(scope, StringComparison.Ordinal) && (result is null || scope.Length > result.Scope.Length))
                {
                    result = registration;
                }
            }

            return result;
        }
    }

    private string CreateWorkerId()
    {
        lock (_lock)
        {
            return (++_nextWorkerId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The global objects that hold ServiceWorker and ServiceWorkerRegistration objects
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Registers a global object whose objects are updated by the algorithms (see <see cref="GlobalScope.ServiceWorkerObjects"/>).</summary>
    internal void AddObjectHolder(GlobalScope global)
    {
        lock (_lock)
        {
            _objectHolders.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _objectHolders.Add(new WeakReference<GlobalScope>(global));
        }
    }

    /// <summary>The global objects whose origin is <paramref name="origin"/> that hold service worker objects.</summary>
    private List<GlobalScope> GetObjectHolders(string origin)
    {
        var result = new List<GlobalScope>();
        lock (_lock)
        {
            foreach (var reference in _objectHolders)
            {
                if (reference.TryGetTarget(out var global) && global.EnvironmentDocument.Origin is { IsOpaque: false } globalOrigin && globalOrigin.Serialize() == origin)
                {
                    result.Add(global);
                }
            }
        }

        return result;
    }

    /// <summary>The workers of a registration and their states, given to the tasks that create the objects of the registration.</summary>
    internal ServiceWorkerRegistrationSnapshot Snapshot(ServiceWorkerRegistrationRecord registration)
    {
        lock (_lock)
        {
            return new ServiceWorkerRegistrationSnapshot(
                registration.Installing, registration.Installing?.State ?? default,
                registration.Waiting, registration.Waiting?.State ?? default,
                registration.Active, registration.Active?.State ?? default,
                _registrationStateVersion);
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#update-registration-state</summary>
    private void UpdateRegistrationState(ServiceWorkerRegistrationRecord registration, ServiceWorkerRegistrationSlot target, ServiceWorkerRecord? source)
    {
        // The tasks are queued with the lock: the tasks of two changes made by algorithms that run in parallel are queued in
        // the order of the changes
        lock (_lock)
        {
            var sourceState = source?.State ?? default;
            switch (target)
            {
                case ServiceWorkerRegistrationSlot.Installing:
                    registration.Installing = source;
                    break;
                case ServiceWorkerRegistrationSlot.Waiting:
                    registration.Waiting = source;
                    break;
                default:
                    registration.Active = source;
                    break;
            }

            // An object created after the task was queued already reflects the change: it is created from a newer snapshot
            var version = ++_registrationStateVersion;
            foreach (var global in GetObjectHolders(registration.ScopeUrl.SerializeOrigin()))
            {
                global.QueueGlobalTask(() =>
                {
                    if (global.ServiceWorkerObjectsOrNull?.Registrations.TryGetValue(registration, out var registrationObject) is true && version > registrationObject.StateVersion)
                    {
                        registrationObject.StateVersion = version;
                        registrationObject.SetWorker(target, source is null ? null : global.GetServiceWorkerObject(source, sourceState));
                    }
                });
            }
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#update-state-algorithm: returns a task completed once the queued tasks ran.</summary>
    private Task UpdateWorkerState(ServiceWorkerRecord worker, ServiceWorkerState state)
    {
        List<ServiceWorkerWait<bool>>? waiters = null;
        var tasks = new List<Task>();

        // The tasks are queued with the lock: the statechange events of two changes made by algorithms that run in parallel
        // (e.g. the end of the installation and an activation started by the end of a functional event) are fired in the
        // order of the changes
        lock (_lock)
        {
            worker.State = state;
            if (state is ServiceWorkerState.Activated or ServiceWorkerState.Redundant && worker.ActivationWaiters.Count > 0)
            {
                waiters = [.. worker.ActivationWaiters];
                worker.ActivationWaiters.Clear();
            }

            foreach (var global in GetObjectHolders(worker.ScriptUrl.SerializeOrigin()))
            {
                // The task is discarded when its event loop is disposed (e.g. the global object of a worker that is terminated
                // meanwhile): the algorithm does not wait for it
                var eventLoop = global.EventLoop;
                var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                tasks.Add(Task.WhenAny(executed.Task, eventLoop.WhenDisposed));
                eventLoop.QueueTask(() =>
                {
                    try
                    {
                        // An object created after the task was queued already has the state (or a later one): it was created
                        // with the state of the worker at that time, and the state of a worker only advances
                        if (global.CanRunScript && global.ServiceWorkerObjectsOrNull?.Workers.TryGetValue(worker, out var workerObject) is true && state > workerObject.State)
                        {
                            workerObject.SetState(state);
                            workerObject.FireEvent(new Event("statechange"));
                        }
                    }
                    finally
                    {
                        executed.TrySetResult();
                    }
                });
            }
        }

        if (waiters is not null)
        {
            foreach (var waiter in waiters)
            {
                waiter.Complete(true);
            }
        }

        return Task.WhenAll(tasks);
    }

    /// <summary>Waits until an activating worker is activated (or redundant).</summary>
    internal Task WaitForActivationAsync(ServiceWorkerRecord worker)
    {
        lock (_lock)
        {
            if (worker.State != ServiceWorkerState.Activating)
                return Task.CompletedTask;

            var waiter = CreateWait<bool>();
            worker.ActivationWaiters.Add(waiter);
            return waiter.WaitAsync();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Jobs (https://w3c.github.io/ServiceWorker/#dfn-job)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/ServiceWorker/#schedule-job</summary>
    public void ScheduleJob(ServiceWorkerJob job)
    {
        // The job queues are keyed by storage key too: the jobs of a credentialless iframe are not equivalent to the jobs of
        // the other documents of the same origin
        var key = (job.StorageKey, job.ScopeUrl.Serialize());
        bool start;
        lock (_lock)
        {
            if (!_jobQueues.TryGetValue(key, out var queue))
            {
                queue = [];
                _jobQueues.Add(key, queue);
            }

            start = queue.Count == 0;
            if (start)
            {
                queue.Add(job);
            }
            else if (queue[^1] is var lastJob && job.IsEquivalentTo(lastJob) && !lastJob.IsSettled)
            {
                lastJob.EquivalentJobs.Add(job);
            }
            else
            {
                queue.Add(job);
            }
        }

        if (start)
        {
            RunInParallel(() => RunJobQueueAsync(key, job));
        }
    }

    /// <summary>Runs the jobs of a job queue in order (https://w3c.github.io/ServiceWorker/#run-job, https://w3c.github.io/ServiceWorker/#finish-job).</summary>
    private async Task RunJobQueueAsync((StorageKey StorageKey, string Scope) key, ServiceWorkerJob firstJob)
    {
        var job = firstJob;
        while (true)
        {
            var current = job;
            RunInParallel(() => RunJobAsync(current));
            await current.Finished.Task.ConfigureAwait(false);
            lock (_lock)
            {
                var queue = _jobQueues[key];
                queue.RemoveAt(0);
                if (queue.Count == 0)
                {
                    _jobQueues.Remove(key);
                    return;
                }

                job = queue[0];
            }
        }
    }

    private async Task RunJobAsync(ServiceWorkerJob job)
    {
        try
        {
            switch (job.Type)
            {
                case ServiceWorkerJobType.Register:
                    await RegisterAsync(job).ConfigureAwait(false);
                    break;
                case ServiceWorkerJobType.Update:
                    await UpdateAsync(job).ConfigureAwait(false);
                    break;
                default:
                    Unregister(job);
                    break;
            }
        }
#pragma warning disable CA1031 // An unexpected failure rejects the promise of the job
        catch (Exception)
#pragma warning restore CA1031
        {
            RejectJobPromise(job, static () => new TypeErrorException("The service worker job failed"));
            FinishJob(job);
        }
    }

    /// <summary>
    /// Runs an algorithm "in parallel" on the thread pool: the event loop of the group considers that an operation is in
    /// progress until it completes.
    /// </summary>
    internal void RunInParallel(Func<Task> action)
    {
        BeginParallelOperation();
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The exceptions of an algorithm that runs in parallel are ignored
            catch (Exception)
#pragma warning restore CA1031
            {
            }
            finally
            {
                EndParallelOperation();
            }
        });
    }

    /// <summary>Registers an operation that runs in parallel (see <see cref="RunInParallel"/>) until <see cref="EndParallelOperation"/>.</summary>
    internal void BeginParallelOperation() => _parallelOperations.BeginRunningOperation();

    internal void EndParallelOperation() => _parallelOperations.EndRunningOperation();

    /// <summary>Creates a wait of an algorithm that runs in parallel for a service worker or a client (see <see cref="ServiceWorkerWait{T}"/>).</summary>
    internal ServiceWorkerWait<T> CreateWait<T>() => new(this);

    /// <summary>https://w3c.github.io/ServiceWorker/#finish-job</summary>
    private static void FinishJob(ServiceWorkerJob job) => job.Finished.TrySetResult();

    /// <summary>https://w3c.github.io/ServiceWorker/#resolve-job-promise-algorithm: <paramref name="value"/> is a registration, or a boolean for an unregister job.</summary>
    private static void ResolveJobPromise(ServiceWorkerJob job, object value)
    {
        ServiceWorkerRegistrationSnapshot? snapshot = value is ServiceWorkerRegistrationRecord record ? record.Registry.Snapshot(record) : null;
        foreach (var target in (ReadOnlySpan<ServiceWorkerJob>)[job, .. job.EquivalentJobs])
        {
            target.IsSettled = true;
            if (target.Client is not { } client || target.Promise is null)
                continue;

            client.QueueGlobalTask(() =>
            {
                switch (target.Promise)
                {
                    case TaskCompletionSource<ServiceWorkerRegistration> registrationPromise:
                        registrationPromise.TrySetResult(client.GetServiceWorkerRegistrationObject((ServiceWorkerRegistrationRecord)value, snapshot));
                        break;
                    case TaskCompletionSource<bool> booleanPromise:
                        booleanPromise.TrySetResult((bool)value);
                        break;
                }
            });
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#reject-job-promise: the error is created on the thread of each client.</summary>
    private static void RejectJobPromise(ServiceWorkerJob job, Func<Exception> createError)
    {
        foreach (var target in (ReadOnlySpan<ServiceWorkerJob>)[job, .. job.EquivalentJobs])
        {
            target.IsSettled = true;
            if (target.Client is not { } client || target.Promise is null)
                continue;

            client.QueueGlobalTask(() =>
            {
                switch (target.Promise)
                {
                    case TaskCompletionSource<ServiceWorkerRegistration> registrationPromise:
                        registrationPromise.TrySetException(createError());
                        break;
                    case TaskCompletionSource<bool> booleanPromise:
                        booleanPromise.TrySetException(createError());
                        break;
                }
            });
        }
    }

    private static DomException SecurityError(string message) => new DomException(message, "SecurityError");

    /// <summary>https://w3c.github.io/ServiceWorker/#register-algorithm</summary>
    private Task RegisterAsync(ServiceWorkerJob job)
    {
        var scriptUrl = job.ScriptUrl!;
        if (!ReferrerPolicies.IsPotentiallyTrustworthy(scriptUrl))
        {
            RejectJobPromise(job, static () => SecurityError("The script URL of a service worker must be potentially trustworthy"));
            FinishJob(job);
            return Task.CompletedTask;
        }

        var referrerOrigin = job.Referrer?.SerializeOrigin();
        if (scriptUrl.SerializeOrigin() != referrerOrigin)
        {
            RejectJobPromise(job, static () => SecurityError("The script URL of a service worker must have the origin of the client"));
            FinishJob(job);
            return Task.CompletedTask;
        }

        if (job.ScopeUrl.SerializeOrigin() != referrerOrigin)
        {
            RejectJobPromise(job, static () => SecurityError("The scope URL of a service worker must have the origin of the client"));
            FinishJob(job);
            return Task.CompletedTask;
        }

        if (GetRegistration(job.StorageKey, job.ScopeUrl) is { } registration)
        {
            ServiceWorkerRecord? newestWorker;
            ServiceWorkerUpdateViaCache updateViaCache;
            lock (_lock)
            {
                newestWorker = registration.NewestWorker;
                updateViaCache = registration.UpdateViaCache;
            }

            if (newestWorker is not null && newestWorker.ScriptUrl.Serialize() == scriptUrl.Serialize() && newestWorker.Type == job.WorkerType && job.UpdateViaCache == updateViaCache)
            {
                ResolveJobPromise(job, registration);
                FinishJob(job);
                return Task.CompletedTask;
            }
        }
        else
        {
            SetRegistration(job.StorageKey, job.ScopeUrl, job.UpdateViaCache);
        }

        return UpdateAsync(job);
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#unregister-algorithm</summary>
    private void Unregister(ServiceWorkerJob job)
    {
        if (GetRegistration(job.StorageKey, job.ScopeUrl) is not { } registration)
        {
            ResolveJobPromise(job, false);
            FinishJob(job);
            return;
        }

        RemoveRegistration(registration);
        ResolveJobPromise(job, true);

        // The controlled clients whose document is in the back/forward cache are evicted ("serviceworker-unregistered"): they
        // stop using the registration when they are destroyed
        foreach (var client in GetClients())
        {
            if (client.Environment?.ActiveServiceWorker?.Registration == registration)
            {
                client.TryEvictFromBackForwardCache("serviceworker-unregistered");
            }
        }

        TryClearRegistration(registration);
        FinishJob(job);
    }

    /// <summary>
    /// The delay between a request handled by a service worker (https://w3c.github.io/ServiceWorker/#handle-fetch) and the
    /// soft update of its registration. The specification runs the Soft Update algorithm in parallel, so it does not order the
    /// update check and the load of the document of a navigation; the browsers defer the update check (1 second:
    /// <c>kUpdateDelay</c> in Chromium, <c>dom.serviceWorkers.update_delay</c> in Gecko, <c>softUpdateDelay</c> in WebKit) so
    /// that it does not compete with the load of the page, and pages rely on it: the <c>updatefound</c> event of the update
    /// is fired after the <c>load</c> event of the document.
    /// </summary>
    internal static readonly TimeSpan SoftUpdateDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Runs the Soft Update algorithm for a registration after <see cref="SoftUpdateDelay"/>, measured by the timers of the
    /// event loop of the group. The requests handled while a soft update is scheduled share it.
    /// </summary>
    public void ScheduleSoftUpdate(ServiceWorkerRegistrationRecord registration)
    {
        lock (_lock)
        {
            if (registration.IsSoftUpdateScheduled || registration.IsUnregistered)
                return;

            registration.IsSoftUpdateScheduled = true;
        }

        EventLoop.QueueTimer(SoftUpdateDelay, () =>
        {
            lock (_lock)
            {
                registration.IsSoftUpdateScheduled = false;
            }

            SoftUpdate(registration);
        });
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#soft-update</summary>
    public void SoftUpdate(ServiceWorkerRegistrationRecord registration, bool forceBypassCache = false)
    {
        ServiceWorkerRecord? newestWorker;
        lock (_lock)
        {
            if (registration.IsUnregistered || !IsPersisted(registration.StorageKey))
                return;

            newestWorker = registration.NewestWorker;
        }

        if (newestWorker is null)
            return;

        ScheduleJob(new ServiceWorkerJob
        {
            Type = ServiceWorkerJobType.Update,
            StorageKey = registration.StorageKey,
            ScopeUrl = registration.ScopeUrl,
            ScriptUrl = newestWorker.ScriptUrl,
            WorkerType = newestWorker.Type,
            UpdateViaCache = registration.UpdateViaCache,
            ForceBypassCache = forceBypassCache,
        });
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#install-algorithm</summary>
    private async Task InstallAsync(ServiceWorkerJob job, ServiceWorkerRecord worker, ServiceWorkerRegistrationRecord registration)
    {
        ServiceWorkerRecord? newestWorker;
        lock (_lock)
        {
            newestWorker = registration.NewestWorker;
            registration.UpdateViaCache = job.UpdateViaCache;
        }

        UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Installing, worker);
        _ = UpdateWorkerState(worker, ServiceWorkerState.Installing);
        ResolveJobPromise(job, registration);

        foreach (var global in GetObjectHolders(registration.ScopeUrl.SerializeOrigin()))
        {
            global.QueueGlobalTask(() =>
            {
                if (global.ServiceWorkerObjectsOrNull?.Registrations.TryGetValue(registration, out var registrationObject) is true)
                {
                    registrationObject.FireEvent(new Event("updatefound"));
                }
            });
        }

        var installFailed = false;
        if (!ShouldSkipEvent(worker, "install"))
        {
            if (await RunServiceWorkerAsync(worker).ConfigureAwait(false) == ServiceWorkerStartStatus.Failure)
            {
                installFailed = true;
            }
            else
            {
                var succeeded = await DispatchExtendableEventAsync(worker, static () => new InstallEvent("install")).ConfigureAwait(false);
                installFailed = !succeeded;
            }
        }

        if (installFailed)
        {
            // As in Clear Registration, the attribute of the registration objects is updated before the statechange event
            lock (_lock)
            {
                UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Installing, null);
                _ = UpdateWorkerState(worker, ServiceWorkerState.Redundant);
            }

            TerminateServiceWorker(worker);
            if (newestWorker is null)
            {
                RemoveRegistration(registration);
            }

            FinishJob(job);
            return;
        }

        ServiceWorkerRecord? redundantWorker;
        lock (_lock)
        {
            // The scripts that were not used by the installing worker are removed from its script resource map
            foreach (var url in worker.ScriptResourceMap.Keys.ToArray())
            {
                if (!worker.UsedScripts.Contains(url))
                {
                    worker.ScriptResourceMap.Remove(url);
                }
            }

            redundantWorker = registration.Waiting;
        }

        if (redundantWorker is not null)
        {
            TerminateServiceWorker(redundantWorker);
        }

        // The installing worker becomes the installed waiting worker in one step: Try Activate, which also runs in parallel
        // when the worker has no pending events anymore, never activates a waiting worker that is not installed yet
        Task stateTasks;
        lock (_lock)
        {
            UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Waiting, worker);
            UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Installing, null);
            stateTasks = UpdateWorkerState(worker, ServiceWorkerState.Installed);
        }

        if (redundantWorker is not null)
        {
            stateTasks = Task.WhenAll(stateTasks, UpdateWorkerState(redundantWorker, ServiceWorkerState.Redundant));
        }

        Persist(registration);
        FinishJob(job);

        // The tasks queued by Update Worker State run before Try Activate
        await stateTasks.ConfigureAwait(false);
        await TryActivateAsync(registration).ConfigureAwait(false);
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#try-activate-algorithm</summary>
    internal Task TryActivateAsync(ServiceWorkerRegistrationRecord registration)
    {
        lock (_lock)
        {
            if (!TryBeginActivationLocked(registration))
                return Task.CompletedTask;
        }

        return ActivateAsync(registration);
    }

    /// <summary>
    /// The conditions of Try Activate. When they are met, the Activate algorithm is started for the registration (see
    /// <see cref="ServiceWorkerRegistrationRecord.ActivationInProgress"/>): the caller must run <see cref="ActivateAsync"/>.
    /// Must be called while holding the lock.
    /// </summary>
    private bool TryBeginActivationLocked(ServiceWorkerRegistrationRecord registration)
    {
        if (registration.Waiting is not { } waiting || registration.ActivationInProgress)
            return false;

        if (registration.Active is { State: ServiceWorkerState.Activating })
            return false;

        if (registration.Active is { } active && !(active.HasNoPendingEvents && (!IsUsedLocked(registration) || waiting.SkipWaiting)))
            return false;

        registration.ActivationInProgress = true;
        return true;
    }

    /// <summary>
    /// The steps that run in parallel when a service worker may have no pending events anymore (the last step of
    /// https://w3c.github.io/ServiceWorker/#dom-extendableevent-waituntil), or when a client stops using the registration
    /// (https://w3c.github.io/ServiceWorker/#on-client-unload-algorithm): an unregistered registration is cleared, else its
    /// waiting worker may be activated. The two checks are one atomic step: an algorithm that ends another pending event
    /// meanwhile runs these steps again, and never observes a registration that is being activated instead of cleared.
    /// </summary>
    internal void TryClearOrActivateInParallel(ServiceWorkerRegistrationRecord registration)
    {
        RunInParallel(() =>
        {
            lock (_lock)
            {
                if (registration.IsUnregistered)
                {
                    TryClearRegistration(registration);
                }

                if (!TryBeginActivationLocked(registration))
                    return Task.CompletedTask;
            }

            return ActivateAsync(registration);
        });
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#activation-algorithm (started by <see cref="TryBeginActivationLocked"/>)</summary>
    private async Task ActivateAsync(ServiceWorkerRegistrationRecord registration)
    {
        try
        {
            // The waiting worker becomes the activating active worker in one step: an algorithm that runs in parallel (e.g.
            // the one of the ready promise of a client, which queues a task with a snapshot of the registration) never
            // observes a registration whose waiting worker is also its active worker, and its task runs after the tasks of
            // that step
            ServiceWorkerRecord activeWorker;
            lock (_lock)
            {
                // The registration was cleared since Try Activate
                if (registration.Waiting is null)
                    return;

                if (registration.Active is { } previousActive)
                {
                    TerminateServiceWorker(previousActive);
                    _ = UpdateWorkerState(previousActive, ServiceWorkerState.Redundant);
                }

                activeWorker = registration.Waiting;
                UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Active, activeWorker);
                UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Waiting, null);
                _ = UpdateWorkerState(activeWorker, ServiceWorkerState.Activating);
            }

            Persist(registration);

            // The ready promises of the clients whose creation URL matches the registration are resolved
            var snapshot = Snapshot(registration);
            foreach (var client in GetClients())
            {
                if (client.Environment is null || client.StorageKey != registration.StorageKey || !client.CreationUrl.StartsWith(registration.Scope, StringComparison.Ordinal))
                    continue;

                client.QueueTask(() => client.Global?.ServiceWorkerObjectsOrNull?.Container?.ResolveReadyPromise(registration, snapshot));
            }

            // The clients that use the registration are controlled by the new active worker
            foreach (var client in GetClients())
            {
                if (client.Environment is not { } environment || environment.ActiveServiceWorker?.Registration != registration)
                    continue;

                environment.ActiveServiceWorker = activeWorker;
                NotifyControllerChange(client, activeWorker, ServiceWorkerState.Activating);
            }

            if (!ShouldSkipEvent(activeWorker, "activate") && await RunServiceWorkerAsync(activeWorker).ConfigureAwait(false) != ServiceWorkerStartStatus.Failure)
            {
                // The task that dispatches the event is queued before the tasks that resolve the promises of skipWaiting()
                var dispatched = DispatchExtendableEventAsync(activeWorker, static () => new ExtendableEvent("activate"));
                ResolveSkipWaitingPromises(activeWorker, isActivateEventQueued: true);
                await dispatched.ConfigureAwait(false);
            }
            else
            {
                ResolveSkipWaitingPromises(activeWorker, isActivateEventQueued: true);
            }

            lock (_lock)
            {
                // The worker is redundant when its registration was cleared during the activation
                if (activeWorker.State == ServiceWorkerState.Activating)
                {
                    _ = UpdateWorkerState(activeWorker, ServiceWorkerState.Activated);
                }
            }

            Persist(registration);
        }
        finally
        {
            lock (_lock)
            {
                registration.ActivationInProgress = false;
            }
        }

        // A worker that was installed during the activation is activated now; when it is not (the conditions of Try Activate
        // are not met), the promises of its calls of skipWaiting() made during the activation are resolved
        await TryActivateAsync(registration).ConfigureAwait(false);
        ServiceWorkerRecord? waitingWorker;
        lock (_lock)
        {
            waitingWorker = registration.Waiting;
        }

        if (waitingWorker is not null)
        {
            ResolveSkipWaitingPromises(waitingWorker);
        }
    }

    /// <summary>
    /// Resolves the pending promises of the calls of <c>skipWaiting()</c> of a worker (each one from a task of the worker):
    /// when the worker is activated, once the task that dispatches its activate event is queued, so that the promises are
    /// resolved after the event and before the task that sets the "activated" state; else once Try Activate ran.
    /// </summary>
    /// <param name="worker">The worker.</param>
    /// <param name="isActivateEventQueued">Whether the caller is the Activate algorithm of the worker, which queued the task that dispatches its activate event.</param>
    internal void ResolveSkipWaitingPromises(ServiceWorkerRecord worker, bool isActivateEventQueued = false)
    {
        Action[] resolvers;
        lock (_lock)
        {
            if (isActivateEventQueued)
            {
                worker.IsActivateEventQueued = true;
            }
            else if (!worker.IsActivateEventQueued && worker.Registration is { ActivationInProgress: true } registration
                && (ReferenceEquals(registration.Waiting, worker) || (ReferenceEquals(registration.Active, worker) && worker.State == ServiceWorkerState.Activating)))
            {
                // Try Activate ran while the Activate algorithm that another algorithm started in parallel is activating the
                // worker (e.g. the end of the functional event that called skipWaiting()): that algorithm resolves the
                // promises once the activate event is queued
                return;
            }

            if (worker.SkipWaitingResolvers.Count == 0)
                return;

            resolvers = [.. worker.SkipWaitingResolvers];
            worker.SkipWaitingResolvers.Clear();
        }

        foreach (var resolve in resolvers)
        {
            resolve();
        }
    }

    /// <summary>
    /// Ends a functional event counted by <see cref="ServiceWorkerRecord.PendingFunctionalEvents"/>: the worker may have no
    /// pending events anymore, so its registration may be cleared (when it was unregistered while the event was pending) or
    /// its waiting worker activated.
    /// </summary>
    internal void OnFunctionalEventEnded(ServiceWorkerRecord worker)
    {
        if (Interlocked.Decrement(ref worker.PendingFunctionalEvents) == 0)
        {
            TryClearOrActivateInParallel(worker.Registration);
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#try-clear-registration-algorithm</summary>
    internal void TryClearRegistration(ServiceWorkerRegistrationRecord registration)
    {
        // The check and Clear Registration are one step: an algorithm that runs in parallel (Try Activate) observes the
        // registration before or after it is cleared
        lock (_lock)
        {
            if (IsUsedLocked(registration))
                return;

            foreach (var worker in (ReadOnlySpan<ServiceWorkerRecord?>)[registration.Installing, registration.Waiting, registration.Active])
            {
                if (worker is not null && !worker.HasNoPendingEvents)
                    return;
            }

            ClearRegistration(registration);
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#clear-registration-algorithm</summary>
    private void ClearRegistration(ServiceWorkerRegistrationRecord registration)
    {
        // The steps run atomically. The attributes of the registration objects are all updated before the statechange events
        // (as in the browsers: the listeners of the events see a registration without worker)
        lock (_lock)
        {
            var installing = registration.Installing;
            var waiting = registration.Waiting;
            var active = registration.Active;
            if (installing is not null)
            {
                UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Installing, null);
            }

            if (waiting is not null)
            {
                UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Waiting, null);
            }

            if (active is not null)
            {
                UpdateRegistrationState(registration, ServiceWorkerRegistrationSlot.Active, null);
            }

            foreach (var worker in (ReadOnlySpan<ServiceWorkerRecord?>)[installing, waiting, active])
            {
                if (worker is not null)
                {
                    TerminateServiceWorker(worker);
                    _ = UpdateWorkerState(worker, ServiceWorkerState.Redundant);
                }
            }
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#should-skip-event-algorithm</summary>
    internal static bool ShouldSkipEvent(ServiceWorkerRecord worker, string eventName) => worker.EventTypesToHandle is { } types && !types.Contains(eventName);

    // ----------------------------------------------------------------------------------------------------------------
    // The service worker clients
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a client uses the registration (its active service worker belongs to it). Must be called while holding the lock.</summary>
    private bool IsUsedLocked(ServiceWorkerRegistrationRecord registration)
    {
        foreach (var client in _clients)
        {
            if (client.Environment?.ActiveServiceWorker?.Registration == registration)
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#notify-controller-change-algorithm. The <see cref="ServiceWorker"/> object of the
    /// new controller is created with <paramref name="controllerState"/>, the state of the worker when the task is queued
    /// (the tasks queued afterwards update it): the listeners of the event of a worker that is being activated see an
    /// activating controller, even when the activation already ended on the other threads.
    /// </summary>
    internal static void NotifyControllerChange(ServiceWorkerClientEntry client, ServiceWorkerRecord controller, ServiceWorkerState controllerState)
        => client.QueueTask(() =>
        {
            if (client.Global is { ServiceWorkerObjectsOrNull.Container: { } container } global)
            {
                global.GetServiceWorkerObject(controller, controllerState);
                container.FireEvent(new Event("controllerchange"));
            }
        });
}

/// <summary>
/// A wait of an algorithm that runs in parallel for a service worker or a client (e.g. the extend lifetime promises of an
/// event, a task of a window): the algorithm is not running while it waits, so that a host that advances a fake clock can
/// run the timers that the service worker waits for. The operation is given back to the algorithm by <see cref="Complete"/>,
/// on the thread that completes the wait, before the algorithm resumes on the thread pool.
/// </summary>
internal sealed class ServiceWorkerWait<T>
{
    private readonly ServiceWorkerRegistry _registry;
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;

    public ServiceWorkerWait(ServiceWorkerRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Completes the wait (once): the algorithm is running again from now on.</summary>
    public void Complete(T result)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        _registry.BeginParallelOperation();
        _completion.TrySetResult(result);
    }

    /// <summary>Waits for the result: the operation of the algorithm is released until <see cref="Complete"/> is called.</summary>
    public async Task<T> WaitAsync()
    {
        _registry.EndParallelOperation();
        return await _completion.Task.ConfigureAwait(false);
    }
}

/// <summary>The worker slots of a registration (the target of https://w3c.github.io/ServiceWorker/#update-registration-state).</summary>
internal enum ServiceWorkerRegistrationSlot
{
    Installing,
    Waiting,
    Active,
}

/// <summary>The result of Run Service Worker (https://w3c.github.io/ServiceWorker/#run-service-worker-algorithm).</summary>
internal enum ServiceWorkerStartStatus
{
    /// <summary>The script ran without exception.</summary>
    Normal,

    /// <summary>The script threw an exception (an abrupt completion).</summary>
    Abrupt,

    /// <summary>The service worker could not start.</summary>
    Failure,
}
