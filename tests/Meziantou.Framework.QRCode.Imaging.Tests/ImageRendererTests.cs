using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Tests;

public sealed class ImageRendererTests
{
    [Fact]
    public void ReadmeExample_QRCodeToImage()
    {
        #region QRCodeImaging_QRCodeToImage
        var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);

        // Default options: 10 pixels per module, a 4-module quiet zone, black on white
        using var image = qrCode.ToImage();
        Assert.Equal((330, 330), (image.Width, image.Height));

        // Save to a file (image.Save("qrcode.webp", new WebPEncoder())) or to a stream
        using var webp = new MemoryStream();
        image.Save(webp, new WebPEncoder());

        // Custom options
        using var customImage = qrCode.ToImage(new QRCodeImageOptions
        {
            ModuleSize = 4,
            QuietZoneModules = 2,
            DarkColor = new Rgba32(0x00, 0x33, 0x66),
            LightColor = Colors.Transparent,
        });
        Assert.Equal((116, 116), (customImage.Width, customImage.Height));
        Assert.Equal(Colors.Transparent, customImage.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(0x00, 0x33, 0x66), customImage.Frames[0][8, 8]);

        // An indexed PNG stores the two colors in a palette, as ToPng does
        using var png = new MemoryStream();
        customImage.Save(png, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
        #endregion
    }

    [Fact]
    public void ReadmeExample_BarcodeToImage()
    {
        #region QRCodeImaging_BarcodeToImage
        var barcode = Barcode.CreateEan13("5901234123457");
        using var image = barcode.ToImage(new BarcodeImageOptions { ModuleWidth = 3, ModuleHeight = 100 });
        Assert.Equal((345, 100), (image.Width, image.Height));
        #endregion
    }

    [Fact]
    public void ReadmeExample_DrawOntoImage()
    {
        #region QRCodeImaging_DrawOntoImage
        using var ticket = new Image<Rgba32>(400, 200, Colors.White); // or Image.Load<Rgba32>("ticket.png")
        using var qrCodeImage = QRCode.Create("TICKET-0042").ToImage(new QRCodeImageOptions { ModuleSize = 4 });
        ticket.Draw(canvas => canvas.DrawImage(qrCodeImage.Frames[0], new PointD(16, 16)));
        Assert.Equal(Colors.Black, ticket.Frames[0][32, 32]); // the top-left finder pattern, after the 16-pixel quiet zone

        // Save to a file (ticket.Save("ticket-with-qrcode.png")) or to a stream
        using var png = new MemoryStream();
        ticket.Save(png, new PngEncoder());
        #endregion
    }

    public static TheoryData<QRCodeType, int, int> QRCodeLayouts()
    {
        var data = new TheoryData<QRCodeType, int, int>();
        foreach (var type in new[] { QRCodeType.Standard, QRCodeType.MicroQR, QRCodeType.RMQR })
        {
            // A module size of 1 with no quiet zone gives widths that are not a multiple of 8,
            // which the 1-bit PNG renderer packs differently from whole bytes.
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

    public static TheoryData<BarcodeType, int, int, int> BarcodeLayouts()
    {
        var data = new TheoryData<BarcodeType, int, int, int>();
        foreach (var type in Enum.GetValues<BarcodeType>())
        {
            data.Add(type, 1, 1, 0);
            data.Add(type, 2, 80, 10);
            data.Add(type, 3, 5, 1);
        }

        return data;
    }

    [Fact]
    public void QRCode_ToImage_DefaultOptions_UsesModuleSize10_QuietZone4()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        using var image = qr.ToImage();

        Assert.Equal(290, image.Width);
        Assert.Equal(290, image.Height);
        Assert.Equal(1, image.Frames.Count);

        var pixels = GetPixels(image);
        Assert.Equal(Colors.White, pixels[0]);
        Assert.Equal(Colors.Black, pixels[(40 * 290) + 40]); // Top-left corner of the finder pattern
    }

    [Theory]
    [MemberData(nameof(QRCodeLayouts))]
    public void QRCode_ToImage_MatchesToPng(QRCodeType type, int moduleSize, int quietZoneModules)
    {
        var qr = CreateSample(type);
        var expected = qr.ToPng(new QRCodePngOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules });

        using var actual = qr.ToImage(new QRCodeImageOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules });

        AssertSamePixels(expected, actual);
    }

    [Theory]
    [MemberData(nameof(QRCodeLayouts))]
    public void QRCode_ToImage_PixelsMatchTheModuleMatrix(QRCodeType type, int moduleSize, int quietZoneModules)
    {
        var qr = CreateSample(type);
        using var image = qr.ToImage(new QRCodeImageOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules });

        Assert.Equal((qr.Width + (2 * quietZoneModules)) * moduleSize, image.Width);
        Assert.Equal((qr.Height + (2 * quietZoneModules)) * moduleSize, image.Height);

        var pixels = GetPixels(image);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var row = (y / moduleSize) - quietZoneModules;
                var column = (x / moduleSize) - quietZoneModules;
                var expected = row >= 0 && row < qr.Height && column >= 0 && column < qr.Width && qr[row, column] ? Colors.Black : Colors.White;
                if (expected != pixels[(y * image.Width) + x])
                {
                    Assert.Fail($"Pixel ({x}, {y}) is {pixels[(y * image.Width) + x]} but module ({column}, {row}) is {expected}.");
                }
            }
        }
    }

    [Fact]
    public void QRCode_ToImage_UsesCustomColors()
    {
        var qr = QRCode.Create("SCAN ME", ErrorCorrectionLevel.L);
        var dark = new Rgba32(0x00, 0x33, 0x66, 0xC0);
        var light = Colors.Transparent;

        using var image = qr.ToImage(new QRCodeImageOptions { ModuleSize = 3, QuietZoneModules = 1, DarkColor = dark, LightColor = light });

        var pixels = GetPixels(image);
        Assert.Equal(light, pixels[0]);
        Assert.Equal(dark, pixels[(3 * image.Width) + 3]);
        Assert.All(pixels, pixel => Assert.True(pixel == dark || pixel == light, $"Unexpected pixel {pixel}"));
    }

    [Fact]
    public void QRCode_ToImage_MatchesToPng_WithCustomColors()
    {
        var qr = QRCode.Create("SCAN ME", ErrorCorrectionLevel.Q);
        var expected = qr.ToPng(new QRCodePngOptions { ModuleSize = 3, DarkColor = Color.FromRgb(0x00, 0x33, 0x66), LightColor = Color.Transparent });

        using var actual = qr.ToImage(new QRCodeImageOptions { ModuleSize = 3, DarkColor = new Rgba32(0x00, 0x33, 0x66), LightColor = Colors.Transparent });

        AssertSamePixels(expected, actual);
    }

    [Fact]
    public void QRCode_ToImage_IndexedPngRoundTrip()
    {
        var qr = QRCode.CreateRMQR("https://example.com", ErrorCorrectionLevel.M);
        using var image = qr.ToImage(new QRCodeImageOptions { ModuleSize = 2, LightColor = Colors.Transparent });

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });

        AssertSamePixels(stream.ToArray(), image);
    }

    [Fact]
    public void QRCode_ToImage_CanBeDrawnOntoAnotherImage()
    {
        var qr = QRCode.Create("TICKET-0042", ErrorCorrectionLevel.L);
        using var qrImage = qr.ToImage(new QRCodeImageOptions { ModuleSize = 1, QuietZoneModules = 0 });
        using var ticket = new Image<Rgba32>(qrImage.Width + 32, qrImage.Height + 32, Colors.Red);

        ticket.Draw(canvas => canvas.DrawImage(qrImage.Frames[0], new PointD(16, 16)));

        var expected = GetPixels(qrImage);
        var actual = GetPixels(ticket);
        Assert.Equal(Colors.Red, actual[0]);
        for (var y = 0; y < qrImage.Height; y++)
        {
            for (var x = 0; x < qrImage.Width; x++)
            {
                Assert.Equal(expected[(y * qrImage.Width) + x], actual[((y + 16) * ticket.Width) + x + 16]);
            }
        }
    }

    [Fact]
    public void QRCode_ToImage_NullQRCode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodeImageRenderer.ToImage(qrCode: null!));
    }

    [Fact]
    public void QRCode_ToImage_NullOptions_Throws()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentNullException>(() => qr.ToImage(options: null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QRCode_ToImage_InvalidModuleSize_Throws(int moduleSize)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToImage(new QRCodeImageOptions { ModuleSize = moduleSize }));
    }

    [Fact]
    public void QRCode_ToImage_NegativeQuietZone_Throws()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToImage(new QRCodeImageOptions { QuietZoneModules = -1 }));
    }

    [Fact]
    public void QRCode_ToImage_DimensionsTooLarge_Throws()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToImage(new QRCodeImageOptions { ModuleSize = int.MaxValue }));
        Assert.Equal(nameof(QRCodeImageOptions.ModuleSize), exception.ParamName);
    }

    [Fact]
    public void Barcode_ToImage_DefaultOptions()
    {
        var barcode = Barcode.CreateCode128("Hello");
        using var image = barcode.ToImage();

        Assert.Equal((barcode.Width + 20) * 2, image.Width);
        Assert.Equal(barcode.Height * 80, image.Height);

        var pixels = GetPixels(image);
        Assert.Equal(Colors.White, pixels[0]);
        Assert.Equal(Colors.Black, pixels[20]); // Code 128 starts with a bar
    }

    [Theory]
    [MemberData(nameof(BarcodeLayouts))]
    public void Barcode_ToImage_MatchesToPng(BarcodeType type, int moduleWidth, int moduleHeight, int quietZoneModules)
    {
        var barcode = CreateSample(type);
        var expected = barcode.ToPng(new BarcodePngOptions { ModuleWidth = moduleWidth, ModuleHeight = moduleHeight, QuietZoneModules = quietZoneModules });

        using var actual = barcode.ToImage(new BarcodeImageOptions { ModuleWidth = moduleWidth, ModuleHeight = moduleHeight, QuietZoneModules = quietZoneModules });

        AssertSamePixels(expected, actual);
    }

    [Fact]
    public void Barcode_ToImage_UsesCustomColors()
    {
        var barcode = Barcode.CreateEan13("5901234123457", extension: "12");
        var dark = new Rgba32(0x10, 0x20, 0x30);
        var light = new Rgba32(0xF0, 0xE0, 0xD0, 0x80);

        using var image = barcode.ToImage(new BarcodeImageOptions { DarkColor = dark, LightColor = light });

        var pixels = GetPixels(image);
        Assert.Contains(dark, pixels);
        Assert.Contains(light, pixels);
        Assert.All(pixels, pixel => Assert.True(pixel == dark || pixel == light, $"Unexpected pixel {pixel}"));
    }

    [Fact]
    public void Barcode_ToImage_NullBarcode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BarcodeImageRenderer.ToImage(barcode: null!));
    }

    [Fact]
    public void Barcode_ToImage_NullOptions_Throws()
    {
        var barcode = Barcode.CreateCode39("ABC");

        Assert.Throws<ArgumentNullException>(() => barcode.ToImage(options: null!));
    }

    [Fact]
    public void Barcode_ToImage_InvalidModuleWidth_Throws()
    {
        var barcode = Barcode.CreateCode39("ABC");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToImage(new BarcodeImageOptions { ModuleWidth = 0 }));
    }

    [Fact]
    public void Barcode_ToImage_InvalidModuleHeight_Throws()
    {
        var barcode = Barcode.CreateCode39("ABC");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToImage(new BarcodeImageOptions { ModuleHeight = 0 }));
    }

    [Fact]
    public void Barcode_ToImage_NegativeQuietZone_Throws()
    {
        var barcode = Barcode.CreateCode39("ABC");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToImage(new BarcodeImageOptions { QuietZoneModules = -1 }));
    }

    [Fact]
    public void Barcode_ToImage_DimensionsTooLarge_Throws()
    {
        var barcode = Barcode.CreateCode39("ABC");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToImage(new BarcodeImageOptions { ModuleWidth = int.MaxValue }));
        Assert.Equal(nameof(BarcodeImageOptions.ModuleWidth), exception.ParamName);
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

    private static Barcode CreateSample(BarcodeType type)
    {
        return type switch
        {
            BarcodeType.Code39 => Barcode.CreateCode39("ABC-123"),
            BarcodeType.Code93 => Barcode.CreateCode93("ABC-123"),
            BarcodeType.Code128 => Barcode.CreateCode128("Hello 12345678"),
            BarcodeType.Ean8 => Barcode.CreateEan8("9638507"),
            BarcodeType.Ean13 => Barcode.CreateEan13("590123412345", extension: "12345"),
            BarcodeType.UpcA => Barcode.CreateUpcA("03600029145"),
            BarcodeType.Codabar => Barcode.CreateCodabar("40156"),
            BarcodeType.Itf => Barcode.CreateItf("12345678"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };
    }

    private static Rgba32[] GetPixels(Image<Rgba32> image)
    {
        var pixels = new Rgba32[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels;
    }

    private static void AssertSamePixels(byte[] expectedPng, Image<Rgba32> actual)
    {
        using var expected = Image.Load<Rgba32>(expectedPng);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);

        var expectedPixels = GetPixels(expected);
        var actualPixels = GetPixels(actual);
        for (var i = 0; i < expectedPixels.Length; i++)
        {
            if (expectedPixels[i] != actualPixels[i])
            {
                Assert.Fail($"Pixel ({i % actual.Width}, {i / actual.Width}) is {actualPixels[i]}; the PNG renderer gives {expectedPixels[i]}.");
            }
        }
    }
}
