using System.Runtime.CompilerServices;
using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Barcodes.Tests;

public sealed class ImageRendererTests
{
    [Fact]
    public void ReadmeExample_QRCodeToImage()
    {
        #region ImagingBarcodes_QRCodeToImage
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
        #region ImagingBarcodes_BarcodeToImage
        var barcode = Barcode.CreateEan13("5901234123457");
        using var image = barcode.ToImage(new BarcodeImageOptions { ModuleWidth = 3, ModuleHeight = 100 });
        Assert.Equal((345, 100), (image.Width, image.Height));
        #endregion
    }

    [Fact]
    public void ReadmeExample_DrawQRCode()
    {
        #region ImagingBarcodes_DrawQRCode
        using var ticket = new Image<Rgba32>(400, 200, Colors.White); // or Image.Load<Rgba32>("ticket.png")
        var qrCode = QRCode.Create("TICKET-0042");
        var options = new QRCodeDrawingOptions { ModuleSize = 4, QuietZoneModules = 2 };

        // The size of the drawing, quiet zone included, to lay out the page before drawing
        var size = QRCodeImageRenderer.MeasureQRCode(qrCode, options);
        Assert.Equal((100, 100), (size.Width, size.Height)); // (21 modules + 2 x 2) x 4 pixels

        // Drawn as a vector shape: the transform and the clip of the canvas apply, any brush works
        ticket.Draw(canvas => canvas.DrawQRCode(qrCode, new PointD(400 - 16 - size.Width, 16), options));
        Assert.Equal(Colors.Black, ticket.Frames[0][284 + 8, 16 + 8]); // the top-left finder pattern, after the 8-pixel quiet zone

        // Save to a file (ticket.Save("ticket-with-qrcode.png")) or to a stream
        using var png = new MemoryStream();
        ticket.Save(png, new PngEncoder());
        #endregion
    }

    [Fact]
    public void ReadmeExample_DrawBarcode()
    {
        #region ImagingBarcodes_DrawBarcode
        using var label = new Image<Rgba32>(300, 120, Colors.White);
        var barcode = Barcode.CreateEan13("590123412345");
        var options = new BarcodeDrawingOptions
        {
            ModuleWidth = 2,
            ModuleHeight = 60,
            QuietZoneModules = 9,
            DarkBrush = new SolidBrush(new Rgba32(0x00, 0x33, 0x66)),
        };

        label.Draw(canvas =>
        {
            var bounds = canvas.DrawBarcode(barcode, new PointD(20, 20), options);
            Assert.Equal(new RectangleD(20, 20, 226, 60), bounds); // (95 modules + 2 x 9) x 2 pixels wide
        });
        Assert.Equal(new Rgba32(0x00, 0x33, 0x66), label.Frames[0][20 + 18, 50]); // the first guard bar
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

    [Theory]
    [MemberData(nameof(QRCodeLayouts))]
    public void DrawQRCode_MatchesToImage(QRCodeType type, int moduleSize, int quietZoneModules)
    {
        var qr = CreateSample(type);
        using var expected = qr.ToImage(new QRCodeImageOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules });
        var options = new QRCodeDrawingOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules };

        using var actual = new Image<Rgba32>(expected.Width + 20, expected.Height + 20, Colors.Red);
        var bounds = DrawOn(actual, (qr, options), static (canvas, state) => canvas.DrawQRCode(state.qr, new PointD(10, 10), state.options));

        Assert.Equal(new RectangleD(10, 10, expected.Width, expected.Height), bounds);
        Assert.Equal(bounds, Offset(QRCodeImageRenderer.MeasureQRCode(qr, options), 10, 10));
        AssertDrawnAt(expected, actual, 10, 10);
    }

    [Theory]
    [MemberData(nameof(BarcodeLayouts))]
    public void DrawBarcode_MatchesToImage(BarcodeType type, int moduleWidth, int moduleHeight, int quietZoneModules)
    {
        var barcode = CreateSample(type);
        using var expected = barcode.ToImage(new BarcodeImageOptions { ModuleWidth = moduleWidth, ModuleHeight = moduleHeight, QuietZoneModules = quietZoneModules });
        var options = new BarcodeDrawingOptions { ModuleWidth = moduleWidth, ModuleHeight = moduleHeight, QuietZoneModules = quietZoneModules };

        using var actual = new Image<Rgba32>(expected.Width + 20, expected.Height + 20, Colors.Red);
        var bounds = DrawOn(actual, (barcode, options), static (canvas, state) => canvas.DrawBarcode(state.barcode, new PointD(10, 10), state.options));

        Assert.Equal(new RectangleD(10, 10, expected.Width, expected.Height), bounds);
        Assert.Equal(bounds, Offset(BarcodeImageRenderer.MeasureBarcode(barcode, options), 10, 10));
        AssertDrawnAt(expected, actual, 10, 10);
    }

    [Fact]
    public void DrawQRCode_NullLightBrush_KeepsTheBackground()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodeDrawingOptions { ModuleSize = 2, QuietZoneModules = 1, LightBrush = null };

        using var actual = new Image<Rgba32>(50, 50, Colors.Red);
        DrawOn(actual, (qr, options), static (canvas, state) => canvas.DrawQRCode(state.qr, default, state.options));

        var pixels = GetPixels(actual);
        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var row = (y / 2) - 1;
                var column = (x / 2) - 1;
                var isDark = row >= 0 && row < qr.Height && column >= 0 && column < qr.Width && qr[row, column];
                Assert.Equal(isDark ? Colors.Black : Colors.Red, pixels[(y * actual.Width) + x]);
            }
        }
    }

    [Fact]
    public void DrawQRCode_GradientBrush_PaintsOnlyTheDarkModules()
    {
        var qr = QRCode.Create("GRADIENT", ErrorCorrectionLevel.L);
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(100, 0), [new GradientStop(0, Colors.Blue), new GradientStop(1, Colors.Green)]);
        var options = new QRCodeDrawingOptions { ModuleSize = 4, QuietZoneModules = 0, DarkBrush = gradient };

        using var actual = new Image<Rgba32>(qr.Width * 4, qr.Height * 4, Colors.Red);
        DrawOn(actual, (qr, options), static (canvas, state) => canvas.DrawQRCode(state.qr, default, state.options));

        var pixels = GetPixels(actual);
        var darkColors = new HashSet<Rgba32>();
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                var pixel = pixels[(((row * 4) + 2) * actual.Width) + (column * 4) + 2];
                if (qr[row, column])
                {
                    Assert.NotEqual(Colors.White, pixel);
                    darkColors.Add(pixel);
                }
                else
                {
                    Assert.Equal(Colors.White, pixel);
                }
            }
        }

        Assert.HasCountGreaterThan(1, darkColors);
    }

    [Fact]
    public void DrawQRCode_SnapToPixels_RoundsTheModuleSizeAndThePosition()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodeDrawingOptions { ModuleSize = 2.6, QuietZoneModules = 1 };

        using var actual = new Image<Rgba32>(100, 100, Colors.Red);
        var bounds = DrawOn(actual, (qr, options), static (canvas, state) => canvas.DrawQRCode(state.qr, new PointD(10.4, 10.6), state.options));

        Assert.Equal(new RectangleD(10, 11, 23 * 3, 23 * 3), bounds);
        Assert.Equal(new RectangleD(0, 0, 23 * 3, 23 * 3), QRCodeImageRenderer.MeasureQRCode(qr, options));
        using var expected = qr.ToImage(new QRCodeImageOptions { ModuleSize = 3, QuietZoneModules = 1 });
        AssertDrawnAt(expected, actual, 10, 11);
    }

    [Fact]
    public void DrawQRCode_WithoutSnapping_UsesTheExactSizeAndPosition()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodeDrawingOptions { ModuleSize = 2.5, QuietZoneModules = 1, SnapToPixels = false };

        using var actual = new Image<Rgba32>(100, 100, Colors.Red);
        var bounds = DrawOn(actual, (qr, options), static (canvas, state) => canvas.DrawQRCode(state.qr, new PointD(10.25, 10.5), state.options));

        Assert.Equal(new RectangleD(10.25, 10.5, 23 * 2.5, 23 * 2.5), bounds);
        Assert.Equal(new RectangleD(0, 0, 23 * 2.5, 23 * 2.5), QRCodeImageRenderer.MeasureQRCode(qr, options));

        // A partly covered edge pixel is a mix of the background and the quiet zone, a fully covered one is the quiet zone
        var pixels = GetPixels(actual);
        Assert.Equal(Colors.Red, pixels[(5 * actual.Width) + 5]);
        Assert.NotEqual(Colors.Red, pixels[(20 * actual.Width) + 10]);
        Assert.NotEqual(Colors.White, pixels[(20 * actual.Width) + 10]);
        Assert.Equal(Colors.White, pixels[(20 * actual.Width) + 11]);
    }

    [Fact]
    public void DrawQRCode_UnderATransform_IsScaled()
    {
        var qr = QRCode.Create("SCALE", ErrorCorrectionLevel.L);
        using var expected = qr.ToImage(new QRCodeImageOptions { ModuleSize = 2, QuietZoneModules = 2 });
        var options = new QRCodeDrawingOptions { ModuleSize = 1, QuietZoneModules = 2 };

        using var actual = new Image<Rgba32>(expected.Width + 20, expected.Height + 20, Colors.Red);
        DrawOn(actual, (qr, options), static (canvas, state) =>
        {
            canvas.Transform(AffineTransform.CreateScale(2, 2), state, static (canvas, state) => canvas.DrawQRCode(state.qr, new PointD(5, 5), state.options));
            return default;
        });

        AssertDrawnAt(expected, actual, 10, 10);
    }

    [Fact]
    public void DrawBarcode_WithFont_DrawsTheTextUnderTheBars()
    {
        var font = LoadAhem();
        var barcode = Barcode.CreateEan8("5512345");
        var options = new BarcodeDrawingOptions { ModuleWidth = 2, ModuleHeight = 40, QuietZoneModules = 7, Font = font, FontSize = 10, TextSpacing = 3 };
        var text = TextLayout.Create(barcode.Text, font, 10);
        Assert.Equal("55123457", barcode.Text);

        var size = BarcodeImageRenderer.MeasureBarcode(barcode, options);
        Assert.Equal((67 + 14) * 2, size.Width); // the text (8 boxes of 10 pixels) is narrower than the bars
        Assert.Equal(40 + 3 + text.Height, size.Height);

        using var actual = new Image<Rgba32>(200, 100, Colors.Red);
        var bounds = DrawOn(actual, (barcode, options), static (canvas, state) => canvas.DrawBarcode(state.barcode, new PointD(4, 4), state.options));
        Assert.Equal(Offset(size, 4, 4), bounds);

        var pixels = GetPixels(actual);
        var textTop = 4 + 43;
        var textLeft = 4 + (int)((size.Width - text.Width) / 2);
        Assert.Equal(Colors.White, pixels[((textTop - 1) * actual.Width) + textLeft + 2]); // the spacing
        Assert.Equal(Colors.Black, pixels[((textTop + (int)text.Baseline - 2) * actual.Width) + textLeft + 2]); // inside the first box
        Assert.Equal(Colors.Red, pixels[((4 + (int)size.Height + 1) * actual.Width) + 20]); // under the drawing
    }

    [Fact]
    public void DrawBarcode_TextWiderThanTheBars_WidensTheDrawing()
    {
        var font = LoadAhem();
        var barcode = Barcode.CreateCode128("WIDE");
        var options = new BarcodeDrawingOptions { ModuleWidth = 1, ModuleHeight = 10, QuietZoneModules = 0, Font = font, FontSize = 40, TextBrush = new SolidBrush(Colors.Blue) };

        var size = BarcodeImageRenderer.MeasureBarcode(barcode, options);
        Assert.Equal(160, size.Width);

        using var actual = new Image<Rgba32>(200, 100, Colors.Red);
        DrawOn(actual, (barcode, options), static (canvas, state) => canvas.DrawBarcode(state.barcode, default, state.options));
        var pixels = GetPixels(actual);
        var barsLeft = (160 - barcode.Width) / 2;
        Assert.Equal(Colors.White, pixels[5 + (barsLeft - 1)]); // left of the centered bars
        Assert.Equal(Colors.Black, pixels[(5 * actual.Width) + barsLeft]); // Code 128 starts with a bar
        Assert.Equal(Colors.Blue, pixels[(30 * actual.Width) + 5]); // the text brush
    }

    [Fact]
    public void Measure_InvalidOptions_Throw()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var barcode = Barcode.CreateCode39("ABC");
        var font = LoadAhem();

        Assert.Throws<ArgumentNullException>(() => QRCodeImageRenderer.MeasureQRCode(qrCode: null!));
        Assert.Throws<ArgumentNullException>(() => BarcodeImageRenderer.MeasureBarcode(barcode: null!));
        Assert.Throws<ArgumentNullException>(() => QRCodeImageRenderer.MeasureQRCode(qr, new QRCodeDrawingOptions { DarkBrush = null! }));
        Assert.Throws<ArgumentNullException>(() => BarcodeImageRenderer.MeasureBarcode(barcode, new BarcodeDrawingOptions { DarkBrush = null! }));

        foreach (var size in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.Equal(nameof(QRCodeDrawingOptions.ModuleSize), Assert.Throws<ArgumentOutOfRangeException>(() => QRCodeImageRenderer.MeasureQRCode(qr, new QRCodeDrawingOptions { ModuleSize = size })).ParamName);
            Assert.Equal(nameof(BarcodeDrawingOptions.ModuleWidth), Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeImageRenderer.MeasureBarcode(barcode, new BarcodeDrawingOptions { ModuleWidth = size })).ParamName);
            Assert.Equal(nameof(BarcodeDrawingOptions.ModuleHeight), Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeImageRenderer.MeasureBarcode(barcode, new BarcodeDrawingOptions { ModuleHeight = size })).ParamName);
            Assert.Equal(nameof(BarcodeDrawingOptions.FontSize), Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeImageRenderer.MeasureBarcode(barcode, new BarcodeDrawingOptions { Font = font, FontSize = size })).ParamName);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => QRCodeImageRenderer.MeasureQRCode(qr, new QRCodeDrawingOptions { QuietZoneModules = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeImageRenderer.MeasureBarcode(barcode, new BarcodeDrawingOptions { QuietZoneModules = -1 }));
        Assert.Equal(nameof(BarcodeDrawingOptions.TextSpacing), Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeImageRenderer.MeasureBarcode(barcode, new BarcodeDrawingOptions { Font = font, TextSpacing = -1 })).ParamName);

        // Without a font, the text options are not used
        _ = BarcodeImageRenderer.MeasureBarcode(barcode, new BarcodeDrawingOptions { FontSize = -1, TextSpacing = -1 });

        // A module size below 0.5 rounds up to 1 when snapping
        Assert.Equal(qr.Width + 8, QRCodeImageRenderer.MeasureQRCode(qr, new QRCodeDrawingOptions { ModuleSize = 0.1 }).Width);
    }

    [Fact]
    public void Draw_InvalidArguments_ThrowBeforeDrawing()
    {
        using var image = new Image<Rgba32>(10, 10, Colors.Red);
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var exceptions = new List<Exception>();

        image.Draw((qr, exceptions), static (canvas, state) =>
        {
            try
            {
                canvas.DrawQRCode(qrCode: null!, default);
            }
            catch (ArgumentNullException ex)
            {
                state.exceptions.Add(ex);
            }

            try
            {
                canvas.DrawQRCode(state.qr, new PointD(double.NaN, 0), new QRCodeDrawingOptions { SnapToPixels = false });
            }
            catch (ArgumentException ex)
            {
                state.exceptions.Add(ex);
            }

            try
            {
                canvas.DrawBarcode(barcode: null!, default);
            }
            catch (ArgumentNullException ex)
            {
                state.exceptions.Add(ex);
            }
        });

        Assert.HasCount(3, exceptions);
        Assert.All(GetPixels(image), pixel => Assert.Equal(Colors.Red, pixel));
    }

    private static Font LoadAhem()
    {
        using var stream = typeof(ImageRendererTests).Assembly.GetManifestResourceStream("Ahem.ttf")!;
        return Font.Load(stream);
    }

    private static RectangleD Offset(RectangleD rectangle, double dx, double dy) => new(rectangle.X + dx, rectangle.Y + dy, rectangle.Width, rectangle.Height);

    private static RectangleD DrawOn<TState>(Image<Rgba32> image, TState state, Func<DrawingCanvas, TState, RectangleD> draw)
    {
        var result = new StrongBox<RectangleD>();
        image.Draw((state, draw, result), static (canvas, context) => context.result.Value = context.draw(canvas, context.state));
        return result.Value;
    }

    private static void AssertDrawnAt(Image<Rgba32> expected, Image<Rgba32> actual, int left, int top)
    {
        var expectedPixels = GetPixels(expected);
        var actualPixels = GetPixels(actual);
        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var inside = x >= left && x < left + expected.Width && y >= top && y < top + expected.Height;
                var expectedPixel = inside ? expectedPixels[((y - top) * expected.Width) + x - left] : Colors.Red;
                var actualPixel = actualPixels[(y * actual.Width) + x];
                if (expectedPixel != actualPixel)
                {
                    Assert.Fail($"Pixel ({x}, {y}) is {actualPixel}; expected {expectedPixel}.");
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
