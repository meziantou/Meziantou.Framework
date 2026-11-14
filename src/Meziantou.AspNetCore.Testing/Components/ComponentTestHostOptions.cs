using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>Configures the browser of a <see cref="ComponentTestHost"/>.</summary>
public sealed class ComponentTestHostOptions
{
    /// <summary>
    /// Gets the URL of the page that hosts the components. It is the base URI of the <c>NavigationManager</c>. The default
    /// is <c>https://localhost/</c>.
    /// </summary>
    public string BaseUrl { get; init; } = "https://localhost/";

    /// <summary>
    /// Gets the HTML of the page that hosts the components: add the style sheets of the application in its <c>head</c>
    /// (<c>&lt;link rel=stylesheet&gt;</c> elements are loaded by <see cref="DocumentLoader"/>). The components are rendered
    /// in its <c>body</c> by default.
    /// </summary>
    public string HostPage { get; init; } = """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body></body>
        </html>
        """;

    /// <summary>Gets the CSS style sheets added to the host page, as the content of <c>&lt;style&gt;</c> elements.</summary>
    public IList<string> StyleSheets { get; } = [];

    /// <summary>
    /// Gets the folders that serve the static files of the host page (style sheets, images, fonts, scripts), by URL path
    /// prefix: <c>{ "/" : "wwwroot" }</c> serves <c>https://localhost/css/app.css</c> from <c>wwwroot/css/app.css</c>.
    /// Ignored when <see cref="DocumentLoader"/> is set.
    /// </summary>
    public IDictionary<string, string> StaticFiles { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the loader of the resources of the page. When it is null, the <see cref="StaticFiles"/> are served.</summary>
    public DocumentLoader? DocumentLoader { get; init; }

    /// <summary>Gets the size of the viewport, in CSS pixels. The default is 1280x720.</summary>
    public ViewportOptions Viewport { get; init; } = new() { Width = 1280, Height = 720 };

    /// <summary>
    /// Gets whether the page runs JavaScript (<see cref="Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngine"/>):
    /// the scripts of the host page run, and the JavaScript interop calls that are not set up in
    /// <see cref="ComponentTestHost.JSInterop"/> call the functions of the page.
    /// </summary>
    public bool EnableJavaScript { get; init; }

    /// <summary>Gets the clock of the timers of the page (<c>setTimeout()</c>, the CSS animations...). The default is the system clock.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Gets the maximum time to wait for the components and the page to be idle. The default is 10 seconds.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the factories of the <see cref="EventArgs"/> of the custom event types, by DOM event type. The built-in event
    /// types (<c>MouseEventArgs</c>, <c>KeyboardEventArgs</c>, <c>ChangeEventArgs</c>...) are created from the DOM events.
    /// </summary>
    public IDictionary<string, Func<Event, EventArgs>> CustomEventArgs { get; } = new Dictionary<string, Func<Event, EventArgs>>(StringComparer.Ordinal);

    /// <summary>Gets the name of the attribute that <see cref="LocatorScope.GetByTestId(string)"/> matches. The default is <c>data-testid</c>.</summary>
    public string TestIdAttribute { get; init; } = "data-testid";
}
