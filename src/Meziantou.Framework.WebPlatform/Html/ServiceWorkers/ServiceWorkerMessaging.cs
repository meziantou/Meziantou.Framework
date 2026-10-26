using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The messages between the service workers and their clients, and the steps of the Clients API that run on the thread of
/// a client (the event loop of its browsing context group): a service worker never waits synchronously for a client.
/// </summary>
internal static class ServiceWorkerMessaging
{
    /// <summary>https://w3c.github.io/ServiceWorker/#dom-serviceworker-postmessage-message-options (the steps after the serialization)</summary>
    public static void PostMessageToServiceWorker(ServiceWorkerRecord worker, GlobalScope incumbent, PortMessage message)
    {
        if (ServiceWorkerRegistry.ShouldSkipEvent(worker, "message"))
            return;

        // The source of the event is created in the realm of the service worker from the incumbent global object: its service
        // worker, or a client (whose window state is read now, on the thread of the incumbent global object)
        var origin = incumbent.EnvironmentDocument.Origin.Serialize();
        ServiceWorkerRecord? sourceWorker = null;
        ServiceWorkerClientEntry? sourceClient = null;
        WindowData? sourceWindowData = null;
        if (incumbent is ServiceWorkerGlobalScope serviceWorkerGlobal)
        {
            sourceWorker = serviceWorkerGlobal.ServiceWorkerRecord;
        }
        else if (incumbent.EnvironmentDocument.ServiceWorkerClient is { } client)
        {
            sourceClient = client;
            if (client.Type == ClientType.Window)
            {
                sourceWindowData = GetWindowData(client);
            }
        }

        // The message is a pending event until it is dispatched (or discarded): the registration is not cleared (and the worker
        // is not terminated) by an unregistration between the call and the dispatch of the event
        var registry = worker.Registration.Registry;
        Interlocked.Increment(ref worker.PendingFunctionalEvents);

        // The steps run in parallel, but the messages are delivered in the order they were posted: the event of a message is
        // queued after the event of the previous message
        var messageQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previousMessageQueued;
        lock (registry.Lock)
        {
            previousMessageQueued = worker.LastMessageQueued;
            worker.LastMessageQueued = messageQueued.Task;
        }

        registry.RunInParallel(async () =>
        {
            var queued = false;
            try
            {
                await previousMessageQueued.ConfigureAwait(false);
                if (await registry.RunServiceWorkerAsync(worker).ConfigureAwait(false) == ServiceWorkerStartStatus.Failure)
                    return;

                queued = true;
                registry.QueueTaskOnWorker(worker, global =>
                {
                    object? source = sourceWorker is not null ? global.GetServiceWorkerObject(sourceWorker)
                        : sourceClient is null ? null
                        : sourceWindowData is not null ? new WindowClient(global, sourceClient, sourceWindowData)
                        : new Client(global, sourceClient);

                    ExtendableMessageEvent @event;
                    if (message.TryDeserialize(global, out var data, out var ports))
                    {
                        @event = new ExtendableMessageEvent("message", new ExtendableMessageEventInit { Data = data, Origin = origin, Source = source, Ports = ports });
                    }
                    else
                    {
                        @event = new ExtendableMessageEvent("messageerror", new ExtendableMessageEventInit { Origin = origin, Source = source });
                    }

                    global.FireEvent(@event);
                    global.UpdateExtendedEvents(@event);
                    registry.OnFunctionalEventEnded(worker);
                }, onDiscarded: () => registry.OnFunctionalEventEnded(worker));
            }
            finally
            {
                messageQueued.TrySetResult();
                if (!queued)
                {
                    registry.OnFunctionalEventEnded(worker);
                }
            }
        });
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#dom-client-postmessage-message-options (the steps after the serialization)</summary>
    public static void PostMessageToClient(ServiceWorkerGlobalScope source, ServiceWorkerClientEntry client, PortMessage message)
    {
        if (client.IsDiscarded || client.Global is not { } target)
            return;

        // A message posted to a client whose document is in the back/forward cache evicts it ("serviceworker-postmessage")
        if (client.TryEvictFromBackForwardCache("serviceworker-postmessage"))
            return;

        var origin = source.Origin;
        var worker = source.ServiceWorkerRecord;
        target.QueueGlobalTask(() =>
        {
            var container = GetContainer(target);
            container?.QueueClientMessage(() =>
            {
                var sourceObject = target.GetServiceWorkerObject(worker);
                if (message.TryDeserialize(target, out var data, out var ports))
                {
                    container.FireEvent(new MessageEvent("message", new MessageEventInit { Data = data, Origin = origin, Source = sourceObject, Ports = ports }));
                }
                else
                {
                    container.FireEvent(new MessageEvent("messageerror", new MessageEventInit { Origin = origin, Source = sourceObject }));
                }
            });
        });
    }

    /// <summary>The <see cref="ServiceWorkerContainer"/> of the navigator of a global object (created when needed), or null.</summary>
    private static ServiceWorkerContainer? GetContainer(GlobalScope global) => global switch
    {
        Window window => window.Navigator.ServiceWorker,
        WorkerGlobalScope worker => worker.Navigator.ServiceWorker,
        _ => null,
    };

    /// <summary>
    /// Reads the state of the window of a client on the thread of its browsing context group
    /// (https://w3c.github.io/ServiceWorker/#clients-matchall): returns null when the window was discarded or its document
    /// is no longer the active document of its browsing context.
    /// </summary>
    public static Task<WindowData?> GetWindowDataAsync(ServiceWorkerClientEntry client)
        => RunOnGroupAsync(client.Registry, () => Task.FromResult(GetWindowData(client)));

    /// <summary>Reads the state of the window of a client (on the thread of its browsing context group).</summary>
    public static WindowData? GetWindowData(ServiceWorkerClientEntry client)
    {
        if (client.IsDiscarded || client.Global is not Window { BrowsingContext: { } browsingContext } window || !ReferenceEquals(browsingContext.Document, window.Document))
            return null;

        var document = window.Document;
        return new WindowData(ServiceWorkerRegistry.GetFrameType(browsingContext), document.VisibilityState, document.HasFocus(), [.. document.AncestorOriginsList], document.LastFocusOrder);
    }

    /// <summary>
    /// Runs an operation on the thread of the event loop of the browsing context group, and returns its result (the result
    /// is the default value when the event loop was disposed).
    /// </summary>
    public static async Task<T?> RunOnGroupAsync<T>(ServiceWorkerRegistry registry, Func<Task<T>> operation)
    {
        var completion = registry.CreateWait<T?>();
        var eventLoop = registry.EventLoop;
        eventLoop.QueueTask(() => _ = RunAsync());
        async Task RunAsync()
        {
            try
            {
                completion.Complete(await operation().ConfigureAwait(true));
            }
#pragma warning disable CA1031 // The operation fails as if the client was discarded
            catch (Exception)
#pragma warning restore CA1031
            {
                completion.Complete(default);
            }
        }

        if (eventLoop.IsDisposed)
        {
            completion.Complete(default);
        }

        // The algorithm that waits for the operation (in parallel) is not running meanwhile
        return await completion.WaitAsync().ConfigureAwait(false);
    }

    /// <summary>Whether a window of the browsing context group whose origin is <paramref name="origin"/> has the transient activation (on the thread of the group).</summary>
    public static bool HasTransientActivation(BrowsingContext groupOwner, string origin)
    {
        foreach (var topLevel in groupOwner.GetGroupTopLevelBrowsingContexts())
        {
            foreach (var browsingContext in topLevel.GetInclusiveDescendantNavigables())
            {
                if (browsingContext.Window is { HasTransientActivation: true } window && window.Document.SerializeOrigin() == origin)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Navigates a browsing context (on the thread of its group) and returns the document activated by the navigation
    /// (https://w3c.github.io/ServiceWorker/#client-navigate, HandleNavigate). The specification does not handle the
    /// navigations that fail; as in the browsers, the task is faulted with a TypeError when the navigation creates no
    /// document (e.g. a URL whose scheme is not a fetch scheme) or the error document of a network error.
    /// </summary>
    public static Task<Document> NavigateAndWaitAsync(BrowsingContext browsingContext, UrlRecord url, Document? sourceDocument = null)
    {
        var completion = new TaskCompletionSource<Document>();
        void Unsubscribe()
        {
            browsingContext.DocumentActivated -= OnActivated;
            browsingContext.NavigationEndedWithoutDocument -= OnEndedWithoutDocument;
        }

        void OnActivated(Document document)
        {
            Unsubscribe();
            if (document.IsNetworkErrorDocument)
            {
                completion.TrySetException(new TypeErrorException("The navigation failed"));
            }
            else
            {
                completion.TrySetResult(document);
            }
        }

        void OnEndedWithoutDocument()
        {
            Unsubscribe();
            completion.TrySetException(new TypeErrorException("The navigation did not create a document"));
        }

        browsingContext.DocumentActivated += OnActivated;
        browsingContext.NavigationEndedWithoutDocument += OnEndedWithoutDocument;
        try
        {
            browsingContext.Navigate(url, sourceDocument, NavigationHistoryBehavior.Replace, exceptionsEnabled: true);
        }
        catch
        {
            Unsubscribe();
            throw;
        }

        return completion.Task;
    }
}
