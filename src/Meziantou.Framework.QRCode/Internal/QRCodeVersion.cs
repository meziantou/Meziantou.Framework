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
        var totalCodewords = GetTotalCodewords(version);
        var ecCodewordsPerBlock = GetECCodewordsPerBlock(version, ecLevel);
        var (group1Blocks, _, group2Blocks, _) = GetBlockInfo(version, ecLevel);
        var totalBlocks = group1Blocks + group2Blocks;
        var totalECCodewords = ecCodewordsPerBlock * totalBlocks;

        return totalCodewords - totalECCodewords;
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
            28, 26, 28, 28,    // V20
            28, 26, 30, 28,    // V21
            28, 28, 24, 30,    // V22
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
    /// Gets the block structure for a given version and EC level.
    /// Returns (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords).
    /// </summary>
    public static (int Group1Blocks, int Group1DataCodewords, int Group2Blocks, int Group2DataCodewords) GetBlockInfo(int version, ErrorCorrectionLevel ecLevel)
    {
        // Packed as: group1Blocks, group1DataCW, group2Blocks, group2DataCW
        // Index: (version-1) * 4 + ecLevel, each entry is 4 bytes
        ReadOnlySpan<byte> group1Blocks =
        [
            1, 1, 1, 1,       // V1
            1, 1, 1, 1,       // V2
            1, 1, 2, 2,       // V3
            1, 2, 2, 4,       // V4
            1, 2, 2, 2,       // V5
            2, 4, 4, 4,       // V6
            2, 4, 2, 4,       // V7
            2, 2, 4, 4,       // V8
            2, 3, 4, 4,       // V9
            2, 4, 6, 6,       // V10
            4, 1, 4, 3,       // V11
            2, 6, 4, 7,       // V12
            4, 8, 8, 12,      // V13
            3, 4, 11, 11,     // V14
            5, 5, 5, 11,      // V15
            5, 7, 15, 3,      // V16
            1, 10, 1, 2,      // V17
            5, 9, 17, 2,      // V18
            3, 3, 17, 9,      // V19
            3, 3, 15, 15,     // V20
            4, 17, 17, 19,    // V21
            2, 17, 7, 34,     // V22
            4, 4, 11, 16,     // V23
            6, 6, 11, 30,     // V24
            8, 8, 7, 22,      // V25
            10, 19, 28, 33,   // V26
            8, 22, 8, 12,     // V27
            3, 3, 26, 11,     // V28
            7, 21, 7, 23,     // V29
            5, 19, 10, 15,    // V30
            13, 2, 29, 42,    // V31
            17, 10, 10, 23,   // V32
            17, 14, 29, 44,   // V33
            13, 14, 44, 59,   // V34
            12, 12, 39, 22,   // V35
            6, 6, 46, 2,      // V36
            17, 29, 49, 24,   // V37
            4, 13, 48, 42,    // V38
            20, 40, 43, 10,   // V39
            19, 18, 34, 20,   // V40
        ];

        ReadOnlySpan<byte> group1DataCW =
        [
            19, 16, 13, 9,    // V1
            34, 28, 22, 16,   // V2
            55, 44, 17, 13,   // V3
            80, 32, 24, 9,    // V4
            108, 43, 15, 11,  // V5
            68, 27, 19, 15,   // V6
            78, 31, 14, 13,   // V7
            97, 38, 18, 14,   // V8
            116, 36, 16, 12,  // V9
            68, 43, 19, 15,   // V10
            81, 50, 22, 12,   // V11
            92, 36, 20, 14,   // V12
            107, 37, 20, 11,  // V13
            115, 40, 16, 12,  // V14
            87, 41, 24, 12,   // V15
            98, 45, 19, 15,   // V16
            107, 46, 22, 14,  // V17
            120, 43, 22, 14,  // V18
            113, 44, 21, 13,  // V19
            107, 41, 24, 15,  // V20
            116, 42, 22, 16,  // V21
            111, 46, 24, 13,  // V22
            121, 47, 24, 15,  // V23
            117, 45, 24, 16,  // V24
            106, 47, 24, 15,  // V25
            114, 46, 22, 16,  // V26
            122, 45, 23, 15,  // V27
            117, 45, 24, 15,  // V28
            116, 45, 23, 15,  // V29
            115, 45, 24, 15,  // V30
            115, 46, 24, 15,  // V31
            115, 46, 24, 15,  // V32
            115, 46, 24, 15,  // V33
            115, 46, 24, 15,  // V34
            121, 47, 24, 15,  // V35
            121, 47, 24, 15,  // V36
            122, 46, 24, 15,  // V37
            122, 46, 24, 15,  // V38
            117, 47, 24, 15,  // V39
            118, 47, 24, 15,  // V40
        ];

        ReadOnlySpan<byte> group2Blocks =
        [
            0, 0, 0, 0,       // V1
            0, 0, 0, 0,       // V2
            0, 0, 0, 0,       // V3
            0, 0, 0, 0,       // V4
            0, 0, 2, 2,       // V5
            0, 0, 0, 0,       // V6
            0, 0, 4, 1,       // V7
            0, 2, 2, 2,       // V8
            0, 2, 4, 4,       // V9
            2, 1, 2, 2,       // V10
            0, 4, 4, 8,       // V11
            2, 2, 6, 4,       // V12
            0, 1, 4, 4,       // V13
            1, 5, 5, 5,       // V14
            1, 5, 7, 7,       // V15
            1, 3, 2, 13,      // V16
            5, 1, 15, 17,     // V17
            1, 4, 1, 19,      // V18
            4, 11, 4, 16,     // V19
            5, 13, 5, 10,     // V20
            4, 0, 6, 6,       // V21
            7, 0, 16, 0,      // V22
            5, 14, 14, 14,    // V23
            4, 14, 16, 2,     // V24
            4, 13, 22, 13,    // V25
            2, 4, 6, 4,       // V26
            4, 3, 26, 28,     // V27
            10, 23, 2, 31,    // V28
            7, 7, 34, 26,     // V29
            10, 10, 32, 32,   // V30
            3, 29, 2, 1,      // V31
            0, 23, 32, 23,    // V32
            1, 21, 14, 3,     // V33
            6, 23, 7, 1,      // V34
            7, 26, 7, 41,     // V35
            14, 34, 10, 64,   // V36
            4, 14, 10, 46,    // V37
            18, 32, 14, 32,   // V38
            4, 7, 22, 67,     // V39
            6, 31, 34, 61,    // V40
        ];

        ReadOnlySpan<byte> group2DataCW =
        [
            0, 0, 0, 0,       // V1
            0, 0, 0, 0,       // V2
            0, 0, 0, 0,       // V3
            0, 0, 0, 0,       // V4
            0, 0, 16, 12,     // V5
            0, 0, 0, 0,       // V6
            0, 0, 15, 14,     // V7
            0, 39, 19, 15,    // V8
            0, 37, 17, 13,    // V9
            69, 44, 20, 16,   // V10
            0, 51, 23, 13,    // V11
            93, 37, 21, 15,   // V12
            0, 38, 21, 12,    // V13
            116, 41, 17, 13,  // V14
            88, 42, 25, 13,   // V15
            99, 46, 20, 16,   // V16
            108, 47, 23, 15,  // V17
            121, 44, 23, 15,  // V18
            114, 45, 22, 14,  // V19
            108, 42, 25, 16,  // V20
            117, 0, 23, 17,   // V21
            112, 0, 25, 0,    // V22
            122, 48, 25, 16,  // V23
            118, 46, 25, 17,  // V24
            107, 48, 25, 16,  // V25
            115, 47, 23, 17,  // V26
            123, 46, 24, 16,  // V27
            118, 46, 25, 16,  // V28
            117, 46, 24, 16,  // V29
            116, 46, 25, 16,  // V30
            116, 47, 25, 16,  // V31
            0, 47, 25, 16,    // V32
            116, 47, 25, 16,  // V33
            116, 47, 25, 16,  // V34
            122, 48, 25, 16,  // V35
            122, 48, 25, 16,  // V36
            123, 47, 25, 16,  // V37
            123, 47, 25, 16,  // V38
            118, 48, 25, 16,  // V39
            119, 48, 25, 16,  // V40
        ];

        var idx = ((version - 1) * 4) + (int)ecLevel;

        return (group1Blocks[idx], group1DataCW[idx], group2Blocks[idx], group2DataCW[idx]);
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
