using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Wpt.Runner;

namespace Meziantou.Framework.WebPlatform.Wpt;

/// <summary>The emulated WebTransport server of the runner, driven through the API of the loader (without browser).</summary>
public sealed class WptWebTransportServerTests : IDisposable
{
    private const string BaseUrl = "https://web-platform.test:11000/webtransport/handlers/";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly TestEventLoop _eventLoop = new();
    private readonly ConcurrentQueue<string> _log = new();
    private readonly WptWebTransportServer _server;

    public WptWebTransportServerTests()
    {
        _server = new WptWebTransportServer(_eventLoop.QueueTask, new WptStash(), _log.Enqueue);
    }

    public void Dispose() => _eventLoop.Dispose();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Echo_BidirectionalStream()
    {
        await using var connection = await ConnectAsync("echo.py");

        // session_established opens a bidirectional stream
        var incoming = await connection.AcceptStreamAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        Assert.True(incoming.IsBidirectional);

        var stream = await connection.OpenStreamAsync(bidirectional: true, waitUntilAvailable: false, CancellationToken);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanWrite);
        await stream.WriteAsync("hello"u8.ToArray(), endOfStream: false, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        await stream.WriteAsync(" world"u8.ToArray(), endOfStream: true, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        await stream.WritesClosed.WaitAsync(Timeout, CancellationToken);
        Assert.Equal("hello world", Encoding.UTF8.GetString(await ReadToEndAsync(stream)));

        await incoming.WriteAsync("abc"u8.ToArray(), endOfStream: true, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        Assert.Equal("abc", Encoding.UTF8.GetString(await ReadToEndAsync(incoming)));
        await AssertNoHandlerErrorAsync();
    }

    [Fact]
    public async Task Echo_UnidirectionalStream()
    {
        await using var connection = await ConnectAsync("echo.py");
        await connection.AcceptStreamAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);

        var stream = await connection.OpenStreamAsync(bidirectional: false, waitUntilAvailable: false, CancellationToken);
        Assert.False(stream.IsBidirectional);
        Assert.False(stream.CanRead);
        Assert.True(stream.CanWrite);
        await stream.WriteAsync("abc"u8.ToArray(), endOfStream: false, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        await stream.WriteAsync("def"u8.ToArray(), endOfStream: true, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);

        var echo = await connection.AcceptStreamAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        Assert.False(echo.IsBidirectional);
        Assert.True(echo.CanRead);
        Assert.False(echo.CanWrite);
        Assert.Equal("abcdef", Encoding.UTF8.GetString(await ReadToEndAsync(echo)));
        await AssertNoHandlerErrorAsync();
    }

    [Fact]
    public async Task Echo_Datagrams()
    {
        await using var connection = await ConnectAsync("echo.py");
        Assert.Equal(WptWebTransportSession.MaxDatagramSize, connection.MaxDatagramSize);

        // A datagram larger than the maximum size is not sent
        await connection.SendDatagramAsync(new byte[connection.MaxDatagramSize + 1], CancellationToken);
        await connection.SendDatagramAsync(new byte[] { 1, 2, 3 }, CancellationToken);
        var datagram = await connection.ReceiveDatagramAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        Assert.Equal([1, 2, 3], datagram.ToArray());

        var statistics = await connection.GetStatisticsAsync(CancellationToken);
        Assert.True(statistics.BytesSent >= 3);
        Assert.True(statistics.BytesReceived >= 3);
        Assert.True(statistics.MinRtt > TimeSpan.Zero);
        await AssertNoHandlerErrorAsync();
    }

    [Fact]
    public async Task EchoDatagramLength()
    {
        await using var connection = await ConnectAsync("echo_datagram_length.py");
        await connection.SendDatagramAsync(new byte[connection.MaxDatagramSize], CancellationToken);
        var datagram = await connection.ReceiveDatagramAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        using var document = JsonDocument.Parse(datagram);
        Assert.Equal(connection.MaxDatagramSize, document.RootElement.GetProperty("length").GetInt32());
    }

    [Theory]
    [InlineData("custom-response.py?:status=301")]
    [InlineData("custom-response.py?:status=404")]
    [InlineData("custom-response.py?:status=abc")]
    [InlineData("unknown.py")]
    public async Task Connect_Rejected(string handler)
    {
        var connection = await _server.ConnectAsync(CreateRequest(handler), CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        Assert.Null(connection);
    }

    [Theory]
    [InlineData("https://web-platform.test:8443/webtransport/handlers/echo.py")]
    [InlineData("https://nonexistent.web-platform.test:11000/webtransport/handlers/echo.py")]
    [InlineData("https://example.com:11000/webtransport/handlers/echo.py")]
    public async Task Connect_NotServed(string url)
    {
        var connection = await _server.ConnectAsync(new WebTransportConnectionRequest { Url = url }, CancellationToken);
        Assert.Null(connection);
    }

    [Fact]
    public async Task Connect_ServerCertificateHashes()
    {
        var request = new WebTransportConnectionRequest { Url = BaseUrl + "echo.py", ServerCertificateHashes = [new byte[32]] };
        Assert.Null(await _server.ConnectAsync(request, CancellationToken));
    }

    [Fact]
    public async Task CustomResponse_Headers()
    {
        await using var connection = await ConnectAsync("custom-response.py?:status=204&x-test-header=hello&wt-protocol=%22b%22%3B%20a%3Dc", protocols: ["a", "b"]);
        Assert.Equal("b", connection.Protocol);
        Assert.Contains(new KeyValuePair<string, string>("x-test-header", "hello"), connection.ResponseHeaders);
        Assert.DoesNotContain(connection.ResponseHeaders, header => header.Key is ":status" or "wt-protocol");
    }

    [Theory]
    [InlineData("custom-response.py?wt-protocol=%22d%22")]
    [InlineData("custom-response.py?wt-protocol=b")]
    public async Task CustomResponse_IgnoredProtocol(string handler)
    {
        await using var connection = await ConnectAsync(handler, protocols: ["a", "b"]);
        Assert.Equal("", connection.Protocol);
    }

    [Fact]
    public async Task EchoRequestHeaders()
    {
        await using var connection = await ConnectAsync("echo-request-headers.py", protocols: ["a", "b\"c"], headers: [new("x-client-header", "sent")]);
        var stream = await connection.AcceptStreamAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        using var document = JsonDocument.Parse(await ReadToEndAsync(stream));
        var headers = document.RootElement;
        Assert.Equal("CONNECT", headers.GetProperty(":method").GetString());
        Assert.Equal("https", headers.GetProperty(":scheme").GetString());
        Assert.Equal("web-platform.test:11000", headers.GetProperty(":authority").GetString());
        Assert.Equal("/webtransport/handlers/echo-request-headers.py", headers.GetProperty(":path").GetString());
        Assert.Equal("webtransport", headers.GetProperty(":protocol").GetString());
        Assert.Equal("https://web-platform.test:8443", headers.GetProperty("origin").GetString());
        Assert.Equal("sent", headers.GetProperty("x-client-header").GetString());
        Assert.Equal("\"a\", \"b\\\"c\"", headers.GetProperty("wt-available-protocols").GetString());
    }

    [Fact]
    public async Task ClientClose_SessionCloseInfo()
    {
        var token = Guid.NewGuid().ToString();
        var connection = await ConnectAsync("client-close.py?token=" + token);
        await connection.CloseAsync(99, "reason X");
        var closeInfo = await connection.Closed.WaitAsync(Timeout, CancellationToken);
        Assert.Equal(new WebTransportConnectionCloseInfo(99, "reason X"), closeInfo);
        await connection.DisposeAsync();

        using var data = await QueryAsync(token);
        var info = data.RootElement.GetProperty("session-close-info");
        Assert.False(info.GetProperty("abruptly").GetBoolean());
        Assert.Equal(99, info.GetProperty("close_info").GetProperty("code").GetInt32());
        Assert.Equal("reason X", info.GetProperty("close_info").GetProperty("reason").GetString());
        await AssertNoHandlerErrorAsync();
    }

    [Fact]
    public async Task ClientClose_Dispose()
    {
        var token = Guid.NewGuid().ToString();
        var connection = await ConnectAsync("client-close.py?token=" + token);
        await connection.DisposeAsync();

        using var data = await QueryAsync(token);
        var info = data.RootElement.GetProperty("session-close-info");
        Assert.True(info.GetProperty("abruptly").GetBoolean());
        Assert.Equal(JsonValueKind.Null, info.GetProperty("close_info").ValueKind);
    }

    [Fact]
    public async Task ClientClose_StreamFin()
    {
        var token = Guid.NewGuid().ToString();
        await using var connection = await ConnectAsync("client-close.py?token=" + token);
        var stream = await connection.OpenStreamAsync(bidirectional: false, waitUntilAvailable: false, CancellationToken);
        await stream.WriteAsync(ReadOnlyMemory<byte>.Empty, endOfStream: true, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);

        using var data = await QueryAsync(token);
        Assert.Equal("FIN", data.RootElement.GetProperty("stream-close-info").GetProperty("source").GetString());
    }

    [Fact]
    public async Task ClientClose_StreamReset()
    {
        var token = Guid.NewGuid().ToString();
        await using var connection = await ConnectAsync("client-close.py?token=" + token);
        var stream = await connection.OpenStreamAsync(bidirectional: true, waitUntilAvailable: false, CancellationToken);
        stream.AbortWrite(139);
        await stream.WritesClosed.WaitAsync(Timeout, CancellationToken);

        using var data = await QueryAsync(token);
        var info = data.RootElement.GetProperty("stream-close-info");
        Assert.Equal("reset", info.GetProperty("source").GetString());
        Assert.Equal(WptWebTransportServer.ToHttpErrorCode(139), info.GetProperty("code").GetInt64());
    }

    [Fact]
    public async Task AbortStreamFromServer_StopSending()
    {
        await using var connection = await ConnectAsync("abort-stream-from-server.py?code=" + WptWebTransportServer.ToHttpErrorCode(139));
        var stream = await connection.OpenStreamAsync(bidirectional: false, waitUntilAvailable: false, CancellationToken);

        // The write completes (the server received the data), then the server sends STOP_SENDING
        await stream.WriteAsync(new byte[] { 64 }, endOfStream: false, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        var exception = await Assert.ThrowsAsync<WebTransportStreamAbortedException>(() => stream.WritesClosed.WaitAsync(Timeout, CancellationToken));
        Assert.Equal(139u, exception.ErrorCode);

        exception = await Assert.ThrowsAsync<WebTransportStreamAbortedException>(() => stream.WriteAsync(new byte[] { 65 }, endOfStream: false, CancellationToken).AsTask());
        Assert.Equal(139u, exception.ErrorCode);
        await AssertNoHandlerErrorAsync();
    }

    [Fact]
    public async Task AbortStreamFromServer_Reset()
    {
        await using var connection = await ConnectAsync("abort-stream-from-server.py?code=" + WptWebTransportServer.ToHttpErrorCode(uint.MaxValue));
        var stream = await connection.OpenStreamAsync(bidirectional: true, waitUntilAvailable: false, CancellationToken);
        await stream.WriteAsync(new byte[] { 64 }, endOfStream: false, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);

        var exception = await Assert.ThrowsAsync<WebTransportStreamAbortedException>(() => stream.ReadAsync(new byte[16], CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken));
        Assert.Equal(uint.MaxValue, exception.ErrorCode);
        exception = await Assert.ThrowsAsync<WebTransportStreamAbortedException>(() => stream.WritesClosed.WaitAsync(Timeout, CancellationToken));
        Assert.Equal(uint.MaxValue, exception.ErrorCode);
        await AssertNoHandlerErrorAsync();
    }

    [Fact]
    public async Task StopSendingBidiStreamThenSend()
    {
        await using var connection = await ConnectAsync("stop-sending-bidi-stream-then-send.py?code=" + WptWebTransportServer.ToHttpErrorCode(21));
        var stream = await connection.OpenStreamAsync(bidirectional: true, waitUntilAvailable: false, CancellationToken);
        await stream.WriteAsync(new byte[] { 1 }, endOfStream: false, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);

        var exception = await Assert.ThrowsAsync<WebTransportStreamAbortedException>(() => stream.WritesClosed.WaitAsync(Timeout, CancellationToken));
        Assert.Equal(21u, exception.ErrorCode);
        Assert.Equal("abc", Encoding.UTF8.GetString(await ReadToEndAsync(stream)));
    }

    [Fact]
    public async Task ServerClose()
    {
        await using var connection = await ConnectAsync("server-close.py?code=32&reason=abc");
        Assert.Equal(new WebTransportConnectionCloseInfo(32, "abc"), await connection.Closed.WaitAsync(Timeout, CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => connection.AcceptStreamAsync(CancellationToken).AsTask());

        await using var connectionWithoutCode = await ConnectAsync("server-close.py");
        Assert.Equal(new WebTransportConnectionCloseInfo(0, ""), await connectionWithoutCode.Closed.WaitAsync(Timeout, CancellationToken));
    }

    [Fact]
    public async Task ServerConnectionClose()
    {
        await using var connection = await ConnectAsync("server-connection-close.py");
        var stream = await connection.AcceptStreamAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        var read = stream.ReadAsync(new byte[16], CancellationToken).AsTask();
        await stream.WriteAsync(new byte[] { 65 }, endOfStream: false, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);

        await Assert.ThrowsAsync<IOException>(() => connection.Closed.WaitAsync(Timeout, CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => read.WaitAsync(Timeout, CancellationToken));
    }

    [Fact]
    public async Task ServerDrain()
    {
        await using var connection = await ConnectAsync("server-drain.py?drain");
        await connection.Draining.WaitAsync(Timeout, CancellationToken);
        Assert.False(connection.Closed.IsCompleted);

        await using var notDraining = await ConnectAsync("server-drain.py");
        await WaitUntilIdleAsync();
        Assert.False(notDraining.Draining.IsCompleted);
    }

    [Fact]
    public async Task ServerCreateMultipleStreams()
    {
        await using var connection = await ConnectAsync("server-create-multiple-streams.py?type=unidi&count=2&marker=1");
        Assert.Equal("stream0", Encoding.UTF8.GetString(await ReadToEndAsync(await connection.AcceptStreamAsync(CancellationToken))));
        Assert.Equal("stream1", Encoding.UTF8.GetString(await ReadToEndAsync(await connection.AcceptStreamAsync(CancellationToken))));
        Assert.Equal("marker", Encoding.UTF8.GetString(await ReadToEndAsync(await connection.AcceptStreamAsync(CancellationToken))));
    }

    [Fact]
    public async Task AnticipatedStreams()
    {
        await using var connection = await ConnectAsync("anticipated-streams.py", anticipatedUnidirectional: 500, anticipatedBidirectional: 300);
        Assert.Equal((300, 500), connection.IncomingStreamLimits);

        var stream = await connection.AcceptStreamAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        await stream.WriteAsync("{\"uni\": 500, \"bidi\": 300}"u8.ToArray(), endOfStream: true, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        using var response = JsonDocument.Parse(await ReadToEndAsync(stream));
        Assert.Equal(500, response.RootElement.GetProperty("uni_created").GetInt32());
        Assert.Equal(300, response.RootElement.GetProperty("bidi_created").GetInt32());
        Assert.Equal(500, response.RootElement.GetProperty("uni_requested").GetInt32());
    }

    [Fact]
    public async Task OpenStream_Limit()
    {
        await using var connection = await ConnectAsync("echo.py");
        var streams = new List<WebTransportConnectionStream>();
        while (true)
        {
            try
            {
                streams.Add(await connection.OpenStreamAsync(bidirectional: false, waitUntilAvailable: false, CancellationToken));
            }
            catch (InvalidOperationException)
            {
                break;
            }
        }

        Assert.NotEmpty(streams);

        // A stream that is closed no longer counts
        var waiting = connection.OpenStreamAsync(bidirectional: false, waitUntilAvailable: true, CancellationToken).AsTask();
        Assert.False(waiting.IsCompleted);
        await streams[0].WriteAsync(ReadOnlyMemory<byte>.Empty, endOfStream: true, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        await waiting.WaitAsync(Timeout, CancellationToken);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0x1du)]
    [InlineData(0x1eu)]
    [InlineData(139u)]
    [InlineData(uint.MaxValue)]
    public void ErrorCodes_RoundTrip(uint code)
    {
        Assert.Equal(code, WptWebTransportServer.ToWebTransportErrorCode(WptWebTransportServer.ToHttpErrorCode(code)));
    }

    [Fact]
    public void ErrorCodes_NotWebTransport()
    {
        Assert.Equal(WptWebTransportServer.LastHttpErrorCode, WptWebTransportServer.ToHttpErrorCode(uint.MaxValue));
        Assert.Null(WptWebTransportServer.ToWebTransportErrorCode(0));
        Assert.Null(WptWebTransportServer.ToWebTransportErrorCode(WptWebTransportServer.FirstHttpErrorCode - 1));
        Assert.Null(WptWebTransportServer.ToWebTransportErrorCode(WptWebTransportServer.LastHttpErrorCode + 1));

        // The reserved code points (0x1f * N + 0x21) are skipped
        Assert.Null(WptWebTransportServer.ToWebTransportErrorCode(WptWebTransportServer.FirstHttpErrorCode + 0x1e));
    }

    private static WebTransportConnectionRequest CreateRequest(string handler, IReadOnlyList<string>? protocols = null, IReadOnlyList<KeyValuePair<string, string>>? headers = null, int? anticipatedUnidirectional = null, int? anticipatedBidirectional = null) => new()
    {
        Url = BaseUrl + handler,
        Headers = [new("origin", "https://web-platform.test:8443"), .. headers ?? []],
        Protocols = protocols ?? [],
        AnticipatedConcurrentIncomingUnidirectionalStreams = anticipatedUnidirectional,
        AnticipatedConcurrentIncomingBidirectionalStreams = anticipatedBidirectional,
    };

    private async Task<WebTransportConnection> ConnectAsync(string handler, IReadOnlyList<string>? protocols = null, IReadOnlyList<KeyValuePair<string, string>>? headers = null, int? anticipatedUnidirectional = null, int? anticipatedBidirectional = null)
    {
        var request = CreateRequest(handler, protocols, headers, anticipatedUnidirectional, anticipatedBidirectional);
        var connection = await _server.ConnectAsync(request, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        Assert.NotNull(connection);
        return connection;
    }

    /// <summary>The value of the stash of a token, read by query.py.</summary>
    private async Task<JsonDocument> QueryAsync(string token)
    {
        await using var connection = await ConnectAsync("query.py?token=" + token);
        var stream = await connection.AcceptStreamAsync(CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
        return JsonDocument.Parse(await ReadToEndAsync(stream));
    }

    private static async Task<byte[]> ReadToEndAsync(WebTransportConnectionStream stream)
    {
        var result = new List<byte>();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, CancellationToken).AsTask().WaitAsync(Timeout, CancellationToken);
            if (read == 0)
                return [.. result];

            result.AddRange(buffer.AsSpan(0, read));
        }
    }

    /// <summary>Waits until the server is idle and the tasks it queued ran.</summary>
    private async Task WaitUntilIdleAsync()
    {
        while (_server.IsBusy)
        {
            await Task.Delay(1, CancellationToken);
        }

        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventLoop.QueueTask(drained.SetResult);
        await drained.Task.WaitAsync(Timeout, CancellationToken);
    }

    private async Task AssertNoHandlerErrorAsync()
    {
        await WaitUntilIdleAsync();
        Assert.Empty(_log);
    }

    /// <summary>The event loop of the test page: the tasks run one at a time on a thread, in order.</summary>
    private sealed class TestEventLoop : IDisposable
    {
        private readonly BlockingCollection<Action> _tasks = [];
        private readonly Thread _thread;

        public TestEventLoop()
        {
            _thread = new Thread(() =>
            {
                foreach (var task in _tasks.GetConsumingEnumerable())
                {
                    task();
                }
            })
            {
                IsBackground = true,
                Name = "WebTransport test event loop",
            };
            _thread.Start();
        }

        public void QueueTask(Action task)
        {
            try
            {
                _tasks.Add(task);
            }
            catch (InvalidOperationException)
            {
                // The test is over
            }
        }

        public void Dispose()
        {
            _tasks.CompleteAdding();

            // The last continuation of a test may run on the thread of the event loop
            if (Thread.CurrentThread != _thread)
            {
                _thread.Join();
                _tasks.Dispose();
            }
        }
    }
}
