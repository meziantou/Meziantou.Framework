namespace Meziantou.Framework.Internal.RMQR;

internal static class RMQRFormatInfo
{
    // ISO/IEC 23941:2022 Annex C, Table C.1 (finder pattern side)
    private static ReadOnlySpan<int> FinderSideMaskedPatterns =>
    [
        0x1FAB2, 0x1E597, 0x1DBDD, 0x1C4F8, 0x1B86C, 0x1A749, 0x19903, 0x18626,
        0x17F0E, 0x1602B, 0x15E61, 0x14144, 0x13DD0, 0x122F5, 0x11CBF, 0x1039A,
        0x0F1CA, 0x0EEEF, 0x0D0A5, 0x0CF80, 0x0B314, 0x0AC31, 0x0927B, 0x08D5E,
        0x07476, 0x06B53, 0x05519, 0x04A3C, 0x036A8, 0x0298D, 0x017C7, 0x008E2,
        0x3F367, 0x3EC42, 0x3D208, 0x3CD2D, 0x3B1B9, 0x3AE9C, 0x390D6, 0x38FF3,
        0x376DB, 0x369FE, 0x357B4, 0x34891, 0x33405, 0x32B20, 0x3156A, 0x30A4F,
        0x2F81F, 0x2E73A, 0x2D970, 0x2C655, 0x2BAC1, 0x2A5E4, 0x29BAE, 0x2848B,
        0x27DA3, 0x26286, 0x25CCC, 0x243E9, 0x23F7D, 0x22058, 0x21E12, 0x20137,
    ];

    // ISO/IEC 23941:2022 Annex C, Table C.1 (finder sub-pattern side)
    private static ReadOnlySpan<int> SubFinderSideMaskedPatterns =>
    [
        0x20A7B, 0x2155E, 0x22B14, 0x23431, 0x248A5, 0x25780, 0x269CA, 0x276EF,
        0x28FC7, 0x290E2, 0x2AEA8, 0x2B18D, 0x2CD19, 0x2D23C, 0x2EC76, 0x2F353,
        0x30103, 0x31E26, 0x3206C, 0x33F49, 0x343DD, 0x35CF8, 0x362B2, 0x37D97,
        0x384BF, 0x39B9A, 0x3A5D0, 0x3BAF5, 0x3C661, 0x3D944, 0x3E70E, 0x3F82B,
        0x003AE, 0x01C8B, 0x022C1, 0x03DE4, 0x04170, 0x05E55, 0x0601F, 0x07F3A,
        0x08612, 0x09937, 0x0A77D, 0x0B858, 0x0C4CC, 0x0DBE9, 0x0E5A3, 0x0FA86,
        0x108D6, 0x117F3, 0x129B9, 0x1369C, 0x14A08, 0x1552D, 0x16B67, 0x17442,
        0x18D6A, 0x1924F, 0x1AC05, 0x1B320, 0x1CFB4, 0x1D091, 0x1EEDB, 0x1F1FE,
    ];

    public static int GetFinderSideFormatInfo(ErrorCorrectionLevel ecLevel, int version)
    {
        return FinderSideMaskedPatterns[GetPatternIndex(ecLevel, version)];
    }

    public static int GetSubFinderSideFormatInfo(ErrorCorrectionLevel ecLevel, int version)
    {
        return SubFinderSideMaskedPatterns[GetPatternIndex(ecLevel, version)];
    }

    private static int GetPatternIndex(ErrorCorrectionLevel ecLevel, int version)
    {
        if (version < 1 || version > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "rMQR version must be in the range 1-32.");
        }

        var versionIndex = version - 1;
        return ecLevel switch
        {
            ErrorCorrectionLevel.M => versionIndex,
            ErrorCorrectionLevel.H => versionIndex + 32,
            _ => throw new ArgumentOutOfRangeException(nameof(ecLevel), $"rMQR only supports EC levels M and H, got {ecLevel}."),
        };
    }
}
