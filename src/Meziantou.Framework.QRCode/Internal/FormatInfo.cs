namespace Meziantou.Framework.Internal;

internal static class FormatInfo
{
    // Format information strings for each (EC level, mask pattern) combination
    // BCH(15,5) encoded with mask pattern 101010000010010
    private static ReadOnlySpan<ushort> FormatInfoTable =>
    [
        // EC Level L (0), masks 0-7
        0x77C4, 0x72F3, 0x7DAA, 0x789D, 0x662F, 0x6318, 0x6C41, 0x6976,
        // EC Level M (1), masks 0-7
        0x5412, 0x5125, 0x5E7C, 0x5B4B, 0x45F9, 0x40CE, 0x4F97, 0x4AA0,
        // EC Level Q (2), masks 0-7
        0x355F, 0x3068, 0x3F31, 0x3A06, 0x24B4, 0x2183, 0x2EDA, 0x2BED,
        // EC Level H (3), masks 0-7
        0x1689, 0x13BE, 0x1CE7, 0x19D0, 0x0762, 0x0255, 0x0D0C, 0x083B,
    ];

    /// <summary>
    /// Gets the 15-bit format information for the given EC level and mask pattern.
    /// </summary>
    public static int GetFormatInfo(ErrorCorrectionLevel ecLevel, int maskPattern)
    {
        return FormatInfoTable[((int)ecLevel * 8) + maskPattern];
    }

    // Version information strings for versions 7-40
    // BCH(18,6) encoded
    private static ReadOnlySpan<int> VersionInfoTable =>
    [
        0x07C94, 0x085BC, 0x09A99, 0x0A4D3, 0x0BBF6, 0x0C762, 0x0D847, 0x0E60D,
        0x0F928, 0x10B78, 0x1145D, 0x12A17, 0x13532, 0x149A6, 0x15683, 0x168C9,
        0x177EC, 0x18EC4, 0x191E1, 0x1AFAB, 0x1B08E, 0x1CC1A, 0x1D33F, 0x1ED75,
        0x1F250, 0x209D5, 0x216F0, 0x228BA, 0x2379F, 0x24B0B, 0x2542E, 0x26A64,
        0x27541, 0x28C69,
    ];

    /// <summary>
    /// Gets the 18-bit version information for versions 7-40.
    /// Returns 0 for versions 1-6 (no version info needed).
    /// </summary>
    public static int GetVersionInfo(int version)
    {
        if (version < 7)
        {
            return 0;
        }

        return VersionInfoTable[version - 7];
    }
}
