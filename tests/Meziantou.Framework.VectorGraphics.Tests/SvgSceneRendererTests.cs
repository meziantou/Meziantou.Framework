namespace Meziantou.Framework.VectorGraphics.Tests;

public sealed class SvgSceneRendererTests
{
    [Fact]
    public void CreateAScene()
    {
        #region VectorGraphics_CreateScene
        var scene = new Scene(width: 200, height: 80, fontFamily: AfmTextMeasurer.Default.FontFamily) { Title = "A box", Background = SceneColor.White };

        // Shapes are filled and stroked; texts are positioned by their baseline
        scene.Root.Add(new SceneRectangle(10, 10, 180, 60) { Fill = (SceneColor)"#e0f2fe", Stroke = (SceneColor)"#0369a1", StrokeWidth = 2, CornerRadius = 8 });
        scene.Root.Add(new SceneText(100, 45, "Hello", new SceneFont(16, Weight: SceneFont.BoldWeight), (SceneColor)"#0c4a6e") { Anchor = SceneTextAnchor.Middle });

        // Paths use the commands of SVG
        var arrow = new PathBuilder().MoveTo(20, 60).LineTo(60, 60).LineTo(55, 55).MoveTo(60, 60).LineTo(55, 65).Build();
        scene.Root.Add(new ScenePath(arrow) { Stroke = (SceneColor)"#0369a1" });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("CreateScene", svg);

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"80\"", svg, StringComparison.Ordinal);
        Assert.Contains("<title>A box</title>", svg, StringComparison.Ordinal);
        Assert.Contains(">Hello</text>", svg, StringComparison.Ordinal);
        Assert.Contains("d=\"M20,60 L60,60 L55,55 M60,60 L55,65\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void SvgNumbers()
    {
        Assert.Equal("0", SvgNumber.Format(-0.001, 2));
        Assert.Equal("1.13", SvgNumber.Format(1.125, 2));
        Assert.Equal("1", SvgNumber.Format(1.005, 2)); // 1.005 is 1.00499999999999989 in binary
        Assert.Equal("-1.5", SvgNumber.Format(-1.5, 2));
        Assert.Equal("2", SvgNumber.Format(1.6, 0));
        Assert.Equal("0", SvgNumber.Format(double.NaN, 2));
        var path = new PathBuilder().MoveTo(0, 0).LineTo(1, 1).CubicTo(1, 2, 3, 4, 5, 6).QuadraticTo(7, 8, 9, 10).ArcTo(5, 5, 30, largeArc: true, sweep: false, 11, 12).Close().Build();
        Assert.Equal("M0,0 L1,1 C1,2 3,4 5,6 Q7,8 9,10 A5,5 30 1,0 11,12 Z", path.ToString());
        Assert.Equal(6, path.Commands.Count);
        Assert.Equal("M0,0 L1,0 L1,1 L0,1 Z", new PathBuilder().AddRectangle(0, 0, 1, 1).Build().ToString());
    }

    [Fact]
    public void PathShapes()
    {
        #region VectorGraphics_PathShapes
        // A triangle, an ellipse and a rounded rectangle in a single path
        var path = new PathBuilder()
            .AddPolygon((0, 0), (10, 0), (5, 8))
            .AddEllipse(centerX: 30, centerY: 5, radiusX: 10, radiusY: 5)
            .AddRoundedRectangle(x: 50, y: 0, width: 20, height: 10, radius: 2)
            .Build();

        // Paths can be moved, and combined into a new path
        var moved = path.Translate(offsetX: 0, offsetY: 20);
        var both = new PathBuilder().Append(path).Append(moved).Build();
        Assert.HasCount(2 * path.Commands.Count, both.Commands);
        #endregion

        Assert.Equal(
            "M0,0 L10,0 L5,8 Z M40,5 C40,7.76 35.52,10 30,10 C24.48,10 20,7.76 20,5 C20,2.24 24.48,0 30,0 C35.52,0 40,2.24 40,5 Z " +
            "M52,0 L68,0 C69.1,0 70,0.9 70,2 L70,8 C70,9.1 69.1,10 68,10 L52,10 C50.9,10 50,9.1 50,8 L50,2 C50,0.9 50.9,0 52,0 Z",
            path.ToString());
        Assert.Equal(
            "M0,20 L10,20 L5,28 Z M40,25 C40,27.76 35.52,30 30,30 C24.48,30 20,27.76 20,25 C20,22.24 24.48,20 30,20 C35.52,20 40,22.24 40,25 Z " +
            "M52,20 L68,20 C69.1,20 70,20.9 70,22 L70,28 C70,29.1 69.1,30 68,30 L52,30 C50.9,30 50,29.1 50,28 L50,22 C50,20.9 50.9,20 52,20 Z",
            moved.ToString());
    }

    [Fact]
    public void PathShapesEdgeCases()
    {
        Assert.True(new PathBuilder().AddPolygon().IsEmpty);
        Assert.Equal("M1,2 Z", new PathBuilder().AddPolygon((1, 2)).Build().ToString());

        // The radius is limited to half the smaller side, and no radius gives a plain rectangle
        Assert.Equal("M0,0 L10,0 L10,4 L0,4 Z", new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, 0).Build().ToString());
        Assert.Equal("M0,0 L10,0 L10,4 L0,4 Z", new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, -3).Build().ToString());
        Assert.Equal(new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, 2).Build().ToString(), new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, 50).Build().ToString());

        // Translating keeps the radii and the rotation of the arcs, and Close has no coordinates
        var arc = new PathBuilder().MoveTo(0, 0).ArcTo(5, 6, 30, largeArc: true, sweep: false, 10, 0).QuadraticTo(1, 2, 3, 4).Close().Build();
        Assert.Equal("M1,2 A5,6 30 1,0 11,2 Q2,4 4,6 Z", arc.Translate(1, 2).ToString());
        Assert.Equal(new PathCommand(PathCommandKind.Close, 0, 0), arc.Translate(1, 2).Commands[^1]);

        Assert.Throws<ArgumentNullException>("path", () => new PathBuilder().Append(null!));
    }

    [Fact]
    public void ImagesClipPathsAndRadialGradients()
    {
        #region VectorGraphics_ImagesAndClipping
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");
        var scene = new Scene(width: 100, height: 100, fontFamily: "sans-serif");

        // A radial gradient, from white at the center to blue on the circle
        scene.Root.Add(new SceneRectangle(0, 0, 100, 100) { Fill = new RadialGradientPaint(50, 50, 50, [new(0, SceneColor.White), new(1, (SceneColor)"#0369a1")]) });

        // An image, stretched to its rectangle and clipped to a circle
        var avatar = scene.Root.Add(new SceneGroup { ClipPath = new PathBuilder().AddCircle(50, 50, 30).Build() });
        avatar.Add(new SceneImage(20, 20, 60, 60, png));

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("ImagesAndClipping", svg);

        Assert.Contains("<radialGradient id=\"scene-gradient0\" gradientUnits=\"userSpaceOnUse\" cx=\"50\" cy=\"50\" r=\"50\">", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"url(#scene-gradient0)\"", svg, StringComparison.Ordinal);
        Assert.Contains("<clipPath id=\"scene-clip0\"><path d=\"M50,20 A30,30 0 0,1 50,80 A30,30 0 0,1 50,20 Z\"/></clipPath>", svg, StringComparison.Ordinal);
        Assert.Contains("<g clip-path=\"url(#scene-clip0)\">", svg, StringComparison.Ordinal);
        Assert.Contains("<image x=\"20\" y=\"20\" width=\"60\" height=\"60\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC\"/>", svg, StringComparison.Ordinal);
        Assert.Equal("image/png", new SceneImage(0, 0, 1, 1, png).MediaType);
    }

    [Fact]
    public void ClipsAndFocalPoints()
    {
        // A clip rectangle and a clip path both apply, in the coordinates of the parent: they clip outer groups
        var scene = new Scene(100, 100, "sans-serif");
        var group = scene.Root.Add(new SceneGroup
        {
            Clip = new SceneBounds(0, 0, 50, 50),
            ClipPath = new PathBuilder().AddRectangle(10, 10, 80, 80).AddRectangle(20, 20, 10, 10).Build(),
            ClipRule = SceneFillRule.EvenOdd,
            Transform = SceneTransform.Translate(5, 5),
        });
        group.Add(new SceneCircle(0, 0, 10) { Fill = new RadialGradientPaint(0, 0, 10, 2, 3, [new(0, SceneColor.White), new(1, SceneColor.Black)]) });
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Contains("<clipPath id=\"scene-clip1\"><path d=\"M10,10 L90,10 L90,90 L10,90 Z M20,20 L30,20 L30,30 L20,30 Z\" clip-rule=\"evenodd\"/></clipPath>", svg, StringComparison.Ordinal);
        Assert.Contains("<g clip-path=\"url(#scene-clip0)\">\n<g clip-path=\"url(#scene-clip1)\">\n<g transform=\"translate(5 5)\">", svg, StringComparison.Ordinal);
        Assert.Contains("cx=\"0\" cy=\"0\" r=\"10\" fx=\"2\" fy=\"3\">", svg, StringComparison.Ordinal);
        Assert.Contains("</g>\n</g>\n</g>\n", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, "image/gif")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "image/webp")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E' }, null)]
    [InlineData(new byte[] { 0x89, (byte)'P', (byte)'N' }, null)]
    [InlineData(new byte[0], null)]
    public void ImageMediaTypes(byte[] data, string? expected)
    {
        Assert.Equal(expected, SceneImage.GetMediaType(data));
        if (expected is null)
        {
            Assert.Throws<ArgumentException>(nameof(data), () => new SceneImage(0, 0, 1, 1, data));
        }
    }

    [Fact]
    public void DashOffsetAndMiterLimit()
    {
        var scene = new Scene(100, 100, "sans-serif");
        scene.Root.Add(new SceneLine(0, 10, 90, 10) { Stroke = SceneColor.Black, StrokeDashArray = [4, 2], StrokeDashOffset = 3 });
        scene.Root.Add(new ScenePath(new PathBuilder().MoveTo(0, 0).LineTo(5, 50).LineTo(10, 0).Build()) { Stroke = SceneColor.Black, MiterLimit = 10 });

        // The defaults, a miter limit that does not apply, and invalid values write nothing
        scene.Root.Add(new SceneLine(0, 20, 90, 20) { Stroke = SceneColor.Black, StrokeDashOffset = 3, MiterLimit = 10, LineJoin = SceneLineJoin.Round });
        scene.Root.Add(new SceneLine(0, 30, 90, 30) { Stroke = SceneColor.Black, StrokeDashArray = [4, 2], StrokeDashOffset = double.NaN, MiterLimit = 0.5 });
        var svg = SvgSceneRenderer.Render(scene);

        Assert.Contains("stroke-dasharray=\"4 2\" stroke-dashoffset=\"3\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke-miterlimit=\"10\"", svg, StringComparison.Ordinal);
        Assert.Equal(1, svg.Split("stroke-dashoffset").Length - 1);
        Assert.Equal(1, svg.Split("stroke-miterlimit").Length - 1);
        Assert.Equal(4, new SceneRectangle(0, 0, 1, 1).MiterLimit);
    }

    [Fact]
    public void TextWeightsAndDecorations()
    {
        #region VectorGraphics_TextStyles
        var scene = new Scene(width: 300, height: 125, fontFamily: "sans-serif");

        // A weight from 1 to 1000, as in CSS, and lines drawn with the text
        scene.Root.Add(new SceneText(10, 20, "Light", new SceneFont(16, Weight: 300), SceneColor.Black));
        scene.Root.Add(new SceneText(10, 45, "Bold and underlined", new SceneFont(16, Weight: SceneFont.BoldWeight), SceneColor.Black) { Decorations = SceneTextDecorations.Underline });
        scene.Root.Add(new SceneText(10, 70, "Removed", new SceneFont(16), SceneColor.Black) { Decorations = SceneTextDecorations.Strikethrough });

        // Space between the letters and the words, in pixels, and a line over the text
        scene.Root.Add(new SceneText(10, 105, "Spaced and overlined", new SceneFont(16), SceneColor.Black) { LetterSpacing = 2, WordSpacing = 6, Decorations = SceneTextDecorations.Overline });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("TextStyles", svg);

        Assert.Contains("font-size=\"16\" font-weight=\"300\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-weight=\"bold\" text-decoration=\"underline\"", svg, StringComparison.Ordinal);
        Assert.Contains("text-decoration=\"line-through\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-weight=\"1000\"", SvgSceneRenderer.Render(Single(new SceneText(0, 0, "a", new SceneFont(10, Weight: 5000), SceneColor.Black) { Decorations = SceneTextDecorations.Underline | SceneTextDecorations.Strikethrough })), StringComparison.Ordinal);
        Assert.Contains("text-decoration=\"underline line-through\"", SvgSceneRenderer.Render(Single(new SceneText(0, 0, "a", new SceneFont(10), SceneColor.Black) { Decorations = SceneTextDecorations.Underline | SceneTextDecorations.Strikethrough })), StringComparison.Ordinal);
        Assert.True(new SceneFont(10, Weight: 600).IsBold);
        Assert.False(new SceneFont(10, Weight: 599).IsBold);
        Assert.Contains("letter-spacing=\"2\" word-spacing=\"6\" fill=\"#000000\"", svg, StringComparison.Ordinal);
        Assert.Contains("text-decoration=\"overline\"", svg, StringComparison.Ordinal);
        Assert.Contains("text-decoration=\"underline overline line-through\"", SvgSceneRenderer.Render(Single(new SceneText(0, 0, "a", new SceneFont(10), SceneColor.Black) { Decorations = SceneTextDecorations.Underline | SceneTextDecorations.Strikethrough | SceneTextDecorations.Overline })), StringComparison.Ordinal);
    }

    [Fact]
    public void SpacingOfSpans()
    {
        // A span writes the spacing that differs from the text; a spacing that is not finite is 0
        var text = new SceneText(0, 10, [new SceneTextSpan("a"), new SceneTextSpan("b") { LetterSpacing = 3 }, new SceneTextSpan("c") { LetterSpacing = 1, WordSpacing = double.NaN }], new SceneFont(10), SceneColor.Black) { LetterSpacing = 1, WordSpacing = double.PositiveInfinity };
        var svg = SvgSceneRenderer.Render(Single(text));
        Assert.Contains("<text x=\"0\" y=\"10\" font-size=\"10\" letter-spacing=\"1\" fill=\"#000000\">a<tspan letter-spacing=\"3\">b</tspan>c</text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TextPaints()
    {
        #region VectorGraphics_TextPaints
        var scene = new Scene(width: 300, height: 100, fontFamily: "sans-serif");
        var font = new SceneFont(28, Weight: SceneFont.BoldWeight);

        // A text filled with a gradient, and an outlined text: a stroke and a transparent fill
        var gradient = new LinearGradientPaint(10, 0, 140, 0, [new(0, (SceneColor)"#0369a1"), new(1, (SceneColor)"#be123c")]);
        scene.Root.Add(new SceneText(10, 35, "Gradient", font, SceneColor.Black) { Fill = gradient });
        scene.Root.Add(new SceneText(160, 35, "Outline", font, SceneColor.Transparent) { Stroke = (SceneColor)"#0369a1", StrokeWidth = 1.5 });

        // A thick outline painted under the fill, as a halo
        scene.Root.Add(new SceneText(10, 80, "Halo", font, SceneColor.Black) { Stroke = (SceneColor)"#fde047", StrokeWidth = 6, LineJoin = SceneLineJoin.Round, PaintOrder = ScenePaintOrder.StrokeFillMarkers });

        // Lines painted with the color of the text under spans of other colors, as SVG paints the lines of a parent element
        SceneTextSpan[] spans = [new("Red and "), new("blue") { Color = (SceneColor)"#0000ff" }];
        scene.Root.Add(new SceneText(110, 80, spans, new SceneFont(20), (SceneColor)"#ff0000") { Decorations = SceneTextDecorations.Underline, DecorationFill = (SceneColor)"#ff0000" });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("TextPaints", svg);
        Assert.Contains("<linearGradient id=\"scene-gradient0\"", svg, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"35\" font-size=\"28\" font-weight=\"bold\" fill=\"url(#scene-gradient0)\">Gradient</text>", svg, StringComparison.Ordinal);
        Assert.Contains("<text x=\"160\" y=\"35\" font-size=\"28\" font-weight=\"bold\" fill=\"#000000\" fill-opacity=\"0\" stroke=\"#0369a1\" stroke-width=\"1.5\">Outline</text>", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#000000\" stroke=\"#fde047\" stroke-width=\"6\" stroke-linejoin=\"round\" paint-order=\"stroke\">Halo</text>", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ff0000\"><tspan text-decoration=\"underline\">Red and </tspan><tspan fill=\"#ff0000\" text-decoration=\"underline\"><tspan fill=\"#0000ff\">blue</tspan></tspan></text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TextPaintsOfSpans()
    {
        // A span overrides the paints of the text; an opaque paint resets the opacity the span inherits
        var pattern = new PatternPaint(new SceneBounds(0, 0, 4, 4));
        pattern.Content.Add(new SceneRectangle(0, 0, 2, 2) { Fill = SceneColor.Black });
        SceneTextSpan[] spans =
        [
            new("a"),
            new("b") { Fill = pattern },
            new("c") { Stroke = (SceneColor)"#00ff00" },
            new("d") { Color = (SceneColor)"#ff0000", Fill = (SceneColor)"#0000ff" },
            new("e") { Color = (SceneColor)"#ff0000" },
        ];
        var text = new SceneText(0, 10, spans, new SceneFont(10), (SceneColor)"#00000080") { Stroke = (SceneColor)"#ff000080", StrokeWidth = 2, StrokeDashArray = [1, 2], StrokeDashOffset = 1, LineCap = SceneLineCap.Round, MiterLimit = 8 };
        var svg = SvgSceneRenderer.Render(Single(text));
        Assert.Contains("<pattern id=\"scene-pattern0\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#000000\" fill-opacity=\"0.502\" stroke=\"#ff0000\" stroke-opacity=\"0.502\" stroke-width=\"2\" stroke-dasharray=\"1 2\" stroke-dashoffset=\"1\" stroke-linecap=\"round\" stroke-miterlimit=\"8\">a", svg, StringComparison.Ordinal);
        Assert.Contains("<tspan fill=\"url(#scene-pattern0)\" fill-opacity=\"1\">b</tspan>", svg, StringComparison.Ordinal);
        Assert.Contains("<tspan stroke=\"#00ff00\" stroke-opacity=\"1\">c</tspan>", svg, StringComparison.Ordinal);
        Assert.Contains("<tspan fill=\"#0000ff\" fill-opacity=\"1\">d</tspan>", svg, StringComparison.Ordinal);
        Assert.Contains("<tspan fill=\"#ff0000\" fill-opacity=\"1\">e</tspan>", svg, StringComparison.Ordinal);

        // A text without spans whose lines have their own paint: the glyphs are in a tspan of their own fill
        var lined = new SceneText(0, 10, "x", new SceneFont(10), SceneColor.Black) { Decorations = SceneTextDecorations.Underline, DecorationFill = (SceneColor)"#ff000080" };
        Assert.Contains("text-decoration=\"underline\" fill=\"#ff0000\" fill-opacity=\"0.502\"><tspan fill=\"#000000\" fill-opacity=\"1\">x</tspan></text>", SvgSceneRenderer.Render(Single(lined)), StringComparison.Ordinal);

        // Lines of the paint of the glyphs, an outline without width, and a stroke without paint are not written
        Assert.Contains(">x</text>", SvgSceneRenderer.Render(Single(new SceneText(0, 10, "x", new SceneFont(10), SceneColor.Black) { Decorations = SceneTextDecorations.Underline, DecorationFill = SceneColor.Black })), StringComparison.Ordinal);
        Assert.DoesNotContain("stroke", SvgSceneRenderer.Render(Single(new SceneText(0, 10, "x", new SceneFont(10), SceneColor.Black) { Stroke = SceneColor.Black, StrokeWidth = 0 })), StringComparison.Ordinal);
        Assert.DoesNotContain("stroke", SvgSceneRenderer.Render(Single(new SceneText(0, 10, "x", new SceneFont(10), SceneColor.Black) { StrokeWidth = 3, PaintOrder = ScenePaintOrder.StrokeFillMarkers })), StringComparison.Ordinal);

        // The outline of a text extends its bounds
        var outlined = new SceneText(0, 10, "x", new SceneFont(10), SceneColor.Black) { Stroke = SceneColor.Black, StrokeWidth = 4, LineJoin = SceneLineJoin.Round };
        Assert.Equal(new SceneBounds(-2, -2, 24, 24), outlined.GetBounds(_ => new SceneBounds(0, 0, 20, 20)));
    }

    [Fact]
    public void TextPositions()
    {
        #region VectorGraphics_TextPositions
        var scene = new Scene(width: 300, height: 150, fontFamily: "sans-serif");
        var font = new SceneFont(16);
        var small = font with { Size = 10 };

        // A formula with a subscript and a superscript: the shift applies to its span only
        scene.Root.Add(new SceneText(10, 30, [new("H"), new("2") { Font = small, BaselineShift = -4 }, new("O and x"), new("2") { Font = small, BaselineShift = 7 }], font, SceneColor.Black));

        // Spans at their own positions: each line is a text chunk, centered on its x
        scene.Root.Add(new SceneText(220, 30, [new("First line"), new("Second line") { X = 220, Dy = 20 }, new("Third") { X = 220, Dy = 20 }], font, SceneColor.Black) { Anchor = SceneTextAnchor.Middle });

        // Turned letters, and a span moved to the right
        scene.Root.Add(new SceneText(10, 75, [new("Wavy") { GlyphRotation = 15 }, new("text") { Dx = 8 }], font, SceneColor.Black));

        // A text centered vertically on its y, and a text stretched to 120 pixels
        scene.Root.Add(new SceneLine(10, 110, 140, 110) { Stroke = (SceneColor)"#cbd5e1" });
        scene.Root.Add(new SceneText(10, 110, "Middle", font, SceneColor.Black) { Baseline = SceneTextBaseline.Middle });
        scene.Root.Add(new SceneText(10, 140, "Stretched", font, SceneColor.Black) { TextLength = 120, LengthAdjust = SceneTextLengthAdjust.SpacingAndGlyphs });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("TextPositions", svg);
        Assert.Contains("fill=\"#000000\">H<tspan baseline-shift=\"-4\" font-size=\"10\">2</tspan>O and x<tspan baseline-shift=\"7\" font-size=\"10\">2</tspan></text>", svg, StringComparison.Ordinal);
        Assert.Contains(">First line<tspan x=\"220\" dy=\"20\">Second line</tspan><tspan x=\"220\" dy=\"20\">Third</tspan></text>", svg, StringComparison.Ordinal);
        Assert.Contains("><tspan rotate=\"15\">Wavy</tspan><tspan dx=\"8\">text</tspan></text>", svg, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"110\" dominant-baseline=\"middle\" font-size=\"16\"", svg, StringComparison.Ordinal);
        Assert.Contains("textLength=\"120\" lengthAdjust=\"spacingAndGlyphs\"", svg, StringComparison.Ordinal);

        // A right-to-left text; values that are not finite are not written; on a path, the absolute positions and the rotations are not written
        var rightToLeft = SvgSceneRenderer.Render(Single(new SceneText(100, 10, "\u05E9\u05DC\u05D5\u05DD", font, SceneColor.Black) { Direction = SceneTextDirection.RightToLeft, TextLength = double.NaN }));
        Assert.Contains("<text x=\"100\" y=\"10\" direction=\"rtl\" font-size=\"16\" fill=\"#000000\">", rightToLeft, StringComparison.Ordinal);
        Assert.Contains(">a<tspan dy=\"3\">b</tspan></text>", SvgSceneRenderer.Render(Single(new SceneText(0, 10, [new("a"), new("b") { X = double.NaN, Dx = double.PositiveInfinity, Dy = 3, GlyphRotation = double.NaN, BaselineShift = double.NaN }], font, SceneColor.Black) { TextLength = -1 })), StringComparison.Ordinal);
        var onPath = new SceneText(0, 0, [new("a"), new("b") { X = 5, Y = 5, Dx = 2, GlyphRotation = 30 }], font, SceneColor.Black) { Path = new PathBuilder().MoveTo(0, 0).LineTo(100, 0).Build() };
        Assert.Contains("<textPath href=\"#scene-textpath0\">a<tspan dx=\"2\">b</tspan></textPath>", SvgSceneRenderer.Render(Single(onPath)), StringComparison.Ordinal);
    }

    [Fact]
    public void TextPlacementOfChunksAndShifts()
    {
        // Three spans of 10 pixels: the second one starts a chunk at x = 100, moved 5 down; the third one follows it
        var text = new SceneText(0, 50, [new("a"), new("b") { X = 100, Dy = 5 }, new("c") { Dx = 2, BaselineShift = 3 }], new SceneFont(10), SceneColor.Black) { Anchor = SceneTextAnchor.Middle };
        var placement = TextPlacement.Place(text, [new(0, 10, 1, 0), new(1, 10, 1, 0), new(2, 10, 1, 0)]);
        Assert.Equal([-5.0, 89, 101], placement.X);
        Assert.Equal([50.0, 55, 52], placement.Y);

        // The anchor of a chunk is the position of its first character, after its shift
        text = new SceneText(0, 0, [new("a") { Dx = 20 }, new("b")], new SceneFont(10), SceneColor.Black) { Anchor = SceneTextAnchor.End };
        placement = TextPlacement.Place(text, [new(0, 10, 1, 0), new(1, 10, 1, 0)]);
        Assert.Equal([0.0, 10], placement.X);

        // From right to left, the spans follow each other to the left of the start
        text = new SceneText(100, 0, [new("a"), new("b")], new SceneFont(10), SceneColor.Black) { Direction = SceneTextDirection.RightToLeft };
        placement = TextPlacement.Place(text, [new(0, 10, 1, 0), new(1, 30, 1, 0)]);
        Assert.Equal([90.0, 60], placement.X);
        text.Anchor = SceneTextAnchor.End;
        placement = TextPlacement.Place(text, [new(0, 10, 1, 0), new(1, 30, 1, 0)]);
        Assert.Equal([130.0, 100], placement.X);

        // The baseline offset moves the alphabetic baseline down from the y of the text
        placement = TextPlacement.Place(new SceneText(0, 10, "a", new SceneFont(10), SceneColor.Black), [new(-1, 10, 1, 4)]);
        Assert.Equal([14.0], placement.Y);
    }

    [Fact]
    public void TextPlacementOfTheLength()
    {
        // 5 clusters of 10 pixels stretched to 70: 5 pixels in each of the 4 gaps, none after the last cluster
        var text = new SceneText(0, 0, [new("ab"), new("cde")], new SceneFont(10), SceneColor.Black) { TextLength = 70 };
        var placement = TextPlacement.Place(text, [new(0, 20, 2, 0), new(1, 30, 3, 0)]);
        Assert.Equal(5, placement.GapSpacing);
        Assert.Equal(1, placement.LastItem);
        Assert.Equal([30.0, 40], placement.Width);
        Assert.Equal([0.0, 30], placement.X);
        Assert.Equal(1, placement.Scale);

        // Scaled with the glyphs, from the start; centered with the new length
        text = new SceneText(100, 0, "abcde", new SceneFont(10), SceneColor.Black) { TextLength = 100, LengthAdjust = SceneTextLengthAdjust.SpacingAndGlyphs, Anchor = SceneTextAnchor.Middle };
        placement = TextPlacement.Place(text, [new(-1, 50, 5, 0)]);
        Assert.Equal(2, placement.Scale);
        Assert.Equal([50.0], placement.X);
        Assert.Equal([100.0], placement.Width);

        // On a path, the glyphs are not scaled: the spacing changes
        text = new SceneText(0, 0, "ab", new SceneFont(10), SceneColor.Black) { TextLength = 30, LengthAdjust = SceneTextLengthAdjust.SpacingAndGlyphs, Path = new PathBuilder().MoveTo(0, 0).LineTo(100, 0).Build() };
        placement = TextPlacement.Place(text, [new(-1, 20, 2, 0)]);
        Assert.Equal(1, placement.Scale);
        Assert.Equal(10, placement.GapSpacing);

        // A length that is negative or not finite is ignored, as one cluster has no gap
        foreach (var length in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            placement = TextPlacement.Place(new SceneText(0, 0, "ab", new SceneFont(10), SceneColor.Black) { TextLength = length }, [new(-1, 20, 2, 0)]);
            Assert.Equal([20.0], placement.Width);
        }

        Assert.Equal([10.0], TextPlacement.Place(new SceneText(0, 0, "a", new SceneFont(10), SceneColor.Black) { TextLength = 50 }, [new(-1, 10, 1, 0)]).Width);

        // The measurers give the length of the text, and the extent of its moved spans
        var measurer = AfmTextMeasurer.Default;
        Assert.Equal(70, measurer.Measure(new SceneText(0, 0, "abc", new SceneFont(10), SceneColor.Black) { TextLength = 70 }).Width, 6);
        var width = measurer.Measure("a", new SceneFont(10)).Width + measurer.Measure("b", new SceneFont(10)).Width;
        Assert.Equal(width + 20, measurer.Measure(new SceneText(0, 0, [new("a"), new("b") { Dx = 20 }], new SceneFont(10), SceneColor.Black)).Width, 6);
    }

    [Theory]
    [InlineData(SceneTextBaseline.Alphabetic, 0)]
    [InlineData(SceneTextBaseline.Middle, 2.5)]
    [InlineData(SceneTextBaseline.Central, 3)]
    [InlineData(SceneTextBaseline.Hanging, 6.4)]
    [InlineData(SceneTextBaseline.Mathematical, 4)]
    [InlineData(SceneTextBaseline.Ideographic, -2)]
    [InlineData(SceneTextBaseline.TextTop, 8)]
    [InlineData(SceneTextBaseline.TextBottom, -2)]
    public void BaselineOffsets(SceneTextBaseline baseline, double expected)
    {
        // An ascent of 8, a descent of 2 and an x-height of 5
        Assert.Equal(expected, TextPlacement.GetBaselineOffset(baseline, 8, 2, 5), 9);
    }

    [Fact]
    public void BaselineOffsetWithoutXHeight()
    {
        // The x-height is half the ascent when the font does not give it
        Assert.Equal(2, TextPlacement.GetBaselineOffset(SceneTextBaseline.Middle, 8, 2, 0), 9);
    }

    [Fact]
    public void ReversedPaths()
    {
        // The segments are followed back: the control points of a curve are swapped, an arc turns the other way
        var path = new PathBuilder().MoveTo(0, 0).LineTo(10, 0).CubicTo(12, 1, 14, 2, 20, 5).QuadraticTo(25, 10, 30, 5).ArcTo(5, 5, 0, largeArc: false, sweep: true, 40, 5).Build();
        Assert.Equal("M40,5 A5,5 0 0,0 30,5 Q25,10 20,5 C14,2 12,1 10,0 L0,0", path.Reverse().ToString());
        Assert.Equal(path.ToString(), path.Reverse().Reverse().ToString());
        Assert.Equal(path.GetLength(), path.Reverse().GetLength(), 6);

        // The subpaths are in the reverse order; a closed subpath starts and closes at the end of its last segment
        var square = new PathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Close().MoveTo(20, 20).LineTo(30, 20).Build();
        Assert.Equal("M30,20 L20,20 M10,10 L10,0 L0,0 Z", square.Reverse().ToString());
        Assert.Equal(square.ToString(), square.Reverse().Reverse().ToString());
        Assert.Equal(square.GetLength(), square.Reverse().GetLength(), 6);

        // A path without a move starts at the origin, and a segment after a close starts at the start of the closed subpath
        Assert.Equal("M10,10 L0,0", new PathBuilder().LineTo(10, 10).Build().Reverse().ToString());
        Assert.Equal("M5,5 L0,0 M10,0 L0,0 Z", new PathBuilder().MoveTo(0, 0).LineTo(10, 0).Close().LineTo(5, 5).Build().Reverse().ToString());
        Assert.Equal("M1,1 M0,0", new PathBuilder().MoveTo(0, 0).MoveTo(1, 1).Build().Reverse().ToString());
        Assert.True(new PathBuilder().Build().Reverse().IsEmpty);
    }

    [Fact]
    public void EffectsAndGradientSpread()
    {
        #region VectorGraphics_Effects
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // A card with a drop shadow, and a blurred circle
        scene.Root.Add(new SceneRectangle(10, 10, 80, 50) { Fill = SceneColor.White, CornerRadius = 4, Filter = new SceneDropShadowFilter(offsetX: 0, offsetY: 2, standardDeviation: 3, (SceneColor)"#00000040") });
        scene.Root.Add(new SceneCircle(150, 50, 20) { Fill = (SceneColor)"#0369a1", Filter = new SceneBlurFilter(standardDeviation: 2) });

        // Stripes: a gradient of 10 pixels, reflected, and rotated by its transform
        var stripes = new LinearGradientPaint(0, 0, 10, 0, [new(0, SceneColor.White), new(1, (SceneColor)"#0369a1")])
        {
            Spread = SceneGradientSpread.Reflect,
            Transform = SceneTransform.Rotate(45),
        };
        scene.Root.Add(new SceneRectangle(10, 70, 180, 20) { Fill = stripes, BlendMode = SceneBlendMode.Multiply });

        // A group faded from left to right by a luminance mask
        var faded = scene.Root.Add(new SceneGroup { Mask = new SceneMask() });
        faded.Mask!.Content.Add(new SceneRectangle(100, 0, 100, 100) { Fill = new LinearGradientPaint(100, 0, 200, 0, [new(0, SceneColor.White), new(1, SceneColor.Black)]) });
        faded.Add(new SceneText(150, 95, "Fading", new SceneFont(12), SceneColor.Black) { Anchor = SceneTextAnchor.Middle });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("Effects", svg);

        Assert.Contains("<filter id=\"scene-filter0\" filterUnits=\"userSpaceOnUse\"", svg, StringComparison.Ordinal);
        Assert.Contains("<feGaussianBlur in=\"SourceAlpha\" stdDeviation=\"3\"/><feOffset dx=\"0\" dy=\"2\" result=\"shadow\"/><feFlood flood-color=\"#000000\" flood-opacity=\"0.251\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feGaussianBlur stdDeviation=\"2\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("filter=\"url(#scene-filter0)\"", svg, StringComparison.Ordinal);
        Assert.Contains("filter=\"url(#scene-filter1)\"", svg, StringComparison.Ordinal);
        Assert.Contains("spreadMethod=\"reflect\" gradientTransform=\"matrix(", svg, StringComparison.Ordinal);
        Assert.Contains("style=\"mix-blend-mode:multiply\"", svg, StringComparison.Ordinal);
        Assert.Contains("<mask id=\"scene-mask0\" maskUnits=\"userSpaceOnUse\"", svg, StringComparison.Ordinal);
        Assert.Contains("<g mask=\"url(#scene-mask0)\">", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Patterns()
    {
        #region VectorGraphics_Patterns
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // A tile of 10 by 10 pixels with a dot, repeated over the shapes it paints
        var dots = new PatternPaint(new SceneBounds(0, 0, 10, 10));
        dots.Content.Add(new SceneCircle(5, 5, 2) { Fill = (SceneColor)"#0369a1" });
        scene.Root.Add(new SceneRectangle(10, 10, 80, 80) { Fill = dots, Stroke = SceneColor.Black });

        // Hatches: a line in a tile, turned by the transform of the pattern
        var hatches = new PatternPaint(new SceneBounds(0, 0, 8, 8)) { Transform = SceneTransform.Rotate(45) };
        hatches.Content.Add(new SceneLine(0, 4, 8, 4) { Stroke = (SceneColor)"#b91c1c", StrokeWidth = 2 });
        scene.Root.Add(new SceneCircle(150, 50, 40) { Fill = hatches, Stroke = SceneColor.Black });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("Patterns", svg);
        Assert.Contains("<pattern id=\"scene-pattern0\" patternUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"10\" height=\"10\">\n<g>\n<circle cx=\"5\" cy=\"5\" r=\"2\" fill=\"#0369a1\"/>\n</g>\n</pattern>", svg, StringComparison.Ordinal);
        Assert.Contains("<pattern id=\"scene-pattern1\" patternUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"8\" height=\"8\" patternTransform=\"matrix(", svg, StringComparison.Ordinal);
        Assert.Contains("<rect x=\"10\" y=\"10\" width=\"80\" height=\"80\" fill=\"url(#scene-pattern0)\" stroke=\"#000000\"/>", svg, StringComparison.Ordinal);

        // A tile that does not start at the origin keeps the coordinates of its content; a pattern that paints with itself is written once
        var offset = new PatternPaint(new SceneBounds(5, 5, 10, 10));
        offset.Content.Add(new SceneRectangle(5, 5, 5, 5) { Fill = offset, Id = "inner" });
        var offsetSvg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 10, 10) { Stroke = offset }));
        Assert.Contains("<pattern id=\"scene-pattern0\" patternUnits=\"userSpaceOnUse\" x=\"5\" y=\"5\" width=\"10\" height=\"10\" viewBox=\"5 5 10 10\">\n<g>\n<rect id=\"inner\" x=\"5\" y=\"5\" width=\"5\" height=\"5\" fill=\"url(#scene-pattern0)\"/>", offsetSvg, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(offsetSvg, "<pattern "));
        Assert.Contains("stroke=\"url(#scene-pattern0)\"", offsetSvg, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectsOfTransformedGroupsAreInTheCoordinatesOfTheParent()
    {
        var scene = new Scene(100, 100, "sans-serif");
        var mask = new SceneMask { Mode = SceneMaskMode.Alpha };
        mask.Content.Add(new SceneCircle(50, 50, 40) { Fill = SceneColor.Black });
        var group = scene.Root.Add(new SceneGroup { Mask = mask, BlendMode = SceneBlendMode.ColorDodge, Filter = new SceneBlurFilter(1), Transform = SceneTransform.Translate(5, 5) });
        group.Add(new SceneLine(0, 10, 90, 10) { Stroke = SceneColor.Black });

        // A text has no known size: its filter region is relative to its bounding box
        scene.Root.Add(new SceneText(0, 50, "a", new SceneFont(10), SceneColor.Black) { Filter = new SceneBlurFilter(double.NaN) });
        var svg = SvgSceneRenderer.Render(scene);

        Assert.Contains("<g style=\"mix-blend-mode:color-dodge\">\n<g mask=\"url(#scene-mask0)\">\n<g filter=\"url(#scene-filter0)\">\n<g transform=\"translate(5 5)\">", svg, StringComparison.Ordinal);
        Assert.Contains("mask-type=\"alpha\"", svg, StringComparison.Ordinal);
        Assert.Contains("x=\"-50%\" y=\"-50%\" width=\"200%\" height=\"200%\"", svg, StringComparison.Ordinal);
        Assert.Contains("<feGaussianBlur stdDeviation=\"0\"/>", svg, StringComparison.Ordinal);

        // The line, translated, is 1 pixel thick: its region has a height
        Assert.Contains("<filter id=\"scene-filter0\" filterUnits=\"userSpaceOnUse\" x=\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("height=\"0\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TextSpans()
    {
        #region VectorGraphics_TextSpans
        var scene = new Scene(width: 300, height: 40, fontFamily: "sans-serif");
        var font = new SceneFont(16);

        // A text whose parts have their own font, color or lines: what a span does not set is the style of the text
        SceneTextSpan[] spans =
        [
            new("Total: "),
            new("42") { Font = font with { Weight = SceneFont.BoldWeight }, Color = (SceneColor)"#0369a1" },
            new(" items, "),
            new("3 removed") { Decorations = SceneTextDecorations.Strikethrough },
        ];
        var measurer = AfmTextMeasurer.Default;
        scene.Root.Add(new SceneText(10, 25, spans, font, SceneColor.Black) { MeasuredWidth = measurer.Measure(spans, font).Width });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("TextSpans", svg);

        Assert.Contains(">Total: <tspan font-weight=\"bold\" fill=\"#0369a1\">42</tspan> items, <tspan text-decoration=\"line-through\">3 removed</tspan></text>", svg, StringComparison.Ordinal);
        Assert.Equal("Total: 42 items, 3 removed", ((SceneText)scene.Root.Children[0]).Text);
        Assert.Equal(measurer.Measure("Total: ", font).Width + measurer.Measure("42", font with { Weight = SceneFont.BoldWeight }).Width + measurer.Measure(" items, 3 removed", font).Width, measurer.Measure(spans, font).Width, 0.5);

        // The lines of an underlined text are on each span, so a span can have none
        var underlined = new SceneText(0, 10, [new SceneTextSpan("a"), new SceneTextSpan("b") { Decorations = SceneTextDecorations.None, Font = new SceneFont(20, Weight: 300, Italic: true, Family: "Mono") }], new SceneFont(10), SceneColor.Black) { Decorations = SceneTextDecorations.Underline };
        var underlinedSvg = SvgSceneRenderer.Render(Single(underlined));
        Assert.Contains("font-size=\"10\" fill=\"#000000\"><tspan text-decoration=\"underline\">a</tspan><tspan font-family=\"Mono\" font-size=\"20\" font-weight=\"300\" font-style=\"italic\">b</tspan></text>", underlinedSvg, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>("spans", () => new SceneText(0, 0, [null!], new SceneFont(10), SceneColor.Black));
        Assert.Throws<ArgumentNullException>("text", () => new SceneTextSpan(null!));
    }

    [Fact]
    public void ColorMatrixFilters()
    {
        var scene = Single(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black, Filter = SceneColorMatrixFilter.Grayscale() });
        Assert.Contains("<feColorMatrix type=\"matrix\" values=\"0.2126 0.7152 0.0722 0 0 0.2126 0.7152 0.0722 0 0 0.2126 0.7152 0.0722 0 0 0 0 0 1 0\"/>", SvgSceneRenderer.Render(scene), StringComparison.Ordinal);

        // The functions of CSS: 0 keeps the colors
        double[] identity = [1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0];
        foreach (var filter in new[] { SceneColorMatrixFilter.Grayscale(0), SceneColorMatrixFilter.Sepia(0), SceneColorMatrixFilter.Saturate(1), SceneColorMatrixFilter.HueRotate(0), SceneColorMatrixFilter.Invert(0), SceneColorMatrixFilter.Brightness(1), SceneColorMatrixFilter.Contrast(1) })
        {
            Assert.Equal(identity, filter.Values.Select(value => Math.Round(value, 3, MidpointRounding.AwayFromZero)));
        }

        Assert.Equal([-1, 0, 0, 0, 1], SceneColorMatrixFilter.Invert().Values.Take(5));
        Assert.Throws<ArgumentException>("values", () => new SceneColorMatrixFilter(1, 2, 3));
        Assert.Throws<ArgumentException>("values", () => new SceneColorMatrixFilter([double.NaN, .. identity[1..]]));
    }

    [Fact]
    public void EmbeddedFontsIncludeTheSpansAndTheMasks()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");
        var fonts = SceneFontCollection.Load(Path.Combine(testData, "LiberationSans-Regular.woff2"), Path.Combine(testData, "LiberationSans-Bold.woff2"), Path.Combine(testData, "LiberationMono-Regular.woff2"));
        var scene = new Scene(100, 40, fonts.FontFamily) { Fonts = fonts };

        // Liberation Mono only draws a span, Liberation Sans Bold only a text of a mask
        scene.Root.Add(new SceneText(0, 20, [new SceneTextSpan("a"), new SceneTextSpan("b") { Font = new SceneFont(10, Family: "Liberation Mono") }], new SceneFont(10), SceneColor.Black));
        var mask = new SceneMask();
        mask.Content.Add(new SceneText(0, 30, "m", new SceneFont(10, Weight: SceneFont.BoldWeight), SceneColor.White));
        scene.Root.Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black, Mask = mask });
        var svg = SvgSceneRenderer.Render(scene, new SvgRenderOptions { EmbedFonts = true });

        Assert.Contains("font-family:&quot;Liberation Mono&quot;", svg, StringComparison.Ordinal);
        Assert.Contains("font-family:&quot;Liberation Sans&quot;;font-weight:700", svg, StringComparison.Ordinal);
        Assert.Contains("font-family:&quot;Liberation Sans&quot;;font-weight:400", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void OpaqueSpansOfTranslucentTextsAreOpaque()
    {
        var text = new SceneText(0, 10, [new SceneTextSpan("a"), new SceneTextSpan("b") { Color = (SceneColor)"#0000ff" }, new SceneTextSpan("c") { Color = (SceneColor)"#00ff0080" }], new SceneFont(10), (SceneColor)"#ff000080");
        var svg = SvgSceneRenderer.Render(Single(text));
        Assert.Contains("fill=\"#ff0000\" fill-opacity=\"0.502\"", svg, StringComparison.Ordinal);
        Assert.Contains("<tspan fill=\"#0000ff\" fill-opacity=\"1\">b</tspan>", svg, StringComparison.Ordinal);
        Assert.Contains("<tspan fill=\"#00ff00\" fill-opacity=\"0.502\">c</tspan>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void OpacityOfAGroupAppliesAfterItsFilter()
    {
        var scene = new Scene(100, 100, "sans-serif");
        var group = scene.Root.Add(new SceneGroup { Opacity = 0.5, Filter = SceneColorMatrixFilter.Invert(), Transform = SceneTransform.Translate(5, 5) });
        group.Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black });
        var plain = scene.Root.Add(new SceneGroup { Opacity = 0.5, Transform = SceneTransform.Translate(5, 5) });
        plain.Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black });
        var svg = SvgSceneRenderer.Render(scene);

        Assert.Contains("<g opacity=\"0.5\">\n<g filter=\"url(#scene-filter0)\">\n<g transform=\"translate(5 5)\">", svg, StringComparison.Ordinal);

        // Without a filter, the opacity stays on the group, as before
        Assert.Contains("<g opacity=\"0.5\" transform=\"translate(5 5)\">", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterRegionsIncludeTheFiltersOfTheChildren()
    {
        // The shadow of the child is 30 units right of it: the region of the filter of the group contains it
        var scene = new Scene(100, 100, "sans-serif");
        var group = scene.Root.Add(new SceneGroup { Filter = new SceneBlurFilter(0) });
        group.Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black, Filter = new SceneDropShadowFilter(30, 0, 0, SceneColor.Black) });
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Contains("<filter id=\"scene-filter0\" filterUnits=\"userSpaceOnUse\" x=\"-1\" y=\"-1\" width=\"42\" height=\"12\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void FamiliesTheFontsDoNotHaveAreNotWritten()
    {
        // The texts are measured with the families of the fonts: a family they do not have would draw them with other widths
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");
        var fonts = SceneFontCollection.Load(Path.Combine(testData, "LiberationSans-Regular.woff2"), Path.Combine(testData, "LiberationMono-Regular.woff2"));
        var scene = new Scene(100, 40, "Comic Sans MS, sans-serif") { Fonts = fonts };
        scene.Root.Add(new SceneText(0, 10, "a", new SceneFont(10, Family: "Courier New, 'Liberation Mono', monospace"), SceneColor.Black));
        var svg = SvgSceneRenderer.Render(scene);

        Assert.Contains("font-family=\"Liberation Sans, Liberation Mono, sans-serif\">", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Liberation Mono, Liberation Sans, monospace\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Comic", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Courier", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundsOfElements()
    {
        static SceneBounds? NoText(SceneText text) => null;

        // A stroke extends by half its width, a miter join by half the miter limit times the width
        Assert.Equal(new SceneBounds(9, 9, 22, 22), new SceneRectangle(10, 10, 20, 20) { Stroke = SceneColor.Black, StrokeWidth = 2, LineJoin = SceneLineJoin.Round }.GetBounds(NoText));
        Assert.Equal(new SceneBounds(6, 6, 28, 28), new SceneRectangle(10, 10, 20, 20) { Stroke = SceneColor.Black, StrokeWidth = 2 }.GetBounds(NoText));
        Assert.Equal(new SceneBounds(6, 6, 28, 28), new SceneRectangle(10, 10, 20, 20) { Stroke = SceneColor.Black, StrokeWidth = 2, MiterLimit = double.NaN }.GetBounds(NoText));
        Assert.Null(new SceneRectangle(0, 0, 10, 10) { Opacity = 0 }.GetBounds(NoText));

        // A group transforms and clips its children, and contains the shadow of a child, not its own filter
        var group = new SceneGroup { Transform = SceneTransform.Translate(100, 0), Clip = new SceneBounds(0, 0, 145, 100), Filter = new SceneBlurFilter(50) };
        group.Add(new SceneRectangle(0, 0, 10, 10) { Filter = new SceneDropShadowFilter(30, 0, 0, SceneColor.Black) });
        Assert.Equal(new SceneBounds(100, 0, 40, 10), group.GetBounds(NoText));
        group.Clip = new SceneBounds(0, 0, 120, 100);
        Assert.Equal(new SceneBounds(100, 0, 20, 10), group.GetBounds(NoText));

        // A text is measured by the renderer, then rotated around its anchor
        var text = new SceneText(0, 0, "a", new SceneFont(10), SceneColor.Black) { Rotation = 90 };
        var rotated = text.GetBounds(_ => new SceneBounds(0, -10, 20, 10))!.Value;
        Assert.Equal(0, rotated.X, 1e-9);
        Assert.Equal(0, rotated.Y, 1e-9);
        Assert.Equal(10, rotated.Width, 1e-9);
        Assert.Equal(20, rotated.Height, 1e-9);
        Assert.Throws<ArgumentNullException>("measureText", () => text.GetBounds(null!));
    }

    [Fact]
    public void FilterRegionsOfGroupsIgnoreTheirOwnClips()
    {
        // The clip of the group wraps its filtered output: the shadow, 60 units right of the rectangle, is in the clip
        var scene = new Scene(200, 100, "sans-serif");
        var group = scene.Root.Add(new SceneGroup { Clip = new SceneBounds(100, 0, 50, 50), Filter = new SceneDropShadowFilter(60, 0, 0, SceneColor.Black) });
        group.Add(new SceneRectangle(40, 10, 20, 20) { Fill = SceneColor.Black });
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Contains("<filter id=\"scene-filter0\" filterUnits=\"userSpaceOnUse\" x=\"-21\" y=\"-51\" width=\"142\" height=\"142\"", svg, StringComparison.Ordinal);
        Assert.Null(group.GetBounds(_ => null));
        Assert.Equal(new SceneBounds(40, 10, 20, 20), group.GetBounds(_ => null, applyClips: false));
    }

    [Fact]
    public void FilterGraph()
    {
        #region VectorGraphics_FilterGraph
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // A glow: the alpha of the shape, thickened, blurred and colored, under the shape
        var glow = new SceneFilterGraph(
        [
            new SceneMorphologyPrimitive(SceneMorphologyOperator.Dilate, radiusX: 2, radiusY: 2) { Input = SceneFilterInput.SourceAlpha },
            new SceneGaussianBlurPrimitive(standardDeviation: 4),
            new SceneFloodPrimitive((SceneColor)"#38bdf8"),
            new SceneCompositePrimitive(SceneCompositeOperator.In, SceneFilterInput.Result(1)),
            new SceneMergePrimitive(SceneFilterInput.Previous, SceneFilterInput.SourceGraphic),
        ]);
        scene.Root.Add(new SceneRectangle(30, 30, 60, 40) { Fill = (SceneColor)"#0369a1", CornerRadius = 6, Filter = glow });

        // A rough edge: the shape moved by noise, in a region of its own
        var rough = new SceneFilterGraph(
        [
            new SceneTurbulencePrimitive(SceneTurbulenceType.FractalNoise, baseFrequencyX: 0.05, baseFrequencyY: 0.05, octaves: 2, seed: 3),
            new SceneDisplacementMapPrimitive(scale: 10, map: SceneFilterInput.Previous)
            {
                Input = SceneFilterInput.SourceGraphic,
                XChannel = SceneColorChannel.Red,
                YChannel = SceneColorChannel.Green,
            },
        ])
        {
            Region = new SceneBounds(110, 10, 80, 80),
        };
        scene.Root.Add(new SceneCircle(150, 50, 30) { Fill = (SceneColor)"#b91c1c", Filter = rough });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("FilterGraph", svg);

        // The default region is the bounds of the shape enlarged by the reach of the primitives (2 + 3 times 4) and a pixel
        Assert.Contains("<filter id=\"scene-filter0\" filterUnits=\"userSpaceOnUse\" x=\"15\" y=\"15\" width=\"90\" height=\"70\" primitiveUnits=\"userSpaceOnUse\">", svg, StringComparison.Ordinal);
        Assert.Contains("<feMorphology in=\"SourceAlpha\" operator=\"dilate\" radius=\"2\"/><feGaussianBlur result=\"r1\" stdDeviation=\"4\"/><feFlood flood-color=\"#38bdf8\"/><feComposite in2=\"r1\" operator=\"in\"/><feMerge><feMergeNode/><feMergeNode in=\"SourceGraphic\"/></feMerge></filter>", svg, StringComparison.Ordinal);
        Assert.Contains("<filter id=\"scene-filter1\" filterUnits=\"userSpaceOnUse\" x=\"110\" y=\"10\" width=\"80\" height=\"80\" primitiveUnits=\"userSpaceOnUse\">", svg, StringComparison.Ordinal);
        Assert.Contains("<feTurbulence type=\"fractalNoise\" baseFrequency=\"0.05\" numOctaves=\"2\" seed=\"3\"/><feDisplacementMap in=\"SourceGraphic\" scale=\"10\" xChannelSelector=\"R\" yChannelSelector=\"G\"/>", svg, StringComparison.Ordinal);

        // A graph draws in its region: the bounds of the circle are its region, the ones of the rectangle enlarged by the reach
        Assert.Equal(new SceneBounds(16, 10, 174, 80), scene.Root.GetBounds(_ => null));
    }

    [Fact]
    public void FilterGraphPrimitives()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");
        var light = new SceneSpotLight(1, 2, 3, 4, 5, 6) { SpecularExponent = 2, LimitingConeAngle = 30 };
        var graph = new SceneFilterGraph(
        [
            new SceneGaussianBlurPrimitive(1, 2) { EdgeMode = SceneFilterEdgeMode.Wrap, ColorSpace = SceneFilterColorSpace.Srgb, Subregion = new SceneBounds(0, 0, 5, 5) },
            new SceneOffsetPrimitive(1.5, -2),
            new SceneFloodPrimitive((SceneColor)"#ff000080"),
            new SceneCompositePrimitive(SceneCompositeOperator.Arithmetic, SceneFilterInput.Result(0)) { K1 = 0.5, K2 = 1, K3 = -1, K4 = 0.25 },
            new SceneBlendPrimitive(SceneBlendMode.Multiply, SceneFilterInput.SourceGraphic),
            new SceneMorphologyPrimitive(SceneMorphologyOperator.Erode, 1, 2),
            new SceneTurbulencePrimitive(SceneTurbulenceType.Turbulence, 0.1, 0.2, 1, 7) { StitchTiles = true },
            new SceneConvolveMatrixPrimitive(3, 1, 1, 2, 1) { Divisor = 4, Bias = 0.5, TargetX = 0, TargetY = 0, EdgeMode = SceneFilterEdgeMode.None, PreserveAlpha = true },
            new SceneDiffuseLightingPrimitive(new SceneDistantLight(45, 30), (SceneColor)"#ffffff") { SurfaceScale = 2, DiffuseConstant = 0.5 },
            new SceneSpecularLightingPrimitive(light, (SceneColor)"#00ff00") { SpecularExponent = 20 },
            new SceneDiffuseLightingPrimitive(new ScenePointLight(1, 2, 3), SceneColor.White),
            new SceneComponentTransferPrimitive
            {
                Red = SceneTransferFunction.CreateTable(0, 1),
                Green = SceneTransferFunction.CreateDiscrete(0, 0.5),
                Blue = SceneTransferFunction.CreateLinear(2, 0.1),
                Alpha = SceneTransferFunction.CreateGamma(1, 2, 0),
            },
            new SceneTilePrimitive { Input = SceneFilterInput.Result(0) },
            new SceneColorMatrixPrimitive(SceneColorMatrixFilter.Invert()),
            SceneColorMatrixPrimitive.LuminanceToAlpha,
            new SceneDropShadowPrimitive(1, 2, 3, 3, (SceneColor)"#000000"),
            new SceneImagePrimitive(new SceneImage(0, 0, 4, 4, png)) { Subregion = new SceneBounds(0, 0, 4, 4) },
        ]);
        var svg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black, Filter = graph }));

        Assert.Contains("<feGaussianBlur x=\"0\" y=\"0\" width=\"5\" height=\"5\" color-interpolation-filters=\"sRGB\" result=\"r0\" stdDeviation=\"1 2\" edgeMode=\"wrap\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feOffset dx=\"1.5\" dy=\"-2\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feFlood flood-color=\"#ff0000\" flood-opacity=\"0.502\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feComposite in2=\"r0\" operator=\"arithmetic\" k1=\"0.5\" k2=\"1\" k3=\"-1\" k4=\"0.25\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feBlend in2=\"SourceGraphic\" mode=\"multiply\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feMorphology radius=\"1 2\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feTurbulence baseFrequency=\"0.1 0.2\" numOctaves=\"1\" seed=\"7\" stitchTiles=\"stitch\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feConvolveMatrix order=\"3 1\" kernelMatrix=\"1 2 1\" divisor=\"4\" bias=\"0.5\" targetX=\"0\" targetY=\"0\" edgeMode=\"none\" preserveAlpha=\"true\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feDiffuseLighting surfaceScale=\"2\" diffuseConstant=\"0.5\" lighting-color=\"#ffffff\"><feDistantLight azimuth=\"45\" elevation=\"30\"/></feDiffuseLighting>", svg, StringComparison.Ordinal);
        Assert.Contains("<feSpecularLighting surfaceScale=\"1\" specularConstant=\"1\" specularExponent=\"20\" lighting-color=\"#00ff00\"><feSpotLight x=\"1\" y=\"2\" z=\"3\" pointsAtX=\"4\" pointsAtY=\"5\" pointsAtZ=\"6\" specularExponent=\"2\" limitingConeAngle=\"30\"/></feSpecularLighting>", svg, StringComparison.Ordinal);
        Assert.Contains("<fePointLight x=\"1\" y=\"2\" z=\"3\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feComponentTransfer><feFuncR type=\"table\" tableValues=\"0 1\"/><feFuncG type=\"discrete\" tableValues=\"0 0.5\"/><feFuncB type=\"linear\" slope=\"2\" intercept=\"0.1\"/><feFuncA type=\"gamma\" amplitude=\"1\" exponent=\"2\" offset=\"0\"/></feComponentTransfer>", svg, StringComparison.Ordinal);
        Assert.Contains("<feTile in=\"r0\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feColorMatrix type=\"matrix\" values=\"-1 0 0 0 1", svg, StringComparison.Ordinal);
        Assert.Contains("<feColorMatrix type=\"matrix\" values=\"0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0.2125 0.7154 0.0721 0 0\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feDropShadow dx=\"1\" dy=\"2\" stdDeviation=\"3\" flood-color=\"#000000\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<feImage x=\"0\" y=\"0\" width=\"4\" height=\"4\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,", svg, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(svg, "<filter "));
    }

    [Fact]
    public void FilterGraphImagesReferToTheirElements()
    {
        // An element that is drawn only by an image primitive is written in the definitions, with an identifier
        var scene = new Scene(100, 100, "sans-serif");
        var star = new ScenePath(new PathBuilder().AddPolygon((50, 0), (60, 40), (100, 50), (60, 60), (50, 100), (40, 60), (0, 50), (40, 40)).Build()) { Fill = (SceneColor)"#facc15" };
        var graph = new SceneFilterGraph([new SceneImagePrimitive(star), new SceneMergePrimitive(SceneFilterInput.SourceGraphic, SceneFilterInput.Previous)]);
        scene.Root.Add(new SceneRectangle(10, 10, 80, 80) { Fill = SceneColor.Black, Filter = graph });

        // An image with a transform is an element too, and an element of the scene keeps its own identifier
        var named = scene.Root.Add(new SceneCircle(5, 5, 5) { Id = "dot", Fill = SceneColor.White });
        var transformed = new SceneImage(0, 0, 10, 10, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC")) { Transform = SceneTransform.Translate(1, 1) };
        scene.Root.Add(new SceneRectangle(0, 0, 10, 10) { Filter = new SceneFilterGraph([new SceneImagePrimitive(named), new SceneImagePrimitive(transformed)]) });
        var svg = SvgSceneRenderer.Render(scene);

        Assert.Contains("<feImage href=\"#scene-element0\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<defs>\n<path id=\"scene-element0\" d=\"", svg, StringComparison.Ordinal);
        Assert.Contains("<feImage href=\"#dot\"/><feImage href=\"#scene-element1\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<image id=\"scene-element1\" x=\"0\" y=\"0\"", svg, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(svg, "id=\"dot\""));

        // An element whose filter draws it again is written once
        var loop = new SceneGroup();
        var inner = loop.Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black });
        inner.Filter = new SceneFilterGraph([new SceneImagePrimitive(loop)]);
        var loopSvg = SvgSceneRenderer.Render(Single(loop));
        Assert.Equal(1, CountOccurrences(loopSvg, "<rect "));
    }

    [Fact]
    public void FilterGraphsValidateTheirPrimitives()
    {
        Assert.Throws<ArgumentException>("primitives", () => new SceneFilterGraph([]));
        Assert.Throws<ArgumentException>("primitives", () => new SceneFilterGraph([null!]));
        Assert.Throws<ArgumentException>("primitives", () => new SceneFilterGraph([new SceneOffsetPrimitive(0, 0) { Input = SceneFilterInput.Result(0) }]));
        Assert.Throws<ArgumentException>("primitives", () => new SceneFilterGraph([new SceneFloodPrimitive(SceneColor.Black), new SceneCompositePrimitive(SceneCompositeOperator.Over, SceneFilterInput.Result(1))]));
        Assert.Throws<ArgumentNullException>("primitives", () => new SceneFilterGraph(null!));
        Assert.Throws<ArgumentOutOfRangeException>("index", () => SceneFilterInput.Result(-1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SceneFilterGraph([new SceneFloodPrimitive(SceneColor.Black)]) { Region = new SceneBounds(0, 0, -1, 1) });
        Assert.Throws<ArgumentException>("value", () => new SceneFloodPrimitive(SceneColor.Black) { Subregion = new SceneBounds(double.NaN, 0, 1, 1) });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SceneFloodPrimitive(SceneColor.Black) { ColorSpace = (SceneFilterColorSpace)2 });
        Assert.Throws<ArgumentOutOfRangeException>("standardDeviationX", () => new SceneGaussianBlurPrimitive(-1));
        Assert.Throws<ArgumentException>("offsetY", () => new SceneOffsetPrimitive(0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>("operator", () => new SceneCompositePrimitive((SceneCompositeOperator)(-1), default));
        Assert.Throws<ArgumentException>("value", () => new SceneCompositePrimitive(SceneCompositeOperator.Arithmetic, default) { K1 = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>("mode", () => new SceneBlendPrimitive((SceneBlendMode)99, default));
        Assert.Throws<ArgumentOutOfRangeException>("radiusY", () => new SceneMorphologyPrimitive(SceneMorphologyOperator.Erode, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>("octaves", () => new SceneTurbulencePrimitive(SceneTurbulenceType.Turbulence, 0.1, 0.1, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SceneDisplacementMapPrimitive(1, default) { XChannel = (SceneColorChannel)4 });
        Assert.Throws<ArgumentOutOfRangeException>("orderX", () => new SceneConvolveMatrixPrimitive(0, 1, []));
        Assert.Throws<ArgumentException>("kernel", () => new SceneConvolveMatrixPrimitive(2, 2, 1, 2, 3));
        Assert.Throws<ArgumentException>("value", () => new SceneConvolveMatrixPrimitive(1, 1, 1) { Divisor = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SceneConvolveMatrixPrimitive(2, 1, 1, 1) { TargetX = 2 });
        Assert.Throws<ArgumentNullException>("light", () => new SceneDiffuseLightingPrimitive(null!, SceneColor.White));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SceneSpecularLightingPrimitive(new SceneDistantLight(0, 0), SceneColor.White) { SpecularExponent = 0.5 });
        Assert.Throws<ArgumentException>("z", () => new ScenePointLight(0, 0, double.NaN));
        Assert.Throws<ArgumentNullException>("value", () => new SceneComponentTransferPrimitive { Red = null! });
        Assert.Throws<ArgumentException>("values", () => SceneTransferFunction.CreateTable(double.NaN));
        Assert.Throws<ArgumentException>("values", () => new SceneColorMatrixPrimitive(1, 2));
        Assert.Throws<ArgumentNullException>("content", () => new SceneImagePrimitive(null!));

        // The default region is the bounds enlarged by the sum of the reaches of the primitives
        var graph = new SceneFilterGraph([new SceneGaussianBlurPrimitive(1), new SceneOffsetPrimitive(3, -4), new SceneDropShadowPrimitive(1, 0, 0, 0, SceneColor.Black)]);
        Assert.Equal(new SceneBounds(-8, -8, 26, 26), graph.GetRegion(new SceneBounds(0, 0, 10, 10)));
        Assert.Equal(new SceneBounds(-8, -8, 26, 26), Single(new SceneRectangle(0, 0, 10, 10) { Filter = graph }).Root.GetBounds(_ => null));
        Assert.Equal(SceneColorChannel.Alpha, new SceneDisplacementMapPrimitive(1, default).XChannel);
        Assert.Equal(SceneFilterEdgeMode.Duplicate, new SceneConvolveMatrixPrimitive(1, 1, 1).EdgeMode);
        Assert.Equal(SceneFilterColorSpace.LinearRgb, new SceneTilePrimitive().ColorSpace);
    }

    private static int CountOccurrences(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    private static Scene Single(SceneElement element)
    {
        var scene = new Scene(10, 10, "sans-serif");
        scene.Root.Add(element);
        return scene;
    }

    [Fact]
    public void PathLengthAndPoints()
    {
        #region VectorGraphics_PathMeasure
        var path = new PathBuilder().MoveTo(0, 0).LineTo(30, 40).LineTo(30, 100).Build();

        // The length of the path, and a point at a distance along it with the direction of the path there
        Assert.Equal(110, path.GetLength(), 1e-9);
        var point = path.GetPointAtLength(80);
        Assert.Equal(30, point.X, 1e-9);
        Assert.Equal(70, point.Y, 1e-9);
        Assert.Equal(90, point.Angle, 1e-9); // Down: the y axis points down
        #endregion

        // At the end of a segment, the direction of the next one; beyond the ends, the ends
        Assert.Equal(90, path.GetPointAtLength(50).Angle, 1e-9);
        Assert.Equal(new PathPoint(0, 0, path.GetPointAtLength(0).Angle), path.GetPointAtLength(-10));
        Assert.Equal(30, path.GetPointAtLength(1000).X, 1e-9);
        Assert.Equal(100, path.GetPointAtLength(1000).Y, 1e-9);
        Assert.Equal(0, path.GetPointAtLength(double.NaN).X, 1e-9);

        // A closed rectangle goes back to its start; the moves between subpaths have no length
        Assert.Equal(40, new PathBuilder().AddRectangle(0, 0, 10, 10).Build().GetLength(), 1e-9);
        Assert.Equal(20, new PathBuilder().MoveTo(0, 0).LineTo(10, 0).MoveTo(100, 100).LineTo(100, 110).Build().GetLength(), 1e-9);
        Assert.Equal(new PathPoint(100, 105, 90), new PathBuilder().MoveTo(0, 0).LineTo(10, 0).MoveTo(100, 100).LineTo(100, 110).Build().GetPointAtLength(15));

        // The curves are measured by their chords
        Assert.Equal(2 * Math.PI * 10, new PathBuilder().AddCircle(0, 0, 10).Build().GetLength(), 0.05);
        var top = new PathBuilder().MoveTo(0, 0).ArcTo(10, 10, 0, largeArc: false, sweep: true, 20, 0).Build();
        var middle = top.GetPointAtLength(top.GetLength() / 2);
        Assert.Equal(10, middle.X, 0.01);
        Assert.Equal(-10, middle.Y, 0.01);
        Assert.Equal(0, middle.Angle, 0.5);

        // A path without length gives its first point; an empty path has none
        Assert.Equal(new PathPoint(5, 6, 0), new PathBuilder().MoveTo(5, 6).Build().GetPointAtLength(1));
        Assert.Equal(0, new PathBuilder().Build().GetLength());
        Assert.Throws<InvalidOperationException>(() => new PathBuilder().Build().GetPointAtLength(0));
        Assert.Equal(10, new PathBuilder().MoveTo(0, 0).LineTo(double.NaN, 0).LineTo(10, 0).LineTo(20, 0).Build().GetLength(), 1e-9);
    }

    [Fact]
    public void Markers()
    {
        #region VectorGraphics_Markers
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // An arrowhead whose tip, at (10, 5), is on the vertex: it follows the direction of the path, scaled by the stroke width
        var arrow = new SceneMarker { RefX = 10, RefY = 5 };
        arrow.Content.Add(new ScenePath(new PathBuilder().AddPolygon((0, 0), (10, 5), (0, 10)).Build()) { Fill = (SceneColor)"#0369a1" });

        // A dot on every vertex between the ends, in the coordinates of the path
        var dot = new SceneMarker { Units = SceneMarkerUnits.UserSpace };
        dot.Content.Add(new SceneCircle(0, 0, 3) { Fill = (SceneColor)"#0369a1" });

        var path = new PathBuilder().MoveTo(10, 80).LineTo(60, 20).LineTo(110, 80).LineTo(170, 30).Build();
        scene.Root.Add(new ScenePath(path) { Stroke = (SceneColor)"#0369a1", StrokeWidth = 2, MarkerMid = dot, MarkerEnd = arrow });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("Markers", svg);
        Assert.Contains("<marker id=\"scene-marker0\" markerUnits=\"userSpaceOnUse\" orient=\"auto\" overflow=\"visible\">\n<g>\n<circle cx=\"0\" cy=\"0\" r=\"3\"", svg, StringComparison.Ordinal);
        Assert.Contains("<marker id=\"scene-marker1\" refX=\"10\" refY=\"5\" orient=\"auto\" overflow=\"visible\">", svg, StringComparison.Ordinal);
        Assert.Contains("stroke-width=\"2\" marker-mid=\"url(#scene-marker0)\" marker-end=\"url(#scene-marker1)\"/>", svg, StringComparison.Ordinal);

        // A line has a start and an end; the other orientations
        var reversed = new SceneMarker { Orientation = SceneMarkerOrientation.AutoStartReverse };
        var fixedAngle = new SceneMarker { Orientation = SceneMarkerOrientation.Angle, Angle = 30 };
        var lineSvg = SvgSceneRenderer.Render(Single(new SceneLine(0, 0, 10, 0) { Stroke = SceneColor.Black, MarkerStart = reversed, MarkerEnd = fixedAngle }));
        Assert.Contains("orient=\"auto-start-reverse\"", lineSvg, StringComparison.Ordinal);
        Assert.Contains("orient=\"30\"", lineSvg, StringComparison.Ordinal);
        Assert.Contains("marker-start=\"url(#scene-marker0)\" marker-end=\"url(#scene-marker1)\"", lineSvg, StringComparison.Ordinal);

        // The bounds of a shape contain its markers: the arrowhead is 20 by 20 pixels with a stroke of 2, before the vertex
        Assert.Equal(new SceneBounds(-4, 40, 108, 20), new SceneLine(0, 50, 100, 50) { Stroke = SceneColor.Black, MarkerEnd = arrow, StrokeWidth = 2 }.GetBounds(_ => null));

        // A marker drawn on a shape of its own content is not drawn again
        var recursive = new SceneMarker();
        recursive.Content.Add(new SceneLine(0, 0, 1, 0) { Stroke = SceneColor.Black, MarkerEnd = recursive });
        Assert.Equal(1, CountOccurrences(SvgSceneRenderer.Render(Single(new SceneLine(0, 0, 1, 0) { MarkerEnd = recursive })), "<marker "));
        Assert.NotNull(new SceneLine(0, 0, 1, 0) { MarkerEnd = recursive }.GetBounds(_ => null));
    }

    [Fact]
    public void PathVertices()
    {
        // The end points of the commands; between two segments, the direction halfway between them
        var polyline = new PathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Build();
        Assert.Equal([new PathPoint(0, 0, 0), new PathPoint(10, 0, 45), new PathPoint(10, 10, 90)], polyline.GetVertices());

        // A closed subpath joins its last segment and its first one, at its start and where it closes
        var square = new PathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).LineTo(0, 10).Close().Build();
        Assert.Equal([new PathPoint(0, 0, -45), new PathPoint(10, 0, 45), new PathPoint(10, 10, 135), new PathPoint(0, 10, -135), new PathPoint(0, 0, -45)], square.GetVertices());

        // The tangents of an arc: a half circle through the top goes up, then down
        var arc = new PathBuilder().MoveTo(0, 0).ArcTo(5, 5, 0, largeArc: false, sweep: true, 10, 0).Build().GetVertices();
        Assert.Equal(2, arc.Count);
        Assert.Equal(-90, arc[0].Angle, 1e-6);
        Assert.Equal(90, arc[1].Angle, 1e-6);

        // The tangents of curves, and a segment without length that keeps the previous direction
        var curve = new PathBuilder().MoveTo(0, 0).CubicTo(0, 10, 10, 10, 10, 0).LineTo(10, 0).QuadraticTo(20, 0, 20, 10).Build().GetVertices();
        Assert.Equal([90, -45, -45, 90], curve.Select(vertex => Math.Round(vertex.Angle, 6, MidpointRounding.AwayFromZero)));

        // The segments without length at the start of a subpath take the direction of the first segment that has one
        Assert.Equal([90, 90, 90], new PathBuilder().MoveTo(0, 0).LineTo(0, 0).LineTo(0, 10).Build().GetVertices().Select(vertex => vertex.Angle));

        // Without a move, the path starts at the origin; an empty path has no vertex
        Assert.Equal([new PathPoint(0, 0, 0), new PathPoint(5, 0, 0)], new PathBuilder().LineTo(5, 0).Build().GetVertices());
        Assert.Empty(new PathBuilder().Build().GetVertices());

        // The transform of a marker: its reference point on the vertex, scaled by the stroke, turned to the direction
        var marker = new SceneMarker { RefX = 1, RefY = 2 };
        var transform = marker.GetTransform(new PathPoint(10, 20, 90), strokeWidth: 3);
        var (x, y) = transform.Apply(1, 2);
        Assert.Equal(10, x, 1e-9);
        Assert.Equal(20, y, 1e-9);
        (x, y) = transform.Apply(2, 2);
        Assert.Equal(10, x, 1e-9);
        Assert.Equal(23, y, 1e-9);
        Assert.Equal(new SceneTransform(1, 0, 0, 1, 10, 20), new SceneMarker { Orientation = SceneMarkerOrientation.Angle, Angle = double.NaN, Units = SceneMarkerUnits.UserSpace }.GetTransform(new PathPoint(10, 20, 45), 3));
        (x, y) = new SceneMarker { Orientation = SceneMarkerOrientation.AutoStartReverse }.GetTransform(new PathPoint(0, 0, 0), 1, isStart: true).Apply(1, 0);
        Assert.Equal(-1, x, 1e-9);
    }

    [Fact]
    public void CurvesThatDoubleBackAreMeasuredAlongTheCurve()
    {
        // The control points are on the chord, beyond its ends: the curve goes to x = 29, back to x = -24, then to x = 10
        var path = new PathBuilder().MoveTo(0, 0).CubicTo(100, 0, -100, 0, 10, 0).Build();
        Assert.InRange(path.GetLength(), 116, 116.3);
        Assert.InRange(path.GetPointAtLength(25).X, 24, 26);
    }

    [Fact]
    public void ReusedElementsInsideOtherReusedElementsAreWrittenOnce()
    {
        // A group that is not in the scene, and its rectangle used before it: the rectangle is written with the group
        var group = new SceneGroup();
        var rectangle = group.Add(new SceneRectangle(0, 0, 5, 5) { Id = "square", Fill = SceneColor.Black });
        var scene = new Scene(10, 10, "sans-serif");
        scene.Root.Add(new SceneUse(rectangle));
        scene.Root.Add(new SceneUse(group));
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Equal(1, CountOccurrences(svg, "id=\"square\""));
        Assert.Equal(1, CountOccurrences(svg, "id=\"scene-element0\""));
        Assert.Contains("<defs>\n<g id=\"scene-element0\">\n<rect id=\"square\"", svg, StringComparison.Ordinal);

        // A view box that draws nothing keeps, in definitions, the children a use draws
        var hidden = new SceneGroup { ViewBox = new SceneViewBox(new SceneBounds(0, 0, 0, 0), new SceneBounds(0, 0, 10, 10)) };
        var circle = hidden.Add(new SceneCircle(5, 5, 5) { Fill = SceneColor.Black });
        var withHidden = new Scene(10, 10, "sans-serif");
        withHidden.Root.Add(hidden);
        withHidden.Root.Add(new SceneUse(circle));
        Assert.Contains("<g>\n<defs>\n<circle id=\"scene-element0\"", SvgSceneRenderer.Render(withHidden), StringComparison.Ordinal);

        // The contents of two patterns that paint with each other are written once each, and both uses refer to them
        var first = new PatternPaint(new SceneBounds(0, 0, 4, 4));
        var second = new PatternPaint(new SceneBounds(0, 0, 4, 4));
        first.Content.Add(new SceneRectangle(0, 0, 2, 2) { Fill = second, Id = "first-square" });
        second.Content.Add(new SceneRectangle(0, 0, 2, 2) { Fill = first, Id = "second-square" });
        var mutual = new Scene(10, 10, "sans-serif");
        mutual.Root.Add(new SceneUse(first.Content));
        mutual.Root.Add(new SceneUse(second.Content));
        var mutualSvg = SvgSceneRenderer.Render(mutual);
        Assert.Contains("<use href=\"#scene-element0\"/>", mutualSvg, StringComparison.Ordinal);
        Assert.Contains("<use href=\"#scene-element1\"/>", mutualSvg, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(mutualSvg, "id=\"scene-element0\""));
        Assert.Equal(1, CountOccurrences(mutualSvg, "id=\"scene-element1\""));
        Assert.Equal(1, CountOccurrences(mutualSvg, "id=\"first-square\""));
        Assert.Equal(1, CountOccurrences(mutualSvg, "id=\"second-square\""));

        // The same element twice in such a content is written twice: its identifier is a duplicate
        var square = new SceneRectangle(0, 0, 1, 1) { Id = "twice", Fill = SceneColor.Black };
        first.Content.Add(square);
        first.Content.Add(square);
        Assert.Throws<InvalidOperationException>(() => SvgSceneRenderer.Render(mutual));

        // A use that refers to a use in its own content refers to an empty group
        var cycle = new SceneGroup();
        var circular = cycle.Add(new SceneUse(cycle));
        var withCircular = new Scene(10, 10, "sans-serif");
        withCircular.Root.Add(cycle);
        withCircular.Root.Add(new SceneUse(circular));
        var circularSvg = SvgSceneRenderer.Render(withCircular);
        Assert.Contains("<g id=\"scene-element0\"/>", circularSvg, StringComparison.Ordinal);
        Assert.Contains("<use href=\"#scene-element0\"/>", circularSvg, StringComparison.Ordinal);
    }

    [Fact]
    public void TextOnAPath()
    {
        #region VectorGraphics_TextOnPath
        var scene = new Scene(width: 200, height: 110, fontFamily: "sans-serif");

        // The text follows the arc, centered on its middle
        var arc = new PathBuilder().MoveTo(20, 105).ArcTo(80, 80, 0, largeArc: false, sweep: true, 180, 105).Build();
        scene.Root.Add(new ScenePath(arc) { Stroke = (SceneColor)"#cbd5e1" });
        scene.Root.Add(new SceneText(0, 0, "Text along a path", new SceneFont(16), SceneColor.Black)
        {
            Path = arc,
            PathStartOffset = arc.GetLength() / 2,
            Anchor = SceneTextAnchor.Middle,
        });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("TextOnPath", svg);
        Assert.Contains("<path id=\"scene-textpath0\" d=\"M20,105 A80,80 0 0,1 180,105\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<text text-anchor=\"middle\" font-size=\"16\"", svg, StringComparison.Ordinal);
        Assert.Contains("><textPath href=\"#scene-textpath0\" startOffset=\"", svg, StringComparison.Ordinal);
        Assert.Contains("\">Text along a path</textPath></text>", svg, StringComparison.Ordinal);

        // The bounds of a text on a path are the bounds of the path, enlarged by the size of its font
        var text = new SceneText(0, 0, "a", new SceneFont(10), SceneColor.Black) { Path = new PathBuilder().MoveTo(0, 50).LineTo(100, 50).Build() };
        Assert.Equal(new SceneBounds(-10, 40, 120, 20), text.GetBounds(_ => throw new InvalidOperationException()));
    }

    [Fact]
    public void ToCubicCurves()
    {
        // A quadratic curve becomes the cubic curve of the same shape; the other commands are kept
        Assert.Equal("M0,0 C2,4 4,4 6,0 L6,6 Z", new PathBuilder().MoveTo(0, 0).QuadraticTo(3, 6, 6, 0).LineTo(6, 6).Close().Build().ToCubicCurves().ToString());

        // A half circle of radius 5 is two quarters; with the sweep flag, it goes through the top (the y axis points down)
        var half = new PathBuilder().MoveTo(0, 0).ArcTo(5, 5, 0, largeArc: false, sweep: true, 10, 0).Build().ToCubicCurves();
        Assert.Equal([PathCommandKind.MoveTo, PathCommandKind.CubicTo, PathCommandKind.CubicTo], half.Commands.Select(command => command.Kind));
        Assert.Equal(5, half.Commands[1].X, 1e-9);
        Assert.Equal(-5, half.Commands[1].Y, 1e-9);
        Assert.Equal((10.0, 0.0), (half.Commands[2].X, half.Commands[2].Y));
        Assert.Equal(5, new PathBuilder().MoveTo(0, 0).ArcTo(5, 5, 0, largeArc: false, sweep: false, 10, 0).Build().ToCubicCurves().Commands[1].Y, 1e-9);

        // Radii too small are scaled up, a radius of 0 gives a line, and an arc that ends where it starts is omitted
        Assert.Equal(-5, new PathBuilder().MoveTo(0, 0).ArcTo(1, 1, 0, largeArc: false, sweep: true, 10, 0).Build().ToCubicCurves().Commands[1].Y, 1e-9);
        Assert.Equal("M0,0 L10,0", new PathBuilder().MoveTo(0, 0).ArcTo(0, 5, 0, largeArc: false, sweep: true, 10, 0).Build().ToCubicCurves().ToString());
        Assert.Equal("M1,1", new PathBuilder().MoveTo(1, 1).ArcTo(5, 5, 0, largeArc: true, sweep: true, 1, 1).Build().ToCubicCurves().ToString());

        // A large arc of three quarters of a circle is three curves
        Assert.HasCount(4, new PathBuilder().MoveTo(5, 0).ArcTo(5, 5, 0, largeArc: true, sweep: true, 0, 5).Build().ToCubicCurves().Commands);
    }

    [Fact]
    public void Transforms()
    {
        #region VectorGraphics_Transforms
        // Scale by 2 around the origin, then move by (10, 20)
        var transform = SceneTransform.Scale(2, 2).Then(SceneTransform.Translate(10, 20));
        Assert.Equal((16.0, 28.0), transform.Apply(3, 4));
        Assert.Equal(new SceneTransform(2, 0, 0, 2, 10, 20), transform);
        #endregion

        // A scale around a point keeps the point in place
        Assert.Equal((5.0, 5.0), SceneTransform.Scale(3, 4, 5, 5).Apply(5, 5));

        // Then applies the transforms in order
        var rotate = SceneTransform.Rotate(90);
        var translate = SceneTransform.Translate(1, 0);
        var (x, y) = rotate.Then(translate).Apply(1, 0);
        Assert.Equal(1.0, x, 1e-12);
        Assert.Equal(1.0, y, 1e-12);
        (x, y) = translate.Then(rotate).Apply(1, 0);
        Assert.Equal(0.0, x, 1e-12);
        Assert.Equal(2.0, y, 1e-12);

        Assert.Equal(rotate, rotate.Then(SceneTransform.Identity));
        Assert.Equal(rotate, SceneTransform.Identity.Then(rotate));
    }

    [Fact]
    public void ItalicTextAndHyperlinks()
    {
        var scene = new Scene(100, 50, "sans-serif");
        var group = scene.Root.Add(new SceneGroup { Hyperlink = "https://example.com/?a=1&b=2" });
        group.Add(new SceneText(10, 20, "Italic", new SceneFont(12, Italic: true), SceneColor.Black));
        group.Add(new SceneText(10, 40, "Both", new SceneFont(12, Weight: SceneFont.BoldWeight, Italic: true), SceneColor.Black));
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Equal(
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="50" viewBox="0 0 100 50" role="img" font-family="sans-serif">
            <a href="https://example.com/?a=1&amp;b=2">
            <g>
            <text x="10" y="20" font-size="12" font-style="italic" fill="#000000">Italic</text>
            <text x="10" y="40" font-size="12" font-weight="bold" font-style="italic" fill="#000000">Both</text>
            </g>
            </a>
            </svg>

            """.ReplaceLineEndings("\n"),
            svg);
    }

    [Fact]
    public void TransformsAndHyperlinksOfElements()
    {
        #region VectorGraphics_ElementTransformAndLink
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // Any element has a transform and a hyperlink, without a group around it
        scene.Root.Add(new SceneRectangle(0, 0, 40, 20)
        {
            Fill = (SceneColor)"#0369a1",
            Transform = SceneTransform.Rotate(30).Then(SceneTransform.Translate(20, 10)),
            Hyperlink = "https://example.com/",
        });

        // The rotation of a text applies before its transform
        scene.Root.Add(new SceneText(0, 0, "Moved", new SceneFont(12), SceneColor.Black) { Rotation = -90, Transform = SceneTransform.Translate(150, 90) });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        Assert.Equal(
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 200 100" role="img" font-family="sans-serif">
            <a href="https://example.com/">
            <rect x="0" y="0" width="40" height="20" fill="#0369a1" transform="matrix(0.866025 0.5 -0.5 0.866025 20 10)"/>
            </a>
            <text x="0" y="0" font-size="12" fill="#000000" transform="translate(150 90) rotate(-90 0 0)">Moved</text>
            </svg>

            """.ReplaceLineEndings("\n"),
            svg);

        // The mask and the filter of a transformed element are in the coordinates of its parent: a group around it has them
        var effects = new Scene(100, 100, "sans-serif");
        var mask = new SceneMask();
        mask.Content.Add(new SceneRectangle(0, 0, 50, 100) { Fill = SceneColor.White });
        effects.Root.Add(new SceneCircle(0, 0, 10) { Fill = SceneColor.Black, Opacity = 0.5, Mask = mask, Filter = new SceneBlurFilter(2), Transform = SceneTransform.Translate(50, 50) });
        Assert.Contains("<g opacity=\"0.5\" filter=\"url(#scene-filter0)\" mask=\"url(#scene-mask0)\">\n<circle cx=\"0\" cy=\"0\" r=\"10\" fill=\"#000000\" transform=\"translate(50 50)\"/>\n</g>", SvgSceneRenderer.Render(effects), StringComparison.Ordinal);

        // The bounds of an element are in the coordinates of its parent
        Assert.Equal(new SceneBounds(10, 20, 4, 2), new SceneRectangle(0, 0, 2, 1) { Transform = SceneTransform.Scale(2, 2).Then(SceneTransform.Translate(10, 20)) }.GetBounds(_ => null));
        Assert.Equal(new SceneBounds(150, 80, 10, 10), new SceneText(0, 0, "a", new SceneFont(10), SceneColor.Black) { Transform = SceneTransform.Translate(150, 90) }.GetBounds(_ => new SceneBounds(0, -10, 10, 10)));
    }

    [Fact]
    public void IdentifiersOfElements()
    {
        #region VectorGraphics_Ids
        var scene = new Scene(width: 100, height: 50, fontFamily: "sans-serif");

        // The identifier of an element is its id attribute: a script or a style sheet finds the element with it
        scene.Root.Add(new SceneRectangle(10, 10, 30, 30) { Id = "north", Fill = SceneColor.Black });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        Assert.Contains("<rect id=\"north\" x=\"10\"", svg, StringComparison.Ordinal);

        // The identifier is on the outermost element written for it: the link, the groups of the effects
        var linked = new Scene(100, 50, "sans-serif");
        linked.Root.Add(new SceneCircle(5, 5, 5) { Id = "linked", Hyperlink = "https://example.com/", Fill = SceneColor.Black });
        var masked = linked.Root.Add(new SceneGroup { Id = "masked", Mask = new SceneMask(), Transform = SceneTransform.Translate(1, 1) });
        masked.Mask!.Content.Add(new SceneRectangle(0, 0, 10, 10) { Id = "mask-content", Fill = SceneColor.White });
        masked.Add(new SceneRectangle(0, 0, 10, 10) { Id = "child", Fill = SceneColor.Black });
        var linkedSvg = SvgSceneRenderer.Render(linked);
        Assert.Contains("<a id=\"linked\" href=\"https://example.com/\">\n<circle cx=", linkedSvg, StringComparison.Ordinal);
        Assert.Contains("<g id=\"masked\" mask=\"url(#scene-mask0)\">\n<g transform=\"translate(1 1)\">\n<rect id=\"child\"", linkedSvg, StringComparison.Ordinal);
        Assert.Contains("<rect id=\"mask-content\"", linkedSvg, StringComparison.Ordinal);

        // The generated identifiers skip the identifiers of the elements
        var clipped = new Scene(10, 10, "sans-serif");
        clipped.Root.Add(new SceneGroup { Id = "scene-clip0", Clip = new SceneBounds(0, 0, 5, 5) });
        Assert.Contains("<clipPath id=\"scene-clip1\">", SvgSceneRenderer.Render(clipped), StringComparison.Ordinal);

        // Two elements cannot have the same identifier, even in the content of a mask
        var duplicate = new Scene(10, 10, "sans-serif");
        duplicate.Root.Add(new SceneRectangle(0, 0, 1, 1) { Id = "a" });
        var duplicateMask = new SceneMask();
        duplicateMask.Content.Add(new SceneRectangle(0, 0, 1, 1) { Id = "a" });
        duplicate.Root.Add(new SceneRectangle(0, 0, 1, 1) { Mask = duplicateMask });
        Assert.Throws<InvalidOperationException>(() => SvgSceneRenderer.Render(duplicate));

        Assert.True(SceneElement.IsValidId("a-b_c.1"));
        Assert.False(SceneElement.IsValidId("1a"));
        Assert.False(SceneElement.IsValidId("a b"));
        Assert.False(SceneElement.IsValidId("é"));
        Assert.False(SceneElement.IsValidId(""));
        Assert.False(SceneElement.IsValidId(null));
        Assert.Throws<ArgumentException>("value", () => new SceneRectangle(0, 0, 1, 1) { Id = "a\"b" });
    }

    [Fact]
    public void ReusedElements()
    {
        #region VectorGraphics_Reuse
        var scene = new Scene(width: 200, height: 60, fontFamily: "sans-serif");

        // A star that is not in the scene: the SVG writes it once in its definitions, and each use refers to it
        var star = new ScenePath(new PathBuilder().AddPolygon((0, -20), (6, -6), (20, -6), (9, 3), (13, 18), (0, 9), (-13, 18), (-9, 3), (-20, -6), (-6, -6)).Build()) { Fill = (SceneColor)"#eab308" };
        for (var i = 0; i < 3; i++)
        {
            scene.Root.Add(new SceneUse(star) { Transform = SceneTransform.Translate(30 + (i * 50), 30) });
        }

        // An element of the scene drawn again, smaller
        var circle = scene.Root.Add(new SceneCircle(0, 0, 10) { Id = "dot", Fill = (SceneColor)"#0369a1", Transform = SceneTransform.Translate(180, 15) });
        scene.Root.Add(new SceneUse(circle) { Transform = SceneTransform.Scale(0.5, 0.5, 180, 45) });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("Reuse", svg);
        Assert.Contains("<defs>\n<path id=\"scene-element0\" d=\"M0,-20", svg, StringComparison.Ordinal);
        Assert.Contains("<use href=\"#scene-element0\" transform=\"translate(80 30)\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<circle id=\"dot\" cx=\"0\" cy=\"0\" r=\"10\" fill=\"#0369a1\" transform=\"translate(180 15)\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<use href=\"#dot\" transform=\"matrix(0.5 0 0 0.5 90 22.5)\"/>", svg, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(svg, "<path "));

        // The bounds of a use are the bounds of its content, transformed by the use
        Assert.Equal(new SceneBounds(175, 25, 10, 10), new SceneUse(circle) { Transform = SceneTransform.Scale(0.5, 0.5, 180, 45) }.GetBounds(_ => null));

        // A use in its own content draws nothing, as in SVG; the identifier of a linked content is on its link
        var group = new SceneGroup { Hyperlink = "https://example.com/" };
        group.Add(new SceneRectangle(0, 0, 5, 5) { Fill = SceneColor.Black });
        var inner = group.Add(new SceneUse(group));
        var cycle = new Scene(10, 10, "sans-serif");
        var outer = cycle.Root.Add(new SceneUse(group));
        var root = cycle.Root.Add(new SceneUse(cycle.Root));
        Assert.True(inner.IsCircular());
        Assert.True(root.IsCircular());
        Assert.False(outer.IsCircular());
        var cycleSvg = SvgSceneRenderer.Render(cycle);
        Assert.Contains("<defs>\n<a id=\"scene-element0\" href=\"https://example.com/\">", cycleSvg, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(cycleSvg, "<use "));
        Assert.Equal(new SceneBounds(0, 0, 5, 5), new SceneUse(group).GetBounds(_ => null));
        Assert.Throws<ArgumentNullException>("content", () => new SceneUse(null!));
    }

    [Fact]
    public void ViewBoxes()
    {
        #region VectorGraphics_ViewBox
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // A drawing of 100 by 100 units, fitted in a viewport of 200 by 80 pixels: scaled by 0.8 and centered
        var chart = scene.Root.Add(new SceneGroup { ViewBox = new SceneViewBox(new SceneBounds(0, 0, 100, 100), new SceneBounds(0, 10, 200, 80)) });
        chart.Add(new SceneRectangle(0, 0, 100, 100) { Fill = (SceneColor)"#e0f2fe", Stroke = (SceneColor)"#0369a1" });
        chart.Add(new SceneCircle(50, 50, 40) { Fill = (SceneColor)"#0369a1" });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("ViewBox", svg);
        Assert.Contains("<g>\n<svg x=\"0\" y=\"10\" width=\"200\" height=\"80\" viewBox=\"0 0 100 100\">\n<rect", svg, StringComparison.Ordinal);
        Assert.Equal(new SceneTransform(0.8, 0, 0, 0.8, 60, 10), chart.ViewBox!.GetTransform());
        // The stroke, with its miter joins, is cut by the viewport
        var bounds = chart.GetBounds(_ => null)!.Value;
        Assert.Equal(58.4, bounds.X, 1e-9);
        Assert.Equal(10, bounds.Y, 1e-9);
        Assert.Equal(141.6, bounds.Right, 1e-9);
        Assert.Equal(90, bounds.Bottom, 1e-9);

        var box = new SceneBounds(0, 0, 100, 50);
        var viewport = new SceneBounds(0, 0, 200, 200);
        Assert.Equal(new SceneTransform(2, 0, 0, 2, 0, 50), new SceneViewBox(box, viewport).GetTransform());
        Assert.Equal(new SceneTransform(2, 0, 0, 2, 0, 100), new SceneViewBox(box, viewport) { Alignment = SceneAspectRatio.XMaxYMax }.GetTransform());
        Assert.Equal(new SceneTransform(4, 0, 0, 4, -100, 0), new SceneViewBox(box, viewport) { Slice = true }.GetTransform());
        Assert.Equal(new SceneTransform(2, 0, 0, 4, 0, 0), new SceneViewBox(box, viewport) { Alignment = SceneAspectRatio.None, Slice = true }.GetTransform());
        Assert.Null(new SceneViewBox(new SceneBounds(0, 0, 0, 50), viewport).GetTransform());
        Assert.Null(new SceneViewBox(box, new SceneBounds(0, 0, double.NaN, 50)).GetTransform());

        // The alignment and the slice are written as preserveAspectRatio; a view box that draws nothing has no children
        Assert.Contains("preserveAspectRatio=\"none\"", SvgSceneRenderer.Render(Single(new SceneGroup { ViewBox = new SceneViewBox(box, viewport) { Alignment = SceneAspectRatio.None } })), StringComparison.Ordinal);
        Assert.Contains("preserveAspectRatio=\"xMinYMax slice\"", SvgSceneRenderer.Render(Single(new SceneGroup { ViewBox = new SceneViewBox(box, viewport) { Alignment = SceneAspectRatio.XMinYMax, Slice = true } })), StringComparison.Ordinal);
        var empty = new SceneGroup { ViewBox = new SceneViewBox(new SceneBounds(0, 0, 0, 0), viewport) };
        empty.Add(new SceneRectangle(0, 0, 1, 1));
        Assert.DoesNotContain("<rect", SvgSceneRenderer.Render(Single(empty)), StringComparison.Ordinal);
        Assert.Null(empty.GetBounds(_ => null));
    }

    [Fact]
    public void IdentifiersUseThePrefix()
    {
        var scene = new Scene(10, 10, "sans-serif");
        scene.Root.Add(new SceneGroup { Clip = new SceneBounds(0, 0, 5, 5) });
        Assert.Contains("id=\"scene-clip0\"", SvgSceneRenderer.Render(scene));
        Assert.Contains("id=\"diagram-clip0\"", SvgSceneRenderer.Render(scene, new SvgRenderOptions { IdPrefix = "diagram" }));
    }

    [Theory]
    [InlineData("rgb(255, 0, 0)", "#ff0000")]
    [InlineData("RGB(255,128,0)", "#ff8000")]
    [InlineData("rgba(232,232,232, 0.8)", "#e8e8e8cc")]
    [InlineData("rgb(255 0 0 / 50%)", "#ff000080")]
    [InlineData("rgb(100%, 50%, 0%)", "#ff8000")]
    [InlineData("hsl(240, 60%, 86.2745098039%)", "#c7c7f1")]
    [InlineData("hsl(-79.4117647059, 100%, 93.3333333333%)", "#f4ddff")]
    [InlineData("hsla(0, 0%, 0%, 0.5)", "#00000080")]
    [InlineData("hsl(120deg 100% 25%)", "#008000")]
    [InlineData("rebeccapurple", "#663399")]
    [InlineData("LightGoldenRodYellow", "#fafad2")]
    [InlineData(" #ABC ", "#aabbcc")]
    public void CssColors(string value, string expected)
    {
        Assert.True(SceneColor.TryParse(value, out var color));
        Assert.Equal(expected, color.ToString());
    }

    [Theory]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb(1, 2, 3, 4, 5)")]
    [InlineData("rgb(1 2, 3)")]
    [InlineData("rgb(1, 2, 3")]
    [InlineData("rgb(a, b, c)")]
    [InlineData("foo(1, 2, 3)")]
    [InlineData("rgb(1 2 3 4)")]
    [InlineData("rgb(NaN, 0, 0)")]
    [InlineData("notacolor")]
    public void InvalidCssColors(string value)
    {
        Assert.False(SceneColor.TryParse(value, out _));
    }

    [Fact]
    public void DataAttributeNames()
    {
        Assert.True(SceneElement.IsValidDataAttributeName("node-id.v_2"));
        Assert.False(SceneElement.IsValidDataAttributeName("Node"));
        Assert.False(SceneElement.IsValidDataAttributeName(string.Empty));
        Assert.False(SceneElement.IsValidDataAttributeName(null));
        var element = new SceneCircle(0, 0, 1);
        element.SetDataAttribute("id", "a");
        Assert.Equal("a", element.DataAttributes["id"]);
        Assert.Throws<ArgumentException>("name", () => element.SetDataAttribute("Id", "a"));
        Assert.Throws<ArgumentException>("paramName", () => SceneElement.ThrowIfInvalidDataAttributeName("node id", "paramName"));
    }

    [Fact]
    public void SceneFonts()
    {
        #region VectorGraphics_SceneFonts
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");

        // The fonts of the texts: the family of the first face is the default one
        var fonts = SceneFontCollection.Load(
            Path.Combine(testData, "LiberationSans-Regular.woff2"),
            Path.Combine(testData, "LiberationSans-Bold.woff2"),
            Path.Combine(testData, "LiberationMono-Regular.woff2"));

        // The layout measures the texts with the fonts, and the scene keeps them so every renderer draws with them
        var measurer = new FontFaceTextMeasurer(fonts);
        var scene = new Scene(width: 300, height: 60, fontFamily: measurer.FontFamily) { Fonts = measurer.Fonts };

        // Each text has its own family, or the family of the scene
        var code = new SceneFont(12, Family: "Liberation Mono");
        scene.Root.Add(new SceneText(10, 20, "Title", new SceneFont(16, Weight: SceneFont.BoldWeight), SceneColor.Black));
        scene.Root.Add(new SceneText(10, 45, "var x = 1;", code, SceneColor.Black) { MeasuredWidth = measurer.Measure("var x = 1;", code).Width });

        // The SVG can embed the fonts, so every viewer draws the texts with them
        var svg = SvgSceneRenderer.Render(scene, new SvgRenderOptions { EmbedFonts = true });
        #endregion

        DocumentationImages.AssertSvg("SceneFonts", svg);

        Assert.Contains("font-family=\"Liberation Sans, Liberation Mono\"", svg, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"45\" font-family=\"Liberation Mono, Liberation Sans\" font-size=\"12\"", svg, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"20\" font-size=\"16\" font-weight=\"bold\"", svg, StringComparison.Ordinal);

        // Only the faces that draw a character are embedded: the regular face of Liberation Sans draws none
        Assert.Contains("<style>@font-face{font-family:&quot;Liberation Sans&quot;;font-weight:700;font-style:normal;src:url(data:font/woff2;base64,", svg, StringComparison.Ordinal);
        Assert.Contains("@font-face{font-family:&quot;Liberation Mono&quot;;font-weight:400;", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family:&quot;Liberation Sans&quot;;font-weight:400", svg, StringComparison.Ordinal);
        Assert.Equal(svg, SvgSceneRenderer.Render(scene, new SvgRenderOptions { EmbedFonts = true }));

        // Without the option, or without fonts, nothing is embedded
        Assert.DoesNotContain("@font-face", SvgSceneRenderer.Render(scene), StringComparison.Ordinal);
        scene.Fonts = null;
        var withoutFonts = SvgSceneRenderer.Render(scene, new SvgRenderOptions { EmbedFonts = true });
        Assert.DoesNotContain("@font-face", withoutFonts, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"45\" font-family=\"Liberation Mono\" font-size=\"12\"", withoutFonts, StringComparison.Ordinal);
    }

    [Fact]
    public void TextWithTheFamilyOfTheSceneHasNoFontFamily()
    {
        var scene = new Scene(100, 20, "Helvetica");
        scene.Root.Add(new SceneText(0, 10, "a", new SceneFont(10, Family: "Helvetica"), SceneColor.Black));
        scene.Root.Add(new SceneText(0, 10, "b", new SceneFont(10, Family: "Courier \"New\" & <Co>"), SceneColor.Black));
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Contains("<text x=\"0\" y=\"10\" font-size=\"10\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Courier &quot;New&quot; &amp; &lt;Co&gt;\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ClippingPaths()
    {
        #region VectorGraphics_ClippingPaths
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // The union of two circles, in the coordinates of the parent of the clipped element
        var circles = new SceneClipPath();
        circles.Content.Add(new SceneCircle(40, 50, 30));
        circles.Content.Add(new SceneCircle(80, 50, 30));
        scene.Root.Add(new SceneRectangle(0, 0, 120, 100) { Fill = (SceneColor)"#0369a1", ClippingPath = circles });

        // The glyphs of a text show a gradient
        var letters = new SceneClipPath();
        letters.Content.Add(new SceneText(130, 70, "Clip", new SceneFont(40, Weight: SceneFont.BoldWeight), SceneColor.Black));
        var gradient = new LinearGradientPaint(130, 0, 200, 0, [new(0, (SceneColor)"#0369a1"), new(1, (SceneColor)"#e11d48")]);
        scene.Root.Add(new SceneRectangle(120, 0, 80, 100) { Fill = gradient, ClippingPath = letters });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("ClippingPaths", svg);
        Assert.Contains("<clipPath id=\"scene-clip0\">\n<circle cx=\"40\" cy=\"50\" r=\"30\"/>\n<circle cx=\"80\" cy=\"50\" r=\"30\"/>\n</clipPath>", svg, StringComparison.Ordinal);
        Assert.Contains("<rect x=\"0\" y=\"0\" width=\"120\" height=\"100\" fill=\"#0369a1\" clip-path=\"url(#scene-clip0)\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<clipPath id=\"scene-clip1\">\n<text x=\"130\" y=\"70\" font-size=\"40\" font-weight=\"bold\" fill=\"#000000\">Clip</text>", svg, StringComparison.Ordinal);

        // The bounds of a clipped element are cut by its clipping path
        Assert.Equal(new SceneBounds(10, 20, 100, 60), scene.Root.Children[0].GetBounds(_ => null));
        Assert.Equal(new SceneBounds(0, 0, 120, 100), scene.Root.Children[0].GetBounds(_ => null, applyClips: false));
    }

    [Fact]
    public void ClippingPathsOfTransformedElementsAndGroups()
    {
        // The groups of the content are flattened, with their transforms on the shapes; the fill rule is the clip rule
        var clipPath = new SceneClipPath();
        var group = clipPath.Content.Add(new SceneGroup { Transform = SceneTransform.Translate(10, 0) });
        group.Add(new ScenePath(new PathBuilder().AddRectangle(0, 0, 10, 10).Build()) { FillRule = SceneFillRule.EvenOdd, Transform = SceneTransform.Translate(0, 5) });
        group.Add(new SceneLine(0, 0, 10, 10));
        var svg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 50, 50) { Fill = SceneColor.Black, ClippingPath = clipPath, Transform = SceneTransform.Scale(2, 2) }));
        Assert.Contains("<clipPath id=\"scene-clip0\">\n<path d=\"M0,0 L10,0 L10,10 L0,10 Z\" clip-rule=\"evenodd\" transform=\"translate(10 5)\"/>\n</clipPath>", svg, StringComparison.Ordinal);

        // The clipping path of a transformed element is in the coordinates of its parent: a group has it
        Assert.Contains("<g clip-path=\"url(#scene-clip0)\">\n<rect x=\"0\" y=\"0\" width=\"50\" height=\"50\" fill=\"#000000\" transform=\"matrix(2 0 0 2 0 0)\"/>\n</g>", svg, StringComparison.Ordinal);

        // A group writes it on a group outside of its transform
        var clippedGroup = new SceneGroup { ClippingPath = clipPath, Transform = SceneTransform.Translate(1, 2) };
        clippedGroup.Add(new SceneRectangle(0, 0, 1, 1));
        Assert.Contains("<g clip-path=\"url(#scene-clip0)\">\n<g transform=\"translate(1 2)\">", SvgSceneRenderer.Render(Single(clippedGroup)), StringComparison.Ordinal);

        // A clipping path that cuts another one is its clip-path, in the same coordinates; the clipping path of a shape
        // of the content is in the coordinates of its parent: it is written for the space of the shape
        var outer = new SceneClipPath();
        outer.Content.Add(new SceneRectangle(0, 0, 5, 5));
        var inner = new SceneClipPath();
        inner.Content.Add(new SceneRectangle(0, 0, 20, 20));
        var nested = new SceneClipPath { ClippingPath = outer };
        nested.Content.Add(new SceneCircle(0, 0, 10) { ClippingPath = inner, Transform = SceneTransform.Translate(5, 5) });
        var nestedSvg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 50, 50) { ClippingPath = nested }));
        Assert.Contains("<clipPath id=\"scene-clip0\" clip-path=\"url(#scene-clip1)\">\n<circle cx=\"0\" cy=\"0\" r=\"10\" transform=\"translate(5 5)\" clip-path=\"url(#scene-clip2)\"/>\n</clipPath>", nestedSvg, StringComparison.Ordinal);
        Assert.Contains("<clipPath id=\"scene-clip1\">\n<rect x=\"0\" y=\"0\" width=\"5\" height=\"5\"/>\n</clipPath>", nestedSvg, StringComparison.Ordinal);
        Assert.Contains("<clipPath id=\"scene-clip2\">\n<rect x=\"0\" y=\"0\" width=\"20\" height=\"20\" transform=\"translate(-5 -5)\"/>\n</clipPath>", nestedSvg, StringComparison.Ordinal);
        Assert.Equal(new SceneBounds(0, 0, 5, 5), new SceneRectangle(0, 0, 50, 50) { ClippingPath = nested }.GetBounds(_ => null));

        // A clipping path that cuts itself leaves nothing visible
        var circular = new SceneClipPath();
        circular.Content.Add(new SceneRectangle(0, 0, 5, 5));
        circular.ClippingPath = circular;
        Assert.Contains("<clipPath id=\"scene-clip0\"/>", SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 50, 50) { ClippingPath = circular })), StringComparison.Ordinal);
        Assert.Null(new SceneRectangle(0, 0, 50, 50) { ClippingPath = circular }.GetBounds(_ => null));

        // The paints of a text of a clipping path are defined, though they do not change its geometry
        var painted = new SceneClipPath();
        painted.Content.Add(new SceneText(0, 10, "a", new SceneFont(10), SceneColor.Black) { Fill = new LinearGradientPaint(0, 0, 10, 0, [new(0, SceneColor.Black)]), Stroke = SceneColor.White });
        var paintedSvg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 50, 50) { ClippingPath = painted }));
        Assert.Contains("<linearGradient id=\"scene-gradient0\"", paintedSvg, StringComparison.Ordinal);
        Assert.Contains("fill=\"url(#scene-gradient0)\"", paintedSvg, StringComparison.Ordinal);
    }

    [Fact]
    public void PaintOptions()
    {
        #region VectorGraphics_PaintOptions
        var scene = new Scene(width: 200, height: 100, fontFamily: "sans-serif");

        // One arrowhead for lines of every color: it is painted with the stroke of the line it ends
        var arrow = new SceneMarker { RefX = 10, RefY = 5 };
        arrow.Content.Add(new ScenePath(new PathBuilder().AddPolygon((0, 0), (10, 5), (0, 10)).Build()) { Fill = ContextPaint.Stroke });
        scene.Root.Add(new SceneLine(10, 20, 90, 20) { Stroke = (SceneColor)"#0369a1", StrokeWidth = 2, MarkerEnd = arrow });
        scene.Root.Add(new SceneLine(10, 50, 90, 50) { Stroke = (SceneColor)"#e11d48", StrokeWidth = 2, MarkerEnd = arrow });

        // The stroke under the fill, and a stroke of 1 pixel in a group scaled 20 times
        scene.Root.Add(new SceneRectangle(110, 10, 80, 30) { Fill = (SceneColor)"#e0f2fe", Stroke = (SceneColor)"#0369a1", StrokeWidth = 8, PaintOrder = ScenePaintOrder.StrokeFillMarkers });
        var zoomed = scene.Root.Add(new SceneGroup { Transform = SceneTransform.Scale(20, 20) });
        zoomed.Add(new SceneCircle(7.5, 3.5, 1) { Fill = (SceneColor)"#e0f2fe", Stroke = (SceneColor)"#0369a1", NonScalingStroke = true });

        // A radial gradient whose first color fills a circle of 10 pixels around the focal point
        scene.Root.Add(new SceneCircle(30, 80, 15) { Fill = new RadialGradientPaint(30, 80, 15, [new(0, (SceneColor)"#e11d48"), new(1, (SceneColor)"#0369a1")]) { FocalRadius = 10 } });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        DocumentationImages.AssertSvg("PaintOptions", svg);
        Assert.Contains("fill=\"context-stroke\"", svg, StringComparison.Ordinal);
        Assert.Contains("<line x1=\"10\" y1=\"20\" x2=\"90\" y2=\"20\" fill=\"none\" stroke=\"#0369a1\" stroke-width=\"2\" marker-end=\"url(#scene-marker0)\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("stroke-width=\"8\" paint-order=\"stroke\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#0369a1\" vector-effect=\"non-scaling-stroke\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<radialGradient id=\"scene-gradient0\" gradientUnits=\"userSpaceOnUse\" cx=\"30\" cy=\"80\" r=\"15\" fr=\"10\">", svg, StringComparison.Ordinal);

        // The other paint orders, and the context fill
        Assert.Contains("paint-order=\"markers stroke\"", SvgSceneRenderer.Render(Single(new SceneCircle(0, 0, 1) { PaintOrder = ScenePaintOrder.MarkersStrokeFill })), StringComparison.Ordinal);
        Assert.Contains("paint-order=\"fill markers\"", SvgSceneRenderer.Render(Single(new SceneCircle(0, 0, 1) { PaintOrder = ScenePaintOrder.FillMarkersStroke })), StringComparison.Ordinal);
        Assert.Contains("fill=\"context-fill\"", SvgSceneRenderer.Render(Single(new SceneCircle(0, 0, 1) { Fill = ContextPaint.Fill })), StringComparison.Ordinal);
        Assert.DoesNotContain("fr=", SvgSceneRenderer.Render(Single(new SceneCircle(0, 0, 1) { Fill = new RadialGradientPaint(0, 0, 1, [new(0, SceneColor.Black)]) { FocalRadius = double.NaN } })), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SceneAspectRatio.None, false, "none")]
    [InlineData(SceneAspectRatio.XMidYMid, false, "xMidYMid")]
    [InlineData(SceneAspectRatio.XMinYMax, false, "xMinYMax")]
    [InlineData(SceneAspectRatio.XMidYMid, true, "xMidYMid slice")]
    [InlineData(SceneAspectRatio.None, true, "none")]
    public void AspectRatiosOfImages(SceneAspectRatio alignment, bool slice, string expected)
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");
        var svg = SvgSceneRenderer.Render(Single(new SceneImage(0, 0, 40, 20, png) { Alignment = alignment, Slice = slice }));
        Assert.Contains("preserveAspectRatio=\"" + expected + "\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ClippingPathChainsThatLoopAndReusedClipContents()
    {
        // A chain that loops after its first clipping path leaves nothing visible, and is written
        var first = new SceneClipPath();
        first.Content.Add(new SceneRectangle(0, 0, 5, 5));
        var looping = new SceneClipPath();
        looping.Content.Add(new SceneRectangle(0, 0, 5, 5));
        looping.ClippingPath = looping;
        first.ClippingPath = looping;
        var svg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 50, 50) { ClippingPath = first }));
        Assert.Contains("<clipPath id=\"scene-clip0\"/>", svg, StringComparison.Ordinal);
        Assert.Null(new SceneRectangle(0, 0, 50, 50) { ClippingPath = first }.GetBounds(_ => null));

        // A use of the content of a clipping path refers to a definition of its own: the clipping path writes its geometry
        var reused = new SceneClipPath();
        reused.Content.Add(new SceneCircle(5, 5, 5) { Fill = SceneColor.Black });
        var scene = new Scene(20, 20, "sans-serif");
        scene.Root.Add(new SceneRectangle(0, 0, 20, 20) { Fill = SceneColor.Black, ClippingPath = reused });
        scene.Root.Add(new SceneUse(reused.Content) { Transform = SceneTransform.Translate(10, 10) });
        var reusedSvg = SvgSceneRenderer.Render(scene);
        Assert.Contains("<g id=\"scene-element0\">\n<circle cx=\"5\" cy=\"5\" r=\"5\" fill=\"#000000\"/>\n</g>", reusedSvg, StringComparison.Ordinal);
        Assert.Contains("<clipPath id=\"scene-clip0\">\n<circle cx=\"5\" cy=\"5\" r=\"5\"/>\n</clipPath>", reusedSvg, StringComparison.Ordinal);
        Assert.Contains("<use href=\"#scene-element0\" transform=\"translate(10 10)\"/>", reusedSvg, StringComparison.Ordinal);

        // The text a use of a clipping path draws has its font embedded, as the other texts
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");
        var fonts = SceneFontCollection.Load(Path.Combine(testData, "LiberationSans-Regular.woff2"), Path.Combine(testData, "LiberationMono-Regular.woff2"));
        var withFonts = new Scene(20, 20, "Liberation Sans") { Fonts = fonts };
        var lettering = new SceneClipPath();
        lettering.Content.Add(new SceneUse(new SceneText(0, 15, "x", new SceneFont(12, Family: "Liberation Mono"), SceneColor.Black)));
        withFonts.Root.Add(new SceneRectangle(0, 0, 20, 20) { Fill = SceneColor.Black, ClippingPath = lettering });
        var embedded = SvgSceneRenderer.Render(withFonts, new SvgRenderOptions { EmbedFonts = true });
        Assert.Contains("@font-face{font-family:&quot;Liberation Mono&quot;", embedded, StringComparison.Ordinal);
        Assert.Contains("<clipPath id=\"scene-clip0\">\n<text x=\"0\" y=\"15\"", embedded, StringComparison.Ordinal);

        // A masked shape drawn by the scene and reused in a clipping path: the clipping path ignores its mask, which is
        // written once, with the identifiers of its content
        var mask = new SceneMask();
        mask.Content.Add(new SceneRectangle(0, 0, 20, 20) { Id = "mask-content", Fill = SceneColor.White });
        var masked = new SceneCircle(10, 10, 5) { Fill = SceneColor.Black, Mask = mask };
        var maskedScene = new Scene(20, 20, "sans-serif");
        maskedScene.Root.Add(masked);
        var reuse = new SceneClipPath();
        reuse.Content.Add(new SceneUse(masked));
        maskedScene.Root.Add(new SceneRectangle(0, 0, 20, 20) { Fill = SceneColor.Black, ClippingPath = reuse });
        var maskedSvg = SvgSceneRenderer.Render(maskedScene);
        Assert.Equal(1, CountOccurrences(maskedSvg, "id=\"mask-content\""));
        Assert.Contains("<clipPath id=\"scene-clip0\">\n<circle cx=\"10\" cy=\"10\" r=\"5\"/>\n</clipPath>", maskedSvg, StringComparison.Ordinal);

        // The clipping path of a group of a clipping path does not apply: its content, and the patterns of its texts, are not written
        var ignored = new SceneClipPath();
        var tile = new PatternPaint(new SceneBounds(0, 0, 4, 4));
        tile.Content.Add(new SceneUse(new SceneCircle(2, 2, 2) { Fill = SceneColor.Black }));
        ignored.Content.Add(new SceneText(0, 10, "a", new SceneFont(10), SceneColor.Black) { Fill = tile });
        var outerWithGroup = new SceneClipPath();
        outerWithGroup.Content.Add(new SceneGroup { ClippingPath = ignored }).Add(new SceneRectangle(0, 0, 10, 10));
        var ignoredSvg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 20, 20) { Fill = SceneColor.Black, ClippingPath = outerWithGroup }));
        Assert.Equal(1, CountOccurrences(ignoredSvg, "<clipPath "));
        Assert.DoesNotContain("<pattern", ignoredSvg, StringComparison.Ordinal);

        // The bounds of a clipping path are the ones of its geometry, whatever the opacity of its content
        var transparent = new SceneClipPath();
        transparent.Content.Add(new SceneRectangle(0, 0, 10, 10) { Opacity = 0 });
        transparent.Content.Add(new SceneRectangle(20, 0, 10, 10) { Opacity = 0 });
        Assert.Equal(new SceneBounds(0, 0, 30, 10), transparent.GetBounds(_ => null));
    }

    [Fact]
    public void ImagePrimitivesAreInlineOnlyInTheRectangleOfTheirImage()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");

        // Without a subregion, the image is cut to the region of the filter: the tile that follows repeats the region
        var image = new SceneImage(0, 0, 2, 2, png);
        var svg = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 20, 20) { Filter = new SceneFilterGraph([new SceneImagePrimitive(image), new SceneTilePrimitive()]) { Region = new SceneBounds(0, 0, 20, 20) } }));
        Assert.Contains("<feImage href=\"#scene-element0\"/><feTile/>", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/png", svg.Split("<filter", StringSplitOptions.None)[1].Split("</filter>", StringSplitOptions.None)[0], StringComparison.Ordinal);
        Assert.Contains("<image id=\"scene-element0\"", svg, StringComparison.Ordinal);

        // With the rectangle of the image as its subregion, the image is written in the filter, with its aspect ratio
        var fitted = new SceneImage(0, 0, 2, 2, png) { Alignment = SceneAspectRatio.XMinYMin };
        var inline = SvgSceneRenderer.Render(Single(new SceneRectangle(0, 0, 20, 20) { Filter = new SceneFilterGraph([new SceneImagePrimitive(fitted) { Subregion = new SceneBounds(0, 0, 2, 2) }]) }));
        Assert.Contains("<feImage x=\"0\" y=\"0\" width=\"2\" height=\"2\" preserveAspectRatio=\"xMinYMin\" href=\"data:image/png;base64,", inline, StringComparison.Ordinal);
    }
}
