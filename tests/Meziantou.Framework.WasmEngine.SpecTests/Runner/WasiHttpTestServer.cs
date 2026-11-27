using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>
/// The HTTP servers of the tests of wasi:http: the servers of a specification (whose endpoints reply with a static
/// response, the body of the request, or its headers), and the server of a component of the world wasi:http/service.
/// </summary>
internal static class WasiHttpTestServer
{
    /// <summary>The key of the trailers of a request in its options: .NET requests have no trailers.</summary>
    public static readonly HttpRequestOptionsKey<List<KeyValuePair<string, string>>> RequestTrailers = new("trailers");

    /// <summary>Returns the handler of the endpoints of a server of a specification (test_case.py, ServerConfig).</summary>
    public static Func<HttpRequestMessage, Task<HttpResponseMessage>> CreateEndpoints(JsonElement server)
    {
        var endpoints = new Dictionary<(string Method, string Path), JsonElement>();
        if (server.TryGetProperty("endpoints", out var list))
        {
            foreach (var endpoint in list.EnumerateArray())
            {
                var method = endpoint.TryGetProperty("method", out var m) ? m.GetString()!.ToUpperInvariant() : "GET";
                var path = endpoint.TryGetProperty("path", out var p) ? p.GetString()! : "/";
                endpoints[(method, path)] = endpoint;
            }
        }

        return async request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (!endpoints.TryGetValue((request.Method.Method, path), out var endpoint))
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };

            var mode = endpoint.TryGetProperty("mode", out var modeValue) ? modeValue.GetString() : "static";
            switch (mode)
            {
                case "echo-body":
                    var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

                case "echo-headers":
                    if (request.Content is not null)
                    {
                        _ = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }

                    var echo = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                    echo.Headers.TryAddWithoutValidation("x-request-method", request.Method.Method);
                    echo.Headers.TryAddWithoutValidation("x-request-path", path);
                    foreach (var (name, values) in request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
                    {
                        echo.Headers.TryAddWithoutValidation("x-echo-" + name.ToLowerInvariant(), string.Join(", ", values));
                    }

                    if (request.Options.TryGetValue(RequestTrailers, out var trailers))
                    {
                        foreach (var (name, value) in trailers)
                        {
                            echo.Headers.TryAddWithoutValidation("x-trailer-" + name.ToLowerInvariant(), value);
                        }
                    }

                    // The trailers of a request of the in-memory client
                    if (request.Options.TryGetValue(Wasi.WasiHttpClient.RequestTrailers, out var pending) && await pending.ConfigureAwait(false) is { } requestTrailers)
                    {
                        foreach (var (name, values) in requestTrailers)
                        {
                            echo.Headers.TryAddWithoutValidation("x-trailer-" + name.ToLowerInvariant(), string.Join(", ", values));
                        }
                    }

                    return echo;

                default:
                    return CreateStaticResponse(endpoint.TryGetProperty("response", out var response) ? response : default);
            }
        };
    }

    private static HttpResponseMessage CreateStaticResponse(JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.String)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(response.GetString()!)) };

        var status = response.ValueKind == JsonValueKind.Object && response.TryGetProperty("status", out var s) ? s.GetInt32() : 200;
        var message = new HttpResponseMessage((HttpStatusCode)status);
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("chunks", out var chunks))
        {
            message.Content = new ChunkedContent([.. chunks.EnumerateArray().Select(chunk => Encoding.UTF8.GetBytes(chunk.GetString()!))]);
            if (response.TryGetProperty("trailers", out var trailers))
            {
                foreach (var trailer in trailers.EnumerateObject())
                {
                    message.TrailingHeaders.TryAddWithoutValidation(trailer.Name, trailer.Value.GetString());
                }
            }
        }
        else
        {
            var body = response.ValueKind == JsonValueKind.Object && response.TryGetProperty("body", out var b) ? b.GetString()! : "";
            message.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        }

        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("headers", out var headers))
        {
            foreach (var header in headers.EnumerateObject())
            {
                if (!message.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString()))
                {
                    message.Content.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
                }
            }
        }

        return message;
    }

    /// <summary>A body sent in chunks (<c>Transfer-Encoding: chunked</c>), with the trailers of the response.</summary>
    public sealed class ChunkedContent(byte[][] chunks) : HttpContent
    {
        public byte[][] Chunks { get; } = chunks;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Chunks.SelectMany(chunk => chunk).ToArray()).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = Chunks.Sum(chunk => (long)chunk.Length);
            return true;
        }
    }

    /// <summary>A minimal HTTP/1.1 server on the loopback address: one request per connection (<c>Connection: close</c>).</summary>
    public sealed class Server : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        private readonly CancellationTokenSource _stop = new();

        public Server(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                Socket socket;
                try
                {
                    socket = await _listener.AcceptSocketAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(socket));
            }
        }

        private async Task ServeAsync(Socket socket)
        {
            using var stream = new NetworkStream(socket, ownsSocket: true);
            try
            {
                var request = await ReadRequestAsync(stream, Port).ConfigureAwait(false);
                if (request is null)
                    return;

                using (request)
                {
                    using var response = await _handler(request).ConfigureAwait(false);
                    await WriteResponseAsync(stream, response).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
            {
                // The client closed the connection
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Dispose();
            _stop.Dispose();
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream)
    {
        var line = new List<byte>();
        var buffer = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(buffer).ConfigureAwait(false) == 0)
                return line.Count == 0 ? null : throw new InvalidDataException("The connection ended in a line");

            if (buffer[0] == '\n')
                return Encoding.Latin1.GetString([.. line]).TrimEnd('\r');

            line.Add(buffer[0]);
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
        return buffer;
    }

    private static async Task<List<KeyValuePair<string, string>>> ReadFieldsAsync(Stream stream)
    {
        var fields = new List<KeyValuePair<string, string>>();
        while (await ReadLineAsync(stream).ConfigureAwait(false) is { Length: > 0 } line)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            fields.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        return fields;
    }

    /// <summary>Reads a request: its line, its headers, and its body (with a length, or chunked with trailers).</summary>
    private static async Task<HttpRequestMessage?> ReadRequestAsync(Stream stream, int port)
    {
        if (await ReadLineAsync(stream).ConfigureAwait(false) is not { } requestLine)
            return null;

        var parts = requestLine.Split(' ');
        var headers = await ReadFieldsAsync(stream).ConfigureAwait(false);
        byte[] body = [];
        List<KeyValuePair<string, string>>? trailers = null;
        if (headers.Exists(header => string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && header.Value.Contains("chunked", StringComparison.OrdinalIgnoreCase)))
        {
            using var content = new MemoryStream();
            while (true)
            {
                var size = int.Parse((await ReadLineAsync(stream).ConfigureAwait(false))!.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0)
                    break;

                content.Write(await ReadExactlyAsync(stream, size).ConfigureAwait(false));
                await ReadLineAsync(stream).ConfigureAwait(false);
            }

            trailers = await ReadFieldsAsync(stream).ConfigureAwait(false);
            body = content.ToArray();
        }
        else if (headers.Find(header => string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) is { Value: { } length })
        {
            body = await ReadExactlyAsync(stream, int.Parse(length, CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }

        var request = new HttpRequestMessage(new HttpMethod(parts[0]), new Uri("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + parts[1])) { Content = new ByteArrayContent(body) };
        foreach (var (name, value) in headers)
        {
            if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (trailers is not null)
        {
            request.Options.Set(RequestTrailers, trailers);
        }

        return request;
    }

    /// <summary>Writes a response: chunked when it has chunks or trailers, else with its length.</summary>
    private static async Task WriteResponseAsync(Stream stream, HttpResponseMessage response)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase ?? "OK"}\r\n");
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var value in values)
            {
                builder.Append(name).Append(": ").Append(value).Append("\r\n");
            }
        }

        var chunks = response.Content is ChunkedContent chunked ? chunked.Chunks : null;
        var hasTrailers = response.TrailingHeaders.Any();
        var body = chunks is null ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false) : null;
        builder.Append(chunks is not null || hasTrailers ? "Transfer-Encoding: chunked\r\n" : "Content-Length: " + body!.Length.ToString(CultureInfo.InvariantCulture) + "\r\n");
        builder.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(builder.ToString())).ConfigureAwait(false);
        if (chunks is null && !hasTrailers)
        {
            await stream.WriteAsync(body).ConfigureAwait(false);
            return;
        }

        foreach (var chunk in chunks ?? [body!])
        {
            if (chunk.Length == 0)
                continue;

            await stream.WriteAsync(Encoding.Latin1.GetBytes(chunk.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n")).ConfigureAwait(false);
            await stream.WriteAsync(chunk).ConfigureAwait(false);
            await stream.WriteAsync("\r\n"u8.ToArray()).ConfigureAwait(false);
        }

        var end = new StringBuilder("0\r\n");
        foreach (var (name, values) in response.TrailingHeaders)
        {
            end.Append(name).Append(": ").Append(string.Join(", ", values)).Append("\r\n");
        }

        end.Append("\r\n");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(end.ToString())).ConfigureAwait(false);
    }
}
