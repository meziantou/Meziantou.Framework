using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Targets;

namespace Meziantou.Framework.WebPlatform.DevTools.Browser;

/// <summary>
/// The browser of a connection: its browser contexts (the default one, and the ones of <c>Target.createBrowserContext</c>), its
/// targets (the pages), and the attachment of the sessions to the targets.
/// </summary>
internal sealed class DevToolsBrowser
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, BrowserContextHost> _contexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PageTarget> _targets = new(StringComparer.Ordinal);
    private BrowserContextHost? _defaultContext;
    private bool _autoAttach;
    private bool _waitForDebuggerOnStart;
    private bool _discoverTargets;
    private bool _closed;
    private int _nextExecutionContextId;

    public DevToolsBrowser(CdpConnection connection)
    {
        Connection = connection;
    }

    public CdpConnection Connection { get; }

    public DevToolsServerOptions Options => Connection.Options;

    public string TargetId { get; } = CdpConnection.CreateId();

    public StreamRegistry Streams { get; } = new();

    public BrowserContextHost DefaultContext
    {
        get
        {
            lock (_lock)
            {
                return _defaultContext ??= new BrowserContextHost(this, CdpConnection.CreateId(), isDefault: true);
            }
        }
    }

    /// <summary>Returns a new identifier of execution context (they are unique in the browser, as the ones of Chromium).</summary>
    public int CreateExecutionContextId() => Interlocked.Increment(ref _nextExecutionContextId);

    public PageTarget? FindTarget(string targetId)
    {
        lock (_lock)
        {
            return _targets.GetValueOrDefault(targetId);
        }
    }

    public PageTarget[] GetTargets()
    {
        lock (_lock)
        {
            return [.. _targets.Values];
        }
    }

    private BrowserContextHost GetContext(string? browserContextId)
    {
        if (browserContextId is null)
            return DefaultContext;

        lock (_lock)
        {
            if (_contexts.TryGetValue(browserContextId, out var context))
                return context;

            if (_defaultContext is not null && _defaultContext.Id == browserContextId)
                return _defaultContext;
        }

        throw new ProtocolException("Failed to find browser context with id " + browserContextId);
    }

    public async Task<JsonObject?> InvokeAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "Browser.getVersion":
                return new JsonObject
                {
                    ["protocolVersion"] = DevToolsServer.ProtocolVersion,
                    ["product"] = "HeadlessChrome/" + DevToolsServer.ChromiumVersion,
                    ["revision"] = "@0",
                    ["userAgent"] = DevToolsServer.BrowserUserAgent,
                    ["jsVersion"] = "",
                };

            case "Browser.close":
                // The response is sent before the connection is closed (the client waits for it, then for the disconnection)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
                    Connection.Close();
                });
                return null;

            case "Browser.getWindowForTarget":
                return new JsonObject { ["windowId"] = 1, ["bounds"] = new JsonObject { ["left"] = 0, ["top"] = 0, ["width"] = 800, ["height"] = 600, ["windowState"] = "normal" } };

            case "Browser.getWindowBounds":
                return new JsonObject { ["bounds"] = new JsonObject { ["left"] = 0, ["top"] = 0, ["width"] = 800, ["height"] = 600, ["windowState"] = "normal" } };

            case "Browser.setDownloadBehavior":
            case "Browser.grantPermissions":
            case "Browser.resetPermissions":
            case "Browser.setPermission":
            case "Browser.setWindowBounds":
            case "Browser.setDockTile":
            case "Browser.cancelDownload":
            case "Target.activateTarget":
            case "Target.setRemoteLocations":
            case "Security.setIgnoreCertificateErrors":
            case "Log.enable":
            case "Log.disable":
            case "Inspector.enable":
                return null;

            case "Target.setAutoAttach":
                await SetAutoAttachAsync(parameters.GetBoolean("autoAttach", defaultValue: false), parameters.GetBoolean("waitForDebuggerOnStart", defaultValue: false)).ConfigureAwait(false);
                return null;

            case "Target.setDiscoverTargets":
                SetDiscoverTargets(parameters.GetBoolean("discover", defaultValue: false));
                return null;

            case "Target.getTargets":
                return new JsonObject { ["targetInfos"] = new JsonArray([.. GetTargets().Select(target => (JsonNode)target.GetTargetInfo())]) };

            case "Target.getTargetInfo":
                if (parameters.GetString("targetId") is { } infoTargetId && infoTargetId != TargetId)
                    return new JsonObject { ["targetInfo"] = (FindTarget(infoTargetId) ?? throw new ProtocolException("No target with given id found")).GetTargetInfo() };

                return new JsonObject { ["targetInfo"] = GetBrowserTargetInfo() };

            case "Target.createBrowserContext":
                return new JsonObject { ["browserContextId"] = CreateContext().Id };

            case "Target.disposeBrowserContext":
                await DisposeContextAsync(parameters.GetRequiredString("browserContextId")).ConfigureAwait(false);
                return null;

            case "Target.getBrowserContexts":
                lock (_lock)
                {
                    return new JsonObject { ["browserContextIds"] = new JsonArray([.. _contexts.Keys.Select(id => (JsonNode)id)]) };
                }

            case "Target.createTarget":
                var context = GetContext(parameters.GetString("browserContextId"));
                var url = parameters.GetString("url") is { Length: > 0 } targetUrl ? targetUrl : "about:blank";
                var created = await context.CreatePageAsync(url).ConfigureAwait(false);
                return new JsonObject { ["targetId"] = created.TargetId };

            case "Target.closeTarget":
                var targetToClose = FindTarget(parameters.GetRequiredString("targetId")) ?? throw new ProtocolException("No target with given id found");
                await targetToClose.CloseAsync(runBeforeUnload: false).ConfigureAwait(false);
                return new JsonObject { ["success"] = true };

            case "Target.attachToTarget":
                var targetToAttach = FindTarget(parameters.GetRequiredString("targetId")) ?? throw new ProtocolException("No target with given id found");
                var session = await targetToAttach.Context.Engine.InvokeAsync(() => Attach(targetToAttach, waitingForDebugger: false)).ConfigureAwait(false);
                return new JsonObject { ["sessionId"] = session.Id };

            case "Target.attachToBrowserTarget":
                throw new ProtocolException("Attaching to the browser target is not supported");

            case "Target.detachFromTarget":
                if (parameters.GetString("sessionId") is { } sessionId && Connection.FindSession(sessionId) is PageSession pageSession)
                {
                    await pageSession.Target.Context.Engine.InvokeAsync(() => Detach(pageSession)).ConfigureAwait(false);
                    return null;
                }

                throw new ProtocolException("No session with given id");

            case "Storage.getCookies":
                var cookieContext = GetContext(parameters.GetString("browserContextId"));
                return new JsonObject { ["cookies"] = await cookieContext.Engine.InvokeAsync(() => cookieContext.GetCookies(urls: null)).ConfigureAwait(false) };

            case "Storage.setCookies":
                var setCookieContext = GetContext(parameters.GetString("browserContextId"));
                var cookies = parameters.TryGet("cookies", out var cookiesElement) ? cookiesElement.Clone() : default;
                await setCookieContext.Engine.InvokeAsync(() => setCookieContext.SetCookies(cookies)).ConfigureAwait(false);
                return null;

            case "Storage.clearCookies":
                var clearCookieContext = GetContext(parameters.GetString("browserContextId"));
                await clearCookieContext.Engine.InvokeAsync(clearCookieContext.Cookies.Clear).ConfigureAwait(false);
                return null;

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    private JsonObject GetBrowserTargetInfo() => new()
    {
        ["targetId"] = TargetId,
        ["type"] = "browser",
        ["title"] = "",
        ["url"] = "",
        ["attached"] = true,
        ["canAccessOpener"] = false,
    };

    private BrowserContextHost CreateContext()
    {
        var context = new BrowserContextHost(this, CdpConnection.CreateId(), isDefault: false);
        lock (_lock)
        {
            if (_closed)
            {
                context.Dispose();
                throw new ProtocolException("Browser is closed");
            }

            _contexts.Add(context.Id, context);
        }

        return context;
    }

    private async Task DisposeContextAsync(string browserContextId)
    {
        BrowserContextHost? context;
        lock (_lock)
        {
            _contexts.Remove(browserContextId, out context);
        }

        if (context is null)
            throw new ProtocolException("Failed to find context with id " + browserContextId);

        try
        {
            await context.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            context.Dispose();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Targets and sessions
    // ----------------------------------------------------------------------------------------------------------------

    private async Task SetAutoAttachAsync(bool autoAttach, bool waitForDebuggerOnStart)
    {
        PageTarget[] targets;
        lock (_lock)
        {
            _autoAttach = autoAttach;
            _waitForDebuggerOnStart = waitForDebuggerOnStart;
            targets = autoAttach ? [.. _targets.Values] : [];
        }

        // The existing targets are attached (once): Chromium does not pause them
        foreach (var target in targets)
        {
            await target.Context.Engine.InvokeAsync(() =>
            {
                if (!target.IsAutoAttached && !target.IsClosed)
                {
                    target.IsAutoAttached = true;
                    Attach(target, waitingForDebugger: false);
                }
            }).ConfigureAwait(false);
        }
    }

    private void SetDiscoverTargets(bool discover)
    {
        PageTarget[] targets;
        lock (_lock)
        {
            var wasDiscovering = _discoverTargets;
            _discoverTargets = discover;
            targets = discover && !wasDiscovering ? [.. _targets.Values] : [];
        }

        foreach (var target in targets)
        {
            Connection.RootSession.SendEvent("Target.targetCreated", new JsonObject { ["targetInfo"] = target.GetTargetInfo() });
        }
    }

    /// <summary>
    /// Adds a target (on the thread of its context): the clients that discover the targets are notified, and the target is
    /// attached when the targets are attached automatically. Returns whether the target waits for the client before it runs.
    /// </summary>
    public bool AddTarget(PageTarget target)
    {
        bool autoAttach;
        bool waitForDebugger;
        bool discover;
        lock (_lock)
        {
            _targets.Add(target.TargetId, target);
            autoAttach = _autoAttach;
            waitForDebugger = _waitForDebuggerOnStart;
            discover = _discoverTargets;
        }

        if (discover)
        {
            Connection.RootSession.SendEvent("Target.targetCreated", new JsonObject { ["targetInfo"] = target.GetTargetInfo() });
        }

        if (autoAttach)
        {
            target.IsAutoAttached = true;
            Attach(target, waitForDebugger);
            return waitForDebugger;
        }

        return false;
    }

    /// <summary>Removes a closed target (on the thread of its context): its sessions are detached.</summary>
    public void RemoveTarget(PageTarget target)
    {
        bool discover;
        lock (_lock)
        {
            if (!_targets.Remove(target.TargetId))
                return;

            discover = _discoverTargets;
        }

        foreach (var session in target.GetSessions())
        {
            Detach(session);
        }

        if (discover)
        {
            Connection.RootSession.SendEvent("Target.targetDestroyed", new JsonObject { ["targetId"] = target.TargetId });
        }
    }

    /// <summary>Sends the <c>Target.targetInfoChanged</c> event of a target (its URL or its title changed).</summary>
    public void OnTargetInfoChanged(PageTarget target)
    {
        bool discover;
        lock (_lock)
        {
            discover = _discoverTargets;
        }

        if (discover)
        {
            Connection.RootSession.SendEvent("Target.targetInfoChanged", new JsonObject { ["targetInfo"] = target.GetTargetInfo() });
        }
    }

    /// <summary>Attaches a new session to a target (on the thread of its context).</summary>
    public PageSession Attach(PageTarget target, bool waitingForDebugger)
    {
        var session = new PageSession(Connection, CdpConnection.CreateId(), target);
        Connection.AddSession(session);
        target.AddSession(session);
        var targetInfo = target.GetTargetInfo();
        targetInfo["attached"] = true;
        Connection.RootSession.SendEvent("Target.attachedToTarget", new JsonObject
        {
            ["sessionId"] = session.Id,
            ["targetInfo"] = targetInfo,
            ["waitingForDebugger"] = waitingForDebugger,
        });
        return session;
    }

    /// <summary>Detaches a session from its target (on the thread of its context).</summary>
    public void Detach(PageSession session)
    {
        if (session.IsDetached)
            return;

        session.Target.RemoveSession(session);
        Connection.RemoveSession(session);
        Connection.RootSession.SendEvent("Target.detachedFromTarget", new JsonObject { ["sessionId"] = session.Id, ["targetId"] = session.Target.TargetId });
    }

    /// <summary>Closes the browser contexts (the connection is closed).</summary>
    public void Shutdown()
    {
        BrowserContextHost[] contexts;
        lock (_lock)
        {
            _closed = true;
            contexts = [.. _contexts.Values];
            _contexts.Clear();
            if (_defaultContext is not null)
            {
                contexts = [.. contexts, _defaultContext];
            }
        }

        foreach (var context in contexts)
        {
            context.Dispose();
        }
    }
}
