using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// Runs a testharness.js test: the test page is parsed with the HTML parser, scripts are executed by the engine (see
/// <see cref="WptEngine"/>) when the library
/// prepares them (when the parser reaches their end tag, or when a script inserts them), and the event loop runs (with
/// virtual time for timers) until testharness.js reports the results.
/// </summary>
#pragma warning disable CA1001 // The engine is disposed at the end of Run
internal sealed partial class WptTestRunner
#pragma warning restore CA1001
{
    private const string ReportScript = """
        (function () {
          setup({ output: false });
          add_completion_callback(function (tests, harness_status) {
            __wpt_report(JSON.stringify({
              status: harness_status.status,
              message: harness_status.message,
              tests: tests.map(function (t) { return { name: t.name, status: t.status, message: t.message }; })
            }));
          });
        })();
        """;

    /// <summary>
    /// The longest a test may run, in wall-clock time: 60 seconds times <see cref="TimeoutMultiplier"/>. No test is expected
    /// to reach it; it ends a test that never finishes.
    /// </summary>
    internal static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60 * TimeoutMultiplier);

    /// <summary>
    /// Gets the factor of <see cref="MaxDuration"/>, as the --timeout-multiplier option of wptrunner: WPT_TIMEOUT_MULTIPLIER,
    /// or 3 on a CI agent (CI=true). The heaviest tests (IndexedDB cloning of large values, the WebAssembly SIMD core tests,
    /// 65 KB WebSocket messages) take 5 to 8 times longer on the macOS agents than on the Linux ones, and exceeded 60
    /// seconds in both of their runs there, while they take a few seconds on a developer machine.
    /// </summary>
    private static double TimeoutMultiplier
        => Environment.GetEnvironmentVariable("WPT_TIMEOUT_MULTIPLIER") is { Length: > 0 } value
            ? double.Parse(value, CultureInfo.InvariantCulture)
            : Environment.GetEnvironmentVariable("CI") is "true" ? 3 : 1;

    /// <summary>The WebAssembly runtime of the tests: the compiled modules are shared by the tests.</summary>
    private static readonly Wasmtime.WasmtimeRuntime WebAssemblyRuntime = new();

    /// <summary>The testdriver vendor implementation of the runner (Runner/testdriver-vendor.js).</summary>
    private static readonly Lazy<string> TestDriverVendorScript = new(() => WptFiles.ReadAllText(Path.Combine(WptPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Wpt", "Runner", "testdriver-vendor.js")));

    private readonly WptTest _test;
    private readonly List<string> _log = [];

    /// <summary>Adds a line to the log of the test (the workers log from their own threads).</summary>
    internal void Log(string line)
    {
        lock (_log)
        {
            _log.Add(line);
        }
    }

    /// <summary>A copy of the log of the test.</summary>
    private List<string> LogSnapshot
    {
        get
        {
            lock (_log)
            {
                return [.. _log];
            }
        }
    }

    // The functions that give the console messages of a window to the WebDriver BiDi log listeners of its realm (testdriver-vendor.js)
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<GlobalScope, Action<ConsoleMessage>> _consoleListeners = [];

    /// <summary>The states of the permissions set by test_driver.set_permission(), by name.</summary>
    private readonly Dictionary<string, string> _permissions = new(StringComparer.Ordinal);

    /// <summary>The permission store of the browsing contexts, whose states are set by test_driver.set_permission().</summary>
    private readonly PermissionStore _permissionStore = new();
    private readonly WptStash _stash = new();

    /// <summary>The WebSocket server of wptserve (its connections are in-memory pipes).</summary>
    private readonly WptWebSocketServer _webSocketServer;

    /// <summary>Gets the stash of the WPT server.</summary>
    internal WptStash Stash => _stash;
    // Each read of the clock advances it by 100 µs (the resolution of performance.now()): time passes while a script runs
    // (e.g. performance.now() is positive and increases, a loop waiting for the clock ends)
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.FromTicks(1000) };

    /// <summary>The cookie store of the browser (its expiry times are measured by the virtual clock).</summary>
    private readonly CookieJar _cookieJar;

    /// <summary>Gets the virtual clock of the test.</summary>
    internal TimeProvider Clock => _clock;

    /// <summary>Gets the current time of the virtual clock (<c>time.time()</c> in a handler).</summary>
    internal DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>Queues a callback after a delay measured by the virtual clock (on the event loop of the test page). Can be called from any thread.</summary>
    internal void QueueTimer(TimeSpan delay, Action callback) => _eventLoop.QueueTimer(delay, callback);

    /// <summary>Queues a task on the event loop of the test page. Can be called from any thread.</summary>
    internal void QueueTask(Action callback) => _eventLoop.QueueTask(callback);
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private WptEngine _engine = null!;
    private DomEventLoop _eventLoop = null!;

    // The thread that runs the event loop of the windows and advances the virtual clock
    private int _eventLoopThreadId;
    private Document? _document;
    private WptResult? _result;

    private WptTestRunner(WptTest test, Rendering.RenderingBackend renderingBackend, bool reftest)
    {
        _test = test;
        _renderingBackend = renderingBackend;
        _reftestWait = reftest ? new ReftestWait(this) : null;
        _cookieJar = new CookieJar(_clock);
        _webSocketServer = new WptWebSocketServer(this);
    }

    /// <summary>Gets the cookie store of the browser (the cookies of the test driver).</summary>
    internal CookieJar CookieJar => _cookieJar;

    // The testharness.js tests have the rendering backend of the reftests: the images are decoded (decode(), createImageBitmap())
    public static WptResult Run(WptTest test) => new WptTestRunner(test, WptReftestRunner.Backend, reftest: false).Run();

    private WptResult Run()
    {
        // The test page becomes the current global object of the thread (OnDocumentActivated). It is restored at the end of the
        // test: the thread runs the next tests, whose objects would capture the page (e.g. as the relevant global object of
        // their event targets), so each test would keep the previous ones alive.
        var previousGlobalDocument = Document.CurrentGlobalDocumentOrNull;
        BrowsingContext? browsingContext = null;
        try
        {
            // An infinite recursion of a page of a reftest (e.g. an onload attribute calling a global onload() function, which is the
            // handler itself) aborts the page instead of overflowing the stack of the thread
            _engine = WptEngine.Create(this, _reftestWait is null ? null : ReftestRecursionLimit);

            // The test page is loaded in a top-level browsing context. The documents of its iframes and the scripts are the files of the test suite.
            browsingContext = new BrowsingContext(new BrowsingContextOptions
            {
                DocumentLoader = new WptDocumentLoader(this),

                // The cookies of the responses of the WPT server, sent with the next requests (see GetRequestHeaders)
                CookieJar = _cookieJar,

                // The history of the visited links of the test, which starts empty: the page of the test is visited (as in the
                // browsers, where the tests link to their own URL to get a visited link), the other URLs are not
                VisitedLinks = new InMemoryVisitedLinkStore(),
                ScriptEngine = _engine.ScriptEngine,

                // The WebAssembly modules run on Wasmtime (V8 has its own WebAssembly namespace)
                WebAssemblyRuntime = WebAssemblyRuntime,
                TimeProvider = _clock,
                SpeculativeParsing = true,
                // The WPT server is reached directly: the tests only check the Sec-Purpose header of the anonymized prefetches
                SpeculativeLoading = new SpeculativeLoadingOptions { AnonymousClientIpSupported = true },
                UnhandledScriptError = (_, error) => Log("Uncaught " + GetErrorDescription(error)),

                // The messages of the console namespace: added to the log, and given to the listeners of test_driver.bidi.log.entry_added
                ConsoleMessage = OnConsoleMessage,

                // The permissions set by test_driver.set_permission(): the access is allowed unless it is denied
                Permissions = _permissionStore,
                ClipboardPermission = (_, access) => !_permissions.TryGetValue(access == ClipboardAccess.Read ? "clipboard-read" : "clipboard-write", out var state) || state != "denied",

                // The pages of the reftests are rasterized; the images of all the tests are decoded by the backend
                RenderingBackend = _renderingBackend,

                // The fonts of the CSS test suite, and fallback fonts for the scripts that the bundled fonts do not cover
                InstalledFonts = WptFonts.Installed,

                // The media files are not decoded: their metadata is read and they are played with the virtual clock
                MediaBackend = WptMediaBackend.Instance,

                // The values of the client hints: a desktop user agent on Linux, with the memory and the network of a
                // recent machine (the library sends no device memory nor network hint by default)
                ClientHints = TestClientHints,

                // The pages of the forced-colors-mode directory are loaded in forced colors mode
                Viewport = GetViewport(_test),

                // The downloads are saved nowhere, but their whole body is read, as a browser does
                DownloadStarted = (context, download) => _ = ReadDownloadAsync(download.Resource),
            });
            var eventLoop = browsingContext.EventLoop;
            _eventLoop = eventLoop;
            _eventLoopThreadId = Environment.CurrentManagedThreadId;
            _browsingContext = browsingContext;

            if (_test.Kind == WptTestKind.Xml)
            {
                try
                {
                    var content = File.ReadAllText(_test.FilePath);
                    var url = new Url(_test.Url);
                    var document = XmlDocumentParser.ParseDocument(WptServer.HasSubstitutions(url) ? WptServer.Substitute(content, url) : content, new XmlParserOptions
                    {
                        Url = _test.Url,
                        ContentType = GetXmlContentType(_test.RelativePath),
                        BrowsingContext = browsingContext,
                    });

                    // The XML pages are not the response of a navigation: the language of their .headers file is the default
                    // language of the elements (css/CSS2/selectors/lang-selector-005.xht)
                    if (GetHeaders(_test.FilePath, url).FindLast(header => header.Key.Equals("Content-Language", StringComparison.OrdinalIgnoreCase)) is { Value: { Length: > 0 } contentLanguage })
                    {
                        document.ProtocolLanguage = contentLanguage.Split(',')[0].Trim();
                    }
                }
                catch (System.Xml.XmlException ex)
                {
                    return new WptResult("ERROR", "The test page is not well-formed XML: " + ex.Message, [], LogSnapshot);
                }
            }
            else
            {
                // The test page is the response of a navigation, with the headers of its .headers file. It is decoded like a
                // browser does: BOM, the charset of the .headers file, <meta charset>, then windows-1252.
                // The request headers of the substitutions are the ones of a navigation from the address bar.
                var testUrl = new Url(_test.Url);
                var testRequestHeaders = GetRequestHeaders(testUrl, new DocumentRequest(_test.Url) { Mode = RequestMode.Navigate, UserActivation = true });
                browsingContext.Navigate(_test.Url, _ =>
                {
                    // The generated page of a .any.js or .window.js test has the headers of the .headers files of the test
                    // file, as wptserve serves its wrappers (e.g. Cross-Origin-Opener-Policy for a cross-origin isolated test)
                    if (_test.Kind != WptTestKind.Html)
                        return new DocumentResource(GetTestHtml()) { Headers = [new("Content-Type", "text/html"), .. GetHeaders(_test.FilePath, testUrl)] };

                    // The cookies of the response of the test page (a navigation from the address bar, which is same-site)
                    var resource = CreateResource(new DocumentResource(WptServer.HasSubstitutions(testUrl) ? WptServer.Substitute(File.ReadAllBytes(_test.FilePath), testUrl, testRequestHeaders) : File.ReadAllBytes(_test.FilePath)), _test.FilePath, testUrl);
                    foreach (var header in resource.Headers.Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)))
                    {
                        _cookieJar.SetCookie(_test.Url, header.Value);
                    }

                    return resource;
                });
            }

            // The parser queued the DOMContentLoaded and load events (https://html.spec.whatwg.org/multipage/parsing.html#the-end)
            eventLoop.PerformMicrotaskCheckpoint();
            RunEventLoop(eventLoop);

            // A page of a reftest is rendered once it is ready for the screenshot. The tasks that are already queued run first
            // (e.g. the response of an image fetched by the last update of the rendering), without advancing the clock
            if (_reftestWait is not null)
            {
                if (_reftestWait.Pending is null)
                {
                    for (var i = 0; i < 1000 && eventLoop.RunTask(); i++)
                    {
                    }
                }

                return TakeReftestScreenshot(browsingContext);
            }

            return _result ?? new WptResult("TIMEOUT", "The test did not complete", [], LogSnapshot);
        }
        catch (Exception ex) when (ex is TimeoutException || _engine.IsTimeout(ex))
        {
            return new WptResult("TIMEOUT", ex.Message, _result?.Subtests ?? [], LogSnapshot);
        }
        finally
        {
            browsingContext?.Dispose();
            _engine?.Dispose();
            Document.CurrentGlobalDocumentOrNull = previousGlobalDocument;
        }
    }

    /// <summary>
    /// Returns the viewport and the preferences of the user of a page: the pages of the <c>forced-colors-mode</c> directory (the
    /// tests and their references) are loaded in forced colors mode, with the system colors of the light color scheme as the
    /// forced colors palette. That is the configuration of the virtual test suite of Chromium that runs the directory
    /// (<c>--force-high-contrast</c>, which keeps the default system colors in the tests): the tests expect its colors (e.g. a
    /// yellow <c>Mark</c>). The other pages have the default viewport. As in the browsers, the scrolls of the user are animated
    /// (the tests of the scroll events and of the snapping interrupt them and wait for their end).
    /// </summary>
    private static ViewportOptions GetViewport(WptTest test)
        => test.RelativePath.StartsWith("forced-colors-mode/", StringComparison.Ordinal) ? ForcedColorsViewport : DefaultViewport;

    private static readonly ViewportOptions DefaultViewport = new() { SmoothUserScrolling = true };

    private static readonly ViewportOptions ForcedColorsViewport = DefaultViewport with { ForcedColors = true };

    private static string GetXmlContentType(string path) => Path.GetExtension(path) switch
    {
        ".svg" => "image/svg+xml",
        ".xml" => "application/xml",
        _ => "application/xhtml+xml",
    };

    /// <summary>
    /// Called when a document becomes the active document of a browsing context, before it is parsed. The test page is the
    /// active document of the browsing context of the test (not the auxiliary browsing contexts opened by the test): when
    /// it navigates, the new document reports the results.
    /// </summary>
    internal void OnDocumentActivated(Document document)
    {
        _activatedDocuments++;
        if (ReferenceEquals(document.BrowsingContext, _browsingContext) && !document.IsInitialAboutBlank)
        {
            _document = document;
            Document.CurrentGlobalDocument = document;
        }
    }

    /// <summary>The browsing context of the test page.</summary>
    private BrowsingContext? _browsingContext;

    /// <summary>
    /// The maximum number of documents a test can activate: a navigation loop (e.g. a load event handler that navigates the
    /// frame again, while the virtual clock does not advance) is stopped before it exhausts the memory.
    /// </summary>
    private const int MaxActivatedDocuments = 2000;

    private int _activatedDocuments;

    /// <summary>
    /// Loads the documents of the navigations (e.g. the src of an iframe) and the scripts from the files of the test suite,
    /// synchronously, and the responses of the Python handlers (see <see cref="WptPythonHandlers"/>). The content type of a
    /// document is defined by its extension or its .headers file. The documents are loaded as bytes, so the library
    /// determines their encoding (BOM, charset of the .headers file, &lt;meta charset&gt;, XML declaration, encoding of the
    /// parent document), as are the style sheets. The response of a script, a style sheet, an image or a font is delayed by
    /// the pipe=trickle() query parameter (using the virtual clock).
    /// </summary>
    private sealed class WptDocumentLoader(WptTestRunner runner) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<DocumentResource?>(cancellationToken);

            // The requests of fetch() and XMLHttpRequest
            if (request.Destination == RequestDestination.Empty)
                return runner.LoadFetchResource(request, cancellationToken);

            return Load(request.Url, request);
        }

        /// <summary>
        /// The WebSocket connections are opened by the emulated WebSocket server of wptserve (see <see cref="WptWebSocketServer"/>).
        /// The opening handshake sent to a port of the HTTP server is answered as an HTTP request (e.g. the dispatcher records
        /// its headers), so the connection fails.
        /// </summary>
        public override ValueTask<DocumentResource?> ConnectWebSocketAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var url = new Url(request.Url);
            if (!WptServer.IsWebSocketHost(url) && WptServer.IsHost(url))
                return runner.LoadFetchResource(request, cancellationToken);

            return runner._webSocketServer.ConnectAsync(request, cancellationToken);
        }

        /// <summary>Loads the resource of <paramref name="url"/>: the URL of the request, or the location of a redirect.</summary>
        private ValueTask<DocumentResource?> Load(string url, DocumentRequest request, int redirectCount = 0)
        {
            // The library performs the CORS checks with the headers of the response (the .headers files and the pipe=header() parameter)
            var parsed = new Url(url);

            if (WptServer.IsHost(parsed) && WptPythonHandlers.IsHandler(parsed))
                return LoadHandler(parsed, request, redirectCount);

            if (WptServer.IsHost(parsed) && GetDirectory(parsed) is var (path, directory))
                return LoadDirectory(parsed, path, directory, request, redirectCount);

            // The images and the fonts are bytes
            if (request.Destination is RequestDestination.Image or RequestDestination.Font)
                return Trickle(parsed, () => runner.GetResponse(url, GetRequestHeaders(parsed, request)) is var (body, headers, responseUrl) ? new DocumentResource(body) { Headers = headers, StatusCode = WptPipes.GetStatus(WptPipes.Get(responseUrl)) ?? 200 } : null);

            // The media resources and the text tracks are bytes, with the status of the status pipe (a media element fails for an HTTP error)
            if (request.Destination is RequestDestination.Audio or RequestDestination.Video or RequestDestination.Track)
                return Trickle(parsed, () => runner.GetResponse(url) is var (body, headers, _) ? new DocumentResource(body) { Headers = headers, StatusCode = WptPipes.GetStatus(WptPipes.Get(parsed)) ?? 200 } : null);

            if (request.Destination is not (RequestDestination.Script or RequestDestination.PaintWorklet or RequestDestination.Json or RequestDestination.Text or RequestDestination.Style or RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker))
                return ValueTask.FromResult(runner.Trickle(parsed, request, runner.LoadDocument(url, request)));

            return Trickle(parsed, () =>
            {
                // The cookies of the response of a script are stored too (e.g. the Set-Cookie header of the .headers file of the script of a worker)
                var resource = runner.LoadScript(url, request.Destination);
                if (resource is not null)
                {
                    StoreCookies(parsed, request, resource.Headers);
                }

                return resource;
            });
        }

        /// <summary>
        /// The response of a file, delayed by the trickle pipe of the WPT server (https://web-platform-tests.org/writing-tests/server-pipes.html#trickle)
        /// with the virtual clock.
        /// </summary>
        private ValueTask<DocumentResource?> Trickle(Url url, Func<DocumentResource?> load)
        {
            if (WptPipes.GetTrickleDelay(WptPipes.Get(url)) is not { } delay)
                return ValueTask.FromResult(load());

            var completion = new TaskCompletionSource<DocumentResource?>();
            runner._eventLoop.QueueTimer(delay, () => completion.SetResult(load()));
            return new ValueTask<DocumentResource?>(completion.Task);
        }

        /// <summary>
        /// The response of a Python handler, after its delay. The redirects are followed when the request follows them
        /// (<see cref="DocumentRequest.FollowRedirects"/>; the library follows the redirects of its requests), up to 20
        /// redirects (https://fetch.spec.whatwg.org/#http-redirect-fetch): more redirects are a network error.
        /// </summary>
        private async ValueTask<DocumentResource?> LoadHandler(Url url, DocumentRequest request, int redirectCount)
        {
            var (resource, delay, retryAfter) = runner.RunHandler(url, request, isFetch: false);
            if (retryAfter is { } retry)
            {
                (resource, delay) = await runner.RunPollingHandlerAsync(url, request, isFetch: false, retry).ConfigureAwait(false);
            }

            if (delay > TimeSpan.Zero)
            {
                await runner.DelayAsync(delay).ConfigureAwait(false);
            }

            if (request.FollowRedirects && resource is { StatusCode: 301 or 302 or 303 or 307 or 308 } && resource.Headers.FirstOrDefault(header => header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase)).Value is { } location)
            {
                if (redirectCount >= 20)
                    return null;

                var locationUrl = new Url(location, url.Href).Href;
                return await FollowRedirect(locationUrl, CreateRedirectRequest(request, locationUrl, resource.Headers), redirectCount + 1).ConfigureAwait(false);
            }

            return resource;
        }

        /// <summary>A request for a directory of the test suite: the redirect to the path with a trailing slash is followed.</summary>
        private ValueTask<DocumentResource?> LoadDirectory(Url url, string path, string directory, DocumentRequest request, int redirectCount)
        {
            var resource = GetDirectoryResource(url, path, directory);
            if (resource.StatusCode != 301 || !request.FollowRedirects)
                return ValueTask.FromResult<DocumentResource?>(resource);

            var location = resource.Headers.First(header => header.Key == "Location").Value;
            return FollowRedirect(location, CreateRedirectRequest(request, location, resource.Headers), redirectCount + 1);
        }

        /// <summary>
        /// The request of the location of a redirect (https://fetch.spec.whatwg.org/#http-redirect-fetch): the referrer policy
        /// is the one of the <c>Referrer-Policy</c> header of the redirect (step 16), and the referrer is determined again for
        /// the new URL (https://fetch.spec.whatwg.org/#main-fetch, step 9).
        /// </summary>
        private static DocumentRequest CreateRedirectRequest(DocumentRequest request, string location, IReadOnlyList<KeyValuePair<string, string>> redirectHeaders)
        {
            var referrerPolicy = ReferrerPolicies.ParseHeader(redirectHeaders) is { Length: > 0 } policy ? policy : request.ReferrerPolicy;
            var referrer = request.Referrer is { } previousReferrer && Urls.UrlParser.Parse(previousReferrer, baseUrl: null) is { } referrerUrl && Urls.UrlParser.Parse(location, baseUrl: null) is { } currentUrl
                ? ReferrerPolicies.DetermineReferrer(referrerUrl, currentUrl, referrerPolicy)
                : null;
            var redirectRequest = new DocumentRequest(location)
            {
                CookieJar = request.CookieJar,
                SiteForCookies = request.SiteForCookies,
                TopLevelOrigin = request.TopLevelOrigin,
                RedirectUrls = [.. request.RedirectUrls, request.Url],
                Destination = request.Destination,
                Initiator = request.Initiator,
                Origin = request.Origin,
                Referrer = referrer,
                ReferrerPolicy = referrerPolicy,
                Mode = request.Mode,
                IsSpeculative = request.IsSpeculative,
                Method = request.Method,
                Body = request.Body,
                BodyContentType = request.BodyContentType,
                Headers = request.Headers,
                IncludeCredentials = request.IncludeCredentials,
                FollowRedirects = request.FollowRedirects,
                IsSynchronous = request.IsSynchronous,
                UserActivation = request.UserActivation,
            };
            return redirectRequest;
        }

        private async ValueTask<DocumentResource?> FollowRedirect(string location, DocumentRequest request, int redirectCount)
        {
            var resource = await Load(location, request, redirectCount).ConfigureAwait(false);
            if (resource is null)
                return null;

            // The location is the URL of the response, or the first redirect of the redirects of the location
            string[] redirectUrls = resource.Url is null ? [] : [location, .. resource.RedirectUrls];
            var url = resource.Url ?? location;
            return resource.Content is { } text
                ? new DocumentResource(text) { Headers = resource.Headers, StatusCode = resource.StatusCode, Url = url, RedirectUrls = redirectUrls }
                : new DocumentResource(resource.ContentBytes!.Value) { Headers = resource.Headers, StatusCode = resource.StatusCode, Url = url, RedirectUrls = redirectUrls };
        }
    }

    /// <summary>
    /// The document of a navigation whose URL has a <c>trickle()</c> pipe: the headers are received at once, and the parts of
    /// the body as wptserve sends them (see <see cref="WptPipes.GetTrickleChunks"/>), so the document is parsed as they arrive.
    /// </summary>
    private DocumentResource? Trickle(Url url, DocumentRequest request, DocumentResource? resource)
    {
        if (resource is not { ContentBytes: { } content, RemainingContentBytes: null } || request.Destination is not (RequestDestination.Document or RequestDestination.Iframe or RequestDestination.Frame)
            || WptPipes.GetTrickleChunks(content.ToArray(), WptPipes.Get(url)) is not var (first, chunks))
        {
            return resource;
        }

        var response = new WptHandlerResponse();
        response.Chunks.AddRange(chunks);
        return new DocumentResource(first) { Headers = resource.Headers, StatusCode = resource.StatusCode, StatusText = resource.StatusText, Url = resource.Url, RedirectUrls = resource.RedirectUrls, RemainingContentBytes = GetChunksAsync(response, repeatAfter: null) };
    }

    /// <summary>
    /// The chunks of a streamed response, each one after its delay measured by the virtual clock. With
    /// <paramref name="repeatAfter"/>, the chunks are sent again after that delay, until the request is canceled.
    /// </summary>
    private async IAsyncEnumerable<ReadOnlyMemory<byte>> GetChunksAsync(WptHandlerResponse response, TimeSpan? repeatAfter, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // A handler that waits for another request (WptHandlerResponse.ChunksCondition): when it gives up, the connection is
        // closed before the end of the body
        if (response.ChunksCondition is { } condition)
        {
            var interval = TimeSpan.FromMilliseconds(100);
            for (var elapsed = TimeSpan.Zero; !condition(); elapsed += interval)
            {
                if (elapsed >= response.ChunksTimeout)
                    throw new IOException("The connection was closed before the end of the body");

                await DelayAsync(interval).ConfigureAwait(false);
            }
        }

        var chunks = response.Chunks;
        try
        {
            while (true)
            {
                foreach (var (delay, data) in chunks)
                {
                    if (delay > TimeSpan.Zero)
                    {
                        await DelayAsync(delay).ConfigureAwait(false);
                    }

                    yield return data;
                }

                // A body that fails after its chunks (e.g. an invalid chunked encoding)
                if (response.ChunksError)
                    throw new IOException("The chunked encoding of the body is invalid");

                response.Completed?.Invoke();

                if (repeatAfter is not { } repeatDelay || cancellationToken.IsCancellationRequested || response.ChunksStopCondition?.Invoke() == true)
                    yield break;

                await DelayAsync(repeatDelay).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    yield break;
            }
        }
        finally
        {
            // The connection is closed: the end of the body, or the client stopped reading it (e.g. an aborted fetch)
            response.Closed?.Invoke();
        }
    }

    /// <summary>Reads the rest of the body of a download (<see cref="DocumentResource.RemainingContentBytes"/>), whose content is discarded.</summary>
    private static async Task ReadDownloadAsync(DocumentResource resource)
    {
        if (resource.RemainingContentBytes is not { } remainingBytes)
            return;

        try
        {
            await foreach (var _ in remainingBytes.ConfigureAwait(false))
            {
            }
        }
        catch (IOException)
        {
            // The connection was closed before the end of the body: the download failed
        }
    }

    /// <summary>Returns a task that completes after <paramref name="delay"/>, measured by the virtual clock.</summary>
    private Task DelayAsync(TimeSpan delay)
    {
        var completion = new TaskCompletionSource();
        _eventLoop.QueueTimer(delay, completion.SetResult);
        return completion.Task;
    }

    /// <summary>The unescaped path of <paramref name="url"/> and the directory of the test suite it designates, or null when it is not a directory.</summary>
    private static (string Path, string Directory)? GetDirectory(Url url)
    {
        var path = Uri.UnescapeDataString(url.Pathname);
        var directory = Path.Combine(WptPaths.Wpt, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(directory) ? (path, directory) : null;
    }

    private DocumentResource? LoadDocument(string url, DocumentRequest? request = null)
    {
        var parsed = new Url(url);
        if (!WptServer.IsHost(parsed))
            return null;

        var path = Uri.UnescapeDataString(parsed.Pathname);
        var file = Path.Combine(WptPaths.Wpt, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file))
        {
            Log("Document not found: " + path);

            // wptserve responds with a 404 error, whose document has the origin of its URL (it is not a network error)
            if (request?.Mode == RequestMode.Navigate)
                return new DocumentResource("""{"error": {"code": 404, "message": ""}}""") { Headers = [new("Content-Type", "application/json")], StatusCode = 404 };

            return null;
        }

        if (path.EndsWith(".asis", StringComparison.Ordinal))
            return LoadAsIs(file);

        var content = File.ReadAllBytes(file);
        if (WptServer.HasSubstitutions(parsed))
        {
            // The substitutions are ASCII: the bytes of the other characters are preserved whatever the encoding of the document
            content = WptServer.Substitute(content, parsed, request is null ? null : GetRequestHeaders(parsed, request));
        }

        var resource = CreateResource(new DocumentResource(content), file, parsed);
        if (request is not null)
        {
            StoreCookies(parsed, request, resource.Headers);
        }

        return resource;
    }

    /// <summary>The URL list of a request (https://fetch.spec.whatwg.org/#concept-request-url-list): the URLs of its redirects, then its URL.</summary>
    private static List<Urls.UrlRecord> GetUrlList(DocumentRequest request) => [.. request.RedirectUrls.Select(url => new Url(url).Record), new Url(request.Url).Record];

    /// <summary>The values of the client hints of the browsing contexts of the tests (see <see cref="BrowsingContextOptions.ClientHints"/>).</summary>
    private static readonly ClientHintsOptions TestClientHints = new()
    {
        Platform = "Linux",
        Architecture = "x86",
        Bitness = "64",
        DeviceMemory = 8,
        RoundTripTime = TimeSpan.FromMilliseconds(50),
        Downlink = 10,
        EffectiveConnectionType = "4g",
    };

    /// <summary>The User-Agent header sent with the requests.</summary>
    private const string UserAgent = Navigator.DefaultUserAgent;

    /// <summary>
    /// The headers of a request as the WPT server receives them. The requests of the fetch algorithm (whose redirects are
    /// followed by the library) have its headers (<see cref="DocumentRequest.Headers"/>); the headers of the other requests are
    /// the ones a browser sends (in the order of Chromium): the <c>Accept</c> header of the destination, the <c>Origin</c>
    /// header of a cross-origin CORS request (or of a request that is not a GET request), the <c>Referer</c> header
    /// (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch), the <c>Origin</c>, <c>Ping-From</c> and <c>Ping-To</c>
    /// headers of a ping and the <c>Content-Type</c> of the body. The network
    /// layer adds <c>Host</c>, <c>Connection</c>, <c>User-Agent</c>, <c>Accept-Encoding</c>, <c>Accept-Language</c> and the cookies.
    /// </summary>
    internal static List<KeyValuePair<string, string>> GetRequestHeaders(Url url, DocumentRequest request)
    {
        var headers = new List<KeyValuePair<string, string>>();
        void AddHeader(string name, string value)
        {
            if (!headers.Exists(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                headers.Add(new(name, value));
            }
        }

        if (request.Destination == RequestDestination.Empty || !request.FollowRedirects)
        {
            headers.AddRange(request.Headers);
            AddHeader("Host", url.Host);
            AddHeader("Connection", "keep-alive");
            AddHeader("User-Agent", UserAgent);
            AddHeader("Accept-Language", "en-US,en");
            AddHeader("Accept-Encoding", "gzip, deflate");
        }
        else
        {
            headers.Add(new("Host", url.Host));
            headers.Add(new("Connection", "keep-alive"));
            headers.Add(new("User-Agent", UserAgent));
            headers.Add(new("Accept", request.Destination switch
            {
                RequestDestination.Document or RequestDestination.Iframe or RequestDestination.Frame or RequestDestination.Embed or RequestDestination.Object => "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                RequestDestination.Image => "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8",
                RequestDestination.Style => "text/css,*/*;q=0.1",
                RequestDestination.Json => "application/json,*/*;q=0.5",
                RequestDestination.Text => "text/plain,*/*;q=0.5",
                _ => "*/*",
            }));

            // The Origin header of a ping, else the one a browser sends
            if (request.Destination == RequestDestination.Ping && HeaderList.Get(request.Headers, "Origin") is { } originHeader)
            {
                headers.Add(new("Origin", originHeader));
            }
            else if (request.Origin is { } origin && ((request.Mode == RequestMode.Cors && url.Origin != origin) || request.Method != "GET"))
            {
                headers.Add(new("Origin", origin));
            }

            // https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, step 8.13
            FetchMetadata.AppendHeaders(headers, request, GetUrlList(request));
            if (request.Referrer is { } referrer)
            {
                headers.Add(new("Referer", referrer));
            }

            // The Ping-From and Ping-To headers of a ping
            if (request.Destination == RequestDestination.Ping)
            {
                headers.AddRange(request.Headers.Where(header => !header.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase)));
            }

            headers.Add(new("Accept-Encoding", "gzip, deflate"));
            headers.Add(new("Accept-Language", "en-US,en"));
            if (request.Body is { } body)
            {
                if (request.BodyContentType is { } contentType)
                {
                    headers.Add(new("Content-Type", contentType));
                }

                headers.Add(new("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        if (request.GetCookieHeader(url.Href) is { Length: > 0 } cookies)
        {
            AddHeader("Cookie", cookies);
        }

        return headers;
    }

    /// <summary>Stores the cookies of the <c>Set-Cookie</c> headers of a response in the cookies of the browsing context.</summary>
    private static void StoreCookies(Url url, DocumentRequest request, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        request.StoreCookies(headers.Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)).Select(header => header.Value), url.Href);
    }

    /// <summary>
    /// Runs the Python handler of a request (<see cref="WptPythonHandlers"/>) and returns its response, with the pipes of the
    /// URL applied, and its delay (measured by the virtual clock). The cookies of the response are stored. A handler that
    /// is not emulated is a <c>404</c> response for <c>fetch()</c> and <c>XMLHttpRequest</c> (so that the tests that only
    /// check the response of another request continue), and a network error for the other requests. A handler that throws
    /// returns a <c>500</c> response, as wptserve does.
    /// </summary>
    /// <param name="url">The URL of the request.</param>
    /// <param name="request">The request.</param>
    /// <param name="isFetch">Whether the request is a request of <c>fetch()</c> or <c>XMLHttpRequest</c>: the response has a status message and a <c>Content-Length</c>.</param>
    /// <param name="pollingElapsed">The time elapsed since the request was received, when a handler that polls the stash runs again.</param>
    private (DocumentResource? Resource, TimeSpan Delay, TimeSpan? RetryAfter) RunHandler(Url url, DocumentRequest request, bool isFetch, TimeSpan pollingElapsed = default, CancellationToken cancellationToken = default)
    {
        var path = Uri.UnescapeDataString(url.Pathname);
        WptHandlerResponse? response;
        try
        {
            response = WptPythonHandlers.Handle(new WptHandlerRequest(url, request, GetRequestHeaders(url, request)) { PollingElapsed = pollingElapsed, Now = Now }, _stash);
            if (response?.RetryAfter is { } retryAfter)
                return (null, TimeSpan.Zero, retryAfter);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("Handler error: " + path + ": " + ex);
            response = new WptHandlerResponse { Status = 500 };
            response.Set("Content-Type", "text/plain");
        }

        if (response is null)
        {
            // A handler that does not exist is a 404 response of wptserve, as a missing file
            var handlerExists = File.Exists(Path.Combine(WptPaths.Wpt, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            Log((handlerExists ? "Handler not emulated: " : "Handler not found: ") + path);
            if (!isFetch && handlerExists)
                return (null, TimeSpan.Zero, null);

            var notFound = "404 Not Found"u8.ToArray();
            return (new DocumentResource(notFound) { StatusCode = 404, StatusText = "Not Found", Headers = [new("Content-Type", "text/plain"), new("Content-Length", "13")] }, TimeSpan.Zero, null);
        }

        // https://web-platform-tests.org/writing-tests/server-pipes.html: the pipes apply to the responses of the handlers too
        var pipes = WptPipes.Get(url);
        // wptserve writes the headers as they are: a line break in a value ends the header, and the browsers ignore the
        // next line (which is not a header)
        var headers = response.Headers.Select(header => header.Value.IndexOfAny(['\r', '\n']) is var lineBreak and >= 0 ? new KeyValuePair<string, string>(header.Key, header.Value[..lineBreak]) : header).ToList();
        WptPipes.ApplyHeaders(headers, pipes);
        var status = WptPipes.GetStatus(pipes) ?? response.Status;
        var body = WptPipes.HasSubstitutions(pipes) ? WptServer.Substitute(response.Body, url) : response.Body;
        WptPipes.ApplyGzip(headers, pipes, body);
        StoreCookies(url, request, headers);

        // A response that never completes is aborted by the test
        if (response.Pending)
            return (null, TimeSpan.FromHours(1), null);

        // The document of a navigation is parsed as the parts of a trickle() pipe arrive; the other responses are delayed
        var isNavigation = request.Destination is RequestDestination.Document or RequestDestination.Iframe or RequestDestination.Frame;
        var delay = response.Delay;
        if (isNavigation && response.Chunks.Count == 0 && WptPipes.GetTrickleChunks(body, pipes) is var (firstPart, trickleChunks))
        {
            body = firstPart;
            response.Chunks.AddRange(trickleChunks);
        }
        else
        {
            delay += WptPipes.GetTrickleDelay(pipes) ?? TimeSpan.Zero;
        }
        if (response.NetworkError)
            return (null, delay, null);

        // The document of a navigation is parsed as its chunks arrive, a media resource is processed as they arrive, the
        // content of a template with a src attribute is parsed as it arrives, and an event stream is interpreted as its chunks
        // arrive (the library waits for the end of the body for the requests that are not the ones of an EventSource); the
        // other responses are complete after the delays of their chunks. A handler that runs code once its whole body was
        // sent (WptHandlerResponse.Completed) streams the body of the navigations of the object and embed elements too, so
        // that a client that stops reading it (e.g. a blocked download) is seen.
        var isContainerNavigation = request.Destination is RequestDestination.Object or RequestDestination.Embed && response.Completed is not null;
        if (response.Chunks.Count > 0 && (isNavigation || isContainerNavigation || request.Destination is RequestDestination.Audio or RequestDestination.Video || request.Initiator is HtmlTemplateElement))
            return (new DocumentResource(body) { Headers = headers, StatusCode = status, RemainingContentBytes = GetChunksAsync(response, repeatAfter: null, CancellationToken.None) }, delay, null);

        if (response.Chunks.Count > 0 && !request.IsSynchronous && HeaderList.ExtractMimeType(headers)?.Essence == "text/event-stream")
            return (new DocumentResource(body) { Headers = headers, StatusCode = status, StatusText = GetStatusText(url, status, response.StatusText), RemainingContentBytes = GetChunksAsync(response, response.ChunksRepeatAfter, CancellationToken.None) }, delay, null);

        // The body of fetch() is read as its chunks arrive. The connection is closed once, at the end of the body or when the
        // request is canceled (e.g. an aborted fetch whose body is not read)
        if (response.Chunks.Count > 0 && isFetch && request.StreamsResponseBody)
        {
            if (response.Closed is { } closed)
            {
                var closedOnce = 0;
                void Close()
                {
                    if (Interlocked.Exchange(ref closedOnce, 1) == 0)
                    {
                        closed();
                    }
                }

                response.Closed = Close;
                cancellationToken.Register(Close);
            }

            return (new DocumentResource(body)
            {
                StatusCode = status,
                StatusText = response.StatusText ?? WptReasonPhrase(status),
                Headers = response.Streamed ? headers : AddContentLength(headers, body.Length + response.Chunks.Sum(chunk => chunk.Data.Length)),
                RemainingContentBytes = GetChunksAsync(response, response.ChunksRepeatAfter, CancellationToken.None),
            }, delay, null);
        }

        foreach (var (chunkDelay, chunk) in response.Chunks)
        {
            delay += chunkDelay;
            body = [.. body, .. chunk];
        }

        if (response.ChunksError)
            return (null, delay, null);

        response.Completed?.Invoke();

        if (!isFetch)
            return (new DocumentResource(body) { Headers = headers, StatusCode = status }, delay, null);

        return (new DocumentResource(body)
        {
            StatusCode = status,
            StatusText = GetStatusText(url, status, response.StatusText),
            Headers = response.Streamed ? headers : AddContentLength(headers, body.Length),
        }, delay, null);
    }

    /// <summary>
    /// Loads the response of a request of fetch() or XMLHttpRequest, as the WPT server does: an emulated Python handler
    /// (see <see cref="WptPythonHandlers"/>) or a file of the test suite, with its status, its status message and its
    /// headers. The redirects are not followed (the library follows them). The responses of the handlers and the
    /// <c>pipe=trickle()</c> responses are delayed with the virtual clock, except for the synchronous requests of the windows.
    /// </summary>
    private ValueTask<DocumentResource?> LoadFetchResource(DocumentRequest request, CancellationToken cancellationToken)
    {
        var url = new Url(request.Url);
        if (!WptServer.IsHost(url))
            return ValueTask.FromResult<DocumentResource?>(null);

        var path = Uri.UnescapeDataString(url.Pathname);
        DocumentResource? resource;
        var delay = TimeSpan.Zero;
        if (WptPythonHandlers.IsHandler(url))
        {
            (resource, delay, var retryAfter) = RunHandler(url, request, isFetch: true, cancellationToken: cancellationToken);
            if (retryAfter is { } retry)
                return LoadPollingFetchResourceAsync(url, request, retry);
        }
        else
        {
            var file = Path.Combine(WptPaths.Wpt, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (path == "/resources/WebIDLParser.js")
            {
                file = Path.Combine(WptPaths.Wpt, "resources", "webidl2", "lib", "webidl2.js");
            }

            if (Directory.Exists(file))
            {
                resource = GetDirectoryResource(url, path, file);
            }
            else if (File.Exists(file) && file.EndsWith(".asis", StringComparison.Ordinal))
            {
                // https://web-platform-tests.org/writing-tests/server-features.html#asis-files: a raw HTTP response
                resource = ParseRawResponse(File.ReadAllBytes(file));
            }
            else if (File.Exists(file))
            {
                var bytes = File.ReadAllBytes(file);
                if (WptServer.HasSubstitutions(url))
                {
                    bytes = WptServer.Substitute(bytes, url, GetRequestHeaders(url, request));
                }

                // The body of a file served with a content coding is decoded (see CreateResource)
                var fileResource = CreateResource(new DocumentResource(bytes), file, url, GetRequestHeaders(url, request));
                bytes = fileResource.ContentBytes?.ToArray() ?? bytes;
                resource = new DocumentResource(bytes) { StatusCode = fileResource.StatusCode, StatusText = GetStatusText(url, fileResource.StatusCode), Headers = AddContentLength(fileResource.Headers, bytes.Length) };
                StoreCookies(url, request, resource.Headers);
                delay = WptPipes.GetTrickleDelay(WptPipes.Get(url)) ?? TimeSpan.Zero;
            }
            else if (GetWorkerWrapper(path, url) is { } wrapper)
            {
                // The script generated for a .any.js test run in a worker (e.g. fetched by the test with self.location.href)
                resource = wrapper;
            }
            else if (_test.Kind != WptTestKind.Html && path == new Url(_test.Url).Pathname)
            {
                // The page generated for a .any.js or .window.js test (e.g. fetched by the test with self.location.href)
                var body = Encoding.UTF8.GetBytes(GetTestHtml());
                resource = new DocumentResource(body) { Headers = [new("Content-Type", "text/html; charset=utf-8"), new("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))] };
            }
            else
            {
                // wptserve returns a 404 page
                var body = "404 Not Found"u8.ToArray();
                resource = new DocumentResource(body) { StatusCode = 404, StatusText = "Not Found", Headers = [new("Content-Type", "text/plain"), new("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))] };
            }
        }

        // A synchronous request of a window blocks the thread that advances the clock: it is not delayed. A synchronous request
        // of a worker blocks the thread of the worker only (the clock is advanced while it waits)
        if (delay <= TimeSpan.Zero || (request.IsSynchronous && Environment.CurrentManagedThreadId == _eventLoopThreadId))
            return ValueTask.FromResult(resource);

        var completion = new TaskCompletionSource<DocumentResource?>();
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        _eventLoop.QueueTimer(delay, () => completion.TrySetResult(resource));
        return new ValueTask<DocumentResource?>(completion.Task);
    }

    /// <summary>The response of a handler of a request of fetch() or XMLHttpRequest that polls the stash (see <see cref="RunPollingHandlerAsync"/>).</summary>
    private async ValueTask<DocumentResource?> LoadPollingFetchResourceAsync(Url url, DocumentRequest request, TimeSpan retryAfter)
    {
        var (resource, delay) = await RunPollingHandlerAsync(url, request, isFetch: true, retryAfter).ConfigureAwait(false);
        if (delay > TimeSpan.Zero && !IsSynchronousWindowRequest(request))
        {
            await DelayAsync(delay).ConfigureAwait(false);
        }

        return resource;
    }

    /// <summary>
    /// Runs a handler that polls the stash (<see cref="WptHandlerResponse.RetryAfter"/>) again after each delay it asks for,
    /// measured by the virtual clock, until it responds. A synchronous request of a window blocks the thread that advances the
    /// clock: the handler runs again at once.
    /// </summary>
    private async ValueTask<(DocumentResource? Resource, TimeSpan Delay)> RunPollingHandlerAsync(Url url, DocumentRequest request, bool isFetch, TimeSpan retryAfter)
    {
        var elapsed = TimeSpan.Zero;
        while (true)
        {
            if (!IsSynchronousWindowRequest(request))
            {
                await DelayAsync(retryAfter).ConfigureAwait(false);
            }

            elapsed += retryAfter;
            var (resource, delay, next) = RunHandler(url, request, isFetch, elapsed);
            if (next is not { } nextRetry)
                return (resource, delay);

            retryAfter = nextRetry;
        }
    }

    /// <summary>Whether the request is a synchronous request of a window, which blocks the thread that advances the virtual clock.</summary>
    private bool IsSynchronousWindowRequest(DocumentRequest request) => request.IsSynchronous && Environment.CurrentManagedThreadId == _eventLoopThreadId;

    /// <summary>
    /// The response of wptserve to a request for a directory: a <c>301</c> redirect to the path with a trailing slash, or the
    /// listing of the directory (see <see cref="WptServer.GetDirectoryListing"/>).
    /// </summary>
    private static DocumentResource GetDirectoryResource(Url url, string path, string directory)
    {
        if (!path.EndsWith('/', StringComparison.Ordinal))
        {
            var location = url.Protocol + "//" + url.Host + url.Pathname + "/" + url.Search;
            return new DocumentResource(Array.Empty<byte>()) { StatusCode = 301, StatusText = GetStatusText(url, 301), Headers = [new("Location", location), new("Content-Length", "0")] };
        }

        var body = WptServer.GetDirectoryListing(path, directory);
        return new DocumentResource(body) { StatusText = GetStatusText(url, 200), Headers = [new("Content-Type", "text/html"), new("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))] };
    }

    private static List<KeyValuePair<string, string>> AddContentLength(IReadOnlyList<KeyValuePair<string, string>> headers, int length)
    {
        var result = new List<KeyValuePair<string, string>>(headers);
        if (!result.Exists(header => header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new("Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return result;
    }

    /// <summary>Parses a raw HTTP/1.1 response (a <c>.asis</c> file): the status line, the headers and the body.</summary>
    private static DocumentResource? ParseRawResponse(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var separatorLength = 4;
        if (headerEnd < 0)
        {
            headerEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
            separatorLength = 2;
        }

        var head = headerEnd < 0 ? text : text[..headerEnd];
        var body = headerEnd < 0 ? [] : bytes.AsSpan(headerEnd + separatorLength).ToArray();
        var lines = head.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var statusLine = lines[0].Split(' ', 3);
        if (statusLine.Length < 2 || !int.TryParse(statusLine[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var status))
            return null;

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var index = line.IndexOf(':', StringComparison.Ordinal);
            if (index > 0)
            {
                headers.Add(new(line[..index], line[(index + 1)..]));
            }
        }

        return new DocumentResource(body) { StatusCode = status, StatusText = statusLine.Length > 2 ? statusLine[2] : "", Headers = headers };
    }

    /// <summary>
    /// The status message of a response of wptserve: the one set by the handler, else the reason phrase of the status. The
    /// responses of the HTTP/2 server have none (HTTP/2 has no reason phrase).
    /// </summary>
    private static string GetStatusText(Url url, int status, string? statusText = null) => WptServer.IsHttp2(url) ? "" : statusText ?? WptReasonPhrase(status);

    /// <summary>The reason phrases of wptserve (the responses of Python's http.server).</summary>
    private static string WptReasonPhrase(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        206 => "Partial Content",
        301 => "Moved Permanently",
        302 => "Found",
        303 => "See Other",
        304 => "Not Modified",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "",
    };

    /// <summary>
    /// Returns the response of a <c>.asis</c> file, which is the whole HTTP response (status line, headers and body), or null
    /// (a network error) when the file is not an HTTP response (https://web-platform-tests.org/writing-tests/server-features.html#asis-files).
    /// </summary>
    private static DocumentResource? LoadAsIs(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var text = Encoding.Latin1.GetString(bytes);
        var statusLine = Regex.Match(text, @"\AHTTP/1\.[01] (?<status>[0-9]{3})[^\n]*\n", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        if (!statusLine.Success)
            return null;

        var headers = new List<KeyValuePair<string, string>>();
        var index = statusLine.Length;
        while (index < text.Length)
        {
            var end = text.IndexOf('\n', index, StringComparison.Ordinal);
            var line = text[index..(end < 0 ? text.Length : end)].TrimEnd('\r');
            index = end < 0 ? text.Length : end + 1;
            if (line.Length == 0)
                break;

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
            }
        }

        return new DocumentResource(bytes.AsMemory(index)) { Headers = headers, StatusCode = int.Parse(statusLine.Groups["status"].Value, System.Globalization.CultureInfo.InvariantCulture) };
    }

    /// <summary>Sets the status and the headers of a file of the test suite (see <see cref="GetHeaders"/>).</summary>
    private static DocumentResource CreateResource(DocumentResource resource, string file, Url url, IReadOnlyList<KeyValuePair<string, string>>? requestHeaders = null)
    {
        var headers = GetHeaders(file, url, requestHeaders);
        if (!headers.Exists(header => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            headers.Insert(0, new("Content-Type", GetContentType(file)));
        }

        // https://web-platform-tests.org/writing-tests/server-pipes.html#status
        var pipes = WptPipes.Get(url);
        var status = WptPipes.GetStatus(pipes) ?? 200;
        WptPipes.ApplyGzip(headers, pipes, resource.Content is { } content ? System.Text.Encoding.UTF8.GetBytes(content) : resource.ContentBytes!.Value.Span);

        // A file served with a content coding (e.g. foo.text.gz and its .headers file) is decoded by the network layer of a
        // browser: the Content-Length is the length of the encoded body
        if (resource.ContentBytes is { } encodedBody && !pipes.Exists(pipe => pipe.Name == "gzip") && HeaderList.Get(headers, "Content-Encoding") is { } contentEncoding && WptPipes.Decode(encodedBody.Span, contentEncoding) is { } decodedBody)
        {
            headers.RemoveAll(header => header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase));
            headers.Add(new("Content-Length", encodedBody.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return new DocumentResource(decodedBody) { Headers = headers, StatusCode = status };
        }

        return resource.Content is { } text
            ? new DocumentResource(text) { Headers = headers, StatusCode = status }
            : new DocumentResource(resource.ContentBytes!.Value) { Headers = headers, StatusCode = status };
    }

    /// <summary>
    /// The .headers files of a file of the test suite, in the order in which wptserve concatenates their headers: the
    /// <c>__dir__.headers</c> files of its directory, then its own .headers files.
    /// </summary>
    private static IEnumerable<string> GetHeadersFiles(string file)
    {
        var directory = Path.GetDirectoryName(file)!;
        return new[] { Path.Combine(directory, "__dir__.headers"), Path.Combine(directory, "__dir__.sub.headers"), file + ".headers", Path.ChangeExtension(file, null) + ".sub.headers", file + ".sub.headers" }.Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the headers of a file of the test suite: the ones of the .headers files (<see cref="GetHeadersFiles"/>) and of
    /// the <c>pipe=header()</c> query parameter (https://web-platform-tests.org/writing-tests/server-features.html#headers).
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="url">The URL of the request.</param>
    /// <param name="requestHeaders">The headers of the request, for the substitutions of the <c>.sub.headers</c> files (e.g. <c>{{header_or_default(Origin, *)}}</c>), or null when they are unknown.</param>
    private static List<KeyValuePair<string, string>> GetHeaders(string file, Url url, IReadOnlyList<KeyValuePair<string, string>>? requestHeaders = null)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var headersFile in GetHeadersFiles(file))
        {
            if (WptFiles.ReadHeadersFile(headersFile) is not { } lines)
                continue;

            // The .sub.headers files get the substitutions (https://web-platform-tests.org/writing-tests/server-features.html#headers)
            if (headersFile.EndsWith(".sub.headers", StringComparison.Ordinal))
            {
                var variables = new Dictionary<string, string>(StringComparer.Ordinal);
                lines = [.. lines.Select(line => WptServer.Substitute(line, url, requestHeaders, escape: false, variables: variables))];
            }

            foreach (var line in lines)
            {
                var index = line.IndexOf(':', StringComparison.Ordinal);
                if (index > 0)
                {
                    headers.Add(new(line[..index].Trim(), line[(index + 1)..].Trim()));
                }
            }
        }

        // https://web-platform-tests.org/writing-tests/server-pipes.html#header
        WptPipes.ApplyHeaders(headers, WptPipes.Get(url));

        return headers;
    }

    private static string GetContentType(string file)
    {
        string? contentType = null;
        foreach (var headersFile in GetHeadersFiles(file).Where(headersFile => !headersFile.EndsWith(".sub.headers", StringComparison.Ordinal)))
        {
            if (WptFiles.ReadHeadersFile(headersFile) is not { } lines)
                continue;

            foreach (var line in lines)
            {
                var index = line.IndexOf(':', StringComparison.Ordinal);
                if (index > 0 && line[..index].Trim().Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = line[(index + 1)..].Trim();
                }
            }
        }

        if (contentType is not null)
            return contentType;

        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html",
            ".xhtml" or ".xht" => "application/xhtml+xml",
            ".xml" => "application/xml",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".js" or ".mjs" => "text/javascript",
            ".css" => "text/css",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".avif" => "image/avif",
            ".jxl" => "image/jxl",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            ".webm" => "video/webm",
            ".mp4" or ".m4v" => "video/mp4",
            ".m4a" => "audio/mp4",
            ".ogv" => "video/ogg",
            ".oga" or ".opus" => "audio/ogg",
            ".ogg" => "application/ogg",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/x-wav",
            ".flac" => "audio/flac",
            ".vtt" => "text/vtt",
            ".event_stream" => "text/event-stream",
            _ => "application/octet-stream",
        };
    }

    private string GetTestHtml()
    {
        if (_test.Kind == WptTestKind.Html)
            return File.ReadAllText(_test.FilePath);

        // https://web-platform-tests.org/writing-tests/testharness.html#multi-global-tests
        var meta = WptTestDiscovery.ParseMeta(File.ReadAllText(_test.FilePath));
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<meta charset=utf-8>\n");
        if (meta.Find(m => m.Key == "title") is { Value: { } title })
        {
            html.Append("<title>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>\n");
        }

        if (meta.Exists(m => m.Key == "timeout" && m.Value == "long"))
        {
            html.Append("<meta name=timeout content=long>\n");
        }

        if (_test.Kind == WptTestKind.AnyJs)
        {
            html.Append("<script>self.GLOBAL = { isWindow: function() { return true; }, isWorker: function() { return false; }, isShadowRealm: function() { return false; } };</script>\n");
        }

        // The tests of a worker are run by the worker, and their results are sent to the page (the wrappers of wptserve,
        // https://github.com/web-platform-tests/wpt/blob/master/tools/serve/serve.py)
        if (_test.IsWorker)
        {
            var workerUrl = JavaScriptString("/" + GetWorkerScriptPath() + _test.Variant);
            var worker = _test.Kind switch
            {
                WptTestKind.AnyJsDedicatedWorkerModule => $"new Worker({workerUrl}, {{ type: 'module' }})",
                WptTestKind.AnyJsSharedWorker => $"new SharedWorker({workerUrl})",
                WptTestKind.AnyJsSharedWorkerModule => $"new SharedWorker({workerUrl}, {{ type: 'module' }})",
                _ => $"new Worker({workerUrl})",
            };
            html.Append("<script src=\"/resources/testharness.js\"></script>\n<script src=\"/resources/testharnessreport.js\"></script>\n<div id=log></div>\n");
            if (_test.Kind is WptTestKind.AnyJsServiceWorker or WptTestKind.AnyJsServiceWorkerModule)
            {
                // The service worker is registered for a scope without clients, after the registration of a previous run is removed
                var options = _test.Kind == WptTestKind.AnyJsServiceWorkerModule ? "{ scope, type: 'module' }" : "{ scope }";
                html.Append("<script>\n(async function() {\n  const scope = 'does/not/exist';\n  let reg = await navigator.serviceWorker.getRegistration(scope);\n  if (reg) await reg.unregister();\n");
                html.Append("  reg = await navigator.serviceWorker.register(").Append(workerUrl).Append(", ").Append(options).Append(");\n  fetch_tests_from_worker(reg.installing);\n})();\n</script>\n");
                return html.ToString();
            }

            html.Append("<script>\nfetch_tests_from_worker(").Append(worker).Append(");\n</script>\n");
            return html.ToString();
        }

        html.Append("<script src=\"/resources/testharness.js\"></script>\n<script src=\"/resources/testharnessreport.js\"></script>\n");
        foreach (var script in meta.Where(m => m.Key == "script"))
        {
            html.Append("<script src=\"").Append(script.Value).Append("\"></script>\n");
        }

        html.Append("<div id=log></div>\n<script src=\"/").Append(_test.RelativePath).Append("\"></script>\n");
        return html.ToString();
    }

    /// <summary>The path of the script of the worker of the test: the <c>.worker.js</c> file, or the wrapper of the <c>.any.js</c> file.</summary>
    private string GetWorkerScriptPath() => _test.Kind switch
    {
        WptTestKind.WorkerJs => _test.RelativePath,
        WptTestKind.AnyJsDedicatedWorkerModule or WptTestKind.AnyJsSharedWorkerModule or WptTestKind.AnyJsServiceWorkerModule => _test.RelativePath[..^".any.js".Length] + ".any.worker-module.js",
        _ => _test.RelativePath[..^".any.js".Length] + ".any.worker.js",
    };

    /// <summary>
    /// The wrapper of a <c>.any.js</c> test run in a worker (<c>.any.worker.js</c> or <c>.any.worker-module.js</c>), as
    /// served by wptserve: the harness, the scripts of the META comments and the test, then <c>done()</c>, with the headers
    /// of the .headers files of the test file. Returns null when the path is not the wrapper of a test file.
    /// </summary>
    private static DocumentResource? GetWorkerWrapper(string path, Url url)
    {
        var isModule = path.EndsWith(".any.worker-module.js", StringComparison.Ordinal);
        if (!isModule && !path.EndsWith(".any.worker.js", StringComparison.Ordinal))
            return null;

        var testPath = path[..^(isModule ? ".any.worker-module.js" : ".any.worker.js").Length] + ".any.js";
        var file = Path.Combine(WptPaths.Wpt, testPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file))
            return null;

        var meta = WptTestDiscovery.ParseMeta(File.ReadAllText(file));
        var script = new StringBuilder();
        if (meta.Find(m => m.Key == "title") is { Value: { } title })
        {
            script.Append("self.META_TITLE = ").Append(JavaScriptString(title)).Append(";\n");
        }

        script.Append("self.GLOBAL = { isWindow: function() { return false; }, isWorker: function() { return true; }, isShadowRealm: function() { return false; } };\n");
        var import = isModule ? "import " : "importScripts(";
        var end = isModule ? ";\n" : ");\n";
        script.Append(import).Append(JavaScriptString("/resources/testharness.js")).Append(end);
        foreach (var item in meta.Where(m => m.Key == "script"))
        {
            script.Append(import).Append(JavaScriptString(item.Value)).Append(end);
        }

        script.Append(import).Append(JavaScriptString(testPath)).Append(end);
        script.Append("done();\n");
        return new DocumentResource(script.ToString()) { Headers = [new("Content-Type", "text/javascript"), .. GetHeaders(file, url)] };
    }

    private static string JavaScriptString(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    /// <summary>
    /// The cookies of the test driver (https://w3c.github.io/webdriver/#get-all-cookies): the associated cookies of the
    /// active document (the cookies its URL would receive in a same-site request, including the HttpOnly cookies), as the
    /// JSON array of the serialized cookies (https://w3c.github.io/webdriver/#dfn-serialized-cookie), optionally only the
    /// cookies with a name.
    /// </summary>
    private string GetWebDriverCookies(Document document, string? name)
    {
        var context = CookieContexts.ForWebDriver(document);
        var cookies = new List<Dictionary<string, object>>();
        foreach (var cookie in _cookieJar.Retrieve(context, updateAccessTime: false))
        {
            var cookieName = Infra.InfraString.Utf8DecodeWithoutBom(cookie.Name);
            if (name is not null && cookieName != name)
                continue;

            var serialized = new Dictionary<string, object>
            {
                ["name"] = cookieName,
                ["value"] = Infra.InfraString.Utf8DecodeWithoutBom(cookie.Value),
                ["path"] = cookie.Path,
                ["domain"] = cookie.Domain,
                ["secure"] = cookie.SecureOnly,
                ["httpOnly"] = cookie.HttpOnly,
                ["sameSite"] = cookie.SameSite switch { CookieSameSite.None => "None", CookieSameSite.Strict => "Strict", _ => "Lax" },
            };
            if (cookie.ExpiryTime is { } expiry)
            {
                serialized["expiry"] = expiry.ToUnixTimeSeconds();
            }

            cookies.Add(serialized);
        }

        return JsonSerializer.Serialize(cookies);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Global object
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Initializes the global object of a realm with the functions the test runner provides (it runs in the realm).</summary>
    internal void SetupGlobals<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm)
        where TAdapter : struct, IJsAdapter<TValue>
        where TValue : class
    {
        // The tests of the workers communicate with the test page by messages (fetch_tests_from_worker)
        if (realm.Window is null)
            return;

        var adapter = realm.Adapter;

        // The functions are not enumerable: the tests that enumerate the properties of a window (e.g. to call each method
        // with another this value) must not see them
        void Define(string name, int length, JsHostFunction<TValue> function)
            => adapter.DefineOwnProperty(realm.Global, adapter.FromString(name), JsPropertyDescriptor<TValue>.Data(adapter.CreateFunction(name, length, function), writable: true, enumerable: false, configurable: true));

        Define("__wpt_report", 1, (_, args) =>
        {
            // Only the test page reports the results (the documents of the iframes may include testharness.js)
            if (ReferenceEquals(realm.Window, _document?.RelevantWindow))
            {
                OnReport(adapter.ToString(args[0]));
            }

            return adapter.Undefined;
        });
        Define("__wpt_bidi_log_dispatcher", 1, (_, args) =>
        {
            // The function of testdriver-vendor.js that emits the WebDriver BiDi log.entryAdded events of the realm
            var dispatch = args[0];
            _consoleListeners.AddOrUpdate(realm.GlobalScope, message => realm.Run(() =>
            {
                var values = new TValue[message.Arguments.Count];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = realm.ToJs(message.Arguments[i]);
                }

                var level = message.Level switch
                {
                    ConsoleMessageLevel.Debug => "debug",
                    ConsoleMessageLevel.Warning => "warn",
                    ConsoleMessageLevel.Error => "error",
                    _ => "info",
                };
                return adapter.Call(dispatch, adapter.Undefined, [adapter.FromString(message.Method), adapter.FromString(level), adapter.FromString(message.Text), adapter.CreateArray(values)]);
            }));
            return adapter.Undefined;
        });
        // The functions of a promise of testdriver-vendor.js, called from a task of the event loop
        Action<string?> CreateCompletion(TValue resolve, TValue reject) => error => realm.Run(() =>
            error is null ? adapter.Call(resolve, adapter.Undefined, []) : adapter.Call(reject, adapter.Undefined, [adapter.FromString(error)]));

        Define("__wpt_click", 5, (_, args) =>
        {
            var element = realm.ToInterface<Element>(args[0], "Element");
            var (x, y) = (adapter.ToNumber(args[1]), adapter.ToNumber(args[2]));
            var sequence = new WptActionSequence(realm.Window);
            sequence.Add(0, () => WptTestDriver.Click(element, x, y));
            sequence.Run(CreateCompletion(args[3], args[4]));
            return adapter.Undefined;
        });

        // The action sequences of the realm that are being built (see testdriver-vendor.js)
        var sequences = new Dictionary<int, WptActionSequence>();
        WptActionSequence GetSequence(TValue id) => sequences[(int)adapter.ToNumber(id)];

        // The origin of an action: an element, "pointer" (a string), or the viewport (null)
        object? GetOrigin(TValue value)
        {
            if (adapter.IsObject(value))
                return realm.ToInterface<Element>(value, "Element");

            return adapter.ToString(value) == "pointer" ? "pointer" : null;
        }

        Define("__wpt_actions_create", 1, (_, args) =>
        {
            var id = sequences.Count == 0 ? 1 : sequences.Keys.Max() + 1;
            sequences.Add(id, new WptActionSequence(realm.ToInterface<Window>(args[0], "Window"), releaseActions: true));
            return adapter.FromNumber(id);
        });
        Define("__wpt_actions_pause", 3, (_, args) =>
        {
            GetSequence(args[0]).AddDuration((int)adapter.ToNumber(args[1]), adapter.ToNumber(args[2]));
            return adapter.Undefined;
        });
        Define("__wpt_actions_pointer", 11, (_, args) =>
        {
            // (sequence, tick, source id, pointer type, action type, origin, x, y, button, properties, duration of a move)
            var sequence = GetSequence(args[0]);
            var (sourceId, pointerType, type, origin) = (adapter.ToString(args[2]), adapter.ToString(args[3]), adapter.ToString(args[4]), GetOrigin(args[5]));
            var (x, y, button) = (adapter.ToNumber(args[6]), adapter.ToNumber(args[7]), (int)adapter.ToNumber(args[8]));
            var properties = WptTestDriver.ParsePointerProperties(adapter.ToString(args[9]));
            var duration = args.Length > 10 ? adapter.ToNumber(args[10]) : 0;
            Action<Action<double>>? interpolate = duration > 0 ? step => sequence.AddMove(step, duration) : null;
            sequence.Add((int)adapter.ToNumber(args[1]), () => WptTestDriver.PointerAction(sequence.Context, sourceId, pointerType, type, origin, x, y, button, properties, interpolate));
            return adapter.Undefined;
        });
        Define("__wpt_actions_wheel", 7, (_, args) =>
        {
            var sequence = GetSequence(args[0]);
            var origin = GetOrigin(args[2]);
            var (x, y, deltaX, deltaY) = (adapter.ToNumber(args[3]), adapter.ToNumber(args[4]), adapter.ToNumber(args[5]), adapter.ToNumber(args[6]));
            sequence.Add((int)adapter.ToNumber(args[1]), () => WptTestDriver.WheelAction(sequence.Context, origin, x, y, deltaX, deltaY));
            return adapter.Undefined;
        });
        Define("__wpt_actions_key", 4, (_, args) =>
        {
            // The keys are dispatched to the focused element (the driver retargets the events from the body to the focused element)
            var sequence = GetSequence(args[0]);
            var (type, value) = (adapter.ToString(args[2]), adapter.ToString(args[3]));
            sequence.Add((int)adapter.ToNumber(args[1]), () => WptTestDriver.KeyAction(sequence.Context, type, value));
            return adapter.Undefined;
        });
        Define("__wpt_actions_run", 3, (_, args) =>
        {
            var id = (int)adapter.ToNumber(args[0]);
            var sequence = sequences[id];
            sequences.Remove(id);
            sequence.Run(CreateCompletion(args[1], args[2]));
            return adapter.Undefined;
        });
        Define("__wpt_send_keys", 2, (_, args) =>
        {
            WptTestDriver.SendKeys(realm.ToInterface<Element>(args[0], "Element"), adapter.ToString(args[1]));
            return adapter.Undefined;
        });
        Define("__wpt_set_window_visibility", 2, (_, args) =>
        {
            // The window of the top-level browsing context is minimized or restored (see testdriver-vendor.js)
            if (realm.ToInterface<Window>(args[0], "Window").BrowsingContext is { } browsingContext)
            {
                browsingContext.IsVisible = adapter.ToBoolean(args[1]);
            }

            return adapter.Undefined;
        });
        Define("__wpt_set_permission", 3, (_, args) =>
        {
            var name = adapter.ToString(args[0]);
            var state = adapter.ToString(args[1]);
            var completion = CreateCompletion(args[2], args[2]);

            // As a WebDriver command, the permission is set after the tasks that are already queued (e.g. the task that
            // resolves a pending navigator.permissions.query()), without waiting for a timer
            QueueTask(() =>
            {
                _permissions[name] = state;
                _permissionStore.SetState(name, state switch
                {
                    "granted" => PermissionState.Granted,
                    "denied" => PermissionState.Denied,
                    _ => PermissionState.Prompt,
                });
                completion(null);
            });
            return adapter.Undefined;
        });
        Define("__wpt_generate_test_report", 1, (_, args) =>
        {
            realm.Window.GenerateAndQueueReport(new TestReportBody(adapter.ToString(args[0])), "test", "default");
            return adapter.Undefined;
        });
        Define("__wpt_delete_all_cookies", 0, (_, _) =>
        {
            _cookieJar.Clear();
            return adapter.Undefined;
        });
        Define("__wpt_get_cookies", 1, (_, args) => adapter.FromString(GetWebDriverCookies(realm.Window.Document, args.Length > 0 && !adapter.IsUndefined(args[0]) ? adapter.ToString(args[0]) : null)));
        Define("__wpt_computed_role", 1, (_, args) => adapter.FromString(realm.ToInterface<Element>(args[0], "Element").ComputedRole));
        Define("__wpt_computed_label", 1, (_, args) => adapter.FromString(realm.ToInterface<Element>(args[0], "Element").ComputedName));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scripts
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns a script of the test suite given its absolute URL, or null when it does not exist.</summary>
    private DocumentResource? LoadScript(string url, RequestDestination destination = RequestDestination.Script)
    {
        var parsed = new Url(url);
        var path = Uri.UnescapeDataString(parsed.Pathname);
        if (!WptServer.IsHost(parsed))
            return null;

        if (path == "/resources/testharnessreport.js")
            return new DocumentResource(ReportScript) { Headers = [new("Content-Type", "text/javascript")] };

        if (path == "/resources/testdriver-vendor.js")
            return new DocumentResource(TestDriverVendorScript.Value) { Headers = [new("Content-Type", "text/javascript")] };

        // resources/WebIDLParser.js is a symbolic link to the webidl2 parser in the WPT repository (the symbolic links are not materialized)
        if (path == "/resources/WebIDLParser.js")
        {
            path = "/resources/webidl2/lib/webidl2.js";
        }

        var file = Path.Combine(WptPaths.Wpt, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        // As wptrunner, which appends its implementation of the test driver to testdriver.js: the documents that only load
        // testdriver.js (e.g. the frames of a test, which use test_driver.set_test_context()) can use the test driver
        if (path == "/resources/testdriver.js" && File.Exists(file))
            return new DocumentResource(WptFiles.ReadAllText(file) + "\n" + TestDriverVendorScript.Value) { Headers = [new("Content-Type", "text/javascript")] };

        if (!File.Exists(file))
        {
            if (GetWorkerWrapper(path, parsed) is { } wrapper)
                return wrapper;

            Log("Script not found: " + path);
            return null;
        }

        if (path.EndsWith(".asis", StringComparison.Ordinal))
            return LoadAsIs(file);

        // The style sheets are bytes: the library determines their encoding (BOM, charset of the Content-Type, @charset, environment encoding).
        // The scripts of the workers, the JSON modules and the text modules are bytes: the library decodes them as UTF-8. The other scripts are
        // bytes unless they are ASCII: the library determines their encoding (BOM, charset of the Content-Type, charset
        // attribute, encoding of the document, UTF-8 for the modules).
        if (destination is RequestDestination.Style or RequestDestination.PaintWorklet or RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker or RequestDestination.Json or RequestDestination.Text || !WptFiles.IsAscii(file))
        {
            var bytes = File.ReadAllBytes(file);
            if (WptServer.HasSubstitutions(parsed))
            {
                bytes = WptServer.Substitute(bytes, parsed);
            }

            return CreateResource(new DocumentResource(bytes), file, parsed);
        }

        var content = WptFiles.ReadAllText(file);
        if (WptServer.HasSubstitutions(parsed))
        {
            content = WptServer.Substitute(content, parsed);
        }

        return CreateResource(new DocumentResource(content), file, parsed);
    }

    /// <summary>
    /// Returns the body and the headers (the Content-Type, and the headers of the .headers files and of the pipes of a file of
    /// the test suite) of a URL: a file of the test suite, a blob or a data: URL, or null.
    /// </summary>
    private (byte[] Body, List<KeyValuePair<string, string>> Headers, Url Url)? GetResponse(string url, IReadOnlyList<KeyValuePair<string, string>>? requestHeaders = null)
    {
        var parsed = new Url(url, _document?.Url ?? _test.Url);
        if (parsed.Protocol == "blob:")
            return _document is not null && Dom.FileApi.BlobUrlStore.Get(_document).Resolve(parsed.Record) is { } entry ? (entry.Object.Data.ToArray(), [new("Content-Type", entry.Object.Type)], parsed) : null;

        if (parsed.Protocol == "data:")
            return Urls.DataUrl.Process(parsed.Record) is var (mimeType, data) ? (data, [new("Content-Type", mimeType)], parsed) : null;

        if (!WptServer.IsHost(parsed))
            return null;

        var path = Uri.UnescapeDataString(parsed.Pathname);
        var file = Path.Combine(WptPaths.Wpt, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file))
            return null;

        var body = File.ReadAllBytes(file);
        return (body, [.. CreateResource(new DocumentResource(body), file, parsed, requestHeaders).Headers], parsed);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Event loop
    // ----------------------------------------------------------------------------------------------------------------

    private void RunEventLoop(DomEventLoop eventLoop)
    {
        // The waits for the threads of the workers spin, then yield, then sleep: a worker usually answers quickly
        var workerWait = default(SpinWait);
        while (_result is null && _reftestWait?.Update() is not true)
        {
            if (_stopwatch.Elapsed > MaxDuration)
                throw new TimeoutException("The test exceeded the maximum duration");

            _reftestWait?.CheckTimeout();

            if (_activatedDocuments > MaxActivatedDocuments)
                throw new TimeoutException("The test activated too many documents (navigation loop)");

            _engine.ResetConstraints();

            // Read before the next timer is computed: a worker that runs a task afterwards may add a timer or queue a task
            var workerActivity = eventLoop.WorkerActivity;
            var delay = eventLoop.PromoteDueTimers();
            if (eventLoop.RunTask())
            {
                workerWait.Reset();
                continue;
            }

            // The workers run on their own threads: the clock is not advanced while they have tasks to run. The next timer is
            // computed again once they are idle: a worker may have added a timer meanwhile.
            if (eventLoop.HasRunningWorkers)
            {
                workerWait.SpinOnce();
                continue;
            }

            if (eventLoop.HasWorkers)
            {
                delay = eventLoop.PromoteDueTimers();
                if (eventLoop.HasPendingWork)
                    continue;
            }

            // The handlers of the WebSocket server run on the thread pool: the clock is not advanced while a connection is busy
            // (a handler is about to respond, or the library is processing a message). The task queue is checked afterwards: a
            // connection that becomes idle has queued its tasks
            if (_webSocketServer.IsBusy)
            {
                workerWait.SpinOnce();
                continue;
            }

            if (eventLoop.HasPendingWork)
                continue;

            // The timers use a virtual clock: the clock is advanced to the next timer (the loader delays the responses with timers)
            if (delay is { } next)
            {
                // A due timer of a worker is promoted by the thread of the worker: the loop waits for it instead of spinning,
                // as each read of the clock advances it
                if (next <= TimeSpan.Zero && eventLoop.HasWorkers)
                {
                    workerWait.SpinOnce();
                    continue;
                }

                // A worker may have moved a timer to its task queue while the next timer was computed, or run a task since then
                // (e.g. a timer that became due when the clock was read): the next timer is computed again
                if (eventLoop.HasRunningWorkers || eventLoop.WorkerActivity != workerActivity)
                    continue;

                workerWait.Reset();
                _clock.Advance(next);
                continue;
            }

            // Asynchronous operations (e.g. document loads) queue a task when they complete
            if (eventLoop.HasPendingOperations)
            {
                Thread.Sleep(1);
                continue;
            }

            break;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Errors and results
    // ----------------------------------------------------------------------------------------------------------------

    private string GetErrorDescription(ScriptError error) => _engine.DescribeError(error.Error) ?? error.Message;

    private void OnConsoleMessage(GlobalScope window, ConsoleMessage message)
    {
        Log(new string(' ', message.GroupDepth * 2) + message.Text);
        if (_consoleListeners.TryGetValue(window, out var listener))
        {
            listener(message);
        }
    }

    private void OnReport(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var subtests = new List<WptSubtestResult>();
        foreach (var test in root.GetProperty("tests").EnumerateArray())
        {
            subtests.Add(new WptSubtestResult(
                test.GetProperty("name").GetString() ?? "",
                WptResult.GetSubtestStatus(test.GetProperty("status").GetInt32()),
                test.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null));
        }

        _result = new WptResult(
            WptResult.GetHarnessStatus(root.GetProperty("status").GetInt32()),
            root.TryGetProperty("message", out var harnessMessage) && harnessMessage.ValueKind == JsonValueKind.String ? harnessMessage.GetString() : null,
            subtests,
            LogSnapshot);
    }
}
