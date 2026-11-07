using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>The browser of a connection (the pre-launched browser): it creates the browser contexts.</summary>
internal sealed class BrowserDispatcher : Dispatcher
{
    private readonly List<BrowserContextDispatcher> _contexts = [];

    public BrowserDispatcher(BrowserTypeDispatcher parent, string browserName, string sdkLanguage)
        : base(parent, "Browser", "browser", new JsonObject { ["version"] = Version, ["name"] = browserName, ["browserName"] = browserName })
    {
        BrowserName = browserName;
        SdkLanguage = sdkLanguage;
    }

    /// <summary>The version of the browser (the version of the library).</summary>
    public static string Version { get; } = typeof(BrowsingContext).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string BrowserName { get; }

    /// <summary>Gets the language of the client (<c>javascript</c>, <c>python</c>, <c>java</c>, <c>csharp</c>): the locators of the logs use its syntax.</summary>
    public string SdkLanguage { get; }

    public override async ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        switch (method)
        {
            case "newContext":
            case "newContextForReuse":
                var context = await BrowserContextDispatcher.CreateAsync(this, parameters).ConfigureAwait(false);
                lock (_contexts)
                {
                    _contexts.Add(context);
                }

                SendEvent("context", new JsonObject { ["context"] = context.ToReference() });
                return new JsonObject { ["context"] = context.ToReference() };

            case "close":
                await CloseContextsAsync(parameters.GetString("reason")).ConfigureAwait(false);
                SendEvent("close");
                Dispose();
                return null;

            case "killForTests":
            case "disconnectFromReusedContext":
            case "stopServer":
                return null;

            case "defaultUserAgentForTest":
                return new JsonObject { ["userAgent"] = Navigator.DefaultUserAgent };

            default:
                throw new PlaywrightError($"Browser.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");
        }
    }

    internal void RemoveContext(BrowserContextDispatcher context)
    {
        lock (_contexts)
        {
            _contexts.Remove(context);
        }
    }

    private async Task CloseContextsAsync(string? reason)
    {
        BrowserContextDispatcher[] contexts;
        lock (_contexts)
        {
            contexts = [.. _contexts];
        }

        foreach (var context in contexts)
        {
            await context.CloseAsync(reason).ConfigureAwait(false);
        }
    }

    protected override void OnDisposed()
    {
        BrowserContextDispatcher[] contexts;
        lock (_contexts)
        {
            contexts = [.. _contexts];
            _contexts.Clear();
        }

        foreach (var context in contexts)
        {
            context.Shutdown();
        }
    }
}
