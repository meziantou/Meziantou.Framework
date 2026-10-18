using System.Net;
using System.Net.Http;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class HttpDocumentLoaderTests
{
    [Fact]
    public void Create_WithoutClient_ReturnsTheSharedLoader()
    {
        Assert.Same(DocumentLoader.Create(), DocumentLoader.Create(httpClient: null));

        using var client = new HttpClient();
        Assert.NotSame(DocumentLoader.Create(), DocumentLoader.Create(client));
    }

    [Fact]
    public async Task LoadAsync_FollowsRedirectsWithCookies()
    {
        var requests = new List<string>();
        using var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri + " " + string.Join(";", request.Headers.TryGetValues("Cookie", out var values) ? values : []));
            if (request.RequestUri.AbsolutePath == "/start")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("/final", UriKind.Relative);
                redirect.Headers.Add("Set-Cookie", "session=abc; Path=/");
                return redirect;
            }

            // windows-1252: 0xE9 is é
            var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([(byte)'<', (byte)'p', (byte)'>', 0xE9]) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = "windows-1252" };
            response.Headers.Add("X-Test", ["a", "b"]);
            return response;
        });

        using var client = new HttpClient(handler);
        var cookies = new CookieJar();
        var resource = await DocumentLoader.Create(client).LoadAsync(new DocumentRequest("https://example.com/start") { CookieJar = cookies }, TestContext.Current.CancellationToken);

        Assert.Equal(["https://example.com/start ", "https://example.com/final session=abc"], requests);
        Assert.NotNull(resource);
        Assert.Equal("https://example.com/final", resource.Url);
        Assert.Equal(404, resource.StatusCode);
        Assert.Contains(new KeyValuePair<string, string>("Content-Type", "text/html; charset=windows-1252"), resource.Headers);
        Assert.Equal(["a", "b"], resource.Headers.Where(header => header.Key == "X-Test").Select(header => header.Value));
        Assert.Equal(4, resource.ContentBytes!.Value.Length);
        Assert.Equal("abc", cookies.GetCookies("https://example.com/").Single(cookie => cookie.Name == "session").Value);
    }

    [Theory]
    [InlineData(null, "https://example.com/dir/page.html?q", "https://example.com/")]
    [InlineData("no-referrer", "https://example.com/dir/page.html?q", null)]
    [InlineData("unsafe-url", "https://example.com/dir/page.html?q", "https://example.com/dir/page.html?q")]
    public async Task LoadAsync_SendsTheReferrerOfEachRequestOfTheRedirectChain(string? redirectReferrerPolicy, string expectedFirst, string? expectedSecond)
    {
        // https://fetch.spec.whatwg.org/#main-fetch: the referrer is determined again for each URL, with the Referrer-Policy of the redirect
        var referrers = new List<string?>();
        using var handler = new StubHandler(request =>
        {
            referrers.Add(request.Headers.TryGetValues("Referer", out var values) ? values.Single() : null);
            if (request.RequestUri!.AbsolutePath == "/start")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://other.example/final");
                if (redirectReferrerPolicy is not null)
                {
                    redirect.Headers.Add("Referrer-Policy", redirectReferrerPolicy);
                }

                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });

        using var client = new HttpClient(handler);
        var request = new DocumentRequest("https://example.com/start") { Referrer = "https://example.com/dir/page.html?q", ReferrerPolicy = "strict-origin-when-cross-origin" };
        await DocumentLoader.Create(client).LoadAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal([expectedFirst, expectedSecond], referrers);
    }

    [Fact]
    public async Task LoadAsync_OtherSchemes_AreNetworkErrors()
    {
        using var handler = new StubHandler(_ => throw new InvalidOperationException("No request expected"));
        using var client = new HttpClient(handler);
        var loader = DocumentLoader.Create(client);
        Assert.Null(await loader.LoadAsync(new DocumentRequest("file:///etc/passwd"), TestContext.Current.CancellationToken));
        Assert.Null(await loader.LoadAsync(new DocumentRequest("data:text/html,test"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BrowsingContext_DecodesTheResponse()
    {
        using var handler = new StubHandler(request =>
        {
            // No charset parameter: the <meta charset> declares the encoding
            var bytes = System.Text.Encoding.Latin1.GetBytes("<meta charset=windows-1252><title>café</title>");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });

        using var client = new HttpClient(handler);
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = DocumentLoader.Create(client) });
        var document = context.Navigate("https://example.com/", _ => "<iframe src='frame.html'></iframe>");
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        var frameDocument = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentDocument!;
        Assert.Equal("café", frameDocument.Title);
        Assert.Equal("windows-1252", frameDocument.CharacterSet);
        Assert.Equal("https://example.com/frame.html", frameDocument.Url);
    }

    [Fact]
    public async Task BrowsingContext_ParsesTheDocumentOfANavigationAsItsBodyArrives()
    {
        var body = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        using var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChannelStream(body.Reader)) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
            return response;
        });

        using var client = new HttpClient(handler);
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = DocumentLoader.Create(client) });
        context.Navigate("https://example.com/");
        body.Writer.TryWrite("<p id=a>a</p>"u8.ToArray());
        await context.EventLoop.RunUntilAsync(() => context.Document.GetElementById("a") is not null, TestContext.Current.CancellationToken);
        var document = context.Document;
        Assert.Equal(DocumentReadyState.Loading, document.ReadyState);

        body.Writer.TryWrite("<p id=b>b</p>"u8.ToArray());
        body.Writer.Complete();
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(document.GetElementById("b"));
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    /// <summary>A stream whose bytes are written to a channel.</summary>
    private sealed class ChannelStream(System.Threading.Channels.ChannelReader<byte[]> reader) : Stream
    {
        private ReadOnlyMemory<byte> _current;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_current.IsEmpty)
            {
                if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    return 0;

                if (reader.TryRead(out var chunk))
                {
                    _current = chunk;
                }
            }

            var count = Math.Min(buffer.Length, _current.Length);
            _current[..count].CopyTo(buffer);
            _current = _current[count..];
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
