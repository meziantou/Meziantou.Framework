using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>https://html.spec.whatwg.org/multipage/parsing.html#speculative-html-parsing</summary>
public sealed class SpeculativeParsingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly JintScriptEngine _engine = new();
    private readonly RecordingLoader _loader = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    /// <summary>Navigates to a document whose first script (<c>block.js</c>) is pending until <see cref="RecordingLoader.CompleteBlockingScript"/> is called.</summary>
    private Document Navigate(string html, bool speculativeParsing = true, KeyValuePair<string, string>[]? headers = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            SpeculativeParsing = speculativeParsing,
        });

        // The document is decoded from bytes (windows-1252 without encoding declaration)
        return context.Navigate("https://example.com/dir/page.html", _ => new DocumentResource(System.Text.Encoding.ASCII.GetBytes(html)) { Headers = headers ?? [new("Content-Type", "text/html")] });
    }

    private List<string> SpeculativeRequests => [.. _loader.Requests.Where(r => r.IsSpeculative).Select(r => r.Destination + " " + r.Url)];

    private List<string> Requests => [.. _loader.Requests.Where(r => !r.IsSpeculative).Select(r => r.Destination + " " + r.Url)];

    [Fact]
    public void WhileTheParserWaitsForAScript_TheResourcesOfTheRestOfTheDocumentAreFetched()
    {
        var document = Navigate("""
            <script src=block.js></script>
            <img src=a.png>
            <script src=b.js></script>
            <link rel=stylesheet href=c.css>
            <link rel=preload as=font href=d.woff2>
            <video poster=e.png></video>
            <svg><image href=f.png /><script href=g.js></script></svg>
            <picture><source srcset=h.png><img></picture>
            """);

        Assert.Equal(["Script https://example.com/dir/block.js"], Requests);
        Assert.Equal(
            [
                "Image https://example.com/dir/a.png",
                "Script https://example.com/dir/b.js",
                "Style https://example.com/dir/c.css",
                "Font https://example.com/dir/d.woff2",
                "Image https://example.com/dir/e.png",
                "Image https://example.com/dir/f.png",
                "Script https://example.com/dir/g.js",
                "Image https://example.com/dir/h.png",
            ], SpeculativeRequests);

        // The speculative requests are created as the requests of the parser
        Assert.All(_loader.Requests.Where(r => r.IsSpeculative), request =>
        {
            Assert.Null(request.Initiator);
            Assert.Equal("https://example.com", request.Origin);
            Assert.Equal("https://example.com/dir/page.html", request.Referrer);
        });
        Assert.Equal(RequestMode.NoCors, _loader.Requests.Single(r => r.Destination == RequestDestination.Font).Mode);

        // The parser fetches the resources again when it reaches the elements
        _loader.CompleteBlockingScript("");
        _contexts.EventLoop.RunUntilIdle();
        Assert.NotNull(document.QuerySelector("video"));
        Assert.Contains("Image https://example.com/dir/a.png", Requests);
        Assert.Contains("Style https://example.com/dir/c.css", Requests);
    }

    /// <summary>Navigates to a document whose bytes arrive over time: the first bytes, then the ones written to <paramref name="chunks"/>.</summary>
    private Document NavigateStreamed(string firstBytes, System.Threading.Channels.Channel<ReadOnlyMemory<byte>> chunks, KeyValuePair<string, string>[]? headers = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            SpeculativeParsing = true,
        });

        return context.Navigate("https://example.com/dir/page.html", _ => new DocumentResource(System.Text.Encoding.ASCII.GetBytes(firstBytes))
        {
            Headers = headers ?? [new("Content-Type", "text/html")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });
    }

    [Fact]
    public async Task WhileTheParserWaitsForAScript_TheChunksThatArriveAreParsedSpeculatively()
    {
        var chunks = System.Threading.Channels.Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = NavigateStreamed("<script src=block.js></script><img src=a.png>", chunks, [new("Content-Type", "text/html; charset=utf-8")]);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Image https://example.com/dir/a.png"], SpeculativeRequests);

        chunks.Writer.TryWrite("<img src=b.png>"u8.ToArray());
        await _contexts.EventLoop.RunUntilAsync(() => SpeculativeRequests.Count == 2, TestContext.Current.CancellationToken);
        Assert.Equal(["Image https://example.com/dir/a.png", "Image https://example.com/dir/b.png"], SpeculativeRequests);
        Assert.Equal(["Script https://example.com/dir/block.js"], Requests);

        _loader.CompleteBlockingScript("");
        chunks.Writer.Complete();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
        Assert.Contains("Image https://example.com/dir/b.png", Requests);
    }

    [Fact]
    public async Task WhileTheEncodingIsNotDetermined_TheBytesReceivedAreParsedSpeculatively()
    {
        // Without encoding declaration in the first bytes, the document waits for the bytes that determine its encoding,
        // and the bytes received are parsed speculatively with the default encoding (windows-1252)
        var chunks = System.Threading.Channels.Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = NavigateStreamed("<!DOCTYPE html><script src=\"a.js?&#xE9;\"></script>", chunks);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Script https://example.com/dir/a.js?%E9"], SpeculativeRequests);
        Assert.Empty(Requests);
        Assert.Null(document.QuerySelector("script"));

        // The meta element of the second chunk determines the encoding: the parser fetches the script with it
        chunks.Writer.TryWrite("<meta charset=utf-8>"u8.ToArray());
        chunks.Writer.Complete();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("UTF-8", document.CharacterSet);
        Assert.Equal(["Script https://example.com/dir/a.js?%C3%A9"], Requests);
        Assert.Single(SpeculativeRequests);
    }

    [Fact]
    public void DisabledByDefault()
    {
        Navigate("<script src=block.js></script><img src=a.png>", speculativeParsing: false);
        Assert.Empty(SpeculativeRequests);
    }

    [Fact]
    public void TheResourcesThatTheParserWouldNotFetch_AreNotFetched()
    {
        Navigate("""
            <script src=block.js></script>
            <template><img src=template.png><script src=template.js></script></template>
            <script src=plain.js type=text/plain></script>
            <script src=nomodule.js nomodule></script>
            <link rel="alternate stylesheet" href=alternate.css>
            <link rel=stylesheet href=disabled.css disabled>
            <link rel=stylesheet href=media.css media="not all">
            <link rel=stylesheet href=type.css type=text/plain>
            <link href=norel.css>
            <img src=lazy.png loading=lazy>
            <img data-src=data.png>
            <picture><source srcset=nomatch.png media="not all"></picture>
            <svg><script src=svg.js></script><image src=svg.png /></svg>
            <math><script src=math.js></script></math>
            <xmp><script src=xmp.js></script></xmp>
            <img src="data:image/png;base64,">
            """);

        Assert.Empty(SpeculativeRequests);
    }

    [Fact]
    public void TheContentsOfDeclarativeShadowRoots_AreFetched()
    {
        Navigate("<script src=block.js></script><div><template shadowrootmode=open><img src=shadow.png></template></div><template><div><template shadowrootmode=open><img src=inert.png></template></div></template>");
        Assert.Equal(["Image https://example.com/dir/shadow.png"], SpeculativeRequests);
    }

    [Fact]
    public void BaseAndMetaReferrer_ApplyToTheSubsequentFetches()
    {
        Navigate("""
            <script src=block.js></script>
            <img src=before.png>
            <base href=https://cdn.example.com/assets/>
            <meta name=referrer content=no-referrer>
            <img src=after.png>
            """);

        Assert.Equal(["Image https://example.com/dir/before.png", "Image https://cdn.example.com/assets/after.png"], SpeculativeRequests);
        Assert.Equal("https://example.com/dir/page.html", _loader.Requests.Single(r => r.Url.EndsWith("before.png", StringComparison.Ordinal)).Referrer);
        Assert.Null(_loader.Requests.Single(r => r.Url.EndsWith("after.png", StringComparison.Ordinal)).Referrer);
    }

    [Fact]
    public void TheBaseOfTheDocument_AppliesToTheSpeculativeFetches()
    {
        Navigate("<base href=https://cdn.example.com/><script src=block.js></script><img src=a.png>");
        Assert.Equal(["Image https://cdn.example.com/a.png"], SpeculativeRequests);
    }

    [Fact]
    public void TheSpeculativeParserContinuesFromTheStateOfTheParser()
    {
        // The table and the formatting elements are open: the image is inserted (foster parented) in the body
        Navigate("<table><tr><td><b><script src=block.js></script><img src=a.png></b></td></tr></table><noscript><img src=noscript.png></noscript>");
        Assert.Equal(["Image https://example.com/dir/a.png"], SpeculativeRequests);
    }

    [Fact]
    public void TheMarkupWrittenAfterTheBlockingScript_IsNotParsed_ButItsResourcesWereFetchedSpeculatively()
    {
        var document = Navigate("<script src=block.js></script><img src=a.png>");
        _loader.CompleteBlockingScript("document.write('<plaintext>')");
        _contexts.EventLoop.RunUntilIdle();

        Assert.Null(document.QuerySelector("img"));
        Assert.Equal(["Image https://example.com/dir/a.png"], SpeculativeRequests);
        Assert.Equal(["Script https://example.com/dir/block.js"], Requests);
    }

    [Fact]
    public void TheMarkupWrittenByAScript_IsParsedSpeculatively()
    {
        Navigate("<script>document.write('<script src=block.js><\\/script><img src=written.png>')</script><img src=a.png>");
        Assert.Equal(["Image https://example.com/dir/written.png", "Image https://example.com/dir/a.png"], SpeculativeRequests);
    }

    [Fact]
    public void EachUrlIsFetchedSpeculativelyOnce()
    {
        Navigate("<script src=block.js></script><img src=a.png><img src=a.png><link rel=stylesheet href=a.png>");
        Assert.Equal(["Image https://example.com/dir/a.png"], SpeculativeRequests);
    }

    [Fact]
    public void DocumentPolicyExpectNoLinkedResources_DisablesTheSpeculativeParsing()
    {
        Navigate("<script src=block.js></script><img src=a.png>", headers: [new("Content-Type", "text/html"), new("Document-Policy", "expect-no-linked-resources")]);
        Assert.Empty(SpeculativeRequests);
    }

    [Fact]
    public void MetaCharset_DoesNotChangeTheEncodingOfTheSpeculativeFetches()
    {
        // The encoding declaration is after the 1024 bytes of the prescan, outside of the head
        Navigate("<!--" + new string(' ', 1024) + "--><body><script src=block.js></script><meta charset=windows-1254><img src=\"a.png?&#x11E;\">");
        Assert.Equal(["Image https://example.com/dir/a.png?%26%23286%3B"], SpeculativeRequests);
    }

    [Fact]
    public void MetaCharsetInTheHeadAfterTheFirstKilobyte_ChangesTheEncodingWhenTheParserInsertsIt()
    {
        var document = Navigate("<head><!--" + new string(' ', 1024) + "--><script src=block.js></script><meta charset=windows-1254><link rel=stylesheet href=\"a.css?&#x11E;\">");
        Assert.Equal(["Style https://example.com/dir/a.css?%26%23286%3B"], SpeculativeRequests);
        Assert.Equal("windows-1252", document.CharacterSet);

        _loader.CompleteBlockingScript("");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("windows-1254", document.CharacterSet);
        Assert.Contains("Style https://example.com/dir/a.css?%D0", Requests);
    }

    /// <summary>Records the requests. The blocking script is pending; the other resources are empty responses.</summary>
    private sealed class RecordingLoader : DocumentLoader
    {
        private readonly TaskCompletionSource<DocumentResource?> _blockingScript = new();

        public List<DocumentRequest> Requests { get; } = [];

        public void CompleteBlockingScript(string content) => _blockingScript.SetResult(new DocumentResource(content) { Headers = [new("Content-Type", "text/javascript")] });

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Url.EndsWith("/block.js", StringComparison.Ordinal) && !request.IsSpeculative)
                return new ValueTask<DocumentResource?>(_blockingScript.Task);

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("") { Headers = [new("Content-Type", "text/plain")] });
        }
    }
}
