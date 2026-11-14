using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class RMQRCodeTests
{
    [Fact]
    public void CreateRMQR_M_Short()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        Assert.True(qr.Width > qr.Height);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#.#.#.#.#.###
            #.....#.###.#.##....#.#.#.#
            #.###.#..####..##..#.#...##
            #.###.#...#.##..####.###...
            #.###.#....#..#...##..###.#
            #.....#..##.####.##.###.##.
            #######.###.##.#.#...######
            ........#..#.###....#.#...#
            ##########.#.#..##.#.##.#.#
            #.##.#...####.#...#.#.#...#
            ###.#.#.#.#.#.#.#.#.#.#####
            """);
    }

    [Fact]
    public void CreateRMQR_H_Short()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.H);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#.#.#.#.#.###
            #.....#..##.###.....#.#.#.#
            #.###.#....#####.#.#.#...##
            #.###.#.####.##.####.###...
            #.###.#..#.#...#.###..###.#
            #.....#.###.##....#####..#.
            #######..#.##.#.....#.#####
            .........#####.##..#..#...#
            ###.#..##.#..#.###.####.#.#
            #.#.#.#.#.#.##.#..###.#...#
            ###.#.#.#.#.#.#.#.#.#.#####
            """);
    }

    [Fact]
    public void CreateRMQR_Numeric()
    {
        var qr = QRCode.CreateRMQR("12345", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#.#.#.#.#.###
            #.....#.###.#.#..###....#.#
            #.###.#..#####.#.#..#...#.#
            #.###.#...#.####........#..
            #.###.#....##.#.#######..##
            #.....#..##.#.......###.##.
            #######.#..#.####.#..######
            ........##.###.####.#.#...#
            ##.##.#.#.....#...##.##.#.#
            #.##.#....#....#.#..#.#...#
            ###.#.#.#.#.#.#.#.#.#.#####
            """);
    }

    [Fact]
    public void CreateRMQR_Byte()
    {
        var qr = QRCode.CreateRMQR("hello", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        Assert.True(qr.Width > qr.Height);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#.#.#.#.#.###
            #.....#.###..##...#....#..#
            #.###.#..####.#.#######..##
            #.###.#...#.######..####.#.
            #.###.#......#.#...####...#
            #.....#..####..#.#..###.##.
            #######..##..#.#.....######
            ........#..#####.##.#.#...#
            ##...#.##.#..#..##.#.##.#.#
            #.#....###...#.#..#.#.#...#
            ###.#.#.#.#.#.#.#.#.#.#####
            """);
    }

    [Fact]
    public void CreateRMQR_Url()
    {
        var qr = QRCode.CreateRMQR("https://example.com", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        Assert.Equal(9, qr.Height);
        Assert.Equal(59, qr.Width);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#.#.###.#.#.#.#.#.#.#.#.###.#.#.#.#.#.#.#.###
            #.....#.#..#.####.#.##.#..#.#...###..##.#.#..#.#..##.###..#
            #.###.#.#.##....#.###..###.#...#.#..#######.####...#.####.#
            #.###.#...#.##...#..#...#..##.#.####.#....#.####.#......##.
            #.###.#..#.#####.#.#####.##.#.#......#####..#.###..########
            #.....#.....##.##.#...###..########..#...##....#....#.#...#
            #######..#.#.###..###....###..#####...#######..#..#####.#.#
            .........#..###.#.#.##.#....#.#..#...##.#...###..#...##...#
            ###.#.#.#.#.#.#.#.###.#.#.#.#.#.#.#.#.###.#.#.#.#.#.#.#####
            """);
    }

    // ───── Properties ─────

    [Fact]
    public void CreateRMQR_IsRectangular()
    {
        var qr = QRCode.CreateRMQR("TEST", ErrorCorrectionLevel.M);

        Assert.NotEqual(qr.Width, qr.Height);
        Assert.True(qr.Width > qr.Height);
    }

    // ───── Mixed modes and segments ─────

    [Theory]
    [InlineData("12345")]
    [InlineData("HELLO WORLD")]
    [InlineData("https://www.meziantou.net")]
    [InlineData("漢字のテスト")]
    public void CreateRMQR_SingleMode_WhenSegmentsAreNotSmaller(string data)
    {
        QRCodeSegment segment = data switch
        {
            "12345" => QRCodeSegment.CreateNumeric(data),
            "HELLO WORLD" => QRCodeSegment.CreateAlphanumeric(data),
            "漢字のテスト" => QRCodeSegment.CreateKanji(data),
            _ => QRCodeSegment.CreateByte(data),
        };

        Assert.Equal(RenderAsText(QRCode.CreateRMQR(data)), RenderAsText(QRCode.CreateRMQR([segment])));
    }

    [Fact]
    public void CreateRMQR_MixedModes_Snapshot()
    {
        // The lowercase letters in byte mode, the digits in numeric mode; large modules and a quiet zone, so that the snapshot can be scanned
        var qr = QRCode.CreateRMQR("Order 12345678901234567890");

        Snapshot.Validate(qr.ToPng(new QRCodePngOptions { ModuleSize = 8, QuietZoneModules = 2 }), SnapshotType.Png);
    }

    [Fact]
    public void CreateRMQR_MixedModes_GiveASmallerSymbol()
    {
        var mixed = QRCode.CreateRMQR("item 12345678901234567890 OK");
        var segments = QRCode.CreateRMQR([QRCodeSegment.CreateByte("item "), QRCodeSegment.CreateNumeric("12345678901234567890"), QRCodeSegment.CreateAlphanumeric(" OK")]);
        var single = QRCode.CreateRMQR([QRCodeSegment.CreateByte("item 12345678901234567890 OK")]);

        Assert.Equal(RenderAsText(mixed), RenderAsText(segments));
        Assert.True(mixed.Width * mixed.Height < single.Width * single.Height);
    }

    [Fact]
    public void CreateRMQR_Segments_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("segments", () => QRCode.CreateRMQR((IEnumerable<QRCodeSegment>)null!));
        Assert.Throws<ArgumentException>("segments", () => QRCode.CreateRMQR([QRCodeSegment.CreateNumeric("1"), null!]));
        Assert.Throws<ArgumentException>("segments", () => QRCode.CreateRMQR([QRCodeSegment.CreateEci(26), QRCodeSegment.CreateNumeric("1")]));
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateRMQR([QRCodeSegment.CreateNumeric("1")], ErrorCorrectionLevel.L));
        Assert.Throws<ArgumentException>("options", () => QRCode.CreateRMQR([QRCodeSegment.CreateNumeric("1")], ErrorCorrectionLevel.M, new RMQRCodeOptions { Height = 7, Width = 27 }));
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateRMQR([QRCodeSegment.CreateByte(new byte[200])], ErrorCorrectionLevel.H));
    }

    // ───── Error cases ─────

    [Fact]
    public void CreateRMQR_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QRCode.CreateRMQR((string)null!));
    }

    [Fact]
    public void CreateRMQR_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.CreateRMQR(""));
    }

    [Fact]
    public void CreateRMQR_ECLevel_L_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateRMQR("AB", ErrorCorrectionLevel.L));
    }

    [Fact]
    public void CreateRMQR_ECLevel_Q_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateRMQR("AB", ErrorCorrectionLevel.Q));
    }

    // ───── Determinism ─────

    [Fact]
    public void CreateRMQR_Deterministic()
    {
        var qr1 = QRCode.CreateRMQR("TEST", ErrorCorrectionLevel.M);
        var qr2 = QRCode.CreateRMQR("TEST", ErrorCorrectionLevel.M);

        Assert.Equal(qr1.Width, qr2.Width);
        Assert.Equal(qr1.Height, qr2.Height);
        for (var row = 0; row < qr1.Height; row++)
        {
            for (var col = 0; col < qr1.Width; col++)
            {
                Assert.Equal(qr1[row, col], qr2[row, col]);
            }
        }
    }

    // ───── Renderers work with rMQR ─────

    [Fact]
    public void CreateRMQR_ToSvg_ProducesValidRectangularSvg()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 });

        Assert.StartsWith("<svg ", svg);
        Assert.Contains($"viewBox=\"0 0 {qr.Width} {qr.Height}\"", svg);
        Assert.EndsWith("</svg>", svg);
    }

    [Fact]
    public void CreateRMQR_ToConsoleString_ProducesOutput()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var text = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0 });

        Assert.NotEmpty(text);
    }

    private static string RenderAsText(QRCode qr)
    {
        var sb = new StringBuilder();
        for (var row = 0; row < qr.Height; row++)
        {
            if (row > 0)
            {
                sb.AppendLine();
            }

            for (var col = 0; col < qr.Width; col++)
            {
                sb.Append(qr[row, col] ? '#' : '.');
            }
        }

        return sb.ToString();
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void CreateRMQR_ReportsTheErrorCorrectionLevel(ErrorCorrectionLevel ecLevel)
    {
        Assert.Equal(ecLevel, QRCode.CreateRMQR("HELLO", ecLevel).ErrorCorrectionLevel);
    }

    // ───── Size constraints ─────

    public static TheoryData<int, int> AllSizes() => new()
    {
        { 7, 43 }, { 7, 59 }, { 7, 77 }, { 7, 99 }, { 7, 139 },
        { 9, 43 }, { 9, 59 }, { 9, 77 }, { 9, 99 }, { 9, 139 },
        { 11, 27 }, { 11, 43 }, { 11, 59 }, { 11, 77 }, { 11, 99 }, { 11, 139 },
        { 13, 27 }, { 13, 43 }, { 13, 59 }, { 13, 77 }, { 13, 99 }, { 13, 139 },
        { 15, 43 }, { 15, 59 }, { 15, 77 }, { 15, 99 }, { 15, 139 },
        { 17, 43 }, { 17, 59 }, { 17, 77 }, { 17, 99 }, { 17, 139 },
    };

    [Theory]
    [MemberData(nameof(AllSizes))]
    public void CreateRMQR_ExactSize(int height, int width)
    {
        var qr = QRCode.CreateRMQR("1", ErrorCorrectionLevel.H, new RMQRCodeOptions { Height = height, Width = width });

        Assert.Equal((height, width), (qr.Height, qr.Width));
    }

    [Fact]
    public void CreateRMQR_MaximumSize_UsesTheSmallestAreaThatFits()
    {
        // R11x27 has the smallest area, R7x43 is the smallest symbol 7 modules high
        var automatic = QRCode.CreateRMQR("12345");
        Assert.Equal((11, 27), (automatic.Height, automatic.Width));
        var low = QRCode.CreateRMQR("12345", ErrorCorrectionLevel.M, new RMQRCodeOptions { MaximumHeight = 8 });
        Assert.Equal((7, 43), (low.Height, low.Width));
        var narrow = QRCode.CreateRMQR(new string('9', 30), ErrorCorrectionLevel.M, new RMQRCodeOptions { MaximumWidth = 43 });
        Assert.True(narrow.Width <= 43);
        var tall = QRCode.CreateRMQR("12345", ErrorCorrectionLevel.M, new RMQRCodeOptions { Height = 15 });
        Assert.Equal((15, 43), (tall.Height, tall.Width));
    }

    [Fact]
    public void CreateRMQR_DataDoesNotFitTheAllowedSizes_Throws()
    {
        // R7x43-M holds 12 digits
        var options = new RMQRCodeOptions { Height = 7, Width = 43 };
        var full = QRCode.CreateRMQR("123456789012", ErrorCorrectionLevel.M, options);
        Assert.Equal((7, 43), (full.Height, full.Width));
        var exception = Assert.Throws<InvalidOperationException>(() => QRCode.CreateRMQR("1234567890123", ErrorCorrectionLevel.M, options));
        Assert.Equal("The data is too long to be encoded in an rMQR code of the allowed sizes (height 7, width 43).", exception.Message);
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateRMQR(new string('9', 500), ErrorCorrectionLevel.M, new RMQRCodeOptions { MaximumWidth = 59 }));
    }

    [Theory]
    [InlineData(7, 27)]
    [InlineData(17, 27)]
    public void CreateRMQR_NoSuchSize_Throws(int height, int width)
    {
        Assert.Throws<ArgumentException>("options", () => QRCode.CreateRMQR("1", ErrorCorrectionLevel.M, new RMQRCodeOptions { Height = height, Width = width }));
    }

    [Fact]
    public void CreateRMQR_NoSizeWithinTheMaximums_Throws()
    {
        Assert.Throws<ArgumentException>("options", () => QRCode.CreateRMQR("1", ErrorCorrectionLevel.M, new RMQRCodeOptions { MaximumHeight = 9, MaximumWidth = 30 }));
        Assert.Throws<ArgumentException>("options", () => QRCode.CreateRMQR("1", ErrorCorrectionLevel.M, new RMQRCodeOptions { Height = 15, MaximumWidth = 30 }));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(19)]
    public void RMQRCodeOptions_InvalidHeight_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(nameof(value), () => new RMQRCodeOptions { Height = value });
    }

    [Theory]
    [InlineData(26)]
    [InlineData(28)]
    [InlineData(140)]
    public void RMQRCodeOptions_InvalidWidth_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(nameof(value), () => new RMQRCodeOptions { Width = value });
    }

    [Fact]
    public void RMQRCodeOptions_MaximumBelowTheSmallestSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RMQRCodeOptions { MaximumHeight = 6 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RMQRCodeOptions { MaximumWidth = 26 });
    }

    // ───── Decoding ─────

    public static TheoryData<int, int, ErrorCorrectionLevel> AllSizesAndLevels()
    {
        var data = new TheoryData<int, int, ErrorCorrectionLevel>();
        foreach (var row in AllSizes())
        {
            data.Add(row.Data.Item1, row.Data.Item2, ErrorCorrectionLevel.M);
            data.Add(row.Data.Item1, row.Data.Item2, ErrorCorrectionLevel.H);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllSizesAndLevels))]
    public void Decode_RoundTrips(int height, int width, ErrorCorrectionLevel ecLevel)
    {
        var options = new RMQRCodeOptions { Height = height, Width = width };

        // The longest text of digits, letters and kanji that the size holds
        var text = "";
        for (var length = 1; ; length++)
        {
            var candidate = string.Concat(Enumerable.Range(0, length).Select(i => "0123456789AZaz日"[i % 15]));
            try
            {
                QRCode.CreateRMQR(candidate, ecLevel, options);
                text = candidate;
            }
            catch (InvalidOperationException)
            {
                break;
            }
        }

        var result = QRCodeDecoder.Decode(QRCode.CreateRMQR(text, ecLevel, options));

        Assert.Equal((QRCodeType.RMQR, ecLevel, text, (int?)null), (result.Type, result.ErrorCorrectionLevel, result.Text, result.MaskPattern));
    }

    [Fact]
    public void Decode_CorrectsErrors()
    {
        var qr = QRCode.CreateRMQR("https://www.meziantou.net", ErrorCorrectionLevel.H);
        var modules = new bool[qr.Height, qr.Width];
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                modules[row, column] = qr[row, column];
            }
        }

        modules[qr.Height / 2, 15] = !modules[qr.Height / 2, 15];

        var result = QRCodeDecoder.Decode(modules);

        Assert.Equal("https://www.meziantou.net", result.Text);
        Assert.Equal(1, result.CorrectedErrorCount);
    }
}
