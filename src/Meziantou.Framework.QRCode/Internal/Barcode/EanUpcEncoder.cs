namespace Meziantou.Framework.Internal.Barcodes;

internal static class EanUpcEncoder
{
    private static readonly string[] LPatterns =
    [
        "0001101",
        "0011001",
        "0010011",
        "0111101",
        "0100011",
        "0110001",
        "0101111",
        "0111011",
        "0110111",
        "0001011",
    ];

    private static readonly string[] GPatterns =
    [
        "0100111",
        "0110011",
        "0011011",
        "0100001",
        "0011101",
        "0111001",
        "0000101",
        "0010001",
        "0001001",
        "0010111",
    ];

    private static readonly string[] RPatterns =
    [
        "1110010",
        "1100110",
        "1101100",
        "1000010",
        "1011100",
        "1001110",
        "1010000",
        "1000100",
        "1001000",
        "1110100",
    ];

    private static readonly int[] Ean13FirstDigitEncodings = [0x00, 0x0B, 0x0D, 0x0E, 0x13, 0x19, 0x1C, 0x15, 0x16, 0x1A];
    private static readonly int[] Extension5CheckDigitEncodings = [0x18, 0x14, 0x12, 0x11, 0x0C, 0x06, 0x03, 0x0A, 0x09, 0x05];

    public static Barcode EncodeEan8(string data, string? extension)
    {
        var normalized = NormalizeAndValidate(data, 7, 8, "EAN-8");
        var modules = new List<bool>(67 + GetExtensionModuleCount(extension));

        AppendPattern(modules, "101");
        for (var i = 0; i < 4; i++)
        {
            AppendPattern(modules, LPatterns[DigitToInt(normalized[i], nameof(data), "EAN-8")]);
        }

        AppendPattern(modules, "01010");
        for (var i = 4; i < 8; i++)
        {
            AppendPattern(modules, RPatterns[DigitToInt(normalized[i], nameof(data), "EAN-8")]);
        }

        AppendPattern(modules, "101");
        AppendExtension(modules, extension);

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Ean8, modules);
    }

    public static Barcode EncodeEan13(string data, string? extension)
    {
        var normalized = NormalizeAndValidate(data, 12, 13, "EAN-13");
        var modules = new List<bool>(95 + GetExtensionModuleCount(extension));

        AppendPattern(modules, "101");

        var firstDigit = DigitToInt(normalized[0], nameof(data), "EAN-13");
        var parity = Ean13FirstDigitEncodings[firstDigit];
        for (var i = 1; i <= 6; i++)
        {
            var digit = DigitToInt(normalized[i], nameof(data), "EAN-13");
            var useGPattern = ((parity >> (6 - i)) & 1) == 1;
            AppendPattern(modules, useGPattern ? GPatterns[digit] : LPatterns[digit]);
        }

        AppendPattern(modules, "01010");
        for (var i = 7; i <= 12; i++)
        {
            var digit = DigitToInt(normalized[i], nameof(data), "EAN-13");
            AppendPattern(modules, RPatterns[digit]);
        }

        AppendPattern(modules, "101");
        AppendExtension(modules, extension);

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Ean13, modules);
    }

    public static Barcode EncodeUpcA(string data, string? extension)
    {
        var normalized = NormalizeAndValidate(data, 11, 12, "UPC-A");
        var modules = new List<bool>(95 + GetExtensionModuleCount(extension));

        AppendPattern(modules, "101");
        for (var i = 0; i < 6; i++)
        {
            AppendPattern(modules, LPatterns[DigitToInt(normalized[i], nameof(data), "UPC-A")]);
        }

        AppendPattern(modules, "01010");
        for (var i = 6; i < 12; i++)
        {
            AppendPattern(modules, RPatterns[DigitToInt(normalized[i], nameof(data), "UPC-A")]);
        }

        AppendPattern(modules, "101");
        AppendExtension(modules, extension);

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.UpcA, modules);
    }

    private static string NormalizeAndValidate(string data, int lengthWithoutChecksum, int lengthWithChecksum, string symbology)
    {
        ValidateDigits(data, nameof(data), symbology);

        if (data.Length == lengthWithoutChecksum)
        {
            var checksum = ComputeChecksumDigit(data.AsSpan());
            return string.Concat(data, (char)('0' + checksum));
        }

        if (data.Length == lengthWithChecksum)
        {
            var expectedChecksum = ComputeChecksumDigit(data.AsSpan(0, lengthWithoutChecksum));
            var actualChecksum = DigitToInt(data[lengthWithChecksum - 1], nameof(data), symbology);
            if (expectedChecksum != actualChecksum)
            {
                throw new ArgumentException($"The data is not a valid {symbology} value because the checksum digit is invalid.", nameof(data));
            }

            return data;
        }

        throw new ArgumentException($"The data should be {lengthWithoutChecksum} or {lengthWithChecksum} digits long for {symbology}.", nameof(data));
    }

    private static int ComputeChecksumDigit(ReadOnlySpan<char> data)
    {
        var sum = 0;
        var useWeight3 = true;
        for (var i = data.Length - 1; i >= 0; i--)
        {
            var digit = DigitToInt(data[i], nameof(data), "UPC/EAN");
            sum += digit * (useWeight3 ? 3 : 1);
            useWeight3 = !useWeight3;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static void AppendExtension(List<bool> modules, string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return;

        ValidateDigits(extension, nameof(extension), "UPC/EAN extension");
        if (extension.Length is not (2 or 5))
        {
            throw new ArgumentException("The extension should be 2 or 5 digits long.", nameof(extension));
        }

        BarcodeEncoderHelper.AppendModules(modules, isDark: false, 7);
        AppendPattern(modules, "1011");

        if (extension.Length == 2)
        {
            EncodeTwoDigitExtension(modules, extension);
        }
        else
        {
            EncodeFiveDigitExtension(modules, extension);
        }
    }

    private static void EncodeTwoDigitExtension(List<bool> modules, string extension)
    {
        var parity = ((extension[0] - '0') * 10 + (extension[1] - '0')) % 4;

        for (var i = 0; i < extension.Length; i++)
        {
            if (i > 0)
            {
                AppendPattern(modules, "01");
            }

            var digit = DigitToInt(extension[i], nameof(extension), "UPC/EAN extension");
            var useGPattern = ((parity >> (1 - i)) & 1) == 1;
            AppendPattern(modules, useGPattern ? GPatterns[digit] : LPatterns[digit]);
        }
    }

    private static void EncodeFiveDigitExtension(List<bool> modules, string extension)
    {
        var checksum = ComputeFiveDigitExtensionChecksum(extension);
        var parity = Extension5CheckDigitEncodings[checksum];

        for (var i = 0; i < extension.Length; i++)
        {
            if (i > 0)
            {
                AppendPattern(modules, "01");
            }

            var digit = DigitToInt(extension[i], nameof(extension), "UPC/EAN extension");
            var useGPattern = ((parity >> (4 - i)) & 1) == 1;
            AppendPattern(modules, useGPattern ? GPatterns[digit] : LPatterns[digit]);
        }
    }

    private static int ComputeFiveDigitExtensionChecksum(string extension)
    {
        var sum = 0;
        for (var i = extension.Length - 2; i >= 0; i -= 2)
        {
            sum += extension[i] - '0';
        }

        sum *= 3;
        for (var i = extension.Length - 1; i >= 0; i -= 2)
        {
            sum += extension[i] - '0';
        }

        sum *= 3;
        return sum % 10;
    }

    private static int GetExtensionModuleCount(string? extension)
    {
        return extension?.Length switch
        {
            2 => 27,
            5 => 54,
            _ => 0,
        };
    }

    private static void AppendPattern(List<bool> modules, string pattern)
    {
        foreach (var bit in pattern)
        {
            modules.Add(bit == '1');
        }
    }

    private static void ValidateDigits(string value, string parameterName, string symbology)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                throw new ArgumentException($"The value contains invalid character '{character}' for {symbology}.", parameterName);
            }
        }
    }

    private static int DigitToInt(char character, string parameterName, string symbology)
    {
        if (!char.IsAsciiDigit(character))
        {
            throw new ArgumentException($"The value contains invalid character '{character}' for {symbology}.", parameterName);
        }

        return character - '0';
    }
}
