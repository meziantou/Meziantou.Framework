using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>A page: a top-level browsing context (a tab, or a popup opened by a page) and its frames.</summary>
internal sealed partial class PageDispatcher : Dispatcher
{
    private const int MaxRecordedMessages = 200;

    private readonly HashSet<string> _subscriptions = new(StringComparer.Ordinal);
    private readonly List<FrameDispatcher> _frames = [];
    private readonly List<InitScript> _initScripts = [];
    private readonly Dictionary<string, BindingRegistration> _bindings = new(StringComparer.Ordinal);
    private readonly List<JsonObject> _consoleMessages = [];
    private readonly List<JsonObject> _pageErrors = [];
    private bool _closed;

    private PageDispatcher(BrowserContextDispatcher context, BrowsingContext browsingContext, FrameDispatcher mainFrame, PageDispatcher? opener)
        : base(context, "Page", "page", CreateInitializer(browsingContext, mainFrame, opener))
    {
        Context = context;
        BrowsingContext = browsingContext;
        MainFrame = mainFrame;
        Opener = opener;
        Keyboard = new PageKeyboard(this);
        Mouse = new PageMouse(this);
    }

    private static JsonObject CreateInitializer(BrowsingContext browsingContext, FrameDispatcher mainFrame, PageDispatcher? opener)
    {
        var viewport = browsingContext.Viewport;
        var initializer = new JsonObject
        {
            ["mainFrame"] = mainFrame.ToReference(),
            ["viewportSize"] = new JsonObject { ["width"] = (int)viewport.Width, ["height"] = (int)viewport.Height },
            ["isClosed"] = false,
        };
        if (opener is not null)
        {
            initializer["opener"] = opener.ToReference();
        }

        return initializer;
    }

    public static PageDispatcher Create(BrowserContextDispatcher context, BrowsingContext browsingContext, PageDispatcher? opener)
    {
        var mainFrame = new FrameDispatcher(context, browsingContext, parentFrame: null);
        var page = new PageDispatcher(context, browsingContext, mainFrame, opener);
        mainFrame.Page = page;
        page.Adopt(mainFrame);
        lock (page._frames)
        {
            page._frames.Add(mainFrame);
        }

        context.RegisterFrame(mainFrame);
        return page;
    }

    public BrowserContextDispatcher Context { get; }

    public BrowsingContext BrowsingContext { get; }

    public FrameDispatcher MainFrame { get; }

    public PageDispatcher? Opener { get; }

    public PageKeyboard Keyboard { get; }

    public PageMouse Mouse { get; }

    public List<DialogDispatcher> OpenDialogs { get; } = [];

    /// <summary>Gets the headers added to the requests of the page (<c>page.setExtraHTTPHeaders()</c>), or null.</summary>
    public List<KeyValuePair<string, string>>? ExtraHttpHeaders { get; private set; }

    public bool IsClosed => _closed || IsDisposed;

    /// <summary>Gets or sets whether the <c>page</c> event of the page was sent (a popup is reported after its initial navigation commits).</summary>
    public bool IsReported { get; set; }

    /// <summary>Gets the reason given to <c>page.close()</c> (the message of the errors of the pending operations), or null.</summary>
    public string? CloseReason { get; private set; }

    public bool IsSubscribed(string eventName)
    {
        lock (_subscriptions)
        {
            return _subscriptions.Contains(eventName);
        }
    }

    public FrameDispatcher[] GetFrames()
    {
        lock (_frames)
        {
            return [.. _frames];
        }
    }

    /// <summary>Creates the frame of a nested browsing context (an iframe inserted in a document of the page).</summary>
    internal void AttachFrame(BrowsingContext browsingContext, FrameDispatcher parentFrame)
    {
        var frame = new FrameDispatcher(this, browsingContext, parentFrame) { Page = this };
        lock (_frames)
        {
            _frames.Add(frame);
        }

        Context.RegisterFrame(frame);
        SendEvent("frameAttached", new JsonObject { ["frame"] = frame.ToReference() });
    }

    /// <summary>Removes a frame and its descendants (their container was removed, or the parent document navigated).</summary>
    internal void DetachFrame(FrameDispatcher frame)
    {
        foreach (var child in GetFrames().Where(f => ReferenceEquals(f.ParentFrame, frame)))
        {
            DetachFrame(child);
        }

        lock (_frames)
        {
            if (!_frames.Remove(frame))
                return;
        }

        Context.UnregisterFrame(frame);
        frame.OnDetached();
        SendEvent("frameDetached", new JsonObject { ["frame"] = frame.ToReference() });
        frame.Dispose();
    }

    /// <summary>Removes the frames of the documents that were replaced (the frames whose browsing context is destroyed).</summary>
    internal void RemoveDestroyedFrames()
    {
        foreach (var frame in GetFrames())
        {
            if (frame.ParentFrame is not null && frame.BrowsingContext.IsDestroyed)
            {
                DetachFrame(frame);
            }
        }
    }

    internal void AddConsoleMessage(string type, string text, JsonObject location, double timestamp)
    {
        lock (_consoleMessages)
        {
            _consoleMessages.Add(new JsonObject { ["type"] = type, ["text"] = text, ["args"] = new JsonArray(), ["location"] = location.DeepClone(), ["timestamp"] = timestamp });
            if (_consoleMessages.Count > MaxRecordedMessages)
            {
                _consoleMessages.RemoveAt(0);
            }
        }
    }

    internal void AddPageError(JsonObject error)
    {
        lock (_pageErrors)
        {
            _pageErrors.Add((JsonObject)error.DeepClone());
            if (_pageErrors.Count > MaxRecordedMessages)
            {
                _pageErrors.RemoveAt(0);
            }
        }
    }

    internal void OnFileChooser(FrameDispatcher frame, HtmlInputElement input)
    {
        if (!IsSubscribed("fileChooser"))
            return;

        var context = frame.GetExecutionContext();
        var element = frame.CreateHandle(context, context.ToJsValue(input));
        SendEvent("fileChooser", new JsonObject { ["element"] = element.ToReference(), ["isMultiple"] = input.Multiple });
    }

    public InitScript[] GetInitScripts()
    {
        lock (_initScripts)
        {
            return [.. _initScripts];
        }
    }

    public bool HasBinding(string name)
    {
        lock (_bindings)
        {
            return _bindings.ContainsKey(name);
        }
    }

    public BindingRegistration[] GetBindings()
    {
        lock (_bindings)
        {
            return [.. _bindings.Values];
        }
    }

    /// <summary>Defines a binding in the documents of the page that are loaded (on the thread of the context).</summary>
    internal void InstallBindingInFrames(BindingRegistration binding)
    {
        foreach (var frame in GetFrames())
        {
            frame.InstallBinding(binding);
        }
    }

    public override async ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        var engine = Context.Engine;
        switch (method)
        {
            case "updateSubscription":
                lock (_subscriptions)
                {
                    var eventName = parameters.GetRequiredString("event");
                    if (parameters.GetBoolean("enabled", defaultValue: false))
                    {
                        _subscriptions.Add(eventName);
                    }
                    else
                    {
                        _subscriptions.Remove(eventName);
                    }
                }

                return null;

            case "close":
                await engine.InvokeAsync(() => Close(runBeforeUnload: false, parameters.GetString("reason"))).ConfigureAwait(false);
                return null;

            case "runBeforeUnload":
                await engine.InvokeAsync(() => Close(runBeforeUnload: true, reason: null)).ConfigureAwait(false);
                return null;

            case "reload":
                return await engine.InvokeAsync(() => MainFrame.ReloadAsync(parameters.GetString("waitUntil") ?? "load", progress)).ConfigureAwait(false);

            case "goBack":
                return await engine.InvokeAsync(() => MainFrame.TraverseHistoryAsync(-1, parameters.GetString("waitUntil") ?? "load", progress)).ConfigureAwait(false);

            case "goForward":
                return await engine.InvokeAsync(() => MainFrame.TraverseHistoryAsync(1, parameters.GetString("waitUntil") ?? "load", progress)).ConfigureAwait(false);

            case "setViewportSize":
                var size = parameters.TryGet("viewportSize", out var viewportSize) ? viewportSize : default;
                await engine.InvokeAsync(() =>
                {
                    BrowsingContext.Viewport = BrowsingContext.Viewport with { Width = size.GetInt32("width") ?? 0, Height = size.GetInt32("height") ?? 0 };
                }).ConfigureAwait(false);
                return null;

            case "emulateMedia":
                await engine.InvokeAsync(() => EmulateMedia(parameters)).ConfigureAwait(false);
                return null;

            case "screenshot":
                var screenshot = await engine.InvokeAsync(() => Screenshotter.TakePageScreenshotAsync(this, parameters, progress)).ConfigureAwait(false);
                return new JsonObject { ["binary"] = Convert.ToBase64String(screenshot) };

            case "pdf":
                var pdf = await engine.InvokeAsync(() => Screenshotter.PrintToPdf(this, parameters)).ConfigureAwait(false);
                return new JsonObject { ["pdf"] = Convert.ToBase64String(pdf) };

            case "keyboardDown":
                await engine.InvokeAsync(() => Keyboard.Down(parameters.GetRequiredString("key"))).ConfigureAwait(false);
                return null;

            case "keyboardUp":
                await engine.InvokeAsync(() => Keyboard.Up(parameters.GetRequiredString("key"))).ConfigureAwait(false);
                return null;

            case "keyboardInsertText":
                await engine.InvokeAsync(() => Keyboard.InsertText(parameters.GetRequiredString("text"))).ConfigureAwait(false);
                return null;

            case "keyboardType":
                await engine.InvokeAsync(() => Keyboard.TypeAsync(parameters.GetRequiredString("text"), parameters.GetDouble("delay") ?? 0, progress)).ConfigureAwait(false);
                return null;

            case "keyboardPress":
                await engine.InvokeAsync(() => Keyboard.PressAsync(parameters.GetRequiredString("key"), parameters.GetDouble("delay") ?? 0, progress)).ConfigureAwait(false);
                return null;

            case "mouseMove":
                await engine.InvokeAsync(() => Mouse.Move(parameters.GetDouble("x") ?? 0, parameters.GetDouble("y") ?? 0, parameters.GetInt32("steps") ?? 1)).ConfigureAwait(false);
                return null;

            case "mouseDown":
                await engine.InvokeAsync(() => Mouse.Down(parameters.GetString("button") ?? "left")).ConfigureAwait(false);
                return null;

            case "mouseUp":
                await engine.InvokeAsync(() => Mouse.Up(parameters.GetString("button") ?? "left")).ConfigureAwait(false);
                return null;

            case "mouseClick":
                await engine.InvokeAsync(() => Mouse.ClickAsync(parameters.GetDouble("x") ?? 0, parameters.GetDouble("y") ?? 0, parameters.GetString("button") ?? "left", parameters.GetInt32("clickCount") ?? 1, parameters.GetDouble("delay") ?? 0, progress)).ConfigureAwait(false);
                return null;

            case "mouseWheel":
                await engine.InvokeAsync(() => Mouse.Wheel(parameters.GetDouble("deltaX") ?? 0, parameters.GetDouble("deltaY") ?? 0)).ConfigureAwait(false);
                return null;

            case "touchscreenTap":
                if (!BrowsingContext.Viewport.HasTouch)
                    throw new PlaywrightError("hasTouch must be enabled on the browser context before using the touchscreen.");

                await engine.InvokeAsync(() => Mouse.Tap(parameters.GetDouble("x") ?? 0, parameters.GetDouble("y") ?? 0)).ConfigureAwait(false);
                return null;

            case "addInitScript":
                var initScript = new InitScript(parameters.GetRequiredString("source"));
                lock (_initScripts)
                {
                    _initScripts.Add(initScript);
                }

                return new JsonObject { ["disposable"] = new DisposableDispatcher(this, () => RemoveInitScript(initScript)).ToReference() };

            case "exposeBinding":
                var binding = Context.ExposeBinding(this, parameters.GetRequiredString("name"));
                lock (_bindings)
                {
                    _bindings[binding.Name] = binding;
                }

                return new JsonObject { ["disposable"] = new DisposableDispatcher(this, () => RemoveBinding(binding)).ToReference() };

            case "setExtraHTTPHeaders":
                ExtraHttpHeaders = parameters.GetNameValues("headers");
                return null;

            case "setNetworkInterceptionPatterns":
                Context.Network.SetInterceptionPatterns(this, parameters);
                return null;

            case "consoleMessages":
                lock (_consoleMessages)
                {
                    return new JsonObject { ["messages"] = new JsonArray([.. _consoleMessages.Select(m => m.DeepClone())]) };
                }

            case "clearConsoleMessages":
                lock (_consoleMessages)
                {
                    _consoleMessages.Clear();
                }

                return null;

            case "pageErrors":
                lock (_pageErrors)
                {
                    return new JsonObject { ["errors"] = new JsonArray([.. _pageErrors.Select(m => m.DeepClone())]) };
                }

            case "clearPageErrors":
                lock (_pageErrors)
                {
                    _pageErrors.Clear();
                }

                return null;

            case "requests":
                return new JsonObject { ["requests"] = Context.Network.GetPageRequests(this) };

            case "webStorageItems":
            case "webStorageGetItem":
            case "webStorageSetItem":
            case "webStorageRemoveItem":
            case "webStorageClear":
                return await engine.InvokeAsync(() => InvokeWebStorage(method, parameters)).ConfigureAwait(false);

            case "bringToFront":
            case "setWebSocketInterceptionPatterns":
            case "requestGC":
            case "setDockTile":
            case "hideHighlight":
            case "cancelPickLocator":
            case "startJSCoverage":
            case "startCSSCoverage":
            case "screencastFrameAck":
            case "screencastHideActions":
            case "screencastShowActions":
                return null;

            case "stopJSCoverage":
            case "stopCSSCoverage":
                return new JsonObject { ["entries"] = new JsonArray() };

            case "registerLocatorHandler":
                return new JsonObject { ["uid"] = 0 };

            case "unregisterLocatorHandler":
            case "resolveLocatorHandlerNoReply":
                return null;

            default:
                throw new PlaywrightError($"Page.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");
        }
    }

    private void RemoveInitScript(InitScript script)
    {
        lock (_initScripts)
        {
            _initScripts.Remove(script);
        }
    }

    private void RemoveBinding(BindingRegistration binding)
    {
        lock (_bindings)
        {
            _bindings.Remove(binding.Name);
        }
    }

    private void EmulateMedia(JsonElement parameters)
    {
        var viewport = BrowsingContext.Viewport;
        var contextViewport = Context.Options.CreateViewport();
        if (parameters.GetString("media") is { } media)
        {
            viewport = viewport with { MediaType = media == "no-override" ? "screen" : media };
        }

        if (parameters.GetString("colorScheme") is { } colorScheme)
        {
            viewport = viewport with { PreferredColorScheme = colorScheme switch { "dark" => PreferredColorScheme.Dark, "no-override" => contextViewport.PreferredColorScheme, _ => PreferredColorScheme.Light } };
        }

        if (parameters.GetString("reducedMotion") is { } reducedMotion)
        {
            viewport = viewport with { PrefersReducedMotion = reducedMotion == "no-override" ? contextViewport.PrefersReducedMotion : reducedMotion == "reduce" };
        }

        if (parameters.GetString("forcedColors") is { } forcedColors)
        {
            viewport = viewport with { ForcedColors = forcedColors == "no-override" ? contextViewport.ForcedColors : forcedColors == "active" };
        }

        if (parameters.GetString("contrast") is { } contrast)
        {
            viewport = viewport with { PreferredContrast = contrast switch { "more" => PreferredContrast.More, "no-override" => contextViewport.PreferredContrast, _ => PreferredContrast.NoPreference } };
        }

        BrowsingContext.Viewport = viewport;
    }

    private JsonObject? InvokeWebStorage(string method, JsonElement parameters)
    {
        var window = BrowsingContext.Window;
        var storage = parameters.GetString("kind") == "session" ? window.SessionStorage : window.LocalStorage;
        switch (method)
        {
            case "webStorageItems":
                var items = new List<KeyValuePair<string, string>>();
                for (uint i = 0; i < storage.Length; i++)
                {
                    if (storage.Key(i) is { } key)
                    {
                        items.Add(new(key, storage.GetItem(key) ?? ""));
                    }
                }

                return new JsonObject { ["items"] = JsonExtensions.ToNameValues(items) };

            case "webStorageGetItem":
                var result = new JsonObject();
                if (storage.GetItem(parameters.GetRequiredString("name")) is { } value)
                {
                    result["value"] = value;
                }

                return result;

            case "webStorageSetItem":
                storage.SetItem(parameters.GetRequiredString("name"), parameters.GetString("value") ?? "");
                return null;

            case "webStorageRemoveItem":
                storage.RemoveItem(parameters.GetRequiredString("name"));
                return null;

            default:
                storage.Clear();
                return null;
        }
    }

    /// <summary>Closes the page (on the thread of the context).</summary>
    private void Close(bool runBeforeUnload, string? reason)
    {
        if (_closed)
            return;

        foreach (var dialog in OpenDialogs.ToArray())
        {
            dialog.Close();
        }

        if (!BrowsingContext.TryClose(runBeforeUnload))
            return;

        // The callback of the destroyed browsing context closed the page, unless the browsing context was already destroyed
        CloseFromContext(reason);
    }

    /// <summary>The browsing context of the page was destroyed (e.g. <c>window.close()</c>).</summary>
    internal void OnBrowsingContextClosed() => CloseFromContext(reason: null);

    /// <summary>Closes the page and disposes its objects (on the thread of the context).</summary>
    internal void CloseFromContext(string? reason)
    {
        if (_closed)
            return;

        Context.ReportPage(this);
        _closed = true;
        CloseReason = reason;
        foreach (var dialog in OpenDialogs.ToArray())
        {
            dialog.Close();
        }

        foreach (var frame in GetFrames())
        {
            frame.OnDetached();
            Context.UnregisterFrame(frame);
        }

        if (!BrowsingContext.IsDestroyed)
        {
            BrowsingContext.TryClose();
        }

        Context.RemovePage(this);
        Context.Network.RemovePage(this);
        SendEvent("close");
        Dispose();
    }
}
