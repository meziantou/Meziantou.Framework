using System.Globalization;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Loads a page whose subresources (images, style sheets and their @import rules) and nested navigations (iframes, through a
/// redirect) are fetched with an in-memory loader that completes synchronously: the cost of the fetch algorithm.
/// </summary>
[MemoryDiagnoser]
public class FetchBenchmarks
{
    private const string Origin = "https://example.com/";

    // The smallest valid PNG: the signature and the IHDR chunk of a 1x1 image
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89];

    private readonly InMemoryLoader _loader = new();
    private string _subresourcesPage = "";
    private string _navigationsPage = "";

    [GlobalSetup]
    public void Setup()
    {
        var subresources = new System.Text.StringBuilder("<!DOCTYPE html><title>Subresources</title>");
        for (var i = 0; i < 20; i++)
        {
            _loader.Add($"style{i}.css", "text/css", $"@import url(import{i}.css); .a{i} {{ color: red }}");
            _loader.Add($"import{i}.css", "text/css", $".b{i} {{ color: green }}");
            subresources.Append(CultureInfo.InvariantCulture, $"<link rel=stylesheet href=style{i}.css>");
        }

        for (var i = 0; i < 100; i++)
        {
            _loader.Add($"image{i}.png", "image/png", Png);
            subresources.Append(CultureInfo.InvariantCulture, $"<img src=image{i}.png>");
        }

        // Some images are redirected, from another origin
        for (var i = 0; i < 20; i++)
        {
            _loader.AddRedirect($"redirect-image{i}.png", $"https://cdn.example/image{i}.png");
            _loader.Add($"https://cdn.example/image{i}.png", "image/png", Png);
            subresources.Append(CultureInfo.InvariantCulture, $"<img src=redirect-image{i}.png>");
        }

        _subresourcesPage = subresources.ToString();

        var navigations = new System.Text.StringBuilder("<!DOCTYPE html><title>Navigations</title>");
        for (var i = 0; i < 20; i++)
        {
            _loader.AddRedirect($"redirect-frame{i}.html", $"frame{i}.html");
            _loader.Add($"frame{i}.html", "text/html", $"<!DOCTYPE html><p>Frame {i}</p>");
            navigations.Append(CultureInfo.InvariantCulture, $"<iframe src=redirect-frame{i}.html></iframe>");
        }

        _navigationsPage = navigations.ToString();
    }

    [Benchmark]
    public uint Subresources() => Load(_subresourcesPage).Images.Length;

    [Benchmark]
    public uint Navigations() => Load(_navigationsPage).QuerySelectorAll("iframe").Length;

    private Document Load(string html)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = _loader });
        var document = context.Navigate(Origin, _ => html);
        context.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>
    /// A loader of in-memory resources, which completes synchronously. A redirect is returned as a 302 response, or followed
    /// when the request asks the loader to follow the redirects.
    /// </summary>
    private sealed class InMemoryLoader : DocumentLoader
    {
        private readonly Dictionary<string, (string ContentType, byte[] Body)> _resources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _redirects = new(StringComparer.Ordinal);

        public void Add(string url, string contentType, string body) => Add(url, contentType, System.Text.Encoding.UTF8.GetBytes(body));

        public void Add(string url, string contentType, byte[] body) => _resources[Resolve(url)] = (contentType, body);

        public void AddRedirect(string url, string location) => _redirects[Resolve(url)] = Resolve(location);

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var url = request.Url;
            if (_redirects.TryGetValue(url, out var location))
            {
                if (!request.FollowRedirects)
                    return ValueTask.FromResult<DocumentResource?>(new DocumentResource(ReadOnlyMemory<byte>.Empty) { StatusCode = 302, Headers = [new("Location", location)] });

                url = location;
            }

            if (!_resources.TryGetValue(url, out var resource))
                return ValueTask.FromResult<DocumentResource?>(null);

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource(resource.Body)
            {
                Url = url == request.Url ? null : url,
                Headers = [new("Content-Type", resource.ContentType)],
            });
        }

        private static string Resolve(string url) => new Url(url, Origin).Href;
    }
}
