using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>
/// The API requests of a browser context (<c>context.request</c>): the requests are loaded by
/// <see cref="PlaywrightServerOptions.DocumentLoader"/>, with the cookies of the context.
/// </summary>
internal sealed class APIRequestContextDispatcher : Dispatcher
{
    private readonly ConcurrentDictionary<string, FetchResponse> _responses = new(StringComparer.Ordinal);
    private BrowserContextDispatcher? _context;
    private long _nextFetchUid;

    public APIRequestContextDispatcher(Dispatcher parent, TracingDispatcher tracing)
        : base(parent, "APIRequestContext", "request-context", new JsonObject { ["tracing"] = tracing.ToReference() })
    {
    }

    public void Initialize(BrowserContextDispatcher context) => _context = context;

    public FetchResponse? GetFetchResponse(string fetchUid) => _responses.GetValueOrDefault(fetchUid);

    public override async ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        switch (method)
        {
            case "fetch":
                return await FetchAsync(parameters, progress).ConfigureAwait(false);

            case "fetchResponseBody":
                var result = new JsonObject();
                if (_responses.TryGetValue(parameters.GetRequiredString("fetchUid"), out var response))
                {
                    result["binary"] = Convert.ToBase64String(response.Body);
                }

                return result;

            case "fetchLog":
                return new JsonObject { ["log"] = new JsonArray() };

            case "disposeAPIResponse":
                _responses.TryRemove(parameters.GetRequiredString("fetchUid"), out _);
                return null;

            case "storageState":
                if (_context is null)
                    return new JsonObject { ["cookies"] = new JsonArray(), ["origins"] = new JsonArray() };

                return await _context.Engine.InvokeAsync(_context.GetStorageState).ConfigureAwait(false);

            case "dispose":
                _responses.Clear();
                return null;

            default:
                throw new PlaywrightError($"APIRequestContext.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");
        }
    }

    private async Task<JsonObject> FetchAsync(JsonElement parameters, Progress progress)
    {
        var loader = Connection.Options.DocumentLoader ?? throw new PlaywrightError("The network is disabled: the server has no DocumentLoader (see PlaywrightServerOptions.DocumentLoader)");
        var url = parameters.GetRequiredString("url");
        if (_context?.Options.BaseUrl is { } baseUrl && Uri.TryCreate(new Uri(baseUrl), url, out var resolved))
        {
            url = resolved.ToString();
        }

        url = AppendQuery(url, parameters);
        var method = parameters.GetString("method") ?? "GET";
        var headers = new List<KeyValuePair<string, string>>();
        if (_context is not null)
        {
            headers.AddRange(_context.Options.ExtraHttpHeaders);
        }

        headers.AddRange(parameters.GetNameValues("headers"));
        var (body, contentType) = GetBody(parameters);
        if (contentType is not null && !headers.Exists(header => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add(new("Content-Type", contentType));
        }

        if (_context is not null && !headers.Exists(header => header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add(new("User-Agent", _context.Options.UserAgent ?? Navigator.DefaultUserAgent));
        }

        var request = new DocumentRequest(url)
        {
            Method = method,
            Headers = headers,
            Body = body,
            BodyContentType = contentType,
            CookieJar = _context?.Cookies,
            IncludeCredentials = true,
            FollowRedirects = parameters.GetInt32("maxRedirects") is not 0,
            Destination = RequestDestination.Empty,
            Mode = RequestMode.Cors,
        };

        DocumentResource? resource;
        try
        {
            resource = await progress.RaceAsync(loader.LoadAsync(request, progress.CancellationToken).AsTask()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlaywrightError)
        {
            throw new PlaywrightError(ex.Message, ex);
        }

        if (resource is null)
            throw new PlaywrightError("net::ERR_FAILED at " + url);

        byte[] responseBody;
        if (resource.ContentBytes is { } bytes)
        {
            using var stream = new MemoryStream();
            stream.Write(bytes.Span);
            if (resource.RemainingContentBytes is { } remaining)
            {
                await foreach (var chunk in remaining.WithCancellation(progress.CancellationToken).ConfigureAwait(false))
                {
                    stream.Write(chunk.Span);
                }
            }

            responseBody = stream.ToArray();
        }
        else
        {
            responseBody = Encoding.UTF8.GetBytes(resource.Content ?? "");
        }

        var fetchUid = Interlocked.Increment(ref _nextFetchUid).ToString(CultureInfo.InvariantCulture);
        var fetchResponse = new FetchResponse(resource.StatusCode, resource.Headers, responseBody);
        _responses[fetchUid] = fetchResponse;
        if (parameters.GetBoolean("failOnStatusCode", defaultValue: false) && resource.StatusCode is < 200 or >= 400)
            throw new PlaywrightError($"{resource.StatusCode} {resource.StatusText}");

        return new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["fetchUid"] = fetchUid,
                ["url"] = resource.Url ?? url,
                ["status"] = resource.StatusCode,
                ["statusText"] = resource.StatusText ?? "",
                ["headers"] = JsonExtensions.ToNameValues(resource.Headers),
            },
        };
    }

    private static string AppendQuery(string url, JsonElement parameters)
    {
        var query = parameters.GetString("encodedParams");
        if (query is null && parameters.TryGet("params", out _))
        {
            query = string.Join('&', parameters.GetNameValues("params").Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        }

        if (string.IsNullOrEmpty(query))
            return url;

        return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query;
    }

    private static (byte[]? Body, string? ContentType) GetBody(JsonElement parameters)
    {
        if (parameters.GetBinary("postData") is { } postData)
            return (postData, "application/octet-stream");

        if (parameters.GetString("jsonData") is { } json)
            return (Encoding.UTF8.GetBytes(json), "application/json");

        if (parameters.TryGet("formData", out _))
        {
            var form = string.Join('&', parameters.GetNameValues("formData").Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
            return (Encoding.UTF8.GetBytes(form), "application/x-www-form-urlencoded");
        }

        if (parameters.TryGet("multipartData", out _))
        {
            var boundary = "----PlaywrightFormBoundary" + System.Guid.NewGuid().ToString("N");
            using var stream = new MemoryStream();
            foreach (var field in parameters.GetArray("multipartData"))
            {
                var name = field.GetString("name") ?? "";
                Write(stream, "--" + boundary + "\r\n");
                if (field.TryGet("file", out var file))
                {
                    Write(stream, $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{file.GetString("name")}\"\r\n");
                    Write(stream, "Content-Type: " + (file.GetString("mimeType") ?? "application/octet-stream") + "\r\n\r\n");
                    stream.Write(file.GetBinary("buffer") ?? []);
                    Write(stream, "\r\n");
                }
                else
                {
                    Write(stream, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
                    Write(stream, (field.GetString("value") ?? "") + "\r\n");
                }
            }

            Write(stream, "--" + boundary + "--\r\n");
            return (stream.ToArray(), "multipart/form-data; boundary=" + boundary);
        }

        return (null, null);

        static void Write(MemoryStream stream, string text) => stream.Write(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>A response of an API request, kept until the client disposes it (its body, and the fulfillment of a route with it).</summary>
internal sealed record FetchResponse(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body);
