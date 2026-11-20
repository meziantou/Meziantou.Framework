using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the <c>payment-method-manifest</c> tests: the payment method identifier, the payment method manifest and
/// the web app manifest log the requests of the browser in the stash, and <c>stash-query.py</c> returns them.
/// </summary>
internal static partial class WptPythonHandlers
{
    private const string PaymentManifestStashPath = "/payment-method-manifest/resources/";

    private static readonly Dictionary<string, Handler> PaymentsHandlers = new(StringComparer.Ordinal)
    {
        // payment-method-identifier.py: redirects (num_redirects hops, or to redirect_location), or responds with the Link
        // headers of the link parameters (by default, a link to payment-method-manifest.py; "none" omits it)
        ["/payment-method-manifest/resources/payment-method-identifier.py"] = (request, response, stash) =>
        {
            if (!LogPaymentManifestRequest(request, response, stash, "payment-method-identifier", out var id))
                return;

            var redirectStatus = ParseInt(request.Get("redirect_status"), 302);
            if (request.Get("num_redirects") is { } numRedirects && int.Parse(numRedirects, System.Globalization.CultureInfo.InvariantCulture) is > 0 and var remaining)
            {
                var query = string.Join('&', request.GetParameters.Select(parameter => Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Key == "num_redirects" ? (remaining - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : parameter.Value)));
                response.Status = redirectStatus;
                response.Set("Location", request.Url.Origin + request.Url.Pathname + "?" + query);
                response.Content = "Redirecting...";
                return;
            }

            if (request.Get("redirect_location") is { Length: > 0 } redirectLocation)
            {
                response.Status = redirectStatus;
                response.Set("Location", redirectLocation);
                response.Content = "Redirecting...";
                return;
            }

            response.Status = ParseInt(request.Get("status"), 200);
            response.Set("Content-Type", "text/html");
            var links = request.GetParameters.Where(parameter => parameter.Key == "link").Select(parameter => parameter.Value).ToList();
            if (links.Count == 0)
            {
                response.Set("Link", $"</payment-method-manifest/resources/payment-method-manifest.py?id={id}>; rel=\"payment-method-manifest\"");
            }

            foreach (var link in links)
            {
                if (link != "none")
                {
                    response.Add("Link", link);
                }
            }

            response.Content = "<!DOCTYPE html><html><body>PMI Endpoint</body></html>";
        },

        // payment-method-manifest.py: by default, a manifest whose supported origin is the origin of the request
        ["/payment-method-manifest/resources/payment-method-manifest.py"] = (request, response, stash) =>
        {
            if (!LogPaymentManifestRequest(request, response, stash, "payment-method-manifest", out _) || RedirectPaymentManifestRequest(request, response, "Redirecting manifest..."))
                return;

            response.Status = ParseInt(request.Get("status"), 200);
            response.Set("Content-Type", request.Get("content_type") ?? "application/json");
            response.Content = request.Get("body") ?? new JsonObject { ["supported_origins"] = new JsonArray(request.Url.Origin) }.ToJsonString();
        },

        // web-app-manifest.py: by default, the manifest of a payment application with a service worker
        ["/payment-method-manifest/resources/web-app-manifest.py"] = (request, response, stash) =>
        {
            if (!LogPaymentManifestRequest(request, response, stash, "web-app-manifest", out _) || RedirectPaymentManifestRequest(request, response, "Redirecting web app manifest..."))
                return;

            response.Status = ParseInt(request.Get("status"), 200);
            response.Set("Content-Type", request.Get("content_type") ?? "application/manifest+json");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Content = request.Get("body") ?? """{"name": "Default Test Payment App", "icons": [{"src": "/images/rgrg-256x256.png", "sizes": "256x256", "type": "image/png"}], "serviceworker": {"src": "app-sw.js", "scope": "./"}}""";
        },

        // stash-query.py: the requests logged for the id (kept in the stash)
        ["/payment-method-manifest/resources/stash-query.py"] = (request, response, stash) =>
        {
            response.Set("Content-Type", "application/json");
            response.Set("Access-Control-Allow-Origin", "*");
            if (request.Get("id") is not { Length: > 0 } id)
            {
                response.Status = 400;
                response.Content = """{"error": "missing id"}""";
                return;
            }

            lock (stash)
            {
                var logs = stash.TakeValue(id, PaymentManifestStashPath) as JsonArray ?? [];
                if (logs.Count > 0)
                {
                    stash.Put(id, logs, PaymentManifestStashPath);
                }

                response.Content = logs.ToJsonString();
            }
        },
    };

    /// <summary>
    /// Logs a request of the payment method manifest handlers in the stash (its endpoint, method, URL and headers, whose names
    /// are in lower case and whose values are joined), or responds with a <c>400</c> response without the id parameter.
    /// </summary>
    private static bool LogPaymentManifestRequest(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, string endpoint, out string id)
    {
        id = request.Get("id") ?? "";
        if (id.Length == 0)
        {
            response.Status = 400;
            response.Set("Content-Type", "text/plain");
            response.Content = "Missing required 'id' query parameter";
            return false;
        }

        var headers = new JsonObject();
        foreach (var group in request.Headers.GroupBy(header => header.Key.ToLowerInvariant(), StringComparer.Ordinal))
        {
            headers[group.Key] = string.Join(", ", group.Select(header => header.Value));
        }

        lock (stash)
        {
            var logs = stash.TakeValue(id, PaymentManifestStashPath) as JsonArray ?? [];
            logs.Add(new JsonObject
            {
                ["endpoint"] = endpoint,
                ["step"] = endpoint,
                ["method"] = request.Method,
                ["url"] = request.Url.Href,
                ["headers"] = headers,
            });
            stash.Put(id, logs, PaymentManifestStashPath);
        }

        return true;
    }

    /// <summary>The redirect_location parameter of the manifest handlers: a redirect response.</summary>
    private static bool RedirectPaymentManifestRequest(WptHandlerRequest request, WptHandlerResponse response, string content)
    {
        if (request.Get("redirect_location") is not { Length: > 0 } location)
            return false;

        response.Status = ParseInt(request.Get("redirect_status"), 302);
        response.Set("Location", location);
        response.Content = content;
        return true;
    }
}
