using Meziantou.Framework.InlineSnapshotTesting;
using System.Text;
using Xunit;

namespace Meziantou.Framework.Tests;

public class RMQRCodeTests
{
    [Fact]
    public void CreateRMQR_M_Short()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        Assert.True(qr.Width > qr.Height);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.....#.#######.#.#
            #.....#.##.#.....#...###.#.
            #.###.#...#.###..##..#.#..#
            #.###.#.##.###.##..#...#.#.
            #.###.#...#..#####...#.#..#
            #.....#.##.#.#.#.#.#.#.#.##
            #######.#.#.######..#.#.#.#
            .........#.##.#.##.#.##...#
            #.#.#.#.#.#..##..#..#.#.#.#
            .#.#.#.#.#.#.#...#.####...#
            #####.#.#.#.###.###.###.#.#
            """);
    }

    [Fact]
    public void CreateRMQR_H_Short()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.H);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#####.###.#################.#.#.#.#
            #.....#..#..##..#######.##.#...#..#..#.#...
            #.###.#..........#..#.#.#.#..#.##.###..##.#
            #.###.#..##.##.#..#..#...#.#..###.#..#..##.
            #.###.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#
            #.....#..##..###.##.###.....##.##..#..#...#
            #######.#.###.#..##....#######...#..###.#.#
            .........#.##...######..#...#.##..#####...#
            #.#################.###.#####.###.#.#.#.#.#
            """);
    }

    [Fact]
    public void CreateRMQR_Numeric()
    {
        var qr = QRCode.CreateRMQR("12345", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#.....#.#######.#.#
            #.....#.##.#.....##..#.###.
            #.###.#...#.#...#####.....#
            #.###.#.##.#....#.#..##....
            #.###.#...#.#######.#####.#
            #.....#.##.#.#.#.#.#.#.#.##
            #######.#.#.#.####..###.#.#
            .........#.##.#.#######...#
            #.#.#.#.#.#.##.##.....#.#.#
            .#.#.#.#.#.#.#..#.###.#...#
            #####.#.#.#.###.###.###.#.#
            """);
    }

    [Fact]
    public void CreateRMQR_Byte()
    {
        var qr = QRCode.CreateRMQR("hello", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        Assert.True(qr.Width > qr.Height);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.#############.###.#.#######.#.#.#.#
            #.....#.########..##.#.#.#..#..#.#..######.
            #.###.#...#.##...#.##...###.#.#..#.#..##.##
            #.###.#......###..#.##....#....##..###..##.
            #.###.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#
            #.....#..###.##.#.#.###..##..#.#..##..#...#
            #######.#.#.#..###.##....#..#..#..##..#.#.#
            .........#.####...#.##...####....##..##...#
            #.#######.#.###.#############.###.#.###.#.#
            """);
    }

    [Fact]
    public void CreateRMQR_Url()
    {
        var qr = QRCode.CreateRMQR("https://example.com", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.RMQR, qr.Type);
        Assert.Equal(17, qr.Height);
        Assert.Equal(43, qr.Width);
        InlineSnapshot.Validate(RenderAsText(qr), """
            #######.###.#####.#.#.#.#####.#.#.#.#.#.#.#
            #.....#.##.#.#.#.#####.#.#######..#...###..
            #.###.#.#.#.#.#..##....#...#.#.....##.#...#
            #.###.#.##.#.#########......##.#.####.##.#.
            #.###.#...#.#.#.##.##...#.#.####.###.#.####
            #.....#.##.#.##.########.###..#...##..##...
            #######.#.#.##.####...........##....#.#...#
            .........#.#..#####.##..####..##.###..###..
            #.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#.#
            .#.#.#.#.#.#.....#.#.###.###.#...##...#..#.
            #.#.#.#.#.#.##.#..#.#....###.##..###.##..##
            .#.#.#.#.#.#...#..#..#....#.#.#.##.#.#.###.
            #.#.#.#.#.#.#.#...##...###.#.........##.#.#
            .#.#.#.#.#.#.#.##.####.##.###...#....##...#
            #.#.#.#.#.#.#.#...#....####.##.####..##.#.#
            .#.#.#.#.#.#.#.#.#.#.#.##.#.###.#######...#
            #.#.#.#.#####.#.#.#.#####.#####.#######.#.#
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
        Assert.Throws<ArgumentOutOfRangeException>(() => QRCode.CreateRMQR("AB", ErrorCorrectionLevel.L));
    }

    [Fact]
    public void CreateRMQR_ECLevel_Q_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QRCode.CreateRMQR("AB", ErrorCorrectionLevel.Q));
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

        Assert.StartsWith("<svg ", svg, StringComparison.Ordinal);
        Assert.Contains($"viewBox=\"0 0 {qr.Width} {qr.Height}\"", svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", svg, StringComparison.Ordinal);
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
}
