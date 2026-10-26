using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The Python handlers of the WPT server (<c>.py</c> files), emulated in .NET: each handler is a port of the
/// <c>main(request, response)</c> function of its file. The handler sees the request as wptserve does (the headers sent by
/// the browser, see <see cref="WptHandlerRequest"/>), and fills the response; the runner then applies the pipes of the URL
/// (<see cref="WptPipes"/>), the delays and the cookies. A request of a handler that is not emulated is a <c>404</c>
/// response. The handlers are grouped by directory: the generic handlers of <c>common</c> (this file), the ones of the
/// <c>xhr</c> and <c>fetch</c> tests (<c>WptPythonHandlers.Fetch.cs</c>), the ones of
/// <c>common/security-features</c> (<c>WptPythonHandlers.SecurityFeatures.cs</c>), the ones of the <c>content-security-policy</c>
/// tests and of the violation reports (<c>WptPythonHandlers.ContentSecurityPolicy.cs</c>), the ones of the <c>beacon</c> tests
/// (<c>WptPythonHandlers.Beacon.cs</c>), the ones of the <c>client-hints</c> tests (<c>WptPythonHandlers.ClientHints.cs</c>), the ones of the <c>css</c> tests (<c>WptPythonHandlers.Css.cs</c>), the ones of the paint
/// timing tests (<c>WptPythonHandlers.PaintTiming.cs</c>), the ones of the <c>resource-timing</c> and <c>navigation-timing</c> tests
/// (<c>WptPythonHandlers.Timing.cs</c>), the event streams of the <c>eventsource</c> tests (<c>WptPythonHandlers.EventSource.cs</c>),
/// the ones of the <c>preload</c> tests (<c>WptPythonHandlers.Preload.cs</c>), the ones of the <c>worklets</c> tests
/// (<c>WptPythonHandlers.Worklets.cs</c>), and the other
/// ones (<c>WptPythonHandlers.Html.cs</c>).
/// </summary>
internal static partial class WptPythonHandlers
{
    private delegate void Handler(WptHandlerRequest request, WptHandlerResponse response, WptStash stash);

    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, Handler> CommonHandlers = new(StringComparer.Ordinal)
    {
        ["/common/redirect.py"] = (request, response, _) =>
        {
            response.Status = ParseInt(request.Get("status"), 302);
            response.Set("Location", request.Get("location"));
            if (request.Get("enable-cors") is not null && request.Header("Origin") is { Length: > 0 } origin)
            {
                response.Set("Content-Type", "text/plain");
                response.Set("Access-Control-Allow-Origin", origin);
                response.Set("Access-Control-Allow-Credentials", "true");
            }
        },
        ["/common/redirect-opt-in.py"] = (request, response, _) =>
        {
            response.Status = ParseInt(request.Get("status"), 302);
            response.Set("Location", request.Get("location"));
            response.Set("Timing-Allow-Origin", "*");
        },
        ["/common/slow-redirect.py"] = (request, response, _) =>
        {
            response.Status = ParseInt(request.Get("status"), 302);
            response.Delay = TimeSpan.FromSeconds(ParseInt(request.Get("delay"), 2));
            response.Set("Location", request.Get("location"));
        },

        // An empty response, delayed. wptserve sends no Content-Type: the browsers sniff an HTML document.
        ["/common/slow.py"] = (request, response, _) =>
        {
            response.Delay = Milliseconds(request.Get("delay"), 2000);
            response.Set("Content-Type", "text/html");
        },
        ["/common/echo.py"] = (request, response, _) =>
        {
            response.Set("X-XSS-Protection", "0");
            response.Set("Content-Type", "text/html");
            response.Content = request.Get("content") ?? "";
        },
        ["/common/refresh.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            response.Set("Refresh", "0; URL=" + request.Get("location"));
        },
        ["/common/CustomCorsResponse.py"] = CustomCorsResponse,
        ["/common/dispatcher/dispatcher.py"] = Dispatcher,
        ["/scroll-to-text-fragment/stash.py"] = (request, response, stash) =>
        {
            // The results that the target page of a text directive posts for the test page
            var key = request.Get("key") ?? "";
            if (request.Method == "POST")
            {
                stash.Put(key, Encoding.UTF8.GetString(request.Body), "/scroll-to-text-fragment/");
                TextResponse(response, "ok");
            }
            else
            {
                TextResponse(response, stash.TakeValue(key, "/scroll-to-text-fragment/") as string ?? "");
            }
        },
    };

    // The static field initializers of the partial declarations run before the static constructor
    static WptPythonHandlers()
    {
        foreach (var handlers in new[] { CommonHandlers, SpeculationRulesHandlers, BeaconHandlers, ClientHintsHandlers, CorsHandlers, ContentSecurityPolicyHandlers, CookieHandlers, CrossOriginIsolationHandlers, CssHandlers, FetchHandlers, FetchMetadataHandlers, HtmlHandlers, MediaHandlers, PaintTimingHandlers, SecurityFeaturesHandlers, ServiceWorkersHandlers, TimingHandlers, WebSocketsHandlers, EventSourceHandlers, PreloadHandlers, WorkletsHandlers })
        {
            foreach (var (path, handler) in handlers)
            {
                Handlers.Add(path, handler);
            }
        }
    }

    /// <summary>Whether the URL is the one of a Python handler (a <c>.py</c> file).</summary>
    public static bool IsHandler(Url url) => Uri.UnescapeDataString(url.Pathname).EndsWith(".py", StringComparison.Ordinal);

    /// <summary>
    /// Runs the handler of a request, or returns null when the handler is not emulated. A handler that polls the stash sets
    /// <see cref="WptHandlerResponse.RetryAfter"/>: the runner runs it again after the delay.
    /// </summary>
    public static WptHandlerResponse? Handle(WptHandlerRequest request, WptStash stash)
    {
        // The path is the one of a file: the empty segments are ignored (e.g. /cors//resources/status.py)
        var path = Uri.UnescapeDataString(request.Path);
        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        if (!Handlers.TryGetValue(path, out var handler))
            return null;

        var response = new WptHandlerResponse();
        handler(request, response, stash);
        return response;
    }

    private static void Respond(WptHandlerResponse response, string contentType, byte[] body)
    {
        response.Add("Content-Type", contentType);
        response.Body = body;
    }

    private static void TextResponse(WptHandlerResponse response, string content)
    {
        response.Set("Content-Type", "text/plain");
        response.Content = content;
    }

    private static TimeSpan Milliseconds(string? value, double defaultValue) => TimeSpan.FromMilliseconds(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds) ? milliseconds : defaultValue);

    /// <summary><c>request.GET[name]</c> in Python: a missing parameter is a <c>KeyError</c>, so the response is a <c>500</c>.</summary>
    private static string RequiredParameter(WptHandlerRequest request, string name) => request.Get(name) ?? throw new KeyNotFoundException("The " + name + " parameter is missing");

    private static int ParseInt(string? value, int defaultValue) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static string? GetPost(WptHandlerRequest request, string name) => request.Post.Find(field => field.Key == name) is { Key: not null } field ? field.Value.StringValue : null;

    /// <summary>A byte string (a parameter or a header of the request) decoded as UTF-8, like <c>value.decode("utf-8")</c> in Python.</summary>
    private static string DecodeUtf8(string value) => Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));

    /// <summary><c>json.dumps(value)</c> of a string in Python: the non-ASCII characters are escaped.</summary>
    private static string PythonJsonString(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    result.Append("\\\"");
                    break;
                case '\\':
                    result.Append("\\\\");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\r':
                    result.Append("\\r");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                case '\b':
                    result.Append("\\b");
                    break;
                case '\f':
                    result.Append("\\f");
                    break;
                case < ' ' or > '~':
                    result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }

        return result.Append('"').ToString();
    }

    /// <summary><c>html.escape(value)</c> in Python.</summary>
    private static string PythonHtmlEscape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#x27;", StringComparison.Ordinal);

    // common/CustomCorsResponse.py: the headers (a JSON object), the content (a JSON string) and the status (a JSON number)
    // of the query
    private static void CustomCorsResponse(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Get("headers") is { } headers)
        {
            using var document = JsonDocument.Parse(DecodeUtf8(headers));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in document.RootElement.EnumerateObject())
                {
                    response.Set(header.Name, header.Value.GetString());
                }
            }
        }

        response.Body = Encoding.UTF8.GetBytes(request.Get("content") is { } content ? JsonSerializer.Deserialize<string>(DecodeUtf8(content)) ?? "" : "/* CustomCorsResponse.py content */");
        response.Status = request.Get("status") is { } status ? JsonSerializer.Deserialize<int>(status) : 200;
    }

    /// <summary>The path of the stash of <c>common/dispatcher/dispatcher.py</c>.</summary>
    private const string DispatcherStashPath = "/common/dispatcher";

    // common/dispatcher/dispatcher.py: a message queue per uuid, used by RemoteContext and the executors
    // (common/dispatcher/dispatcher.js). A POST request appends its body to the queue, a GET request removes the first
    // message ("not ready" when the queue is empty), and the show-headers parameter appends the headers of the request
    // (a JSON object).
    private static void Dispatcher(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Credentials", "true");
        response.Set("Access-Control-Allow-Methods", "OPTIONS, GET, POST");
        response.Set("Access-Control-Allow-Headers", "Content-Type");
        response.Set("Access-Control-Allow-Origin", request.Header("Origin") ?? "*");
        response.Set("Cache-Control", request.Has("cacheable") ? "max-age=31536000" : "no-cache, no-store, must-revalidate");
        if (request.Method == "OPTIONS")
            return;

        var uuid = request.Get("uuid") ?? throw new InvalidOperationException("The uuid parameter is missing");
        var queue = stash.TakeValue(uuid, DispatcherStashPath) as List<byte[]> ?? [];
        if (request.Has("show-headers"))
        {
            // The names of the headers are lowercase (wptserve's RequestHeaders), and their values are joined by ", "
            var headers = new List<string>();
            foreach (var name in request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
            {
                headers.Add(PythonJsonString(name) + ": " + PythonJsonString(request.Header(name) ?? ""));
            }

            queue.Add(Encoding.UTF8.GetBytes("{" + string.Join(", ", headers) + "}"));
        }
        else if (request.Method == "POST")
        {
            queue.Add(request.Body);
            response.Content = "done";
        }
        else if (queue.Count == 0)
        {
            response.Content = "not ready";
        }
        else
        {
            response.Body = queue[0];
            queue.RemoveAt(0);
        }

        stash.Put(uuid, queue, DispatcherStashPath);
    }
}
