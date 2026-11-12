using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Runtime;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The frames and the lifecycle of their documents (on the thread of the context)
internal sealed partial class PageTarget
{
    public static double Timestamp => Environment.TickCount64 / 1000d;

    public static double WallTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;

    /// <summary>Creates the frame of a nested browsing context (an iframe inserted in a document of the page).</summary>
    internal void AttachFrame(BrowsingContext browsingContext, FrameState parentFrame)
    {
        if (Context.FindFrame(browsingContext) is not null)
            return;

        var frame = new FrameState(this, CdpConnection.CreateId(), parentFrame) { BrowsingContext = browsingContext };
        _frames.Add(frame.FrameId, frame);
        Context.RegisterFrame(frame);
        Broadcast(session => session.PageEnabled, "Page.frameAttached", new JsonObject { ["frameId"] = frame.FrameId, ["parentFrameId"] = parentFrame.FrameId });
        if (browsingContext.Document is { } document)
        {
            OnDocumentCommitted(frame, document);
        }
    }

    /// <summary>Removes a frame and its descendants (their container was removed, or the parent document navigated).</summary>
    internal void DetachFrame(FrameState frame)
    {
        foreach (var child in GetFrames().Where(f => ReferenceEquals(f.ParentFrame, frame)))
        {
            DetachFrame(child);
        }

        if (!_frames.Remove(frame.FrameId))
            return;

        DestroyExecutionContexts(frame);
        Context.UnregisterFrame(frame);
        frame.OnDetached();
        Broadcast(session => session.PageEnabled, "Page.frameDetached", new JsonObject { ["frameId"] = frame.FrameId, ["reason"] = "remove" });
    }

    /// <summary>Removes the frames whose browsing context was destroyed (the documents that contained them were replaced).</summary>
    private void RemoveDestroyedFrames()
    {
        foreach (var frame in GetFrames())
        {
            if (frame.ParentFrame is not null && frame.BrowsingContext is { IsDestroyed: true })
            {
                DetachFrame(frame);
            }
        }
    }

    /// <summary>
    /// A document became the active document of a frame (the end of a navigation), before its scripts run: the execution
    /// contexts of the previous document are destroyed, the document is committed (<c>Page.frameNavigated</c>), the contexts of
    /// the new document are created, then the bindings are defined and the scripts of the sessions run.
    /// </summary>
    internal void OnDocumentCommitted(FrameState frame, Document document)
    {
        if (_closed || frame.IsDetached || ReferenceEquals(frame.Document, document))
            return;

        // The first document of the main frame is the document it had when the page started to run: its identifier is the one
        // the clients already know. The first document of a child frame is committed as any other document.
        var isInitialBinding = frame.Document is null && frame.ParentFrame is null;
        DestroyExecutionContexts(frame);
        RemoveDestroyedFrames();

        frame.Document = document;
        frame.Url = document.Url;
        frame.IsInitialEmptyDocument = isInitialBinding && Opener is not null && document.IsInitialAboutBlank;
        frame.Name = frame.BrowsingContext?.Name ?? "";
        frame.MimeType = document.ContentType;
        if (!isInitialBinding)
        {
            frame.LoaderId = frame.PendingLoaderId ?? CdpConnection.CreateId();
        }

        var navigationError = document.IsNetworkErrorDocument ? frame.PendingNavigationError ?? (IsOffline() ? "net::ERR_INTERNET_DISCONNECTED" : "net::ERR_FAILED") : null;
        frame.PendingLoaderId = null;
        frame.PendingNavigationError = null;
        frame.IsClientNavigation = false;
        frame.LifecycleEvents.Clear();
        frame.NetworkIdleGeneration++;

        if (!isInitialBinding)
        {
            if (frame.ParentFrame is null)
            {
                HasCommittedNavigation = true;
            }

            Broadcast(session => session.PageEnabled, "Page.frameNavigated", new JsonObject { ["frame"] = frame.ToPayload(), ["type"] = "Navigation" });
        }

        CreateExecutionContexts(frame);
        AddLifecycleEvent(frame, "init");
        AddLifecycleEvent(frame, "commit");
        InstallBindings(frame);
        RunInitScripts(frame);
        ListenToLifecycleEvents(frame, document);
        frame.CompleteNavigation(new NavigationResult(frame.LoaderId, navigationError));
        if (frame.ParentFrame is null && !isInitialBinding)
        {
            Browser.OnTargetInfoChanged(this);
        }
    }

    /// <summary>
    /// A popup that does not navigate (window.open() without URL, or a navigation that ends without a document) keeps its
    /// initial empty document: as Chromium, it is committed as <c>about:blank</c>, so that the clients that wait for the first
    /// navigation of the popup report it.
    /// </summary>
    internal async Task CommitInitialPopupDocumentAsync()
    {
        // The navigation of window.open(url) starts in the task that created the popup
        await Task.Yield();
        while (!_closed && !HasCommittedNavigation && (BrowsingContext!.IsNavigating || MainFrame.PendingLoaderId is not null))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(true);
        }

        if (_closed || HasCommittedNavigation || !MainFrame.IsInitialEmptyDocument)
            return;

        MainFrame.IsInitialEmptyDocument = false;
        HasCommittedNavigation = true;
        Broadcast(session => session.PageEnabled, "Page.frameNavigated", new JsonObject { ["frame"] = MainFrame.ToPayload(), ["type"] = "Navigation" });
        Browser.OnTargetInfoChanged(this);
    }

    /// <summary>The URL of the document changed without a new document (a fragment navigation, <c>history.pushState()</c>).</summary>
    internal void OnSameDocumentNavigated(FrameState frame)
    {
        if (frame.Document is not { } document)
            return;

        var previousUrl = frame.Url;
        frame.Url = document.Url;
        var navigationType = StripFragment(previousUrl) == StripFragment(frame.Url) ? "fragment" : "historyApi";
        Broadcast(session => session.PageEnabled, "Page.navigatedWithinDocument", new JsonObject { ["frameId"] = frame.FrameId, ["url"] = frame.Url, ["navigationType"] = navigationType });

        // A navigation of the client (Page.navigate) to a fragment ends here: the next navigations are the ones of the page
        frame.IsClientNavigation = false;
        frame.CompleteNavigation(new NavigationResult(LoaderId: null, ErrorText: null));
        if (frame.ParentFrame is null)
        {
            Browser.OnTargetInfoChanged(this);
        }

        static string StripFragment(string url) => url.IndexOf('#', StringComparison.Ordinal) is var index and >= 0 ? url[..index] : url;
    }

    private void CreateExecutionContexts(FrameState frame)
    {
        frame.MainWorld = new ScriptContext(Browser.CreateExecutionContextId(), frame, worldName: "") { Document = frame.Document };
        OnExecutionContextCreated(frame.MainWorld);
        foreach (var worldName in GetWorldNames())
        {
            CreateIsolatedWorld(frame, worldName);
        }
    }

    /// <summary>Creates an isolated world in the current document of a frame (it runs in the realm of the main world).</summary>
    public ScriptContext CreateIsolatedWorld(FrameState frame, string worldName)
    {
        if (frame.IsolatedWorlds.TryGetValue(worldName, out var existing))
            return existing;

        var context = new ScriptContext(Browser.CreateExecutionContextId(), frame, worldName) { Document = frame.Document };
        frame.IsolatedWorlds.Add(worldName, context);
        OnExecutionContextCreated(context);
        return context;
    }

    private void OnExecutionContextCreated(ScriptContext context)
    {
        Broadcast(session => session.RuntimeEnabled, "Runtime.executionContextCreated", new JsonObject { ["context"] = context.ToDescription() });
    }

    private void DestroyExecutionContexts(FrameState frame)
    {
        foreach (var context in frame.GetExecutionContexts().ToArray())
        {
            context.Destroy();
            foreach (var session in GetSessions())
            {
                session.Objects.RemoveContext(context);
            }

            Broadcast(session => session.RuntimeEnabled, "Runtime.executionContextDestroyed", new JsonObject { ["executionContextId"] = context.Id, ["executionContextUniqueId"] = context.UniqueId });
        }

        frame.MainWorld = null;
        frame.IsolatedWorlds.Clear();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Lifecycle events
    // ----------------------------------------------------------------------------------------------------------------

    private void AddLifecycleEvent(FrameState frame, string name)
    {
        var timestamp = Timestamp;
        frame.LifecycleEvents.Add((name, timestamp));
        Broadcast(session => session.LifecycleEventsEnabled, "Page.lifecycleEvent", new JsonObject
        {
            ["frameId"] = frame.FrameId,
            ["loaderId"] = frame.LoaderId,
            ["name"] = name,
            ["timestamp"] = timestamp,
        });
    }

    /// <summary>Sends the lifecycle events of the current documents to a session that enables them (as Chromium does).</summary>
    public void SendLifecycleEvents(PageSession session)
    {
        foreach (var frame in GetFrames())
        {
            foreach (var (name, timestamp) in frame.LifecycleEvents)
            {
                session.SendEvent("Page.lifecycleEvent", new JsonObject
                {
                    ["frameId"] = frame.FrameId,
                    ["loaderId"] = frame.LoaderId,
                    ["name"] = name,
                    ["timestamp"] = timestamp,
                });
            }
        }
    }

    // The events already fired by the document (the initial about:blank document, a document restored from the back/forward
    // cache); the next ones are reported by the browsing context (BrowsingContextOptions.LifecycleEventFired), each time they
    // are fired (document.open() fires them again)
    private void ListenToLifecycleEvents(FrameState frame, Document document)
    {
        if (document.ReadyState != DocumentReadyState.Loading)
        {
            OnDomContentLoaded(frame);
        }

        if (document.ReadyState == DocumentReadyState.Complete)
        {
            OnLoad(frame);
        }
    }

    /// <summary>The <c>DOMContentLoaded</c> or the <c>load</c> event of a document of a frame was fired.</summary>
    internal void OnDocumentLifecycleEvent(FrameState frame, Document document, string name)
    {
        if (!ReferenceEquals(frame.Document, document) || _closed)
            return;

        if (name == "DOMContentLoaded")
        {
            OnDomContentLoaded(frame);
        }
        else
        {
            OnLoad(frame);
        }
    }

    private void OnDomContentLoaded(FrameState frame)
    {
        AddLifecycleEvent(frame, "DOMContentLoaded");
        if (frame.ParentFrame is null)
        {
            Broadcast(session => session.PageEnabled, "Page.domContentEventFired", new JsonObject { ["timestamp"] = Timestamp });
        }
    }

    private void OnLoad(FrameState frame)
    {
        AddLifecycleEvent(frame, "load");
        if (frame.ParentFrame is null)
        {
            Broadcast(session => session.PageEnabled, "Page.loadEventFired", new JsonObject { ["timestamp"] = Timestamp });
        }

        Broadcast(session => session.PageEnabled, "Page.frameStoppedLoading", new JsonObject { ["frameId"] = frame.FrameId });
        if (frame.InflightRequests == 0)
        {
            StartNetworkIdleTimer(frame);
        }
    }

    // The network idle events: no request of the frame for 500 ms after the load event

    internal void OnRequestStarted(FrameState frame)
    {
        frame.InflightRequests++;
        frame.NetworkIdleGeneration++;
    }

    internal void OnRequestFinished(FrameState frame)
    {
        frame.InflightRequests = Math.Max(0, frame.InflightRequests - 1);
        if (frame.InflightRequests == 0 && frame.LifecycleEvents.Exists(e => e.Name == "load"))
        {
            StartNetworkIdleTimer(frame);
        }
    }

    private void StartNetworkIdleTimer(FrameState frame)
    {
        // The timer runs as posted work (this method can run in a task of the event loop): its continuation runs on the thread
        // of the context
        var generation = ++frame.NetworkIdleGeneration;
        Context.Engine.Post(_ => _ = StartNetworkIdleTimerAsync(frame, generation), state: null);
    }

    private async Task StartNetworkIdleTimerAsync(FrameState frame, int generation)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(true);
        if (generation != frame.NetworkIdleGeneration || frame.IsDetached || _closed || frame.InflightRequests > 0)
            return;

        if (!frame.LifecycleEvents.Exists(e => e.Name == "networkIdle"))
        {
            AddLifecycleEvent(frame, "networkAlmostIdle");
            AddLifecycleEvent(frame, "networkIdle");
        }
    }
}
