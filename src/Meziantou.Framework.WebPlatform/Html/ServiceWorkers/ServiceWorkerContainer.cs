using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The service workers of a window or a worker (https://w3c.github.io/ServiceWorker/#serviceworkercontainer-interface),
/// <c>navigator.serviceWorker</c>: registers the service workers of its origin, gives the service worker that controls the
/// client (<see cref="Controller"/>), and receives the messages of the service workers.
/// </summary>
/// <remarks>
/// The registrations are stored by <see cref="BrowsingContextOptions.ServiceWorkers"/>. The service workers need a script
/// engine (<see cref="BrowsingContextOptions.ScriptEngine"/>): they run on their own thread, with their own event loop.
/// </remarks>
/// <seealso href="https://w3c.github.io/ServiceWorker/#serviceworkercontainer"/>
[IdlInterface("ServiceWorkerContainer", "https://w3c.github.io/ServiceWorker/#serviceworkercontainer")]
public sealed class ServiceWorkerContainer : EventTarget
{
    private readonly GlobalScope _global;
    private TaskCompletionSource<ServiceWorkerRegistration>? _readyPromise;
    private List<Action>? _clientMessageQueue;
    private bool _clientMessageQueueEnabled;

    internal ServiceWorkerContainer(GlobalScope global)
    {
        _global = global;
        SetRelevantGlobalCore(global);
        global.ServiceWorkerObjects.Container = this;
    }

    private Document Client => _global.EnvironmentDocument;

    /// <summary>Gets the service worker that controls the client (https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-controller), or null.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-controller"/>
    [IdlMember("controller", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-controller")]
    public ServiceWorker? Controller => Client.ActiveServiceWorker is { } worker && Client.ServiceWorkerClient is not { IsDiscarded: true } ? _global.GetServiceWorkerObject(worker) : null;

    /// <summary>
    /// Gets a promise fulfilled with the registration of the client once it has an active worker
    /// (https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-ready).
    /// </summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-ready"/>
    [IdlMember("ready", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-ready")]
    public Task<ServiceWorkerRegistration> Ready
    {
        get
        {
            if (_readyPromise is null)
            {
                _readyPromise = new TaskCompletionSource<ServiceWorkerRegistration>();
                PromiseSlots.Register(_readyPromise.Task);
            }

            var readyPromise = _readyPromise;
            if (!readyPromise.Task.IsCompleted && ServiceWorkerRegistry.Get(Client) is { } registry && ServiceWorkerRegistry.GetStorageKey(Client) is { } storageKey)
            {
                var clientUrl = Client.UrlRecord;
                registry.RunInParallel(() =>
                {
                    if (registry.Match(storageKey, clientUrl) is { } registration && HasActiveWorker(registration))
                    {
                        var snapshot = registry.Snapshot(registration);
                        _global.QueueGlobalTask(() => ResolveReadyPromise(registration, snapshot));
                    }

                    return Task.CompletedTask;
                });
            }

            return readyPromise.Task;
        }
    }

    private static bool HasActiveWorker(ServiceWorkerRegistrationRecord registration)
    {
        lock (registration.Registry.Lock)
        {
            return registration.Active is not null;
        }
    }

    /// <summary>Resolves the ready promise with a registration (on the thread of the client), when it was created and is pending.</summary>
    internal void ResolveReadyPromise(ServiceWorkerRegistrationRecord registration, ServiceWorkerRegistrationSnapshot snapshot)
    {
        if (_readyPromise is { Task.IsCompleted: false } readyPromise)
        {
            readyPromise.TrySetResult(_global.GetServiceWorkerRegistrationObject(registration, snapshot));
        }
    }

    /// <summary>Registers a service worker (https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-register).</summary>
    /// <param name="scriptUrl">The URL of the script, relative to the API base URL of the client.</param>
    /// <param name="options">The options: the scope, the type of the script and the update via cache mode.</param>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-register"/>
    [IdlMember("register", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-register")]
    public Task<ServiceWorkerRegistration> Register(string scriptUrl, RegistrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scriptUrl);
        return Document.DelayWhilePrerendering(_global, () => RegisterCore(scriptUrl, options));
    }

    /// <summary>Registers a service worker whose script URL was created by a Trusted Types policy (https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-register).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-register"/>
    [IdlMember("register", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-register")]
    public Task<ServiceWorkerRegistration> Register(TrustedScriptUrl scriptUrl, RegistrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scriptUrl);
        return Document.DelayWhilePrerendering(_global, () => RegisterCore(scriptUrl, options));
    }

    private Task<ServiceWorkerRegistration> RegisterCore(object scriptUrl, RegistrationOptions? options)
    {
        // The promise of a job is settled by a task of its client, which a window that is closed (or closing) never runs:
        // as the browsers do for a detached window, the promise is rejected now
        if (_global is Window window && (window.BrowsingContext is not { IsDestroyed: false, IsClosing: false } || !window.Document.IsFullyActive))
            return Task.FromException<ServiceWorkerRegistration>(DomException.InvalidState("The document of the window is not fully active, or the window is closing"));

        options ??= new RegistrationOptions();
        string compliantScriptUrl;
        try
        {
            compliantScriptUrl = Worker.GetCompliantScriptUrl(_global, scriptUrl, "ServiceWorkerContainer register");
        }
        catch (Exception ex) when (ex is DomException or TypeErrorException)
        {
            return Task.FromException<ServiceWorkerRegistration>(ex);
        }

        var promise = new TaskCompletionSource<ServiceWorkerRegistration>();
        var parsedScriptUrl = UrlParser.Parse(compliantScriptUrl, _global.ApiBaseUrl);
        var scopeUrl = options.Scope is { } scope ? UrlParser.Parse(scope, _global.ApiBaseUrl) : null;
        StartRegister(options.Scope is not null, scopeUrl, parsedScriptUrl, promise, options.Type, options.UpdateViaCache);
        return promise.Task;
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#start-register-algorithm</summary>
    private void StartRegister(bool hasScope, UrlRecord? scopeUrl, UrlRecord? scriptUrl, TaskCompletionSource<ServiceWorkerRegistration> promise, WorkerType workerType, ServiceWorkerUpdateViaCache updateViaCache)
    {
        if (scriptUrl is null)
        {
            promise.TrySetException(new TypeErrorException("The script URL is invalid"));
            return;
        }

        scriptUrl = scriptUrl.Clone();
        scriptUrl.Fragment = null;
        if (scriptUrl.Scheme is not ("http" or "https"))
        {
            promise.TrySetException(new TypeErrorException("The script URL must be an HTTP(S) URL"));
            return;
        }

        if (HasEscapedSlash(scriptUrl))
        {
            promise.TrySetException(new TypeErrorException("The path of the script URL must not contain an escaped slash or backslash"));
            return;
        }

        if (!hasScope)
        {
            scopeUrl = UrlParser.Parse("./", scriptUrl);
        }

        if (scopeUrl is null)
        {
            promise.TrySetException(new TypeErrorException("The scope URL is invalid"));
            return;
        }

        scopeUrl = scopeUrl.Clone();
        scopeUrl.Fragment = null;
        if (scopeUrl.Scheme is not ("http" or "https"))
        {
            promise.TrySetException(new TypeErrorException("The scope URL must be an HTTP(S) URL"));
            return;
        }

        if (HasEscapedSlash(scopeUrl))
        {
            promise.TrySetException(new TypeErrorException("The path of the scope URL must not contain an escaped slash or backslash"));
            return;
        }

        if (ServiceWorkerRegistry.GetStorageKey(Client) is not { } storageKey)
        {
            promise.TrySetException(new DomException("The service workers cannot be used by an opaque origin", "SecurityError"));
            return;
        }

        if (ServiceWorkerRegistry.Get(Client) is not { } registry)
        {
            promise.TrySetException(DomException.InvalidState("The client has no browsing context"));
            return;
        }

        registry.ScheduleJob(new ServiceWorkerJob
        {
            Type = ServiceWorkerJobType.Register,
            StorageKey = storageKey,
            ScopeUrl = scopeUrl,
            ScriptUrl = scriptUrl,
            WorkerType = workerType,
            UpdateViaCache = updateViaCache,
            Client = _global,
            Referrer = Client.UrlRecord,
            Promise = promise,
        });
    }

    private static bool HasEscapedSlash(UrlRecord url)
    {
        foreach (var segment in url.Path)
        {
            if (segment.Contains("%2f", StringComparison.OrdinalIgnoreCase) || segment.Contains("%5c", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Returns the registration whose scope matches a URL (https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-getregistration).</summary>
    /// <param name="clientUrl">The URL, relative to the API base URL of the client (the URL of the client by default).</param>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-getregistration"/>
    [IdlMember("getRegistration", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-getregistration")]
    public Task<ServiceWorkerRegistration?> GetRegistration(string clientUrl = "")
    {
        ArgumentNullException.ThrowIfNull(clientUrl);
        if (UrlParser.Parse(clientUrl, _global.ApiBaseUrl) is not { } url)
            return Task.FromException<ServiceWorkerRegistration?>(new TypeErrorException("The URL is invalid"));

        url = url.Clone();
        url.Fragment = null;
        if (!OriginRecord.IsSameOrigin(OriginRecord.FromUrl(url), Client.Origin))
            return Task.FromException<ServiceWorkerRegistration?>(new DomException("The URL must have the origin of the client", "SecurityError"));

        if (ServiceWorkerRegistry.Get(Client) is not { } registry || ServiceWorkerRegistry.GetStorageKey(Client) is not { } storageKey)
            return Task.FromResult<ServiceWorkerRegistration?>(null);

        var promise = new TaskCompletionSource<ServiceWorkerRegistration?>();
        registry.RunInParallel(() =>
        {
            var registration = registry.Match(storageKey, url);
            ServiceWorkerRegistrationSnapshot? snapshot = registration is null ? null : registry.Snapshot(registration);
            _global.QueueGlobalTask(() => promise.TrySetResult(registration is null ? null : _global.GetServiceWorkerRegistrationObject(registration, snapshot)));
            return Task.CompletedTask;
        });
        return promise.Task;
    }

    /// <summary>Returns the registrations of the origin of the client (https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-getregistrations).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-getregistrations"/>
    [IdlMember("getRegistrations", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-getregistrations")]
    public Task<IReadOnlyList<ServiceWorkerRegistration>> GetRegistrations()
    {
        if (ServiceWorkerRegistry.Get(Client) is not { } registry || ServiceWorkerRegistry.GetStorageKey(Client) is not { } storageKey)
            return Task.FromResult<IReadOnlyList<ServiceWorkerRegistration>>([]);

        var promise = new TaskCompletionSource<IReadOnlyList<ServiceWorkerRegistration>>();
        registry.RunInParallel(() =>
        {
            var registrations = registry.GetRegistrations(storageKey).Select(registration => (Registration: registration, Snapshot: registry.Snapshot(registration))).ToList();
            _global.QueueGlobalTask(() => promise.TrySetResult([.. registrations.Select(item => _global.GetServiceWorkerRegistrationObject(item.Registration, item.Snapshot))]));
            return Task.CompletedTask;
        });
        return promise.Task;
    }

    /// <summary>Enables the client message queue: the messages of the service workers are dispatched (https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-startmessages).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-startmessages"/>
    [IdlMember("startMessages", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-startmessages")]
    public void StartMessages() => EnableClientMessageQueue();

    /// <summary>
    /// Enables the client message queue (https://w3c.github.io/ServiceWorker/#dfn-client-message-queue): the messages queued
    /// while it was disabled are dispatched.
    /// </summary>
    internal void EnableClientMessageQueue()
    {
        if (_clientMessageQueueEnabled)
            return;

        _clientMessageQueueEnabled = true;
        if (_clientMessageQueue is { } queue)
        {
            _clientMessageQueue = null;
            foreach (var task in queue)
            {
                _global.QueueGlobalTask(task);
            }
        }
    }

    /// <summary>
    /// Adds a task to the client message queue (on the thread of the client). The queue of a window is enabled after the
    /// DOMContentLoaded event of its document (https://html.spec.whatwg.org/multipage/parsing.html#the-end; a document that
    /// has no parser, e.g. the initial about:blank document, is completely loaded from the start), the queue of a worker
    /// once its script ran.
    /// </summary>
    internal void QueueClientMessage(Action task)
    {
        if (!_clientMessageQueueEnabled && (_global is not Window window || window.Document.IsDomContentLoadedFired || window.Document.ReadyState == DocumentReadyState.Complete))
        {
            EnableClientMessageQueue();
        }

        if (_clientMessageQueueEnabled)
        {
            _global.QueueGlobalTask(task);
        }
        else
        {
            (_clientMessageQueue ??= []).Add(task);
        }
    }

    /// <summary>Gets or sets the event handler of the <c>controllerchange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-oncontrollerchange"/>
    [IdlMember("oncontrollerchange", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-oncontrollerchange")]
    public Action<Event>? OnControllerChange
    {
        get => GetEventHandler("controllerchange");
        set => SetEventHandler("controllerchange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>message</c> event: setting it enables the client message queue.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-onmessage"/>
    [IdlMember("onmessage", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>Gets or sets the event handler of the <c>messageerror</c> event.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-onmessageerror"/>
    [IdlMember("onmessageerror", "https://w3c.github.io/ServiceWorker/#dom-serviceworkercontainer-onmessageerror")]
    public Action<Event>? OnMessageError
    {
        get => GetEventHandler("messageerror");
        set => SetEventHandler("messageerror", value);
    }

    private protected override void OnEventHandlerSet(string type)
    {
        if (type == "message")
        {
            EnableClientMessageQueue();
        }
    }
}
