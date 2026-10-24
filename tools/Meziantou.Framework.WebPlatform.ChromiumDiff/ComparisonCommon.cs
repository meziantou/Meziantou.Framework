using System.Diagnostics;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Playwright;

namespace ChromiumDiff;

/// <summary>The helpers shared by the comparisons that render the same page in Chromium and in the library.</summary>
internal static class ComparisonCommon
{
    public const string FontFamily = "ChromiumDiff Sans";

    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>
    /// The style sheet injected in both after the load of the page: all the text uses the bundled Arimo of the library (as a
    /// web font, the same file in both), and the animations and the transitions are disabled.
    /// </summary>
    public static string CreateStyleSheet(string root)
    {
        var fonts = Path.Combine(root, "src", "Meziantou.Framework.WebPlatform", "Css", "Fonts", "Bundled");
        var regular = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(fonts, "Arimo-VF.woff2")));
        var italic = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(fonts, "Arimo-Italic-VF.woff2")));
        return $$"""
            @font-face { font-family: "{{FontFamily}}"; src: url(data:font/woff2;base64,{{regular}}) format("woff2"); font-weight: 400 700; font-style: normal; font-display: block }
            @font-face { font-family: "{{FontFamily}}"; src: url(data:font/woff2;base64,{{italic}}) format("woff2"); font-weight: 400 700; font-style: italic; font-display: block }
            *, *::before, *::after, *::marker, *::placeholder, *::file-selector-button { font-family: "{{FontFamily}}" !important }
            *, *::before, *::after { animation: none !important; transition: none !important }
            """;
    }

    // The style element of an SVG document is an SVG element: an element without namespace (createElement in an XML
    // document) is not a style sheet
    private const string InjectScript = """
        async css => {
          const root = document.documentElement;
          const style = root && root.namespaceURI === 'http://www.w3.org/2000/svg'
            ? document.createElementNS('http://www.w3.org/2000/svg', 'style')
            : document.createElement('style');
          style.textContent = css;
          (document.head || root).append(style);
          root.getBoundingClientRect();
          await document.fonts.ready;
        }
        """;

    /// <summary>Injects the style sheet in a page of Chromium and waits for the resources and the fonts that it uses.</summary>
    public static async Task PrepareChromiumAsync(IPage page, string styleSheet)
    {
        await page.EvaluateAsync(InjectScript, styleSheet);

        // The resources that the new styles use (e.g. a background image of an element that was not rendered)
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
        }

        await page.EvaluateAsync("async () => { document.documentElement.getBoundingClientRect(); await document.fonts.ready; }");
    }

    /// <summary>Injects the style sheet in a document of the library (an SVG style element in an SVG document).</summary>
    public static void InjectStyleSheet(Document document, string styleSheet)
    {
        var root = document.DocumentElement;
        var style = root?.NamespaceUri == SvgNamespace ? document.CreateElementNS(SvgNamespace, "style") : document.CreateElement("style");
        style.TextContent = styleSheet;
        (document.Head ?? root)?.AppendChild(style);
    }

    /// <summary>
    /// Injects the style sheet in the active document of a browsing context and runs its event loop until the fonts and
    /// the images that the new styles use are loaded.
    /// </summary>
    public static async Task PrepareLibraryAsync(BrowsingContext context, string styleSheet, Stopwatch stopwatch, TimeSpan timeout)
    {
        var document = context.Document;
        InjectStyleSheet(document, styleSheet);

        // The fonts and the images that the new styles use are fetched when the document is laid out
        for (var i = 0; i < 5; i++)
        {
            document.DocumentElement?.GetBoundingClientRect();
            await RunUntilIdleAsync(context.EventLoop, stopwatch, timeout);
            if (document.Fonts.Status == FontFaceSetLoadStatus.Loaded)
                break;
        }
    }

    /// <summary>
    /// Launches headless Chromium. Playwright hides the scrollbars of headless Chromium (except the ones styled with
    /// ::-webkit-scrollbar): with <paramref name="classicScrollbars"/>, the classic scrollbars of 15px are shown, as the ones
    /// of the library.
    /// </summary>
    public static Task<IBrowser> LaunchChromiumAsync(IPlaywright playwright, bool classicScrollbars)
    {
        var options = new BrowserTypeLaunchOptions { Headless = true };
        if (classicScrollbars)
            options.IgnoreDefaultArgs = ["--hide-scrollbars"];

        return playwright.Chromium.LaunchAsync(options);
    }

    /// <summary>Runs the tasks of the event loop until it is idle, or throws a <see cref="TimeoutException"/> after the timeout of the page.</summary>
    public static async Task RunUntilIdleAsync(DomEventLoop eventLoop, Stopwatch stopwatch, TimeSpan timeout)
    {
        while (true)
        {
            while (eventLoop.RunTask())
            {
                if (stopwatch.Elapsed > timeout)
                    throw new TimeoutException($"The page is not laid out after {timeout.TotalSeconds} s");
            }

            if (!eventLoop.HasPendingOperations)
                return;

            if (stopwatch.Elapsed > timeout)
                throw new TimeoutException($"The page is not laid out after {timeout.TotalSeconds} s");

            await Task.Delay(10);
        }
    }

    public static string ReadResource(string name)
    {
        using var stream = typeof(ComparisonCommon).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Resource '{name}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
