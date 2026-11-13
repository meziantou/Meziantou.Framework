using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

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

    // ───── Mixed modes and segments ─────

    [Fact]
    public void Documentation_SmallSymbolSegments()
    {
        #region QRCode_SmallSymbolSegments
        // Micro QR and rMQR codes split a text into segments of different modes, like standard QR codes: the lowercase
        // letters in byte mode, the digits in numeric mode
        var microQr = QRCode.CreateMicroQR("abc12345678");
        var rmqr = QRCode.CreateRMQR("Order 12345678901234567890");
        Assert.Equal((3, 15, 15), (microQr.Version, microQr.Width, microQr.Height));
        Assert.Equal((43, 11), (rmqr.Width, rmqr.Height));

        // The same text in a single byte segment needs a larger symbol
        var microQrByte = QRCode.CreateMicroQR([QRCodeSegment.CreateByte("abc12345678")]);
        var rmqrByte = QRCode.CreateRMQR([QRCodeSegment.CreateByte("Order 12345678901234567890")]);
        Assert.Equal((4, 17, 17), (microQrByte.Version, microQrByte.Width, microQrByte.Height));
        Assert.Equal((43, 13), (rmqrByte.Width, rmqrByte.Height));
        #endregion
    }

    [Fact]
    public void CreateMicroQR_MixedModes_Snapshot()
    {
        // The lowercase letters in byte mode, the digits in numeric mode; large modules and a quiet zone, so that the snapshot can be scanned
        var qr = QRCode.CreateMicroQR("abc12345678");

        Snapshot.Validate(qr.ToPng(new QRCodePngOptions { ModuleSize = 8, QuietZoneModules = 2 }), SnapshotType.Png);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("HELLO")]
    [InlineData("hello")]
    [InlineData("漢字")]
    [InlineData("\u00E9t\u00E9 2026")]
    public void CreateMicroQR_SingleMode_WhenSegmentsAreNotSmaller(string data)
    {
        // A text with characters other than ASCII and kanji stays in one byte segment, as readers guess the encoding of each segment
        QRCodeSegment segment = data switch
        {
            "12345" => QRCodeSegment.CreateNumeric(data),
            "HELLO" => QRCodeSegment.CreateAlphanumeric(data),
            "漢字" => QRCodeSegment.CreateKanji(data),
            _ => QRCodeSegment.CreateByte(data),
        };

        Assert.Equal(RenderAsSvg(QRCode.CreateMicroQR(data)), RenderAsSvg(QRCode.CreateMicroQR([segment])));
    }

    [Fact]
    public void CreateMicroQR_MixedModes_UseTheModesOfTheVersion()
    {
        // M2-L holds 40 bits: "A1234567" takes 39 bits in two segments (10 + 29), 48 bits in alphanumeric mode
        Assert.Equal(2, QRCode.CreateMicroQR("A1234567").Version);
        Assert.Equal(2, QRCode.CreateMicroQR([QRCodeSegment.CreateAlphanumeric("A"), QRCodeSegment.CreateNumeric("1234567")]).Version);
        Assert.Equal(3, QRCode.CreateMicroQR([QRCodeSegment.CreateAlphanumeric("A1234567")]).Version);

        // M2 has no byte mode: digits and uppercase letters only
        Assert.Equal(3, QRCode.CreateMicroQR("a1234567").Version);
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR("a1234567", ErrorCorrectionLevel.L, new MicroQRCodeOptions { MaximumVersion = 2 }));
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR([QRCodeSegment.CreateByte("a")], ErrorCorrectionLevel.L, new MicroQRCodeOptions { MaximumVersion = 2 }));
    }

    [Fact]
    public void CreateMicroQR_M1_HoldsASingleNumericSegment()
    {
        Assert.Equal(1, QRCode.CreateMicroQR([QRCodeSegment.CreateNumeric("12345")]).Version);
        Assert.Equal(2, QRCode.CreateMicroQR([QRCodeSegment.CreateNumeric("12"), QRCodeSegment.CreateNumeric("345")]).Version);
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR([QRCodeSegment.CreateNumeric("1"), QRCodeSegment.CreateNumeric("2")], ErrorCorrectionLevel.L, new MicroQRCodeOptions { MaximumVersion = 1 }));
    }

    [Fact]
    public void CreateMicroQR_Segments_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("segments", () => QRCode.CreateMicroQR((IEnumerable<QRCodeSegment>)null!));
        Assert.Throws<ArgumentException>("segments", () => QRCode.CreateMicroQR(Array.Empty<QRCodeSegment>()));
        Assert.Throws<ArgumentException>("segments", () => QRCode.CreateMicroQR([QRCodeSegment.CreateEci(26), QRCodeSegment.CreateNumeric("1")]));
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateMicroQR([QRCodeSegment.CreateNumeric("1")], ErrorCorrectionLevel.H));
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR([QRCodeSegment.CreateNumeric(new string('1', 36))]));
    }

    // ───── Error cases ─────

    [Fact]
    public void CreateMicroQR_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QRCode.CreateMicroQR((string)null!));
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
        Assert.Throws<ArgumentOutOfRangeException>("errorCorrectionLevel", () => QRCode.CreateMicroQR(data, ErrorCorrectionLevel.H));
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

    // ───── Size constraints ─────

    [Theory]
    [InlineData(1, 11)]
    [InlineData(2, 13)]
    [InlineData(3, 15)]
    [InlineData(4, 17)]
    public void CreateMicroQR_ExactVersion(int version, int size)
    {
        var qr = QRCode.CreateMicroQR("12", ErrorCorrectionLevel.L, new MicroQRCodeOptions { MinimumVersion = version, MaximumVersion = version });

        Assert.Equal((version, size, size), (qr.Version, qr.Width, qr.Height));
    }

    [Fact]
    public void CreateMicroQR_VersionRange_UsesTheSmallestVersionThatHoldsTheData()
    {
        var options = new MicroQRCodeOptions { MinimumVersion = 2, MaximumVersion = 3 };

        Assert.Equal(2, QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.M, options).Version);
        Assert.Equal(3, QRCode.CreateMicroQR("hello", ErrorCorrectionLevel.M, options).Version);
    }

    [Theory]
    [InlineData(1, 1, ErrorCorrectionLevel.M)]
    [InlineData(1, 3, ErrorCorrectionLevel.Q)]
    [InlineData(1, 1, ErrorCorrectionLevel.Q)]
    public void CreateMicroQR_NoVersionSupportsTheErrorCorrectionLevel_Throws(int minVersion, int maxVersion, ErrorCorrectionLevel level)
    {
        var options = new MicroQRCodeOptions { MinimumVersion = minVersion, MaximumVersion = maxVersion };

        Assert.Throws<ArgumentException>("options", () => QRCode.CreateMicroQR("1", level, options));
    }

    [Fact]
    public void CreateMicroQR_DataDoesNotFitTheAllowedVersions_Throws()
    {
        var options = new MicroQRCodeOptions { MaximumVersion = 2 };

        // M2-L holds 10 digits, and no lowercase letters
        Assert.Equal(2, QRCode.CreateMicroQR("0123456789", ErrorCorrectionLevel.L, options).Version);
        var exception = Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR("01234567890", ErrorCorrectionLevel.L, options));
        Assert.Equal("The data is too long to be encoded in a Micro QR code of version M1 to M2.", exception.Message);
        Assert.Throws<InvalidOperationException>(() => QRCode.CreateMicroQR("a", ErrorCorrectionLevel.L, options));
    }

    [Fact]
    public void CreateMicroQR_MinimumVersionGreaterThanMaximumVersion_Throws()
    {
        Assert.Throws<ArgumentException>("options", () => QRCode.CreateMicroQR("1", ErrorCorrectionLevel.L, new MicroQRCodeOptions { MinimumVersion = 3, MaximumVersion = 2 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void MicroQRCodeOptions_InvalidVersion_Throws(int value)
    {
        var options = new MicroQRCodeOptions();

        Assert.Throws<ArgumentOutOfRangeException>(nameof(value), () => options.MinimumVersion = value);
        Assert.Throws<ArgumentOutOfRangeException>(nameof(value), () => options.MaximumVersion = value);
    }

    // ───── Decoding ─────

    [Theory]
    [InlineData(1, ErrorCorrectionLevel.L, "12345")]
    [InlineData(1, ErrorCorrectionLevel.L, "0")]
    [InlineData(2, ErrorCorrectionLevel.L, "0123456789")]
    [InlineData(2, ErrorCorrectionLevel.L, "AB-CD")]
    [InlineData(2, ErrorCorrectionLevel.M, "HELLO")]
    [InlineData(3, ErrorCorrectionLevel.L, "hello")]
    [InlineData(3, ErrorCorrectionLevel.M, "日本")]
    [InlineData(3, ErrorCorrectionLevel.L, "12345678901234567890123")]
    [InlineData(4, ErrorCorrectionLevel.L, "Hello, World!")]
    [InlineData(4, ErrorCorrectionLevel.M, "AB12日本ab")]
    [InlineData(4, ErrorCorrectionLevel.Q, "MICRO QR")]
    public void Decode_RoundTrips(int version, ErrorCorrectionLevel ecLevel, string text)
    {
        var qr = QRCode.CreateMicroQR(text, ecLevel, new MicroQRCodeOptions { MinimumVersion = version, MaximumVersion = version });

        var result = QRCodeDecoder.Decode(qr);

        Assert.Equal((QRCodeType.MicroQR, version, ecLevel, text, 0), (result.Type, result.Version, result.ErrorCorrectionLevel, result.Text, result.CorrectedErrorCount));
        Assert.True(result.MaskPattern is >= 0 and <= 3);
    }

    [Theory]
    [InlineData(1, ErrorCorrectionLevel.L)]
    [InlineData(2, ErrorCorrectionLevel.L)]
    [InlineData(2, ErrorCorrectionLevel.M)]
    [InlineData(3, ErrorCorrectionLevel.L)]
    [InlineData(3, ErrorCorrectionLevel.M)]
    [InlineData(4, ErrorCorrectionLevel.L)]
    [InlineData(4, ErrorCorrectionLevel.M)]
    [InlineData(4, ErrorCorrectionLevel.Q)]
    public void Decode_FullCapacity(int version, ErrorCorrectionLevel ecLevel)
    {
        var options = new MicroQRCodeOptions { MinimumVersion = version, MaximumVersion = version };
        var text = "";
        for (var length = 1; ; length++)
        {
            var candidate = string.Concat(Enumerable.Range(0, length).Select(i => (char)('0' + (i % 10))));
            try
            {
                QRCode.CreateMicroQR(candidate, ecLevel, options);
                text = candidate;
            }
            catch (InvalidOperationException)
            {
                break;
            }
        }

        Assert.Equal(text, QRCodeDecoder.Decode(QRCode.CreateMicroQR(text, ecLevel, options)).Text);
    }

    [Fact]
    public void Decode_CorrectsErrors()
    {
        var qr = QRCode.CreateMicroQR("HELLO WORLD", ErrorCorrectionLevel.M, new MicroQRCodeOptions { MinimumVersion = 4, MaximumVersion = 4 });
        var modules = new bool[qr.Height, qr.Width];
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                modules[row, column] = qr[row, column];
            }
        }

        // The bottom-right corner holds the first codeword
        modules[16, 16] = !modules[16, 16];
        modules[15, 15] = !modules[15, 15];

        var result = QRCodeDecoder.Decode(modules);

        Assert.Equal("HELLO WORLD", result.Text);
        Assert.Equal(1, result.CorrectedErrorCount);
    }

    [Fact]
    public void Decode_M1_DetectsErrorsOnly()
    {
        var qr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);
        var modules = new bool[qr.Height, qr.Width];
        for (var row = 0; row < qr.Height; row++)
        {
            for (var column = 0; column < qr.Width; column++)
            {
                modules[row, column] = qr[row, column];
            }
        }

        modules[10, 10] = !modules[10, 10];

        Assert.False(QRCodeDecoder.TryDecode(modules, out _));
    }
}
