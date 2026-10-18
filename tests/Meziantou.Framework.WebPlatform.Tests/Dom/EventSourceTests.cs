using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The EventSource interface (https://html.spec.whatwg.org/multipage/server-sent-events.html), with a stub loader and with a loopback server.</summary>
public sealed class EventSourceTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly JintScriptEngine _engine = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(DocumentLoader loader, string html, string url = "https://example.com/", TimeProvider? timeProvider = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            DocumentLoader = loader,
            ScriptEngine = _engine,
            TimeProvider = timeProvider ?? _clock,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });
        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>Runs the tasks of the event loop until the condition is true (the events of an open connection are not awaited by RunUntilIdleAsync).</summary>
    private async Task WaitUntilAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            _contexts.EventLoop.RunUntilIdle();
            if (condition())
                return;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(describe());

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private const string LogScript = """
        const log = [];
        const show = () => document.title = log.join('|');
        function listen(source) {
          source.onopen = () => { log.push('open:' + source.readyState); show(); };
          source.onerror = () => { log.push('error:' + source.readyState); show(); };
          source.onmessage = e => { log.push('message:' + e.data + ':' + e.lastEventId + ':' + e.origin); show(); };
        }
        """;

    [Theory]
    [InlineData("data: a\n\n", "message:a:")]
    [InlineData("data:a\r\rdata:b\r\n\r\ndata:c\n\n", "message:a:|message:b:|message:c:")]
    [InlineData("data: a\ndata:  b\ndata\n\n", "message:a\n b\n:")]
    [InlineData(": comment\nevent: custom\nid: 1\ndata: a\n\ndata: b\n\n", "custom:a:1|message:b:1")]
    [InlineData("id: 1\ndata: a\n\nid\ndata: b\n\n", "message:a:1|message:b:")]
    [InlineData("id: 1\n\nid: x\0y\ndata: a\n\n", "message:a:1")]
    [InlineData("\U0000FEFFdata: a\n\n\U0000FEFFdata: b\n\ndata: c\n\n", "message:a:|message:c:")]
    [InlineData("event: custom\n\ndata: a\n\nData: b\nunknown: c\n\ndata: d", "message:a:")]
    [InlineData("retry: 1000\nretry: 1e3\nretry:\nretry: 20\ndata: a\n\n", "retry:1000|retry:20|message:a:")]
    public void Parser_InterpretsTheEventStream(string stream, string expected)
    {
        // The stream is interpreted the same way whatever the chunks: as a whole, and byte by byte
        var bytes = Encoding.UTF8.GetBytes(stream);
        foreach (var chunkSize in (int[])[bytes.Length, 1, 3])
        {
            var log = new List<string>();
            var parser = new EventStreamParser("", message => log.Add(message.Type + ":" + message.Data + ":" + message.LastEventId), time => log.Add("retry:" + time.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            for (var i = 0; i < bytes.Length; i += chunkSize)
            {
                parser.Write(bytes.AsSpan(i, Math.Min(chunkSize, bytes.Length - i)));
            }

            Assert.Equal(expected, string.Join('|', log));
        }
    }

    [Fact]
    public void Parser_KeepsTheLastEventIdOfTheEventSource()
    {
        var log = new List<string>();
        var parser = new EventStreamParser("previous", message => log.Add(message.Data + ":" + message.LastEventId), _ => { });
        parser.Write("data: a\n\nid: next\n\n"u8);

        Assert.Equal(["a:previous"], log);
        Assert.Equal("next", parser.LastEventId);
    }

    [Fact]
    public async Task EventSource_DispatchesTheEventsAsTheChunksArrive()
    {
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var loader = new StubLoader(request => new DocumentResource("retry: 500\ndata: first\n\n"u8.ToArray())
        {
            Headers = [new("Content-Type", "text/event-stream")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });
        var document = Load(loader, $$"""
            <script>
              {{LogScript}}
              const source = new EventSource('/events?a');
              log.push(source.url + ':' + source.readyState + ':' + source.withCredentials);
              listen(source);
              source.addEventListener('custom', e => { log.push('custom:' + e.data + ':' + e.lastEventId); show(); });
            </script>
            """);

        // The connection is open and the first event is dispatched before the end of the response
        Assert.Equal("https://example.com/events?a:0:false|open:1|message:first::https://example.com", document.Title);
        var request = Assert.Single(loader.Requests);
        Assert.Equal("text/event-stream", HeaderList.Get(request.Headers, "Accept"));
        Assert.Equal("no-cache", HeaderList.Get(request.Headers, "Cache-Control"));
        Assert.Null(HeaderList.Get(request.Headers, "Last-Event-ID"));
        Assert.Equal(RequestMode.Cors, request.Mode);
        Assert.Equal(RequestDestination.Empty, request.Destination);

        // An event split between two chunks
        chunks.Writer.TryWrite("event: custom\nid: caf\U000000E9\nda"u8.ToArray());
        chunks.Writer.TryWrite("ta: second\n\n"u8.ToArray());
        await WaitUntilAsync(() => document.Title.Contains("custom:", StringComparison.Ordinal), () => document.Title);
        Assert.EndsWith("|custom:second:caf\U000000E9", document.Title, StringComparison.Ordinal);

        // The end of the response: the connection is reestablished after the reconnection time, with the last event ID
        chunks.Writer.Complete();
        await WaitUntilAsync(() => document.Title.Contains("error:", StringComparison.Ordinal), () => document.Title);
        Assert.EndsWith("|error:0", document.Title, StringComparison.Ordinal);
        Assert.Single(loader.Requests);

        _clock.Advance(TimeSpan.FromMilliseconds(499));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(loader.Requests);

        loader.Respond = _ => new DocumentResource("data: again\n\n") { Headers = [new("Content-Type", "text/event-stream;charset=windows-1252")] };
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(2, loader.Requests.Count);
        Assert.Equal("caf\U000000C3\U000000A9", HeaderList.Get(loader.Requests[1].Headers, "Last-Event-ID"));
        Assert.EndsWith("|open:1|message:again:caf\U000000E9:https://example.com|error:0", document.Title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(404, "text/event-stream")]
    [InlineData(204, "text/event-stream")]
    [InlineData(200, "text/plain")]
    [InlineData(200, null)]
    public void EventSource_FailsTheConnection_WhenTheResponseIsNotAnEventStream(int status, string? contentType)
    {
        var loader = new StubLoader(_ => new DocumentResource("data: a\n\n") { StatusCode = status, Headers = contentType is null ? [] : [new("Content-Type", contentType)] });
        var document = Load(loader, $$"""
            <script>
              {{LogScript}}
              listen(new EventSource('/events'));
            </script>
            """);

        Assert.Equal("error:2", document.Title);

        // The connection is not reestablished
        _clock.Advance(TimeSpan.FromMinutes(1));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(loader.Requests);
    }

    [Fact]
    public void EventSource_ReestablishesTheConnection_WhenTheServerCannotBeReached()
    {
        var loader = new StubLoader(_ => null);
        var document = Load(loader, $$"""
            <script>
              {{LogScript}}
              listen(new EventSource('/events'));
            </script>
            """);
        Assert.Equal("error:0", document.Title);

        loader.Respond = _ => new DocumentResource("data: a\n\n") { Headers = [new("Content-Type", "text/event-stream")] };
        _clock.Advance(TimeSpan.FromSeconds(3));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("error:0|open:1|message:a::https://example.com|error:0", document.Title);
    }

    [Fact]
    public void EventSource_CrossOrigin_ChecksTheCorsHeadersAndSendsTheCredentialsWhenAsked()
    {
        var loader = new StubLoader(request => new DocumentResource("data: " + request.IncludeCredentials + "\n\n")
        {
            Headers = request.Url.Contains("denied", StringComparison.Ordinal)
                ? [new("Content-Type", "text/event-stream")]
                : [new("Content-Type", "text/event-stream"), new("Access-Control-Allow-Origin", "https://example.com"), new("Access-Control-Allow-Credentials", "true")],
        });
        var document = Load(loader, $$"""
            <script>
              {{LogScript}}
              for (const [url, init] of [['https://other.example/a', undefined], ['https://other.example/b', { withCredentials: true }], ['https://other.example/denied', {}], ['ftp://other.example/', {}]]) {
                const source = new EventSource(url, init);
                source.onmessage = e => { log.push(new URL(source.url).pathname + ':' + source.withCredentials + ':' + e.data + ':' + e.origin); source.close(); show(); };
                source.onerror = () => { log.push(new URL(source.url).pathname + ':error:' + source.readyState); show(); };
              }
            </script>
            """);

        // A response that fails the CORS check and a URL that cannot be fetched fail the connection
        Assert.Equal(["/:error:2", "/a:false:False:https://other.example", "/b:true:True:https://other.example", "/denied:error:2"], document.Title.Split('|').Order(StringComparer.Ordinal));
        Assert.All(loader.Requests, request => Assert.Equal("https://example.com", HeaderList.Get(request.Headers, "Origin")));
    }

    [Fact]
    public void Close_AbortsTheRequestAndDropsTheEvents()
    {
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var loader = new StubLoader(_ => new DocumentResource("data: a\n\ndata: b\n\n"u8.ToArray()) { Headers = [new("Content-Type", "text/event-stream")], RemainingContentBytes = chunks.Reader.ReadAllAsync() });
        var document = Load(loader, $$"""
            <script>
              {{LogScript}}
              const source = new EventSource('/events');
              listen(source);
              source.addEventListener('message', () => { source.close(); log.push('closed:' + source.readyState); show(); });
            </script>
            """);

        // The second event was received, but it is not dispatched once the event source is closed
        Assert.Equal("open:1|message:a::https://example.com|closed:2", document.Title);
        Assert.True(Assert.Single(loader.CancellationTokens).IsCancellationRequested);

        chunks.Writer.TryWrite("data: c\n\n"u8.ToArray());
        chunks.Writer.Complete();
        _clock.Advance(TimeSpan.FromMinutes(1));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("open:1|message:a::https://example.com|closed:2", document.Title);
        Assert.Single(loader.Requests);
    }

    [Fact]
    public void EventSource_IsClosed_WhenTheDocumentIsUnloaded()
    {
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var loader = new StubLoader(_ => new DocumentResource(ReadOnlyMemory<byte>.Empty) { Headers = [new("Content-Type", "text/event-stream")], RemainingContentBytes = chunks.Reader.ReadAllAsync() });
        var document = Load(loader, "<script>window.source = new EventSource('/events');</script>");
        var context = document.BrowsingContext!;
        var token = Assert.Single(loader.CancellationTokens);
        Assert.False(token.IsCancellationRequested);

        context.Navigate("https://example.com/next", _ => "");
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task EventSource_InAWorker()
    {
        var loader = new StubLoader(request => request.Url.EndsWith("worker.js", StringComparison.Ordinal)
            ? new DocumentResource("""
                const source = new EventSource('events');
                source.onmessage = e => { postMessage(source.url + ':' + e.data + ':' + e.lastEventId); source.close(); };
                """)
            { Headers = [new("Content-Type", "text/javascript")] }
            : new DocumentResource("id: 7\ndata: from the worker\n\n") { Headers = [new("Content-Type", "text/event-stream")] });
        var document = Load(loader, "<script>new Worker('/dir/worker.js').onmessage = e => document.title = e.data;</script>", timeProvider: TimeProvider.System);

        await WaitUntilAsync(() => document.Title.Length > 0, () => "no message");
        Assert.Equal("https://example.com/dir/events:from the worker:7", document.Title);
    }

    [Fact]
    public async Task EventSource_WithTheHttpLoader_ReadsTheResponseAsItArrives()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var secondEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            var request = await ReadRequestAsync(stream);

            // The response has no length: it ends when the connection is closed. The document has another port: the response
            // allows its origin
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nAccess-Control-Allow-Origin: http://127.0.0.1\r\nConnection: close\r\n\r\ndata: one\n\n"u8.ToArray(), TestContext.Current.CancellationToken);
            await stream.FlushAsync(TestContext.Current.CancellationToken);
            await secondEvent.Task.WaitAsync(TestContext.Current.CancellationToken);
            await stream.WriteAsync("data: two\n\n"u8.ToArray(), TestContext.Current.CancellationToken);
            await stream.FlushAsync(TestContext.Current.CancellationToken);

            // The client closes the connection when the event source is closed
            var buffer = new byte[16];
            while (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0)
            {
            }

            return request;
        }, TestContext.Current.CancellationToken);

        var document = Load(DocumentLoader.Create(), $$"""
            <script>
              {{LogScript}}
              const source = new EventSource('http://127.0.0.1:{{port}}/events');
              listen(source);
              source.addEventListener('message', e => { if (e.data === 'two') { source.close(); log.push('closed'); show(); } });
            </script>
            """, "http://127.0.0.1/", TimeProvider.System);

        // The first event is dispatched while the response is not complete
        await WaitUntilAsync(() => document.Title.Contains("message:one", StringComparison.Ordinal), () => document.Title);
        Assert.Equal($"open:1|message:one::http://127.0.0.1:{port}", document.Title);

        secondEvent.SetResult();
        await WaitUntilAsync(() => document.Title.EndsWith("closed", StringComparison.Ordinal), () => document.Title);
        Assert.Equal($"open:1|message:one::http://127.0.0.1:{port}|message:two::http://127.0.0.1:{port}|closed", document.Title);

        var request = await serverTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.StartsWith("GET /events HTTP/1.1\r\n", request, StringComparison.Ordinal);
        Assert.Contains("\r\nAccept: text/event-stream\r\n", request, StringComparison.Ordinal);
        Assert.Contains("\r\nOrigin: http://127.0.0.1\r\n", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetch_OfAnEventStream_GetsTheCompleteBody()
    {
        // The requests that do not interpret the body as it arrives wait for the end of the response
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var loader = new StubLoader(_ => new DocumentResource("data: a\n\n"u8.ToArray()) { Headers = [new("Content-Type", "text/event-stream")], RemainingContentBytes = chunks.Reader.ReadAllAsync() });
        var document = Load(loader, "<script>fetch('/events').then(response => response.text()).then(text => document.title = JSON.stringify(text));</script>");
        Assert.Equal("", document.Title);

        chunks.Writer.TryWrite("data: b\n\n"u8.ToArray());
        chunks.Writer.Complete();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("\"data: a\\n\\ndata: b\\n\\n\"", document.Title);
    }

    private static async Task<string> ReadRequestAsync(NetworkStream stream)
    {
        var head = new List<byte>();
        var buffer = new byte[1];
        while (head.Count < 4 || !head.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
        {
            if (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken) == 0)
                throw new IOException("The connection was closed before the end of the request");

            head.Add(buffer[0]);
        }

        return Encoding.Latin1.GetString([.. head]);
    }

    /// <summary>A loader that records the requests and answers them with a function.</summary>
    private sealed class StubLoader(Func<DocumentRequest, DocumentResource?> respond) : DocumentLoader
    {
        public Func<DocumentRequest, DocumentResource?> Respond { get; set; } = respond;

        public List<DocumentRequest> Requests { get; } = [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
                CancellationTokens.Add(cancellationToken);
            }

            return ValueTask.FromResult(Respond(request));
        }
    }
}
