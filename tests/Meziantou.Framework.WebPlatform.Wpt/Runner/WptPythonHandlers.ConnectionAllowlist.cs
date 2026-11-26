using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the <c>connection-allowlist</c> tests: the key-value store that records the requests the allowlists let
/// through, the framed document of the embedded enforcement tests, and the Early Hints loader.
/// </summary>
internal static partial class WptPythonHandlers
{
    private const string ConnectionAllowlistResources = "/connection-allowlist/tentative/resources/";

    private static readonly Dictionary<string, Handler> ConnectionAllowlistHandlers = new(StringComparer.Ordinal)
    {
        // key-value-store.py: stores the value of a key, or takes it (an empty body when there is none). The responses allow
        // the CORS requests (and their preflights) of the module preloads.
        [ConnectionAllowlistResources + "key-value-store.py"] = (request, response, stash) =>
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Set("Access-Control-Allow-Headers", "*");
            if (request.Method == "OPTIONS")
                return;

            var key = ConnectionAllowlistResources + "key-value-store.py|" + request.Get("key");
            if (request.Get("value") is { Length: > 0 } value)
            {
                stash.Put(key, value);
                return;
            }

            response.Content = stash.Take(key) ?? "";
        },

        // embedded-enforcement-child.py: a document whose Allow-Connection-Allowlist-From and Connection-Allowlist headers are
        // the allow_from and allowlist parameters, which posts to the top-level window the request headers it received
        [ConnectionAllowlistResources + "embedded-enforcement-child.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            if (request.Get("allow_from") is { } allowFrom)
            {
                response.Set("Allow-Connection-Allowlist-From", allowFrom);
            }

            if (request.Get("allowlist") is { } allowlist)
            {
                response.Set("Connection-Allowlist", allowlist);
            }

            var message = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["connectionAllowlistChildLoaded"] = true,
                ["id"] = request.Get("id") ?? "",
                ["secRequiredConnectionAllowlist"] = request.Header("Sec-Required-Connection-Allowlist"),
                ["xInjected"] = request.Header("X-Injected"),
            });
            response.Content = "<!DOCTYPE html>\n<meta charset=\"utf-8\">\n<script>\n  window.top.postMessage(" + message + ", \"*\");\n</script>\n<body>child</body>\n";
        },

        // early-hints-connection-allowlist-loader.h2.py: a 103 response whose Link header preloads (or preconnects to) an
        // origin of the alternate host, unique to the key, and whose Connection-Allowlist header allows that origin or not;
        // the final response has no allowlist
        [ConnectionAllowlistResources + "early-hints-connection-allowlist-loader.h2.py"] = (request, response, _) =>
        {
            var type = request.Get("type") ?? "";
            var fileName = type switch
            {
                "preload" or "modulepreload" => "early-hints-preload-modulepreload.html",
                "preconnect" => "early-hints-preconnect.html",
                _ => null,
            };
            if (fileName is null)
            {
                response.Status = 400;
                return;
            }

            var allow = request.Get("allow");
            if (allow is "true" or "false")
            {
                var key = request.Get("key") ?? "";
                var targetOrigin = "https://" + key + "." + (request.Get("alt_host") ?? "not-web-platform.test") + ":" + (request.Get("https_port") ?? "8443");
                var linkUrl = type == "preconnect" ? targetOrigin : targetOrigin + "/connection-allowlist/tentative/resources/key-value-store.py?key=" + key + "&value=hello";
                var linkAttributes = type switch
                {
                    "preload" => "; rel=preload; as=script",
                    "modulepreload" => "; rel=modulepreload; crossorigin",
                    _ => "; rel=preconnect; crossorigin",
                };
                var allowlist = allow == "true" ? "(response-origin \"" + targetOrigin + "\")" : "(response-origin)";
                EarlyHintsResponse(response, ("connection-allowlist", allowlist), ("link", "<" + linkUrl + ">" + linkAttributes));
                response.Delay = TimeSpan.FromMilliseconds(200);
            }

            response.Set("content-type", "text/html");
            response.Body = ReadWptFile("connection-allowlist/tentative/resources/" + fileName);
        },
    };
}
