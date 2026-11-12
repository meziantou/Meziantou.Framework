using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Runtime;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>
/// A frame of a page: a browsing context (the main frame is the top-level browsing context of the page, its identifier is the
/// identifier of the target). Its state is used on the thread of the browser context.
/// </summary>
internal sealed class FrameState(PageTarget target, string frameId, FrameState? parentFrame)
{
    private readonly List<TaskCompletionSource<NavigationResult>> _navigationWaiters = [];

    public PageTarget Target { get; } = target;

    public string FrameId { get; } = frameId;

    public FrameState? ParentFrame { get; } = parentFrame;

    /// <summary>Gets or sets the browsing context of the frame (null until the page runs).</summary>
    public BrowsingContext? BrowsingContext { get; set; }

    /// <summary>Gets or sets the current document (null until the page runs).</summary>
    public Document? Document { get; set; }

    public string Url { get; set; } = "about:blank";

    /// <summary>
    /// Gets or sets whether the document is the initial empty document of a popup: as Chromium, its URL is reported as an
    /// empty string, and the clients wait for the first navigation of the popup.
    /// </summary>
    public bool IsInitialEmptyDocument { get; set; }

    public string Name { get; set; } = "";

    public string MimeType { get; set; } = "text/html";

    /// <summary>Gets or sets the identifier of the current document (the <c>loaderId</c> of the protocol).</summary>
    public string LoaderId { get; set; } = Protocol.CdpConnection.CreateId();

    /// <summary>Gets or sets the identifier of the document of the ongoing navigation: the identifier of its request.</summary>
    public string? PendingLoaderId { get; set; }

    /// <summary>Gets or sets the error of the request of the ongoing navigation (e.g. <c>net::ERR_FAILED</c>).</summary>
    public string? PendingNavigationError { get; set; }

    /// <summary>Gets or sets the referrer of the next navigation (<c>Page.navigate</c>), consumed by its request.</summary>
    public string? PendingReferrer { get; set; }

    /// <summary>Gets or sets whether the ongoing navigation was started by the client (<c>Page.navigate</c>, <c>Page.reload</c>).</summary>
    public bool IsClientNavigation { get; set; }

    public ScriptContext? MainWorld { get; set; }

    public Dictionary<string, ScriptContext> IsolatedWorlds { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the lifecycle events of the current document (sent again when a client enables them).</summary>
    public List<(string Name, double Timestamp)> LifecycleEvents { get; } = [];

    public bool IsDetached { get; private set; }

    public int InflightRequests { get; set; }

    public int NetworkIdleGeneration { get; set; }

    public string SecurityOrigin
    {
        get
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "ws" or "wss")
                return uri.GetLeftPart(UriPartial.Authority);

            return Url.StartsWith("about:", StringComparison.Ordinal) && ParentFrame is not null ? ParentFrame.SecurityOrigin : "://";
        }
    }

    public IEnumerable<ScriptContext> GetExecutionContexts()
    {
        if (MainWorld is not null)
            yield return MainWorld;

        foreach (var world in IsolatedWorlds.Values)
        {
            yield return world;
        }
    }

    /// <summary>The <c>Frame</c> of the protocol.</summary>
    public JsonObject ToPayload()
    {
        var url = IsInitialEmptyDocument ? "" : Url;
        string? fragment = null;
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = url[hash..];
            url = url[..hash];
        }

        var payload = new JsonObject { ["id"] = FrameId };
        if (ParentFrame is not null)
        {
            payload["parentId"] = ParentFrame.FrameId;
        }

        payload["loaderId"] = LoaderId;
        payload["name"] = Name;
        payload["url"] = url;
        if (fragment is not null)
        {
            payload["urlFragment"] = fragment;
        }

        payload["domainAndRegistry"] = "";
        payload["securityOrigin"] = SecurityOrigin;
        payload["mimeType"] = MimeType;
        payload["secureContextType"] = Url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || Url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) || Url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ? "Secure" : "InsecureScheme";
        payload["crossOriginIsolatedContextType"] = "NotIsolated";
        payload["gatedAPIFeatures"] = new JsonArray();
        return payload;
    }

    public TaskCompletionSource<NavigationResult> AddNavigationWaiter()
    {
        var waiter = new TaskCompletionSource<NavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationWaiters.Add(waiter);
        return waiter;
    }

    public void RemoveNavigationWaiter(TaskCompletionSource<NavigationResult> waiter) => _navigationWaiters.Remove(waiter);

    public void CompleteNavigation(NavigationResult result)
    {
        foreach (var waiter in _navigationWaiters.ToArray())
        {
            _navigationWaiters.Remove(waiter);
            waiter.TrySetResult(result);
        }
    }

    public void OnDetached()
    {
        IsDetached = true;
        foreach (var context in GetExecutionContexts())
        {
            context.Destroy();
        }

        CompleteNavigation(new NavigationResult(LoaderId: null, "net::ERR_ABORTED"));
    }
}

/// <summary>The end of a navigation started by the client: a new document (its identifier, or an error), or a same-document navigation.</summary>
internal sealed record NavigationResult(string? LoaderId, string? ErrorText);
