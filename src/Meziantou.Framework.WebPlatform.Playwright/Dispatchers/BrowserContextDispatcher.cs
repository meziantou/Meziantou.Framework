using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Playwright.Engine;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>
/// A browser context: an isolated session (its own cookies, storage and script engine) whose pages run on their own thread
/// (<see cref="EngineThread"/>). The browsing contexts of its pages share its <see cref="BrowsingContextOptions"/>: the callbacks
/// of the options route the events of the documents to the objects of the pages and of the frames.
/// </summary>
#pragma warning disable CA1001 // The script engine is disposed when the context is closed (Shutdown)
internal sealed partial class BrowserContextDispatcher : Dispatcher
#pragma warning restore CA1001
{
    private readonly HashSet<string> _subscriptions = new(StringComparer.Ordinal);
    private readonly List<PageDispatcher> _pages = [];
    private readonly Dictionary<BrowsingContext, PageDispatcher> _pagesByBrowsingContext = [];
    private readonly Dictionary<BrowsingContext, FrameDispatcher> _frames = [];
    private readonly List<InitScript> _initScripts = [];
    private readonly Dictionary<string, BindingRegistration> _bindings = new(StringComparer.Ordinal);
    private BrowsingContextOptions? _browsingContextOptions;
    private PlaywrightScriptEngine? _scriptEngine;
    private bool _closed;

    private BrowserContextDispatcher(BrowserDispatcher browser, ContextOptions options, JsonObject initializer)
        : base(browser, "BrowserContext", "browser-context", initializer)
    {
        Browser = browser;
        Options = options;
        Engine = new EngineThread("Playwright browser context " + Guid);
        Network = new ContextNetwork(this, browser.Connection.Options.DocumentLoader);
    }

    public BrowserDispatcher Browser { get; }

    public ContextOptions Options { get; }

    public EngineThread Engine { get; }

    public ContextNetwork Network { get; }

    public CookieJar Cookies { get; } = new();

    public StorageShed LocalStorage { get; } = new();

    public PlaywrightScriptEngine ScriptEngine => _scriptEngine ?? throw new InvalidOperationException("The browser context is not initialized");

    public BrowsingContextOptions BrowsingContextOptions => _browsingContextOptions ?? throw new InvalidOperationException("The browser context is not initialized");

    public APIRequestContextDispatcher RequestContext { get; private set; } = null!;

    public TracingDispatcher Tracing { get; private set; } = null!;

    public bool IsClosed => _closed || IsDisposed;

    public static async Task<BrowserContextDispatcher> CreateAsync(BrowserDispatcher browser, JsonElement parameters)
    {
        var options = ContextOptions.Parse(parameters);
        var debugger = new StubDispatcher(browser, "Debugger", "debugger");
        var requestTracing = new TracingDispatcher(browser);
        var requestContext = new APIRequestContextDispatcher(browser, requestTracing);
        requestContext.Adopt(requestTracing);
        var tracing = new TracingDispatcher(browser);

        // The options of the initializer are the options of the context as the client sent them
        var contextOptions = parameters.ValueKind == JsonValueKind.Object ? (JsonObject)JsonNode.Parse(parameters.GetRawText())! : [];
        contextOptions.Remove("storageState");
        contextOptions.Remove("proxy");
        var initializer = new JsonObject
        {
            ["debugger"] = debugger.ToReference(),
            ["requestContext"] = requestContext.ToReference(),
            ["tracing"] = tracing.ToReference(),
            ["options"] = contextOptions,
        };

        var context = new BrowserContextDispatcher(browser, options, initializer)
        {
            RequestContext = requestContext,
            Tracing = tracing,
        };
        context.Adopt(debugger);
        context.Adopt(requestContext);
        context.Adopt(tracing);
        requestContext.Initialize(context);
        await context.Engine.InvokeAsync(context.Initialize).ConfigureAwait(false);
        return context;
    }

    /// <summary>Creates the script engine and the options of the browsing contexts (on the thread of the context).</summary>
    private void Initialize()
    {
        var serverOptions = Connection.Options;
        _scriptEngine = new PlaywrightScriptEngine(this, CreateScriptEngineOptions(serverOptions.ScriptEngineOptions));
        _browsingContextOptions = new BrowsingContextOptions
        {
            ScriptEngine = _scriptEngine,
            ScriptingEnabled = Options.JavaScriptEnabled,
            DocumentLoader = Network,
            CookieJar = Cookies,
            LocalStorage = LocalStorage,
            RenderingBackend = new ImagingRenderingBackend(),
            UseSystemFonts = serverOptions.UseSystemFonts,
            UserAgent = Options.UserAgent,
            Languages = Options.GetLanguages(),
            Viewport = Options.CreateViewport(),
            BrowsingContextCreated = OnBrowsingContextCreated,
            BrowsingContextDestroyed = OnBrowsingContextDestroyed,
            DocumentActivated = OnDocumentActivated,
            SameDocumentNavigated = OnSameDocumentNavigated,
            ConsoleMessage = OnConsoleMessage,
            UnhandledScriptError = OnUnhandledScriptError,
            UserPromptHandler = OnUserPrompt,
            ShowFilePicker = OnShowFilePicker,
        };

        RestoreStorageState(Options.StorageState);
    }

    private JavaScriptScriptEngineOptions CreateScriptEngineOptions(JavaScriptScriptEngineOptions? options)
    {
        options ??= new JavaScriptScriptEngineOptions();
        return new JavaScriptScriptEngineOptions
        {
            Timeout = options.Timeout,
            MemoryLimit = options.MemoryLimit,
            MaxStatements = options.MaxStatements,
            RecursionLimit = options.RecursionLimit,
            RegexTimeout = options.RegexTimeout,
            TimeProvider = options.TimeProvider,
            ConfigureEngine = engineOptions =>
            {
                if (Options.TimezoneId is { } timeZone)
                {
                    engineOptions.TimeZone = timeZone;
                }

                if (Options.Locale is { } locale)
                {
                    engineOptions.DefaultLocale = locale;
                }

                options.ConfigureEngine?.Invoke(engineOptions);
            },
        };
    }

    public bool IsSubscribed(string eventName, PageDispatcher? page)
    {
        lock (_subscriptions)
        {
            if (_subscriptions.Contains(eventName))
                return true;
        }

        return page is not null && page.IsSubscribed(eventName);
    }

    public override async ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        switch (method)
        {
            case "newPage":
                var page = await Engine.InvokeAsync(() => CreatePage(opener: null)).ConfigureAwait(false);
                return new JsonObject { ["page"] = page.ToReference() };

            case "close":
                await CloseAsync(parameters.GetString("reason")).ConfigureAwait(false);
                return null;

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

            case "cookies":
                return await Engine.InvokeAsync(() => new JsonObject { ["cookies"] = GetCookies(parameters.GetArray("urls").Select(url => url.GetString()!).ToList()) }).ConfigureAwait(false);

            case "addCookies":
                await Engine.InvokeAsync(() => AddCookies(parameters.GetArray("cookies"))).ConfigureAwait(false);
                return null;

            case "clearCookies":
                await Engine.InvokeAsync(() => ClearCookies(parameters)).ConfigureAwait(false);
                return null;

            case "storageState":
                return await Engine.InvokeAsync(GetStorageState).ConfigureAwait(false);

            case "setStorageState":
                var storageState = parameters.TryGet("storageState", out var state) ? state.Clone() : default;
                await Engine.InvokeAsync(() =>
                {
                    Cookies.Clear();
                    LocalStorage.Clear();
                    RestoreStorageState(storageState);
                }).ConfigureAwait(false);
                return null;

            case "setExtraHTTPHeaders":
                Options.ExtraHttpHeaders = parameters.GetNameValues("headers");
                return null;

            case "setHTTPCredentials":
                Options.HttpCredentials = parameters.TryGet("httpCredentials", out var credentials) ? [HttpCredentials.Parse(credentials)] : [];
                return null;

            case "setOffline":
                var offline = parameters.GetBoolean("offline", defaultValue: false);
                Options.Offline = offline;
                await Engine.InvokeAsync(() =>
                {
                    foreach (var p in GetPages())
                    {
                        p.BrowsingContext.IsOffline = offline;
                    }
                }).ConfigureAwait(false);
                return null;

            case "addInitScript":
                var initScript = new InitScript(parameters.GetRequiredString("source"));
                lock (_initScripts)
                {
                    _initScripts.Add(initScript);
                }

                return new JsonObject { ["disposable"] = new DisposableDispatcher(this, () => RemoveInitScript(initScript)).ToReference() };

            case "exposeBinding":
                var binding = ExposeBinding(page: null, parameters.GetRequiredString("name"));
                return new JsonObject { ["disposable"] = new DisposableDispatcher(this, () => RemoveBinding(binding)).ToReference() };

            case "registerSelectorEngine":
                if (parameters.TryGet("selectorEngine", out var selectorEngine))
                {
                    Options.SelectorEngines.Add((selectorEngine.GetString("name") ?? "", selectorEngine.GetString("source") ?? ""));
                }

                return null;

            case "setTestIdAttributeName":
                Options.TestIdAttributeName = parameters.GetString("testIdAttributeName") ?? "data-testid";
                return null;

            case "setNetworkInterceptionPatterns":
                Network.SetInterceptionPatterns(page: null, parameters);
                return null;

            case "setWebSocketInterceptionPatterns":
            case "grantPermissions":
            case "clearPermissions":
            case "setGeolocation":
            case "pause":
            case "exposeConsoleApi":
            case "stopRecording":
                return null;

            case "createTempFiles":
                throw new PlaywrightError("Uploading files from paths is not supported by a remote Playwright server: pass the content of the files (FilePayload)");

            default:
                throw new PlaywrightError($"BrowserContext.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");
        }
    }

    /// <summary>Creates a page (on the thread of the context): a new top-level browsing context, or a popup opened by a page.</summary>
    private PageDispatcher CreatePage(PageDispatcher? opener)
    {
        var browsingContext = new BrowsingContext(BrowsingContextOptions)
        {
            Viewport = Options.CreateViewport(),
            IsOffline = Options.Offline,
        };
        return RegisterPage(browsingContext, opener);
    }

    private PageDispatcher RegisterPage(BrowsingContext browsingContext, PageDispatcher? opener, bool report = true)
    {
        Engine.AddEventLoop(browsingContext.EventLoop);
        var page = PageDispatcher.Create(this, browsingContext, opener);
        lock (_pages)
        {
            _pages.Add(page);
            _pagesByBrowsingContext[browsingContext] = page;
        }

        if (report)
        {
            ReportPage(page);
        }

        return page;
    }

    /// <summary>Sends the <c>page</c> event of a page (the clients add the page to the context, and emit the <c>popup</c> event of its opener).</summary>
    internal void ReportPage(PageDispatcher page)
    {
        if (page.IsReported || page.IsDisposed)
            return;

        page.IsReported = true;
        SendEvent("page", new JsonObject { ["page"] = page.ToReference() });
    }

    public PageDispatcher[] GetPages()
    {
        lock (_pages)
        {
            return [.. _pages];
        }
    }

    public PageDispatcher? FindPage(BrowsingContext? browsingContext)
    {
        if (browsingContext is null)
            return null;

        lock (_pages)
        {
            return _pagesByBrowsingContext.GetValueOrDefault(browsingContext.Top);
        }
    }

    public FrameDispatcher? FindFrame(BrowsingContext? browsingContext)
    {
        if (browsingContext is null)
            return null;

        lock (_frames)
        {
            return _frames.GetValueOrDefault(browsingContext);
        }
    }

    internal void RegisterFrame(FrameDispatcher frame)
    {
        lock (_frames)
        {
            _frames[frame.BrowsingContext] = frame;
        }
    }

    internal void UnregisterFrame(FrameDispatcher frame)
    {
        lock (_frames)
        {
            _frames.Remove(frame.BrowsingContext);
        }
    }

    internal void RemovePage(PageDispatcher page)
    {
        lock (_pages)
        {
            _pages.Remove(page);
            _pagesByBrowsingContext.Remove(page.BrowsingContext);
        }
    }

    public InitScript[] GetInitScripts()
    {
        lock (_initScripts)
        {
            return [.. _initScripts];
        }
    }

    private void RemoveInitScript(InitScript script)
    {
        lock (_initScripts)
        {
            _initScripts.Remove(script);
        }
    }

    public BindingRegistration[] GetBindings()
    {
        lock (_bindings)
        {
            return [.. _bindings.Values];
        }
    }

    /// <summary>Exposes a function to the pages of the context (<paramref name="page"/> is null) or to a page.</summary>
    public BindingRegistration ExposeBinding(PageDispatcher? page, string name)
    {
        var binding = new BindingRegistration(name, page);
        lock (_bindings)
        {
            if (_bindings.ContainsKey(name) || GetPages().Any(p => p.HasBinding(name)))
                throw new PlaywrightError($"Function \"{name}\" has been already registered");

            if (page is null)
            {
                _bindings.Add(name, binding);
            }
        }

        // The function is defined in the documents that are already loaded
        Engine.Post(_ =>
        {
            foreach (var p in GetPages())
            {
                if (page is null || ReferenceEquals(p, page))
                {
                    p.InstallBindingInFrames(binding);
                }
            }
        }, state: null);
        return binding;
    }

    private void RemoveBinding(BindingRegistration binding)
    {
        lock (_bindings)
        {
            _bindings.Remove(binding.Name);
        }
    }

    /// <summary>Closes the context: its pages are closed, and its thread is stopped.</summary>
    public async Task CloseAsync(string? reason)
    {
        if (_closed)
            return;

        _closed = true;
        await Engine.InvokeAsync(() =>
        {
            foreach (var page in GetPages())
            {
                page.CloseFromContext(reason);
            }
        }).ConfigureAwait(false);

        SendEvent("close");
        Browser.RemoveContext(this);
        Dispose();
    }

    /// <summary>Stops the thread of the context (the connection is closed).</summary>
    public void Shutdown()
    {
        _closed = true;
        var engine = Engine;
        engine.Post(_ =>
        {
            foreach (var page in GetPages())
            {
                page.BrowsingContext.Dispose();
            }

            _scriptEngine?.Dispose();
        }, state: null);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            engine.Dispose();
        });
    }

    protected override void OnDisposed()
    {
        Shutdown();
    }
}

/// <summary>A script evaluated in each document before its scripts (<c>addInitScript</c>).</summary>
internal sealed class InitScript(string source)
{
    public string Source { get; } = source;
}

/// <summary>A function exposed to the pages (<c>exposeBinding</c>): the calls are given to the client (<c>BindingCall</c>).</summary>
internal sealed class BindingRegistration(string name, PageDispatcher? page)
{
    public string Name { get; } = name;

    /// <summary>Gets the page of a binding exposed by <c>page.exposeBinding()</c>, or null for a binding of the context.</summary>
    public PageDispatcher? Page { get; } = page;
}
