namespace Meziantou.Framework.Barcodes.Internal.MicroQR;

internal static class MicroQRVersion
{
    // Micro QR versions are 1-4 (M1-M4)
    // Size = 2 * version + 9

    public static int GetSideLength(int version) => (2 * version) + 9;

    // Total codewords per version: M1=5, M2=10, M3=17, M4=24
    public static int GetTotalCodewords(int version) => version switch
    {
        1 => 5,
        2 => 10,
        3 => 17,
        4 => 24,
        _ => throw new ArgumentOutOfRangeException(nameof(version), $"Micro QR version must be 1-4, got {version}."),
    };

    // Data codewords indexed by [version-1, ecLevel]
    // Not all EC levels are valid for all versions.
    // M1: only "detection" (we use L as placeholder, 3 data CW)
    // M2: L=5, M=4
    // M3: L=11, M=9
    // M4: L=16, M=14, Q=10
    public static int GetDataCodewords(int version, ErrorCorrectionLevel ecLevel)
    {
        return (version, ecLevel) switch
        {
            (1, _) => 3,
            (2, ErrorCorrectionLevel.L) => 5,
            (2, ErrorCorrectionLevel.M) => 4,
            (3, ErrorCorrectionLevel.L) => 11,
            (3, ErrorCorrectionLevel.M) => 9,
            (4, ErrorCorrectionLevel.L) => 16,
            (4, ErrorCorrectionLevel.M) => 14,
            (4, ErrorCorrectionLevel.Q) => 10,
            _ => throw new InvalidOperationException($"Invalid Micro QR version {version} with EC level {ecLevel}."),
        };
    }

    public static int GetECCodewords(int version, ErrorCorrectionLevel ecLevel)
    {
        return GetTotalCodewords(version) - GetDataCodewords(version, ecLevel);
    }

    // Mode indicator bit length: version - 1
    public static int GetModeIndicatorBits(int version) => version - 1;

    // Mode indicator values for each version/mode combination
    public static int GetModeIndicatorValue(int version, EncodingMode mode)
    {
        return (version, mode) switch
        {
            (1, EncodingMode.Numeric) => 0, // No indicator needed (0 bits)
            (2, EncodingMode.Numeric) => 0,
            (2, EncodingMode.Alphanumeric) => 1,
            (3, EncodingMode.Numeric) => 0,
            (3, EncodingMode.Alphanumeric) => 1,
            (3, EncodingMode.Byte) => 2,
            (3, EncodingMode.Kanji) => 3,
            (4, EncodingMode.Numeric) => 0,
            (4, EncodingMode.Alphanumeric) => 1,
            (4, EncodingMode.Byte) => 2,
            (4, EncodingMode.Kanji) => 3,
            _ => throw new InvalidOperationException($"Mode {mode} is not supported in Micro QR version M{version}."),
        };
    }

    // Character count indicator bits per version and mode
    public static int GetCharacterCountBits(int version, EncodingMode mode)
    {
        return (version, mode) switch
        {
            (1, EncodingMode.Numeric) => 3,
            (2, EncodingMode.Numeric) => 4,
            (2, EncodingMode.Alphanumeric) => 3,
            (3, EncodingMode.Numeric) => 5,
            (3, EncodingMode.Alphanumeric) => 4,
            (3, EncodingMode.Byte) => 4,
            (3, EncodingMode.Kanji) => 3,
            (4, EncodingMode.Numeric) => 6,
            (4, EncodingMode.Alphanumeric) => 5,
            (4, EncodingMode.Byte) => 5,
            (4, EncodingMode.Kanji) => 4,
            _ => throw new InvalidOperationException($"Mode {mode} is not supported in Micro QR version M{version}."),
        };
    }

    // Terminator bits: M1=3, M2=5, M3=7, M4=9
    public static int GetTerminatorBits(int version) => (version * 2) + 1;

    // The number of data bits: in M1 and M3 the last data codeword has 4 bits (ISO/IEC 18004, 7.4.10)
    public static int GetDataBitCapacity(int version, ErrorCorrectionLevel ecLevel)
    {
        var bits = GetDataCodewords(version, ecLevel) * 8;
        return version % 2 == 1 ? bits - 4 : bits;
    }

    // Symbol number for format information encoding
    // M1=0, M2-L=1, M2-M=2, M3-L=3, M3-M=4, M4-L=5, M4-M=6, M4-Q=7
    public static int GetSymbolNumber(int version, ErrorCorrectionLevel ecLevel)
    {
        return (version, ecLevel) switch
        {
            (1, _) => 0,
            (2, ErrorCorrectionLevel.L) => 1,
            (2, ErrorCorrectionLevel.M) => 2,
            (3, ErrorCorrectionLevel.L) => 3,
            (3, ErrorCorrectionLevel.M) => 4,
            (4, ErrorCorrectionLevel.L) => 5,
            (4, ErrorCorrectionLevel.M) => 6,
            (4, ErrorCorrectionLevel.Q) => 7,
            _ => throw new InvalidOperationException($"Invalid Micro QR version {version} with EC level {ecLevel}."),
        };
    }

    // Check whether the given mode is supported in the given version
    public static bool IsModeSupported(int version, EncodingMode mode)
    {
        return (version, mode) switch
        {
            (1, EncodingMode.Numeric) => true,
            (2, EncodingMode.Numeric or EncodingMode.Alphanumeric) => true,
            (3 or 4, EncodingMode.Numeric or EncodingMode.Alphanumeric or EncodingMode.Byte or EncodingMode.Kanji) => true,
            _ => false,
        };
    }

    // Check whether the given EC level is supported in the given version
    public static bool IsECLevelSupported(int version, ErrorCorrectionLevel ecLevel)
    {
        return (version, ecLevel) switch
        {
            // M1 carries error detection only, so it cannot satisfy a request for M or Q. Treating
            // it as satisfying every level silently handed back a symbol with no error correction.
            (1, ErrorCorrectionLevel.L) => true,
            (2, ErrorCorrectionLevel.L or ErrorCorrectionLevel.M) => true,
            (3, ErrorCorrectionLevel.L or ErrorCorrectionLevel.M) => true,
            (4, ErrorCorrectionLevel.L or ErrorCorrectionLevel.M or ErrorCorrectionLevel.Q) => true,
            _ => false,
        };
    }

    // Check whether a version of the range supports the given EC level
    public static bool IsECLevelSupported(int minVersion, int maxVersion, ErrorCorrectionLevel ecLevel)
    {
        for (var version = minVersion; version <= maxVersion; version++)
        {
            if (IsECLevelSupported(version, ecLevel))
                return true;
        }

        return false;
    }

    // Resolve the effective EC level for M1 (which only supports error detection)
    public static ErrorCorrectionLevel ResolveECLevel(int version, ErrorCorrectionLevel requested)
    {
        if (version == 1)
        {
            // M1 only supports error detection; map any requested level to L
            return ErrorCorrectionLevel.L;
        }

        return requested;
    }
}
