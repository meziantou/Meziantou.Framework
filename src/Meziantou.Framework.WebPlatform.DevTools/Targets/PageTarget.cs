using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.DevTools.Browser;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Runtime;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>
/// A page target: a top-level browsing context (a page created by <c>Target.createTarget</c>, or a popup) and its frames. A
/// page created by the client runs once the client lets it run (<c>Runtime.runIfWaitingForDebugger</c>): until then, it has no
/// browsing context, and its settings (the <c>Emulation</c> domain, the scripts and the bindings) are recorded. Its state is
/// used on the thread of its browser context.
/// </summary>
internal sealed partial class PageTarget
{
    private readonly List<PageSession> _sessions = [];
    private readonly Dictionary<string, FrameState> _frames = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _pendingUrl;
    private bool _closed;

    public PageTarget(BrowserContextHost context, BrowsingContext? browsingContext, PageTarget? opener, string? pendingUrl)
    {
        Context = context;
        Opener = opener;
        TargetId = CdpConnection.CreateId();
        MainFrame = new FrameState(this, TargetId, parentFrame: null);
        _frames.Add(TargetId, MainFrame);
        Emulation = opener?.Emulation.Clone() ?? new PageEmulation();
        if (browsingContext is not null)
        {
            BindBrowsingContext(browsingContext);
        }
        else
        {
            _pendingUrl = pendingUrl;
        }
    }

    public BrowserContextHost Context { get; }

    public DevToolsBrowser Browser => Context.Browser;

    public string TargetId { get; }

    public PageTarget? Opener { get; }

    public FrameState MainFrame { get; }

    /// <summary>Gets the browsing context of the page (null until the page runs).</summary>
    public BrowsingContext? BrowsingContext { get; private set; }

    public PageEmulation Emulation { get; }

    public bool IsClosed => _closed;

    public bool IsLive => BrowsingContext is not null;

    /// <summary>
    /// Gets whether the page waits for the client to let it run (<c>Runtime.runIfWaitingForDebugger</c>): a command can
    /// create its browsing context meanwhile (e.g. an evaluation), but it does not navigate to its URL, and the requests of a
    /// popup wait.
    /// </summary>
    public bool IsPaused => !_resumed.Task.IsCompleted;

    /// <summary>Gets a task completed when the client lets the page run, or when the page is closed.</summary>
    public Task Resumed => _resumed.Task;

    /// <summary>Gets or sets whether the target was attached by the auto-attachment of the browser session.</summary>
    public bool IsAutoAttached { get; set; }

    /// <summary>Gets whether a document other than the initial document was committed in the main frame.</summary>
    public bool HasCommittedNavigation { get; private set; }

    public NodeRegistry Nodes { get; } = new();

    public JsonObject GetTargetInfo()
    {
        var info = new JsonObject
        {
            ["targetId"] = TargetId,
            ["type"] = "page",
            ["title"] = GetTitle(),
            ["url"] = MainFrame.Url,
            ["attached"] = GetSessions().Length > 0,
            ["canAccessOpener"] = Opener is not null,
            ["browserContextId"] = Context.Id,
        };
        if (Opener is not null)
        {
            info["openerId"] = Opener.TargetId;
            info["openerFrameId"] = Opener.TargetId;
        }

        return info;
    }

    private string GetTitle()
    {
        try
        {
            if (MainFrame.Document is { } document && document.Title is { Length: > 0 } title)
                return title;
        }
        catch (InvalidOperationException)
        {
        }

        return MainFrame.Url;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Sessions
    // ----------------------------------------------------------------------------------------------------------------

    public PageSession[] GetSessions()
    {
        lock (_sessions)
        {
            return [.. _sessions];
        }
    }

    public void AddSession(PageSession session)
    {
        lock (_sessions)
        {
            _sessions.Add(session);
        }
    }

    public void RemoveSession(PageSession session)
    {
        lock (_sessions)
        {
            _sessions.Remove(session);
        }

        session.OnDetached();
        ApplyNetworkSettings();
    }

    /// <summary>Sends an event to the sessions that match a predicate.</summary>
    public void Broadcast(Func<PageSession, bool> predicate, string method, JsonObject parameters)
    {
        var first = true;
        foreach (var session in GetSessions())
        {
            if (!predicate(session))
                continue;

            session.SendEvent(method, first ? parameters : (JsonObject)parameters.DeepClone());
            first = false;
        }
    }

    public void Broadcast(Func<PageSession, bool> predicate, string method, Func<PageSession, JsonObject> parameters)
    {
        foreach (var session in GetSessions())
        {
            if (predicate(session))
            {
                session.SendEvent(method, parameters(session));
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Frames
    // ----------------------------------------------------------------------------------------------------------------

    public FrameState? FindFrame(string frameId) => _frames.GetValueOrDefault(frameId);

    public FrameState GetFrame(string? frameId)
    {
        if (frameId is null)
            return MainFrame;

        return FindFrame(frameId) ?? throw new ProtocolException("No frame for given id found");
    }

    public FrameState[] GetFrames() => [.. _frames.Values];

    public ScriptContext? FindExecutionContext(int id)
    {
        foreach (var frame in _frames.Values)
        {
            foreach (var context in frame.GetExecutionContexts())
            {
                if (context.Id == id)
                    return context;
            }
        }

        return null;
    }

    public ScriptContext? FindExecutionContext(string uniqueId)
    {
        foreach (var frame in _frames.Values)
        {
            foreach (var context in frame.GetExecutionContexts())
            {
                if (context.UniqueId == uniqueId)
                    return context;
            }
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Running the page
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Lets the page run (<c>Runtime.runIfWaitingForDebugger</c>): the browsing context is created, then the page navigates to its URL.</summary>
    public void Resume()
    {
        // A command can create the browsing context before the page is resumed: the page navigates to its URL once only
        if (_closed || !_resumed.TrySetResult())
            return;

        EnsureLive();
        if (Opener is not null)
        {
            // The wait runs as posted work: its continuations run on the thread of the context
            Context.Engine.Post(_ => _ = CommitInitialPopupDocumentAsync(), state: null);
        }

        var url = _pendingUrl;
        _pendingUrl = null;
        if (url is not null && url != "about:blank")
        {
            try
            {
                MainFrame.IsClientNavigation = true;
                BrowsingContext!.Navigate(url);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    /// <summary>Creates the browsing context of the page when it does not run yet (a command needs its documents).</summary>
    public void EnsureLive()
    {
        if (_closed)
            throw new ProtocolException("Target closed");

        if (IsLive)
            return;

        var options = Context.GetBrowsingContextOptions(Emulation);
        var browsingContext = new BrowsingContext(options)
        {
            Viewport = Emulation.CreateViewport(),
            IsOffline = IsOffline(),
        };
        BindBrowsingContext(browsingContext);
    }

    private void BindBrowsingContext(BrowsingContext browsingContext)
    {
        BrowsingContext = browsingContext;
        Context.Engine.AddEventLoop(browsingContext.EventLoop);
        MainFrame.BrowsingContext = browsingContext;
        Context.RegisterFrame(MainFrame);
        using var scope = Context.ScriptEngine.Engine.Enter();
        OnDocumentCommitted(MainFrame, browsingContext.Document);
    }

    /// <summary>Applies the emulation settings to the browsing context (a setting of the <c>Emulation</c> domain changed).</summary>
    public void ApplyViewport()
    {
        if (BrowsingContext is not { } browsingContext)
            return;

        var current = browsingContext.Viewport;
        browsingContext.Viewport = Emulation.CreateViewport() with
        {
            CaretVisible = current.CaretVisible,
            ScrollbarsVisible = current.ScrollbarsVisible,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Network settings (the union of the settings of the sessions)
    // ----------------------------------------------------------------------------------------------------------------

    public bool IsOffline() => GetSessions().Any(session => session.IsOffline);

    public void ApplyNetworkSettings()
    {
        if (BrowsingContext is { } browsingContext)
        {
            browsingContext.IsOffline = IsOffline();
        }
    }

    public List<KeyValuePair<string, string>> GetExtraHeaders()
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var session in GetSessions())
        {
            result.AddRange(session.ExtraHeaders);
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Closing
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Closes the page (<c>Target.closeTarget</c>, <c>Page.close</c>); the <c>beforeunload</c> handlers can cancel it.</summary>
    public Task CloseAsync(bool runBeforeUnload) => Context.Engine.InvokeAsync(() =>
    {
        if (_closed)
            return;

        if (BrowsingContext is { } browsingContext && !browsingContext.IsDestroyed && !browsingContext.TryClose(runBeforeUnload))
            return;

        CloseFromContext();
    });

    /// <summary>Closes the page and removes the target (on the thread of the context).</summary>
    public void CloseFromContext()
    {
        if (_closed)
            return;

        _closed = true;
        _resumed.TrySetResult();
        CancelDialog();
        foreach (var frame in GetFrames())
        {
            frame.OnDetached();
            Context.UnregisterFrame(frame);
        }

        if (BrowsingContext is { IsDestroyed: false } browsingContext)
        {
            browsingContext.TryClose();
        }

        Context.RemovePage(this);
        Context.Loader.RemovePage(this);
        Browser.RemoveTarget(this);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scripts and bindings
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the names of the isolated worlds created in each new document (the worlds of the scripts of the sessions).</summary>
    public HashSet<string> GetWorldNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in GetSessions())
        {
            foreach (var script in session.InitScripts.Values)
            {
                if (script.WorldName is { Length: > 0 } worldName)
                {
                    names.Add(worldName);
                }
            }
        }

        return names;
    }

    /// <summary>Defines the bindings of the sessions (<c>Runtime.addBinding</c>) in the main world of a frame.</summary>
    private void InstallBindings(FrameState frame)
    {
        if (frame.MainWorld is not { } context)
            return;

        foreach (var session in GetSessions())
        {
            foreach (var binding in session.Bindings)
            {
                InstallBinding(context, session, binding);
            }
        }
    }

    /// <summary>Defines a binding in a context: a global function that sends its argument to the session (<c>Runtime.bindingCalled</c>).</summary>
    public static void InstallBinding(ScriptContext context, PageSession session, string name)
    {
        try
        {
            var host = context.Realm.CreateFunction(name, 1, (_, arguments) =>
            {
                if (context.IsDestroyed || session.IsDetached)
                    return JsValue.Undefined;

                var payload = arguments.Length > 0 ? context.Engine.Engine.ToString(arguments[0]) : "undefined";
                session.SendEvent("Runtime.bindingCalled", new JsonObject
                {
                    ["name"] = name,
                    ["payload"] = payload,
                    ["executionContextId"] = context.Id,
                });
                return JsValue.Undefined;
            });
            context.Call("(name, host) => { globalThis[name] = (payload) => { host(payload); }; }", name, host);
        }
        catch (Exception ex) when (ex is JsException or ProtocolException)
        {
        }
    }

    /// <summary>Runs the scripts of the sessions (<c>Page.addScriptToEvaluateOnNewDocument</c>) in a new document of a frame.</summary>
    private void RunInitScripts(FrameState frame)
    {
        if (frame.MainWorld is not { } context)
            return;

        foreach (var session in GetSessions())
        {
            foreach (var script in session.InitScripts.Values.ToArray())
            {
                RunScript(context, script.Source);
            }
        }
    }

    public static void RunScript(ScriptContext context, string source)
    {
        if (source.Length == 0)
            return;

        try
        {
            context.Evaluate(source);
        }
        catch (Exception ex) when (ex is JsException or ProtocolException)
        {
        }
    }
}
