using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>Configures a <see cref="WebAppBrowser"/> and its pages.</summary>
public sealed class WebAppBrowserOptions
{
    /// <summary>Gets the size of the viewport of the pages, in CSS pixels. The default is 1280x720.</summary>
    public ViewportOptions Viewport { get; init; } = new() { Width = 1280, Height = 720 };

    /// <summary>Gets whether the pages run their scripts. The default is true.</summary>
    public bool EnableJavaScript { get; init; } = true;

    /// <summary>Gets the clock of the timers of the pages, or null for the system clock.</summary>
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

    /// <summary>Gets the name of the attribute that <see cref="LocatorScope.GetByTestId(string)"/> matches. The default is <c>data-testid</c>.</summary>
    public string TestIdAttribute { get; init; } = "data-testid";
}
