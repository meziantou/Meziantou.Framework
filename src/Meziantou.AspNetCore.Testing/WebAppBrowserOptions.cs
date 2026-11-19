using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.AspNetCore.Testing;

/// <summary>Configures a <see cref="WebAppBrowser"/> and its pages.</summary>
public sealed class WebAppBrowserOptions
{
    /// <summary>Gets the size of the viewport of the pages, in CSS pixels. The default is 1280x720.</summary>
    public ViewportOptions Viewport { get; init; } = new() { Width = 1280, Height = 720 };

    /// <summary>Gets whether the pages run their scripts. The default is true.</summary>
    public bool EnableJavaScript { get; init; } = true;

    /// <summary>
    /// Gets the runtime that compiles and runs the WebAssembly modules of the scripts of the pages, or null: the
    /// <c>WebAssembly</c> namespace is then not defined, so a Blazor WebAssembly application does not start. The
    /// <c>Meziantou.Framework.WebPlatform.Wasmtime</c> package provides a runtime (<c>WasmtimeRuntime</c>). The pages share
    /// it, and the browser does not dispose it.
    /// </summary>
    public WebAssemblyRuntime? WebAssemblyRuntime { get; init; }

    /// <summary>
    /// Gets the clock of the pages, or null for the system clock: the clock of the timers (<c>setTimeout()</c>, the
    /// animations...) and of the dates (<c>Date.now()</c>, <c>new Date()</c>, and <c>DateTime.Now</c> in a Blazor
    /// WebAssembly application), including the local time zone of the dates (<see cref="TimeProvider.LocalTimeZone"/>).
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Gets the maximum time the waits of the pages wait for. The default is 10 seconds.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets a function that returns whether a request is long-lived: such a request does not prevent a page from being
    /// idle. The default is <see cref="IsDefaultLongLivedRequest"/>.
    /// </summary>
    public Func<DocumentRequest, bool>? IsLongLivedRequest { get; init; } = IsDefaultLongLivedRequest;

    /// <summary>
    /// Returns whether a request is long-lived by default: the server-sent events (the requests that accept
    /// <c>text/event-stream</c>) and the long polling of SignalR (a <c>GET</c> request with the <c>id</c> and <c>_</c> query
    /// parameters, which Blazor Server uses when WebSockets are not available).
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>True when the request is long-lived.</returns>
    public static bool IsDefaultLongLivedRequest(DocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Headers.Any(header => string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase) && header.Value.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (request.Method == "GET" && Uri.TryCreate(request.Url, UriKind.Absolute, out var url) && url.Query.Length > 1)
        {
            var hasId = false;
            var hasCacheBuster = false;
            foreach (var parameter in url.Query[1..].Split('&'))
            {
                var name = parameter.Split('=')[0];
                hasId |= name == "id";
                hasCacheBuster |= name == "_";
            }

            return hasId && hasCacheBuster;
        }

        return false;
    }

    /// <summary>
    /// Gets a function that changes the options of the browsing contexts of the pages, or null (see
    /// <see cref="TestPageSettings.ConfigureBrowsingContext"/>). It is called for each page: return the same
    /// <see cref="BrowsingContextOptions.Clipboard"/> (for instance a <see cref="FakeClipboard"/>) for the pages to share it.
    /// </summary>
    public Func<BrowsingContextOptions, BrowsingContextOptions>? ConfigureBrowsingContext { get; init; }

    /// <summary>Gets the name of the attribute that <see cref="LocatorScope.GetByTestId(string)"/> matches. The default is <c>data-testid</c>.</summary>
    public string TestIdAttribute { get; init; } = "data-testid";
}
