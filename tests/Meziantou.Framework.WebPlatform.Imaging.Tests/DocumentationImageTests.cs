using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.WebPlatform.Dom;
using Xunit.Sdk;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>
/// Renders the images of the documentation site (docs/10-WebPlatform/*/images): each test runs an example of the
/// documentation, and compares its pixels with the committed PNG. With the environment variable
/// <see cref="UpdateVariable"/> set to 1, a missing or different image is written instead (see docs/AGENTS.md).
/// </summary>
public sealed class DocumentationImageTests
{
    private const string UpdateVariable = "MEZIANTOU_WEBPLATFORM_UPDATE_DOCS_IMAGES";
    private const string Rendering = "07-Rendering";
    private const string LoadingPages = "05-LoadingPages";

    private static readonly string DocumentationRoot = Path.Combine(TestPaths.Root, "docs", "10-WebPlatform");

    [Fact]
    public void PageScreenshot()
    {
        #region WebPlatformImaging_PageScreenshot
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            Viewport = new ViewportOptions { Width = 480, Height = 270 },
            RenderingBackend = new ImagingRenderingBackend(),
        });

        context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <style>
              body { margin: 0; height: 100vh; display: grid; place-items: center; background: #e2e8f0; font-family: sans-serif }
              .card { width: 320px; padding: 20px 24px; border-radius: 12px; background: white; box-shadow: 0 4px 16px rgb(0 0 0 / 0.2) }
              h1 { margin: 0 0 8px; font-size: 22px; color: #0f172a }
              p { margin: 0 0 16px; color: #475569; line-height: 1.4 }
              button { font: inherit; padding: 8px 16px; border: 0; border-radius: 6px; background: #2563eb; color: white }
            </style>
            <div class="card">
              <h1>Weekly report</h1>
              <p>12 new orders and 3 returns this week. The details are in the report.</p>
              <button>Open the report</button>
            </div>
            """);
        context.EventLoop.RunUntilIdle();

        using var screenshot = context.TakeScreenshot(); // the viewport: 480x270 pixels
        var png = context.TakeScreenshotAsPng();          // the same pixels, encoded as PNG
        #endregion

        Assert.Equal((480, 270), (screenshot.Width, screenshot.Height));
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.AsSpan(0, 4).ToArray());
        AssertImage(Rendering, "PageScreenshot", screenshot);
    }

    [Fact]
    public void ElementScreenshot()
    {
        #region WebPlatformImaging_ElementScreenshot
        using var context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new ImagingRenderingBackend() });
        var document = context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <style>
              body { font-family: sans-serif }
              table { border-collapse: collapse; font-size: 14px }
              th, td { padding: 6px 12px; border-bottom: 1px solid #cbd5e1; text-align: left }
              th { background: #1e293b; color: white }
              th:last-child, td:last-child { text-align: right }
            </style>
            <h1>Orders</h1>
            <table id="orders">
              <tr><th>Customer</th><th>Status</th><th>Total</th></tr>
              <tr><td>Ada</td><td>Shipped</td><td>$120.00</td></tr>
              <tr><td>Grace</td><td>Pending</td><td>$75.50</td></tr>
              <tr><td>Alan</td><td>Delivered</td><td>$310.25</td></tr>
            </table>
            """);
        context.EventLoop.RunUntilIdle();

        // The border box of the element, at twice the resolution of the page
        var table = document.GetElementById("orders")!;
        using var screenshot = table.TakeScreenshot(new ScreenshotOptions { DevicePixelRatio = 2 });
        #endregion

        var bounds = table.GetBoundingClientRect();
        Assert.Equal((int)Math.Ceiling(bounds.Width * 2), screenshot.Width);
        AssertImage(Rendering, "ElementScreenshot", screenshot);
    }

    [Fact]
    public void DarkColorScheme()
    {
        #region WebPlatformImaging_DarkColorScheme
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            Viewport = new ViewportOptions { Width = 360, Height = 140, PreferredColorScheme = PreferredColorScheme.Dark },
            RenderingBackend = new ImagingRenderingBackend(),
        });

        var document = context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <meta name="color-scheme" content="light dark">
            <style>
              body { font-family: sans-serif }
              .notice { padding: 12px 16px; border-radius: 8px; background: #fef3c7; color: #78350f }
              @media (prefers-color-scheme: dark) {
                .notice { background: #422006; color: #fde68a }
              }
            </style>
            <p>The page follows the color scheme of the viewport.</p>
            <div class="notice">Maintenance on Sunday at 2 AM.</div>
            """);
        context.EventLoop.RunUntilIdle();

        Assert.True(context.Window.MatchMedia("(prefers-color-scheme: dark)").Matches);
        using var screenshot = context.TakeScreenshot();
        #endregion

        AssertImage(LoadingPages, "DarkColorScheme", screenshot);
    }

    [Fact]
    public void PrintedPages()
    {
        #region WebPlatformImaging_PrintPages
        using var context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new ImagingRenderingBackend() });
        context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <style>
              @page {
                size: 320px 200px;
                margin: 24px 24px 36px;
                @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 11px sans-serif; color: #64748b }
              }
              body { margin: 0; font: 13px/1.4 sans-serif }
              h2 { margin: 0 0 8px; font-size: 18px; color: #1d4ed8 }
              .chapter { break-before: page }
            </style>
            <h2>Introduction</h2>
            <p>Each chapter starts on a new page, and the bottom margin of every page shows its number.</p>
            <div class="chapter">
              <h2>Results</h2>
              <p>The pages are laid out with the print media type.</p>
            </div>
            """);
        context.EventLoop.RunUntilIdle();

        // One image per page: 320x200 pixels each, as set by the @page rule
        var pages = context.RenderPages();
        #endregion

        try
        {
            Assert.Equal(2, pages.Count);
            AssertImage(Rendering, "PrintedPage1", pages[0]);
            AssertImage(Rendering, "PrintedPage2", pages[1]);
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    [Fact]
    public void CanvasChart()
    {
        #region WebPlatformImaging_CanvasChart
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            RenderingBackend = new ImagingRenderingBackend(),
            ScriptingEnabled = true, // as in a browser, a canvas shows its fallback content when scripting is disabled
        });
        var document = context.Navigate("https://example.com/", _ => """<canvas id="chart" width="320" height="160"></canvas>""");
        context.EventLoop.RunUntilIdle();

        // Without a rendering backend, GetContext("2d") returns null, as in a browser without canvas support
        var canvas = (HtmlCanvasElement)document.GetElementById("chart")!;
        var context2D = (CanvasRenderingContext2D)canvas.GetContext("2d")!;
        context2D.FillStyle = "#f8fafc";
        context2D.FillRect(0, 0, 320, 160);

        double[] values = [40, 90, 65, 120, 80];
        for (var i = 0; i < values.Length; i++)
        {
            context2D.FillStyle = i == 3 ? "#ea580c" : "#0ea5e9";
            context2D.FillRect(20 + (i * 60), 140 - values[i], 40, values[i]);
        }

        context2D.StrokeStyle = "#334155";
        context2D.LineWidth = 2;
        context2D.BeginPath();
        context2D.MoveTo(10, 141);
        context2D.LineTo(310, 141);
        context2D.Stroke();

        using var screenshot = canvas.TakeScreenshot();
        #endregion

        Assert.Equal((320, 160), (screenshot.Width, screenshot.Height));
        AssertImage(Rendering, "CanvasChart", screenshot);
    }

    /// <summary>
    /// Compares an image with the committed PNG: same size, and each component within 8 levels (the antialiasing may differ
    /// at the rounding of the floating-point operations between the processors).
    /// </summary>
    private static void AssertImage(string group, string name, Image<Rgba32> actual)
    {
        var expectedPath = Path.Combine(DocumentationRoot, group, "images", name + ".png");
        var difference = File.Exists(expectedPath) ? Compare(expectedPath, actual) : "the image does not exist";
        if (difference is null)
            return;

        var relativePath = Path.GetRelativePath(TestPaths.Root, expectedPath).Replace('\\', '/');
        if (Environment.GetEnvironmentVariable(UpdateVariable) is not "1")
            throw new XunitException($"{relativePath}: {difference}. Run the test with the environment variable {UpdateVariable}=1 to write the image, look at it, and commit it.");

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(expectedPath)!, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = File.Create(temporaryPath))
        {
            actual.Save(stream, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        }

        File.Move(temporaryPath, expectedPath, overwrite: true);
    }

    private static string? Compare(string expectedPath, Image<Rgba32> actual)
    {
        using var expected = Image.Load<Rgba32>(expectedPath);
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return $"the image is {actual.Width}x{actual.Height} instead of {expected.Width}x{expected.Height}";

        var expectedPixels = ScreenshotAssert.GetPixels(expected);
        var actualPixels = ScreenshotAssert.GetPixels(actual);
        var differentPixels = 0;
        for (var i = 0; i < expectedPixels.Length; i += 4)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                if (Math.Abs(expectedPixels[i + channel] - actualPixels[i + channel]) > 8)
                {
                    differentPixels++;
                    break;
                }
            }
        }

        return differentPixels is 0 ? null : $"{differentPixels} pixels differ";
    }
}
