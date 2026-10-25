using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>The root object of a connection (guid ""): its <c>initialize</c> method creates the <c>Playwright</c> object.</summary>
internal sealed class RootDispatcher(PlaywrightConnection connection) : Dispatcher(connection, "Root", "")
{
    private PlaywrightDispatcher? _playwright;

    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        if (method != "initialize")
            return base.InvokeAsync(method, parameters, progress);

        if (_playwright is not null)
            throw new PlaywrightError("The connection is already initialized");

        _playwright = new PlaywrightDispatcher(this, parameters.GetString("sdkLanguage") ?? "javascript");
        return ValueTask.FromResult<JsonNode?>(new JsonObject { ["playwright"] = _playwright.ToReference() });
    }
}

/// <summary>An object of the protocol whose methods are not implemented (e.g. <c>Android</c>, <c>Electron</c>).</summary>
internal sealed class StubDispatcher(Dispatcher parent, string type, string guidPrefix, JsonObject? initializer = null) : Dispatcher(parent, type, guidPrefix, initializer)
{
    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
        => throw new PlaywrightError($"{Type}.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");
}

/// <summary>The <c>Playwright</c> object: the browser types, and the pre-launched browser of the connection.</summary>
internal sealed class PlaywrightDispatcher : Dispatcher
{
    public PlaywrightDispatcher(RootDispatcher parent, string sdkLanguage)
        : base(parent, "Playwright", "Playwright", CreateInitializer(parent, sdkLanguage, out var browser))
    {
        Browser = browser;
    }

    public BrowserDispatcher Browser { get; }

    private static JsonObject CreateInitializer(RootDispatcher root, string sdkLanguage, out BrowserDispatcher browser)
    {
        var options = root.Connection.Options;
        var chromium = new BrowserTypeDispatcher(root, "chromium");
        var firefox = new BrowserTypeDispatcher(root, "firefox");
        var webkit = new BrowserTypeDispatcher(root, "webkit");
        var android = new StubDispatcher(root, "Android", "android");
        var electron = new StubDispatcher(root, "Electron", "electron");
        var browserName = root.Connection.RequestedBrowserName is "chromium" or "firefox" or "webkit" ? root.Connection.RequestedBrowserName : options.BrowserName;
        var browserType = browserName switch
        {
            "firefox" => firefox,
            "webkit" => webkit,
            _ => chromium,
        };
        browser = new BrowserDispatcher(browserType, browserType.Name, sdkLanguage);
        return new JsonObject
        {
            ["chromium"] = chromium.ToReference(),
            ["firefox"] = firefox.ToReference(),
            ["webkit"] = webkit.ToReference(),
            ["android"] = android.ToReference(),
            ["electron"] = electron.ToReference(),
            ["preLaunchedBrowser"] = browser.ToReference(),
        };
    }

    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
        => throw new PlaywrightError($"Playwright.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");
}

/// <summary>A browser type (<c>chromium</c>, <c>firefox</c>, <c>webkit</c>): the browser is pre-launched, so it cannot launch browsers.</summary>
internal sealed class BrowserTypeDispatcher(RootDispatcher parent, string name) : Dispatcher(parent, "BrowserType", "browser-type", new JsonObject { ["executablePath"] = "", ["name"] = name })
{
    public string Name { get; } = name;

    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
        => throw new PlaywrightError($"BrowserType.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform: use BrowserType.connect()");
}
