using Meziantou.Framework.WebPlatform.DevTools.Engine;
using Meziantou.Framework.WebPlatform.DevTools.Network;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Targets;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.Framework.WebPlatform.DevTools.Browser;

/// <summary>
/// A browser context: an isolated session (its own cookies, storage and script engine) whose pages run on their own thread
/// (<see cref="EngineThread"/>). The script engine and the options of the browsing contexts are created with the first page
/// that runs: the clients set the user agent, the locale and the time zone of a page (the <c>Emulation</c> domain) after the
/// page is created, before they let it run (<c>Runtime.runIfWaitingForDebugger</c>).
/// </summary>
internal sealed partial class BrowserContextHost : IDisposable
{
    private readonly List<PageTarget> _pages = [];
    private readonly Dictionary<BrowsingContext, FrameState> _frames = [];
    private DevToolsScriptEngine? _scriptEngine;
    private BrowsingContextOptions? _browsingContextOptions;
    private bool _closed;

    public BrowserContextHost(DevToolsBrowser browser, string id, bool isDefault)
    {
        Browser = browser;
        Id = id;
        IsDefault = isDefault;
        Engine = new EngineThread("DevTools browser context " + id);
        Loader = new ContextLoader(this, browser.Options.DocumentLoader);
    }

    public DevToolsBrowser Browser { get; }

    public string Id { get; }

    public bool IsDefault { get; }

    public EngineThread Engine { get; }

    public ContextLoader Loader { get; }

    public CookieJar Cookies { get; } = new();

    public StorageShed LocalStorage { get; } = new();

    public bool IsClosed => _closed;

    public DevToolsScriptEngine ScriptEngine => _scriptEngine ?? throw new InvalidOperationException("The browser context is not initialized");

    public bool IsInitialized => _browsingContextOptions is not null;

    /// <summary>
    /// Creates the script engine and the options of the browsing contexts (on the thread of the context), with the emulation
    /// settings of the first page that runs: the user agent, the locale and the time zone are the ones of the script engine
    /// and of the options, shared by the pages of the context.
    /// </summary>
    public BrowsingContextOptions GetBrowsingContextOptions(PageEmulation emulation)
    {
        if (_browsingContextOptions is not null)
            return _browsingContextOptions;

        _scriptEngine = new DevToolsScriptEngine(this, CreateScriptEngineOptions(Browser.Options.ScriptEngineOptions, emulation));
        Engine.ScriptEngine = _scriptEngine.Engine;
        _browsingContextOptions = new BrowsingContextOptions
        {
            ScriptEngine = _scriptEngine,
            ScriptingEnabled = !emulation.ScriptExecutionDisabled,
            DocumentLoader = Loader,
            CookieJar = Cookies,
            LocalStorage = LocalStorage,
            RenderingBackend = new ImagingRenderingBackend(),
            UseSystemFonts = Browser.Options.UseSystemFonts,
            UserAgent = emulation.UserAgent,
            Languages = emulation.GetLanguages(),
            Viewport = emulation.CreateViewport(),
            BrowsingContextCreated = OnBrowsingContextCreated,
            BrowsingContextDestroyed = OnBrowsingContextDestroyed,
            DocumentActivated = OnDocumentActivatedCallback,
            SameDocumentNavigated = OnSameDocumentNavigated,
            ConsoleMessage = OnConsoleMessage,
            UnhandledScriptError = OnUnhandledScriptError,
            UserPromptHandler = OnUserPrompt,
            ShowFilePicker = OnShowFilePicker,
            LifecycleEventFired = OnLifecycleEventFired,
        };
        return _browsingContextOptions;
    }

    private static JavaScriptScriptEngineOptions CreateScriptEngineOptions(JavaScriptScriptEngineOptions? options, PageEmulation emulation)
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
                if (emulation.TimezoneId is { } timeZone)
                {
                    engineOptions.TimeZone = timeZone;
                }

                if (emulation.Locale is { } locale)
                {
                    engineOptions.DefaultLocale = locale;
                }

                options.ConfigureEngine?.Invoke(engineOptions);
            },
        };
    }

    /// <summary>Creates a page (<c>Target.createTarget</c>): it runs once the client lets it run, or at once when no client waits for it.</summary>
    public Task<PageTarget> CreatePageAsync(string url)
    {
        if (_closed)
            throw new ProtocolException("Failed to find browser context with id " + Id);

        return Engine.InvokeAsync(() =>
        {
            var target = new PageTarget(this, browsingContext: null, opener: null, pendingUrl: url);
            AddPage(target);
            if (!Browser.AddTarget(target))
            {
                target.Resume();
            }

            return target;
        });
    }

    internal void AddPage(PageTarget page)
    {
        lock (_pages)
        {
            _pages.Add(page);
        }
    }

    internal void RemovePage(PageTarget page)
    {
        lock (_pages)
        {
            _pages.Remove(page);
        }
    }

    public PageTarget[] GetPages()
    {
        lock (_pages)
        {
            return [.. _pages];
        }
    }

    public FrameState? FindFrame(BrowsingContext? browsingContext)
    {
        if (browsingContext is null)
            return null;

        lock (_frames)
        {
            return _frames.GetValueOrDefault(browsingContext);
        }
    }

    internal void RegisterFrame(FrameState frame)
    {
        lock (_frames)
        {
            _frames[frame.BrowsingContext!] = frame;
        }
    }

    internal void UnregisterFrame(FrameState frame)
    {
        if (frame.BrowsingContext is null)
            return;

        lock (_frames)
        {
            _frames.Remove(frame.BrowsingContext);
        }
    }

    /// <summary>Closes the context (<c>Target.disposeBrowserContext</c>): its pages are closed, and its thread is stopped.</summary>
    public async Task CloseAsync()
    {
        if (_closed)
            return;

        await Engine.InvokeAsync(() =>
        {
            foreach (var page in GetPages())
            {
                page.CloseFromContext();
            }
        }).ConfigureAwait(false);

        Dispose();
    }

    /// <summary>Stops the thread of the context (the context is disposed, or the connection is closed).</summary>
    public void Dispose()
    {
        if (_closed)
            return;

        _closed = true;
        var engine = Engine;
        engine.Post(_ =>
        {
            foreach (var page in GetPages())
            {
                page.CloseFromContext();
            }

            _scriptEngine?.Dispose();
        }, state: null);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            engine.Dispose();
        });
    }
}
