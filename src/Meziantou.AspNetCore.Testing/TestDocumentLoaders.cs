using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.TestHost;

namespace Meziantou.AspNetCore.Testing;

/// <summary>Creates the loaders that connect the pages to an application in memory, to an HTTP handler, or to folders.</summary>
public static class TestDocumentLoaders
{
    /// <summary>
    /// Creates a loader that sends the requests of the pages to an in-memory ASP.NET Core application (<see cref="TestServer"/>,
    /// for instance <c>WebApplicationFactory.Server</c>). The cookies and the redirects are handled by the pages, as in a browser.
    /// </summary>
    /// <param name="server">The test server.</param>
    /// <returns>The loader.</returns>
    public static DocumentLoader FromTestServer(TestServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return FromHttpMessageHandler(server.CreateHandler());
    }

    /// <summary>
    /// Creates a loader that sends the requests of the pages to an HTTP message handler. The handler must not follow the
    /// redirects nor handle the cookies: the pages do it, as in a browser.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <returns>The loader.</returns>
    public static DocumentLoader FromHttpMessageHandler(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // The client lives as long as the loader: the pages send their requests until they are disposed
#pragma warning disable CA2000
        return DocumentLoader.Create(new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan });
#pragma warning restore CA2000
    }

    /// <summary>
    /// Creates a loader that serves the files of folders for the URLs of an origin, by URL path prefix: <c>{ "/" : "wwwroot" }</c>
    /// serves <c>https://localhost/css/app.css</c> from <c>wwwroot/css/app.css</c>, and <c>https://localhost/</c> from
    /// <c>wwwroot/index.html</c>. The other URLs are network errors.
    /// </summary>
    /// <param name="origin">The origin of the URLs (<c>https://localhost/</c>).</param>
    /// <param name="folders">The folders, by URL path prefix.</param>
    /// <returns>The loader.</returns>
    public static DocumentLoader FromDirectories(Uri origin, IEnumerable<KeyValuePair<string, string>> folders) => FromDirectories(origin, folders, fallbackPath: null);

    /// <summary>
    /// Creates a loader that serves the files of folders for the URLs of an origin, by URL path prefix: <c>{ "/" : "wwwroot" }</c>
    /// serves <c>https://localhost/css/app.css</c> from <c>wwwroot/css/app.css</c>, and <c>https://localhost/</c> from
    /// <c>wwwroot/index.html</c>. The URLs that match no file and whose path has no extension serve the file of
    /// <paramref name="fallbackPath"/>, as <c>MapFallbackToFile</c> does: the routes of a single-page application, such as a
    /// Blazor WebAssembly application, serve its page. The other URLs are network errors.
    /// </summary>
    /// <param name="origin">The origin of the URLs (<c>https://localhost/</c>).</param>
    /// <param name="folders">The folders, by URL path prefix.</param>
    /// <param name="fallbackPath">The URL path of the file served for the routes of the application (<c>/index.html</c>), or null.</param>
    /// <returns>The loader.</returns>
    public static DocumentLoader FromDirectories(Uri origin, IEnumerable<KeyValuePair<string, string>> folders, string? fallbackPath)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(folders);
        return new StaticFilesDocumentLoader(origin, [.. folders], fallbackPath);
    }
}
