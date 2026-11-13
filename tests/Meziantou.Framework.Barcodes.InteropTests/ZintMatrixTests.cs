using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Barcodes.InteropTests;

/// <summary>
/// Compares the module matrices with the ones of zint. The version, the error correction level and the mask that ZXingReader
/// reads from our symbol are given to zint, so that the matrices only differ when the encodings differ. The payloads use one
/// encoding mode, where the segmentation of the data cannot legitimately differ.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
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
            { BarcodeType.Code128, "A\nB", "" },
            { BarcodeType.Code128, "abc\tdef", "" },
            { BarcodeType.Code128, "ab\t\n\rcd", "" },
            { BarcodeType.Code128, "\u0000\u0001ABC123456\u001f", "" },
            { BarcodeType.Code128, "\tA\na\rB", "" },
            { BarcodeType.Gs1128, "[01]09521234543213[17]250101[10]ABC123", "" },
            { BarcodeType.Gs1128, "[01]09521234543213[10]ABC123[17]250101", "" },
            { BarcodeType.Gs1128, "[00]123456789012345675", "" },
            { BarcodeType.Gs1128, "[3103]000123[410]1234567890123", "" },
            { BarcodeType.Gs1128, "[8200]A(1)%[22]b-2", "" }, // parentheses are data
            { BarcodeType.Gs1128, "[10]1234[21]5678", "" },
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
            { BarcodeType.UpcE, "0123456", "" },
            { BarcodeType.UpcE, "1234567", "" },
            { BarcodeType.UpcE, "0425261", "12" },
            { BarcodeType.UpcE, "0180432", "54321" },
            { BarcodeType.Gs1DataBar, "0950110153001", "" },
            { BarcodeType.Gs1DataBar, "2001234567890", "" },
            { BarcodeType.Gs1DataBar, "9999999999999", "" },
            { BarcodeType.Gs1DataBarStacked, "0950110153001", "" },
            { BarcodeType.Gs1DataBarStacked, "3918528337671", "" },
            { BarcodeType.Gs1DataBarStackedOmnidirectional, "0950110153001", "" },
            { BarcodeType.Gs1DataBarStackedOmnidirectional, "2997220033224", "" }, // right finder value 3
            { BarcodeType.Gs1DataBarLimited, "1501234567890", "" },
            { BarcodeType.Gs1DataBarLimited, "0000000000000", "" },
            { BarcodeType.Gs1DataBarLimited, "1999999999999", "" },
            { BarcodeType.Gs1DataBarExpanded, "[01]98898765432106[3202]012345[15]991231", "" },
            { BarcodeType.Gs1DataBarExpanded, "[01]90012345678908[3103]001750", "" },
            { BarcodeType.Gs1DataBarExpanded, "[01]90012345678908[3203]001750", "" },
            { BarcodeType.Gs1DataBarExpanded, "[01]90012345678908[3102]001750[11]100503", "" },
            { BarcodeType.Gs1DataBarExpanded, "[01]90012345678908[3922]795", "" },
            { BarcodeType.Gs1DataBarExpanded, "[01]90012345678908[3932]0401234[21]XY", "" },
            { BarcodeType.Gs1DataBarExpanded, "[01]09501101530003[17]250101[10]ABC123", "" },
            { BarcodeType.Gs1DataBarExpanded, "[10]12", "" },
            { BarcodeType.Gs1DataBarExpanded, "[21]abc-DEF/12345%xyz", "" },
            { BarcodeType.Gs1DataBarExpanded, "[8200]http://example.com/abc", "" },
            { BarcodeType.Gs1DataBarExpandedStacked, "[01]09501101530003[17]250101[10]ABC123", "4" },
            { BarcodeType.Gs1DataBarExpandedStacked, "[01]09501101530003[17]250101[10]ABC123[21]XY", "4" },
            { BarcodeType.Gs1DataBarExpandedStacked, "[90]0123456789ABCDEFGHIJ0123456789", "2" },
            { BarcodeType.Gs1DataBarExpandedStacked, "[90]0123456789ABCDEFGHIJ0123456789", "6" },
            { BarcodeType.Gs1DataBarExpandedStacked, "[90]0123456789ABCDEFGHIJ0123456789abc", "8" },
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

    // zint is an independent encoder: its symbols, with the version, the segmentation and the mask that it chooses, must
    // decode to the data
    [Theory]
    [MemberData(nameof(QRCodes))]
    public async Task ZintQRCode_IsDecoded(QRCodeType type, ErrorCorrectionLevel level, string data)
    {
        var tools = await BarcodeTools.GetAsync();
        var symbology = type switch
        {
            QRCodeType.MicroQR => "97",
            QRCodeType.RMQR => "145",
            _ => "58",
        };

        var result = QRCodeDecoder.Decode(await GetZintQRCodeAsync(tools, ["-b", symbology, "--secure=" + GetZintLevel(level)], data));

        Assert.Equal((type, level, data), (result.Type, result.ErrorCorrectionLevel, result.Text));
    }

    [Theory]
    [InlineData(26, "héllo wörld")]
    [InlineData(3, "caf\u00E9")]
    [InlineData(7, "Кириллица")]
    [InlineData(20, "漢字のテスト")]
    [InlineData(28, "繁體字")]
    public async Task ZintQRCodeWithEci_IsDecoded(int eci, string data)
    {
        var tools = await BarcodeTools.GetAsync();

        var result = QRCodeDecoder.Decode(await GetZintQRCodeAsync(tools, ["-b", "58", "--eci=" + eci.ToString(CultureInfo.InvariantCulture)], data));

        Assert.Equal(data, result.Text);
        Assert.Contains(eci, result.Segments.Select(segment => segment.EciAssignmentNumber ?? -1).ToArray());
    }

    private static async Task<bool[,]> GetZintQRCodeAsync(BarcodeTools tools, IEnumerable<string> arguments, string data)
    {
        // The dump gives the rows, padded to a multiple of 4 modules: the width of an rMQR code is the size that fits
        var rows = await tools.GetZintRowsAsync(arguments, data);
        var digits = rows[0].Length;
        var width = (rows.Count + 3) / 4 == digits ? rows.Count : RMQRSizes.Select(size => size.Width).Distinct().Single(width => (width + 3) / 4 == digits);
        return await tools.GetZintMatrixAsync(arguments, data, width);
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

    // The Data Matrix sizes in the order of zint's --vers: the squares, then the rectangles
    private static readonly (int Height, int Width)[] DataMatrixSizes =
    [
        (10, 10), (12, 12), (14, 14), (16, 16), (18, 18), (20, 20), (22, 22), (24, 24), (26, 26), (32, 32), (36, 36), (40, 40),
        (44, 44), (48, 48), (52, 52), (64, 64), (72, 72), (80, 80), (88, 88), (96, 96), (104, 104), (120, 120), (132, 132), (144, 144),
        (8, 18), (8, 32), (12, 26), (12, 36), (16, 36), (16, 48),
    ];

    // zint chooses the encodation schemes with its own algorithm, which can differ from the look-ahead of ISO/IEC 16022
    // when two choices have the same length: the payloads are ones where the choices agree
    [Theory]
    [InlineData("123456", DataMatrixShape.Square)]
    [InlineData("0123456789012345678901234567890123456789", DataMatrixShape.Square)]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890", DataMatrixShape.Square)]
    [InlineData("A", DataMatrixShape.Square)]
    [InlineData("Hello", DataMatrixShape.Square)]
    [InlineData("Hello, World!", DataMatrixShape.Square)]
    [InlineData("@@@@ABCD^^^^", DataMatrixShape.Square)]
    [InlineData("123456", DataMatrixShape.Rectangle)]
    public async Task DataMatrix_MatchesZint(string data, DataMatrixShape shape)
    {
        var tools = await BarcodeTools.GetAsync();
        var code = Barcode2D.CreateDataMatrix(data, new DataMatrixOptions { Shape = shape });
        var version = Array.IndexOf(DataMatrixSizes, (code.Height, code.Width)) + 1;
        var expected = await tools.GetZintMatrixAsync(["-b", "71", "--vers=" + version.ToString(CultureInfo.InvariantCulture)], data, code.Width);
        AssertSameModules(expected, code.Width, code.Height, (row, column) => code[row, column]);
    }

    [Theory]
    [InlineData("[01]09521234543213[17]250101[10]ABC123")]
    [InlineData("[01]09521234543213[10]12345678[21]1234")]
    [InlineData("[00]123456789012345675")]
    public async Task Gs1DataMatrix_MatchesZint(string data)
    {
        var tools = await BarcodeTools.GetAsync();
        var code = Barcode2D.CreateGs1DataMatrix([.. ParseGs1(data)]);
        var version = Array.IndexOf(DataMatrixSizes, (code.Height, code.Width)) + 1;
        var expected = await tools.GetZintMatrixAsync(["-b", "71", "--gs1", "--gs1nocheck", "--vers=" + version.ToString(CultureInfo.InvariantCulture)], data, code.Width);
        AssertSameModules(expected, code.Width, code.Height, (row, column) => code[row, column]);
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
            BarcodeType.Code128 => (Barcode.CreateCode128(data), ["-b", "20", "--esc"], EscapeForZint(data)),
            BarcodeType.Ean8 => (Barcode.CreateEan8(data, extension), ["-b", "10", "--addongap=7"], WithAddOn(data, extension)),
            BarcodeType.Ean13 => (Barcode.CreateEan13(data, extension), ["-b", "15", "--addongap=7"], WithAddOn(data, extension)),
            BarcodeType.UpcA => (Barcode.CreateUpcA(data, extension), ["-b", "34", "--addongap=9"], WithAddOn(data, extension)),
            BarcodeType.Codabar => (Barcode.CreateCodabar(data), ["-b", "18"], "A" + data + "B"),
            BarcodeType.Itf => (Barcode.CreateItf(data), ["-b", "3"], data),
            BarcodeType.Gs1128 => (Barcode.CreateGs1128([.. ParseGs1(data)]), ["-b", "16", "--gs1nocheck"], data),
            BarcodeType.UpcE => (Barcode.CreateUpcE(data, extension), ["-b", "37", "--addongap=9"], WithAddOn(data, extension)),
            BarcodeType.Gs1DataBar => (Barcode.CreateGs1DataBar(data), ["-b", "29"], data),
            BarcodeType.Gs1DataBarStacked => (Barcode.CreateGs1DataBarStacked(data), ["-b", "79"], data),
            BarcodeType.Gs1DataBarStackedOmnidirectional => (Barcode.CreateGs1DataBarStackedOmnidirectional(data), ["-b", "80"], data),
            BarcodeType.Gs1DataBarLimited => (Barcode.CreateGs1DataBarLimited(data), ["-b", "30"], data),
            BarcodeType.Gs1DataBarExpanded => (Barcode.CreateGs1DataBarExpanded([.. ParseGs1(data)]), ["-b", "31", "--gs1nocheck"], data),
            BarcodeType.Gs1DataBarExpandedStacked => (Barcode.CreateGs1DataBarExpandedStacked(int.Parse(option, CultureInfo.InvariantCulture), [.. ParseGs1(data)]), ["-b", "81", "--gs1nocheck", "--cols=" + (int.Parse(option, CultureInfo.InvariantCulture) / 2).ToString(CultureInfo.InvariantCulture)], data),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };

        // zint ends GS1 DataBar Limited with 5 light modules
        var zintWidth = type is BarcodeType.Gs1DataBarLimited ? barcode.Width + 5 : barcode.Width;
        var expected = await tools.GetZintMatrixAsync(arguments, zintData, zintWidth);

        // zint gives each row of a stacked symbol once, the barcode as many times as it is modules high
        var rows = GetDistinctRows(barcode);
        AssertSameModules(expected, zintWidth, rows.Count, (row, column) => column < barcode.Width && barcode[rows[row], column]);

        static string WithAddOn(string data, string? extension) => extension is null ? data : data + "+" + extension;
    }

    // zint picks the shortest sequence of code sets; when several sequences have the same length, it may choose another
    // one than ours, so the random payloads only compare the number of modules
    [Fact]
    public async Task Code128_RandomAscii_IsAsShortAsZint()
    {
        var tools = await BarcodeTools.GetAsync();
        var random = new Random(4204);
        string[] classes = ["0123456789", "\u0000\u0001\t\n\r\u001b\u001f", "ABCXYZ !@_^\\", "abcxyz`{|}~\u007f"];
        for (var i = 0; i < 200; i++)
        {
            var data = new StringBuilder();
            var length = random.Next(1, 30);
            while (data.Length < length)
            {
                var characters = classes[random.Next(classes.Length)];
                var runLength = random.Next(1, 7);
                for (var j = 0; j < runLength; j++)
                {
                    data.Append(characters[random.Next(characters.Length)]);
                }
            }

            var barcode = Barcode.CreateCode128(data.ToString());
            var zintRow = await tools.GetZintRowsAsync(["-b", "20", "--esc"], EscapeForZint(data.ToString()));
            Assert.HasCount((barcode.Width + 3) / 4, Assert.Single(zintRow), message: EscapeForZint(data.ToString()));
        }
    }

    [Fact]
    public async Task Gs1128_RandomElements_AreAsShortAsZint()
    {
        var tools = await BarcodeTools.GetAsync();
        var random = new Random(4203);
        for (var i = 0; i < 200; i++)
        {
            var zintData = CreateRandomGs1Data(random);
            var barcode = Barcode.CreateGs1128([.. ParseGs1(zintData)]);
            var zintRow = await tools.GetZintRowsAsync(["-b", "16", "--gs1nocheck"], zintData);
            Assert.HasCount((barcode.Width + 3) / 4, Assert.Single(zintRow), message: zintData);
        }
    }

    // Element strings in the syntax of zint: [01]09521234543213[10]ABC
    internal static string CreateRandomGs1Data(Random random)
    {
        string From(string characters, int min, int max) => string.Concat(Enumerable.Range(0, random.Next(min, max + 1)).Select(_ => characters[random.Next(characters.Length)]));

        var data = new StringBuilder();
        var count = random.Next(1, 4);
        for (var i = 0; i < count; i++)
        {
            data.Append(random.Next(5) switch
            {
                0 => "[01]" + From("0123456789", 14, 14),
                1 => "[17]" + From("0123456789", 6, 6),
                2 => "[3103]" + From("0123456789", 6, 6),
                3 => "[10]" + From("0123456789", 1, 10),
                _ => "[21]" + From("0123456789ABCXYZabcxyz-./_%", 1, 10),
            });
        }

        return data.ToString();
    }

    internal static IEnumerable<Gs1Element> ParseGs1(string data)
    {
        foreach (var part in data.Split('[', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf(']', StringComparison.Ordinal);
            yield return new Gs1Element(part[..index], part[(index + 1)..]);
        }
    }

    // The data, the number of columns, the error correction level, and whether the data is bytes (one per character)
    public static TheoryData<string, int, int, bool> Pdf417Codes()
    {
        return new TheoryData<string, int, int, bool>
        {
            { "0123456789012", 1, 0, false },
            { "1234567890123456789012345678901234567890123456789", 2, 2, false },
            { string.Concat(Enumerable.Repeat("9876543210", 30)), 10, 4, false },
            { "HELLO WORLD", 2, 2, false },
            { "PDF417 SYMBOL", 3, 1, false },
            { "hello world", 4, 3, false },
            { string.Concat(Enumerable.Repeat("THE QUICK BROWN FOX ", 20)), 6, 5, false },
            { "\u0080\u0081þÿ\u0000\u0010", 3, 2, true },
            { "ÀÁÂÃÄÅÆ", 5, 3, true },
            { string.Concat(Enumerable.Range(128, 128).Select(value => (char)value)), 8, 6, true },
            { "ABC", 30, 8, false },
        };
    }

    [Theory]
    [MemberData(nameof(Pdf417Codes))]
    public async Task Pdf417_MatchesZint(string data, int columns, int level, bool binary)
    {
        var tools = await BarcodeTools.GetAsync();
        var options = new Pdf417Options { Columns = columns, SecurityLevel = level, RowHeight = 2 };
        var barcode = binary ? Barcode2D.CreatePdf417(Encoding.Latin1.GetBytes(data), options) : Barcode2D.CreatePdf417(data, options);

        List<string> arguments = ["-b", "55", "--cols=" + columns.ToString(CultureInfo.InvariantCulture), "--secure=" + level.ToString(CultureInfo.InvariantCulture)];
        if (binary)
        {
            arguments.AddRange(["--binary", "--esc"]);
        }

        var expected = await tools.GetZintMatrixAsync(arguments, binary ? EscapeBytesForZint(data) : data, barcode.Width);

        // Each row of the symbol is RowHeight rows of the matrix, and one row of zint
        Assert.Equal(barcode.Height, expected.GetLength(0) * options.RowHeight);
        AssertSameModules(expected, barcode.Width, expected.GetLength(0), (row, column) => barcode[row * options.RowHeight, column]);
    }

    // Every byte that is not a printable ASCII character as an escape sequence (--esc)
    private static string EscapeBytesForZint(string data)
    {
        var builder = new StringBuilder();
        foreach (var character in data)
        {
            if (character is < ' ' or > '~' or '\\')
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\x{(int)character:X2}");
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    // zint reads control characters as escape sequences (--esc), and a backslash as a double backslash
    private static string EscapeForZint(string data)
    {
        var builder = new StringBuilder();
        foreach (var character in data)
        {
            if (character == '\\')
            {
                builder.Append(@"\\");
            }
            else if (character < ' ' || character == '\u007f')
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\x{(int)character:X2}");
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static TheoryData<string, bool, int> AztecCodes()
    {
        // The data, whether the symbol is compact, and the number of layers. The payloads use one mode (upper, lower or
        // digit), where the encodation cannot legitimately differ. The size is given to zint: zint picks the size from
        // tables of capacities that do not follow a formula, where we reserve the requested percentage of the symbol capacity
        // plus 3 codewords. With the same size, the error correction codewords fill what the data leaves in both encoders.
        return new TheoryData<string, bool, int>
        {
            { "HELLO", true, 1 },
            { "HELLO WORLD", true, 2 },
            { "12345678901234567890", true, 2 },
            { "AZTEC", true, 3 },
            { "AZTEC", true, 4 },
            { "AZTEC", false, 1 },
            { "AZTEC", false, 2 },
            { "0123456789", false, 4 },
            { "abcdefghijklmnopqrstuvwxyz", false, 5 },
            { new string('7', 200), false, 9 },
            { string.Concat(Enumerable.Repeat("ABCDEFGHIJ", 30)), false, 15 },
            { new string('5', 1500), false, 23 },
            { string.Concat(Enumerable.Repeat("XYZ", 200)), false, 32 },
        };
    }

    [Theory]
    [MemberData(nameof(AztecCodes))]
    public async Task Aztec_MatchesZint(string data, bool compact, int layers)
    {
        var tools = await BarcodeTools.GetAsync();
        var barcode = Barcode2D.CreateAztec(data, new AztecOptions { Compact = compact, Layers = layers });

        // zint numbers the compact sizes 1 to 4, and the full-range sizes 5 to 36
        var version = compact ? layers : layers + 4;
        var expected = await tools.GetZintMatrixAsync(["-b", "92", "--vers=" + version.ToString(CultureInfo.InvariantCulture)], data, barcode.Width);
        AssertSameModules(expected, barcode.Width, barcode.Height, (row, column) => barcode[row, column]);
    }

    [Theory]
    [InlineData("ÉCI", true, 2)]
    [InlineData("ÀÉÎÕÜ", false, 3)]
    [InlineData("日本", true, 2)]
    public async Task AztecWithEci_MatchesZint(string data, bool compact, int layers)
    {
        var tools = await BarcodeTools.GetAsync();
        var barcode = Barcode2D.CreateAztec(data, new AztecOptions { Compact = compact, Layers = layers });

        var version = compact ? layers : layers + 4;
        var expected = await tools.GetZintMatrixAsync(["-b", "92", "--eci=26", "--vers=" + version.ToString(CultureInfo.InvariantCulture)], data, barcode.Width);
        AssertSameModules(expected, barcode.Width, barcode.Height, (row, column) => barcode[row, column]);
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

    private static string GetCheckDigit(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            sum += (digits[digits.Length - 1 - i] - '0') * (i % 2 == 0 ? 3 : 1);
        }

        return ((10 - (sum % 10)) % 10).ToString(CultureInfo.InvariantCulture);
    }

    // The first row of each run of identical rows
    private static List<int> GetDistinctRows(Barcode barcode)
    {
        var rows = new List<int>();
        for (var row = 0; row < barcode.Height; row++)
        {
            if (rows.Count == 0 || Enumerable.Range(0, barcode.Width).Any(column => barcode[row, column] != barcode[rows[^1], column]))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    [Fact]
    public async Task Gs1DataBarExpanded_RandomElements_MatchZint()
    {
        var tools = await BarcodeTools.GetAsync();
        var random = new Random(4684);
        for (var i = 0; i < 100; i++)
        {
            // zint does not check the GTINs (--gs1nocheck) and omits their check digit, while we keep a wrong one
            var zintData = string.Concat(ParseGs1(CreateRandomGs1Data(random)).Select(element => "[" + element.ApplicationIdentifier + "]" + (element.ApplicationIdentifier is "01" ? element.Value[..13] + GetCheckDigit(element.Value[..13]) : element.Value)));
            var segmentsPerRow = 2 * random.Next(1, 11);
            var barcode = Barcode.CreateGs1DataBarExpandedStacked(segmentsPerRow, [.. ParseGs1(zintData)]);
            var expected = await tools.GetZintMatrixAsync(["-b", "81", "--gs1nocheck", "--cols=" + (segmentsPerRow / 2).ToString(CultureInfo.InvariantCulture)], zintData, barcode.Width);
            var rows = GetDistinctRows(barcode);
            AssertSameModules(expected, barcode.Width, rows.Count, (row, column) => barcode[rows[row], column]);
        }
    }

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
