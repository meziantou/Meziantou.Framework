using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Barcodes.InteropTests;

/// <summary>
/// Decodes the images of the library with two independent readers: ZXingReader reads every type, zbarimg every type
/// except Micro QR and rMQR. The images come from <c>ToPng</c>, from <c>ToImage</c>, and from the canvas drawing, with
/// fractional sizes, transforms and gradient brushes.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
public sealed class DecodeInteropTests
{
    private const int RandomPayloadsPerType = 40;

    public static TheoryData<string> DrawingVariants() => ["ToPng", "ToImage", "Canvas", "CanvasFractional", "CanvasGradient", "CanvasRotated"];

    [Theory]
    [MemberData(nameof(DrawingVariants))]
    public async Task QRCodes_AreDecoded(string variant)
    {
        var tools = await BarcodeTools.GetAsync();
        await using var directory = TemporaryDirectory.Create();
        var cases = new List<(string Path, QRCode QRCode, string Data)>();
        foreach (var (qr, data) in GetSampleQRCodes())
        {
            var path = directory.FullPath / $"qr-{cases.Count.ToString(CultureInfo.InvariantCulture)}.png";
            await File.WriteAllBytesAsync(path, RenderQRCode(qr, variant), XunitCancellationToken);
            cases.Add((path, qr, data));
        }

        var results = await tools.ReadWithZXingAsync([.. cases.Select(item => item.Path)]);
        foreach (var (path, qr, data) in cases)
        {
            var result = Assert.Single(results, result => result.FilePath == path, message: $"{qr.Type} {qr.ErrorCorrectionLevel} {Escape(data)}");
            Assert.Equal(data, result.Text);
            Assert.Equal(qr.ErrorCorrectionLevel.ToString(), result.ErrorCorrectionLevel);

            if (qr.Type is QRCodeType.Standard)
            {
                Assert.Equal(["QR-Code:" + data], await tools.ReadWithZBarAsync(path));
            }
        }
    }

    [Theory]
    [MemberData(nameof(DrawingVariants))]
    public async Task Barcodes_AreDecoded(string variant)
    {
        var tools = await BarcodeTools.GetAsync();
        await using var directory = TemporaryDirectory.Create();
        var cases = new List<(string Path, Barcode Barcode)>();
        foreach (var barcode in GetSampleBarcodes())
        {
            var path = directory.FullPath / $"barcode-{cases.Count.ToString(CultureInfo.InvariantCulture)}.png";
            await File.WriteAllBytesAsync(path, RenderBarcode(barcode, variant), XunitCancellationToken);
            cases.Add((path, barcode));
        }

        var results = await tools.ReadWithZXingAsync([.. cases.Select(item => item.Path)]);
        foreach (var (path, barcode) in cases)
        {
            var (main, addOn) = SplitAddOn(barcode);
            var found = results.Where(result => result.FilePath == path).Select(result => result.Text).ToList();
            Assert.Contains(GetExpectedZXingText(barcode.Type, main, addOn), found);

            var zbar = await tools.ReadWithZBarAsync(path);
            Assert.Contains(GetZBarText(barcode.Type, main), zbar);
        }
    }

    [Theory]
    [InlineData(QRCodeType.Standard)]
    [InlineData(QRCodeType.MicroQR)]
    [InlineData(QRCodeType.RMQR)]
    public async Task RandomQRCodePayloads_AreDecoded(QRCodeType type)
    {
        var tools = await BarcodeTools.GetAsync();
        var random = new Random(20261025 + (int)type);
        await using var directory = TemporaryDirectory.Create();
        var cases = new List<(string Path, string Data, ErrorCorrectionLevel Level)>();
        while (cases.Count < RandomPayloadsPerType)
        {
            var data = CreateRandomText(random, type switch { QRCodeType.MicroQR => 8, QRCodeType.RMQR => 40, _ => 300 });
            var level = type switch
            {
                QRCodeType.MicroQR => random.Next(2) == 0 ? ErrorCorrectionLevel.L : ErrorCorrectionLevel.M,
                QRCodeType.RMQR => random.Next(2) == 0 ? ErrorCorrectionLevel.M : ErrorCorrectionLevel.H,
                _ => (ErrorCorrectionLevel)random.Next(4),
            };

            QRCode qr;
            try
            {
                qr = type switch
                {
                    QRCodeType.MicroQR => QRCode.CreateMicroQR(data, level),
                    QRCodeType.RMQR => QRCode.CreateRMQR(data, level),
                    _ => QRCode.Create(data, level),
                };
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // The data does not fit (or Micro QR cannot encode a character): another payload is drawn
                continue;
            }

            var path = directory.FullPath / $"random-{cases.Count.ToString(CultureInfo.InvariantCulture)}.png";
            await File.WriteAllBytesAsync(path, qr.ToPng(new QRCodePngOptions { ModuleSize = 3 }), XunitCancellationToken);
            cases.Add((path, data, qr.ErrorCorrectionLevel));
        }

        var results = await tools.ReadWithZXingAsync([.. cases.Select(item => item.Path)]);
        foreach (var (path, data, level) in cases)
        {
            var result = results.SingleOrDefault(result => result.FilePath == path);
            Assert.NotNull(result);
            Assert.Equal(data, result.Text, message: $"{type} {level} {Escape(data)}");
        }
    }

    [Fact]
    public async Task RandomBarcodePayloads_AreDecoded()
    {
        var tools = await BarcodeTools.GetAsync();
        var random = new Random(20261025);
        await using var directory = TemporaryDirectory.Create();
        var cases = new List<(string Path, Barcode Barcode)>();
        foreach (var type in Enum.GetValues<BarcodeType>())
        {
            for (var i = 0; i < RandomPayloadsPerType / 4; i++)
            {
                var barcode = CreateRandomBarcode(random, type);
                var path = directory.FullPath / $"random-{cases.Count.ToString(CultureInfo.InvariantCulture)}.png";
                await File.WriteAllBytesAsync(path, barcode.ToPng(new BarcodePngOptions { ModuleWidth = 2, ModuleHeight = 40 }), XunitCancellationToken);
                cases.Add((path, barcode));
            }
        }

        var results = await tools.ReadWithZXingAsync([.. cases.Select(item => item.Path)]);
        foreach (var (path, barcode) in cases)
        {
            var (main, addOn) = SplitAddOn(barcode);
            var expected = GetExpectedZXingText(barcode.Type, main, addOn);
            var found = results.Where(result => result.FilePath == path).Select(result => barcode.Type is BarcodeType.Gs1128 ? result.HumanReadableText : result.Text).ToList();
            Assert.Contains(expected, found);
        }
    }

    // A reader reports a GS1-128 symbol with the symbology identifier ]C1, and its FNC1 separators as GS (U+001D)
    [Theory]
    [MemberData(nameof(DrawingVariants))]
    public async Task Gs1128_IsReadAsGs1(string variant)
    {
        var tools = await BarcodeTools.GetAsync();
        await using var directory = TemporaryDirectory.Create();
        (Barcode Barcode, string Data)[] samples =
        [
            (Barcode.CreateGs1128(new Gs1Element("01", "09521234543213"), new Gs1Element("17", "250101"), new Gs1Element("10", "ABC123")), "01095212345432131725010110ABC123"),
            (Barcode.CreateGs1128(new Gs1Element("01", "09521234543213"), new Gs1Element("10", "ABC123"), new Gs1Element("21", "XY")), "010952123454321310ABC123\u001D21XY"),
            (Barcode.CreateGs1128(new Gs1Element("00", "123456789012345675")), "00123456789012345675"),
            (Barcode.CreateGs1128(new Gs1Element("10", "1234"), new Gs1Element("21", "5678"), new Gs1Element("3103", "000123")), "101234\u001D215678\u001D3103000123"),
        ];

        foreach (var (barcode, data) in samples)
        {
            var path = directory.FullPath / "gs1.png";
            await File.WriteAllBytesAsync(path, RenderBarcode(barcode, variant), XunitCancellationToken);

            var result = Assert.Single(await tools.ReadWithZXingAsync([path]));
            Assert.Equal("]C1", result.SymbologyIdentifier);
            Assert.Equal("GS1", result.ContentType);
            Assert.Equal(data, result.Text);
            Assert.Equal(barcode.Text, result.HumanReadableText);

            Assert.Equal(["CODE-128:" + data], await tools.ReadWithZBarAsync(path));
        }
    }

    [Fact]
    public async Task Code128_IsNotReadAsGs1()
    {
        var tools = await BarcodeTools.GetAsync();
        await using var directory = TemporaryDirectory.Create();
        var path = directory.FullPath / "code128.png";
        await File.WriteAllBytesAsync(path, Barcode.CreateCode128("0109521234543213").ToPng(new BarcodePngOptions { ModuleWidth = 2, ModuleHeight = 60 }), XunitCancellationToken);

        var result = Assert.Single(await tools.ReadWithZXingAsync([path]));
        Assert.Equal("]C0", result.SymbologyIdentifier);
        Assert.Equal("0109521234543213", result.Text);
    }

    private static IEnumerable<(QRCode QRCode, string Data)> GetSampleQRCodes()
    {
        foreach (var level in Enum.GetValues<ErrorCorrectionLevel>())
        {
            yield return (QRCode.Create("https://www.meziantou.net/", level), "https://www.meziantou.net/");
        }

        yield return (QRCode.Create("Mixed 0123456789 ÉÀ 漢字", ErrorCorrectionLevel.Q), "Mixed 0123456789 ÉÀ 漢字");
        yield return (QRCode.Create(QRCodePayload.Wifi("MyNetwork", "p@ss;word"), ErrorCorrectionLevel.M), QRCodePayload.Wifi("MyNetwork", "p@ss;word"));
        yield return (QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L), "12345");
        yield return (QRCode.CreateMicroQR("HELLO", ErrorCorrectionLevel.M), "HELLO");
        yield return (QRCode.CreateMicroQR("micro", ErrorCorrectionLevel.L), "micro");
        yield return (QRCode.CreateRMQR("https://example.com", ErrorCorrectionLevel.M), "https://example.com");
        yield return (QRCode.CreateRMQR("RMQR 123", ErrorCorrectionLevel.H), "RMQR 123");
    }

    private static IEnumerable<Barcode> GetSampleBarcodes()
    {
        yield return Barcode.CreateCode39("ABC-123", includeChecksum: true);
        yield return Barcode.CreateCode93("Code93 test");
        yield return Barcode.CreateCode128("Hello 12345678");
        yield return Barcode.CreateEan8("9638507");
        yield return Barcode.CreateEan13("590123412345");
        yield return Barcode.CreateEan13("590123412345", extension: "12345");
        yield return Barcode.CreateUpcA("03600029145", extension: "12");
        yield return Barcode.CreateCodabar("40156");
        yield return Barcode.CreateItf("12345678");
    }

    private static byte[] RenderQRCode(QRCode qr, string variant)
    {
        if (variant is "ToPng")
            return qr.ToPng(new QRCodePngOptions { ModuleSize = 4 });

        if (variant is "ToImage")
        {
            using var image = qr.ToImage(new QRCodeDrawingOptions { ModuleSize = 4 });
            return Encode(image);
        }

        var options = variant switch
        {
            // The rMQR detector of ZXingReader misses some fractional sizes, of zint's symbols as well as ours (3.4, 4.6, 5.3)
            "CanvasFractional" => new QRCodeDrawingOptions { ModuleSize = 3.5, SnapToPixels = false },
            "CanvasGradient" => new QRCodeDrawingOptions { ModuleSize = 4, DarkBrush = new LinearGradientBrush(new PointD(0, 0), new PointD(400, 400), [new GradientStop(0, Colors.Black), new GradientStop(1, new Rgba32(0x00, 0x33, 0x66))]) },
            _ => new QRCodeDrawingOptions { ModuleSize = 4 },
        };

        var size = QRCodeImageRenderer.MeasureQRCode(qr, options);
        if (variant is "CanvasRotated")
        {
            // ZXingReader reads rMQR codes rotated by a few degrees unreliably, zint's as well as ours: they are turned a quarter
            var side = (int)Math.Ceiling(Math.Max(size.Width, size.Height)) + 80;
            using var rotated = new Image<Rgba32>(side, side, Colors.White);
            rotated.Draw((qr, options, size, side), static (canvas, state) =>
            {
                var center = new PointD(state.side / 2.0, state.side / 2.0);
                var angle = state.qr.Type is QRCodeType.RMQR ? 90 : 10;
                canvas.Transform(AffineTransform.CreateRotation(angle, center), state, static (canvas, state) => canvas.DrawQRCode(state.qr, new PointD((state.side - state.size.Width) / 2, (state.side - state.size.Height) / 2), state.options));
            });
            return Encode(rotated);
        }

        using var canvas = new Image<Rgba32>((int)Math.Ceiling(size.Width) + 60, (int)Math.Ceiling(size.Height) + 60, Colors.White);
        canvas.Draw((qr, options), static (canvas, state) => canvas.DrawQRCode(state.qr, new PointD(30.3, 30.6), state.options));
        return Encode(canvas);
    }

    private static byte[] RenderBarcode(Barcode barcode, string variant)
    {
        if (variant is "ToPng")
            return barcode.ToPng(new BarcodePngOptions { ModuleWidth = 2, ModuleHeight = 60 });

        if (variant is "ToImage")
        {
            using var image = barcode.ToImage(new BarcodeDrawingOptions { ModuleWidth = 2, ModuleHeight = 60 });
            return Encode(image);
        }

        var options = variant switch
        {
            "CanvasFractional" => new BarcodeDrawingOptions { ModuleWidth = 2.4, ModuleHeight = 60.5, SnapToPixels = false },
            "CanvasGradient" => new BarcodeDrawingOptions { ModuleWidth = 2, ModuleHeight = 60, DarkBrush = new LinearGradientBrush(new PointD(0, 0), new PointD(600, 0), [new GradientStop(0, Colors.Black), new GradientStop(1, new Rgba32(0x00, 0x33, 0x66))]) },
            _ => new BarcodeDrawingOptions { ModuleWidth = 2, ModuleHeight = 60 },
        };

        var size = BarcodeImageRenderer.MeasureBarcode(barcode, options);
        using var canvas = new Image<Rgba32>((int)Math.Ceiling(size.Width) + 60, (int)Math.Ceiling(size.Height) + 60, Colors.White);
        canvas.Draw((barcode, options, size, rotated: variant is "CanvasRotated"), static (canvas, state) =>
        {
            if (state.rotated)
            {
                var center = new PointD(30 + (state.size.Width / 2), 30 + (state.size.Height / 2));
                canvas.Transform(AffineTransform.CreateRotation(3, center), state, static (canvas, state) => canvas.DrawBarcode(state.barcode, new PointD(30, 30), state.options));
            }
            else
            {
                canvas.DrawBarcode(state.barcode, new PointD(30.3, 30.6), state.options);
            }
        });
        return Encode(canvas);
    }

    private static byte[] Encode(Image<Rgba32> image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    // The text of an EAN or UPC symbol with an add-on is the main digits, a space, and the add-on digits
    private static (string Main, string? AddOn) SplitAddOn(Barcode barcode)
    {
        var text = barcode.Text;
        var index = barcode.Type is BarcodeType.Ean8 or BarcodeType.Ean13 or BarcodeType.UpcA ? text.IndexOf(' ', StringComparison.Ordinal) : -1;
        return index < 0 ? (text, null) : (text[..index], text[(index + 1)..]);
    }

    // ZXingReader reads a UPC-A symbol as the EAN-13 number it is part of, with a leading 0, and gives a symbol with an
    // add-on as its main digits followed by the add-on digits (sometimes with a second result of the main digits alone)
    private static string GetExpectedZXingText(BarcodeType type, string main, string? addOn) => (type is BarcodeType.UpcA ? "0" + main : main) + addOn;

    private static string GetZBarText(BarcodeType type, string text) => type switch
    {
        BarcodeType.Code39 => "CODE-39:" + text,
        BarcodeType.Code93 => "CODE-93:" + text,
        BarcodeType.Code128 => "CODE-128:" + text,
        BarcodeType.Ean8 => "EAN-8:" + text,
        BarcodeType.Ean13 => "EAN-13:" + text,
        BarcodeType.UpcA => "EAN-13:0" + text,
        BarcodeType.Codabar => "Codabar:" + text,
        BarcodeType.Itf => "I2/5:" + text,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
    };

    private static string CreateRandomText(Random random, int maxLength)
    {
        // Runs of one class of characters, so that the encoder switches between the modes
        string[] classes = ["0123456789", "ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:", "abcdefghijklmnopqrstuvwxyz!\"#&'(),;<=>?@[\\]^_`{|}~", "éàçüßøæœ€£", "漢字日本語東京", "😀🎉"];
        var builder = new StringBuilder();
        var length = random.Next(1, maxLength + 1);
        while (builder.Length < length)
        {
            var characters = classes[random.Next(classes.Length)];
            var runLength = random.Next(1, 12);
            for (var i = 0; i < runLength && builder.Length < length; i++)
            {
                if (char.IsHighSurrogate(characters[0]))
                {
                    var index = random.Next(characters.Length / 2) * 2;
                    builder.Append(characters, index, 2);
                }
                else
                {
                    builder.Append(characters[random.Next(characters.Length)]);
                }
            }
        }

        return builder.ToString();
    }

    private static Barcode CreateRandomBarcode(Random random, BarcodeType type)
    {
        string Digits(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + random.Next(10))));
        string From(string characters, int min, int max) => string.Concat(Enumerable.Range(0, random.Next(min, max + 1)).Select(_ => characters[random.Next(characters.Length)]));
        string? AddOn() => random.Next(3) switch { 0 => null, 1 => Digits(2), _ => Digits(5) };

        return type switch
        {
            // Without $ / + %: a reader may take "/F" for the Full ASCII "&" of Code 39 Extended
            BarcodeType.Code39 => Barcode.CreateCode39(From("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. ", 1, 20), includeChecksum: random.Next(2) == 0),
            BarcodeType.Code93 => Barcode.CreateCode93(From("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-. $/+%", 1, 20)),
            BarcodeType.Code128 => Barcode.CreateCode128(From("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz !#$%&()*+,-./:;<=>?@[]^_{|}~\t\n\r\u001b\u001d\u007f", 1, 30)),
            BarcodeType.Ean8 => Barcode.CreateEan8(Digits(7), AddOn()),
            BarcodeType.Ean13 => Barcode.CreateEan13(Digits(12), AddOn()),
            BarcodeType.UpcA => Barcode.CreateUpcA(Digits(11), AddOn()),
            BarcodeType.Codabar => Barcode.CreateCodabar(From("0123456789-$:/.+", 1, 20), "ABCD"[random.Next(4)], "ABCD"[random.Next(4)]),
            // ZXingReader reads ITF symbols of 6 digits or more
            BarcodeType.Itf => Barcode.CreateItf(Digits(2 * random.Next(3, 10))),
            BarcodeType.Gs1128 => Barcode.CreateGs1128([.. ZintMatrixTests.ParseGs1(ZintMatrixTests.CreateRandomGs1Data(random))]),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };
    }

    private static string Escape(string value) => string.Concat(value.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString()));
}
