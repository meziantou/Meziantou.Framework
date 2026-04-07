using System.Text;

namespace Meziantou.Framework.Internal.MicroQR;

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

    // Character capacity per version-EC-mode combination
    public static int GetCharacterCapacity(int version, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        return (version, ecLevel, mode) switch
        {
            (1, _, EncodingMode.Numeric) => 5,
            (2, ErrorCorrectionLevel.L, EncodingMode.Numeric) => 10,
            (2, ErrorCorrectionLevel.L, EncodingMode.Alphanumeric) => 6,
            (2, ErrorCorrectionLevel.M, EncodingMode.Numeric) => 8,
            (2, ErrorCorrectionLevel.M, EncodingMode.Alphanumeric) => 5,
            (3, ErrorCorrectionLevel.L, EncodingMode.Numeric) => 23,
            (3, ErrorCorrectionLevel.L, EncodingMode.Alphanumeric) => 14,
            (3, ErrorCorrectionLevel.L, EncodingMode.Byte) => 9,
            (3, ErrorCorrectionLevel.L, EncodingMode.Kanji) => 6,
            (3, ErrorCorrectionLevel.M, EncodingMode.Numeric) => 18,
            (3, ErrorCorrectionLevel.M, EncodingMode.Alphanumeric) => 11,
            (3, ErrorCorrectionLevel.M, EncodingMode.Byte) => 7,
            (3, ErrorCorrectionLevel.M, EncodingMode.Kanji) => 4,
            (4, ErrorCorrectionLevel.L, EncodingMode.Numeric) => 35,
            (4, ErrorCorrectionLevel.L, EncodingMode.Alphanumeric) => 21,
            (4, ErrorCorrectionLevel.L, EncodingMode.Byte) => 15,
            (4, ErrorCorrectionLevel.L, EncodingMode.Kanji) => 9,
            (4, ErrorCorrectionLevel.M, EncodingMode.Numeric) => 30,
            (4, ErrorCorrectionLevel.M, EncodingMode.Alphanumeric) => 18,
            (4, ErrorCorrectionLevel.M, EncodingMode.Byte) => 13,
            (4, ErrorCorrectionLevel.M, EncodingMode.Kanji) => 8,
            (4, ErrorCorrectionLevel.Q, EncodingMode.Numeric) => 21,
            (4, ErrorCorrectionLevel.Q, EncodingMode.Alphanumeric) => 13,
            (4, ErrorCorrectionLevel.Q, EncodingMode.Byte) => 9,
            (4, ErrorCorrectionLevel.Q, EncodingMode.Kanji) => 5,
            _ => 0, // Unsupported combination
        };
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
            (1, _) => true, // M1 only has error detection, accept any EC level
            (2, ErrorCorrectionLevel.L or ErrorCorrectionLevel.M) => true,
            (3, ErrorCorrectionLevel.L or ErrorCorrectionLevel.M) => true,
            (4, ErrorCorrectionLevel.L or ErrorCorrectionLevel.M or ErrorCorrectionLevel.Q) => true,
            _ => false,
        };
    }

    // Determine the best version for the given data, EC level, and mode.
    // Returns the smallest version that can hold the data.
    public static int DetermineVersion(string data, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        var charCount = mode switch
        {
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data),
            EncodingMode.Kanji => data.Length,
            _ => data.Length,
        };

        for (var version = 1; version <= 4; version++)
        {
            if (!IsModeSupported(version, mode))
            {
                continue;
            }

            if (!IsECLevelSupported(version, ecLevel))
            {
                continue;
            }

            var capacity = GetCharacterCapacity(version, ecLevel, mode);
            if (capacity >= charCount)
            {
                return version;
            }
        }

        throw new InvalidOperationException("The data is too long to be encoded in a Micro QR code.");
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
