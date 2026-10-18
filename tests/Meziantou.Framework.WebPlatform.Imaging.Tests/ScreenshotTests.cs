using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>The screenshots of simple documents (Ahem text, colored boxes) compared to the expected PNG files of <c>Screenshots/</c>.</summary>
public sealed class ScreenshotTests : IDisposable
{
    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    [Fact]
    public void Boxes()
    {
        var context = _pages.Load("""
            <style>
              div { position: absolute; width: 40px; height: 30px }
              #a { left: 10px; top: 10px; background: red; border: 5px solid blue }
              #b { left: 40px; top: 30px; background: lime; z-index: 1 }
              #c { left: 100px; top: 10px; background: blue; opacity: 0.5 }
              #d { left: 100px; top: 50px; border-width: 4px 8px; border-style: solid; border-color: red lime blue black; width: 60px }
            </style>
            <div id=a></div><div id=b></div><div id=c></div><div id=d></div>
            """);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(200, screenshot.Width);
        Assert.Equal(100, screenshot.Height);
        Assert.Equal(Colors.White, screenshot.GetPixel(199, 99));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(12, 12));
        Assert.Equal(Colors.Red, screenshot.GetPixel(20, 20));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(45, 35));
        Assert.Equal(new Rgba32(128, 128, 255), screenshot.GetPixel(110, 20), ColorComparer(2));
        ScreenshotAssert.Matches(screenshot, "boxes");
    }

    [Fact]
    public void AhemText()
    {
        var context = _pages.Load("""
            <style>
              p { margin: 0 0 10px 0 }
            </style>
            <p>Hello <span style="color: blue">world</span></p>
            <p style="color: green; font-size: 20px">AB <span style="background: yellow">C</span></p>
            <p style="text-align: right; color: red">xyz</p>
            """);

        using var screenshot = context.TakeScreenshot();

        // "H" covers the first 10x10 pixels, minus the descent of Ahem (0.2em below the baseline is ink too: the glyph is the em square)
        Assert.Equal(Colors.Black, screenshot.GetPixel(5, 5));
        Assert.Equal(Colors.White, screenshot.GetPixel(55, 5)); // the space
        Assert.Equal(Colors.Blue, screenshot.GetPixel(75, 5));
        Assert.Equal(Colors.Green, screenshot.GetPixel(5, 30));
        Assert.Equal(Colors.Red, screenshot.GetPixel(195, 55));
        ScreenshotAssert.Matches(screenshot, "ahem-text");
    }

    [Fact]
    public void AhemText_NotAntialiased()
    {
        // The ascent of a 16px Ahem glyph is 12.8px and its descent 3.2px: the glyph paints the same pixels as a 16px box (as
        // in the browsers, the glyphs of Ahem are not antialiased), also at a fractional position
        var context = _pages.Load("""
            <div style="font: 16px/1 Ahem; margin-left: 0.4px">X</div>
            <div style="width: 16px; height: 16px; margin-left: 0.4px; background: black"></div>
            """);

        using var screenshot = context.TakeScreenshot();
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                Assert.Equal(x < 16 ? Colors.Black : Colors.White, screenshot.GetPixel(x, y));
                Assert.Equal(screenshot.GetPixel(x, y + 16), screenshot.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void DevicePixelRatio()
    {
        var context = _pages.Load("<div style='margin: 10px; width: 30px; height: 20px; background: blue'></div><span>ab</span>", width: 100, height: 50, devicePixelRatio: 2);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(200, screenshot.Width);
        Assert.Equal(100, screenshot.Height);
        Assert.Equal(Colors.Blue, screenshot.GetPixel(21, 21));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(79, 59));
        Assert.Equal(Colors.White, screenshot.GetPixel(81, 61));
        ScreenshotAssert.Matches(screenshot, "device-pixel-ratio");

        // The ratio of the screenshot can differ from the one of the viewport
        using var scaled = context.TakeScreenshot(new ScreenshotOptions { DevicePixelRatio = 0.5 });
        Assert.Equal(50, scaled.Width);
        Assert.Equal(25, scaled.Height);
    }

    [Fact]
    public void Element()
    {
        var context = _pages.Load("""
            <div style="height: 15px"></div>
            <div id=target style="margin-left: 20px; width: 50px; border: 2px solid red; color: blue">ab<br>c</div>
            <div style="position: absolute; left: 30px; top: 20px; width: 10px; height: 10px; background: lime"></div>
            """);

        var target = context.Document.GetElementById("target")!;
        using var screenshot = target.TakeScreenshot();
        Assert.Equal(54, screenshot.Width);
        Assert.Equal(24, screenshot.Height);
        Assert.Equal(Colors.Red, screenshot.GetPixel(0, 0));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(3, 3));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(12, 7));
        ScreenshotAssert.Matches(screenshot, "element");

        var png = target.TakeScreenshotAsPng();
        using var decoded = Image.Load<Rgba32>(png);
        Assert.Equal(54, decoded.Width);
        Assert.Equal(Colors.Lime, decoded.GetPixel(12, 7));
    }

    [Fact]
    public void Element_NotRendered()
    {
        var context = _pages.Load("<div id=hidden style='display: none'></div>");
        Assert.Throws<InvalidOperationException>(() => context.Document.GetElementById("hidden")!.TakeScreenshot());
    }

    [Fact]
    public void Document_WithoutBrowsingContext()
    {
        var document = HtmlDocumentParser.ParseDocument("<p>a</p>");
        Assert.Throws<InvalidOperationException>(() => document.TakeScreenshot());
    }

    [Fact]
    public void Images()
    {
        // A 2x2 image scaled 20 times without smoothing, and stretched with object-fit: fill
        _pages.Resources["/checker.png"] = (TestPages.CreatePng(2, 2, Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow), "image/png");
        var context = _pages.Load("""
            <img src=checker.png style="width: 40px; height: 40px; image-rendering: pixelated">
            <img src=checker.png style="width: 60px; height: 20px; image-rendering: pixelated; vertical-align: top">
            """);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Red, screenshot.GetPixel(5, 5));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(35, 5));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(5, 35));
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(35, 35));
        ScreenshotAssert.Matches(screenshot, "images");
    }

    [Fact]
    public void Images_WithoutBackend_AreNotDrawn()
    {
        // The documents of a browsing context without backend do not keep the bytes of their images: a screenshot (with a temporary backend) does not draw them
        _pages.Resources["/red.png"] = (TestPages.CreatePng(1, 1, Colors.Red), "image/png");
        var context = _pages.Load("<img src=red.png style='width: 20px; height: 20px'><span style='color: blue'>a</span>", backend: RenderingBackendKind.None);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.White, screenshot.GetPixel(5, 5));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(25, 15));
    }

    [Fact]
    public void CornerShapes()
    {
        // https://drafts.csswg.org/css-borders-4/#corner-shaping: the corners of the backgrounds, of the borders (one color, or
        // a color per side), of the shadows, of the outlines and of the overflow clips are shaped by corner-shape
        var context = _pages.Load("""
            <style>
              div { position: absolute; box-sizing: border-box; width: 50px; height: 50px; border-radius: 15px; background: yellow }
              #bevel { left: 10px; top: 10px; corner-shape: bevel; border: 5px solid blue }
              #scoop { left: 70px; top: 10px; corner-shape: scoop; border: 5px solid blue }
              #notch { left: 130px; top: 10px; corner-shape: notch; border: 5px solid blue }
              #squircle { left: 190px; top: 10px; border-radius: 25px; corner-shape: squircle; background: green }
              #mixed { left: 250px; top: 10px; corner-shape: bevel scoop notch square; border-width: 5px 10px; border-style: solid; border-color: red lime blue black }
              #shadow { left: 20px; top: 80px; corner-shape: scoop; box-shadow: 0 0 0 8px blue, inset 0 0 0 5px red }
              #outline { left: 90px; top: 80px; corner-shape: bevel; outline: 3px solid blue; outline-offset: 4px }
              #clip { left: 160px; top: 80px; corner-shape: superellipse(-2); overflow: hidden; background: none }
              #clip > div { position: static; width: 100px; height: 100px; border-radius: 0; background: green }
              #overlap { left: 220px; top: 80px; width: 80px; border-radius: 50%; corner-shape: scoop; border: 8px solid blue }
            </style>
            <div id=bevel></div><div id=scoop></div><div id=notch></div><div id=squircle></div><div id=mixed></div>
            <div id=shadow></div><div id=outline></div><div id=clip><div></div></div><div id=overlap></div>
            """, width: 320, height: 150);

        using var screenshot = context.TakeScreenshot();

        // The bevel: the corner area is cut along its diagonal, the border follows it
        Assert.Equal(Colors.White, screenshot.GetPixel(13, 13));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(19, 19));
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(24, 24));

        // The scoop: the curve is concave (the middle of the corner area is outside of the box, in the ellipse of a round corner)
        Assert.Equal(Colors.White, screenshot.GetPixel(77, 17));
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(95, 35));

        // The notch: the corner area is outside of the box, its two sides have a border
        Assert.Equal(Colors.White, screenshot.GetPixel(140, 20));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(147, 15));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(135, 27));
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(155, 35));

        // The squircle of the size of the box covers more than its inscribed circle
        Assert.Equal(Colors.Green, screenshot.GetPixel(197, 17));
        Assert.Equal(Colors.White, screenshot.GetPixel(191, 11));

        // The spread of the shadow follows the scoop, the inset shadow too
        Assert.Equal(Colors.Blue, screenshot.GetPixel(16, 105));
        Assert.Equal(Colors.Red, screenshot.GetPixel(22, 105));
        Assert.Equal(Colors.White, screenshot.GetPixel(15, 75));

        // The contents are clipped by the padding edge of the concave corners
        Assert.Equal(Colors.Green, screenshot.GetPixel(185, 105));
        Assert.Equal(Colors.White, screenshot.GetPixel(165, 85));

        // A color per side: each side is painted between the diagonals of its corners, also in the walls of the notch
        Assert.Equal(Colors.Black, screenshot.GetPixel(252, 40));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(296, 35));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(275, 58));

        // Four scoops of 50%: the corner areas of the padding edge overlap in the middle of the sides (their union is outside
        // of the padding edge: the border is painted there)
        Assert.Equal(Colors.White, screenshot.GetPixel(240, 90));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(260, 95));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(232, 105));
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(260, 105));
        ScreenshotAssert.Matches(screenshot, "corner-shapes", maxDifferentPixels: 30);
    }

    [Fact]
    public void BorderShapes()
    {
        // https://drafts.csswg.org/css-borders-4/#border-shape: the border is the stroke of a shape or the area between two
        // shapes; the backgrounds, the shadows, the outline and the overflow clip follow its edges
        var context = _pages.Load("""
            <style>
              div { position: absolute; box-sizing: border-box; width: 50px; height: 50px; background: yellow }
              #stroke { left: 10px; top: 10px; border: 5px solid blue; border-shape: circle() }
              #polygon { left: 70px; top: 10px; border: 6px solid red; border-shape: polygon(50% 0, 100% 50%, 50% 100%, 0 50%); background: green }
              #two { left: 130px; top: 10px; border-color: blue; border-shape: inset(0) circle(30%) }
              #shadow { left: 20px; top: 80px; width: 40px; height: 40px; border-shape: circle(); box-shadow: 0 0 0 6px blue, inset 0 0 0 5px red }
              #outline { left: 90px; top: 80px; width: 40px; height: 40px; border-shape: polygon(50% 0, 100% 50%, 50% 100%, 0 50%); outline: 3px solid blue; outline-offset: 4px }
              #clip { left: 160px; top: 80px; border: 5px solid blue; border-shape: circle(); overflow: hidden; background: none }
              #clip > div { position: static; width: 100px; height: 100px; background: green }
              #area { left: 230px; top: 80px; border: 8px solid transparent; border-shape: circle(); background: linear-gradient(red, red) border-area }
            </style>
            <div id=stroke></div><div id=polygon></div><div id=two></div>
            <div id=shadow></div><div id=outline></div><div id=clip><div></div></div><div id=area></div>
            """, width: 320, height: 150);

        using var screenshot = context.TakeScreenshot();

        // One shape: the stroke of a circle in the middle of the border, the background inside of it
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(35, 35));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(35, 12));
        Assert.Equal(Colors.White, screenshot.GetPixel(13, 13));

        // The stroke of a polygon has miter joins
        Assert.Equal(Colors.Green, screenshot.GetPixel(95, 35));
        Assert.Equal(Colors.Red, screenshot.GetPixel(95, 11));
        Assert.Equal(Colors.White, screenshot.GetPixel(73, 13));

        // Two shapes: the area between them has the border color (without border style), the background is in the inner one
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(155, 35));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(135, 15));

        // The outer shadow is outside of the outer edge, the inset shadow inside of the inner edge
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(40, 100));
        Assert.Equal(Colors.Red, screenshot.GetPixel(40, 83));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(40, 77));
        Assert.Equal(Colors.White, screenshot.GetPixel(16, 76));

        // The outline follows the outer edge at the distance of its offset
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(110, 100));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(110, 72));
        Assert.Equal(Colors.White, screenshot.GetPixel(110, 77));

        // The contents are clipped by the inner edge of the stroke
        Assert.Equal(Colors.Green, screenshot.GetPixel(185, 105));
        Assert.Equal(Colors.Green, screenshot.GetPixel(185, 88));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(185, 83));
        Assert.Equal(Colors.White, screenshot.GetPixel(163, 83));

        // background-clip: border-area is the area of the stroke
        Assert.Equal(Colors.Red, screenshot.GetPixel(255, 84));
        Assert.Equal(Colors.White, screenshot.GetPixel(255, 105));
        ScreenshotAssert.Matches(screenshot, "border-shapes", maxDifferentPixels: 30);
    }

    [Fact]
    public void RoundedBordersAndTransforms()
    {
        var context = _pages.Load("""
            <style>
              div { position: absolute; width: 30px; height: 30px }
            </style>
            <div style="left: 10px; top: 10px; border: 5px solid blue; border-radius: 15px; background: yellow"></div>
            <div style="left: 70px; top: 20px; background: red; transform: rotate(45deg)"></div>
            <div style="left: 130px; top: 20px; background: green; transform: perspective(100px) rotateY(45deg)"></div>
            """);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.White, screenshot.GetPixel(11, 11)); // outside of the rounded corner
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(30, 30));
        Assert.Equal(Colors.Red, screenshot.GetPixel(85, 18)); // near the top corner of the rotated square
        Assert.Equal(Colors.White, screenshot.GetPixel(72, 22)); // the top-left corner of the box before the rotation

        // The perspective makes the left side of the box (rotated towards the viewer) taller than its right side
        Assert.Equal(Colors.Green, screenshot.GetPixel(137, 22));
        Assert.Equal(Colors.White, screenshot.GetPixel(153, 18));
        ScreenshotAssert.Matches(screenshot, "rounded-transforms", maxDifferentPixels: 20);
    }

    [Fact]
    public void SyntheticStylesAndVariations()
    {
        var context = _pages.Load("""
            <p id=ahem style="margin: 0; font: 40px/1 Ahem; width: 160px">I<i>I</i><b>I</b></p>
            <p style="margin: 0; font: 20px/1 sans-serif">Il<b>Il</b></p>
            """);

        using var screenshot = context.TakeScreenshot();

        // Ahem has no italic and no bold face: the italic glyph is slanted (synthesized oblique), the bold one emboldened (its
        // outline grows beyond its em square). The glyphs of Ahem are not antialiased: a pixel is painted when its center is in
        // the outline
        Assert.Equal(Colors.Black, screenshot.GetPixel(20, 2));
        Assert.Equal(Colors.White, screenshot.GetPixel(42, 2)); // the top of the slanted glyph moved to the right
        Assert.Equal(Colors.Black, screenshot.GetPixel(76, 2));
        Assert.Equal(Colors.Black, screenshot.GetPixel(82, 2));
        Assert.Equal(Colors.Black, screenshot.GetPixel(120, 20)); // the bold glyph is wider than its em square
        Assert.Equal(Colors.White, screenshot.GetPixel(122, 20));

        // Arimo (sans-serif) is a variable font: the bold text uses its wght axis (more ink than the regular text)
        var pixels = ScreenshotAssert.GetPixels(screenshot);
        var regularInk = CountInk(pixels, screenshot.Width, 0, 10, 40, 60);
        var boldInk = CountInk(pixels, screenshot.Width, 10, 22, 40, 60);
        Assert.True(boldInk > regularInk * 1.2, $"bold {boldInk}, regular {regularInk}");

        // The glyphs are rasterized by the font engine of the platform (CoreText, DirectWrite, FreeType): only the squares of
        // Ahem give the same pixels everywhere
        using var ahem = context.Document.GetElementById("ahem")!.TakeScreenshot();
        ScreenshotAssert.Matches(ahem, "synthetic-styles", maxDifferentPixels: 40);
    }

    [Fact]
    public void Effects()
    {
        var context = _pages.Load("""
            <style>
              div { position: absolute; width: 40px; height: 40px; top: 10px }
              #clip { left: 0; background: lime; clip-path: inset(0 20px 0 0) }
              #mask { left: 50px; background: blue; mask-image: linear-gradient(black, black) ; mask-size: 20px 40px; mask-repeat: no-repeat }
              #filter { left: 100px; background: red; filter: url(#f) }
              #backdrop-under { left: 150px; background: rgb(0, 0, 255) }
              #backdrop { left: 150px; backdrop-filter: invert(1) }
            </style>
            <svg width=0 height=0 style="position: absolute"><filter id=f color-interpolation-filters=sRGB><feFlood flood-color=lime /><feComposite in2=SourceAlpha operator=in /></filter></svg>
            <div id=clip></div><div id=mask></div><div id=filter></div><div id=backdrop-under></div><div id=backdrop></div>
            """);

        using var screenshot = context.TakeScreenshot();

        // clip-path: the right half is clipped
        Assert.Equal(Colors.Lime, screenshot.GetPixel(10, 20));
        Assert.Equal(Colors.White, screenshot.GetPixel(30, 20));

        // mask: the mask image covers the left half
        Assert.Equal(Colors.Blue, screenshot.GetPixel(60, 20));
        Assert.Equal(Colors.White, screenshot.GetPixel(80, 20));

        // SVG filter: the flood color in the alpha of the source
        Assert.Equal(Colors.Lime, screenshot.GetPixel(120, 20));
        Assert.Equal(Colors.White, screenshot.GetPixel(120, 55));

        // backdrop-filter: the blue box below is inverted
        Assert.Equal(new Rgba32(255, 255, 0), screenshot.GetPixel(170, 30));
    }

    [Fact]
    public void Svg()
    {
        // An SVG image (scaled to the size of the img element), a gradient, a pattern and a marker
        var image = Uri.EscapeDataString("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10' fill='lime'/></svg>");
        var context = _pages.Load($"""
            <img src="data:image/svg+xml,{image}" style="display: block; width: 40px; height: 40px">
            <svg width=200 height=60 style="display: block">
              <linearGradient id=g><stop offset=0 stop-color=red /><stop offset=1 stop-color=blue /></linearGradient>
              <pattern id=p width=10 height=10 patternUnits=userSpaceOnUse><rect width=5 height=10 fill=blue /></pattern>
              <marker id=m markerWidth=10 markerHeight=10 refX=5 refY=5 markerUnits=userSpaceOnUse><rect width=10 height=10 fill=lime /></marker>
              <rect width=50 height=50 fill="url(#g)" />
              <rect x=60 width=50 height=50 fill="url(#p)" />
              <path d="M130 25H180" stroke=black marker-end="url(#m)" />
            </svg>
            """, width: 200, height: 100);
        context.EventLoop.RunUntilIdle();

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Lime, screenshot.GetPixel(35, 35));
        Assert.Equal(Colors.Red, screenshot.GetPixel(0, 60), ColorComparer(16));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(49, 60), ColorComparer(16));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(62, 60));
        Assert.Equal(Colors.White, screenshot.GetPixel(67, 60));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(183, 63));
    }

    [Fact]
    public void FragmentedBox_DecorationsAreSlicedFromTheStitchedBox()
    {
        // https://drafts.csswg.org/css-break-3/#valdef-box-decoration-break-slice: the box is 100px tall in two columns of 50px
        var context = _pages.Load("""
            <div style="margin: 10px; columns: 2; column-gap: 20px; column-fill: auto; width: 100px; height: 50px">
              <div style="height: 100px; background: linear-gradient(red 50%, blue 50%); box-shadow: 0 0 0 5px lime"></div>
            </div>
            """);

        using var screenshot = context.TakeScreenshot();

        // The gradient is positioned in the two fragments stitched together: the first one is red, the second one blue
        Assert.Equal(Colors.Red, screenshot.GetPixel(20, 55));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(80, 15));

        // The shadow is painted around the stitched box, not at the breaks (below the first fragment, above the second one)
        Assert.Equal(Colors.Lime, screenshot.GetPixel(20, 7));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(7, 30));
        Assert.Equal(Colors.White, screenshot.GetPixel(20, 62));
        Assert.Equal(Colors.White, screenshot.GetPixel(80, 7));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(80, 62));
    }

    [Fact]
    public void RoundedBorder_WithAMissingSideKeepsItsCurve()
    {
        // The inner curve of the top corners belongs to the top, left and right sides: it is painted although the border has no
        // bottom side (the sides are not one ring)
        var context = _pages.Load("""
            <div style="width: 100px; height: 60px; border: 20px solid blue; border-bottom: none; border-radius: 60px 60px 0 0; background: yellow"></div>
            """);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Blue, screenshot.GetPixel(24, 24));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(115, 24));
        Assert.Equal(Colors.Yellow, screenshot.GetPixel(70, 40));
        Assert.Equal(Colors.White, screenshot.GetPixel(3, 3));
    }

    [Fact]
    public void SvgAnimations()
    {
        // SMIL animations: an attribute (width), a property (fill), a transform and a motion path; an animated SVG image is
        // painted at the begin of its timeline
        var image = Uri.EscapeDataString("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10' fill='lime'><set attributeName='fill' to='red' begin='1s'/></rect></svg>");
        var context = _pages.Load($"""
            <svg id=svg width=200 height=60 style="display: block">
              <rect width=10 height=20 fill=blue><animate attributeName=width from=10 to=50 dur=4s /></rect>
              <rect x=60 width=20 height=20 fill=red><set attributeName=fill to=lime begin=1s /></rect>
              <rect width=20 height=20 fill=blue><animateTransform attributeName=transform type=translate from="100 0" to="100 40" dur=4s /></rect>
              <rect width=20 height=20 fill=blue><animateMotion path="M 140 0 V 40" dur=4s /></rect>
            </svg>
            <img src="data:image/svg+xml,{image}" style="display: block; width: 40px; height: 40px">
            """, width: 200, height: 100);
        var svg = (Dom.SvgSvgElement)context.Document.GetElementById("svg")!;
        svg.PauseAnimations();
        svg.SetCurrentTime(2);

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Blue, screenshot.GetPixel(28, 10));
        Assert.Equal(Colors.White, screenshot.GetPixel(32, 10));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(70, 10));
        Assert.Equal(Colors.White, screenshot.GetPixel(110, 10));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(110, 30));
        Assert.Equal(Colors.White, screenshot.GetPixel(150, 10));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(150, 30));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(20, 80));

        // The painting follows the timeline
        svg.SetCurrentTime(0);
        using var first = context.TakeScreenshot();
        Assert.Equal(Colors.White, first.GetPixel(28, 10));
        Assert.Equal(Colors.Red, first.GetPixel(70, 10));
        Assert.Equal(Colors.Blue, first.GetPixel(110, 10));
    }

    [Fact]
    public void ScopedViewTransition()
    {
        // The pseudo-elements of a scoped view transition are painted above the contents of its root element (also above its
        // descendants with a z-index), with its transform, without its opacity, and they are not clipped by its overflow
        var context = _pages.Load("""
            <style>
              body { margin: 0 }
              #scope { position: relative; width: 100px; height: 60px; background: yellow; overflow: hidden; view-transition-name: none }
              #scope.updated { opacity: 0.5; transform: translateX(200px) }
              #part { width: 40px; height: 30px; background: red; view-transition-name: part }
              #part.moved { margin-left: 80px; background: lime }
              #above { position: absolute; z-index: 5; left: 0; top: 0; width: 20px; height: 20px; background: blue }
              ::view-transition-group(*) { animation-duration: 100s; animation-timing-function: steps(1, jump-start) }
              ::view-transition-old(*) { animation: none; opacity: 0 }
              ::view-transition-new(*) { animation: none; opacity: 1 }
            </style>
            <div id=scope><div id=part></div><div id=above></div></div>
            """, width: 400);
        var document = context.Document;
        var scope = document.GetElementById("scope")!;
        var pending = new TaskCompletionSource();
        var transition = scope.StartViewTransition(() =>
        {
            document.GetElementById("part")!.ClassList.Add("moved");
            scope.ClassList.Add("updated");
            return pending.Task;
        });
        _pages.Clock.Advance(TimeSpan.FromMilliseconds(20));
        context.EventLoop.RunUntilIdle();
        Assert.False(transition.Ready.IsCompleted);

        // The rendering is paused within the root element: the frame of its old state is displayed, with its new transform
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Blue, screenshot.GetPixel(210, 10));
            Assert.Equal(Colors.Red, screenshot.GetPixel(230, 10));
            Assert.Equal(Colors.Yellow, screenshot.GetPixel(290, 10));
            Assert.Equal(Colors.White, screenshot.GetPixel(10, 10));
        }

        pending.SetResult();
        _pages.Clock.Advance(TimeSpan.FromMilliseconds(20));
        context.EventLoop.RunUntilIdle();
        Assert.True(transition.Ready.IsCompletedSuccessfully);
        using (var screenshot = context.TakeScreenshot())
        {
            // The image of the part is opaque above the translucent root element, over the blue box, and overflows the root element
            Assert.Equal(Colors.Lime, screenshot.GetPixel(285, 10));
            Assert.Equal(Colors.Lime, screenshot.GetPixel(310, 10));
            Assert.Equal(new Rgba32(128, 128, 255), screenshot.GetPixel(210, 10));
            Assert.Equal(new Rgba32(255, 255, 128), screenshot.GetPixel(250, 50));
        }

        transition.SkipTransition();
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.White, screenshot.GetPixel(310, 10));
            Assert.Equal(new Rgba32(128, 255, 128), screenshot.GetPixel(285, 10));
        }
    }

    [Fact]
    public void ViewTransition()
    {
        // The old state is rasterized by the backend when it is captured (with its ink overflow: the shadow), and painted by the
        // ::view-transition-old() pseudo-element; the captured element is not painted in the document while the transition runs
        var context = _pages.Load("""
            <style>
              #target { width: 40px; height: 30px; background: red; box-shadow: 4px 4px blue; view-transition-name: target }
              #target.moved { margin-left: 100px; background: lime; box-shadow: none }
              ::view-transition-group(*) { animation-duration: 100s }
              ::view-transition-old(*) { animation: none; opacity: 1 }
              ::view-transition-new(*) { animation: none; opacity: 0 }
            </style>
            <div id=target></div>
            """);
        var document = context.Document;
        var target = document.GetElementById("target")!;
        var transition = document.StartViewTransition(() =>
        {
            target.ClassList.Add("moved");
            return null;
        });
        _pages.Clock.Advance(TimeSpan.FromMilliseconds(20));
        context.EventLoop.RunUntilIdle();
        Assert.True(transition.Ready.IsCompletedSuccessfully);

        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Red, screenshot.GetPixel(10, 10));
            Assert.Equal(Colors.Blue, screenshot.GetPixel(42, 20));
            Assert.Equal(Colors.Blue, screenshot.GetPixel(20, 32));
            Assert.Equal(Colors.White, screenshot.GetPixel(110, 10));
            ScreenshotAssert.Matches(screenshot, "view-transition");
        }

        // The images of the new state are live: the new element is painted by ::view-transition-new()
        var style = document.CreateElement("style");
        style.TextContent = "::view-transition-old(*) { opacity: 0 } ::view-transition-new(*) { opacity: 1 } ::view-transition-group(*) { animation: none }";
        document.Head!.AppendChild(style);
        target.SetAttribute("style", "background: blue");
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.White, screenshot.GetPixel(10, 10));
            Assert.Equal(Colors.Blue, screenshot.GetPixel(110, 10));
        }

        transition.SkipTransition();
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.White, screenshot.GetPixel(10, 10));
            Assert.Equal(Colors.Blue, screenshot.GetPixel(110, 10));
        }
    }

    private static Func<Rgba32, Rgba32, bool> ColorComparer(int tolerance) => (a, b) => Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance && Math.Abs(a.A - b.A) <= tolerance;

    /// <summary>Returns the sum of the darkness of the pixels of an area.</summary>
    private static int CountInk(byte[] pixels, int width, int left, int right, int top, int bottom)
    {
        var ink = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                ink += 255 - pixels[((y * width) + x) * 4];
            }
        }

        return ink;
    }
}
