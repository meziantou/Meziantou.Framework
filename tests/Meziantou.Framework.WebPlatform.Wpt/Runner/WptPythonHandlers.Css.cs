using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>css</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> CssHandlers = new(StringComparer.Ordinal)
    {
        // The stash.py handlers of css-scroll-snap: a POST request stores its body by key (the result of a test window), a
        // GET request takes it (an empty response when there is none)
        ["/css/css-scroll-snap/snap-after-relayout/multiple-aligned-targets/stash.py"] = (request, response, stash) => ScrollSnapStash(request, response, stash, "/css/css-scroll-snap/snap-after-relayout/multiple-aligned-targets"),
        ["/css/css-scroll-snap/scroll-initial-target/stash.py"] = (request, response, stash) => ScrollSnapStash(request, response, stash, "/css/css-scroll-snap/scroll-initial-target/"),

        // echo-css.py: a style sheet whose content is the content parameter
        ["/css/fetching/support/echo-css.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/css");
            response.Content = request.Get("content") is { } content ? DecodeUtf8(content) : "";
        },

        // echo-headers.py: records the headers of the request by location in the stash of the token, then redirects to the
        // location; the location "echo" returns the recorded headers as JSON
        ["/css/fetching/support/echo-headers.py"] = (request, response, stash) =>
        {
            var token = request.Get("token") ?? "";
            var location = request.Get("location") ?? "";
            var store = stash.Take(token);
            if (location == "echo")
            {
                response.Content = store ?? "";
                return;
            }

            var json = store is null ? [] : JsonNode.Parse(store)?.AsObject() ?? [];

            // The names of the headers are lowercase (wptserve's RequestHeaders)
            var headers = new JsonObject();
            foreach (var name in request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
            {
                headers[name] = DecodeUtf8(request.Header(name) ?? "");
            }

            json[DecodeUtf8(location)] = headers;
            stash.Put(token, json.ToJsonString());
            response.Status = 302;
            response.Set("Location", location);
        },

        // font-referrer-policy.py: the Ahem font when the Referer header is the expected one, else an empty body
        ["/css/css-values/urls/support/font-referrer-policy.py"] = (request, response, _) =>
        {
            response.Set("access-control-allow-origin", "*");
            response.Set("cache-control", "no-cache; must-revalidate");
            if (MatchesExpectedReferrer(request))
            {
                response.Set("content-type", "font/truetype");
                response.Body = ReadWptFile("fonts/Ahem.ttf");
            }
            else
            {
                response.Set("content-type", "text/plain");
            }
        },

        // image-referrer-policy.py: a green image when the Referer header is the expected one, else a red image (a navy image
        // when the expected_referrer parameter is unknown)
        ["/css/css-values/urls/support/image-referrer-policy.py"] = (request, response, _) =>
        {
            response.Set("content-type", "image/png");
            if (request.Get("corp") is { } corp)
            {
                response.Set("cross-origin-resource-policy", corp);
            }

            if (request.Get("acao") is { } acao)
            {
                response.Set("access-control-allow-origin", acao);
            }

            response.Set("cache-control", "no-cache; must-revalidate");
            var image = request.Get("expected_referrer") is "none" or "origin" or "url" ? (MatchesExpectedReferrer(request) ? "1x1-green.png" : "1x1-red.png") : "1x1-navy.png";
            response.Body = ReadWptFile("css/css-values/urls/support/" + image);
        },

        // svg-filter-referrer-policy.py: an SVG document with a hue-rotate filter when the Referer header is the expected one,
        // else an empty body
        ["/css/css-values/urls/support/svg-filter-referrer-policy.py"] = (request, response, _) =>
        {
            response.Set("access-control-allow-origin", "*");
            response.Set("content-type", "image/svg+xml");
            response.Set("cache-control", "no-cache; must-revalidate");
            response.Content = MatchesExpectedReferrer(request)
                ? """<svg width="0" height="0" xmlns="http://www.w3.org/2000/svg" version="1.1"><defs><filter id="MyFilter" color-interpolation-filters="sRGB"><feColorMatrix type="hueRotate" values="90"/></filter></defs></svg>"""
                : "";
        },

        // stylesheet-referrer-policy.py: a style sheet that colors #test green when the Referer header is the expected one, else red
        ["/css/css-values/urls/support/stylesheet-referrer-policy.py"] = (request, response, _) =>
        {
            response.Set("access-control-allow-origin", "*");
            response.Set("content-type", "text/css");
            response.Set("cache-control", "no-cache; must-revalidate");
            response.Content = MatchesExpectedReferrer(request) ? "#test { color: green; }" : "#test { color: red; }";
        },
    };

    /// <summary>The expected_referrer parameter of the referrer-policy handlers of <c>css/css-values/urls/support</c>: none, origin or url.</summary>
    private static bool MatchesExpectedReferrer(WptHandlerRequest request)
    {
        var actual = request.Header("Referer") ?? "";
        return request.Get("expected_referrer") switch
        {
            "none" => actual.Length == 0,
            "origin" => actual == request.Get("origin"),
            "url" => actual == request.Get("url"),
            _ => false,
        };
    }

    private static void ScrollSnapStash(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, string path)
    {
        var key = request.Get("key") ?? "";
        if (request.Method == "POST")
        {
            stash.Put(key, System.Text.Encoding.Latin1.GetString(request.Body), path);
            response.Content = "ok";
        }
        else
        {
            response.Content = stash.TakeValue(key, path) as string ?? "";
        }
    }
}
