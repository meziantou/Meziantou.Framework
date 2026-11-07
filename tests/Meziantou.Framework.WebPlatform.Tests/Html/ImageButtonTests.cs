using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The image of the input elements in the Image Button state (https://html.spec.whatwg.org/multipage/input.html#image-button-state-(type=image)).</summary>
public sealed class ImageButtonTests : IDisposable
{
    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly List<string> _requests = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = new ImageLoader(this),
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 } input { font: 10px/10px monospace }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>Serves a PNG for the URLs ending with .png and a text file for the other ones.</summary>
    private sealed class ImageLoader(ImageButtonTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request.Destination + " " + request.Url);
            var resource = request.Url.Contains(".png", StringComparison.Ordinal) ? new DocumentResource(Png) { Headers = [new("Content-Type", "image/png")] } : new DocumentResource("not an image") { Headers = [new("Content-Type", "text/plain")] };
            return ValueTask.FromResult<DocumentResource?>(resource);
        }
    }

    private static HtmlInputElement Input(Document document) => (HtmlInputElement)document.QuerySelector("input")!;

    [Fact]
    public void Src_IsFetchedAndRenderedAsTheImage()
    {
        var document = Load("<input type=image src=a.png alt=Go onload=\"document.title += 'load,'\"><script>onload = () => document.title += 'document'</script>");
        var input = Input(document);
        Assert.Equal("load,document", document.Title);
        Assert.Equal(["Image https://example.com/a.png"], _requests);
        Assert.Equal((2u, 3u), (input.Width, input.Height));
        var rect = input.GetBoundingClientRect();
        Assert.Equal((2d, 3d), (rect.Width, rect.Height));
    }

    [Fact]
    public void Src_ThatIsNotAnImage_FiresErrorAndRendersTheAlternativeText()
    {
        var document = Load("<input type=image src=a.txt alt='Go now' onerror=\"document.title = 'error'\">");
        var input = Input(document);
        Assert.Equal("error", document.Title);

        // The alternative text is the content of a non-replaced inline-block box, as in the browsers: two words that wrap in a
        // narrow container
        Assert.Contains("\"Go now\"", document.GetLayoutEngine().Dump(), StringComparison.Ordinal);
        var width = input.GetBoundingClientRect().Width;
        Assert.True(width > 20);
        Assert.Equal((uint)Math.Round(width, MidpointRounding.AwayFromZero), input.Width);
        document.Body!.SetAttribute("style", "width: 1px");
        Assert.True(input.GetBoundingClientRect().Height > 10);
    }

    [Fact]
    public void Src_ChangeFetchesTheNewImage()
    {
        var document = Load("<input type=image alt=Go>");
        var input = Input(document);
        Assert.Empty(_requests);

        input.SetAttribute("src", "b.png");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Image https://example.com/b.png"], _requests);
        Assert.Equal(2, input.GetBoundingClientRect().Width);

        // Another state, then the Image Button state again with the same source: the image is not fetched again
        input.Type = "submit";
        input.Type = "image";
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(_requests);
        Assert.Equal(2, input.GetBoundingClientRect().Width);
    }
}
