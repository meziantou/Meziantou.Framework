using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The detection of the encoding of the documents that do not declare one
/// (https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding): <see cref="EncodingDetector"/>.
/// </summary>
public sealed class EncodingDetectionTests : IDisposable
{
    // "This is a test of the characters." in Japanese, and in Russian
    private const string Japanese = "\U00003053\U0000308C\U0000306F\U00006587\U00005B57\U00005B9F\U00009A13\U00003067\U00003059\U00003002";
    private const string Russian = "\U0000042D\U00000442\U0000043E \U00000442\U00000435\U00000441\U00000442 \U0000043A\U0000043E\U00000434\U00000438\U00000440\U0000043E\U00000432\U0000043A\U00000438 \U00000441\U00000438\U0000043C\U00000432\U0000043E\U0000043B\U0000043E\U00000432.";

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private static byte[] Encode(string text, string encodingLabel)
    {
        Assert.True(WebEncodingRegistry.TryGetEncoding(encodingLabel, out var encoding));
        if (encoding == WebEncodingId.Utf8)
            return Encoding.UTF8.GetBytes(text);

        var bytes = new ArrayBufferWriter<byte>();
        WebEncodingRegistry.CreateEncoder(encoding).EncodeHtml(text, bytes);
        return bytes.WrittenSpan.ToArray();
    }

    private static Document Parse(byte[] bytes, HtmlParserOptions? options = null) => new HtmlDocumentParser(options).Parse(bytes);

    [Theory]
    [InlineData(Japanese, "Shift_JIS")]
    [InlineData(Japanese, "EUC-JP")]
    [InlineData(Russian, "windows-1251")]
    [InlineData(Russian, "KOI8-U")]
    [InlineData(Russian, "IBM866")]
    public void Parse_DetectsTheLegacyEncodings(string text, string encoding)
    {
        var document = Parse(Encode("<!DOCTYPE html><title>test</title><p>" + text, encoding));
        Assert.Equal(encoding, document.CharacterSet);
        Assert.Equal(text, document.QuerySelector("p")!.TextContent);
    }

    [Theory]
    [InlineData(null, "UTF-8")]
    [InlineData("file:///C:/test.html", "UTF-8")]
    [InlineData("https://example.com/", "windows-1252")]
    [InlineData("http://example.com/", "windows-1252")]
    public void Parse_Utf8IsOnlyDetectedForTheDocumentsThatDoNotComeFromTheNetwork(string? url, string expected)
    {
        var document = Parse(Encode("<p>" + Japanese, "UTF-8"), new HtmlParserOptions { Url = url });
        Assert.Equal(expected, document.CharacterSet);
    }

    [Fact]
    public void Parse_NotDetected_DefaultEncoding()
    {
        // The bytes are valid UTF-8, which is not detected for a document of the network
        var document = Parse(Encode("<p>" + Japanese, "UTF-8"), new HtmlParserOptions { Url = "https://example.com/", DefaultEncoding = "windows-1251" });
        Assert.Equal("windows-1251", document.CharacterSet);

        // ASCII bytes
        document = Parse("<p>test"u8.ToArray(), new HtmlParserOptions { DefaultEncoding = "windows-1251" });
        Assert.Equal("windows-1251", document.CharacterSet);
    }

    [Fact]
    public void Parse_WithoutDetector_DefaultEncoding()
    {
        var document = Parse(Encode("<p>" + Japanese, "Shift_JIS"), new HtmlParserOptions { EncodingDetector = null });
        Assert.Equal("windows-1252", document.CharacterSet);
    }

    [Fact]
    public void Parse_TheDeclaredEncodingsHaveThePrecedence()
    {
        var bytes = Encode("<p>" + Japanese, "Shift_JIS");

        // The byte order mark
        var document = Parse([0xEF, 0xBB, 0xBF, .. bytes]);
        Assert.Equal("UTF-8", document.CharacterSet);

        // The transport layer
        document = Parse(bytes, new HtmlParserOptions { TransportEncoding = "windows-1251" });
        Assert.Equal("windows-1251", document.CharacterSet);

        // The meta element found by the prescan
        document = Parse(Encode("<meta charset=windows-1251><p>" + Japanese, "Shift_JIS"));
        Assert.Equal("windows-1251", document.CharacterSet);
    }

    [Fact]
    public void Parse_MetaInTheHeadAfterTheFirstKilobyte_ChangesTheDetectedEncoding()
    {
        var head = "<head><!--" + new string(' ', 1024) + "-->";
        var document = Parse(Encode(head + "<meta charset=koi8-u><title>" + Russian + "</title>", "KOI8-U"));
        Assert.Equal("KOI8-U", document.CharacterSet);
        Assert.Equal(Russian, document.Title);

        // The meta element is not in the head: it does not declare the encoding
        document = Parse(Encode(head + "</head><body><meta charset=windows-1251><p>" + Russian, "KOI8-U"));
        Assert.Equal("KOI8-U", document.CharacterSet);
        Assert.Equal(Russian, document.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public void Parse_CustomDetector()
    {
        var detector = new StubDetector("windows-1251");
        var bytes = Encode("<p>" + Japanese, "Shift_JIS");
        var document = Parse(bytes, new HtmlParserOptions { EncodingDetector = detector, Url = "https://example.com/page" });
        Assert.Equal("windows-1251", document.CharacterSet);
        var call = Assert.Single(detector.Calls);
        Assert.Equal(bytes, call.Bytes);
        Assert.Equal("https://example.com/page", call.Url);
        Assert.True(call.IsComplete);

        // No URL
        detector.Calls.Clear();
        Parse(bytes, new HtmlParserOptions { EncodingDetector = detector });
        Assert.Null(Assert.Single(detector.Calls).Url);
    }

    [Fact]
    public void Parse_CustomDetector_IsNotCalledForPlainAsciiBytes()
    {
        var detector = new StubDetector("windows-1251");
        var document = Parse("<p>test"u8.ToArray(), new HtmlParserOptions { EncodingDetector = detector });
        Assert.Equal("windows-1252", document.CharacterSet);
        Assert.Empty(detector.Calls);

        // The escape character starts the escape sequences of ISO-2022-JP
        detector = new StubDetector("ISO-2022-JP");
        document = Parse(Encode("<p>" + Japanese, "ISO-2022-JP"), new HtmlParserOptions { EncodingDetector = detector });
        Assert.Equal("ISO-2022-JP", document.CharacterSet);
        Assert.Equal(Japanese, document.QuerySelector("p")!.TextContent);
        Assert.Single(detector.Calls);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    [InlineData("replacement")]
    [InlineData("iso-2022-kr")]
    public void Parse_CustomDetector_UnsupportedEncoding_DefaultEncoding(string label)
    {
        var document = Parse(Encode("<p>" + Japanese, "Shift_JIS"), new HtmlParserOptions { EncodingDetector = new StubDetector(label), DefaultEncoding = "windows-1251" });
        Assert.Equal("windows-1251", document.CharacterSet);
    }

    [Fact]
    public void Parse_Utf16DefaultEncoding_TheDetectorIsNotUsed()
    {
        var detector = new StubDetector("windows-1251");
        var document = Parse(Encoding.Unicode.GetBytes("<p>" + Japanese), new HtmlParserOptions { EncodingDetector = detector, DefaultEncoding = "utf-16le" });
        Assert.Equal("UTF-16LE", document.CharacterSet);
        Assert.Equal(Japanese, document.QuerySelector("p")!.TextContent);
        Assert.Empty(detector.Calls);
    }

    [Fact]
    public void BuiltInDetector_Iso2022JPIsNotDetected()
    {
        var document = Parse(Encode("<p>" + Japanese, "ISO-2022-JP"));
        Assert.Equal("windows-1252", document.CharacterSet);
    }

    [Fact]
    public void BuiltInDetector_IncompleteBytes()
    {
        // The bytes end in the middle of a character: this only disqualifies the encoding when they are the whole document
        var bytes = Encode(Japanese, "Shift_JIS");
        Assert.Equal("Shift_JIS", EncodingDetector.Default.Detect(bytes.AsSpan(0, bytes.Length - 1), new EncodingDetectionContext("https://example.com/", isComplete: false)));
        Assert.NotEqual("Shift_JIS", EncodingDetector.Default.Detect(bytes.AsSpan(0, bytes.Length - 1), new EncodingDetectionContext("https://example.com/", isComplete: true)));
    }

    [Fact]
    public void BuiltInDetector_LargeDocument()
    {
        // The legacy encodings are guessed from the 256 KiB that follow the first non-ASCII byte
        var russian = Encode("<p>" + Russian + "</p>\n", "windows-1251");
        var japanese = Encode("<p>" + Japanese + "</p>\n", "Shift_JIS");
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("<!DOCTYPE html><!--" + new string('-', 100_000) + "-->"));
        while (bytes.Count < 400_000)
        {
            bytes.AddRange(russian);
        }

        while (bytes.Count < 2_000_000)
        {
            bytes.AddRange(japanese);
        }

        Assert.Equal("windows-1251", EncodingDetector.Default.Detect([.. bytes], new EncodingDetectionContext("https://example.com/", isComplete: true)));

        // UTF-8 is valid when all the bytes are
        var utf8 = Encode("<p>" + Russian + "</p>\n", "UTF-8");
        bytes.Clear();
        while (bytes.Count < 400_000)
        {
            bytes.AddRange(utf8);
        }

        Assert.Equal("UTF-8", EncodingDetector.Default.Detect([.. bytes], new EncodingDetectionContext(url: null, isComplete: true)));
        Assert.Null(EncodingDetector.Default.Detect([.. bytes], new EncodingDetectionContext("https://example.com/", isComplete: true)));
        bytes.AddRange(russian);
        Assert.NotEqual("UTF-8", EncodingDetector.Default.Detect([.. bytes], new EncodingDetectionContext(url: null, isComplete: true)));
    }

    [Fact]
    public void BuiltInDetector_Utf8_IncompleteBytes()
    {
        var bytes = Encode(Japanese, "UTF-8");
        for (var length = bytes.Length - 3; length <= bytes.Length; length++)
        {
            Assert.Equal("UTF-8", EncodingDetector.Default.Detect(bytes.AsSpan(0, length), new EncodingDetectionContext(url: null, isComplete: false)));
        }

        // A truncated character is not valid at the end of the document, and an invalid one is never valid
        Assert.NotEqual("UTF-8", EncodingDetector.Default.Detect(bytes.AsSpan(0, bytes.Length - 1), new EncodingDetectionContext(url: null, isComplete: true)));
        Assert.NotEqual("UTF-8", EncodingDetector.Default.Detect([.. bytes, 0xE3, 0x41], new EncodingDetectionContext(url: null, isComplete: false)));
        Assert.NotEqual("UTF-8", EncodingDetector.Default.Detect([.. bytes, 0xE0, 0x80], new EncodingDetectionContext(url: null, isComplete: false)));
        Assert.NotEqual("UTF-8", EncodingDetector.Default.Detect([.. bytes, 0x80], new EncodingDetectionContext(url: null, isComplete: false)));
    }

    [Fact]
    public void Navigate_DetectsTheEncoding()
    {
        var context = _contexts.Create();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode("<p>" + Japanese, "Shift_JIS")) { Headers = [new("Content-Type", "text/html")] });
        Assert.Equal("Shift_JIS", document.CharacterSet);
        Assert.Equal(Japanese, document.QuerySelector("p")!.TextContent);

        // UTF-8 is not detected for the documents of the network
        document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode("<p>" + Japanese, "UTF-8")) { Headers = [new("Content-Type", "text/html")] });
        Assert.Equal("windows-1252", document.CharacterSet);

        document = context.Navigate("file:///test.html", _ => new DocumentResource(Encode("<p>" + Japanese, "UTF-8")) { Headers = [new("Content-Type", "text/html")] });
        Assert.Equal("UTF-8", document.CharacterSet);
    }

    [Fact]
    public void Navigate_TheCharsetOfTheContentTypeHasThePrecedence()
    {
        var context = _contexts.Create();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode("<p>" + Japanese, "Shift_JIS")) { Headers = [new("Content-Type", "text/html; charset=windows-1251")] });
        Assert.Equal("windows-1251", document.CharacterSet);
    }

    [Fact]
    public void Navigate_WithoutDetector_DefaultEncoding()
    {
        var context = _contexts.Create(new BrowsingContextOptions { EncodingDetector = null });
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode("<p>" + Japanese, "Shift_JIS")) { Headers = [new("Content-Type", "text/html")] });
        Assert.Equal("windows-1252", document.CharacterSet);
    }

    [Fact]
    public void Navigate_CustomDetector()
    {
        var detector = new StubDetector("EUC-JP");
        var context = _contexts.Create(new BrowsingContextOptions { EncodingDetector = detector });
        var document = context.Navigate("https://example.com/page", _ => new DocumentResource(Encode("<p>" + Japanese, "EUC-JP")) { Headers = [new("Content-Type", "text/html")] });
        Assert.Equal("EUC-JP", document.CharacterSet);
        Assert.Equal("https://example.com/page", Assert.Single(detector.Calls).Url);
    }

    [Fact]
    public void Navigate_OnlyTheHtmlDocumentsAreDetected()
    {
        // A text file is decoded as UTF-8, and an XML document has the encoding of its declaration, else UTF-8
        var detector = new StubDetector("windows-1251");
        var context = _contexts.Create(new BrowsingContextOptions { EncodingDetector = detector });
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode(Japanese, "Shift_JIS")) { Headers = [new("Content-Type", "text/plain")] });
        Assert.Equal("UTF-8", document.CharacterSet);
        document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode("<root>" + Japanese + "</root>", "Shift_JIS")) { Headers = [new("Content-Type", "application/xml")] });
        Assert.Equal("UTF-8", document.CharacterSet);
        Assert.Empty(detector.Calls);
    }

    [Fact]
    public void Navigate_ChildDocument_TheEncodingOfTheSameOriginContainerDocumentHasThePrecedence()
    {
        var loader = new StubLoader();
        loader.Responses["https://example.com/child"] = new DocumentResource(Encode("<p>" + Japanese, "Shift_JIS")) { Headers = [new("Content-Type", "text/html")] };
        loader.Responses["https://other.example/child"] = new DocumentResource(Encode("<p>" + Japanese, "Shift_JIS")) { Headers = [new("Content-Type", "text/html")] };
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = loader });
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode("<iframe src=child></iframe><iframe src=https://other.example/child></iframe>", "windows-1251")) { Headers = [new("Content-Type", "text/html; charset=windows-1251")] });
        context.EventLoop.RunUntilIdle();
        var iframes = document.QuerySelectorAll("iframe").Cast<HtmlIFrameElement>().ToArray();
        Assert.Equal("windows-1251", iframes[0].ContentDocument!.CharacterSet);

        // The cross-origin document does not get the encoding of its container document: it is detected
        Assert.Equal("Shift_JIS", iframes[1].ContentWindow!.Document.CharacterSet);
    }

    [Fact]
    public async Task Navigate_StreamedResponse_DetectsTheEncodingWhenTheFirstNonAsciiChunkArrives()
    {
        var detector = new StubDetector(result: null) { Fallback = EncodingDetector.Default };
        var context = _contexts.Create(new BrowsingContextOptions { EncodingDetector = detector });
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

        // The prescan ends after the first 1024 bytes, once the head ends
        var firstChunk = Encoding.ASCII.GetBytes("<!DOCTYPE html><p id=a>a</p><!--" + new string(' ', 1024) + "-->");
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(firstChunk)
        {
            Headers = [new("Content-Type", "text/html")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });

        // The ASCII bytes are the same in all the encodings: they are parsed as they arrive
        context.EventLoop.RunUntilIdle();
        Assert.Equal("a", document.GetElementById("a")!.TextContent);
        Assert.Equal("windows-1252", document.CharacterSet);
        chunks.Writer.TryWrite("<p id=b>b</p>"u8.ToArray());
        await context.EventLoop.RunUntilAsync(() => document.GetElementById("b") is not null, TestContext.Current.CancellationToken);
        Assert.Empty(detector.Calls);

        // The detector gets the bytes received so far. The last character is split between two chunks.
        var japanese = Encode("<p id=c>" + Japanese + "</p>", "Shift_JIS");
        var end = Array.LastIndexOf(japanese, (byte)'<') - 1;
        chunks.Writer.TryWrite(japanese.AsMemory(0, end));
        chunks.Writer.TryWrite(japanese.AsMemory(end));
        await context.EventLoop.RunUntilAsync(() => document.GetElementById("c")?.TextContent == Japanese, TestContext.Current.CancellationToken);
        Assert.Equal("Shift_JIS", document.CharacterSet);
        Assert.Equal(Japanese, document.GetElementById("c")!.TextContent);
        var call = Assert.Single(detector.Calls);
        Assert.False(call.IsComplete);
        Assert.Equal([.. firstChunk, .. "<p id=b>b</p>"u8, .. japanese.AsSpan(0, end)], call.Bytes);

        // The encoding is detected once
        chunks.Writer.TryWrite(Encode("<p id=d>" + Russian + "</p>", "windows-1251"));
        chunks.Writer.Complete();
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Shift_JIS", document.CharacterSet);
        Assert.NotNull(document.GetElementById("d"));
        Assert.Single(detector.Calls);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public async Task Navigate_StreamedResponse_WaitsForTheBytesThatDetermineTheEncoding()
    {
        var context = _contexts.Create();
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(Encode("<p id=a>" + Russian + "</p>", "windows-1251"))
        {
            Headers = [new("Content-Type", "text/html")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });

        // A meta element may follow in the first 1024 bytes
        context.EventLoop.RunUntilIdle();
        Assert.Null(document.GetElementById("a"));
        Assert.Equal(DocumentReadyState.Loading, document.ReadyState);

        // The encoding is detected from the whole body
        chunks.Writer.TryWrite(Encode("<p id=b>" + Russian + "</p>", "windows-1251"));
        chunks.Writer.Complete();
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("windows-1251", document.CharacterSet);
        Assert.Equal(Russian, document.GetElementById("a")!.TextContent);
        Assert.Equal(Russian, document.GetElementById("b")!.TextContent);
    }

    [Fact]
    public async Task Navigate_StreamedResponse_MetaElementOfALaterChunk()
    {
        var context = _contexts.Create();
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<!DOCTYPE html><title>a</title>"u8.ToArray())
        {
            Headers = [new("Content-Type", "text/html")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });

        context.EventLoop.RunUntilIdle();
        Assert.Null(document.QuerySelector("title"));

        chunks.Writer.TryWrite((byte[])[.. "<meta charset=windows-1251><p>"u8, 0xE6]);
        await context.EventLoop.RunUntilAsync(() => document.QuerySelector("p") is not null, TestContext.Current.CancellationToken);
        Assert.Equal("windows-1251", document.CharacterSet);
        Assert.Equal("\u0436", document.QuerySelector("p")!.TextContent);
        chunks.Writer.Complete();
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public async Task Navigate_StreamedResponse_TheEncodingIsDeterminedAfterATimeout()
    {
        var clock = new FakeTimeProvider();
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = clock });
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<!DOCTYPE html><title>a</title>"u8.ToArray())
        {
            Headers = [new("Content-Type", "text/html")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });

        context.EventLoop.RunUntilIdle();
        Assert.Null(document.QuerySelector("title"));
        clock.Advance(TimeSpan.FromMilliseconds(499));
        context.EventLoop.RunUntilIdle();
        Assert.Null(document.QuerySelector("title"));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        context.EventLoop.RunUntilIdle();
        Assert.Equal("a", document.Title);
        Assert.Equal("windows-1252", document.CharacterSet);

        // The meta element of the head changes the encoding on the fly: the bytes before it are ASCII bytes
        chunks.Writer.TryWrite((byte[])[.. "<meta charset=windows-1251><p>"u8, 0xE6]);
        await context.EventLoop.RunUntilAsync(() => document.QuerySelector("p") is not null, TestContext.Current.CancellationToken);
        Assert.Equal("windows-1251", document.CharacterSet);
        Assert.Equal("\u0436", document.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public async Task Navigate_StreamedResponse_MetaElementAfterANonAsciiByte_DoesNotChangeTheEncoding()
    {
        var clock = new FakeTimeProvider();
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = clock, EncodingDetector = null });
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource((byte[])[.. "<!DOCTYPE html><title>"u8, 0xE6, .. "</title>"u8])
        {
            Headers = [new("Content-Type", "text/html")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });

        clock.Advance(TimeSpan.FromMilliseconds(500));
        context.EventLoop.RunUntilIdle();
        Assert.Equal("\u00E6", document.Title);

        // The specification navigates to the document again with the new encoding
        chunks.Writer.TryWrite((byte[])[.. "<meta charset=windows-1251><p>"u8, 0xE6]);
        await context.EventLoop.RunUntilAsync(() => document.QuerySelector("p") is not null, TestContext.Current.CancellationToken);
        Assert.Equal("windows-1252", document.CharacterSet);
        Assert.Equal("\u00E6", document.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public async Task Navigate_StreamedResponse_AsciiBytes_DefaultEncoding()
    {
        var detector = new StubDetector("windows-1251");
        var context = _contexts.Create(new BrowsingContextOptions { EncodingDetector = detector });
        var chunks = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<p id=a>a</p>"u8.ToArray())
        {
            Headers = [new("Content-Type", "text/html")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });

        chunks.Writer.TryWrite("<p id=b>b</p>"u8.ToArray());
        chunks.Writer.Complete();
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("windows-1252", document.CharacterSet);
        Assert.Equal("b", document.GetElementById("b")!.TextContent);
        Assert.Empty(detector.Calls);
    }

    private sealed class StubDetector(string? result) : EncodingDetector
    {
        public List<(byte[] Bytes, string? Url, bool IsComplete)> Calls { get; } = [];

        public EncodingDetector? Fallback { get; init; }

        public override string? Detect(ReadOnlySpan<byte> bytes, EncodingDetectionContext context)
        {
            Calls.Add((bytes.ToArray(), context.Url, context.IsComplete));
            return result ?? Fallback?.Detect(bytes, context);
        }
    }

    private sealed class StubLoader : DocumentLoader
    {
        public Dictionary<string, DocumentResource> Responses { get; } = new(StringComparer.Ordinal);

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(Responses.GetValueOrDefault(request.Url));
    }
}
