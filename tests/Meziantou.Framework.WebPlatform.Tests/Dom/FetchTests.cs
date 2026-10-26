using System.Text;
using System.Threading.Channels;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The Fetch Standard (Headers, fetch(), Request, Response) and XMLHttpRequest, with a stub loader.</summary>
public sealed class FetchTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly StubLoader _loader = new();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            DocumentLoader = _loader,
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>Runs the tasks of the event loop until the condition is true (a body read as it arrives is not awaited by RunUntilIdle).</summary>
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

    private const string StreamLogScript = """
        const log = [];
        const show = () => document.title = log.join('|');
        const decoder = new TextDecoder();
        async function readAll(response) {
          const reader = response.body.getReader();
          try {
            while (true) {
              const { done, value } = await reader.read();
              if (done) { log.push('done'); break; }
              log.push(decoder.decode(value));
              show();
            }
          } catch (e) {
            log.push(e.name);
          }

          show();
        }
        """;

    [Fact]
    public void Headers_CombinesTheValuesAndSortsTheNames()
    {
        var headers = new Headers([new KeyValuePair<string, string>("X-B", "1"), new("Set-Cookie", "a"), new("x-b", " 2 "), new("Set-Cookie", "b"), new("A", "c")]);
        Assert.Equal("1, 2", headers.Get("X-B"));
        Assert.True(headers.Has("x-b"));
        Assert.Null(headers.Get("missing"));
        Assert.Equal(["a", "b"], headers.GetSetCookie());
        Assert.Equal(["a: c", "set-cookie: a", "set-cookie: b", "x-b: 1, 2"], headers.Select(header => header.Key + ": " + header.Value));

        headers.Set("X-B", "3");
        headers.Delete("Set-Cookie");
        Assert.Equal(["a: c", "x-b: 3"], headers.Select(header => header.Key + ": " + header.Value));
    }

    [Fact]
    public void Headers_InvalidNameOrValue_ThrowsTypeError()
    {
        var headers = new Headers();
        Assert.Throws<TypeErrorException>(() => headers.Append("a b", "c"));
        Assert.Throws<TypeErrorException>(() => headers.Append("a", "b\nc"));
        Assert.Throws<TypeErrorException>(() => new Headers([(IReadOnlyList<string>)["a"]]));
    }

    [Fact]
    public void Fetch_ReadsTheResponse()
    {
        _loader.Responses["https://example.com/data.json"] = new DocumentResource("{\"a\": 1}") { Headers = [new("Content-Type", "application/json"), new("X-Test", "value")] };
        var document = Load("""
            <script>
              fetch('data.json').then(async response => {
                const json = await response.json();
                document.title = [response.status, response.statusText, response.ok, response.type, response.url, response.headers.get('x-test'), json.a, response.bodyUsed].join('|');
              });
            </script>
            """);
        Assert.Equal("200|OK|true|basic|https://example.com/data.json|value|1|true", document.Title);

        var request = Assert.Single(_loader.Requests, request => request.Url == "https://example.com/data.json");
        Assert.Equal(RequestDestination.Empty, request.Destination);
        Assert.False(request.FollowRedirects);
        Assert.Contains(new KeyValuePair<string, string>("Accept", "*/*"), request.Headers);
    }

    [Fact]
    public void Fetch_SendsTheMethodTheHeadersAndTheBody()
    {
        _loader.Responses["https://example.com/post"] = new DocumentResource("ok");
        var document = Load("""
            <script>
              fetch('post', { method: 'POST', headers: { 'X-Custom': 'a' }, body: new URLSearchParams('a=b c') }).then(response => response.text()).then(text => { document.title = text; });
            </script>
            """);
        Assert.Equal("ok", document.Title);

        var request = Assert.Single(_loader.Requests, request => request.Url == "https://example.com/post");
        Assert.Equal("POST", request.Method);
        Assert.Equal("a=b+c", Encoding.UTF8.GetString(request.Body!.Value.Span));
        Assert.Contains(new KeyValuePair<string, string>("X-Custom", "a"), request.Headers);
        Assert.Contains(new KeyValuePair<string, string>("Content-Type", "application/x-www-form-urlencoded;charset=UTF-8"), request.Headers);
        Assert.Contains(new KeyValuePair<string, string>("Origin", "https://example.com"), request.Headers);
    }

    [Fact]
    public void Fetch_CrossOriginWithoutCors_IsANetworkError()
    {
        _loader.Responses["https://other.example/denied"] = new DocumentResource("secret");
        _loader.Responses["https://other.example/allowed"] = new DocumentResource("public") { Headers = [new("Access-Control-Allow-Origin", "*")] };
        var document = Load("""
            <script>
              Promise.all([
                fetch('https://other.example/denied').then(() => 'resolved', e => e.name),
                fetch('https://other.example/allowed').then(r => r.type + ':' + r.status),
                fetch('https://other.example/denied', { mode: 'no-cors' }).then(r => r.type + ':' + r.status),
              ]).then(results => { document.title = results.join('|'); });
            </script>
            """);
        Assert.Equal("TypeError|cors:200|opaque:0", document.Title);
    }

    [Fact]
    public void Fetch_FollowsTheRedirects()
    {
        _loader.Responses["https://example.com/redirect"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "/final")] };
        _loader.Responses["https://example.com/final"] = new DocumentResource("final");
        var document = Load("""
            <script>
              Promise.all([
                fetch('redirect').then(async r => [r.url, r.redirected, await r.text()].join()),
                fetch('redirect', { redirect: 'manual' }).then(r => r.type + ':' + r.status),
                fetch('redirect', { redirect: 'error' }).then(() => 'resolved', e => e.name),
              ]).then(results => { document.title = results.join('|'); });
            </script>
            """);
        Assert.Equal("https://example.com/final,true,final|opaqueredirect:0|TypeError", document.Title);
    }

    [Fact]
    public void Fetch_DataAndBlobUrls()
    {
        var document = Load("""
            <script>
              Promise.all([
                fetch('data:text/plain,hello').then(r => r.text()),
                fetch(URL.createObjectURL(new Blob(['blob'], { type: 'text/plain' }))).then(async r => r.headers.get('content-type') + ':' + await r.text()),
              ]).then(results => { document.title = results.join('|'); });
            </script>
            """);
        Assert.Equal("hello|text/plain:blob", document.Title);
    }

    [Fact]
    public void Fetch_Abort_RejectsWithTheReason()
    {
        _loader.Responses["https://example.com/data"] = new DocumentResource("data");
        var document = Load("""
            <script>
              const controller = new AbortController();
              fetch('data', { signal: controller.signal }).then(() => { document.title = 'resolved'; }, e => { document.title = e.name; });
              controller.abort();
            </script>
            """);
        Assert.Equal("AbortError", document.Title);
    }

    [Fact]
    public void RequestAndResponse()
    {
        var document = Load("""
            <script>
              const request = new Request('a?b', { method: 'post', body: 'body', headers: [['X-A', '1']] });
              const clone = request.clone();
              const response = new Response(new Blob(['{"x":2}'], { type: 'application/json' }), { status: 201, headers: { 'X-B': '2' } });
              Promise.all([clone.text(), response.json()]).then(([text, json]) => {
                document.title = [request.method, request.url, request.headers.get('x-a'), request.headers.get('content-type'), text, request.bodyUsed, clone.bodyUsed,
                  response.status, response.headers.get('content-type'), json.x, Response.error().type, Response.redirect('/x', 301).headers.get('location')].join('|');
              });
            </script>
            """);
        Assert.Equal("POST|https://example.com/a?b|1|text/plain;charset=UTF-8|body|false|true|201|application/json|2|error|https://example.com/x", document.Title);
    }

    [Fact]
    public void XmlHttpRequest_Asynchronous()
    {
        _loader.Responses["https://example.com/text"] = new DocumentResource("hello") { Headers = [new("Content-Type", "text/plain"), new("X-Test", "1")] };
        var document = Load("""
            <script>
              const xhr = new XMLHttpRequest();
              const events = [];
              xhr.onreadystatechange = () => events.push('rs' + xhr.readyState);
              for (const type of ['loadstart', 'progress', 'load', 'loadend']) xhr.addEventListener(type, () => events.push(type));
              xhr.onloadend = () => { document.title = [events.join(' '), xhr.status, xhr.statusText, xhr.responseText, xhr.responseURL, xhr.getResponseHeader('x-test'), xhr.getAllResponseHeaders().replaceAll('\r\n', ';')].join('|'); };
              xhr.open('GET', 'text');
              xhr.send();
            </script>
            """);
        Assert.Equal("rs1 loadstart rs2 rs3 progress rs4 load loadend|200|OK|hello|https://example.com/text|1|content-type: text/plain;x-test: 1;", document.Title);
    }

    [Fact]
    public void XmlHttpRequest_Synchronous()
    {
        _loader.Responses["https://example.com/echo"] = new DocumentResource("<root><a/></root>") { Headers = [new("Content-Type", "application/xml")] };
        var document = Load("""
            <script>
              const xhr = new XMLHttpRequest();
              xhr.open('POST', 'echo', false);
              xhr.setRequestHeader('X-Custom', 'value');
              xhr.send('body');
              document.title = [xhr.readyState, xhr.responseXML.documentElement.localName, xhr.responseXML.contentType].join('|');
            </script>
            """);
        Assert.Equal("4|root|application/xml", document.Title);

        var request = Assert.Single(_loader.Requests, request => request.Url == "https://example.com/echo");
        Assert.True(request.IsSynchronous);
        Assert.Equal("body", Encoding.UTF8.GetString(request.Body!.Value.Span));
        Assert.Contains(new KeyValuePair<string, string>("X-Custom", "value"), request.Headers);
        Assert.Contains(new KeyValuePair<string, string>("Content-Type", "text/plain;charset=UTF-8"), request.Headers);
    }

    [Fact]
    public void XmlHttpRequest_ResponseTypes()
    {
        _loader.Responses["https://example.com/json"] = new DocumentResource("{\"a\":[1,2]}") { Headers = [new("Content-Type", "application/json")] };
        _loader.Responses["https://example.com/html"] = new DocumentResource("<title>t</title>") { Headers = [new("Content-Type", "text/html")] };
        var document = Load("""
            <script>
              function load(url, type) {
                return new Promise(resolve => {
                  const xhr = new XMLHttpRequest();
                  xhr.open('GET', url);
                  xhr.responseType = type;
                  xhr.responseType = 'invalid';
                  xhr.onload = () => resolve(xhr.response);
                  xhr.send();
                });
              }

              Promise.all([load('json', 'json'), load('html', 'document'), load('json', 'arraybuffer'), load('json', 'blob')]).then(([json, doc, buffer, blob]) => {
                document.title = [json.a.join(), doc.title, buffer.byteLength, blob.type, blob.size].join('|');
              });
            </script>
            """);
        Assert.Equal("1,2|t|11|application/json|11", document.Title);
    }

    [Fact]
    public void XmlHttpRequest_DocumentResponse_TheEncodingIsNotDetected()
    {
        // Japanese text in Shift_JIS: the encoding of a document response is the charset of the response, else the one the
        // prescan finds, else UTF-8 (https://xhr.spec.whatwg.org/#document-response)
        byte[] body = [.. "<p>"u8, 0x82, 0xB1, 0x82, 0xEA, 0x82, 0xCD, 0x95, 0xB6, 0x8E, 0x9A, 0x8E, 0xC0, 0x8C, 0xB1, 0x82, 0xC5, 0x82, 0xB7, 0x81, 0x42];
        _loader.Responses["https://example.com/html"] = new DocumentResource(body) { Headers = [new("Content-Type", "text/html")] };
        var document = Load("""
            <script>
              const xhr = new XMLHttpRequest();
              xhr.open('GET', 'html');
              xhr.responseType = 'document';
              xhr.onload = () => { document.title = xhr.response.characterSet; };
              xhr.send();
            </script>
            """);
        Assert.Equal("UTF-8", document.Title);
    }

    [Fact]
    public void XmlHttpRequest_NetworkError()
    {
        var document = Load("""
            <script>
              const xhr = new XMLHttpRequest();
              const events = [];
              for (const type of ['load', 'error', 'loadend']) xhr.addEventListener(type, () => events.push(type));
              xhr.onloadend = () => { document.title = events.join(' ') + '|' + xhr.status; };
              xhr.open('GET', 'https://other.example/missing');
              xhr.send();
            </script>
            """);
        Assert.Equal("error loadend|0", document.Title);
    }

    [Fact]
    public async Task Fetch_StreamsTheResponseBody()
    {
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        _loader.Responses["https://example.com/stream"] = new DocumentResource("a"u8.ToArray()) { Headers = [new("Content-Type", "text/plain")], RemainingContentBytes = chunks.Reader.ReadAllAsync() };
        var document = Load($$"""
            <script>
              {{StreamLogScript}}
              fetch('stream').then(response => { log.push('response:' + response.status); show(); return readAll(response); });
            </script>
            """);

        // fetch() resolves once the headers are received, and the chunks are read as they arrive
        await WaitUntilAsync(() => document.Title == "response:200|a", () => document.Title);
        Assert.True(Assert.Single(_loader.Requests, request => request.Url == "https://example.com/stream").StreamsResponseBody);

        chunks.Writer.TryWrite("b"u8.ToArray());
        await WaitUntilAsync(() => document.Title == "response:200|a|b", () => document.Title);

        chunks.Writer.TryWrite(ReadOnlyMemory<byte>.Empty);
        chunks.Writer.TryWrite("c"u8.ToArray());
        chunks.Writer.TryComplete();
        await WaitUntilAsync(() => document.Title.EndsWith("|done", StringComparison.Ordinal), () => document.Title);
        Assert.Equal("response:200|a|b|c|done", document.Title);
    }

    [Fact]
    public async Task Fetch_BodyFailingAfterTheResponse_ErrorsTheStream()
    {
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        _loader.Responses["https://example.com/stream"] = new DocumentResource("a"u8.ToArray()) { RemainingContentBytes = chunks.Reader.ReadAllAsync() };
        var document = Load($$"""
            <script>
              {{StreamLogScript}}
              fetch('stream').then(response => readAll(response), e => { log.push('rejected:' + e.name); show(); });
            </script>
            """);

        await WaitUntilAsync(() => document.Title == "a", () => document.Title);
        chunks.Writer.TryComplete(new IOException("The chunked encoding is invalid"));
        await WaitUntilAsync(() => document.Title != "a", () => document.Title);
        Assert.Equal("a|TypeError", document.Title);
    }

    [Fact]
    public async Task Fetch_AbortAfterTheResponse_StopsReadingTheBody()
    {
        var body = new TrackedBody();
        _loader.Responses["https://example.com/stream"] = new DocumentResource("a"u8.ToArray()) { RemainingContentBytes = body };
        var document = Load($$"""
            <script>
              {{StreamLogScript}}
              const controller = new AbortController();
              fetch('stream', { signal: controller.signal }).then(response => {
                readAll(response);
                window.abortFetch = () => controller.abort();
              });
            </script>
            """);

        await WaitUntilAsync(() => document.Title == "a" && body.IsReading, () => document.Title);
        TestScriptEngines.Evaluate(_engine, document.DefaultView!, "abortFetch()");
        await WaitUntilAsync(() => document.Title == "a|AbortError" && body.IsDisposed, () => document.Title + ":" + body.IsDisposed);
        Assert.True(body.WasCanceled);
    }

    [Fact]
    public async Task Fetch_CancelingTheBody_StopsReadingTheBody()
    {
        var body = new TrackedBody();
        _loader.Responses["https://example.com/stream"] = new DocumentResource("a"u8.ToArray()) { RemainingContentBytes = body };
        var document = Load("""
            <script>
              fetch('stream').then(async response => {
                const reader = response.body.getReader();
                const { value } = await reader.read();
                await reader.cancel();
                document.title = new TextDecoder().decode(value) + ':canceled';
              });
            </script>
            """);

        await WaitUntilAsync(() => document.Title == "a:canceled" && body.IsDisposed, () => document.Title + ":" + body.IsDisposed);
    }

    [Fact]
    public async Task HttpDocumentLoader_StreamsTheBodyOfFetch()
    {
        using var handler = new StubHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("body") });
        using var client = new System.Net.Http.HttpClient(handler);
        var resource = await DocumentLoader.Create(client).LoadAsync(new DocumentRequest("https://example.com/stream")
        {
            Destination = RequestDestination.Empty,
            FollowRedirects = false,
            StreamsResponseBody = true,
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(resource);
        Assert.NotNull(resource.RemainingContentBytes);
        var body = new List<byte>();
        await foreach (var chunk in resource.RemainingContentBytes.WithCancellation(TestContext.Current.CancellationToken))
        {
            body.AddRange(chunk.ToArray());
        }

        Assert.Equal("body", Encoding.UTF8.GetString([.. body]));
    }

    [Fact]
    public async Task HttpDocumentLoader_DoesNotFollowTheRedirectsOfFetch()
    {
        var requests = new List<string>();
        using var handler = new StubHandler(request =>
        {
            requests.Add(request.Method + " " + string.Join(";", request.Headers.Select(header => header.Key + "=" + string.Join(",", header.Value))));
            var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Found);
            response.Headers.Location = new Uri("/final", UriKind.Relative);
            return response;
        });

        using var client = new System.Net.Http.HttpClient(handler);
        var resource = await DocumentLoader.Create(client).LoadAsync(new DocumentRequest("https://example.com/start")
        {
            Method = "PUT",
            Destination = RequestDestination.Empty,
            Headers = [new("X-Test", "1"), new("Accept", "*/*")],
            FollowRedirects = false,
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(resource);
        Assert.Equal(302, resource.StatusCode);
        Assert.Equal("Found", resource.StatusText);
        Assert.Equal(["PUT X-Test=1;Accept=*/*"], requests);
    }

    private sealed class StubLoader : DocumentLoader
    {
        public Dictionary<string, DocumentResource> Responses { get; } = new(StringComparer.Ordinal);

        public List<DocumentRequest> Requests { get; } = [];

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(Responses.GetValueOrDefault(request.Url));
        }
    }

    /// <summary>A body whose next chunk never arrives: records whether it is being read, canceled and disposed.</summary>
    private sealed class TrackedBody : IAsyncEnumerable<ReadOnlyMemory<byte>>
    {
        private volatile bool _isReading;
        private volatile bool _wasCanceled;
        private volatile bool _isDisposed;

        public bool IsReading => _isReading;

        public bool WasCanceled => _wasCanceled;

        public bool IsDisposed => _isDisposed;

        public async IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            try
            {
                _isReading = true;
                await Task.Delay(Timeout.Infinite, cancellationToken);
                yield break;
            }
            finally
            {
                _wasCanceled = cancellationToken.IsCancellationRequested;
                _isDisposed = true;
            }
        }
    }

    private sealed class StubHandler(Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
