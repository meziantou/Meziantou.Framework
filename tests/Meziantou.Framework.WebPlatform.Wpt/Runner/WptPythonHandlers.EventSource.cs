using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>eventsource</c> tests (<c>eventsource/resources/*.py</c>): event streams.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> EventSourceHandlers = new(StringComparer.Ordinal)
    {
        ["/eventsource/resources/cors.py"] = EventSourceCors,
        ["/eventsource/resources/cors-cookie.py"] = EventSourceCorsCookie,
        ["/eventsource/resources/last-event-id.py"] = (request, response, _) =>
        {
            // The identifier of the Last-Event-ID header, or an event with an identifier (U+2026 by default, in UTF-8)
            response.Set("Content-Type", "text/event-stream");
            response.Content = request.Header("Last-Event-ID") is { Length: > 0 } lastEventId
                ? "data: " + lastEventId + "\n\n"
                : "id: " + (request.Get("idvalue") ?? "\U000000E2\U00000080\U000000A6") + "\nretry: 200\ndata: hello\n\n";
        },
        ["/eventsource/resources/last-event-id2.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/event-stream");
            response.Content = (int.TryParse(request.Get("type", "1"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var type) ? type : 1) switch
            {
                // The identifier persists
                1 => "id: 1\ndata: 1\n\ndata: 2\n\nid: 2\ndata:3\n\ndata:4\n\n",

                // The identifier is reset by an empty id field, with or without colon
                2 => "id: 1\ndata: 1\n\nid:\ndata:2\n\ndata:3\n\n",
                3 => "id: 1\ndata: 1\n\nid\ndata:2\n\ndata:3\n\n",
                _ => "data: invalid_test\n\n",
            };
        },
        ["/eventsource/resources/message.py"] = EventSourceMessage,
        ["/eventsource/resources/message2.py"] = (_, response, _) =>
        {
            // The events are written in a loop, every 2 seconds: the response never ends
            response.Set("Content-Type", "text/event-stream");
            response.Set("Cache-Control", "no-cache");
            response.Streamed = true;
            foreach (var chunk in (string[])["data:msg", "\n", "data: msg", "\n\n", ":", "\n", "falsefield:msg", "\n\n", "falsefield:msg", "\n", "Data:data", "\n\n", "data", "\n\n", "data:end", "\n\n"])
            {
                response.Chunks.Add((TimeSpan.Zero, Encoding.Latin1.GetBytes(chunk)));
            }

            response.ChunksRepeatAfter = TimeSpan.FromSeconds(2);
        },
        ["/eventsource/resources/reconnect-fail.py"] = (request, response, _) =>
        {
            // The state of the connection is a cookie: opened, reconnected, then closed (a 204 response)
            var name = "recon_fail_" + request.Get("id");
            response.Set("Content-Type", "text/event-stream");
            switch (ParseRequestCookies(request).Find(cookie => cookie.Key == name).Value)
            {
                case "opened":
                    response.StatusText = "RECONNECT";
                    SetCookie(response, name, "reconnected");
                    response.Content = "data: reconnected\n\n";
                    break;

                case "reconnected":
                    response.Status = 204;
                    response.StatusText = "NO CONTENT (CLOSE)";
                    DeleteCookie(response, name);
                    response.Content = "data: closed\n\n";
                    break;

                default:
                    response.StatusText = "OPEN";
                    SetCookie(response, name, "opened");
                    response.Content = "retry: 2\ndata: opened\n\n";
                    break;
            }
        },
        ["/eventsource/resources/status-error.py"] = (request, response, _) =>
        {
            var status = request.Get("status") ?? "404";
            response.Status = int.Parse(status, System.Globalization.CultureInfo.InvariantCulture);
            response.StatusText = "HAHAHAHA";
            response.Set("Content-Type", "text/event-stream");
            response.Content = status is "204" or "205" ? "" : "data: data\n\n";
        },
        ["/eventsource/resources/status-reconnect.py"] = EventSourceStatusReconnect,
    };

    /// <summary><c>response.set_cookie(name, value)</c> of wptserve: a cookie of the path <c>/</c>.</summary>
    private static void SetCookie(WptHandlerResponse response, string name, string value, string? expires = null)
        => response.Add("Set-Cookie", name + "=" + value + (expires is null ? "" : "; expires=" + expires) + "; Path=/");

    /// <summary><c>response.delete_cookie(name)</c> of wptserve: the cookie expires.</summary>
    private static void DeleteCookie(WptHandlerResponse response, string name) => response.Add("Set-Cookie", name + "=; Max-Age=0; Path=/");

    // eventsource/resources/message.py: an event stream with the message and the MIME type of the query
    private static void EventSourceMessage(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var newline = request.Get("newline") == "none" ? "" : "\n\n";
        response.Delay = Milliseconds(request.Get("sleep"), 0);
        response.Set("Content-Type", request.Get("mime", "text/event-stream"));
        response.Content = request.Get("message", "data: data") + newline + "\n";
    }

    // eventsource/resources/status-reconnect.py: a response with the status of the query, then (the cookie of the first
    // response is sent back) a 200 response
    private static void EventSourceStatusReconnect(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var statusCode = request.Get("status") ?? "204";
        var cookieName = "request" + request.Get("id", statusCode);
        response.Set("Content-Type", "text/event-stream");
        if (ParseRequestCookies(request).Find(cookie => cookie.Key == cookieName).Value == statusCode)
        {
            DeleteCookie(response, cookieName);
            response.Content = "data: data\n\n";
        }
        else
        {
            SetCookie(response, cookieName, statusCode);
            response.Status = int.Parse(statusCode, System.Globalization.CultureInfo.InvariantCulture);
            response.StatusText = "TEST";
            response.Content = "retry: 2\n" + (request.Has("ok_first") ? "data: ok\n\n" : "");
        }
    }

    // eventsource/resources/cors.py: the CORS headers of the query, and the response of another handler
    private static void EventSourceCors(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        // request.headers[b"origin"] raises a KeyError when the request has no Origin header
        var requestOrigin = request.Header("Origin") ?? throw new InvalidOperationException("Missing Origin header");
        response.Set("Access-Control-Allow-Origin", request.Get("origin", requestOrigin));
        response.Set("Access-Control-Allow-Credentials", request.Get("credentials", "true"));
        switch (request.Get("run"))
        {
            case "cache-control":
                // cache-control.event_stream with the substitutions: the Cache-Control header of the request
                response.Set("Content-Type", "text/event-stream");
                response.Body = WptServer.Substitute(ReadWptFile("eventsource/resources/cache-control.event_stream"), request.Url, request.Headers);
                break;

            case "redirect":
                Handlers["/common/redirect.py"](request, response, stash);
                break;

            case "status-reconnect":
                EventSourceStatusReconnect(request, response, stash);
                break;

            case "message":
                EventSourceMessage(request, response, stash);
                break;
        }
    }

    // eventsource/resources/cors-cookie.py: the events tell whether the request had the cookie set by the previous response
    private static void EventSourceCorsCookie(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var lastEventId = request.Header("Last-Event-Id") ?? "";
        var ident = request.Get("ident") ?? "test";
        var cookie = ParseRequestCookies(request).Exists(item => item.Key == ident) ? "COOKIE" : "NO_COOKIE";
        var requestOrigin = request.Header("Origin") ?? throw new InvalidOperationException("Missing Origin header");
        var origin = request.Get("origin", requestOrigin);
        var credentials = request.Get("credentials", "true");
        if (origin != "none")
        {
            response.Add("Access-Control-Allow-Origin", origin);
        }

        if (credentials != "none")
        {
            response.Add("Access-Control-Allow-Credentials", credentials);
        }

        switch (lastEventId)
        {
            case "":
                response.Add("Content-Type", "text/event-stream");
                SetCookie(response, ident, "COOKIE");
                response.Content = "id: 1\nretry: 200\ndata: first " + cookie + "\n\n";
                break;

            case "1":
                // The cookie expired a long time ago
                response.Add("Content-Type", "text/event-stream");
                SetCookie(response, ident, "COOKIE", expires: "Fri, 27 Jul 2001 00:00:00 GMT");
                response.Content = "id: 2\ndata: second " + cookie + "\n\n";
                break;

            default:
                response.Add("Content-Type", "stop");
                response.Content = "data: " + lastEventId + cookie + "\n\n";
                break;
        }
    }
}
