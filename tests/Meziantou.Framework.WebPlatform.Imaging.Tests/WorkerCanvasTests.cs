using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>The text of the <c>OffscreenCanvas</c> objects of the workers uses the font faces of the font source of the worker (<c>self.fonts</c>, #503).</summary>
public sealed class WorkerCanvasTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly TestPages _pages = new();
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _pages.Dispose();
        _engine.Dispose();
    }

    /// <summary>Runs a worker and returns the message it posts (the worker runs on its own thread).</summary>
    private async Task<string> RunWorkerAsync(string script)
    {
        _pages.Resources["/worker.js"] = (System.Text.Encoding.UTF8.GetBytes(script), "text/javascript");
        _pages.Resources["/font.ttf"] = (File.ReadAllBytes(Path.Combine(TestPaths.Root, "src", "Meziantou.Framework.WebPlatform", "Css", "Fonts", "Bundled", "Ahem.ttf")), "font/ttf");
        var context = _pages.Load("<script>new Worker('/worker.js').onmessage = e => document.title = e.data;</script>", scriptEngine: _engine);
        var document = context.Document;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (document.Title.Length == 0)
        {
            Assert.True(stopwatch.Elapsed < Timeout, "The worker did not post its message");
            using var cancellation = new CancellationTokenSource(Timeout);
            await context.EventLoop.RunUntilIdleAsync(cancellation.Token);
            if (document.Title.Length == 0)
            {
                await Task.Delay(1);
            }
        }

        return document.Title;
    }

    [Fact]
    public async Task Text_UsesTheLoadedFontFacesOfTheWorker()
    {
        // The glyphs of the font are 1em squares with a 0.8em ascent: the text is measured and drawn with the font face once it
        // is loaded and in the set, and with the next family of the list before
        var message = await RunWorkerAsync("""
            (async () => {
              const results = [];
              const canvas = new OffscreenCanvas(100, 50);
              const context = canvas.getContext('2d');
              const pixel = (x, y) => Array.from(context.getImageData(x, y, 1, 1).data).join(' ');
              context.font = '20px WorkerFont, sans-serif';
              results.push(context.font, context.measureText('iiii').width < 40);

              const face = new FontFace('WorkerFont', 'url(/font.ttf)');
              await face.load();
              context.font = '20px WorkerFont, sans-serif';
              results.push(context.measureText('iiii').width < 40);

              self.fonts.add(face);
              await self.fonts.ready;
              context.font = '20px WorkerFont, sans-serif';
              results.push(context.measureText('iiii').width);
              context.fillStyle = '#00f';
              context.fillText('XX', 10, 30);
              results.push(pixel(11, 15), pixel(49, 33), pixel(51, 15), pixel(11, 35));

              self.fonts.delete(face);
              context.font = '20px WorkerFont, sans-serif';
              results.push(context.measureText('iiii').width < 40);

              // A font face of the set that is not loaded starts to load when a text needs it
              const lazy = new FontFace('LazyFont', 'url(/font.ttf)');
              self.fonts.add(lazy);
              context.font = '20px LazyFont, sans-serif';
              results.push(lazy.status, context.measureText('iiii').width < 40, lazy.status);
              await lazy.loaded;
              results.push(context.measureText('iiii').width);
              postMessage(results.join());
            })().catch(e => postMessage('error: ' + e));
            """);

        Assert.Equal("20px WorkerFont, sans-serif,true,true,80,0 0 255 255,0 0 255 255,0 0 0 0,0 0 0 0,true,unloaded,true,loading,80", message);
    }
}
