using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the <c>content-security-policy</c> tests and of the reports of the violations
/// (<c>reporting/resources/report.py</c>, which records the reports of the <c>report-uri</c> directive and returns them).
/// </summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> ContentSecurityPolicyHandlers = new(StringComparer.Ordinal)
    {
        ["/reporting/resources/report.py"] = ReportPy,

        // content-security-policy/support/resource.py: a JSON resource that any origin can read
        ["/content-security-policy/support/resource.py"] = (_, response, _) =>
        {
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = "{ \"result\": \"success\" }";
        },

        // content-security-policy/generic/support/304-response.py: a document with nonced inline scripts, and a 304 response
        // with another policy when it is revalidated (If-None-Match)
        ["/content-security-policy/generic/support/304-response.py"] = (request, response, _) =>
        {
            var revalidation = !string.IsNullOrEmpty(request.Header("If-None-Match"));
            response.Add("Content-Type", "text/html");
            response.Add("Content-Security-Policy", "script-src 'nonce-" + (revalidation ? "def" : "abc") + "' 'sha256-IIB78ZS1RMMrAWpsLg/RrDbVPhI14rKm3sFOeKPYulw='");
            response.Add("Cache-Control", "private, max-age=0, must-revalidate");
            response.Add(revalidation ? "ETag" : "Etag", "123456");
            if (revalidation)
            {
                response.Status = 304;
                response.Content = "";
                return;
            }

            response.Content = """

                <!DOCTYPE html>
                <html>
                <head>
                    <script>
                        window.addEventListener("securitypolicyviolation", function(e) {
                            top.postMessage(e.originalPolicy, '*');
                        });
                    </script>
                    <script nonce="abc">
                        top.postMessage('abc_executed', '*');
                    </script>
                    <script nonce="def">
                        top.postMessage('def_executed', '*');
                    </script>
                </head>
                </html>

                """;
        },

        // x-frame-options/support/xfo.py: a document with the X-Frame-Options headers of the value and value2 parameters (and
        // the policy of csp_value), which posts "Loaded" to its parent
        ["/x-frame-options/support/xfo.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("X-Frame-Options", request.Get("value") ?? "");
            if (request.Get("value2") is { } value2)
            {
                response.Add("X-Frame-Options", value2);
            }

            if (request.Get("csp_value") is { } policy)
            {
                response.Add("Content-Security-Policy", policy);
            }

            response.Content = """
                <!DOCTYPE html>
                        <html>
                        <head>
                          <title>XFO.</title>
                          <script>window.parent.postMessage('Loaded', '*');</script>
                        </head>
                        <body>
                          Loaded
                        </body>
                        </html>

                """;
        },

        // x-frame-options/support/nested.py: a document that frames xfo.py of the origin parameter, and forwards its messages
        ["/x-frame-options/support/nested.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Content = $$"""
                <!DOCTYPE html>
                <title>XFO.</title>
                <body>
                <script>
                  var gotMessage = false;
                  window.addEventListener("message", e => {
                    gotMessage = true;
                    window.parent.postMessage(e.data, "*");
                  });

                  var i = document.createElement("iframe");
                  i.src = "{{request.Get("origin")}}/x-frame-options/support/xfo.py?value={{request.Get("value")}}";
                  i.onload = _ => {
                    setTimeout(_ => {
                      if (!gotMessage && {{request.Get("loadShouldSucceed", "false")}} != true) {
                        window.parent.postMessage("Failed", "*");
                      }
                    }, 100);
                  };
                  document.body.appendChild(i);
                </script>

                """;
        },

        // x-frame-options/support/redirect.py: a redirect to the url parameter with the X-Frame-Options header of the value parameter
        ["/x-frame-options/support/redirect.py"] = (request, response, _) =>
        {
            response.Status = 302;
            response.Add("X-Frame-Options", request.Get("value") ?? "");
            response.Add("Location", request.Get("url") ?? "");
        },

        // content-security-policy/support/echo-policy.py: a document whose policy is the policy parameter
        ["/content-security-policy/support/echo-policy.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("Content-Security-Policy", request.Get("policy"));
            response.Content = "<!DOCTYPE html><title>Echo.</title>";
        },

        // content-security-policy/embedded-enforcement/support/echo-allow-csp-from.py: a document with the Allow-CSP-From header
        // of the allow_csp_from parameter, which posts the violations of its policies, the load of an image and its id to the
        // top-level window
        ["/content-security-policy/embedded-enforcement/support/echo-allow-csp-from.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            if (request.Get("allow_csp_from") is { } allowCspFrom)
            {
                response.Add("Allow-CSP-From", allowCspFrom);
            }

            var id = request.Get("id");
            response.Body = Encoding.UTF8.GetBytes($$"""

                <!DOCTYPE html>
                <html>
                <head>
                    <title>This page enforces embedder's policies</title>
                    <script nonce="123">
                        document.addEventListener("securitypolicyviolation", function(e) {
                            var response = {};
                            response["id"] = "{{DecodeUtf8(id ?? "")}}";
                            response["securitypolicyviolation"] = true;
                            response["blockedURI"] = e.blockedURI;
                            response["lineNumber"] = e.lineNumber;
                            window.top.postMessage(response, '*');
                        });
                    </script>
                </head>
                <body>
                    <script nonce="123">
                        let img = document.createElement('img');
                        img.src = "../../support/pass.png";
                        img.onload = function() { window.top.postMessage("img loaded", '*'); }
                        document.body.appendChild(img);
                    </script>
                    <style>
                        body {
                            background-color: maroon;
                        }
                    </style>
                    <script nonce="123">
                        var response = {};
                        response["id"] = "{{DecodeUtf8(id ?? "")}}";
                        response["loaded"] = true;
                        window.top.postMessage(response, '*');
                    </script>
                    <script>
                        // Inline script which might be blocked by CSP.
                        navigator.userAgent;
                    </script>
                </body>
                </html>

                """);
        },

        // content-security-policy/embedded-enforcement/support/echo-policy-multiple.py: a document with the
        // Content-Security-Policy headers of the policy, policy2 and policy3 parameters, which posts its id to the top-level window
        ["/content-security-policy/embedded-enforcement/support/echo-policy-multiple.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            foreach (var name in (ReadOnlySpan<string>)["policy", "policy2", "policy3"])
            {
                if (request.Get(name) is { } policy)
                {
                    response.Add("Content-Security-Policy", policy);
                }
            }

            response.Body = Encoding.UTF8.GetBytes($$"""

                <!DOCTYPE html>
                <html>
                <head>
                    <title>This page sets given CSP upon itself.</title>
                </head>
                <body>
                    <script nonce="abc">
                        var response = {};
                        response["id"] = "{{DecodeUtf8(request.Get("id") ?? "")}}";
                        response["loaded"] = true;
                        window.top.postMessage(response, '*');
                    </script>
                </body>
                </html>

                """);
        },

        // content-security-policy/embedded-enforcement/support/echo-required-csp.py: a document that posts the
        // Sec-Required-CSP header of its request (and the header that a line break in it would inject) to its parent, forwards
        // the messages of its child, and creates a child iframe with the csp attribute of the second_level_iframe_csp parameter
        ["/content-security-policy/embedded-enforcement/support/echo-required-csp.py"] = (request, response, _) =>
        {
            var message = "{\"test_header_injection\": " + JsonOrNull(request.Header("Test-Header-Injection")) + ", \"required_csp\": " + JsonOrNull(request.Header("Sec-Required-CSP")) + "}";
            var secondLevelIframe = "";
            if (request.Has("include_second_level_iframe"))
            {
                secondLevelIframe = request.Get("second_level_iframe_csp") is { Length: > 0 } secondLevelCsp
                    ? $$"""
                        <script>
                                    var i2 = document.createElement('iframe');
                                    i2.src = 'echo-required-csp.py';
                                    i2.csp = "{{secondLevelCsp}}";
                                    document.body.appendChild(i2);
                                    </script>
                        """
                    : """
                        <script>
                                    var i2 = document.createElement('iframe');
                                    i2.src = 'echo-required-csp.py';
                                    document.body.appendChild(i2);
                                    </script>
                        """;
            }

            response.Add("Content-Type", "text/html");
            response.Add("Allow-CSP-From", "*");
            response.Content = $$"""

                <!DOCTYPE html>
                <html>
                <head>
                    <script>
                      window.addEventListener('message', function(e) {
                        window.parent.postMessage(e.data, '*');
                      });

                      window.parent.postMessage({{message}}, '*');
                    </script>
                </head>
                <body>
                {{secondLevelIframe}}
                </body>
                </html>

                """;

            static string JsonOrNull(string? value) => value is null ? "null" : JsonSerializer.Serialize(value);
        },

        // content-security-policy/parsing/support/csp.py: a document whose policy is the policy parameter, which posts the
        // result of the load of an image to its parent
        ["/content-security-policy/parsing/support/csp.py"] = (request, response, _) =>
        {
            var policy = request.Get("policy") ?? "";
            response.Add("Content-Type", "text/html");
            response.Add("Content-Security-Policy", policy);
            response.Body = Encoding.UTF8.GetBytes($$"""
                <!DOCTYPE html>
                        <html>
                        <head>
                          <title>CSP</title>
                        </head>
                        <body>
                          <p>{{DecodeUtf8(policy)}}</p>
                          <img src="/content-security-policy/support/pass.png" />
                          <script>
                            let img = document.querySelector("img");
                            img.onload = img.onerror = (event) => {
                              window.parent.postMessage(event.type, '*');
                            };
                          </script>
                        </body>
                        </html>

                """);
        },

        // content-security-policy/reporting/support/set-cookie.py: sets the cookie of the name, value and path parameters
        ["/content-security-policy/reporting/support/set-cookie.py"] = (request, response, _) =>
        {
            var name = request.Get("name") ?? throw new InvalidOperationException("The name parameter is missing");
            var path = request.Get("path") ?? throw new InvalidOperationException("The path parameter is missing");
            var value = request.Get("value") ?? "1";
            response.Add("Content-Type", "application/json");
            response.Add("Set-Cookie", string.Create(CultureInfo.InvariantCulture, $"{name}={value}; Path={path}; Expires=09 Jun {DateTime.UtcNow.Year + 1} 10:18:14 GMT"));
            response.Content = "{}";
        },

        // content-security-policy/reporting/support/redirect-throw-function.sub.py: a redirect to throw-function.js
        ["/content-security-policy/reporting/support/redirect-throw-function.sub.py"] = (request, response, _) =>
        {
            response.Status = 302;
            var url = request.Url.Href;
            response.Set("Location", url[..url.IndexOf("redirect-throw-function", StringComparison.Ordinal)] + "throw-function.js?secret=1234#ref");
        },

        // content-security-policy/reporting/support/not-embeddable-frame.py: a response whose frame-ancestors directive
        // forbids any embedder, reported to report.py
        ["/content-security-policy/reporting/support/not-embeddable-frame.py"] = (request, response, _) =>
        {
            if (request.Get("xFrameOptions") is { Length: > 0 } xFrameOptions)
            {
                response.Add("X-Frame-Options", xFrameOptions);
            }

            var header = request.Get("reportOnly") == "true" ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
            response.Add(header, "frame-ancestors 'none'; report-uri " + (request.Get("reportUriBase") ?? "") + "/reporting/resources/report.py?op=put&reportID=" + request.Get("reportID"));
            response.Content = "{}";
        },

        // content-security-policy/unsafe-eval/support/echo-eval-with-policy.py: a document whose policy is the policy
        // parameter, which posts to its parent whether eval() is allowed in its realm and in the realm of its parent
        ["/content-security-policy/unsafe-eval/support/echo-eval-with-policy.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("Content-Security-Policy", request.Get("policy"));
            response.Content = """

                <!DOCTYPE html>
                <html>
                <script>
                function check_eval(context) {
                  context.eval_check_variable = 0;
                  try {
                    id = context.eval("eval_check_variable + 1");
                  } catch (e) {
                    if (e instanceof EvalError) {
                      if (context.eval_check_variable === 0)
                        return "blocked";
                      else
                        return "EvalError exception, but eval was executed";
                    } else {
                      return "Unexpected exception: " + e.message;
                    }
                  }
                  return "allowed";
                }

                window.parent.postMessage({
                  evalInIframe: check_eval(window),
                  evalInParent: check_eval(parent),
                });
                </script>
                </html>

                """;
        },
    };

    /// <summary>The interval of the polling of <c>report.py</c> (<c>time.sleep(0.5)</c>).</summary>
    private static readonly TimeSpan ReportPollingInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// reporting/resources/report.py: a POST request records the reports of its body (a report of the <c>report-uri</c>
    /// directive, or a list of reports of the Reporting API) with the <c>Content-Type</c> of the request, the cookies of the
    /// request and the number of requests, under the key of the reportID (or endpoint) parameter. A GET request returns
    /// them (<c>op=retrieve_report</c>, <c>retrieve_cookies</c> or <c>retrieve_count</c>) once they are recorded: the handler
    /// polls the stash every 0.5 second (measured by the virtual clock) until the timeout parameter elapses.
    /// </summary>
    private static void ReportPy(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        // The CORS-preflight requests
        if (request.Method == "OPTIONS")
        {
            if ((request.Header("Origin") ?? "").Contains("www2", StringComparison.Ordinal))
            {
                response.Status = 400;
                response.Content = "CORS preflight rejected for www2";
                return;
            }

            response.Add("Content-Type", "text/plain");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Add("Access-Control-Allow-Methods", "post");
            response.Add("Access-Control-Allow-Headers", "Content-Type");
            response.Content = "CORS allowed";
            return;
        }

        // The deletion of the reports
        JsonNode? body = null;
        if (request.Method == "POST")
        {
            body = JsonNode.Parse(request.Body);
            if (body is JsonObject operation && operation.ContainsKey("op"))
            {
                if (operation["op"]?.GetValueKind() == JsonValueKind.String && (string?)operation["op"] == "DELETE" && operation["reportIDs"] is JsonArray reportIds)
                {
                    lock (stash)
                    {
                        foreach (var reportId in reportIds)
                        {
                            stash.TakeValue(reportId?.ToString() ?? "", request.Path);
                        }
                    }

                    response.Content = "reports cleared";
                    return;
                }

                response.Status = 400;
                response.Content = "op parameter value not recognized";
                return;
            }
        }

        string key;
        if (request.Get("reportID") is { } reportIdParameter)
        {
            key = reportIdParameter;
        }
        else if (request.Get("endpoint") is { } endpoint)
        {
            // uuid.uuid5(uuid.NAMESPACE_OID, endpoint).urn[9:]
            key = CreateUuid5(Guid.Parse("6ba7b812-9dad-11d1-80b4-00c04fd430c8"), Encoding.UTF8.GetBytes(DecodeUtf8(endpoint)));
        }
        else
        {
            response.Status = 400;
            response.Content = "Either reportID or endpoint parameter is required.";
            return;
        }

        // The cookie and count keys are derived from the report ID
        var cookieKey = "cccc" + (key.Length > 4 ? key[4..] : "");
        var countKey = "dddd" + (key.Length > 4 ? key[4..] : "");
        if (request.Method == "GET")
        {
            var timeout = double.TryParse(request.Get("timeout"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(0.5);
            var minCount = int.TryParse(request.Get("min_count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 1;
            var retain = request.Has("retain");
            var op = request.Get("op") ?? "";
            switch (op)
            {
                case "retrieve_report" or "":
                    response.Add("Content-Type", "application/json");
                    RetrieveFromStash(key, "[]", minCount, retain);
                    return;
                case "retrieve_cookies":
                    response.Add("Content-Type", "application/json");
                    RetrieveFromStash(cookieKey, "\"None\"", minCount: null, retain: false, value => "{ \"reportCookies\" : " + value + "}");
                    return;
                case "retrieve_count":
                    response.Add("Content-Type", "application/json");
                    RetrieveFromStash(countKey, "0", minCount: null, retain: false, value => "{ \"report_count\": " + value + " }");
                    return;
                default:
                    response.Status = 400;
                    response.Content = "op parameter value not recognized.";
                    return;
            }

            // retrieve_from_stash(): the value of the stash (and at least min_count reports), polled every 0.5 second until the timeout
            void RetrieveFromStash(string stashKey, string defaultValue, int? minCount, bool retain, Func<string, string>? format = null)
            {
                if (request.PollingElapsed == TimeSpan.Zero)
                {
                    response.RetryAfter = ReportPollingInterval;
                    return;
                }

                lock (stash)
                {
                    if (stash.TakeValue(stashKey, request.Path) is { } value)
                    {
                        var hasSufficientReports = minCount is null || (value is JsonArray reports && reports.Count >= minCount);
                        if (retain || !hasSufficientReports)
                        {
                            stash.Put(stashKey, value, request.Path);
                        }

                        if (hasSufficientReports)
                        {
                            var json = value is JsonNode node ? node.ToJsonString() : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                            response.Content = format is null ? json : format(json);
                            return;
                        }
                    }
                }

                if (request.PollingElapsed < timeout)
                {
                    response.RetryAfter = ReportPollingInterval;
                    return;
                }

                response.Content = format is null ? defaultValue : format(defaultValue);
            }
        }

        // The cookies of the request
        var cookies = ParseRequestCookies(request);
        if (cookies.Count > 0)
        {
            var cookiesObject = new JsonObject();
            foreach (var name in cookies.Select(cookie => cookie.Key).Distinct(StringComparer.Ordinal))
            {
                // str(request.cookies.get_list(name)): the list of the CookieValue objects of wptserve, whose repr() is the
                // OutputString() of the morsel (name=value)
                cookiesObject[name] = "[" + string.Join(", ", cookies.Where(cookie => cookie.Key == name).Select(cookie => cookie.Key + "=" + cookie.Value)) + "]";
            }

            lock (stash)
            {
                stash.TakeValue(cookieKey, request.Path);
                stash.Put(cookieKey, cookiesObject, request.Path);
            }
        }

        // The reports (a report of the report-uri directive is an object, not a list)
        var newReports = body is JsonArray array ? array : new JsonArray(body);
        foreach (var report in newReports)
        {
            if (report is JsonObject reportObject)
            {
                reportObject["metadata"] = new JsonObject { ["content_type"] = request.Header("Content-Type") };
            }
        }

        lock (stash)
        {
            var reports = stash.TakeValue(key, request.Path) as JsonArray ?? [];
            foreach (var report in newReports.ToArray())
            {
                newReports.Remove(report);
                reports.Add(report);
            }

            stash.Put(key, reports, request.Path);

            // The number of requests received by the endpoint
            var count = stash.TakeValue(countKey, request.Path) is int previousCount ? previousCount : 0;
            stash.Put(countKey, count + 1, request.Path);
        }

        response.Add("Content-Type", "text/plain");
        if (!(request.Header("Origin") ?? "").Contains("www2", StringComparison.Ordinal))
        {
            response.Add("Access-Control-Allow-Origin", "*");
        }

        response.Body = [.. "Recorded report "u8, .. request.Body];
    }

    /// <summary>The string form (without the <c>urn:uuid:</c> prefix) of a name-based UUID using SHA-1 (RFC 9562, version 5), as Python's <c>uuid.uuid5()</c>.</summary>
    private static string CreateUuid5(Guid namespaceId, byte[] name)
    {
        var namespaceBytes = namespaceId.ToByteArray(bigEndian: true);
#pragma warning disable CA5350 // UUID version 5 is defined with SHA-1
        var hash = System.Security.Cryptography.SHA1.HashData([.. namespaceBytes, .. name]);
#pragma warning restore CA5350
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true).ToString("D");
    }
}
