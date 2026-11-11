using Meziantou.Framework.Charts;
using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.Pdf.VectorGraphics.Tests;

public sealed class ScenePdfRendererTests
{
    private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void DrawAChartAsPdf()
    {
        var testData = TestData;

        #region PdfVectorGraphics_ToPdf
        // The fonts of the texts: the layout measures the texts with them, and the PDF embeds the subsets it uses
        var fonts = SceneFontCollection.Load(Path.Combine(testData, "LiberationSans-Regular.woff2"), Path.Combine(testData, "LiberationSans-Bold.woff2"));

        var plot = new Plot { Title = "Sales by region", Width = 400, Height = 300 };
        plot.Add(new BarMark<(string Region, double Amount)>([("North", 12), ("South", 7)], x: sale => sale.Region, y: sale => sale.Amount));

        var document = plot.ToPdf(new ScenePdfOptions { Fonts = fonts });
        var pdf = document.ToByteArray();
        #endregion

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
    public void TextsWithoutFontsUseHelvetica()
    {
        var measurer = ScenePdfRenderer.CreateMeasurer();
        Assert.Same(AfmTextMeasurer.Default, measurer);
        var scene = new Scene(200, 50, measurer.FontFamily);
        scene.Root.Add(new SceneText(100, 30, "Hello 中", new SceneFont(20, Bold: true), SceneColor.Black) { Anchor = SceneTextAnchor.Middle });
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
