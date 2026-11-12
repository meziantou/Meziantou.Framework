using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the Early Hints tests (<c>loading/early-hints/resources/*.h2.py</c>): their <c>handle_headers</c> function
/// sends <c>103</c> responses (<see cref="WptHandlerResponse.InterimResponses"/>) before the headers of the response, and their
/// <c>main</c> function sends the body.
/// </summary>
internal static partial class WptPythonHandlers
{
    /// <summary>The directory of the handlers, the path of the values they put in the stash (<c>_url_dir()</c> of <c>utils.py</c>).</summary>
    private const string EarlyHintsDirectory = "/loading/early-hints/resources/";

    private static readonly Dictionary<string, Handler> EarlyHintsHandlers = new(StringComparer.Ordinal)
    {
        // A response with a 103 response that preloads the URL of the resource-url parameter as an image, whose status is 404
        [EarlyHintsDirectory + "404-with-early-hints.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=image"));
            response.Status = 404;
            response.Set("content-type", "text/html");
            response.Body = ReadEarlyHintsFile("404-with-early-hints.html");
        },
        [EarlyHintsDirectory + "arbitrary-header-in-early-hints.h2.py"] = (_, response, _) =>
        {
            EarlyHintsResponse(response, ("x-arbitrary-header", "foobar"));
            EarlyHintsPage(response, "arbitrary-header-in-early-hints.html");
        },
        [EarlyHintsDirectory + "coep-mismatch.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("cross-origin-embedder-policy", EarlyHintsParameter(request, "early-hints-policy")), ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script"));
            EarlyHintsPage(response, "coep-mismatch.html");
            response.Set("cross-origin-embedder-policy", EarlyHintsParameter(request, "final-policy"));
        },
        [EarlyHintsDirectory + "csp-basic-loader.h2.py"] = (request, response, _) =>
        {
            var resourceOrigin = EarlyHintsParameter(request, "resource-origin");
            List<(string, string)> headers = [("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script")];
            if (GetEarlyHintsContentSecurityPolicy(EarlyHintsParameter(request, "early-hints-policy"), resourceOrigin) is { } earlyHintsPolicy)
            {
                headers.Add(("content-security-policy", earlyHintsPolicy));
            }

            EarlyHintsResponse(response, [.. headers]);
            EarlyHintsPage(response, "csp-basic.html");
            if (GetEarlyHintsContentSecurityPolicy(EarlyHintsParameter(request, "final-policy"), resourceOrigin) is { } finalPolicy)
            {
                response.Set("content-security-policy", finalPolicy);
            }
        },
        [EarlyHintsDirectory + "csp-document-disallow-loader.h2.py"] = (request, response, _) =>
        {
            List<(string, string)> headers = [("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script")];
            if (EarlyHintsParameter(request, "early-hints-policy") == "allowed")
            {
                headers.Add(("content-security-policy", "script-src 'self' 'unsafe-inline' " + EarlyHintsParameter(request, "resource-origin")));
            }

            EarlyHintsResponse(response, [.. headers]);
            EarlyHintsPage(response, "csp-document-disallow.html");
        },

        // A script sent once the resume-delayed-js.h2.py request of its id is received
        [EarlyHintsDirectory + "delayed-js.h2.py"] = (request, response, stash) =>
        {
            if (!WaitForEarlyHintsPreload(response, stash, EarlyHintsParameter(request, "id")))
                return;

            response.Add("Content-Type", "text/javascript");
            response.Add("Cache-Control", "max-age=600");
            response.Content = "/*empty script*/";
        },

        // The 103 response is sent after the first delay, and the headers of the response after the second one
        [EarlyHintsDirectory + "early-hints-delay.h2.py"] = (request, response, _) =>
        {
            response.InterimResponses.Add((Milliseconds(request.Get("delay1"), 0), 103, [new("link", "</empty.js>; rel=preload; as=script")]));
            response.Delay = Milliseconds(request.Get("delay2"), 0);
            response.Set("content-type", "text/html");
            response.Content = "Hello";
        },

        // Records the time and the headers of the request in the stash (store_request_timing_and_headers() of utils.py)
        [EarlyHintsDirectory + "fetch-and-record-js.h2.py"] = (request, response, stash) =>
        {
            var timestamp = request.Now.ToUnixTimeMilliseconds() / 1000.0;
            var headers = new List<string>();
            foreach (var name in request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
            {
                headers.Add(PythonJsonString(name) + ": " + PythonJsonString(request.Header(name) ?? ""));
            }

            stash.Put(EarlyHintsParameter(request, "id"), "{\"timestamp\": " + timestamp.ToString("R", CultureInfo.InvariantCulture) + ", \"headers\": {" + string.Join(", ", headers) + "}}", EarlyHintsDirectory);
            response.Add("Content-Type", "text/javascript");
            response.Add("Cache-Control", "max-age=600");
            response.Content = "/*empty script*/";
            response.Delay = TimeSpan.FromMilliseconds(50);
        },
        [EarlyHintsDirectory + "get-fetch-timing-and-headers.h2.py"] = (request, response, stash) =>
        {
            response.Add("Content-Type", "application/json");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = stash.TakeValue(EarlyHintsParameter(request, "id"), EarlyHintsDirectory) as string ?? "";
        },
        [EarlyHintsDirectory + "html-with-early-hints.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script"));
            response.Set("content-type", "text/html");
            if (request.Get("x-frame-options") is { } xFrameOptions)
            {
                response.Set("x-frame-options", DecodeUtf8(xFrameOptions));
            }

            response.Content = "<!-- empty -->";
        },

        // The 103 response has the header of the header-value parameter, which is not valid (a line break or a null byte)
        [EarlyHintsDirectory + "invalid-headers-in-early-hints.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("invalid-header", request.Get("header-value") ?? throw new KeyNotFoundException("The header-value parameter is missing")));
            response.Set("content-type", "text/html");
            response.Content = "<div>This page should not be loaded.</div>";
        },
        [EarlyHintsDirectory + "early-hints-test-loader.h2.py"] = EarlyHintsTestLoader,
        [EarlyHintsDirectory + "modulepreload-in-early-hints.h2.py"] = (request, response, _) =>
        {
            var link = "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=modulepreload";
            if (request.Get("as") is { Length: > 0 } asValue)
            {
                link += "; as=" + DecodeUtf8(asValue);
            }

            EarlyHintsResponse(response, ("link", link));
            EarlyHintsPage(response, "modulepreload-in-early-hints.html");
        },

        // Two 103 responses: only the first one is processed by the browsers
        [EarlyHintsDirectory + "multiple-early-hints-responses.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("content-security-policy", "script-src 'self' 'unsafe-inline'"), ("link", "<" + EarlyHintsParameter(request, "first-preload") + ">; rel=preload; as=script"));
            EarlyHintsResponse(response, ("content-security-policy", "script-src 'self' 'unsafe-inline' " + EarlyHintsParameter(request, "second-preload-origin")), ("link", "<" + EarlyHintsParameter(request, "second-preload") + ">; rel=preload; as=script"));
            EarlyHintsPage(response, "multiple-early-hints-responses.html");
        },
        [EarlyHintsDirectory + "pdf-with-early-hints.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script"));
            response.Delay = TimeSpan.FromMilliseconds(100);
            response.Set("content-type", "application/pdf");
            response.Body = ReadWptFile("resources/example.pdf");
        },
        [EarlyHintsDirectory + "png-with-early-hints.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script"));
            response.Delay = TimeSpan.FromMilliseconds(100);
            response.Set("content-type", "image/png");
            response.Body = ReadEarlyHintsFile("square.png");
        },
        [EarlyHintsDirectory + "preconnect-in-early-hints.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-origin") + ">; rel=preconnect"));
            EarlyHintsPage(response, "preconnect-in-early-hints.html");
        },
        [EarlyHintsDirectory + "preload-as-test.h2.py"] = (request, response, _) =>
        {
            var link = "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload";
            if (request.Get("as") is { Length: > 0 } asValue)
            {
                link += "; as=" + DecodeUtf8(asValue);
            }

            EarlyHintsResponse(response, ("link", link));
            EarlyHintsPage(response, "preload-as-test.html");
        },

        // The headers of the response are sent once the preload of the 103 response was received
        [EarlyHintsDirectory + "preload-finished-before-final-response.h2.py"] = (request, response, stash) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script"));
            var resourceId = EarlyHintsParameter(request, "resource-id");
            response.HeadersCondition = () => stash.TakeValue(resourceId, EarlyHintsDirectory) is not null;
            response.Delay = TimeSpan.FromMilliseconds(100);
            EarlyHintsPage(response, "preload-finished-before-final-response.html");
        },

        // The body of the response is sent once the preload of the 103 response was received
        [EarlyHintsDirectory + "preload-finished-while-receiving-final-response-body.h2.py"] = (request, response, stash) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script"));
            response.Set("content-type", "text/html");
            var resourceId = EarlyHintsParameter(request, "resource-id");
            response.ChunksCondition = () => stash.TakeValue(resourceId, EarlyHintsDirectory) is not null;
            response.ChunksTimeout = TimeSpan.FromMinutes(1);
            response.Chunks.Add((TimeSpan.FromMilliseconds(100), ReadEarlyHintsFile("preload-finished-while-receiving-final-response-body.html")));
        },
        [EarlyHintsDirectory + "preload-in-flight-when-consumed.h2.py"] = (request, response, _) =>
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "resource-url") + ">; rel=preload; as=script"));
            EarlyHintsPage(response, "preload-in-flight-when-consumed.html");
        },
        [EarlyHintsDirectory + "redirect-between-early-hints.h2.py"] = RedirectBetweenEarlyHints,
        [EarlyHintsDirectory + "redirect-with-early-hints.h2.py"] = (request, response, _) =>
        {
            var preloadUrl = EarlyHintsParameter(request, "preload-url");
            EarlyHintsResponse(response, ("link", "<" + preloadUrl + ">; rel=preload; as=script"));
            response.Status = 302;
            response.Set("location", EarlyHintsParameter(request, "redirect-url") + "?preload-url=" + preloadUrl);
        },

        // The 103 response and the response have the referrer policy and the preloads of the parameters (the second Link
        // header of the response replaces the first one)
        [EarlyHintsDirectory + "referrer-policy-test-loader.h2.py"] = (request, response, _) =>
        {
            (string Name, string Value)[] headers =
            [
                ("referrer-policy", request.Get("referrer-policy") ?? throw new KeyNotFoundException("The referrer-policy parameter is missing")),
                ("link", "<" + EarlyHintsParameter(request, "same-origin-preload-url") + ">; rel=preload; as=script"),
                ("link", "<" + EarlyHintsParameter(request, "cross-origin-preload-url") + ">; rel=preload; as=script"),
            ];
            EarlyHintsResponse(response, headers);
            response.Delay = TimeSpan.FromMilliseconds(200);
            EarlyHintsPage(response, "referrer-policy-test.html");
            foreach (var (name, value) in headers)
            {
                response.Set(name, value);
            }
        },
        [EarlyHintsDirectory + "resume-delayed-js.h2.py"] = (request, response, stash) =>
        {
            stash.Put(EarlyHintsParameter(request, "id"), true, EarlyHintsDirectory);
            response.Add("Content-Type", "text/plain");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = "OK";
        },
    };

    /// <summary><c>request.GET.first(name).decode()</c>: a missing parameter is a <c>KeyError</c> (a <c>500</c> response).</summary>
    private static string EarlyHintsParameter(WptHandlerRequest request, string name) => DecodeUtf8(RequiredParameter(request, name));

    private static byte[] ReadEarlyHintsFile(string name) => ReadWptFile("loading/early-hints/resources/" + name);

    /// <summary>Adds a <c>103</c> response with the headers to the response (<c>write_raw_header_frame()</c>).</summary>
    private static void EarlyHintsResponse(WptHandlerResponse response, params (string Name, string Value)[] headers)
        => response.InterimResponses.Add((TimeSpan.Zero, 103, [.. headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value))]));

    /// <summary>The <c>text/html</c> response whose body is a file of the directory.</summary>
    private static void EarlyHintsPage(WptHandlerResponse response, string name)
    {
        response.Set("content-type", "text/html");
        response.Body = ReadEarlyHintsFile(name);
    }

    /// <summary>The content security policy of a policy of the parameters of csp-basic-loader.h2.py (<c>_calculate_csp_value()</c>), or null.</summary>
    private static string? GetEarlyHintsContentSecurityPolicy(string policy, string resourceOrigin) => policy switch
    {
        "allowed" => "script-src 'self' 'unsafe-inline' " + resourceOrigin,
        "disallowed" => "script-src 'self' 'unsafe-inline'",
        _ => null,
    };

    /// <summary>
    /// <c>wait_for_preload_to_finish()</c> of <c>utils.py</c>: polls the stash every 100 ms until it has a value for the id
    /// (which is taken), then waits 100 ms. Returns false while the handler polls (see <see cref="WptHandlerResponse.RetryAfter"/>).
    /// </summary>
    private static bool WaitForEarlyHintsPreload(WptHandlerResponse response, WptStash stash, string id)
    {
        if (stash.TakeValue(id, EarlyHintsDirectory) is null)
        {
            response.RetryAfter = TimeSpan.FromMilliseconds(100);
            return false;
        }

        response.Delay = TimeSpan.FromMilliseconds(100);
        return true;
    }

    // early-hints-test-loader.h2.py: a 103 response with the preloads and the preconnects of the parameters (JSON objects),
    // then the page of the test_url parameter, whose response has the preloads too unless exclude_preloads_from_ok_response
    // is true
    private static void EarlyHintsTestLoader(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var preloadHeaders = new List<string>();
        foreach (var (name, value) in request.GetParameters)
        {
            if (name != "preloads")
                continue;

            using var preload = JsonDocument.Parse(DecodeUtf8(value));
            var root = preload.RootElement;
            var header = new StringBuilder("<").Append(root.GetProperty("url").GetString()).Append(">; rel=preload; as=").Append(root.GetProperty("as_attr").GetString());
            if (root.TryGetProperty("crossorigin_attr", out var crossOrigin))
            {
                header.Append(crossOrigin.GetString() is { Length: > 0 } crossOriginValue ? "; crossorigin=" + crossOriginValue : "; crossorigin");
            }

            if (root.TryGetProperty("fetchpriority_attr", out var fetchPriority) && fetchPriority.GetString() is { Length: > 0 } fetchPriorityValue)
            {
                header.Append("; fetchpriority=").Append(fetchPriorityValue);
            }

            if (root.TryGetProperty("integrity_attr", out var integrity) && integrity.GetString() is { Length: > 0 } integrityValue)
            {
                header.Append("; integrity=\"").Append(integrityValue).Append('"');
            }

            preloadHeaders.Add(header.ToString());
        }

        var preconnectHeaders = new List<string>();
        foreach (var (name, value) in request.GetParameters)
        {
            if (name != "preconnects")
                continue;

            using var preconnect = JsonDocument.Parse(DecodeUtf8(value));
            var root = preconnect.RootElement;
            var header = "<" + root.GetProperty("url").GetString() + ">; rel=preconnect";
            if (root.TryGetProperty("crossorigin_attr", out var crossOrigin))
            {
                header += crossOrigin.GetString() is { Length: > 0 } crossOriginValue ? "; crossorigin=" + crossOriginValue : "; crossorigin";
            }

            preconnectHeaders.Add(header);
        }

        EarlyHintsResponse(response, [.. preloadHeaders.Concat(preconnectHeaders).Select(header => ("link", header))]);

        // The generation of the response takes time
        response.Delay = TimeSpan.FromMilliseconds(200);
        response.Set("content-type", "text/html");
        if (EarlyHintsParameter(request, "exclude_preloads_from_ok_response") != "true")
        {
            foreach (var header in preloadHeaders)
            {
                response.Add("link", header);
            }
        }

        var testPath = EarlyHintsParameter(request, "test_url");
        response.Body = ReadEarlyHintsFile(testPath.StartsWith("resources/", StringComparison.Ordinal) ? testPath["resources/".Length..] : testPath);
    }

    // redirect-between-early-hints.h2.py: the "redirect" step sends a 103 response, then redirects to the redirect-url
    // parameter with the parameters of the request (test-step=final-response), whose step sends another 103 response and the
    // page of the final-test-page parameter
    private static void RedirectBetweenEarlyHints(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var step = EarlyHintsParameter(request, "test-step");
        if (step == "redirect")
        {
            EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "preload-before-redirect") + ">; rel=preload; as=script"));

            // The parameters are not encoded again (the first value of each parameter, in the order of the query)
            var parameters = new List<string>();
            foreach (var name in request.GetParameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal))
            {
                parameters.Add(name == "test-step" ? "test-step=final-response" : name + "=" + DecodeUtf8(request.Get(name) ?? ""));
            }

            response.Status = 302;
            response.Set("location", EarlyHintsParameter(request, "redirect-url") + "?" + string.Join('&', parameters));
            return;
        }

        if (step != "final-response")
            throw new InvalidOperationException("Invalid step: " + step);

        EarlyHintsResponse(response, ("link", "<" + EarlyHintsParameter(request, "preload-after-redirect") + ">; rel=preload; as=script"));
        EarlyHintsPage(response, EarlyHintsParameter(request, "final-test-page"));
    }
}
