using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Imaging;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The rendering of a page of a reftest: the screenshot of the viewport (owned by the caller), or the status and the message of the error.</summary>
internal sealed record WptPageRendering(WptScreenshot? Screenshot, string Status, string? Message, IReadOnlyList<string> Log)
{
    /// <summary>The images of the pages of a printed page (a print reftest), owned by the caller, or null.</summary>
    public IReadOnlyList<WptScreenshot>? Pages { get; init; }
}

/// <summary>
/// A screenshot of a page of a reftest: 4 bytes per pixel (red, green, blue, alpha; not premultiplied), row by row. The
/// pixels are in a pooled array (the screenshots of the tens of thousands of pages would otherwise be as many large objects
/// for the garbage collector), returned when the screenshot is disposed of.
/// </summary>
internal sealed class WptScreenshot : IDisposable
{
    private byte[]? _pixels;

    /// <summary>Creates a screenshot whose pixels are not initialized.</summary>
    public WptScreenshot(int width, int height)
        : this(width, height, System.Buffers.ArrayPool<byte>.Shared.Rent(width * height * 4))
    {
    }

    /// <summary>Creates a screenshot from an array rented from the shared pool.</summary>
    public WptScreenshot(int width, int height, byte[] rentedPixels)
    {
        Width = width;
        Height = height;
        _pixels = rentedPixels;
    }

    public int Width { get; }

    public int Height { get; }

    public Span<byte> Pixels => _pixels.AsSpan(0, Width * Height * 4);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _pixels, null) is { } pixels)
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(pixels);
        }
    }
}

internal sealed partial class WptTestRunner
{
    /// <summary>The rendering backend of the pages (the Imaging backend shared by all the tests).</summary>
    private readonly RenderingBackend _renderingBackend;

    /// <summary>The steps of wptrunner before the screenshot of a page of a reftest, or null for a testharness.js test.</summary>
    private readonly ReftestWait? _reftestWait;

    /// <summary>The screenshot of the page of a reftest.</summary>
    private WptScreenshot? _screenshot;

    /// <summary>Whether the page is printed (a page of a print reftest).</summary>
    private bool _print;

    /// <summary>The images of the pages of a printed page.</summary>
    private IReadOnlyList<WptScreenshot>? _pages;

    /// <summary>
    /// The size of the pages of the print reftests (https://web-platform-tests.org/writing-tests/print-reftests.html): 5in x 3in
    /// with margins of 0.5in, the default page of the <c>@page</c> rules, rendered at 96 dpi (wptrunner prints the page to PDF
    /// with these settings and renders the PDF pages with pdf.js at a scale of 96/72).
    /// </summary>
    internal const int PrintPageWidth = 480;

    /// <inheritdoc cref="PrintPageWidth"/>
    internal const int PrintPageHeight = 288;

    private static readonly PrintOptions PrintReftestOptions = new()
    {
        PageWidth = PrintPageWidth,
        PageHeight = PrintPageHeight,
        MarginTop = 48,
        MarginRight = 48,
        MarginBottom = 48,
        MarginLeft = 48,
    };

    /// <summary>
    /// Loads a page of a reftest (the test or a reference) as a test page, runs it until it is ready for the screenshot (see
    /// <see cref="ReftestWait"/>), and renders its viewport with the backend.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="backend">The rendering backend.</param>
    /// <param name="longTimeout">Whether the page has the long timeout.</param>
    /// <param name="print">Whether the page is printed (a page of a print reftest): the images are its pages.</param>
    public static WptPageRendering Render(WptTest page, RenderingBackend backend, bool longTimeout, bool print = false)
    {
        var runner = new WptTestRunner(page, backend, reftest: true) { _print = print };
        runner._reftestWait!.Timeout = longTimeout ? ReftestLongTimeout : ReftestTimeout;
        var result = runner.Run();
        return new WptPageRendering(runner._screenshot, result.HarnessStatus, result.HarnessMessage, result.Log) { Pages = runner._pages };
    }

    /// <summary>The timeout of a reftest in wptrunner (measured by the virtual clock), and the one of a test with <c>&lt;meta name=timeout content=long&gt;</c>.</summary>
    private static readonly TimeSpan ReftestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReftestLongTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The maximum depth of the calls of the scripts of the pages of the reftests.</summary>
    private const int ReftestRecursionLimit = 1000;

    private WptResult TakeReftestScreenshot(BrowsingContext browsingContext)
    {
        if (_reftestWait!.Pending is { } pending)
            return new WptResult("TIMEOUT", "The page is not ready for the screenshot: " + pending, [], LogSnapshot);

        if (_print)
            return PrintReftestPages(browsingContext);

        // The viewport at a device pixel ratio of 1 (800x600 pixels), with its scrollbars. An image of another size (a
        // fractional viewport) is drawn over the white window, so the images have the same size.
        var (pixels, screenshotWidth, screenshotHeight) = ImagingScreenshotExtensions.RenderViewport(browsingContext.Document, new ScreenshotOptions { DevicePixelRatio = 1 }, System.Buffers.ArrayPool<byte>.Shared);
        var screenshot = new WptScreenshot(screenshotWidth, screenshotHeight, pixels);
        var width = (int)Math.Ceiling(browsingContext.Options.Viewport?.Width ?? 800);
        var height = (int)Math.Ceiling(browsingContext.Options.Viewport?.Height ?? 600);
        if (screenshot.Width != width || screenshot.Height != height)
        {
            var window = new WptScreenshot(width, height);
            window.Pixels.Fill(0xFF);
            var rowBytes = Math.Min(screenshot.Width, width) * 4;
            for (var y = 0; y < Math.Min(screenshot.Height, height); y++)
            {
                screenshot.Pixels.Slice(y * screenshot.Width * 4, rowBytes).CopyTo(window.Pixels.Slice(y * width * 4, rowBytes));
            }

            screenshot.Dispose();
            screenshot = window;
        }

        _screenshot = screenshot;
        return new WptResult("OK", null, [], LogSnapshot);
    }

    /// <summary>
    /// Prints the page of a print reftest (as wptrunner prints it to PDF, https://web-platform-tests.org/writing-tests/print-reftests.html):
    /// the pages of 5in x 3in with the default margins of 0.5in. The resources that only the print styles use are fetched, and
    /// the tasks that are queued run (without advancing the clock) before the pages are rendered. The safe printable inset of
    /// the sheet is the one of <c>&lt;meta name=safe-printable-inset content="(centimeters)"&gt;</c>, as in the test runner of
    /// Chromium (css/css-page/tentative/safe-printable-inset-*).
    /// </summary>
    private WptResult PrintReftestPages(BrowsingContext browsingContext)
    {
        var document = browsingContext.Document;
        var options = PrintReftestOptions;
        if (document.QuerySelector("meta[name=safe-printable-inset]")?.GetAttribute("content") is { } inset
            && double.TryParse(inset, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var centimeters) && centimeters > 0)
        {
            options = options with { SafePrintableInset = centimeters * 96 / 2.54 };
        }

        using var session = document.BeginPrint(options);
        session.FetchResources();
        for (var i = 0; i < 1000 && _eventLoop.RunTask(); i++)
        {
        }

        var pages = ImagingPrintExtensions.RenderPages(document, session, new ScreenshotOptions { DevicePixelRatio = 1 }, System.Buffers.ArrayPool<byte>.Shared);
        _pages = [.. pages.Select(page => new WptScreenshot(page.Width, page.Height, page.Pixels))];
        return new WptResult("OK", null, [], LogSnapshot);
    }

    /// <summary>
    /// The steps of wptrunner before the screenshot of a reftest (https://web-platform-tests.org/writing-tests/reftests.html#controlling-when-comparison-occurs,
    /// <c>tools/wptrunner/wptrunner/executors/test-wait.js</c>): wait for the <c>load</c> event and the web fonts
    /// (<c>document.fonts.ready</c>), then for the pending paints (two animation frames). When the root element has the
    /// <c>reftest-wait</c> class (or <c>test-wait</c>), fire <c>TestRendered</c> at it, wait until the class is removed, and wait
    /// for the pending paints again.
    /// </summary>
    private sealed class ReftestWait(WptTestRunner runner)
    {
        private enum State
        {
            Load,
            Fonts,
            Paints,
            ClassRemoval,
            Ready,
        }

        private State _state;
        private bool _paintsDone;
        private bool _testRenderedFired;
        private DateTimeOffset? _start;

        public TimeSpan Timeout { get; set; } = ReftestTimeout;

        /// <summary>Gets what the page is waiting for, or null when it is ready for the screenshot.</summary>
        public string? Pending => _state switch
        {
            State.Load => "the load event was not fired",
            State.Fonts => "the web fonts are not loaded",
            State.Paints => "the animation frames did not run",
            State.ClassRemoval => "the reftest-wait class of the root element was not removed",
            _ => null,
        };

        /// <summary>Throws a <see cref="TimeoutException"/> when the page is not ready after the timeout of the reftest (virtual clock).</summary>
        public void CheckTimeout()
        {
            var now = runner._clock.GetUtcNow();
            _start ??= now;
            if (now - _start.Value > Timeout)
                throw new TimeoutException("The page is not ready for the screenshot after " + Timeout.TotalSeconds + " s: " + Pending);
        }

        /// <summary>Runs the next steps between two tasks of the event loop. Returns true when the page is ready for the screenshot.</summary>
        public bool Update()
        {
            var document = runner._browsingContext?.Document;
            if (document is null || document.IsInitialAboutBlank)
                return false;

            while (true)
            {
                switch (_state)
                {
                    case State.Load:
                        // The load event is fired in the task that sets the ready state to complete
                        if (document.ReadyState != DocumentReadyState.Complete)
                            return false;

                        _state = State.Fonts;
                        continue;

                    case State.Fonts:
                        if (document.Fonts.Status != FontFaceSetLoadStatus.Loaded)
                            return false;

                        WaitPaints(document);
                        continue;

                    case State.Paints:
                        if (!_paintsDone)
                            return false;

                        if (!HasWaitClass(document))
                        {
                            _state = State.Ready;
                            continue;
                        }

                        _state = State.ClassRemoval;
                        if (!_testRenderedFired && document.DocumentElement is { } root)
                        {
                            _testRenderedFired = true;
                            root.DispatchEvent(new Event("TestRendered", new EventInit { Bubbles = true }));
                            runner._eventLoop.PerformMicrotaskCheckpoint();
                        }

                        continue;

                    case State.ClassRemoval:
                        if (HasWaitClass(document))
                            return false;

                        WaitPaints(document);
                        continue;

                    default:
                        return true;
                }
            }
        }

        // requestAnimationFrame(() => requestAnimationFrame(...)): the pending paints are completed
        private void WaitPaints(Document document)
        {
            _state = State.Paints;
            _paintsDone = false;
            // A document whose rendering is suppressed by a view transition (its update callback is running) runs no animation
            // frame callback: the frame it displays, the one of the old state, is already painted
            if (document.DefaultView is not { } window || document.ViewTransitionRenderingSuppressed)
            {
                _paintsDone = true;
                return;
            }

            window.RequestAnimationFrame(_ => window.RequestAnimationFrame(_ => _paintsDone = true));
        }

        private static bool HasWaitClass(Document document) => document.DocumentElement is { } root && (root.ClassList.Contains("reftest-wait") || root.ClassList.Contains("test-wait"));
    }
}
