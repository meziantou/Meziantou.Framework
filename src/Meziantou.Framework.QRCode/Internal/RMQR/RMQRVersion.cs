using System.Text;

namespace Meziantou.Framework.Internal.RMQR;

internal static class RMQRVersion
{
    // 32 versions indexed 0-31
    // Each entry is (height, width)
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

    // Total codewords by version (0-31)
    private static ReadOnlySpan<byte> TotalCodewords =>
    [
        13, 21, 32, 44, 68,
        22, 32, 44, 60, 90,
        12, 32, 44, 60, 80, 118,
        12, 32, 48, 64, 88, 130,
        40, 54, 74, 100, 148,
        46, 62, 82, 112, 245,
    ];

    // Data codewords for EC level M by version (0-31)
    private static ReadOnlySpan<byte> DataCodewordsM =>
    [
        4, 7, 10, 14, 24,
        8, 12, 16, 22, 36,
        4, 12, 18, 24, 34, 54,
        4, 14, 20, 28, 40, 62,
        16, 24, 32, 46, 72,
        20, 28, 38, 52, 121,
    ];

    // Data codewords for EC level H by version (0-31)
    private static ReadOnlySpan<byte> DataCodewordsH =>
    [
        2, 4, 6, 8, 14,
        4, 6, 10, 14, 22,
        2, 6, 10, 14, 20, 32,
        2, 8, 12, 16, 24, 36,
        10, 14, 20, 28, 44,
        12, 18, 24, 32, 61,
    ];

    public static int GetHeight(int version) => Heights[version];

    public static int GetWidth(int version) => Widths[version];

    public static int GetTotalCodewords(int version) => TotalCodewords[version];

    public static int GetDataCodewords(int version, ErrorCorrectionLevel ecLevel)
    {
        return ecLevel switch
        {
            ErrorCorrectionLevel.M => DataCodewordsM[version],
            ErrorCorrectionLevel.H => DataCodewordsH[version],
            _ => throw new ArgumentOutOfRangeException(nameof(ecLevel), $"rMQR only supports EC levels M and H, got {ecLevel}."),
        };
    }

    public static int GetECCodewords(int version, ErrorCorrectionLevel ecLevel)
    {
        return GetTotalCodewords(version) - GetDataCodewords(version, ecLevel);
    }

    // Character count indicator bits by version and mode
    // Group 0: versions 0,1,5         -> Numeric=4, Alpha=3, Byte=3, Kanji=2
    // Group 1: versions 2,3,6,7,10,11,16 -> Numeric=5, Alpha=4, Byte=4, Kanji=3
    // Group 2: versions 4,8,9,12-15,17-26,27-30 -> Numeric=6, Alpha=5, Byte=5, Kanji=4
    // Group 3: version 31              -> Numeric=7, Alpha=6, Byte=6, Kanji=5
    public static int GetCharacterCountBits(int version, EncodingMode mode)
    {
        var group = GetCCIGroup(version);
        return (mode, group) switch
        {
            (EncodingMode.Numeric, 0) => 4,
            (EncodingMode.Numeric, 1) => 5,
            (EncodingMode.Numeric, 2) => 6,
            (EncodingMode.Numeric, 3) => 7,
            (EncodingMode.Alphanumeric, 0) => 3,
            (EncodingMode.Alphanumeric, 1) => 4,
            (EncodingMode.Alphanumeric, 2) => 5,
            (EncodingMode.Alphanumeric, 3) => 6,
            (EncodingMode.Byte, 0) => 3,
            (EncodingMode.Byte, 1) => 4,
            (EncodingMode.Byte, 2) => 5,
            (EncodingMode.Byte, 3) => 6,
            (EncodingMode.Kanji, 0) => 2,
            (EncodingMode.Kanji, 1) => 3,
            (EncodingMode.Kanji, 2) => 4,
            (EncodingMode.Kanji, 3) => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private static int GetCCIGroup(int version)
    {
        return version switch
        {
            0 or 1 or 5 => 0,
            2 or 3 or 6 or 7 or 10 or 11 or 16 => 1,
            31 => 3,
            _ => 2, // versions 4,8,9,12-15,17-26,27-30
        };
    }

    // Mode indicator is 3 bits for rMQR
    public static int GetModeIndicatorBits() => 3;

    // Mode indicator values (3-bit)
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

    // Terminator is always 3 bits for rMQR
    public static int GetTerminatorBits() => 3;

    // Determine the smallest version that can fit the data.
    // Smallest = smallest total module area (height * width).
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

        for (var version = 0; version < 32; version++)
        {
            var dataCW = GetDataCodewords(version, ecLevel);
            var totalDataBits = dataCW * 8;

            // Compute how many bits we need for this version
            var modeIndicatorBits = GetModeIndicatorBits();
            var cciBits = GetCharacterCountBits(version, mode);

            // Check if char count fits in the CCI field
            if (charCount >= (1 << cciBits))
            {
                continue;
            }

            var neededBits = modeIndicatorBits + cciBits + GetDataBitsForMode(data, mode);
            // We need at least the data bits; terminator and padding are added up to totalDataBits
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

    // Alignment pattern center positions for rMQR versions.
    // Only versions with width >= 43 have additional alignment patterns.
    // The alignment patterns are placed in the interior at specific column positions.
    // Returns the column centers for the alignment row(s).
    public static int[] GetAlignmentPatternColumnPositions(int version)
    {
        var width = GetWidth(version);

        // For narrow codes (width 27), no alignment patterns
        if (width <= 27)
        {
            return [];
        }

        // Alignment pattern columns are placed between the finder and sub-finder.
        // They appear at columns that create roughly even spacing.
        // The first possible column is 21 (after finder+separator+timing area),
        // then subsequent ones at intervals, with the last being width-13 (before sub-finder area).
        // Based on the spec, alignment centers are at fixed positions depending on width.
        return width switch
        {
            43 => [21],
            59 => [19, 39],
            77 => [25, 51],
            99 => [27, 55, 75],
            139 => [21, 47, 73, 99, 119],
            _ => [],
        };
    }

    // Alignment pattern row positions for rMQR.
    // For heights 7 and 9, alignment is only in the middle row area of the timing edge.
    // For heights >= 11, alignment patterns appear at row positions.
    public static int[] GetAlignmentPatternRowPositions(int version)
    {
        var height = GetHeight(version);
        // Alignment patterns in rMQR sit along a horizontal center line.
        // For all heights, the alignment center row is at the vertical midpoint.
        // But actually rMQR alignment patterns are all on the horizontal center stripe.
        // There's really just one row for alignments.
        return height switch
        {
            7 => [3],  // center of 7-row symbol
            9 => [4],
            11 => [5],
            13 => [6],
            15 => [7],
            17 => [8],
            _ => [],
        };
    }
}
