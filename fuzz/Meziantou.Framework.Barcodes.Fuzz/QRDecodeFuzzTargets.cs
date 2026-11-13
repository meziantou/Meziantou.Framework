using Meziantou.Framework.Fuzzing;

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

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("barcodes-decode-qrcode", DecodeTarget) { Seeds = GetSeeds, Dictionary = ["\0\0\0\0", "\xFF\xFF\xFF\xFF", "\x55\x55", "\xAA\xAA", "\x7F", "\x80"] },
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
}
