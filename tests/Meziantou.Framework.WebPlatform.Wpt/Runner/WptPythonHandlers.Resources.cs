using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers that serve the resources of the other tests: the slow and the revalidated resources, the refreshes, the
/// stashes of a single test and the cookies set by a document.
/// </summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> ResourceHandlers = new(StringComparer.Ordinal)
    {
        // The headers after a delay, then the body after another delay
        ["/html/browsers/browsing-the-web/back-forward-cache/resources/slow.py"] = (request, response, _) =>
        {
            response.Delay = Milliseconds(request.Get("delayBeforeHeader"), 0);
            if (request.Has("cors"))
            {
                response.Set("Access-Control-Allow-Origin", "*");
            }

            response.Streamed = true;
            response.Chunks.Add((Milliseconds(request.Get("delayBeforeBody"), 0), "Body"u8.ToArray()));
        },

        // A refresh after the number of seconds of the query, in a meta element or in a header
        ["/html/webappapis/dynamic-markup-insertion/opening-the-input-stream/resources/meta-refresh.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Content = "<meta http-equiv=refresh content=" + (request.Query.Length > 0 ? request.Query : "0") + ">";
        },
        ["/html/webappapis/dynamic-markup-insertion/opening-the-input-stream/resources/http-refresh.py"] = (request, response, _) =>
        {
            response.Add("Refresh", request.Query.Length > 0 ? request.Query : "0");
            response.Add("Content-Type", "text/html");
        },

        // A 1x1 PNG image after 2 seconds, without Content-Type
        ["/html/webappapis/dynamic-markup-insertion/opening-the-input-stream/resources/slow-png.py"] = (_, response, _) =>
        {
            response.Delay = TimeSpan.FromSeconds(2);
            response.Body = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNiAAAABgADNjd8qAAAAABJRU5ErkJggg==");
        },

        // A worker script that must be revalidated, and a 304 response to its revalidation
        ["/html/cross-origin-embedder-policy/resources/dedicated-worker-supporting-revalidation.py"] = (request, response, _) =>
        {
            if (request.Header("if-none-match") is { Length: > 0 })
            {
                response.Status = 304;
                response.StatusText = "Not Modified";
                return;
            }

            response.Add("Content-Type", "text/javascript");
            response.Add("Cross-Origin-Embedder-Policy", "require-corp");
            response.Add("Cache-Control", "private, max-age=0, must-revalidate");
            response.Add("ETag", "abcdef");
            response.Content = "self.onmessage = (e) => { self.postMessage('LOADED'); };";
        },

        // An image whose revalidation answers 304 after 2 seconds
        ["/css/css-images/support/object-fit-stale-image.py"] = (request, response, _) =>
        {
            const string ETag = "\"object-fit-stale-image\"";
            response.Set("Cache-Control", "max-age=0");
            response.Set("ETag", ETag);
            response.Set("Content-Type", "image/png");
            if (request.Header("If-None-Match") == ETag)
            {
                response.Delay = TimeSpan.FromSeconds(2);
                response.Status = 304;
                response.StatusText = "Not Modified";
                return;
            }

            response.Body = ReadWptFile("css/css-images/support/object-fit-stale-image.png");
        },

        // A style sheet whose response waits for a POST request with the same key
        ["/css/css-view-transitions/navigation/resources/render-blocking-stylesheet.py"] = (request, response, stash) =>
        {
            var key = RequiredParameter(request, "key");
            if (request.Method == "POST")
            {
                stash.Put(key, "doResponse");
                response.Content = "done";
                return;
            }

            if (stash.TakeValue(key) is null)
            {
                response.RetryAfter = TimeSpan.FromMilliseconds(100);
                return;
            }

            response.Add("Content-Type", "text/css");
        },

        ["/web-animations/resources/xhr-doc.py"] = (_, response, _) => Respond(response, "text/html;charset=utf-8", "<!doctype html><div id=test></div>"u8.ToArray()),

        // The URL of the request, then the stashed URL
        ["/preload/resources/stash-url.py"] = (request, response, stash) =>
        {
            response.Set("Content-Type", "application/json");
            response.Set("Cache-Control", "no-store");
            stash.Put(RequiredParameter(request, "key"), request.Url.Href, "preload/resources/");
            response.Content = """{"ok": true}""";
        },
        ["/preload/resources/stash-read.py"] = (request, response, stash) => response.Body = stash.TakeValue(RequiredParameter(request, "key"), "preload/resources/") is string value ? Encoding.UTF8.GetBytes(value) : [],

        // A document that sets the SameSite cookies
        ["/workers/same-site-cookies/resources/set_cookies.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Headers[0] = new("Content-Type", "text/html; charset=utf-8");
            response.Add("Set-Cookie", MakeCookieHeader("samesite_strict_set_before_load", "test", ("SameSite", "Strict"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_lax_set_before_load", "test", ("SameSite", "Lax"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_none_set_before_load", "test", ("SameSite", "None"), ("path", "/"), ("Secure", "")));
            response.Content = "<!DOCTYPE html>";
        },

        // The steps of the SameSite cookies of the workers: the redirects, the shared worker script and the cookies of its fetch
        ["/workers/same-site-cookies/resources/worker_redirect.py"] = (_, response, _) => SameSiteCookiesRedirect(response, "/workers/same-site-cookies/resources/worker.py", "redirect_load"),
        ["/workers/same-site-cookies/resources/get_cookies_redirect.py"] = (_, response, _) => SameSiteCookiesRedirect(response, "/workers/same-site-cookies/resources/get_cookies.py", "redirect_fetch"),
        ["/workers/same-site-cookies/resources/get_cookies.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            var cookies = ReadCookies(request);
            AddSameSiteCookies(response, "fetch");
            response.Content = PythonJsonDump(cookies);
        },
        ["/workers/same-site-cookies/resources/worker.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Headers[0] = new("Content-Type", "text/javascript");
            AddSameSiteCookies(response, "load");
            var cookies = ReadCookies(request);
            var message = "ReadOnLoad:"
                + (cookies.Exists(cookie => cookie.Key == "samesite_strict_set_before_load") ? "Strict" : "")
                + (cookies.Exists(cookie => cookie.Key == "samesite_lax_set_before_load") ? "Lax" : "")
                + (cookies.Exists(cookie => cookie.Key == "samesite_none_set_before_load") ? "None" : "");
            response.Content = """

                self.onconnect = (e) => {
                    fetch("/workers/same-site-cookies/resources/get_cookies_redirect.py", {credentials: 'include'}).then((resp) => {
                        resp.json().then((cookies) => {
                            let message = "MESSAGE,ReadOnFetch:";
                            if (cookies.hasOwnProperty("samesite_strict_set_before_load")) {
                                message += "Strict";
                            }
                            if (cookies.hasOwnProperty("samesite_lax_set_before_load")) {
                                message += "Lax";
                            }
                            if (cookies.hasOwnProperty("samesite_none_set_before_load")) {
                                message += "None";
                            }
                            e.ports[0].postMessage(message);
                            self.close();
                        });
                    });
                }

                """.Replace("MESSAGE", message, StringComparison.Ordinal);
        },

        ["/fetch/origin/resources/referrer-policy.py"] = (request, response, _) =>
        {
            if (request.Get("referrerPolicy") is { } referrerPolicy)
            {
                response.Set("Referrer-Policy", referrerPolicy);
            }

            response.Set("Content-Type", "text/html");
            response.Content = "<meta charset=utf-8>\n<body><script>parent.postMessage('action','*')</script></body>";
        },

        // A document that uses a deprecated feature, whose Reporting-Endpoints header is only sent the first time
        ["/reporting/resources/generate-report-once.py"] = (request, response, stash) =>
        {
            if (request.Method == "OPTIONS")
            {
                if (request.Header("Origin")?.Contains("www2", StringComparison.Ordinal) == true)
                {
                    response.Status = 400;
                    response.Content = "CORS preflight rejected for www2";
                    return;
                }

                response.Add("Content-Type", "text/plain");
                response.Add("Access-Control-Allow-Origin", "*");
                response.Add("Access-Control-Allow-Methods", "get");
                response.Add("Access-Control-Allow-Headers", "Content-Type");
                response.Content = "CORS allowed";
                return;
            }

            if (request.Get("reportID") is not { } key)
            {
                response.Status = 400;
                response.Content = "reportID parameter is required.";
                return;
            }

            // (the stash of the path of the handler, not the one of report.py)
            if (stash.TakeValue(key, request.Path) is null)
            {
                response.Set("Reporting-Endpoints", "default=\"/reporting/resources/report.py?reportID=" + key + "\"");
            }

            stash.Put(key, true, request.Path);
            response.Content = """

                <!DOCTYPE HTML>
                <meta charset=utf-8>
                <title>Generate deprecation report</title>
                <script>
                  if (typeof window.webkitRequestAnimationFrame === "function") {
                    window.webkitRequestAnimationFrame(() => {});
                  } else {
                    window.fullScreen;
                  }
                </script>

                """;
        },

        // A filter that makes a rectangle green, unless the referrer of the request contains no-referrer
        ["/referrer-policy/css-integration/svg/filter.py"] = (request, response, _) =>
        {
            var body = (request.Header("referer") ?? "").Contains("no-referrer", StringComparison.Ordinal)
                ? ""
                : """<svg xmlns="http://www.w3.org/2000/svg">  <defs>    <filter id="make-solid-green" color-interpolation-filters="sRGB">      <feColorMatrix type="matrix" values="        0 0 0 0 0        0 0 0 0 0.502        0 0 0 0 0        0 0 0 1 0" />    </filter>  </defs></svg>""";
            response.RawMessage = Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\naccess-control-allow-origin: *\r\ncontent-type: image/svg+xml\r\ncache-control: no-cache; must-revalidate\r\ncontent-length: " + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n\r\n" + body);
        },

        // The executor of the prefetch tests: a prefetch gets a 503 response after a delay
        ["/speculation-rules/prefetch/resources/delayed-conditional-status.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            response.Set("Cache-Control", "no-store");
            var prefetch = IsPrefetchRequest(request);
            if (prefetch)
            {
                response.Delay = TimeSpan.FromSeconds(1);
            }

            response.Status = prefetch ? 503 : 200;
            response.Body = [.. GetPrefetchExecutorHtml(request, additionalScript: null), .. (prefetch ? "<body>503"u8 : "<body>200"u8)];
        },

        // The executor of the prefetch tests: a prefetch gets the headers of a 503 response, and its body after a delay
        ["/speculation-rules/prefetch/resources/slow-body-conditional-status.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            response.Set("Cache-Control", "no-store");
            var executor = GetPrefetchExecutorHtml(request, additionalScript: null);
            if (IsPrefetchRequest(request))
            {
                response.Status = 503;
                response.Streamed = true;
                response.Chunks.Add((TimeSpan.FromSeconds(2), [.. executor, .. "<body>503"u8]));
            }
            else
            {
                response.Body = [.. executor, .. "<body>200"u8];
            }
        },
    };

    /// <summary>The Strict, Lax and None cookies of a step of workers/same-site-cookies (<c>samesite_*_set_on_{step}</c>).</summary>
    private static void AddSameSiteCookies(WptHandlerResponse response, string step)
    {
        response.Add("Set-Cookie", MakeCookieHeader("samesite_strict_set_on_" + step, "test", ("SameSite", "Strict"), ("path", "/"), ("Secure", "")));
        response.Add("Set-Cookie", MakeCookieHeader("samesite_lax_set_on_" + step, "test", ("SameSite", "Lax"), ("path", "/"), ("Secure", "")));
        response.Add("Set-Cookie", MakeCookieHeader("samesite_none_set_on_" + step, "test", ("SameSite", "None"), ("path", "/"), ("Secure", "")));
    }

    /// <summary>A redirect of workers/same-site-cookies that sets the cookies of its step.</summary>
    private static void SameSiteCookiesRedirect(WptHandlerResponse response, string location, string step)
    {
        response.Status = 302;
        response.Add("Location", location);
        AddSameSiteCookies(response, step);
    }
}
