// Takes a screenshot of a page with the managed rendering backend (Meziantou.Framework.WebPlatform.Imaging).
//   dotnet run --project samples/Meziantou.Framework.WebPlatform.ScreenshotSample -- [url] [output.png]
// Without a URL, a page of the sample is rendered. The scripts do not run (no script engine is configured).
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;

var url = args.Length > 0 && args[0].Length > 0 ? args[0] : null;
var output = Path.GetFullPath(args.Length > 1 ? args[1] : "screenshot.png");

using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 800, Height = 600, DevicePixelRatio = 2 },
    DocumentLoader = DocumentLoader.Create(), // the style sheets, the fonts and the images of the page
    RenderingBackend = new ImagingRenderingBackend(),
});

if (url is null)
{
    await context.NavigateAsync("https://example.com/", _ => """
        <!DOCTYPE html>
        <style>
          body { font-family: sans-serif; margin: 40px; background: #f5f5f5 }
          h1 { color: #333 }
          .card { display: inline-block; width: 200px; padding: 16px; margin: 8px; border-radius: 12px; background: white; border: 2px solid #48c; vertical-align: top }
          .card:nth-child(2) { transform: rotate(-3deg); opacity: 0.8 }
        </style>
        <h1>Meziantou.Framework.WebPlatform</h1>
        <div class=card>Layout and painting by the library</div><div class=card>Rasterization in <b>managed code</b></div>
        """);
}
else
{
    context.Navigate(url);
}

// Wait for the page and its resources
await context.EventLoop.RunUntilIdleAsync();

await File.WriteAllBytesAsync(output, context.TakeScreenshotAsPng());
Console.WriteLine($"Screenshot of {context.Document.Url} saved to {output}");
