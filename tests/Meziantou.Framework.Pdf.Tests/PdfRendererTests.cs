using System.Globalization;
using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.TestHarness;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfRendererTests
{
    // A ToUnicode CMap that maps the code 0001 to H
    private const string ToUnicodeH = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Test def 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfchar <0001> <0048> endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";

    private static readonly PdfSize Size = new(100, 100);

    // The glyph procedure of Type3SquareFont: a square of the glyph space, painted with the fill color (d1)
    private static readonly string Type3SquareProcedure = HandWrittenPdf.Stream("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f");

    // A 4 x 4 threshold array of 16 levels: at the gray 0.5, the pixels whose x + y is even are white
    private static readonly byte[] BayerThresholds = [8, 136, 40, 168, 200, 72, 232, 104, 56, 184, 24, 152, 248, 120, 216, 88];

    private static readonly PdfRenderOptions HalftoneOptions = new() { UseSystemFonts = false, UseHalftones = true, PathAntialiasing = false };

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
    [InlineData("/CS5 cs 0.5 sc", 128, 128, 128)]
    [InlineData("/CS6 cs 0.5 sc", 188, 188, 188)]
    [InlineData("/CS7 cs 1 0 0 sc", 128, 0, 0)]
    [InlineData("/CS7 cs 2 0 0 sc", 255, 0, 0)]
    [InlineData("/CS8 cs 0 sc", 255, 255, 255)]
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

            // CIE-based grays: the gamma of sRGB gives the gray of sRGB; a gamma of 1 is linear light, 0.5 being 188 in sRGB
            [PdfName.Create("CS5")] = new PdfArray(PdfName.Create("CalGray"), new PdfDictionary { [PdfName.Create("WhitePoint")] = PdfArray.FromNumbers(0.9505, 1, 1.089), [PdfName.Create("Gamma")] = PdfReal.Get(2.2) }),
            [PdfName.Create("CS6")] = new PdfArray(PdfName.Create("CalGray"), new PdfDictionary { [PdfName.Create("WhitePoint")] = PdfArray.FromNumbers(0.9505, 1, 1.089) }),

            // The sRGB profile with components from 0 to 2: 1 is the middle of the range
            [PdfName.Create("CS7")] = new PdfArray(PdfName.Create("ICCBased"), document.Objects.Add(CreateIccStream(IccProfile.Srgb, 3, PdfArray.FromNumbers(0, 2, 0, 2, 0, 2)))),

            // An indexed color space whose base has a range: the bytes of the table are scaled to it
            [PdfName.Create("CS8")] = new PdfArray(PdfName.Create("Indexed"), new PdfArray(PdfName.Create("ICCBased"), document.Objects.Add(CreateIccStream(IccProfile.Srgb, 3, PdfArray.FromNumbers(0, 2, 0, 2, 0, 2)))), PdfInteger.Get(0), PdfString.FromBytes([255, 255, 255])),
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
    [InlineData(1, 255, 0, 0)]
    [InlineData(0, 76, 85, 255)] // The YCbCr samples of red drawn as RGB
    public void DctColorTransformIsHonored(int colorTransform, int r, int g, int b)
    {
        using var red = Image.ImportPixelData<Rgb24>([.. Enumerable.Repeat(new Rgb24(255, 0, 0), 64)], 8, 8);
        using var jpeg = new MemoryStream();
        red.Save(jpeg, new Imaging.Formats.JpegEncoder { Quality = 100, ChromaSubsampling = Imaging.Formats.JpegChromaSubsampling.Ratio444 });
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent("q 100 0 0 100 0 0 cm /Im0 Do Q"u8);
        var image = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfName.Create("Image"),
            [PdfNames.Width] = PdfInteger.Get(8),
            [PdfNames.Height] = PdfInteger.Get(8),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(8),
            [PdfNames.ColorSpace] = PdfNames.DeviceRGB,
        });
        image.SetEncodedData(jpeg.ToArray(), PdfNames.DCTDecode, new PdfDictionary { [PdfName.Create("ColorTransform")] = PdfInteger.Get(colorTransform) });
        var resources = page.Dictionary.Get<PdfDictionary>(PdfNames.Resources) ?? new PdfDictionary();
        page.Dictionary[PdfNames.Resources] = resources;
        resources[PdfNames.XObject] = new PdfDictionary { [PdfName.Create("Im0")] = document.Objects.Add(image) };

        using var rendered = Render(document);

        AssertColor(Pixel(rendered, 50, 50), r, g, b, tolerance: 4);
    }

    [Theory]
    [InlineData("/W 2 /H 1 /BPC 1 /IM true", "@", 0, 255, 0, 255, 255, 255)]
    [InlineData("/W 2 /H 1 /BPC 1 /IM true /D [1 0]", "@", 255, 255, 255, 0, 255, 0)]
    [InlineData("/W 2 /H 1 /BPC 8 /CS /G", "\u0000\u00FF", 0, 0, 0, 255, 255, 255)]
    [InlineData("/W 2 /H 1 /BPC 8 /CS /G /D [1 0]", "\u0000\u00FF", 255, 255, 255, 0, 0, 0)]
    [InlineData("/W 2 /H 1 /BPC 4 /CS /RGB", "\u00F0\u0000\u000F", 255, 0, 0, 0, 0, 255)]
    [InlineData("/W 2 /H 1 /BPC 8 /CS /G /F /AHx", "00FF>", 0, 0, 0, 255, 255, 255)]
    [InlineData("/W 2 /H 1 /BPC 8 /CS /RGB /Mask [0 10 0 0 0 0] /F /AHx", "0A0000C80000>", 255, 255, 255, 200, 0, 0)]
    [InlineData("/W 2 /H 1 /BPC 4 /CS /G /Mask [0 5]", "\u0038", 255, 255, 255, 136, 136, 136)]
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
    public void InlineImagesHaveTheDecodeLimitsOfTheDocument()
    {
        // An inline image of 200 samples in 4 bytes of RunLengthDecode (two runs of 100 zeros): within the limit of the
        // content stream, beyond the limit of the decoded data the document is opened with
        var document = new PdfDocument();
        var content = Encoding.Latin1.GetBytes("q 100 0 0 100 0 0 cm BI /W 200 /H 1 /BPC 8 /CS /G /F /RL ID \u009D\u0000\u009D\u0000\u0080 EI Q");
        document.Pages.Add(Size).CreateContentBuilder().AppendRawContent(content);
        var data = document.ToByteArray(new PdfSaveOptions { CompressStreams = false });

        using var image = PdfRenderer.RenderPage(PdfDocument.Open(data, new PdfReadOptions { MaxDecodedStreamLength = 1000 }).Pages[0], new PdfRenderOptions { UseSystemFonts = false });
        AssertColor(Pixel(image, 50, 50), 0, 0, 0);
        Assert.Throws<PdfResourceLimitException>(() => PdfRenderer.RenderPage(PdfDocument.Open(data, new PdfReadOptions { MaxDecodedStreamLength = 150 }).Pages[0], new PdfRenderOptions { UseSystemFonts = false }));
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
    public void Example_RenderHalftones()
    {
        // A page whose gray is halftoned by a 4 x 4 threshold array (a type 6 halftone)
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        var document = OpenHandWritten("/H gs 0.5 g 0 0 100 100 re f", "<< /ExtGState << /H << /HT 5 0 R >> >> >>", objects: thresholds);

        #region PdfRendering_RenderHalftones
        var page = document.Pages[0];

        // The halftones of the document turn the color components on or off, as a printer would
        var options = new PdfRenderOptions
        {
            UseHalftones = true,
            PathAntialiasing = false,
            TextAntialiasing = false,
        };

        using var image = page.Render(options);
        #endregion

        AssertHalftone(image, (x, y) => (x + y) % 2 == 0);
    }

    [Fact]
    public void HalftonesAreIgnoredByDefault()
    {
        using var image = RenderHandWritten("/H gs 0.5 g 0 0 100 100 re f", "<< /ExtGState << /H << /HT 5 0 R >> >> >>", objects: HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds));

        AssertColor(Pixel(image, 10, 10), 128, 128, 128);
        AssertColor(Pixel(image, 11, 10), 128, 128, 128);
    }

    [Fact]
    public void HalftoneThresholdArray()
    {
        // 10.6.5.3: the first threshold at the device origin (the top left pixel), the rows from the top. A component
        // below the threshold of its pixel is off (black). Ghostscript 10.06 (pbm device) gives the same pixels for gray.
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        using var gray = RenderHalftone("0.5 g 0 0 100 100 re f", "<< /HT 5 0 R >>", thresholds);
        AssertHalftone(gray, (x, y) => (x + y) % 2 == 0);

        // Each component of the device is halftoned on its own: 0.25 × 255 = 63.75 turns on the thresholds up to 64
        using var color = RenderHalftone("1 0.5 0.25 rg 0 0 100 100 re f", "<< /HT 5 0 R >>", thresholds);
        AssertHalftone(color, (x, y) => true, (x, y) => (x + y) % 2 == 0, (x, y) => BayerThresholds[(4 * (y % 4)) + (x % 4)] <= 64);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(128, 129)]
    [InlineData(254, 255)]
    [InlineData(255, 256)]
    public void HalftoneThresholdComparison(int level, int whitePixels)
    {
        // The thresholds 0 to 255: a level turns on the thresholds up to it, and 0 counts as 1, so that black stays
        // black (10.6.4). Ghostscript 10.06 gives these counts for 0, 1, 128 and 255.
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 16 /Height 16", [.. Enumerable.Range(0, 256).Select(value => (byte)value)]);
        using var image = RenderHalftone(string.Create(CultureInfo.InvariantCulture, $"{level / 255.0:0.######} g 0 0 100 100 re f"), "<< /HT 5 0 R >>", thresholds);

        Assert.Equal(whitePixels, CountDevicePixels(image, 0, 0, 16, 16, pixel => pixel.R == 255));
        Assert.Equal(256 - whitePixels, CountDevicePixels(image, 0, 0, 16, 16, pixel => pixel.R == 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    public void HalftoneType10(int level)
    {
        // Two squares of 3 and 2 pixels, the thresholds 10 + 20 × rank: the level 20 × k turns on the ranks below k. The
        // ranks of the pixels of a period of device space were read from the output of Ghostscript 10.06 (pbm device).
        int[] ranks = [5, 0, 9, 2, 11, 7, 1, 12, 4, 8, 3, 10, 6];
        var thresholds = HalftoneStream(" /HalftoneType 10 /Xsquare 3 /Ysquare 2", [.. ranks.Select(rank => (byte)(10 + (20 * rank)))]);
        using var image = RenderHalftone(string.Create(CultureInfo.InvariantCulture, $"{20 * level / 255.0:0.######} g 0 0 100 100 re f"), "<< /HT 5 0 R >>", thresholds);

        var expected = ParseRanks("""
             5  0  9  1 12  4 10  6  2 11  7  8  3
             2 11  7  8  3  5  0  9  1 12  4 10  6
             1 12  4 10  6  2 11  7  8  3  5  0  9
             8  3  5  0  9  1 12  4 10  6  2 11  7
            10  6  2 11  7  8  3  5  0  9  1 12  4
             0  9  1 12  4 10  6  2 11  7  8  3  5
            11  7  8  3  5  0  9  1 12  4 10  6  2
            12  4 10  6  2 11  7  8  3  5  0  9  1
             3  5  0  9  1 12  4 10  6  2 11  7  8
             6  2 11  7  8  3  5  0  9  1 12  4 10
             9  1 12  4 10  6  2 11  7  8  3  5  0
             7  8  3  5  0  9  1 12  4 10  6  2 11
             4 10  6  2 11  7  8  3  5  0  9  1 12
            """);
        AssertHalftone(image, (x, y) => expected[y % 13][x % 13] < level);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    public void HalftoneType16WithTwoRectangles(int level)
    {
        // A rectangle of 3 x 2 pixels, and one of 2 x 3 below it, of 16-bit thresholds 257 × (10 + 20 × rank). The ranks
        // of a period of device space are in the order of the output of Ghostscript 10.06 (pbm device), which turns on
        // floor(g × n) pixels of the 12 of a cell instead of comparing the thresholds.
        int[] ranks = [3, 0, 7, 10, 1, 5, 11, 2, 8, 6, 4, 9];
        var data = ranks.SelectMany(rank => new[] { (byte)((257 * (10 + (20 * rank))) >> 8), (byte)(257 * (10 + (20 * rank))) }).ToArray();
        var thresholds = HalftoneStream(" /HalftoneType 16 /Width 3 /Height 2 /Width2 2 /Height2 3", data);
        using var image = RenderHalftone(string.Create(CultureInfo.InvariantCulture, $"{20 * level / 255.0:0.######} g 0 0 100 100 re f"), "<< /HT 5 0 R >>", thresholds);

        var expected = ParseRanks("""
             3  0  7  8  6 10  1  5  4  9 11  2
            10  1  5  4  9 11  2  3  0  7  8  6
            11  2  3  0  7  8  6 10  1  5  4  9
             8  6 10  1  5  4  9 11  2  3  0  7
             4  9 11  2  3  0  7  8  6 10  1  5
             0  7  8  6 10  1  5  4  9 11  2  3
             1  5  4  9 11  2  3  0  7  8  6 10
             2  3  0  7  8  6 10  1  5  4  9 11
             6 10  1  5  4  9 11  2  3  0  7  8
             9 11  2  3  0  7  8  6 10  1  5  4
             7  8  6 10  1  5  4  9 11  2  3  0
             5  4  9 11  2  3  0  7  8  6 10  1
            """);
        AssertHalftone(image, (x, y) => expected[y % 12][x % 12] < level);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(63)]
    public void HalftoneSpotFunction(int level)
    {
        // 9 cells per inch at 72 dpi: cells of 8 x 8 pixels from the device origin. The spot function 0.9 x + 0.1 y
        // whitens the columns from the left, each from the top (10.6.3: the lowest value first), and the gray k / 64
        // whitens k pixels. Ghostscript 10.06 whitens the pixels in the same order, from a cell that starts elsewhere.
        var spotFunction = HandWrittenPdf.Stream("{ 0.1 mul exch 0.9 mul add }", " /FunctionType 4 /Domain [-1 1 -1 1] /Range [-1 1]");
        using var image = RenderHalftone(string.Create(CultureInfo.InvariantCulture, $"{level / 64.0:0.######} g 0 0 100 100 re f"), "<< /HT << /HalftoneType 1 /Frequency 9 /Angle 0 /SpotFunction 5 0 R >> >>", spotFunction);

        AssertHalftone(image, (x, y) => (8 * (x % 8)) + (y % 8) < level);
    }

    [Theory]
    [InlineData("SimpleDot", 0)]
    [InlineData("Round", 0)]
    [InlineData("SimpleDot", 45)]
    [InlineData("Ellipse", 30)]
    public void HalftonePredefinedSpotFunctions(string name, int angle)
    {
        // A cell of n pixels renders a gray g with floor(g × n) white pixels (10.6.2): at 45 degrees, the cell of side
        // 4√2 pixels is spanned by (4, 4) and (-4, 4), two cells of 32 pixels in each 8 x 8 block; the side of 8 pixels at
        // 30 degrees is rounded to (7, 4), cells of 65 pixels. Ghostscript 10.06 gives the same counts at 0 and 45 degrees.
        var frequency = angle == 45 ? 72 / (4 * Math.Sqrt(2)) : 9;
        var halftone = string.Create(CultureInfo.InvariantCulture, $"<< /HT << /HalftoneType 1 /Frequency {frequency:0.######} /Angle {angle} /SpotFunction /{name} >> >>");
        using var image = RenderHalftone("0.3 g 0 0 100 100 re f", halftone);

        if (angle == 30)
        {
            // The lattice of (7, 4) and (-4, 7) repeats every 65 pixels on both axes: 65 x 65 pixels are 65 cells
            Assert.Equal(65 * 19, CountDevicePixels(image, 0, 0, 65, 65, pixel => pixel.R == 255));
            return;
        }

        var cells = angle == 45 ? 2 : 1;
        Assert.Equal(cells * (int)Math.Floor(0.3 * 64 / cells), CountDevicePixels(image, 0, 0, 8, 8, pixel => pixel.R == 255));
        Assert.Equal(64 - (cells * (int)Math.Floor(0.3 * 64 / cells)), CountDevicePixels(image, 0, 0, 8, 8, pixel => pixel.R == 0));
    }

    [Fact]
    public void HalftoneType5()
    {
        // A screen for the red component, and the default one for green and blue (10.6.5.6)
        var red = HalftoneStream(" /HalftoneType 6 /Width 2 /Height 2", [200, 8, 8, 200]);
        var fallback = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        using var image = RenderHalftone("0.5 g 0 0 100 100 re f", "<< /HT << /HalftoneType 5 /Red 5 0 R /Default 6 0 R >> >>", red, fallback);

        AssertHalftone(image, (x, y) => (x + y) % 2 == 1, (x, y) => (x + y) % 2 == 0, (x, y) => (x + y) % 2 == 0);
    }

    [Fact]
    public void HalftoneOrigin()
    {
        // The first threshold at the halftone origin (HTO, PDF 2.0) instead of the device origin. Ghostscript 10.06
        // ignores HTO: the pixels are computed by hand.
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        using var image = RenderHalftone("0.25 g 0 0 100 100 re f", "<< /HT 5 0 R /HTO [1 2] >>", thresholds);

        AssertHalftone(image, (x, y) => BayerThresholds[(4 * ((y + 2) % 4)) + ((x + 3) % 4)] <= 64);
    }

    [Fact]
    public void HalftoneTransferFunction()
    {
        // The transfer function of the halftone replaces the one of the graphics state: here an inversion of the gray
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        var thresholdsWithIdentity = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4 /TransferFunction /Identity", BayerThresholds);
        const string Invert = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";
        using var inverted = RenderHalftone("0.25 g 0 0 100 100 re f", "<< /HT 5 0 R /TR " + Invert + " >>", thresholds);
        using var identity = RenderHalftone("0.25 g 0 0 100 100 re f", "<< /HT 5 0 R /TR " + Invert + " >>", thresholdsWithIdentity);

        AssertHalftone(inverted, (x, y) => BayerThresholds[(4 * (y % 4)) + (x % 4)] <= 192);
        AssertHalftone(identity, (x, y) => BayerThresholds[(4 * (y % 4)) + (x % 4)] <= 64);
    }

    [Theory]
    [InlineData("<< /HT 5 0 R /ca 0.5 >>", 191)]
    [InlineData("<< /HT 5 0 R /BM /Multiply >>", 128)]
    [InlineData("<< /HT /Default >>", 128)]
    [InlineData("<< /HT 6 0 R >>", 128)]
    public void HalftonesApplyToOpaqueObjects(string graphicsState, int gray)
    {
        // The halftone of a transparent object is the default halftone (11.7.5.2); /Default and a threshold array that
        // is too short are the default halftone too
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        var tooShort = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", [1, 2, 3]);
        using var image = RenderHandWritten("/H0 gs /H gs 0.5 g 0 0 100 100 re f", "<< /ExtGState << /H0 << /HT 5 0 R >> /H " + graphicsState + " >> >>", options: HalftoneOptions, objects: [thresholds, tooShort]);

        Assert.Equal(10_000, CountPixels(image, pixel => Math.Abs(pixel.R - gray) <= 2));
    }

    [Fact]
    public void HalftonesInGroups()
    {
        // The objects of a group painted with an alpha are not opaque (11.7.5.2); in an opaque group, they are
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        var group = HandWrittenPdf.Stream("0.5 g 0 0 100 100 re f", " /Type /XObject /Subtype /Form /BBox [0 0 100 100] /Group << /S /Transparency >>");
        using var transparent = RenderHandWritten("/H gs /A gs /G Do", "<< /ExtGState << /H << /HT 5 0 R >> /A << /ca 0.5 >> >> /XObject << /G 6 0 R >> >>", options: HalftoneOptions, objects: [thresholds, group]);
        using var opaque = RenderHandWritten("/H gs /G Do", "<< /ExtGState << /H << /HT 5 0 R >> >> /XObject << /G 6 0 R >> >>", options: HalftoneOptions, objects: [thresholds, group]);

        Assert.Equal(10_000, CountPixels(transparent, pixel => Math.Abs(pixel.R - 191) <= 2));
        AssertHalftone(opaque, (x, y) => (x + y) % 2 == 0);
    }

    [Fact]
    public void HalftonesOfImages()
    {
        // An image is halftoned in the pixels of the device it covers
        var thresholds = HalftoneStream(" /HalftoneType 6 /Width 4 /Height 4", BayerThresholds);
        var image = HandWrittenPdf.Stream("80>", " /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode");
        using var rendered = RenderHandWritten("/H gs 100 0 0 100 0 0 cm /I Do", "<< /ExtGState << /H << /HT 5 0 R >> >> /XObject << /I 6 0 R >> >>", options: HalftoneOptions, objects: [thresholds, image]);

        AssertHalftone(rendered, (x, y) => BayerThresholds[(4 * (y % 4)) + (x % 4)] <= 128);
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

    [Theory]
    [InlineData(0.5, 255, 0, 0)]
    [InlineData(2, 96, 0, 159)]
    public void ExponentialFunctionsOfNegativeInputs(double exponent, int r, int g, int b)
    {
        // t is -0.8 at the pixel 10: 0.64 for an exponent of 2; an exponent that is not an integer is not defined below 0,
        // and gives the color of 0, C0, instead of a color that is not a number (black)
        using var image = RenderHandWritten(
            "/Sh1 sh",
            resources: "<< /Shading << /Sh1 5 0 R >> >>",
            objects: string.Create(CultureInfo.InvariantCulture, $"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0.5 0 100.5 0] /Domain [-1 1] /Function << /FunctionType 2 /Domain [-1 1] /C0 [1 0 0] /C1 [0 0 1] /N {exponent} >> >>"));

        AssertColor(Pixel(image, 10, 50), r, g, b, tolerance: 4);
        AssertColor(Pixel(image, 99, 50), 0, 0, 255, tolerance: 12);
    }

    [Fact]
    public void ADefaultColorSpaceThatIsNotSupportedIsTheDeviceColorSpace()
    {
        using var image = RenderHandWritten("1 0 0 rg 0 0 100 100 re f", resources: "<< /ColorSpace << /DefaultRGB [/Unknown] >> >>");

        AssertColor(Pixel(image, 50, 50), 255, 0, 0);
    }

    [Fact]
    public void ImagesOfAnIccBasedColorSpaceWithARangeAreDecodedToTheRange()
    {
        // The default decode array of the image is the range: 255 is 2, the maximum of the components
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        var image = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfName.Create("Image"),
            [PdfNames.Width] = PdfInteger.Get(2),
            [PdfNames.Height] = PdfInteger.Get(1),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(8),
            [PdfNames.ColorSpace] = new PdfArray(PdfName.Create("ICCBased"), document.Objects.Add(CreateIccStream(IccProfile.Srgb, 3, PdfArray.FromNumbers(0, 2, 0, 2, 0, 2)))),
        });
        image.SetData([255, 0, 0, 128, 0, 0]);
        page.CreateContentBuilder().AppendRawContent("100 0 0 100 0 0 cm /Im1 Do"u8);
        page.Dictionary[PdfNames.Resources] = new PdfDictionary { [PdfNames.XObject] = new PdfDictionary { [PdfName.Create("Im1")] = document.Objects.Add(image) } };

        using var rendered = Render(document);

        AssertColor(Pixel(rendered, 25, 50), 255, 0, 0);
        AssertColor(Pixel(rendered, 75, 50), 128, 0, 0);
    }

    [Fact]
    public void OptionalContentSequencesDoNotLeaveTheirContentStream()
    {
        // A form opens a sequence of a hidden group and does not close it: the page after it is drawn. A form whose EMC
        // has no BMC does not end the hidden sequence of the page that paints it
        using var image = RenderHandWritten(
            "/Fm1 Do 1 0 0 rg 0 0 50 100 re f /OC /G BDC /Fm2 Do 0 0 1 rg 50 0 50 100 re f EMC",
            resources: "<< /XObject << /Fm1 6 0 R /Fm2 7 0 R >> /Properties << /G 5 0 R >> >>",
            catalogEntries: "/OCProperties << /OCGs [5 0 R] /D << /OFF [5 0 R] >> >>",
            objects:
            [
                "<< /Type /OCG /Name (Hidden) >>",
                HandWrittenPdf.Stream("/OC /P BDC", " /Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /Properties << /P 5 0 R >> >>"),
                HandWrittenPdf.Stream("EMC EMC", " /Type /XObject /Subtype /Form /BBox [0 0 100 100]"),
            ]);

        AssertColor(Pixel(image, 25, 50), 255, 0, 0);
        AssertColor(Pixel(image, 75, 50), 255, 255, 255);
    }

    [Fact]
    public void ClipsBeyondTheCoordinatesOfTheCanvasClipToTheirBounds()
    {
        // The clipping paths are beyond 2^24 pixels: the first covers the page, the second is out of it and hides what follows
        using var image = RenderHandWritten(
            "q 100000000 0 0 100000000 0 0 cm -1 -1 2 2 re W n 0.00000001 0 0 0.00000001 0 0 cm 1 0 0 rg 0 0 50 100 re f Q " +
            "q 100000000 0 0 100000000 0 0 cm 1 1 1 1 re W n 0.00000001 0 0 0.00000001 0 0 cm 0 0 1 rg 50 0 50 100 re f Q");

        AssertColor(Pixel(image, 25, 50), 255, 0, 0);
        AssertColor(Pixel(image, 75, 50), 255, 255, 255);
    }

    [Fact]
    public void AnnotationOpacityAppliesToTheAppearanceAsAGroup()
    {
        // /CA 0.5: the red of the appearance is half transparent, where its two rectangles overlap too
        using var image = RenderHandWritten(
            "",
            pageEntries: "/Annots [5 0 R]",
            objects:
            [
                "<< /Type /Annot /Subtype /Square /Rect [0 0 100 100] /CA 0.5 /AP << /N 6 0 R >> >>",
                HandWrittenPdf.Stream("1 0 0 rg 0 0 10 10 re f 0 0 5 10 re f", " /Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            ]);

        AssertColor(Pixel(image, 25, 50), 255, 128, 128);
        AssertColor(Pixel(image, 75, 50), 255, 128, 128);
    }

    [Fact]
    public void AnnotationAppearancesAreTransparencyGroups()
    {
        // An isolated group: the green multiplied with nothing is green, not the black of green multiplied with the red page
        using var image = RenderHandWritten(
            "1 0 0 rg 0 0 100 100 re f",
            pageEntries: "/Annots [5 0 R]",
            objects:
            [
                "<< /Type /Annot /Subtype /Square /Rect [0 0 50 100] /AP << /N 6 0 R >> >>",
                HandWrittenPdf.Stream("/GS0 gs 0 1 0 rg 0 0 10 10 re f", " /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency /I true >> /Resources << /ExtGState << /GS0 << /BM /Multiply >> >> >>"),
            ]);

        AssertColor(Pixel(image, 25, 50), 0, 255, 0);
        AssertColor(Pixel(image, 75, 50), 255, 0, 0);
    }

    [Theory]
    [InlineData(0, 45, 60, 70, 45)]
    [InlineData(16, 70, 45, 45, 70)]
    public void NoRotateAnnotationsAreNotRotatedWithThePage(int flags, int redX, int redY, int whiteX, int whiteY)
    {
        // A rectangle 40 x 10 on a page turned by 90 degrees: tall, or wide from its upper-left corner with NoRotate
        using var image = RenderHandWritten(
            "",
            pageEntries: "/Rotate 90 /Annots [5 0 R]",
            objects:
            [
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /Annot /Subtype /Square /Rect [40 40 80 50] /F {flags} /AP << /N 6 0 R >> >>"),
                HandWrittenPdf.Stream("1 0 0 rg 0 0 40 10 re f", " /Type /XObject /Subtype /Form /BBox [0 0 40 10]"),
            ]);

        AssertColor(image.Frames[0][redX, redY], 255, 0, 0);
        AssertColor(image.Frames[0][whiteX, whiteY], 255, 255, 255);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(8, 255, 255, 255)]
    public void NoZoomAnnotationsKeepTheirSize(int flags, int r, int g, int b)
    {
        // At 144 dpi, a rectangle of 20 points is 40 pixels, or 20 with NoZoom, from its upper-left corner
        var document = OpenHandWritten(
            "",
            pageEntries: "/Annots [5 0 R]",
            objects:
            [
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /Annot /Subtype /Square /Rect [0 80 20 100] /F {flags} /AP << /N 6 0 R >> >>"),
                HandWrittenPdf.Stream("1 0 0 rg 0 0 20 20 re f", " /Type /XObject /Subtype /Form /BBox [0 0 20 20]"),
            ]);

        using var image = PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { Dpi = 144 });

        AssertColor(image.Frames[0][10, 10], 255, 0, 0);
        AssertColor(image.Frames[0][30, 30], r, g, b);
    }

    [Theory]
    [InlineData(PdfRenderIntent.View, true, true, false, false)]
    [InlineData(PdfRenderIntent.Export, true, true, false, false)]
    [InlineData(PdfRenderIntent.Print, true, false, true, false)]
    public void TheIntentSelectsTheAnnotations(PdfRenderIntent intent, bool print, bool none, bool printNoView, bool printHidden)
    {
        // Print; no flag; Print and NoView; Print and Hidden
        var document = OpenHandWritten(
            "",
            pageEntries: "/Annots [5 0 R 6 0 R 7 0 R 8 0 R]",
            objects:
            [
                "<< /Type /Annot /Subtype /Square /Rect [0 0 25 100] /F 4 /AP << /N 9 0 R >> >>",
                "<< /Type /Annot /Subtype /Square /Rect [25 0 50 100] /AP << /N 9 0 R >> >>",
                "<< /Type /Annot /Subtype /Square /Rect [50 0 75 100] /F 36 /AP << /N 9 0 R >> >>",
                "<< /Type /Annot /Subtype /Square /Rect [75 0 100 100] /F 6 /AP << /N 9 0 R >> >>",
                HandWrittenPdf.Stream("1 0 0 rg 0 0 10 10 re f", " /Type /XObject /Subtype /Form /BBox [0 0 10 10]"),
            ]);

        using var image = PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { Intent = intent });

        bool[] expected = [print, none, printNoView, printHidden];
        for (var i = 0; i < 4; i++)
        {
            AssertColor(Pixel(image, 12 + (25 * i), 50), 255, expected[i] ? 0 : 255, expected[i] ? 0 : 255);
        }
    }

    [Fact]
    public void AppearancesAreGeneratedForMarkupAnnotations()
    {
        // A square without appearance: its border (/C, /BS) and its interior (/IC); a highlight multiplies its color
        using var image = RenderHandWritten(
            "0 1 1 rg 50 0 50 100 re f",
            pageEntries: "/Annots [5 0 R 6 0 R]",
            objects:
            [
                "<< /Type /Annot /Subtype /Square /Rect [0 0 50 100] /C [0 0 1] /IC [1 0 0] /BS << /W 6 >> >>",
                "<< /Type /Annot /Subtype /Highlight /Rect [50 0 100 50] /C [1 1 0] /QuadPoints [50 50 100 50 50 0 100 0] >>",
            ]);

        AssertColor(Pixel(image, 2, 50), 0, 0, 255);
        AssertColor(Pixel(image, 25, 50), 255, 0, 0);
        AssertColor(Pixel(image, 75, 25), 0, 255, 0);
        AssertColor(Pixel(image, 75, 75), 0, 255, 255);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/AP << /N 7 0 R >>", "/NeedAppearances true")]
    public void AppearancesAreGeneratedForTextFields(string appearance, string form)
    {
        // A text field without appearance, or whose form asks for new appearances: its value in the default appearance
        using var image = RenderHandWritten(
            "",
            pageEntries: "/Annots [5 0 R]",
            catalogEntries: "/AcroForm << /Fields [5 0 R] /DR << /Font << /Helv 6 0 R >> >> " + form + " >>",
            objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Name) /V (HHHH) /DA (/Helv 40 Tf 1 0 0 rg) /Rect [0 0 100 100] " + appearance + " >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                HandWrittenPdf.Stream("", " /Type /XObject /Subtype /Form /BBox [0 0 100 100]"),
            ]);

        Assert.True(CountPixels(image, pixel => pixel.R > 200 && pixel.G < 64) > 100);
    }

    [Fact]
    public void ShadingPatternsApplyTheirGraphicsState()
    {
        using var image = RenderHandWritten(
            "/Pattern cs /P1 scn 0 0 100 100 re f",
            resources: "<< /Pattern << /P1 5 0 R >> >>",
            objects: "<< /PatternType 2 /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 100 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [1 0 0] /N 1 >> >> /ExtGState << /ca 0.5 >> >>");

        AssertColor(Pixel(image, 50, 50), 255, 128, 128);
    }

    [Fact]
    public void TilingPatternCellsWiderThanTwoStepsAreAllPainted()
    {
        // A cell 30 wide every 10: its band at 25 to 30 is painted at 5 to 10, 15 to 20, ... three steps to the left
        using var image = RenderHandWritten(
            "/Pattern cs /P1 scn 0 0 100 100 re f",
            resources: "<< /Pattern << /P1 5 0 R >> >>",
            objects: HandWrittenPdf.Stream("1 0 0 rg 25 0 5 100 re f", " /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 30 100] /XStep 10 /YStep 100 /Resources << >>"));

        AssertColor(Pixel(image, 7, 50), 255, 0, 0);
        AssertColor(Pixel(image, 2, 50), 255, 255, 255);
        AssertColor(Pixel(image, 17, 50), 255, 0, 0);
        AssertColor(Pixel(image, 12, 50), 255, 255, 255);
    }

    [Theory]
    [InlineData(7, 255, 255, 255)]
    [InlineData(4, 0, 0, 255)]
    public void Type3GlyphsClipInTheClippingModes(int mode, int r, int g, int b)
    {
        // The glyph is a square of 40 x 40: the red painted after the text is inside it only; mode 4 also fills it in blue
        using var image = RenderHandWritten(
            string.Create(CultureInfo.InvariantCulture, $"0 0 1 rg BT /T3 40 Tf {mode} Tr 10 10 Td (A) Tj ET 1 0 0 rg 0 0 100 30 re f"),
            resources: "<< /Font << /T3 5 0 R >> >>",
            objects: [Type3SquareFont(6), Type3SquareProcedure]);

        AssertColor(Pixel(image, 30, 20), 255, 0, 0);
        AssertColor(Pixel(image, 70, 20), 255, 255, 255);
        AssertColor(Pixel(image, 30, 40), r, g, b);
    }

    [Theory]
    [InlineData("", 255, 128, 128)]
    [InlineData("/TK false", 255, 64, 64)]
    public void TheGlyphsOfATextKnockOutOneAnother(string textKnockout, int r, int g, int b)
    {
        // Two squares of 40 that overlap by 20, half transparent: their overlap is painted once, unless TK is false
        using var image = RenderHandWritten(
            "/GS0 gs 1 0 0 rg BT /T3 40 Tf -20 Tc 10 10 Td (AA) Tj ET",
            resources: "<< /Font << /T3 5 0 R >> /ExtGState << /GS0 << /ca 0.5 " + textKnockout + " >> >> >>",
            objects: [Type3SquareFont(6), Type3SquareProcedure]);

        AssertColor(Pixel(image, 20, 30), 255, 128, 128);
        AssertColor(Pixel(image, 40, 30), r, g, b);
    }

    [Theory]
    [InlineData(false, 255, 128, 128)]
    [InlineData(true, 128, 0, 128)]
    public void AlphaIsShapeInKnockoutGroups(bool alphaIsShape, int r, int g, int b)
    {
        // In a knockout group, a red of alpha 0.5 over blue: as opacity, the red composited with the white backdrop of the
        // group; as shape, half of the blue knocked out by red
        using var image = RenderHandWritten(
            "/Fm1 Do",
            resources: "<< /XObject << /Fm1 5 0 R >> >>",
            objects: HandWrittenPdf.Stream(
                "0 0 1 rg 0 0 100 100 re f /GS0 gs 1 0 0 rg 0 0 50 100 re f",
                " /Type /XObject /Subtype /Form /BBox [0 0 100 100] /Group << /S /Transparency /K true >> /Resources << /ExtGState << /GS0 << /ca 0.5 /AIS " + (alphaIsShape ? "true" : "false") + " >> >> >>"));

        AssertColor(Pixel(image, 25, 50), r, g, b);
        AssertColor(Pixel(image, 75, 50), 0, 0, 255);
    }

    [Fact]
    public void CompositeFontsWithAType1ProgramAreReplaced()
    {
        // A CIDFont cannot have a Type 1 program: the font is replaced, and draws the H of its ToUnicode CMap
        using var image = RenderHandWritten(
            "BT /F1 100 Tf 10 20 Td <0001> Tj ET",
            resources: "<< /Font << /F1 5 0 R >> >>",
            objects:
            [
                "<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 8 0 R >>",
                "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Test /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 722 /FontDescriptor 7 0 R >>",
                "<< /Type /FontDescriptor /FontName /Test /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 1000 /Descent 0 /CapHeight 700 /StemV 80 /FontFile 9 0 R >>",
                HandWrittenPdf.Stream(ToUnicodeH),
                HandWrittenPdf.Stream("%!PS-AdobeFont-1.0: Test\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\ncurrentfile eexec\n00000000"),
            ]);

        Assert.True(Pixel(image, 22, 50).R < 64);
    }

    [Theory]
    [InlineData(1444, true)]
    [InlineData(361, false)]
    public void ReplacedGlyphsAreFittedToTheWidthsOfTheDocument(int width, bool reachesRight)
    {
        // The H of the replacement font is 722 wide: twice or half that width in the document stretches or narrows it
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, string.Create(CultureInfo.InvariantCulture, $"<< /Type /Font /Subtype /TrueType /BaseFont /Arial /FirstChar 72 /LastChar 72 /Widths [{width}] /Encoding /WinAnsiEncoding >>"))
            .Add(5, HandWrittenPdf.Stream("BT /F1 100 Tf 10 20 Td (H) Tj ET"))
            .ToArray();

        using var image = PdfRenderer.RenderPage(PdfDocument.Open(file).Pages[0], new PdfRenderOptions { UseSystemFonts = false });

        var right = Enumerable.Range(0, 200).Where(x => Pixel(image, x, 60).R < 128).DefaultIfEmpty(-1).Max();
        Assert.Equal(reachesRight, right > 120);
        Assert.Equal(!reachesRight, right is > 0 and < 50);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("/W2 [1 [-2000 500 880]]", true)]
    public void VerticalTextUsesTheVerticalMetricsOfTheCids(string metrics, bool lowered)
    {
        // Two H in vertical writing, 20 points: the second is 1 em below the first by default, 2 with a w1y of -2000
        using var image = RenderHandWritten(
            "BT /F1 20 Tf 50 90 Td <00010001> Tj ET",
            resources: "<< /Font << /F1 5 0 R >> >>",
            objects:
            [
                "<< /Type /Font /Subtype /Type0 /BaseFont /Arial /Encoding /Identity-V /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>",
                "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " + metrics + " >>",
                HandWrittenPdf.Stream(ToUnicodeH),
            ]);

        Assert.Equal(lowered, CountPixels(image, 40, 35, 55, 45, pixel => pixel.R < 128) > 5);
        Assert.Equal(!lowered, CountPixels(image, 40, 55, 55, 65, pixel => pixel.R < 128) > 5);
    }

    [Fact]
    public void TheMatteOfASoftMaskIsRemovedFromTheColors()
    {
        // Red at an alpha of 0.5, premultiplied with the black matte: (128, 0, 0) in the image, red at half opacity once drawn
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);
        var mask = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfName.Create("Image"),
            [PdfNames.Width] = PdfInteger.Get(2),
            [PdfNames.Height] = PdfInteger.Get(1),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(8),
            [PdfNames.ColorSpace] = PdfName.Create("DeviceGray"),
            [PdfName.Create("Matte")] = PdfArray.FromNumbers(0, 0, 0),
        });
        mask.SetData([128, 255]);
        var image = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfName.Create("Image"),
            [PdfNames.Width] = PdfInteger.Get(2),
            [PdfNames.Height] = PdfInteger.Get(1),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(8),
            [PdfNames.ColorSpace] = PdfName.Create("DeviceRGB"),
            [PdfNames.SMask] = document.Objects.Add(mask),
        });
        image.SetData([128, 0, 0, 0, 0, 255]);
        page.CreateContentBuilder().AppendRawContent("100 0 0 100 0 0 cm /Im1 Do"u8);
        page.Dictionary[PdfNames.Resources] = new PdfDictionary { [PdfNames.XObject] = new PdfDictionary { [PdfName.Create("Im1")] = document.Objects.Add(image) } };

        using var rendered = Render(document);

        AssertColor(Pixel(rendered, 25, 50), 255, 128, 128, tolerance: 3);
        AssertColor(Pixel(rendered, 75, 50), 0, 0, 255);
    }

    [Theory]
    [InlineData(null, 128, 0, 0)]
    [InlineData(0, 128, 0, 0)]
    [InlineData(1, 191, 128, 128)]
    [InlineData(2, 255, 128, 128)]
    public void TheOpacityOfJpeg2000ImagesFollowsSMaskInData(int? smaskInData, int r, int g, int b)
    {
        // A JPEG 2000 image with an opacity channel: (128, 0, 0) at an alpha of 0.5, which is red premultiplied by it
        using var source = Image.ImportPixelData<Rgba32>([new Rgba32(128, 0, 0, 128), new Rgba32(0, 0, 255, 255)], 2, 1);
        using var jpx = new MemoryStream();
        source.Save(jpx, new Imaging.Formats.Jpeg2000Encoder());
        var entry = smaskInData is { } value ? string.Create(CultureInfo.InvariantCulture, $" /SMaskInData {value}") : "";

        using var rendered = RenderHandWritten(
            "100 0 0 100 0 0 cm /Im1 Do",
            resources: "<< /XObject << /Im1 5 0 R >> >>",
            objects: HandWrittenPdf.Stream(Encoding.Latin1.GetString(jpx.ToArray()), " /Type /XObject /Subtype /Image /Width 2 /Height 1 /Filter /JPXDecode" + entry));

        AssertColor(Pixel(rendered, 25, 50), r, g, b, tolerance: 3);
        AssertColor(Pixel(rendered, 75, 50), 0, 0, 255);
    }

    [Fact]
    public void RenderingCanBeCanceled()
    {
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        Assert.Throws<OperationCanceledException>(() => PdfRenderer.RenderPage(document.Pages[0], options: null, canceled.Token));
        using var target = new Image<Rgba32>(100, 100);
        Assert.Throws<OperationCanceledException>(() => PdfRenderer.RenderPage(document.Pages[0], target, options: null, canceled.Token));
    }

    [Fact]
    public void RenderingIsCanceledWhileThePageIsDrawn()
    {
        // The token is canceled when the font of the second text is resolved: the rendering stops at the next operator
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /TrueType /BaseFont /Corporate-Sans /FirstChar 72 /LastChar 72 /Widths [722] /Encoding /WinAnsiEncoding >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 10 Tf 10 20 Td (H) Tj ET 1 0 0 rg 0 0 100 100 re f"))
            .ToArray();
        using var cancellation = new CancellationTokenSource();
        var options = new PdfRenderOptions
        {
            UseSystemFonts = false,
            FontResolver = _ =>
            {
                cancellation.Cancel();
                return null;
            },
        };

        Assert.Throws<OperationCanceledException>(() => PdfRenderer.RenderPage(PdfDocument.Open(file).Pages[0], options, cancellation.Token));
    }

    [Fact]
    public void RenderARegionOfAPage()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        page.CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(50, 50, 10, 10).Fill();

        #region PdfRendering_RenderRegion
        // The square of 50 x 50 points at (50, 50) of the page, as an image 100 pixels wide
        var options = new PdfRenderOptions
        {
            Region = new PdfRectangle(50, 50, 100, 100),
            Width = 100,
        };
        using var image = page.Render(options);
        #endregion

        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
        Assert.Equal(new Size(100, 100), PdfRenderer.GetImageSize(page, options));
        AssertColor(Pixel(image, 10, 10), 255, 0, 0);
        AssertColor(Pixel(image, 30, 30), 255, 255, 255);
    }

    [Theory]
    [InlineData(0, 50, 50, 50, 25)]
    [InlineData(90, 50, 50, 25, 50)]
    [InlineData(0, null, 40, 80, 40)]
    [InlineData(0, 30, null, 30, 15)]
    public void RenderAPageToASize(int rotation, int? width, int? height, int expectedWidth, int expectedHeight)
    {
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(200, 100)).Rotation = rotation;

        using var image = document.Pages[0].Render(new PdfRenderOptions { Width = width, Height = height });

        Assert.Equal(expectedWidth, image.Width);
        Assert.Equal(expectedHeight, image.Height);
    }

    [Fact]
    public void InvalidRegionsAndSizesAreRejected()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(Size);

        Assert.Throws<ArgumentException>(() => PdfRenderer.GetImageSize(page, new PdfRenderOptions { Region = new PdfRectangle(200, 200, 300, 300) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfRenderOptions { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfRenderOptions { Height = -1 });
        Assert.Throws<PdfResourceLimitException>(() => PdfRenderer.GetImageSize(page, new PdfRenderOptions { Width = 1_000_000 }));
    }

    [Fact]
    public void RenderToAnImageOfTheApplication()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 100));
        page.CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill();

        #region PdfRendering_RenderToImage
        // A grayscale image of the size of the page, allocated by the application
        var size = PdfRenderer.GetImageSize(page);
        using var gray = new Image<Gray8>(size.Width, size.Height);
        PdfRenderer.RenderPage(page, gray);
        #endregion

        using var rgba = page.Render();
        using var target = new Image<Rgba32>(200, 100);
        PdfRenderer.RenderPage(page, target);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                Assert.Equal(rgba.Frames[0][x, y], target.Frames[0][x, y]);
            }
        }

        Assert.Equal(255, gray.Frames[0][150, 50].Value);
        Assert.InRange(gray.Frames[0][50, 50].Value, 40, 100);

        // A smaller image receives the part of the page it has room for
        using var small = new Image<Rgba32>(50, 50);
        PdfRenderer.RenderPage(page, small);
        AssertColor(small.Frames[0][25, 25], 255, 0, 0);
    }

    [Fact]
    public void AntialiasingCanBeTurnedOff()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder()
            .AppendRawContent("0 g 0 0 50.5 50 re f "u8)
            .BeginText().SetFont(font, 40).MoveTextPosition(10, 60).ShowText("H").EndText();
        static bool IsBlended(Rgba32 pixel) => pixel.R is > 0 and < 255;

        using var antialiased = PdfRenderer.RenderPage(document.Pages[0]);
        using var hardPaths = PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { PathAntialiasing = false });
        using var hardText = PdfRenderer.RenderPage(document.Pages[0], new PdfRenderOptions { TextAntialiasing = false });

        Assert.True(IsBlended(Pixel(antialiased, 50, 25)));
        Assert.False(IsBlended(Pixel(hardPaths, 50, 25)));
        Assert.True(CountPixels(antialiased, 0, 55, 100, 100, IsBlended) > 0);
        Assert.True(CountPixels(hardPaths, 0, 55, 100, 100, IsBlended) > 0);
        Assert.Equal(0, CountPixels(hardText, 0, 55, 100, 100, IsBlended));
        Assert.True(IsBlended(Pixel(hardText, 50, 25)));
    }

    [Theory]
    [InlineData(null, PdfRenderIntent.View, null, true, false, true)]
    [InlineData("Alternate", PdfRenderIntent.View, null, false, true, true)]
    [InlineData(null, PdfRenderIntent.Print, null, false, true, true)]
    [InlineData(null, PdfRenderIntent.View, "Right", true, true, true)]
    public void OptionalContentConfigurationsAndIntents(string? configuration, PdfRenderIntent intent, string? turnedOn, bool left, bool right, bool design)
    {
        // Left is on and Right off by default; the alternate configuration exchanges them, and so does the print usage.
        // The group of the Design intent is off, but the configuration is for viewing: it hides nothing
        var document = OpenHandWritten(
            "/OC /L BDC 1 0 0 rg 0 0 30 100 re f EMC /OC /R BDC 1 0 0 rg 30 0 30 100 re f EMC /OC /D BDC 1 0 0 rg 60 0 40 100 re f EMC",
            resources: "<< /Properties << /L 5 0 R /R 6 0 R /D 7 0 R >> >>",
            catalogEntries: "/OCProperties << /OCGs [5 0 R 6 0 R 7 0 R] /D << /OFF [6 0 R 7 0 R] /AS [<< /Event /Print /Category [/Print] /OCGs [5 0 R 6 0 R] >>] >> /Configs [<< /Name (Alternate) /BaseState /OFF /ON [6 0 R 7 0 R] >>] >>",
            objects:
            [
                "<< /Type /OCG /Name (Left) /Usage << /Print << /PrintState /OFF >> >> >>",
                "<< /Type /OCG /Name (Right) /Usage << /Print << /PrintState /ON >> >> >>",
                "<< /Type /OCG /Name (Design) /Intent /Design >>",
            ]);
        var options = new PdfRenderOptions
        {
            OptionalContentConfiguration = configuration,
            Intent = intent,
            OptionalContentGroupStates = turnedOn is null ? null : new Dictionary<string, bool>(StringComparer.Ordinal) { [turnedOn] = true },
        };

        using var image = PdfRenderer.RenderPage(document.Pages[0], options);

        AssertColor(Pixel(image, 15, 50), 255, left ? 0 : 255, left ? 0 : 255);
        AssertColor(Pixel(image, 45, 50), 255, right ? 0 : 255, right ? 0 : 255);
        AssertColor(Pixel(image, 80, 50), 255, design ? 0 : 255, design ? 0 : 255);
    }

    [Fact]
    public void Example_PrintWithoutALayer()
    {
        // A layer named Watermark, on in the configuration of the document, and an annotation that is not printed
        var document = OpenHandWritten(
            "/OC /W BDC 1 0 0 rg 0 0 50 100 re f EMC",
            resources: "<< /Properties << /W 5 0 R >> >>",
            pageEntries: "/Annots [6 0 R]",
            catalogEntries: "/OCProperties << /OCGs [5 0 R] /D << >> >>",
            objects:
            [
                "<< /Type /OCG /Name (Watermark) >>",
                "<< /Type /Annot /Subtype /Square /Rect [50 0 100 100] /C [0 0 1] /IC [0 0 1] >>",
            ]);
        var page = document.Pages[0];

        #region PdfRendering_Print
        var options = new PdfRenderOptions
        {
            // The annotations and the layers meant for printing
            Intent = PdfRenderIntent.Print,

            // A layer turned off, whatever the document says
            OptionalContentGroupStates = new Dictionary<string, bool>(StringComparer.Ordinal) { ["Watermark"] = false },
            Dpi = 300,
        };
        using var image = page.Render(options);
        #endregion

        using var view = page.Render();
        AssertColor(Pixel(view, 25, 50), 255, 0, 0);
        AssertColor(Pixel(view, 75, 50), 0, 0, 255);
        AssertColor(image.Frames[0][100, 200], 255, 255, 255);
        AssertColor(image.Frames[0][300, 200], 255, 255, 255);
    }

    [Fact]
    public void Example_CancelTheRendering()
    {
        var document = new PdfDocument();
        document.Pages.Add(Size).CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 0, 100, 100).Fill();
        var page = document.Pages[0];

        #region PdfRendering_Cancellation
        // A page that takes too long is abandoned: OperationCanceledException
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var image = PdfRenderer.RenderPage(page, new PdfRenderOptions { Dpi = 150 }, timeout.Token);
        #endregion

        AssertColor(image.Frames[0][100, 100], 255, 0, 0);
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

    [Fact]
    public void Example_RenderSession()
    {
        var document = new PdfDocument();
        for (var i = 0; i < 3; i++)
        {
            document.Pages.Add(new PdfSize(200, 100)).CreateContentBuilder().SetFillRgb(0, 0, 1).Rectangle(0, 0, 100, 100).Fill();
        }

        #region PdfRendering_RenderSession
        // The pages share the fonts, images and color spaces the session reads
        using var session = new PdfRenderSession(document, new PdfRenderOptions { Dpi = 144 });
        foreach (var page in document.Pages)
        {
            using var image = session.RenderPage(page);
            Assert.Equal(400, image.Width);
        }
        #endregion
    }

    [Fact]
    public void ASessionRendersThePagesAsRenderPageDoes()
    {
        // Two pages paint the same image: the session decodes it for the first one and keeps it for the second one
        var document = new PdfDocument();
        using var source = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 0, 255, 255)], 2, 1);
        var image = PdfImage.FromImage(source);
        document.Pages.Add(Size).CreateContentBuilder().DrawImage(image, 0, 0, 100, 100);
        document.Pages.Add(Size).CreateContentBuilder().DrawImage(image, 0, 0, 50, 50);
        var reopened = PdfDocument.Open(document.ToByteArray());
        var options = new PdfRenderOptions { UseSystemFonts = false };
        var session = new PdfRenderSession(reopened, options);
        foreach (var page in reopened.Pages)
        {
            using var expected = PdfRenderer.RenderPage(page, options);
            using var actual = session.RenderPage(page);
            AssertSamePixels(expected, actual);
        }

        Assert.Throws<ArgumentException>(() => session.RenderPage(new PdfDocument().Pages.Add(Size)));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.RenderPage(reopened.Pages[0]));
    }

    [Fact]
    public void ASessionCountsTheWorkOfEachPage()
    {
        var document = new PdfDocument();
        for (var page = 0; page < 3; page++)
        {
            var builder = document.Pages.Add(Size).CreateContentBuilder();
            for (var i = 0; i < 400; i++)
            {
                builder.Rectangle(0, 0, 1, 1).Fill();
            }
        }

        // The smallest power of 2 that is enough for one page is less than the work of two pages
        var limit = 256L;
        while (!CanRender(document.Pages[0], limit))
        {
            limit *= 2;
        }

        using var session = new PdfRenderSession(document, new PdfRenderOptions { MaxOperations = limit });
        foreach (var page in document.Pages)
        {
            using var image = session.RenderPage(page);
        }

        static bool CanRender(PdfPage page, long limit)
        {
            try
            {
                using var image = PdfRenderer.RenderPage(page, new PdfRenderOptions { MaxOperations = limit });
                return true;
            }
            catch (PdfResourceLimitException)
            {
                return false;
            }
        }
    }

    [Fact]
    public void TheDefaultColorSpacesOfTheResourcesOfAFormApplyToTheObjectsItShares()
    {
        // Two forms paint the same gray image and fill with the same indexed color space; the resources of the second
        // one replace DeviceGray with a separation that is red at 0, and DeviceRGB with a DeviceN that swaps red and blue
        var document = new PdfDocument();
        var imageStream = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfName.Create("Image"),
            [PdfNames.Width] = PdfInteger.Get(1),
            [PdfNames.Height] = PdfInteger.Get(1),
            [PdfNames.ColorSpace] = PdfName.Create("DeviceGray"),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(8),
        });
        imageStream.SetData([0]);
        var image = document.Objects.Add(imageStream);
        var indexed = document.Objects.Add(new PdfArray(PdfName.Create("Indexed"), PdfName.Create("DeviceRGB"), PdfInteger.Get(0), PdfString.FromBytes([255, 0, 0])));
        var red = new PdfArray(PdfName.Create("Separation"), PdfName.Create("Red"), PdfName.Create("DeviceRGB"), new PdfDictionary
        {
            [PdfName.Create("FunctionType")] = PdfInteger.Get(2),
            [PdfName.Create("Domain")] = PdfArray.FromNumbers(0, 1),
            [PdfName.Create("C0")] = PdfArray.FromNumbers(1, 0, 0),
            [PdfName.Create("C1")] = PdfArray.FromNumbers(1, 1, 1),
            [PdfNames.N] = PdfInteger.Get(1),
        });
        var swap = new PdfStream(new PdfDictionary
        {
            [PdfName.Create("FunctionType")] = PdfInteger.Get(4),
            [PdfName.Create("Domain")] = PdfArray.FromNumbers(0, 1, 0, 1, 0, 1),
            [PdfName.Create("Range")] = PdfArray.FromNumbers(0, 1, 0, 1, 0, 1),
        });
        swap.SetData("{ 3 1 roll exch }"u8);
        var bgr = new PdfArray(PdfName.Create("DeviceN"), new PdfArray(PdfName.Create("B"), PdfName.Create("G"), PdfName.Create("R")), PdfName.Create("DeviceRGB"), document.Objects.Add(swap));
        PdfReference CreateForm(bool hasDefaults)
        {
            var colorSpaces = new PdfDictionary { [PdfName.Create("CS0")] = indexed };
            if (hasDefaults)
            {
                colorSpaces[PdfName.Create("DefaultGray")] = red;
                colorSpaces[PdfName.Create("DefaultRGB")] = bgr;
            }

            var form = new PdfStream(new PdfDictionary
            {
                [PdfNames.Type] = PdfNames.XObject,
                [PdfNames.Subtype] = PdfNames.Form,
                [PdfNames.BBox] = PdfArray.FromNumbers(0, 0, 50, 100),
                [PdfNames.Resources] = new PdfDictionary
                {
                    [PdfNames.XObject] = new PdfDictionary { [PdfName.Create("Im0")] = image },
                    [PdfNames.ColorSpace] = colorSpaces,
                },
            });
            form.SetData("q 50 0 0 50 0 0 cm /Im0 Do Q /CS0 cs 0 sc 0 50 50 50 re f"u8);
            return document.Objects.Add(form);
        }

        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent("/Fm0 Do q 1 0 0 1 50 0 cm /Fm1 Do Q"u8);
        page.Dictionary[PdfNames.Resources] = new PdfDictionary
        {
            [PdfNames.XObject] = new PdfDictionary { [PdfName.Create("Fm0")] = CreateForm(hasDefaults: false), [PdfName.Create("Fm1")] = CreateForm(hasDefaults: true) },
        };

        using var rendered = Render(document);

        AssertColor(Pixel(rendered, 25, 25), 0, 0, 0);
        AssertColor(Pixel(rendered, 75, 25), 255, 0, 0);
        AssertColor(Pixel(rendered, 25, 75), 255, 0, 0);
        AssertColor(Pixel(rendered, 75, 75), 0, 0, 255);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASoftMaskMasksEachDrawingAsItMasksTheFirstOne(bool alpha)
    {
        // The first drawing renders the mask, the next ones the image of the mask the page keeps: the pixels are the
        // same as those of one drawing
        PdfDocument Create(params (double X, double Width)[] rectangles)
        {
            var document = new PdfDocument();
            var group = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Rgb });
            var content = group.CreateContentBuilder();
            if (alpha)
            {
                content.SetGraphicsState(new PdfExtGState { FillAlpha = 0.37 }).SetFillRgb(0.2, 0.5, 0.9).Rectangle(10, 10, 60, 60).Fill()
                    .SetGraphicsState(new PdfExtGState { FillAlpha = 0.8 }).Rectangle(40, 40, 50, 50).Fill();
            }
            else
            {
                content.PaintShading(PdfShading.Axial(PdfDeviceColorSpace.Rgb, 0, 0, 100, 100, PdfFunction.Exponential([1, 1, 1], [0, 0, 0]), extendStart: true, extendEnd: true));
            }

            var mask = alpha ? PdfSoftMask.FromAlpha(group) : PdfSoftMask.FromLuminosity(group);
            var builder = document.Pages.Add(Size).CreateContentBuilder().SetGraphicsState(new PdfExtGState { SoftMask = mask }).SetFillRgb(1, 0, 0);
            foreach (var (x, width) in rectangles)
            {
                builder.Rectangle(x, 0, width, 100).Fill();
            }

            return document;
        }

        using var whole = Render(Create((0, 100)));
        using var parts = Render(Create((0, 25), (25, 25), (50, 50)));

        AssertSamePixels(whole, parts);
        Assert.NotEqual(Pixel(whole, 5, 5), Pixel(whole, 50, 50));
    }

    [Fact]
    public void TheGlyphProceduresOfAType3FontAreChargedEachTimeTheyRun()
    {
        var document = new PdfDocument();
        var procedure = new PdfStream();
        procedure.SetData(Encoding.ASCII.GetBytes("1000 0 0 0 1000 1000 d1 " + string.Concat(Enumerable.Repeat("0 0 1000 1000 re f ", 25))));
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
            [PdfNames.Widths] = PdfArray.FromNumbers(0),
        };
        var page = document.Pages.Add(Size);
        page.CreateContentBuilder().AppendRawContent(Encoding.ASCII.GetBytes("BT /T3 10 Tf 10 10 Td (" + new string('A', 400) + ") Tj ET"));
        page.Dictionary[PdfNames.Resources] = new PdfDictionary { [PdfNames.Font] = new PdfDictionary { [PdfName.Create("T3")] = document.Objects.Add(font) } };
        var reopened = PdfDocument.Open(document.ToByteArray());

        using var image = PdfRenderer.RenderPage(reopened.Pages[0], new PdfRenderOptions { MaxOperations = 100_000 });
        AssertColor(Pixel(image, 15, 15), 0, 0, 0);
        Assert.Throws<PdfResourceLimitException>(() => PdfRenderer.RenderPage(reopened.Pages[0], new PdfRenderOptions { MaxOperations = 10_000 }));
    }

    [Fact]
    public void AMeshPatternPaintsEachDrawingWithTheMesh()
    {
        // The mesh is read once for the rectangles it paints: its left triangle is red, its right one blue
        var red = PdfColor.FromRgb(1, 0, 0);
        var blue = PdfColor.FromRgb(0, 0, 1);
        var document = new PdfDocument();
        var mesh = PdfShading.TriangleMesh(PdfDeviceColorSpace.Rgb,
        [
            (new PdfMeshVertex(0, 0, red), new PdfMeshVertex(0, 100, red), new PdfMeshVertex(100, 100, red)),
            (new PdfMeshVertex(0, 0, blue), new PdfMeshVertex(100, 0, blue), new PdfMeshVertex(100, 100, blue)),
        ]);
        document.Pages.Add(Size).CreateContentBuilder().SetFillPattern(new PdfShadingPattern(mesh))
            .Rectangle(0, 50, 30, 50).Fill()
            .Rectangle(70, 0, 30, 50).Fill();

        using var image = Render(document);

        AssertColor(Pixel(image, 10, 90), 255, 0, 0);
        AssertColor(Pixel(image, 90, 10), 0, 0, 255);
        AssertColor(Pixel(image, 50, 50), 255, 255, 255);
    }

    [Fact]
    public void TheRowsOfALargeCmykImageAreConvertedAsOneColor()
    {
        // A row of 300 CMYK pixels is converted with a buffer of the pool, one pixel with a buffer of the stack
        static Rgba32 RenderCmyk(int width)
        {
            var document = new PdfDocument();
            var pixels = string.Concat(Enumerable.Repeat("33669900", width));
            document.Pages.Add(Size).CreateContentBuilder().AppendRawContent(Encoding.ASCII.GetBytes($"q 100 0 0 100 0 0 cm BI /W {width} /H 1 /CS /DeviceCMYK /BPC 8 /F /AHx ID {pixels}> EI Q"));
            using var image = Render(document);
            return Pixel(image, 50, 50);
        }

        Assert.Equal(RenderCmyk(1), RenderCmyk(300));
    }

    private static void AssertSamePixels(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var expectedPixels = new Rgba32[expected.Width * expected.Height];
        var actualPixels = new Rgba32[actual.Width * actual.Height];
        expected.Frames[0].CopyPixelDataTo(expectedPixels);
        actual.Frames[0].CopyPixelDataTo(actualPixels);
        Assert.True(expectedPixels.AsSpan().SequenceEqual(actualPixels), "The images are different");
    }

    private static Image<Rgba32> Render(PdfDocument document) => PdfRenderer.RenderPage(PdfDocument.Open(document.ToByteArray()).Pages[0], new PdfRenderOptions { UseSystemFonts = false });

    /// <summary>Gets a Type 3 font whose glyph of A is a square of the glyph space, its procedure being the object <paramref name="procedure"/>.</summary>
    private static string Type3SquareFont(int procedure) => string.Create(CultureInfo.InvariantCulture, $"<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /square {procedure} 0 R >> /Encoding << /Differences [65 /square] >> /FirstChar 65 /LastChar 65 /Widths [1000] >>");

    private static int CountPixels(Image<Rgba32> image, Func<Rgba32, bool> predicate) => CountPixels(image, 0, 0, image.Width, image.Height, predicate);

    /// <summary>Counts the pixels of a rectangle of the page (y up, as <see cref="Pixel"/>) that match a predicate.</summary>
    private static int CountPixels(Image<Rgba32> image, int left, int bottom, int right, int top, Func<Rgba32, bool> predicate)
    {
        var count = 0;
        for (var y = bottom; y < top; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (predicate(Pixel(image, x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static PdfStream CreateIccStream(IccProfile profile, int components, PdfArray? range = null)
    {
        var stream = new PdfStream(new PdfDictionary { [PdfNames.N] = PdfInteger.Get(components) });
        if (range is not null)
        {
            stream.Dictionary[PdfName.Create("Range")] = range;
        }

        stream.SetData(profile.Data.ToArray());
        return stream;
    }

    /// <summary>Gets a halftone stream whose thresholds are written in hexadecimal.</summary>
    private static string HalftoneStream(string entries, byte[] thresholds) => HandWrittenPdf.Stream(Convert.ToHexString(thresholds) + ">", " /Type /Halftone /Filter /ASCIIHexDecode" + entries);

    /// <summary>Renders a page whose content is painted after the graphics state <c>/H</c>, with the halftones applied.</summary>
    private static Image<Rgba32> RenderHalftone(string content, string graphicsState, params string[] objects)
        => RenderHandWritten("/H gs " + content, "<< /ExtGState << /H " + graphicsState + " >> >>", options: HalftoneOptions, objects: objects);

    private static int[][] ParseRanks(string text) => [.. text.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray())];

    /// <summary>Checks that every pixel of the image is fully on or off, as a function of its device coordinates (y down) says.</summary>
    private static void AssertHalftone(Image<Rgba32> image, Func<int, int, bool> isWhite) => AssertHalftone(image, isWhite, isWhite, isWhite);

    private static void AssertHalftone(Image<Rgba32> image, Func<int, int, bool> red, Func<int, int, bool> green, Func<int, int, bool> blue)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                var expected = (R: red(x, y) ? 255 : 0, G: green(x, y) ? 255 : 0, B: blue(x, y) ? 255 : 0);
                Assert.True(pixel.R == expected.R && pixel.G == expected.G && pixel.B == expected.B, $"The pixel ({x}, {y}) is ({pixel.R}, {pixel.G}, {pixel.B}), not {expected}");
            }
        }
    }

    /// <summary>Counts the pixels of a rectangle of the device (y down) that match a predicate.</summary>
    private static int CountDevicePixels(Image<Rgba32> image, int left, int top, int right, int bottom, Func<Rgba32, bool> predicate)
    {
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (predicate(image.Frames[0][x, y]))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Renders the page of a document written by hand: object 3 is the page (100 x 100), 4 its content, and the objects given are numbered from 5.</summary>
    private static Image<Rgba32> RenderHandWritten(string content, string resources = "<< >>", string pageEntries = "", string catalogEntries = "", PdfRenderOptions? options = null, params string[] objects)
        => PdfRenderer.RenderPage(OpenHandWritten(content, resources, pageEntries, catalogEntries, objects).Pages[0], options ?? new PdfRenderOptions { UseSystemFonts = false });

    private static PdfDocument OpenHandWritten(string content, string resources = "<< >>", string pageEntries = "", string catalogEntries = "", params string[] objects)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R " + catalogEntries + " >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources " + resources + " /Contents 4 0 R " + pageEntries + " >>")
            .Add(4, HandWrittenPdf.Stream(content));
        for (var i = 0; i < objects.Length; i++)
        {
            file.Add(5 + i, objects[i]);
        }

        return PdfDocument.Open(file.ToArray());
    }

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
