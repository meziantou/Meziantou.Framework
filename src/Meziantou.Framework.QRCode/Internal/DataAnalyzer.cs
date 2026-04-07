using System.Text;

namespace Meziantou.Framework.Internal;

internal static class DataAnalyzer
{
    private const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private static readonly Encoding ShiftJis = GetShiftJisEncoding();

    private static Encoding GetShiftJisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("shift_jis");
    }

    public static EncodingMode DetermineMode(string data)
    {
        if (IsNumeric(data))
        {
            return EncodingMode.Numeric;
        }

        if (IsAlphanumeric(data))
        {
            return EncodingMode.Alphanumeric;
        }

        if (IsKanji(data))
        {
            return EncodingMode.Kanji;
        }

        return EncodingMode.Byte;
    }

    public static EncodingMode DetermineMode(ReadOnlySpan<byte> data)
    {
        // Binary data always uses byte mode
        return EncodingMode.Byte;
    }

    public static int DetermineVersion(string data, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        var charCount = mode switch
        {
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data),
            EncodingMode.Kanji => data.Length,
            _ => data.Length,
        };

        return DetermineVersion(charCount, ecLevel, mode);
    }

    public static int DetermineVersion(int dataLength, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        for (var version = 1; version <= 40; version++)
        {
            var capacity = QRCodeVersion.GetCharacterCapacity(version, ecLevel, mode);
            if (capacity >= dataLength)
            {
                return version;
            }
        }

        throw new InvalidOperationException("The data is too long to be encoded in a QR code.");
    }

    public static int GetAlphanumericValue(char c)
    {
        var index = AlphanumericChars.IndexOf(c);
        if (index < 0)
        {
            throw new InvalidOperationException($"Character '{c}' is not valid in alphanumeric mode.");
        }

        return index;
    }

    private static bool IsNumeric(string data)
    {
        foreach (var c in data)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return data.Length > 0;
    }

    private static bool IsAlphanumeric(string data)
    {
        foreach (var c in data)
        {
            if (!AlphanumericChars.Contains(c))
            {
                return false;
            }
        }

        return data.Length > 0;
    }

    /// <summary>
    /// Checks if the entire string can be encoded in Kanji mode.
    /// Each character must encode to exactly 2 bytes in Shift JIS,
    /// and the resulting value must be in one of the valid QR Kanji ranges.
    /// </summary>
    private static bool IsKanji(string data)
    {
        if (data.Length == 0)
        {
            return false;
        }

        var singleCharBuffer = new char[1];
        var byteBuffer = new byte[4];

        foreach (var c in data)
        {
            singleCharBuffer[0] = c;
            var byteCount = ShiftJis.GetBytes(singleCharBuffer, 0, 1, byteBuffer, 0);
            if (byteCount != 2)
            {
                return false;
            }

            var value = (byteBuffer[0] << 8) | byteBuffer[1];
            if (value is not ((>= 0x8140 and <= 0x9FFC) or (>= 0xE040 and <= 0xEBBF)))
            {
                return false;
            }
        }

        return true;
    }

    public static byte[] GetShiftJisBytes(string data)
    {
        return ShiftJis.GetBytes(data);
    }
}
