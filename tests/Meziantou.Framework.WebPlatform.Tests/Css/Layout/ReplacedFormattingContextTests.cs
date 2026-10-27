using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The sizing of the replaced elements (natural sizes, aspect ratios, min/max, object-fit) and of the boxes with an aspect-ratio.</summary>
public sealed class ReplacedFormattingContextTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html, bool quirks = false) => Load((quirks ? "" : "<!DOCTYPE html>") + "<style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
            DocumentLoader = new PngLoader(),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Serves a PNG of WxH pixels for the URLs ending with <c>/WxH.png</c> (only the header is read), an empty SVG document for the URLs ending with <c>.svg</c>, and a 404 for the other ones.</summary>
    private sealed class PngLoader : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var name = request.Url[(request.Url.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
            if (name.EndsWith(".png", StringComparison.Ordinal) && name[..^4].Split('x') is [var w, var h])
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(CreatePng(uint.Parse(w, CultureInfo.InvariantCulture), uint.Parse(h, CultureInfo.InvariantCulture))) { Headers = [new("Content-Type", "image/png")] });

            if (name.EndsWith(".svg", StringComparison.Ordinal))
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg'/>") { Headers = [new("Content-Type", "image/svg+xml")] });

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] });
        }

        private static byte[] CreatePng(uint width, uint height)
        {
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
            return png;
        }
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    private static BoxFragment GetFragment(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!)[0];

    private static (double Width, double Height) GetSize(Document document, string id)
    {
        var rect = GetRect(document, id);
        return (rect.Width, rect.Height);
    }

    [Theory]
    [InlineData("", 200, 100)]
    [InlineData("width: 100px", 100, 50)]
    [InlineData("height: 50px", 100, 50)]
    [InlineData("width: 30px; height: 40px", 30, 40)]
    [InlineData("max-width: 50px", 50, 25)]
    [InlineData("min-width: 400px", 400, 200)]
    [InlineData("max-height: 20px", 40, 20)]
    [InlineData("min-height: 300px", 600, 300)]
    [InlineData("max-width: 50px; min-height: 40px", 50, 40)]
    [InlineData("min-width: 400px; max-height: 120px", 400, 120)]
    [InlineData("max-width: 100px; max-height: 20px", 40, 20)]
    [InlineData("min-width: 300px; min-height: 200px", 400, 200)]
    [InlineData("height: 50px; max-height: 20px", 40, 20)]
    [InlineData("width: 50%", 400, 200)]
    [InlineData("height: 50%", 200, 100)]
    [InlineData("padding: 5px; border: 1px solid; width: 100px", 112, 62)]
    [InlineData("box-sizing: border-box; padding: 5px; width: 110px", 110, 60)]
    public void Image_NaturalSizeAndConstraints(string style, double width, double height)
    {
        // CSS 2.1 §10.3.2, §10.6.2 and the constraint table of §10.4 (https://drafts.csswg.org/css2/#min-max-widths)
        var document = Parse($"<img id=i src=200x100.png style='{style}'>");
        Assert.Equal((width, height), GetSize(document, "i"));
    }

    [Theory]
    [InlineData("aspect-ratio: 1", 200, 200)]
    [InlineData("aspect-ratio: 1; width: 50px", 50, 50)]
    [InlineData("aspect-ratio: auto 1; width: 50px", 50, 25)]
    [InlineData("aspect-ratio: 1 / 2; width: 50px", 50, 100)]
    [InlineData("aspect-ratio: 0 / 1; width: 50px", 50, 25)]
    public void Image_AspectRatioProperty(string style, double width, double height)
    {
        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: a <ratio> overrides the natural ratio, auto && <ratio> does not,
        // a degenerate ratio behaves as auto
        var document = Parse($"<img id=i src=200x100.png style='{style}'>");
        Assert.Equal((width, height), GetSize(document, "i"));
    }

    [Fact]
    public void SvgImage_NaturalHeightOfZeroIsTransferredThroughTheViewBoxRatio()
    {
        // WPT css/css-grid/alignment/grid-item-aspect-ratio-stretch-4.html
        var document = Parse("""<img id=i src="data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' width='20px' height='0px' viewBox='0 0 50 100'></svg>"><img id=j src="data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' width='0px' height='20px' viewBox='0 0 50 100'></svg>">""");
        Assert.Equal((20, 40), GetSize(document, "i"));
        Assert.Equal((0, 20), GetSize(document, "j"));
    }

    [Fact]
    public void Image_DimensionAttributes()
    {
        // The attributes map to width, height and aspect-ratio: auto w / h, so the ratio is known before the image loads
        var document = Parse("<img id=a src=200x100.png width=50><img id=b src=missing.png width=100 height=40 style='height: auto'>");
        Assert.Equal((50, 25), GetSize(document, "a"));
        Assert.Equal((100, 40), GetSize(document, "b"));
    }

    [Fact]
    public void Image_Inline_SitsOnTheBaseline()
    {
        // A replaced element has no baseline: the block-end edge of its margin box is on the baseline (ascent 8px)
        var document = Parse("<div id=d>X<img id=i src=20x20.png>X</div>");
        Assert.Equal(new PhysicalRect(10, 0, 20, 20), GetRect(document, "i"));
        Assert.Equal(22, GetRect(document, "d").Height);
    }

    [Fact]
    public void Image_Block_AutoMarginsCenter()
    {
        var document = Parse("<img id=i src=200x100.png style='display: block; margin: 0 auto'>");
        Assert.Equal(new PhysicalRect(300, 0, 200, 100), GetRect(document, "i"));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 100'></svg>", 800, 200)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'></svg>", 40, 20)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='40'></svg>", 40, 150)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' height='20' viewBox='0 0 2 1'></svg>", 40, 20)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>", 300, 150)]
    public void SvgImage_NaturalSize(string svg, double width, double height)
    {
        // An SVG image with only a natural aspect ratio fills its containing block (https://drafts.csswg.org/css2/#inline-replaced-width)
        var document = Parse($"<img id=i src=\"data:image/svg+xml,{svg}\">");
        Assert.Equal((width, height), GetSize(document, "i"));
        var image = (HtmlImageElement)document.GetElementById("i")!;
        Assert.Equal(((uint)width, (uint)height), (image.Width, image.Height));
    }

    [Fact]
    public void Image_WidthAndHeightAreTheRenderedSize()
    {
        var document = Parse("<img id=a src=200x100.png style='width: 50px; padding: 3px'><img id=b src=200x100.png width=10 style='width: auto; display: none'>");
        var a = (HtmlImageElement)document.GetElementById("a")!;
        Assert.Equal((50u, 25u), (a.Width, a.Height));

        // Not rendered: the attributes, else the natural size
        var b = (HtmlImageElement)document.GetElementById("b")!;
        Assert.Equal((10u, 100u), (b.Width, b.Height));
    }

    [Fact]
    public void Image_XAndY()
    {
        var document = Parse("<div style='height: 30px'></div><div style='padding-left: 15px'><img id=i src=20x20.png style='margin-left: 5px'></div>");
        var image = (HtmlImageElement)document.GetElementById("i")!;
        Assert.Equal((20, 30), (image.X, image.Y));
    }

    [Fact]
    public void Image_LoadInvalidatesTheLayout()
    {
        var document = Parse("<img id=i>");
        Assert.Equal((0, 0), GetSize(document, "i"));
        var image = (HtmlImageElement)document.GetElementById("i")!;
        image.SetAttribute("src", "30x10.png");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal((30, 10), GetSize(document, "i"));
    }

    [Fact]
    public void BrokenImage_AltText_IsRenderedAsText()
    {
        // https://html.spec.whatwg.org/multipage/rendering.html#images-3: an image that represents some text is a non-replaced
        // inline whose content is the text; an image that represents nothing has no natural size
        var document = Parse("<div><img id=a src=missing.png alt='abc'></div><div><img id=b src=missing.png alt='' style='width: 50px'></div><div><img id=c src=missing.png style='width: 32px'></div>");
        var a = GetFragment(document, "a");
        Assert.Equal(ContainerBoxKind.Normal, a.Box.Kind);
        Assert.Equal((30, 10), (a.Size.Width, a.Size.Height));
        var image = (HtmlImageElement)document.GetElementById("a")!;
        Assert.Equal(30u, image.Width);
        Assert.Equal((50, 0), GetSize(document, "b"));

        // No alt attribute: the broken image icon (16x16, without aspect ratio)
        Assert.Equal((32, 16), GetSize(document, "c"));
    }

    [Fact]
    public void BrokenImage_SrcChangeRebuildsTheBoxTree()
    {
        var document = Parse("<img id=i src=missing.png alt='abc'>");
        Assert.Equal(ContainerBoxKind.Normal, GetFragment(document, "i").Box.Kind);
        document.GetElementById("i")!.SetAttribute("src", "20x10.png");
        _contexts.EventLoop.RunUntilIdle();
        var fragment = GetFragment(document, "i");
        Assert.Equal(ContainerBoxKind.Replaced, fragment.Box.Kind);
        Assert.Equal((20, 10), (fragment.Size.Width, fragment.Size.Height));
    }

    [Theory]
    [InlineData("<iframe id=e></iframe>", 304, 154)]
    [InlineData("<iframe id=e style='border: 0; width: 100px'></iframe>", 100, 150)]
    [InlineData("<embed id=e type=text/plain>", 300, 150)]
    [InlineData("<video id=e></video>", 300, 150)]
    [InlineData("<video id=e width=100 height=20></video>", 100, 20)]
    [InlineData("<svg id=e></svg>", 300, 150)]
    [InlineData("<svg id=e width=40></svg>", 40, 150)]
    [InlineData("<svg id=e viewBox='0 0 4 1'></svg>", 800, 200)]
    [InlineData("<svg id=e viewBox='0 0 4 1' style='width: 40px'></svg>", 40, 10)]
    public void EmbeddedContent_DefaultSizes(string html, double width, double height)
    {
        // The default object size is 300x150 (https://drafts.csswg.org/css-images-3/#default-object-size); the iframes have a
        // 2px border in the user agent style sheet
        var document = Parse(html);
        Assert.Equal((width, height), GetSize(document, "e"));
    }

    [Fact]
    public void Object_SvgDocument_NaturalSizeFollowsTheAttributesOfItsRoot()
    {
        // https://svgwg.org/svg2-draft/coords.html#SizingSVGInCSS: the natural size of the SVG document of an object element
        var document = Parse("<object id=e data='a.svg'></object>");
        Assert.Equal((300, 150), GetSize(document, "e"));
        var root = ((HtmlObjectElement)document.GetElementById("e")!).GetSvgDocument()!.DocumentElement!;
        root.SetAttribute("width", "100");
        Assert.Equal((100, 150), GetSize(document, "e"));
        root.SetAttribute("viewBox", "0 0 10 5");
        Assert.Equal((100, 50), GetSize(document, "e"));
        root.RemoveAttribute("width");
        Assert.Equal((800, 400), GetSize(document, "e"));
    }

    [Theory]
    [InlineData("<canvas id=e width=40 height=20></canvas>", 40, 20)]
    [InlineData("<canvas id=e width=40 height=20 style='width: 80px'></canvas>", 80, 40)]
    [InlineData("<canvas id=e></canvas>", 300, 150)]
    public void Canvas_NaturalSize(string html, double width, double height)
    {
        // The canvas is a replaced element when scripting is enabled
        using var engine = TestScriptEngines.CreateDisposable();
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, ScriptEngine = engine, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal((width, height), GetSize(document, "e"));
    }

    [Fact]
    public void Video_PosterNaturalSize()
    {
        var document = Parse("<video id=e poster=60x30.png></video>");
        Assert.Equal((60, 30), GetSize(document, "e"));
    }

    [Fact]
    public void GeneratedImage_NaturalSize()
    {
        // https://drafts.csswg.org/css-content-3/#content-property: an element whose content is an image is replaced by it
        var document = Parse("<style>#a { content: url(20x10.png) } #b::before { content: url(8x30.png) }</style><div id=a>text</div><div id=b></div>");

        // The images are fetched by the rendering steps
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal((20, 10), GetSize(document, "a"));
        // The image of the ::before sits on the baseline (descent 2px)
        Assert.Equal(32, GetFragment(document, "b").Size.Height);
    }

    [Theory]
    [InlineData("fill", "", 0, 0, 100, 100)]
    [InlineData("contain", "", 0, 25, 100, 50)]
    [InlineData("cover", "", -50, 0, 200, 100)]
    [InlineData("none", "", -50, 0, 200, 100)]
    [InlineData("scale-down", "", 0, 25, 100, 50)]
    [InlineData("contain", "left top", 0, 0, 100, 50)]
    [InlineData("contain", "right 10px bottom 0px", -10, 50, 100, 50)]
    [InlineData("none", "10px 20%", 10, 0, 200, 100)]
    public void ObjectFit_ContentRect(string fit, string position, double x, double y, double width, double height)
    {
        // https://drafts.csswg.org/css-images-3/#the-object-fit: the rectangle of the painted image in the content box
        var positionStyle = position.Length > 0 ? $"; object-position: {position}" : "";
        var document = Parse($"<img id=i src=200x100.png style='width: 100px; height: 100px; padding: 5px; object-fit: {fit}{positionStyle}'>");
        var rect = GetFragment(document, "i").ReplacedContentRect;
        Assert.Equal(new PhysicalRect(5 + x, 5 + y, width, height), rect);
    }

    [Theory]
    [InlineData("inset(50px 0px 0px 0px)", "fill", 100, 100, 0, -100, 100, 200)]
    [InlineData("inset(50px 0px 0px 0px)", "contain", 100, 200, 0, 62.5, 100, 50)]
    [InlineData("xywh(100px 0px 100px 100px)", "none", 100, 100, -100, 0, 200, 100)]
    [InlineData("rect(0px 50% 100% 0px)", "fill", 50, 100, 0, 0, 100, 100)]
    public void ObjectViewBox_ContentRect(string viewBox, string fit, double boxWidth, double boxHeight, double x, double y, double width, double height)
    {
        // https://drafts.csswg.org/css-images-5/#the-object-view-box: the view box is fitted in the content box, and the
        // rectangle of the whole image (200x100) is scaled as the view box
        var document = Parse($"<img id=i src=200x100.png style='width: {boxWidth}px; height: {boxHeight}px; object-fit: {fit}; object-view-box: {viewBox}'>");
        Assert.Equal(new PhysicalRect(x, y, width, height), GetFragment(document, "i").ReplacedContentRect);
    }

    [Theory]
    [InlineData("inset(0px 100px 0px 0px)", 100, 100)]
    [InlineData("xywh(10px 20px 50px 25px)", 50, 25)]
    [InlineData("inset(0px)", 200, 100)]
    [InlineData("inset(60px)", 200, 100)]
    public void ObjectViewBox_NaturalSize(string viewBox, double width, double height)
    {
        // The natural size of the element is the size of the view box; a view box that is the whole image or that is empty is ignored
        var document = Parse($"<img id=i src=200x100.png style='object-view-box: {viewBox}'>");
        Assert.Equal((width, height), GetSize(document, "i"));
    }

    [Fact]
    public void ObjectFit_SizeContainment_UsesTheNaturalSizeOfTheImage()
    {
        // The box is sized without its contents, which are still fitted with their natural aspect ratio
        var document = Parse("<img id=i src=200x100.png style='contain: size; width: 100px; height: 100px; object-fit: contain'>");
        Assert.Equal(new PhysicalRect(0, 25, 100, 50), GetFragment(document, "i").ReplacedContentRect);
    }

    [Theory]
    [InlineData("embed", "src")]
    [InlineData("object", "data")]
    public void EmbedAndObject_Image(string element, string attribute)
    {
        // The image of an embed or object element is sized and fitted as the image of an img element
        var document = Parse($"<{element} id=e {attribute}=200x100.png></{element}><{element} id=f {attribute}=200x100.png style='width: 100px; height: 100px; object-fit: contain'></{element}>");
        Assert.Equal((200, 100), GetSize(document, "e"));
        Assert.Equal(new PhysicalRect(0, 25, 100, 50), GetFragment(document, "f").ReplacedContentRect);
    }

    [Fact]
    public void ContentImage_ReplacesAnImageWithAlternativeText()
    {
        // The img element represents the image of its content property, not its alternative text
        var document = Parse("<img id=i alt=Something style='content: url(20x10.png)'>");
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal((20, 10), GetSize(document, "i"));
    }

    [Fact]
    public void ImageSet_NaturalResolution()
    {
        // https://drafts.csswg.org/css-images-4/#image-set-notation: the resolution of the option is the natural resolution of
        // the image; the option is the one with the smallest resolution that is not smaller than the device pixel ratio (1),
        // among the options whose type is supported
        var document = Parse("""
            <style>
              #a { content: image-set(url(200x100.png) 2x) }
              #b { content: image-set(url(200x100.png) 0.5x, url(20x10.png) 1x) }
              #c { content: image-set(url(200x100.png) 1x type("image/unsupported"), url(20x10.png) 1x) }
              #d { content: -webkit-image-set(url(200x100.png) 4x) }
            </style>
            <div id=a></div><div id=b></div><div id=c></div><div id=d></div>
            """);

        // The images are fetched by the rendering steps
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal((100, 50), GetSize(document, "a"));
        Assert.Equal((20, 10), GetSize(document, "b"));
        Assert.Equal((20, 10), GetSize(document, "c"));
        Assert.Equal((50, 25), GetSize(document, "d"));
    }

    [Fact]
    public void ObjectFit_ScaleDown_SmallImage()
    {
        var document = Parse("<img id=i src=20x10.png style='width: 100px; height: 100px; object-fit: scale-down'>");
        Assert.Equal(new PhysicalRect(40, 45, 20, 10), GetFragment(document, "i").ReplacedContentRect);
    }

    [Theory]
    [InlineData("", 800, 400)]
    [InlineData("height: 100px", 200, 100)]
    [InlineData("width: 100px", 100, 50)]
    [InlineData("max-height: 100px", 200, 100)]
    [InlineData("box-sizing: border-box; padding: 10px; width: 100px", 100, 50)]
    [InlineData("padding: 10px; width: 100px", 120, 70)]
    public void NonReplaced_AspectRatio(string style, double width, double height)
    {
        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: the automatic size is transferred through the ratio, and the
        // min/max sizes too (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers)
        var document = Parse($"<div id=d style='aspect-ratio: 2 / 1; {style}'></div>");
        Assert.Equal((width, height), GetSize(document, "d"));
    }

    [Fact]
    public void NonReplaced_AspectRatio_AutomaticMinimum()
    {
        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum: the box grows to contain its contents, unless its minimum
        // size is not auto or it is a scroll container
        var document = Parse("""
            <div id=a style='aspect-ratio: 10; width: 100px'>X<br>X<br>X</div>
            <div id=b style='aspect-ratio: 10; width: 100px; min-height: 0'>X<br>X<br>X</div>
            <div id=c style='aspect-ratio: 10; width: 100px; overflow: auto'>X<br>X<br>X</div>
            <div id=d style='aspect-ratio: 1; height: 20px; float: left'>XXXXX</div>
            """);
        Assert.Equal((100, 30), GetSize(document, "a"));
        Assert.Equal((100, 10), GetSize(document, "b"));
        Assert.Equal((100, 10), GetSize(document, "c"));
        Assert.Equal((50, 20), GetSize(document, "d"));
    }

    [Fact]
    public void NonReplaced_AspectRatio_DefinitePercentageBasis()
    {
        // The block size computed through the ratio is definite: the percentages of the children resolve against it
        var document = Parse("<div style='aspect-ratio: 4; width: 200px'><div id=c style='height: 50%'></div></div>");
        Assert.Equal(25, GetSize(document, "c").Height);
    }

    [Theory]
    [InlineData("<input id=e>", 200, 10)]
    [InlineData("<input id=e size=5>", 50, 10)]
    [InlineData("<input id=e type=checkbox>", 13, 13)]
    [InlineData("<input id=e type=range>", 129, 16)]
    [InlineData("<input id=e type=submit value=OK>", 36, 16)]
    [InlineData("<input id=e type=button>", 16, 16)]
    [InlineData("<input id=e type=button value='a&#10;abc'>", 46, 26)]
    [InlineData("<input id=e type=checkbox style='appearance: none'>", 0, 0)]
    [InlineData("<select id=e size=0><option>ab<option>abcd</select>", 50, 10)]
    [InlineData("<select id=e style='line-height: 30px'><option>ab</select>", 30, 10)]
    [InlineData("<select id=e style='line-height: 30px; appearance: none'><option>ab</select>", 30, 30)]
    [InlineData("<embed id=e src=1x1.png style='width: 20px; height: 30px'>", 20, 30)]
    [InlineData("<textarea id=e cols=5 rows=3></textarea>", 65, 30)]
    [InlineData("<textarea id=e></textarea>", 215, 20)]
    [InlineData("<select id=e><option>ab<option>abcd</select>", 50, 10)]
    [InlineData("<select id=e multiple><option>ab</select>", 35, 40)]
    [InlineData("<select id=e size=2><option>ab</select>", 35, 20)]
    [InlineData("<select id=e size=2><option>ab<option style='display: none'>abcd<optgroup label=abcdef style='display: none'><option>abcde</select>", 35, 20)]
    [InlineData("<meter id=e></meter>", 50, 10)]
    [InlineData("<progress id=e></progress>", 100, 10)]
    [InlineData("<input id=e style='field-sizing: content' value=abc>", 31, 10)]
    [InlineData("<input id=e style='field-sizing: content' placeholder=ab>", 21, 10)]
    [InlineData("<textarea id=e style='field-sizing: content'>a&#10;abc</textarea>", 31, 20)]
    [InlineData("<input id=e style='width: 30px; height: 40px'>", 30, 40)]
    [InlineData("<input id=e type=checkbox switch>", 26, 13)]
    [InlineData("<input id=e type=checkbox switch style='writing-mode: vertical-rl'>", 13, 26)]
    [InlineData("<style>#e::placeholder { font-size: 20px }</style><input id=e style='field-sizing: content' placeholder=ab>", 41, 20)]
    [InlineData("<textarea id=e style='field-sizing: content; width: 30px'>abc abc</textarea>", 30, 20)]
    [InlineData("<select id=e multiple style='field-sizing: content'></select>", 15, 0)]
    [InlineData("<select id=e style='contain: size'><option>abcdef</select>", 10, 10)]
    [InlineData("<select id=e style='contain: size; contain-intrinsic-size: 30px 20px'><option>abcdef</select>", 30, 20)]
    public void FormControls_NaturalSizes(string html, double width, double height)
    {
        // The sizes of the rendering section of HTML (https://html.spec.whatwg.org/multipage/rendering.html#widgets), with
        // Ahem (the average and the maximum character widths are 1em), the 15px classic scrollbars of the multiline controls, the
        // arrow of the drop-down boxes (1em) and the paddings and borders of the buttons of the user agent style sheet
        // (1px 6px, 2px)
        var document = Parse(html);
        Assert.Equal((width, height), GetSize(document, "e"));
    }

    [Fact]
    public void Checkbox_AppearanceNone_LaysOutItsPseudoElements()
    {
        var document = Parse("<style>input { appearance: none; margin: 0; width: 50px; height: 40px } input::before { content: ''; display: block; width: 20px; height: 10px }</style><input id=e type=checkbox>");
        var fragment = GetFragment(document, "e");
        Assert.Equal((50, 40), GetSize(document, "e"));
        var before = Assert.IsType<BoxFragment>(Assert.Single(fragment.Children));
        Assert.Equal(new PhysicalSize(20, 10), before.Size);
    }

    [Fact]
    public void FormControls_ButtonLabelLines_BaselineOfTheLastLine()
    {
        // The line feeds of the value break the label of an input button; its baseline is the one of its last line
        var document = Parse("<div id=d>X<input id=i type=button value='a&#10;b' style='border: 0; padding: 0'></div>");
        var input = GetRect(document, "i");
        Assert.Equal(20, input.Height);
        var x = document.GetLayoutEngine().GetTextFragments((Text)document.GetElementById("d")!.FirstChild!)[0].GetRectInRoot();
        Assert.Equal(input.Y + 10, x.Y);
    }

    [Theory]
    [InlineData("<input id=i type=checkbox style='margin: 0 0 5px; padding: 0 0 3px'>", 0)]
    [InlineData("<input id=i type=radio style='margin: 0 0 5px; padding: 0 0 3px'>", 0)]
    [InlineData("<input id=i type=checkbox style='margin: 0 0 5px; appearance: none'>", -5)]
    [InlineData("<input id=i type=checkbox style='margin: 0 0 5px; border: 2px solid'>", -5)]
    [InlineData("<input id=i type=color style='margin: 0 0 5px; padding: 0 0 3px; border: 2px solid'>", 5)]
    [InlineData("<input id=i type=color style='margin: 0 0 5px; appearance: none; padding: 0 0 3px'>", 3)]
    public void FormControls_BaselineOfTheCheckboxesAndColorWells(string control, double baselineAboveBottom)
    {
        // As in the browsers (https://github.com/whatwg/html/issues/5065): a checkbox or a radio button with its native
        // appearance is aligned by the block-end edge of its padding box (by the block-end edge of its margin box without it),
        // a color well by the block-end edge of its content box
        var document = Parse("<div id=d>X" + control + "</div>");
        var input = GetRect(document, "i");
        var x = document.GetLayoutEngine().GetTextFragments((Text)document.GetElementById("d")!.FirstChild!)[0].GetRectInRoot();
        Assert.Equal(input.Bottom - baselineAboveBottom, x.Y + 8);
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("CENTER")]
    public void Image_AlignMiddle_CentersTheImageOnTheBaseline(string align)
    {
        // https://html.spec.whatwg.org/multipage/rendering.html#attributes-for-embedded-content-and-images: the vertical middle
        // of the image is on the baseline of its parent (-webkit-baseline-middle, as in the browsers)
        var document = Parse($"<div id=d>X<img id=i src=20x20.png align={align}></div>");
        var image = GetRect(document, "i");
        var x = document.GetLayoutEngine().GetTextFragments((Text)document.GetElementById("d")!.FirstChild!)[0].GetRectInRoot();
        Assert.Equal(image.Y + 10, x.Y + 8);
        Assert.Equal("-webkit-baseline-middle", document.DefaultView!.GetComputedStyle(document.GetElementById("i")!).GetPropertyValue("vertical-align"));
    }

    [Fact]
    public void FormControls_TextFieldLineHeight_ResolvedValueIsAtLeastNormal()
    {
        // https://html.spec.whatwg.org/multipage/rendering.html#the-input-element-as-a-text-entry-widget: the used line height of
        // a text field is no smaller than the normal one (Ahem: 1em); the computed value of the other elements is kept
        var document = Parse("<style>input, div { line-height: 1px }</style><input id=i><input id=n type=number><div id=d></div>");
        var window = document.DefaultView!;
        Assert.Equal("10px", window.GetComputedStyle(document.GetElementById("i")!).GetPropertyValue("line-height"));
        Assert.Equal("1px", window.GetComputedStyle(document.GetElementById("n")!).GetPropertyValue("line-height"));
        Assert.Equal("1px", window.GetComputedStyle(document.GetElementById("d")!).GetPropertyValue("line-height"));
    }

    [Fact]
    public void FormControls_TextBaseline()
    {
        // The baseline of a text field is the baseline of its text, centered in the content box (ascent 0.8em)
        var document = Parse("<div id=d>X<input id=i style='height: 30px; font-size: 20px'></div>");
        var input = GetRect(document, "i");
        Assert.Equal(30, input.Height);

        // The text of the input (20px, ascent 16px) is centered: its baseline is 5 + 16 = 21px below the top of the input
        var x = document.GetLayoutEngine().GetTextFragments((Text)document.GetElementById("d")!.FirstChild!)[0].GetRectInRoot();
        Assert.Equal(input.Y + 21, x.Y + 8);
    }

    [Fact]
    public void QuirksPercentageHeight_IsTransferredToTheIntrinsicInlineSize()
    {
        // https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk: the percentage height of the image resolves
        // against the height of the float (through the auto height div), and its width, transferred through the aspect ratio,
        // is the shrink-to-fit width of the float
        var document = Parse("<div id=f style='float: left; height: 100px'><div><img id=a src=10x10.png style='height: 100%'></div></div>", quirks: true);
        Assert.Equal((100, 100), GetSize(document, "a"));
        Assert.Equal((100, 100), GetSize(document, "f"));

        // The height of an ancestor of the float
        document = Parse("<div style='height: 80px'><div id=f style='float: left'><div><img id=a src=10x10.png style='height: 100%'></div></div></div>", quirks: true);
        Assert.Equal((80, 80), GetSize(document, "a"));
        Assert.Equal(80, GetSize(document, "f").Width);

        // No quirk in no-quirks mode: the percentage behaves as auto
        document = Parse("<div id=f style='float: left; height: 100px'><div><img id=a src=10x10.png style='height: 100%'></div></div>");
        Assert.Equal((10, 10), GetSize(document, "a"));
        Assert.Equal(10, GetSize(document, "f").Width);
    }

    [Fact]
    public void OrthogonalReplacedElement_PercentageBlockSizeDoesNotCompressTheMinContentContribution()
    {
        // The percentage block size of the image is in the inline axis of its containing block: it behaves as auto for the
        // min-content contribution (only the percentages of the inline axis of a replaced element resolve against zero)
        var document = Parse("<div id=d style='width: min-content'><img id=a src=100x100.png style='display: block; writing-mode: vertical-rl; block-size: 100%'></div><div id=e style='width: min-content'><img id=b src=100x100.png style='display: block; width: 100%'></div>");
        Assert.Equal(100, GetSize(document, "d").Width);
        Assert.Equal((100, 100), GetSize(document, "a"));
        Assert.Equal(0, GetSize(document, "e").Width);
    }

    [Fact]
    public void TableCell_PercentageBlockSizeOfAReplacedChildResolvesAgainstTheHeightOfTheCell()
    {
        // The maximum height of the image is the height of the cell, in the two layouts of the cell: the row is not taller
        var document = Parse("<div id=c style='display: table-cell; height: 100px; line-height: 0'><img id=a src=200x200.png style='max-height: 100%'></div>");
        Assert.Equal((100, 100), GetSize(document, "a"));
        Assert.Equal((100, 100), GetSize(document, "c"));

        // The basis is the height of the cell, not the height its row gives it
        document = Parse("<div style='display: table'><div style='display: table-cell; height: 50px'><div></div><img id=a src=10x10.png style='display: block; height: 200%'></div><div style='display: table-cell'><div style='height: 300px'></div></div></div>");
        Assert.Equal((100, 100), GetSize(document, "a"));

        // The percentages of the other children resolve against the block size of the cell
        document = Parse("<div style='display: table'><div style='display: table-cell; height: 50px'><div id=a style='height: 100%'></div></div><div style='display: table-cell'><div style='height: 300px'></div></div></div>");
        Assert.Equal(300, GetSize(document, "a").Height);
    }

    [Theory]
    [InlineData("width: 100px; height: 1000px; max-height: min-content", 100, 50)]
    [InlineData("width: 100px; height: 1000px; max-height: max-content", 100, 50)]
    [InlineData("width: 100px; height: 1px; min-height: min-content", 100, 50)]
    [InlineData("width: max-content; height: 0; min-height: max-content; aspect-ratio: 1", 200, 200)]
    [InlineData("width: max-content; height: 500px; max-height: max-content; aspect-ratio: 1", 200, 200)]
    public void IntrinsicBlockSizeKeywords_UseTheInlineSizeTransferredThroughTheRatio(string style, double width, double height)
    {
        var document = Parse($"<img id=a src=200x100.png style='display: block; {style}'>");
        Assert.Equal((width, height), GetSize(document, "a"));
    }

    [Fact]
    public void FormControl_MinContentSizeIsZeroWithAPercentageSize()
    {
        // min-width: min-content does not keep the default width of a range input whose width is a percentage
        var document = Parse("<div style='width: 40px'><input id=a type=range style='margin: 0; width: 50%; min-width: min-content'><input id=b type=range style='margin: 0; max-width: 50%; min-width: min-content'></div>");
        Assert.Equal(20, GetSize(document, "a").Width);
        Assert.True(GetSize(document, "b").Width > 40);

        // The same in the block axis, where a percentage maximum size also compresses the control
        document = Parse("<div style='height: 10px'><input id=a type=range style='display: block; margin: 0; height: 50%; min-height: min-content'><input id=b type=range style='display: block; margin: 0; max-height: 50%; min-height: min-content'></div>");
        Assert.Equal(5, GetSize(document, "a").Height);
        Assert.Equal(5, GetSize(document, "b").Height);
    }

    [Fact]
    public void SizeContainment_UsesContainIntrinsicSize()
    {
        var document = Parse("<img id=a src=200x100.png style='contain: size'><img id=b src=200x100.png style='contain: size; contain-intrinsic-size: 30px 20px'>");
        Assert.Equal((0, 0), GetSize(document, "a"));
        Assert.Equal((30, 20), GetSize(document, "b"));
    }

    [Fact]
    public void SizesAuto_UsesTheLayoutWidth()
    {
        // https://html.spec.whatwg.org/multipage/images.html#parse-a-sizes-attribute: the auto size is the concrete object size width
        var document = Parse("""
            <div style='width: 40px'><img id=a loading=lazy sizes=auto srcset='50x50.png 50w, 100x100.png 100w' style='width: 100%'></div>
            <div style='width: 80px'><img id=b loading=lazy sizes=auto srcset='50x50.png 50w, 100x100.png 100w' style='width: 100%'></div>
            """);
        Assert.EndsWith("50x50.png", ((HtmlImageElement)document.GetElementById("a")!).CurrentSrc, StringComparison.Ordinal);
        Assert.EndsWith("100x100.png", ((HtmlImageElement)document.GetElementById("b")!).CurrentSrc, StringComparison.Ordinal);
    }
}
