using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>cors</c> tests (<c>cors/resources/*.py</c>) and <c>images/blue-png-cachable.py</c>.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> CorsHandlers = new(StringComparer.Ordinal)
    {
        ["/cors/resources/cors-makeheader.py"] = CorsMakeHeader,
        ["/cors/resources/cors-cookie.py"] = CorsCookie,
        ["/cors/resources/preflight.py"] = (request, response, stash) => CorsPreflight(request, response, stash, partitioned: false),
        ["/cors/resources/preflight-partitioning.py"] = (request, response, stash) => CorsPreflight(request, response, stash, partitioned: true),
        ["/cors/resources/status.py"] = CorsStatus,
        ["/cors/resources/304.py"] = CorsNotModified,
        ["/cors/resources/cache-304.py"] = CorsCacheNotModified,
        ["/cors/resources/expose-headers.py"] = CorsExposeHeaders,
        ["/images/blue-png-cachable.py"] = (_, response, _) =>
        {
            response.Set("Cache-Control", "max-age=3600");
            response.Set("Content-Type", "image/png");
            response.Body = File.ReadAllBytes(Path.Combine(WptPaths.Wpt, "images", "blue.png"));
        },
    };

    // cors/resources/cors-makeheader.py: the CORS headers of the query, the status of the response or of the preflight, a
    // redirect, and the headers of the request as a JSON object. A preflight records its token in the stash, which a request
    // with the check parameter reads.
    private static void CorsMakeHeader(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        const string StashPath = "/cors/resources/cors-makeheader.py";
        var origin = request.Get("origin", request.Header("origin") is { Length: > 0 } requestOrigin ? requestOrigin : "none");
        if (request.Has("check"))
        {
            var token = request.Get("token") ?? throw new InvalidOperationException("The token parameter is missing");
            var value = stash.TakeValue(token, StashPath);
            if (value is not null && request.Get("check") == "keep")
            {
                stash.Put(token, value, StashPath);
            }

            TextResponse(response, value is not null ? "1" : "0");
            return;
        }

        if (origin != "none")
        {
            response.Set("Access-Control-Allow-Origin", origin);
        }

        if (request.Has("origin2"))
        {
            response.Add("Access-Control-Allow-Origin", request.Get("origin2"));
        }

        // The headers of the preflight
        if (request.Has("headers"))
        {
            response.Set("Access-Control-Allow-Headers", request.Get("headers"));
        }

        if (request.Has("credentials"))
        {
            response.Set("Access-Control-Allow-Credentials", request.Get("credentials"));
        }

        if (request.Has("methods"))
        {
            response.Set("Access-Control-Allow-Methods", request.Get("methods"));
        }

        int? code = request.Get("code") is { Length: > 0 } rawCode ? int.Parse(rawCode, CultureInfo.InvariantCulture) : null;
        if (request.Method == "OPTIONS")
        {
            if (request.Has("preflight"))
            {
                code = int.Parse(request.Get("preflight")!, CultureInfo.InvariantCulture);
            }

            if (request.Has("token"))
            {
                stash.Put(request.Get("token")!, "1", StashPath);
            }
        }

        if (request.Has("location"))
        {
            code ??= 302;
            if (code is >= 300 and < 400)
            {
                response.Set("Location", request.Get("location"));
            }
        }

        // The names of the headers are lowercase (wptserve's RequestHeaders)
        var headers = new List<string>();
        foreach (var name in request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            headers.Add(PythonJsonString(name) + ": " + PythonJsonString(request.Header(name) ?? ""));
        }

        headers.Add("\"get_value\": " + PythonJsonString(request.Get("get_value", "")!));
        response.Content = "{" + string.Join(", ", headers) + "}";
        if (code is > 0)
        {
            response.Status = code.Value;
            response.StatusText = "StatusText";
        }
    }

    // cors/resources/cors-cookie.py: the body tells whether the request had the cookie that the previous response set
    private static void CorsCookie(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var origin = request.Get("origin", request.Header("origin")) ?? throw new InvalidOperationException("Missing Origin header");
        var credentials = request.Get("credentials", "true");
        response.Set("Content-Type", "text/plain");
        if (origin != "none")
        {
            response.Set("Access-Control-Allow-Origin", origin);
        }

        if (credentials != "none")
        {
            response.Set("Access-Control-Allow-Credentials", credentials);
        }

        var ident = request.Get("ident", "test")!;
        if (ParseRequestCookies(request).Find(cookie => cookie.Key == ident) is { Key: not null } cookie)
        {
            response.Content = cookie.Value;
            DeleteCookie(response, ident);
        }
        else
        {
            SetCookie(response, ident, "COOKIE");
            response.Content = "NO_COOKIE";
        }
    }

    // cors/resources/preflight.py and preflight-partitioning.py: a preflight records its token in the stash, and the body is
    // the x-print header of the request
    private static void CorsPreflight(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, bool partitioned)
    {
        var stashPath = partitioned ? "/cors/resources/preflight-partitioning.py" : "/cors/resources/preflight.py";
        response.Set("Content-Type", "text/plain");
        if (partitioned)
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }

        if (partitioned ? request.Method == "GET" : request.Has("check"))
        {
            var token = request.Get("token") ?? throw new InvalidOperationException("The token parameter is missing");
            var value = stash.TakeValue(token, stashPath);
            if (value is not null && request.Get("check") == "keep")
            {
                stash.Put(token, value, stashPath);
            }

            response.Content = value is null ? "0" : "1";
            return;
        }

        if (request.Method == "OPTIONS")
        {
            if (request.Header("Access-Control-Request-Method") is not { } method)
            {
                // response.set_error(400, ...) is replaced by the returned body: the status stays
                response.Status = 400;
                response.Content = "ERROR: No access-control-request-method in preflight!";
                return;
            }

            response.Set("Access-Control-Allow-Methods", method);
            if (request.Has("max_age"))
            {
                response.Set("Access-Control-Max-Age", request.Get("max_age"));
            }

            if (request.Has("token"))
            {
                stash.Put(request.Get("token")!, "1", stashPath);
            }
        }

        if (!partitioned)
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }

        response.Set("Access-Control-Allow-Headers", "x-print");
        response.Content = request.Header("x-print") ?? "NO";
    }

    // cors/resources/status.py: the status and the status message of the query (or of the preflight)
    private static void CorsStatus(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Expose-Headers", "X-Request-Method");
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Methods", "GET, CHICKEN, HEAD, POST, PUT");
        }

        if (request.Has("headers"))
        {
            response.Set("Access-Control-Allow-Headers", request.Get("headers"));
        }

        response.Set("X-Request-Method", request.Method);
        response.Set("X-A-C-Request-Method", request.Header("Access-Control-Request-Method") ?? "");
        var code = ParseInt(request.Get("code"), 200);
        if (request.Method == "OPTIONS" && request.Has("preflight"))
        {
            code = int.Parse(request.Get("preflight")!, CultureInfo.InvariantCulture);
        }

        response.Status = code;
        response.StatusText = request.Get("text", "OMG");
        if (request.Has("type"))
        {
            response.Set("Content-Type", request.Get("type"));
        }

        response.Content = request.Get("content", "")!;
    }

    // cors/resources/304.py: the odd requests of a pair are fresh responses with an ETag, and the even ones are 304 responses
    // whose CORS headers differ; the content-language header of the request correlates them
    private static void CorsNotModified(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        const string StateHeader = "content-language";
        const string ETag = "abcdef";
        var inm = request.Header("if-none-match");
        if (request.Header(StateHeader) is not { } rawRequestNumber)
        {
            Error("no req_num header in request");
            return;
        }

        var requestNumber = int.Parse(rawRequestNumber, CultureInfo.InvariantCulture);
        if (requestNumber > 8)
        {
            Error($"req_num {requestNumber} out of range");
            return;
        }

        if (requestNumber % 2 == 1)
        {
            if (!string.IsNullOrEmpty(inm))
            {
                Error("If-None-Match on first request");
                return;
            }

            AddStateHeaders();
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Content-Type", "text/plain");
            response.Set("Cache-Control", "private, max-age=3, must-revalidate");
            response.Set("ETag", ETag);
            response.Content = "Success";
            return;
        }

        if (inm != ETag)
        {
            Error(inm is null ? "If-None-Match missing" : "If-None-Match '%s' mismatches");
            return;
        }

        // The headers of the returned list are set one by one: the last Access-Control-Expose-Headers wins
        AddStateHeaders();
        switch (requestNumber)
        {
            case 4:
                response.Set("Access-Control-Expose-Headers", "a, b");
                break;
            case 6:
                response.Set("Access-Control-Expose-Headers", "a");
                break;
            case 8:
                response.Set("Access-Control-Allow-Origin", "other.origin.example:80");
                break;
        }

        response.Status = 304;
        response.StatusText = "Not Modified";

        void AddStateHeaders()
        {
            var value = requestNumber.ToString(CultureInfo.InvariantCulture);
            response.Set("Access-Control-Expose-Headers", StateHeader);
            response.Set(StateHeader, value);
            response.Set("A", value);
            response.Set("B", value);
        }

        void Error(string message)
        {
            response.Status = 299;
            response.StatusText = "Client Error";
            response.Set("content-type", "text/plain");
            response.Set("access-control-allow-origin", "*");
            response.Set("access-control-expose-headers", StateHeader);
            response.Set("cache-control", "no-store");
            response.Content = message;
        }
    }

    // cors/resources/cache-304.py: a script with an ETag, and a 304 response without CORS headers for its revalidation
    private static void CorsCacheNotModified(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Header("If-None-Match") == "mybestscript-v1")
        {
            response.Status = 304;
            response.StatusText = "YEP";
            return;
        }

        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Cache-Control", "must-revalidate");
        response.Set("ETag", "mybestscript-v1");
        response.Set("Content-Type", "text/javascript");
        response.Content = "function hep() { }";
    }

    // cors/resources/expose-headers.py: a raw response whose last header lines are the expose parameter (it can contain
    // line breaks, i.e. several headers)
    private static void CorsExposeHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Status = 221;
        response.StatusText = "ALL YOUR BASE BELONG TO H1";
        response.Streamed = true;
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Connection", "close");
        response.Add("BB-8", "hey");
        response.Add("Content-Language", "mkay");
        foreach (var line in (request.Get("expose") ?? throw new InvalidOperationException("The expose parameter is missing")).Split("\r\n"))
        {
            // The whitespace around the value is not part of the value (the HTTP parser of the browser strips it)
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                response.Add(line[..colon], line[(colon + 1)..].Trim(' ', '\t'));
            }
        }
    }
}
