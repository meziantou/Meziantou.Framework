using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>preload</c> tests (<c>preload/resources/*.py</c>).</summary>
internal static partial class WptPythonHandlers
{
    /// <summary>The path of the stash of <c>preload/resources/stash-put.py</c> and <c>stash-take.py</c> (the directory of the handlers).</summary>
    private const string PreloadStashPath = "/preload/resources/";

    /// <summary>The key of the counter of <c>preload/resources/preload-count.py</c>.</summary>
    private const string PreloadCountKey = "a8697ae7-c8cb-4dbd-a8ef-27111dc7042f";

    private static readonly Dictionary<string, Handler> PreloadHandlers = new(StringComparer.Ordinal)
    {
        ["/preload/resources/cross-origin-module.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/javascript");
            response.Add("Access-Control-Allow-Origin", request.Header("Origin"));
            response.Add("Timing-Allow-Origin", request.Header("Origin"));
            response.Add("Access-Control-Allow-Credentials", "true");
            response.Content = "// Cross-origin module, nothing to see here";
        },

        // echo-preload-header.py: a response with the Content-Type and the Link header of the query, whose body is a file of
        // the directory or the content of the query
        ["/preload/resources/echo-preload-header.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", request.Get("type"));
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Credentials", "true");
            if (request.Get("link") is { } link)
            {
                response.Set("Link", link);
            }

            if (request.Get("file") is { } file)
            {
                var path = Path.Combine(WptPaths.Wpt, "preload", "resources", file.Replace('/', Path.DirectorySeparatorChar));
                if (file.Contains("..", StringComparison.Ordinal))
                {
                    response.Status = 403;
                    response.Content = "Forbidden";
                }
                else if (!File.Exists(path))
                {
                    response.Status = 404;
                    response.Content = "Not found";
                }
                else
                {
                    response.Body = File.ReadAllBytes(path);
                }
            }
            else
            {
                response.Content = request.Get("content") ?? "";
            }
        },

        // echo-with-cors.py: the content and the Content-Type of the query, with the CORS headers that allow the origin
        ["/preload/resources/echo-with-cors.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", request.Get("type"));
            if (request.Header("Origin") is { } origin)
            {
                response.Set("Access-Control-Allow-Origin", origin);
                response.Set("Access-Control-Allow-Credentials", "true");
            }

            response.Content = request.Get("content") ?? "";
        },

        // link-header-referrer-policy.py: a document of the directory with a Link header that preloads a script with the
        // referrer policy of the query. wptserve sends no Content-Type: the browsers sniff an HTML document.
        ["/preload/resources/link-header-referrer-policy.py"] = (request, response, _) =>
        {
            response.Add("Link", "<" + request.Get("href", "") + ">;rel=\"preload\";" + request.Get("preload-policy", "") + ";as=\"script\"");
            response.Add("Content-Type", "text/html");
            response.Body = ReadWptFile("preload/resources/" + DecodeUtf8(request.Get("resource-name") ?? ""));
        },

        // prefetch-info.py: records the headers and the URL of the requests of a key; the info mode returns them (JSON)
        ["/preload/resources/prefetch-info.py"] = (request, response, stash) =>
        {
            var key = "prefetch-info|" + request.Get("key");
            var mode = request.Get("mode", "content");
            if (request.Get("cors", "true") == "true" || mode == "info")
            {
                response.Set("Access-Control-Allow-Origin", "*");
            }

            response.Status = ParseInt(request.Get("status"), 200);
            var requests = stash.TakeValue(key) as List<string> ?? [];
            if (mode == "info")
            {
                response.Set("Content-Type", "application/json");
                response.Body = Encoding.UTF8.GetBytes("[" + string.Join(", ", requests) + "]");
                stash.Put(key, requests);
                return;
            }

            // The names of the headers are in lowercase (wptserve), and the first value of each header is recorded
            var headers = new List<string>();
            foreach (var name in request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
            {
                headers.Add(PythonJsonString(name) + ": " + PythonJsonString(request.Headers.Find(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value));
            }

            requests.Add("{\"headers\": {" + string.Join(", ", headers) + "}, \"url\": " + PythonJsonString(request.Url.Href) + "}");
            stash.Put(key, requests);
            response.Set("Content-Type", request.Get("type", "text/plain"));
            response.Set("Cache-Control", request.Get("cache-control", "max-age: 604800"));
            if (request.Get("file") is { } file)
            {
                response.Body = ReadWptFile("preload/resources/" + Path.GetFileName(file));
            }
            else
            {
                response.Content = request.Get("content", "123") ?? "";
            }
        },

        // preload-count.py: counts the requests (a 404 response); the result action returns the count as a script
        ["/preload/resources/preload-count.py"] = (request, response, stash) =>
        {
            var count = stash.TakeValue(PreloadCountKey) as int? ?? 0;
            if (request.Get("action") == "result")
            {
                response.Add("Content-Type", "text/javascript");
                response.Content = "preloadCount = " + count.ToString(CultureInfo.InvariantCulture) + ";";
                return;
            }

            stash.Put(PreloadCountKey, count + 1);
            response.Status = 404;
            response.Content = "No entry is found";
        },

        // preload-module.py: a JavaScript module; block=1 blocks the response until a release=1 request with the same uuid
        // arrives (the stash is polled every 0.1 second, for 30 seconds at most)
        ["/preload/resources/preload-module.py"] = (request, response, stash) =>
        {
            var uuid = "preload-module|" + request.Get("uuid");
            response.Add("Content-Type", "text/javascript");
            response.Add("Cache-Control", "no-store");
            if (request.Has("release"))
            {
                stash.Put(uuid, "released");
                return;
            }

            if (request.Has("block") && stash.TakeValue(uuid) is null && request.PollingElapsed < TimeSpan.FromSeconds(30))
            {
                response.RetryAfter = TimeSpan.FromSeconds(0.1);
                return;
            }

            response.Content = request.Has("syntaxerror")
                ? "if ("
                : request.Get("import") is { } dependency ? "import " + PythonJsonString(DecodeUtf8(dependency)) + ";\n" : "export default 1;\n";
        },

        // stash-put.py: puts the value of the key in the stash of the directory
        ["/preload/resources/stash-put.py"] = (request, response, stash) =>
        {
            if (request.Method == "OPTIONS")
            {
                response.Set("Access-Control-Allow-Origin", "*");
                response.Set("Access-Control-Allow-Methods", "*");
                response.Set("Access-Control-Allow-Headers", "*");
                response.Content = "done";
                return;
            }

            stash.Put(request.Get("key") ?? "", request.Get("value", "value") ?? "", PreloadStashPath);
            response.Set("Access-Control-Allow-Origin", "*");
            response.Content = "done";
        },

        // stash-referrer.py: puts the Referer header of the request in the stash, or takes it
        ["/preload/resources/stash-referrer.py"] = (request, response, stash) =>
        {
            response.Set("Access-Control-Allow-Origin", "*");
            var key = "stash-referrer|" + request.Get("key");
            if (request.Get("operation") == "put")
            {
                stash.Put(key, request.Header("Referer") is { Length: > 0 } referrer ? referrer : "NO-REFERER");
                response.Content = "Added value to stash";
            }
            else if (request.Get("operation") == "take")
            {
                response.Content = stash.Take(key) ?? "";
            }
            else
            {
                throw new InvalidOperationException("Unknown operation");
            }
        },

        // stash-take.py (a JSON handler): takes the value of the key, or a 404 response
        ["/preload/resources/stash-take.py"] = (request, response, stash) =>
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Content-Type", "application/json");
            if (stash.TakeValue(request.Get("key") ?? "", PreloadStashPath) is string value)
            {
                response.Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
            }
            else
            {
                response.Status = 404;
                response.Content = "\"No entry is found\"";
            }
        },
    };
}
