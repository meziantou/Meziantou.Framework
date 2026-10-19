using System.Collections.ObjectModel;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The clients of a service worker (https://w3c.github.io/ServiceWorker/#clients-interface): the windows and the workers of
/// its origin, in the browsing context group of the service worker.
/// </summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#clients"/>
[IdlInterface("Clients", "https://w3c.github.io/ServiceWorker/#clients")]
public sealed class Clients
{
    private readonly ServiceWorkerGlobalScope _global;

    internal Clients(ServiceWorkerGlobalScope global)
    {
        _global = global;
    }

    private ServiceWorkerRecord Worker => _global.ServiceWorkerRecord;

    private ServiceWorkerRegistry Registry => Worker.Registration.Registry;

    /// <summary>
    /// Runs the steps of <c>get()</c> and <c>matchAll()</c> in parallel, one call after the other
    /// (https://html.spec.whatwg.org/multipage/infrastructure.html#parallel-queue): the promises of two calls are settled in
    /// the order of the calls, as in the browsers (WPT ServiceWorkerGlobalScope/extendable-message-event.https.html posts
    /// messages to a client from the reactions of two <c>clients.get()</c> and expects them in the order of the calls).
    /// The methods are called on the thread of the worker.
    /// </summary>
    private void RunInParallelQueue(Func<Task> steps)
    {
        var previous = _parallelQueue;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _parallelQueue = completion.Task;
        Registry.RunInParallel(async () =>
        {
            await previous.ConfigureAwait(false);
            try
            {
                await steps().ConfigureAwait(false);
            }
            finally
            {
                completion.TrySetResult();
            }
        });
    }

    private Task _parallelQueue = Task.CompletedTask;

    /// <summary>Returns the client whose id is <paramref name="id"/>, or null (https://w3c.github.io/ServiceWorker/#clients-get).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clients-get"/>
    [IdlMember("get", "https://w3c.github.io/ServiceWorker/#dom-clients-get")]
    public Task<Client?> Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var promise = new TaskCompletionSource<Client?>();
        var storageKey = Worker.Registration.StorageKey;
        RunInParallelQueue(async () =>
        {
            var client = Registry.GetClient(id);
            if (client is null || client.StorageKey != storageKey)
            {
                _global.QueueGlobalTask(() => promise.TrySetResult(null));
                return;
            }

            // https://w3c.github.io/ServiceWorker/#resolve-get-client-promise
            if (!client.IsSecureContext)
            {
                _global.QueueGlobalTask(() => promise.TrySetException(new DomException("The client is not a secure context", "SecurityError")));
                return;
            }

            if (client.Type != ClientType.Window)
            {
                _global.QueueGlobalTask(() => promise.TrySetResult(new Client(_global, client)));
                return;
            }

            var windowData = await ServiceWorkerMessaging.GetWindowDataAsync(client).ConfigureAwait(false);
            _global.QueueGlobalTask(() => promise.TrySetResult(windowData is null || client.IsDiscarded ? null : new WindowClient(_global, client, windowData)));
        });
        return promise.Task;
    }

    /// <summary>Returns the clients of the service worker (https://w3c.github.io/ServiceWorker/#clients-matchall).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clients-matchall"/>
    [IdlMember("matchAll", "https://w3c.github.io/ServiceWorker/#dom-clients-matchall")]
    public Task<IReadOnlyList<Client>> MatchAll(ClientQueryOptions? options = null)
    {
        options ??= new ClientQueryOptions();
        var promise = new TaskCompletionSource<IReadOnlyList<Client>>();
        var worker = Worker;
        var storageKey = worker.Registration.StorageKey;
        RunInParallelQueue(async () =>
        {
            var matchedWindows = new List<(ServiceWorkerClientEntry Client, WindowData Data)>();
            var matchedClients = new List<ServiceWorkerClientEntry>();
            foreach (var client in Registry.GetClients())
            {
                if (client.StorageKey != storageKey || client.IsDiscarded || !client.IsSecureContext)
                    continue;

                if (!options.IncludeUncontrolled && client.Environment?.ActiveServiceWorker != worker)
                    continue;

                if (client.Type == ClientType.Window)
                {
                    if (options.Type is ClientType.Window or ClientType.All && await ServiceWorkerMessaging.GetWindowDataAsync(client).ConfigureAwait(false) is { } windowData)
                    {
                        matchedWindows.Add((client, windowData));
                    }
                }
                else if (options.Type == ClientType.All || options.Type == client.Type)
                {
                    matchedClients.Add(client);
                }
            }

            _global.QueueGlobalTask(() =>
            {
                // The windows that were focused first, the most recently focused one first (the documents focused by the same
                // focus change, a document and its ancestors, in creation order), then the windows that were never focused
                // and the workers in creation order
                var clients = new List<Client>();
                clients.AddRange(matchedWindows.OrderByDescending(window => window.Data.LastFocusOrder).ThenBy(window => window.Client.CreationOrder).Select(window => new WindowClient(_global, window.Client, window.Data)));
                clients.AddRange(matchedClients.OrderBy(client => client.CreationOrder).Select(client => new Client(_global, client)));
                promise.TrySetResult(new ReadOnlyCollection<Client>(clients));
            });
        });
        return promise.Task;
    }

    /// <summary>
    /// Opens a new top-level browsing context, navigated to <paramref name="url"/> (https://w3c.github.io/ServiceWorker/#clients-openwindow):
    /// returns its window client, or null when its document does not have the origin of the service worker.
    /// </summary>
    /// <remarks>A window of the origin must have the transient activation (e.g. the user clicked it).</remarks>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clients-openwindow"/>
    [IdlMember("openWindow", "https://w3c.github.io/ServiceWorker/#dom-clients-openwindow")]
    public Task<WindowClient?> OpenWindow(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (UrlParser.Parse(url, _global.ApiBaseUrl) is not { } parsedUrl)
            return Task.FromException<WindowClient?>(new TypeErrorException("The URL is invalid"));

        if (parsedUrl.Serialize() == "about:blank")
            return Task.FromException<WindowClient?>(new TypeErrorException("The URL cannot be about:blank"));

        var promise = new TaskCompletionSource<WindowClient?>();
        var registry = Registry;
        var storageKey = Worker.Registration.StorageKey;
        var origin = _global.Origin;
        registry.RunInParallel(async () =>
        {
            var result = await ServiceWorkerMessaging.RunOnGroupAsync(registry, async () =>
            {
                if (!ServiceWorkerMessaging.HasTransientActivation(registry.GroupOwner, origin))
                    return (Error: (Exception?)new DomException("No window of the origin has a transient activation", "InvalidAccessError"), Client: (ServiceWorkerClientEntry?)null, Data: (WindowData?)null);

                var newContext = registry.GroupOwner.CreateAuxiliaryBrowsingContext("", opener: null);
                Document document;
                try
                {
                    document = await ServiceWorkerMessaging.NavigateAndWaitAsync(newContext, parsedUrl).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is DomException or TypeErrorException)
                {
                    return (Error: ex, Client: null, Data: null);
                }

                if (ServiceWorkerRegistry.GetStorageKey(document) != storageKey || document.ServiceWorkerClient is not { } client)
                    return (Error: null, Client: null, Data: null);

                return (Error: null, Client: client, Data: ServiceWorkerMessaging.GetWindowData(client));
            }).ConfigureAwait(false);

            _global.QueueGlobalTask(() =>
            {
                if (result.Error is { } error)
                {
                    promise.TrySetException(error);
                }
                else
                {
                    promise.TrySetResult(result.Client is { } client && result.Data is { } data ? new WindowClient(_global, client, data) : null);
                }
            });
        });
        return promise.Task;
    }

    /// <summary>
    /// Makes the service worker the controller of the clients whose URL matches its registration
    /// (https://w3c.github.io/ServiceWorker/#clients-claim).
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the service worker is not the active worker of its registration.</exception>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clients-claim"/>
    [IdlMember("claim", "https://w3c.github.io/ServiceWorker/#dom-clients-claim")]
    public Task Claim()
    {
        var worker = Worker;
        var registration = worker.Registration;
        var registry = Registry;
        lock (registry.Lock)
        {
            if (registration.Active != worker)
                return Task.FromException(DomException.InvalidState("The service worker is not the active worker of its registration"));
        }

        var promise = new TaskCompletionSource();
        registry.RunInParallel(() =>
        {
            ServiceWorkerState workerState;
            lock (registry.Lock)
            {
                workerState = worker.State;
            }

            foreach (var client in registry.GetClients())
            {
                if (client.StorageKey != registration.StorageKey || client.IsDiscarded || !client.IsSecureContext || client.Environment is not { } environment)
                    continue;

                if (UrlParser.Parse(client.CreationUrl, baseUrl: null) is not { } creationUrl || registry.Match(registration.StorageKey, creationUrl) != registration)
                    continue;

                if (environment.ActiveServiceWorker != worker)
                {
                    var previousRegistration = environment.ActiveServiceWorker?.Registration;
                    environment.ActiveServiceWorker = worker;
                    if (previousRegistration is not null && previousRegistration != registration)
                    {
                        registry.OnClientStoppedUsing(previousRegistration);
                    }

                    ServiceWorkerRegistry.NotifyControllerChange(client, worker, workerState);
                }
            }

            _global.QueueGlobalTask(() => promise.TrySetResult());
            return Task.CompletedTask;
        });
        return promise.Task;
    }
}

/// <summary>A client of a service worker (https://w3c.github.io/ServiceWorker/#client-interface): a window or a worker of its origin.</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#client"/>
[IdlInterface("Client", "https://w3c.github.io/ServiceWorker/#client")]
public class Client
{
    internal Client(ServiceWorkerGlobalScope global, ServiceWorkerClientEntry entry, FrameType frameType = FrameType.None)
    {
        Global = global;
        Entry = entry;
        FrameType = frameType;
    }

    internal ServiceWorkerGlobalScope Global { get; }

    internal ServiceWorkerClientEntry Entry { get; }

    /// <summary>Gets the creation URL of the client.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-client-url"/>
    [IdlMember("url", "https://w3c.github.io/ServiceWorker/#dom-client-url")]
    public string Url => Entry.CreationUrl;

    /// <summary>Gets the frame type of the client.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-client-frametype"/>
    [IdlMember("frameType", "https://w3c.github.io/ServiceWorker/#dom-client-frametype")]
    public FrameType FrameType { get; }

    /// <summary>Gets the id of the client.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-client-id"/>
    [IdlMember("id", "https://w3c.github.io/ServiceWorker/#dom-client-id")]
    public string Id => Entry.Id;

    /// <summary>Gets the type of the client.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-client-type"/>
    [IdlMember("type", "https://w3c.github.io/ServiceWorker/#dom-client-type")]
    public ClientType Type => Entry.Type;

    /// <summary>Posts a message to the client (https://w3c.github.io/ServiceWorker/#dom-client-postmessage).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-client-postmessage"/>
    [IdlMember("postMessage", "https://w3c.github.io/ServiceWorker/#dom-client-postmessage")]
    public void PostMessage(object? message, IReadOnlyList<object> transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        PostMessage(message, new StructuredSerializeOptions { Transfer = transfer });
    }

    /// <summary>Posts a message to the client: it is dispatched at its <see cref="ServiceWorkerContainer"/> (https://w3c.github.io/ServiceWorker/#dom-client-postmessage-message-options).</summary>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the message cannot be serialized.</exception>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-client-postmessage-message-options"/>
    [IdlMember("postMessage", "https://w3c.github.io/ServiceWorker/#dom-client-postmessage-message-options")]
    public void PostMessage(object? message, StructuredSerializeOptions? options = null)
    {
        var serialized = PortMessage.Serialize(Global, message, options?.Transfer ?? []);
        ServiceWorkerMessaging.PostMessageToClient(Global, Entry, serialized);
    }
}

/// <summary>A window client of a service worker (https://w3c.github.io/ServiceWorker/#windowclient-interface).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#windowclient"/>
[IdlInterface("WindowClient", "https://w3c.github.io/ServiceWorker/#windowclient")]
public sealed class WindowClient : Client
{
    internal WindowClient(ServiceWorkerGlobalScope global, ServiceWorkerClientEntry entry, WindowData data)
        : base(global, entry, data.FrameType)
    {
        VisibilityState = data.VisibilityState;
        Focused = data.Focused;
        AncestorOrigins = new ReadOnlyCollection<string>([.. data.AncestorOrigins]);
    }

    /// <summary>Gets the visibility state of the document of the client (<c>visible</c> or <c>hidden</c>).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-windowclient-visibilitystate"/>
    [IdlMember("visibilityState", "https://w3c.github.io/ServiceWorker/#dom-windowclient-visibilitystate")]
    public string VisibilityState { get; }

    /// <summary>Gets whether the document of the client has the focus.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-windowclient-focused"/>
    [IdlMember("focused", "https://w3c.github.io/ServiceWorker/#dom-windowclient-focused")]
    public bool Focused { get; }

    /// <summary>Gets the ancestor origins of the document of the client.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-windowclient-ancestororigins"/>
    [IdlMember("ancestorOrigins", "https://w3c.github.io/ServiceWorker/#dom-windowclient-ancestororigins")]
    public IReadOnlyList<string> AncestorOrigins { get; }

    /// <summary>Focuses the window of the client (https://w3c.github.io/ServiceWorker/#client-focus).</summary>
    /// <remarks>A window of the origin must have the transient activation (e.g. the user clicked it).</remarks>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-windowclient-focus"/>
    [IdlMember("focus", "https://w3c.github.io/ServiceWorker/#dom-windowclient-focus")]
    public Task<WindowClient> Focus()
    {
        var registry = Entry.Registry;
        var origin = Global.Origin;
        var promise = new TaskCompletionSource<WindowClient>();
        registry.RunInParallel(async () =>
        {
            var result = await ServiceWorkerMessaging.RunOnGroupAsync(registry, () =>
            {
                if (!ServiceWorkerMessaging.HasTransientActivation(registry.GroupOwner, origin))
                    return Task.FromResult<(bool Allowed, WindowData? Data)>((false, null));

                if (Entry.Global is Window { BrowsingContext: { } browsingContext } window && ReferenceEquals(browsingContext.Document, window.Document))
                {
                    browsingContext.Window?.Focus();
                }

                return Task.FromResult<(bool Allowed, WindowData? Data)>((true, ServiceWorkerMessaging.GetWindowData(Entry)));
            }).ConfigureAwait(false);

            Global.QueueGlobalTask(() =>
            {
                if (!result.Allowed)
                {
                    promise.TrySetException(new DomException("No window of the origin has a transient activation", "InvalidAccessError"));
                }
                else if (result.Data is { Focused: true } data)
                {
                    promise.TrySetResult(new WindowClient(Global, Entry, data));
                }
                else
                {
                    promise.TrySetException(new TypeErrorException("The window could not be focused"));
                }
            });
        });
        return promise.Task;
    }

    /// <summary>
    /// Navigates the window of the client to <paramref name="url"/> (https://w3c.github.io/ServiceWorker/#client-navigate):
    /// returns its window client, or null when the new document does not have the origin of the service worker.
    /// </summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-windowclient-navigate"/>
    [IdlMember("navigate", "https://w3c.github.io/ServiceWorker/#dom-windowclient-navigate")]
    public Task<WindowClient?> Navigate(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (UrlParser.Parse(url, Global.ApiBaseUrl) is not { } parsedUrl)
            return Task.FromException<WindowClient?>(new TypeErrorException("The URL is invalid"));

        if (parsedUrl.Serialize() == "about:blank")
            return Task.FromException<WindowClient?>(new TypeErrorException("The URL cannot be about:blank"));

        if (Entry.Environment?.ActiveServiceWorker != Global.ServiceWorkerRecord)
            return Task.FromException<WindowClient?>(new TypeErrorException("The client is not controlled by the service worker"));

        var registry = Entry.Registry;
        var origin = Global.Origin;
        var promise = new TaskCompletionSource<WindowClient?>();
        registry.RunInParallel(async () =>
        {
            var result = await ServiceWorkerMessaging.RunOnGroupAsync(registry, async () =>
            {
                if (Entry.Global is not Window { BrowsingContext: { } browsingContext } window || !ReferenceEquals(browsingContext.Document, window.Document) || !window.Document.IsFullyActive)
                    return (Error: (Exception?)new TypeErrorException("The document of the client is not fully active"), Client: (ServiceWorkerClientEntry?)null, Data: (WindowData?)null);

                Document document;
                try
                {
                    document = await ServiceWorkerMessaging.NavigateAndWaitAsync(browsingContext, parsedUrl, window.Document).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is DomException or TypeErrorException)
                {
                    return (Error: ex, Client: null, Data: null);
                }

                if (document.SerializeOrigin() != origin || document.ServiceWorkerClient is not { } client)
                    return (Error: null, Client: null, Data: null);

                return (Error: null, Client: client, Data: ServiceWorkerMessaging.GetWindowData(client));
            }).ConfigureAwait(false);

            Global.QueueGlobalTask(() =>
            {
                if (result.Error is { } error)
                {
                    promise.TrySetException(error);
                }
                else
                {
                    promise.TrySetResult(result.Client is { } client && result.Data is { } data ? new WindowClient(Global, client, data) : null);
                }
            });
        });
        return promise.Task;
    }
}

/// <summary>The state of the window of a client, read on its thread (the arguments of https://w3c.github.io/ServiceWorker/#create-windowclient-algorithm).</summary>
internal sealed record WindowData(FrameType FrameType, string VisibilityState, bool Focused, IReadOnlyList<string> AncestorOrigins, long LastFocusOrder = 0);
