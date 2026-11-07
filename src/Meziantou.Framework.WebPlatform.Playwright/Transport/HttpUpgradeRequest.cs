using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Playwright.Transport;

/// <summary>The HTTP/1.1 request that starts a connection: a WebSocket upgrade, or a plain GET (e.g. <c>/json</c>).</summary>
internal sealed class HttpUpgradeRequest
{
    private const int MaxHeaderSize = 64 * 1024;

    private HttpUpgradeRequest(string method, string target, Dictionary<string, string> headers)
    {
        Method = method;
        Target = target;
        Headers = headers;
    }

    public string Method { get; }

    /// <summary>Gets the request target (the path and the query).</summary>
    public string Target { get; }

    public string Path => Target.IndexOf('?', StringComparison.Ordinal) is var index and >= 0 ? Target[..index] : Target;

    public Dictionary<string, string> Headers { get; }

    public string? GetHeader(string name) => Headers.GetValueOrDefault(name);

    public bool IsWebSocketUpgrade => GetHeader("Upgrade") is { } upgrade && upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase) && GetHeader("Sec-WebSocket-Key") is not null;

    /// <summary>Reads the request line and the headers (the body is ignored), or returns null when the request is invalid.</summary>
    public static async Task<HttpUpgradeRequest?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHeaderSize];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
                return null;

            var read = await stream.ReadAsync(buffer.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;

            length += read;
            if (length >= 4 && buffer[length - 4] == '\r' && buffer[length - 3] == '\n' && buffer[length - 2] == '\r' && buffer[length - 1] == '\n')
                break;
        }

        var text = Encoding.Latin1.GetString(buffer, 0, length - 4);
        var lines = text.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.AsSpan(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
                continue;

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;
        }

        return new HttpUpgradeRequest(requestLine[0], requestLine[1], headers);
    }

    /// <summary>The value of the <c>Sec-WebSocket-Accept</c> header of the response (https://www.rfc-editor.org/rfc/rfc6455#section-4.2.2).</summary>
    public static string ComputeWebSocketAccept(string key)
    {
#pragma warning disable CA5350 // SHA-1 is required by the WebSocket protocol
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
#pragma warning restore CA5350
        return Convert.ToBase64String(hash);
    }

    public static async Task WriteResponseAsync(Stream stream, string statusLine, string? contentType, string body, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(statusLine).Append("\r\n");
        if (contentType is not null)
        {
            builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
        }

        builder.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
        builder.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
