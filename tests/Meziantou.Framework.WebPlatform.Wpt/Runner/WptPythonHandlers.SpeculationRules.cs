using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>speculation-rules</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private const string PrefetchResources = "/speculation-rules/prefetch/resources/";
    private const string PrerenderResources = "/speculation-rules/prerender/resources/";

    private static readonly Dictionary<string, Handler> SpeculationRulesHandlers = new(StringComparer.Ordinal)
    {
        // prefetch.py: counts the prefetch requests of a UUID (JSON); a request that is not a prefetch reads and resets the count
        [PrefetchResources + "prefetch.py"] = (request, response, stash) =>
        {
            var uuid = "prefetch|" + request.Get("uuid");
            response.Set("Content-Type", "application/json");
            response.Set("Cache-Control", "no-store");
            var count = stash.TakeValue(uuid) as int? ?? 0;
            if (IsPrefetchRequest(request))
            {
                count++;
                stash.Put(uuid, count);
            }

            response.Content = count.ToString(CultureInfo.InvariantCulture);
        },

        // counting-executor.py: the executor document, which counts the prefetch requests and the other requests of a UUID
        [PrefetchResources + "counting-executor.py"] = (request, response, stash) =>
        {
            response.Set("Content-Type", "text/html");
            response.Set("Cache-Control", "no-store");
            var uuid = "counting-executor|" + request.Get("uuid");
            var counts = stash.TakeValue(uuid) as int[] ?? [0, 0];
            if (request.Has("check"))
            {
                response.Content = "{\"prefetch\": " + counts[0].ToString(CultureInfo.InvariantCulture) + ", \"nonPrefetch\": " + counts[1].ToString(CultureInfo.InvariantCulture) + "}";
                return;
            }

            counts[IsPrefetchRequest(request) ? 0 : 1]++;
            stash.Put(uuid, counts);
            if (request.Get("delay") is { } delay)
            {
                response.Delay = Milliseconds(delay, 0);
            }

            if (request.Get("location") is { } location)
            {
                response.Status = 302;
                response.Set("Location", location);
                return;
            }

            response.Body = GetPrefetchExecutorHtml(request, "");
        },

        // cookies.py: the executor document with the cookies of the request, which sets the "count" and "type" cookies
        [PrefetchResources + "cookies.py"] = (request, response, _) =>
        {
            var requestCookies = ParseRequestCookies(request);
            var cookies = "{" + string.Join(", ", requestCookies.Select(cookie => PythonJsonString(cookie.Key) + ": " + PythonJsonString(cookie.Value))) + "}";
            var count = ParseInt(requestCookies.Find(cookie => cookie.Key == "count").Value, 0);
            response.Add("Set-Cookie", "count=" + (count + 1).ToString(CultureInfo.InvariantCulture) + "; Path=/speculation-rules/; Secure; SameSite=None");
            response.Add("Set-Cookie", "type=" + (IsPrefetchRequest(request) ? "prefetch" : "navigate") + "; Path=/speculation-rules/");
            response.Add("Content-Type", "text/html");
            response.Add("Cache-Control", "no-store");
            if (request.Has("cookieindices"))
            {
                response.Add("Vary", "Cookie");
                response.Add("Cookie-Indices", "\"vary1\", \"vary2\"");
            }

            response.Body = GetPrefetchExecutorHtml(request, "window.requestCookies = " + cookies + ";");
        },

        // authenticate.py: a 401 response unless the request has credentials or is a prefetch; the executor document has the credentials
        [PrefetchResources + "authenticate.py"] = (request, response, _) =>
        {
            static string Format(string? value) => value is null ? "undefined" : "\"" + value + "\"";
            var (username, password) = request.Auth;
            response.Add("Content-Type", "text/html");
            response.Add("WWW-Authenticate", "Basic");
            response.Add("Cache-Control", "no-store");
            response.Status = username is not null || IsPrefetchRequest(request) ? 200 : 401;
            response.Body = GetPrefetchExecutorHtml(request, "window.requestCredentials = {\n        username: " + Format(username) + ",\n        password: " + Format(password) + "\n      };\n    ");
        },

        // conditional-status.py: a 503 response for a prefetch, else a 200 response
        [PrefetchResources + "conditional-status.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            var prefetch = IsPrefetchRequest(request);
            response.Status = prefetch ? 503 : 200;
            response.Body = [.. GetPrefetchExecutorHtml(request, null), .. Encoding.ASCII.GetBytes(prefetch ? "<body>503" : "<body>200")];
        },

        // random_redirect.py: a redirect to the location with a random query parameter
        [PrefetchResources + "random_redirect.py"] = (request, response, _) =>
        {
            var location = request.Get("location") ?? "";
            var query = location.IndexOf('?', StringComparison.Ordinal) is var index and >= 0 && index < location.Length - 1 && location.IndexOf('#', StringComparison.Ordinal) != index + 1;
            response.Status = 302;
            response.Set("Location", location + (query ? "&" : "?") + "random=" + Guid.NewGuid().ToString("N"));
        },

        // slow-executor.py: the executor document, after a delay in seconds
        [PrefetchResources + "slow-executor.py"] = (request, response, _) =>
        {
            response.Delay = TimeSpan.FromSeconds(double.Parse(request.Get("delay") ?? "0", CultureInfo.InvariantCulture));
            response.Set("Content-Type", "text/html");
            response.Set("Cache-Control", "no-store");
            response.Body = GetPrefetchExecutorHtml(request, null);
        },

        // post-navigation-handler.py: the executor document (it accepts the POST navigations)
        [PrefetchResources + "post-navigation-handler.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            response.Set("Cache-Control", "no-store");
            response.Body = GetPrefetchExecutorHtml(request, null);
        },

        // ruleset.py: a speculation rule set that prefetches a URL, with the status, the MIME type, the encoding, the CORS
        // headers and the redirect of the query
        [PrefetchResources + "ruleset.py"] = (request, response, _) =>
        {
            var url = DecodeUtf8(request.Get("url") ?? "");
            var uuid = request.Get("uuid");
            var page = request.Get("page");
            if (request.Get("redirect") == "true")
            {
                response.Status = 301;
                response.Add("Location", request.Url.Href.Replace("redirect=true", "redirect=false", StringComparison.Ordinal));
                response.Add("Access-Control-Allow-Origin", "*");
                return;
            }

            var validEncoding = request.Get("valid_encoding") == "true";
            var content = "\n      {\n        \"prefetch\": [\n          {\n            \"source\":\"list\",\n            \"urls\":[\"" + url + "?uuid=" + uuid + "&page=" + page + "&str=" + (char)0xF7 + "\"]\n          }\n        ]\n      }\n  ";
            var destination = request.Header("Sec-Fetch-Dest")?.ToLowerInvariant();
            if (request.Get("empty_json") == "true")
            {
                content = "";
            }
            else if (request.Get("valid_json") != "true")
            {
                content = "invalid json";
            }
            else if (destination is null)
            {
                content = "Missing Sec-Fetch-Dest";
            }
            else if (destination != "speculationrules")
            {
                content = "Unexpected Sec-Fetch-Dest " + destination;
            }

            response.Status = ParseInt(request.Get("status"), 200);
            response.Add("Content-Type", (request.Get("valid_mime") == "true" ? "application/speculationrules+json" : "application/json") + "; charset=" + (validEncoding ? "utf-8" : "windows-1250"));
            if (request.Get("fail_cors") != "true")
            {
                response.Add("Access-Control-Allow-Origin", request.Header("Origin") ?? "*");
            }

            // U+00F7 is the byte 0xF7 in windows-1250
            response.Body = validEncoding ? Encoding.UTF8.GetBytes(content) : Encoding.Latin1.GetBytes(content);
        },

        // prefetch_nvs_hint.py: the executor document with the No-Vary-Search header of the query; the response of a prefetch
        // waits until another request unblocks it
        [PrefetchResources + "prefetch_nvs_hint.py"] = (request, response, stash) =>
        {
            response.Set("Cache-Control", "no-store");
            var uuid = "nvs|" + request.Get("uuid");
            var waitForPrefetchStartUuid = request.Get("wait_for_prefetch_uuid") is { } value ? "nvs|" + value : null;
            if (request.Has("unblock"))
            {
                stash.Put(uuid, 0);
                return;
            }

            if (request.Has("wait_for_prefetch"))
            {
                if (waitForPrefetchStartUuid is not null && stash.TakeValue(waitForPrefetchStartUuid) is null)
                {
                    response.RetryAfter = TimeSpan.FromSeconds(0.1);
                }

                return;
            }

            if (request.Get("nvs_header") is { } noVarySearch)
            {
                response.Set("No-Vary-Search", noVarySearch);
            }

            if (IsPrefetchRequest(request))
            {
                // The start of the prefetch is recorded once (the handler runs again while it waits)
                if (waitForPrefetchStartUuid is not null && request.PollingElapsed == TimeSpan.Zero)
                {
                    stash.Put(waitForPrefetchStartUuid, 0);
                }

                if (stash.TakeValue(uuid) is null)
                {
                    response.RetryAfter = TimeSpan.FromSeconds(0.1);
                    return;
                }
            }

            response.Set("Content-Type", "text/html");
            response.Body = GetPrefetchExecutorHtml(request, "");
        },

        // counter.py: counts the requests of a UUID (JSON); the "check" parameter reads the count
        ["/speculation-rules/resources/counter.py"] = (request, response, stash) =>
        {
            response.Set("Content-Type", "application/json");
            response.Set("Cache-Control", "no-store");
            var uuid = "counter|" + request.Get("uuid");
            var count = stash.TakeValue(uuid) as int? ?? 0;
            if (!request.Has("check"))
            {
                count++;
            }

            stash.Put(uuid, count);
            response.Content = count.ToString(CultureInfo.InvariantCulture);
        },

        // exec.py: the executor document of the prerender tests, with the status of the query for a prerender request; counts
        // the requests of a UUID
        [PrerenderResources + "exec.py"] = (request, response, stash) =>
        {
            var code = request.Header("Sec-Purpose") == "prefetch;prerender" && request.Has("code") ? ParseInt(request.Get("code"), 200) : 200;
            if (request.Get("uuid") is { } uuid)
            {
                var count = stash.TakeValue(uuid, PrerenderResources + "exec.py") as int? ?? 0;
                if (request.Has("get-fetch-count"))
                {
                    response.Content = count.ToString(CultureInfo.InvariantCulture);
                    stash.Put(uuid, count, PrerenderResources + "exec.py");
                    return;
                }

                stash.Put(uuid, count + 1, PrerenderResources + "exec.py");
            }

            response.Body = ReadWptFile("speculation-rules/prerender/resources/exec.html");
            response.Status = code;
        },

        // key-value-store.py: stores the value of a key, or takes it (an empty body when there is none)
        [PrerenderResources + "key-value-store.py"] = (request, response, stash) =>
        {
            var key = "key-value-store|" + request.Get("key");
            if (request.Get("value") is { Length: > 0 } value)
            {
                stash.Put(key, value);
                return;
            }

            response.Content = stash.Take(key) ?? "";
        },

        // deprecated-broadcast-channel.py: the messages of a channel (the PrerenderChannel of the tests), by name and uid: a
        // POST request adds a message, a GET request returns the messages (JSON)
        [PrerenderResources + "deprecated-broadcast-channel.py"] = (request, response, stash) =>
        {
            var key = "broadcast-channel|" + request.Get("uid");
            var name = request.Get("name") ?? "";
            response.Delay = TimeSpan.FromSeconds(0.1);
            var messages = stash.TakeValue(key) as Dictionary<string, List<string>> ?? new(StringComparer.Ordinal);
            if (!messages.TryGetValue(name, out var messagesByName))
            {
                messagesByName = [];
            }

            if (request.Method == "POST")
            {
                messagesByName.Add(Encoding.UTF8.GetString(request.Body));
                messages[name] = messagesByName;
                response.Status = 204;
            }
            else
            {
                response.Set("Content-Type", "application/json");
                response.Body = Encoding.UTF8.GetBytes("[" + string.Join(", ", messagesByName) + "]");
            }

            stash.Put(key, messages);
        },

        // echo-referrer.py: a document that posts the Referer header of its request to the PrerenderChannel of the test
        [PrerenderResources + "echo-referrer.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Body = Encoding.UTF8.GetBytes("\n<html>\n<head>\n<title>Echo referrer</title>\n</head>\n<script src=\"/speculation-rules/prerender/resources/utils.js\"></script>\n<body>\n<script>\nconst bc = new PrerenderChannel('prerender-channel', '"
                + DecodeUtf8(request.Get("uid") ?? "") + "');\nbc.postMessage({referrer: '" + DecodeUtf8(request.Header("Referer") ?? "(none)") + "'});\n</script>\n</body>\n</html>\n");
        },

        // echo-client-hints-received.py and echo-prerender-page-client-hints-received.py: the document of the Python file
        // (its html_template), with whether the request had the Sec-CH-UA-Bitness and Sec-CH-UA-Full-Version-List hints; the
        // prerendered page opts into the second one
        [PrerenderResources + "echo-client-hints-received.py"] = (request, response, _) => response.Body = GetClientHintsEchoDocument(request, "echo-client-hints-received.py"),
        [PrerenderResources + "echo-prerender-page-client-hints-received.py"] = (request, response, _) =>
        {
            response.Set("Accept-CH", "sec-ch-ua-full-version-list");
            response.Body = GetClientHintsEchoDocument(request, "echo-prerender-page-client-hints-received.py");
        },

        // echo-subresource-client-hints-received.py: response headers that tell whether the request had the two hints
        [PrerenderResources + "echo-subresource-client-hints-received.py"] = (request, response, _) =>
        {
            response.Set("server_received_bitness", request.HasHeader("sec-ch-ua-bitness") ? "true" : "false");
            response.Set("server_received_full_version_list", request.HasHeader("sec-ch-ua-full-version-list") ? "true" : "false");
        },

        // image-with-headers-stash.py: an image that records the headers of its request (JSON pairs), which another request reads
        [PrerenderResources + "image-with-headers-stash.py"] = (request, response, stash) =>
        {
            if (request.Get("image") is { } image)
            {
                var pairs = request.Headers.Select(header => "[" + PythonJsonString(header.Key.ToLowerInvariant()) + ", " + PythonJsonString(header.Value) + "]");
                stash.Put("image-headers|" + image, "[" + string.Join(", ", pairs) + "]");
                response.Add("Content-Type", "image/png");
                response.Add("Access-Control-Allow-Origin", "*");
                response.Body = ReadWptFile("common/square.png");
            }
            else if (request.Get("read") is { } read)
            {
                response.Add("Content-Type", "application/json");
                response.Content = stash.Take("image-headers|" + read) ?? "null";
            }
            else
            {
                response.Status = 404;
                response.Content = "Not found";
            }
        },

        // shared-worker.py: a shared worker script that posts its time origin, and records that it was requested
        [PrerenderResources + "shared-worker.py"] = (request, response, stash) =>
        {
            var id = "shared-worker|" + request.Get("id");
            if (request.Has("check"))
            {
                response.Set("Content-Type", "text/plain");
                response.Content = stash.Take(id) ?? "";
                return;
            }

            stash.Put(id, "ok");
            response.Set("Content-Type", "text/javascript");
            response.Content = "onconnect = ({ports: [port]}) => port.postMessage(performance.timeOrigin);";
        },

        // activation_beacon.py: the executor document with the on-prefetch-activation header, the beacon that the activation
        // of the prefetch sends, and the state of the UUID (JSON)
        ["/speculation-rules/activation-header/resources/activation_beacon.py"] = (request, response, stash) =>
        {
            if (request.Get("uuid") is not { } uuid)
            {
                response.Status = 400;
                response.Add("Content-Type", "text/plain");
                response.Content = "Missing uuid";
                return;
            }

            var key = "activation-beacon|" + uuid;
            var state = stash.TakeValue(key) as List<string> ?? [];
            switch (request.Get("action", "serve"))
            {
                case "serve":
                    response.Add("Content-Type", "text/html");
                    response.Add("Cache-Control", "no-store");
                    response.Add("on-prefetch-activation", "activation_beacon.py?uuid=" + uuid + "&action=beacon");
                    AddState("\"fetched\": true");
                    response.Body = GetPrefetchExecutorHtml(request, null);
                    break;

                case "beacon":
                    response.Add("Content-Type", "text/plain");
                    if (request.Method != "GET")
                    {
                        AddState("\"error\": \"Method not allowed: " + request.Method + "\"");
                        response.Status = 405;
                        response.Content = "Method not allowed";
                    }
                    else
                    {
                        AddState("\"beacon_received\": true");
                        response.Status = 204;
                    }

                    break;

                case "check":
                    response.Add("Content-Type", "application/json");
                    response.Add("Cache-Control", "no-store");
                    response.Content = "{" + string.Join(", ", state) + "}";
                    break;

                default:
                    AddState("\"error\": \"Invalid action\"");
                    response.Status = 400;
                    response.Add("Content-Type", "text/plain");
                    response.Content = "Invalid action";
                    break;
            }

            stash.Put(key, state);

            void AddState(string entry)
            {
                if (!state.Contains(entry))
                {
                    state.Add(entry);
                }
            }
        },

        // clear-site-data-prefetchCache.py and clear-site-data-prerenderCache.py: a document with the Clear-Site-Data header,
        // which posts a message to its opener (or its parent) after one second and closes itself
        ["/clear-site-data/support/clear-site-data-prefetchCache.py"] = (request, response, _) => ClearSiteDataDocument(request, response, "prefetchCache"),
        ["/clear-site-data/support/clear-site-data-prerenderCache.py"] = (request, response, _) => ClearSiteDataDocument(request, response, "prerenderCache"),

        // clear-site-data-cache.py: a resource of the type of the query with a unique content, which is cacheable or not, with
        // the Clear-Site-Data header of the query (for the first request of a cache helper with clear_first). The image
        // responses (random PNG images) are not emulated.
        ["/clear-site-data/support/clear-site-data-cache.py"] = (request, response, stash) =>
        {
            var responseType = request.Get("response");
            var cacheHelper = request.Get("cache_helper") ?? "";
            var clear = request.Get("clear");
            if (request.Has("clear_first") && stash.TakeValue("clear-site-data|" + cacheHelper) is null)
            {
                clear = request.Get("clear_first");
            }

            if (request.Has("clear_first"))
            {
                stash.Put("clear-site-data|" + cacheHelper, "");
            }

            response.Add("Content-Type", responseType switch
            {
                "json" => "application/json",
                "image" => "image/png",
                "css" => "text/css",
                "js" => "text/javascript",
                _ => "text/html",
            });
            response.Add("cache-control", request.Has("cache") ? "public, max-age=31536000, immutable" : "no-store");
            if (clear is not null)
            {
                response.Add("Clear-Site-Data", clear == "all" ? "\"*\"" : "\"" + clear + "\"");
            }

            var uuid = Guid.NewGuid().ToString("D");
            var url = request.Url.Href;
            var path = request.Path;
            const string PostToOpener = "if(window.opener) {{\n window.opener.postMessage({0}, \"*\");\n }} else {{\n window.parent.postMessage({0}, \"*\");\n }}\n window.close();\n";
            switch (responseType)
            {
                case "single_html" when request.Get("iframe") is { } iframeUrl:
                    response.Content = "\n<script>\n// forward iframe uuid to opener\nwindow.addEventListener('message', function(event) {\n" + string.Format(CultureInfo.InvariantCulture, PostToOpener, "event.data") + "});\n</script>\n<br>\n" + url + "<br>\n" + iframeUrl + "<br>\n<iframe src=\"" + iframeUrl + "\"></iframe>\n</body>\n";
                    break;

                case "single_html":
                    response.Content = "\n<script>\n" + string.Format(CultureInfo.InvariantCulture, PostToOpener, "\"" + uuid + "\"") + "</script>\n<body>\n" + url + "\n</body>";
                    break;

                case "json":
                    response.Content = "[\"" + uuid + "\"]";
                    break;

                case "html_embed_json":
                    {
                        var jsonUrl = path + "?response=json&cache&cache_helper=" + cacheHelper;
                        response.Content = "\n<script>\nfetch(\"" + jsonUrl + "\")\n.then(response => response.json())\n.then(uuid => {\nwindow.opener.postMessage(uuid[0], \"*\");\nwindow.close();\n});\n</script>\n<body>\n" + url + "<br>\n" + jsonUrl + "\n</body>";
                        break;
                    }

                case "css":
                    response.Content = "\n:root {\n--uuid: \"" + uuid + "\"\n}";
                    break;

                case "html_embed_css":
                    {
                        var cssUrl = path + "?response=css&cache&cache_helper=" + cacheHelper;
                        response.Content = "\n<!DOCTYPE html>\n<link rel=\"stylesheet\" href=\"" + cssUrl + "\">\n<script>\nlet computed = getComputedStyle(document.documentElement);\nlet uuid = computed.getPropertyValue(\"--uuid\").trim().replaceAll('\"', '');\nwindow.opener.postMessage(uuid, \"*\");\nwindow.close();\n</script>\n<body>\n" + url + "<br>\n" + cssUrl + "\n</body>";
                        break;
                    }

                case "js":
                    response.Content = "\nwindow.opener.postMessage(\"" + uuid + "\", \"*\");\nwindow.close();\n";
                    break;

                case "html_embed_js":
                    {
                        var scriptUrl = path + "?response=js&cache&cache_helper=" + cacheHelper;
                        response.Content = "\n<script src=\"" + scriptUrl + "\"></script>\n<body>\n" + url + "<br>\n" + scriptUrl + "\n</body>";
                        break;
                    }

                default:
                    throw new NotSupportedException("The image responses of clear-site-data-cache.py are not emulated");
            }
        },
    };

    private static void ClearSiteDataDocument(WptHandlerRequest request, WptHandlerResponse response, string type)
    {
        response.Add("Content-Type", "text/html");
        response.Add("Clear-Site-Data", "\"" + type + "\"");
        response.Content = "\n<script>\nsetTimeout(() => {\nif(window.opener) {\nwindow.opener.postMessage(\"message\", \"*\");\n} else {\nwindow.parent.postMessage(\"message\", \"*\");\n}\nwindow.close();\n}, 1000);\n</script>\n<body>\n" + request.Url.Href + "\n</body>";
    }

    /// <summary>The <c>html_template</c> of a Python handler of <c>prerender/resources</c>, with the script that tells which client hints the request had.</summary>
    private static byte[] GetClientHintsEchoDocument(WptHandlerRequest request, string handler)
    {
        var source = Encoding.UTF8.GetString(ReadWptFile("speculation-rules/prerender/resources/" + handler));
        const string Marker = "html_template = \"\"\"";
        var start = source.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
        var template = source[start..source.IndexOf("\"\"\"", start, StringComparison.Ordinal)];
        var script = "\nconst server_received_bitness =\n    " + (request.HasHeader("sec-ch-ua-bitness") ? "true" : "false")
            + ";\nconst server_received_full_version_list =\n    " + (request.HasHeader("sec-ch-ua-full-version-list") ? "true" : "false") + ";\n";
        return Encoding.UTF8.GetBytes(template.Replace("%s", script, StringComparison.Ordinal));
    }

    private static bool IsPrefetchRequest(WptHandlerRequest request) => request.Header("Sec-Purpose")?.StartsWith("prefetch", StringComparison.Ordinal) == true;

    /// <summary>
    /// <c>util.get_executor_html()</c>: <c>executor.sub.html</c> with the substitutions of the request, and
    /// <paramref name="additionalScript"/> inserted at the start of each of its scripts that have the nonce (null for the
    /// handlers that only apply the substitutions).
    /// </summary>
    private static byte[] GetPrefetchExecutorHtml(WptHandlerRequest request, string? additionalScript)
    {
        var content = Encoding.Latin1.GetString(WptServer.Substitute(ReadWptFile("speculation-rules/prefetch/resources/executor.sub.html"), request.Url, request.Headers));
        if (additionalScript is not null)
        {
            content = content.Replace("<script nonce=\"abc\">", "<script nonce=\"abc\">" + additionalScript, StringComparison.Ordinal);
        }

        return Encoding.Latin1.GetBytes(content);
    }
}
