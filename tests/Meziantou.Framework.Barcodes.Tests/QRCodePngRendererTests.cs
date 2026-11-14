using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.VectorGraphics;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class QRCodePngRendererTests
{
    [Fact]
    public void ReadmeExample_ToPng()
    {
        #region QRCode_ToPng
        var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
        var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);
        var barcode = Barcode.CreateCode39("ABC-123", includeChecksum: true);

        // ToImage comes from Meziantou.Framework.Imaging.Barcodes. Save the image to a file (image.Save("qrcode.png"))
        // or to a stream, in any format
        using var image = qrCode.ToImage();
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        Assert.Equal((330, 330), (image.Width, image.Height));

        using var microImage = microQr.ToImage(new QRCodeRenderOptions
        {
            ModuleSize = 4,
            QuietZoneModules = 2,
            DarkPaint = new SceneColor(0x1f, 0x29, 0x37),
            LightPaint = new SceneColor(0xf9, 0xfa, 0xfb),
        });
        Assert.Equal((60, 60), (microImage.Width, microImage.Height));

        // Twice as many pixels, for high-density screens
        using var barcodeImage = barcode.ToImage(
            new BarcodeRenderOptions
            {
                ModuleWidth = 2,
                ModuleHeight = 80,
                DarkPaint = new SceneColor(0x0f, 0x76, 0x6e),
                LightPaint = new SceneColor(0xec, 0xfe, 0xff),
            },
            new SceneImageOptions { Scale = 2 });
        Assert.Equal(160, barcodeImage.Height); // 80 units high, at 2 pixels per unit
        #endregion

        Assert.NotEqual(0, stream.Length);
    }

    [Fact]
    public void Documentation_Colors()
    {
        #region QRCode_Colors
        var qrCode = QRCode.Create("https://www.meziantou.net");
        var options = new QRCodeRenderOptions
        {
            ModuleSize = 4,
            QuietZoneModules = 2,
            DarkPaint = SceneColor.Parse("#1e3a8a"),
            LightPaint = SceneColor.Parse("rgb(239, 246, 255)"),
        };

        var svg = qrCode.ToSvg(options);
        using var image = qrCode.ToImage(options);
        #endregion

        Assert.Contains("#1e3a8a", svg, StringComparison.Ordinal);
        DocumentationImages.AssertPng("Colors", qrCode.ToPng(options));
    }

    public static IEnumerable<object[]> ModuleSizes
    {
        get
        {
            for (var moduleSize = 1; moduleSize <= 10; moduleSize++)
            {
                yield return [moduleSize];
            }
        }
    }

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();
        foreach (var scenario in QRCodePngScenarios.All)
        {
            data.Add(scenario.Name);
        }

        return data;
    }

    public static TheoryData<QRCodeType, int, int> Layouts()
    {
        var data = new TheoryData<QRCodeType, int, int>();
        foreach (var type in new[] { QRCodeType.Standard, QRCodeType.MicroQR, QRCodeType.RMQR })
        {
            // A module size of 1 with no quiet zone leaves a row width that is not a multiple of
            // 8, which is where the bit packing is most likely to go wrong.
            foreach (var moduleSize in new[] { 1, 2, 3, 7 })
            {
                foreach (var quietZoneModules in new[] { 0, 1, 4 })
                {
                    data.Add(type, moduleSize, quietZoneModules);
                }
            }
        }

        return data;
    }

    [Fact]
    public void ToPng_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var png = qr.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_DefaultOptions_UsesModuleSize10_QuietZone4()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng();

        var parsed = ParsePng(png);
        Assert.Equal(290, parsed.Width);
        Assert.Equal(290, parsed.Height);
    }

    [Theory]
    [MemberData(nameof(ModuleSizes))]
    public void ToPng_UsesConfiguredModuleSize(int moduleSize)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodeRenderOptions { ModuleSize = moduleSize });

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_MicroQr_HasExpectedDimensions()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodeRenderOptions { ModuleSize = 2, QuietZoneModules = 1 });

        var parsed = ParsePng(png);
        Assert.Equal((11 + 2) * 2, parsed.Width);
        Assert.Equal((11 + 2) * 2, parsed.Height);
    }

    [Fact]
    public void ToPng_Rmqr_HasExpectedDimensions()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var png = qr.ToPng(new QRCodeRenderOptions { ModuleSize = 3, QuietZoneModules = 0 });

        var parsed = ParsePng(png);
        Assert.Equal(27 * 3, parsed.Width);
        Assert.Equal(11 * 3, parsed.Height);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ToPng_PixelsMatchTheModuleMatrix(QRCodeType type, int moduleSize, int quietZoneModules)
    {
        var qr = CreateSample(type);
        var png = qr.ToPng(new QRCodeRenderOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules });

        var parsed = ParsePng(png);
        Assert.Equal((qr.Width + (2 * quietZoneModules)) * moduleSize, parsed.Width);
        Assert.Equal((qr.Height + (2 * quietZoneModules)) * moduleSize, parsed.Height);

        for (var y = 0; y < parsed.Height; y++)
        {
            for (var x = 0; x < parsed.Width; x++)
            {
                var row = (y / moduleSize) - quietZoneModules;
                var column = (x / moduleSize) - quietZoneModules;
                var expected = row >= 0 && row < qr.Height && column >= 0 && column < qr.Width && qr[row, column];
                if (expected != parsed.IsSet(y, x))
                {
                    Assert.Fail($"Pixel ({x}, {y}) is {(expected ? "light" : "dark")} but module ({column}, {row}) is {(expected ? "dark" : "light")}.");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void ToPng_Scenario(string name)
    {
        var scenario = QRCodePngScenarios.Get(name);

        Snapshot.Validate(scenario.CreatePng(), SnapshotType.Png);
    }

    [Fact]
    public void Scenarios_HaveUniqueNames()
    {
        // Two scenarios sharing a name would share a snapshot file, so one of them would never
        // be checked against anything.
        Assert.Distinct(QRCodePngScenarios.All.Select(scenario => scenario.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void ToPng_IsDeterministic()
    {
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.Q);

        Assert.Equal(qr.ToPng(), qr.ToPng());
    }

    [Fact]
    public void ToPng_CustomColors_UsesConfiguredRgb()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var custom = ParsePng(qr.ToPng(new QRCodeRenderOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
            DarkPaint = new SceneColor(0x11, 0x22, 0x33),
            LightPaint = new SceneColor(0xaa, 0xbb, 0xcc),
        }));

        for (var y = 0; y < custom.Height; y++)
        {
            for (var x = 0; x < custom.Width; x++)
            {
                var expected = qr[y, x] ? new Rgba32(0x11, 0x22, 0x33, 0xff) : new Rgba32(0xaa, 0xbb, 0xcc, 0xff);
                Assert.Equal(expected, custom.Pixels[x, y]);
            }
        }
    }

    [Fact]
    public void ToPng_NullLightPaint_IsTransparent()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var parsed = ParsePng(qr.ToPng(new QRCodeRenderOptions { ModuleSize = 1, QuietZoneModules = 1, LightPaint = null }));

        Assert.Equal(0, parsed.Pixels[0, 0].A);
        Assert.True(parsed.IsSet(1, 1));
    }

    [Fact]
    public void ToPng_TransparentColors_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodeRenderOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
            DarkPaint = new SceneColor(0x11, 0x22, 0x33, 0x80),
            LightPaint = SceneColor.Transparent,
        });

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ToPng_InvalidModuleSize_ThrowsArgumentOutOfRangeException(int moduleSize)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodeRenderOptions { ModuleSize = moduleSize }));
    }

    [Fact]
    public void ToPng_NegativeQuietZone_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodeRenderOptions { QuietZoneModules = -1 }));
    }

    [Fact]
    public void ToPng_ModuleSizeOverflowsTheImageDimensions_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        // The size is rejected before anything is allocated, so this must not try to build a
        // 21 * int.MaxValue pixel image.
        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodeRenderOptions { ModuleSize = int.MaxValue }));
    }

    [Fact]
    public void ToPng_QuietZoneOverflowsTheImageDimensions_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodeRenderOptions { ModuleSize = 1, QuietZoneModules = int.MaxValue }));
    }

    [Fact]
    public void ToPng_BarcodePixelsMatchTheModules()
    {
        var barcode = Barcode.CreateCode128("Hello 123");
        foreach (var (moduleWidth, quietZoneModules) in new[] { (1, 0), (1, 3), (3, 1), (5, 2) })
        {
            var parsed = ParsePng(barcode.ToPng(new BarcodeRenderOptions { ModuleWidth = moduleWidth, ModuleHeight = 3, QuietZoneModules = quietZoneModules }));
            Assert.Equal((barcode.Width + (2 * quietZoneModules)) * moduleWidth, parsed.Width);
            Assert.Equal(barcode.Height * 3, parsed.Height);

            for (var y = 0; y < parsed.Height; y++)
            {
                for (var x = 0; x < parsed.Width; x++)
                {
                    var column = (x / moduleWidth) - quietZoneModules;
                    var expected = column >= 0 && column < barcode.Width && barcode[y / 3, column];
                    if (expected != parsed.IsSet(y, x))
                    {
                        Assert.Fail($"Pixel ({x}, {y}) with module width {moduleWidth} and quiet zone {quietZoneModules} is wrong.");
                    }
                }
            }
        }
    }

    private static QRCode CreateSample(QRCodeType type)
    {
        return type switch
        {
            QRCodeType.MicroQR => QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L),
            QRCodeType.RMQR => QRCode.CreateRMQR("https://example.com", ErrorCorrectionLevel.M),
            _ => QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.M),
        };
    }

    /// <summary>The pixels of a PNG file; a pixel is set when it is darker than middle gray.</summary>
    private sealed class ParsedPng(Image<Rgba32> image)
    {
        public int Width { get; } = image.Width;

        public int Height { get; } = image.Height;

        public Rgba32[,] Pixels { get; } = ReadPixels(image);

        public bool IsSet(int row, int column) => Pixels[column, row] is { A: > 127 } pixel && pixel.R + pixel.G + pixel.B < 384;

        private static Rgba32[,] ReadPixels(Image<Rgba32> image)
        {
            var bytes = new byte[image.Width * image.Height * 4];
            image.Frames[0].CopyPixelBytesTo(bytes);
            var pixels = new Rgba32[image.Width, image.Height];
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var offset = ((y * image.Width) + x) * 4;
                    pixels[x, y] = new Rgba32(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
                }
            }

            return pixels;
        }
    }

    private static ParsedPng ParsePng(byte[] data)
    {
        using var image = Image.Load<Rgba32>(data);
        return new ParsedPng(image);
    }
}
