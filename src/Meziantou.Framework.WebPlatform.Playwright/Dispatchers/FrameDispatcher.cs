using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>
/// A frame of a page: a browsing context (the main frame is the top-level browsing context of the page). It tracks the
/// lifecycle of its documents (the <c>loadstate</c> and <c>navigated</c> events), and runs the evaluations, the queries and the
/// actions of the clients in the execution context of its document. Its state is used on the thread of the browser context.
/// </summary>
internal sealed partial class FrameDispatcher : Dispatcher
{
    private static readonly string[] AllLifecycleEvents = ["commit", "domcontentloaded", "load", "networkidle"];

    private readonly HashSet<string> _loadStates;
    private readonly List<(string State, TaskCompletionSource Completion)> _loadStateWaiters = [];
    private readonly List<TaskCompletionSource<NavigationEvent>> _navigationWaiters = [];
    private readonly TaskCompletionSource _detached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FrameExecutionContext? _executionContext;
    private NetworkRequest? _pendingNavigationRequest;
    private int _inflightRequests;
    private int _networkIdleGeneration;

    public FrameDispatcher(Dispatcher parentScope, BrowsingContext browsingContext, FrameDispatcher? parentFrame)
        : base(parentScope, "Frame", "frame", CreateInitializer(browsingContext, parentFrame, GetInitialLoadStates(browsingContext)))
    {
        BrowsingContext = browsingContext;
        ParentFrame = parentFrame;
        Url = browsingContext.Document.Url;
        Name = browsingContext.Name;
        _loadStates = [.. GetInitialLoadStates(browsingContext)];
        if (browsingContext.Document.ReadyState != DocumentReadyState.Complete)
        {
            ListenToLifecycleEvents(browsingContext.Document);
        }
    }

    private static string[] GetInitialLoadStates(BrowsingContext browsingContext)
        => browsingContext.Document.ReadyState == DocumentReadyState.Complete ? AllLifecycleEvents : ["commit"];

    private static JsonObject CreateInitializer(BrowsingContext browsingContext, FrameDispatcher? parentFrame, string[] loadStates)
    {
        var initializer = new JsonObject
        {
            ["url"] = browsingContext.Document.Url,
            ["name"] = browsingContext.Name,
            ["loadStates"] = new JsonArray([.. loadStates.Select(state => (JsonNode)state)]),
        };
        if (parentFrame is not null)
        {
            initializer["parentFrame"] = parentFrame.ToReference();
        }

        return initializer;
    }

    public PageDispatcher Page { get; internal set; } = null!;

    public BrowserContextDispatcher Context => Page.Context;

    public BrowsingContext BrowsingContext { get; }

    public FrameDispatcher? ParentFrame { get; }

    public string Url { get; private set; }

    public string Name { get; private set; }

    public bool IsDetached => _detached.Task.IsCompleted;

    /// <summary>Gets the request of the navigation of the current document, or null.</summary>
    public NetworkRequest? CurrentDocumentRequest { get; private set; }

    /// <summary>Gets the referer of the next navigation of the frame (<c>goto({ referer })</c>), consumed by its request.</summary>
    public string? PendingReferer { get; internal set; }

    /// <summary>Returns the execution context of the current document (created when needed).</summary>
    public FrameExecutionContext GetExecutionContext()
    {
        if (IsDetached || BrowsingContext.IsDestroyed)
            throw new PlaywrightError("Frame was detached");

        var document = BrowsingContext.Document;
        if (_executionContext is null || !ReferenceEquals(_executionContext.Document, document))
        {
            _executionContext?.Destroy();
            _executionContext = new FrameExecutionContext(document, Context.ScriptEngine, CreateInjectedScriptOptions);
        }

        return _executionContext;
    }

    private JsonObject CreateInjectedScriptOptions()
    {
        var customEngines = new JsonArray();
        foreach (var (name, source) in Context.Options.SelectorEngines)
        {
            customEngines.Add((JsonNode)new JsonObject { ["name"] = name, ["source"] = "(" + source + ")" });
        }

        return new JsonObject
        {
            ["isUnderTest"] = false,
            ["sdkLanguage"] = Context.Browser.SdkLanguage,
            ["frameSeq"] = 0,
            ["testIdAttributeName"] = Context.Options.TestIdAttributeName,
            ["stableRafCount"] = 1,
            ["browserName"] = Context.Browser.BrowserName,
            ["shouldPrependErrorPrefix"] = false,
            ["isUtilityWorld"] = false,
            ["customEngines"] = customEngines,
        };
    }

    /// <summary>Creates a handle to a value of an execution context of the frame: an element handle for a node.</summary>
    public JSHandleDispatcher CreateHandle(FrameExecutionContext context, JsValue value)
    {
        if (value.IsObject && context.FromJsValue(value) is Node)
        {
            var preview = "JSHandle@" + context.CallInjected("(injected, node) => injected.previewNode(node)", value).AsString();
            return new ElementHandleDispatcher(this, context, value, preview);
        }

        return new JSHandleDispatcher(this, context, value, JSHandleDispatcher.GetPreview(context, value));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Lifecycle
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A new document became the active document of the frame (the end of a navigation).</summary>
    internal void OnDocumentActivated(Document document)
    {
        _executionContext?.Destroy();
        _executionContext = null;
        Page.RemoveDestroyedFrames();

        Url = document.Url;
        Name = BrowsingContext.Name;
        CurrentDocumentRequest = _pendingNavigationRequest;
        _pendingNavigationRequest = null;

        // The lifecycle events of the previous document are removed, then the new document is committed
        foreach (var state in _loadStates.ToArray())
        {
            SendEvent("loadstate", new JsonObject { ["remove"] = state });
        }

        _loadStates.Clear();
        _networkIdleGeneration++;
        AddLifecycleEvent("commit");

        var newDocument = new JsonObject();
        if (CurrentDocumentRequest is { } request)
        {
            newDocument["request"] = request.GetDispatcher().ToReference();
        }

        SendEvent("navigated", new JsonObject { ["url"] = Url, ["name"] = Name, ["newDocument"] = newDocument });
        if (ParentFrame is null)
        {
            Context.ReportPage(Page);
        }

        string? error = null;
        if (document.IsNetworkErrorDocument)
        {
            error = CurrentDocumentRequest?.FailureText ?? "net::ERR_FAILED";
        }

        CompleteNavigationWaiters(new NavigationEvent(Url, IsNewDocument: true, CurrentDocumentRequest, error));
        ListenToLifecycleEvents(document);

        // A document restored from the back/forward cache is already loaded: its load event is not fired again
        if (document.ReadyState == DocumentReadyState.Complete)
        {
            AddLifecycleEvent("load");
        }

        if (_inflightRequests == 0)
        {
            StartNetworkIdleTimer();
        }
    }

    /// <summary>The URL of the document changed without a new document (a fragment navigation, <c>history.pushState()</c>).</summary>
    internal void OnSameDocumentNavigated()
    {
        Url = BrowsingContext.Document.Url;
        SendEvent("navigated", new JsonObject { ["url"] = Url, ["name"] = Name });
        CompleteNavigationWaiters(new NavigationEvent(Url, IsNewDocument: false, Request: null, Error: null));
    }

    /// <summary>A navigation of the frame ended without a new document (e.g. a 204 response, or a request aborted by a route).</summary>
    internal void OnNavigationAborted(NetworkRequest request, string errorText)
    {
        if (!ReferenceEquals(_pendingNavigationRequest, request))
            return;

        _pendingNavigationRequest = null;
        SendEvent("navigated", new JsonObject { ["url"] = Url, ["name"] = Name, ["newDocument"] = new JsonObject { ["request"] = request.GetDispatcher().ToReference() }, ["error"] = errorText });
        CompleteNavigationWaiters(new NavigationEvent(Url, IsNewDocument: true, request, errorText));
    }

    internal void OnDetached()
    {
        _executionContext?.Destroy();
        _detached.TrySetResult();
        foreach (var waiter in _navigationWaiters.ToArray())
        {
            waiter.TrySetException(Page.IsClosed ? PlaywrightError.TargetClosed(Page.CloseReason) : new PlaywrightError("Frame was detached"));
        }

        foreach (var (_, completion) in _loadStateWaiters.ToArray())
        {
            completion.TrySetException(new PlaywrightError("Frame was detached"));
        }
    }

    private void ListenToLifecycleEvents(Document document)
    {
        document.AddEventListener("DOMContentLoaded", _ =>
        {
            if (ReferenceEquals(BrowsingContext.Document, document))
            {
                AddLifecycleEvent("domcontentloaded");
            }
        });
        if (document.DefaultView is { } window)
        {
            window.AddEventListener("load", e =>
            {
                if (ReferenceEquals(e.Target, document) && ReferenceEquals(BrowsingContext.Document, document))
                {
                    AddLifecycleEvent("load");
                    if (_inflightRequests == 0)
                    {
                        StartNetworkIdleTimer();
                    }
                }
            });
        }

        if (document.ReadyState != DocumentReadyState.Loading)
        {
            AddLifecycleEvent("domcontentloaded");
        }
    }

    private void AddLifecycleEvent(string state)
    {
        if (!_loadStates.Add(state))
            return;

        SendEvent("loadstate", new JsonObject { ["add"] = state });
        foreach (var waiter in _loadStateWaiters.ToArray())
        {
            if (waiter.State == state)
            {
                _loadStateWaiters.Remove(waiter);
                waiter.Completion.TrySetResult();
            }
        }

        if (state == "load")
        {
            Page.MainFrame.RecalculateNetworkIdle();
        }
    }

    public bool HasLoadState(string state) => _loadStates.Contains(state);

    /// <summary>Waits until the frame reaches a lifecycle state (<c>waitForLoadState</c>).</summary>
    public async Task WaitForLoadStateAsync(string state, Progress progress)
    {
        if (state is not ("load" or "domcontentloaded" or "networkidle" or "commit"))
            throw new PlaywrightError("waitUntil: expected one of (load|domcontentloaded|networkidle|commit)");

        if (_loadStates.Contains(state))
            return;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _loadStateWaiters.Add((state, completion));
        try
        {
            await progress.RaceAsync(completion.Task).ConfigureAwait(true);
        }
        finally
        {
            _loadStateWaiters.RemoveAll(waiter => ReferenceEquals(waiter.Completion, completion));
        }
    }

    // The network idle state: no request of the frame and of its child frames for 500 ms after the load event

    internal void OnRequestStarted(NetworkRequest request)
    {
        _inflightRequests++;
        _networkIdleGeneration++;
        if (request.IsNavigationRequest)
        {
            _pendingNavigationRequest = request;
            Page.OnNavigationRequested(this);
        }

        if (_loadStates.Contains("networkidle"))
        {
            _loadStates.Remove("networkidle");
            SendEvent("loadstate", new JsonObject { ["remove"] = "networkidle" });
        }
    }

    internal void OnRequestFinished()
    {
        _inflightRequests = Math.Max(0, _inflightRequests - 1);
        if (_inflightRequests == 0)
        {
            StartNetworkIdleTimer();
        }
    }

    private void StartNetworkIdleTimer()
    {
        var generation = ++_networkIdleGeneration;
        _ = StartNetworkIdleTimerAsync(generation);
    }

    private async Task StartNetworkIdleTimerAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(true);
        if (generation != _networkIdleGeneration || IsDetached || _inflightRequests > 0 || !_loadStates.Contains("load"))
            return;

        IsNetworkIdleSelf = true;
        Page.MainFrame.RecalculateNetworkIdle();
    }

    private bool IsNetworkIdleSelf { get; set; }

    private void RecalculateNetworkIdle()
    {
        var isIdle = IsNetworkIdleSelf && _inflightRequests == 0 && _loadStates.Contains("load");
        foreach (var child in Page.GetFrames().Where(frame => ReferenceEquals(frame.ParentFrame, this)))
        {
            child.RecalculateNetworkIdle();
            if (!child.HasLoadState("networkidle"))
            {
                isIdle = false;
            }
        }

        if (isIdle)
        {
            AddLifecycleEvent("networkidle");
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Navigation
    // ----------------------------------------------------------------------------------------------------------------

    private TaskCompletionSource<NavigationEvent> AddNavigationWaiter()
    {
        var completion = new TaskCompletionSource<NavigationEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationWaiters.Add(completion);
        return completion;
    }

    private void CompleteNavigationWaiters(NavigationEvent navigation)
    {
        foreach (var waiter in _navigationWaiters.ToArray())
        {
            _navigationWaiters.Remove(waiter);
            waiter.TrySetResult(navigation);
        }
    }

    private async Task<JsonObject> WaitForNavigationResultAsync(TaskCompletionSource<NavigationEvent> waiter, string url, string waitUntil, Progress progress)
    {
        NavigationEvent navigation;
        try
        {
            navigation = await progress.RaceAsync(waiter.Task).ConfigureAwait(true);
        }
        finally
        {
            _navigationWaiters.Remove(waiter);
        }

        if (navigation.Error is not null)
            throw new PlaywrightError(navigation.Error + " at " + url);

        await WaitForLoadStateAsync(waitUntil, progress).ConfigureAwait(true);
        var result = new JsonObject();
        if (navigation.Request is { } request && await request.GetFinalRequest().GetResponseAsync(progress).ConfigureAwait(true) is { } response)
        {
            result["response"] = response.GetDispatcher().ToReference();
        }

        return result;
    }

    /// <summary>Navigates the frame (<c>goto</c>), and waits for the lifecycle event.</summary>
    public async Task<JsonObject> GotoAsync(string url, string waitUntil, string? referer, Progress progress)
    {
        url = ResolveUrl(url);
        progress.AddLog($"navigating to \"{url}\", waiting until \"{waitUntil}\"");
        var waiter = AddNavigationWaiter();
        try
        {
            PendingReferer = referer;
            BrowsingContext.Navigate(url);
        }
        catch (ArgumentException ex)
        {
            _navigationWaiters.Remove(waiter);
            throw new PlaywrightError("Cannot navigate to invalid URL", ex);
        }

        return await WaitForNavigationResultAsync(waiter, url, waitUntil, progress).ConfigureAwait(true);
    }

    /// <summary>Reloads the document of the frame (<c>page.reload()</c>).</summary>
    public async Task<JsonObject> ReloadAsync(string waitUntil, Progress progress)
    {
        var waiter = AddNavigationWaiter();
        BrowsingContext.Reload();
        return await WaitForNavigationResultAsync(waiter, Url, waitUntil, progress).ConfigureAwait(true);
    }

    /// <summary>Traverses the session history (<c>page.goBack()</c>, <c>page.goForward()</c>): null when there is no entry.</summary>
    public async Task<JsonObject> TraverseHistoryAsync(int delta, string waitUntil, Progress progress)
    {
        var history = BrowsingContext.Window.History;
        var waiter = AddNavigationWaiter();
        var urlBefore = BrowsingContext.Document.Url;
        var documentBefore = BrowsingContext.Document;
        if (delta < 0)
        {
            history.Back();
        }
        else
        {
            history.Forward();
        }

        // The traversal runs from a task: there is no entry when nothing happens
        var timeout = Task.Delay(TimeSpan.FromMilliseconds(250));
        var winner = await Task.WhenAny(waiter.Task, timeout).ConfigureAwait(true);
        if (winner != waiter.Task && ReferenceEquals(documentBefore, BrowsingContext.Document) && urlBefore == BrowsingContext.Document.Url && _pendingNavigationRequest is null)
        {
            _navigationWaiters.Remove(waiter);
            return [];
        }

        return await WaitForNavigationResultAsync(waiter, Url, waitUntil, progress).ConfigureAwait(true);
    }

    private string ResolveUrl(string url)
    {
        if (Context.Options.BaseUrl is { } baseUrl && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var resolved))
            return resolved.ToString();

        // A host without scheme (e.g. "example.com") is an http URL, as the address bar of a browser
        if (!url.Contains(':', StringComparison.Ordinal) && !url.StartsWith("/", StringComparison.Ordinal))
            return "http://" + url;

        return url;
    }

    /// <summary>Sets the content of the frame (<c>setContent</c>): the document is written with <c>document.open()</c>.</summary>
    public async Task SetContentAsync(string html, string waitUntil, Progress progress)
    {
        progress.AddLog($"setting frame content, waiting until \"{waitUntil}\"");
        var context = GetExecutionContext();
        var document = context.Document;

        // document.open() starts a new lifecycle: the load states of the document are cleared
        foreach (var state in _loadStates.ToArray())
        {
            SendEvent("loadstate", new JsonObject { ["remove"] = state });
        }

        _loadStates.Clear();
        _networkIdleGeneration++;
        AddLifecycleEvent("commit");
        try
        {
            context.Call("(html) => { document.open(); document.write(html); document.close(); }", html);
        }
        catch (JsException ex)
        {
            throw context.ToPlaywrightError(ex);
        }

        ListenToLifecycleEvents(document);
        if (_inflightRequests == 0)
        {
            StartNetworkIdleTimer();
        }

        await WaitForLoadStateAsync(waitUntil, progress).ConfigureAwait(true);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Bindings and init scripts
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Defines a binding (<c>exposeBinding</c>) in the current document of the frame.</summary>
    internal void InstallBinding(BindingRegistration binding)
    {
        if (IsDetached || BrowsingContext.IsDestroyed || BrowsingContext.Document.DefaultView is null)
            return;

        try
        {
            Context.ScriptEngine.InstallBinding(this, BrowsingContext.Document, binding);
        }
        catch (JsException)
        {
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Protocol
    // ----------------------------------------------------------------------------------------------------------------

    public override async ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        var engine = Context.Engine;
        switch (method)
        {
            case "goto":
                return await engine.InvokeAsync(() => GotoAsync(parameters.GetRequiredString("url"), parameters.GetString("waitUntil") ?? "load", parameters.GetString("referer"), progress)).ConfigureAwait(false);

            case "setContent":
                await engine.InvokeAsync(() => SetContentAsync(parameters.GetString("html") ?? "", parameters.GetString("waitUntil") ?? "load", progress)).ConfigureAwait(false);
                return null;

            case "content":
                return await engine.InvokeAsync(() => new JsonObject
                {
                    ["value"] = GetExecutionContext().CallInjected("(injected, includeShadow) => injected.documentContent(includeShadow)", parameters.GetBoolean("includeShadow", defaultValue: false)).AsString(),
                }).ConfigureAwait(false);

            case "title":
                return await engine.InvokeAsync(() => new JsonObject { ["value"] = BrowsingContext.Document.Title }).ConfigureAwait(false);

            case "evaluateExpression":
                return await engine.InvokeAsync(async () =>
                {
                    var context = GetExecutionContext();
                    return new JsonObject { ["value"] = await Evaluation.EvaluateAsync(context, parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"), GetArgument(parameters), Connection, progress).ConfigureAwait(true) };
                }).ConfigureAwait(false);

            case "evaluateExpressionHandle":
                return await engine.InvokeAsync(async () =>
                {
                    var context = GetExecutionContext();
                    var value = await Evaluation.EvaluateHandleAsync(context, parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"), GetArgument(parameters), Connection, progress).ConfigureAwait(true);
                    return new JsonObject { ["handle"] = CreateHandle(context, value).ToReference() };
                }).ConfigureAwait(false);

            case "waitForTimeout":
                await progress.WaitAsync(TimeSpan.FromMilliseconds(parameters.GetDouble("waitTimeout") ?? 0)).ConfigureAwait(false);
                return null;

            case "frameElement":
                return await engine.InvokeAsync(() =>
                {
                    if (ParentFrame is null || BrowsingContext.Container is not { } container)
                        throw new PlaywrightError("Frame has been detached.");

                    var context = ParentFrame.GetExecutionContext();
                    return new JsonObject { ["element"] = ParentFrame.CreateHandle(context, context.ToJsValue(container)).ToReference() };
                }).ConfigureAwait(false);

            default:
                return await InvokeSelectorMethodAsync(method, parameters, progress).ConfigureAwait(false);
        }
    }

    private static JsonElement GetArgument(JsonElement parameters) => parameters.TryGet("arg", out var argument) ? argument : default;

    protected override void OnDisposed()
    {
        _detached.TrySetResult();
    }
}

/// <summary>A navigation of a frame: a new document (with its request, or an error), or a same-document navigation.</summary>
internal sealed record NavigationEvent(string Url, bool IsNewDocument, NetworkRequest? Request, string? Error);
