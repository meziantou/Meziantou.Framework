using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// A WebSocket handler of pywebsocket (a <c>*_wsh.py</c> file): <c>web_socket_do_extra_handshake</c>,
/// <c>web_socket_transfer_data</c> and <c>web_socket_passive_closing_handshake</c> (which returns the status code and the
/// reason of the Close frame that acknowledges the Close frame of the browser; the default is <c>1000</c>).
/// </summary>
internal sealed record WptWebSocketHandler(
    Func<WptWebSocketRequest, Task>? DoExtraHandshake,
    Func<WptWebSocketRequest, Task> TransferData,
    Func<WptWebSocketRequest, Task<(int? Code, string? Reason)>>? PassiveClosingHandshake = null);

/// <summary>
/// The WebSocket handlers of <c>websockets/handlers</c> ported to .NET: each port mirrors the functions of its file. The path
/// of the URL selects the handler (<c>/echo</c> is <c>echo_wsh.py</c>), as the WebSocket server of wptserve does.
/// </summary>
internal static class WptWebSocketHandlers
{
    private const string GoodbyeMessage = "Goodbye";

    private static readonly Dictionary<string, WptWebSocketHandler> Handlers = new(StringComparer.Ordinal)
    {
        ["/basic_auth"] = new(BasicAuthExtraHandshake, _ => Task.CompletedTask),
        ["/delayed-passive-close"] = new(null, ReceiveOneMessageAsync, DelayedPassiveClosingHandshakeAsync),
        ["/echo-cookie"] = new(request =>
        {
            request.Properties["ws_cookie"] = request.Header("Cookie");
            return Task.CompletedTask;
        }, request =>
        {
            request.SendMessage(request.Properties["ws_cookie"] as string ?? "(none)");
            return Task.CompletedTask;
        }),
        ["/echo-query_v13"] = new(null, request =>
        {
            // request.unparsed_uri.split('?')[1]: the URL must have a query
            var parts = request.Uri.Split('?');
            request.SendMessage(parts.Length > 1 ? parts[1] : throw new InvalidOperationException("list index out of range"));
            return Task.CompletedTask;
        }),
        ["/echo-query"] = new(null, request =>
        {
            var index = request.Uri.IndexOf('?', StringComparison.Ordinal);
            request.SendMessage(index >= 0 ? request.Uri[(index + 1)..] : throw new InvalidOperationException("list index out of range"));
            return Task.CompletedTask;
        }),
        ["/echo_close_data"] = new(null, ReceiveUntilGoodbyeAsync),
        ["/echo_exit"] = new(null, ReceiveUntilGoodbyeAsync),
        ["/echo_raw"] = new(null, EchoRawAsync),
        ["/echo"] = new(request =>
        {
            if (request.WsRequestedProtocols is { } protocols && protocols.Contains("echo", StringComparer.Ordinal))
            {
                request.WsProtocol = "echo";
            }

            return Task.CompletedTask;
        }, EchoAsync, EchoPassiveClosingHandshakeAsync),
        ["/empty-message"] = new(null, async request =>
        {
            var line = await request.ReceiveMessageAsync().ConfigureAwait(false);
            request.SendMessage(line is "" ? "pass" : "fail");
        }),
        ["/handshake_no_extensions"] = new(null, _ => Task.CompletedTask),
        ["/handshake_no_protocol"] = new(request =>
        {
            request.WsRequestedProtocols = null;
            return Task.CompletedTask;
        }, _ => Task.CompletedTask),
        ["/handshake_protocol"] = new(request =>
        {
            request.WsProtocol = "foobar";
            return Task.CompletedTask;
        }, _ => Task.CompletedTask),
        ["/handshake_sleep_2"] = new(request => request.SleepAsync(2), _ => Task.CompletedTask),
        ["/invalid"] = new(request =>
        {
            request.Write("FOO BAR BAZ\r\n\r\n"u8);
            return Task.CompletedTask;
        }, _ => Task.CompletedTask),
        ["/origin"] = new(null, request =>
        {
            request.SendMessage(request.WsOrigin!);
            return Task.CompletedTask;
        }),
        ["/passive-close-abort"] = new(null, async request =>
        {
            while (await request.ReceiveMessageAsync().ConfigureAwait(false) is not null)
            {
            }
        }, _ => throw new WptWebSocketAbortException()),
        ["/protocol_array"] = new(request =>
        {
            if (request.Header("Sec-WebSocket-Protocol") is { Length: > 0 } line)
            {
                request.WsProtocol = line.Split(',', 2)[0];
            }

            return Task.CompletedTask;
        }, request =>
        {
            request.SendMessage(request.WsProtocol ?? "");
            return Task.CompletedTask;
        }),
        ["/protocol"] = new(request =>
        {
            request.WsProtocol = request.Header("Sec-WebSocket-Protocol");
            return Task.CompletedTask;
        }, request =>
        {
            request.SendMessage(request.WsProtocol!);
            return Task.CompletedTask;
        }),
        ["/receive-backpressure"] = new(null, async request =>
        {
            await request.SleepAsync(2).ConfigureAwait(false);
            await request.ReceiveMessageAsync().ConfigureAwait(false);
        }),
        ["/receive-many-with-backpressure"] = new(null, async request =>
        {
            while (true)
            {
                await request.SleepAsync(0.1).ConfigureAwait(false);
                var line = await request.ReceiveMessageAsync().ConfigureAwait(false);
                if (line is null)
                    return;

                // len() of a str is its number of code points
                var length = line is string text ? text.EnumerateRunes().Count() : ((byte[])line).Length;
                request.SendMessage(length.ToString(CultureInfo.InvariantCulture), binary: false);
            }
        }),
        ["/referrer"] = new(null, request =>
        {
            request.SendMessage(request.Header("Referer") ?? "MISSING AS PER FETCH");
            return Task.CompletedTask;
        }),
        ["/remote-close"] = new(null, RemoteCloseAsync),
        ["/send-backpressure"] = new(null, request =>
        {
            const int MessageSize = 1024 * 1024;
            const int MessageCount = 16;
            request.SendMessage(Array.Empty<byte>(), binary: true);
            var start = request.Connection.Server.Runner.Now;
            for (var i = 0; i < MessageCount; i++)
            {
                request.SendMessage(Enumerable.Repeat((byte)' ', MessageSize).ToArray(), binary: true);
            }

            request.SendMessage((request.Connection.Server.Runner.Now - start).TotalSeconds.ToString(CultureInfo.InvariantCulture), binary: false);
            return Task.CompletedTask;
        }),
        ["/set-cookie-secure"] = new(request => SetCookie(request, "ws_test_" + Query(request) + "=test; Secure; Path=/"), ReceiveOneMessageAsync),
        ["/set-cookie_http"] = new(request => SetCookie(request, "ws_test_" + Query(request) + "=test; Path=/; HttpOnly\r\nSec-WebSocket-Origin: " + request.WsOrigin), ReceiveOneMessageAsync),
        ["/set-cookie"] = new(request => SetCookie(request, "ws_test_" + Query(request) + "=test; Path=/"), ReceiveOneMessageAsync),
        ["/set-cookies-samesite"] = new(SetSameSiteCookies, ReceiveOneMessageAsync),
        ["/simple_handshake"] = new(SimpleHandshakeAsync, _ => Task.CompletedTask),
        ["/sleep_10_v13"] = new(request => request.SleepAsync(10), async request =>
        {
            while (true)
            {
                var line = await request.ReceiveMessageAsync().ConfigureAwait(false);
                if (line is null or GoodbyeMessage)
                    return;

                request.SendMessage(line, binary: false);
            }
        }),
        ["/stash_responder"] = new(null, StashResponderAsync),
        ["/wrong_accept_key"] = new(request =>
        {
            request.Write(Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols:\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: WebSocket\r\n" +
                "Sec-WebSocket-Origin: " + request.WsOrigin + "\r\n" +
                "Sec-WebSocket-Accept: thisisawrongacceptkey\r\n\r\n"));
            return Task.CompletedTask;
        }, request =>
        {
            request.SendMessage("test", binary: false);
            return Task.CompletedTask;
        }),
    };

    /// <summary>The handler of a path, or null when it is not emulated.</summary>
    public static WptWebSocketHandler? Get(string path) => Handlers.GetValueOrDefault(path);

    private static string Query(WptWebSocketRequest request) => request.Url.Search.Length > 0 ? request.Url.Search[1..] : "";

    private static Task SetCookie(WptWebSocketRequest request, string value)
    {
        request.ExtraHeaders.Add(new("Set-Cookie", value));
        return Task.CompletedTask;
    }

    private static async Task ReceiveOneMessageAsync(WptWebSocketRequest request) => await request.ReceiveMessageAsync().ConfigureAwait(false);

    private static async Task ReceiveUntilGoodbyeAsync(WptWebSocketRequest request)
    {
        while (true)
        {
            var line = await request.ReceiveMessageAsync().ConfigureAwait(false);
            if (line is null or GoodbyeMessage)
                return;
        }
    }

    /// <summary>basic_auth_wsh.py</summary>
    private static Task BasicAuthExtraHandshake(WptWebSocketRequest request)
    {
        if (request.Header("Authorization") != "Basic Zm9vOmJhcg==")
        {
            request.Write("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nWWW-Authenticate: Basic realm=\"camelot\"\r\n\r\n"u8);
            throw new WptWebSocketAbortException();
        }

        return Task.CompletedTask;
    }

    /// <summary>delayed-passive-close_wsh.py</summary>
    private static async Task<(int? Code, string? Reason)> DelayedPassiveClosingHandshakeAsync(WptWebSocketRequest request)
    {
        var code = request.WsCloseCode == 1005 ? null : request.WsCloseCode;
        await request.SleepAsync(1).ConfigureAwait(false);
        return (code, request.WsCloseReason);
    }

    /// <summary>echo_raw_wsh.py: the payload of a message is written as raw bytes.</summary>
    private static async Task EchoRawAsync(WptWebSocketRequest request)
    {
        while (true)
        {
            var line = await request.ReceiveMessageAsync().ConfigureAwait(false);
            if (line is null)
                return;

            if (line is byte[] bytes)
            {
                if (bytes.AsSpan().SequenceEqual("exit"u8))
                    return;

                request.Write(bytes);
            }
            else
            {
                request.Write(Encoding.UTF8.GetBytes((string)line));
            }
        }
    }

    /// <summary>echo_wsh.py</summary>
    private static async Task EchoAsync(WptWebSocketRequest request)
    {
        while (true)
        {
            var line = await request.ReceiveMessageAsync().ConfigureAwait(false);
            switch (line)
            {
                case null:
                    return;
                case string text:
                    request.SendMessage(text, binary: false);
                    if (text == GoodbyeMessage)
                        return;

                    break;
                default:
                    request.SendMessage(line, binary: true);
                    break;
            }
        }
    }

    /// <summary>echo_wsh.py: the Close frame of the browser is echoed.</summary>
    private static Task<(int? Code, string? Reason)> EchoPassiveClosingHandshakeAsync(WptWebSocketRequest request)
        => Task.FromResult((request.WsCloseCode == 1005 ? null : request.WsCloseCode, request.WsCloseReason));

    /// <summary>remote-close_wsh.py</summary>
    private static async Task RemoteCloseAsync(WptWebSocketRequest request)
    {
        var parameters = new WptHandlerRequest(request.Url, new Dom.DocumentRequest(request.Url.Href), []);
        if (parameters.Has("abrupt"))
        {
            request.SendPing("ping");
            throw new WptWebSocketAbortException();
        }

        int? code = null;
        string? reason = null;
        if (parameters.Get("code") is { } codeValue)
        {
            code = int.Parse(codeValue, CultureInfo.InvariantCulture);
            reason = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(parameters.Get("reason") ?? ""));
        }

        await request.CloseConnectionAsync(code, reason ?? "").ConfigureAwait(false);
    }

    /// <summary>set-cookies-samesite_wsh.py</summary>
    private static Task SetSameSiteCookies(WptWebSocketRequest request)
    {
        var query = Query(request);
        var maxAge = query.Contains("clear", StringComparison.Ordinal) ? "; Max-Age=0" : "";
        var value = query.Contains("value", StringComparison.Ordinal) ? new WptHandlerRequest(request.Url, new Dom.DocumentRequest(request.Url.Href), []).Get("value") ?? "" : "1";
        request.ExtraHeaders.Add(new("Set-Cookie", "samesite-unspecified=" + value + "; Path=/" + maxAge));
        request.ExtraHeaders.Add(new("Set-Cookie", "samesite-lax=" + value + "; Path=/; SameSite=Lax" + maxAge));
        request.ExtraHeaders.Add(new("Set-Cookie", "samesite-strict=" + value + "; Path=/; SameSite=Strict" + maxAge));
        request.ExtraHeaders.Add(new("Set-Cookie", "samesite-none=" + value + "; Path=/; SameSite=None; Secure" + maxAge));
        return Task.CompletedTask;
    }

    /// <summary>
    /// simple_handshake_wsh.py: the handler writes its own handshake response followed by a Close frame (1001, PASS), reads
    /// the Close frame of the browser, and closes the connection.
    /// </summary>
    private static async Task SimpleHandshakeAsync(WptWebSocketRequest request)
    {
        var response = "HTTP/1.1 101 Switching Protocols:\r\n" +
            "Connection: Upgrade\r\n" +
            "Upgrade: WebSocket\r\n" +
            "Set-Cookie: ws_test=test\r\n" +
            "Sec-WebSocket-Origin: " + request.WsOrigin + "\r\n" +
            "Sec-WebSocket-Accept: " + WptWebSocketServer.ComputeAccept(request.Header("Sec-WebSocket-Key")!) + "\r\n\r\n";
        request.Write(Encoding.UTF8.GetBytes(response));
        byte[] closeBody = [0x03, 0xE9, .. "PASS"u8];
        request.WriteFrame(0x8, closeBody);

        // The Close frame of the browser: its header (2 bytes), its mask (4 bytes) and its payload
        await request.ReadAsync(2 + closeBody.Length + 4).ConfigureAwait(false);
        throw new WptWebSocketAbortException();
    }

    /// <summary>stash_responder_wsh.py (used by the WebSocket subresources of common/security-features)</summary>
    private static async Task StashResponderAsync(WptWebSocketRequest request)
    {
        while (true)
        {
            var line = await request.ReceiveMessageAsync().ConfigureAwait(false);
            if (line is null)
                return;

            if (line is not "echo")
                continue;

            var query = new WptHandlerRequest(request.Url, new Dom.DocumentRequest(request.Url.Href), []);
            var path = query.Get("path", request.Url.Pathname)!;
            var key = query.Get("key")!;
            string responseData;
            switch (query.Get("action"))
            {
                case "put":
                    request.Stash.TakeValue(key, path);
                    request.Stash.Put(key, query.Get("value")!, path);
                    responseData = JsonSerializer.Serialize(new { status = "success", result = key });
                    break;
                case "purge":
                    responseData = JsonSerializer.Serialize(new { status = "success", result = request.Stash.TakeValue(key, path) as string });
                    break;
                case "check-scheme":
                    if (request.IsHttps)
                    {
                        request.Stash.TakeValue(key, path);
                        request.Stash.Put(key, "upgraded", path);
                    }

                    responseData = JsonSerializer.Serialize(new { status = "success", result = key });
                    break;
                case "take":
                    var value = request.Stash.TakeValue(key, path) as string;
                    responseData = JsonSerializer.Serialize(new { status = value is null ? "allowed" : "blocked", result = value });
                    break;
                default:
                    throw new InvalidOperationException("Unknown action");
            }

            request.SendMessage(responseData);
            return;
        }
    }
}
