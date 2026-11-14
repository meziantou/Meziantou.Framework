using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>storage-access-api</c> tests (<c>storage-access-api/resources/*.py</c>).</summary>
internal static partial class WptPythonHandlers
{
    /// <summary>The path of the stash of the storage access headers (<c>header_helpers.RETRIEVAL_PATH</c>).</summary>
    private const string StorageAccessHeadersStashPath = "/storage-access-api/resources/retrieve-storage-access-headers.py";

    private static readonly Dictionary<string, Handler> StorageAccessHandlers = new(StringComparer.Ordinal)
    {
        // echo-cookie-header.py: the Cookie header of the request, with the CORS headers of a credentialed request
        ["/storage-access-api/resources/echo-cookie-header.py"] = (request, response, _) =>
        {
            if (request.Header("Origin") is { Length: > 0 } origin)
            {
                response.Set("Content-Type", "text/plain");
                response.Set("Access-Control-Allow-Origin", origin);
                response.Set("Access-Control-Allow-Credentials", "true");
            }

            response.Content = request.Header("Cookie") ?? "";
        },

        // embedded_worker.py: a dedicated worker that replies with the Cookie header of its script (load) or the response of a fetch
        ["/storage-access-api/resources/embedded_worker.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Set("Content-Type", "text/javascript");
            response.Content = """

                "use strict";

                self.onmessage = async (message) => {
                  function reply(data) {
                    self.postMessage({data});
                  }

                  switch (message.data.command) {
                    case "fetch": {
                      const response = await fetch(message.data.url, {mode: 'cors', credentials: 'include'})
                        .then((resp) => resp.text());
                      reply(response);
                      break;
                    }
                    case "load": {
                      reply("
                """ + (request.Header("Cookie") ?? "") + """
                ");
                      break;
                    }
                    default:
                  }
                };

                """;
        },

        // get_cookies.py: the cookies of the request as a JSON object
        ["/storage-access-api/resources/get_cookies.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Content = PythonJsonDump(ReadCookies(request));
        },

        // set_cookies.py: sets a Strict, a Lax and a None SameSite cookie
        ["/storage-access-api/resources/set_cookies.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Set("Content-Type", "text/html; charset=utf-8");
            response.Add("Set-Cookie", MakeCookieHeader("samesite_strict", "test", ("SameSite", "Strict"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_lax", "test", ("SameSite", "Lax"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_none", "test", ("SameSite", "None"), ("path", "/"), ("Secure", "")));
            response.Content = "<!DOCTYPE html>";
        },

        // set-cookie-header.py: the Set-Cookie header of the decoded query
        ["/storage-access-api/resources/set-cookie-header.py"] = (request, response, _) =>
        {
            response.Add("Set-Cookie", IsomorphicEncode(PythonUnquote(request.Query)));
        },

        // script-with-cookie-header.py: a document that declares the Cookie header of its request (httpCookies) and loads a script
        ["/storage-access-api/resources/script-with-cookie-header.py"] = (request, response, _) =>
        {
            var script = RequiredParameter(request, "script");
            var shouldAckLoad = request.Get("should_ack_load") == "true" ? "true" : "false";
            response.Content = $$"""

                  <!DOCTYPE html>
                  <meta charset="utf-8">
                  <title>Subframe with HTTP Cookies</title>
                  <script src="/resources/testharness.js"></script>
                  <script src="/resources/testdriver.js"></script>
                  <script src="/resources/testdriver-vendor.js"></script>
                  <script>
                    var httpCookies = "{{request.Header("Cookie") ?? ""}}";
                    var should_ack_load = {{shouldAckLoad}};
                  </script>

                  <body>
                  <script src="{{script}}"></script>
                  </body>


                """;
        },

        // shared-worker-cookies.py: a shared worker that reports the SameSite cookies of its script and of a fetch
        ["/storage-access-api/resources/shared-worker-cookies.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Set("Content-Type", "text/javascript");
            var cookies = ReadCookies(request);
            var message = new StringBuilder("ReadOnLoad:");
            foreach (var (name, label) in new[] { ("samesite_strict", "Strict"), ("samesite_lax", "Lax"), ("samesite_none", "None") })
            {
                if (cookies.Exists(cookie => cookie.Key == name))
                {
                    message.Append(label);
                }
            }

            response.Content = """

                let connection_count = 0;
                self.onconnect = (e) => {
                    connection_count++;
                    fetch("/storage-access-api/resources/get_cookies.py", {credentials: 'include'}).then((resp) => {
                        resp.json().then((cookies) => {
                            let message = "
                """ + message + """
                ,ReadOnFetch:";
                            if (cookies.hasOwnProperty("samesite_strict")) {
                                message += "Strict";
                            }
                            if (cookies.hasOwnProperty("samesite_lax")) {
                                message += "Lax";
                            }
                            if (cookies.hasOwnProperty("samesite_none")) {
                                message += "None";
                            }
                            message += ",ConnectionsMade:" + connection_count;
                            e.ports[0].postMessage(message);
                        });
                    });
                }

                """;
        },

        // handle-headers-non-retry.py: stashes the headers of the request (Activate-Storage-Access: load with the load parameter)
        ["/storage-access-api/resources/handle-headers-non-retry.py"] = (request, response, stash) =>
        {
            if (request.Get("key") is not { } key)
            {
                response.Status = 400;
                return;
            }

            if (request.Has("load"))
            {
                response.Set("Activate-Storage-Access", "load");
            }

            stash.Put(MakeStorageAccessStashKey(key, request), GetStashableHeaders(request), StorageAccessHeadersStashPath);
            response.Content = MakeStorageAccessResponseBody(request);
        },

        // handle-headers-retry.py: stashes the headers of the request (under another key once the request is active), and asks
        // to retry the request (Activate-Storage-Access: retry) or redirects it
        ["/storage-access-api/resources/handle-headers-retry.py"] = (request, response, stash) =>
        {
            if (request.Get("key") is not { } key)
            {
                response.Status = 400;
                return;
            }

            var storageAccessStatus = request.Header("Sec-Fetch-Storage-Access");
            if (storageAccessStatus == "active")
            {
                key += "active";
            }

            switch (request.Get("retry-allowed-origin"))
            {
                case null:
                    break;
                case "*":
                    response.Set("Activate-Storage-Access", "retry; allowed-origin=*");
                    break;
                case "":
                    response.Set("Activate-Storage-Access", "retry");
                    break;
                case var allowedOrigin:
                    response.Set("Activate-Storage-Access", "retry; allowed-origin=\"" + allowedOrigin + "\"");
                    break;
            }

            // header_helpers.maybe_set_redirect()
            var location = storageAccessStatus == "active" ? request.Get("once-active-redirect-location", "") : request.Get("redirect-location", "");
            if (location is { Length: > 0 })
            {
                response.Status = 302;
                response.Set("Location", location);
            }

            stash.Put(MakeStorageAccessStashKey(key, request), GetStashableHeaders(request), StorageAccessHeadersStashPath);
            response.Content = MakeStorageAccessResponseBody(request);
        },

        // nested-handle-storage-access-headers.py: the document of header_helpers.make_response_body()
        ["/storage-access-api/resources/nested-handle-storage-access-headers.py"] = (request, response, _) =>
        {
            response.Content = MakeStorageAccessResponseBody(request);
        },

        // retrieve-storage-access-headers.py: the stashed headers of a key (204 when there are none)
        ["/storage-access-api/resources/retrieve-storage-access-headers.py"] = (request, response, stash) =>
        {
            if (request.Get("key") is not { } key)
            {
                response.Status = 400;
                return;
            }

            if (stash.TakeValue(MakeStorageAccessStashKey(key, request), StorageAccessHeadersStashPath) is not string headers)
            {
                response.Status = 204;
                return;
            }

            response.Content = headers;
        },
    };

    /// <summary>
    /// header_helpers.make_stash_key(): the redirected requests stash their headers at another key. The handler hashes the
    /// key with MD5, which does not change which keys are equal.
    /// </summary>
    private static string MakeStorageAccessStashKey(string key, WptHandlerRequest request) => request.Has("redirected") ? key + "redirected" : key;

    /// <summary>
    /// header_helpers.get_stashable_headers(): the headers of the request as a JSON object, whose keys are the lowercase
    /// names of the headers (wptserve's RequestHeaders) and whose values are the lists of their values.
    /// </summary>
    private static string GetStashableHeaders(WptHandlerRequest request)
    {
        var names = new List<string>();
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, value) in request.Headers)
        {
            var lowerName = name.ToLowerInvariant();
            if (!values.TryGetValue(lowerName, out var list))
            {
                names.Add(lowerName);
                values[lowerName] = list = [];
            }

            list.Add(value);
        }

        return "{" + string.Join(", ", names.Select(name => PythonJsonString(name) + ": [" + string.Join(", ", values[name].Select(PythonJsonString)) + "]")) + "}";
    }

    /// <summary>header_helpers.make_response_body(): a document that loads the script of the script parameter, or an empty body.</summary>
    private static string MakeStorageAccessResponseBody(WptHandlerRequest request)
    {
        if (request.Get("script") is not { } script)
            return "";

        return $$"""

                <!DOCTYPE html>
              <meta charset="utf-8">
              <title>Subframe with script</title>
              <script src="/resources/testharness.js"></script>
              <script src="/resources/testdriver.js"></script>
              <script src="/resources/testdriver-vendor.js"></script>
              <script>
                var should_ack_load = false;
              </script>

              <body>
              <script src="{{script}}"></script>
              </body>


            """;
    }
}
