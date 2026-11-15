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
            Logo = new BarcodeLogo(GreenPng) { SizePercent = 20, ClearModules = false },
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

    [Fact]
    public void ToSvg_WithLogo_Backdrop_Snapshot()
    {
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.H);
        var options = new QRCodeRenderOptions
        {
            ModuleSize = 10,
            QuietZoneModules = 4,
            LightPaint = null,
            Logo = new BarcodeLogo(GreenPng) { SizePercent = 20, MarginModules = 1, BackdropPaint = new SceneColor(0xee, 0xee, 0xee), BackdropCornerRadius = 1.5 },
        };
        var svg = qr.ToSvg(options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Logo_ClearModules_SkipsExactlyTheModulesAroundTheLogo(int margin)
    {
        // Version 4 (33x33): the logo is 82x82 at (164, 164), over modules 12 to 20
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.H);
        var scene = qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 10, QuietZoneModules = 4, Logo = new BarcodeLogo(GreenPng) { MarginModules = margin } });
        var image = Assert.IsType<SceneImage>(scene.Root.Children[^1]);
        Assert.Equal((164d, 164d, 82d, 82d), (image.X, image.Y, image.Width, image.Height));

        var first = 12 - margin;
        var end = 21 + margin;
        var drawn = GetDrawnModules(scene, qr, moduleSize: 10, quietZoneModules: 4);
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                var isCleared = row >= first && row < end && column >= first && column < end;
                Assert.Equal(qr[row, column] && !isCleared, drawn[row, column]);
            }
        }

        // No backdrop without a backdrop paint: the light paint shows
        Assert.Equal(2, scene.Root.Children.OfType<SceneRectangle>().Count() + scene.Root.Children.OfType<ScenePath>().Count());
    }

    [Fact]
    public void Logo_Backdrop_IsOnTheModuleBoundaries()
    {
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.H);
        var backdropPaint = new SceneColor(0xee, 0xee, 0xee);
        var scene = qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 10, QuietZoneModules = 4, Logo = new BarcodeLogo(GreenPng) { MarginModules = 1, BackdropPaint = backdropPaint, BackdropCornerRadius = 100 } });

        // Modules 11 to 21, under the logo
        var backdrop = Assert.IsType<SceneRectangle>(scene.Root.Children[^2]);
        Assert.Equal((150d, 150d, 110d, 110d), (backdrop.X, backdrop.Y, backdrop.Width, backdrop.Height));
        Assert.Equal(backdropPaint, Assert.IsType<SolidPaint>(backdrop.Fill).Color);

        // The radius is at most half the side
        Assert.Equal(55, backdrop.CornerRadius);
    }

    [Fact]
    public void Logo_ClearModulesFalse_DrawsEveryModule()
    {
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.L);
        var scene = qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 10, QuietZoneModules = 4, Logo = new BarcodeLogo(GreenPng) { ClearModules = false, BackdropPaint = SceneColor.White } });
        var drawn = GetDrawnModules(scene, qr, moduleSize: 10, quietZoneModules: 4);

        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                Assert.Equal(qr[row, column], drawn[row, column]);
            }
        }

        Assert.Single(scene.Root.Children.OfType<SceneRectangle>());
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    public void Logo_ClearedAreaExceedsTheErrorCorrection_ThrowsArgumentOutOfRangeException(ErrorCorrectionLevel level)
    {
        // The 7x7 modules under the logo have modules of 10 codewords of a block, more than levels L and M recover
        var qr = QRCode.Create("https://www.meziantou.net", level);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => qr.CreateScene(new QRCodeRenderOptions { Logo = new BarcodeLogo(GreenPng) }));

        Assert.Equal("Logo", exception.ParamName);
        Assert.Contains("has modules of 10 codewords of an error correction block", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Logo_ClearedAreaCoversTheFormatInformation_ThrowsArgumentOutOfRangeException()
    {
        // Version 1 (21x21): a logo of 20% reaches the separators of the finder patterns
        var qr = QRCode.Create("A", ErrorCorrectionLevel.H);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToSvg(new QRCodeRenderOptions { Logo = new BarcodeLogo(GreenPng) }));

        Assert.Equal("Logo", exception.ParamName);
        Assert.Contains("covers the module (7, 7) of a finder pattern", exception.Message, StringComparison.Ordinal);

        // Drawing the modules under the logo is not checked
        Assert.Contains("<image", qr.ToSvg(new QRCodeRenderOptions { Logo = new BarcodeLogo(GreenPng) { ClearModules = false } }), StringComparison.Ordinal);
    }

    [Fact]
    public void Logo_SmallSymbols_ClearAccordingToTheirErrorCorrection()
    {
        // Micro QR codes and rMQR codes are small: a centered logo hits a codeword-heavy area, or the timing pattern of an rMQR code
        var microQR = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.M);
        Assert.Throws<ArgumentOutOfRangeException>(() => microQR.CreateScene(new QRCodeRenderOptions { Logo = new BarcodeLogo(GreenPng) { SizePercent = 10 } }));

        var rmqr = QRCode.CreateRMQR("HELLO WORLD", ErrorCorrectionLevel.H);
        Assert.Throws<ArgumentOutOfRangeException>(() => rmqr.CreateScene(new QRCodeRenderOptions { Logo = new BarcodeLogo(GreenPng) { SizePercent = 10 } }));
    }

    [Theory]
    [InlineData(-1, 0d)]
    [InlineData(0, -1d)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    public void Logo_InvalidMarginOrCornerRadius_ThrowsArgumentOutOfRangeException(int margin, double cornerRadius)
    {
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.H);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => qr.CreateScene(new QRCodeRenderOptions { Logo = new BarcodeLogo(GreenPng) { MarginModules = margin, BackdropCornerRadius = cornerRadius } }));

        Assert.Equal("Logo", exception.ParamName);
    }

    // The modules the dark path covers: the center of each module is in one of its rectangles
    private static bool[,] GetDrawnModules(Scene scene, QRCode qr, double moduleSize, int quietZoneModules)
    {
        var path = Assert.Single(scene.Root.Children.OfType<ScenePath>()).Data;
        var vertices = new List<(double X, double Y)>();
        var rectangles = new List<(double Left, double Top, double Right, double Bottom)>();
        foreach (var command in path.Commands)
        {
            if (command.Kind is PathCommandKind.MoveTo && vertices.Count > 0)
            {
                AddRectangle();
            }

            if (command.Kind is not PathCommandKind.Close)
            {
                vertices.Add((command.X, command.Y));
            }
        }

        AddRectangle();
        var drawn = new bool[qr.Height, qr.Width];
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                var x = (quietZoneModules + column + 0.5) * moduleSize;
                var y = (quietZoneModules + row + 0.5) * moduleSize;
                drawn[row, column] = rectangles.Any(rectangle => x > rectangle.Left && x < rectangle.Right && y > rectangle.Top && y < rectangle.Bottom);
            }
        }

        return drawn;

        void AddRectangle()
        {
            rectangles.Add((vertices.Min(point => point.X), vertices.Min(point => point.Y), vertices.Max(point => point.X), vertices.Max(point => point.Y)));
            vertices.Clear();
        }
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

        Assert.Throws<ArgumentException>(() => qr.ToSvg(new QRCodeRenderOptions { Logo = new BarcodeLogo("<svg/>"u8.ToArray()) { ClearModules = false } }));
    }

    [Fact]
    public void ToSvg_Logo_IsCenteredOnTheSymbol()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.H);
        var scene = qr.CreateScene(new QRCodeRenderOptions { ModuleSize = 10, QuietZoneModules = 4, Logo = new BarcodeLogo(GreenPng) { SizePercent = 20, ClearModules = false } });
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

        // The modules around the logo are not drawn: the error correction of level H recovers them
        var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.H);
        var options = new QRCodeRenderOptions
        {
            ModuleSize = 4,
            Logo = new BarcodeLogo(logoStream.ToArray())
            {
                SizePercent = 20,
                MarginModules = 1, // A ring of light modules around the logo
            },
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

        Assert.Contains(">5901234123457</text>", svg, StringComparison.Ordinal);
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
