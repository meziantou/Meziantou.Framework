using System.Buffers;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Html.Parsing;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform;

/// <summary>Options of the <see cref="HtmlDocumentParser"/>.</summary>
public sealed class HtmlParserOptions
{
    /// <summary>
    /// Gets or sets whether scripting is enabled for the parsed documents: this changes how <c>&lt;noscript&gt;</c> is
    /// parsed. The default is false (as for documents created by <c>DOMParser</c>). The scripts never run in the parsed
    /// documents: they only run in the documents loaded by <see cref="BrowsingContext.Navigate(string, Func{Url, string})"/>
    /// in a browsing context that has a <see cref="BrowsingContextOptions.ScriptEngine"/>.
    /// </summary>
    public bool ScriptingEnabled { get; init; }

    /// <summary>Gets whether scripting is enabled for the parsed documents, including by the browsing context.</summary>
    internal bool IsScriptingEnabled => ScriptingEnabled || BrowsingContext is { Options.ScriptingEnabled: true };

    /// <summary>Gets or sets whether <c>&lt;template shadowrootmode&gt;</c> creates declarative shadow roots. The default is true.</summary>
    public bool AllowDeclarativeShadowRoots { get; init; } = true;

    /// <summary>Gets or sets the callback invoked for each parse error. Tracking positions has no cost when this is null.</summary>
    public Action<HtmlParseError>? ParseErrorHandler { get; init; }

    /// <summary>Gets or sets the URL of the document (used to resolve relative URLs). The default is <c>about:blank</c>.</summary>
    public string? Url { get; init; }

    /// <summary>Gets or sets the encoding label provided by the transport layer (e.g. the charset of the Content-Type header).</summary>
    public string? TransportEncoding { get; init; }

    /// <summary>Gets or sets the encoding used when it cannot be determined from the content. The default is windows-1252.</summary>
    public string DefaultEncoding { get; init; } = "windows-1252";

    /// <summary>
    /// Gets or sets the detector of the encoding of the documents parsed from bytes that do not declare their encoding: no
    /// byte order mark, no <see cref="TransportEncoding"/>, no <c>meta</c> element, and no container document that gives
    /// its encoding. The default is <see cref="WebPlatform.EncodingDetector.Default"/>, which guesses the legacy encodings
    /// from the content (and UTF-8 when <see cref="Url"/> is not set or is a <c>file:</c> URL). Null disables the detection:
    /// these documents get the <see cref="DefaultEncoding"/>, as the ones whose encoding is not detected. The detector is
    /// not used when the <see cref="DefaultEncoding"/> is UTF-16BE/LE (the detected encodings are ASCII-compatible).
    /// </summary>
    public EncodingDetector? EncodingDetector { get; init; } = EncodingDetector.Default;

    // The browsing context in which the document is loaded, set by the navigations of the browsing context. The parsed
    // document becomes its active document, and the steps of "the end" run. The default is null: the document has no
    // browsing context. Use BrowsingContext.Navigate to load a document in a browsing context.
    internal BrowsingContext? BrowsingContext { get; init; }

    // The document whose origin and relevant global object the parsed document has (a document created by DOMParser), set
    // before the parser runs so that the element tasks queued while parsing run in the event loop of its window
    internal Document? OriginDocument { get; init; }

    /// <summary>
    /// Gets or sets the custom element registry of the parsed document, which defines the custom elements the parser creates
    /// (see <see cref="Dom.CustomElementRegistry"/>). The default is null: the document has no registry, and custom elements are
    /// not defined. The documents loaded in a browsing context (see <see cref="BrowsingContext.Navigate(string, Func{Url, string})"/>)
    /// get the global registry of their window instead.
    /// </summary>
    public CustomElementRegistry? CustomElementRegistry { get; init; }
}

/// <summary>Parses HTML documents and fragments according to the HTML Standard.</summary>
public sealed class HtmlDocumentParser
{
    private readonly HtmlParserOptions _options;
    private readonly UrlRecord? _url;

    /// <summary>
    /// Creates a parser with the given options, or with the default options when <c>options</c> is null.
    /// </summary>
    public HtmlDocumentParser(HtmlParserOptions? options = null)
    {
        _options = options ?? new HtmlParserOptions();
        if (_options.Url is not null)
        {
            _url = UrlParser.Parse(_options.Url, baseUrl: null) ?? throw new ArgumentException($"'{_options.Url}' is not a valid URL", nameof(options));
        }
    }

    /// <summary>
    /// Parses an HTML document. The document has no browsing context: use
    /// <see cref="BrowsingContext.Navigate(string, Func{Url, string})"/> to load a document in a browsing context.
    /// </summary>
    public static Document ParseDocument(string html, HtmlParserOptions? options = null) => new HtmlDocumentParser(options).Parse(html);

    /// <summary>
    /// Parses an HTML document, and waits until the parser has finished: while the parser waits for a script, the tasks of
    /// the <see cref="DomEventLoop"/> of the browsing context run.
    /// </summary>
    public static Task<Document> ParseDocumentAsync(string html, HtmlParserOptions? options = null, CancellationToken cancellationToken = default) => new HtmlDocumentParser(options).ParseAsync(html, cancellationToken);

    /// <summary>
    /// Parses an HTML document from a string, and waits until the parser has finished: while the parser waits for a script,
    /// the tasks of the <see cref="DomEventLoop"/> of the browsing context run.
    /// </summary>
    public async Task<Document> ParseAsync(string html, CancellationToken cancellationToken = default)
    {
        var document = Parse(html);
        await WaitForParserAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>Waits until the parser of a document has finished, running the tasks of the event loop of its browsing context.</summary>
    internal static async Task WaitForParserAsync(Document document, CancellationToken cancellationToken)
    {
        if (document.ActiveParser is not { } parser)
            return;

        // Without a browsing context, nothing pauses the parser
        if (document.BrowsingContext?.EventLoop is { } eventLoop)
        {
            await eventLoop.RunUntilAsync(() => parser.IsFinished, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Parses an HTML document from a string (the encoding is irrelevant).</summary>
    public Document Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = CreateDocument();
        RunParser(document, html, lateDeclaration: null);
        return document;
    }

    /// <summary>Parses an HTML document from bytes, determining the character encoding as specified.</summary>
    public Document Parse(ReadOnlySpan<byte> bytes)
    {
        if (!WebEncodingRegistry.TryGetEncoding(_options.DefaultEncoding, out var defaultEncoding))
            throw new ArgumentException($"The default encoding '{_options.DefaultEncoding}' is not supported", nameof(bytes));

        var (sniffedEncoding, _) = EncodingSniffer.Sniff(bytes, _options.TransportEncoding, out var lateDeclaration);
        var document = CreateDocument();
        var encoding = sniffedEncoding ?? GetContainerDocumentEncoding(document) ?? DetectEncoding(bytes, defaultEncoding, isComplete: true) ?? defaultEncoding;
        document.Encoding = encoding;
        RunParser(document, Decode(bytes, encoding), lateDeclaration is { } declaration ? (bytes.ToArray(), declaration) : null);
        return document;
    }

    /// <summary>
    /// How long a document parsed as its bytes arrive waits for the bytes that determine its encoding, measured by the clock
    /// of the event loop (https://html.spec.whatwg.org/multipage/parsing.html#encoding-sniffing-algorithm: "a user agent
    /// might wait 500ms or 1024 bytes, whichever came first").
    /// </summary>
    internal static readonly TimeSpan EncodingSniffingTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Parses an HTML document whose bytes arrive over time (the response of a navigation, see
    /// <see cref="DocumentResource.RemainingContentBytes"/>). The encoding sniffing algorithm waits for the bytes that
    /// determine the encoding (the byte order mark, and the prescan, which ends once it finds a <c>meta</c> element, or after
    /// the first 1024 bytes once the head ends), for the end of the body, or for <see cref="EncodingSniffingTimeout"/> after
    /// the first bytes (and the end of an XML declaration), whichever comes first. Meanwhile, as in Gecko, the bytes received are parsed speculatively with the encoding the
    /// document gets when it declares none (the encoding of its container document, or the default encoding), so that the
    /// resources they reference are fetched (see <see cref="SpeculativeHtmlParser"/>). Then each chunk is decoded and appended
    /// to the input stream of the parser from a task of the event loop of the browsing context (the tokenizer waits for more
    /// characters at the end of the input). The end of the sequence is the end of the file.
    /// </summary>
    internal Document Parse(ReadOnlyMemory<byte> firstBytes, IAsyncEnumerable<ReadOnlyMemory<byte>> remainingBytes)
    {
        if (!WebEncodingRegistry.TryGetEncoding(_options.DefaultEncoding, out var defaultEncoding))
            throw new ArgumentException($"The default encoding '{_options.DefaultEncoding}' is not supported", nameof(firstBytes));

        // The parser waits for its input until the encoding is determined
        var document = CreateDocument();
        document.Encoding = GetContainerDocumentEncoding(document) ?? defaultEncoding;
        var parser = RunParser(document, "", lateDeclaration: null, endOfFile: false);
        var eventLoop = document.BrowsingContext?.EventLoop;
        var streamedInput = new StreamedInput(this, parser, eventLoop, remainingBytes.GetAsyncEnumerator(), defaultEncoding);
        if (eventLoop is null)
        {
            // Nothing waits for the next chunks without an event loop: the first bytes are the whole document
            streamedInput.Receive(firstBytes.Span, endOfFile: true);
            parser.Resume();
            return document;
        }

        streamedInput.Receive(firstBytes.Span, endOfFile: false);
        parser.Resume();
        ReceiveNextChunk(streamedInput);
        return document;
    }

    /// <summary>The input of a document parsed as its bytes arrive (see <see cref="Parse(ReadOnlyMemory{byte}, IAsyncEnumerable{ReadOnlyMemory{byte}})"/>).</summary>
    private sealed class StreamedInput(HtmlDocumentParser owner, HtmlScriptingParser parser, DomEventLoop? eventLoop, IAsyncEnumerator<ReadOnlyMemory<byte>> chunks, WebEncodingId defaultEncoding)
    {
        // The bytes received while the encoding is not determined, or null once it is
        private ArrayBufferWriter<byte>? _sniffedBytes = new();

        // The speculative parser of the bytes received while the encoding is not determined, the decoder of their encoding,
        // and the number of bytes it received
        private SpeculativeHtmlParser? _speculativeParser;
        private WebDecoder? _speculativeDecoder;
        private int _speculativeLength;

        // Whether the timeout of the wait for the bytes that determine the encoding has elapsed (during an XML declaration)
        private bool _sniffingTimeoutElapsed;

        // The bytes received while a meta element of the head can change the encoding on the fly (see OnMetaElementInsertedInHead),
        // the position of the first one that does not have the same interpretation in all the encodings (-1 when there is none),
        // and the number of characters of the bytes inserted in the input stream
        private ArrayBufferWriter<byte>? _receivedBytes;
        private int _firstIncompatibleByte = -1;
        private int _insertedCharacters;

        private WebDecoder? _decoder;

        public HtmlScriptingParser Parser => parser;

        public DomEventLoop? EventLoop => eventLoop;

        public IAsyncEnumerator<ReadOnlyMemory<byte>> Chunks => chunks;

        /// <summary>
        /// The bytes received so far while the encoding of the document is not declared and can still be detected (they are
        /// plain ASCII bytes), else null.
        /// </summary>
        private ArrayBufferWriter<byte>? _undetectedBytes;

        /// <summary>Appends the characters of a chunk to the input stream of the parser (the caller resumes the parser).</summary>
        public void Receive(ReadOnlySpan<byte> chunk, bool endOfFile)
        {
            if (_receivedBytes is not null && !CanStillChangeEncodingOnTheFly())
            {
                StopChangingEncodingOnTheFly();
            }

            var text = new StringBuilder();
            if (_sniffedBytes is { } sniffedBytes)
            {
                sniffedBytes.Write(chunk);
                if (!EncodingSniffer.TrySniff(sniffedBytes.WrittenSpan, endOfFile, owner._options.TransportEncoding, out var result, out _))
                {
                    if (!_sniffingTimeoutElapsed || EncodingSniffer.IsIncompleteXmlDeclaration(sniffedBytes.WrittenSpan))
                    {
                        Speculate();
                        return;
                    }

                    result = EncodingSniffer.Sniff(sniffedBytes.WrittenSpan, owner._options.TransportEncoding, out _);
                }

                DetermineEncoding(result.Encoding, isComplete: endOfFile, text);
            }
            else
            {
                DetectEncoding(chunk);
                _decoder!.DecodeTo(chunk, text, flush: false);
                Retain(chunk);
            }

            if (endOfFile)
            {
                _undetectedBytes = null;
                _decoder!.DecodeTo([], text, flush: true);
            }

            Insert(text.ToString(), endOfFile);
        }

        private void Insert(string text, bool endOfFile)
        {
            var tokenizer = parser.TreeBuilder.Tokenizer;
            if (endOfFile)
            {
                tokenizer.EofInserted = true;
            }

            if (text.Length > 0)
            {
                tokenizer.Insert(positionFromEnd: 0, text);
                _insertedCharacters += text.Length;
            }

            // The speculative parser of a parser that waits for a parsing-blocking script parses the new characters too
            parser.AppendSpeculativeInput(text, endOfFile);
        }

        /// <summary>
        /// Parses speculatively the bytes received while the encoding is not determined, and starts the timeout of the wait
        /// when they are the first ones.
        /// </summary>
        private void Speculate()
        {
            var bytes = _sniffedBytes!.WrittenSpan;
            if (bytes.IsEmpty)
                return;

            if (_speculativeDecoder is null)
            {
                eventLoop?.QueueTimer(EncodingSniffingTimeout, OnSniffingTimeout);
                _speculativeDecoder = WebEncodingRegistry.CreateDecoder(parser.Document.Encoding);
                _speculativeParser = SpeculativeHtmlParser.Start(parser.Document, parser.TreeBuilder, parser.Document.Encoding);
            }

            if (_speculativeParser is null || bytes.Length == _speculativeLength)
                return;

            var text = new StringBuilder();
            _speculativeDecoder.DecodeTo(bytes[_speculativeLength..], text, flush: false);
            _speculativeLength = bytes.Length;
            _speculativeParser.Append(text.ToString(), endOfFile: false);
        }

        /// <summary>
        /// The bytes received before the timeout do not determine the encoding: it is determined from them, or once the XML
        /// declaration they start ends.
        /// </summary>
        private void OnSniffingTimeout()
        {
            if (_sniffedBytes is not { } sniffedBytes || parser.IsFinished)
                return;

            _sniffingTimeoutElapsed = true;
            if (EncodingSniffer.IsIncompleteXmlDeclaration(sniffedBytes.WrittenSpan))
                return;

            var (encoding, _) = EncodingSniffer.Sniff(sniffedBytes.WrittenSpan, owner._options.TransportEncoding, out _);
            var text = new StringBuilder();
            DetermineEncoding(encoding, isComplete: false, text);
            Insert(text.ToString(), endOfFile: false);
            parser.Resume();
        }

        /// <summary>
        /// The end of the encoding sniffing algorithm (https://html.spec.whatwg.org/multipage/parsing.html#encoding-sniffing-algorithm):
        /// <paramref name="sniffedEncoding"/> is the encoding given by the bytes (null when they do not declare one), then the
        /// encoding of the container document, the encoding detected from the content, and the default encoding. The bytes
        /// received so far are decoded into <paramref name="text"/>.
        /// </summary>
        private void DetermineEncoding(WebEncodingId? sniffedEncoding, bool isComplete, StringBuilder text)
        {
            var bytes = _sniffedBytes!.WrittenSpan;
            _sniffedBytes = null;
            _speculativeParser = null;
            _speculativeDecoder = null;

            // The encoding of a document that does not declare it is detected from the bytes received when the first chunk
            // that has a byte that is not a plain ASCII one arrives: the bytes before it are the same in all the encodings, so
            // they are parsed as they arrive
            var document = parser.Document;
            var encoding = sniffedEncoding ?? GetContainerDocumentEncoding(document);
            if (encoding is null && owner.CanDetectEncoding(defaultEncoding))
            {
                if (EncodingSniffer.CanChangeEncodingOnTheFly(bytes))
                {
                    _undetectedBytes = new ArrayBufferWriter<byte>(Math.Max(bytes.Length, 256));
                    _undetectedBytes.Write(bytes);
                }
                else
                {
                    encoding = owner.DetectEncoding(bytes, defaultEncoding, isComplete);
                }
            }

            document.Encoding = encoding ?? defaultEncoding;

            // The confidence is tentative when the bytes do not declare the encoding: a meta element of the head changes it
            if (sniffedEncoding is null && document.Encoding is not (WebEncodingId.Utf16BE or WebEncodingId.Utf16LE))
            {
                _receivedBytes = new ArrayBufferWriter<byte>(Math.Max(bytes.Length, 256));
                parser.TreeBuilder.MetaElementInsertedInHead = OnMetaElementInsertedInHead;
            }

            // The byte order mark is removed (https://encoding.spec.whatwg.org/#decode)
            var bomLength = WebEncodingRegistry.SniffBom(bytes, out var bomEncoding);
            _decoder = WebEncodingRegistry.CreateDecoder(bomLength > 0 ? bomEncoding : document.Encoding);
            _decoder.DecodeTo(bytes[bomLength..], text, flush: false);
            Retain(bytes);
        }

        /// <summary>
        /// Detects the encoding when the chunk is the first one that has a byte that is not a plain ASCII one: the document
        /// gets the detected encoding, which decodes this chunk and the next ones.
        /// </summary>
        private void DetectEncoding(ReadOnlySpan<byte> chunk)
        {
            if (_undetectedBytes is not { } bytes)
                return;

            bytes.Write(chunk);
            if (EncodingSniffer.CanChangeEncodingOnTheFly(chunk))
                return;

            _undetectedBytes = null;
            if (owner.DetectEncoding(bytes.WrittenSpan, defaultEncoding, isComplete: false) is { } encoding && encoding != parser.Document.Encoding)
            {
                parser.Document.Encoding = encoding;
                _decoder = WebEncodingRegistry.CreateDecoder(encoding);
            }
        }

        /// <summary>Keeps the bytes of a chunk while a meta element of the head can change the encoding on the fly.</summary>
        private void Retain(ReadOnlySpan<byte> chunk)
        {
            if (_receivedBytes is not { } receivedBytes)
                return;

            if (_firstIncompatibleByte < 0 && !EncodingSniffer.CanChangeEncodingOnTheFly(chunk))
            {
                var nonAscii = chunk.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F);
                var escape = chunk.IndexOfAny((byte)0x0E, (byte)0x0F, (byte)0x1B);
                _firstIncompatibleByte = receivedBytes.WrittenCount + (nonAscii < 0 ? escape : escape < 0 ? nonAscii : Math.Min(nonAscii, escape));
            }

            receivedBytes.Write(chunk);
        }

        /// <summary>
        /// Whether a meta element of the head can still change the encoding on the fly: the parser has not consumed a byte that
        /// does not have the same interpretation in all the encodings, and has not inserted the body element.
        /// </summary>
        private bool CanStillChangeEncodingOnTheFly() => (_firstIncompatibleByte < 0 || ConsumedCharacters <= _firstIncompatibleByte) && parser.Document.Body is null;

        // The number of characters of the bytes consumed by the tokenizer. The characters written by document.write() are
        // inserted before the characters of the bytes, so the characters after a meta element of the bytes are the ones of the
        // bytes when the tree builder inserts it.
        private int ConsumedCharacters => _insertedCharacters - parser.TreeBuilder.Tokenizer.PositionFromEnd;

        private void StopChangingEncodingOnTheFly()
        {
            _receivedBytes = null;
            parser.TreeBuilder.MetaElementInsertedInHead = null;
        }

        /// <summary>
        /// https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inhead: a meta element of the head that declares
        /// an encoding changes the encoding while the confidence is tentative
        /// (https://html.spec.whatwg.org/multipage/parsing.html#change-the-encoding). The first one makes the confidence
        /// certain. The encoding is changed on the fly when the bytes consumed so far have the same interpretation in both
        /// encodings (they are ASCII bytes: one character per byte); otherwise, the specification navigates to the document
        /// again with the new encoding, which is not implemented (the encoding does not change, as for a whole body). As in
        /// Gecko, the meta elements written by <c>document.write()</c> do not change the encoding.
        /// </summary>
        private void OnMetaElementInsertedInHead(Element meta)
        {
            if (parser.TreeBuilder.ScriptNestingLevel > 0 || EncodingSniffer.GetMetaElementEncoding(meta) is not { } encoding)
                return;

            var receivedBytes = _receivedBytes!;
            StopChangingEncodingOnTheFly();
            var document = parser.Document;
            var consumed = ConsumedCharacters;
            if (encoding == document.Encoding || consumed < 0 || consumed > receivedBytes.WrittenCount || (_firstIncompatibleByte >= 0 && consumed > _firstIncompatibleByte))
                return;

            var tokenizer = parser.TreeBuilder.Tokenizer;
            document.Encoding = encoding;
            _undetectedBytes = null;
            _decoder = WebEncodingRegistry.CreateDecoder(encoding);
            var text = new StringBuilder();
            _decoder.DecodeTo(receivedBytes.WrittenSpan[consumed..], text, flush: tokenizer.EofInserted);
            tokenizer.ReplaceRemainingInput(text.ToString());
            _insertedCharacters = consumed + text.Length;
        }
    }

    /// <summary>Waits for the next chunk of a document parsed as its bytes arrive, and appends it to the input stream from a task.</summary>
    private static void ReceiveNextChunk(StreamedInput streamedInput)
    {
        var chunks = streamedInput.Chunks;
        var eventLoop = streamedInput.EventLoop!;
        var parser = streamedInput.Parser;
        ValueTask<bool> next;
        try
        {
            next = chunks.MoveNextAsync();
        }
#pragma warning disable CA1031 // An exception of the response ends the input
        catch (Exception ex)
#pragma warning restore CA1031
        {
            next = ValueTask.FromException<bool>(ex);
        }

        eventLoop.BeginOperation();
        _ = next.AsTask().ContinueWith(
            completed =>
            {
                eventLoop.QueueTask(() =>
                {
                    // The parser was aborted (e.g. the document was unloaded): the rest of the response is ignored
                    if (parser.IsFinished)
                    {
                        _ = chunks.DisposeAsync().AsTask();
                        return;
                    }

                    if (completed.IsCompletedSuccessfully && completed.Result)
                    {
                        var chunk = chunks.Current;
                        streamedInput.Receive(chunk.Span, endOfFile: false);
                        parser.Document.AddNavigationResponseBytes(chunk.Length);
                        ReceiveNextChunk(streamedInput);
                    }
                    else
                    {
                        streamedInput.Receive([], endOfFile: true);
                        parser.Document.MarkNavigationResponseEnd();
                        _ = chunks.DisposeAsync().AsTask();
                    }

                    parser.Resume();
                });
                eventLoop.EndOperation();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Whether the encoding of a document that does not declare it can be detected: the detected encodings are
    /// ASCII-compatible, so the detector is not used when the default encoding is UTF-16BE/LE.
    /// </summary>
    private bool CanDetectEncoding(WebEncodingId defaultEncoding) => _options.EncodingDetector is not null && defaultEncoding is not (WebEncodingId.Utf16BE or WebEncodingId.Utf16LE);

    /// <summary>
    /// The step of the encoding sniffing algorithm that autodetects the encoding from the data stream
    /// (https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding), with the
    /// <see cref="HtmlParserOptions.EncodingDetector"/>. Returns null when the encoding is not detected.
    /// </summary>
    private WebEncodingId? DetectEncoding(ReadOnlySpan<byte> bytes, WebEncodingId defaultEncoding, bool isComplete)
    {
        // The plain ASCII bytes are the same in all the encodings
        if (!CanDetectEncoding(defaultEncoding) || EncodingSniffer.CanChangeEncodingOnTheFly(bytes))
            return null;

        var label = _options.EncodingDetector!.Detect(bytes, new EncodingDetectionContext(_options.Url, isComplete));
        if (label is null || !WebEncodingRegistry.TryGetEncoding(label, out var encoding))
            return null;

        return encoding is WebEncodingId.Utf16BE or WebEncodingId.Utf16LE or WebEncodingId.Replacement ? null : encoding;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#encoding-sniffing-algorithm: the document of a child navigable
    /// gets the encoding of its container document when they are same origin, unless it is UTF-16.
    /// </summary>
    private static WebEncodingId? GetContainerDocumentEncoding(Document document)
    {
        if (document.BrowsingContext?.Container?.NodeDocument is not { } parentDocument || !Document.IsSameOrigin(parentDocument, document))
            return null;

        return parentDocument.Encoding is WebEncodingId.Utf16BE or WebEncodingId.Utf16LE ? null : parentDocument.Encoding;
    }

    /// <summary>Parses an HTML document from a stream, determining the character encoding as specified.</summary>
    public Document Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Parse(memory.GetBuffer().AsSpan(0, (int)memory.Length));
    }

    /// <summary>
    /// Parses an HTML document from a stream, determining the character encoding as specified, and waits until the parser
    /// has finished (see <see cref="ParseAsync(string, CancellationToken)"/>).
    /// </summary>
    public async Task<Document> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var document = Parse(memory.GetBuffer().AsSpan(0, (int)memory.Length));
        await WaitForParserAsync(document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>
    /// Runs the HTML fragment parsing algorithm (https://html.spec.whatwg.org/multipage/parsing.html#html-fragment-parsing-algorithm)
    /// with <paramref name="context"/> as the context element. The nodes are created in the context's node document.
    /// </summary>
    public DocumentFragment ParseFragment(string html, Element context) => ParseFragment(context, html, allowDeclarativeShadowRoots: false, ParserScriptingMode.Inert, _options.ParseErrorHandler);

    /// <param name="target">The context of the parsing: an element, or a shadow root.</param>
    /// <param name="input">The markup.</param>
    /// <param name="allowDeclarativeShadowRoots">Whether the template elements with a shadowrootmode attribute attach a shadow root.</param>
    /// <param name="scriptingMode">The scripting mode of the parser.</param>
    /// <param name="errorHandler">The handler of the parse errors.</param>
    /// <param name="sanitizer">The sanitizer configuration applied while parsing (see <see cref="SanitizerConfiguration.GetEffectiveConfiguration"/>), or null.</param>
    internal static DocumentFragment ParseFragment(Node target, string input, bool allowDeclarativeShadowRoots, ParserScriptingMode scriptingMode, Action<HtmlParseError>? errorHandler, SanitizerConfiguration? sanitizer = null)
    {
        var fragment = new DocumentFragment(target.NodeDocument);
        var (builder, _) = CreateFragmentParser(target, fragment, referenceChild: null, input, allowDeclarativeShadowRoots, scriptingMode, errorHandler, sanitizer, streaming: false);
        builder.Run();
        return fragment;
    }

    /// <summary>
    /// Creates the parser of the HTML streamed into <paramref name="target"/> before <paramref name="referenceChild"/>
    /// (https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html): a fragment parser that inserts the nodes
    /// directly into the target, and waits for the explicit "EOF" character at the end of its input.
    /// </summary>
    internal static HtmlStreamWriter CreateStreamingParser(Node target, Node? referenceChild, ParserScriptingMode scriptingMode, SanitizerConfiguration? sanitizer)
    {
        var (builder, tokenizer) = CreateFragmentParser(target, target, referenceChild, input: "", allowDeclarativeShadowRoots: true, scriptingMode, errorHandler: null, sanitizer, streaming: true);
        tokenizer.EofInserted = false;
        return new HtmlStreamWriter(target, referenceChild, builder, tokenizer);
    }

    /// <summary>https://whatpr.org/html/12758/parsing.html#initialize-an-html-fragment-parser</summary>
    private static (HtmlTreeBuilder Builder, HtmlTokenizer Tokenizer) CreateFragmentParser(Node target, Node rootInsertionTarget, Node? referenceChild, string input, bool allowDeclarativeShadowRoots, ParserScriptingMode scriptingMode, Action<HtmlParseError>? errorHandler, SanitizerConfiguration? sanitizer, bool streaming)
    {
        var context = target as Element ?? ((DocumentFragment)target).Host ?? throw new ArgumentException("The target must be an element or have a host", nameof(target));
        var document = Document.CreateHtmlDocument();
        var contextDocument = context.NodeDocument;
        document.Mode = contextDocument.Mode;
        document.OriginDocument = Document.GetOriginDocument(contextDocument);
        document.RealmDocument = contextDocument;
        // The template contents are parsed with the scripting of the template contents owner document (disabled), as in
        // the browsers (tested by WPT html/syntax/serializing-html-fragments/escaping.html), rather than the scripting of
        // the node document of the template
        if (!contextDocument.ScriptingEnabled || context.IsHtml(TagId.Template))
        {
            scriptingMode = ParserScriptingMode.Disabled;
        }

        var errorCallback = CreateErrorCallback(errorHandler);
        var tokenizer = new HtmlTokenizer(input, errorCallback, new NameTable());
        var builder = new HtmlTreeBuilder(document, tokenizer, errorCallback, scriptingMode)
        {
            AllowDeclarativeShadowRoots = allowDeclarativeShadowRoots,
            Sanitizer = sanitizer,
        };

        if (context.IsHtmlNamespace)
        {
            tokenizer.State = context.TagId switch
            {
                TagId.Title or TagId.Textarea => TokenizerState.Rcdata,
                TagId.Style or TagId.Xmp or TagId.Iframe or TagId.Noembed or TagId.Noframes => TokenizerState.Rawtext,
                TagId.Script => TokenizerState.ScriptData,
                TagId.Noscript when scriptingMode != ParserScriptingMode.Disabled => TokenizerState.Rawtext,
                TagId.Plaintext => TokenizerState.Plaintext,
                _ => TokenizerState.Data,
            };
        }

        builder.SetupFragmentCase(context, rootInsertionTarget, referenceChild, CustomElementRegistry.LookUp(target), streaming);
        return (builder, tokenizer);
    }

    private Document CreateDocument()
    {
        var document = Document.CreateHtmlDocument();
        document.ScriptingEnabled = _options.IsScriptingEnabled;
        document.AllowDeclarativeShadowRoots = _options.AllowDeclarativeShadowRoots;
        if (_url is not null)
        {
            document.UrlRecord = _url;
        }

        if (_options.BrowsingContext is { } browsingContext)
        {
            browsingContext.InitializeDocument(document);
        }
        else
        {
            document.CustomElementRegistryStorage = _options.CustomElementRegistry;
            document.OriginDocument = _options.OriginDocument;
        }

        return document;
    }

    /// <param name="document">The document.</param>
    /// <param name="input">The decoded input.</param>
    /// <param name="lateDeclaration">The bytes of the input and the meta element of the head that declares the encoding after the first 1024 bytes, if any.</param>
    /// <param name="endOfFile">Whether the input is the whole document (false when the rest of the input arrives later).</param>
    private HtmlScriptingParser RunParser(Document document, string input, (byte[] Bytes, (WebEncodingId Encoding, int End) Declaration)? lateDeclaration, bool endOfFile = true)
    {
        var errorCallback = CreateErrorCallback(_options.ParseErrorHandler);
        var tokenizer = new HtmlTokenizer(input, errorCallback, new NameTable()) { EofInserted = endOfFile };
        var builder = new HtmlTreeBuilder(document, tokenizer, errorCallback, document.ScriptingEnabled ? ParserScriptingMode.Normal : ParserScriptingMode.Disabled)
        {
            AllowDeclarativeShadowRoots = _options.AllowDeclarativeShadowRoots,
        };

        // https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inhead: the meta element found by the prescan after
        // the first 1024 bytes changes the encoding when the parser inserts it in the head (the input consumed so far has the
        // same interpretation in both encodings). The characters inserted by document.write() are not part of the bytes.
        if (lateDeclaration is { } late && document.Encoding is not (WebEncodingId.Utf16BE or WebEncodingId.Utf16LE))
        {
            builder.MetaElementInsertedInHead = _ =>
            {
                if (!ReferenceEquals(tokenizer.Input, input))
                {
                    builder.MetaElementInsertedInHead = null;
                    return;
                }

                var position = input.Length - tokenizer.PositionFromEnd;
                if (position != late.Declaration.End)
                    return;

                builder.MetaElementInsertedInHead = null;
                if (late.Declaration.Encoding != document.Encoding)
                {
                    tokenizer.ReplaceRemainingInput(WebEncodingRegistry.CreateDecoder(late.Declaration.Encoding).DecodeToString(late.Bytes.AsSpan(position)));
                    document.Encoding = late.Declaration.Encoding;
                }
            };
        }

        // The steps of "the end" run for the documents of a browsing context (https://html.spec.whatwg.org/multipage/parsing.html#the-end)
        var runTheEnd = _options.BrowsingContext is not null && ReferenceEquals(document.BrowsingContext, _options.BrowsingContext);
        var parser = new HtmlScriptingParser(document, builder, runTheEnd);
        parser.Start();
        return parser;
    }

    private static Action<HtmlParseErrorInfo>? CreateErrorCallback(Action<HtmlParseError>? handler)
    {
        if (handler is null)
            return null;

        return error => handler(error.ToParseError());
    }

    /// <summary>https://encoding.spec.whatwg.org/#decode (the BOM, if any, is removed).</summary>
    private static string Decode(ReadOnlySpan<byte> input, WebEncodingId encoding)
    {
        var bomLength = WebEncodingRegistry.SniffBom(input, out var bomEncoding);
        if (bomLength > 0)
        {
            encoding = bomEncoding;
        }

        return WebEncodingRegistry.CreateDecoder(encoding).DecodeToString(input[bomLength..]);
    }
}
