using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>fetch/metadata</c> tests, which record or echo the Fetch Metadata headers of the requests.</summary>
internal static partial class WptPythonHandlers
{
    private const string FetchMetadataStashPath = "/fetch/metadata/resources";

    private static readonly Dictionary<string, Handler> FetchMetadataHandlers = new(StringComparer.Ordinal)
    {
        ["/fetch/metadata/resources/record-headers.py"] = RecordHeaders,
        ["/fetch/metadata/resources/record-header.py"] = RecordHeader,

        // header-link.py: an empty document with the Link header of the location and rel parameters
        ["/fetch/metadata/resources/header-link.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("Link", "<" + request.Get("location") + ">; rel=" + request.Get("rel"));
        },

        // post-to-owner.py: posts the Fetch Metadata headers to the opener and to the top-level window
        ["/fetch/metadata/resources/post-to-owner.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/html");
            response.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            response.Content = "<!DOCTYPE html>\n<script>\n    var data = " + FetchMetadataJson(request) + ";\n    if (window.opener)\n        window.opener.postMessage(data, \"*\");\n    if (window.top != window)\n        window.top.postMessage(data, \"*\");\n</script>\n";
        },

        // echo-as-json.py: the Fetch Metadata headers as JSON (a preflight must have Sec-Fetch-Mode: cors)
        ["/fetch/metadata/resources/echo-as-json.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/json");
            response.Add("Access-Control-Allow-Credentials", "true");
            if (request.Header("Origin") is { } origin)
            {
                response.Add("Access-Control-Allow-Origin", origin);
            }

            if (request.Method == "OPTIONS")
            {
                if (request.Header("Sec-Fetch-Mode") != "cors")
                {
                    response.Status = 403;
                    response.StatusText = "Failed";
                    return;
                }

                response.Add("Access-Control-Allow-Methods", "*");
                response.Add("Access-Control-Allow-Headers", "*");
                return;
            }

            response.Content = FetchMetadataJson(request);
        },

        // echo-as-script.py: a script declaring the Fetch Metadata headers in the header variable
        ["/fetch/metadata/resources/echo-as-script.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/javascript");
            response.Content = "var header = " + FetchMetadataJson(request) + ";";
        },
    };

    /// <summary>The Fetch Metadata headers of a request as JSON (json.dumps() of the dest, mode, site and user members).</summary>
    private static string FetchMetadataJson(WptHandlerRequest request)
        => "{\"dest\": " + PythonJsonString(request.Header("Sec-Fetch-Dest") ?? "")
        + ", \"mode\": " + PythonJsonString(request.Header("Sec-Fetch-Mode") ?? "")
        + ", \"site\": " + PythonJsonString(request.Header("Sec-Fetch-Site") ?? "")
        + ", \"user\": " + PythonJsonString(request.Header("Sec-Fetch-User") ?? "") + "}";

    // fetch/metadata/resources/record-headers.py: stores the headers of the request (a JSON object whose values are the
    // lists of the values of the headers, as wptserve's request.headers), or returns them (retrieve: 204 when nothing is
    // recorded). The response is the body parameter, with the Content-Type of the mime parameter.
    private static void RecordHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Has("requireOPTIONS") && request.Method != "OPTIONS")
            return;

        var key = request.Get("key") ?? GetPost(request, "key") ?? "";
        if (request.Has("retrieve"))
        {
            if (stash.TakeValue(key, FetchMetadataStashPath) is not string recorded)
            {
                response.Status = 204;
                return;
            }

            response.Content = recorded;
            return;
        }

        var json = new StringBuilder("{");
        foreach (var group in request.Headers.GroupBy(header => header.Key.ToLowerInvariant(), StringComparer.Ordinal))
        {
            if (json.Length > 1)
            {
                json.Append(", ");
            }

            json.Append(PythonJsonString(group.Key)).Append(": [").AppendJoin(", ", group.Select(header => PythonJsonString(DecodeUtf8(header.Value)))).Append(']');
        }

        stash.Put(key, json.Append('}').ToString(), FetchMetadataStashPath);
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Cache-Control", "no-cache, no-store, must-revalidate");
        response.Set("Pragma", "no-cache");
        response.Set("Expires", "0");
        if (request.Get("mime") is { } mime)
        {
            response.Set("Content-Type", mime);
        }

        response.Content = request.Get("body") ?? GetPost(request, "body") ?? "";
    }

    // fetch/metadata/resources/record-header.py: stores the Fetch Metadata headers of the request (file parameter), or
    // returns them (retrieve). The response is a valid resource of the type given by the prefix of the file parameter.
    private static void RecordHeader(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var key = request.Get("file") ?? GetPost(request, "file") ?? "";
        if (request.Has("retrieve"))
        {
            response.Set("Connection", "close");
            response.Content = stash.TakeValue(key, FetchMetadataStashPath) as string ?? "No header has been recorded";
            return;
        }

        stash.Put(key, FetchMetadataJson(request), FetchMetadataStashPath);
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Cache-Control", "no-cache, no-store, must-revalidate");
        response.Set("Pragma", "no-cache");
        response.Set("Expires", "0");
        (string? ContentType, Func<byte[]> Body)? resource = key switch
        {
            _ when key.StartsWith("serviceworker", StringComparison.Ordinal) => ("application/javascript", () => []),
            _ when key.StartsWith("image", StringComparison.Ordinal) => ("image/png", () => ReadWptFile("media/1x1-green.png")),
            _ when key.StartsWith("track", StringComparison.Ordinal) => (null, () => "WEBVTT"u8.ToArray()),
            _ when key.StartsWith("sharedworker", StringComparison.Ordinal) => ("application/javascript", () => ReadWptFile("fetch/metadata/resources/sharedWorker.js")),
            _ when key.StartsWith("font", StringComparison.Ordinal) => ("application/x-font-ttf", () => ReadWptFile("fonts/Ahem.ttf")),
            _ when key.StartsWith("audio", StringComparison.Ordinal) => ("audio/mpeg", () => ReadWptFile("media/sound_5.mp3")),
            _ when key.StartsWith("video", StringComparison.Ordinal) => ("video/mp4", () => ReadWptFile("media/A4.mp4")),
            _ when key.StartsWith("style", StringComparison.Ordinal) => ("text/css", () => "div { }"u8.ToArray()),
            _ when key.StartsWith("embed", StringComparison.Ordinal) || key.StartsWith("object", StringComparison.Ordinal) => ("text/html", () => "<html>EMBED!</html>"u8.ToArray()),
            _ when key.StartsWith("redirect", StringComparison.Ordinal) => ("image/jpeg", () => ReadWptFile("media/1x1-green.png")),
            _ when key.StartsWith("text", StringComparison.Ordinal) => ("text/plain", () => []),
            _ when key.StartsWith("worker", StringComparison.Ordinal) => ("application/javascript", () => "self.postMessage('loaded');"u8.ToArray()),
            _ when key.StartsWith("worklet", StringComparison.Ordinal) => ("application/javascript", () => []),
            _ when key.StartsWith("script", StringComparison.Ordinal) => ("application/javascript", () => "void 0;"u8.ToArray()),
            _ => null,
        };

        if (resource is var (contentType, body))
        {
            response.Set("Content-Type", contentType);
            response.Body = TryRead(body);
        }

        static byte[] TryRead(Func<byte[]> body)
        {
            try
            {
                return body();
            }
            catch (IOException)
            {
                // The media files are not fetched
                return [];
            }
        }
    }
}
