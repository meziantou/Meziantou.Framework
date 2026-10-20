using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/workers.html#workertype</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#workertype"/>
[IdlInterface("WorkerType", "https://html.spec.whatwg.org/multipage/workers.html#workertype")]
public enum WorkerType
{
    /// <summary>A classic script worker.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workertype-classic"/>
    [IdlMember("classic", "https://html.spec.whatwg.org/multipage/workers.html#dom-workertype-classic")]
    Classic,

    /// <summary>A module script worker.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workertype-module"/>
    [IdlMember("module", "https://html.spec.whatwg.org/multipage/workers.html#dom-workertype-module")]
    Module,
}

/// <summary>The credentials mode of a request (https://fetch.spec.whatwg.org/#requestcredentials).</summary>
/// <seealso href="https://fetch.spec.whatwg.org/#requestcredentials"/>
[IdlInterface("RequestCredentials", "https://fetch.spec.whatwg.org/#requestcredentials")]
public enum RequestCredentials
{
    /// <summary>Credentials are never sent.</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-requestcredentials-omit"/>
    [IdlMember("omit", "https://fetch.spec.whatwg.org/#dom-requestcredentials-omit")]
    Omit,

    /// <summary>Credentials are sent only to same-origin URLs.</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-requestcredentials-same-origin"/>
    [IdlMember("same-origin", "https://fetch.spec.whatwg.org/#dom-requestcredentials-same-origin")]
    SameOrigin,

    /// <summary>Credentials are always sent.</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-requestcredentials-include"/>
    [IdlMember("include", "https://fetch.spec.whatwg.org/#dom-requestcredentials-include")]
    Include,
}

/// <summary>https://html.spec.whatwg.org/multipage/workers.html#workeroptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#workeroptions"/>
[IdlInterface("WorkerOptions", "https://html.spec.whatwg.org/multipage/workers.html#workeroptions")]
public class WorkerOptions
{
    /// <summary>Gets the name of the worker, exposed by its global object.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workeroptions-name"/>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/workers.html#dom-workeroptions-name")]
    public string Name { get; init; } = "";

    /// <summary>Gets the type of the script: a classic script, or a module script.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workeroptions-type"/>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/workers.html#dom-workeroptions-type")]
    public WorkerType Type { get; init; } = WorkerType.Classic;

    /// <summary>Gets the credentials mode of the fetch of a module script.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workeroptions-credentials"/>
    [IdlMember("credentials", "https://html.spec.whatwg.org/multipage/workers.html#dom-workeroptions-credentials")]
    public RequestCredentials Credentials { get; init; } = RequestCredentials.SameOrigin;
}

/// <summary>https://html.spec.whatwg.org/multipage/workers.html#sharedworkeroptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#sharedworkeroptions"/>
[IdlInterface("SharedWorkerOptions", "https://html.spec.whatwg.org/multipage/workers.html#sharedworkeroptions")]
public sealed class SharedWorkerOptions : WorkerOptions
{
    /// <summary>Gets whether the worker outlives its last owner for a while (not supported: the shared workers live as long as their browsing context group).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworkeroptions-extendedlifetime"/>
    [IdlMember("extendedLifetime", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworkeroptions-extendedlifetime")]
    public bool ExtendedLifetime { get; init; }
}

/// <summary>
/// A dedicated worker (https://html.spec.whatwg.org/multipage/workers.html#dedicated-workers-and-the-worker-interface): a
/// script that runs in its own <see cref="DedicatedWorkerGlobalScope"/>, on its own thread, with its own event loop and its
/// own script engine (<see cref="Scripting.ScriptEngine.CreateWorkerScriptEngine"/>). The messages and the errors of the
/// worker are fired at this object by the event loop of its owner.
/// </summary>
/// <remarks>
/// The worker is terminated when <see cref="Terminate"/> is called, when the document of its owner is discarded, or when
/// the browsing context of its owner is disposed. The event loop of the owner considers that an operation is in progress
/// while the worker has a task to run.
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#worker"/>
[IdlInterface("Worker", "https://html.spec.whatwg.org/multipage/workers.html#worker")]
public sealed class Worker : EventTarget
{
    private readonly MessagePort _outsidePort;
    private readonly WorkerAgent _agent;

    /// <summary>Creates a worker whose owner is the current global object (https://html.spec.whatwg.org/multipage/workers.html#dom-worker).</summary>
    /// <param name="scriptUrl">The URL of the script, relative to the API base URL of the current global object.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="InvalidOperationException">There is no current global object (no script is running).</exception>
    /// <exception cref="DomException">A <c>SyntaxError</c> when the URL is invalid.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-worker"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/workers.html#dom-worker")]
    public Worker(string scriptUrl, WorkerOptions? options = null)
        : this(GetCurrentGlobal(), GetCompliantScriptUrl(GetCurrentGlobal(), scriptUrl, "Worker constructor"), options)
    {
    }

    /// <summary>Creates a worker whose owner is the current global object (https://html.spec.whatwg.org/multipage/workers.html#dom-worker).</summary>
    /// <param name="scriptUrl">The URL of the script, created by a Trusted Types policy.</param>
    /// <param name="options">The options.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-worker"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/workers.html#dom-worker")]
    public Worker(TrustedScriptUrl scriptUrl, WorkerOptions? options = null)
        : this(GetCurrentGlobal(), GetCompliantScriptUrl(GetCurrentGlobal(), scriptUrl, "Worker constructor"), options)
    {
    }

    private static GlobalScope GetCurrentGlobal() => GlobalScope.Current ?? throw new InvalidOperationException("A worker can only be created by a window or a worker");

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-string-algorithm: the URL of the script of a
    /// worker is checked by the Trusted Types of the global object that creates it (a window or a worker).
    /// </summary>
    internal static string GetCompliantScriptUrl(GlobalScope owner, object scriptUrl, string sink)
    {
        ArgumentNullException.ThrowIfNull(scriptUrl);
        var document = owner.EnvironmentDocument;
        return scriptUrl is TrustedScriptUrl trustedScriptUrl
            ? TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScriptUrl, document, trustedScriptUrl, sink)
            : TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScriptUrl, document, (string)scriptUrl, sink);
    }

    /// <summary>Creates a worker whose owner is <paramref name="owner"/> (https://html.spec.whatwg.org/multipage/workers.html#dom-worker).</summary>
    /// <param name="owner">The window or the worker that owns the worker.</param>
    /// <param name="scriptUrl">The URL of the script, relative to the API base URL of the owner.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="DomException">A <c>SyntaxError</c> when the URL is invalid.</exception>
    [NotInIdl("The .NET constructor whose owner is explicit")]
    public Worker(GlobalScope owner, string scriptUrl, WorkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(scriptUrl);
        options ??= new WorkerOptions();
        SetRelevantGlobalCore(owner);

        // https://html.spec.whatwg.org/multipage/workers.html#dom-worker (step 5): the URL is encoding-parsed relative to the outside settings
        var url = Html.Reflection.EncodingParseUrl(owner.EnvironmentDocument, scriptUrl) ?? throw new DomException($"The URL '{scriptUrl}' is invalid", "SyntaxError");

        // The outside port of the worker is entangled with the inside port of its global object; the messages of the outside
        // port are fired at the worker object
        _outsidePort = new MessagePort { MessageEventTarget = this };
        _outsidePort.SetRelevantGlobalCore(owner);
        var insidePort = new MessagePort();
        MessagePort.Entangle(_outsidePort, insidePort);
        _outsidePort.Start();

        _agent = new WorkerAgent(owner, url, options.Name, options.Type, isShared: false)
        {
            WorkerObject = this,
            InsidePort = insidePort,
        };
        owner.AddWorker(_agent);
        _agent.Start();

        var agent = _agent;
        WorkerScriptFetching.FetchWorkerScript(owner, agent.EventLoop, url, isShared: false, options.Type, options.Credentials, script =>
        {
            if (script is null)
            {
                agent.OnScriptLoadFailed();
                return;
            }

            agent.EventLoop.QueueTask(() => agent.RunScript(script));
        });
    }

    /// <summary>Terminates the worker: its running script is aborted and its event loop ends (https://html.spec.whatwg.org/multipage/workers.html#dom-worker-terminate).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-worker-terminate"/>
    [IdlMember("terminate", "https://html.spec.whatwg.org/multipage/workers.html#dom-worker-terminate")]
    public void Terminate()
    {
        // The messages of the worker that are not delivered yet are discarded
        _outsidePort.DiscardMessages();
        _agent.Terminate();
    }

    /// <summary>Posts a message to the worker (https://html.spec.whatwg.org/multipage/workers.html#dom-worker-postmessage).</summary>
    /// <param name="message">The message: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    /// <param name="transfer">The objects to transfer.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-worker-postmessage"/>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/workers.html#dom-worker-postmessage")]
    public void PostMessage(object? message, IReadOnlyList<object> transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        PostMessage(message, new StructuredSerializeOptions { Transfer = transfer });
    }

    /// <summary>Posts a message to the worker (https://html.spec.whatwg.org/multipage/workers.html#dom-worker-postmessage-options).</summary>
    /// <param name="message">The message: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    /// <param name="options">The objects to transfer.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-worker-postmessage-options"/>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/workers.html#dom-worker-postmessage-options")]
    public void PostMessage(object? message, StructuredSerializeOptions? options = null) => _outsidePort.PostMessageCore(message, options?.Transfer ?? [], source: null);

    /// <summary>Gets or sets the event handler of the <c>message</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageeventtarget-onmessage"/>
    [IdlMember("onmessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageeventtarget-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>Gets or sets the event handler of the <c>messageerror</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageeventtarget-onmessageerror"/>
    [IdlMember("onmessageerror", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageeventtarget-onmessageerror")]
    public Action<Event>? OnMessageError
    {
        get => GetEventHandler("messageerror");
        set => SetEventHandler("messageerror", value);
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
/// A shared worker (https://html.spec.whatwg.org/multipage/workers.html#shared-workers-and-the-sharedworker-interface): the
/// <see cref="SharedWorker"/> objects of the same browsing context group with the same script URL and name (and origin) are
/// connected to the same <see cref="SharedWorkerGlobalScope"/>, which runs on its own thread. Each object communicates with
/// the worker through its <see cref="Port"/>.
/// </summary>
/// <remarks>A shared worker lives until it calls <c>close()</c> or its browsing context group is disposed.</remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#sharedworker"/>
[IdlInterface("SharedWorker", "https://html.spec.whatwg.org/multipage/workers.html#sharedworker")]
public sealed class SharedWorker : EventTarget
{
    /// <summary>Connects to a shared worker, created if needed (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker).</summary>
    /// <param name="scriptUrl">The URL of the script, relative to the API base URL of the current global object.</param>
    /// <param name="name">The name of the worker.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker")]
    public SharedWorker(string scriptUrl, string name)
        : this(scriptUrl, new SharedWorkerOptions { Name = name })
    {
    }

    /// <summary>Connects to a shared worker, created if needed (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker).</summary>
    /// <param name="scriptUrl">The URL of the script, relative to the API base URL of the current global object.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="InvalidOperationException">There is no current window.</exception>
    /// <exception cref="DomException">A <c>SyntaxError</c> when the URL is invalid.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker")]
    public SharedWorker(string scriptUrl, SharedWorkerOptions? options = null)
        : this(GetCurrentGlobal(), Worker.GetCompliantScriptUrl(GetCurrentGlobal(), scriptUrl, "SharedWorker constructor"), options)
    {
    }

    /// <summary>Connects to a shared worker, created if needed (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker).</summary>
    /// <param name="scriptUrl">The URL of the script, created by a Trusted Types policy.</param>
    /// <param name="options">The options.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker")]
    public SharedWorker(TrustedScriptUrl scriptUrl, SharedWorkerOptions? options = null)
        : this(GetCurrentGlobal(), Worker.GetCompliantScriptUrl(GetCurrentGlobal(), scriptUrl, "SharedWorker constructor"), options)
    {
    }

    /// <summary>Connects to a shared worker, created if needed (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker).</summary>
    /// <param name="scriptUrl">The URL of the script, created by a Trusted Types policy.</param>
    /// <param name="name">The name of the worker.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker")]
    public SharedWorker(TrustedScriptUrl scriptUrl, string name)
        : this(scriptUrl, new SharedWorkerOptions { Name = name })
    {
    }

    private static GlobalScope GetCurrentGlobal() => GlobalScope.Current ?? throw new InvalidOperationException("A shared worker can only be created by a window");

    /// <summary>Connects to a shared worker whose owner is <paramref name="owner"/> (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker).</summary>
    [NotInIdl("The .NET constructor whose owner is explicit")]
    public SharedWorker(GlobalScope owner, string scriptUrl, SharedWorkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(scriptUrl);
        options ??= new SharedWorkerOptions();
        SetRelevantGlobalCore(owner);
        var url = Html.Reflection.EncodingParseUrl(owner.EnvironmentDocument, scriptUrl) ?? throw new DomException($"The URL '{scriptUrl}' is invalid", "SyntaxError");
        Port = new MessagePort();
        Port.SetRelevantGlobalCore(owner);
        SharedWorkerManager.Connect(owner, this, url, options);
    }

    /// <summary>Gets the port connected to the <c>connect</c> event of the worker (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker-port).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker-port"/>
    [IdlMember("port", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker-port")]
    public MessagePort Port { get; }

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
/// The shared worker manager (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker, steps 11–12): the
/// shared workers of each browsing context group, by constructor origin, URL and name.
/// </summary>
internal static class SharedWorkerManager
{
    private static readonly ConditionalWeakTable<DomEventLoop, List<(string Key, WorkerAgent Agent, List<SharedWorker> Objects)>> Workers = [];

    public static void Connect(GlobalScope owner, SharedWorker worker, UrlRecord url, SharedWorkerOptions options)
    {
        var groupLoop = GetGroupEventLoop(owner.EventLoop);
        var registry = Workers.GetValue(groupLoop, static _ => []);
        var key = owner.Origin + "\n" + url.Serialize() + "\n" + options.Name;
        WorkerAgent? agent = null;
        lock (registry)
        {
            registry.RemoveAll(entry => entry.Agent.IsClosing);
            foreach (var entry in registry)
            {
                if (entry.Key != key)
                    continue;

                // A worker of another type, credentials mode or extended lifetime cannot be shared (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker step 12.2),
                // nor a worker whose secure context differs from the one of the creator (https://w3c.github.io/webappsec-secure-contexts/#shared-workers)
                if (entry.Agent.Type != options.Type || entry.Agent.Credentials != options.Credentials || entry.Agent.ExtendedLifetime != options.ExtendedLifetime || entry.Agent.CreatorIsSecureContext != owner.IsSecureContext)
                {
                    owner.QueueGlobalTask(() => worker.FireEvent(new Event("error")));
                    return;
                }

                agent = entry.Agent;
                entry.Objects.Add(worker);
                break;
            }

            if (agent is null)
            {
                agent = new WorkerAgent(owner, url, options.Name, options.Type, isShared: true) { Credentials = options.Credentials, ExtendedLifetime = options.ExtendedLifetime };
                registry.Add((key, agent, [worker]));
                agent.Start();
                var newAgent = agent;
                WorkerScriptFetching.FetchWorkerScript(owner, newAgent.EventLoop, url, isShared: true, options.Type, options.Credentials, script =>
                {
                    if (script is null)
                    {
                        newAgent.OnScriptLoadFailed();
                        return;
                    }

                    newAgent.EventLoop.QueueTask(() => newAgent.RunScript(script));
                });
            }
        }

        // https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworker (step 12.5): the inside port is entangled with
        // the port of the object, and a connect event is fired at the global object of the worker
        var insidePort = new MessagePort();
        MessagePort.Entangle(worker.Port, insidePort);
        agent.Connect(() =>
        {
            if (agent.Global is not { } global)
                return;

            insidePort.SetRelevantGlobalCore(global);
            global.FireEvent(new MessageEvent("connect", new MessageEventInit { Data = "", Ports = [insidePort], Source = insidePort }));
        });
    }

    /// <summary>The script of a shared worker cannot be fetched: an error event is fired at its objects.</summary>
    public static void OnScriptLoadFailed(WorkerAgent agent)
    {
        var groupLoop = GetGroupEventLoop(agent.Owner!.EventLoop);
        if (!Workers.TryGetValue(groupLoop, out var registry))
            return;

        List<SharedWorker> objects;
        lock (registry)
        {
            var index = registry.FindIndex(entry => ReferenceEquals(entry.Agent, agent));
            if (index < 0)
                return;

            objects = [.. registry[index].Objects];
            registry.RemoveAt(index);
        }

        foreach (var worker in objects)
        {
            worker.RelevantGlobal?.QueueGlobalTask(() => worker.FireEvent(new Event("error")));
        }
    }

    internal static DomEventLoop GetGroupEventLoop(DomEventLoop eventLoop)
    {
        while (eventLoop.Owner is { } owner)
        {
            eventLoop = owner;
        }

        return eventLoop;
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/workers.html#worker-locations: the URL of a worker.</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#workerlocation"/>
[IdlInterface("WorkerLocation", "https://html.spec.whatwg.org/multipage/workers.html#workerlocation")]
public sealed class WorkerLocation
{
    private readonly WorkerGlobalScope _global;

    internal WorkerLocation(WorkerGlobalScope global)
    {
        _global = global;
    }

    private UrlRecord Record => _global.Url;

    /// <summary>Gets the serialized URL of the worker script.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-href"/>
    [IdlMember("href", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-href")]
    public string Href => Record.Serialize();

    /// <summary>Gets the serialized origin of the worker script URL.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-origin")]
    public string Origin => Record.SerializeOrigin();

    /// <summary>Gets the scheme of the worker script URL followed by a colon.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-protocol"/>
    [IdlMember("protocol", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-protocol")]
    public string Protocol => Record.Scheme + ":";

    /// <summary>Gets the host and the port (when not the default one) of the worker script URL.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-host"/>
    [IdlMember("host", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-host")]
    public string Host => new Url(Record).Host;

    /// <summary>Gets the host of the worker script URL.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-hostname"/>
    [IdlMember("hostname", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-hostname")]
    public string Hostname => new Url(Record).Hostname;

    /// <summary>Gets the port of the worker script URL, or an empty string when it is the default one.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-port"/>
    [IdlMember("port", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-port")]
    public string Port => new Url(Record).Port;

    /// <summary>Gets the path of the worker script URL.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-pathname"/>
    [IdlMember("pathname", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-pathname")]
    public string Pathname => Record.SerializePath();

    /// <summary>Gets the query of the worker script URL, with its leading question mark, or an empty string.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-search"/>
    [IdlMember("search", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-search")]
    public string Search => new Url(Record).Search;

    /// <summary>Gets the fragment of the worker script URL, with its leading number sign, or an empty string.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-hash"/>
    [IdlMember("hash", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerlocation-hash")]
    public string Hash => new Url(Record).Hash;

    /// <summary>Returns <see cref="Href"/>.</summary>
    [NotInIdl("The stringifier of WorkerLocation (the href attribute)")]
    public override string ToString() => Href;
}

/// <summary>https://html.spec.whatwg.org/multipage/workers.html#the-workernavigator-object: the identity and the state of the user agent in a worker.</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#workernavigator"/>
[IdlInterface("WorkerNavigator", "https://html.spec.whatwg.org/multipage/workers.html#workernavigator")]
public sealed partial class WorkerNavigator
{
    private static readonly string[] DefaultLanguages = ["en-US", "en"];

    private readonly WorkerGlobalScope _global;
    private ServiceWorkerContainer? _serviceWorker;
    private LockManager? _locks;
    private StorageManager? _storage;

    internal WorkerNavigator(WorkerGlobalScope global)
    {
        _global = global;
    }

    /// <summary>Gets the storage manager of the worker (https://storage.spec.whatwg.org/#dom-navigatorstorage-storage).</summary>
    /// <seealso href="https://storage.spec.whatwg.org/#dom-navigatorstorage-storage"/>
    [IdlMember("storage", "https://storage.spec.whatwg.org/#dom-navigatorstorage-storage")]
    public StorageManager Storage => _storage ??= new StorageManager(_global);

    /// <summary>Gets the lock manager of the worker (https://w3c.github.io/web-locks/#dom-navigatorlocks-locks).</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-navigatorlocks-locks"/>
    [IdlMember("locks", "https://w3c.github.io/web-locks/#dom-navigatorlocks-locks")]
    public LockManager Locks => _locks ??= new LockManager(_global);

    /// <summary>Gets the service workers of the worker (https://w3c.github.io/ServiceWorker/#navigator-service-worker).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-workernavigator-serviceworker"/>
    [IdlMember("serviceWorker", "https://w3c.github.io/ServiceWorker/#dom-workernavigator-serviceworker")]
    public ServiceWorkerContainer ServiceWorker => _serviceWorker ??= _global.ServiceWorkerObjects.Container ?? new ServiceWorkerContainer(_global);

    /// <summary>Gets the legacy application code name, always <c>Mozilla</c>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appcodename"/>
    [IdlMember("appCodeName", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appcodename")]
    public string AppCodeName => "Mozilla";

    /// <summary>Gets the legacy application name, always <c>Netscape</c>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appname"/>
    [IdlMember("appName", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appname")]
    public string AppName => "Netscape";

    /// <summary>Gets the legacy application version, derived from the user agent.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appversion"/>
    [IdlMember("appVersion", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appversion")]
    public string AppVersion => UserAgent.StartsWith("Mozilla/", StringComparison.Ordinal) ? UserAgent["Mozilla/".Length..] : UserAgent;

    /// <summary>Gets the platform of the user agent, an empty string.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-platform"/>
    [IdlMember("platform", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-platform")]
    public string Platform => "";

    /// <summary>Gets the legacy product name, always <c>Gecko</c>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-product"/>
    [IdlMember("product", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-product")]
    public string Product => "Gecko";

    /// <summary>Gets the legacy product sub-version.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-productsub"/>
    [IdlMember("productSub", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-productsub")]
    public string ProductSub => "20100101";

    /// <summary>Gets the user agent string.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-useragent"/>
    [IdlMember("userAgent", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-useragent")]
    public string UserAgent => Navigator.DefaultUserAgent;

    /// <summary>Gets the vendor of the user agent, an empty string.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendor"/>
    [IdlMember("vendor", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendor")]
    public string Vendor => "";

    /// <summary>Gets the legacy vendor sub-version, an empty string.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendorsub"/>
    [IdlMember("vendorSub", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendorsub")]
    public string VendorSub => "";

    /// <summary>Gets the preferred language of the user.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-language"/>
    [IdlMember("language", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-language")]
    public string Language => DefaultLanguages[0];

    /// <summary>Gets the languages preferred by the user, by order of preference.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-languages"/>
    [IdlMember("languages", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-languages")]
    public IReadOnlyList<string> Languages => DefaultLanguages;

    /// <summary>Gets a value indicating whether the user agent is online.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-online"/>
    [IdlMember("onLine", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-online")]
    public bool OnLine => true;

    /// <summary>Gets the number of logical processors available to the user agent.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-navigator-hardwareconcurrency"/>
    [IdlMember("hardwareConcurrency", "https://html.spec.whatwg.org/multipage/workers.html#dom-navigator-hardwareconcurrency")]
    public ulong HardwareConcurrency => (ulong)Environment.ProcessorCount;
}
