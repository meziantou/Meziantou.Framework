using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.TestHarness;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfRendererTests
{
    private static readonly PdfSize Size = new(100, 100);

    [Fact]
    public void Example_RenderPage()
    {
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(200, 100)).CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill();

        #region PdfRendering_RenderPage
        var page = document.Pages[0];

        // 144 dpi: two pixels per point
        using var image = PdfRenderer.RenderPage(page, new PdfRenderOptions { Dpi = 144 });
        using var png = new MemoryStream();
        image.Save(png, new Imaging.Formats.PngEncoder());
        #endregion

        Assert.NotEqual(0, png.Length);
        Assert.Equal(400, image.Width);
        Assert.Equal(200, image.Height);
        AssertColor(image.Frames[0][50, 50], 255, 0, 0);
        AssertColor(image.Frames[0][300, 50], 255, 255, 255);
    }

    [Theory]
    [InlineData(0, 10, 90)]
    [InlineData(90, 90, 90)]
    [InlineData(180, 90, 10)]
    [InlineData(270, 10, 10)]
    public void PageRotation(int rotation, int x, int y)
    {
        // A red square at the top left corner of the page; the rotation turns the page clockwise
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        page.Rotation = rotation;
        page.CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 80, 20, 20).Fill();

        using var image = Render(document);

        AssertColor(image.Frames[0][x, 100 - y - 1], 255, 0, 0);
        AssertColor(image.Frames[0][50, 50], 255, 255, 255);
    }

    [Fact]
    public void CropBox()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        page.CropBox = new PdfRectangle(50, 50, 150, 150);
        page.CreateContentBuilder().SetFillRgb(0, 0, 1).Rectangle(50, 50, 10, 10).Fill();

        using var image = Render(document);

        Assert.Equal(100, image.Width);
        AssertColor(Pixel(image, 5, 5), 0, 0, 255);
    }

    [Fact]
    public void PathsAndClipping()
    {
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder()
            .SaveGraphicsState()
            .Rectangle(0, 0, 50, 100).Clip().EndPath()
            .SetFillRgb(0, 1, 0).Rectangle(0, 0, 100, 100).Fill()
            .RestoreGraphicsState()
            .SetStrokeRgb(0, 0, 1).SetLineWidth(10).MoveTo(60, 50).LineTo(100, 50).Stroke()

            // Even-odd: the hole of two nested squares is not filled
            .SetFillRgb(1, 0, 0).Rectangle(60, 70, 30, 30).Rectangle(70, 80, 10, 10).Fill(PdfFillRule.EvenOdd);

        using var image = Render(document);

        AssertColor(Pixel(image, 25, 50), 0, 255, 0);
        AssertColor(Pixel(image, 55, 30), 255, 255, 255);
        AssertColor(Pixel(image, 80, 50), 0, 0, 255);
        AssertColor(Pixel(image, 80, 56), 255, 255, 255);
        AssertColor(Pixel(image, 63, 73), 255, 0, 0);
        AssertColor(Pixel(image, 75, 85), 255, 255, 255);
    }

    [Theory]
    [InlineData("/DeviceGray cs 0.5 sc", 128, 128, 128)]
    [InlineData("/DeviceRGB cs 0 0.5 1 sc", 0, 128, 255)]
    [InlineData("/CS0 cs 1 sc", 0, 0, 255)]
    [InlineData("/CS1 cs 0.5 sc", 128, 0, 0)]
    [InlineData("/CS2 cs 100 0 0 sc", 255, 255, 255)]
    [InlineData("/CS3 cs 1 sc", 255, 255, 255)]
    [InlineData("/CS4 cs 1 0 0 sc", 255, 0, 0)]
    [InlineData("/CS4 cs 0.5 0.5 0.5 sc", 128, 128, 128)]
    public void ColorSpaces(string setColor, int r, int g, int b)
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        var tint = new PdfStream(new PdfDictionary { [PdfName.Create("FunctionType")] = PdfInteger.Get(4), [PdfName.Create("Domain")] = PdfArray.FromNumbers(0, 1), [PdfName.Create("Range")] = PdfArray.FromNumbers(0, 1, 0, 1, 0, 1) });
        tint.SetData("{ 0 0 }"u8);
        var palette = new PdfArray(PdfName.Create("Indexed"), PdfName.Create("DeviceRGB"), PdfInteger.Get(1), PdfString.FromBytes([255, 0, 0, 0, 0, 255]));
        page.CreateContentBuilder().AppendRawContent(Encoding.ASCII.GetBytes(setColor + " 0 0 100 100 re f"));
        var resources = page.Dictionary.Get<PdfDictionary>(PdfNames.Resources) ?? new PdfDictionary();
        page.Dictionary[PdfNames.Resources] = resources;
        resources[PdfNames.ColorSpace] = new PdfDictionary
        {
            [PdfName.Create("CS0")] = palette,
            [PdfName.Create("CS1")] = new PdfArray(PdfName.Create("Separation"), PdfName.Create("Red"), PdfName.Create("DeviceRGB"), document.Objects.Add(tint)),
            [PdfName.Create("CS2")] = new PdfArray(PdfName.Create("Lab"), new PdfDictionary { [PdfName.Create("WhitePoint")] = PdfArray.FromNumbers(0.9505, 1, 1.089) }),
            [PdfName.Create("CS3")] = new PdfArray(PdfName.Create("Separation"), PdfName.Create("None"), PdfName.Create("DeviceGray"), document.Objects.Add(tint)),

            // sRGB as a CIE-based RGB color space: its primaries, the D65 white point, a gamma of 2.2
            [PdfName.Create("CS4")] = new PdfArray(PdfName.Create("CalRGB"), new PdfDictionary
            {
                [PdfName.Create("WhitePoint")] = PdfArray.FromNumbers(0.9505, 1, 1.089),
                [PdfName.Create("Gamma")] = PdfArray.FromNumbers(2.2, 2.2, 2.2),
                [PdfNames.Matrix] = PdfArray.FromNumbers(0.4124, 0.2126, 0.0193, 0.3576, 0.7152, 0.1192, 0.1805, 0.0722, 0.9505),
            }),
        };

        using var image = Render(document);

        AssertColor(Pixel(image, 50, 50), r, g, b, tolerance: 3);
    }

    [Fact]
    public void CmykIsConvertedWithAProfile()
    {
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder().SetFillCmyk(1, 0, 0, 0).Rectangle(0, 0, 50, 100).Fill().SetFillCmyk(0, 0, 0, 1).Rectangle(50, 0, 50, 100).Fill();

        using var image = Render(document);

        // Cyan ink: little red, much green and blue; black ink: dark
        var cyan = Pixel(image, 25, 50);
        Assert.True(cyan.R < 80 && cyan.G > 120 && cyan.B > 180, cyan.ToString());
        Assert.True(Pixel(image, 75, 50).R < 60);
    }

    [Fact]
    public void Images()
    {
        var document = new PdfDocument();
        using var rgba = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 255, 255), new Rgba32(0, 0, 0, 0)], 2, 2);
        using var gray = Image.ImportPixelData<Gray16>([new Gray16(0), new Gray16(65535)], 2, 1);
        using var jpegSource = Image.ImportPixelData<Rgb24>(Enumerable.Repeat(new Rgb24(0, 0, 255), 64).ToArray(), 8, 8);
        using var jpeg = new MemoryStream();
        jpegSource.Save(jpeg, new Imaging.Formats.JpegEncoder());
        document.Pages.Add(Size).CreateContentBuilder()
            .DrawImage(PdfImage.FromImage(rgba), 0, 50, 50, 50)
            .DrawImage(PdfImage.FromImage(gray), 50, 50, 50, 50)
            .DrawImage(PdfImage.Load(jpeg.ToArray()), 0, 0, 50, 50);

        using var image = Render(document);

        AssertColor(Pixel(image, 12, 88), 255, 0, 0);
        AssertColor(Pixel(image, 38, 88), 0, 255, 0);
        AssertColor(Pixel(image, 12, 62), 0, 0, 255);

        // The soft mask makes the last pixel transparent
        AssertColor(Pixel(image, 38, 62), 255, 255, 255);
        AssertColor(Pixel(image, 62, 75), 0, 0, 0);
        AssertColor(Pixel(image, 88, 75), 255, 255, 255);
        AssertColor(Pixel(image, 25, 25), 0, 0, 255, tolerance: 8);
    }

    [Fact]
    public void CmykJpegImages()
    {
        // A CMYK JPEG file of WPT (html/canvas/element/manual/wide-gamut-canvas/resources) written by an Adobe application
        // (APP14 segment, inverted inks) with its CMYK profile: 100% cyan
        var jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Generic-CMYK-FF000000.jpg"));
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder().DrawImage(PdfImage.FromJpeg(jpeg), 0, 0, 100, 100);

        using var image = Render(document);

        // The color Poppler renders
        AssertColor(Pixel(image, 50, 50), 0, 163, 219, tolerance: 3);
    }

    [Fact]
    public void CmykJpegImagesWithoutProfileUseTheProfileOfTheColorSpace()
    {
        // The same file without its APP2 segments: the CMYK profile is the one of the ICCBased color space of the image
        var jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Generic-CMYK-FF000000.jpg"));
        using var profile = new MemoryStream();
        using var withoutProfile = new MemoryStream();
        withoutProfile.Write(jpeg.AsSpan(0, 2));
        var position = 2;
        while (jpeg[position + 1] != 0xDA)
        {
            var length = 2 + ((jpeg[position + 2] << 8) | jpeg[position + 3]);
            var segment = jpeg.AsSpan(position, length);
            if (segment[1] == 0xE2 && segment[4..].StartsWith("ICC_PROFILE\0"u8))
            {
                profile.Write(segment[18..]);
            }
            else
            {
                withoutProfile.Write(segment);
            }

            position += length;
        }

        withoutProfile.Write(jpeg.AsSpan(position));
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent("q 100 0 0 100 0 0 cm /Im0 Do Q"u8);
        var iccProfile = new PdfStream(new PdfDictionary { [PdfNames.N] = PdfInteger.Get(4) }, profile.ToArray());
        var image = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfName.Create("Image"),
            [PdfNames.Width] = PdfInteger.Get(2),
            [PdfNames.Height] = PdfInteger.Get(2),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(8),
            [PdfNames.ColorSpace] = new PdfArray(PdfName.Create("ICCBased"), document.Objects.Add(iccProfile)),
            [PdfNames.Decode] = PdfArray.FromNumbers(1, 0, 1, 0, 1, 0, 1, 0),
        });
        image.SetEncodedData(withoutProfile.ToArray(), PdfNames.DCTDecode);
        var resources = page.Dictionary.Get<PdfDictionary>(PdfNames.Resources) ?? new PdfDictionary();
        page.Dictionary[PdfNames.Resources] = resources;
        resources[PdfNames.XObject] = new PdfDictionary { [PdfName.Create("Im0")] = document.Objects.Add(image) };

        using var rendered = Render(document);

        AssertColor(Pixel(rendered, 50, 50), 0, 163, 219, tolerance: 3);
    }

    [Theory]
    [InlineData("/W 2 /H 1 /BPC 1 /IM true", "@", 0, 255, 0, 255, 255, 255)]
    [InlineData("/W 2 /H 1 /BPC 1 /IM true /D [1 0]", "@", 255, 255, 255, 0, 255, 0)]
    [InlineData("/W 2 /H 1 /BPC 8 /CS /G", "\u0000\u00FF", 0, 0, 0, 255, 255, 255)]
    [InlineData("/W 2 /H 1 /BPC 8 /CS /G /D [1 0]", "\u0000\u00FF", 255, 255, 255, 0, 0, 0)]
    [InlineData("/W 2 /H 1 /BPC 4 /CS /RGB", "\u00F0\u0000\u000F", 255, 0, 0, 0, 0, 255)]
    [InlineData("/W 2 /H 1 /BPC 8 /CS /G /F /AHx", "00FF>", 0, 0, 0, 255, 255, 255)]
    public void InlineImages(string dictionary, string data, int r1, int g1, int b1, int r2, int g2, int b2)
    {
        var document = new PdfDocument();
        var content = Encoding.Latin1.GetBytes("0 1 0 rg q 100 0 0 100 0 0 cm BI " + dictionary + " ID " + data + " EI Q");
        document.Pages.Add(Size).CreateContentBuilder().AppendRawContent(content);

        using var image = Render(document);

        AssertColor(Pixel(image, 25, 50), r1, g1, b1);
        AssertColor(Pixel(image, 75, 50), r2, g2, b2);
    }

    [Fact]
    public void EmbeddedFontsAreDrawn()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder().DrawText("H", font, 100, 10, 20);

        using var image = Render(document);

        // The left stem of the H, and the inside of its upper half
        Assert.True(Pixel(image, 22, 50).R < 64);
        Assert.True(Pixel(image, 40, 70).R > 192);
    }

    [Fact]
    public void FontsThatAreNotEmbeddedAreReplaced()
    {
        var face = Framework.Fonts.FontLoader.Load(File.ReadAllBytes(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf"), new Framework.Fonts.PooledFontEnvironment(Framework.Fonts.FontResourceLimits.Default, 1 << 24))[0];
        var requests = new List<PdfFontRequest>();
        var options = new PdfRenderOptions { FontResolver = request => { requests.Add(request); return face; }, UseSystemFonts = false };
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder().DrawText("H", PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold), 100, 10, 20);

        using var image = PdfRenderer.RenderPage(document.Pages[0], options);

        var request = Assert.Single(requests);
        Assert.Equal("Helvetica-Bold", request.StandardName);
        Assert.True(request.IsBold);
        Assert.True(Pixel(image, 22, 50).R < 64);
    }

    [Fact]
    public void StandardFontsAreReplacedByBundledFonts()
    {
        // Without the fonts of the system, a bundled font draws the H of Helvetica Bold: 72 points wide at 100 points
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder()
            .DrawText("H", PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold), 100, 10, 20);

        using var image = Render(document);

        Assert.True(Pixel(image, 22, 50).R < 64);
        Assert.True(Pixel(image, 50, 70).R > 192);
        Assert.True(Pixel(image, 72, 50).R < 64);
    }

    [Fact]
    public void Type3Glyphs()
    {
        // A Type 3 font whose glyph "square" is a filled square of the glyph space; d1 makes it a stencil of the fill color
        var document = new PdfDocument();
        var procedure = new PdfStream();
        procedure.SetData("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f"u8);
        var font = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Font,
            [PdfNames.Subtype] = PdfNames.Type3,
            [PdfNames.FontBBox] = PdfArray.FromNumbers(0, 0, 1000, 1000),
            [PdfNames.FontMatrix] = PdfArray.FromNumbers(0.001, 0, 0, 0.001, 0, 0),
            [PdfNames.CharProcs] = new PdfDictionary { [PdfName.Create("square")] = document.Objects.Add(procedure) },
            [PdfNames.Encoding] = new PdfDictionary { [PdfNames.Differences] = new PdfArray(PdfInteger.Get(65), PdfName.Create("square")) },
            [PdfNames.FirstChar] = PdfInteger.Get(65),
            [PdfNames.LastChar] = PdfInteger.Get(65),
            [PdfNames.Widths] = PdfArray.FromNumbers(1000),
        };
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent("0 0 1 rg BT /T3 40 Tf 10 10 Td (AA) Tj ET"u8);
        page.Dictionary[PdfNames.Resources] = new PdfDictionary { [PdfNames.Font] = new PdfDictionary { [PdfName.Create("T3")] = document.Objects.Add(font) } };

        using var image = Render(document);

        AssertColor(Pixel(image, 30, 30), 0, 0, 255);
        AssertColor(Pixel(image, 70, 30), 0, 0, 255);
        AssertColor(Pixel(image, 95, 30), 255, 255, 255);
        AssertColor(Pixel(image, 30, 60), 255, 255, 255);
    }

    [Fact]
    public void TextClippingModes()
    {
        // Mode 7 adds the glyphs to the clipping path: the rectangle painted after is visible only inside the H
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder()
            .BeginText().SetFont(font, 100).SetTextRenderingMode(PdfTextRenderingMode.Clip).MoveTextPosition(10, 20).ShowText("H").EndText()
            .SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill();

        using var image = Render(document);

        AssertColor(Pixel(image, 22, 50), 255, 0, 0);
        AssertColor(Pixel(image, 40, 70), 255, 255, 255);
    }

    [Fact]
    public void TextClipWithoutGlyphOutlinesHidesEverything()
    {
        // A space has no outline: the clip of mode 7 is empty
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder()
            .BeginText().SetFont(PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 100).SetTextRenderingMode(PdfTextRenderingMode.Clip).MoveTextPosition(10, 20).ShowText(" ").EndText()
            .SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill();

        using var image = Render(document);

        AssertColor(Pixel(image, 22, 50), 255, 255, 255);
        AssertColor(Pixel(image, 90, 90), 255, 255, 255);
    }

    [Fact]
    public void TransferFunctionsOfTheGraphicsState()
    {
        // A transfer function that inverts the components: the red paint is cyan; TR2 replaces TR
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent("q /GS0 gs 1 0 0 rg 0 0 50 100 re f Q q /GS1 gs 1 0 0 rg 50 0 50 100 re f Q"u8);
        var invert = new PdfDictionary
        {
            [PdfName.Create("FunctionType")] = PdfInteger.Get(2),
            [PdfName.Create("Domain")] = PdfArray.FromNumbers(0, 1),
            [PdfName.Create("C0")] = PdfArray.FromNumbers(1),
            [PdfName.Create("C1")] = PdfArray.FromNumbers(0),
            [PdfName.Create("N")] = PdfInteger.Get(1),
        };
        var resources = page.Dictionary.Get<PdfDictionary>(PdfNames.Resources) ?? new PdfDictionary();
        page.Dictionary[PdfNames.Resources] = resources;
        resources[PdfNames.ExtGState] = new PdfDictionary
        {
            [PdfName.Create("GS0")] = new PdfDictionary { [PdfName.Create("TR")] = invert },
            [PdfName.Create("GS1")] = new PdfDictionary { [PdfName.Create("TR")] = invert, [PdfName.Create("TR2")] = PdfName.Create("Default") },
        };

        using var image = Render(document);

        AssertColor(Pixel(image, 25, 50), 0, 255, 255);
        AssertColor(Pixel(image, 75, 50), 255, 0, 0);
    }

    [Fact]
    public void ANonIsolatedGroupMixesWithThePage()
    {
        // On red, green multiplied by the backdrop is black: a non-isolated group drawn at an alpha of 0.5 is half black
        // over red (11.4.8); an isolated group multiplies green by its transparent backdrop, which keeps green
        foreach (var (isolated, expected) in new[] { (false, (128, 0, 0)), (true, (128, 128, 0)) })
        {
            var document = new PdfDocument();
            var form = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup { Isolated = isolated });
            form.CreateContentBuilder().SetGraphicsState(new PdfExtGState { BlendMode = PdfBlendMode.Multiply }).SetFillRgb(0, 1, 0).Rectangle(0, 0, 100, 100).Fill();
            document.Pages.Add(Size).CreateContentBuilder()
                .SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill()
                .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5 }).DrawForm(form);

            using var image = Render(document);

            AssertColor(Pixel(image, 50, 50), expected.Item1, expected.Item2, expected.Item3);
        }
    }

    [Fact]
    public void TheBackdropOfANonIsolatedGroupIsCountedOnce()
    {
        // An isolated group of red at 0.5, then a non-isolated group of blue at 0.5 in it, on white: blue over red over
        // white, (128, 64, 191); without the removal of the backdrop, the red would be composited twice
        var document = new PdfDocument();
        var inner = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup());
        inner.CreateContentBuilder().SetGraphicsState(new PdfExtGState { FillAlpha = 0.5 }).SetFillRgb(0, 0, 1).Rectangle(0, 0, 100, 100).Fill();
        var outer = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup { Isolated = true });
        outer.CreateContentBuilder()
            .SaveGraphicsState().SetGraphicsState(new PdfExtGState { FillAlpha = 0.5 }).SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill().RestoreGraphicsState()
            .SetGraphicsState(new PdfExtGState { BlendMode = PdfBlendMode.Normal, FillAlpha = 0.99 })
            .DrawForm(inner);
        document.Pages.Add(Size).CreateContentBuilder().DrawForm(outer);

        using var image = Render(document);

        AssertColor(Pixel(image, 50, 50), 128, 64, 191, tolerance: 3);
    }

    [Fact]
    public void TheElementsOfAKnockoutGroupKnockOutOneAnother()
    {
        // Red from 0 to 60, then blue from 40 to 100, each at an alpha of 0.5: in a knockout group, blue replaces red
        static PdfDocument Create(bool isolated, double groupAlpha, PdfColor page)
        {
            var document = new PdfDocument();
            var form = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup { Isolated = isolated, Knockout = true });
            form.CreateContentBuilder()
                .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5 })
                .SetFillRgb(1, 0, 0).Rectangle(0, 0, 60, 100).Fill()
                .SetFillRgb(0, 0, 1).Rectangle(40, 0, 60, 100).Fill();
            document.Pages.Add(Size).CreateContentBuilder()
                .SetFillColor(page).Rectangle(0, 0, 100, 100).Fill()
                .SetGraphicsState(new PdfExtGState { FillAlpha = groupAlpha }).DrawForm(form);
            return document;
        }

        // Isolated, on white: the overlap is blue at 0.5 over white, where a group that is not knockout gives (128, 64, 191)
        using (var image = Render(Create(isolated: true, 1, PdfColor.FromRgb(1, 1, 1))))
        {
            AssertColor(Pixel(image, 20, 50), 255, 128, 128);
            AssertColor(Pixel(image, 50, 50), 128, 128, 255);
            AssertColor(Pixel(image, 80, 50), 128, 128, 255);
        }

        // Non-isolated, on yellow: each element is composited with yellow, blue over yellow is (128, 128, 128)
        using (var image = Render(Create(isolated: false, 1, PdfColor.FromRgb(1, 1, 0))))
        {
            AssertColor(Pixel(image, 20, 50), 255, 128, 0);
            AssertColor(Pixel(image, 50, 50), 128, 128, 128);
        }

        // The group at an alpha of 0.5: half of the result of the group over yellow
        using (var image = Render(Create(isolated: false, 0.5, PdfColor.FromRgb(1, 1, 0))))
        {
            AssertColor(Pixel(image, 50, 50), 191, 191, 64);
        }
    }

    [Fact]
    public void TheStrokeOfAPathDoesNotKnockOutItsFill()
    {
        // In a knockout group, a path filled in red and stroked in blue, each at 0.5, is one element: on the inner half
        // of the stroke, blue over red over white (11.7.4.4)
        var document = new PdfDocument();
        var form = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup { Isolated = true, Knockout = true });
        form.CreateContentBuilder()
            .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5, StrokeAlpha = 0.5 })
            .SetFillRgb(1, 0, 0).SetStrokeRgb(0, 0, 1).SetLineWidth(10)
            .Rectangle(20, 20, 60, 60).FillAndStroke();
        document.Pages.Add(Size).CreateContentBuilder().DrawForm(form);

        using var image = Render(document);

        AssertColor(Pixel(image, 22, 50), 128, 64, 191);
        AssertColor(Pixel(image, 50, 50), 255, 128, 128);
        AssertColor(Pixel(image, 17, 50), 128, 128, 255);
    }

    [Fact]
    public void AKnockoutPageGroupKnocksOutTheObjectsOfThePage()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder()
            .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5 })
            .SetFillRgb(1, 0, 0).Rectangle(0, 0, 60, 100).Fill()
            .SetFillRgb(0, 0, 1).Rectangle(40, 0, 60, 100).Fill();
        page.Dictionary[PdfNames.Group] = new PdfDictionary
        {
            [PdfNames.S] = PdfName.Create("Transparency"),
            [PdfNames.K] = PdfBoolean.True,
        };

        using var image = Render(document);

        AssertColor(Pixel(image, 20, 50), 255, 128, 128);
        AssertColor(Pixel(image, 50, 50), 128, 128, 255);
    }

    [Fact]
    public void ABlendModeWithASoftMaskMixesWithTheBackdrop()
    {
        // On yellow, cyan multiplied by yellow is green, through a luminosity mask that is white on the left half and
        // black on the right half: the mask is applied after the blend mode (11.6.4.3)
        var document = new PdfDocument();
        var maskGroup = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Rgb });
        maskGroup.CreateContentBuilder().SetFillRgb(1, 1, 1).Rectangle(0, 0, 50, 100).Fill();
        document.Pages.Add(Size).CreateContentBuilder()
            .SetFillRgb(1, 1, 0).Rectangle(0, 0, 100, 100).Fill()
            .SetGraphicsState(new PdfExtGState { BlendMode = PdfBlendMode.Multiply, SoftMask = PdfSoftMask.FromLuminosity(maskGroup) })
            .SetFillRgb(0, 1, 1).Rectangle(0, 0, 100, 100).Fill();

        using var image = Render(document);

        AssertColor(Pixel(image, 25, 50), 0, 255, 0);
        AssertColor(Pixel(image, 75, 50), 255, 255, 0);
    }

    [Fact]
    public void OptionalContentThatIsOffIsHidden()
    {
        var document = new PdfDocument();
        var group = document.Objects.Add(new PdfDictionary { [PdfNames.Type] = PdfName.Create("OCG"), [PdfNames.Name] = PdfString.FromText("Hidden") });
        document.Catalog[PdfName.Create("OCProperties")] = new PdfDictionary
        {
            [PdfName.Create("OCGs")] = new PdfArray(group),
            [PdfNames.D] = new PdfDictionary { [PdfName.Create("OFF")] = new PdfArray(group) },
        };
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent("/OC /G1 BDC 1 0 0 rg 0 0 50 100 re f EMC 0 0 1 rg 50 0 50 100 re f"u8);
        page.Dictionary[PdfNames.Resources] = new PdfDictionary { [PdfNames.Properties] = new PdfDictionary { [PdfName.Create("G1")] = group } };

        using var image = Render(document);

        AssertColor(Pixel(image, 25, 50), 255, 255, 255);
        AssertColor(Pixel(image, 75, 50), 0, 0, 255);
    }

    [Fact]
    public void AnnotationAppearances()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        var appearance = new PdfStream(new PdfDictionary { [PdfNames.Type] = PdfNames.XObject, [PdfNames.Subtype] = PdfNames.Form, [PdfNames.BBox] = PdfArray.FromNumbers(0, 0, 10, 10) });
        appearance.SetData("0 1 0 rg 0 0 10 10 re f"u8);
        PdfDictionary Annotation(double x, int flags) => new()
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfName.Create("Square"),
            [PdfNames.Rect] = PdfArray.FromNumbers(x, 0, x + 50, 100),
            [PdfName.Create("F")] = PdfInteger.Get(flags),
            [PdfName.Create("AP")] = new PdfDictionary { [PdfNames.N] = document.Objects.Add(appearance) },
        };
        page.Dictionary[PdfNames.Annots] = new PdfArray(document.Objects.Add(Annotation(0, 4)), document.Objects.Add(Annotation(50, 2)));

        using var image = Render(document);
        using var withoutAnnotations = PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { RenderAnnotations = false });

        // The appearance is scaled from its bounding box to the rectangle; the hidden annotation is not drawn
        AssertColor(Pixel(image, 25, 50), 0, 255, 0);
        AssertColor(Pixel(image, 75, 50), 255, 255, 255);
        AssertColor(Pixel(withoutAnnotations, 25, 50), 255, 255, 255);
    }

    [Fact]
    public void InvalidOperatorsAreSkipped()
    {
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder().AppendRawContent("1 0 0 rg /Missing gs /Missing Do (x) Tj 0 0 50 100 re f /Pattern cs /Missing scn 50 0 50 100 re f"u8);

        using var image = Render(document);

        AssertColor(Pixel(image, 25, 50), 255, 0, 0);
        AssertColor(Pixel(image, 75, 50), 255, 255, 255);
    }

    [Fact]
    public void PathsAndGlyphsBeyondTheCoordinatesOfTheCanvasAreSkipped()
    {
        // A scale that takes the paths, the clips, the glyphs and the bounding boxes of forms beyond 2^24 pixels, and
        // points beyond 2^24: they are not drawn, and the page is
        var document = new PdfDocument();
        var form = document.CreateForm(new PdfRectangle(0, 0, 100_000_000, 100_000_000));
        form.CreateContentBuilder().Rectangle(0, 0, 1, 1).Fill();
        var group = document.CreateForm(new PdfRectangle(0, 0, 100_000_000, 100_000_000), group: new PdfTransparencyGroup());
        group.CreateContentBuilder().Rectangle(0, 0, 1, 1).Fill();
        document.Pages.Add(Size).CreateContentBuilder()
            .AppendRawContent("1 0 0 rg 0 0 50 100 re f q 100000000 0 0 100000000 0 0 cm 0 0 1 1 re f 0 0 1 1 re S"u8)
            .DrawText("H", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10, 0, 0)
            .AppendRawContent("0 0 1 1 re W n Q 0 0 100000000 1 re f q 0.5 0 0 0.5 0 0 cm"u8)
            .DrawForm(form)
            .DrawForm(group)
            .AppendRawContent("Q 0 0 1 rg 50 0 50 100 re f"u8);

        using var image = Render(document);

        AssertColor(Pixel(image, 25, 50), 255, 0, 0);
        AssertColor(Pixel(image, 75, 50), 0, 0, 255);
    }

    [Fact]
    public void ColorSpacesAndFunctionsWithTooManyComponentsAreSkipped()
    {
        // A DeviceN color space of 1000 colorants, and a function of 1000 outputs: their values would not fit the stack.
        // The color spaces are not set, and the page is rendered.
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent("1 0 0 rg 0 0 100 100 re f /CS0 cs 0 sc 0 0 50 100 re f /CS1 cs 0 sc 50 0 50 100 re f"u8);
        var names = new PdfArray();
        for (var i = 0; i < 1000; i++)
        {
            names.Add(PdfName.Create("Ink" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var outputs = new PdfArray();
        for (var i = 0; i < 1000; i++)
        {
            outputs.Add(PdfInteger.Get(0));
        }

        var function = new PdfDictionary
        {
            [PdfName.Create("FunctionType")] = PdfInteger.Get(2),
            [PdfName.Create("Domain")] = PdfArray.FromNumbers(0, 1),
            [PdfName.Create("C0")] = outputs,
            [PdfName.Create("C1")] = outputs,
            [PdfName.Create("N")] = PdfInteger.Get(1),
        };
        var resources = page.Dictionary.Get<PdfDictionary>(PdfNames.Resources) ?? new PdfDictionary();
        page.Dictionary[PdfNames.Resources] = resources;
        resources[PdfNames.ColorSpace] = new PdfDictionary
        {
            [PdfName.Create("CS0")] = new PdfArray(PdfName.Create("DeviceN"), names, PdfName.Create("DeviceRGB"), function),
            [PdfName.Create("CS1")] = new PdfArray(PdfName.Create("Separation"), PdfName.Create("Ink"), PdfName.Create("DeviceRGB"), function),
        };

        using var image = Render(document);

        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
    }

    [Fact]
    public void WorkIsBounded()
    {
        // A form that paints itself through another form: the nesting is bounded, and the operators are counted
        var document = new PdfDocument();
        var form = document.CreateForm(new PdfRectangle(0, 0, 100, 100));
        var builder = form.CreateContentBuilder();
        for (var i = 0; i < 100; i++)
        {
            builder.Rectangle(0, 0, 1, 1).Fill();
        }

        var outer = document.CreateForm(new PdfRectangle(0, 0, 100, 100));
        var outerBuilder = outer.CreateContentBuilder();
        for (var i = 0; i < 1000; i++)
        {
            outerBuilder.DrawForm(form);
        }

        var pageBuilder = document.Pages.Add(Size).CreateContentBuilder();
        for (var i = 0; i < 1000; i++)
        {
            pageBuilder.DrawForm(outer);
        }

        Assert.Throws<PdfResourceLimitException>(() => PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { MaxOperations = 1_000_000 }));
        Assert.Throws<PdfResourceLimitException>(() => PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { Dpi = 100_000 }));
    }

    private static Image<Rgba32> Render(PdfDocument document) => PdfRenderer.RenderPage(PdfDocument.Open(document.ToByteArray()).Pages[0], new PdfRenderOptions { UseSystemFonts = false });

    /// <summary>Gets the pixel at a point of the page at 72 dpi: the y axis of the page goes up.</summary>
    private static Rgba32 Pixel(Image<Rgba32> image, int x, int y) => image.Frames[0][x, image.Height - 1 - y];

    private static void AssertColor(Rgba32 pixel, int r, int g, int b, int tolerance = 2)
    {
        Assert.True(Math.Abs(pixel.R - r) <= tolerance && Math.Abs(pixel.G - g) <= tolerance && Math.Abs(pixel.B - b) <= tolerance, $"The pixel is ({pixel.R}, {pixel.G}, {pixel.B}), not ({r}, {g}, {b})");
    }

    [Fact]
    public void Example_RenderThumbnail()
    {
        using var directory = TemporaryDirectory.Create();
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 0, 150, 150).Fill();
        document.Pages.Add(new PdfSize(300, 150)).Rotation = 90;
        document.Save(directory / "input.pdf");

        RenderThumbnails(directory / "input.pdf", directory);

        using var first = Image.Load<Rgba32>(directory / "page-1.png");
        using var second = Image.Load<Rgba32>(directory / "page-2.png");
        Assert.Equal(200, first.Width);
        Assert.Equal(100, first.Height);
        Assert.Equal(200, second.Width);
        Assert.Equal(400, second.Height);
        AssertColor(first.Frames[0][50, 50], 255, 0, 0);
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region PdfRendering_RenderThumbnails
    public static void RenderThumbnails(string pdfPath, string outputDirectory)
    {
        var document = PdfDocument.Open(pdfPath);
        for (var i = 0; i < document.Pages.Count; i++)
        {
            // The width of the page as it is shown: a rotation of 90 or 270 degrees exchanges its sides
            var page = document.Pages[i];
            var width = page.Rotation is 90 or 270 ? page.CropBox.Height : page.CropBox.Width;

            // 72 dpi is one pixel per point: the resolution that gives images 200 pixels wide
            var options = new PdfRenderOptions
            {
                Dpi = 72 * 200 / width,
                UseSystemFonts = false, // the same pixels on every machine
            };

            using var image = page.Render(options);
            image.Save(Path.Combine(outputDirectory, $"page-{i + 1}.png"));
        }
    }
    #endregion
#pragma warning restore xUnit1013

    [Fact]
    public void Example_FontResolver()
    {
        var fontData = File.ReadAllBytes(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /TrueType /BaseFont /Corporate-Sans /FirstChar 72 /LastChar 72 /Widths [722] /Encoding /WinAnsiEncoding >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 100 Tf 10 20 Td (H) Tj ET"))
            .ToArray();

        #region PdfRendering_FontResolver
        // A font of the application, loaded once
        var corporateSans = FontLoader.Load(fontData, new PooledFontEnvironment(FontResourceLimits.Default, maxLiveAllocationBytes: 64 * 1024 * 1024))[0];

        var options = new PdfRenderOptions
        {
            // Called for each font a document does not embed; null lets the library choose
            FontResolver = request => request.Name.StartsWith("Corporate", StringComparison.Ordinal) ? corporateSans : null,
            UseSystemFonts = false,
        };

        var document = PdfDocument.Open(file);
        using var image = document.Pages[0].Render(options);
        #endregion

        // The resolver is called once, for the font the document does not embed, and gives the font of the application
        var faces = new List<(string Name, FontFace? Face)>();
        using var recorded = document.Pages[0].Render(new PdfRenderOptions
        {
            FontResolver = request =>
            {
                var face = options.FontResolver!(request);
                faces.Add((request.Name, face));
                return face;
            },
            UseSystemFonts = false,
        });
        var (name, resolved) = Assert.Single(faces);
        Assert.Equal("Corporate-Sans", name);
        Assert.Same(corporateSans, resolved);
        Assert.True(Pixel(image, 22, 50).R < 64);
    }
}
