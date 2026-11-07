using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.PdfRendering;
using Microsoft.Playwright;

namespace ChromiumDiff;

internal enum RenderKind
{
    Screenshot,
    ElementScreenshot,
    Pdf,
}

/// <summary>A margin of a PDF in CSS pixels (the margins of <c>page.pdf()</c> are converted to pixels, 96 per inch).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct PdfMargins(double Top, double Right, double Bottom, double Left)
{
    public static PdfMargins Zero => default;

    public static PdfMargins All(double value) => new(value, value, value, value);
}

/// <summary>
/// A way to export a page with Playwright: the options of the browser context (the viewport, a device of
/// <c>playwright.Devices</c>), of <c>page.screenshot()</c> or of <c>page.pdf()</c>. The options use the names and the
/// defaults of Playwright; <see cref="RenderScenarios.Map"/> converts them to the options of the library.
/// </summary>
internal sealed record RenderScenario(string Name, RenderKind Kind)
{
    // The browser context
    public string? Device { get; init; }
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public double DeviceScaleFactor { get; init; } = 1;
    public bool PrintMedia { get; init; }

    // page.screenshot()
    public bool OmitBackground { get; init; }
    public bool FullPage { get; init; }
    public (double X, double Y, double Width, double Height)? Clip { get; init; }
    public bool Jpeg { get; init; }
    public int? Quality { get; init; }
    public string ElementSelector { get; init; } = "#target";

    // page.pdf()
    public double PaperWidth { get; init; } = 816;
    public double PaperHeight { get; init; } = 1056;
    public PdfMargins Margins { get; init; }
    public bool Landscape { get; init; }
    public double Scale { get; init; } = 1;
    public bool PrintBackground { get; init; }
    public string? PageRanges { get; init; }
    public bool PreferCssPageSize { get; init; }
    public bool DisplayHeaderFooter { get; init; }
    public string? HeaderTemplate { get; init; }
    public string? FooterTemplate { get; init; }

    /// <summary>Gets the fixtures of the corpus that the scenario renders (by file name).</summary>
    public required Func<string, bool> Fixtures { get; init; }

    /// <summary>Gets the thresholds of the comparison (the JPEG compression loses details).</summary>
    public ComparisonThresholds Thresholds { get; init; } = ComparisonThresholds.Default;

    /// <summary>Gets a description of the Playwright options of the scenario, for the report.</summary>
    public required string Description { get; init; }
}

/// <summary>The device of a scenario, resolved from <c>playwright.Devices</c> (or the viewport of the scenario).</summary>
internal sealed record ResolvedDevice(int Width, int Height, double DeviceScaleFactor, bool IsMobile, bool HasTouch, string? UserAgent, int? ScreenWidth, int? ScreenHeight)
{
    public static ResolvedDevice Resolve(IPlaywright playwright, RenderScenario scenario)
    {
        if (scenario.Device is null)
            return new ResolvedDevice(scenario.Width, scenario.Height, scenario.DeviceScaleFactor, IsMobile: false, HasTouch: false, UserAgent: null, ScreenWidth: null, ScreenHeight: null);

        var device = playwright.Devices[scenario.Device];
        return new ResolvedDevice(
            device.ViewportSize!.Width,
            device.ViewportSize.Height,
            device.DeviceScaleFactor ?? 1,
            device.IsMobile ?? false,
            device.HasTouch ?? false,
            device.UserAgent,
            device.ScreenSize?.Width,
            device.ScreenSize?.Height);
    }

    public BrowserNewContextOptions CreateContextOptions() => new()
    {
        // The scripts do not run in both (the library runs them only with a script engine)
        JavaScriptEnabled = false,
        ViewportSize = new ViewportSize { Width = Width, Height = Height },
        ScreenSize = ScreenWidth is { } screenWidth && ScreenHeight is { } screenHeight ? new ScreenSize { Width = screenWidth, Height = screenHeight } : null,
        DeviceScaleFactor = (float)DeviceScaleFactor,
        IsMobile = IsMobile,
        HasTouch = HasTouch,
        UserAgent = UserAgent,
        ColorScheme = ColorScheme.Light,
        ReducedMotion = ReducedMotion.NoPreference,
        Locale = "en-US",
        TimezoneId = "UTC",
        ServiceWorkers = ServiceWorkerPolicy.Block,
    };
}

/// <summary>The options of the library that export a page as a scenario does in Chromium.</summary>
internal sealed class LibraryMapping
{
    public required ViewportOptions Viewport { get; init; }
    public ScreenshotOptions Screenshot { get; init; } = new();
    public PrintOptions Print { get; init; } = PrintOptions.Default;
    public PdfPrintOptions Pdf { get; init; } = PdfPrintOptions.Default;
    public string? UserAgent { get; init; }

    /// <summary>Gets the Playwright options that the library does not support (the scenario renders without them).</summary>
    public List<string> Unsupported { get; } = [];
}

internal static class RenderScenarios
{
    private static bool IsPrint(string fixture) => fixture.StartsWith("print-", StringComparison.Ordinal);

    private static bool IsMetaViewport(string fixture) => fixture.StartsWith("meta-viewport-", StringComparison.Ordinal);

    private static bool IsScreen(string fixture) => !IsPrint(fixture) && !IsMetaViewport(fixture) && fixture != "element-clip.html" && fixture != "long-page.html";

    private static bool IsPdfFixture(string fixture) => IsPrint(fixture) || fixture is "boxes.html" or "text.html" or "transparent-body.html" or "svg-transparent.svg" or "images.html";

    private const string HeaderTemplate = """<div style="font-size: 10px; width: 100%; text-align: center; font-family: Arial"><span class="title"></span> - <span class="url"></span></div>""";
    private const string FooterTemplate = """<div style="font-size: 10px; width: 100%; text-align: center; font-family: Arial"><span class="pageNumber"></span> / <span class="totalPages"></span></div>""";

    public static IReadOnlyList<RenderScenario> All { get; } =
    [
        // Screenshots of the viewport
        new("desktop", RenderKind.Screenshot) { Description = "viewport 1280x720", Fixtures = f => IsScreen(f) || IsMetaViewport(f) || f is "long-page.html" or "print-basic.html" },
        new("desktop-dpr2", RenderKind.Screenshot) { Device = "Desktop Chrome HiDPI", Description = "Desktop Chrome HiDPI (deviceScaleFactor 2)", Fixtures = IsScreen },
        new("omit-background", RenderKind.Screenshot) { OmitBackground = true, Description = "omitBackground: true", Fixtures = f => IsScreen(f) || f == "long-page.html" },
        new("full-page", RenderKind.Screenshot) { FullPage = true, Description = "fullPage: true", Fixtures = f => f is "long-page.html" or "text.html" },
        new("clip", RenderKind.Screenshot) { Clip = (10, 20, 300, 200), Description = "clip: 10,20 300x200", Fixtures = f => f is "boxes.html" or "gradients.html" or "svg-transparent.svg" },
        new("clip-full-page", RenderKind.Screenshot) { FullPage = true, Clip = (0, 600, 400, 400), Description = "fullPage: true, clip: 0,600 400x400 (beyond the viewport)", Fixtures = f => f == "long-page.html" },
        new("jpeg-q80", RenderKind.Screenshot) { Jpeg = true, Quality = 80, Description = "type: jpeg, quality: 80", Fixtures = f => f is "boxes.html" or "gradients.html" or "svg-transparent.svg", Thresholds = ComparisonThresholds.Lossy },
        new("print-media", RenderKind.Screenshot) { PrintMedia = true, Description = "emulateMedia({ media: 'print' })", Fixtures = f => f is "print-basic.html" or "media-features.html" },
        new("element", RenderKind.ElementScreenshot) { Description = "locator('#target').screenshot()", Fixtures = f => f == "element-clip.html" },

        // Devices of Playwright
        new("iphone-13", RenderKind.Screenshot) { Device = "iPhone 13", Description = "iPhone 13 (390x664, deviceScaleFactor 3, isMobile, hasTouch)", Fixtures = f => IsMetaViewport(f) || f is "media-features.html" or "text.html" or "svg-transparent.svg" },
        new("iphone-13-landscape", RenderKind.Screenshot) { Device = "iPhone 13 landscape", Description = "iPhone 13 landscape", Fixtures = f => f is "meta-viewport-device-width.html" or "media-features.html" },
        new("pixel-7", RenderKind.Screenshot) { Device = "Pixel 7", Description = "Pixel 7 (412x839, deviceScaleFactor 2.625, isMobile, hasTouch)", Fixtures = f => IsMetaViewport(f) || f is "media-features.html" or "boxes.html" },
        new("ipad-gen-7", RenderKind.Screenshot) { Device = "iPad (gen 7)", Description = "iPad (gen 7) (810x1080, deviceScaleFactor 2, isMobile, hasTouch)", Fixtures = f => f is "meta-viewport-device-width.html" or "meta-viewport-none.html" or "media-features.html" },

        // PDF (page.pdf): the defaults of Playwright, then one option at a time (with the backgrounds and the CSS page size)
        new("pdf-default", RenderKind.Pdf) { Description = "page.pdf() (Letter, no margins, no backgrounds, CSS page size ignored)", Fixtures = IsPdfFixture },
        new("pdf-background", RenderKind.Pdf) { PrintBackground = true, PreferCssPageSize = true, Description = "printBackground: true, preferCSSPageSize: true", Fixtures = IsPdfFixture },
        new("pdf-margins", RenderKind.Pdf) { Margins = PdfMargins.All(96), PrintBackground = true, PreferCssPageSize = true, Description = "margin: 1in", Fixtures = f => f is "print-basic.html" or "print-long.html" or "boxes.html" },
        // A paper size whose size in points is not an integer is rounded by Chromium (A4 is 595.9pt wide instead of 595.3pt)
        new("pdf-legal", RenderKind.Pdf) { PaperWidth = 816, PaperHeight = 1344, Margins = PdfMargins.All(48), PrintBackground = true, PreferCssPageSize = true, Description = "format: Legal, margin: 0.5in", Fixtures = f => f is "print-basic.html" or "text.html" },
        new("pdf-landscape", RenderKind.Pdf) { Landscape = true, Margins = PdfMargins.All(48), PrintBackground = true, PreferCssPageSize = true, Description = "landscape: true, margin: 0.5in", Fixtures = f => f is "print-basic.html" or "print-long.html" or "print-page-rules.html" },
        new("pdf-scale-0.5", RenderKind.Pdf) { Scale = 0.5, Margins = PdfMargins.All(48), PrintBackground = true, PreferCssPageSize = true, Description = "scale: 0.5, margin: 0.5in", Fixtures = f => f is "print-basic.html" or "print-long.html" or "print-page-rules.html" },
        new("pdf-scale-1.5", RenderKind.Pdf) { Scale = 1.5, Margins = PdfMargins.All(48), PrintBackground = true, PreferCssPageSize = true, Description = "scale: 1.5, margin: 0.5in", Fixtures = f => f is "print-basic.html" or "print-long.html" },
        new("pdf-ignore-css-size", RenderKind.Pdf) { PrintBackground = true, Description = "printBackground: true, preferCSSPageSize: false", Fixtures = f => f is "print-page-rules.html" or "print-css-size.html" },
        new("pdf-ranges", RenderKind.Pdf) { PageRanges = "2-3, 5", PrintBackground = true, PreferCssPageSize = true, Description = "pageRanges: '2-3, 5'", Fixtures = f => f == "print-long.html" },
        new("pdf-header-footer", RenderKind.Pdf)
        {
            DisplayHeaderFooter = true,
            HeaderTemplate = HeaderTemplate,
            FooterTemplate = FooterTemplate,
            Margins = PdfMargins.All(72),
            PrintBackground = true,
            PreferCssPageSize = true,
            Description = "displayHeaderFooter: true, header (title, url), footer (pageNumber / totalPages), margin: 0.75in",
            Fixtures = f => f is "print-long.html" or "print-basic.html",
        },
    ];

    /// <summary>
    /// Converts the options of a scenario to the options of the library. A Playwright option that the library does not
    /// support is added to <see cref="LibraryMapping.Unsupported"/>, and the library renders without it (or with the closest
    /// approximation).
    /// </summary>
    public static LibraryMapping Map(RenderScenario scenario, ResolvedDevice device)
    {
        var viewport = new ViewportOptions
        {
            Width = device.Width,
            Height = device.Height,
            DevicePixelRatio = device.DeviceScaleFactor,
            MediaType = scenario.PrintMedia ? "print" : "screen",
            ScreenWidth = device.ScreenWidth,
            ScreenHeight = device.ScreenHeight,
            HasTouch = device.HasTouch,
            IsMobile = device.IsMobile,

            // Playwright hides the scrollbars of headless Chromium: they take no space, as the overlay scrollbars
            ScrollbarThickness = 0,
            CaretVisible = false,
        };

        var mapping = new LibraryMapping
        {
            Viewport = viewport,
            UserAgent = device.UserAgent,
            Screenshot = new ScreenshotOptions
            {
                OmitBackground = scenario.OmitBackground,
                FullPage = scenario.FullPage,
                Clip = scenario.Clip is { } clip ? new ScreenshotClip(clip.X, clip.Y, clip.Width, clip.Height) : null,
                Quality = scenario.Quality,
            },
            Print = scenario.Kind is RenderKind.Pdf ? MapPrint(scenario) : PrintOptions.Default,
            Pdf = new PdfPrintOptions { Tagged = false },
        };

        return mapping;
    }

    private static PrintOptions MapPrint(RenderScenario scenario)
    {
        return new PrintOptions
        {
            PageWidth = scenario.PaperWidth,
            PageHeight = scenario.PaperHeight,
            Landscape = scenario.Landscape,
            Scale = scenario.Scale,
            PrintBackground = scenario.PrintBackground,
            PreferCssPageSize = scenario.PreferCssPageSize,
            PageRanges = scenario.PageRanges,
            DisplayHeaderFooter = scenario.DisplayHeaderFooter,
            HeaderTemplate = scenario.HeaderTemplate,
            FooterTemplate = scenario.FooterTemplate,
            MarginTop = scenario.Margins.Top,
            MarginRight = scenario.Margins.Right,
            MarginBottom = scenario.Margins.Bottom,
            MarginLeft = scenario.Margins.Left,
        };
    }

    /// <summary>The options of <c>page.screenshot()</c> of a scenario.</summary>
    public static PageScreenshotOptions CreateScreenshotOptions(RenderScenario scenario) => new()
    {
        FullPage = scenario.FullPage,
        OmitBackground = scenario.OmitBackground,
        Clip = scenario.Clip is { } clip ? new Clip { X = (float)clip.X, Y = (float)clip.Y, Width = (float)clip.Width, Height = (float)clip.Height } : null,
        Type = scenario.Jpeg ? ScreenshotType.Jpeg : ScreenshotType.Png,
        Quality = scenario.Quality,
        Animations = ScreenshotAnimations.Disabled,
        Caret = ScreenshotCaret.Hide,
        Scale = ScreenshotScale.Device,
    };

    /// <summary>The options of <c>page.pdf()</c> of a scenario.</summary>
    public static PagePdfOptions CreatePdfOptions(RenderScenario scenario) => new()
    {
        Width = FormatInches(scenario.PaperWidth),
        Height = FormatInches(scenario.PaperHeight),
        Margin = new Margin
        {
            Top = FormatInches(scenario.Margins.Top),
            Right = FormatInches(scenario.Margins.Right),
            Bottom = FormatInches(scenario.Margins.Bottom),
            Left = FormatInches(scenario.Margins.Left),
        },
        Landscape = scenario.Landscape,
        Scale = (float)scenario.Scale,
        PrintBackground = scenario.PrintBackground,
        PageRanges = scenario.PageRanges,
        PreferCSSPageSize = scenario.PreferCssPageSize,
        DisplayHeaderFooter = scenario.DisplayHeaderFooter,
        HeaderTemplate = scenario.HeaderTemplate,
        FooterTemplate = scenario.FooterTemplate,
        Tagged = false,
        Outline = false,
    };

    private static string FormatInches(double pixels) => (pixels / 96).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "in";
}
