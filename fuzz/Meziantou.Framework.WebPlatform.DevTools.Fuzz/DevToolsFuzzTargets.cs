using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.DevTools.Network;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Transport;

namespace Meziantou.Framework.WebPlatform.DevTools.Fuzz;

/// <summary>
/// The inputs the server reads from its clients: the messages of the protocol (parsing never throws, and accepts exactly the
/// commands that a JSON reader sees as commands), the URL patterns of the interception of the requests (they match as a
/// reference regular expression does), and the HTTP requests that open a connection (parsing never throws, and the hosts
/// it accepts are IP addresses or <c>localhost</c>).
/// </summary>
public static class DevToolsFuzzTargets
{
    private const byte Separator = 0;
    private const int MaxGlobLength = 256;

    private static readonly string[] MessageSeeds =
    [
        """{"id":1,"method":"Browser.getVersion"}""",
        """{"id":2,"method":"Runtime.evaluate","params":{"expression":"1"},"sessionId":"A"}""",
        """{"id":3,"method":"Page.enable","params":null}""",
        """{"method":"Browser.getVersion"}""",
        """{"id":1.5,"method":"Browser.getVersion"}""",
        """{"id":1}""",
        """{"id":1,"method":"A","sessionId":1}""",
        """{"id":1,"method":"A","params":[]}""",
        """[1]""",
        """{"id":""",
        """{"id":4,"method":"Target.attachToTarget","params":{"targetId":"0BC005978161A72847928A31E4237124","flatten":true}}""",
    ];

    private static readonly (string Pattern, string Url)[] GlobSeeds =
    [
        ("*", "https://example.com/"),
        ("", ""),
        ("", "a"),
        ("https://example.com/*", "https://example.com/a/b"),
        ("https://example.com/*", "http://example.com/a"),
        ("*.png", "https://example.com/image.png"),
        ("*.png", "https://example.com/image.png?x=1"),
        ("https://example.com/?", "https://example.com/a"),
        ("*\\*", "a*"),
        ("a\\?b", "a?b"),
        ("a\\", "a\\"),
        ("**a**b**", "xxaxxbxx"),
    ];

    private static readonly string[] HttpSeeds =
    [
        "GET /json/version HTTP/1.1\r\nHost: 127.0.0.1:9222\r\n\r\n",
        "GET /json/list HTTP/1.1\r\nHost: localhost\r\n\r\n",
        "GET /devtools/browser/abc HTTP/1.1\r\nHost: [::1]:9222\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n",
        "GET /json/version HTTP/1.1\r\nHost: example.com\r\n\r\n",
        "GET /json/version?x=1 HTTP/1.1\r\nHost: example.com:9222\r\nHost: second\r\n\r\n",
        "POST / HTTP/1.1\r\n\r\n",
        "GET /\r\n\r\n",
    ];

    private static readonly string[] Tokens =
    [
        "{", "}", "[", "]", ":", ",", "\"", "null", "true", "false", "1", "-1", "1.5", "1e3", "9223372036854775807", "9223372036854775808",
        "\"id\"", "\"method\"", "\"params\"", "\"sessionId\"", "\"Browser.getVersion\"", "\"Runtime.evaluate\"",
        "*", "?", "\\", "https://", "http://", "/", ".", "#",
        "GET ", "POST ", " HTTP/1.1", "\r\n", "\r\n\r\n", "Host: ", "Upgrade: websocket", "Sec-WebSocket-Key: ", "localhost", "127.0.0.1", "[::1]", ":9222",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("devtools-message", Message) { Seeds = () => MessageSeeds.Select((seed, index) => FuzzSeed.FromText("message-" + index, seed)), Dictionary = Tokens },
        new("devtools-url-glob", Glob) { Seeds = () => GlobSeeds.Select((seed, index) => FuzzSeed.FromText("glob-" + index, seed.Pattern + "\0" + seed.Url)), Dictionary = Tokens },
        new("devtools-http-request", Http) { Seeds = () => HttpSeeds.Select((seed, index) => new FuzzSeed("http-" + index, Encoding.Latin1.GetBytes(seed))), Dictionary = Tokens },
    ];

    /// <summary>The input is a message of a client: it is a command exactly when a JSON reader sees an object with an integer id and a string method.</summary>
    private static void Message(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var isCommand = CdpMessage.TryParse(bytes, out var message, out var id, out var errorCode, out var errorMessage);
        var expected = GetExpectedCommand(bytes);
        FuzzAssert.Equal(expected.ErrorCode, isCommand ? 0 : errorCode, "the error of the message differs from the reference");
        if (isCommand)
        {
            FuzzAssert.Equal(expected.Id, (long?)message.Id, "the id of the command differs from the reference");
            FuzzAssert.Equal(expected.Method, message.Method, "the method of the command differs from the reference");
            FuzzAssert.Equal(expected.SessionId, message.SessionId, "the session of the command differs from the reference");
        }
        else
        {
            FuzzAssert.True(errorMessage is { Length: > 0 }, "an invalid message has no error message");
            FuzzAssert.Equal(expected.Id, id, "the id of an invalid message differs from the reference");
        }
    }

    private static (int ErrorCode, long? Id, string? Method, string? SessionId) GetExpectedCommand(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetCharCount(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (CdpMessage.ParseError, null, null, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 256 });
        }
        catch (JsonException)
        {
            return (CdpMessage.ParseError, null, null, null);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (CdpMessage.ParseError, null, null, null);

            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out var id))
                return (CdpMessage.InvalidRequest, null, null, null);

            if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                return (CdpMessage.InvalidRequest, id, null, null);

            string? sessionId = null;
            if (root.TryGetProperty("sessionId", out var session))
            {
                if (session.ValueKind != JsonValueKind.String)
                    return (CdpMessage.InvalidRequest, id, null, null);

                sessionId = session.GetString();
            }

            if (root.TryGetProperty("params", out var parameters) && parameters.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
                return (CdpMessage.InvalidRequest, id, null, null);

            return (0, id, method.GetString(), sessionId);
        }
    }

    /// <summary>The input is a pattern and a URL separated by a zero byte: the pattern matches as the reference regular expression does.</summary>
    private static void Glob(ReadOnlySpan<byte> data)
    {
        var pattern = FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator));
        var url = FuzzText.Decode(data);
        if (pattern.Length > MaxGlobLength || url.Length > MaxGlobLength)
            return;

        var expected = Regex.IsMatch(url, ToRegex(pattern), RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        FuzzAssert.Equal(expected, UrlGlob.IsMatch(pattern, url), "the pattern " + FuzzText.Escape(pattern) + " and the reference disagree on " + FuzzText.Escape(url));

        static string ToRegex(string pattern)
        {
            var builder = new StringBuilder("^");
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                switch (c)
                {
                    case '*':
                        builder.Append(".*");
                        break;

                    case '?':
                        builder.Append('.');
                        break;

                    case '\\' when i + 1 < pattern.Length:
                        builder.Append(Regex.Escape(pattern[++i].ToString()));
                        break;

                    default:
                        builder.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }

            // \z, not $: $ also matches before a final new line
            return builder.Append("\\z").ToString();
        }
    }

    /// <summary>The input is the beginning of a connection: parsing never throws, and the accepted hosts are IP addresses or <c>localhost</c>.</summary>
    private static void Http(ReadOnlySpan<byte> data)
    {
        var request = ReadRequest(data.ToArray());
        if (request is null)
            return;

        FuzzAssert.True(request.Method.Length > 0 && !request.Method.Contains(' ', StringComparison.Ordinal), "the method of the request is empty or has a space");
        FuzzAssert.True(request.Target.StartsWith(request.Path, StringComparison.Ordinal) && !request.Path.Contains('?', StringComparison.Ordinal), "the path of the request is not the beginning of its target");
        var host = request.GetHeader("Host");
        if (DevToolsServer.IsAllowedHost(host) && !string.IsNullOrEmpty(host))
        {
            var name = host.StartsWith('[', StringComparison.Ordinal) ? host : host.Split(':')[0];
            FuzzAssert.True(name.StartsWith('[', StringComparison.Ordinal) || name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || !name.Any(char.IsLetter), "the host " + FuzzText.Escape(host) + " is accepted but it is not an IP address");
        }
    }

    private static HttpRequest? ReadRequest(byte[] bytes) => ReadRequestAsync(bytes).GetAwaiter().GetResult();

    private static async Task<HttpRequest?> ReadRequestAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await HttpRequest.ReadAsync(stream, CancellationToken.None).ConfigureAwait(false);
    }
}
