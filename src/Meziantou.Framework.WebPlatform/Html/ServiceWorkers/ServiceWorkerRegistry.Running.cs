namespace Meziantou.Framework.WebPlatform.Dom;

// Running the service workers (https://w3c.github.io/ServiceWorker/#run-service-worker-algorithm): each running service
// worker has its own agent (a thread, an event loop and a script engine). Its script is the response stored in its script
// resource map, never fetched again. The events are dispatched by tasks queued on its event loop.
internal sealed partial class ServiceWorkerRegistry
{
    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#run-service-worker-algorithm: starts the service worker when it is not running,
    /// and returns its start status once its script ran.
    /// </summary>
    internal Task<ServiceWorkerStartStatus> RunServiceWorkerAsync(ServiceWorkerRecord worker)
    {
        WorkerAgent agent;
        TaskCompletionSource<ServiceWorkerStartStatus> start;
        StoredServiceWorkerScript? mainScript;
        lock (_lock)
        {
            if (worker.Agent is { IsClosing: false } && worker.StartStatus is { } status)
                return status;

            if (worker.State == ServiceWorkerState.Redundant || EventLoop.IsDisposed || (mainScript = worker.MainScript) is null)
                return Task.FromResult(ServiceWorkerStartStatus.Failure);

            agent = new WorkerAgent(GroupOwner, worker.ScriptUrl, worker.Type, GetCookieJar(worker.Registration.StorageKey)) { ServiceWorker = worker };
            start = new TaskCompletionSource<ServiceWorkerStartStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            worker.Agent = agent;
            worker.StartStatus = start.Task;
        }

        // The worker stops running when its agent ends (it is terminated, or its script could not run)
        agent.OnEnded = () =>
        {
            start.TrySetResult(ServiceWorkerStartStatus.Failure);
            List<Action>? discarded = null;
            lock (_lock)
            {
                if (ReferenceEquals(worker.Agent, agent))
                {
                    worker.Agent = null;
                    worker.StartStatus = null;
                    discarded = [.. worker.PendingTaskDiscards];
                    worker.PendingTaskDiscards.Clear();
                }
            }

            Interlocked.Exchange(ref worker.ActiveExtendedEventCount, 0);
            if (discarded is not null)
            {
                foreach (var discard in discarded)
                {
                    discard();
                }
            }
        };

        var script = WorkerScriptFetching.FromStoredScript(mainScript);
        agent.Start();
        agent.EventLoop.QueueTask(() => agent.RunServiceWorker(script, status =>
        {
            if (status != ServiceWorkerStartStatus.Failure && agent.Global is { } global)
            {
                OnServiceWorkerScriptEvaluated(worker, global);
            }

            start.TrySetResult(status);
        }));
        return start.Task;
    }

    /// <summary>
    /// The set of event types to handle of a service worker is the set of the types of the event listeners of its global
    /// object after the first evaluation of its script (https://w3c.github.io/ServiceWorker/#run-service-worker-algorithm).
    /// </summary>
    private void OnServiceWorkerScriptEvaluated(ServiceWorkerRecord worker, GlobalScope global)
    {
        lock (_lock)
        {
            if (worker.EventTypesToHandle is not null)
                return;

            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var listener in global.EventListenerList)
            {
                if (!listener.Removed)
                {
                    types.Add(listener.Type);
                }
            }

            worker.EventTypesToHandle = types;
            worker.AllFetchListenersAreEmpty = !types.Contains("fetch");
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#terminate-service-worker: can be called from any thread.</summary>
    internal void TerminateServiceWorker(ServiceWorkerRecord worker)
    {
        WorkerAgent? agent;
        lock (_lock)
        {
            agent = worker.Agent;
        }

        agent?.Terminate();
    }

    /// <summary>
    /// Queues a task on the event loop of a running service worker. <paramref name="onDiscarded"/> is called when the task
    /// cannot run (the worker is terminated before it runs).
    /// </summary>
    internal bool QueueTaskOnWorker(ServiceWorkerRecord worker, Action<ServiceWorkerGlobalScope> task, Action onDiscarded)
    {
        WorkerAgent? agent;
        var ran = 0;
        void Discard()
        {
            if (Interlocked.Exchange(ref ran, 1) == 0)
            {
                onDiscarded();
            }
        }

        lock (_lock)
        {
            agent = worker.Agent;
            if (agent is null || agent.IsClosing)
            {
                agent = null;
            }
            else
            {
                worker.PendingTaskDiscards.Add(Discard);
            }
        }

        if (agent is null)
        {
            Discard();
            return false;
        }

        agent.EventLoop.QueueTask(() =>
        {
            lock (_lock)
            {
                worker.PendingTaskDiscards.Remove(Discard);
            }

            if (Interlocked.Exchange(ref ran, 1) != 0)
                return;

            if (agent.Global is ServiceWorkerGlobalScope global && !agent.IsClosing)
            {
                task(global);
            }
            else
            {
                onDiscarded();
            }
        });
        return true;
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#fire-functional-event: dispatches a functional event (e.g. <c>notificationclick</c>,
    /// <c>cookiechange</c>) at the global object of the active worker of a registration, which is started when it is not
    /// running. The event is created on the thread of the service worker. Can be called from any thread.
    /// </summary>
    internal void FireFunctionalEvent(ServiceWorkerRegistrationRecord registration, string eventName, Func<ServiceWorkerGlobalScope, ExtendableEvent> createEvent)
    {
        ServiceWorkerRecord? activeWorker;
        bool isStale;
        bool isRunning;
        lock (_lock)
        {
            activeWorker = registration.Active;
            isStale = registration.IsStale(Now);
            isRunning = activeWorker?.Agent is { IsClosing: false };
        }

        // Should Skip Event "may" skip an event whose type had no listener after the first evaluation of the script: the
        // event is skipped when it would start the worker, but a running worker receives it (its listeners may have been
        // added since, as in the browsers)
        if (activeWorker is null || (!isRunning && ShouldSkipEvent(activeWorker, eventName)))
            return;

        // The event is a pending event until it is dispatched (or discarded): the registration is not cleared meanwhile
        Interlocked.Increment(ref activeWorker.PendingFunctionalEvents);
        RunInParallel(async () =>
        {
            var queued = false;
            try
            {
                await WaitForActivationAsync(activeWorker).ConfigureAwait(false);
                if (await RunServiceWorkerAsync(activeWorker).ConfigureAwait(false) == ServiceWorkerStartStatus.Failure)
                    return;

                queued = true;
                QueueTaskOnWorker(activeWorker, global =>
                {
                    var @event = createEvent(global);
                    global.FireEvent(@event);
                    global.UpdateExtendedEvents(@event);
                    OnFunctionalEventEnded(activeWorker);
                }, onDiscarded: () => OnFunctionalEventEnded(activeWorker));
            }
            finally
            {
                if (!queued)
                {
                    OnFunctionalEventEnded(activeWorker);
                }
            }

            if (isStale)
            {
                ScheduleSoftUpdate(registration);
            }
        });
    }

    /// <summary>
    /// Called when the cookie change subscriptions of a registration changed (https://cookiestore.spec.whatwg.org/#cookiestoremanager):
    /// the registration is saved, and its cookie jar notifies it of the changes of the cookies while it has subscriptions.
    /// </summary>
    internal void OnCookieChangeSubscriptionsChanged(ServiceWorkerRegistrationRecord registration)
    {
        UpdateCookieChangeListener(registration);
        Persist(registration);
    }

    private void UpdateCookieChangeListener(ServiceWorkerRegistrationRecord registration)
    {
        var cookieJar = GetCookieJar(registration.StorageKey);
        ServiceWorkerCookieChangeListener listener;
        lock (_lock)
        {
            if (registration.CookieChangeSubscriptions.Count == 0 || registration.IsUnregistered || cookieJar is null)
            {
                registration.CookieChangeListener = null;
                return;
            }

            if (registration.CookieChangeListener is { } existing && ReferenceEquals(existing.CookieJar, cookieJar))
                return;

            listener = new ServiceWorkerCookieChangeListener(registration, cookieJar);
            registration.CookieChangeListener = listener;
        }

        // The jar keeps a weak reference to the listener, which lives as long as the registration references it
        cookieJar.AddListener(listener);
    }

    /// <summary>
    /// Dispatches a lifecycle event (install, activate) at the global object of a running service worker, and waits until
    /// the event is not active (https://w3c.github.io/ServiceWorker/#install-algorithm, WaitForAsynchronousExtensions).
    /// Returns false when the task was discarded or when an extend lifetime promise was rejected.
    /// </summary>
    private async Task<bool> DispatchExtendableEventAsync(ServiceWorkerRecord worker, Func<ExtendableEvent> createEvent)
    {
        var wait = CreateWait<bool>();
        QueueTaskOnWorker(worker, global =>
        {
            var @event = createEvent();
            global.FireEvent(@event);
            global.UpdateExtendedEvents(@event);
            _ = @event.WhenNotActive.ContinueWith(_ => wait.Complete(!@event.HasRejectedLifetimePromise), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }, () => wait.Complete(false));
        return await wait.WaitAsync().ConfigureAwait(false);
    }
}
