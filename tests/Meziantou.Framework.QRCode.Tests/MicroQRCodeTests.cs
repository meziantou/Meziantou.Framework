using Meziantou.Framework.SnapshotTesting;

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
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Version M2 (13x13, numeric + alphanumeric) ─────

    [Fact]
    public void CreateMicroQR_M2L_Numeric()
    {
        var qr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Assert.Equal(11, qr.Width);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void CreateMicroQR_M2M_Alphanumeric()
    {
        var qr = QRCode.CreateMicroQR("AB", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.MicroQR, qr.Type);
        Assert.Equal(2, qr.Version);
        Assert.Equal(13, qr.Width);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Version M3 (15x15, all modes) ─────

    [Fact]
    public void CreateMicroQR_M3L_Byte()
    {
        var qr = QRCode.CreateMicroQR("hello", ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(15, qr.Width);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void CreateMicroQR_M3M_Alphanumeric()
    {
        var qr = QRCode.CreateMicroQR("HELLO", ErrorCorrectionLevel.M);

        Assert.Equal(QRCodeType.MicroQR, qr.Type);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Version M4 (17x17, all modes + EC Q) ─────

    [Fact]
    public void CreateMicroQR_M4L_Byte()
    {
        var qr = QRCode.CreateMicroQR("Hello World!", ErrorCorrectionLevel.L);

        Assert.Equal(4, qr.Version);
        Assert.Equal(17, qr.Width);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void CreateMicroQR_M4Q_Numeric()
    {
        var qr = QRCode.CreateMicroQR("12345678901", ErrorCorrectionLevel.Q);

        Assert.Equal(4, qr.Version);
        Assert.Equal(17, qr.Width);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
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

    [Theory]
    [InlineData("ABCDEF")]
    [InlineData("12345")]
    [InlineData("1")]
    public void CreateMicroQR_ECLevelH_Throws(string data)
    {
        // No Micro QR version supports H. This used to report "the data is too long", and for
        // data small enough to fit M1 it silently returned a symbol with no error correction.
        Assert.Throws<ArgumentOutOfRangeException>(() => QRCode.CreateMicroQR(data, ErrorCorrectionLevel.H));
    }

    [Fact]
    public void CreateMicroQR_M1_ReportsTheLevelItActuallyUsed()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Assert.Equal(ErrorCorrectionLevel.L, qr.ErrorCorrectionLevel);
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    public void CreateMicroQR_LevelStrongerThanM1_SkipsM1(ErrorCorrectionLevel ecLevel)
    {
        // "123" fits M1, but M1 carries error detection only, so a request for M or Q has to
        // move up to a version that genuinely provides it.
        var qr = QRCode.CreateMicroQR("123", ecLevel);

        Assert.True(qr.Version > 1);
        Assert.Equal(ecLevel, qr.ErrorCorrectionLevel);
    }

    [Fact]
    public void CreateMicroQR_ReportsTheRequestedLevel()
    {
        Assert.Equal(ErrorCorrectionLevel.M, QRCode.CreateMicroQR("HELLO", ErrorCorrectionLevel.M).ErrorCorrectionLevel);
        Assert.Equal(ErrorCorrectionLevel.Q, QRCode.CreateMicroQR("12345678901", ErrorCorrectionLevel.Q).ErrorCorrectionLevel);
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

        Assert.StartsWith("<svg ", svg);
        Assert.Contains("viewBox=\"0 0 11 11\"", svg);
        Assert.EndsWith("</svg>", svg);
    }

    [Fact]
    public void CreateMicroQR_ToConsoleString_ProducesOutput()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0 });

        Assert.NotEmpty(text);
    }

    private static string RenderAsSvg(QRCode qr)
    {
        return qr.ToSvg(new QRCodeSvgOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
        });
    }
}
