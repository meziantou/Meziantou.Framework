using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Internal.RMQR;

internal static class RMQRVersion
{
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ErrorCorrectionProfile(byte EcCodewordsPerBlock, byte Group1BlockCount, byte Group1DataCodewords, byte Group2BlockCount, byte Group2DataCodewords)
    {
        public int DataCodewords => (Group1BlockCount * Group1DataCodewords) + (Group2BlockCount * Group2DataCodewords);
        public int BlockCount => Group1BlockCount + Group2BlockCount;
        public int TotalCodewords => (DataCodewords + EcCodewordsPerBlock * BlockCount);
    }

    private static ReadOnlySpan<byte> Heights =>
    [
        7, 7, 7, 7, 7,
        9, 9, 9, 9, 9,
        11, 11, 11, 11, 11, 11,
        13, 13, 13, 13, 13, 13,
        15, 15, 15, 15, 15,
        17, 17, 17, 17, 17,
    ];

    private static ReadOnlySpan<byte> Widths =>
    [
        43, 59, 77, 99, 139,
        43, 59, 77, 99, 139,
        27, 43, 59, 77, 99, 139,
        27, 43, 59, 77, 99, 139,
        43, 59, 77, 99, 139,
        43, 59, 77, 99, 139,
    ];

    private static readonly ErrorCorrectionProfile[] MProfiles =
    [
        new(7, 1, 6, 0, 0),   // 1: R7x43
        new(9, 1, 12, 0, 0),  // 2: R7x59
        new(12, 1, 20, 0, 0), // 3: R7x77
        new(16, 1, 28, 0, 0), // 4: R7x99
        new(24, 1, 44, 0, 0), // 5: R7x139
        new(9, 1, 12, 0, 0),  // 6: R9x43
        new(12, 1, 21, 0, 0), // 7: R9x59
        new(18, 1, 31, 0, 0), // 8: R9x77
        new(24, 1, 42, 0, 0), // 9: R9x99
        new(18, 1, 31, 1, 32), // 10: R9x139
        new(8, 1, 7, 0, 0),   // 11: R11x27
        new(12, 1, 19, 0, 0), // 12: R11x43
        new(16, 1, 31, 0, 0), // 13: R11x59
        new(24, 1, 43, 0, 0), // 14: R11x77
        new(16, 1, 28, 1, 29), // 15: R11x99
        new(24, 2, 42, 0, 0), // 16: R11x139
        new(9, 1, 12, 0, 0),  // 17: R13x27
        new(14, 1, 27, 0, 0), // 18: R13x43
        new(22, 1, 38, 0, 0), // 19: R13x59
        new(16, 1, 26, 1, 27), // 20: R13x77
        new(20, 1, 36, 1, 37), // 21: R13x99
        new(20, 2, 35, 1, 36), // 22: R13x139
        new(18, 1, 33, 0, 0), // 23: R15x43
        new(26, 1, 48, 0, 0), // 24: R15x59
        new(18, 1, 33, 1, 34), // 25: R15x77
        new(24, 2, 44, 0, 0), // 26: R15x99
        new(24, 2, 42, 1, 43), // 27: R15x139
        new(22, 1, 39, 0, 0), // 28: R17x43
        new(16, 2, 28, 0, 0), // 29: R17x59
        new(22, 2, 39, 0, 0), // 30: R17x77
        new(20, 2, 33, 1, 34), // 31: R17x99
        new(20, 4, 38, 0, 0), // 32: R17x139
    ];

    private static readonly ErrorCorrectionProfile[] HProfiles =
    [
        new(10, 1, 3, 0, 0),  // 1
        new(14, 1, 7, 0, 0),  // 2
        new(22, 1, 10, 0, 0), // 3
        new(30, 1, 14, 0, 0), // 4
        new(22, 2, 12, 0, 0), // 5
        new(14, 1, 7, 0, 0),  // 6
        new(22, 1, 11, 0, 0), // 7
        new(16, 1, 8, 1, 9),  // 8
        new(22, 2, 11, 0, 0), // 9
        new(22, 3, 11, 0, 0), // 10
        new(10, 1, 5, 0, 0),  // 11
        new(20, 1, 11, 0, 0), // 12
        new(16, 1, 7, 1, 8),  // 13
        new(22, 1, 11, 1, 12), // 14
        new(30, 1, 14, 1, 15), // 15
        new(30, 3, 14, 0, 0), // 16
        new(14, 1, 7, 0, 0),  // 17
        new(28, 1, 13, 0, 0), // 18
        new(20, 2, 10, 0, 0), // 19
        new(28, 1, 14, 1, 15), // 20
        new(26, 1, 11, 2, 12), // 21
        new(28, 2, 13, 2, 14), // 22
        new(18, 1, 7, 1, 8),  // 23
        new(24, 2, 13, 0, 0), // 24
        new(24, 2, 10, 1, 11), // 25
        new(22, 4, 12, 0, 0), // 26
        new(26, 1, 13, 4, 14), // 27
        new(20, 1, 10, 1, 11), // 28
        new(30, 2, 14, 0, 0), // 29
        new(28, 1, 12, 2, 13), // 30
        new(26, 4, 14, 0, 0), // 31
        new(26, 2, 12, 4, 13), // 32
    ];

    private static ReadOnlySpan<byte> NumericCharacterCountBits =>
    [
        4, 5, 6, 7, 7, 5, 6, 7, 7, 8, 4, 6, 7, 7, 8, 8,
        5, 6, 7, 7, 8, 8, 7, 7, 8, 8, 9, 7, 8, 8, 8, 9,
    ];

    private static ReadOnlySpan<byte> AlphanumericCharacterCountBits =>
    [
        3, 5, 5, 6, 6, 5, 5, 6, 6, 7, 4, 5, 6, 6, 7, 7,
        5, 6, 6, 7, 7, 8, 6, 7, 7, 7, 8, 6, 7, 7, 8, 8,
    ];

    private static ReadOnlySpan<byte> ByteCharacterCountBits =>
    [
        3, 4, 5, 5, 6, 4, 5, 5, 6, 6, 3, 5, 5, 6, 6, 7,
        4, 5, 6, 6, 7, 7, 6, 6, 7, 7, 7, 6, 6, 7, 7, 8,
    ];

    private static ReadOnlySpan<byte> KanjiCharacterCountBits =>
    [
        2, 3, 4, 5, 5, 3, 4, 5, 5, 6, 2, 4, 5, 5, 6, 6,
        3, 5, 5, 6, 6, 7, 5, 5, 6, 6, 7, 5, 6, 6, 6, 7,
    ];

    public static int GetHeight(int version) => Heights[version - 1];

    public static int GetWidth(int version) => Widths[version - 1];

    public static int GetTotalCodewords(int version) => MProfiles[version - 1].TotalCodewords;

    public static int GetDataCodewords(int version, ErrorCorrectionLevel ecLevel) => GetErrorCorrectionProfile(version, ecLevel).DataCodewords;

    public static int GetECCodewords(int version, ErrorCorrectionLevel ecLevel)
    {
        var profile = GetErrorCorrectionProfile(version, ecLevel);
        return profile.EcCodewordsPerBlock * profile.BlockCount;
    }

    public static int GetCharacterCountBits(int version, EncodingMode mode)
    {
        return mode switch
        {
            EncodingMode.Numeric => NumericCharacterCountBits[version - 1],
            EncodingMode.Alphanumeric => AlphanumericCharacterCountBits[version - 1],
            EncodingMode.Byte => ByteCharacterCountBits[version - 1],
            EncodingMode.Kanji => KanjiCharacterCountBits[version - 1],
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    public static int GetModeIndicatorBits() => 3;

    public static int GetModeIndicatorValue(EncodingMode mode)
    {
        return mode switch
        {
            EncodingMode.Numeric => 0b001,
            EncodingMode.Alphanumeric => 0b010,
            EncodingMode.Byte => 0b011,
            EncodingMode.Kanji => 0b100,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    public static int GetTerminatorBits() => 3;

    public static int DetermineVersion(string data, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        var charCount = mode switch
        {
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data),
            EncodingMode.Kanji => data.Length,
            _ => data.Length,
        };

        var bestVersion = -1;
        var bestArea = int.MaxValue;

        for (var version = 1; version <= 32; version++)
        {
            var totalDataBits = GetDataCodewords(version, ecLevel) * 8;
            var cciBits = GetCharacterCountBits(version, mode);
            if (charCount >= (1 << cciBits))
            {
                continue;
            }

            var neededBits = GetModeIndicatorBits() + cciBits + GetDataBitsForMode(data, mode);
            if (neededBits > totalDataBits)
            {
                continue;
            }

            var area = GetHeight(version) * GetWidth(version);
            if (area < bestArea)
            {
                bestArea = area;
                bestVersion = version;
            }
        }

        if (bestVersion < 0)
        {
            throw new InvalidOperationException("The data is too long to be encoded in an rMQR code.");
        }

        return bestVersion;
    }

    public static int[] GetAlignmentPatternColumnPositions(int version)
    {
        var width = GetWidth(version);
        return width switch
        {
            27 => [],
            43 => [21],
            59 => [19, 39],
            77 => [25, 51],
            99 => [23, 49, 75],
            139 => [27, 55, 83, 111],
            _ => throw new InvalidOperationException("Unsupported rMQR width."),
        };
    }

    public static (int Group1BlockCount, int Group1DataCodewords, int Group2BlockCount, int Group2DataCodewords, int EcCodewordsPerBlock) GetErrorCorrectionBlocks(int version, ErrorCorrectionLevel ecLevel)
    {
        var profile = GetErrorCorrectionProfile(version, ecLevel);
        return (profile.Group1BlockCount, profile.Group1DataCodewords, profile.Group2BlockCount, profile.Group2DataCodewords, profile.EcCodewordsPerBlock);
    }

    private static ErrorCorrectionProfile GetErrorCorrectionProfile(int version, ErrorCorrectionLevel ecLevel)
    {
        if (version < 1 || version > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "rMQR version must be in the range 1-32.");
        }

        return ecLevel switch
        {
            ErrorCorrectionLevel.M => MProfiles[version - 1],
            ErrorCorrectionLevel.H => HProfiles[version - 1],
            _ => throw new ArgumentOutOfRangeException(nameof(ecLevel), $"rMQR only supports EC levels M and H, got {ecLevel}."),
        };
    }

    private static int GetDataBitsForMode(string data, EncodingMode mode)
    {
        return mode switch
        {
            EncodingMode.Numeric => GetNumericBits(data.Length),
            EncodingMode.Alphanumeric => GetAlphanumericBits(data.Length),
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data) * 8,
            EncodingMode.Kanji => data.Length * 13,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private static int GetNumericBits(int length)
    {
        var fullGroups = length / 3;
        var remainder = length % 3;
        var bits = fullGroups * 10;
        bits += remainder switch
        {
            2 => 7,
            1 => 4,
            _ => 0,
        };
        return bits;
    }

    private static int GetAlphanumericBits(int length)
    {
        var fullPairs = length / 2;
        var remainder = length % 2;
        return (fullPairs * 11) + (remainder * 6);
    }
}
