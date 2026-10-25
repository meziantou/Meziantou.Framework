using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Playwright;

namespace Meziantou.Framework.WebPlatform.Playwright.Tests;

/// <summary>
/// A Playwright server and a connected browser shared by the tests of a class. The pages of the tests are served by an
/// in-memory loader (<c>https://test.local/...</c>): nothing is fetched from the network.
/// </summary>
public sealed class PlaywrightServerFixture : IAsyncLifetime
{
    private readonly ConcurrentDictionary<string, Func<DocumentRequest, DocumentResource?>> _resources = new(StringComparer.Ordinal);
    private IPlaywright? _playwright;

    public PlaywrightServer Server { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public ConcurrentQueue<DocumentRequest> Requests { get; } = new();

    /// <summary>Serves an HTML page at an URL of the test server.</summary>
    public string AddPage(string path, string html) => AddResource(path, _ => new DocumentResource(html) { Headers = [new("Content-Type", "text/html; charset=utf-8")] });

    /// <summary>Serves a resource at an URL of the test server.</summary>
    public string AddResource(string path, Func<DocumentRequest, DocumentResource?> resource)
    {
        var url = "https://test.local/" + path.TrimStart('/');
        _resources[url] = resource;
        return url;
    }

    public async ValueTask InitializeAsync()
    {
        Server = new PlaywrightServer(new PlaywrightServerOptions { DocumentLoader = new TestLoader(this) });
        await Server.StartAsync();
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await _playwright.Chromium.ConnectAsync(Server.WsEndpoint.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();
        if (Server is not null)
        {
            await Server.DisposeAsync();
        }
    }

    /// <summary>Creates a context and a page; the context is closed with the returned scope.</summary>
    public async Task<(IBrowserContext Context, IPage Page)> NewPageAsync(BrowserNewContextOptions? options = null)
    {
        var context = await Browser.NewContextAsync(options);
        var page = await context.NewPageAsync();
        return (context, page);
    }

    private sealed class TestLoader(PlaywrightServerFixture fixture) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            fixture.Requests.Enqueue(request);
            var url = request.Url;
            var query = url.IndexOf('?', StringComparison.Ordinal);
            if (fixture._resources.TryGetValue(url, out var resource) || (query >= 0 && fixture._resources.TryGetValue(url[..query], out resource)))
                return ValueTask.FromResult(resource(request));

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("Not found") { StatusCode = 404, StatusText = "Not Found", Headers = [new("Content-Type", "text/plain")] });
        }
    }
}
