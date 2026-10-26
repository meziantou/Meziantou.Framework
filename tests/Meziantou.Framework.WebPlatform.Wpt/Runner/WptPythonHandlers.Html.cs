using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>html</c>, <c>dom</c>, <c>encoding</c>, <c>url</c>, <c>workers</c> and <c>FileAPI</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> HtmlHandlers = new(StringComparer.Ordinal)
    {
        // html/browsers/browsing-the-web: a slow empty HTML document (2 seconds), a redirect to the location parameter, a
        // redirect followed once per uuid, and documents with a Refresh header
        ["/html/browsers/browsing-the-web/overlapping-navigations-and-traversals/resources/slow.py"] = (_, response, _) =>
        {
            response.Delay = TimeSpan.FromSeconds(2);
            response.Set("Content-Type", "text/html");
        },
        ["/html/browsers/browsing-the-web/navigating-across-documents/resources/redirect.py"] = (request, response, _) =>
        {
            response.Status = 302;
            response.Set("Location", request.Get("location"));
        },
        ["/html/browsers/browsing-the-web/navigating-across-documents/resources/no-cache-single-redirect.py"] = (request, response, stash) =>
        {
            response.Set("Cache-Control", "no-store");
            var uuid = request.Get("uuid") ?? "";
            if (stash.TakeValue(uuid, request.Path) is null)
            {
                response.Status = 302;
                response.Set("Location", request.Get("location"));
                stash.Put(uuid, "sentinel value", request.Path);
                return;
            }

            Respond(response, "text/html", "No redirect"u8.ToArray());
        },
        ["/html/browsers/browsing-the-web/navigating-across-documents/refresh/resources/refresh.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "text/plain");
            response.Set("Refresh", "0;./refreshed.txt?\u0080\u00FF");
            response.Content = "Not refreshed.\n";
        },
        ["/html/browsers/browsing-the-web/navigating-across-documents/refresh/resources/refresh-with-svg.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "image/svg+xml");
            response.Set("Refresh", "0;./refreshed.txt");
            response.Content = "<svg version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\"><text y=\"14\" text-anchor=\"left\" >Not refreshed.</text></svg>\n";
        },

        // the-script-element/module/dynamic-import/beta/redirect.py: a redirect (302 or the status parameter) to the location parameter
        ["/html/semantics/scripting-1/the-script-element/module/dynamic-import/beta/redirect.py"] = (request, response, _) =>
        {
            response.Status = int.TryParse(request.Get("status"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var status) ? status : 302;
            response.Set("Location", request.Get("location"));
        },

        // the-script-element/css-module/resources/record-fetch.py: counts the fetches of a CSS module (incCount), or returns
        // the count (getCount); the count is taken from the stash in both cases
        ["/html/semantics/scripting-1/the-script-element/css-module/resources/record-fetch.py"] = (request, response, stash) =>
        {
            var key = request.Get("key") ?? "";
            var count = stash.TakeValue(key, request.Path) as int? ?? 0;
            switch (request.Get("action"))
            {
                case "incCount":
                    stash.Put(key, count + 1, request.Path);
                    response.Set("Content-Type", "text/css");
                    response.Content = "#test { background-color: #FF0000; }";
                    break;

                case "getCount":
                    response.Set("Content-Type", "text/json");
                    response.Content = "{\"count\": " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " }";
                    break;

                default:
                    response.Status = 400;
                    break;
            }
        },

        // history-traversal/resources/unset_context_name_stash.py: the stashed value of the id parameter, or NONE (the
        // handler compares the method, a byte string, with a string: it never stores the value)
        ["/html/browsers/browsing-the-web/history-traversal/resources/unset_context_name_stash.py"] = (request, response, stash) =>
        {
            response.Set("Content-Type", "text/html");
            response.Content = stash.TakeValue(request.Get("id") ?? "", request.Path) as string ?? "NONE";
        },

        // FileAPI/file/resources/echo-content-escaped.py: the request body with the controls, the non-ASCII bytes and the
        // backslashes escaped (CRLF is kept)
        ["/FileAPI/file/resources/echo-content-escaped.py"] = (request, response, _) =>
        {
            var content = new StringBuilder();
            foreach (var b in request.Body)
            {
                content.Append(b is <= 0x1F or >= 0x7F ? "\\x" + b.ToString("x2", CultureInfo.InvariantCulture) : b == '\\' ? "\\\\" : ((char)b).ToString());
            }

            response.Set("Content-Type", "text/plain; charset=UTF-8");
            response.Set("X-Request-Method", request.Method);
            response.Set("X-Request-Content-Length", request.Request.Body is null ? "NO" : request.Body.Length.ToString(CultureInfo.InvariantCulture));
            response.Set("X-Request-Content-Type", request.Header("Content-Type") ?? "NO");
            response.Content = content.ToString().Replace("\\x0d\\x0a", "\r\n", StringComparison.Ordinal);
        },

        // html/semantics/forms/form-submission-0/resources/form-submission.py: OK when the body is the expected one
        ["/html/semantics/forms/form-submission-0/resources/form-submission.py"] = (request, response, _) =>
        {
            var contentType = request.Header("Content-Type");
            bool result;
            if (request.Get("query") == "1")
            {
                result = contentType switch
                {
                    "application/x-www-form-urlencoded" => request.Body.AsSpan().SequenceEqual("foo=bara"u8),
                    "text/plain" => request.Body.AsSpan().SequenceEqual("qux=baz\r\n"u8),
                    _ => GetPostField(request.Body, contentType, "foo") == "bar",
                };
            }
            else if (request.Get("expected_body") is { } expectedBody)
            {
                result = request.Body.AsSpan().SequenceEqual(Encoding.Latin1.GetBytes(expectedBody));
            }
            else
            {
                result = false;
            }

            TextResponse(response, result ? "OK" : "FAIL");
        },

        // html/semantics/forms/form-submission-0/form-echo.py: the bytes of the body in hexadecimal
        ["/html/semantics/forms/form-submission-0/form-echo.py"] = (request, response, _) =>
            TextResponse(response, string.Join(' ', request.Body.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)))),

        // html/semantics/forms/form-submission-0/resources/file-submission.py: posts the testinput field to the parent
        ["/html/semantics/forms/form-submission-0/resources/file-submission.py"] = (request, response, _) =>
        {
            var value = GetPostField(request.Body, request.Header("Content-Type"), "testinput") ?? "";
            Respond(response, "text/html", Encoding.UTF8.GetBytes("<script>parent.postMessage(" + System.Text.Json.JsonSerializer.Serialize(value) + ", '*');</script>"));
        },

        // html/semantics/forms/form-submission-target/form-target-iframe-helper.py: OK
        ["/html/semantics/forms/form-submission-target/form-target-iframe-helper.py"] = (_, response, _) => TextResponse(response, "OK"),

        // html/semantics/forms/form-submission-target/resources/form-target-request-header-helper.py: a document that posts OK
        // (when the request has a Content-Type header) or FAIL on the BroadcastChannel named by the value of the body, then closes its window
        ["/html/semantics/forms/form-submission-target/resources/form-target-request-header-helper.py"] = (request, response, _) =>
        {
            var result = string.IsNullOrEmpty(request.Header("Content-Type")) ? "FAIL" : "OK";
            var channelName = Encoding.UTF8.GetString(request.Body).Split('=')[1];
            Respond(response, "text/html", Encoding.UTF8.GetBytes($"\n<script>\nconst channel = new BroadcastChannel('{channelName}');\nchannel.postMessage('{result}', '*');\nwindow.close();\n</script>\n"));
        },

        // html/semantics/scripting-1/the-script-element/serve-with-content-type.py: a file of the directory (fn) with a Content-Type
        // (ct), or a 400 response when a parameter is missing
        ["/html/semantics/scripting-1/the-script-element/serve-with-content-type.py"] = (request, response, _) =>
        {
            var file = Path.Combine(WptPaths.Wpt, "html", "semantics", "scripting-1", "the-script-element", (request.Get("fn") ?? "").Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file) && request.Get("ct") is { } contentType)
            {
                Respond(response, contentType, File.ReadAllBytes(file));
            }
            else
            {
                response.Status = 400;
                response.Set("Content-Type", "text/plain");
            }
        },

        // The load-error-events.py handlers of the module tests: a module importing an existing module (test=..._load)
        // or a missing one; the classic version returns a 404 status for the error tests
        ["/html/semantics/scripting-1/the-script-element/resources/load-error-events.py"] = LoadErrorEvents,
        ["/html/semantics/scripting-1/the-script-element/json-module/load-error-events.py"] = LoadErrorEvents,
        ["/html/semantics/scripting-1/the-script-element/css-module/resources/load-error-events.py"] = LoadErrorEvents,
        ["/html/semantics/scripting-1/the-script-element/text-module/load-error-events.py"] = LoadErrorEvents,

        // the-script-element/log.py: a script logging the id parameter, delayed by sec seconds
        ["/html/semantics/scripting-1/the-script-element/log.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/javascript");
            response.Delay = TimeSpan.FromSeconds(ParseInt(request.Get("sec"), 0));
            response.Content = "log('" + ParseInt(request.Get("id"), 0).ToString(CultureInfo.InvariantCulture) + "')";
        },

        // The referrer-checker.py handlers of the module tests: a module (JavaScript, CSS or JSON) containing the Referer
        // header of the request
        ["/html/semantics/scripting-1/the-script-element/module/resources/referrer-checker.py"] = (request, response, _) =>
            ReferrerChecker(request, response, "text/javascript", "export const referrer = '{0}';"),
        ["/html/semantics/scripting-1/the-script-element/css-module/resources/referrer-checker.py"] = (request, response, _) =>
            ReferrerChecker(request, response, "text/css", ".referrer { content: \"{0}\" }"),
        ["/html/semantics/scripting-1/the-script-element/json-module/referrer-checker.py"] = (request, response, _) =>
            ReferrerChecker(request, response, "application/json", "{\"referrer\": \"{0}\"}"),
        ["/html/semantics/scripting-1/the-script-element/text-module/referrer-checker.py"] = (request, response, _) =>
            ReferrerChecker(request, response, "text/plain", "{0}"),

        // the-script-element/text-module/http-accept-header-checker.py: a text module containing the Accept header
        ["/html/semantics/scripting-1/the-script-element/text-module/http-accept-header-checker.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/plain");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = request.Header("Accept") ?? "";
        },

        // the-script-element/json-module/http-accept-header-checker.py: a JSON module containing the Accept header
        ["/html/semantics/scripting-1/the-script-element/json-module/http-accept-header-checker.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/json");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = "{\"accept\": \"" + (request.Header("Accept") ?? "") + "\"}";
        },

        // the-script-element/module/resources/delayed-modulescript.py: a module delayed by ms milliseconds (500 by default)
        ["/html/semantics/scripting-1/the-script-element/module/resources/delayed-modulescript.py"] = (request, response, _) =>
        {
            response.Delay = Milliseconds(request.Get("ms"), 500);
            response.Add("Content-type", "text/javascript");
            response.Content = "export let delayedLoaded = true;";
        },

        // the-script-element/cacheable-script-throw.py: a cacheable script that throws
        ["/html/semantics/scripting-1/the-script-element/cacheable-script-throw.py"] = (_, response, _) =>
        {
            response.Add("Content-Type", "text/javascript");
            response.Add("Cache-control", "public, max-age=100");
            response.Content = "throw('fox');";
        },

        // the-script-element/script-not-found-not-executed*.py: a script with a 404 status, and a script with a 200 status
        ["/html/semantics/scripting-1/the-script-element/script-not-found-not-executed.py"] = (_, response, _) =>
        {
            response.Status = 404;
            response.Add("Content-Type", "text/javascript");
            response.Content = "test1_token = \"script executed\";";
        },
        ["/html/semantics/scripting-1/the-script-element/script-not-found-not-executed-2.py"] = (_, response, _) =>
        {
            response.Add("Content-Type", "text/javascript");
            response.Content = "test2_token = \"script executed\";";
        },

        // the-script-element/resources/cross-origin.py: a script telling whether the request has the milk=1 cookie, with
        // the CORS headers allowing the credentials of the Origin of the request
        ["/html/semantics/scripting-1/the-script-element/resources/cross-origin.py"] = (request, response, _) =>
        {
            if (request.Header("Origin") is { } origin)
            {
                response.Set("Access-Control-Allow-Origin", origin);
                response.Set("Access-Control-Allow-Methods", "GET");
                response.Set("Access-Control-Allow-Credentials", "true");
            }

            if (request.Method == "OPTIONS")
                return;

            response.Add("Content-Type", "text/javascript");
            response.Content = HasMilkCookie(request) ? "var included = true;" : "var included = false;";
        },

        ["/html/semantics/scripting-1/the-script-element/moving-between-documents/resources/moving-between-documents-iframe.py"] = MovingBetweenDocumentsIframe,

        // the-script-element/moving-between-documents/resources/slow-flag-setter.py: an empty style sheet delayed by 3 seconds
        // (result=css), or a script delayed by 2 seconds that sets didExecute, has a parse error or is a 404 response
        ["/html/semantics/scripting-1/the-script-element/moving-between-documents/resources/slow-flag-setter.py"] = (request, response, _) =>
        {
            var result = request.Get("result") ?? "success";
            if (result == "css")
            {
                response.Delay = TimeSpan.FromSeconds(3);
                response.Add("Content-Type", "text/css");
                return;
            }

            response.Delay = TimeSpan.FromSeconds(2);
            switch (result)
            {
                case "parse-error":
                    response.Add("Content-Type", "text/javascript");
                    response.Content = "1=2 parse error;";
                    break;

                case "fetch-error":
                    response.Status = 404;
                    response.Add("Content-Type", "text/plain");
                    response.Content = "window.didExecute = \"fetch error\";";
                    break;

                default:
                    response.Add("Content-Type", "text/javascript");
                    response.Content = """

                          fetch('exec');
                          console.log('exec');
                          if (!window.readyToEvaluate) {
                            window.didExecute = "executed too early";
                          } else {
                            window.didExecute = "executed";
                          }

                    """;
                    break;
            }
        },

        ["/html/semantics/scripting-1/the-script-element/serve-custom-response.py"] = ServeCustomResponse,
        ["/html/semantics/scripting-1/the-script-element/module-fetch-tester.py"] = ModuleFetchTester,

        // the-script-element/text-module/serve-*-then-*.py: a JavaScript module then a text file (or the reverse) for the
        // successive requests of the key
        ["/html/semantics/scripting-1/the-script-element/text-module/serve-js-then-text.py"] = (request, response, stash) => ServeJsOrText(request, response, stash, jsFirst: true),
        ["/html/semantics/scripting-1/the-script-element/text-module/serve-text-then-js.py"] = (request, response, stash) => ServeJsOrText(request, response, stash, jsFirst: false),

        // preload/resources/echo-referrer.py: a script storing the Referer header of the request in window.referrers[uid]
        ["/preload/resources/echo-referrer.py"] = (request, response, _) =>
        {
            response.Add("Access-Control-Allow-Origin", "*");
            response.Add("Content-Type", "text/javascript");
            response.Content = "\n    window.referrers[\"" + (request.Get("uid") ?? "") + "\"] = \"" + (request.Header("Referer") ?? "") + "\";\n    ";
        },

        // encoding/resources/single-byte-raw.py: the bytes 0x00 to 0xFE in the encoding of the label
        ["/encoding/resources/single-byte-raw.py"] = (request, response, _) => Respond(response, "text/plain;charset=" + request.Get("label"), [.. Enumerable.Range(0, 255).Select(i => (byte)i)]),
        ["/encoding/resources/text-plain-charset.py"] = (request, response, _) => Respond(response, "text/plain;charset=" + request.Get("label"), "hello encoding"u8.ToArray()),
        ["/encoding/resources/text-html-meta-charset.py"] = (request, response, _) => Respond(response, "text/html", Encoding.Latin1.GetBytes("<meta charset=\"" + request.Get("label") + "\">hello encoding")),

        // encoding/resources/json-charset.py: a JSON document encoded as UTF-8, with the type and the charset parameters as its Content-Type
        ["/encoding/resources/json-charset.py"] = (request, response, _) =>
        {
            var contentType = request.Get("type") ?? "application/json";
            if (request.Get("charset") is { } charset)
            {
                contentType += ";charset=" + charset;
            }

            response.Set("Content-Type", contentType);
            response.Set("X-Content-Type-Options", "nosniff");
            response.Body = """{"text":"☃é"}"""u8.ToArray();
        },

        // workers/baseurl/beta/*.py: a redirect to the script of the same name in the gamma directory (the base URL of the
        // worker is the URL of the response), with a body that must not run
        ["/workers/baseurl/beta/import.py"] = (_, response, _) => BaseUrlRedirect(response, "import.js"),
        ["/workers/baseurl/beta/importScripts.py"] = (_, response, _) => BaseUrlRedirect(response, "importScripts.js"),
        ["/workers/baseurl/beta/worker.py"] = (_, response, _) => BaseUrlRedirect(response, "worker.js"),
        ["/workers/baseurl/beta/xhr-worker.py"] = (_, response, _) => BaseUrlRedirect(response, "xhr-worker.js"),
        ["/workers/baseurl/beta/xhr.py"] = (_, response, _) => BaseUrlRedirect(response, "xhr.js"),

        // workers/interfaces/WorkerGlobalScope/location/helper-redirect.py
        ["/workers/interfaces/WorkerGlobalScope/location/helper-redirect.py"] = (_, response, _) =>
        {
            response.Status = 302;
            response.Add("Location", "post-location-members.js?a");
        },

        // workers/modules/resources/redirect.py: a redirect to the location parameter
        ["/workers/modules/resources/redirect.py"] = (request, response, _) =>
        {
            response.Status = 302;
            response.Set("Location", RequiredParameter(request, "location"));
        },

        // workers/modules/resources/export-on-load-script.py: a module script imported from another origin (it answers the
        // CORS preflight of the Service-Worker header too), ending with a comment that changes every second
        ["/workers/modules/resources/export-on-load-script.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Headers", "Service-Worker");
            response.Content = "export const importedModules = ['export-on-load-script.js'];// " + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        },

        // workers/modules/resources/*-checker.py and *-credentials.py: a worker script that posts (or a module that exports) the
        // Referer header or the value of the COOKIE_NAME cookie of its request
        ["/workers/modules/resources/postmessage-referrer-checker.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Content = PostMessageWorkerScript(request.Header("Referer") ?? "");
        },
        ["/workers/modules/resources/export-referrer-checker.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Content = "export const referrer = '" + (request.Header("Referer") ?? "") + "';";
        },
        ["/workers/modules/resources/postmessage-credentials.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Set("Access-Control-Allow-Credentials", "true");
            if (request.Header("Origin") is { Length: > 0 } origin)
            {
                response.Set("Access-Control-Allow-Origin", origin);
            }

            response.Content = PostMessageWorkerScript(GetCookieName(request));
        },
        ["/workers/modules/resources/export-credentials.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Set("Access-Control-Allow-Credentials", "true");
            if (request.Header("Origin") is { Length: > 0 } origin)
            {
                response.Set("Access-Control-Allow-Origin", origin);
            }

            response.Content = "export const cookie = '" + GetCookieName(request) + "';";
        },

        // workers/semantics/encodings/003-1.py: whether the x parameter is "å" encoded as UTF-8 (the URL of a worker request is UTF-8)
        ["/workers/semantics/encodings/003-1.py"] = (request, response, _) => response.Content = DecodeUtf8(RequiredParameter(request, "x")) == "å" ? "PASS" : "FAIL",

        // workers/support/imported_script.py: an empty script with the mime parameter as its Content-Type
        ["/workers/support/imported_script.py"] = (request, response, _) => response.Set("Content-Type", RequiredParameter(request, "mime")),

        // dom/nodes/Document-contentType/support/contenttype_setter.py: a document whose Content-Type is type/subtype (none with
        // removeContentType), with a <meta http-equiv=Content-Type> of the mime parameter
        ["/dom/nodes/Document-contentType/support/contenttype_setter.py"] = (request, response, _) =>
        {
            if (request.Get("type") is { Length: > 0 } type && request.Get("subtype") is { Length: > 0 } subtype)
            {
                response.Set("Content-Type", type + "/" + subtype);
            }

            if (request.Get("removeContentType") is { Length: > 0 })
            {
                response.Set("Content-Type", null);
            }

            var content = "<head>";
            if (request.Get("mime") is { Length: > 0 } mime)
            {
                content += "<meta http-equiv=\"Content-Type\" content=\"" + mime + "; charset=utf-8\"/>";
            }

            response.Content = content + "</head>";
        },

        // html/webappapis/scripting/processing-model-2/unhandled-promise-rejections/support/promise-access-control.py: a
        // script that rejects a promise, with Access-Control-Allow-Origin: * unless the allow parameter is "false"
        ["/html/webappapis/scripting/processing-model-2/unhandled-promise-rejections/support/promise-access-control.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "application/javascript");
            if (request.Get("allow", "false") != "false")
            {
                response.Set("Access-Control-Allow-Origin", "*");
            }

            response.Content = "function handleRejectedPromise(promise) { promise.catch(() => {}); }\n(function() { new Promise(function(resolve, reject) { reject(42); }); })();\n";
        },

        // dom/nodes/encoding.py: a document declaring the label in a <meta charset>
        ["/dom/nodes/encoding.py"] = (request, response, _) => Respond(response, "text/html", Encoding.UTF8.GetBytes("<!doctype html><meta charset=\"" + System.Net.WebUtility.HtmlEncode(DecodeUtf8(request.Get("label") ?? "")) + "\">")),

        // The Refresh header is the input query parameter
        ["/html/semantics/document-metadata/the-meta-element/pragma-directives/attr-meta-http-equiv-refresh/support/refresh.py"] = (request, response, _) =>
        {
            Respond(response, "text/html", "<!doctype html>refresh.py\n"u8.ToArray());
            response.Set("Refresh", DecodeUtf8(request.Get("input") ?? ""));
        },

        // interactions-of-styling-and-scripting/stylesheet.py: a style sheet coloring h1 brown (stylesNotMatchingEnvironment)
        // or purple, delayed by the delay parameter (seconds)
        ["/html/semantics/document-metadata/interactions-of-styling-and-scripting/stylesheet.py"] = (request, response, _) =>
        {
            response.Delay = TimeSpan.FromSeconds(ParseInt(request.Get("delay"), 0));
            Respond(response, "text/css", request.Has("stylesNotMatchingEnvironment") ? "h1 {color: brown;}"u8.ToArray() : "h1 {color: purple;}"u8.ToArray());
        },

        // the-link-element/stylesheet.py: counts the requests of the style sheet of the id (stashed); with the count parameter,
        // returns the count and resets it
        ["/html/semantics/document-metadata/the-link-element/stylesheet.py"] = (request, response, stash) =>
        {
            var id = request.Get("id") ?? "";
            var count = ParseInt(stash.Take(id), 0);
            if (request.Has("count"))
            {
                Respond(response, "text/html", Encoding.ASCII.GetBytes(count.ToString(CultureInfo.InvariantCulture)));
                return;
            }

            stash.Put(id, (count + 1).ToString(CultureInfo.InvariantCulture));
            Respond(response, "text/css", "body {color: red;}"u8.ToArray());
        },

        // the-link-element/resources/css.py: a style sheet without the default headers, with the Content-Type of the
        // content_type parameter (none by default), and the X-Content-Type-Options (nosniff) and
        // Access-Control-Allow-Origin (cors) headers
        ["/html/semantics/document-metadata/the-link-element/resources/css.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", request.Get("content_type"));
            if (request.Has("nosniff"))
            {
                response.Add("x-content-type-options", "nosniff");
            }

            if (request.Has("cors"))
            {
                response.Add("Access-Control-Allow-Origin", "*");
            }

            response.Body = "body { background:red }"u8.ToArray();
            response.Streamed = true;
        },

        // links/downloading-resources/resources/inspect-header.py: put stores the header of the request (a ping), get returns
        // it in the x-request-<header> header
        ["/html/semantics/links/downloading-resources/resources/inspect-header.py"] = (request, response, stash) =>
        {
            response.Set("Content-Type", "text/plain");
            var id = request.Get("id") ?? "";
            var header = request.Get("header") ?? "";
            switch (request.Get("cmd")?.ToLowerInvariant())
            {
                case "put":
                    stash.Put(id, request.Header(header) ?? "");
                    break;

                case "get":
                    if (stash.Take(id) is { } value)
                    {
                        response.Add("x-request-" + header, value);
                    }

                    break;

                default:
                    response.Status = 400;
                    response.Content = "ERROR: Bad Command!";
                    break;
            }
        },

        // links/hyperlink-auditing/resources/stash-headers.py: a POST (the ping) stores its Content-Type, Ping-From and Ping-To
        // headers, a GET returns them as JSON
        ["/html/semantics/links/hyperlink-auditing/resources/stash-headers.py"] = (request, response, stash) =>
        {
            var id = request.Get("id") ?? "";
            switch (request.Method)
            {
                case "POST":
                    stash.Put(id, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["content-type"] = request.Header("Content-Type") ?? "no content-type header",
                        ["ping-from"] = request.Header("Ping-From") ?? "no ping-from header",
                        ["ping-to"] = request.Header("Ping-To") ?? "no ping-to header",
                    }));
                    response.Status = 204;
                    break;

                case "GET":
                    Respond(response, "application/json", Encoding.UTF8.GetBytes(stash.Take(id) ?? "\"no headers yet\""));
                    break;

                default:
                    response.Status = 405;
                    break;
            }
        },

        // url/resources/percent-encoding.py: a link whose query and fragment are the value (base64), in the encoding of
        // the document. The code points are numeric character references, so the HTML parser inserts them.
        ["/url/resources/percent-encoding.py"] = (request, response, _) =>
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String((request.Get("value") ?? "").Replace(' ', '+')));
            var references = new StringBuilder();
            foreach (var rune in value.EnumerateRunes())
            {
                references.Append("&#x").Append(rune.Value.ToString("X", CultureInfo.InvariantCulture)).Append(';');
            }

            Respond(response, "text/html;charset=" + request.Get("encoding"), Encoding.ASCII.GetBytes($"<!doctype html>\n<a href=\"https://doesnotmatter.invalid/?{references}#{references}\">test</a>\n"));
        },

        // html/syntax/speculative-parsing/resources/stash.py: put stores the encodingcheck parameter (as it appears in the
        // URL) and the Origin, Accept and Referer headers of the request; take returns them
        ["/html/syntax/speculative-parsing/resources/stash.py"] = (request, response, stash) =>
        {
            var uuid = request.Get("uuid") ?? "";
            response.Set("Content-Type", "text/html");
            if (request.Get("action") != "put")
            {
                response.Content = stash.Take(uuid) ?? "";
                return;
            }

            var index = request.Query.IndexOf("&encodingcheck=", StringComparison.Ordinal);
            var headers = request.Headers.Where(header => header.Key is "Origin" or "Accept" or "Referer").Select(header => header.Key + ": " + header.Value);
            stash.Put(uuid, "param-encodingcheck: " + (index < 0 ? "" : request.Query[(index + "&encodingcheck=".Length)..]) + "\r\n" + string.Join("\r\n", headers));
        },

        // html/syntax/speculative-parsing/expect-no-linked-resources/resources/stash.py: put stores the uuid, get takes it
        ["/html/syntax/speculative-parsing/expect-no-linked-resources/resources/stash.py"] = (request, response, stash) =>
        {
            var uuid = request.Get("uuid") ?? "";
            response.Set("Content-Type", "text/html");
            if (request.Get("action") == "put")
            {
                stash.Put(uuid, uuid);
                response.Content = "ok";
                return;
            }

            response.Content = stash.Take(uuid) ?? "";
        },

        // html/syntax/xmldecl/support/trickle.py: an XML declaration declaring windows-1251, sent byte by byte every 50 ms
        ["/html/syntax/xmldecl/support/trickle.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            foreach (var b in "<?xml version=\"1.0\" encoding=\"windows-1251\"?"u8)
            {
                response.Chunks.Add((response.Chunks.Count == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(50), [b]));
            }

            response.Chunks.Add((TimeSpan.FromMilliseconds(50), [.. ">\n<p>Normal XML declation as slow byte-by-byte trickle</p>\n<p>Test: "u8, 0xE6, .. "</p>\n<p>If &#x0436;, XML decl takes effect</p>"u8]));
        },

        // html/syntax/speculative-charset/support/speculative-script.py: a script whose URL contains a hex NCR, then, after a
        // delay, a meta charset
        ["/html/syntax/speculative-charset/support/speculative-script.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            response.Content = $"<!DOCTYPE html><script src=\"script.py?uuid={request.Get("uuid")}&character=&#x03B6;\"></script>";
            response.Chunks.Add((TimeSpan.FromSeconds(0.2), [.. "<meta charset=\"windows-1251\"><p>Test: "u8, 0xE6, .. "</p>"u8]));
        },

        // html/syntax/speculative-charset/support/script.py: posts the character parameter, the one of the previous request with
        // the same token, and a byte
        ["/html/syntax/speculative-charset/support/script.py"] = (request, response, stash) =>
        {
            var token = request.Get("uuid") ?? "";
            var character = request.Get("character") ?? "";
            var oldCharacter = stash.Take(token);
            if (oldCharacter is null)
            {
                stash.Put(token, character);
            }

            response.Set("Content-Type", "text/javascript");
            response.Content = $"parent.postMessage(\"token: {token}, character: {character}, previous character: {oldCharacter ?? "NOT LOADED PREVIOUSLY"}, byte: æ\", \"*\");";
        },

        // html/syntax/charset/resources/bogus-charset-http.py and bogus-charset-http-valid-meta.py: a byte that differs in the
        // encodings of the tests, with a Content-Type whose charset is not an encoding (and a meta charset)
        ["/html/syntax/charset/resources/bogus-charset-http.py"] = (_, response, _) =>
            Respond(response, "text/html;charset=this-is-not-a-charset", [0xA2, (byte)'\n']),
        ["/html/syntax/charset/resources/bogus-charset-http-valid-meta.py"] = (_, response, _) =>
            Respond(response, "text/html;charset=this-is-not-a-charset", [.. "<meta charset=windows-1251>"u8, 0xA2, (byte)'\n']),

        // html/browsers/origin/origin-keyed-agent-clusters/resources/send-oac-header.py: a page with the Origin-Agent-Cluster
        // header of the header parameter, after a redirect without the header when redirect-first is present
        ["/html/browsers/origin/origin-keyed-agent-clusters/resources/send-oac-header.py"] = (request, response, _) =>
        {
            if (request.Has("redirect-first"))
            {
                var redirectQuery = new List<string>();
                if (request.Get("header") is { } redirectHeader)
                {
                    redirectQuery.Add("header=" + redirectHeader);
                }

                if (request.Has("send-loaded-message"))
                {
                    redirectQuery.Add("send-loaded-message");
                }

                response.Status = 302;
                response.Set("Location", request.Path + "?" + string.Join('&', redirectQuery));
                return;
            }

            Respond(response, "text/html", """

                <!DOCTYPE html>
                <meta charset="utf-8">
                <title>Helper page for origin-keyed agent cluster tests</title>

                <body>
                <script type="module" src="send-header-page-script.mjs"></script>

                """u8.ToArray());
            response.Set("Origin-Agent-Cluster", request.Get("header"));
        },

        // html/browsers/windows/resources/window-name-stash.py: POST stores the value, GET takes it
        ["/html/browsers/windows/resources/window-name-stash.py"] = (request, response, stash) =>
        {
            var key = "window-name|" + request.Get("id");
            if (request.Method == "POST")
            {
                stash.TakeValue(key);
                stash.Put(key, request.Get("value") ?? "");
                response.Content = "OK";
            }
            else
            {
                response.Content = stash.Take(key) ?? "NONE";
            }
        },

        // html/browsers/browsing-the-web/remote-context-helper/resources/executor-window.py: the document of a window
        // executor of RemoteContextHelper
        ["/html/browsers/browsing-the-web/remote-context-helper/resources/executor-window.py"] = ExecutorWindow,

        // html/infrastructure/urls/terminology-0/resources/echo-referrer-text.py: the Referer header of the request
        ["/html/infrastructure/urls/terminology-0/resources/echo-referrer-text.py"] = (request, response, _) => TextResponse(response, request.Header("Referer") ?? ""),

        // html/infrastructure/urls/resolving-urls/query-encoding/resources/blank.py: an empty document in the encoding
        ["/html/infrastructure/urls/resolving-urls/query-encoding/resources/blank.py"] = (request, response, _) => Respond(response, "text/html; charset=" + request.Get("encoding"), []),

        // html/infrastructure/urls/resolving-urls/query-encoding/resources/css-tmpl.py: the template (tmpl) whose %s is the
        // CSS escape of U+00E5, in the encoding (the result is ASCII)
        ["/html/infrastructure/urls/resolving-urls/query-encoding/resources/css-tmpl.py"] = (request, response, _) =>
            Respond(response, "text/css; charset=" + request.Get("encoding"), EncodeAscii((request.Get("tmpl") ?? "").Replace("%s", "\\0000E5", StringComparison.Ordinal), request.Get("encoding"))),

        // html/infrastructure/urls/resolving-urls/query-encoding/resources/stash.py: put stores the q parameter (as it
        // appears in the URL) by id, take returns it
        ["/html/infrastructure/urls/resolving-urls/query-encoding/resources/stash.py"] = (request, response, stash) =>
        {
            var id = request.Get("id") ?? "";
            if (request.Get("action") == "put")
            {
                var q = GetRawQueryParameter(request, "q") ?? "";
                stash.Put(id, q);
                Respond(response, "text/html", Encoding.Latin1.GetBytes("Put " + q));
                return;
            }

            Respond(response, "text/html", Encoding.Latin1.GetBytes(stash.Take(id) ?? ""));
        },

        // html/infrastructure/urls/resolving-urls/query-encoding/resources/resource.py: a resource of the type that contains
        // the q parameter (as it appears in the URL).
        ["/html/infrastructure/urls/resolving-urls/query-encoding/resources/resource.py"] = QueryEncodingResource,

        // html/semantics/embedded-content/the-iframe-element/stash.py: POST stores the body, GET takes it
        ["/html/semantics/embedded-content/the-iframe-element/stash.py"] = (request, response, stash) =>
        {
            var key = "iframe-stash|" + request.Get("id");
            if (request.Method == "POST")
            {
                stash.Put(key, Encoding.UTF8.GetString(request.Body));
            }
            else
            {
                response.Content = stash.Take(key) ?? "";
            }
        },

        // the-iframe-element/support/download_stash.py: "verify-token" takes the token; a request without "finish-delay"
        // (<a download>) puts it; with "finish-delay", a Content-Disposition: attachment response whose body is streamed over
        // the delay, and the token is put once the whole body was sent (a download canceled by the client does not set it)
        ["/html/semantics/embedded-content/the-iframe-element/support/download_stash.py"] = (request, response, stash) =>
        {
            var token = "download|" + request.Get("token");
            response.Set("Content-Type", "text/html");
            response.Add("X-Content-Type-Options", "nosniff");
            if (request.Has("verify-token"))
            {
                response.Content = stash.TakeValue(token) is true ? "TOKEN_SET" : "TOKEN_NOT_SET";
                return;
            }

            if (request.Get("finish-delay") is { } finishDelay)
            {
                response.Add("Content-Disposition", "attachment");
                var delay = Milliseconds(finishDelay, 0) / 10;
                for (var i = 0; i < 10; i++)
                {
                    response.Chunks.Add((delay, "\n"u8.ToArray()));
                }

                response.Completed = () => stash.Put(token, true);
                return;
            }

            stash.Put(token, true);
        },

        // html/semantics/embedded-content/the-img-element: a 404 response whose body is a PNG image
        ["/html/semantics/embedded-content/the-img-element/404-response-with-actual-image-data.py"] = (_, response, _) =>
        {
            response.Status = 404;
            Respond(response, "image/png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsQAAA7EAZUrDhsAAAAhSURBVDhPY3wro/KfgQLABKXJBqMGjBoAAqMGDLwBDAwAEsoCTFWunmQAAAAASUVORK5CYII="));
        },

        // the-img-element/resources/image-alternate-no-store.py: blue-10.png and green.png alternately (Cache-Control: no-store)
        ["/html/semantics/embedded-content/the-img-element/resources/image-alternate-no-store.py"] = (_, response, stash) =>
        {
            const string Key = "882e0112-2a43-4ff1-9503-658b1a2a8e67";
            var alternate = stash.TakeValue(Key) is true;
            stash.Put(Key, !alternate);
            Respond(response, "image/png", ReadWptFile("html/semantics/embedded-content/the-img-element/resources/" + (alternate ? "blue-10.png" : "green.png")));
            response.Add("Cache-Control", "no-store");
        },

        // the-img-element/resources/image-and-stash.py: "increment" counts the requests of a uuid and returns an image,
        // "read" returns the count without removing it
        ["/html/semantics/embedded-content/the-img-element/resources/image-and-stash.py"] = (request, response, stash) =>
        {
            if (request.Get("increment") is { } incremented)
            {
                stash.Put(incremented, (stash.TakeValue(incremented) as int? ?? 0) + 1);
                Respond(response, "image/png", ReadWptFile("html/semantics/embedded-content/the-img-element/resources/image.png"));
            }
            else if (request.Get("read") is { } read)
            {
                var count = stash.TakeValue(read) as int? ?? 0;
                stash.Put(read, count);
                response.Content = count.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                response.Status = 404;
                response.Content = "Not found";
            }
        },

        // the-img-element/resources/referrer-checker-img.py: an image when the Referer header is the expected_referrer
        // parameter, else a 404 response
        ["/html/semantics/embedded-content/the-img-element/resources/referrer-checker-img.py"] = (request, response, _) =>
        {
            if ((request.Header("Referer") ?? "") == (request.Get("expected_referrer") ?? ""))
            {
                Respond(response, "image/png", ReadWptFile("html/semantics/embedded-content/the-img-element/resources/image.png"));
            }
            else
            {
                response.Status = 404;
                Respond(response, "image/png", "Not found"u8.ToArray());
            }
        },

        // navigation-api/navigate-event/resources/cross-origin-redirect-on-second-visit.py: a document that is not cached;
        // the second request of the key is redirected to /common/blank.html of the remote_origin parameter
        ["/navigation-api/navigate-event/resources/cross-origin-redirect-on-second-visit.py"] = (request, response, stash) =>
        {
            var key = request.Get("key") ?? "";
            var visited = stash.TakeValue(key, request.Path);
            stash.Put(key, true, request.Path);
            response.Set("Cache-Control", "no-cache, no-store, must-revalidate");
            response.Set("Pragma", "no-cache");
            response.Set("Content-Type", "text/html");
            if (visited is null)
            {
                response.Content = "body";
                return;
            }

            response.Status = 301;
            response.Set("Location", request.Get("remote_origin") + "/common/blank.html");
            response.Content = "redirect_body";
        },

        // navigation-api/navigation-methods/return-value/resources/204-205-download-on-second-visit.py: a POST request
        // stores the action parameter for the id; a GET request responds with the stored action (a 204 or 205 response, or
        // a download), or with a document that is not cached when there is none
        ["/navigation-api/navigation-methods/return-value/resources/204-205-download-on-second-visit.py"] = (request, response, stash) =>
        {
            var key = request.Get("id") ?? "";
            if (request.Method == "POST")
            {
                stash.Put(key, request.Get("action") ?? "", request.Path);
                response.Status = 204;
                return;
            }

            if (request.Method != "GET")
            {
                response.Status = 400;
                return;
            }

            switch (stash.TakeValue(key, request.Path) as string)
            {
                case null:
                    response.Set("Content-Type", "text/html");
                    response.Set("Cache-Control", "no-store");
                    response.Content = "initial page";
                    break;

                case "204":
                    response.Status = 204;
                    break;

                case "205":
                    response.Status = 205;
                    break;

                case "download":
                    response.Set("Content-Type", "text/plain");
                    response.Set("Content-Disposition", "attachment");
                    response.Content = "some text to download";
                    break;

                default:
                    response.Status = 400;
                    break;
            }
        },

        // navigation-api/navigation-methods/resources/slow-no-store.py: an empty document that is not cached, delayed by
        // 1 second. wptserve sends no Content-Type: the browsers sniff an HTML document.
        ["/navigation-api/navigation-methods/resources/slow-no-store.py"] = (_, response, _) =>
        {
            response.Delay = TimeSpan.FromSeconds(1);
            response.Set("Cache-Control", "no-cache, no-store, must-revalidate");
            response.Set("Content-Type", "text/html");
        },

        // html/dom/partial-updates: an HTML response in two chunks (chunk1, then chunk2 after delay milliseconds, or, with a
        // key, when the continue action puts "continue" in the stash; check_chunk1 tells whether the first chunk was sent)
        ["/html/dom/partial-updates/tentative/resources/chunked-html.py"] = ChunkedHtml,

        // html/dom/partial-updates: a document whose buffered template content is completed after 500 ms
        ["/html/dom/partial-updates/tentative/fragment/buffer-streaming-reflection.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            response.Streamed = true;
            response.Body = Encoding.UTF8.GetBytes("""
                <!DOCTYPE html>
                <meta charset="utf-8">
                <body>
                <div id="target" marker="dest-marker">
                  <?start name="dest-marker">Original Content<?end>
                </div>
                <script>
                  window.observedLengths = [];
                  const intervalId = setInterval(() => {
                    const tpl = document.getElementById('test-template');
                    if (tpl) {
                      const elementNodes = Array.from(tpl.content.childNodes).filter(n => n.nodeType === Node.ELEMENT_NODE);
                      window.observedLengths.push(elementNodes.length);
                    }
                  }, 10);
                </script>
                <template for="dest-marker" buffer id="test-template">
                  <span id="child1">One</span>

                """);
            response.Chunks.Add((TimeSpan.FromMilliseconds(500), Encoding.UTF8.GetBytes("""
                  <span id="child2">Two</span>
                </template>
                <script>
                  clearInterval(intervalId);
                  // Wait a microtask to ensure finalization completed
                  setTimeout(() => {
                    const target = document.getElementById('target');
                    const child1_ok = target.querySelector('#child1') && target.querySelector('#child1').textContent === 'One';
                    const child2_ok = target.querySelector('#child2') && target.querySelector('#child2').textContent === 'Two';
                    const tpl_removed = document.getElementById('test-template') === null;
                    const progress_ok = window.observedLengths.includes(1);
                    let passed = child1_ok && child2_ok && tpl_removed && progress_ok;
                    let message = "";
                    if (!passed) {
                      message = `child1: ${child1_ok}, child2: ${child2_ok}, tpl_removed: ${tpl_removed}, progress_ok (lengths includes 1): ${progress_ok}. Observed: ${JSON.stringify(window.observedLengths)}`;
                    }
                    window.parent.postMessage({
                      type: 'test-result',
                      passed: passed,
                      message: message
                    }, "*");
                  }, 0);
                </script>
                </body>

                """)));
        },
    };

    /// <summary>html/dom/partial-updates/tentative/resources/chunked-html.py</summary>
    // The value of the COOKIE_NAME cookie of the request of workers/modules/resources/*-credentials.py, or an empty string
    private static string GetCookieName(WptHandlerRequest request) => ParseRequestCookies(request).Find(cookie => cookie.Key == "COOKIE_NAME").Value ?? "";

    // The script of workers/modules/resources/postmessage-*.py: it posts the value from a dedicated worker, or to the port of
    // each connection of a shared worker
    private static string PostMessageWorkerScript(string value) =>
        "if ('DedicatedWorkerGlobalScope' in self &&    self instanceof DedicatedWorkerGlobalScope) {  postMessage('" + value + "');} else if (    'SharedWorkerGlobalScope' in self &&    self instanceof SharedWorkerGlobalScope) {  onconnect = e => e.ports[0].postMessage('" + value + "');}";

    // workers/baseurl/beta/*.py: "302 Moved" to ../gamma/<script>
    private static void BaseUrlRedirect(WptHandlerResponse response, string script)
    {
        response.Status = 302;
        response.StatusText = "Moved";
        response.Set("Location", "../gamma/" + script);
        response.Content = "postMessage('executed redirecting script');";
    }

    private static void ChunkedHtml(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var action = request.Get("action");
        var key = request.Get("key");
        var stashKey = "chunked-html|" + key;
        if (action == "continue")
        {
            stash.Take(stashKey);
            stash.Put(stashKey, "continue");
            TextResponse(response, "ok");
            return;
        }

        if (action == "check_chunk1")
        {
            var value = stash.TakeValue(stashKey) as string;
            if (value is not null)
            {
                stash.Put(stashKey, value);
            }

            TextResponse(response, value == "chunk1_sent" ? "ready" : "waiting");
            return;
        }

        response.Set("Content-Type", "text/html");
        if (request.Get("cors") == "1")
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }

        response.Streamed = true;
        response.Body = Encoding.UTF8.GetBytes(request.Get("chunk1") ?? "");
        var chunk2 = Encoding.UTF8.GetBytes(request.Get("chunk2") ?? "");
        if (key is not null)
        {
            // The second chunk is sent when the test asks for it (the handler gives up after 10 seconds)
            stash.Put(stashKey, "chunk1_sent");
            response.Chunks.Add((TimeSpan.Zero, chunk2));
            response.ChunksCondition = () => stash.Contains(stashKey) && stash.TakeValue(stashKey) is var value && (value as string == "continue" || Restore(value));
            response.ChunksTimeout = TimeSpan.FromSeconds(10);
        }
        else
        {
            response.Chunks.Add((Milliseconds(request.Get("delay"), 300), chunk2));
        }

        bool Restore(object? value)
        {
            if (value is not null)
            {
                stash.Put(stashKey, value);
            }

            return false;
        }
    }

    private static void LoadErrorEvents(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var path = Uri.UnescapeDataString(request.Path);
        var test = request.Get("test") ?? "";
        var isLoad = test.Contains("_load", StringComparison.Ordinal);
        response.Set("Content-Type", "text/javascript");
        if (path.StartsWith("/html/semantics/scripting-1/the-script-element/resources/", StringComparison.Ordinal))
        {
            response.Content = isLoad ? $"\"use strict\"; {test}.executed = true;" : $"\"use strict\"; {test}.test.step(function() {{ assert_unreached(\"404 script should not be executed\"); }});";
            if (!isLoad)
            {
                response.Status = 404;
            }

            return;
        }

        var (dependency, type, missing) = path.Contains("/json-module/", StringComparison.Ordinal) ? ("module.json", "json", "not_found.json")
            : path.Contains("/text-module/", StringComparison.Ordinal) ? ("file.txt", "text", "not_found.txt")
            : ("basic.css", "css", "not_found.css");
        response.Content = isLoad
            ? $"import \"./{dependency}\" with {{ type: \"{type}\"}}; {test}.executed = true;"
            : $"import \"./{missing}\" with {{ type: \"{type}\"}}; {test}.test.step(function() {{ assert_unreached(\"404 script should not be executed\"); }});";
    }

    private static void ReferrerChecker(WptHandlerRequest request, WptHandlerResponse response, string contentType, string template)
    {
        response.Add("Content-Type", contentType);
        response.Add("Access-Control-Allow-Origin", "*");
        response.Content = template.Replace("{0}", request.Header("Referer") ?? "", StringComparison.Ordinal);
    }

    /// <summary>The state of a key of <c>serve-custom-response.py</c>.</summary>
    private sealed class CustomResponseContext
    {
        public bool NetworkError { get; set; }

        public int RunCount { get; set; }

        public int StatusCode { get; set; } = 200;

        public string ContentType { get; set; } = "application/javascript";
    }

    // the-script-element/serve-custom-response.py: a response (a module of the type of the Content-Type, a status, or a
    // network error) configured by a previous request of the key (action=set), and the number of requests of the key
    // (action=stat)
    private static void ServeCustomResponse(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var key = request.Get("key") ?? "";
        var context = stash.TakeValue(key, request.Path) as CustomResponseContext ?? new CustomResponseContext();
        switch (request.Get("action"))
        {
            case "stat":
                stash.Put(key, context, request.Path);
                TextResponse(response, context.RunCount.ToString(CultureInfo.InvariantCulture));
                return;

            case "reset-stat":
                context.RunCount = 0;
                stash.Put(key, context, request.Path);
                TextResponse(response, "OK");
                return;

            case "set":
                if (request.Get("code") is { } code)
                {
                    context.StatusCode = int.Parse(code, CultureInfo.InvariantCulture);
                }

                if (request.Get("content-type") is { } contentType)
                {
                    context.ContentType = contentType;
                }

                if (request.Get("network-error") is { } networkError)
                {
                    context.NetworkError = networkError.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                stash.Put(key, context, request.Path);
                TextResponse(response, "OK");
                return;
        }

        context.RunCount++;
        stash.Put(key, context, request.Path);
        if (context.NetworkError)
        {
            // The connection is closed without a response
            response.NetworkError = true;
            return;
        }

        response.Set("Content-Type", context.ContentType);
        response.Status = context.StatusCode;
        response.Content = context.ContentType switch
        {
            "application/javascript" => "export default 'hello';",
            "application/json" => "{\"hello\": \"world\"}",
            "text/css" => "#test { background-color: rgb(0, 255, 0); }",
            _ => "42",
        };
    }

    // the-script-element/moving-between-documents/resources/moving-between-documents-iframe.py: a chunked document whose
    // first chunk posts a message to the parent and has a style sheet blocking the scripts (hasBlockingStylesheet), and
    // whose second chunk, sent 1 second later, has the script element (inline, external or with an empty src) in a
    // streaming-element
    private static void MovingBetweenDocumentsIframe(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var inlineOrExternal = request.Get("inlineOrExternal") ?? "null";
        var hasBlockingStylesheet = (request.Get("hasBlockingStylesheet") ?? "true") == "true";
        var result = request.Get("result") ?? "success";
        var type = (request.Get("type") ?? "classic") == "classic" ? "text/javascript" : "module";
        response.Set("Content-Type", "text/html; charset=utf-8");

        var body = new StringBuilder("""
            <!DOCTYPE html>
                <head>
                  <script>
                    parent.postMessage("fox", "*");
                  </script>

            """);
        if (hasBlockingStylesheet)
        {
            body.Append("    <link rel=\"stylesheet\" href=\"slow-flag-setter.py?result=css&cache=").Append(Guid.NewGuid().ToString("N")).Append("\">\n");
        }

        body.Append("  </head>\n  <body>\n");
        var streaming = inlineOrExternal is "inline" or "external" or "empty-src";
        if (streaming)
        {
            body.Append("    <streaming-element>\n");
        }

        // Trigger DOM processing
        body.Append('A', 100000);
        var firstChunk = body.ToString();
        body.Clear();
        switch (inlineOrExternal)
        {
            case "inline":
                body.Append("<script id=\"s1\" type=\"").Append(type).Append("\" onload=\"scriptOnLoad()\" onerror=\"scriptOnError(event)\">\n");
                body.Append("if (!window.readyToEvaluate) {\n  window.didExecute = \"executed too early\";\n} else {\n  window.didExecute = \"executed\";\n}\n");
                if (result == "parse-error")
                {
                    body.Append("1=2 parse error\n");
                }

                body.Append("</script>\n</streaming-element>\n");
                break;

            case "external":
                body.Append("<script id=\"s1\" type=\"").Append(type).Append("\" src=\"slow-flag-setter.py?result=").Append(result).Append("&cache=").Append(Guid.NewGuid().ToString("N")).Append("\" onload=\"scriptOnLoad()\" onerror=\"scriptOnError(event)\"></script>\n</streaming-element>\n");
                break;

            case "empty-src":
                body.Append("<script id=\"s1\" type=\"").Append(type).Append("\" src=\"\" onload=\"scriptOnLoad()\" onerror=\"scriptOnError(event)\"></script>\n</streaming-element>\n");
                break;
        }

        // Trigger DOM processing
        body.Append('B', 100000);
        response.Body = Encoding.UTF8.GetBytes(firstChunk);
        response.Chunks.Add((streaming ? TimeSpan.FromSeconds(1) : TimeSpan.Zero, Encoding.UTF8.GetBytes(body.ToString())));
    }

    private static void ServeJsOrText(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, bool jsFirst)
    {
        var key = request.Get("key") ?? throw new InvalidOperationException("The key parameter is missing");
        var runCount = stash.TakeValue(key, request.Path) as int? ?? 0;
        if ((runCount == 0) == jsFirst)
        {
            response.Set("Content-Type", "application/javascript");
            response.Content = "export default 'world';";
        }
        else
        {
            response.Set("Content-Type", "text/plain");
            response.Content = "hello";
        }

        stash.Put(key, runCount + 1, request.Path);
    }

    // the-script-element/module-fetch-tester.py: a module whose first request (mode=fail-first, the default) or every request
    // (mode=always-fail) is a 404 response, and the number of requests of the key (action=stat)
    private static void ModuleFetchTester(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var key = request.Get("key") ?? throw new InvalidOperationException("The key parameter is missing");
        var runCount = stash.TakeValue(key, request.Path) as int? ?? 0;
        if (request.Get("action") == "stat")
        {
            stash.Put(key, runCount, request.Path);
            response.Set("Content-Type", "text/plain");
            response.Content = runCount.ToString(CultureInfo.InvariantCulture);
            return;
        }

        runCount++;
        stash.Put(key, runCount, request.Path);
        var mode = request.Get("mode") ?? "fail-first";
        if (mode == "always-fail" || (mode == "fail-first" && runCount == 1))
        {
            response.Status = 404;
            response.Set("Content-Type", "text/plain");
            response.Content = "not found";
            return;
        }

        response.Set("Content-Type", "application/javascript");
        response.Content = "export default 'hello';";
    }

    // html/browsers/browsing-the-web/remote-context-helper/resources/executor-window.py: a document that runs the executor
    // of the uuid parameter (on the startOn event), with the scripts of the script parameters, and whose
    // window.__requestHeaders are the headers of the request. The status is the status parameter.
    private static void ExecutorWindow(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var initRequestHeaders = new StringBuilder();
        foreach (var name in request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            foreach (var header in request.Headers.Where(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                initRequestHeaders.Append("window.__requestHeaders.append(").Append(PythonJsonString(DecodeUtf8(name))).Append(", ").Append(PythonJsonString(DecodeUtf8(header.Value))).Append(");\n");
            }
        }

        // parse.parse_qs(request.url_parts.query): the values are decoded as UTF-8, the blank values are ignored
        var query = request.GetParameters.Where(parameter => parameter.Value.Length > 0).Select(parameter => (parameter.Key, Value: DecodeUtf8(parameter.Value))).ToList();
        var scripts = string.Join('\n', query.Where(parameter => parameter.Key == "script").Select(parameter => "<script src='" + PythonHtmlEscape(parameter.Value) + "'></script>"));
        var uuid = query.Find(parameter => parameter.Key == "uuid").Value ?? throw new InvalidOperationException("The uuid parameter is missing");
        var startOn = query.Find(parameter => parameter.Key == "startOn").Value is { } value ? "'" + value + "'" : "null";

        response.Status = ParseInt(request.Get("status"), 200);
        response.Set("Content-Type", "text/html");
        response.Body = Encoding.UTF8.GetBytes($"""

            <!DOCTYPE HTML>
            <base href="{PythonHtmlEscape(request.Url.Href)}">
            <script src="/common/dispatcher/dispatcher.js"></script>
            <script src="./executor-common.js"></script>
            <script src="./executor-window.js"></script>

            {scripts}
            <body>
            <script>
            window.__requestHeaders = new Headers();
            {initRequestHeaders}
            requestExecutor("{uuid}", {startOn});
            </script>

            """);
    }

    // html/infrastructure/urls/resolving-urls/query-encoding/resources/resource.py
    private static void QueryEncodingResource(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var encoding = request.Get("encoding") ?? "";
        var q = GetRawQueryParameter(request, "q") ?? "";
        switch (request.Get("type"))
        {
            case "html":
                Respond(response, "text/html; charset=utf-8", Encoding.Latin1.GetBytes(q));
                break;

            case "css":
                Respond(response, "text/css; charset=utf-8", Encoding.Latin1.GetBytes("#test::before { content:'" + q + "' }"));
                break;

            case "js":
                Respond(response, "text/javascript; charset=utf-8", Encoding.Latin1.GetBytes(request.Get("var") + " = '" + q + "';"));
                break;

            case "worker":
                Respond(response, "text/javascript", Encoding.Latin1.GetBytes("postMessage('" + q + "'); close();"));
                break;

            case "sharedworker":
                Respond(response, "text/javascript", Encoding.Latin1.GetBytes("onconnect = function(e) { e.source.postMessage('" + q + "'); close(); };"));
                break;

            case "worker_importScripts":
                Respond(response, "text/javascript; charset=" + encoding, Encoding.Latin1.GetBytes("try { var x = 'importScripts failed to run'; importScripts('?q=\\u00E5&type=js&var=x&encoding=" + encoding + "'); postMessage(x); close(); } catch(ex) { postMessage(String(ex)); }"));
                break;

            case "worker_worker":
                Respond(response, "text/javascript; charset=" + encoding, Encoding.Latin1.GetBytes("try { var worker = new Worker('?q=\\u00E5&type=worker&encoding=" + encoding + "'); worker.onmessage = function(e) { postMessage(e.data); close(); }; } catch(ex) { postMessage(String(ex)); }"));
                break;

            case "sharedworker_importScripts":
                Respond(response, "text/javascript; charset=" + encoding, Encoding.Latin1.GetBytes("var x = 'importScripts failed to run'; onconnect = function(e) { var connect_port = e.source; try { importScripts('?q=\\u00E5&type=js&var=x&encoding=" + encoding + "'); connect_port.postMessage(x); close(); } catch(ex) { connect_port.postMessage(String(ex)); } };"));
                break;

            case "sharedworker_worker":
                Respond(response, "text/javascript; charset=" + encoding, Encoding.Latin1.GetBytes("onconnect = function(e) { var connect_port = e.source; try { var worker = new Worker('?q=\\u00E5&type=worker&encoding=" + encoding + "'); worker.onmessage = function(e) { connect_port.postMessage(e.data); close(); }; } catch(ex) { connect_port.postMessage(String(ex)); } };"));
                break;

            case "eventstream":
                Respond(response, "text/event-stream", Encoding.Latin1.GetBytes("data: " + q + "\n\n"));
                break;

            case "svg":
                Respond(response, "image/svg+xml", Encoding.Latin1.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + q + "</svg>"));
                break;

            case "xmlstylesheet_css":
                Respond(response, "application/xhtml+xml; charset=" + encoding, EncodeAscii("<?xml-stylesheet href=\"?q=&#x00E5;&amp;type=css&amp;encoding=" + encoding + "\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"/>", encoding));
                break;

            case "png":
            {
                var image = q switch
                {
                    "%E5" or "%26%23229%3B" => "green-1x1.png",
                    "%C3%A5" => "green-2x2.png",
                    "%3F" => "green-16x16.png",
                    _ => "green-256x256.png",
                };

                Respond(response, "image/png", File.ReadAllBytes(Path.Combine(WptPaths.Wpt, "images", image)));
                break;
            }

            case "video":
            {
                // The duration of the video tells which query was received
                var video = q switch
                {
                    "%E5" or "%26%23229%3B" => "A4",
                    "%C3%A5" => "movie_5",
                    "%3F" => "green-at-15",
                    _ => "movie_300",
                };

                var extension = request.Get("ext") ?? "";
                Respond(response, "video/" + (extension == "ogv" ? "ogg" : extension), File.ReadAllBytes(Path.Combine(WptPaths.Wpt, "media", video + "." + extension)));
                break;
            }

            case "webvtt":
                Respond(response, "text/vtt", Encoding.Latin1.GetBytes("WEBVTT\n\n00:00:00.000 --> 00:00:01.000\n" + q));
                break;

            default:
                // The handler is not emulated for the other types
                response.Status = 404;
                break;
        }
    }

    /// <summary>Encodes an ASCII text in an encoding, like <c>str.encode(encoding)</c> in Python: the ASCII-compatible encodings are the same, except UTF-16.</summary>
    private static byte[] EncodeAscii(string text, string? encoding) => encoding?.ToLowerInvariant() switch
    {
        "utf-16le" or "utf-16" => Encoding.Unicode.GetBytes(text),
        "utf-16be" => Encoding.BigEndianUnicode.GetBytes(text),
        _ => Encoding.ASCII.GetBytes(text),
    };

    /// <summary>The value of the first <c>name=value</c> of the query of a URL, as it appears in the URL (not percent-decoded), like <c>re.search(r'q=([^&amp;]+)', request.url_parts.query)</c>.</summary>
    private static string? GetRawQueryParameter(WptHandlerRequest request, string name)
    {
        var index = request.Query.IndexOf(name + "=", StringComparison.Ordinal);
        if (index < 0)
            return null;

        var value = request.Query[(index + name.Length + 1)..];
        var end = value.IndexOf('&', StringComparison.Ordinal);
        return end < 0 ? value : value[..end];
    }

    /// <summary>The first value of a field of a POST body (application/x-www-form-urlencoded or multipart/form-data), decoded as Latin-1 like the isomorphic decoding of wptserve.</summary>
    private static string? GetPostField(byte[] body, string? contentType, string name)
    {
        if (contentType is null)
            return null;

        if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            return new UrlSearchParams(Encoding.Latin1.GetString(body)).Get(name);

        var boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
        if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) || boundaryIndex < 0)
            return null;

        var boundary = "--" + contentType[(boundaryIndex + "boundary=".Length)..].Trim('"');
        foreach (var part in Encoding.Latin1.GetString(body).Split(boundary))
        {
            var headerEnd = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
                continue;

            var headers = part[..headerEnd];
            if (!headers.Contains("name=\"" + name + "\"", StringComparison.Ordinal))
                continue;

            var value = part[(headerEnd + 4)..];
            return value.EndsWith("\r\n", StringComparison.Ordinal) ? value[..^2] : value;
        }

        return null;
    }
}
