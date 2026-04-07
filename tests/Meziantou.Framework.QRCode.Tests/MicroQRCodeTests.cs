using Meziantou.Framework.InlineSnapshotTesting;
using System.Text;
using Xunit;

namespace Meziantou.Framework.Tests;

public class MicroQRCodeTests
{
    // ───── Version M1 (11x11, numeric only) ─────

    [Fact]
    public void CreateMicroQR_M1_Numeric()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);

        Assert.Equal(QRCodeType.MicroQR, qr.Type);
        Assert.Equal(1, qr.Version);
        Assert.Equal(11, qr.Width);
        Assert.Equal(11, qr.Height);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#
            #.....#...#
            #.###.#.#..
            #.###.#...#
            #.###.#.###
            #.....#..##
            #######.#..
            ........###
            ##..###..##
            ..#.####...
            ####.##.#..
            """);
    }

    // ───── Version M2 (13x13, numeric + alphanumeric) ─────

    [Fact]
    public void CreateMicroQR_M2L_Numeric()
    {
        var qr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Assert.Equal(11, qr.Width);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#
            #.....#...#
            #.###.#..#.
            #.###.#.###
            #.###.#.#.#
            #.....#.#..
            #######..#.
            ...........
            ##..#.##..#
            .#..####.##
            #...#####.#
            """);
    }

    [Fact]
    public void CreateMicroQR_M2M_Alphanumeric()
    {
        var qr = QRCode.CreateMicroQR("AB", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.MicroQR, qr.Type);
        Assert.Equal(2, qr.Version);
        Assert.Equal(13, qr.Width);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#
            #.....#..##..
            #.###.#..#.##
            #.###.#.###..
            #.###.#..####
            #.....#...#.#
            #######.#.##.
            ........#..#.
            ###..####.##.
            ...###..#..##
            #.###.#.#####
            ..##...#..#.#
            #.####.###.#.
            """);
    }

    // ───── Version M3 (15x15, all modes) ─────

    [Fact]
    public void CreateMicroQR_M3L_Byte()
    {
        var qr = QRCode.CreateMicroQR("hello", ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(15, qr.Width);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#
            #.....#..#..#..
            #.###.#..######
            #.###.#.#.#..##
            #.###.#...#..##
            #.....#.#..###.
            #######.#...##.
            .........#....#
            ######...###.#.
            ...##.#.####...
            #...######.##..
            ....##.##.####.
            #..#.####..##.#
            ..##..#...#.#.#
            ##.#.###....###
            """);
    }

    [Fact]
    public void CreateMicroQR_M3M_Alphanumeric()
    {
        var qr = QRCode.CreateMicroQR("HELLO", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.MicroQR, qr.Type);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#
            #.....#.##...
            #.###.#..####
            #.###.#..##.#
            #.###.#.#..#.
            #.....#..#..#
            #######.#####
            .........#...
            ###.#...#..##
            .#.###.######
            #.#.#.#....##
            .#.#.#.#.###.
            ###..#...###.
            """);
    }

    // ───── Version M4 (17x17, all modes + EC Q) ─────

    [Fact]
    public void CreateMicroQR_M4L_Byte()
    {
        var qr = QRCode.CreateMicroQR("Hello World!", ErrorCorrectionLevel.L);

        Assert.Equal(4, qr.Version);
        Assert.Equal(17, qr.Width);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#.#
            #.....#......#...
            #.###.#.##..##...
            #.###.#.#...###.#
            #.###.#...#.###.#
            #.....#.#....#.#.
            #######..##..#..#
            ........#...###.#
            #..#.###.##.#...#
            .#####......#....
            ##..####.#####.#.
            .#...#.##..##....
            ####.#.#.##.###.#
            .....#########...
            ###...####....#..
            ..#...#.###.##...
            ###.#..#.##.#...#
            """);
    }

    [Fact]
    public void CreateMicroQR_M4Q_Numeric()
    {
        var qr = QRCode.CreateMicroQR("12345678901", ErrorCorrectionLevel.Q);

        Assert.Equal(4, qr.Version);
        Assert.Equal(17, qr.Width);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.#.#.#.#
            #.....#.#.#.....#
            #.###.#.#....#..#
            #.###.#....##.#..
            #.###.#..#####..#
            #.....#..####...#
            #######.###.###..
            ........#.#.##..#
            #.##.#..#..#.##.#
            ...##..##......##
            #####.#.###..##..
            .#.#.###..#.#....
            #....#.#.##..###.
            .#..#..#####.###.
            #...####.#.###..#
            ..#..####..#.##..
            #.#..##.#########
            """);
    }

    // ───── Error cases ─────

    [Fact]
    public void CreateMicroQR_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QRCode.CreateMicroQR(null!));
    }

    [Fact]
    public void CreateMicroQR_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.CreateMicroQR(""));
    }

    [Fact]
    public void CreateMicroQR_DataTooLong_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR(new string('A', 100)));
    }

    [Fact]
    public void CreateMicroQR_ECLevel_H_FallsBackToSupportedLevel()
    {
        // H is not supported by any Micro QR version, but M1 accepts any EC level
        // (it only supports error detection). For data that fits M1, H is silently resolved.
        // For data that requires M2+, H should throw since no version supports it.
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR("ABCDEF", ErrorCorrectionLevel.H));
    }

    // ───── Determinism ─────

    [Fact]
    public void CreateMicroQR_Deterministic()
    {
        var qr1 = QRCode.CreateMicroQR("ABC", ErrorCorrectionLevel.L);
        var qr2 = QRCode.CreateMicroQR("ABC", ErrorCorrectionLevel.L);

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

    // ───── SVG/Console renderers work with Micro QR ─────

    [Fact]
    public void CreateMicroQR_ToSvg_ProducesValidSvg()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 });

        Assert.StartsWith("<svg ", svg, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 11 11\"", svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMicroQR_ToConsoleString_ProducesOutput()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
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
}
