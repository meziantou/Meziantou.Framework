namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A service worker (https://w3c.github.io/ServiceWorker/#serviceworker-interface), as seen by a window or a worker: its
/// script URL, its state, and the messages posted to it. Each global object has its own object per service worker.
/// </summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#serviceworker"/>
[IdlInterface("ServiceWorker", "https://w3c.github.io/ServiceWorker/#serviceworker")]
public sealed class ServiceWorker : EventTarget
{
    internal ServiceWorker(GlobalScope global, ServiceWorkerRecord record, ServiceWorkerState state)
    {
        SetRelevantGlobalCore(global);
        Record = record;
        State = state;
    }

    internal ServiceWorkerRecord Record { get; }

    /// <summary>Gets the URL of the script of the service worker.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworker-scripturl"/>
    [IdlMember("scriptURL", "https://w3c.github.io/ServiceWorker/#dom-serviceworker-scripturl")]
    public string ScriptUrl => Record.ScriptUrl.Serialize();

    /// <summary>Gets the state of the service worker, as last set by a <c>statechange</c> task.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworker-state"/>
    [IdlMember("state", "https://w3c.github.io/ServiceWorker/#dom-serviceworker-state")]
    public ServiceWorkerState State { get; private set; }

    internal void SetState(ServiceWorkerState state) => State = state;

    /// <summary>Posts a message to the service worker (https://w3c.github.io/ServiceWorker/#dom-serviceworker-postmessage).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworker-postmessage"/>
    [IdlMember("postMessage", "https://w3c.github.io/ServiceWorker/#dom-serviceworker-postmessage")]
    public void PostMessage(object? message, IReadOnlyList<object> transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        PostMessage(message, new StructuredSerializeOptions { Transfer = transfer });
    }

    /// <summary>Posts a message to the service worker (https://w3c.github.io/ServiceWorker/#dom-serviceworker-postmessage-message-options).</summary>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the message cannot be serialized, or an <c>InvalidStateError</c> when the document of the window of the object is not fully active (e.g. its iframe was removed).</exception>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworker-postmessage-message-options"/>
    [IdlMember("postMessage", "https://w3c.github.io/ServiceWorker/#dom-serviceworker-postmessage-message-options")]
    public void PostMessage(object? message, StructuredSerializeOptions? options = null)
    {
        // The specification does not handle the object of a detached window; the browsers throw
        if (RelevantGlobal is Window { Document.IsFullyActive: false })
            throw DomException.InvalidState("The document of the ServiceWorker object is not fully active");

        var incumbent = GlobalScope.Current ?? RelevantGlobal ?? throw new InvalidOperationException("The service worker has no global object");
        var serialized = PortMessage.Serialize(incumbent, message, options?.Transfer ?? []);

        // https://wicg.github.io/nav-speculation/prerendering.html#delay-while-prerendering
        if (incumbent is Window { Document: { Prerendering: true } prerenderingDocument })
        {
            prerenderingDocument.RunAfterPrerendering(() => ServiceWorkerMessaging.PostMessageToServiceWorker(Record, incumbent, serialized));
            return;
        }

        ServiceWorkerMessaging.PostMessageToServiceWorker(Record, incumbent, serialized);
    }

    /// <summary>Gets or sets the event handler of the <c>statechange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworker-onstatechange"/>
    [IdlMember("onstatechange", "https://w3c.github.io/ServiceWorker/#dom-serviceworker-onstatechange")]
    public Action<Event>? OnStateChange
    {
        get => GetEventHandler("statechange");
        set => SetEventHandler("statechange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#handler-abstractworker-onerror"/>
    [IdlMember("onerror", "https://html.spec.whatwg.org/multipage/workers.html#handler-abstractworker-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }
}

/// <summary>
/// A service worker registration (https://w3c.github.io/ServiceWorker/#serviceworkerregistration-interface), as seen by a
/// window or a worker. Its <see cref="Installing"/>, <see cref="Waiting"/> and <see cref="Active"/> workers are updated by
/// tasks queued by the algorithms of the registration.
/// </summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#serviceworkerregistration"/>
[IdlInterface("ServiceWorkerRegistration", "https://w3c.github.io/ServiceWorker/#serviceworkerregistration")]
public sealed class ServiceWorkerRegistration : EventTarget
{
    private NavigationPreloadManager? _navigationPreload;

    internal ServiceWorkerRegistration(GlobalScope global, ServiceWorkerRegistrationRecord record)
    {
        SetRelevantGlobalCore(global);
        Global = global;
        Record = record;
    }

    internal GlobalScope Global { get; }

    internal ServiceWorkerRegistrationRecord Record { get; }

    /// <summary>
    /// The version of the state of the registration that the object reflects (see <see cref="ServiceWorkerRegistrationSnapshot"/>):
    /// a task that updates the object for an earlier change is ignored (the object was created afterwards, from a newer state).
    /// </summary>
    internal long StateVersion { get; set; }

    /// <summary>Gets the installing worker, or null.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-installing"/>
    [IdlMember("installing", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-installing")]
    public ServiceWorker? Installing { get; private set; }

    /// <summary>Gets the waiting worker, or null.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-waiting"/>
    [IdlMember("waiting", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-waiting")]
    public ServiceWorker? Waiting { get; private set; }

    /// <summary>Gets the active worker, or null.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-active"/>
    [IdlMember("active", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-active")]
    public ServiceWorker? Active { get; private set; }

    /// <summary>Gets the navigation preload manager of the registration.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-navigationpreload"/>
    [IdlMember("navigationPreload", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-navigationpreload")]
    public NavigationPreloadManager NavigationPreload => _navigationPreload ??= new NavigationPreloadManager(this);

    /// <summary>Gets the serialized scope URL.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-scope"/>
    [IdlMember("scope", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-scope")]
    public string Scope => Record.Scope;

    /// <summary>Gets the update via cache mode.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-updateviacache"/>
    [IdlMember("updateViaCache", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-updateviacache")]
    public ServiceWorkerUpdateViaCache UpdateViaCache
    {
        get
        {
            lock (Record.Registry.Lock)
            {
                return Record.UpdateViaCache;
            }
        }
    }

    internal void SetWorker(ServiceWorkerRegistrationSlot slot, ServiceWorker? worker)
    {
        switch (slot)
        {
            case ServiceWorkerRegistrationSlot.Installing:
                Installing = worker;
                break;
            case ServiceWorkerRegistrationSlot.Waiting:
                Waiting = worker;
                break;
            default:
                Active = worker;
                break;
        }
    }

    /// <summary>Checks whether the script of the service worker changed (https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-update).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-update"/>
    [IdlMember("update", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-update")]
    public Task<ServiceWorkerRegistration> Update() => Document.DelayWhilePrerendering(Global, UpdateCore);

    private Task<ServiceWorkerRegistration> UpdateCore()
    {
        var registry = Record.Registry;
        ServiceWorkerRecord? newestWorker;
        lock (registry.Lock)
        {
            newestWorker = Record.NewestWorker;
        }

        if (newestWorker is null)
            return Task.FromException<ServiceWorkerRegistration>(DomException.InvalidState("The registration has no service worker"));

        if (Global is ServiceWorkerGlobalScope serviceWorkerGlobal && serviceWorkerGlobal.ServiceWorkerRecord.State == ServiceWorkerState.Installing)
            return Task.FromException<ServiceWorkerRegistration>(DomException.InvalidState("update() cannot be called by an installing service worker"));

        var promise = new TaskCompletionSource<ServiceWorkerRegistration>();
        registry.ScheduleJob(new ServiceWorkerJob
        {
            Type = ServiceWorkerJobType.Update,
            StorageKey = Record.StorageKey,
            ScopeUrl = Record.ScopeUrl,
            ScriptUrl = newestWorker.ScriptUrl,
            WorkerType = newestWorker.Type,
            UpdateViaCache = UpdateViaCache,
            Client = Global,
            Referrer = Global.EnvironmentDocument.UrlRecord,
            Promise = promise,
        });
        return promise.Task;
    }

    /// <summary>Unregisters the registration (https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-unregister).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-unregister"/>
    [IdlMember("unregister", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-unregister")]
    public Task<bool> Unregister() => Document.DelayWhilePrerendering(Global, UnregisterCore);

    private Task<bool> UnregisterCore()
    {
        var promise = new TaskCompletionSource<bool>();
        Record.Registry.ScheduleJob(new ServiceWorkerJob
        {
            Type = ServiceWorkerJobType.Unregister,
            StorageKey = Record.StorageKey,
            ScopeUrl = Record.ScopeUrl,
            Client = Global,
            Referrer = Global.EnvironmentDocument.UrlRecord,
            Promise = promise,
        });
        return promise.Task;
    }

    /// <summary>Gets or sets the event handler of the <c>updatefound</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-onupdatefound"/>
    [IdlMember("onupdatefound", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerregistration-onupdatefound")]
    public Action<Event>? OnUpdateFound
    {
        get => GetEventHandler("updatefound");
        set => SetEventHandler("updatefound", value);
    }
}

/// <summary>The navigation preload of a registration (https://w3c.github.io/ServiceWorker/#navigationpreloadmanager).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#navigationpreloadmanager"/>
[IdlInterface("NavigationPreloadManager", "https://w3c.github.io/ServiceWorker/#navigationpreloadmanager")]
public sealed class NavigationPreloadManager
{
    private readonly ServiceWorkerRegistration _registration;

    internal NavigationPreloadManager(ServiceWorkerRegistration registration)
    {
        _registration = registration;
    }

    /// <summary>Enables the navigation preload (https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-enable).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-enable"/>
    [IdlMember("enable", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-enable")]
    public Task Enable() => Update(registration => registration.NavigationPreloadEnabled = true);

    /// <summary>Disables the navigation preload (https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-disable).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-disable"/>
    [IdlMember("disable", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-disable")]
    public Task Disable() => Update(registration => registration.NavigationPreloadEnabled = false);

    /// <summary>Sets the value of the <c>Service-Worker-Navigation-Preload</c> header (https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-setheadervalue).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-setheadervalue"/>
    [IdlMember("setHeaderValue", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-setheadervalue")]
    public Task SetHeaderValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = FetchHeaders.Normalize(value);
        if (!FetchHeaders.IsHeaderValue(normalized))
            return Task.FromException(new TypeErrorException("The value is not a valid header value"));

        return Update(registration => registration.NavigationPreloadHeaderValue = normalized);
    }

    /// <summary>Returns the navigation preload state (https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-getstate).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-getstate"/>
    [IdlMember("getState", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadmanager-getstate")]
    public Task<NavigationPreloadState> GetState()
    {
        var record = _registration.Record;
        NavigationPreloadState state;
        lock (record.Registry.Lock)
        {
            state = new NavigationPreloadState { Enabled = record.NavigationPreloadEnabled, HeaderValue = record.NavigationPreloadHeaderValue };
        }

        var promise = new TaskCompletionSource<NavigationPreloadState>();
        _registration.Global.QueueGlobalTask(() => promise.TrySetResult(state));
        return promise.Task;
    }

    private Task Update(Action<ServiceWorkerRegistrationRecord> update)
    {
        var record = _registration.Record;
        var promise = new TaskCompletionSource();
        bool hasActiveWorker;
        lock (record.Registry.Lock)
        {
            hasActiveWorker = record.Active is not null;
            if (hasActiveWorker)
            {
                update(record);
            }
        }

        if (hasActiveWorker)
        {
            record.Registry.Persist(record);
        }

        _registration.Global.QueueGlobalTask(() =>
        {
            if (hasActiveWorker)
            {
                promise.TrySetResult();
            }
            else
            {
                promise.TrySetException(DomException.InvalidState("The registration has no active worker"));
            }
        });
        return promise.Task;
    }
}
