namespace Meziantou.Framework.Internal;

/// <summary>
/// QR code version information and capacity tables per ISO/IEC 18004.
/// </summary>
internal static class QRCodeVersion
{
    public static int GetSideLength(int version) => 17 + (version * 4);

    /// <summary>
    /// Gets the total number of data codewords for a given version and error correction level.
    /// </summary>
    public static int GetDataCodewords(int version, ErrorCorrectionLevel ecLevel)
    {
        var (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords) = GetBlockInfo(version, ecLevel);

        return (group1Blocks * group1DataCodewords) + (group2Blocks * group2DataCodewords);
    }

    /// <summary>
    /// Gets the total number of codewords (data + EC) for a given version.
    /// </summary>
    public static int GetTotalCodewords(int version)
    {
        // Total codewords for versions 1-40
        ReadOnlySpan<ushort> table =
        [
            26, 44, 70, 100, 134, 172, 196, 242, 292, 346,
            404, 466, 532, 581, 655, 733, 815, 901, 991, 1085,
            1156, 1258, 1364, 1474, 1588, 1706, 1828, 1921, 2051, 2185,
            2323, 2465, 2611, 2761, 2876, 3034, 3196, 3362, 3532, 3706,
        ];

        return table[version - 1];
    }

    /// <summary>
    /// Gets the number of error correction codewords per block for a given version and EC level.
    /// </summary>
    public static int GetECCodewordsPerBlock(int version, ErrorCorrectionLevel ecLevel)
    {
        // EC codewords per block, indexed by [version-1, ecLevel]
        // Rows: version 1-40, Columns: L, M, Q, H
        ReadOnlySpan<byte> table =
        [
            7, 10, 13, 17,     // V1
            10, 16, 22, 28,    // V2
            15, 26, 18, 22,    // V3
            20, 18, 26, 16,    // V4
            26, 24, 18, 22,    // V5
            18, 16, 24, 28,    // V6
            20, 18, 18, 26,    // V7
            24, 22, 22, 26,    // V8
            30, 22, 20, 24,    // V9
            18, 26, 24, 28,    // V10
            20, 30, 28, 24,    // V11
            24, 22, 26, 28,    // V12
            26, 22, 24, 22,    // V13
            30, 24, 20, 24,    // V14
            22, 24, 30, 24,    // V15
            24, 28, 24, 30,    // V16
            28, 28, 28, 28,    // V17
            30, 26, 28, 28,    // V18
            28, 26, 26, 26,    // V19
            28, 26, 30, 28,    // V20
            28, 26, 28, 30,    // V21
            28, 28, 30, 24,    // V22
            30, 28, 30, 30,    // V23
            30, 28, 30, 30,    // V24
            26, 28, 30, 30,    // V25
            28, 28, 28, 30,    // V26
            30, 28, 30, 30,    // V27
            30, 28, 30, 30,    // V28
            30, 28, 30, 30,    // V29
            30, 28, 30, 30,    // V30
            30, 28, 30, 30,    // V31
            30, 28, 30, 30,    // V32
            30, 28, 30, 30,    // V33
            30, 28, 30, 30,    // V34
            30, 28, 30, 30,    // V35
            30, 28, 30, 30,    // V36
            30, 28, 30, 30,    // V37
            30, 28, 30, 30,    // V38
            30, 28, 30, 30,    // V39
            30, 28, 30, 30,    // V40
        ];

        return table[((version - 1) * 4) + (int)ecLevel];
    }

    /// <summary>
    /// Gets the number of Reed-Solomon blocks for a given version and EC level, per ISO/IEC 18004 Table 9.
    /// </summary>
    public static int GetBlockCount(int version, ErrorCorrectionLevel ecLevel)
    {
        // Rows: version 1-40, Columns: L, M, Q, H
        ReadOnlySpan<byte> table =
        [
            1, 1, 1, 1,       // V1
            1, 1, 1, 1,       // V2
            1, 1, 2, 2,       // V3
            1, 2, 2, 4,       // V4
            1, 2, 4, 4,       // V5
            2, 4, 4, 4,       // V6
            2, 4, 6, 5,       // V7
            2, 4, 6, 6,       // V8
            2, 5, 8, 8,       // V9
            4, 5, 8, 8,       // V10
            4, 5, 8, 11,      // V11
            4, 8, 10, 11,     // V12
            4, 9, 12, 16,     // V13
            4, 9, 16, 16,     // V14
            6, 10, 12, 18,    // V15
            6, 10, 17, 16,    // V16
            6, 11, 16, 19,    // V17
            6, 13, 18, 21,    // V18
            7, 14, 21, 25,    // V19
            8, 16, 20, 25,    // V20
            8, 17, 23, 25,    // V21
            9, 17, 23, 34,    // V22
            9, 18, 25, 30,    // V23
            10, 20, 27, 32,   // V24
            12, 21, 29, 35,   // V25
            12, 23, 34, 37,   // V26
            12, 25, 34, 40,   // V27
            13, 26, 35, 42,   // V28
            14, 28, 38, 45,   // V29
            15, 29, 40, 48,   // V30
            16, 31, 43, 51,   // V31
            17, 33, 45, 54,   // V32
            18, 35, 48, 57,   // V33
            19, 37, 51, 60,   // V34
            19, 38, 53, 63,   // V35
            20, 40, 56, 66,   // V36
            21, 43, 59, 70,   // V37
            22, 45, 62, 74,   // V38
            24, 47, 65, 77,   // V39
            25, 49, 68, 81,   // V40
        ];

        return table[((version - 1) * 4) + (int)ecLevel];
    }

    /// <summary>
    /// Gets the block structure for a given version and EC level.
    /// Returns (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords).
    /// </summary>
    /// <remarks>
    /// ISO/IEC 18004 distributes the data codewords as evenly as possible across the blocks, so the
    /// split is fully determined by the total codeword count, the EC codewords per block and the
    /// block count. Deriving it here keeps <see cref="GetDataCodewords"/> and this method from
    /// disagreeing, which previously produced symbols the error correction encoder could not build.
    /// </remarks>
    public static (int Group1Blocks, int Group1DataCodewords, int Group2Blocks, int Group2DataCodewords) GetBlockInfo(int version, ErrorCorrectionLevel ecLevel)
    {
        var totalBlocks = GetBlockCount(version, ecLevel);
        var dataCodewords = GetTotalCodewords(version) - (GetECCodewordsPerBlock(version, ecLevel) * totalBlocks);

        var group2Blocks = dataCodewords % totalBlocks;
        var group1Blocks = totalBlocks - group2Blocks;
        var group1DataCodewords = dataCodewords / totalBlocks;

        return (group1Blocks, group1DataCodewords, group2Blocks, group1DataCodewords + 1);
    }

    /// <summary>
    /// Gets the character capacity for a given version, EC level, and encoding mode.
    /// </summary>
    public static int GetCharacterCapacity(int version, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        var dataCodewords = GetDataCodewords(version, ecLevel);
        var dataBits = dataCodewords * 8;

        // Subtract mode indicator (4 bits)
        dataBits -= 4;

        // Subtract character count indicator bits
        var cciBits = GetCharacterCountBits(version, mode);
        dataBits -= cciBits;

        if (dataBits < 0)
        {
            return 0;
        }

        return mode switch
        {
            EncodingMode.Numeric => (dataBits / 10 * 3) + (dataBits % 10 >= 7 ? 2 : (dataBits % 10 >= 4 ? 1 : 0)),
            EncodingMode.Alphanumeric => (dataBits / 11 * 2) + (dataBits % 11 >= 6 ? 1 : 0),
            EncodingMode.Byte => dataBits / 8,
            EncodingMode.Kanji => dataBits / 13,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>
    /// Gets the number of bits in the character count indicator.
    /// </summary>
    public static int GetCharacterCountBits(int version, EncodingMode mode)
    {
        return (mode, version) switch
        {
            (EncodingMode.Numeric, <= 9) => 10,
            (EncodingMode.Numeric, <= 26) => 12,
            (EncodingMode.Numeric, _) => 14,

            (EncodingMode.Alphanumeric, <= 9) => 9,
            (EncodingMode.Alphanumeric, <= 26) => 11,
            (EncodingMode.Alphanumeric, _) => 13,

            (EncodingMode.Byte, <= 9) => 8,
            (EncodingMode.Byte, <= 26) => 16,
            (EncodingMode.Byte, _) => 16,

            (EncodingMode.Kanji, <= 9) => 8,
            (EncodingMode.Kanji, <= 26) => 10,
            (EncodingMode.Kanji, _) => 12,

            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>
    /// Gets the alignment pattern center positions for a given version.
    /// </summary>
    public static ReadOnlySpan<byte> GetAlignmentPatternPositions(int version)
    {
        if (version == 1)
        {
            return [];
        }

        // Alignment pattern positions for versions 2-40
        ReadOnlySpan<byte> allPositions =
        [
            // V2: 2 positions
            6, 18,
            // V3: 2 positions
            6, 22,
            // V4: 2 positions
            6, 26,
            // V5: 2 positions
            6, 30,
            // V6: 2 positions
            6, 34,
            // V7: 3 positions
            6, 22, 38,
            // V8: 3 positions
            6, 24, 42,
            // V9: 3 positions
            6, 26, 46,
            // V10: 3 positions
            6, 28, 50,
            // V11: 3 positions
            6, 30, 54,
            // V12: 3 positions
            6, 32, 58,
            // V13: 3 positions
            6, 34, 62,
            // V14: 4 positions
            6, 26, 46, 66,
            // V15: 4 positions
            6, 26, 48, 70,
            // V16: 4 positions
            6, 26, 50, 74,
            // V17: 4 positions
            6, 30, 54, 78,
            // V18: 4 positions
            6, 30, 56, 82,
            // V19: 4 positions
            6, 30, 58, 86,
            // V20: 4 positions
            6, 34, 62, 90,
            // V21: 5 positions
            6, 28, 50, 72, 94,
            // V22: 5 positions
            6, 26, 50, 74, 98,
            // V23: 5 positions
            6, 30, 54, 78, 102,
            // V24: 5 positions
            6, 28, 54, 80, 106,
            // V25: 5 positions
            6, 32, 58, 84, 110,
            // V26: 5 positions
            6, 30, 58, 86, 114,
            // V27: 5 positions
            6, 34, 62, 90, 118,
            // V28: 6 positions
            6, 26, 50, 74, 98, 122,
            // V29: 6 positions
            6, 30, 54, 78, 102, 126,
            // V30: 6 positions
            6, 26, 52, 78, 104, 130,
            // V31: 6 positions
            6, 30, 56, 82, 108, 134,
            // V32: 6 positions
            6, 34, 60, 86, 112, 138,
            // V33: 6 positions
            6, 30, 58, 86, 114, 142,
            // V34: 6 positions
            6, 34, 62, 90, 118, 146,
            // V35: 7 positions
            6, 30, 54, 78, 102, 126, 150,
            // V36: 7 positions
            6, 24, 50, 76, 102, 128, 154,
            // V37: 7 positions
            6, 28, 54, 80, 106, 132, 158,
            // V38: 7 positions
            6, 32, 58, 84, 110, 136, 162,
            // V39: 7 positions
            6, 26, 54, 82, 110, 138, 166,
            // V40: 7 positions
            6, 30, 58, 86, 114, 142, 170,
        ];

        // Offsets and lengths for each version
        ReadOnlySpan<byte> offsets =
        [
            0, 2, 4, 6, 8,                  // V2-V6
            10, 13, 16, 19, 22, 25, 28,     // V7-V13
            31, 35, 39, 43, 47, 51, 55,     // V14-V20
            59, 64, 69, 74, 79, 84, 89,     // V21-V27
            94, 100, 106, 112, 118, 124, 130, 136, // V28-V35
            143, 150, 157, 164, 171,         // V36-V40
        ];

        ReadOnlySpan<byte> lengths =
        [
            2, 2, 2, 2, 2,                  // V2-V6
            3, 3, 3, 3, 3, 3, 3,            // V7-V13
            4, 4, 4, 4, 4, 4, 4,            // V14-V20
            5, 5, 5, 5, 5, 5, 5,            // V21-V27
            6, 6, 6, 6, 6, 6, 6, 7,         // V28-V35
            7, 7, 7, 7, 7,                  // V36-V40
        ];

        var idx = version - 2;
        var offset = offsets[idx];
        var length = lengths[idx];

        return allPositions.Slice(offset, length);
    }
}
