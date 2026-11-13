using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes;

namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>
/// The QR code decoder reads arbitrary module matrices: it must fail with <see cref="FormatException"/> only, and a result
/// must describe a symbol of the size of the matrix, whose segments give its text, and which decodes the same way twice.
/// The seeds are the symbols of the unit tests, which the mutations damage module by module.
/// </summary>
internal static class QRDecodeFuzzTargets
{
    private static readonly string[] SeedTexts =
    [
        "HELLO WORLD", "12345", "0123456789", "https://www.meziantou.net", "Hello, World!", "café", "日本語", "ABC12345678901234567890abc日本語",
    ];

    private static readonly string[] ModuleTokens = ["\0\0\0\0", "\xFF\xFF\xFF\xFF", "\x55\x55", "\xAA\xAA", "\x7F", "\x80"];

    private static readonly string[] Barcode2DSeedTexts =
    [
        "Hello", "HELLO WORLD 12345", "hello world", "*ABC>123*\r", "@ABC^_@DEF", "0123456789012345678901234567890123", "caf\u00E9", "Ελληνικά 漢字",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("barcodes-decode-qrcode", DecodeTarget) { Seeds = GetSeeds, Dictionary = ModuleTokens },
        new("barcodes-decode-2d", Decode2DTarget) { Seeds = Get2DSeeds, Dictionary = ModuleTokens },
        new("barcodes-read-qrcode-image", ReadQRCodeImageTarget) { Seeds = GetImageSeeds, Dictionary = ModuleTokens },
        new("barcodes-read-qrcode-roundtrip", ReadQRCodeRoundTripTarget) { Seeds = () => SeedTexts.Select((text, index) => FuzzSeed.FromText($"roundtrip-{index}", [(byte)index, (byte)(index * 37)], text)), Dictionary = ["HELLO", "0123456789", "https://", "日本"] },
        new("barcodes-read-2d-image", Read2DImageTarget) { Seeds = Get2DImageSeeds, Dictionary = ModuleTokens },
        new("barcodes-read-2d-roundtrip", Read2DRoundTripTarget) { Seeds = () => Barcode2DSeedTexts.Select((text, index) => FuzzSeed.FromText($"roundtrip-2d-{index}", [(byte)index, (byte)(index * 37)], text)), Dictionary = ["HELLO", "0123456789", "abc", "*>\r", "@^_"] },
        new("barcodes-decode-linear", DecodeLinearTarget) { Seeds = GetLinearSeeds, Dictionary = ["\x01\x01\x01", "\x02\x02", "\x03", "\x04", "\x0A", "\x00"] },
    ];

    internal static bool[,] GetModules(QRCode qr)
    {
        var modules = new bool[qr.Height, qr.Width];
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                modules[row, column] = qr[row, column];
            }
        }

        return modules;
    }

    // The first byte selects the type and the second the size; the next bytes are the modules, row by row, 8 per byte
    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        for (var index = 0; index < SeedTexts.Length; index++)
        {
            foreach (var level in new[] { ErrorCorrectionLevel.L, ErrorCorrectionLevel.M, ErrorCorrectionLevel.Q, ErrorCorrectionLevel.H })
            {
                yield return CreateSeed($"decode-standard-{index}-{level}", 0, QRCode.Create(SeedTexts[index], level));
            }

            foreach (var level in new[] { ErrorCorrectionLevel.M, ErrorCorrectionLevel.H })
            {
                yield return CreateSeed($"decode-rmqr-{index}-{level}", 2, QRCode.CreateRMQR(SeedTexts[index], level));
            }
        }

        yield return CreateSeed("decode-micro-1", 1, QRCode.CreateMicroQR("12345"));
        yield return CreateSeed("decode-micro-2", 1, QRCode.CreateMicroQR("HELLO"));
        yield return CreateSeed("decode-micro-3", 1, QRCode.CreateMicroQR("hello", ErrorCorrectionLevel.M));
        yield return CreateSeed("decode-micro-4", 1, QRCode.CreateMicroQR("日本", ErrorCorrectionLevel.Q));
        yield return CreateSeed("decode-structured-append", 0, QRCode.CreateStructuredAppend(new string('7', 100), ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 2 })[1]);
        yield return CreateSeed("decode-eci", 0, QRCode.Create("Café", ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = QRCodeOptions.Utf8EciAssignmentNumber }));
    }

    private static FuzzSeed CreateSeed(string name, byte type, QRCode qr)
    {
        var size = type switch
        {
            0 => (qr.Size - 21) / 4,
            1 => (qr.Size - 11) / 2,
            _ => Array.IndexOf(RMQRSizes, (qr.Height, qr.Width)),
        };

        var bytes = new byte[2 + ((qr.Width * qr.Height) + 7) / 8];
        bytes[0] = type;
        bytes[1] = (byte)size;
        for (var i = 0; i < qr.Width * qr.Height; i++)
        {
            if (qr[i / qr.Width, i % qr.Width])
            {
                bytes[2 + (i / 8)] |= (byte)(0x80 >> (i % 8));
            }
        }

        return new FuzzSeed(name, bytes);
    }

    private static readonly (int Height, int Width)[] RMQRSizes =
    [
        (7, 43), (7, 59), (7, 77), (7, 99), (7, 139),
        (9, 43), (9, 59), (9, 77), (9, 99), (9, 139),
        (11, 27), (11, 43), (11, 59), (11, 77), (11, 99), (11, 139),
        (13, 27), (13, 43), (13, 59), (13, 77), (13, 99), (13, 139),
        (15, 43), (15, 59), (15, 77), (15, 99), (15, 139),
        (17, 43), (17, 59), (17, 77), (17, 99), (17, 139),
    ];

    private static void DecodeTarget(ReadOnlySpan<byte> data)
    {
        var type = FuzzInput.ReadByte(ref data) % 4;
        var size = FuzzInput.ReadByte(ref data);
        var (height, width) = type switch
        {
            0 => (21 + (4 * (size % 40)), 21 + (4 * (size % 40))),
            1 => (11 + (2 * (size % 4)), 11 + (2 * (size % 4))),
            2 => RMQRSizes[size % RMQRSizes.Length],
            _ => (1 + (size % 24), 1 + (size / 24 % 24)), // Any size
        };

        var modules = new bool[height, width];
        for (var i = 0; i < height * width && i / 8 < data.Length; i++)
        {
            modules[i / width, i % width] = (data[i / 8] & (0x80 >> (i % 8))) != 0;
        }

        QRCodeDecodeResult result;
        try
        {
            result = QRCodeDecoder.Decode(modules);
        }
        catch (FormatException)
        {
            FuzzAssert.False(QRCodeDecoder.TryDecode(modules, out _), "TryDecode succeeds where Decode fails");
            return;
        }

        var (expectedType, versionRange) = (height, width) switch
        {
            _ when height != width => (QRCodeType.RMQR, (1, 32)),
            _ when height < 21 => (QRCodeType.MicroQR, (1, 4)),
            _ => (QRCodeType.Standard, (1, 40)),
        };

        FuzzAssert.Equal(expectedType, result.Type, "The type of the symbol");
        FuzzAssert.True(result.Version >= versionRange.Item1 && result.Version <= versionRange.Item2, $"Version {result.Version}");
        FuzzAssert.True(result.CorrectedErrorCount >= 0, "The number of corrected errors");
        FuzzAssert.True(result.StructuredAppend is null || (result.StructuredAppend.Index < result.StructuredAppend.Count && result.StructuredAppend.Count <= 16), "The structured append header");
        FuzzAssert.True(result.Type is not QRCodeType.RMQR || result.MaskPattern is null, "An rMQR code has no mask pattern number");

        // The text is the one of the segments, and the matrix decodes the same way twice
        FuzzAssert.True(result.Segments.All(segment => segment.Mode is QRCodeSegmentMode.Byte or QRCodeSegmentMode.Eci || segment.Text is not null), "A segment of characters has no text");
        FuzzAssert.True(QRCodeDecoder.TryDecode(modules, out var other) && other.Text == result.Text && other.CorrectedErrorCount == result.CorrectedErrorCount, "Decoding twice gives different results");

        // Without structured append, the decoded segments can be encoded again, in a symbol that decodes to the same text
        if (result.StructuredAppend is null && result.Type is QRCodeType.Standard && result.Segments.Any(segment => segment.Mode is not QRCodeSegmentMode.Eci))
        {
            QRCode reencoded;
            try
            {
                reencoded = QRCode.Create(result.Segments, result.ErrorCorrectionLevel);
            }
            catch (ArgumentException)
            {
                // A Kanji segment has characters that the encoder does not accept, or a segment is empty
                return;
            }
            catch (InvalidOperationException)
            {
                // The empty segments of the symbol cost bits
                return;
            }

            FuzzAssert.Equal(result.Text, QRCodeDecoder.Decode(reencoded).Text, "The text of the encoded segments");
        }
    }

    // The first byte selects the type, the next two the height and the width; the next bytes are the modules, row by row
    private static IEnumerable<FuzzSeed> Get2DSeeds()
    {
        for (var index = 0; index < Barcode2DSeedTexts.Length; index++)
        {
            var text = Barcode2DSeedTexts[index];
            yield return Create2DSeed($"decode-datamatrix-{index}", Barcode2D.CreateDataMatrix(text));
            yield return Create2DSeed($"decode-pdf417-{index}", Barcode2D.CreatePdf417(text, new Pdf417Options { RowHeight = 1 }));
            yield return Create2DSeed($"decode-aztec-{index}", Barcode2D.CreateAztec(text));
        }

        yield return Create2DSeed("decode-datamatrix-rectangle", Barcode2D.CreateDataMatrix("ABC-12345", new DataMatrixOptions { Shape = DataMatrixShape.Rectangle }));
        yield return Create2DSeed("decode-datamatrix-gs1", Barcode2D.CreateGs1DataMatrix(new Gs1Element("01", "09521234543213"), new Gs1Element("10", "ABC123"), new Gs1Element("21", "XYZ")));
        yield return Create2DSeed("decode-datamatrix-binary", Barcode2D.CreateDataMatrix([0x00, 0xFF, 0x10, 0x80, 0x7F]));
        yield return Create2DSeed("decode-aztec-full", Barcode2D.CreateAztec("Full-range", new AztecOptions { Compact = false }));
        yield return Create2DSeed("decode-aztec-binary", Barcode2D.CreateAztec([0x00, 0xFF, 0x10, 0x80, 0x7F]));
    }

    private static FuzzSeed Create2DSeed(string name, Barcode2D barcode)
    {
        var bytes = new byte[3 + ((barcode.Width * barcode.Height) + 7) / 8];
        bytes[0] = (byte)barcode.Type;
        bytes[1] = (byte)barcode.Height;
        bytes[2] = (byte)barcode.Width;
        for (var i = 0; i < barcode.Width * barcode.Height; i++)
        {
            if (barcode[i / barcode.Width, i % barcode.Width])
            {
                bytes[3 + (i / 8)] |= (byte)(0x80 >> (i % 8));
            }
        }

        return new FuzzSeed(name, bytes);
    }

    private static void Decode2DTarget(ReadOnlySpan<byte> data)
    {
        var type = (Barcode2DType)(FuzzInput.ReadByte(ref data) % 3);
        var height = 1 + FuzzInput.ReadByte(ref data);
        var width = 1 + FuzzInput.ReadByte(ref data);
        var modules = new bool[height, width];
        for (var i = 0; i < height * width && i / 8 < data.Length; i++)
        {
            modules[i / width, i % width] = (data[i / 8] & (0x80 >> (i % 8))) != 0;
        }

        Barcode2DDecodeResult result;
        try
        {
            result = Barcode2DDecoder.Decode(modules, type);
        }
        catch (FormatException)
        {
            FuzzAssert.False(Barcode2DDecoder.TryDecode(modules, type, out _), "TryDecode succeeds where Decode fails");
            return;
        }

        FuzzAssert.Equal(type, result.Type, "The type of the symbol");
        FuzzAssert.True(result.CorrectedErrorCount >= 0, "The number of corrected errors");
        FuzzAssert.True(result.EciAssignmentNumber is null or (>= 0 and <= 999999 + 810900), $"ECI {result.EciAssignmentNumber}");
        FuzzAssert.True(!result.IsGs1 || type is Barcode2DType.DataMatrix, "Only Data Matrix symbols are read as GS1 data");
        FuzzAssert.True(Barcode2DDecoder.TryDecode(modules, type, out var other) && other.Text == result.Text && other.Bytes.Span.SequenceEqual(result.Bytes.Span), "Decoding twice gives different results");
    }

    // Each byte is the width of a run; a 0 byte separates the scan lines
    private static IEnumerable<FuzzSeed> GetLinearSeeds()
    {
        Barcode[] barcodes =
        [
            Barcode.CreateCode39("ABC-123", includeChecksum: true), Barcode.CreateCode93("Hello 93"), Barcode.CreateCode128("SKU-123456"),
            Barcode.CreateGs1128(new Gs1Element("01", "09521234543213"), new Gs1Element("10", "ABC")), Barcode.CreateEan8("5512345", "12"),
            Barcode.CreateEan13("590123412345", "12345"), Barcode.CreateUpcA("03600029145"), Barcode.CreateUpcE("0425261"),
            Barcode.CreateCodabar("40156"), Barcode.CreateItf("12345678"), Barcode.CreateGs1DataBar("0950110153001"),
            Barcode.CreateGs1DataBarLimited("1501234567890"), Barcode.CreateGs1DataBarStacked("0950110153001"),
            Barcode.CreateGs1DataBarExpanded(new Gs1Element("01", "90012345678908"), new Gs1Element("3103", "001750")),
            Barcode.CreateGs1DataBarExpandedStacked(2, new Gs1Element("10", "ABC123"), new Gs1Element("21", "xyz")),
        ];

        foreach (var barcode in barcodes)
        {
            var bytes = new List<byte>();
            var previous = -1;
            for (var row = 0; row < barcode.Height; row++)
            {
                if (row > 0 && previous == row - 1 && Enumerable.Range(0, barcode.Width).All(column => barcode[row, column] == barcode[row - 1, column]))
                    continue;

                previous = row;
                if (bytes.Count > 0)
                {
                    bytes.Add(0);
                }

                bytes.Add(20);
                var runs = new List<int> { 0 };
                for (var column = 0; column < barcode.Width; column++)
                {
                    if (barcode[row, column] != (runs.Count % 2 == 0))
                    {
                        runs.Add(0);
                    }

                    runs[^1] += 2;
                }

                // The quiet zones: the line starts with a light run, and ends with one
                var tail = runs.Skip(1).ToList();
                if (runs.Count % 2 == 1)
                {
                    tail[^1] += 20;
                }
                else
                {
                    tail.Add(20);
                }

                bytes.AddRange(tail.Select(width => (byte)width));
            }

            yield return new FuzzSeed($"decode-linear-{barcode.Type}", bytes.ToArray());
        }
    }

    private static void DecodeLinearTarget(ReadOnlySpan<byte> data)
    {
        var lines = new List<int[]>();
        var current = new List<int>();
        foreach (var value in data)
        {
            if (value is 0)
            {
                lines.Add([.. current]);
                current.Clear();
                continue;
            }

            current.Add(value);
        }

        lines.Add([.. current]);
        var results = BarcodeDecoder.DecodeScanLines(lines);
        foreach (var result in results)
        {
            FuzzAssert.True(result.FirstScanLine >= 0 && result.FirstScanLine <= result.LastScanLine && result.LastScanLine < lines.Count, "The scan lines of a result");
            var width = lines.Skip(result.FirstScanLine).Take(result.LastScanLine - result.FirstScanLine + 1).Max(line => line.Sum());
            FuzzAssert.True(result.Start >= 0 && result.Start < result.End && result.End <= width + 1, $"The position {result.Start}-{result.End} of a result on lines {width} wide");
            FuzzAssert.True(result.Text.Length > 0, "A result without text");
        }

        // Reading the lines again gives the same results
        var again = BarcodeDecoder.DecodeScanLines(lines);
        FuzzAssert.Equal(results.Count, again.Count, "The number of results of the same lines");
    }

    // The first two bytes are the width and the height, the next ones the gray levels, row by row
    private static IEnumerable<FuzzSeed> GetImageSeeds()
    {
        foreach (var (name, qr) in new[] { ("standard", QRCode.Create("HELLO")), ("micro", QRCode.CreateMicroQR("12345")), ("rmqr", QRCode.CreateRMQR("HELLO")) })
        {
            using var image = qr.ToImage(new QRCodeDrawingOptions { ModuleSize = 2, QuietZoneModules = 2 });
            var pixels = new Rgba32[image.Width * image.Height];
            image.Frames[0].CopyPixelDataTo(pixels);
            yield return new FuzzSeed($"image-{name}", (byte[])[(byte)image.Width, (byte)image.Height, .. pixels.Select(pixel => pixel.R)]);
        }
    }

    private static void ReadQRCodeImageTarget(ReadOnlySpan<byte> data)
    {
        var width = 1 + FuzzInput.ReadByte(ref data);
        var height = 1 + FuzzInput.ReadByte(ref data);
        var pixels = new Gray8[width * height];
        for (var i = 0; i < pixels.Length && i < data.Length; i++)
        {
            pixels[i] = new Gray8(data[i]);
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, width, height);
        foreach (var result in image.ReadQRCodes())
        {
            FuzzAssert.Equal(4, result.Corners.Count, "The number of corners");
            FuzzAssert.True(result.Corners.All(corner => double.IsFinite(corner.X) && double.IsFinite(corner.Y)), "A corner is not finite");
            var center = new PointD(result.Corners.Average(corner => corner.X), result.Corners.Average(corner => corner.Y));
            FuzzAssert.True(center.X >= 0 && center.Y >= 0 && center.X <= width && center.Y <= height, $"The center {center} of a symbol is outside the image");
        }
    }

    // A QR code rendered with modules of 3 to 6 pixels, turned by a multiple of 90°, is read back
    private static void ReadQRCodeRoundTripTarget(ReadOnlySpan<byte> data)
    {
        var type = (QRCodeType)(FuzzInput.ReadByte(ref data) % 3);
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(data);
        QRCode qr;
        try
        {
            qr = type switch
            {
                QRCodeType.MicroQR => QRCode.CreateMicroQR(text),
                QRCodeType.RMQR => QRCode.CreateRMQR(text),
                _ => QRCode.Create(text),
            };
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (qr.Width * qr.Height > 80 * 80)
            return;

        using var image = qr.ToImage(new QRCodeDrawingOptions { ModuleSize = 3 + (flags % 4) });
        if ((flags & 0x30) is not 0)
        {
            image.Rotate((flags >> 4 & 3) switch { 1 => RotateMode.Rotate90, 2 => RotateMode.Rotate180, _ => RotateMode.Rotate270 });
        }

        var results = image.ReadQRCodes();
        var expected = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(text));
        FuzzAssert.True(results.Count is 1 && results[0].Text == expected && results[0].Result.Type == type, $"The {type} code of version {qr.Version} is read as [{string.Join(", ", results.Select(result => result.Result.Type + " " + result.Text))}]");
    }

    private static IEnumerable<FuzzSeed> Get2DImageSeeds()
    {
        foreach (var (name, barcode) in new[] { ("datamatrix", Barcode2D.CreateDataMatrix("HELLO")), ("pdf417", Barcode2D.CreatePdf417("HELLO", new Pdf417Options { Columns = 1, RowHeight = 2 })), ("aztec", Barcode2D.CreateAztec("HELLO")) })
        {
            using var image = barcode.ToImage(new Barcode2DDrawingOptions { ModuleSize = 2, QuietZoneModules = 2 });
            var pixels = new Rgba32[image.Width * image.Height];
            image.Frames[0].CopyPixelDataTo(pixels);
            yield return new FuzzSeed($"image-2d-{name}", (byte[])[(byte)image.Width, (byte)image.Height, .. pixels.Select(pixel => pixel.R)]);
        }
    }

    private static void Read2DImageTarget(ReadOnlySpan<byte> data)
    {
        var width = 1 + FuzzInput.ReadByte(ref data);
        var height = 1 + FuzzInput.ReadByte(ref data);
        var pixels = new Gray8[width * height];
        for (var i = 0; i < pixels.Length && i < data.Length; i++)
        {
            pixels[i] = new Gray8(data[i]);
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, width, height);
        foreach (var result in image.ReadBarcodes2D())
        {
            FuzzAssert.Equal(4, result.Corners.Count, "The number of corners");
            FuzzAssert.True(result.Corners.All(corner => double.IsFinite(corner.X) && double.IsFinite(corner.Y)), "A corner is not finite");
            var center = new PointD(result.Corners.Average(corner => corner.X), result.Corners.Average(corner => corner.Y));
            FuzzAssert.True(center.X >= -1 && center.Y >= -1 && center.X <= width + 1 && center.Y <= height + 1, $"The center {center} of a symbol is outside the image");
        }
    }

    // A Data Matrix, PDF417 or Aztec symbol rendered with modules of 3 to 6 pixels, turned by a multiple of 90°, is read back
    private static void Read2DRoundTripTarget(ReadOnlySpan<byte> data)
    {
        var type = (Barcode2DType)(FuzzInput.ReadByte(ref data) % 3);
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(data);
        Barcode2D barcode;
        try
        {
            barcode = type switch
            {
                Barcode2DType.DataMatrix => Barcode2D.CreateDataMatrix(text),
                Barcode2DType.Pdf417 => Barcode2D.CreatePdf417(text),
                _ => Barcode2D.CreateAztec(text),
            };
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (barcode.Width * barcode.Height > 100 * 100)
            return;

        using var image = barcode.ToImage(new Barcode2DDrawingOptions { ModuleSize = 3 + (flags % 4) });
        if ((flags & 0x30) is not 0)
        {
            image.Rotate((flags >> 4 & 3) switch { 1 => RotateMode.Rotate90, 2 => RotateMode.Rotate180, _ => RotateMode.Rotate270 });
        }

        var results = image.ReadBarcodes2D();
        var expected = Barcode2DDecoder.Decode(barcode).Text;
        FuzzAssert.True(results.Count is 1 && results[0].Text == expected && results[0].Result.Type == type, $"The {type} symbol of {barcode.Width}x{barcode.Height} modules is read as [{string.Join(", ", results.Select(result => result.Result.Type + " " + result.Text))}]");
    }
}
