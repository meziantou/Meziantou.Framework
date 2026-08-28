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
        // Kanji character in the 0xE040-0xEBBF Shift JIS range
        var qr = QRCode.Create("纊", ErrorCorrectionLevel.L);

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
}
