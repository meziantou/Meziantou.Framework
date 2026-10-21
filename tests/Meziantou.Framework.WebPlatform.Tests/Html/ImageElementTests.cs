using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The images of the img elements (https://html.spec.whatwg.org/multipage/images.html).</summary>
public sealed class ImageElementTests : IDisposable
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

    private Document Load(string html, ViewportOptions? viewport = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = new ImageLoader(this),
            Viewport = viewport,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>Serves a PNG for the URLs ending with .png and a text file for the other ones.</summary>
    private sealed class ImageLoader(ImageElementTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request.Destination + " " + request.Url);
            var resource = request.Url.Contains(".png", StringComparison.Ordinal) ? new DocumentResource(Png) { Headers = [new("Content-Type", "image/png")] } : new DocumentResource("not an image") { Headers = [new("Content-Type", "text/plain")] };
            return ValueTask.FromResult<DocumentResource?>(resource);
        }
    }

    private static HtmlImageElement Image(Document document) => (HtmlImageElement)document.QuerySelector("img")!;

    [Fact]
    public void Src_IsFetchedAndLoaded()
    {
        var document = Load("<img src=a.png onload=\"document.title = 'load ' + this.complete\">");
        var image = Image(document);
        Assert.Equal("load true", document.Title);
        Assert.Equal("https://example.com/a.png", image.CurrentSrc);
        Assert.Equal((2u, 3u), (image.NaturalWidth, image.NaturalHeight));
        Assert.True(image.Complete);
        Assert.Equal(["Image https://example.com/a.png"], _requests);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>", 300u, 150u)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='60' viewBox='0 0 600 200'></svg>", 60u, 20u)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' height='1in'></svg>", 300u, 96u)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 600 200'></svg>", 300u, 100u, 784u, 261u)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='50%' height='-5'></svg>", 300u, 150u)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='0' viewBox='0 0 600 200'></svg>", 0u, 0u)]
    public void Svg_NaturalSizeUsesTheDefaultSizingAlgorithm(string svg, uint width, uint height, uint? renderedWidth = null, uint? renderedHeight = null)
    {
        var document = Load($"<img src=\"data:image/svg+xml,{svg}\">");
        var image = Image(document);
        Assert.True(image.Complete);
        Assert.Equal((width, height), (image.NaturalWidth, image.NaturalHeight));

        // The width and height are the rendered size: an image with only an aspect ratio fills its containing block (the body,
        // 784px wide in a 800px wide viewport)
        Assert.Equal((renderedWidth ?? width, renderedHeight ?? height), (image.Width, image.Height));
    }

    [Fact]
    public void WidthAndHeight_AreTheRenderedSize()
    {
        // The rendered size of the content box (the width attribute maps to the width property, the natural aspect ratio gives
        // the height); a broken image without alt attribute renders a 16x16 icon
        var document = Load("<img id=a src=a.png><img id=b src=a.png width=10><img id=c src=a.txt height=4><img id=d src=a.png width=10 hidden>");
        Assert.Equal((2u, 3u), Size("a"));
        Assert.Equal((10u, 15u), Size("b"));
        Assert.Equal((16u, 4u), Size("c"));

        // Not rendered: the natural size replaced by the attributes
        Assert.Equal((10u, 3u), Size("d"));

        (uint, uint) Size(string id)
        {
            var image = (HtmlImageElement)document.GetElementById(id)!;
            return (image.Width, image.Height);
        }
    }

    [Fact]
    public void NotAnImage_FiresError()
    {
        var document = Load("<img src=a.txt onerror=\"document.title = 'error ' + this.complete\">");
        Assert.Equal("error true", document.Title);
        Assert.Equal("https://example.com/a.txt", Image(document).CurrentSrc);
        Assert.Equal(0u, Image(document).NaturalWidth);
    }

    [Fact]
    public void EmptySrc_FiresError()
    {
        var document = Load("<img src='' onerror=\"document.title = 'error'\"><img id=nosrc>");
        Assert.Equal("error", document.Title);
        Assert.Equal("", Image(document).CurrentSrc);
        Assert.True(((HtmlImageElement)document.GetElementById("nosrc")!).Complete);
        Assert.Empty(_requests);
    }

    [Fact]
    public void Srcset_SelectsTheDensity()
    {
        var document = Load("<img srcset='a.png 1x, b.png 2x'>", new ViewportOptions { DevicePixelRatio = 2 });
        var image = Image(document);
        Assert.Equal("https://example.com/b.png", image.CurrentSrc);

        // The natural size is divided by the density
        Assert.Equal(1u, image.NaturalWidth);
    }

    [Theory]
    [InlineData("a.png .5x", "https://example.com/a.png")]
    [InlineData("a.png .5e1x", "https://example.com/a.png")]
    [InlineData("a.png -0x", "https://example.com/a.png")]
    [InlineData("a.png 5.x", "")]
    [InlineData("a.png -.x", "")]
    [InlineData("a.png 1.e1x", "")]
    [InlineData("a.png +1x", "")]
    public void Srcset_DensityIsAValidFloatingPointNumber(string srcset, string expected)
    {
        var document = Load($"<img srcset='{srcset}'>");
        Assert.Equal(expected, Image(document).CurrentSrc);
    }

    [Fact]
    public void Srcset_WidthDescriptorsUseTheSizes()
    {
        var document = Load("<img srcset='small.png 100w, large.png 400w' sizes='(max-width: 500px) 100px, 400px'>", new ViewportOptions { Width = 400 });
        Assert.Equal("https://example.com/small.png", Image(document).CurrentSrc);

        document = Load("<img srcset='small.png 100w, large.png 400w' sizes='(max-width: 500px) 100px, 400px'>", new ViewportOptions { Width = 800 });
        Assert.Equal("https://example.com/large.png", Image(document).CurrentSrc);
    }

    [Theory]
    [InlineData("(max-width: 500px) 100px, 400px", "small")]
    [InlineData("not print 100px, 400px", "large")]
    [InlineData("print 100px, 400px", "large")]
    [InlineData("not (min-width: 1000px) 100px, 400px", "small")]
    [InlineData("calc(1px / 0), 100px", "small")]
    [InlineData("calc(-1px / 0), 100px", "small")]
    [InlineData("calc(-1px), 400px", "small")]
    public void Sizes_SourceSizeHasAMediaCondition(string sizes, string expected)
    {
        var document = Load($"<img srcset='small.png 100w, large.png 400w' sizes='{sizes}'>", new ViewportOptions { Width = 400 });
        Assert.Equal($"https://example.com/{expected}.png", Image(document).CurrentSrc);
    }

    [Theory]
    [InlineData("", "small")]
    [InlineData("sizes='400px'", "large")]
    public void Picture_SourceWithoutSizesUsesTheAutoSizesOfTheImage(string sourceSizes, string expected)
    {
        var document = Load($"<picture><source srcset='small.png 100w, large.png 400w' {sourceSizes}><img loading=lazy sizes=auto width=100></picture>", new ViewportOptions { Width = 400 });
        Assert.Equal($"https://example.com/{expected}.png", Image(document).CurrentSrc);
    }

    [Fact]
    public void Picture_SelectsTheFirstMatchingSource()
    {
        var document = Load("""
            <picture>
              <source srcset="print.png" media="print">
              <source srcset="webp.png" type="image/x-unknown">
              <source srcset="screen.png" media="screen">
              <img src="fallback.png">
            </picture>
            """);
        Assert.Equal("https://example.com/screen.png", Image(document).CurrentSrc);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("image/png")]
    public void Picture_SourceWithAnEmptyTypeIsSelected(string type)
    {
        var document = Load($"<picture><source srcset=source.png type='{type}'><img src=fallback.png></picture>");
        Assert.Equal("https://example.com/source.png", Image(document).CurrentSrc);
    }

    [Fact]
    public void InsertingAPicture_DoesNotUpdateItsImages()
    {
        var document = Load("<div></div><picture><source srcset=source.png><img src=fallback.png></picture>");
        var picture = document.QuerySelector("picture")!;
        document.QuerySelector("div")!.AppendChild(picture);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Image https://example.com/source.png"], _requests);

        // Inserting an img or a source element into a picture element updates the image data
        var image = Image(document);
        picture.AppendChild(image);
        picture.InsertBefore(document.CreateElement("source"), image);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Image https://example.com/source.png", "Image https://example.com/source.png"], _requests);
    }

    [Theory]
    [InlineData("<img srcset=''>")]
    [InlineData("<picture><img></picture>")]
    public void NoSource_DoesNotFireErrorWithoutSrc(string html)
    {
        var document = Load(html.Replace("<img", "<img onerror=\"document.title = 'error'\"", StringComparison.Ordinal));
        Assert.Equal("", document.Title);
        Assert.Equal("", Image(document).CurrentSrc);
    }

    [Fact]
    public void ChangingSrc_UpdatesTheImageData()
    {
        var document = Load("<img src=a.png>");
        var image = Image(document);
        image.Src = "b.png";
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/b.png", image.CurrentSrc);
    }

    [Fact]
    public void DataUrl_IsLoaded()
    {
        var document = Load("<img onload=\"document.title = this.naturalWidth\" src='data:image/svg+xml,<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\"/>'>");
        Assert.Equal("20", document.Title);
    }

    [Fact]
    public void Script_ImageConstructorAndDecode()
    {
        var document = Load("""
            <script>
              const image = new Image(5, 6);
              const results = [image.width, image.height, image.localName];
              image.src = 'a.png';
              image.decode().then(() => {
                results.push('decoded', image.naturalWidth);
                return new Image().decode();
              }).catch(e => {
                results.push(e.name);
                document.title = results.join();
              });
            </script>
            """);
        Assert.Equal("5,6,img,decoded,2,EncodingError", document.Title);
    }

    [Fact]
    public void Decode_IsRejectedWhenTheCurrentRequestChanges()
    {
        var document = Load("""
            <script>
              const image = new Image();
              image.src = 'a.png';
              const first = image.decode();
              image.src = 'b.png';
              const second = image.decode();
              Promise.allSettled([first, second]).then(results => document.title = results.map(r => r.status === 'rejected' ? r.reason.name : r.status).join());
            </script>
            """);
        Assert.Equal("EncodingError,fulfilled", document.Title);
    }

    [Fact]
    public void Decode_IsRejectedInADocumentThatIsNotFullyActive()
    {
        var document = Load("""
            <script>
              const image = document.createElement('img');
              image.src = 'a.png';
              const promise = image.decode();
              document.implementation.createHTMLDocument().body.appendChild(image);
              promise.catch(e => document.title = e.name);
            </script>
            """);
        Assert.Equal("EncodingError", document.Title);
    }

    [Fact]
    public void Complete_IsFalseUntilTheImageDataIsUpdated()
    {
        var document = Load("<img src=a.png>");
        var image = Image(document);
        Assert.True(image.Complete);
        image.Src = "b.png";
        Assert.False(image.Complete);
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(image.Complete);
    }

    [Fact]
    public void AvailableImage_IsUsedSynchronously()
    {
        var document = Load("<img src=a.png><img id=second>");
        var image = (HtmlImageElement)document.GetElementById("second")!;
        image.Src = "a.png";

        // The image of the list of available images is not fetched again, and its dimensions are available at once
        Assert.True(image.Complete);
        Assert.Equal(2u, image.NaturalWidth);
        Assert.Equal("", image.CurrentSrc);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/a.png", image.CurrentSrc);
        Assert.Equal(["Image https://example.com/a.png"], _requests);
    }

    [Fact]
    public void RemovingSrc_ClearsTheImageAtOnce()
    {
        var document = Load("<img src=a.png onerror=\"document.title = 'error'\">");
        var image = Image(document);
        Assert.Equal(2u, image.Width);
        image.RemoveAttribute("src");
        Assert.Equal((0u, ""), (image.Width, image.CurrentSrc));
        Assert.True(image.Complete);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("", document.Title);
    }

    [Fact]
    public void Decode_PictureWithoutSource()
    {
        var document = Load("""
            <script>
              var picture = document.createElement("picture");
              var source = document.createElement("source");
              var img = document.createElement("img");
              picture.appendChild(source);
              picture.appendChild(img);
              img.decode().then(() => document.title = 'resolved', e => document.title = e.name);
            </script>
            """);
        Assert.Equal("EncodingError", document.Title);
    }

    [Fact]
    public void Images_DelayTheLoadEvent()
    {
        var document = Load("<body onload=\"document.title = document.querySelector('img').complete\"><img src=a.png>");
        Assert.Equal("true", document.Title);
    }

    [Fact]
    public void DocumentWithoutBrowsingContext_DoesNotFetch()
    {
        var document = new HtmlDocumentParser().Parse("<img src=a.png>");
        Assert.Equal("", Image(document).CurrentSrc);
        Assert.False(Image(document).Complete);
    }
}
