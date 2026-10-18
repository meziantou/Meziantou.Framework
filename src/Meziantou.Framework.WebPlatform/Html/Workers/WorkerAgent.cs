using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The agent of a dedicated or shared worker (https://html.spec.whatwg.org/multipage/webappapis.html#dedicated-worker-agent):
/// its thread, its event loop, its script engine and its global object. The event loop of the worker runs on its own
/// thread until the worker is closed or terminated (https://html.spec.whatwg.org/multipage/workers.html#run-a-worker).
/// </summary>
#pragma warning disable CA1001 // The cancellation source is disposed by the thread of the worker when it ends
internal sealed class WorkerAgent
#pragma warning restore CA1001
{
    // The maximum stack size of the thread of a worker: the script engines recurse on the stack of the thread
    private const int StackSize = 16 * 1024 * 1024;

    private readonly CancellationTokenSource _termination = new();
    private readonly ScriptEngine? _ownerScriptEngine;
    private readonly List<Action> _pendingConnections = [];
    private volatile bool _closing;
    private volatile bool _terminated;

    public WorkerAgent(GlobalScope owner, UrlRecord url, string name, WorkerType type, bool isShared)
    {
        Owner = owner;
        Options = owner.Options;
        CreatorDocument = owner.EnvironmentDocument;
        CookieJar = CreatorDocument.CookieJar;
        CreatorIsSecureContext = owner.IsSecureContext;
        CreatorCrossOriginIsolated = owner.CrossOriginIsolated;
        CreatorAgentClusterKey = owner.AgentClusterKey;
        CreatorAgentClusterCrossOriginIsolated = owner.IsAgentClusterCrossOriginIsolated;
        Url = url;
        Name = name;
        Type = type;
        Kind = isShared ? WorkerKind.Shared : WorkerKind.Dedicated;
        _ownerScriptEngine = owner.ScriptEngine;
        EventLoop = new DomEventLoop(owner.EventLoop, Terminate);
        if (EventLoop.IsDisposed)
        {
            _terminated = true;
            _closing = true;
        }
    }

    /// <summary>
    /// Creates the agent of a service worker (https://w3c.github.io/ServiceWorker/#run-service-worker-algorithm): it has no
    /// creator, it belongs to the browsing context group of <paramref name="groupOwner"/> (its event loop is a child of the
    /// event loop of the group, which terminates it when it is disposed), and it is a secure context.
    /// </summary>
    public WorkerAgent(BrowsingContext groupOwner, UrlRecord url, WorkerType type)
    {
        GroupBrowsingContext = groupOwner;
        Options = groupOwner.Options;
        CookieJar = groupOwner.GetCookieJar(credentialless: false);
        CreatorIsSecureContext = true;
        Url = url;
        Name = "";
        Type = type;
        Kind = WorkerKind.Service;
        _ownerScriptEngine = groupOwner.Options.ScriptEngine;
        EventLoop = new DomEventLoop(groupOwner.EventLoop, Terminate);
        if (EventLoop.IsDisposed)
        {
            _terminated = true;
            _closing = true;
        }
    }

    /// <summary>The global object that created the worker (for a shared worker, the global object of its first <see cref="SharedWorker"/>), or null for a service worker.</summary>
    public GlobalScope? Owner { get; }

    /// <summary>The browsing context that owns the browsing context group of a service worker, or null for the other workers (see <see cref="Document.EnvironmentBrowsingContext"/>).</summary>
    public BrowsingContext? GroupBrowsingContext { get; }

    public BrowsingContextOptions Options { get; }

    /// <summary>The environment of the creator of the worker (the outside settings), the client of the fetch of its script, or null for a service worker.</summary>
    public Document? CreatorDocument { get; }

    public CookieJar? CookieJar { get; }

    public bool CreatorIsSecureContext { get; }

    /// <summary>The cross-origin isolated capability of the creator (read on the thread of the creator).</summary>
    public bool CreatorCrossOriginIsolated { get; }

    /// <summary>The agent cluster of the creator (the agent cluster of a dedicated worker).</summary>
    public object? CreatorAgentClusterKey { get; }

    /// <summary>Whether the cross-origin isolation mode of the agent cluster of the creator is not "none".</summary>
    public bool CreatorAgentClusterCrossOriginIsolated { get; }

    /// <summary>The URL of the script of the worker (the global object uses the URL of the response).</summary>
    public UrlRecord Url { get; }

    public string Name { get; }

    public WorkerType Type { get; }

    /// <summary>The credentials mode of the options of the worker (a shared worker is only shared by the objects with the same options).</summary>
    public RequestCredentials Credentials { get; init; } = RequestCredentials.SameOrigin;

    /// <summary>The extended lifetime option of a shared worker.</summary>
    public bool ExtendedLifetime { get; init; }

    public WorkerKind Kind { get; }

    /// <summary>The service worker run by the agent of a service worker, or null.</summary>
    public ServiceWorkerRecord? ServiceWorker { get; init; }

    /// <summary>Called when the worker ended: on its thread after its cleanup, or by <see cref="Start"/> when it was terminated before it started.</summary>
    public Action? OnEnded { get; set; }

    public bool IsShared => Kind == WorkerKind.Shared;

    public DomEventLoop EventLoop { get; }

    public ScriptEngine? ScriptEngine { get; private set; }

    public WorkerGlobalScope? Global { get; private set; }

    /// <summary>The <see cref="Dom.Worker"/> object of a dedicated worker, whose events are fired on the thread of its owner.</summary>
    public Worker? WorkerObject { get; init; }

    /// <summary>The inside port of a dedicated worker, entangled with the outside port of its <see cref="Dom.Worker"/> object.</summary>
    public MessagePort? InsidePort { get; init; }

    /// <summary>The closing flag of the global object (https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-closing).</summary>
    public bool IsClosing => _closing;

    /// <summary>Whether the worker was terminated: the exceptions of the aborted scripts are not reported.</summary>
    public bool IsTerminated => _terminated;

    /// <summary>Whether the script of the worker ran (the connections of a shared worker are processed afterwards).</summary>
    private bool ScriptStarted { get; set; }

    public void Start()
    {
        if (_terminated)
        {
            OnEnded?.Invoke();
            return;
        }

        var thread = new Thread(Run, StackSize)
        {
            IsBackground = true,
            Name = Kind switch { WorkerKind.Shared => "SharedWorker ", WorkerKind.Service => "ServiceWorker ", _ => "Worker " } + Url.Serialize(),
        };
        thread.Start();
    }

    private void Run()
    {
        EventLoop.OnWorkerThreadStarted();
        try
        {
            ScriptEngine = _ownerScriptEngine?.CreateWorkerScriptEngine(_termination.Token);
            EventLoop.RunWorker(() => _closing, _termination.Token);
        }
#pragma warning disable CA1031 // The thread of a worker must not end the process
        catch (Exception)
#pragma warning restore CA1031
        {
            // The exceptions of a terminated worker are ignored. An exception that is not an error of a script (e.g. a
            // constraint of the script engine) ends the worker, as if the agent had crashed
        }
        finally
        {
            EventLoop.OnWorkerEnding();
            Cleanup();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#run-a-worker (the steps after the event loop ends)</summary>
    private void Cleanup()
    {
        _closing = true;
        if (Global is { } global)
        {
            // https://w3c.github.io/FileAPI/#lifeTime: the blob URLs created by the worker are revoked
            Dom.FileApi.BlobUrlStore.Get(global.EnvironmentDocument).RemoveEntries(global.EnvironmentDocument);
            BroadcastChannel.RemoveChannels(global);

            // The connections of the WebSockets of the worker are closed (with the going away status code)
            WebSocket.MakeDisappear(global);
            EventSource.ForciblyClose(global);
            IdbFactory.CloseConnections(global);
            WebLocks.OnEnvironmentDiscarded(global);
            global.ReleaseFileSystemResources();
            ServiceWorkerRegistry.OnEnvironmentDiscarded(global.EnvironmentDocument);
            InsidePort?.Disentangle();
            try
            {
                ScriptEngine?.DestroyRealm(global);
            }
#pragma warning disable CA1031 // The engine is released anyway
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        (ScriptEngine as IDisposable)?.Dispose();
        EventLoop.Dispose();
        GlobalScope.CurrentWorkerGlobal = null;
        Document.CurrentGlobalDocumentOrNull = null;
        _termination.Dispose();
        OnEnded?.Invoke();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#terminate-a-worker: can be called from any thread.</summary>
    public void Terminate()
    {
        if (_terminated)
            return;

        _closing = true;
        _terminated = true;
        EventLoop.OnWorkerEnding();
        try
        {
            _termination.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        EventLoop.SignalWork();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-close: called by the worker.</summary>
    public void Close() => _closing = true;

    /// <summary>
    /// Runs the script of the worker, on its thread (https://html.spec.whatwg.org/multipage/workers.html#run-a-worker): the
    /// global object and the realm are created with the URL of the response.
    /// </summary>
    public void RunScript(FetchedWorkerScript script)
    {
        if (_closing)
            return;

        WorkerGlobalScope global = IsShared
            ? new SharedWorkerGlobalScope(this, script.Url, Name)
            : new DedicatedWorkerGlobalScope(this, script.Url, Name);
        Global = global;
        global.ApplyResponsePolicies(script);

        // https://html.spec.whatwg.org/multipage/workers.html#check-a-global-object's-embedder-policy: a dedicated worker
        // whose owner requires CORP (or is credentialless) must also have such an embedder policy, else its script is a
        // network error. The violations are reported to the owner (also for its report-only embedder policy).
        if (Kind == WorkerKind.Dedicated && !CheckEmbedderPolicy(global, Owner!.EnvironmentDocument, script.Url))
        {
            OnScriptLoadFailed();
            return;
        }
        GlobalScope.CurrentWorkerGlobal = global;
        Document.CurrentGlobalDocument = global.EnvironmentDocument;

        // https://w3c.github.io/ServiceWorker/#control-and-use-worker-client: a worker is controlled by the active service
        // worker given by the fetch of its script, or by the controller of its creator for a blob: URL
        global.EnvironmentDocument.ActiveServiceWorker = script.Url.Scheme == "blob" ? CreatorDocument?.ActiveServiceWorker : script.ActiveServiceWorker;
        if (script.ReservedClientId is { } reservedClientId)
        {
            global.EnvironmentDocument.EnvironmentId = reservedClientId;
        }

        ServiceWorkerRegistry.OnEnvironmentCreated(global, IsShared ? ClientType.SharedWorker : ClientType.Worker);
        if (InsidePort is { } insidePort)
        {
            insidePort.MessageEventTarget = global;
            insidePort.SetRelevantGlobal(global);
        }

        if (ScriptEngine is not { } engine)
        {
            OnScriptRun();
            return;
        }

        EventLoop.AttachScriptEngine(engine);
        engine.CreateRealm(global);
        if (Type == WorkerType.Classic)
        {
            // https://html.spec.whatwg.org/multipage/workers.html#run-a-worker: a script that cannot be parsed fires an error
            // event at the worker object (an Event, not an ErrorEvent)
            var classicScript = new ClassicScript(global, script.SourceText, script.Url.Serialize()) { Url = script.Url.Serialize() };
            if (engine.ParseClassicScript(classicScript) is not null)
            {
                OnScriptLoadFailed();
                return;
            }

            ScriptExecution.RunClassicScript(engine, classicScript);
            OnScriptRun();
            return;
        }

        if (!engine.SupportsModuleScripts)
        {
            OnScriptLoadFailed();
            return;
        }

        var moduleScript = new ModuleScript(global, script.SourceText, script.Url.Serialize()) { Url = script.Url.Serialize(), FetchOptions = new ModuleFetchOptions { IncludeCredentials = script.IncludeCredentials } };
        global.IsFetchingModuleScriptGraph = true;
        engine.LoadModuleScriptGraph(moduleScript, loaded =>
        {
            global.IsFetchingModuleScriptGraph = false;

            // A module of the graph that cannot be fetched or parsed fires an error event at the worker object (an Event)
            if (!loaded || moduleScript.ErrorToRethrow is not null)
            {
                OnScriptLoadFailed();
                return;
            }

            ScriptExecution.RunModuleScript(engine, moduleScript);
            OnScriptRun();
        });
    }

    /// <summary>
    /// Runs the script of a service worker, on its thread (https://w3c.github.io/ServiceWorker/#run-service-worker-algorithm):
    /// <paramref name="onStarted"/> is called with the start status once the script was evaluated.
    /// </summary>
    public void RunServiceWorker(FetchedWorkerScript script, Action<ServiceWorkerStartStatus> onStarted)
    {
        if (_closing)
        {
            onStarted(ServiceWorkerStartStatus.Failure);
            return;
        }

        var global = new ServiceWorkerGlobalScope(this, ServiceWorker!.ScriptUrl);
        Global = global;
        global.ApplyResponsePolicies(script);
        GlobalScope.CurrentWorkerGlobal = global;
        Document.CurrentGlobalDocument = global.EnvironmentDocument;

        // The objects of the registration and of the worker (self.registration, self.serviceWorker) are created now: the
        // tasks that the algorithms of the registration queue from then on update them in order, so the scripts see the
        // states in the order of the tasks of the worker (e.g. an "activating" worker until the task that follows its
        // activate event), whatever the progress of the algorithms on the other threads
        global.GetServiceWorkerRegistrationObject(ServiceWorker.Registration);
        global.GetServiceWorkerObject(ServiceWorker);
        if (ScriptEngine is not { } engine)
        {
            onStarted(ServiceWorkerStartStatus.Failure);
            return;
        }

        EventLoop.AttachScriptEngine(engine);
        engine.CreateRealm(global);
        if (Type == WorkerType.Classic)
        {
            var classicScript = new ClassicScript(global, script.SourceText, script.Url.Serialize()) { Url = script.Url.Serialize() };
            if (engine.ParseClassicScript(classicScript) is not null)
            {
                onStarted(ServiceWorkerStartStatus.Failure);
                return;
            }

            ScriptExecution.RunClassicScript(engine, classicScript, out var threw);
            ScriptStarted = true;
            onStarted(_terminated ? ServiceWorkerStartStatus.Failure : threw ? ServiceWorkerStartStatus.Abrupt : ServiceWorkerStartStatus.Normal);
            return;
        }

        if (!engine.SupportsModuleScripts)
        {
            onStarted(ServiceWorkerStartStatus.Failure);
            return;
        }

        // https://w3c.github.io/ServiceWorker/#update-algorithm: the module graph of a service worker is fetched with the
        // credentials mode "omit"
        var moduleScript = new ModuleScript(global, script.SourceText, script.Url.Serialize()) { Url = script.Url.Serialize(), FetchOptions = new ModuleFetchOptions { CredentialsMode = FetchCredentialsMode.Omit } };
        global.IsFetchingModuleScriptGraph = true;
        engine.LoadModuleScriptGraph(moduleScript, loaded =>
        {
            global.IsFetchingModuleScriptGraph = false;
            if (!loaded || moduleScript.ErrorToRethrow is not null)
            {
                onStarted(ServiceWorkerStartStatus.Failure);
                return;
            }

            // The evaluation of the module is an abrupt completion when its evaluation promise is rejected (or, for an
            // engine that does not give the state of the promise, when the evaluation reports an exception). A promise that
            // is still pending is the one of an asynchronous module (a top-level await), which a service worker cannot be
            // (https://w3c.github.io/ServiceWorker/#update-algorithm, Is Async Module).
            var reportedErrors = EventDispatcher.ReportedExceptionCount;
            ScriptExecution.RunModuleScript(engine, moduleScript);
            ScriptStarted = true;
            var status = moduleScript.EvaluationState switch
            {
                ModuleEvaluationState.Pending => ServiceWorkerStartStatus.Failure,
                ModuleEvaluationState.Rejected => ServiceWorkerStartStatus.Abrupt,
                ModuleEvaluationState.Fulfilled => ServiceWorkerStartStatus.Normal,
                _ => EventDispatcher.ReportedExceptionCount != reportedErrors ? ServiceWorkerStartStatus.Abrupt : ServiceWorkerStartStatus.Normal,
            };
            onStarted(_terminated ? ServiceWorkerStartStatus.Failure : status);
        });
    }

    /// <summary>The script ran: the inside port of a dedicated worker is enabled, and the connections of a shared worker are processed.</summary>
    private void OnScriptRun()
    {
        ScriptStarted = true;
        InsidePort?.Start();
        List<Action> connections;
        lock (_pendingConnections)
        {
            connections = [.. _pendingConnections];
            _pendingConnections.Clear();
        }

        foreach (var connection in connections)
        {
            connection();
        }
    }

    /// <summary>The script of the worker cannot be fetched or parsed: an error event is fired at the worker object, and the worker ends.</summary>
    public void OnScriptLoadFailed()
    {
        _closing = true;
        if (IsShared)
        {
            SharedWorkerManager.OnScriptLoadFailed(this);
        }
        else if (WorkerObject is { } worker)
        {
            Owner!.QueueGlobalTask(() => worker.FireEvent(new Event("error")));
        }

        EventLoop.SignalWork();
    }

    /// <summary>Runs a connection of a shared worker on its thread, after its script ran (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker).</summary>
    public void Connect(Action connection)
    {
        EventLoop.QueueTask(() =>
        {
            if (_closing)
                return;

            if (!ScriptStarted)
            {
                lock (_pendingConnections)
                {
                    _pendingConnections.Add(connection);
                }

                return;
            }

            connection();
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#check-a-global-object's-embedder-policy, for a dedicated worker.</summary>
    private static bool CheckEmbedderPolicy(WorkerGlobalScope global, Document owner, UrlRecord responseUrl)
    {
        var policy = global.EnvironmentDocument.EmbedderPolicy;
        var ownerPolicy = owner.EmbedderPolicy;
        if (EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(ownerPolicy.ReportOnlyValue) && !EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(policy.Value))
        {
            EmbedderPolicy.QueueInheritanceViolation(responseUrl, "worker initialization", ownerPolicy.ReportOnlyReportingEndpoint, "reporting", owner);
        }

        if (!EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(ownerPolicy.Value) || EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(policy.Value))
            return true;

        EmbedderPolicy.QueueInheritanceViolation(responseUrl, "worker initialization", ownerPolicy.ReportingEndpoint, "enforce", owner);
        return false;
    }
}

/// <summary>The kind of a worker agent.</summary>
internal enum WorkerKind
{
    Dedicated,
    Shared,
    Service,
}

/// <summary>The response of the script of a worker (https://html.spec.whatwg.org/multipage/workers.html#worker-processing-model).</summary>
internal sealed record FetchedWorkerScript(UrlRecord Url, string SourceText, IReadOnlyList<KeyValuePair<string, string>> Headers, bool IncludeCredentials)
{
    /// <summary>The active service worker of the reserved client of the fetch of the script (the controller of the worker), or null.</summary>
    public ServiceWorkerRecord? ActiveServiceWorker { get; init; }

    /// <summary>The id of the reserved client of the fetch of the script (the id of the environment of the worker), or null.</summary>
    public string? ReservedClientId { get; init; }
}

/// <summary>
/// Fetches the scripts of the workers: the script of a new worker
/// (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-worker-script) and the scripts of
/// <c>importScripts()</c> (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-worker-imported-script).
/// </summary>
internal static class WorkerScriptFetching
{
    private static readonly System.Text.UTF8Encoding Utf8WithoutByteOrderMark = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>
    /// Fetches the script of a new worker with the environment of its creator. <paramref name="onComplete"/> is called with
    /// the script, or null when the fetch fails (a network error, a non-ok status or a MIME type that is not JavaScript).
    /// </summary>
    public static void FetchWorkerScript(GlobalScope owner, DomEventLoop workerEventLoop, UrlRecord url, bool isShared, WorkerType type, RequestCredentials credentials, Action<FetchedWorkerScript?> onComplete)
    {
        var client = owner.EnvironmentDocument;
        var credentialsMode = type == WorkerType.Classic ? FetchCredentialsMode.SameOrigin : ToCredentialsMode(credentials);
        var request = new FetchRequest(url)
        {
            Client = client,
            Origin = client.SerializeOrigin(),
            Destination = isShared ? RequestDestination.SharedWorker : RequestDestination.Worker,

            // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script: the top-level script of a
            // module worker is also fetched with the "same-origin" mode
            Mode = RequestMode.SameOrigin,
            CredentialsMode = credentialsMode,
            BlobUrlEntry = url.Scheme == "blob" ? Dom.FileApi.BlobUrlStore.Get(client).Resolve(url) : null,

            // The environment of the worker, which a service worker may control (https://w3c.github.io/ServiceWorker/#control-and-use-worker-client)
            ReservedClient = new ReservedClient(url.SerializeOrigin()),
        };

        // The timing of the fetch of the script of a dedicated worker is added to the performance timeline of its creator: the
        // initiator type of a classic worker script is "other", the one of a module worker script is "script"
        // (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-worker-script). The script of a shared
        // worker is not reported (a shared worker may already run when a document connects to it).
        var timing = isShared ? null : ResourceTiming.Start(owner, request, type == WorkerType.Classic ? "other" : "script");

        // The worker is running until its script is fetched: a host that advances a fake clock does not advance it meanwhile
        workerEventLoop.BeginRunningOperation();
        Task<FetchResponse> fetch;
        try
        {
            fetch = Fetching.FetchAsync(request, CancellationToken.None);
        }
#pragma warning disable CA1031 // A failure of the fetch is a network error
        catch (Exception ex)
#pragma warning restore CA1031
        {
            fetch = Task.FromException<FetchResponse>(ex);
        }

        _ = fetch.ContinueWith(
            completed =>
            {
                try
                {
                    if (timing is not null && completed.IsCompletedSuccessfully && request.IsSentToLoader)
                    {
                        var response = completed.Result;
                        owner.QueueGlobalTask(() => timing.Report(response));
                    }

                    var script = completed.IsCompletedSuccessfully ? ProcessResponse(completed.Result, includeCredentials: credentialsMode == FetchCredentialsMode.Include, isWorkerScript: true) : null;
                    onComplete(script is null ? null : script with { ActiveServiceWorker = request.ReservedClient?.ActiveServiceWorker, ReservedClientId = request.ReservedClient?.Id });
                }
                finally
                {
                    workerEventLoop.EndRunningOperation();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Fetches a script of <c>importScripts()</c> synchronously, with the environment of the worker. Returns null when the
    /// fetch fails; <paramref name="mutedErrors"/> is true when the response is opaque (a cross-origin script without CORS).
    /// </summary>
    public static FetchedWorkerScript? FetchImportedScript(WorkerGlobalScope global, UrlRecord url, Dom.FileApi.BlobUrlEntry? blobUrlEntry, out bool mutedErrors)
    {
        var response = FetchImportedScriptResponse(global, url, blobUrlEntry, ServiceWorkersMode.All, cacheMode: "default");
        return ProcessImportedScriptResponse(global, response, out mutedErrors);
    }

    /// <summary>Fetches a script of <c>importScripts()</c> synchronously, with the environment of the worker, and returns the response.</summary>
    public static FetchResponse FetchImportedScriptResponse(WorkerGlobalScope global, UrlRecord url, Dom.FileApi.BlobUrlEntry? blobUrlEntry, ServiceWorkersMode serviceWorkersMode, string cacheMode)
    {
        var client = global.EnvironmentDocument;
        var request = new FetchRequest(url)
        {
            Client = client,
            Origin = client.SerializeOrigin(),
            Destination = RequestDestination.Script,
            Mode = RequestMode.NoCors,
            CredentialsMode = FetchCredentialsMode.Include,
            BlobUrlEntry = blobUrlEntry,
            Synchronous = true,
            ServiceWorkersMode = serviceWorkersMode,
            CacheMode = cacheMode,
        };

        // The fetch waits for the loader (the wait is a blocking wait of the event loop, see Fetching). Its timing is added to
        // the performance timeline of the worker: the initiator type of the imported scripts is "other".
        var timing = ResourceTiming.Start(global, request, "other");
        FetchResponse response;
        try
        {
            response = Fetching.FetchAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }
#pragma warning disable CA1031 // A failure of the fetch is a network error
        catch (Exception)
#pragma warning restore CA1031
        {
            response = FetchResponse.NetworkError();
        }

        timing?.Report(response);
        return response;
    }

    /// <summary>
    /// Processes the response of a script of <c>importScripts()</c>: returns null when the fetch failed;
    /// <paramref name="mutedErrors"/> is true when the response is opaque (a cross-origin script without CORS).
    /// </summary>
    public static FetchedWorkerScript? ProcessImportedScriptResponse(WorkerGlobalScope global, FetchResponse response, out bool mutedErrors)
    {
        // The errors of a script fetched without CORS from another origin are muted (e.g. after a cross-origin redirect)
        mutedErrors = response.Type == FetchResponseType.Opaque
            || (response.Url is { Scheme: "http" or "https" } finalUrl && response.Type != FetchResponseType.Cors && !OriginRecord.IsSameOrigin(OriginRecord.FromUrl(finalUrl), global.EnvironmentDocument.Origin));
        return ProcessResponse(response, includeCredentials: true, isWorkerScript: false);
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#bad-import-script-response</summary>
    public static bool IsBadImportScriptResponse(FetchResponse response)
    {
        if (response.IsNetworkError)
            return true;

        var internalResponse = response.Type == FetchResponseType.Opaque ? response.Internal : response;
        return internalResponse.Status is < 200 or > 299 || HeaderList.ExtractMimeType(internalResponse.HeaderList) is not { IsJavaScript: true };
    }

    /// <summary>Whether a stored response of the script resource map of a service worker is a bad import script response (see <see cref="IsBadImportScriptResponse"/>).</summary>
    public static bool IsBadImportScript(StoredServiceWorkerScript script)
        => script.Status is < 200 or > 299 || HeaderList.ExtractMimeType([.. script.Headers]) is not { IsJavaScript: true };

    /// <summary>The script of a stored response of the script resource map of a service worker.</summary>
    public static FetchedWorkerScript FromStoredScript(StoredServiceWorkerScript script)
    {
        var url = UrlParser.Parse(script.ResponseUrl ?? script.Url, baseUrl: null)!;
        var body = script.Body.Span;
        var bytes = body.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? body[3..] : body;
        return new FetchedWorkerScript(url, Utf8WithoutByteOrderMark.GetString(bytes), script.Headers, IncludeCredentials: true);
    }

    /// <summary>The response of a stored response of the script resource map of a service worker (the response of a static import of a module service worker).</summary>
    public static FetchResponse ToResponse(StoredServiceWorkerScript script)
    {
        var response = FetchResponse.Create(script.Status, script.StatusText, [.. script.Headers], script.Body.ToArray());
        response.UrlList.Add(UrlParser.Parse(script.ResponseUrl ?? script.Url, baseUrl: null)!);
        return response;
    }

    /// <summary>The response stored in the script resource map of a service worker (the internal response of an opaque response).</summary>
    public static StoredServiceWorkerScript ToStoredScript(string requestUrl, FetchResponse response)
    {
        var internalResponse = response.Type == FetchResponseType.Opaque ? response.Internal : response;
        var responseUrl = internalResponse.Url?.Serialize();
        return new StoredServiceWorkerScript
        {
            Url = requestUrl,
            ResponseUrl = responseUrl == requestUrl ? null : responseUrl,

            // The status of a network error is 0
            Status = internalResponse.Status,
            StatusText = internalResponse.StatusMessage,
            Headers = [.. internalResponse.HeaderList],
            Body = internalResponse.Body ?? [],
        };
    }

    private static FetchedWorkerScript? ProcessResponse(FetchResponse response, bool includeCredentials, bool isWorkerScript)
    {
        if (response.IsNetworkError)
            return null;

        // An opaque response (a cross-origin import without CORS) has the URL, the status and the body of its internal response
        var internalResponse = response.Type == FetchResponseType.Opaque ? response.Internal : response;
        if (internalResponse.Url is not { } responseUrl || internalResponse.Status is < 200 or > 299)
            return null;

        // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-worker-script: the MIME type of an HTTP(S)
        // response must be a JavaScript MIME type (any MIME type is allowed for a data: or a blob: URL), and the MIME type
        // of an imported script must be a JavaScript MIME type (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-worker-imported-script)
        var essence = HeaderList.ExtractMimeType(internalResponse.HeaderList)?.Essence ?? "";
        if ((!isWorkerScript || responseUrl.Scheme is "http" or "https") && !HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(essence))
            return null;

        // https://encoding.spec.whatwg.org/#utf-8-decode: the UTF-8 byte order mark is removed, and the other byte order marks
        // are decoded as UTF-8 (a UTF-16 script is a syntax error)
        var body = internalResponse.Body ?? [];
        var bytes = body.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? body.AsSpan(3) : body.AsSpan();
        var sourceText = Utf8WithoutByteOrderMark.GetString(bytes);
        return new FetchedWorkerScript(responseUrl, sourceText, internalResponse.HeaderList, includeCredentials);
    }

    private static FetchCredentialsMode ToCredentialsMode(RequestCredentials credentials) => credentials switch
    {
        RequestCredentials.Omit => FetchCredentialsMode.Omit,
        RequestCredentials.Include => FetchCredentialsMode.Include,
        _ => FetchCredentialsMode.SameOrigin,
    };
}
