namespace Meziantou.Framework.Barcodes.Internal;

internal static class DataAnalyzer
{
    private const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private static readonly Encoding ShiftJis = GetShiftJisEncoding();

    private static Encoding GetShiftJisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Explicit fallbacks disable the best-fit mappings of the default encoder, which encodes characters that have no
        // Shift JIS code, such as U+00A2 (¢), as a similar one (0x8191, which decodes as U+FFE0 ￠). The symbol would then
        // not give back the original text, and two different texts would give the same symbol.
        return Encoding.GetEncoding("shift_jis", new EncoderReplacementFallback(""), new DecoderReplacementFallback(""));
    }

    public static EncodingMode DetermineMode(string data) => DetermineMode(data, allowKanji: true);

    public static EncodingMode DetermineMode(string data, bool allowKanji)
    {
        if (IsNumeric(data))
        {
            return EncodingMode.Numeric;
        }

        if (IsAlphanumeric(data))
        {
            return EncodingMode.Alphanumeric;
        }

        if (allowKanji && IsKanji(data))
        {
            return EncodingMode.Kanji;
        }

        return EncodingMode.Byte;
    }

    public static EncodingMode DetermineMode(ReadOnlySpan<byte> data)
    {
        _ = data;

        // Binary data always uses byte mode
        return EncodingMode.Byte;
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
        var index = AlphanumericChars.IndexOf(c, StringComparison.Ordinal);
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
            if (!IsAlphanumeric(c))
            {
                return false;
            }
        }

        return data.Length > 0;
    }

    public static bool IsAlphanumeric(char c) => AlphanumericChars.Contains(c, StringComparison.Ordinal);

    /// <summary>
    /// Checks if a text without an ECI designator can be split into segments: its byte segments hold UTF-8 that readers
    /// decode by guessing the encoding of each segment, and a short UTF-8 segment can be guessed as another encoding
    /// (zbarimg reads "Café " as EUC-JP). The segments are safe when the text only has ASCII and Kanji characters.
    /// </summary>
    public static bool CanSegmentWithoutEci(string data)
    {
        foreach (var c in data)
        {
            if (!char.IsAscii(c) && !IsKanji(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if the entire string can be encoded in Kanji mode.
    /// </summary>
    private static bool IsKanji(string data)
    {
        if (data.Length == 0)
        {
            return false;
        }

        foreach (var c in data)
        {
            if (!IsKanji(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a character can be encoded in Kanji mode, so that every reader gives it back.
    /// </summary>
    /// <remarks>
    /// The character must encode to 2 bytes in Shift JIS, in the QR Kanji ranges, and decode to itself. Readers decode
    /// Kanji mode with either the JIS X 0208 table or its Microsoft variant (code page 932, which .NET uses), so the
    /// character must also be in JIS X 0208 (the Microsoft extensions are not) and have the same code point in both tables.
    /// </remarks>
    public static bool IsKanji(char c)
    {
        if (char.IsSurrogate(c))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];
        var byteCount = ShiftJis.GetBytes(new ReadOnlySpan<char>(in c), bytes);
        if (byteCount != 2)
        {
            return false;
        }

        var value = (bytes[0] << 8) | bytes[1];
        if (value is not ((>= 0x8140 and <= 0x9FFC) or (>= 0xE040 and <= 0xEBBF)))
        {
            return false;
        }

        // The rows of JIS X 0208: 1 to 8 (symbols, letters, kana), 16 to 84 (kanji). Row 13 holds the NEC special characters.
        var row = ((bytes[0] - (bytes[0] <= 0x9F ? 0x81 : 0xC1)) * 2) + (bytes[1] >= 0x9F ? 2 : 1);
        if (row is (> 8 and < 16) or > 84)
        {
            return false;
        }

        // The codes that readers decode to different characters, depending on their table: ― — \ ～ 〜 ‖ ∥ − － ¢ ￠ £ ￡ ¬ ￢
        if (value is 0x815C or 0x815F or 0x8160 or 0x8161 or 0x817C or 0x8191 or 0x8192 or 0x81CA)
        {
            return false;
        }

        Span<char> decoded = stackalloc char[2];
        return ShiftJis.GetChars(bytes[..2], decoded) == 1 && decoded[0] == c;
    }

    public static byte[] GetShiftJisBytes(string data)
    {
        return ShiftJis.GetBytes(data);
    }
}
