using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The HTTP handlers of the <c>websockets</c> tests (the WebSocket handlers are in <see cref="WptWebSocketHandlers"/>).</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> WebSocketsHandlers = new(StringComparer.Ordinal)
    {
        // check.py: the Sec-WebSocket-Key header cannot be set by XMLHttpRequest (a forbidden request header)
        ["/websockets/security/check.py"] = (request, response, _) => response.Body = request.HasHeader("Sec-WebSocket-Key") ? "FAIL"u8.ToArray() : "PASS"u8.ToArray(),

        // set-cookie.py: the cookie is the percent-decoded query (isomorphic_encode(unquote(query)))
        ["/websockets/cookies/support/set-cookie.py"] = (request, response, _) =>
        {
            response.Set("Set-Cookie", Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(PythonUnquote(request.Query))));
            response.Set("Content-Type", "text/plain");
        },

        // redirect_response.py: a redirect to the echo WebSocket handler
        ["/websockets/opening-handshake/resources/redirect_response.py"] = (_, response, _) =>
        {
            response.Status = 301;
            response.StatusText = "moved";
            response.Set("Location", "/echo");
        },
    };
}
