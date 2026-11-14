using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Microsoft.AspNetCore.TestHost;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// A browser connected to a web application: an ASP.NET Core application in memory (<see cref="TestServer"/>, for instance
/// <c>WebApplicationFactory.Server</c>), or any server. Its pages share their cookies.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs" ref:region="WebAppBrowser_Usage" ref:dedent="true">
/// // The application runs in memory: Program is the entry point of the application under test
/// await using var factory = new WebApplicationFactory&lt;Program&gt;();
/// await using var browser = WebAppBrowser.Create(factory.Server);
/// var page = await browser.OpenAsync("/");
/// await Expect(page).ToHaveTitleAsync("Home");
///
/// // Follow a link, fill a form and submit it: the POST, the redirect and the cookie go through the application
/// await page.GetByRole("link", "Sign in").ClickAsync();
/// await page.GetByLabel("Name").FillAsync("Ada");
/// await page.GetByRole("button", "Sign in").ClickAsync();
///
/// await Expect(page).ToHaveUrlAsync("/profile");
/// await Expect(page.Locator("#greeting")).ToHaveTextAsync("Hello Ada");
/// </code>
/// </example>
public sealed class WebAppBrowser : IAsyncDisposable, IDisposable
{
    private readonly TrackingDocumentLoader _loader;
    private readonly WebAppBrowserOptions _options;
    private readonly List<BrowserPage> _pages = [];

    /// <summary>Initializes a new instance of the <see cref="WebAppBrowser"/> class.</summary>
    /// <param name="baseAddress">The URL that the relative URLs of <see cref="OpenAsync"/> are resolved against.</param>
    /// <param name="loader">The loader of the requests of the pages (see <see cref="TestDocumentLoaders"/>).</param>
    /// <param name="options">The options, or null for the default options.</param>
    public WebAppBrowser(Uri baseAddress, DocumentLoader loader, WebAppBrowserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(loader);
        BaseAddress = baseAddress;
        _options = options ?? new WebAppBrowserOptions();
        _loader = new TrackingDocumentLoader(loader, _options.IsLongLivedRequest);
    }

    /// <summary>Gets the URL that the relative URLs of <see cref="OpenAsync"/> are resolved against.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets the cookies of the pages.</summary>
    public CookieJar Cookies { get; } = new();

    /// <summary>Gets the open pages.</summary>
    public IReadOnlyList<BrowserPage> Pages => [.. _pages];

    /// <summary>Creates a browser connected to an ASP.NET Core application in memory.</summary>
    /// <param name="server">The test server (<c>WebApplicationFactory.Server</c>).</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The browser.</returns>
    public static WebAppBrowser Create(TestServer server, WebAppBrowserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        return new WebAppBrowser(server.BaseAddress, TestDocumentLoaders.FromTestServer(server), options);
    }

    /// <summary>Creates a browser connected to a server over the network (HTTP).</summary>
    /// <param name="baseAddress">The URL of the application.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The browser.</returns>
    public static WebAppBrowser Create(Uri baseAddress, WebAppBrowserOptions? options = null) => new(baseAddress, DocumentLoader.Create(), options);

    /// <summary>Opens a new page, on <c>about:blank</c>.</summary>
    /// <returns>The page.</returns>
    public BrowserPage NewPage()
    {
        var page = new BrowserPage(this, new TestPageSettings
        {
            Viewport = _options.Viewport,
            DocumentLoader = _loader,
            CookieJar = Cookies,
            ScriptEngine = _options.EnableJavaScript ? new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { Timeout = _options.DefaultTimeout }) : null,
            TimeProvider = _options.TimeProvider,
            DefaultTimeout = _options.DefaultTimeout,
            TestIdAttribute = _options.TestIdAttribute,
        });
        _pages.Add(page);
        return page;
    }

    /// <summary>Opens a new page and navigates it to a URL, then waits until the document is loaded.</summary>
    /// <param name="url">The URL, relative to <see cref="BaseAddress"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The page.</returns>
    public async Task<BrowserPage> OpenAsync(string url = "", CancellationToken cancellationToken = default)
    {
        var page = NewPage();
        await page.GoToAsync(url, cancellationToken).ConfigureAwait(false);
        return page;
    }

    internal TrackingDocumentLoader.RequestCounters GetRequestCounters(BrowsingContext context) => _loader.GetCounters(context);

    internal void OnPageDisposed(BrowserPage page) => _pages.Remove(page);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var page in _pages.ToArray())
        {
            page.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var page in _pages.ToArray())
        {
            await page.DisposeAsync().ConfigureAwait(false);
        }
    }
}
