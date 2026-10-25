using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>The options of a browser context (the parameters of <c>Browser.newContext</c>).</summary>
internal sealed class ContextOptions
{
    /// <summary>The viewport of the pages when the client does not set one (the default viewport of Playwright).</summary>
    public const int DefaultViewportWidth = 1280;

    public const int DefaultViewportHeight = 720;

    public int ViewportWidth { get; private init; } = DefaultViewportWidth;

    public int ViewportHeight { get; private init; } = DefaultViewportHeight;

    public bool NoDefaultViewport { get; private init; }

    public int? ScreenWidth { get; private init; }

    public int? ScreenHeight { get; private init; }

    public double DeviceScaleFactor { get; private init; } = 1;

    public bool IsMobile { get; private init; }

    public bool HasTouch { get; private init; }

    public string? ColorScheme { get; private init; }

    public string? ReducedMotion { get; private init; }

    public string? ForcedColors { get; private init; }

    public string? Contrast { get; private init; }

    public string? UserAgent { get; private init; }

    public string? Locale { get; private init; }

    public string? TimezoneId { get; private init; }

    public bool JavaScriptEnabled { get; private init; } = true;

    public bool BypassCsp { get; private init; }

    public bool IgnoreHttpsErrors { get; private init; }

    public List<KeyValuePair<string, string>> ExtraHttpHeaders { get; set; } = [];

    public bool Offline { get; set; }

    public List<HttpCredentials> HttpCredentials { get; set; } = [];

    public string? BaseUrl { get; private init; }

    public bool StrictSelectors { get; private init; }

    public string TestIdAttributeName { get; set; } = "data-testid";

    public List<(string Name, string Source)> SelectorEngines { get; } = [];

    public JsonElement StorageState { get; private init; }

    public static ContextOptions Parse(JsonElement parameters)
    {
        var options = new ContextOptions
        {
            NoDefaultViewport = parameters.GetBoolean("noDefaultViewport", defaultValue: false),
            ViewportWidth = parameters.TryGet("viewport", out var viewport) ? viewport.GetInt32("width") ?? DefaultViewportWidth : DefaultViewportWidth,
            ViewportHeight = parameters.TryGet("viewport", out viewport) ? viewport.GetInt32("height") ?? DefaultViewportHeight : DefaultViewportHeight,
            ScreenWidth = parameters.TryGet("screen", out var screen) ? screen.GetInt32("width") : null,
            ScreenHeight = parameters.TryGet("screen", out screen) ? screen.GetInt32("height") : null,
            DeviceScaleFactor = parameters.GetDouble("deviceScaleFactor") ?? 1,
            IsMobile = parameters.GetBoolean("isMobile", defaultValue: false),
            HasTouch = parameters.GetBoolean("hasTouch", defaultValue: false),
            ColorScheme = parameters.GetString("colorScheme"),
            ReducedMotion = parameters.GetString("reducedMotion"),
            ForcedColors = parameters.GetString("forcedColors"),
            Contrast = parameters.GetString("contrast"),
            UserAgent = parameters.GetString("userAgent"),
            Locale = parameters.GetString("locale"),
            TimezoneId = parameters.GetString("timezoneId"),
            JavaScriptEnabled = parameters.GetBoolean("javaScriptEnabled", defaultValue: true),
            BypassCsp = parameters.GetBoolean("bypassCSP", defaultValue: false),
            IgnoreHttpsErrors = parameters.GetBoolean("ignoreHTTPSErrors", defaultValue: false),
            ExtraHttpHeaders = parameters.GetNameValues("extraHTTPHeaders"),
            Offline = parameters.GetBoolean("offline", defaultValue: false),
            HttpCredentials = [.. parameters.GetArray("httpCredentials").Select(Server.HttpCredentials.Parse)],
            BaseUrl = parameters.GetString("baseURL"),
            StrictSelectors = parameters.GetBoolean("strictSelectors", defaultValue: false),
            TestIdAttributeName = parameters.GetString("testIdAttributeName") ?? "data-testid",
            StorageState = parameters.TryGet("storageState", out var storageState) ? storageState.Clone() : default,
        };

        foreach (var engine in parameters.GetArray("selectorEngines"))
        {
            options.SelectorEngines.Add((engine.GetString("name") ?? "", engine.GetString("source") ?? ""));
        }

        return options;
    }

    /// <summary>Returns the viewport of the pages and the preferences of the user (the emulated media features).</summary>
    public ViewportOptions CreateViewport() => new()
    {
        Width = ViewportWidth,
        Height = ViewportHeight,
        ScreenWidth = ScreenWidth,
        ScreenHeight = ScreenHeight,
        DevicePixelRatio = DeviceScaleFactor,
        IsMobile = IsMobile,
        HasTouch = HasTouch,
        PreferredColorScheme = ColorScheme == "dark" ? PreferredColorScheme.Dark : PreferredColorScheme.Light,
        PrefersReducedMotion = ReducedMotion == "reduce",
        PreferredContrast = Contrast == "more" ? PreferredContrast.More : PreferredContrast.NoPreference,
        ForcedColors = ForcedColors == "active",
    };

    /// <summary>The preferred languages of the user: the locale of the context and its language (e.g. <c>fr-FR</c> and <c>fr</c>).</summary>
    public IReadOnlyList<string>? GetLanguages()
    {
        if (string.IsNullOrEmpty(Locale))
            return null;

        var dash = Locale.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 ? [Locale, Locale[..dash]] : [Locale];
    }
}

/// <summary>The credentials of the HTTP authentication of a browser context (<c>httpCredentials</c>).</summary>
internal sealed record HttpCredentials(string Username, string Password, string? Origin, string? Send)
{
    public static HttpCredentials Parse(JsonElement element) => new(element.GetString("username") ?? "", element.GetString("password") ?? "", element.GetString("origin"), element.GetString("send"));
}
