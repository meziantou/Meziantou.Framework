using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The global object of a worker (https://html.spec.whatwg.org/multipage/workers.html#the-workerglobalscope-common-interface):
/// a <see cref="DedicatedWorkerGlobalScope"/> or a <see cref="SharedWorkerGlobalScope"/>. A worker runs its own event loop
/// on its own thread, and its scripts run in the realm of its own script engine (<see cref="ScriptEngine.CreateWorkerScriptEngine"/>):
/// the members of a worker global scope must be used on the thread of the worker.
/// </summary>
[IdlInterface("WorkerGlobalScope", "https://html.spec.whatwg.org/multipage/workers.html#workerglobalscope")]
public abstract partial class WorkerGlobalScope : GlobalScope
{
    private WorkerLocation? _location;
    private WorkerNavigator? _navigator;

    private protected WorkerGlobalScope(WorkerAgent agent, UrlRecord url, string name)
        : base(agent.Options, agent.EventLoop)
    {
        Agent = agent;
        Url = url;
        Name = name;
        EnvironmentDocumentCore = Document.CreateWorkerEnvironment(this, url, agent.CreatorDocument);
    }

    internal WorkerAgent Agent { get; }

    /// <summary>
    /// Whether the static imports of the module script of the worker are being fetched
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-worker-script-tree): they are fetched with the
    /// environment of the creator of the worker (see <see cref="Scripting.ModuleFetching"/>).
    /// </summary>
    internal bool IsFetchingModuleScriptGraph { get; set; }

    /// <summary>The URL of the worker (https://html.spec.whatwg.org/multipage/workers.html#concept-workerglobalscope-url): the URL of the response of its script.</summary>
    internal UrlRecord Url { get; }

    private Document EnvironmentDocumentCore { get; }

    /// <summary>Gets the name of the worker (https://html.spec.whatwg.org/multipage/workers.html#concept-workerglobalscope-name).</summary>
    [NotInIdl("The name of the worker, exposed by DedicatedWorkerGlobalScope.name and SharedWorkerGlobalScope.name")]
    public string Name { get; }

    internal override ScriptEngine? ScriptEngine => Agent.ScriptEngine;

    internal override UrlRecord ApiBaseUrl => Url;

    internal override string ScriptUrl => Url.Serialize();

    internal override OriginRecord OriginRecord => EnvironmentDocumentCore.Origin;

    internal override bool CanRunScript => !Agent.IsClosing;

    internal override bool IsScriptingEnabled => true;

    internal override Document EnvironmentDocument => EnvironmentDocumentCore;

    /// <summary>Queues a task on the event loop of the worker: the tasks queued once the worker is closing are discarded.</summary>
    internal override void QueueGlobalTask(Action task)
    {
        if (Agent.IsClosing)
            return;

        EventLoop.QueueTask(() =>
        {
            if (!Agent.IsClosing)
            {
                task();
            }
        });
    }

    /// <summary>Applies the policies of the response of the script of the worker (its referrer policy, its CSP list and its embedder policies).</summary>
    internal void ApplyResponsePolicies(FetchedWorkerScript script)
    {
        if (ReferrerPolicies.ParseHeader(script.Headers) is { Length: > 0 } referrerPolicy)
        {
            EnvironmentDocumentCore.ReferrerPolicy = referrerPolicy;
        }

        // https://html.spec.whatwg.org/multipage/workers.html#run-a-worker: the reporting endpoints of the response
        InitializeReportingEndpoints(script.Headers, Url);

        // https://html.spec.whatwg.org/multipage/workers.html#initialize-worker-policy-container: the CSP list and the embedder
        // policy of the response (a worker whose URL is a local scheme has the policies of its creator, see
        // Document.CreateWorkerEnvironment)
        if (Url.Scheme is "data" or "blob" or "about")
            return;

        EnvironmentDocumentCore.EmbedderPolicy = EmbedderPolicy.Obtain(script.Headers, IsSecureContext);

        // A dedicated worker has the document isolation policy of its creator (https://github.com/explainers-by-googlers/document-isolation-policy)
        if (Agent.Kind != WorkerKind.Dedicated)
        {
            EnvironmentDocumentCore.DocumentIsolationPolicy = BrowsingContext.GetDocumentIsolationPolicy(script.Headers, IsSecureContext);
        }

        EnvironmentDocumentCore.AddContentSecurityPolicies(script.Headers);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-self</summary>
    [IdlMember("self", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-self")]
    public WorkerGlobalScope Self => this;

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-location</summary>
    [IdlMember("location", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-location")]
    public WorkerLocation Location => _location ??= new WorkerLocation(this);

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#dom-worker-navigator</summary>
    [IdlMember("navigator", "https://html.spec.whatwg.org/multipage/workers.html#dom-worker-navigator")]
    public WorkerNavigator Navigator => _navigator ??= new WorkerNavigator(this);

    /// <summary>
    /// The document that holds the font faces of the worker and computes the fonts of its canvases (see
    /// <see cref="Document.FontSourceWorker"/>): the font matching uses the font faces of <see cref="Fonts"/>, then the
    /// bundled fonts.
    /// </summary>
    internal Document FontSourceDocument => field ??= new Document { FontSourceWorker = this };

    /// <summary>
    /// The font source of the worker (https://drafts.csswg.org/css-font-loading/#font-source): the font faces created by
    /// the scripts of the worker and added to the set, which the text of its <see cref="OffscreenCanvas"/> objects uses
    /// once they are loaded. A worker has no CSS-connected font face.
    /// </summary>
    [IdlMember("fonts", "https://drafts.csswg.org/css-font-loading/#dom-fontfacesource-fonts")]
    public Css.Om.FontFaceSet Fonts => FontSourceDocument.Fonts;

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin</summary>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin")]
    public override string Origin => OriginRecord.Serialize();

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext: the creator of the worker is a secure
    /// context, and the URL of the worker is potentially trustworthy (or a data: or blob: URL).
    /// </summary>
    [IdlMember("isSecureContext", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext")]
    public override bool IsSecureContext => Agent.CreatorIsSecureContext && (Url.Scheme is "data" or "blob" || ReferrerPolicies.IsPotentiallyTrustworthy(Url));

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated: a dedicated worker has the cross-origin
    /// isolated capability of its owner (except a worker created from a data: URL); a shared worker is isolated when its
    /// embedder policy is compatible with cross-origin isolation (https://html.spec.whatwg.org/multipage/workers.html#run-a-worker).
    /// </summary>
    [IdlMember("crossOriginIsolated", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated")]
    public override bool CrossOriginIsolated => Agent.Kind != WorkerKind.Dedicated
        ? EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(EnvironmentDocument.EmbedderPolicy.Value)
        : Agent.CreatorCrossOriginIsolated && Url.Scheme != "data";

    // A dedicated worker is in the agent cluster of its owner, a shared or service worker in its own agent cluster
    // (https://html.spec.whatwg.org/multipage/webappapis.html#obtain-a-worker-agent)
    internal override object? AgentClusterKey => Agent.Kind == WorkerKind.Dedicated ? Agent.CreatorAgentClusterKey : Agent;

    internal override bool IsAgentClusterCrossOriginIsolated => Agent.Kind == WorkerKind.Dedicated ? Agent.CreatorAgentClusterCrossOriginIsolated : CrossOriginIsolated;

    /// <summary>
    /// Runs the scripts of <paramref name="urls"/> synchronously, in order, in the realm of the worker
    /// (https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-importscripts). The exceptions of the
    /// scripts are rethrown; a script that cannot be fetched throws a <c>NetworkError</c>.
    /// </summary>
    /// <param name="urls">The URLs of the scripts, relative to the URL of the worker.</param>
    /// <exception cref="TypeErrorException">The worker is a module worker.</exception>
    /// <exception cref="DomException">A <c>SyntaxError</c> when a URL is invalid, or a <c>NetworkError</c> when a script cannot be fetched.</exception>
    [NotInIdl("The .NET overload of importScripts() whose URLs are strings")]
    public void ImportScripts(params string[] urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ImportScripts((IEnumerable<object>)urls);
    }

    /// <summary>
    /// Runs the scripts of <paramref name="urls"/> synchronously, in order, in the realm of the worker
    /// (https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-importscripts).
    /// </summary>
    /// <param name="urls">The URLs of the scripts (strings or <see cref="TrustedScriptUrl"/> objects), relative to the URL of the worker.</param>
    [IdlMember("importScripts", "https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-importscripts")]
    public void ImportScripts(IEnumerable<object> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);

        // https://html.spec.whatwg.org/multipage/workers.html#import-scripts-into-worker-global-scope
        if (Agent.Type == WorkerType.Module)
            throw new TypeErrorException("importScripts() cannot be used in a module worker");

        // The blob: URLs are resolved when the URLs are parsed: a script can revoke the URL of a next script
        var parsedUrls = new List<(UrlRecord Url, Dom.FileApi.BlobUrlEntry? BlobUrlEntry)>();
        foreach (var item in urls)
        {
            // https://html.spec.whatwg.org/multipage/workers.html#dom-workerglobalscope-importscripts: each URL is checked by
            // Trusted Types before the first script is fetched
            var url = item is TrustedScriptUrl trustedScriptUrl
                ? TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScriptUrl, EnvironmentDocument, trustedScriptUrl, "WorkerGlobalScope importScripts")
                : TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScriptUrl, EnvironmentDocument, item as string ?? item?.ToString() ?? "", "WorkerGlobalScope importScripts");
            var parsedUrl = UrlParser.Parse(url, ApiBaseUrl) ?? throw new DomException($"The URL '{url}' is invalid", "SyntaxError");
            parsedUrls.Add((parsedUrl, parsedUrl.Scheme == "blob" ? Dom.FileApi.BlobUrlStore.Get(EnvironmentDocument).Resolve(parsedUrl) : null));
        }

        foreach (var (url, blobUrlEntry) in parsedUrls)
        {
            var script = FetchImportedScript(url, blobUrlEntry, out var mutedErrors)
                ?? throw new DomException($"The script '{url.Serialize()}' could not be loaded", "NetworkError");
            if (ScriptEngine is not { } engine)
                continue;

            // Run the classic script with "rethrow errors": the exceptions propagate to the caller (a muted error is a NetworkError)
            var classicScript = new ClassicScript(this, script.SourceText, script.Url.Serialize()) { Url = script.Url.Serialize(), MutedErrors = mutedErrors };
            try
            {
                engine.ExecuteClassicScript(classicScript);
            }
            catch (Exception) when (mutedErrors && !Agent.IsTerminated)
            {
                throw new DomException("The imported script threw an exception", "NetworkError");
            }
        }
    }

    /// <summary>Fetches a script of <c>importScripts()</c> (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-worker-imported-script), or returns null when it cannot be fetched.</summary>
    private protected virtual FetchedWorkerScript? FetchImportedScript(UrlRecord url, Dom.FileApi.BlobUrlEntry? blobUrlEntry, out bool mutedErrors)
        => WorkerScriptFetching.FetchImportedScript(this, url, blobUrlEntry, out mutedErrors);

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    [IdlMember("onerror", "https://html.spec.whatwg.org/multipage/workers.html#handler-workerglobalscope-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Gets or sets the event handler of the <c>languagechange</c> event.</summary>
    [IdlMember("onlanguagechange", "https://html.spec.whatwg.org/multipage/workers.html#handler-workerglobalscope-onlanguagechange")]
    public Action<Event>? OnLanguageChange
    {
        get => GetEventHandler("languagechange");
        set => SetEventHandler("languagechange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>offline</c> event.</summary>
    [IdlMember("onoffline", "https://html.spec.whatwg.org/multipage/workers.html#handler-workerglobalscope-onoffline")]
    public Action<Event>? OnOffline
    {
        get => GetEventHandler("offline");
        set => SetEventHandler("offline", value);
    }

    /// <summary>Gets or sets the event handler of the <c>online</c> event.</summary>
    [IdlMember("ononline", "https://html.spec.whatwg.org/multipage/workers.html#handler-workerglobalscope-ononline")]
    public Action<Event>? OnOnline
    {
        get => GetEventHandler("online");
        set => SetEventHandler("online", value);
    }

    /// <summary>Gets or sets the event handler of the <c>rejectionhandled</c> event.</summary>
    [IdlMember("onrejectionhandled", "https://html.spec.whatwg.org/multipage/workers.html#handler-workerglobalscope-onrejectionhandled")]
    public Action<Event>? OnRejectionHandled
    {
        get => GetEventHandler("rejectionhandled");
        set => SetEventHandler("rejectionhandled", value);
    }

    /// <summary>Gets or sets the event handler of the <c>unhandledrejection</c> event.</summary>
    [IdlMember("onunhandledrejection", "https://html.spec.whatwg.org/multipage/workers.html#handler-workerglobalscope-onunhandledrejection")]
    public Action<Event>? OnUnhandledRejection
    {
        get => GetEventHandler("unhandledrejection");
        set => SetEventHandler("unhandledrejection", value);
    }

    internal override GlobalScope? RelevantGlobal => this;
}

/// <summary>
/// The global object of a dedicated worker (https://html.spec.whatwg.org/multipage/workers.html#dedicated-workers-and-the-dedicatedworkerglobalscope-interface):
/// the messages it posts are fired at its <see cref="Worker"/> object, and the errors it does not handle are propagated to it.
/// </summary>
[IdlInterface("DedicatedWorkerGlobalScope", "https://html.spec.whatwg.org/multipage/workers.html#dedicatedworkerglobalscope")]
public sealed class DedicatedWorkerGlobalScope : WorkerGlobalScope
{
    private readonly SortedDictionary<uint, FrameRequestCallback> _animationFrameCallbacks = [];
    private uint _animationFrameCallbackIdentifier;
    private bool _animationFrameScheduled;

    internal DedicatedWorkerGlobalScope(WorkerAgent agent, UrlRecord url, string name)
        : base(agent, url, name)
    {
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-animationframeprovider-requestanimationframe:
    /// runs the callback at the next frame of the worker. There is no rendering: the frames run at a fixed rate (60 frames
    /// per second) measured with <see cref="DomEventLoop.TimeProvider"/>.
    /// </summary>
    /// <exception cref="DomException">A <c>NotSupportedError</c> when the owner of the worker is not a window (a nested worker).</exception>
    [IdlMember("requestAnimationFrame", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-animationframeprovider-requestanimationframe")]
    public uint RequestAnimationFrame(FrameRequestCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        // https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animationframeprovider-supported
        if (Agent.Owner is not Window)
            throw new DomException("requestAnimationFrame() is not supported in a worker created by a worker", "NotSupportedError");

        _animationFrameCallbackIdentifier++;
        _animationFrameCallbacks[_animationFrameCallbackIdentifier] = callback;
        if (!_animationFrameScheduled)
        {
            // The frames are aligned on a fixed rate from the time origin of the worker
            _animationFrameScheduled = true;
            var frame = DomEventLoop.FrameDuration.TotalMilliseconds;
            var now = GetCurrentHighResolutionTime();
            var next = (Math.Floor(now / frame) + 1) * frame;
            EventLoop.QueueTimer(TimeSpan.FromMilliseconds(next - now), () => QueueGlobalTask(RunAnimationFrameCallbacks));
        }

        return _animationFrameCallbackIdentifier;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animationframeprovider-cancelanimationframe</summary>
    [IdlMember("cancelAnimationFrame", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animationframeprovider-cancelanimationframe")]
    public void CancelAnimationFrame(uint handle)
    {
        if (Agent.Owner is not Window)
            throw new DomException("cancelAnimationFrame() is not supported in a worker created by a worker", "NotSupportedError");

        _animationFrameCallbacks.Remove(handle);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#run-the-animation-frame-callbacks</summary>
    private void RunAnimationFrameCallbacks()
    {
        _animationFrameScheduled = false;
        var now = GetCurrentHighResolutionTime();
        foreach (var handle in _animationFrameCallbacks.Keys.ToArray())
        {
            if (!_animationFrameCallbacks.Remove(handle, out var callback))
                continue;

            try
            {
                callback(now);
            }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ScriptErrors.Report(ex, this);
            }
        }
    }

    internal override ExposureSet Exposure => ExposureSet.DedicatedWorker;

    internal override string InterfaceName => "DedicatedWorkerGlobalScope";

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-name</summary>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-name")]
    public string WorkerName => Name;

    /// <summary>Posts a message to the <see cref="Worker"/> object (https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-postmessage).</summary>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-postmessage")]
    public void PostMessage(object? message, IReadOnlyList<object> transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        PostMessage(message, new StructuredSerializeOptions { Transfer = transfer });
    }

    /// <summary>Posts a message to the <see cref="Worker"/> object (https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-postmessage-options).</summary>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-postmessage-options")]
    public void PostMessage(object? message, StructuredSerializeOptions? options = null) => Agent.InsidePort?.PostMessageCore(message, options?.Transfer ?? [], source: null);

    /// <summary>Closes the worker: its event loop ends once the running task completes (https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-close).</summary>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/workers.html#dom-dedicatedworkerglobalscope-close")]
    public void Close() => Agent.Close();

    /// <summary>Gets or sets the event handler of the <c>message</c> event.</summary>
    [IdlMember("onmessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageeventtarget-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>Gets or sets the event handler of the <c>messageerror</c> event.</summary>
    [IdlMember("onmessageerror", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageeventtarget-onmessageerror")]
    public Action<Event>? OnMessageError
    {
        get => GetEventHandler("messageerror");
        set => SetEventHandler("messageerror", value);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/workers.html#runtime-script-errors-2: an error that is not handled by the
    /// worker is fired at its <see cref="Worker"/> object, on the thread of its owner, and then reported to the owner when
    /// the event is not canceled.
    /// </summary>
    private protected override void OnErrorNotHandled(ScriptError error)
    {
        if (Agent.WorkerObject is not { } worker)
        {
            base.OnErrorNotHandled(error);
            return;
        }

        // The error value belongs to the realm of the worker: it is not given to the owner
        var message = error.Message;
        var filename = error.Filename;
        var lineNumber = error.LineNumber;
        var columnNumber = error.ColumnNumber;
        var owner = Agent.Owner!;
        owner.QueueGlobalTask(() =>
        {
            var errorEvent = new ErrorEvent("error", new ErrorEventInit
            {
                Cancelable = true,
                Message = message,
                Filename = filename,
                Lineno = (uint)Math.Max(0, lineNumber),
                Colno = (uint)Math.Max(0, columnNumber),
                Error = NullValue.Instance,
            });
            if (worker.FireEvent(errorEvent))
            {
                owner.ReportException(new ScriptError(message, null) { Filename = filename, LineNumber = lineNumber, ColumnNumber = columnNumber });
            }
        });
    }
}

/// <summary>
/// The global object of a shared worker (https://html.spec.whatwg.org/multipage/workers.html#shared-workers-and-the-sharedworkerglobalscope-interface):
/// each <see cref="SharedWorker"/> object connected to the worker fires a <c>connect</c> event, whose port is entangled with
/// the <see cref="SharedWorker.Port"/> of the object.
/// </summary>
[IdlInterface("SharedWorkerGlobalScope", "https://html.spec.whatwg.org/multipage/workers.html#sharedworkerglobalscope")]
public sealed class SharedWorkerGlobalScope : WorkerGlobalScope
{
    internal SharedWorkerGlobalScope(WorkerAgent agent, UrlRecord url, string name)
        : base(agent, url, name)
    {
    }

    internal override ExposureSet Exposure => ExposureSet.SharedWorker;

    internal override string InterfaceName => "SharedWorkerGlobalScope";

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworkerglobalscope-name</summary>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworkerglobalscope-name")]
    public string WorkerName => Name;

    /// <summary>Closes the worker (https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworkerglobalscope-close).</summary>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/workers.html#dom-sharedworkerglobalscope-close")]
    public void Close() => Agent.Close();

    /// <summary>Gets or sets the event handler of the <c>connect</c> event.</summary>
    [IdlMember("onconnect", "https://html.spec.whatwg.org/multipage/workers.html#handler-sharedworkerglobalscope-onconnect")]
    public Action<Event>? OnConnect
    {
        get => GetEventHandler("connect");
        set => SetEventHandler("connect", value);
    }
}
