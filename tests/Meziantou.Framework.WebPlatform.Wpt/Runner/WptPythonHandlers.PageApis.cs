using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>document-policy</c> and <c>page-lifecycle</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> PageApisHandlers = new(StringComparer.Ordinal)
    {
        // echo-policy.py: echoes the Sec-Required-Document-Policy header of the request in the body (a message posted to the
        // top-level window) and in the Document-Policy header of the response
        ["/document-policy/echo-policy.py"] = (request, response, _) =>
        {
            var message = new Dictionary<string, string>(StringComparer.Ordinal);
            var requiredPolicy = request.Header("Sec-Required-Document-Policy");
            response.Add("Content-Type", "text/html");
            if (!string.IsNullOrEmpty(requiredPolicy))
            {
                message["requiredPolicy"] = requiredPolicy;
                response.Add("Document-Policy", requiredPolicy);
            }

            if (request.Get("id") is { Length: > 0 } frameId)
            {
                message["id"] = frameId;
            }

            response.StatusText = "OK";
            response.Content = "<!DOCTYPE html>\n<script>\ntop.postMessage(" + JsonSerializer.Serialize(message) + ", \"*\");\n</script>\n" + (requiredPolicy ?? "None") + "\n";
        },

        // beacon.py: counts the requests of a token in the stash; a query returns the count in the Count header
        ["/page-lifecycle/resources/beacon.py"] = (request, response, stash) =>
        {
            var token = request.Get("token") ?? "";
            var isQuery = request.Has("query");
            lock (stash)
            {
                var count = stash.TakeValue(token, request.Path) is int value ? value : 0;
                if (!isQuery)
                {
                    count++;
                }

                stash.Put(token, count, request.Path);
                if (isQuery)
                {
                    response.Add("Count", count.ToString(CultureInfo.InvariantCulture));
                }
            }
        },
    };
}
