namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The fetch handling of the service workers (https://w3c.github.io/ServiceWorker/#handle-fetch), called by the HTTP fetch
/// of the fetch algorithm (https://fetch.spec.whatwg.org/#concept-http-fetch, step 5) for the requests whose service-workers
/// mode is "all": the request of a navigation or of a worker script is handled by the service worker whose registration
/// matches its URL, a subresource request by the active service worker of its client. The fetch event is dispatched on the
/// thread of the service worker, and the response is given back on the thread of the client.
/// </summary>
internal static class ServiceWorkerFetchHandling
{
    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#handle-fetch: returns the response of the service worker, a network error, or
    /// null when the request is not handled by a service worker (it is fetched from the network).
    /// </summary>
    /// <summary>Whether the scope of a registration that has an active worker matches <paramref name="url"/>: the navigations of <paramref name="browsingContext"/> to the URL are handled by a service worker.</summary>
    public static bool HasActiveWorker(BrowsingContext browsingContext, Urls.UrlRecord url)
    {
        var options = browsingContext.Options;
        if (options.ScriptEngine is null || !options.EffectiveFeatures.ServiceWorkersEnabled || browsingContext.IsDestroyed || url.Scheme is not ("http" or "https") || !ReferrerPolicies.IsPotentiallyTrustworthy(url))
            return false;

        var registry = ServiceWorkerRegistry.Get(browsingContext);
        var registration = registry.Match(ServiceWorkerRegistry.GetStorageKey(url.SerializeOrigin(), browsingContext.IsCredentiallessNavigable, browsingContext), url);
        lock (registry.Lock)
        {
            return registration?.Active is not null;
        }
    }

    public static Task<FetchResponse?> HandleFetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        var eventLoop = request.Navigable?.EventLoop ?? request.Client?.EnvironmentEventLoop ?? request.KeepaliveBrowsingContext?.EventLoop;
        var options = request.Navigable?.Options ?? request.Client?.EnvironmentOptions;
        if (eventLoop is null || options is null || options.ScriptEngine is null || !options.EffectiveFeatures.ServiceWorkersEnabled)
            return Task.FromResult<FetchResponse?>(null);

        if (request.Destination is RequestDestination.Embed or RequestDestination.Object)
            return Task.FromResult<FetchResponse?>(null);

        ServiceWorkerRegistrationRecord? registration;
        var reservedClient = request.ReservedClient;
        // The static imports of a module worker have the destination of the worker, but no reserved client (the specification
        // asserts that a non-subresource request has one): as in the browsers, they are handled as the subresources of their client
        var isNonSubresource = IsNonSubresourceRequest(request) && !(reservedClient is null && request.Destination is RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker);
        if (isNonSubresource)
        {
            // The request of a navigation or of a worker: the registration whose scope matches the URL of the request
            // The registration is matched with the current URL of the request (the URL of the last redirect of a navigation)
            var url = request.CurrentUrl;
            if (reservedClient is null || !ReferrerPolicies.IsPotentiallyTrustworthy(url) || url.Scheme is not ("http" or "https"))
                return Task.FromResult<FetchResponse?>(null);

            var browsingContext = request.Navigable ?? request.Client?.EnvironmentBrowsingContext;
            if (browsingContext is null || browsingContext.IsDestroyed)
                return Task.FromResult<FetchResponse?>(null);

            // The reserved client of a nested navigation is not a secure context when its parent is not one, and its storage key
            // is opaque when the navigable is sandboxed without allow-same-origin
            if (request.Navigable?.Parent?.Window is { IsSecureContext: false } || request.Navigable is { HasSandboxedOriginFlag: true })
                return Task.FromResult<FetchResponse?>(null);

            var registry = ServiceWorkerRegistry.Get(browsingContext);
            var credentialless = request.Navigable is { } navigable ? navigable.IsCredentiallessNavigable : request.Client?.IsCredentialless ?? false;
            registration = registry.Match(ServiceWorkerRegistry.GetStorageKey(url.SerializeOrigin(), credentialless, browsingContext), url);
            ServiceWorkerRecord? activeWorker;
            lock (registry.Lock)
            {
                activeWorker = registration?.Active;
            }

            if (registration is null || activeWorker is null)
                return Task.FromResult<FetchResponse?>(null);

            reservedClient.ActiveServiceWorker = activeWorker;
        }
        else
        {
            if (request.Client?.ActiveServiceWorker is not { } controller)
                return Task.FromResult<FetchResponse?>(null);

            registration = controller.Registration;
        }

        // The navigation preload request is sent in parallel with the fetch event
        Task<FetchResponse>? preload = null;
        if (request.Mode == RequestMode.Navigate && request.Method == "GET" && ShouldPreload(registration, out var headerValue))
        {
            // As the request of the fetch event, the preload request has a single URL (the current URL of the navigation
            // request), and keeps the URLs of the redirects of the navigation (see FetchRequest.NavigationRedirectUrls)
            var preloadRequest = request.Clone();
            preloadRequest.UrlList = [request.CurrentUrl];
            preloadRequest.NavigationRedirectUrls = [.. request.NavigationRedirectUrls, .. request.UrlList.Take(request.UrlList.Count - 1)];
            preloadRequest.HeaderList = [.. request.HeaderList, new("Service-Worker-Navigation-Preload", headerValue)];
            preloadRequest.ServiceWorkersMode = ServiceWorkersMode.None;
            preloadRequest.ReservedClient = null;

            // The redirect of the preload request is not followed: the service worker gets an opaque-redirect filtered
            // response (the fetch of a navigation gives the response of the redirect itself)
            preload = Fetching.StartFetch(preloadRequest, cancellationToken).ContinueWith(
                static completed =>
                {
                    var response = Fetching.GetResult(completed);
                    return !response.IsNetworkError && response.Type != FetchResponseType.OpaqueRedirect && response.Internal.Status is 301 or 302 or 303 or 307 or 308 ? response.Internal.ToOpaqueRedirect() : response;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // The client waits for the service worker (an operation of its event loop), and gets the response from a task of its
        // event loop (the fetch algorithm continues on the thread of the client)
        var clientId = request.Client?.EnvironmentId ?? "";
        // The dispatch runs in parallel: it is running (for a host that advances a fake clock) until the client gets the response
        var dispatchRegistry = registration.Registry;
        dispatchRegistry.BeginParallelOperation();
        request.Timing.ServiceWorkerStart = eventLoop.TimeProvider.GetTimestamp();

        // The response of a speculative fetch is not reused by the fetch of the parser (it is not stored in the HTTP cache), which sends
        // the request again; the browsers send one request. A service worker gets one fetch event for the two requests when
        // the speculative one is still handled: the request of the parser waits for its response.
        var speculativeKey = !isNonSubresource && request is { Method: "GET", Body: null, Client: not null } ? new SpeculativeFetchKey(request.CurrentUrl.Serialize(), request.Destination, request.Mode, request.CredentialsMode) : (SpeculativeFetchKey?)null;
        if (speculativeKey is not { } key || request.IsSpeculative || !request.Client!.TryTakeSpeculativeServiceWorkerFetch(key, out var dispatch))
        {
            dispatch = CreateFetchEventAndDispatchAsync(request, registration, isNonSubresource, clientId, preload, cancellationToken);
            if (speculativeKey is { } speculativeFetchKey && request.IsSpeculative)
            {
                request.Client!.AddSpeculativeServiceWorkerFetch(speculativeFetchKey, dispatch);
            }
        }

        if (request.Synchronous)
        {
            var result = WaitSynchronously(dispatch, cancellationToken);
            dispatchRegistry.EndParallelOperation();
            return Task.FromResult(result);
        }

        var completion = new TaskCompletionSource<FetchResponse?>();
        eventLoop.BeginOperation();
        _ = dispatch.ContinueWith(
            completed =>
            {
                var response = completed.IsCompletedSuccessfully ? completed.Result : FetchResponse.NetworkError(aborted: completed.IsCanceled);
                eventLoop.QueueTask(() => completion.TrySetResult(response));
                if (eventLoop.IsDisposed)
                {
                    completion.TrySetResult(FetchResponse.NetworkError(aborted: true));
                }

                eventLoop.EndOperation();
                dispatchRegistry.EndParallelOperation();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return completion.Task;
    }

    private static FetchResponse? WaitSynchronously(Task<FetchResponse?> dispatch, CancellationToken cancellationToken)
    {
        using var blockingWait = DomEventLoop.EnterBlockingWait(dispatch, cancellationToken);
        using var registration = blockingWait.Registration;
        try
        {
            dispatch.Wait(cancellationToken);
            return dispatch.GetAwaiter().GetResult();
        }
#pragma warning disable CA1031 // A failure of the service worker is a network error
        catch (Exception)
#pragma warning restore CA1031
        {
            return FetchResponse.NetworkError(aborted: cancellationToken.IsCancellationRequested);
        }
    }

    /// <summary>https://fetch.spec.whatwg.org/#non-subresource-request</summary>
    internal static bool IsNonSubresourceRequest(FetchRequest request) => request.Destination is RequestDestination.Document or RequestDestination.Iframe or RequestDestination.Frame
        or RequestDestination.Embed or RequestDestination.Object or RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker;

    private static bool ShouldPreload(ServiceWorkerRegistrationRecord registration, out string headerValue)
    {
        lock (registration.Registry.Lock)
        {
            headerValue = registration.NavigationPreloadHeaderValue;
            return registration.NavigationPreloadEnabled && registration.Active is { EventTypesToHandle: { } types, AllFetchListenersAreEmpty: false } && types.Contains("fetch");
        }
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#create-fetch-event-and-dispatch</summary>
    private static async Task<FetchResponse?> CreateFetchEventAndDispatchAsync(FetchRequest request, ServiceWorkerRegistrationRecord registration, bool isNonSubresource, string clientId, Task<FetchResponse>? preload, CancellationToken cancellationToken)
    {
        var registry = registration.Registry;
        ServiceWorkerRecord? activeWorker;
        bool shouldSoftUpdate;
        lock (registry.Lock)
        {
            activeWorker = registration.Active;
            shouldSoftUpdate = isNonSubresource || registration.IsStale(registry.Now);
        }

        if (activeWorker is null)
            return null;

        if (ServiceWorkerRegistry.ShouldSkipEvent(activeWorker, "fetch") || activeWorker.AllFetchListenersAreEmpty)
        {
            if (shouldSoftUpdate)
            {
                registry.ScheduleSoftUpdate(registration);
            }

            return null;
        }

        // The resulting client of a navigational prefetch is not exposed (https://github.com/WICG/nav-speculation/issues/346)
        var resultingClientId = isNonSubresource && !request.IsPrefetch && request.ReservedClient is { } reservedClient ? reservedClient.Id : "";
        var replacesClientId = request.Mode == RequestMode.Navigate ? request.ReplacesClientId ?? "" : "";
        Interlocked.Increment(ref activeWorker.PendingFunctionalEvents);
        FetchEventOutcome outcome;
        try
        {
            await registry.WaitForActivationAsync(activeWorker).ConfigureAwait(false);
            if (await registry.RunServiceWorkerAsync(activeWorker).ConfigureAwait(false) == ServiceWorkerStartStatus.Failure)
            {
                outcome = FetchEventOutcome.Failed;
            }
            else
            {
                var wait = registry.CreateWait<FetchEventOutcome>();
                request.Timing.ServiceWorkerReady = registry.EventLoop.TimeProvider.GetTimestamp();
                registry.QueueTaskOnWorker(
                    activeWorker,
                    global => DispatchFetchEvent(global, request, clientId, resultingClientId, replacesClientId, preload, wait, cancellationToken),
                    () => wait.Complete(FetchEventOutcome.Failed));
                outcome = await wait.WaitAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            registry.OnFunctionalEventEnded(activeWorker);
        }

        if (shouldSoftUpdate)
        {
            registry.ScheduleSoftUpdate(registration);
        }

        // The handled promise is settled on the thread of the service worker
        var handled = outcome.Handled;
        var global = outcome.Global;
        void SettleHandled(bool networkError)
        {
            if (handled is null || global is null)
                return;

            global.QueueGlobalTask(() =>
            {
                if (networkError)
                {
                    handled.TrySetException(new DomException("The fetch event was not handled", "NetworkError"));
                }
                else
                {
                    handled.TrySetResult();
                }
            });
        }

        if (!outcome.RespondWithEntered)
        {
            SettleHandled(networkError: outcome.Canceled);
            return outcome.Canceled ? FetchResponse.NetworkError() : null;
        }

        if (outcome.HandleFetchFailed || outcome.Response is null)
        {
            SettleHandled(networkError: true);
            return FetchResponse.NetworkError();
        }

        SettleHandled(networkError: false);
        return outcome.Response;
    }

    /// <summary>Dispatches the fetch event at the global object of the service worker (on its thread).</summary>
    private static void DispatchFetchEvent(ServiceWorkerGlobalScope global, FetchRequest request, string clientId, string resultingClientId, string replacesClientId, Task<FetchResponse>? preload, ServiceWorkerWait<FetchEventOutcome> completion, CancellationToken cancellationToken)
    {
        if (global.ScriptEngine?.GetServiceWorkerFetchOperations(global) is not { } operations)
        {
            completion.Complete(FetchEventOutcome.Failed);
            return;
        }

        var abortController = new AbortController();
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => global.QueueGlobalTask(() => abortController.Abort()));
        }

        var requestObject = operations.CreateRequest(request, abortController.Signal);
        Task<object?> preloadResponse;
        if (preload is null)
        {
            preloadResponse = Task.FromResult<object?>(null);
        }
        else
        {
            var preloadCompletion = new TaskCompletionSource<object?>();
            _ = preload.ContinueWith(
                completed => global.QueueGlobalTask(() =>
                {
                    var response = Fetching.GetResult(completed);
                    if (response.IsNetworkError)
                    {
                        preloadCompletion.TrySetException(new TypeErrorException("The navigation preload request failed"));
                    }
                    else
                    {
                        preloadCompletion.TrySetResult(operations.CreateResponse(response));
                    }
                }),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            preloadResponse = preloadCompletion.Task;
        }

        var handled = new TaskCompletionSource();
        var @event = new FetchEvent("fetch", new FetchEventInit
        {
            Cancelable = true,
            Request = requestObject,
            PreloadResponse = preloadResponse,
            ClientId = clientId,
            ResultingClientId = resultingClientId,
            ReplacesClientId = replacesClientId,
            Handled = handled.Task,
        })
        {
            // The body of a response is given to fetch() as it is read (the other requests that read their body as it arrives,
            // e.g. the media elements, get the complete body of the response of a service worker)
            StreamsResponseBody = request.StreamsBody && request.Destination == RequestDestination.Empty,
        };

        global.FireEvent(@event);
        global.UpdateExtendedEvents(@event);
        var respondWithEntered = @event.RespondWithEntered;
        var canceled = @event.CanceledFlag;
        _ = @event.WhenResponded.ContinueWith(
            _ => completion.Complete(new FetchEventOutcome(respondWithEntered, canceled, @event.RespondWithError, @event.PotentialResponse, handled, global)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>The key of a speculative fetch of a document that a service worker handles (see <see cref="HandleFetchAsync"/>).</summary>
    internal readonly record struct SpeculativeFetchKey(string Url, RequestDestination Destination, RequestMode Mode, FetchCredentialsMode CredentialsMode);

    private sealed record FetchEventOutcome(bool RespondWithEntered, bool Canceled, bool HandleFetchFailed, FetchResponse? Response, TaskCompletionSource? Handled, ServiceWorkerGlobalScope? Global)
    {
        public static FetchEventOutcome Failed { get; } = new(RespondWithEntered: false, Canceled: false, HandleFetchFailed: true, Response: null, Handled: null, Global: null);
    }
}

/// <summary>
/// The reserved client of a request (https://fetch.spec.whatwg.org/#concept-request-reserved-client): the environment that
/// the response of a navigation or of a worker script creates, whose id and active service worker are given to the new
/// document or worker.
/// </summary>
internal sealed class ReservedClient
{
    private ServiceWorkerRecord? _activeServiceWorker;

    public ReservedClient(string origin)
    {
        Origin = origin;
    }

    /// <summary>The id of the environment (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-id).</summary>
    public string Id { get; } = Guid.NewGuid().ToString("D");

    /// <summary>The serialized origin of the creation URL of the environment.</summary>
    public string Origin { get; }

    /// <summary>The active service worker set by the fetch handling (read by the thread of the client).</summary>
    public ServiceWorkerRecord? ActiveServiceWorker
    {
        get => Volatile.Read(ref _activeServiceWorker);
        set => Volatile.Write(ref _activeServiceWorker, value);
    }
}
