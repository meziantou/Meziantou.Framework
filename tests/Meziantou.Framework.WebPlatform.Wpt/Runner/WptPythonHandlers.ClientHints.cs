using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the <c>client-hints</c> tests, which echo the client hints of the requests (the <c>Sec-CH-*</c> headers
/// and the legacy ones) or opt into them (<c>Accept-CH</c>, <c>Critical-CH</c>, <c>Clear-Site-Data</c>).
/// </summary>
internal static partial class WptPythonHandlers
{
    /// <summary><c>client_hints_list()</c> of <c>client-hints/resources/clienthintslist.py</c>.</summary>
    private static readonly string[] ClientHintsList =
    [
        "device-memory", "dpr", "viewport-width", "rtt", "downlink", "ect", "sec-ch-ua", "sec-ch-ua-arch", "sec-ch-ua-platform",
        "sec-ch-ua-model", "sec-ch-ua-mobile", "sec-ch-ua-full-version", "sec-ch-ua-platform-version", "sec-ch-prefers-color-scheme",
        "sec-ch-prefers-reduced-motion", "sec-ch-ua-bitness", "sec-ch-viewport-height", "sec-ch-device-memory", "sec-ch-dpr",
        "sec-ch-viewport-width", "sec-ch-ua-full-version-list", "sec-ch-ua-wow64", "sec-ch-prefers-reduced-transparency",
    ];

    /// <summary><c>client_hints_ua_list()</c> of <c>client-hints/resources/clienthintslist.py</c>.</summary>
    private static readonly string[] ClientHintsUserAgentList =
    [
        "sec-ch-ua", "sec-ch-ua-arch", "sec-ch-ua-platform", "sec-ch-ua-platform-version", "sec-ch-ua-model", "sec-ch-ua-full-version",
        "sec-ch-ua-full-version-list", "sec-ch-ua-wow64",
    ];

    private static readonly Dictionary<string, Handler> ClientHintsHandlers = new(StringComparer.Ordinal)
    {
        // echo-client-hints-received.py: a response header per client hint of the request
        ["/client-hints/resources/echo-client-hints-received.py"] = (request, response, _) =>
        {
            AddClientHintsCorsHeaders(response);
            EchoClientHint(request, response, "sec-ch-device-memory", "device-memory-received");
            EchoClientHint(request, response, "device-memory", "device-memory-deprecated-received");
            EchoClientHint(request, response, "sec-ch-dpr", "dpr-received");
            EchoClientHint(request, response, "dpr", "dpr-deprecated-received");
            EchoClientHint(request, response, "sec-ch-viewport-width", "viewport-width-received");
            EchoClientHint(request, response, "viewport-width", "viewport-width-deprecated-received");
            EchoClientHint(request, response, "sec-ch-viewport-height", "viewport-height-received");
            EchoClientHint(request, response, "rtt", "rtt-received");
            EchoClientHint(request, response, "downlink", "downlink-received");
            EchoClientHint(request, response, "ect", "ect-received");
            EchoClientHint(request, response, "sec-ch-ua-mobile", "mobile-received");
            EchoClientHint(request, response, "sec-ch-prefers-color-scheme", "prefers-color-scheme-received");
            EchoClientHint(request, response, "sec-ch-prefers-reduced-motion", "prefers-reduced-motion-received");
            EchoClientHint(request, response, "sec-ch-prefers-reduced-transparency", "prefers-reduced-transparency-received");
        },
        ["/client-hints/accept-ch-stickiness/resources/echo-client-hints-received.py"] = (request, response, _) =>
        {
            AddClientHintsCorsHeaders(response);
            EchoClientHint(request, response, "sec-ch-device-memory", "device-memory-received");
            EchoClientHint(request, response, "device-memory", "device-memory-deprecated-received");
        },

        // echo-ua-client-hints-received.py: a response header per User-Agent client hint of the request
        ["/client-hints/resources/echo-ua-client-hints-received.py"] = (request, response, _) =>
        {
            AddClientHintsCorsHeaders(response);
            foreach (var header in ClientHintsUserAgentList)
            {
                EchoClientHint(request, response, header, header + "-received");
            }
        },

        // expect-client-hints-headers-iframe.py: a document that posts PASS to its parent when the request has the hints
        // whose parameter is true, and only them
        ["/client-hints/resources/expect-client-hints-headers-iframe.py"] = (request, response, _) =>
        {
            var result = "PASS";
            var log = "";
            foreach (var hint in ClientHintsList)
            {
                var should = (request.Get(hint) ?? throw new KeyNotFoundException(hint)) == "true";
                var present = request.Header(hint) is { Length: > 0 } value ? value : null;
                var description = hint + " " + (should ? "True" : "False") + " " + (present ?? "None");
                log += description + ", ";
                if (should != (present is not null))
                {
                    result = "FAIL " + description;
                    break;
                }
            }

            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = "<script>console.log('" + log + "'); window.parent.postMessage('" + result + "', '*');</script>";
        },

        // script-set-dpr-header.py: a script that sets dprHeader to the Sec-CH-DPR header of its request (the handler formats
        // the bytes of the header in a string: b'1')
        ["/client-hints/resources/script-set-dpr-header.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Content = "dprHeader = \"" + (request.Header("sec-ch-dpr") is { } dpr ? "b'" + dpr + "'" : "") + "\";";
        },

        // sec-ch-ua.py: a document that posts the Sec-CH-UA header of its request to its opener
        ["/client-hints/resources/sec-ch-ua.py"] = (request, response, _) =>
        {
            var userAgent = request.Header("Sec-CH-UA") ?? "";
            response.Set("Content-Type", "text/html");
            response.Content = "\n<script>\n  window.opener.postMessage({ header: '" + userAgent + "' }, \"*\");\n</script>\nSec-CH-UA: " + userAgent + "\n";
        },
        ["/client-hints/resources/stale-echo-client-hints.py"] = StaleEchoClientHints,

        // viewport-frame.py: a document that posts the viewport hints of its request to its opener or its parent
        ["/client-hints/resources/viewport-frame.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = "\n<script>\n  let parentOrOpener = window.opener || window.parent;\n  parentOrOpener.postMessage({ viewportWidth: '" + (request.Header("sec-ch-viewport-width") ?? "FAIL")
                + "', viewportHeight: '" + (request.Header("sec-ch-viewport-height") ?? "FAIL") + "' }, \"*\");\n</script>\n";
        },

        // viewport.py: the viewport hints of the request ("width,height")
        ["/client-hints/resources/viewport.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = request.Header("sec-ch-viewport-width") is { } width && request.Header("sec-ch-viewport-height") is { } height ? width + "," + height : "FAIL";
        },

        // 2x3-svg-scaled-by-sec-ch-width.py: an SVG image whose size is 2 x 3 times the Sec-CH-Width header (1 x 1 without it)
        ["/client-hints/resources/2x3-svg-scaled-by-sec-ch-width.py"] = (request, response, _) =>
        {
            var width = 1;
            var height = 1;
            if (request.Header("sec-ch-width") is { } hint)
            {
                var value = int.Parse(hint, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture);
                width = 2 * value;
                height = 3 * value;
            }

            response.Set("Content-Type", "image/svg+xml");
            response.Content = "<svg\n        xmlns=\"http://www.w3.org/2000/svg\"\n        xmlns:xlink=\"http://www.w3.org/1999/xlink\"\n        width=\"" + width.ToString(CultureInfo.InvariantCulture)
                + "\"\n        height=\"" + height.ToString(CultureInfo.InvariantCulture) + "\">\n      <rect width=\"100%\" height=\"100%\" fill=\"green\" />\n    </svg>";
        },

        // accept-ch-and-redir.py: a redirect to the url parameter that opts into hints
        ["/client-hints/accept-ch-stickiness/resources/accept-ch-and-redir.py"] = (request, response, _) =>
        {
            response.Status = 301;
            response.Add("Location", request.Get("url") ?? "");
            response.Add("Accept-CH", "sec-ch-device-memory, device-memory, Sec-CH-DPR, DPR");
        },

        // do-expect-received.py, do-not-expect-received.py and expect-received.py: a document that posts to its opener or its
        // parent whether its request had the device memory hints (and the low-entropy hints, for expect-received.py)
        ["/client-hints/accept-ch-stickiness/resources/do-expect-received.py"] = (request, response, _) =>
            PostClientHintsResult(response, request.HasHeader("device-memory") && request.HasHeader("sec-ch-device-memory") ? "PASS" : "FAIL"),
        ["/client-hints/accept-ch-stickiness/resources/do-not-expect-received.py"] = (request, response, _) =>
            PostClientHintsResult(response, request.HasHeader("device-memory") || request.HasHeader("sec-ch-device-memory") ? "FAIL" : "PASS"),
        ["/client-hints/accept-ch-stickiness/resources/expect-received.py"] = (request, response, _) =>
            PostClientHintsResult(response, !request.HasHeader("sec-ch-device-memory") ? "DEVICE-MEMORY"
                : !request.HasHeader("device-memory") ? "DEVICE-MEMORY-DEPRECATED"
                : !request.HasHeader("sec-ch-ua") ? "UA"
                : !request.HasHeader("sec-ch-ua-mobile") ? "MOBILE"
                : !request.HasHeader("sec-ch-ua-platform") ? "PLATFORM"
                : "PASS"),

        // check-client-hints.py: a document that posts to its opener whether its request had the device memory hint
        ["/client-hints/clear-site-data/resources/check-client-hints.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Content = "\n        <script>\n            window.opener.postMessage(\"" + (request.HasHeader("sec-ch-device-memory") ? "HadDeviceMemory" : "MissingDeviceMemory") + "\" , \"*\");\n        </script>";
        },

        // clear-site-data-iframe.sub.py: a document with an iframe of clear-site-data.py on the main host (the target is the
        // query of its URL, not its target parameter)
        ["/client-hints/clear-site-data/resources/clear-site-data-iframe.sub.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Content = "\n        <iframe src=\"https://{{hosts[][]}}:{{ports[https][0]}}/client-hints/clear-site-data/resources/clear-site-data.py?" + (request.Get("target") ?? "*") + "\">\n        </iframe>";
        },

        // clear-site-data.py: a document with the Clear-Site-Data header of the target parameter, which navigates its
        // top-level window to check-client-hints.py when it is loaded
        ["/client-hints/clear-site-data/resources/clear-site-data.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("Clear-Site-Data", "\"" + (request.Get("target") ?? "*") + "\"");
            response.Content = "\n        <script>\n            window.onload = function() {\n                window.top.location.href = \"/client-hints/clear-site-data/resources/check-client-hints.py\";\n            };\n        </script>";
        },
        ["/client-hints/critical-ch/resources/echo-critical-hint.py"] = EchoCriticalHint,

        // redirect-critical-hint.py: a redirect to the location parameter, which asks for critical hints with the critical parameter
        ["/client-hints/critical-ch/resources/redirect-critical-hint.py"] = (request, response, _) =>
        {
            response.Status = 302;
            response.Set("Location", request.Get("location") ?? throw new KeyNotFoundException("location"));
            response.Set("Access-Control-Allow-Origin", "*");
            if (request.Has("critical"))
            {
                response.Add("Accept-CH", "sec-ch-dpr,dpr");
                response.Add("Critical-CH", "sec-ch-dpr,dpr");
            }
        },

        // was-sec-ch-device-memory-received.py: the status tells whether the request had the device memory hint
        ["/client-hints/inner-delegation/resources/was-sec-ch-device-memory-received.py"] = (request, response, _) =>
        {
            AddClientHintsCorsHeaders(response);
            response.Status = request.HasHeader("sec-ch-device-memory") ? 200 : 400;
        },

        // echo-hint-in-html.py: a document that tells whether its request had the device memory hints; the one of the
        // critical-ch directory asks for them as critical hints
        ["/client-hints/service-workers/resources/echo-hint-in-html.py"] = (request, response, _) => EchoHintInHtml(request, response, critical: false),
        ["/client-hints/service-workers/critical-ch/echo-hint-in-html.py"] = (request, response, _) => EchoHintInHtml(request, response, critical: true),
    };

    private static void AddClientHintsCorsHeaders(WptHandlerResponse response)
    {
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Access-Control-Allow-Headers", "*");
        response.Add("Access-Control-Expose-Headers", "*");
    }

    private static void EchoClientHint(WptHandlerRequest request, WptHandlerResponse response, string requestHeader, string responseHeader)
    {
        if (request.Header(requestHeader) is { } value)
        {
            response.Set(responseHeader, value);
        }
    }

    private static void PostClientHintsResult(WptHandlerResponse response, string result)
    {
        response.Add("Content-Type", "text/html");
        response.Add("Access-Control-Allow-Origin", "*");
        response.Content = "\n<script>\n  let messagee = window.opener || window.parent;\n  messagee.postMessage(\"" + result + "\" , \"*\");\n</script>\n";
    }

    private static void EchoHintInHtml(WptHandlerRequest request, WptHandlerResponse response, bool critical)
    {
        response.Add("Content-Type", "text/html; charset=UTF-8");
        AddClientHintsCorsHeaders(response);
        response.Add("Cache-Control", "no-store");
        if (critical)
        {
            response.Add("Accept-CH", "sec-ch-device-memory,device-memory");
            response.Add("Critical-CH", "sec-ch-device-memory,device-memory");
        }

        response.Content = request.HasHeader("sec-ch-device-memory") && request.HasHeader("device-memory") ? "PASS" : "FAIL";
    }

    // client-hints/critical-ch/resources/echo-critical-hint.py: a document that opts into the device memory hints and asks
    // for them as critical hints (the viewport width hints with the mismatch parameter; one header per hint with the
    // multiple parameter), and posts to its opener or its top-level window whether its request had them (the number of
    // requests of the token parameter instead, when there is one)
    private static void EchoCriticalHint(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("Content-Type", "text/html; charset=UTF-8");
        AddClientHintsCorsHeaders(response);
        var multiple = request.Has("multiple");
        AddHints("Accept-CH", "sec-ch-device-memory,device-memory");
        AddHints("Critical-CH", request.Has("mismatch") ? "sec-ch-viewport-width,viewport-width" : "sec-ch-device-memory,device-memory");
        response.Add("Cache-Control", "no-store");

        var result = request.HasHeader("sec-ch-device-memory") && request.HasHeader("device-memory") ? "PASS" : "FAIL";
        if (request.Get("token") is { } token)
        {
            var count = stash.TakeValue(token) is int previous ? previous + 1 : 1;
            stash.Put(token, count);
            result = count.ToString(CultureInfo.InvariantCulture);
        }

        if (request.HasHeader("sec-ch-viewport-width") && request.HasHeader("viewport-width"))
        {
            result = "MISMATCH";
        }

        response.Content = "<script>(window.opener || window.top).postMessage('" + result + "', '*')</script>";

        void AddHints(string name, string value)
        {
            foreach (var part in multiple ? value.Split(',') : [value])
            {
                response.Add(name, part);
            }
        }
    }

    // client-hints/resources/stale-echo-client-hints.py: echoes the client hints of the request and the ones of the
    // previous request of the token (the "-recieved" and "-previous" headers), and counts the requests of the token: a
    // cacheable script with a unique id (stale-while-revalidate), or the count with the query parameter
    private static void StaleEchoClientHints(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        string[] hints = [.. ClientHintsList, "width", "sec-ch-width"];
        var current = hints.ToDictionary(hint => hint, request.Header, StringComparer.Ordinal);
        var token = request.Get("token") ?? "";
        var isQuery = request.Has("query");
        var count = 0;
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (stash.TakeValue(token) is ValueTuple<int, Dictionary<string, string?>> stored)
        {
            (count, previous) = stored;
        }

        if (isQuery)
        {
            if (count < 2)
            {
                stash.Put(token, (count, current));
            }
        }
        else
        {
            count++;
            stash.Put(token, (count, current));
        }

        foreach (var hint in hints)
        {
            if (current[hint] is { } value)
            {
                response.Set(hint + "-recieved", value);
            }

            if (previous.GetValueOrDefault(hint) is { } previousValue)
            {
                response.Set(hint + "-previous", previousValue);
            }
        }

        if (isQuery)
        {
            response.Add("Count", count.ToString(CultureInfo.InvariantCulture));
            return;
        }

        // 20 random lowercase letters
        var uniqueId = string.Create(20, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = (char)('a' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(26));
            }
        });
        response.Add("Content-Type", "text/html");
        response.Add("Cache-Control", "private, max-age=0, stale-while-revalidate=60");
        response.Add("Unique-Id", uniqueId);
        response.Content = "report('" + uniqueId + "')";
    }
}
