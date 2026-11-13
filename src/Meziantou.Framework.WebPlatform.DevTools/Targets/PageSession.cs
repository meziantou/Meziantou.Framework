using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Network;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Runtime;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>
/// A session attached to a page target: the domains it enabled, the objects it was given, its scripts and bindings, and the
/// interception of the requests. Its commands run on the thread of the browser context of the page, except the ones that
/// answer a script that blocks this thread (a dialog, a request of a synchronous <c>XMLHttpRequest</c>).
/// </summary>
internal sealed partial class PageSession(CdpConnection connection, string id, PageTarget target) : CdpSession(connection, id)
{
    private int _nextScriptId;

    public PageTarget Target { get; } = target;

    public bool PageEnabled { get; private set; }

    public bool RuntimeEnabled { get; private set; }

    public bool NetworkEnabled { get; private set; }

    public bool LifecycleEventsEnabled { get; private set; }

    public bool InterceptFileChooser { get; private set; }

    public bool IsOffline { get; private set; }

    public List<KeyValuePair<string, string>> ExtraHeaders { get; private set; } = [];

    public RemoteObjectRegistry Objects { get; } = new();

    public Dictionary<string, InitScript> InitScripts { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Bindings { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the interception of the requests of the session (the <c>Fetch</c> domain), or null when it is disabled.</summary>
    public FetchInterception? Fetch { get; private set; }

    public override Task<JsonObject?> InvokeAsync(string method, JsonElement parameters)
    {
        // The answers to a script that waits (a dialog blocks the thread of the context, a synchronous request waits for the
        // interception) are given on the thread of the connection
        switch (method)
        {
            case "Page.handleJavaScriptDialog":
                Target.HandleDialog(parameters.GetBoolean("accept", defaultValue: false), parameters.GetString("promptText"));
                return Task.FromResult<JsonObject?>(null);

            case "Fetch.continueRequest":
            case "Fetch.fulfillRequest":
            case "Fetch.failRequest":
            case "Fetch.continueWithAuth":
            case "Fetch.continueResponse":
                (Fetch ?? throw new ProtocolException("Fetch domain is not enabled")).Resolve(method, parameters);
                return Task.FromResult<JsonObject?>(null);
        }

        return Target.Context.Engine.InvokeAsync(() => InvokeOnEngineThreadAsync(method, parameters));
    }

    public override Task DispatchAsync(CdpMessage message)
    {
        if (message.Method is "Page.handleJavaScriptDialog" || message.Method.StartsWith("Fetch.continue", StringComparison.Ordinal) || message.Method is "Fetch.fulfillRequest" or "Fetch.failRequest")
            return base.DispatchAsync(message);

        // The response is sent from the thread of the context, before the events of the next operations of the thread (e.g.
        // the response of Page.getFrameTree is sent before the lifecycle events of the page that runs next)
        return Target.Context.Engine.InvokeAsync(async () =>
        {
            JsonObject? result;
            try
            {
                result = await InvokeOnEngineThreadAsync(message.Method, message.Parameters).ConfigureAwait(true);
            }
#pragma warning disable CA1031 // The errors of the commands are reported to the client
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Connection.Fail(message, ex);
                return;
            }

            Connection.Complete(message, result);
        });
    }

    private async Task<JsonObject?> InvokeOnEngineThreadAsync(string method, JsonElement parameters)
    {
        if (IsDetached)
            throw new ProtocolException("Session with given id not found.");

        var dot = method.IndexOf('.', StringComparison.Ordinal);
        var domain = dot < 0 ? method : method[..dot];
        return domain switch
        {
            "Page" => await InvokePageAsync(method, parameters).ConfigureAwait(true),
            "Runtime" => await InvokeRuntimeAsync(method, parameters).ConfigureAwait(true),
            "DOM" => InvokeDom(method, parameters),
            "Input" => await InvokeInputAsync(method, parameters).ConfigureAwait(true),
            "Network" => InvokeNetwork(method, parameters),
            "Fetch" => InvokeFetch(method, parameters),
            "Emulation" => InvokeEmulation(method, parameters),
            "IO" => Target.Context.Browser.Streams.Invoke(method, parameters),
            "Target" => InvokeTarget(method, parameters),
            "Log" or "Inspector" or "Security" or "Performance" or "CSS" or "Overlay" or "ServiceWorker" or "Accessibility" or "Audits" or "Autofill" or "Debugger" or "Profiler" or "HeapProfiler" or "Media" or "WebAuthn" or "BackgroundService" or "DOMStorage" or "Database" or "IndexedDB" or "CacheStorage" or "Animation" or "LayerTree" or "Memory" or "Storage" or "DeviceAccess" or "FedCm" or "PWA" or "Preload" or "BluetoothEmulation" or "Cast" or "EventBreakpoints" or "DOMDebugger" or "DOMSnapshot" or "HeadlessExperimental" => InvokeNoOp(method),
            _ => throw ProtocolException.NotFound(method),
        };
    }

    /// <summary>The commands of the domains that have no effect (they enable events that are never sent, or features of the browser that do not exist).</summary>
    private static JsonObject? InvokeNoOp(string method) => method switch
    {
        "Debugger.enable" => new JsonObject { ["debuggerId"] = "0" },
        "Debugger.getScriptSource" => new JsonObject { ["scriptSource"] = "" },
        "Profiler.takePreciseCoverage" or "Profiler.stopPreciseCoverage" => new JsonObject { ["result"] = new JsonArray(), ["timestamp"] = PageTarget.Timestamp },
        "Profiler.startPreciseCoverage" => new JsonObject { ["timestamp"] = PageTarget.Timestamp },
        "CSS.stopRuleUsageTracking" => new JsonObject { ["ruleUsage"] = new JsonArray() },
        "CSS.getStyleSheetText" => new JsonObject { ["text"] = "" },
        "Performance.getMetrics" => new JsonObject { ["metrics"] = new JsonArray() },
        "Accessibility.getFullAXTree" => new JsonObject { ["nodes"] = new JsonArray() },
        _ => null,
    };

    private JsonObject? InvokeTarget(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "Target.setAutoAttach":
            case "Target.setDiscoverTargets":
            case "Target.activateTarget":
                // The frames and the workers of the page are not targets: there is nothing to attach
                return null;

            case "Target.getTargetInfo":
                return new JsonObject { ["targetInfo"] = Target.GetTargetInfo() };

            case "Target.detachFromTarget":
                if (parameters.GetString("sessionId") is { } sessionId && sessionId != Id)
                    throw new ProtocolException("No session with given id");

                Target.Browser.Detach(this);
                return null;

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    public string AddInitScript(string source, string? worldName)
    {
        var identifier = (++_nextScriptId).ToString(CultureInfo.InvariantCulture);
        InitScripts.Add(identifier, new InitScript(source, worldName));
        return identifier;
    }

    /// <summary>The session was detached: its interception releases the paused requests.</summary>
    public void OnDetached()
    {
        Objects.Clear();
        Fetch?.Dispose();
        Fetch = null;
        MarkDetached();
    }
}

/// <summary>A script evaluated in each new document (<c>Page.addScriptToEvaluateOnNewDocument</c>), in the main world or in an isolated world.</summary>
internal sealed record InitScript(string Source, string? WorldName);
