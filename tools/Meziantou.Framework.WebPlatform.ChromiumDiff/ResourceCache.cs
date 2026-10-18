using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Playwright;

namespace ChromiumDiff;

/// <summary>A response recorded by <see cref="ResourceCache"/>.</summary>
internal sealed record CachedResponse(int Status, KeyValuePair<string, string>[] Headers, string File);

/// <summary>
/// The subresources of a page (style sheets, images, fonts...), recorded once from the network by Chromium and replayed in
/// Chromium and in the library, so that both lay out the page with the same resources. The cache of a page is a directory
/// of <c>artifacts/chromium-diff/resources</c>: <c>index.json</c> maps the URLs to the recorded responses (status, headers
/// and the file of the body). Only the GET requests of http and https URLs are recorded; the navigations of the nested
/// browsing contexts (iframes) are not.
/// </summary>
internal sealed class ResourceCache
{
    // The headers of the responses that are replayed: the ones that change how the resources are used. The encodings
    // and the lengths are the ones of the decoded bodies, so they are not kept.
    private static readonly HashSet<string> ReplayedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "content-type",
        "access-control-allow-origin",
        "access-control-allow-credentials",
        "timing-allow-origin",
        "x-content-type-options",
        "cross-origin-resource-policy",
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    private readonly string _directory;
    private readonly Dictionary<string, CachedResponse> _responses;

    private ResourceCache(string directory, Dictionary<string, CachedResponse> responses)
    {
        _directory = directory;
        _responses = responses;
    }

    public int Count => _responses.Count;

    /// <summary>Returns the recorded responses of a page, or null when they were not recorded.</summary>
    public static ResourceCache? Load(string directory)
    {
        var index = Path.Combine(directory, "index.json");
        if (!File.Exists(index))
            return null;

        var responses = JsonSerializer.Deserialize<Dictionary<string, CachedResponse>>(File.ReadAllText(index)) ?? [];
        return new ResourceCache(directory, responses);
    }

    /// <summary>An empty cache: the page is laid out without subresources (the files of <c>--corpus</c>).</summary>
    public static ResourceCache Empty { get; } = new ResourceCache("", []);

    public bool TryGet(string url, out CachedResponse response, out byte[] body)
    {
        if (_responses.TryGetValue(url, out response!))
        {
            body = File.ReadAllBytes(Path.Combine(_directory, response.File));
            return true;
        }

        body = [];
        return false;
    }

    /// <summary>
    /// Loads the page in Chromium (the HTML of the page from <paramref name="html"/>, the subresources from the network)
    /// with the settings of the comparison, and records the responses of its subresources in <paramref name="directory"/>.
    /// </summary>
    public static async Task<ResourceCache> RecordAsync(IBrowserContext context, string url, byte[] html, string directory, Func<IPage, Task> prepare)
    {
        var temporary = directory + ".tmp";
        if (Directory.Exists(temporary))
            Directory.Delete(temporary, recursive: true);

        Directory.CreateDirectory(temporary);
        var responses = new Dictionary<string, CachedResponse>(StringComparer.Ordinal);
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (request.IsNavigationRequest)
            {
                if (request.Frame == page.MainFrame && request.Url == url)
                {
                    await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html; charset=utf-8", BodyBytes = html });
                }
                else
                {
                    await route.AbortAsync();
                }

                return;
            }

            if (request.Method != "GET" || !(request.Url.StartsWith("http:", StringComparison.Ordinal) || request.Url.StartsWith("https:", StringComparison.Ordinal)))
            {
                await route.AbortAsync();
                return;
            }

            try
            {
                var response = await route.FetchAsync(new RouteFetchOptions { Timeout = 30_000 });
                var body = await response.BodyAsync();
                var headers = response.Headers.Where(header => ReplayedHeaders.Contains(header.Key)).Select(header => new KeyValuePair<string, string>(header.Key, header.Value)).ToArray();
                lock (responses)
                {
                    if (!responses.ContainsKey(request.Url))
                    {
                        var file = responses.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin";
                        File.WriteAllBytes(Path.Combine(temporary, file), body);
                        responses[request.Url] = new CachedResponse(response.Status, headers, file);
                    }
                }

                await route.FulfillAsync(new RouteFulfillOptions { Response = response, BodyBytes = body });
            }
            catch (PlaywrightException ex)
            {
                Console.Error.WriteLine($"warning: cannot record {request.Url}: {ex.Message.Split('\n')[0]}");
                await route.AbortAsync();
            }
        });

        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 120_000 });
        await prepare(page);
        await page.CloseAsync();

        lock (responses)
        {
            File.WriteAllText(Path.Combine(temporary, "index.json"), JsonSerializer.Serialize(responses, JsonOptions));
        }

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.Move(temporary, directory);
        return Load(directory)!;
    }

    /// <summary>Replays the recorded responses in Chromium; the other requests (and the navigations of the iframes) fail.</summary>
    public async Task RouteAsync(IPage page, string url, byte[] html)
    {
        await page.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (request.IsNavigationRequest && request.Frame == page.MainFrame && request.Url == url)
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html; charset=utf-8", BodyBytes = html });
            }
            else if (!request.IsNavigationRequest && request.Method == "GET" && TryGet(request.Url, out var response, out var body))
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = response.Status,
                    Headers = response.Headers.Select(header => new KeyValuePair<string, string>(header.Key, header.Value)),
                    BodyBytes = body,
                });
            }
            else
            {
                await route.AbortAsync();
            }
        });
    }

    /// <summary>The loader of the library that replays the recorded responses; the other requests are network errors.</summary>
    public DocumentLoader CreateLoader() => new ReplayLoader(this);

    private sealed class ReplayLoader(ResourceCache cache) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            // The navigations of the nested browsing contexts are not recorded
            if (request.Mode == RequestMode.Navigate
                || request.Method != "GET"
                || !cache.TryGet(request.Url, out var response, out var body))
            {
                return ValueTask.FromResult<DocumentResource?>(null);
            }

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource(body) { StatusCode = response.Status, Headers = response.Headers });
        }
    }
}
