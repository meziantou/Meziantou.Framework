using Meziantou.Framework.Charts;
using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.Pdf.VectorGraphics.Tests;

public sealed class ScenePdfRendererTests
{
    private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void DrawAChartAsPdf()
    {
        #region PdfVectorGraphics_ToPdf
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");

        // The fonts of the texts: the layout measures the texts with them, and the PDF embeds the subsets it uses
        var fonts = SceneFontCollection.Load(Path.Combine(testData, "LiberationSans-Regular.woff2"), Path.Combine(testData, "LiberationSans-Bold.woff2"));

        var plot = new Plot { Title = "Sales by region", Width = 400, Height = 300 };
        plot.Add(new BarMark<(string Region, double Amount)>([("North", 12), ("South", 7)], x: sale => sale.Region, y: sale => sale.Amount));

        var document = plot.ToPdf(new ScenePdfOptions { Fonts = fonts });
        var pdf = document.ToByteArray();
        #endregion

        using (var preview = PdfDocument.Open(pdf).Pages[0].Render(new PdfRenderOptions { Dpi = 96 }))
        {
            DocumentationImages.AssertPng("PdfChart", preview);
        }

        var reopened = PdfDocument.Open(pdf);
        var page = reopened.Pages[0];
        Assert.Equal(300, page.MediaBox.Width, 1e-9);
        Assert.Equal(225, page.MediaBox.Height, 1e-9);
        Assert.Equal("Sales by region", reopened.Information.Title);
        var text = page.ExtractText();
        Assert.Contains("Sales by region", text, StringComparison.Ordinal);
        Assert.Contains("North", text, StringComparison.Ordinal);
        Assert.Contains("LiberationSans", Uncompressed(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ShapesAreFilledAndStroked()
    {
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 50, 50) { Fill = (SceneColor)"#ff0000" });
        scene.Root.Add(new SceneCircle(75, 25, 20) { Fill = (SceneColor)"#0000ff" });
        scene.Root.Add(new SceneLine(0, 75, 50, 75) { Stroke = (SceneColor)"#00ff00", StrokeWidth = 6 });

        // A half disc drawn with an arc, and a ring with a hole
        scene.Root.Add(new ScenePath(new PathBuilder().MoveTo(55, 60).ArcTo(20, 20, 0, largeArc: false, sweep: true, 95, 60).Close().Build()) { Fill = (SceneColor)"#000000" });
        scene.Root.Add(new ScenePath(new PathBuilder().AddRectangle(55, 70, 40, 25).AddRectangle(65, 77, 20, 11).Build()) { Fill = (SceneColor)"#ff00ff", FillRule = SceneFillRule.EvenOdd });
        using var image = Render(scene);

        Assert.Equal(new Rgba32(255, 0, 0), image.Frames[0][25, 25]);
        Assert.Equal(new Rgba32(0, 0, 255), image.Frames[0][75, 25]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][95, 5]);
        Assert.Equal(new Rgba32(0, 255, 0), image.Frames[0][25, 75]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][25, 80]);

        // The arc goes up (y points down in the scene): the half disc is above y = 60
        Assert.Equal(new Rgba32(0, 0, 0), image.Frames[0][75, 50]);
        Assert.Equal(new Rgba32(255, 0, 255), image.Frames[0][58, 72]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][75, 82]);
    }

    [Fact]
    public void GradientsOpacityClipAndTransform()
    {
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 100, 20) { Fill = new LinearGradientPaint(0, 0, 100, 0, [new(0, (SceneColor)"#ff0000"), new(1, (SceneColor)"#0000ff")]) });

        // Half transparent, as a whole: the overlap of the two rectangles is not darker
        var group = scene.Root.Add(new SceneGroup { Opacity = 0.5 });
        group.Add(new SceneRectangle(0, 30, 40, 20) { Fill = (SceneColor)"#000000" });
        group.Add(new SceneRectangle(20, 30, 40, 20) { Fill = (SceneColor)"#000000" });

        // Clipped to its left half, then moved by (50, 60)
        var clipped = scene.Root.Add(new SceneGroup { Clip = new SceneBounds(50, 60, 20, 40), Transform = SceneTransform.Translate(50, 60) });
        clipped.Add(new SceneRectangle(0, 0, 40, 40) { Fill = (SceneColor)"#00ff00" });
        using var image = Render(scene);

        Assert.True(image.Frames[0][5, 10].R > 200 && image.Frames[0][5, 10].B < 50);
        Assert.True(image.Frames[0][95, 10].B > 200 && image.Frames[0][95, 10].R < 50);
        Assert.InRange(image.Frames[0][10, 40].R, 120, 136);
        Assert.Equal(image.Frames[0][10, 40], image.Frames[0][30, 40]);
        Assert.Equal(new Rgba32(0, 255, 0), image.Frames[0][60, 80]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][80, 80]);
    }

    [Fact]
    public void ImagesClipPathsAndRadialGradients()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };
        var clipped = scene.Root.Add(new SceneGroup { ClipPath = new PathBuilder().AddCircle(20, 20, 20).Build() });
        clipped.Add(new SceneImage(0, 0, 40, 40, png));
        scene.Root.Add(new SceneRectangle(50, 50, 50, 50) { Fill = new RadialGradientPaint(75, 75, 25, [new(0, SceneColor.Black), new(1, SceneColor.White)]) });
        scene.Root.Add(new SceneImage(50, 0, 40, 40, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 }));
        using var image = Render(scene);

        // The image is upright: red at the top left, blue at the top right
        var red = image.Frames[0][8, 8];
        Assert.True(red.R > 200 && red.G < 80 && red.B < 80, red.ToString());
        var blue = image.Frames[0][32, 8];
        Assert.True(blue.B > 200 && blue.R < 80, blue.ToString());
        var green = image.Frames[0][8, 32];
        Assert.True(green.G > 200 && green.R < 80, green.ToString());
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][1, 1]);
        Assert.True(image.Frames[0][75, 75].R < 30);
        Assert.True(image.Frames[0][98, 75].R > 220);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][70, 20]);
    }

    [Fact]
    public void DashOffsetAndMiterLimit()
    {
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };

        // Dashes of 10 and gaps of 10: the offset of 10 starts the line with a gap
        scene.Root.Add(new SceneLine(0, 5, 40, 5) { Stroke = SceneColor.Black, StrokeWidth = 4, StrokeDashArray = [10, 10] });
        scene.Root.Add(new SceneLine(0, 15, 40, 15) { Stroke = SceneColor.Black, StrokeWidth = 4, StrokeDashArray = [10, 10], StrokeDashOffset = 10 });

        // A corner of about 20 degrees needs a miter 5.6 times the width of the line: a limit of 4 bevels it, 10 does not
        scene.Root.Add(new ScenePath(new PathBuilder().MoveTo(15, 95).LineTo(25, 40).LineTo(35, 95).Build()) { Stroke = SceneColor.Black, StrokeWidth = 6 });
        scene.Root.Add(new ScenePath(new PathBuilder().MoveTo(65, 95).LineTo(75, 40).LineTo(85, 95).Build()) { Stroke = SceneColor.Black, StrokeWidth = 6, MiterLimit = 10 });
        using var image = Render(scene);

        Assert.True(image.Frames[0][5, 5].R < 100);
        Assert.True(image.Frames[0][15, 5].R > 200);
        Assert.True(image.Frames[0][5, 15].R > 200);
        Assert.True(image.Frames[0][15, 15].R < 100);
        Assert.True(image.Frames[0][25, 30].R > 200);
        Assert.True(image.Frames[0][75, 30].R < 160);
    }

    [Fact]
    public void GradientsBlendModesMasksAndDecorations()
    {
        var red = (SceneColor)"#ff0000";
        var blue = (SceneColor)"#0000ff";
        var green = (SceneColor)"#00ff00";
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };

        // A gradient of 10 pixels from red to blue, repeated, reflected, and stretched over 100 pixels by its transform
        scene.Root.Add(new SceneRectangle(0, 0, 100, 10) { Fill = new LinearGradientPaint(0, 0, 10, 0, [new(0, red), new(1, blue)]) { Spread = SceneGradientSpread.Repeat } });
        scene.Root.Add(new SceneRectangle(0, 10, 100, 10) { Fill = new LinearGradientPaint(0, 0, 10, 0, [new(0, red), new(1, blue)]) { Spread = SceneGradientSpread.Reflect } });
        scene.Root.Add(new SceneRectangle(0, 20, 100, 10) { Fill = new LinearGradientPaint(0, 0, 1, 0, [new(0, red), new(1, blue)]) { Transform = SceneTransform.Scale(100, 1) } });

        // Red multiplied with blue is black, with white it is red
        scene.Root.Add(new SceneRectangle(0, 30, 50, 10) { Fill = blue });
        scene.Root.Add(new SceneRectangle(0, 30, 100, 10) { Fill = red, BlendMode = SceneBlendMode.Multiply });

        // A luminance mask shows the right half (white), an alpha mask the left half
        var luminance = scene.Root.Add(new SceneGroup { Mask = new SceneMask() });
        luminance.Mask!.Content.Add(new SceneRectangle(50, 40, 50, 10) { Fill = SceneColor.White });
        luminance.Add(new SceneRectangle(0, 40, 100, 10) { Fill = green });
        var alpha = new SceneMask { Mode = SceneMaskMode.Alpha };
        alpha.Content.Add(new SceneRectangle(0, 50, 50, 10) { Fill = SceneColor.Black });
        scene.Root.Add(new SceneRectangle(0, 50, 100, 10) { Fill = green, Mask = alpha });

        // An underline
        scene.Root.Add(new SceneText(10, 80, "ii", new SceneFont(20), SceneColor.Black) { Decorations = SceneTextDecorations.Underline });
        using var image = Render(scene);

        static bool IsRed(Rgba32 color) => color.R > 150 && color.B < 100;
        static bool IsBlue(Rgba32 color) => color.B > 150 && color.R < 100;
        var pixels = image.Frames[0];
        Assert.True(IsRed(pixels[1, 5]) && IsRed(pixels[11, 5]) && IsBlue(pixels[9, 5]), "repeat");
        Assert.True(IsRed(pixels[1, 15]) && IsBlue(pixels[11, 15]) && IsRed(pixels[19, 15]), "reflect");
        Assert.True(IsRed(pixels[5, 25]) && IsBlue(pixels[95, 25]), "transform");
        Assert.True(pixels[25, 35].R < 30 && pixels[25, 35].B < 30, "multiply with blue");
        Assert.True(IsRed(pixels[75, 35]), "multiply with white");
        Assert.True(pixels[25, 45].G > 200 && pixels[25, 45].R > 200, "luminance mask hides");
        Assert.True(pixels[75, 45].G > 200 && pixels[75, 45].R < 50, "luminance mask shows");
        Assert.True(pixels[25, 55].G > 200 && pixels[25, 55].R < 50, "alpha mask shows");
        Assert.True(pixels[75, 55].G > 200 && pixels[75, 55].R > 200, "alpha mask hides");
        Assert.Contains(Enumerable.Range(81, 3), y => pixels[13, y].R < 200);
        Assert.True(pixels[13, 87].R > 200, "nothing below the underline");
    }

    [Fact]
    public void FiltersAreNotSupported()
    {
        var scene = new Scene(100, 60, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(20, 20, 20, 20) { Fill = SceneColor.Black, Filter = new SceneBlurFilter(3) });
        scene.Root.Add(new SceneRectangle(60, 10, 20, 20) { Fill = SceneColor.Black, Filter = new SceneDropShadowFilter(10, 10, 0, (SceneColor)"#ff0000") });
        var exception = Assert.Throws<NotSupportedException>(() => scene.ToPdf());
        Assert.Contains("SceneBlurFilter", exception.Message, StringComparison.Ordinal);

        // Nothing is drawn on the page
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        Assert.Throws<NotSupportedException>(() => page.DrawScene(scene, new PdfPoint(0, 100)));
        Assert.Empty(page.GetContentData());

        // A filter in the content of a mask is found too
        var masked = new Scene(10, 10, "sans-serif");
        var mask = new SceneMask();
        mask.Content.Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.White, Filter = new SceneBlurFilter(1) });
        masked.Root.Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black, Mask = mask });
        Assert.Throws<NotSupportedException>(() => masked.ToPdf());
    }

    [Fact]
    public void WeightsSelectTheNearestFace()
    {
        var fonts = SceneFontCollection.Load(Path.Combine(TestData, "LiberationSans-Regular.woff2"), Path.Combine(TestData, "LiberationSans-Bold.woff2"));
        var scene = new Scene(200, 60, fonts.FontFamily) { Fonts = fonts };
        scene.Root.Add(new SceneText(0, 20, "Light", new SceneFont(20, Weight: 300), SceneColor.Black));
        scene.Root.Add(new SceneText(0, 50, "Heavy", new SceneFont(20, Weight: 800), SceneColor.Black));
        var pdf = Uncompressed(scene.ToPdf());
        Assert.Contains("LiberationSans-Bold", pdf, StringComparison.Ordinal);

        // Without fonts, the weights from 600 are Helvetica-Bold
        var standard = new Scene(200, 60, "Helvetica");
        standard.Root.Add(new SceneText(0, 20, "SemiBold", new SceneFont(20, Weight: 600), SceneColor.Black));
        Assert.Contains("/Helvetica-Bold", Uncompressed(standard.ToPdf()), StringComparison.Ordinal);
    }

    [Fact]
    public void TextSpans()
    {
        var scene = new Scene(200, 60, "sans-serif") { Background = SceneColor.White };
        var font = new SceneFont(30, Weight: SceneFont.BoldWeight);
        scene.Root.Add(new SceneText(10, 40, [new SceneTextSpan("MM") { Color = (SceneColor)"#ff0000" }, new SceneTextSpan("MM") { Color = (SceneColor)"#0000ff", Decorations = SceneTextDecorations.Underline }], font, SceneColor.Black));
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });

        // The first two letters are red, the last two blue and underlined
        var pixels = image.Frames[0];
        Assert.Contains(Enumerable.Range(10, 40), x => pixels[x, 30].R > 200 && pixels[x, 30].B < 80);
        Assert.Contains(Enumerable.Range(60, 60), x => pixels[x, 30].B > 200 && pixels[x, 30].R < 80);
        Assert.DoesNotContain(Enumerable.Range(60, 60), x => pixels[x, 30].R > 200 && pixels[x, 30].B < 80);
        Assert.Contains(Enumerable.Range(41, 5), y => pixels[100, y].B > 150 && pixels[100, y].R < 150);
        Assert.Contains("MMMM", document.Pages[0].ExtractText(), StringComparison.Ordinal);

        // A color matrix is a filter: PDF cannot draw it
        scene.Root.Add(new SceneRectangle(150, 10, 40, 40) { Fill = (SceneColor)"#ff0000", Filter = SceneColorMatrixFilter.Grayscale() });
        Assert.Throws<NotSupportedException>(() => scene.ToPdf());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LetterSpacingAndOverline(bool embedFonts)
    {
        var font = new SceneFont(20);
        var scene = new Scene(200, 130, "sans-serif") { Background = SceneColor.White };
        if (embedFonts)
        {
            scene.Fonts = SceneFontCollection.Load(Path.Combine(TestData, "LiberationSans-Regular.woff2"));
        }

        scene.Root.Add(new SceneText(10, 30, "MMMM", font, SceneColor.Black));
        scene.Root.Add(new SceneText(10, 70, "MM MM", font, SceneColor.Black) { LetterSpacing = 10, WordSpacing = 5 });
        scene.Root.Add(new SceneText(190, 120, "xx", font, SceneColor.Black) { Anchor = SceneTextAnchor.End, Decorations = SceneTextDecorations.Overline });
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });

        // 4 spaces of 10 pixels between the 5 characters, and 5 more after the space, which is 0.278 em in Helvetica and Liberation Sans
        var (left, right) = GetInkColumns(image, 0, 40);
        var (spacedLeft, spacedRight) = GetInkColumns(image, 40, 80);
        Assert.InRange(spacedLeft, left - 1, left + 1);
        Assert.InRange(spacedRight - right - (0.278 * font.Size), 44, 46);

        // The spaces are not read as spaces between the letters
        Assert.Contains("MM MM", document.Pages[0].ExtractText(), StringComparison.Ordinal);

        // The overline is above the x-height, at the ascent of the font
        var top = Enumerable.Range(90, 30).First(y => Enumerable.Range(150, 50).Any(x => image.Frames[0][x, y].R < 128));
        Assert.InRange(top, 100, 106);
    }

    [Fact]
    public void LetterSpacingOfEmbeddedFonts()
    {
        // The glyphs are placed with the spacing after each cluster: 10 pixels are 500 thousandths of the size
        var fonts = SceneFontCollection.Load(Path.Combine(TestData, "LiberationSans-Regular.woff2"));
        var scene = new Scene(200, 100, fonts.FontFamily) { Fonts = fonts };
        scene.Root.Add(new SceneText(10, 70, "MM MM", new SceneFont(20), SceneColor.Black) { LetterSpacing = 10, WordSpacing = 5 });
        var document = scene.ToPdf();
        var content = Uncompressed(document);
        Assert.Matches(@"\[<[0-9A-F]{4}>-500<[0-9A-F]{4}>-500<[0-9A-F]{4}>-750<[0-9A-F]{4}>-500<[0-9A-F]{4}>\] TJ", content);
        Assert.Contains("/ActualText", content, StringComparison.Ordinal);
        Assert.Contains("MM MM", document.Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void TextOnAPath()
    {
        // A text along a horizontal path from 10 to 60: the characters beyond its end are not drawn
        var scene = new Scene(200, 100, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneText(0, 0, "MMMMMMMMMM", new SceneFont(20), SceneColor.Black) { Path = new PathBuilder().MoveTo(10, 50).LineTo(60, 50).Build(), Decorations = SceneTextDecorations.Underline });

        // Vertical, going down, centered on the middle of the path: the tops of the glyphs are on its right, as in SVG
        scene.Root.Add(new SceneText(0, 0, "MM", new SceneFont(20), SceneColor.Black) { Path = new PathBuilder().MoveTo(150, 10).LineTo(150, 90).Build(), PathStartOffset = 40, Anchor = SceneTextAnchor.Middle });
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var inked = Enumerable.Range(0, 100).Where(x => Enumerable.Range(30, 19).Any(y => image.Frames[0][x, y].R < 128)).ToList();
        Assert.InRange(inked.Min(), 10, 13);
        Assert.InRange(inked.Max(), 50, 62);

        // The underline is below the path
        Assert.Contains(Enumerable.Range(50, 5), y => image.Frames[0][30, y].R < 200);

        var columns = Enumerable.Range(100, 100).Where(x => Enumerable.Range(0, 100).Any(y => image.Frames[0][x, y].R < 128)).ToList();
        Assert.InRange(columns.Min(), 150, 152);
        Assert.InRange(columns.Max(), 163, 170);
        var text = document.Pages[0].ExtractText();
        Assert.Contains("MMM", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TextPaintsAndOutlines()
    {
        var font = new SceneFont(30, Weight: SceneFont.BoldWeight);
        var scene = new Scene(300, 160, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneText(10, 30, "MMMMMM", font, SceneColor.Black) { Fill = new LinearGradientPaint(10, 0, 140, 0, [new(0, (SceneColor)"#0000ff"), new(1, (SceneColor)"#ff0000")]) });
        scene.Root.Add(new SceneText(160, 30, "WWWWWW", font, SceneColor.Transparent) { Stroke = (SceneColor)"#ff0000" });
        scene.Root.Add(new SceneText(10, 80, "Under", font, SceneColor.Black) { Stroke = (SceneColor)"#ffff00", StrokeWidth = 6, PaintOrder = ScenePaintOrder.StrokeFillMarkers });
        scene.Root.Add(new SceneText(160, 80, "Over", font, SceneColor.Black) { Stroke = (SceneColor)"#ffff00", StrokeWidth = 6 });
        scene.Root.Add(new SceneText(10, 130, [new SceneTextSpan("MMM"), new SceneTextSpan("MMM") { Color = (SceneColor)"#0000ff" }], font, (SceneColor)"#ff0000") { Decorations = SceneTextDecorations.Underline, DecorationFill = (SceneColor)"#ff0000" });
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var pixels = image.Frames[0];

        // The gradient goes from blue to red; the outline is red, without fill
        Assert.True(Count(10, 0, 40, 40, pixel => pixel.B > 200 && pixel.R < 100) > 20);
        Assert.True(Count(110, 0, 150, 40, pixel => pixel.R > 200 && pixel.B < 100) > 20);
        Assert.Equal(0, Count(160, 0, 300, 40, pixel => pixel.R < 100 && pixel.G < 100));
        Assert.True(Count(160, 0, 300, 40, pixel => pixel.R > 200 && pixel.G < 100) > 50);

        // A wide outline under the fill leaves more of the glyphs black than the same outline over them
        var blackUnder = Count(0, 45, 150, 95, pixel => pixel.R < 60 && pixel.G < 60);
        var blackOver = Count(150, 45, 300, 95, pixel => pixel.R < 60 && pixel.G < 60);
        Assert.True(blackUnder > blackOver, $"{blackUnder} black pixels with the outline under the fill, {blackOver} with the outline over it");

        // The underline is red under both spans; the glyphs of the second span are blue
        Assert.Contains(Enumerable.Range(130, 8), y => pixels[110, y].R > 200 && pixels[110, y].B < 100);
        Assert.True(Count(90, 100, 160, 125, pixel => pixel.B > 200 && pixel.R < 100) > 50);

        // The outline is the stroke rendering mode; an outline under the fill is drawn first, and the text is extracted once
        var content = Uncompressed(document);
        Assert.Contains("1 Tr", content, StringComparison.Ordinal);
        Assert.Contains("2 Tr", content, StringComparison.Ordinal);
        Assert.Contains("0 Tr", content, StringComparison.Ordinal);
        var text = document.Pages[0].ExtractText();
        Assert.Equal(text.IndexOf("Under", StringComparison.Ordinal), text.LastIndexOf("Under", StringComparison.Ordinal));
        Assert.Contains("Under", text, StringComparison.Ordinal);
        Assert.Contains("Over", text, StringComparison.Ordinal);
        Assert.Contains("WWWWWW", text, StringComparison.Ordinal);

        int Count(int x0, int y0, int x1, int y1, Func<Rgba32, bool> predicate) => Enumerable.Range(y0, y1 - y0).Sum(y => Enumerable.Range(x0, x1 - x0).Count(x => predicate(pixels[x, y])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextPositionsAndBaselines(bool embedFonts)
    {
        var font = new SceneFont(20);
        var scene = new Scene(300, 200, "sans-serif") { Background = SceneColor.White };
        if (embedFonts)
        {
            scene.Fonts = SceneFontCollection.Load(Path.Combine(TestData, "LiberationSans-Regular.woff2"));
        }

        scene.Root.Add(new SceneText(10, 20, "MM", font, SceneColor.Black) { Baseline = SceneTextBaseline.TextTop });
        scene.Root.Add(new SceneText(100, 40, [new SceneTextSpan("MM"), new SceneTextSpan("MM") { BaselineShift = 10 }], font, SceneColor.Black));
        scene.Root.Add(new SceneText(80, 80, [new SceneTextSpan("MM"), new SceneTextSpan("MM") { X = 250, Dy = 20 }], font, SceneColor.Black) { Anchor = SceneTextAnchor.End });
        scene.Root.Add(new SceneText(290, 140, "MM", font, SceneColor.Black) { Direction = SceneTextDirection.RightToLeft });
        scene.Root.Add(new SceneText(10, 150, [new SceneTextSpan("MM") { GlyphRotation = 90 }], font, SceneColor.Black));
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });

        // The ascent of Helvetica (0.718 em) is smaller than the one of Liberation Sans (0.905 em)
        var (_, top, _, bottom) = GetInkBounds(image, 0, 0, 90, 60);
        Assert.InRange(top, 20, 26);
        Assert.InRange(bottom, embedFonts ? 37 : 33, embedFonts ? 39 : 35);

        var (_, _, _, firstBottom) = GetInkBounds(image, 100, 0, 128, 60);
        var (_, _, _, shiftedBottom) = GetInkBounds(image, 136, 0, 200, 60);
        Assert.InRange(firstBottom, 38, 40);
        Assert.InRange(shiftedBottom, 28, 30);

        var (_, _, right, _) = GetInkBounds(image, 0, 62, 100, 82);
        Assert.InRange(right, 76, 80);
        var (_, _, chunkRight, chunkBottom) = GetInkBounds(image, 100, 82, 300, 110);
        Assert.InRange(chunkRight, 246, 250);
        Assert.InRange(chunkBottom, 98, 100);

        (_, _, right, _) = GetInkBounds(image, 200, 120, 300, 145);
        Assert.InRange(right, 286, 290);

        (_, top, _, bottom) = GetInkBounds(image, 0, 130, 100, 200);
        Assert.InRange(top, 148, 152);
        Assert.InRange(bottom, 160, 172);
        Assert.Contains("MMMM", document.Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextLength(bool embedFonts)
    {
        var font = new SceneFont(20);
        var scene = new Scene(300, 120, "sans-serif") { Background = SceneColor.White };
        if (embedFonts)
        {
            scene.Fonts = SceneFontCollection.Load(Path.Combine(TestData, "LiberationSans-Regular.woff2"));
        }

        // 30 pixels more, in the 3 gaps between the 4 letters; twice as wide, the glyphs scaled
        var width = PdfFont.GetStandardFont(PdfStandardFont.Helvetica).GetTextWidth("MMMM", 20);
        scene.Root.Add(new SceneText(10, 30, "MMMM", font, SceneColor.Black));
        scene.Root.Add(new SceneText(10, 70, "MMMM", font, SceneColor.Black) { TextLength = width + 30 });
        scene.Root.Add(new SceneText(10, 110, "MMMM", font, SceneColor.Black) { TextLength = 2 * width, LengthAdjust = SceneTextLengthAdjust.SpacingAndGlyphs });
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var (left, right) = GetInkColumns(image, 0, 40);
        var (spacedLeft, spacedRight) = GetInkColumns(image, 40, 80);
        var (scaledLeft, scaledRight) = GetInkColumns(image, 80, 120);
        Assert.InRange(spacedLeft, left - 1, left + 1);
        Assert.InRange(spacedRight - right, 29, 31);
        Assert.InRange(scaledLeft - 10, ((left - 10) * 2) - 2, ((left - 10) * 2) + 2);
        Assert.InRange(scaledRight - 10, ((right - 10) * 2) - 2, ((right - 10) * 2) + 2);
        Assert.Contains("MMMM", document.Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Patterns()
    {
        // A tile of 10 by 10 pixels with a red square in its top-left quarter, repeated, and scaled by 2
        var pattern = new PatternPaint(new SceneBounds(0, 0, 10, 10));
        pattern.Content.Add(new SceneRectangle(0, 0, 5, 5) { Fill = (SceneColor)"#ff0000" });
        var scaled = new PatternPaint(new SceneBounds(0, 0, 10, 10)) { Transform = SceneTransform.Scale(2, 2) };
        scaled.Content.Add(new SceneRectangle(0, 0, 5, 5) { Fill = (SceneColor)"#0000ff" });
        scaled.Content.Add(new SceneGroup { Hyperlink = "https://example.com/" }).Add(new SceneRectangle(0, 0, 5, 5) { Fill = (SceneColor)"#0000ff" });

        // A pattern that paints with itself does not draw its content again
        var recursive = new PatternPaint(new SceneBounds(0, 0, 10, 10));
        recursive.Content.Add(new SceneRectangle(0, 0, 10, 10) { Fill = recursive });
        var scene = new Scene(100, 60, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 40, 40) { Fill = pattern });
        scene.Root.Add(new SceneGroup { Transform = SceneTransform.Translate(50, 0) }).Add(new SceneRectangle(0, 0, 40, 40) { Fill = scaled });
        scene.Root.Add(new SceneRectangle(0, 45, 10, 10) { Fill = recursive });
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var pixels = image.Frames[0];
        Assert.Equal(new Rgba32(255, 0, 0), pixels[2, 2]);
        Assert.Equal(new Rgba32(255, 0, 0), pixels[12, 22]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[7, 7]);
        Assert.Equal(new Rgba32(0, 0, 255), pixels[58, 8]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[62, 2]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[5, 50]);
        Assert.Contains("/PatternType 1", Uncompressed(document), StringComparison.Ordinal);
        Assert.Empty(document.Pages[0].Annotations);

        // A filter in the content of a pattern cannot be drawn
        var filtered = new PatternPaint(new SceneBounds(0, 0, 10, 10));
        filtered.Content.Add(new SceneRectangle(0, 0, 5, 5) { Fill = SceneColor.Black, Filter = new SceneBlurFilter(1) });
        var withFilter = new Scene(10, 10, "sans-serif");
        withFilter.Root.Add(new SceneRectangle(0, 0, 10, 10) { Stroke = filtered });
        Assert.Throws<NotSupportedException>(() => withFilter.ToPdf());
    }

    [Fact]
    public void Markers()
    {
        // A square of 4 by 4 at the end of a line, centered on it and scaled by the stroke width of 2; a dot on the middle vertex of a path
        var square = new SceneMarker { RefX = 2, RefY = 2 };
        square.Content.Add(new SceneRectangle(0, 0, 4, 4) { Fill = (SceneColor)"#ff0000" });
        square.Content.Add(new SceneGroup { Hyperlink = "https://example.com/" }).Add(new SceneRectangle(0, 0, 1, 1) { Fill = (SceneColor)"#ff0000" });
        var dot = new SceneMarker { Units = SceneMarkerUnits.UserSpace };
        dot.Content.Add(new SceneCircle(0, 0, 3) { Fill = (SceneColor)"#0000ff" });
        var scene = new Scene(100, 60, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneLine(10, 20, 60, 20) { StrokeWidth = 2, MarkerEnd = square });
        scene.Root.Add(new ScenePath(new PathBuilder().MoveTo(10, 50).LineTo(50, 50).LineTo(90, 50).Build()) { Stroke = SceneColor.Black, MarkerMid = dot });
        var document = scene.ToPdf();
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var pixels = image.Frames[0];
        Assert.Equal(new Rgba32(255, 0, 0), pixels[63, 22]);
        Assert.Equal(new Rgba32(255, 0, 0), pixels[56, 16]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[66, 20]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[30, 20]);
        Assert.Equal(new Rgba32(0, 0, 255), pixels[50, 48]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[90, 48]);
        Assert.Empty(document.Pages[0].Annotations);
    }

    [Fact]
    public void ReusedElementsAndViewBoxes()
    {
        // A square drawn twice by uses, moved by them; a use in its own content is not drawn
        var square = new SceneRectangle(0, 0, 10, 10) { Fill = (SceneColor)"#ff0000" };
        var cycle = new SceneGroup();
        cycle.Add(new SceneUse(cycle));
        var scene = new Scene(100, 60, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneUse(square) { Transform = SceneTransform.Translate(10, 10) });
        scene.Root.Add(new SceneUse(square) { Transform = SceneTransform.Translate(30, 10), Opacity = 0.5 });
        scene.Root.Add(new SceneUse(cycle));

        // A drawing of 10 by 10 units in a viewport of 40 by 20 pixels: scaled by 2 and centered; the viewport cuts it
        var viewBox = scene.Root.Add(new SceneGroup { ViewBox = new SceneViewBox(new SceneBounds(0, 0, 10, 10), new SceneBounds(50, 30, 40, 20)) });
        viewBox.Add(new SceneRectangle(0, 0, 10, 10) { Fill = (SceneColor)"#0000ff" });
        viewBox.Add(new SceneRectangle(-20, 0, 20, 10) { Fill = (SceneColor)"#0000ff" });
        using var image = scene.ToPdf().Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var pixels = image.Frames[0];
        Assert.Equal(new Rgba32(255, 0, 0), pixels[15, 15]);
        Assert.InRange(pixels[35, 15].G, 120, 135);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[25, 15]);
        Assert.Equal(new Rgba32(0, 0, 255), pixels[70, 40]);
        Assert.Equal(new Rgba32(0, 0, 255), pixels[55, 40]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[45, 40]);
    }

    [Fact]
    public void SpacedTextsAreDrawnAsTheyAreMeasured()
    {
        // A spaced text of an embedded font that ends where its measured width says starts where the same text anchored at its start starts
        var fonts = SceneFontCollection.Load(Path.Combine(TestData, "LiberationSans-Regular.woff2"));
        var font = new SceneFont(20);
        var width = new FontFaceTextMeasurer(fonts).Measure("ffi office", font, letterSpacing: 3, wordSpacing: 5).Width;
        var scene = new Scene(260, 80, fonts.FontFamily) { Fonts = fonts, Background = SceneColor.White };
        scene.Root.Add(new SceneText(10, 30, "ffi office", font, SceneColor.Black) { LetterSpacing = 3, WordSpacing = 5 });
        scene.Root.Add(new SceneText(10 + width, 70, "ffi office", font, SceneColor.Black) { LetterSpacing = 3, WordSpacing = 5, Anchor = SceneTextAnchor.End });
        using var image = Render(scene);
        var (left, right) = GetInkColumns(image, 0, 40);
        var (endLeft, endRight) = GetInkColumns(image, 40, 80);
        Assert.InRange(endLeft, left - 1, left + 1);
        Assert.InRange(endRight, right - 1, right + 1);
    }

    [Fact]
    public void TheContentGroupsOfPatternsAndMarkersApplyTheirSettings()
    {
        // A pattern whose content is invisible paints nothing; a marker whose content is moved by its transform
        var invisible = new PatternPaint(new SceneBounds(0, 0, 10, 10));
        invisible.Content.Opacity = 0;
        invisible.Content.Add(new SceneRectangle(0, 0, 10, 10) { Fill = (SceneColor)"#ff0000" });
        var moved = new SceneMarker { Units = SceneMarkerUnits.UserSpace };
        moved.Content.Transform = SceneTransform.Translate(10, 0);
        moved.Content.Add(new SceneRectangle(-2, -2, 4, 4) { Fill = (SceneColor)"#0000ff" });
        var scene = new Scene(60, 30, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 20, 20) { Fill = invisible });
        scene.Root.Add(new SceneLine(30, 10, 40, 10) { MarkerEnd = moved });
        using var image = Render(scene);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][5, 5]);
        Assert.Equal(new Rgba32(0, 0, 255), image.Frames[0][50, 10]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][40, 10]);
    }

    [Fact]
    public void DocumentsAreTagged()
    {
        #region PdfVectorGraphics_Tagged
        var scene = new Scene(100, 50, "sans-serif") { Title = "Sales by region", Description = "North sold 12 units, South 7." };
        scene.Root.Add(new SceneGroup { Hyperlink = "https://example.com/sales" }).Add(new SceneRectangle(10, 10, 40, 20) { Fill = SceneColor.Black });

        // The drawing is a figure of the structure tree, described by the title and the description of the scene
        var document = scene.ToPdf();
        #endregion

        var root = Assert.Single(document.StructureTree.Elements);
        Assert.Equal("Document", root.Type);
        var figure = Assert.Single(root.Children);
        Assert.Equal("Figure", figure.Type);
        Assert.Equal("Sales by region. North sold 12 units, South 7.", figure.AlternateDescription);
        Assert.Equal("Link", Assert.Single(figure.Children).Type);

        // Saved and opened again, the tree is there
        var reopened = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("Figure", Assert.Single(Assert.Single(reopened.StructureTree.Elements).Children).Type);

        // Not tagged, or in an element of an existing document
        Assert.False(scene.ToPdf(new ScenePdfOptions { Tagged = false }).StructureTree.Exists);
        var report = new PdfDocument();
        var section = report.StructureTree.AddElement("Sect");
        report.Pages.Add(new PdfSize(200, 200)).DrawScene(new Scene(10, 10, "sans-serif") { Title = "Chart:" , Description = "Details" }, new PdfPoint(0, 200), new ScenePdfOptions { StructureParent = section });
        Assert.Equal("Chart: Details", Assert.Single(section.Children).AlternateDescription);
        Assert.Null(new Scene(10, 10, "sans-serif").ToPdf().StructureTree.Elements[0].Children[0].AlternateDescription);
    }

    [Fact]
    public void ChartsAndDiagramsHaveAlternateDescriptions()
    {
        // The title and the description of a chart describe its figure, and are the title and the subject of the document
        var plot = new Plot { Title = "Sales by region", Description = "North sold 12 units, South 7.", Width = 200, Height = 100 };
        plot.Add(new BarMark<(string Region, double Amount)>([("North", 12), ("South", 7)], x: sale => sale.Region, y: sale => sale.Amount));
        var chart = plot.ToPdf();
        Assert.Equal("Sales by region. North sold 12 units, South 7.", Assert.Single(Assert.Single(chart.StructureTree.Elements).Children).AlternateDescription);
        Assert.Equal("Sales by region", chart.Information.Title);
        Assert.Equal("North sold 12 units, South 7.", chart.Information.Subject);

        // The accessible title of a diagram replaces its title, which is drawn
        var flowchart = new Meziantou.Framework.Diagrams.FlowchartDiagram { Title = "Checkout", AccessibleTitle = "Checkout flow", Description = "From the cart to the payment." };
        flowchart.AddEdge(flowchart.AddNode("cart", "Cart"), flowchart.AddNode("pay", "Payment"));
        var diagram = flowchart.ToPdf();
        Assert.Equal("Checkout flow. From the cart to the payment.", Assert.Single(Assert.Single(diagram.StructureTree.Elements).Children).AlternateDescription);
        Assert.Equal("Checkout flow", diagram.Information.Title);
        Assert.Contains("Checkout", diagram.Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void TransparencyGroupsContainTheMiters()
    {
        // The miter of this corner reaches 16.8 units above it, to y = 23.2: the transparency group of the faded group
        // must contain it (twice the width of the stroke above the path would stop at y = 28)
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };
        var faded = scene.Root.Add(new SceneGroup { Opacity = 0.5 });
        faded.Add(new ScenePath(new PathBuilder().MoveTo(65, 95).LineTo(75, 40).LineTo(85, 95).Build()) { Stroke = SceneColor.Black, StrokeWidth = 6, MiterLimit = 10 });
        using var image = Render(scene);
        Assert.InRange(image.Frames[0][75, 26].R, 100, 240);
        Assert.InRange(image.Frames[0][75, 35].R, 100, 160);
    }

    [Fact]
    public void TextsWithoutFontsUseHelvetica()
    {
        var measurer = ScenePdfRenderer.CreateMeasurer();
        Assert.Same(AfmTextMeasurer.Default, measurer);
        var scene = new Scene(200, 50, measurer.FontFamily);
        scene.Root.Add(new SceneText(100, 30, "Hello 中", new SceneFont(20, Weight: SceneFont.BoldWeight), SceneColor.Black) { Anchor = SceneTextAnchor.Middle });
        var document = scene.ToPdf();
        var pdf = Uncompressed(document);
        Assert.Contains("/Helvetica-Bold", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("/FontFile", pdf, StringComparison.Ordinal);
        Assert.Contains("Hello ?", document.Pages[0].ExtractText(), StringComparison.Ordinal);

        // Centered on its anchor
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var (left, right) = GetInkColumns(image);
        Assert.InRange((left + right) / 2.0, 97, 103);
    }

    [Fact]
    public void EachTextIsDrawnWithTheFaceOfItsFamily()
    {
        var fonts = SceneFontCollection.Load(Path.Combine(TestData, "LiberationSans-Regular.woff2"), Path.Combine(TestData, "LiberationMono-Regular.woff2"), Path.Combine(TestData, "NotoSansThai-Regular.ttf"));
        var measurer = ScenePdfRenderer.CreateMeasurer(new ScenePdfOptions { Fonts = fonts });
        Assert.Same(fonts, measurer.Fonts);

        var scene = new Scene(300, 60, measurer.FontFamily) { Fonts = measurer.Fonts };
        scene.Root.Add(new SceneText(0, 20, "iiiii", new SceneFont(20, Family: "Liberation Mono"), SceneColor.Black));
        scene.Root.Add(new SceneText(0, 50, "ab สวัสดี", new SceneFont(20), SceneColor.Black));
        var document = scene.ToPdf();
        var pdf = Uncompressed(document);

        // The three faces are embedded, and the text can be extracted
        Assert.Contains("LiberationMono", pdf, StringComparison.Ordinal);
        Assert.Contains("LiberationSans", pdf, StringComparison.Ordinal);
        Assert.Contains("NotoSansThai", pdf, StringComparison.Ordinal);
        var text = document.Pages[0].ExtractText();
        Assert.Contains("iiiii", text, StringComparison.Ordinal);
        Assert.Contains("สวัสดี", text, StringComparison.Ordinal);

        // 'i' is 0.6 em wide in Liberation Mono: five of them span 60 pixels
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 96 });
        var (_, right) = GetInkColumns(image, 0, 25);
        Assert.InRange(right, 50, 60);
    }

    [Fact]
    public void HyperlinksBecomeLinkAnnotations()
    {
        var scene = new Scene(100, 100, "sans-serif");
        var link = scene.Root.Add(new SceneGroup { Hyperlink = "https://example.com/", Transform = SceneTransform.Translate(10, 20) });
        link.Add(new SceneRectangle(0, 0, 40, 20) { Fill = SceneColor.Black });
        scene.Root.Add(new SceneGroup { Hyperlink = "relative/path" }).Add(new SceneRectangle(0, 0, 10, 10) { Fill = SceneColor.Black });
        var document = scene.ToPdf(new ScenePdfOptions { Scale = 1 });

        var annotation = Assert.IsType<PdfLinkAnnotation>(Assert.Single(document.Pages[0].Annotations));
        Assert.Equal(new Uri("https://example.com/"), annotation.Uri);
        Assert.Equal(10, annotation.Rectangle.Left, 1e-9);
        Assert.Equal(50, annotation.Rectangle.Right, 1e-9);
        Assert.Equal(60, annotation.Rectangle.Bottom, 1e-9);
        Assert.Equal(80, annotation.Rectangle.Top, 1e-9);
    }

    [Fact]
    public void ElementsHaveTransformsAndHyperlinks()
    {
        // A rectangle moved by its transform links to an address, over the rectangle it draws
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 20, 10) { Fill = (SceneColor)"#ff0000", Transform = SceneTransform.Translate(30, 40), Hyperlink = "https://example.com/" });
        scene.Root.Add(new SceneText(0, 0, "a", new SceneFont(10), SceneColor.Black) { Transform = SceneTransform.Translate(80, 90), Hyperlink = "https://example.com/text" });
        var document = scene.ToPdf(new ScenePdfOptions { Scale = 1 });
        using var image = document.Pages[0].Render(new PdfRenderOptions { Dpi = 72 });
        Assert.Equal(new Rgba32(255, 0, 0), image.Frames[0][40, 45]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][10, 5]);

        var annotations = document.Pages[0].Annotations.OfType<PdfLinkAnnotation>().ToList();
        Assert.HasCount(2, annotations);
        Assert.Equal(new Uri("https://example.com/"), annotations[0].Uri);
        Assert.Equal(30, annotations[0].Rectangle.Left, 1e-9);
        Assert.Equal(50, annotations[0].Rectangle.Right, 1e-9);
        Assert.Equal(60, annotations[0].Rectangle.Top, 1e-9);
        Assert.Equal(50, annotations[0].Rectangle.Bottom, 1e-9);
        Assert.InRange(annotations[1].Rectangle.Left, 79, 81);
        Assert.Equal(2, Assert.Single(Assert.Single(document.StructureTree.Elements).Children).Children.Count);
    }

    [Fact]
    public void HyperlinksOfMasksAreNotLinks()
    {
        // The content of a mask is never visible: its hyperlinks are not links of the page
        var scene = new Scene(100, 100, "sans-serif");
        var mask = new SceneMask();
        mask.Content.Add(new SceneGroup { Hyperlink = "https://example.com/hidden" }).Add(new SceneRectangle(0, 0, 50, 50) { Fill = SceneColor.White });
        scene.Root.Add(new SceneRectangle(0, 0, 100, 100) { Fill = SceneColor.Black, Mask = mask });
        var document = scene.ToPdf();
        Assert.Empty(document.Pages[0].Annotations);
        Assert.Empty(Assert.Single(Assert.Single(document.StructureTree.Elements).Children).Children);
    }

    [Fact]
    public void DrawOnAnExistingPage()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var scene = new Scene(40, 20, "sans-serif");
        scene.Root.Add(new SceneRectangle(0, 0, 40, 20) { Fill = (SceneColor)"#ff0000" });

        var bounds = page.DrawScene(scene, new PdfPoint(50, 150), new ScenePdfOptions { Scale = 2 });
        Assert.Equal(PdfRectangle.FromSize(50, 110, 80, 40), bounds);
        using var image = page.Render();
        Assert.Equal(new Rgba32(255, 0, 0), image.Frames[0][90, 70]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][90, 30]);
    }

    [Fact]
    public void InvalidArguments()
    {
        var scene = new Scene(10, 10, "sans-serif");
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ScenePdfOptions { Scale = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new ScenePdfOptions { Scale = double.PositiveInfinity });
        Assert.Throws<ArgumentNullException>("scene", () => ((Scene)null!).ToPdf());
        Assert.Throws<ArgumentNullException>("source", () => ((ISceneSource)null!).ToPdf());
        Assert.Throws<ArgumentOutOfRangeException>("scene", () => new Scene(0, 10, "sans-serif").ToPdf());
        Assert.Throws<ArgumentException>("position", () => new PdfDocument().Pages.Add(new PdfSize(10, 10)).DrawScene(scene, new PdfPoint(double.NaN, 0)));
    }

    private static string Uncompressed(PdfDocument document) => System.Text.Encoding.Latin1.GetString(document.ToByteArray(new PdfSaveOptions { CompressStreams = false, UseObjectStreams = false }));

    private static Image<Rgba32> Render(Scene scene) => scene.ToPdf().Pages[0].Render(new PdfRenderOptions { Dpi = 96 });

    private static (int Left, int Top, int Right, int Bottom) GetInkBounds(Image<Rgba32> image, int x0, int y0, int x1, int y1)
    {
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, -1, -1);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (image.Frames[0][x, y].R < 128)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (left, top, right, bottom);
    }

    private static (int Left, int Right) GetInkColumns(Image<Rgba32> image, int top = 0, int bottom = -1)
    {
        bottom = bottom < 0 ? image.Height : bottom;
        var (left, right) = (int.MaxValue, -1);
        for (var y = top; y < bottom; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.Frames[0][x, y].R < 128)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
        }

        return (left, right);
    }
}
