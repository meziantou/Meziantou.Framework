using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>The <c>paint()</c> images of the CSS Painting API (#510): the paint callbacks of the paint worklet draw the images.</summary>
public sealed class PaintWorkletTests : IDisposable
{
    private readonly TestPages _pages = new();
    private readonly JintScriptEngine _engine = new();
    private readonly List<string> _messages = [];

    public void Dispose()
    {
        _pages.Dispose();
        _engine.Dispose();
    }

    private BrowsingContext Load(string html, string worklet, double width = 200, double height = 100, double devicePixelRatio = 1, RenderingBackendKind backend = RenderingBackendKind.Imaging)
    {
        _pages.Resources["/paint.js"] = (System.Text.Encoding.UTF8.GetBytes(worklet), "text/javascript");
        var context = _pages.Load(html + "<script>CSS.paintWorklet.addModule('/paint.js').then(() => document.documentElement.dataset.ready = '')</script>", width, height, devicePixelRatio, backend, scriptEngine: _engine, consoleMessage: _messages.Add);
        context.EventLoop.RunUntilIdle();
        Assert.True(context.Document!.DocumentElement!.HasAttribute("data-ready"));
        return context;
    }

    [Fact]
    public void PaintImages()
    {
        // Backgrounds (tiled, with arguments and input properties), a border image, a mask, an opaque context, a zoomed
        // element, and the invalid images (a paint that is not registered, arguments that do not match, a callback that throws)
        var context = Load("""
            <style>
              div { float: left; box-sizing: border-box; width: 40px; height: 30px; margin: 5px; background-color: silver }
              #geometry { background-image: paint(geometry) }
              #tiled { background: paint(geometry) 0 0 / 20px 15px }
              #arguments { background-image: paint(box, blue, 10px) }
              #properties { --color: purple; border-top: 6px solid orange; background-image: paint(properties) }
              #border { border: 10px solid; border-image: paint(geometry) 10; background: none }
              #mask { background: navy; mask-image: paint(geometry) }
              #opaque { background-image: paint(opaque) }
              #zoom { zoom: 0.5; width: 80px; height: 60px; margin: 10px; background-image: paint(geometry) }
              #unknown { background-image: paint(unknown) }
              #mismatch { background-image: paint(box, 10px) }
              #throws { background-image: paint(throws) }
            </style>
            <div id=geometry></div><div id=tiled></div><div id=arguments></div><div id=properties></div>
            <div id=border></div><div id=mask></div><div id=opaque></div><div id=zoom></div>
            <div id=unknown></div><div id=mismatch></div><div id=throws></div>
            """, """
            registerPaint('geometry', class {
              paint(ctx, size) {
                ctx.fillStyle = 'green';
                ctx.fillRect(0, 0, size.width, size.height);
                ctx.fillStyle = 'yellow';
                ctx.beginPath();
                ctx.arc(size.width / 2, size.height / 2, Math.min(size.width, size.height) / 4, 0, 2 * Math.PI);
                ctx.fill();
              }
            });
            registerPaint('box', class {
              static get inputArguments() { return ['<color>', '<length>']; }
              paint(ctx, size, properties, args) {
                ctx.fillStyle = args[0].toString();
                ctx.fillRect(args[1].value, args[1].value, size.width - 2 * args[1].value, size.height - 2 * args[1].value);
              }
            });
            registerPaint('properties', class {
              static get inputProperties() { return ['--color', 'border-top-width']; }
              paint(ctx, size, properties) {
                ctx.strokeStyle = properties.get('--color').toString();
                ctx.lineWidth = properties.get('border-top-width').value;
                ctx.strokeRect(5, 5, size.width - 10, size.height - 10);
              }
            });
            registerPaint('opaque', class {
              static get contextOptions() { return { alpha: false }; }
              paint(ctx, size) {
                ctx.fillStyle = 'rgb(255 0 0 / 0.5)';
                ctx.fillRect(0, 0, size.width / 2, size.height);
              }
            });
            registerPaint('throws', class { paint() { throw new Error('paint failed'); } });
            """, width: 200, height: 120);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(new Rgba32(0, 128, 0), screenshot.GetPixel(7, 7));
        ScreenshotAssert.Matches(screenshot, "paint-worklet");
    }

    [Fact]
    public void PaintCallback_IsInvokedWhenAnInputChanges()
    {
        var context = Load("""
            <div id=target style="width: 50px; height: 50px; --color: red; color: black; background-image: paint(fill)"></div>
            """, """
            registerPaint('fill', class {
              static get inputProperties() { return ['--color']; }
              paint(ctx, size, properties) {
                console.log('paint ' + properties.get('--color') + ' ' + size.width + 'x' + size.height + ' ' + devicePixelRatio);
                ctx.fillStyle = properties.get('--color').toString();
                ctx.fillRect(0, 0, size.width, size.height);
              }
            });
            """, devicePixelRatio: 2);
        var target = (HtmlElement)context.Document!.GetElementById("target")!;

        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Red, screenshot.GetPixel(50, 50));
            Assert.Equal(Colors.White, screenshot.GetPixel(110, 50));
        }

        // The size of the callback is in CSS pixels, and the image is cached: a property that is not an input does not invalidate it
        Assert.Equal(["paint red 50x50 2"], _messages);
        target.Style.SetProperty("color", "blue");
        using (context.TakeScreenshot())
        {
        }

        Assert.Single(_messages);

        // An input property, then the size
        target.Style.SetProperty("--color", "lime");
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Lime, screenshot.GetPixel(50, 50));
        }

        target.Style.SetProperty("width", "80px");
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Lime, screenshot.GetPixel(150, 50));
        }

        Assert.Equal(["paint red 50x50 2", "paint lime 50x50 2", "paint lime 80x50 2"], _messages);

        // A previous image is reused
        target.Style.SetProperty("width", "50px");
        using (context.TakeScreenshot())
        {
        }

        Assert.Equal(3, _messages.Count);
    }

    [Fact]
    public void Paint_RegisteredAfterTheFirstPainting_IsDrawn()
    {
        _pages.Resources["/paint.js"] = ("registerPaint('late', class { paint(ctx, size) { ctx.fillStyle = 'blue'; ctx.fillRect(0, 0, size.width, size.height); } });"u8.ToArray(), "text/javascript");
        var context = _pages.Load("<div style='width: 50px; height: 50px; background: red paint(late)'></div>", scriptEngine: _engine);
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Red, screenshot.GetPixel(25, 25));
        }

        var task = context.Document!.PaintWorklet.AddModule("/paint.js");
        context.EventLoop.RunUntilIdle();
        Assert.True(task.IsCompletedSuccessfully);
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Blue, screenshot.GetPixel(25, 25));
        }
    }

    [Fact]
    public void Paint_WithDifferentDefinitionsInTheGlobalScopes_IsInvalid()
    {
        // The class is registered with other input arguments in each global scope: the paint is invalid for the document
        var context = Load("<div style='width: 50px; height: 50px; background: lime paint(random)'></div>", """
            registerPaint('random', class {
              static get inputArguments() { return ['id' + Math.random().toString().slice(2)]; }
              paint(ctx, size) { ctx.fillStyle = 'red'; ctx.fillRect(0, 0, size.width, size.height); }
            });
            """);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Lime, screenshot.GetPixel(25, 25));
        Assert.Contains(_messages, message => message.Contains("random", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutBackend_ThePaintCallbacksAreNotInvoked()
    {
        var context = Load("<div style='width: 50px; height: 50px; background-image: paint(fill)'></div>", "registerPaint('fill', class { paint() { console.log('paint'); } });", backend: RenderingBackendKind.None);
        Assert.NotNull(context.Document!.GetDisplayList());
        Assert.Empty(_messages);
    }
}
