using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Takes the screenshot of a page as the reftests of web-platform-tests do: a few boxes in the 800×600 viewport. The page
/// is laid out once (not measured): the rasterization of the 16-bit image and its conversion to 8-bit pixels are measured.
/// </summary>
[MemoryDiagnoser]
public class ScreenshotBenchmarks
{
    private BrowsingContext? _context;

    [GlobalSetup]
    public void Setup()
    {
        _context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new ImagingRenderingBackend() });
        _context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <style>div { width: 100px; height: 100px; margin: 10px; background: green; border: 2px solid navy; }</style>
            <p>Test passes if there is a filled green square and no red.</p>
            <div></div><div style="background: rgba(0, 0, 255, 0.5)"></div>
            """);
        using var warmup = _context.TakeScreenshot();
    }

    [GlobalCleanup]
    public void Cleanup() => _context?.Dispose();

    [Benchmark]
    public int TakeScreenshot()
    {
        using var image = _context!.TakeScreenshot();
        return image.Width;
    }
}
