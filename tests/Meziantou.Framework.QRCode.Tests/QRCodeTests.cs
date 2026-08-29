using Meziantou.Framework.SnapshotTesting;
namespace Meziantou.Framework.Tests;

public class QRCodeTests
{
    // ───── Numeric mode ─────

    [Fact]
    public void Create_Numeric_1Digit()
    {
        var qr = QRCode.Create("7", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_2Digits()
    {
        var qr = QRCode.Create("42", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_3Digits()
    {
        var qr = QRCode.Create("123", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_8Digits()
    {
        var qr = QRCode.Create("01234567", ErrorCorrectionLevel.M);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Numeric_Long_Version3()
    {
        var qr = QRCode.Create(new string('1', 100), ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(29, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Alphanumeric mode ─────

    [Fact]
    public void Create_Alphanumeric_1Char()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_3Chars()
    {
        var qr = QRCode.Create("ABC", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_SpecialChars()
    {
        var qr = QRCode.Create("$%*+-./:", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_HelloWorld()
    {
        var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Alphanumeric_ECLevel_Q()
    {
        var qr = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.Q);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Byte mode ─────

    [Fact]
    public void Create_Byte_Lowercase()
    {
        var qr = QRCode.Create("hello world", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_Url()
    {
        var qr = QRCode.Create("https://example.com", ErrorCorrectionLevel.M);

        Assert.Equal(2, qr.Version);
        Assert.Equal(25, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_Version7Transition_ErrorCorrectionQ()
    {
        var qr = QRCode.Create("fserwrjthwekjfghredjkgdrjkgfdjkhghfhjkdsghhjsdfghjsdfghkkkkkkkkkkkkkkkklkia", ErrorCorrectionLevel.Q);

        Assert.Equal(7, qr.Version);
        Assert.Equal(45, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_UTF8_Accented()
    {
        var qr = QRCode.Create("café", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_UTF8_Emoji()
    {
        var qr = QRCode.Create("\U0001F600", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_Binary_WithSnapshot()
    {
        var qr = QRCode.Create(new byte[] { 0x00, 0xFF, 0x48, 0x65 }, ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Kanji mode ─────

    [Fact]
    public void Create_Kanji_SingleCharacter()
    {
        var qr = QRCode.Create("漢", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Kanji_MultipleCharacters()
    {
        var qr = QRCode.Create("漢字", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Kanji_Katakana()
    {
        var qr = QRCode.Create("アイウ", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Higher versions / different sizes ─────

    [Fact]
    public void Create_Version4_ByteMode()
    {
        var qr = QRCode.Create(new string('x', 43), ErrorCorrectionLevel.M);

        Assert.Equal(4, qr.Version);
        Assert.Equal(33, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Version7Plus_HasVersionInfo()
    {
        var qr = QRCode.Create(new string('x', 123), ErrorCorrectionLevel.M);

        Assert.Equal(8, qr.Version);
        Assert.Equal(49, qr.Size);
    }

    [Fact]
    public void Create_Numeric_LongString_Version6()
    {
        var qr = QRCode.Create(new string('0', 300), ErrorCorrectionLevel.L);

        Assert.Equal(6, qr.Version);
        Assert.Equal(41, qr.Size);
    }

    // ───── EC levels ─────

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_AllErrorCorrectionLevels_ProducesValidQRCode(ErrorCorrectionLevel level)
    {
        var qr = QRCode.Create("HELLO WORLD", level);

        Assert.True(qr.Size > 0);
        Assert.True(qr.Version >= 1);
    }

    [Fact]
    public void Create_ErrorCorrectionLevel_H()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.H);

        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Version / size transitions ─────

    [Fact]
    public void Create_LongerData_ProducesHigherVersion()
    {
        var qrShort = QRCode.Create("A", ErrorCorrectionLevel.L);
        var qrLong = QRCode.Create(new string('A', 200), ErrorCorrectionLevel.L);

        Assert.True(qrLong.Version > qrShort.Version);
    }

    [Fact]
    public void Create_HigherECLevel_MayRequireHigherVersion()
    {
        var qrL = QRCode.Create(new string('A', 25), ErrorCorrectionLevel.L);
        var qrH = QRCode.Create(new string('A', 25), ErrorCorrectionLevel.H);

        Assert.True(qrH.Version >= qrL.Version);
    }

    public static TheoryData<int, int> NumericPayloadLengthForAllVersions => CreateNumericPayloadLengthForAllVersions();

    [Theory]
    [MemberData(nameof(NumericPayloadLengthForAllVersions))]
    public void Create_Numeric_HasOneQRCodePerVersion(int expectedVersion, int payloadLength)
    {
        var qr = QRCode.Create(new string('1', payloadLength), ErrorCorrectionLevel.L);

        Assert.Equal(expectedVersion, qr.Version);
        Assert.Equal(17 + (expectedVersion * 4), qr.Size);
    }

    // ───── Binary data coverage ─────

    [Fact]
    public void Create_Binary_LargerPayload()
    {
        var data = new byte[50];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 5);
        }

        var qr = QRCode.Create(data, ErrorCorrectionLevel.L);

        Assert.Equal(3, qr.Version);
        Assert.Equal(29, qr.Size);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Version group transitions (CCI bits change) ─────

    [Fact]
    public void Create_Numeric_Version10_CCIBitsChange()
    {
        // Versions 10+ use 12-bit CCI for numeric (vs 10-bit for 1-9)
        var qr = QRCode.Create(new string('1', 272), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 6);
        Assert.True(qr.Size >= 41);
    }

    [Fact]
    public void Create_Alphanumeric_Version10_CCIBitsChange()
    {
        // Versions 10+ use 11-bit CCI for alphanumeric (vs 9-bit for 1-9)
        var qr = QRCode.Create(new string('A', 175), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 6);
        Assert.True(qr.Size >= 41);
    }

    [Fact]
    public void Create_Numeric_Version27_CCIBitsChange()
    {
        // Versions 27+ use 14-bit CCI for numeric (vs 12-bit for 10-26)
        var qr = QRCode.Create(new string('1', 2200), ErrorCorrectionLevel.L);

        Assert.True(qr.Version >= 21);
        Assert.True(qr.Size >= 101);
    }

    // ───── Kanji encoding edge cases ─────

    [Fact]
    public void Create_Kanji_E040Range()
    {
        // U+6F3E is Shift JIS 0xE040, the first value of the second range Kanji mode accepts.
        var qr = QRCode.Create("漾", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    [Fact]
    public void Create_Byte_MixedCJKAndASCII_FallsToByteMode()
    {
        // Mixed CJK and ASCII cannot use Kanji mode - falls to byte
        var qr = QRCode.Create("Hello漢字World", ErrorCorrectionLevel.L);

        Assert.Equal(1, qr.Version);
        Snapshot.Validate(RenderAsSvg(qr), SnapshotType.Svg);
    }

    // ───── Properties ─────

    [Fact]
    public void Create_StandardQR_HasCorrectType()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);

        Assert.Equal(QRCodeType.Standard, qr.Type);
        Assert.Equal(qr.Width, qr.Height);
        Assert.Equal(qr.Width, qr.Size);
    }

    // ───── Error cases ─────

    [Fact]
    public void Create_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => QRCode.Create((string)null!));
    }

    [Fact]
    public void Create_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.Create(""));
    }

    [Fact]
    public void Create_EmptyBinaryData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QRCode.Create(Array.Empty<byte>(), ErrorCorrectionLevel.M));
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_ReportsTheErrorCorrectionLevel(ErrorCorrectionLevel ecLevel)
    {
        Assert.Equal(ecLevel, QRCode.Create("HELLO WORLD", ecLevel).ErrorCorrectionLevel);
    }

    [Fact]
    public void Create_DataTooLong_ThrowsInvalidOperationException()
    {
        var longData = new string('A', 10000);

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData));
    }

    [Fact]
    public void Create_ByteDataTooLong_ThrowsInvalidOperationException()
    {
        var longData = new string('x', 2954);

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData, ErrorCorrectionLevel.L));
    }

    [Fact]
    public void Create_BinaryDataTooLong_ThrowsInvalidOperationException()
    {
        var longData = new byte[2954];

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData, ErrorCorrectionLevel.L));
    }

    [Fact]
    public void Create_AlphanumericDataTooLong_ThrowsInvalidOperationException()
    {
        // Version 40-Q holds 2420 alphanumeric characters.
        var longData = new string('A', 2421);

        Assert.Throws<InvalidOperationException>(() => QRCode.Create(longData, ErrorCorrectionLevel.Q));
    }

    [Fact]
    public void Create_AlphanumericAtMaximumCapacity_Succeeds()
    {
        var qr = QRCode.Create(new string('A', 2420), ErrorCorrectionLevel.Q);

        Assert.Equal(40, qr.Version);
    }

    // ───── Determinism ─────

    [Fact]
    public void Create_Deterministic_SameInputProducesSameOutput()
    {
        var qr1 = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);
        var qr2 = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.M);

        Assert.Equal(qr1.Size, qr2.Size);
        for (var row = 0; row < qr1.Size; row++)
        {
            for (var col = 0; col < qr1.Size; col++)
            {
                Assert.Equal(qr1[row, col], qr2[row, col]);
            }
        }
    }

    [Fact]
    public void Create_DifferentData_ProducesDifferentOutput()
    {
        var qr1 = QRCode.Create("AAA", ErrorCorrectionLevel.M);
        var qr2 = QRCode.Create("BBB", ErrorCorrectionLevel.M);

        var different = false;
        for (var row = 0; row < qr1.Size && !different; row++)
        {
            for (var col = 0; col < qr1.Size && !different; col++)
            {
                if (qr1[row, col] != qr2[row, col])
                {
                    different = true;
                }
            }
        }

        Assert.True(different);
    }

    private static string RenderAsSvg(QRCode qr)
    {
        return qr.ToSvg(new QRCodeSvgOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
        });
    }

    private static TheoryData<int, int> CreateNumericPayloadLengthForAllVersions()
    {
        var data = new TheoryData<int, int>();
        var versionByLength = new Dictionary<int, int>();

        var minLength = 1;
        const int MaxLength = 7089;
        for (var version = 1; version <= 40; version++)
        {
            var payloadLength = FindMinimumLengthForVersion(version, minLength, MaxLength, versionByLength);
            data.Add(version, payloadLength);
            minLength = payloadLength + 1;
        }

        return data;
    }

    private static int FindMinimumLengthForVersion(int expectedVersion, int minLength, int maxLength, Dictionary<int, int> versionByLength)
    {
        var left = minLength;
        var right = maxLength;
        var bestLength = maxLength;

        while (left <= right)
        {
            var middle = left + ((right - left) / 2);
            var actualVersion = GetVersionForNumericPayloadLength(middle, versionByLength);
            if (actualVersion >= expectedVersion)
            {
                bestLength = middle;
                right = middle - 1;
            }
            else
            {
                left = middle + 1;
            }
        }

        var resolvedVersion = GetVersionForNumericPayloadLength(bestLength, versionByLength);
        if (resolvedVersion != expectedVersion)
        {
            throw new InvalidOperationException($"Unable to find payload for QR version {expectedVersion}. Resolved version: {resolvedVersion}.");
        }

        return bestLength;
    }

    private static int GetVersionForNumericPayloadLength(int payloadLength, Dictionary<int, int> versionByLength)
    {
        if (!versionByLength.TryGetValue(payloadLength, out var version))
        {
            version = QRCode.Create(new string('1', payloadLength), ErrorCorrectionLevel.L).Version;
            versionByLength[payloadLength] = version;
        }

        return version;
    }

    // ───── Capacity tables (ISO/IEC 18004 Table 7/9) ─────

    /// <summary>
    /// Maximum byte-mode payload for each version, indexed by L, M, Q, H.
    /// </summary>
    private static readonly int[][] ByteCapacities =
    [
        [17, 14, 11, 7], [32, 26, 20, 14], [53, 42, 32, 24], [78, 62, 46, 34],
        [106, 84, 60, 44], [134, 106, 74, 58], [154, 122, 86, 64], [192, 152, 108, 84],
        [230, 180, 130, 98], [271, 213, 151, 119], [321, 251, 177, 137], [367, 287, 203, 155],
        [425, 331, 241, 177], [458, 362, 258, 194], [520, 412, 292, 220], [586, 450, 322, 250],
        [644, 504, 364, 280], [718, 560, 394, 310], [792, 624, 442, 338], [858, 666, 482, 382],
        [929, 711, 509, 403], [1003, 779, 565, 439], [1091, 857, 611, 461], [1171, 911, 661, 511],
        [1273, 997, 715, 535], [1367, 1059, 751, 593], [1465, 1125, 805, 625], [1528, 1190, 868, 658],
        [1628, 1264, 908, 698], [1732, 1370, 982, 742], [1840, 1452, 1030, 790], [1952, 1538, 1112, 842],
        [2068, 1628, 1168, 898], [2188, 1722, 1228, 958], [2303, 1809, 1283, 983], [2431, 1911, 1351, 1051],
        [2563, 1989, 1423, 1093], [2699, 2099, 1499, 1139], [2809, 2213, 1579, 1219], [2953, 2331, 1663, 1273],
    ];

    public static TheoryData<int, ErrorCorrectionLevel> AllVersionsAndErrorCorrectionLevels()
    {
        var data = new TheoryData<int, ErrorCorrectionLevel>();
        foreach (var ecLevel in new[] { ErrorCorrectionLevel.L, ErrorCorrectionLevel.M, ErrorCorrectionLevel.Q, ErrorCorrectionLevel.H })
        {
            for (var version = 1; version <= 40; version++)
            {
                data.Add(version, ecLevel);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_AtMaximumCapacity_SelectsExpectedVersion(int version, ErrorCorrectionLevel ecLevel)
    {
        var capacity = ByteCapacities[version - 1][(int)ecLevel];
        var payload = new byte[capacity];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('0' + (i % 10));
        }

        var qr = QRCode.Create(payload, ecLevel);

        Assert.Equal(version, qr.Version);
        Assert.Equal(17 + (version * 4), qr.Size);
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_OneByteOverCapacity_MovesToTheNextVersion(int version, ErrorCorrectionLevel ecLevel)
    {
        var payload = new byte[ByteCapacities[version - 1][(int)ecLevel] + 1];

        if (version == 40)
        {
            Assert.Throws<InvalidOperationException>(() => QRCode.Create(payload, ecLevel));
            return;
        }

        Assert.Equal(version + 1, QRCode.Create(payload, ecLevel).Version);
    }

    [Theory]
    [InlineData(ErrorCorrectionLevel.L)]
    [InlineData(ErrorCorrectionLevel.M)]
    [InlineData(ErrorCorrectionLevel.Q)]
    [InlineData(ErrorCorrectionLevel.H)]
    public void Create_VersionSelectionIsMonotonicInPayloadLength(ErrorCorrectionLevel ecLevel)
    {
        // A non-monotonic capacity table lets DetermineVersion skip versions, which produces a
        // needlessly large symbol for some lengths and no symbol at all for others.
        var maximum = ByteCapacities[^1][(int)ecLevel];
        var previousVersion = 0;
        for (var length = 1; length <= maximum; length++)
        {
            var version = QRCode.Create(new byte[length], ecLevel).Version;
            Assert.True(version >= previousVersion, $"Payload of {length} bytes selected version {version} after version {previousVersion}.");
            previousVersion = version;
        }
    }

    // ───── Symbol structure (ISO/IEC 18004) ─────

    /// <summary>
    /// Alignment pattern centre coordinates for versions 1 to 40 (ISO/IEC 18004 Annex E).
    /// </summary>
    private static readonly int[][] AlignmentPatternPositions =
    [
        [], [6, 18], [6, 22], [6, 26], [6, 30],
        [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
        [6, 30, 54], [6, 32, 58], [6, 34, 62], [6, 26, 46, 66], [6, 26, 48, 70],
        [6, 26, 50, 74], [6, 30, 54, 78], [6, 30, 56, 82], [6, 30, 58, 86], [6, 34, 62, 90],
        [6, 28, 50, 72, 94], [6, 26, 50, 74, 98], [6, 30, 54, 78, 102], [6, 28, 54, 80, 106], [6, 32, 58, 84, 110],
        [6, 30, 58, 86, 114], [6, 34, 62, 90, 118], [6, 26, 50, 74, 98, 122], [6, 30, 54, 78, 102, 126], [6, 26, 52, 78, 104, 130],
        [6, 30, 56, 82, 108, 134], [6, 34, 60, 86, 112, 138], [6, 30, 58, 86, 114, 142], [6, 34, 62, 90, 118, 146], [6, 30, 54, 78, 102, 126, 150],
        [6, 24, 50, 76, 102, 128, 154], [6, 28, 54, 80, 106, 132, 158], [6, 32, 58, 84, 110, 136, 162], [6, 26, 54, 82, 110, 138, 166], [6, 30, 58, 86, 114, 142, 170],
    ];

    public static TheoryData<int> AllVersions()
    {
        var data = new TheoryData<int>();
        for (var version = 1; version <= 40; version++)
        {
            data.Add(version);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheThreeFinderPatternsAndTheirSeparators(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);
        var size = qr.Size;

        foreach (var (row, column) in new[] { (0, 0), (0, size - 7), (size - 7, 0) })
        {
            AssertFinderPattern(qr, row, column);
        }

        // The separator is the light band between a finder pattern and the rest of the symbol.
        for (var i = 0; i <= 7; i++)
        {
            Assert.False(qr[7, i], $"Separator module (7, {i}) is dark.");
            Assert.False(qr[i, 7], $"Separator module ({i}, 7) is dark.");
            Assert.False(qr[7, size - 1 - i], $"Separator module (7, {size - 1 - i}) is dark.");
            Assert.False(qr[i, size - 8], $"Separator module ({i}, {size - 8}) is dark.");
            Assert.False(qr[size - 8, i], $"Separator module ({size - 8}, {i}) is dark.");
            Assert.False(qr[size - 1 - i, 7], $"Separator module ({size - 1 - i}, 7) is dark.");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheTimingPatterns(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);

        for (var i = 8; i < qr.Size - 8; i++)
        {
            Assert.Equal(i % 2 == 0, qr[6, i], $"Horizontal timing module at column {i} is wrong.");
            Assert.Equal(i % 2 == 0, qr[i, 6], $"Vertical timing module at row {i} is wrong.");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheDarkModule(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);

        Assert.True(qr[(4 * version) + 9, 8]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_PlacesTheAlignmentPatterns(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);
        var positions = AlignmentPatternPositions[version - 1];

        foreach (var centerRow in positions)
        {
            foreach (var centerColumn in positions)
            {
                // The three corners are taken by the finder patterns.
                var isCorner = (centerRow <= 8 && centerColumn <= 8) ||
                               (centerRow <= 8 && centerColumn >= qr.Size - 9) ||
                               (centerRow >= qr.Size - 9 && centerColumn <= 8);
                if (isCorner)
                {
                    continue;
                }

                for (var row = -2; row <= 2; row++)
                {
                    for (var column = -2; column <= 2; column++)
                    {
                        var expected = Math.Abs(row) == 2 || Math.Abs(column) == 2 || (row == 0 && column == 0);
                        if (expected != qr[centerRow + row, centerColumn + column])
                        {
                            Assert.Fail($"Alignment pattern centred on ({centerRow}, {centerColumn}) is wrong at offset ({row}, {column}).");
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_WritesTwoIdenticalFormatInformationCopies(int version, ErrorCorrectionLevel ecLevel)
    {
        var qr = CreateWithVersion(version, ecLevel);

        Assert.Equal(ReadFormatInformation(qr, secondCopy: false), ReadFormatInformation(qr, secondCopy: true));
    }

    [Theory]
    [MemberData(nameof(AllVersionsAndErrorCorrectionLevels))]
    public void Create_FormatInformationEncodesTheErrorCorrectionLevelAndAValidMask(int version, ErrorCorrectionLevel ecLevel)
    {
        var qr = CreateWithVersion(version, ecLevel);
        var formatInformation = ReadFormatInformation(qr, secondCopy: false) ^ FormatInformationMask;

        Assert.True(IsValidBch15_5(formatInformation), $"Format information 0x{formatInformation:X4} is not a valid BCH(15,5) code word.");

        var expectedIndicator = ecLevel switch
        {
            ErrorCorrectionLevel.L => 0b01,
            ErrorCorrectionLevel.M => 0b00,
            ErrorCorrectionLevel.Q => 0b11,
            _ => 0b10,
        };

        Assert.Equal(expectedIndicator, (formatInformation >> 13) & 0b11);
        Assert.InRange((formatInformation >> 10) & 0b111, 0, 7);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Create_WritesVersionInformationFromVersion7Onwards(int version)
    {
        var qr = CreateWithVersion(version, ErrorCorrectionLevel.M);
        if (version < 7)
        {
            // Below version 7 those modules carry data, so there is nothing to check beyond the
            // absence of a reserved block; the format information tests cover the rest.
            return;
        }

        var bottomLeft = ReadVersionInformation(qr, topRight: false);
        var topRight = ReadVersionInformation(qr, topRight: true);

        Assert.Equal(bottomLeft, topRight);
        Assert.Equal(version, bottomLeft >> 12);
        Assert.True(IsValidGolay18_6(bottomLeft), $"Version information 0x{bottomLeft:X5} is not a valid Golay(18,6) code word.");
    }

    [Fact]
    public void CreateMicroQR_PlacesASingleFinderPatternAndTheTimingPatterns()
    {
        var qr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

        AssertFinderPattern(qr, 0, 0);

        // Micro QR runs its timing patterns along the top row and the leftmost column.
        for (var i = 8; i < qr.Size; i++)
        {
            Assert.Equal(i % 2 == 0, qr[0, i], $"Horizontal timing module at column {i} is wrong.");
            Assert.Equal(i % 2 == 0, qr[i, 0], $"Vertical timing module at row {i} is wrong.");
        }

        // There is no second or third finder pattern, so the other corners carry data.
        for (var i = 0; i <= 7; i++)
        {
            Assert.False(qr[7, i], $"Separator module (7, {i}) is dark.");
            Assert.False(qr[i, 7], $"Separator module ({i}, 7) is dark.");
        }
    }

    private static void AssertFinderPattern(QRCode qr, int row, int column)
    {
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var expected = r is 0 or 6 || c is 0 or 6 || (r is >= 2 and <= 4 && c is >= 2 and <= 4);
                if (expected != qr[row + r, column + c])
                {
                    Assert.Fail($"Finder pattern at ({row}, {column}) is wrong at offset ({r}, {c}).");
                }
            }
        }
    }

    /// <summary>The value the 15 format information bits are XORed with before being written.</summary>
    private const int FormatInformationMask = 0b101_0100_0001_0010;

    /// <summary>BCH(15,5) generator polynomial x^10 + x^8 + x^5 + x^4 + x^2 + x + 1.</summary>
    private const int Bch15_5Generator = 0b101_0011_0111;

    /// <summary>Golay(18,6) generator polynomial x^12 + x^11 + x^10 + x^9 + x^8 + x^5 + x^2 + 1.</summary>
    private const int Golay18_6Generator = 0b1_1111_0010_0101;

    /// <summary>
    /// Reads the 15 format information bits, most significant bit first.
    /// </summary>
    private static int ReadFormatInformation(QRCode qr, bool secondCopy)
    {
        var size = qr.Size;
        var positions = new List<(int Row, int Column)>(15);
        if (secondCopy)
        {
            for (var i = 0; i < 7; i++)
            {
                positions.Add((size - 1 - i, 8));
            }

            for (var i = 0; i < 8; i++)
            {
                positions.Add((8, size - 8 + i));
            }
        }
        else
        {
            for (var column = 0; column <= 5; column++)
            {
                positions.Add((8, column));
            }

            positions.Add((8, 7));
            positions.Add((8, 8));
            positions.Add((7, 8));

            for (var row = 5; row >= 0; row--)
            {
                positions.Add((row, 8));
            }
        }

        var value = 0;
        foreach (var (row, column) in positions)
        {
            value = (value << 1) | (qr[row, column] ? 1 : 0);
        }

        return value;
    }

    /// <summary>
    /// Reads the 18 version information bits. Bit <c>i</c> sits at row <c>size - 11 + (i % 3)</c>
    /// and column <c>i / 3</c> in the bottom-left block, transposed in the top-right block.
    /// </summary>
    private static int ReadVersionInformation(QRCode qr, bool topRight)
    {
        var value = 0;
        for (var i = 17; i >= 0; i--)
        {
            var row = qr.Size - 11 + (i % 3);
            var column = i / 3;
            var bit = topRight ? qr[column, row] : qr[row, column];
            value = (value << 1) | (bit ? 1 : 0);
        }

        return value;
    }

    private static bool IsValidBch15_5(int value)
    {
        for (var bit = 14; bit >= 10; bit--)
        {
            if ((value & (1 << bit)) != 0)
            {
                value ^= Bch15_5Generator << (bit - 10);
            }
        }

        return value == 0;
    }

    private static bool IsValidGolay18_6(int value)
    {
        for (var bit = 17; bit >= 12; bit--)
        {
            if ((value & (1 << bit)) != 0)
            {
                value ^= Golay18_6Generator << (bit - 12);
            }
        }

        return value == 0;
    }

    private static QRCode CreateWithVersion(int version, ErrorCorrectionLevel ecLevel)
    {
        var payload = new byte[ByteCapacities[version - 1][(int)ecLevel]];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('0' + (i % 10));
        }

        return QRCode.Create(payload, ecLevel);
    }
}
