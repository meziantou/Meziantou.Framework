using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>Loads the documents of a test in browsing contexts with the Imaging backend, and disposes them at the end of the test.</summary>
internal sealed class TestPages : IDisposable
{
    private readonly List<BrowsingContext> _contexts = [];
    private readonly FakeTimeProvider _clock = new();

    /// <summary>The virtual clock of the event loops of the pages.</summary>
    public FakeTimeProvider Clock => _clock;

    /// <summary>The resources served to the documents by URL path (e.g. <c>/a.png</c>).</summary>
    public Dictionary<string, (byte[] Content, string ContentType)> Resources { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Loads a no-quirks document whose body has no margin and the 10px/1 Ahem font (the glyphs of Ahem are squares: the text is
    /// rendered the same way on every platform). The scripts run when a script engine is given.
    /// </summary>
    public BrowsingContext Load(string html, double width = 200, double height = 100, double devicePixelRatio = 1, RenderingBackendKind backend = RenderingBackendKind.Imaging, bool scriptingEnabled = false, Scripting.ScriptEngine? scriptEngine = null, Action<string>? consoleMessage = null)
    {
        var context = new BrowsingContext(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = width, Height = height, DevicePixelRatio = devicePixelRatio },
            DocumentLoader = new Loader(this),
            RenderingBackend = backend == RenderingBackendKind.Imaging ? new ImagingRenderingBackend() : null,
            ScriptingEnabled = scriptingEnabled || scriptEngine is not null,
            ScriptEngine = scriptEngine,
            ConsoleMessage = consoleMessage is null ? null : (_, message) => consoleMessage(message.Text),
        });

        _contexts.Add(context);
        context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        context.EventLoop.RunUntilIdle();
        return context;
    }

    /// <summary>Encodes a bitmap of <paramref name="width"/> x <paramref name="height"/> pixels whose colors are given row by row.</summary>
    public static byte[] CreatePng(int width, int height, params Rgba32[] colors)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            var color = colors[i];
            pixels[i * 4] = color.R;
            pixels[(i * 4) + 1] = color.G;
            pixels[(i * 4) + 2] = color.B;
            pixels[(i * 4) + 3] = color.A;
        }

        return Meziantou.Framework.WebPlatform.Rendering.PngEncoder.Encode(width, height, pixels);
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }
    }

    private sealed class Loader(TestPages pages) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var path = request.Url[request.Url.IndexOf('/', "https://".Length, StringComparison.Ordinal)..];
            if (pages.Resources.TryGetValue(path, out var resource))
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(resource.Content) { Headers = [new("Content-Type", resource.ContentType)] });

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] });
        }
    }
}

/// <summary>The rendering backend of the browsing contexts of <see cref="TestPages"/>.</summary>
internal enum RenderingBackendKind
{
    Imaging,
    None,
}
