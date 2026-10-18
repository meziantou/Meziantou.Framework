using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>beacon</c> tests, which record the requests of <c>navigator.sendBeacon()</c> in the stash.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> BeaconHandlers = new(StringComparer.Ordinal)
    {
        ["/beacon/resources/beacon.py"] = Beacon,

        // content-type-and-body.py: records the Content-Type and the body of the request (put), and returns them (get)
        ["/beacon/resources/content-type-and-body.py"] = (request, response, stash) =>
        {
            var command = request.Get("cmd")?.ToLowerInvariant();
            var id = request.Get("id") ?? "";
            if (command == "put")
            {
                stash.Put(id, (request.Header("Content-Type") ?? "") + ": " + Encoding.Latin1.GetString(request.Body), request.Path);
                TextResponse(response, "");
                return;
            }

            if (command == "get" && stash.TakeValue(id, request.Path) is string stashed)
            {
                TextResponse(response, stashed);
                return;
            }

            response.Status = 400;
            response.StatusText = "Bad Command";
            response.Content = "ERROR: Bad Command!";
        },

        // inspect-header.py: records a header of the request (put), and returns it in the x-request-<header> header (get)
        ["/beacon/resources/inspect-header.py"] = (request, response, stash) =>
        {
            var command = request.Get("cmd")?.ToLowerInvariant();
            var id = request.Get("id") ?? "";
            var header = request.Get("header") ?? "";
            if (command == "put")
            {
                stash.Put(id, request.Header(header) ?? "", request.Path);
            }
            else if (command == "get")
            {
                if (stash.TakeValue(id, request.Path) is string stashed)
                {
                    response.Add("x-request-" + header, stashed);
                }
            }
            else
            {
                response.Status = 400;
                response.StatusText = "Bad Command";
                response.Content = "ERROR: Bad Command!";
                return;
            }

            response.Add("Content-Type", "text/plain");
        },

        // url-echo.py: records the raw query of the request (store), and returns it (stat)
        ["/beacon/resources/url-echo.py"] = (request, response, stash) =>
        {
            var id = request.Get("id") ?? "";
            switch (request.Get("cmd"))
            {
                case "store":
                    stash.Put(id, request.Query, request.Path);
                    break;
                case "stat":
                    TextResponse(response, stash.TakeValue(id, request.Path) as string ?? "");
                    break;
                default:
                    response.Status = 400;
                    break;
            }
        },
    };

    // beacon/resources/beacon.py: records the error and the Content-Type of a POST request (store), and returns them as a
    // JSON array (stat). It answers the CORS-preflight requests when preflightExpected is set, and records an error otherwise.
    private static void Beacon(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var id = request.Get("id") ?? "";
        var command = request.Get("cmd")?.ToLowerInvariant();
        if (request.Get("origin") is { } origin)
        {
            response.Set("Access-Control-Allow-Origin", origin);
        }

        if (request.Get("credentials") is { } credentials)
        {
            response.Set("Access-Control-Allow-Credentials", credentials);
        }

        if (command == "store")
        {
            if (request.Method == "POST")
            {
                var contentType = request.Header("Content-Type") ?? "(missing)";
                var payload = contentType.Contains("form-data", StringComparison.Ordinal) ? GetPost(request, "payload") ?? "" : Encoding.Latin1.GetString(request.Body);
                stash.Put(id, "{\"error\": " + (ValidateBeaconPayload(payload) is { } error ? PythonJsonString(error) : "null") + ", \"type\": " + PythonJsonString(contentType) + "}", request.Path);
            }
            else if (request.Method == "OPTIONS")
            {
                if (request.Has("preflightExpected"))
                {
                    response.Set("Access-Control-Allow-Headers", "content-type");
                    response.Set("Access-Control-Allow-Methods", "POST");
                }
                else
                {
                    stash.Put(id, "{\"error\": \"Preflight not expected.\"}", request.Path);
                }
            }
        }
        else if (command == "stat")
        {
            var testData = stash.TakeValue(id, request.Path) as string;
            TextResponse(response, testData is null ? "[]" : "[" + testData + "]");
        }
        else
        {
            response.Status = 400;
        }

        // The payload is "<size>:***...": its length must be <size>
        static string? ValidateBeaconPayload(string payload)
        {
            var parts = payload.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
                return null;

            var size = int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (size != payload.Length)
                return string.Create(CultureInfo.InvariantCulture, $"expected {size} characters but got {payload.Length}");

            for (var i = parts[0].Length + 1; i < payload.Length; i++)
            {
                if (payload[i] != '*')
                    return string.Create(CultureInfo.InvariantCulture, $"expected '*' at index {i} but got '{payload[i]}''");
            }

            return null;
        }
    }
}
