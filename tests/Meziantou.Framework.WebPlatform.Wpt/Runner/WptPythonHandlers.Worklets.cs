namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>worklets</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> WorkletsHandlers = new(StringComparer.Ordinal)
    {
        // credentials.py: a script, when the cookie of the request has the expected value (no cookie when no value is expected)
        ["/worklets/resources/credentials.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Set("Access-Control-Allow-Origin", GetSourceOrigin(request));
            response.Set("Access-Control-Allow-Credentials", "true");
            response.Status = GetCookieValue(request, "cookieName") == request.Get("value") ? 200 : 404;
        },

        // set-cookie.py: sets the cookie of the name and value parameters
        ["/worklets/resources/set-cookie.py"] = (request, response, _) =>
        {
            response.Set("Set-Cookie", request.Get("name") + "=" + request.Get("value"));
            response.Set("Access-Control-Allow-Origin", GetSourceOrigin(request));
            response.Set("Access-Control-Allow-Credentials", "true");
        },

        // referrer-checker.py: a script, when the referrer of the request (without its query) is the expected one
        ["/worklets/resources/referrer-checker.py"] = (request, response, _) =>
        {
            var referrer = (request.Header("Referer") ?? "").Split('?')[0];
            var expectedReferrer = request.Get("expected_referrer") ?? "";
            response.Set("Content-Type", "text/javascript");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Status = request.Get("referrer_policy") is "no-referrer" or "origin" or "same-origin" && referrer == expectedReferrer ? 200 : 404;
        },
    };

    /// <summary>The Origin header of the request, else the origin of its URL (a same-origin GET request has no Origin header).</summary>
    private static string GetSourceOrigin(WptHandlerRequest request) => request.Header("Origin") ?? request.Url.Origin;

    /// <summary>The value of a cookie of the request, or null.</summary>
    private static string? GetCookieValue(WptHandlerRequest request, string name)
    {
        foreach (var cookie in (request.Header("Cookie") ?? "").Split(';'))
        {
            var separator = cookie.IndexOf('=', StringComparison.Ordinal);
            if (separator >= 0 && cookie.AsSpan(0, separator).Trim().SequenceEqual(name))
                return cookie[(separator + 1)..].Trim();
        }

        return null;
    }
}
