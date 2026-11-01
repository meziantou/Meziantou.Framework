using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Barcodes.InteropTests;

/// <summary>
/// Compares the module matrices with the ones of zint. The version, the error correction level and the mask that ZXingReader
/// reads from our symbol are given to zint, so that the matrices only differ when the encodings differ. The payloads use one
/// encoding mode, where the segmentation of the data cannot legitimately differ.
/// </summary>
public sealed class ZintMatrixTests
{
    // The rMQR sizes in the order of their version numbers (ISO/IEC 23941, Table 1), which is the order of zint's --vers
    private static readonly (int Height, int Width)[] RMQRSizes =
    [
        (7, 43), (7, 59), (7, 77), (7, 99), (7, 139),
        (9, 43), (9, 59), (9, 77), (9, 99), (9, 139),
        (11, 27), (11, 43), (11, 59), (11, 77), (11, 99), (11, 139),
        (13, 27), (13, 43), (13, 59), (13, 77), (13, 99), (13, 139),
        (15, 43), (15, 59), (15, 77), (15, 99), (15, 139),
        (17, 43), (17, 59), (17, 77), (17, 99), (17, 139),
    ];

    public static TheoryData<QRCodeType, ErrorCorrectionLevel, string> QRCodes()
    {
        var data = new TheoryData<QRCodeType, ErrorCorrectionLevel, string>();
        foreach (var level in Enum.GetValues<ErrorCorrectionLevel>())
        {
            data.Add(QRCodeType.Standard, level, "01234567");
            data.Add(QRCodeType.Standard, level, "HELLO WORLD");
            data.Add(QRCodeType.Standard, level, "hello world, from a qr code");
            data.Add(QRCodeType.Standard, level, "漢字のテスト");
            data.Add(QRCodeType.Standard, level, "héllo wörld");
            data.Add(QRCodeType.Standard, level, new string('7', 500)); // version information (version 7 and up)
            data.Add(QRCodeType.Standard, level, string.Concat(Enumerable.Repeat("abcdefghij", 40)));
        }

        foreach (var level in new[] { ErrorCorrectionLevel.L, ErrorCorrectionLevel.M })
        {
            data.Add(QRCodeType.MicroQR, level, "12345");
            data.Add(QRCodeType.MicroQR, level, "ABCDE");
            data.Add(QRCodeType.MicroQR, level, "hello");
            data.Add(QRCodeType.MicroQR, level, "0123456789012345");
        }

        data.Add(QRCodeType.MicroQR, ErrorCorrectionLevel.Q, "1234567");
        foreach (var level in new[] { ErrorCorrectionLevel.M, ErrorCorrectionLevel.H })
        {
            data.Add(QRCodeType.RMQR, level, "1234567890");
            data.Add(QRCodeType.RMQR, level, "HELLO WORLD");
            data.Add(QRCodeType.RMQR, level, "hello world");
            data.Add(QRCodeType.RMQR, level, new string('9', 120));
        }

        return data;
    }

    public static TheoryData<BarcodeType, string, string> Barcodes()
    {
        return new TheoryData<BarcodeType, string, string>
        {
            { BarcodeType.Code39, "ABC-123", "" },
            { BarcodeType.Code39, "ABC-123", "checksum" },
            { BarcodeType.Code39, "0123456789 $%+-./", "" },
            { BarcodeType.Code93, "ABC123", "" },
            { BarcodeType.Code93, "ABC123xyz-.$/+%", "" },
            { BarcodeType.Code128, "HELLO", "" },
            { BarcodeType.Code128, "hello world", "" },
            { BarcodeType.Code128, "12345678", "" },
            { BarcodeType.Code128, "SKU-123456", "" },
            { BarcodeType.Ean8, "5512345", "" },
            { BarcodeType.Ean8, "9638507", "12" },
            { BarcodeType.Ean13, "590123412345", "" },
            { BarcodeType.Ean13, "400638133393", "12" },
            { BarcodeType.Ean13, "590123412345", "12345" },
            { BarcodeType.UpcA, "03600029145", "" },
            { BarcodeType.UpcA, "12345678901", "54321" },
            { BarcodeType.Codabar, "40156", "" },
            { BarcodeType.Codabar, "1234-5678$:/.+", "" },
            { BarcodeType.Itf, "12345678", "" },
            { BarcodeType.Itf, "00", "" },
        };
    }

    [Theory]
    [MemberData(nameof(QRCodes))]
    public async Task QRCode_MatchesZint(QRCodeType type, ErrorCorrectionLevel level, string data)
    {
        var qr = type switch
        {
            QRCodeType.MicroQR => QRCode.CreateMicroQR(data, level),
            QRCodeType.RMQR => QRCode.CreateRMQR(data, level),
            _ => QRCode.Create(data, level),
        };

        await AssertMatchesZintAsync(qr, level, data);
    }

    public static TheoryData<QRCodeType, ErrorCorrectionLevel, string, int, int> SizeConstrainedQRCodes()
    {
        // The type, the level, the data, and the version (Micro QR, standard) or the height and the width (rMQR)
        return new TheoryData<QRCodeType, ErrorCorrectionLevel, string, int, int>
        {
            { QRCodeType.Standard, ErrorCorrectionLevel.M, "HELLO WORLD", 10, 0 },
            { QRCodeType.Standard, ErrorCorrectionLevel.H, "0123456789", 7, 0 },
            { QRCodeType.Standard, ErrorCorrectionLevel.L, "hello", 27, 0 },
            { QRCodeType.MicroQR, ErrorCorrectionLevel.L, "12345", 4, 0 },
            { QRCodeType.MicroQR, ErrorCorrectionLevel.M, "AB", 3, 0 },
            { QRCodeType.RMQR, ErrorCorrectionLevel.M, "HELLO", 17, 139 },
            { QRCodeType.RMQR, ErrorCorrectionLevel.H, "1234", 7, 99 },
            { QRCodeType.RMQR, ErrorCorrectionLevel.M, "123", 13, 27 },
        };
    }

    [Theory]
    [MemberData(nameof(SizeConstrainedQRCodes))]
    public async Task SizeConstrainedQRCode_MatchesZint(QRCodeType type, ErrorCorrectionLevel level, string data, int version, int width)
    {
        var qr = type switch
        {
            QRCodeType.MicroQR => QRCode.CreateMicroQR(data, level, new MicroQRCodeOptions { MinimumVersion = version, MaximumVersion = version }),
            QRCodeType.RMQR => QRCode.CreateRMQR(data, level, new RMQRCodeOptions { Height = version, Width = width }),
            _ => QRCode.Create(data, level, new QRCodeOptions { MinimumVersion = version, MaximumVersion = version }),
        };

        if (type is QRCodeType.RMQR)
        {
            Assert.Equal((version, width), (qr.Height, qr.Width));
        }
        else
        {
            Assert.Equal(version, qr.Version);
        }

        await AssertMatchesZintAsync(qr, level, data);
    }

    private static async Task AssertMatchesZintAsync(QRCode qr, ErrorCorrectionLevel level, string data)
    {
        var tools = await BarcodeTools.GetAsync();
        var type = qr.Type;
        await using var directory = TemporaryDirectory.Create();
        var path = directory.FullPath / "qrcode.png";
        await File.WriteAllBytesAsync(path, qr.ToPng(new QRCodePngOptions { ModuleSize = 4 }), XunitCancellationToken);
        var decoded = Assert.Single(await tools.ReadWithZXingAsync([path]));
        Assert.Equal(data, decoded.Text);
        Assert.Equal(level.ToString(), decoded.ErrorCorrectionLevel);
        Assert.NotNull(decoded.Version);
        Assert.NotNull(decoded.DataMask);

        var (symbology, version) = type switch
        {
            QRCodeType.MicroQR => ("97", decoded.Version[1..]),
            QRCodeType.RMQR => ("145", (Array.IndexOf(RMQRSizes, (qr.Height, qr.Width)) + 1).ToString(CultureInfo.InvariantCulture)),
            _ => ("58", decoded.Version),
        };
        if (type is QRCodeType.Standard)
        {
            Assert.Equal(qr.Version.ToString(CultureInfo.InvariantCulture), decoded.Version);
        }

        List<string> arguments = ["-b", symbology, "--vers=" + version, "--mask=" + decoded.DataMask.Value.ToString(CultureInfo.InvariantCulture), "--secure=" + GetZintLevel(level)];
        if (!Ascii.IsValid(data) && !data.Any(IsKanji))
        {
            arguments.Add("--binary"); // our byte mode is UTF-8 without an ECI header
        }

        var expected = await tools.GetZintMatrixAsync(arguments, data, qr.Width);
        AssertSameModules(expected, qr.Width, qr.Height, (row, column) => qr[row, column]);
    }

    [Theory]
    [InlineData(26, "héllo wörld", true)]
    [InlineData(26, "漢字のテスト", true)]
    [InlineData(26, "Ελληνικά и кириллица", true)]
    [InlineData(26, "plain ascii text", true)]
    [InlineData(3, "caf\u00E9", false)]
    [InlineData(170, "two-byte designator", false)]
    [InlineData(899, "binary", false)]
    [InlineData(20000, "three-byte designator", false)]
    [InlineData(999999, "largest designator", false)]
    public async Task QRCodeWithEci_MatchesZint(int eci, string data, bool text)
    {
        var tools = await BarcodeTools.GetAsync();
        var options = new QRCodeOptions { EciAssignmentNumber = eci };
        var qr = text ? QRCode.Create(data, ErrorCorrectionLevel.M, options) : QRCode.Create(Encoding.UTF8.GetBytes(data), ErrorCorrectionLevel.M, options);

        await using var directory = TemporaryDirectory.Create();
        var path = directory.FullPath / "qrcode.png";
        await File.WriteAllBytesAsync(path, qr.ToPng(new QRCodePngOptions { ModuleSize = 4 }), XunitCancellationToken);
        var decoded = Assert.Single(await tools.ReadWithZXingAsync([path]));
        Assert.True(decoded.HasEci);
        Assert.NotNull(decoded.DataMask);
        Assert.Equal(Encoding.UTF8.GetBytes(data), decoded.Bytes);

        List<string> arguments = ["-b", "58", "--vers=" + qr.Version.ToString(CultureInfo.InvariantCulture), "--mask=" + decoded.DataMask.Value.ToString(CultureInfo.InvariantCulture), "--secure=2", "--eci=" + eci.ToString(CultureInfo.InvariantCulture)];
        if (!text)
        {
            arguments.Add("--binary");
        }

        var expected = await tools.GetZintMatrixAsync(arguments, data, qr.Width);
        AssertSameModules(expected, qr.Width, qr.Height, (row, column) => qr[row, column]);
    }

    [Theory]
    [MemberData(nameof(Barcodes))]
    public async Task Barcode_MatchesZint(BarcodeType type, string data, string option)
    {
        var tools = await BarcodeTools.GetAsync();
        var extension = option is "" or "checksum" ? null : option;
        var (barcode, arguments, zintData) = type switch
        {
            BarcodeType.Code39 when option is "checksum" => (Barcode.CreateCode39(data, includeChecksum: true), new[] { "-b", "8", "--vers=1" }, data),
            BarcodeType.Code39 => (Barcode.CreateCode39(data), ["-b", "8"], data),
            BarcodeType.Code93 => (Barcode.CreateCode93(data), ["-b", "25"], data),
            BarcodeType.Code128 => (Barcode.CreateCode128(data), ["-b", "20"], data),
            BarcodeType.Ean8 => (Barcode.CreateEan8(data, extension), ["-b", "10", "--addongap=7"], WithAddOn(data, extension)),
            BarcodeType.Ean13 => (Barcode.CreateEan13(data, extension), ["-b", "15", "--addongap=7"], WithAddOn(data, extension)),
            BarcodeType.UpcA => (Barcode.CreateUpcA(data, extension), ["-b", "34", "--addongap=9"], WithAddOn(data, extension)),
            BarcodeType.Codabar => (Barcode.CreateCodabar(data), ["-b", "18"], "A" + data + "B"),
            BarcodeType.Itf => (Barcode.CreateItf(data), ["-b", "3"], data),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };

        var expected = await tools.GetZintMatrixAsync(arguments, zintData, barcode.Width);
        AssertSameModules(expected, barcode.Width, barcode.Height, (row, column) => barcode[row, column]);

        static string WithAddOn(string data, string? extension) => extension is null ? data : data + "+" + extension;
    }

    private static string GetZintLevel(ErrorCorrectionLevel level) => level switch
    {
        ErrorCorrectionLevel.L => "1",
        ErrorCorrectionLevel.M => "2",
        ErrorCorrectionLevel.Q => "3",
        ErrorCorrectionLevel.H => "4",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, message: null),
    };

    private static bool IsKanji(char c) => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿';

    private static void AssertSameModules(bool[,] expected, int width, int height, Func<int, int, bool> isDark)
    {
        Assert.Equal(height, expected.GetLength(0));
        var differences = new StringBuilder();
        var count = 0;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (expected[row, column] != isDark(row, column))
                {
                    count++;
                    if (count <= 10)
                    {
                        differences.Append(CultureInfo.InvariantCulture, $" ({column}, {row})");
                    }
                }
            }
        }

        if (count > 0)
        {
            Assert.Fail($"{count} of {width * height} modules differ from zint:{differences}");
        }
    }
}
