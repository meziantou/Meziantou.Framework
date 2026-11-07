using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The handlers of the service-workers tests (cache-storage and service-worker)
internal static partial class WptPythonHandlers
{
    private const string ServiceWorkerResources = "/service-workers/service-worker/resources/";

    private static readonly Dictionary<string, Handler> ServiceWorkersHandlers = new(StringComparer.Ordinal)
    {
        // A response with the status of the query
        ["/service-workers/cache-storage/resources/fetch-status.py"] = (request, response, _) => response.Status = ParseInt(request.Get("status"), 200),
        ["/service-workers/cache-storage/resources/vary.py"] = (request, response, _) =>
        {
            if (request.Has("clear-vary-value-override-cookie"))
            {
                response.Add("Set-Cookie", "vary-value-override=; expires=Thu, 01 Jan 1970 00:00:00 GMT; max-age=0");
                response.Content = "vary cookie cleared";
                return;
            }

            if (request.Get("set-vary-value-override-cookie") is { Length: > 0 } setCookieVary)
            {
                response.Add("Set-Cookie", "vary-value-override=" + setCookieVary);
                response.Content = "vary cookie set";
                return;
            }

            // The header value is the str() of the bytes of the cookie (as the Python 3 handler)
            var cookieVary = request.Header("Cookie")?.Split(';').Select(cookie => cookie.Trim()).FirstOrDefault(cookie => cookie.StartsWith("vary-value-override=", StringComparison.Ordinal))?["vary-value-override=".Length..];
            if (!string.IsNullOrEmpty(cookieVary))
            {
                response.Set("Vary", "b'" + cookieVary + "'");
            }
            else if (request.Get("vary") is { Length: > 0 } queryVary)
            {
                response.Set("Vary", queryVary);
            }

            response.Content = "vary response";
        },

        // fetch/api/resources/echo-content.h2.py: the body of the request, as text/plain (the HTTP/2 handler streams the
        // DATA frames back)
        ["/fetch/api/resources/echo-content.h2.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/plain");
            response.Body = request.Body;
        },

        [ServiceWorkerResources + "fetch-access-control.py"] = FetchAccessControl,

        // A redirect to the Redirect parameter, with the CORS headers of the query
        [ServiceWorkerResources + "redirect.py"] = ServiceWorkerRedirect,
        [ServiceWorkerResources + "scope1/redirect.py"] = ServiceWorkerRedirect,

        // { "result": "success" }, with the CORS headers of the query
        [ServiceWorkerResources + "success.py"] = (request, response, _) =>
        {
            AddAllowOrigins(request, response);
            response.Content = "{ \"result\": \"success\" }";
        },

        // An empty script whose Content-Type is the mime parameter (none without the parameter)
        [ServiceWorkerResources + "mime-type-worker.py"] = (request, response, _) =>
        {
            if (request.Get("mime") is { } mime)
            {
                response.Add("Content-Type", mime);
            }
        },
        [ServiceWorkerResources + "import-mime-type-worker.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/javascript");
            response.Content = request.Get("mime") is { } mime ? "importScripts('./mime-type-worker.py?mime=" + mime + "');" : "importScripts('./mime-type-worker.py');";
        },

        // A script with the time of the request (a new script for each request), cached for a day
        [ServiceWorkerResources + "update-max-aged-worker.py"] = (request, response, _) =>
        {
            AddMaxAgedHeaders(response);
            var test = PythonJsonString(Required(request, "test"));
            response.Body = Encoding.UTF8.GetBytes($$"""

                        const mainTime = {{PythonTime(8)}};
                        const testName = {{test}};
                        importScripts('update-max-aged-worker-imported-script.py');

                        addEventListener('message', event => {
                            event.source.postMessage({
                                mainTime,
                                importTime,
                                test: {{test}}
                            });
                        });

                """);
        },
        [ServiceWorkerResources + "update-max-aged-worker-imported-script.py"] = (_, response, _) =>
        {
            AddMaxAgedHeaders(response);
            response.Content = "\n        const importTime = " + PythonTime(8) + ";\n    ";
        },

        // A script importing bytecheck-worker-imported-script.py, whose content is the time of the request with main=time
        [ServiceWorkerResources + "bytecheck-worker.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/javascript");
            response.Add("Cache-Control", "max-age=0");
            var mainContent = request.Get("main", "") == "time" ? PythonTime(6) : "default";
            var importedRequestPath = request.Get("path", "");
            var importedRequest = request.Get("imported", "") == "time" ? "?imported=time" : "";
            response.Content = request.Get("type") == "module"
                ? "\n        // " + mainContent + "\n        import '" + importedRequestPath + "bytecheck-worker-imported-script.py" + importedRequest + "';\n        "
                : "\n        // " + mainContent + "\n        importScripts('" + importedRequestPath + "bytecheck-worker-imported-script.py" + importedRequest + "');\n        ";
        },
        [ServiceWorkerResources + "bytecheck-worker-imported-script.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/javascript");
            response.Add("Cache-Control", "max-age=0");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = "\n    // " + (request.Get("imported", "") == "time" ? PythonTime(6) : "default") + "\n    ";
        },

        // A script selected by the query (a parse error, an exception...); an unknown query fails (KeyError)
        [ServiceWorkerResources + "malformed-worker.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/javascript");
            response.Body = Encoding.UTF8.GetBytes(request.Query switch
            {
                "parse-error" => "var foo = function() {;",
                "undefined-error" => "foo.bar = 42;",
                "uncaught-exception" => "throw new DOMException(\"AbortError\");",
                "caught-exception" => "try { throw new Error; } catch(e) {}",
                "import-malformed-script" => "importScripts(\"malformed-worker.py?parse-error\");",
                "import-no-such-script" => "importScripts(\"no-such-script.js\");",
                "top-level-await" => "await Promise.resolve(1);",
                "instantiation-error" => "import nonexistent from \"./imported-module-script.js\";",
                "instantiation-error-and-top-level-await" => "import nonexistent from \"./imported-module-script.js\"; await Promise.resolve(1);",
                _ => throw new KeyNotFoundException(request.Query),
            });
        },

        [ServiceWorkerResources + "update-worker.py"] = UpdateWorker,

        // The First file the first time the Key is requested, the Second file the second time
        [ServiceWorkerResources + "update-worker-from-file.py"] = (request, response, stash) =>
        {
            var visitedCount = IncrementCounter(request, stash, Required(request, "Key"));
            var file = visitedCount switch
            {
                1 => Required(request, "First"),
                2 => Required(request, "Second"),
                _ => throw new InvalidOperationException("Unknown state"),
            };

            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            response.Body = ReadServiceWorkerFile("resources/" + file);
        },

        // A new script for each request
        [ServiceWorkerResources + "mint-new-worker.py"] = (request, response, _) =>
        {
            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            response.Body = Encoding.UTF8.GetBytes("/* " + PythonTime(6) + " " + PythonRandom() + " */ " + (request.Has("skip-waiting") ? "skipWaiting();" : "") + " " + MintNewWorkerBody);
        },
        [ServiceWorkerResources + "update-nocookie-worker.py"] = (_, response, _) =>
        {
            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            response.Content = "// " + PythonTime(6) + " " + PythonRandom();
        },
        [ServiceWorkerResources + "update-fetch-worker.py"] = (_, response, _) =>
        {
            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            response.Content = "/* " + PythonTime(6) + " " + PythonRandom() + " */ self.onfetch = (event) => { event.respondWith(fetch(event.request)); };";
        },
        [ServiceWorkerResources + "update-during-installation-worker.py"] = (_, response, _) =>
        {
            response.Add("Content-Type", "application/javascript");
            response.Add("Cache-Control", "max-age=0");
            response.Content = "// " + PythonRandom() + "\nimportScripts('update-during-installation-worker.js');";
        },
        [ServiceWorkerResources + "update-claim-worker.py"] = (_, response, _) =>
        {
            response.Add("Content-Type", "application/javascript");
            response.Body = Encoding.UTF8.GetBytes(UpdateClaimWorkerScript.Replace("%s", PythonTime(6), StringComparison.Ordinal));
        },

        // A new script for each request, followed by the content of the filename parameter
        [ServiceWorkerResources + "update_shell.py"] = (request, response, _) =>
        {
            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            byte[] body = [.. Encoding.Latin1.GetBytes("// " + PythonTime(6) + " " + PythonRandom()), (byte)'\n'];
            if (request.Get("filename") is { } filename)
            {
                body = [.. body, .. ReadServiceWorkerFile("resources/" + filename)];
            }

            response.Body = body;
        },

        // A new script for each request: the time, then the content of update-worker.js
        ["/service-workers/service-worker/ServiceWorkerGlobalScope/resources/update-worker.py"] = (_, response, _) =>
        {
            response.Add("Cache-Control", "max-age: 0");
            response.Add("Content-Type", "application/javascript");
            response.Body = [.. Encoding.UTF8.GetBytes("// " + PythonTime(6) + "\n"), .. ReadServiceWorkerFile("ServiceWorkerGlobalScope/resources/update-worker.js")];
        },

        // A script whose version is the time of the request (delayed so that each version is unique)
        [ServiceWorkerResources + "import-scripts-version.py"] = (_, response, _) =>
        {
            response.Delay = TimeSpan.FromSeconds(0.1);
            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            response.Content = "version = \"" + PythonTime(6) + "\";\n";
        },
        [ServiceWorkerResources + "import-scripts-echo.py"] = (request, response, _) => ImportScriptsEcho(request, response, ""),
        [ServiceWorkerResources + "scope2/import-scripts-echo.py"] = (request, response, _) => ImportScriptsEcho(request, response, " (scope2/)"),
        [ServiceWorkerResources + "subdir/import-scripts-echo.py"] = (request, response, _) => ImportScriptsEcho(request, response, " (subdir/)"),
        [ServiceWorkerResources + "import-scripts-get.py"] = (request, response, _) =>
        {
            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            response.Content = Required(request, "output") + " = \"" + Required(request, "msg") + "\";\n";
        },

        // The classic script and then the module script (or the opposite with classic_first=0)
        [ServiceWorkerResources + "update-registration-with-type.py"] = (request, response, stash) =>
        {
            response.Add("Content-Type", "application/javascript");
            response.Add("Pragma", "no-store");
            response.Add("Cache-Control", "no-store");
            var classicFirst = Required(request, "classic_first") == "1";
            var key = Required(request, "key");
            bool classic;
            if (stash.TakeValue(key, request.Path) is null)
            {
                stash.Put(key, true, request.Path);
                classic = classicFirst;
            }
            else
            {
                classic = !classicFirst;
            }

            response.Content = classic
                ? "\n      importScripts('./imported-classic-script.js');\n      self.onmessage = e => {\n        e.source.postMessage(imported);\n      };\n      "
                : "\n      import * as module from './imported-module-script.js';\n      self.onmessage = e => {\n        e.source.postMessage(module.imported);\n      };\n      ";
        },

        // The imported script can only be requested once: the second request is a 404 response
        [ServiceWorkerResources + "update-missing-import-scripts-imported-worker.py"] = (request, response, stash) =>
        {
            var key = Required(request, "key");
            if (stash.TakeValue(key, request.Path) is null)
            {
                stash.Put(key, true, request.Path);
                response.Add("Content-Type", "application/javascript");
                response.Content = "// initial script";
                return;
            }

            response.Status = 404;
            response.StatusText = "Not found: should not have been able to import this script twice!";
        },
        [ServiceWorkerResources + "update-missing-import-scripts-main-worker.py"] = (request, response, stash) =>
        {
            var key = Required(request, "key");
            response.Add("Content-Type", "application/javascript");
            if (stash.TakeValue(key, request.Path) is null)
            {
                stash.Put(key, true, request.Path);
                response.Body = Encoding.UTF8.GetBytes("importScripts(\"./update-missing-import-scripts-imported-worker.py?key=" + key + "\")");
                return;
            }

            response.Content = "// removed importScripts()";
        },

        // A bad worker the first time (update-recovery-mode cookie), then a good one
        [ServiceWorkerResources + "update-recovery-worker.py"] = (request, response, _) =>
        {
            var mode = ParseRequestCookies(request).Find(cookie => cookie.Key == "update-recovery-mode") is { Key: not null } cookie ? cookie.Value : "init";
            AddNoCacheHeaders(response);
            var extraBody = "";
            if (mode == "init")
            {
                response.Add("Set-Cookie", "update-recovery-mode=bad; Path=/");
                extraBody = "addEventListener('fetch', function(e) { e.respondWith(Promise.reject()); });";
            }
            else if (mode == "bad")
            {
                response.Add("Set-Cookie", PythonDeleteCookie("update-recovery-mode"));
            }

            response.Add("Content-Type", "application/javascript");
            response.Content = extraBody;
        },

        // A script that posts the cookies of its request; the counter of the key changes the script for each request
        [ServiceWorkerResources + "echo-cookie-worker.py"] = (request, response, stash) =>
        {
            response.Add("Content-Type", "text/javascript");
            var values = ParseRequestCookies(request).Select(cookie => "\"" + cookie.Key + "\": \"" + cookie.Value + "\"");
            var counter = IncrementCounter(request, stash, Required(request, "key"));
            response.Content = "\n// " + counter.ToString(CultureInfo.InvariantCulture) + "\nself.addEventListener('message', e => {\n  e.source.postMessage({" + string.Join(',', values) + "})\n});";
        },
        [ServiceWorkerResources + "import-echo-cookie-worker-module.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/javascript");
            response.Content = "import './echo-cookie-worker.py?key=" + Required(request, "key") + "'";
        },

        // A script whose CSP is the directive of the query, with the tests of the directive
        [ServiceWorkerResources + "service-worker-csp-worker.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/javascript");
            var body = "ERROR: Unknown directive";
            switch (Required(request, "directive"))
            {
                case "default":
                    response.Add("Content-Security-Policy", "default-src 'self'");
                    body = CspWorkerDefault;
                    break;
                case "script":
                    response.Add("Content-Security-Policy", "script-src 'self'");
                    body = CspWorkerScript;
                    break;
                case "connect":
                    response.Add("Content-Security-Policy", "connect-src 'self'");
                    body = CspWorkerConnect;
                    break;
            }

            response.Content = body;
        },

        // A service worker script that checks the Service-Worker header of its request
        [ServiceWorkerResources + "service-worker-header.py"] = (request, response, _) =>
        {
            var serviceWorkerHeader = request.Header("service-worker");
            if ((request.Has("header") && serviceWorkerHeader != "script") || (request.Has("no-header") && serviceWorkerHeader == "script"))
            {
                response.Status = 400;
                response.Add("Content-Type", "text/plain");
                response.Content = "Bad Request";
                return;
            }

            AddNoCacheHeaders(response);
            response.Add("Content-Type", "application/javascript");
            var body = "/* This is a service worker script */\n";
            if (request.Get("import") is { } import)
            {
                body += "importScripts('" + import + "');";
            }

            response.Content = body;
        },

        // test-request-headers-worker.js and test-request-mode-worker.js, with the headers of the request and a UUID
        [ServiceWorkerResources + "test-request-headers-worker.py"] = (request, response, _) => RequestHeadersWorker(request, response, "test-request-headers-worker.js"),
        [ServiceWorkerResources + "test-request-mode-worker.py"] = (request, response, _) => RequestHeadersWorker(request, response, "test-request-mode-worker.js"),

        // The headers of the request, as a JSON object
        [ServiceWorkerResources + "request-headers.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/json");
            response.Content = PythonRequestHeadersJson(request);
        },

        // Records the Fetch Metadata headers of the requests of the token, returned with get_records
        [ServiceWorkerResources + "soft-update-fetch-metadata.py"] = SoftUpdateFetchMetadata,

        [ServiceWorkerResources + "fetch-with-body-worker.py"] = (request, response, _) =>
        {
            if (request.Body.Length > 0)
            {
                response.Content = "BODY";
                return;
            }

            response.Status = 400;
            response.Content = "NO BODY";
        },
        [ServiceWorkerResources + "fetch-request-no-freshness-headers-script.py"] = (_, response, _) =>
        {
            response.Add("ETag", "abc123");
            response.Add("Content-Type", "text/javascript");
            response.Content = "/* empty script */";
        },

        // An invalid chunked encoding: the body of the response is a network error
        [ServiceWorkerResources + "invalid-chunked-encoding.py"] = (_, response, _) => response.NetworkError = true,
        [ServiceWorkerResources + "invalid-chunked-encoding-with-flush.py"] = (_, response, _) =>
        {
            // The headers are sent, then the invalid chunk after a second
            response.Delay = TimeSpan.FromSeconds(1);
            response.NetworkError = true;
        },

        // A response of the network, with the method of the request (server_slow, server_no_content, server_not_found)
        [ServiceWorkerResources + "direct.py"] = (request, response, _) =>
        {
            if (request.Query.Contains("server_slow", StringComparison.Ordinal))
            {
                response.Delay = TimeSpan.FromSeconds(0.2);
            }

            response.Add("Content-Type", "text/plain");
            if (request.Query.Contains("server_no_content", StringComparison.Ordinal))
            {
                response.Status = 204;
            }
            else if (request.Query.Contains("server_not_found", StringComparison.Ordinal))
            {
                response.Status = 404;
                response.Content = "Not Found";
                return;
            }

            response.Content = "Network with " + request.Method + " request";
        },

        [ServiceWorkerResources + "404.py"] = (_, response, _) =>
        {
            response.Status = 404;
            response.Add("Content-Type", "text/plain");
            response.Content = "Page not found";
        },
        [ServiceWorkerResources + "echo-content.py"] = EchoContent,

        // The trickle of fetch/api/resources/trickle.py (ms between the count chunks)
        [ServiceWorkerResources + "trickle.py"] = (request, response, _) =>
        {
            var delay = Milliseconds(request.Get("ms"), 500);
            var count = ParseInt(request.Get("count"), 50);
            response.Set("Content-type", "text/plain");
            response.Content = string.Concat(Enumerable.Repeat("TEST_TRICKLE\n", count));
            response.Streamed = true;
            response.Delay = delay * (count + 2);
        },

        // Two PNG images in a multipart/x-mixed-replace response
        [ServiceWorkerResources + "multipart-image.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "multipart/x-mixed-replace; boundary=cutHere");
            if (request.Has("approvecors"))
            {
                response.Add("Access-Control-Allow-Origin", "*");
            }

            response.Body = [.. CreatePart("images/red.png"), .. CreatePart("images/red-16x16.png")];

            static byte[] CreatePart(string path) => [.. "Content-Type: image/png\r\n\r\n"u8, .. ReadWptFile(path), .. "--cutHere"u8];
        },

        // A 1x1 PNG image with a Server-Timing header, or a redirect
        [ServiceWorkerResources + "server-timing.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "image/png");
            response.Add("Server-Timing", "metric;dur=123.4;desc=\"description\"");
            if (request.Has("tao"))
            {
                response.Add("Timing-Allow-Origin", "*");
            }

            if (request.Has("cors"))
            {
                response.Add("Access-Control-Allow-Origin", "*");
                response.Add("Access-Control-Expose-Headers", "Server-Timing, Timing-Allow-Origin");
            }

            if (request.Get("redirect") is { } redirect)
            {
                response.Status = 302;
                response.Add("Location", redirect);
                return;
            }

            response.Body = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
        },

        // A notification icon, setting the cookie of the set-cookie-notification parameter
        [ServiceWorkerResources + "notification_icon.py"] = (request, response, _) =>
        {
            if (request.Get("set-cookie-notification") is { Length: > 0 } cookie)
            {
                response.Add("Set-Cookie", "notification=" + cookie + "; Path=/");
            }

            response.Content = "not really an icon";
        },

        // The frames of about-blank-replacement.https.html (the nested frame must be intercepted by the service worker)
        [ServiceWorkerResources + "about-blank-replacement-frame.py"] = (request, response, _) => AboutBlankReplacementFrameResponse(request, response, AboutBlankReplacementFrame),
        [ServiceWorkerResources + "about-blank-replacement-ping-frame.py"] = (request, response, _) => AboutBlankReplacementFrameResponse(request, response, AboutBlankReplacementPingFrame),
        [ServiceWorkerResources + "about-blank-replacement-popup-frame.py"] = (request, response, _) => AboutBlankReplacementFrameResponse(request, response, AboutBlankReplacementPopupFrame),

        // A redirect to the url parameter, or a page that posts its URL to its parent
        [ServiceWorkerResources + "navigation-redirect-out-scope.py"] = NavigationRedirectScope,
        [ServiceWorkerResources + "navigation-redirect-scope1.py"] = NavigationRedirectScope,
        [ServiceWorkerResources + "navigation-redirect-scope2.py"] = NavigationRedirectScope,

        // A redirect of the POST requests, the path of the request otherwise
        [ServiceWorkerResources + "navigation-redirect-body.py"] = (request, response, _) =>
        {
            if (request.Method == "POST")
            {
                response.Status = 302;
                response.Add("Location", "./navigation-redirect-body.py?redirect");
                return;
            }

            response.Add("Content-Type", "text/plain");
            response.Content = request.Url.Pathname + request.Url.Search;
        },

        // A page that posts the Origin, Referer and Fetch Metadata headers of its request to its parent
        [ServiceWorkerResources + "navigation-headers-server.py"] = (request, response, _) =>
        {
            response.Status = 200;
            response.StatusText = "OK";
            response.Set("Content-Type", "text/html");
            response.Content = $$"""

                    <script>
                      self.addEventListener('load', evt => {
                        self.parent.postMessage({
                          origin: '{{request.Header("origin") ?? "not set"}}',
                          referer: '{{request.Header("referer") ?? "not set"}}',
                          'sec-fetch-site': '{{request.Header("sec-fetch-site") ?? "not set"}}',
                          'sec-fetch-mode': '{{request.Header("sec-fetch-mode") ?? "not set"}}',
                          'sec-fetch-dest': '{{request.Header("sec-fetch-dest") ?? "not set"}}',
                        });
                      });
                    </script>
                """;
        },

        // sandboxed-iframe-fetch-event-iframe.html with the CSP sandbox of the query, or sample.js with test
        [ServiceWorkerResources + "sandboxed-iframe-fetch-event-iframe.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            if (request.Has("test"))
            {
                response.Body = ReadServiceWorkerFile("resources/sample.js");
                return;
            }

            if (request.Get("sandbox") is { } sandbox)
            {
                response.Add("Content-Security-Policy", "sandbox " + sandbox);
            }

            response.Body = ReadServiceWorkerFile("resources/sandboxed-iframe-fetch-event-iframe.html");
        },

        // worker-interception-redirect-webworker.js with the greeting of the query
        [ServiceWorkerResources + "worker_interception_redirect_webworker.py"] = WorkerInterceptionRedirectWebWorker,
        [ServiceWorkerResources + "scope2/worker_interception_redirect_webworker.py"] = WorkerInterceptionRedirectWebWorker,
        [ServiceWorkerResources + "subdir/worker_interception_redirect_webworker.py"] = WorkerInterceptionRedirectWebWorker,

        // navigation-preload: a cookie set by the response, whose body is the value of the cookie in the request ("0" without it)
        ["/service-workers/service-worker/navigation-preload/resources/cookie.py"] = (request, response, _) =>
        {
            var sameSite = Required(request, "same-site");
            var cookieName = Required(request, "cookie-name");
            var cookie = cookieName + "=1; Secure; SameSite=" + sameSite;
            if (Required(request, "drop") == "1")
            {
                cookie += "; Max-Age=0";
            }

            var cookieInRequest = ParseRequestCookies(request).Find(value => value.Key == cookieName) is { Key: not null } requestCookie ? requestCookie.Value : "0";
            response.Add("Content-Type", "text/html");
            response.Add("Set-Cookie", cookie);
            response.Content = cookieInRequest;
        },

        // navigation-preload: the redirects of the type parameter
        ["/service-workers/service-worker/navigation-preload/resources/redirect-scope.py"] = (request, response, _) =>
        {
            if (request.Has("base"))
            {
                response.Add("Content-Type", "text/html");
                response.Content = "OK";
                return;
            }

            switch (Required(request, "type"))
            {
                case "normal":
                    response.Status = 302;
                    response.Add("Location", "redirect-redirected.html");
                    response.Add("Custom-Header", "hello");
                    break;
                case "no-location":
                    response.Status = 302;
                    response.Add("Content-Type", "text/html");
                    response.Add("Custom-Header", "hello");
                    break;
                case "no-location-with-body":
                    response.Status = 302;
                    response.Add("Content-Type", "text/html");
                    response.Add("Custom-Header", "hello");
                    response.Content = "<body>BODY</body>";
                    break;
                case "redirect-to-scope":
                    response.Status = 302;
                    response.Add("Location", "redirect-scope.py?type=redirect-to-scope2");
                    break;
                case "redirect-to-scope2":
                    response.Status = 302;
                    response.Add("Location", "redirect-scope.py?type=redirect-to-scope3");
                    break;
                case "redirect-to-scope3":
                    response.Status = 302;
                    response.Add("Location", "redirect-redirected.html");
                    response.Add("Custom-Header", "hello");
                    break;
            }
        },

        // navigation-preload: a chunked response ("0123456789", a chunk per millisecond), whose chunks are invalid with
        // use_broken_body. The headers of the broken response are valid (the response is received, only its body fails):
        // as the broken-chunked-encoding-scope.asis file, the raw chunks are the body.
        ["/service-workers/service-worker/navigation-preload/resources/chunked-encoding-scope.py"] = (request, response, _) =>
        {
            response.Add("Content-type", "text/html; charset=UTF-8");
            response.Streamed = true;
            response.Delay = TimeSpan.FromMilliseconds(10);
            response.Content = request.Has("use_broken_body")
                ? string.Concat(Enumerable.Range(0, 10).Select(index => "1\n" + index.ToString(CultureInfo.InvariantCulture) + "\n")) + "0\r\n\r\n"
                : "0123456789";
        },

        // navigation-preload: a gzip-encoded "Hello World" (the body is decoded by the network layer)
        ["/service-workers/service-worker/navigation-preload/resources/content-encoding-scope.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "text/html; charset=UTF-8");
            response.Set("Content-Encoding", "gzip");
            var content = "Hello World"u8.ToArray();
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(content);
            }

            response.Set("Content-Length", compressed.Length.ToString(CultureInfo.InvariantCulture));
            response.Body = content;
        },

        // navigation-preload: a redirect with type=redirect. The type == "normal" branch of the Python handler compares
        // bytes with a str, so it is never taken: the other types are an empty response.
        ["/service-workers/service-worker/navigation-preload/resources/resource-timing-scope.py"] = (request, response, _) =>
        {
            if (Required(request, "type") == "redirect")
            {
                response.Status = 302;
                response.Add("Location", "redirect-redirected.html");
            }
        },

        // navigation-preload: the headers of the request (uppercase names, lists of values), as JSON
        ["/service-workers/service-worker/navigation-preload/resources/request-headers-scope.py"] = (request, response, _) =>
        {
            var json = new StringBuilder("{");
            foreach (var group in request.Headers.GroupBy(header => header.Key.ToLowerInvariant(), StringComparer.Ordinal))
            {
                if (json.Length > 1)
                {
                    json.Append(", ");
                }

                json.Append(PythonJsonString(group.Key.ToUpperInvariant())).Append(": [").AppendJoin(", ", group.Select(header => PythonJsonString(header.Value))).Append(']');
            }

            response.Add("Content-Type", "text/html");
            response.Body = Encoding.UTF8.GetBytes(json.Append('}').ToString());
        },
    };

    // resources/service-worker-csp-worker.py
    private const string CspWorkerDefault = """

    importScripts('worker-testharness.js');
    importScripts('test-helpers.sub.js');
    importScripts('/common/get-host-info.sub.js');

    var host_info = get_host_info();

    test(function() {
        var import_script_failed = false;
        try {
          importScripts(host_info.HTTPS_REMOTE_ORIGIN +
            base_path() + 'empty.js');
        } catch(e) {
          import_script_failed = true;
        }
        assert_true(import_script_failed,
                    'Importing the other origins script should fail.');
      }, 'importScripts test for default-src');

    test(function() {
        assert_throws_js(EvalError,
                         function() { eval('1 + 1'); },
                         'eval() should throw EvalError.')
        assert_throws_js(EvalError,
                         function() { new Function('1 + 1'); },
                         'new Function() should throw EvalError.')
      }, 'eval test for default-src');

    async_test(function(t) {
        fetch(host_info.HTTPS_REMOTE_ORIGIN +
              base_path() + 'fetch-access-control.py?ACAOrigin=*',
              {mode: 'cors'})
          .then(function(response){
              assert_unreached('fetch should fail.');
            }, function(){
              t.done();
            })
          .catch(unreached_rejection(t));
      }, 'Fetch test for default-src');

    async_test(function(t) {
        var REDIRECT_URL = host_info.HTTPS_ORIGIN +
          base_path() + 'redirect.py?Redirect=';
        var OTHER_BASE_URL = host_info.HTTPS_REMOTE_ORIGIN +
          base_path() + 'fetch-access-control.py?'
        fetch(REDIRECT_URL + encodeURIComponent(OTHER_BASE_URL + 'ACAOrigin=*'),
              {mode: 'cors'})
          .then(function(response){
              assert_unreached('Redirected fetch should fail.');
            }, function(){
              t.done();
            })
          .catch(unreached_rejection(t));
      }, 'Redirected fetch test for default-src');
    """;

    // resources/service-worker-csp-worker.py
    private const string CspWorkerScript = """

    importScripts('worker-testharness.js');
    importScripts('test-helpers.sub.js');
    importScripts('/common/get-host-info.sub.js');

    var host_info = get_host_info();

    test(function() {
        var import_script_failed = false;
        try {
          importScripts(host_info.HTTPS_REMOTE_ORIGIN +
            base_path() + 'empty.js');
        } catch(e) {
          import_script_failed = true;
        }
        assert_true(import_script_failed,
                    'Importing the other origins script should fail.');
      }, 'importScripts test for script-src');

    test(function() {
        assert_throws_js(EvalError,
                         function() { eval('1 + 1'); },
                         'eval() should throw EvalError.')
        assert_throws_js(EvalError,
                         function() { new Function('1 + 1'); },
                         'new Function() should throw EvalError.')
      }, 'eval test for script-src');

    async_test(function(t) {
        fetch(host_info.HTTPS_REMOTE_ORIGIN +
              base_path() + 'fetch-access-control.py?ACAOrigin=*',
              {mode: 'cors'})
          .then(function(response){
              t.done();
            }, function(){
              assert_unreached('fetch should not fail.');
            })
          .catch(unreached_rejection(t));
      }, 'Fetch test for script-src');

    async_test(function(t) {
        var REDIRECT_URL = host_info.HTTPS_ORIGIN +
          base_path() + 'redirect.py?Redirect=';
        var OTHER_BASE_URL = host_info.HTTPS_REMOTE_ORIGIN +
          base_path() + 'fetch-access-control.py?'
        fetch(REDIRECT_URL + encodeURIComponent(OTHER_BASE_URL + 'ACAOrigin=*'),
              {mode: 'cors'})
          .then(function(response){
              t.done();
            }, function(){
              assert_unreached('Redirected fetch should not fail.');
            })
          .catch(unreached_rejection(t));
      }, 'Redirected fetch test for script-src');
    """;

    // resources/service-worker-csp-worker.py
    private const string CspWorkerConnect = """

    importScripts('worker-testharness.js');
    importScripts('test-helpers.sub.js');
    importScripts('/common/get-host-info.sub.js');

    var host_info = get_host_info();

    test(function() {
        var import_script_failed = false;
        try {
          importScripts(host_info.HTTPS_REMOTE_ORIGIN +
            base_path() + 'empty.js');
        } catch(e) {
          import_script_failed = true;
        }
        assert_false(import_script_failed,
                     'Importing the other origins script should not fail.');
      }, 'importScripts test for connect-src');

    test(function() {
        var eval_failed = false;
        try {
          eval('1 + 1');
          new Function('1 + 1');
        } catch(e) {
          eval_failed = true;
        }
        assert_false(eval_failed,
                     'connect-src without unsafe-eval should not block eval().');
      }, 'eval test for connect-src');

    async_test(function(t) {
        fetch(host_info.HTTPS_REMOTE_ORIGIN +
              base_path() + 'fetch-access-control.py?ACAOrigin=*',
              {mode: 'cors'})
          .then(function(response){
              assert_unreached('fetch should fail.');
            }, function(){
              t.done();
            })
          .catch(unreached_rejection(t));
      }, 'Fetch test for connect-src');

    async_test(function(t) {
        var REDIRECT_URL = host_info.HTTPS_ORIGIN +
          base_path() + 'redirect.py?Redirect=';
        var OTHER_BASE_URL = host_info.HTTPS_REMOTE_ORIGIN +
          base_path() + 'fetch-access-control.py?'
        fetch(REDIRECT_URL + encodeURIComponent(OTHER_BASE_URL + 'ACAOrigin=*'),
              {mode: 'cors'})
          .then(function(response){
              assert_unreached('Redirected fetch should fail.');
            }, function(){
              t.done();
            })
          .catch(unreached_rejection(t));
      }, 'Redirected fetch test for connect-src');
    """;

    // resources/mint-new-worker.py
    private const string MintNewWorkerBody = """

    onactivate = (e) => e.waitUntil(clients.claim());
    var resolve_wait_until;
    var wait_until = new Promise(resolve => {
        resolve_wait_until = resolve;
      });
    onmessage = (e) => {
        if (e.data == 'wait')
          e.waitUntil(wait_until);
        if (e.data == 'go')
          resolve_wait_until();
      };
    """;

    // resources/update-claim-worker.py
    private const string UpdateClaimWorkerScript = """

    // Time stamp: %s
    // (This ensures the source text is *not* a byte-for-byte match with any
    // previously-fetched version of this script.)

    // This no-op fetch handler is necessary to bypass explicitly the no fetch
    // handler optimization by which this service worker script can be skipped.
    addEventListener('fetch', event => {
        return;
      });

    addEventListener('install', event => {
        event.waitUntil(self.skipWaiting());
      });

    addEventListener('activate', event => {
        event.waitUntil(self.clients.claim());
      });
    """;

    // resources/about-blank-replacement-frame.py
    private const string AboutBlankReplacementFrame = """

    <!doctype html>
    <html>
    <body>
    <script>
    function nestedLoaded() {
      parent.postMessage({ type: 'NESTED_LOADED' }, '*');
    }
    </script>
    <iframe src="?nested=true" id="nested" onload="nestedLoaded()"></iframe>
    <script>
    // Helper routine to make it slightly easier for our parent to find
    // the nested frame.
    function nested() {
      return document.getElementById('nested').contentWindow;
    }

    // NOTE: Make sure not to touch the iframe directly here.  We want to
    //       test the case where the initial about:blank document is not
    //       directly accessed before load.
    </script>
    </body>
    </html>

    """;

    // resources/about-blank-replacement-ping-frame.py
    private const string AboutBlankReplacementPingFrame = """

    <!doctype html>
    <html>
    <body>
    <script>
    function nestedLoaded() {
      parent.postMessage({ type: 'NESTED_LOADED' }, '*');
    }
    </script>
    <iframe src="?nested=true&amp;ping=true" id="nested" onload="nestedLoaded()"></iframe>
    <script>
    // Helper routine to make it slightly easier for our parent to find
    // the nested frame.
    function nested() {
      return document.getElementById('nested').contentWindow;
    }

    // This modifies the nested iframe immediately and does not wait for it to
    // load.  This effectively modifies the global for the initial about:blank
    // document.  Any modifications made here should be preserved after the
    // frame loads because the global should be re-used.
    let win = nested();
    if (win.location.href !== 'about:blank') {
      parent.postMessage({
        type: 'NESTED_LOADED',
        result: 'failed: nested iframe does not have an initial about:blank URL'
      }, '*');
    } else {
      win.navigator.serviceWorker.addEventListener('message', evt => {
        if (evt.data.type === 'PING') {
          evt.source.postMessage({
            type: 'PONG',
            location: win.location.toString()
          });
        }
      });
      win.navigator.serviceWorker.startMessages();
    }
    </script>
    </body>
    </html>

    """;

    // resources/about-blank-replacement-popup-frame.py
    private const string AboutBlankReplacementPopupFrame = """

    <!doctype html>
    <html>
    <body>
    <script>
    function nestedLoaded() {
      parent.postMessage({ type: 'NESTED_LOADED' }, '*');
    }

    let popup = window.open('?nested=true');
    popup.onload = nestedLoaded;

    addEventListener('unload', evt => {
      popup.close();
    }, { once: true });

    // Helper routine to make it slightly easier for our parent to find
    // the nested popup window.
    function nested() {
      return popup;
    }
    </script>
    </body>
    </html>

    """;

    // resources/navigation-redirect-scope1.py
    private const string NavigationRedirectScopeBody = """

    <!DOCTYPE html>
    <script>
    onmessage = event => {
      window.parent.postMessage(
          {
            id: event.data.id,
            result: location.href
          }, '*');
    };
    </script>

    """;
    private static readonly Lock UniqueTimeLock = new();
    private static long s_lastUniqueTime;

    /// <summary><c>request.GET[name]</c>: the first value of the parameter, or a <see cref="KeyNotFoundException"/> (the handler fails).</summary>
    private static string Required(WptHandlerRequest request, string name) => request.Get(name) ?? throw new KeyNotFoundException("The parameter '" + name + "' is missing");

    /// <summary>
    /// <c>time.time()</c> formatted with the decimals (<c>%f</c> is 6): the time is strictly increasing, so that the scripts
    /// that embed it (to be different for each request) are different even when the requests are made at the same time.
    /// </summary>
    private static string PythonTime(int decimals)
    {
        var microseconds = (DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10;
        lock (UniqueTimeLock)
        {
            if (microseconds <= s_lastUniqueTime)
            {
                microseconds = s_lastUniqueTime + 1;
            }

            s_lastUniqueTime = microseconds;
        }

        return (microseconds / 1_000_000).ToString(CultureInfo.InvariantCulture) + "." + (microseconds % 1_000_000).ToString("D6", CultureInfo.InvariantCulture) + new string('0', decimals - 6);
    }

    /// <summary><c>str(random.random())</c></summary>
    private static string PythonRandom() => (System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue).ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A file of the service-workers/service-worker directory (e.g. a file next to the handler).</summary>
    private static byte[] ReadServiceWorkerFile(string path) => ReadWptFile("service-workers/service-worker/" + path);

    /// <summary>Increments the counter of the key in the stash of the handler (<c>stash.take(key)</c> then <c>stash.put(key, count)</c>).</summary>
    private static int IncrementCounter(WptHandlerRequest request, WptStash stash, string key)
    {
        var count = (stash.TakeValue(key, request.Path) as int? ?? 0) + 1;
        stash.Put(key, count, request.Path);
        return count;
    }

    private static void AddNoCacheHeaders(WptHandlerResponse response)
    {
        response.Add("Cache-Control", "no-cache, must-revalidate");
        response.Add("Pragma", "no-cache");
    }

    // The headers of update-max-aged-worker.py and its imported script: cached for a day, modified now
    private static void AddMaxAgedHeaders(WptHandlerResponse response)
    {
        response.Add("Content-Type", "application/javascript");
        response.Add("Cache-Control", "max-age=86400");
        response.Add("Last-Modified", DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The <c>Set-Cookie</c> header of <c>response.delete_cookie(name)</c> of wptserve: the Morsel of an empty value that
    /// expired a day ago (the <c>Max-Age</c> of 0 is formatted as an empty string by <c>"%.0d"</c>, so it is omitted).
    /// </summary>
    private static string PythonDeleteCookie(string name)
    {
        var expires = DateTime.UtcNow.AddDays(-1);
        return name + "=; expires=" + expires.ToString("dd", CultureInfo.InvariantCulture) + " " + expires.ToString("MMM", CultureInfo.InvariantCulture).ToLowerInvariant() + " " + expires.ToString("yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT; Path=/";
    }

    /// <summary><c>json.dumps()</c> of the headers of the request (<c>{name: request.headers[name]}</c>): lowercase names, values joined by <c>", "</c>.</summary>
    private static string PythonRequestHeadersJson(WptHandlerRequest request)
        => "{" + string.Join(", ", request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Select(name => PythonJsonString(name) + ": " + PythonJsonString(request.Header(name) ?? ""))) + "}";

    // The Access-Control-Allow-Origin headers of the ACAOrigin parameter (a header per comma-separated value)
    private static void AddAllowOrigins(WptHandlerRequest request, WptHandlerResponse response)
    {
        if (request.Get("ACAOrigin") is { } origins)
        {
            foreach (var origin in origins.Split(','))
            {
                response.Add("Access-Control-Allow-Origin", origin);
            }
        }
    }

    // The CORS headers of the query of fetch-access-control.py and redirect.py: ACAOrigin, ACAHeaders, ACAMethods,
    // ACACredentials and ACEHeaders
    private static void AddServiceWorkerCorsHeaders(WptHandlerRequest request, WptHandlerResponse response)
    {
        AddAllowOrigins(request, response);
        foreach (var suffix in (ReadOnlySpan<string>)["Headers", "Methods", "Credentials"])
        {
            if (request.Get("ACA" + suffix) is { } value)
            {
                response.Add("Access-Control-Allow-" + suffix, value);
            }
        }

        if (request.Get("ACEHeaders") is { } exposeHeaders)
        {
            response.Add("Access-Control-Expose-Headers", exposeHeaders);
        }
    }

    // service-workers/service-worker/resources/redirect.py (and scope1/redirect.py, which loads it)
    private static void ServiceWorkerRedirect(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Status = request.Get("Status") is { } status ? int.Parse(status, NumberStyles.Integer, CultureInfo.InvariantCulture) : 302;
        response.Add("Location", Required(request, "Redirect"));
        AddServiceWorkerCorsHeaders(request, response);
    }

    /// <summary>
    /// service-workers/service-worker/resources/fetch-access-control.py: a PNG image (PNGIMAGE), a video (VIDEO, with range
    /// requests with PartialContent), a 401 response (Auth without credentials, AuthFail), or a script calling
    /// <c>report()</c> with the request (method, headers, body, parameters, credentials, cookie), with the CORS headers of
    /// the query.
    /// </summary>
    private static void FetchAccessControl(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("X-ServiceWorker-ServerHeader", "SetInTheServer");
        AddServiceWorkerCorsHeaders(request, response);
        var (username, password) = request.Auth;
        if ((request.Has("Auth") && string.IsNullOrEmpty(username)) || request.Has("AuthFail"))
        {
            response.Status = 401;
            response.Add("WWW-Authenticate", "Basic realm=\"Restricted\"");
            response.Content = "Authentication canceled";
            return;
        }

        if (request.Has("PNGIMAGE"))
        {
            response.Add("Content-Type", "image/png");
            response.Body = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsQAAA7EAZUrDhsAAAAhSURBVDhPY3wro/KfgQLABKXJBqMGjBoAAqMGDLwBDAwAEsoCTFWunmQAAAAASUVORK5CYII=");
            return;
        }

        if (request.Has("VIDEO"))
        {
            var mp4 = request.Has("mp4");
            response.Add("Content-Type", mp4 ? "video/mp4" : "video/webm");
            var body = ReadWptFile(mp4 ? "media/movie_5.mp4" : "media/movie_5.webm");
            if (!request.Has("PartialContent"))
            {
                response.Body = body;
                return;
            }

            // A range request: the first byte without a Range header (or with bytes=0-), to force range requests
            if (body.Length < 1)
            {
                response.Status = 500;
                response.Content = "file is too small for range requests";
                return;
            }

            var start = 0;
            var end = 0;
            if (request.Header("Range") is { } range)
            {
                end = body.Length - 1;
                var parts = range["bytes=".Length..].Split('-');
                if (parts[0] == "0" && parts[1].Length == 0)
                {
                    end = start;
                }

                if (parts[0].Length > 0)
                {
                    start = int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
                }

                if (parts[1].Length > 0)
                {
                    end = int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
            }

            response.Status = 206;
            response.Add("Accept-Ranges", "bytes");
            response.Add("Content-Length", (end - start + 1).ToString(CultureInfo.InvariantCulture));
            response.Add("Content-Range", string.Create(CultureInfo.InvariantCulture, $"bytes {start}-{end}/{body.Length}"));
            response.Body = body[start..Math.Min(end + 1, body.Length)];
            return;
        }

        // The fields of a form (request.POST): json.dumps() fails on the content of a file (bytes), as the Python handler
        var postData = new List<string>();
        foreach (var field in request.Post)
        {
            if (field.Value.StringValue is not { } value)
                throw new InvalidOperationException("Object of type bytes is not JSON serializable");

            // The fields are byte strings
            var name = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(field.Key));
            if (!postData.Exists(item => item.StartsWith(PythonJsonString(name) + ": ", StringComparison.Ordinal)))
            {
                postData.Add(PythonJsonString(name) + ": " + PythonJsonString(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(value))));
            }
        }

        var getData = request.GetParameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Select(name => PythonJsonString(name) + ": " + PythonJsonString(request.Get(name)!));
        var cookie = ParseRequestCookies(request).Find(item => item.Key == "cookie") is { Key: not null } cookieValue ? cookieValue.Value : "undefined";
        var hasUsername = !string.IsNullOrEmpty(username);
        var json = "{\"jsonpResult\": \"success\", \"method\": " + PythonJsonString(request.Method)
            + ", \"headers\": " + PythonRequestHeadersJson(request)
            + ", \"body\": " + PythonJsonString(Encoding.Latin1.GetString(request.Body))
            + ", \"files\": [], \"GET\": {" + string.Join(", ", getData) + "}"
            + ", \"POST\": {" + string.Join(", ", postData) + "}"
            + ", \"username\": " + PythonJsonString(hasUsername ? username! : "undefined")
            + ", \"password\": " + PythonJsonString(hasUsername ? password ?? "" : "undefined")
            + ", \"cookie\": " + PythonJsonString(cookie) + "}";
        response.Content = "report( " + json + " )";
    }

    /// <summary>
    /// service-workers/service-worker/resources/update-worker.py: a script whose content changes for each request of the
    /// Key (the number of requests); the second request is the response of the Mode (normal, bad_mime_type, not_found,
    /// redirect, syntax_error, throw_install).
    /// </summary>
    private static void UpdateWorker(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var key = Required(request, "Key");
        var mode = Required(request, "Mode");
        var visitedCount = IncrementCounter(request, stash, key);
        var count = visitedCount.ToString(CultureInfo.InvariantCulture);
        if (visitedCount == 2)
        {
            switch (mode)
            {
                case "normal":
                    break;
                case "bad_mime_type":
                    Ok(mimeType: "text/html");
                    return;
                case "not_found":
                    response.Status = 404;
                    response.Add("Content-Type", "text/plain");
                    response.Content = "Page not found";
                    return;
                case "redirect":
                    response.Status = 301;
                    AddNoCacheHeaders(response);
                    response.Add("Content-Type", "application/javascript");
                    response.Add("Location", request.Get("Redirect") is { } redirect ? Uri.UnescapeDataString(redirect) : "empty.js");
                    response.Content = "/* " + count + " */";
                    return;
                case "syntax_error":
                    Ok(extraBody: "badsyntax(isbad;");
                    return;
                case "throw_install":
                    Ok(extraBody: "addEventListener('install', function(e) { throw new Error('boom'); });");
                    return;
            }
        }

        Ok();

        void Ok(string extraBody = "", string mimeType = "application/javascript")
        {
            AddNoCacheHeaders(response);
            response.Add("Content-Type", mimeType);
            response.Content = "/* " + count + " */ " + extraBody;
        }
    }

    // import-scripts-echo.py (and the copies of scope2 and subdir, with their suffix)
    private static void ImportScriptsEcho(WptHandlerRequest request, WptHandlerResponse response, string suffix)
    {
        AddNoCacheHeaders(response);
        response.Add("Content-Type", "application/javascript");
        response.Content = "echo_output = \"" + Required(request, "msg") + suffix + "\";\n";
    }

    // test-request-headers-worker.py and test-request-mode-worker.py: the script with the headers of the request (%HEADERS%)
    // and a UUID (%UUID%)
    private static void RequestHeadersWorker(WptHandlerRequest request, WptHandlerResponse response, string script)
    {
        var body = Encoding.Latin1.GetString(ReadServiceWorkerFile("resources/" + script))
            .Replace("%HEADERS%", PythonRequestHeadersJson(request), StringComparison.Ordinal)
            .Replace("%UUID%", Guid.NewGuid().ToString(), StringComparison.Ordinal);
        response.Add("ETag", "etag");
        response.Add("Content-Type", "text/javascript");
        response.Content = body;
    }

    // soft-update-fetch-metadata.py: records the Service-Worker and Fetch Metadata headers of the requests of a service
    // worker script (and of its imported script), returned as JSON with get_records
    private static void SoftUpdateFetchMetadata(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var token = Required(request, "token");
        var records = stash.TakeValue(token, request.Path) as List<string> ?? [];
        var isGetRecords = request.Has("get_records");
        if (!isGetRecords)
        {
            records.Add("{\"url\": " + PythonJsonString(request.Url.Href)
                + ", \"service-worker\": " + HeaderValue("Service-Worker")
                + ", \"sec-fetch-dest\": " + HeaderValue("Sec-Fetch-Dest")
                + ", \"sec-fetch-mode\": " + HeaderValue("Sec-Fetch-Mode")
                + ", \"sec-fetch-site\": " + HeaderValue("Sec-Fetch-Site") + "}");
        }

        stash.Put(token, records, request.Path);
        if (isGetRecords)
        {
            response.Add("Content-Type", "application/json");
            response.Add("Cache-Control", "no-store");
            response.Content = "[" + string.Join(", ", records) + "]";
            return;
        }

        var importedScriptUrl = "./soft-update-fetch-metadata.py?token=" + DecodeUtf8(request.Get("importtoken", "")!) + "&kind=imported";
        var body = request.Get("kind", "classic") switch
        {
            "imported" => "// Imported script.\n",
            "classic-with-import" => "importScripts('" + importedScriptUrl + "');\nonfetch = e => { e.request; };\n",
            "module-with-import" => "import '" + importedScriptUrl + "';\nonfetch = e => { e.request; };\n",
            _ => "onfetch = e => { e.request; };\n",
        };
        response.Add("Content-Type", "text/javascript");
        response.Add("Cache-Control", "no-store");
        response.Body = Encoding.UTF8.GetBytes(body);

        string HeaderValue(string name) => request.Header(name) is { } value ? PythonJsonString(DecodeUtf8(value)) : "null";
    }

    // about-blank-replacement-*-frame.py: the page, or a failure message for the nested frame (which must be intercepted by
    // the service worker)
    private static void AboutBlankReplacementFrameResponse(WptHandlerRequest request, WptHandlerResponse response, string page)
    {
        response.Add("Content-Type", "text/html");
        response.Content = request.Has("nested") ? "failed: nested frame was not intercepted by the service worker" : page;
    }

    // navigation-redirect-out-scope.py, navigation-redirect-scope1.py and navigation-redirect-scope2.py
    private static void NavigationRedirectScope(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Get("url") is { } url)
        {
            response.Status = 302;
            response.Add("Location", url);
            return;
        }

        response.Status = request.Has("noLocationRedirect") ? 302 : 200;
        response.Add("content-type", "text/html");
        response.Content = NavigationRedirectScopeBody;
    }

    // worker_interception_redirect_webworker.py (scope2/ and subdir/ load the one of resources/): the worker script with
    // the greeting of the query
    private static void WorkerInterceptionRedirectWebWorker(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var body = Encoding.Latin1.GetString(ReadServiceWorkerFile("resources/worker-interception-redirect-webworker.js"));
        response.Add("Content-Type", "text/javascript");
        response.Content = body.Replace("%GREETING_TEXT%", request.Get("greeting", ""), StringComparison.Ordinal);
    }
}
