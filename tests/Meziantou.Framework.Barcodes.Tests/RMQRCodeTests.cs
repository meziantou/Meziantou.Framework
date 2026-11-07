using Meziantou.Framework.InlineSnapshotTesting;

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

    // ───── Error cases ─────

    [Fact]
    public void CreateRMQR_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QRCode.CreateRMQR(null!));
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
}
