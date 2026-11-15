using System.Xml.Linq;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.Pdf.VectorGraphics;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class QRCodeSvgRendererTests
{
    [Fact]
    public void ReadmeExample_ToSvg()
    {
        #region QRCode_ToSvg
        var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
        var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);
        var barcode = Barcode.CreateCode128("SKU-123456");

        var svg = qrCode.ToSvg();
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"330\" height=\"330\" viewBox=\"0 0 330 330\"", svg);

        var microSvg = microQr.ToSvg(new QRCodeRenderOptions { ModuleSize = 2, QuietZoneModules = 2 });
        Assert.Contains("viewBox=\"0 0 30 30\"", microSvg);

        var customSvg = qrCode.ToSvg(new QRCodeRenderOptions
        {
            ModuleSize = 10,
            QuietZoneModules = 4,
            DarkPaint = new SceneColor(0x00, 0x00, 0x00),
            LightPaint = new SceneColor(0xff, 0xff, 0xff),
            // A PNG, JPEG, GIF or WebP file, such as File.ReadAllBytes("logo.png"): the SVG embeds it
            Logo = new BarcodeLogo(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mNg+M8AAAICAQBF9FLUAAAAAElFTkSuQmCC"))
            {
                SizePercent = 20,
            },
        });
        Assert.Contains("href=\"data:image/png;base64,", customSvg);

        var barcodeSvg = barcode.ToSvg(new BarcodeRenderOptions
        {
            ModuleWidth = 2,
            ModuleHeight = 80,
            QuietZoneModules = 10,
        });
        Assert.Contains("viewBox=\"0 0 286 80\"", barcodeSvg);
        #endregion
    }

    private static readonly byte[] GreenPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mNg+M8AAAICAQBF9FLUAAAAAElFTkSuQmCC");

    [Fact]
    public void ToSvg_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_DefaultOptions_UsesModuleSize10_QuietZone4()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg();

        // Default: ModuleSize=10, QuietZoneModules=4 -> (21 + 8) * 10 = 290
        Assert.Contains("viewBox=\"0 0 290 290\"", svg);
        Assert.Contains("fill=\"#000000\"", svg);
        Assert.Contains("fill=\"#ffffff\"", svg);
    }

    [Fact]
    public void ToSvg_SmallQRCode_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeRenderOptions { ModuleSize = 1, QuietZoneModules = 0 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_MicroQR_Snapshot()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var options = new QRCodeRenderOptions { ModuleSize = 1 };
        var svg = qr.ToSvg(options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_RMQR_Snapshot()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var options = new QRCodeRenderOptions { ModuleSize = 1 };
        var svg = qr.ToSvg(options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_CustomModuleSize()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeRenderOptions { ModuleSize = 5 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_CustomColors()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeRenderOptions
        {
            DarkPaint = new SceneColor(0xff, 0x00, 0x00),
            LightPaint = new SceneColor(0x00, 0xff, 0x00),
            QuietZoneModules = 4,
            ModuleSize = 1,
        });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_TransparentColors_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeRenderOptions
        {
            DarkPaint = new SceneColor(0x11, 0x22, 0x33, 0x80),
            LightPaint = SceneColor.Transparent,
            QuietZoneModules = 4,
            ModuleSize = 1,
        });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_QuietZone()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeRenderOptions { ModuleSize = 1, QuietZoneModules = 4 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_FullSnapshot()
    {
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeRenderOptions { ModuleSize = 1, QuietZoneModules = 0 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_WithLogo_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodeRenderOptions
        {
            ModuleSize = 10,
            QuietZoneModules = 4,
            Logo = new BarcodeLogo(GreenPng) { SizePercent = 20 },
        };
        var svg = qr.ToSvg(options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_WithLogo_Snapshot_High()
    {
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.H);
        var options = new QRCodeRenderOptions
        {
            ModuleSize = 10,
            QuietZoneModules = 4,
            Logo = new BarcodeLogo(GreenPng) { SizePercent = 20 },
        };
        var svg = qr.ToSvg(options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ToSvg_InvalidLogoSizePercent_ThrowsArgumentOutOfRangeException(int logoSizePercent)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToSvg(new QRCodeRenderOptions { Logo = new BarcodeLogo(GreenPng) { SizePercent = logoSizePercent } }));

        Assert.Equal("Logo", exception.ParamName);
        Assert.Equal(logoSizePercent, exception.ActualValue);
    }

    [Fact]
    public void ToSvg_LogoIsNotAnImage_ThrowsArgumentException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentException>(() => qr.ToSvg(new QRCodeRenderOptions { Logo = new BarcodeLogo("<svg/>"u8.ToArray()) }));
    }

    [Fact]
    public void ToSvg_Logo_IsCenteredOnTheSymbol()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.H);
        var scene = qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 10, QuietZoneModules = 4, Logo = new BarcodeLogo(GreenPng) { SizePercent = 20 } });
        var image = Assert.IsType<SceneImage>(scene.Root.Children[^1]);

        // 290 * 20% = 58, centered in 290
        Assert.Equal((116d, 116d, 58d, 58d), (image.X, image.Y, image.Width, image.Height));
    }

    [Fact]
    public void ToSvg_LongPayload_ProducesValidSvg()
    {
        var data = """
            067786869696809yhjkhghgkghkjfgtyrfturtfjrftuyrgfjhftyurdftyjfjghftyurfuytfftkiftifif56565685756586586565658655685878j74564567456454534345456475478545456jk7879797@hhhhhhhhhhhhhhhhhhhhhhhhhhhhhrffffffffffffffffffffffffffffffhhhhhhhhhhhhjbjgbhgkjgkhghjghjgkjgkgkgkkhgbnadssdfsghjkghhhhhhhhhhhhhhhhhhhhhjjjjjjjjjjjjjjjgyuyghgjghjgjkgkhkkjhkljhgjlghlk067786869696809yhjkhghgkghkjfgtyrfturtfjrftuyrgfjhftycfffhsdjfhdskjksfsdfsdddddddddddsddddddddddddddddjjjjjjjjjjjjjjjjjjjjjjjjjjjjjkurdftyjfjghftyurfuytfftkiftifif56565685756586586565658655685878j74564567456454534345456475478545456jk7879797@hhhhhhhhhhhhhhhhhhhhhhhhhhhhhrfffffffffffffffffffffffffffffffffffffffghhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhjbjgbhgkjgkhghjghjgkjgkgkgkkhgbnadssdfsghjkghhhhhhhhhhhhhhhhhhhhhjjjjjjjjjjjjjjjgyuyghgjghjgjkgkhkkjhkljhgjlghlk
            """;
        var qr = QRCode.Create(data, ErrorCorrectionLevel.M);
        var svg = qr.ToSvg();
        var document = XDocument.Parse(svg);

        Assert.Equal("svg", document.Root!.Name.LocalName);
    }

    [Fact]
    public void ToSvg_ModuleSizeZero_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToSvg(new QRCodeRenderOptions { ModuleSize = 0 }));
    }

    [Fact]
    public void ToSvg_NegativeQuietZone_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToSvg(new QRCodeRenderOptions { QuietZoneModules = -1 }));
    }

    [Fact]
    public void Documentation_Logo()
    {
        #region QRCode_Logo
        // A logo: the bytes of a PNG, JPEG, GIF or WebP file, such as File.ReadAllBytes("logo.png"). Here, a blue disc
        using var logoImage = new Image<Rgba32>(64, 64, Colors.White);
        logoImage.Draw(canvas => canvas.FillCircle(new SolidBrush(new Rgba32(0x25, 0x63, 0xeb)), new PointD(32, 32), 28));
        using var logoStream = new MemoryStream();
        logoImage.Save(logoStream, new PngEncoder());

        // The error correction of level H recovers the modules under the logo
        var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.H);
        var options = new QRCodeRenderOptions
        {
            ModuleSize = 4,
            Logo = new BarcodeLogo(logoStream.ToArray()) { SizePercent = 20 },
        };

        // Every output draws the logo
        var svg = qrCode.ToSvg(options);
        using var image = qrCode.ToImage(options);
        #endregion

        Assert.Contains("data:image/png;base64,", svg, StringComparison.Ordinal);
        var center = image.Width / 2;
        Assert.Equal(new Rgba32(0x25, 0x63, 0xeb), image.Frames[0][center, center]);
        Assert.Equal("https://www.meziantou.net", Assert.Single(image.ReadQRCodes()).Text);
        DocumentationImages.AssertPng("QRCodeLogo", PngRendering.Encode(image));
    }

    [Fact]
    public void Documentation_BarcodeText()
    {
        #region Barcode_TextInSvg
        var barcode = Barcode.CreateEan13("590123412345");
        var options = new BarcodeRenderOptions
        {
            ModuleHeight = 60,
            Text = new BarcodeTextOptions { Fonts = BarcodeFonts.OcrB, FontSize = 20 },
        };

        // Viewers do not have the OCR-B font: embed it in the document
        var svg = barcode.ToSvg(options, new SvgRenderOptions { EmbedFonts = true });

        // The image (Meziantou.Framework.Imaging.Barcodes) draws the text at the same place
        using var image = barcode.ToImage(options);
        #endregion

        // The first digit left of the bars, then two groups of 6 digits between the guards
        Assert.Contains(">5</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">901234</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">123457</text>", svg, StringComparison.Ordinal);
        Assert.Contains("@font-face", svg, StringComparison.Ordinal);
        XDocument.Parse(svg);
        DocumentationImages.AssertPng("BarcodeText", PngRendering.Encode(image));
    }

    [Fact]
    public void Documentation_ToPdf()
    {
        #region Barcode_ToPdf
        var barcode = Barcode.CreateCode128("SKU-123456");

        // ToPdf comes from Meziantou.Framework.Pdf.VectorGraphics: the bars stay vectors, and OCR-B is embedded
        var scene = barcode.CreateScene(new BarcodeRenderOptions { Text = new BarcodeTextOptions() });
        var pdf = scene.ToPdf().ToByteArray(); // or .Save("barcode.pdf")
        #endregion

        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
    }

    [Fact]
    public void CreateScene_Barcode_TextIsCenteredUnderTheBars()
    {
        var barcode = Barcode.CreateCode128("Hi");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, ModuleHeight = 40, QuietZoneModules = 10, Text = new BarcodeTextOptions { FontSize = 10, Spacing = 3 } });
        var text = Assert.IsType<SceneText>(scene.Root.Children[^1]);
        var measured = new FontFaceTextMeasurer(BarcodeFonts.OcrB).Measure("Hi", text.Font);

        Assert.Equal((barcode.Width + 20) * 2, scene.Width);
        Assert.Equal(43, text.Y);
        Assert.Equal(43 + measured.Height, scene.Height);
        Assert.Equal(Math.Floor((scene.Width - measured.Width) / 2), text.X);
        Assert.Same(BarcodeFonts.OcrB, scene.Fonts);
    }

    [Fact]
    public void CreateScene_Barcode_TextWiderThanTheBarsWidensTheDrawing()
    {
        var barcode = Barcode.CreateCode128("Hi");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 1, QuietZoneModules = 0, Text = new BarcodeTextOptions { FontSize = 100 } });
        var bars = Assert.IsType<ScenePath>(scene.Root.Children[1]);
        var text = Assert.IsType<SceneText>(scene.Root.Children[^1]);

        Assert.Equal(0, text.X);
        Assert.Equal(text.MeasuredWidth, scene.Width);
        Assert.Equal(Math.Floor((scene.Width - barcode.Width) / 2), bars.Data.Commands[0].X);
    }

    [Fact]
    public void CreateScene_Barcode_TextPaint()
    {
        var barcode = Barcode.CreateCode128("Hi");
        var gradient = new LinearGradientPaint(0, 0, 10, 0, [new SceneGradientStop(0, SceneColor.Black), new SceneGradientStop(1, SceneColor.White)]);
        var solid = Assert.IsType<SceneText>(barcode.CreateScene(new BarcodeRenderOptions { DarkPaint = new SceneColor(1, 2, 3), Text = new BarcodeTextOptions() }).Root.Children[^1]);
        var painted = Assert.IsType<SceneText>(barcode.CreateScene(new BarcodeRenderOptions { Text = new BarcodeTextOptions { Paint = gradient } }).Root.Children[^1]);

        Assert.Equal(new SceneColor(1, 2, 3), solid.Color);
        Assert.Null(solid.Fill);
        Assert.Same(gradient, painted.Fill);
    }

    [Fact]
    public void CreateScene_Ean13_GroupsTheDigitsBetweenTheGuards()
    {
        var barcode = Barcode.CreateEan13("590123412345");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, ModuleHeight = 40, QuietZoneModules = 10, Text = new BarcodeTextOptions { FontSize = 10, Spacing = 3 } });
        var texts = GetTexts(scene);
        var bars = GetBars(scene);

        Assert.Equal(["5", "901234", "123457"], texts.Select(text => text.Text));
        Assert.Equal((95 + 20) * 2, scene.Width);

        // The guard bars (modules 0-3, 45-50, 92-95) extend 5 modules below the data bars
        Assert.Equal((20d, 0d, 22d, 50d), bars[0]); // start guard
        Assert.Equal((24d, 0d, 26d, 50d), bars[1]);
        Assert.Equal(40, bars[2].Bottom); // the first data bar
        Assert.Equal([50d, 50d], bars.Where(bar => bar.Left >= 20 + (45 * 2) && bar.Right <= 20 + (50 * 2)).Select(bar => bar.Bottom));
        Assert.Equal(50, bars[^1].Bottom); // end guard

        // The first digit is left of the bars, the groups are centered between the guards, under the data bars
        Assert.True(texts[0].X + texts[0].MeasuredWidth <= 20 - 2);
        Assert.Equal(Math.Floor(20 + (3 * 2) + (((42 * 2) - texts[1].MeasuredWidth) / 2)), texts[1].X);
        Assert.Equal(Math.Floor(20 + (50 * 2) + (((42 * 2) - texts[2].MeasuredWidth) / 2)), texts[2].X);
        Assert.All(texts, text => Assert.Equal(43, text.Y));
        Assert.All(texts, text => Assert.Equal(10, text.Font.Size));
        Assert.Equal(43 + Measure(texts[1]).Height, scene.Height);
    }

    [Fact]
    public void CreateScene_Ean8_GroupsTheDigitsByFour()
    {
        var barcode = Barcode.CreateEan8("5512345");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, ModuleHeight = 40, QuietZoneModules = 7, Text = new BarcodeTextOptions { FontSize = 10 } });
        var texts = GetTexts(scene);

        Assert.Equal(["5512", "3457"], texts.Select(text => text.Text));
        Assert.Equal((67 + 14) * 2, scene.Width);
        Assert.Equal(Math.Floor(14 + (3 * 2) + (((28 * 2) - texts[0].MeasuredWidth) / 2)), texts[0].X);
        Assert.Equal(Math.Floor(14 + (36 * 2) + (((28 * 2) - texts[1].MeasuredWidth) / 2)), texts[1].X);
        Assert.Equal([50d, 50d, 50d, 50d, 50d, 50d], GetBars(scene).Where(bar => bar.Bottom > 40).Select(bar => bar.Bottom)); // 2 bars per guard
    }

    [Fact]
    public void CreateScene_UpcA_PrintsTheNumberSystemAndCheckDigitsSmallerOutsideTheBars()
    {
        var barcode = Barcode.CreateUpcA("03600029145");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, ModuleHeight = 40, QuietZoneModules = 9, Text = new BarcodeTextOptions { FontSize = 10 } });
        var texts = GetTexts(scene);
        var bars = GetBars(scene);

        Assert.Equal(["0", "36000", "29145", "2"], texts.Select(text => text.Text));
        Assert.Equal([8d, 10d, 10d, 8d], texts.Select(text => text.Font.Size));
        Assert.True(texts[0].X + texts[0].MeasuredWidth <= 18 - 2);
        Assert.Equal(18 + (95 * 2) + 2, texts[3].X);
        Assert.Equal(Math.Floor(18 + (10 * 2) + (((35 * 2) - texts[1].MeasuredWidth) / 2)), texts[1].X);

        // The small digits stand on the baseline of the others
        Assert.Equal(texts[1].Y + Measure(texts[1]).Ascent, texts[0].Y + Measure(texts[0]).Ascent, 1e-9);

        // The bars of the first and last characters extend like the guards
        Assert.All(bars.Where(bar => bar.Right <= 18 + (10 * 2) || bar.Left >= 18 + (85 * 2)), bar => Assert.Equal(50, bar.Bottom));
        Assert.All(bars.Where(bar => bar.Left >= 18 + (10 * 2) && bar.Right <= 18 + (45 * 2)), bar => Assert.Equal(40, bar.Bottom));
    }

    [Fact]
    public void CreateScene_UpcE_PrintsSixDigitsUnderTheBars()
    {
        var barcode = Barcode.CreateUpcE("0425261");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, ModuleHeight = 40, QuietZoneModules = 9, Text = new BarcodeTextOptions { FontSize = 10 } });
        var texts = GetTexts(scene);

        Assert.Equal(["0", "425261", "4"], texts.Select(text => text.Text));
        Assert.Equal(Math.Floor(18 + (3 * 2) + (((42 * 2) - texts[1].MeasuredWidth) / 2)), texts[1].X);
        Assert.Equal([50d, 50d, 50d, 50d, 50d], GetBars(scene).Where(bar => bar.Bottom > 40).Select(bar => bar.Bottom)); // "101" and "010101"
    }

    [Fact]
    public void CreateScene_Ean13WithAddOn_PrintsTheAddOnAboveItsShorterBars()
    {
        var barcode = Barcode.CreateEan13("978020137962", "52499");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, ModuleHeight = 40, QuietZoneModules = 10, Text = new BarcodeTextOptions { FontSize = 10, Spacing = 3 } });
        var texts = GetTexts(scene);
        var addOnLeft = 20 + ((95 + 7) * 2);
        var addOnBars = GetBars(scene).Where(bar => bar.Left >= addOnLeft).ToArray();
        var addOnBarsTop = Math.Ceiling(Measure(texts[^1]).Height + 3);

        Assert.Equal(["9", "780201", "379624", "52499"], texts.Select(text => text.Text));
        Assert.Equal(0, texts[^1].Y);
        Assert.Equal(Math.Floor(addOnLeft + (((47 * 2) - texts[^1].MeasuredWidth) / 2)), texts[^1].X);
        Assert.All(addOnBars, bar => Assert.Equal((addOnBarsTop, 50d), (bar.Top, bar.Bottom)));
        Assert.Equal((95 + 7 + 47 + 20) * 2, scene.Width);
    }

    [Fact]
    public void CreateScene_UpcAWithAddOn_PrintsTheCheckDigitInTheGap()
    {
        var barcode = Barcode.CreateUpcA("03600029145", "12");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, Text = new BarcodeTextOptions() });
        var texts = GetTexts(scene);

        Assert.Equal(["0", "36000", "29145", "2", "12"], texts.Select(text => text.Text));
        Assert.True(texts[3].X + texts[3].MeasuredWidth <= 20 + ((95 + 9) * 2) - 2);
        Assert.Equal((95 + 9 + 20 + 20) * 2, scene.Width); // the quiet zone after the add-on is not widened
        AssertLayoutIsValid(scene);
    }

    [Fact]
    public void CreateScene_Ean13_NoQuietZone_WidensTheLeftSideForTheFirstDigit()
    {
        var barcode = Barcode.CreateEan13("590123412345");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 2, QuietZoneModules = 0, Text = new BarcodeTextOptions() });
        var texts = GetTexts(scene);
        var bars = GetBars(scene);
        var left = Math.Ceiling(texts[0].MeasuredWidth + 2);

        Assert.Equal(left, bars[0].Left);
        Assert.Equal(left + (95 * 2), scene.Width);
        Assert.Equal(0, texts[0].X);
    }

    [Fact]
    public void CreateScene_Ean13_LargeFont_ShrinksTheDigitsToFitBetweenTheGuards()
    {
        var barcode = Barcode.CreateEan13("590123412345", "12");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = 1, Text = new BarcodeTextOptions { FontSize = 100 } });
        var texts = GetTexts(scene);

        Assert.All(texts, text => Assert.True(text.Font.Size < 100));
        Assert.Equal(42, Math.Max(texts[1].MeasuredWidth, texts[2].MeasuredWidth), 1e-9); // the widest group fills its range
        AssertLayoutIsValid(scene);
    }

    [Fact]
    public void CreateScene_Ean13WithoutText_DrawsBarsOfOneHeight()
    {
        var scene = Barcode.CreateEan13("590123412345").CreateScene(new BarcodeRenderOptions { ModuleHeight = 40 });

        Assert.All(GetBars(scene), bar => Assert.Equal((0d, 40d), (bar.Top, bar.Bottom)));
        Assert.Empty(GetTexts(scene));
    }

    [Theory]
    [InlineData(BarcodeType.Ean8, "5512345", null)]
    [InlineData(BarcodeType.Ean8, "5512345", "12")]
    [InlineData(BarcodeType.Ean13, "590123412345", null)]
    [InlineData(BarcodeType.Ean13, "590123412345", "52495")]
    [InlineData(BarcodeType.UpcA, "03600029145", null)]
    [InlineData(BarcodeType.UpcA, "03600029145", "52495")]
    [InlineData(BarcodeType.UpcE, "0425261", null)]
    [InlineData(BarcodeType.UpcE, "0425261", "12")]
    public void CreateScene_EanUpc_DigitsDoNotOverlapTheBarsNorEachOther(BarcodeType type, string data, string? addOn)
    {
        var barcode = type switch
        {
            BarcodeType.Ean8 => Barcode.CreateEan8(data, addOn),
            BarcodeType.Ean13 => Barcode.CreateEan13(data, addOn),
            BarcodeType.UpcA => Barcode.CreateUpcA(data, addOn),
            _ => Barcode.CreateUpcE(data, addOn),
        };

        foreach (var (moduleWidth, quietZone, fontSize, snap) in new[] { (2d, 10, 14d, true), (1d, 0, 30d, true), (0.7, 3, 9d, false), (3d, 1, 200d, false) })
        {
            var scene = barcode.CreateScene(new BarcodeRenderOptions { ModuleWidth = moduleWidth, ModuleHeight = 30, QuietZoneModules = quietZone, SnapToPixels = snap, Text = new BarcodeTextOptions { FontSize = fontSize } });

            Assert.Equal(barcode.Text.Replace(" ", "", StringComparison.Ordinal), string.Concat(GetTexts(scene).Select(text => text.Text)));
            AssertLayoutIsValid(scene);
        }
    }

    // The digits are inside the drawing, and their boxes overlap neither the bars nor each other
    private static void AssertLayoutIsValid(Scene scene)
    {
        const double Tolerance = 1e-9;
        var boxes = GetTexts(scene).Select(text => (Left: text.X, Top: text.Y, Right: text.X + text.MeasuredWidth, Bottom: text.Y + Measure(text).Height)).ToArray();
        var bars = GetBars(scene);
        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            Assert.True(box.Left >= -Tolerance && box.Top >= -Tolerance && box.Right <= scene.Width + Tolerance && box.Bottom <= scene.Height + Tolerance, $"Text {i} {box} is outside the scene ({scene.Width}x{scene.Height})");
            Assert.DoesNotContain(bars, bar => Overlap(box, bar));
            for (var j = i + 1; j < boxes.Length; j++)
            {
                Assert.False(Overlap(box, boxes[j]), $"Texts {i} and {j} overlap");
            }
        }

        static bool Overlap((double Left, double Top, double Right, double Bottom) a, (double Left, double Top, double Right, double Bottom) b)
            => a.Left < b.Right - Tolerance && b.Left < a.Right - Tolerance && a.Top < b.Bottom - Tolerance && b.Top < a.Bottom - Tolerance;
    }

    private static SceneText[] GetTexts(Scene scene) => scene.Root.Children.OfType<SceneText>().ToArray();

    private static TextMetrics Measure(SceneText text) => new FontFaceTextMeasurer(BarcodeFonts.OcrB).Measure(text.Text, text.Font);

    // Each run of dark modules is a rectangle: a move, three lines and a close
    private static (double Left, double Top, double Right, double Bottom)[] GetBars(Scene scene)
    {
        var commands = scene.Root.Children.OfType<ScenePath>().Single().Data.Commands;
        var bars = new (double Left, double Top, double Right, double Bottom)[commands.Count / 5];
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i] = (commands[i * 5].X, commands[i * 5].Y, commands[(i * 5) + 2].X, commands[(i * 5) + 2].Y);
        }

        return bars;
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(double.NaN, 2)]
    [InlineData(14, -1)]
    [InlineData(14, double.PositiveInfinity)]
    public void CreateScene_Barcode_InvalidText_Throws(double fontSize, double spacing)
    {
        var barcode = Barcode.CreateCode128("Hi");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.CreateScene(new BarcodeRenderOptions { Text = new BarcodeTextOptions { FontSize = fontSize, Spacing = spacing } }));
    }

    [Fact]
    public void CreateScene_NullLightPaint_HasNoBackground()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var scene = qr.CreateScene(new QRCodeRenderOptions { LightPaint = null });

        Assert.IsType<ScenePath>(Assert.Single(scene.Root.Children));
    }

    [Fact]
    public void CreateScene_SnapToPixels_RoundsTheModuleSize()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Equal(29 * 3, qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 2.6 }).Width);
        Assert.Equal(29 * 2.6, qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 2.6, SnapToPixels = false }).Width, 1e-9);
        Assert.Equal(29, qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 0.2 }).Width);
    }

    [Fact]
    public void BarcodeFonts_AreOcrAAndOcrB()
    {
        Assert.Equal("Meziantou OCR-A", BarcodeFonts.OcrA.DefaultFamily);
        Assert.Equal("Meziantou OCR-B", BarcodeFonts.OcrB.DefaultFamily);
        Assert.Same(BarcodeFonts.OcrB, BarcodeFonts.OcrB);
    }

    [Fact]
    public void CreateScene_Barcode_ControlCharactersOfTheTextAreEscaped()
    {
        // Code 128 encodes every ASCII character: the text is one line, as it is measured
        var barcode = Barcode.CreateCode128("AB\nCD\u001b");
        var scene = barcode.CreateScene(new BarcodeRenderOptions { Text = new BarcodeTextOptions() });
        var text = Assert.IsType<SceneText>(scene.Root.Children[^1]);

        Assert.Equal("AB?CD?", text.Text);
        Assert.Equal(new FontFaceTextMeasurer(BarcodeFonts.OcrB).Measure("AB?CD?", text.Font).Width, text.MeasuredWidth);
    }
}
