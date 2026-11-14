using System.Xml.Linq;
using Meziantou.Framework.Imaging.Barcodes;
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
        var logoBytes = GreenPng;
        #region QRCode_Logo
        // The error correction of level H recovers the modules under the logo
        var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.H);
        var options = new QRCodeRenderOptions
        {
            // The bytes of a PNG, JPEG, GIF or WebP file, such as File.ReadAllBytes("logo.png")
            Logo = new BarcodeLogo(logoBytes) { SizePercent = 20 },
        };

        // Every output draws the logo
        var svg = qrCode.ToSvg(options);
        using var image = qrCode.ToImage(options);
        #endregion

        Assert.Contains("data:image/png;base64,", svg, StringComparison.Ordinal);
        var x = image.Width / 2;
        Assert.Equal(new Imaging.Rgba32(0, 255, 0, 255), ((Imaging.Image<Imaging.Rgba32>)image).Frames[0][x, x]);
    }

    [Fact]
    public void Documentation_BarcodeText()
    {
        #region Barcode_TextInSvg
        var barcode = Barcode.CreateEan13("590123412345");
        var svg = barcode.ToSvg(
            new BarcodeRenderOptions
            {
                Text = new BarcodeTextOptions { Fonts = BarcodeFonts.OcrB, FontSize = 14 },
            },
            new SvgRenderOptions { EmbedFonts = true }); // viewers do not have OCR-B: embed it
        #endregion

        Assert.Contains(">5901234123457</text>", svg, StringComparison.Ordinal);
        Assert.Contains("@font-face", svg, StringComparison.Ordinal);
        XDocument.Parse(svg);
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
}
