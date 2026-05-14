namespace Meziantou.Framework.Internal.Barcodes;

internal static class CodabarEncoder
{
    private const string PayloadCharacters = "0123456789-$:/.+";

    private static readonly Dictionary<char, int> CharacterEncodings = new()
    {
        ['0'] = 0x003,
        ['1'] = 0x006,
        ['2'] = 0x009,
        ['3'] = 0x060,
        ['4'] = 0x012,
        ['5'] = 0x042,
        ['6'] = 0x021,
        ['7'] = 0x024,
        ['8'] = 0x030,
        ['9'] = 0x048,
        ['-'] = 0x00C,
        ['$'] = 0x018,
        [':'] = 0x045,
        ['/'] = 0x051,
        ['.'] = 0x054,
        ['+'] = 0x015,
        ['A'] = 0x01A,
        ['B'] = 0x029,
        ['C'] = 0x00B,
        ['D'] = 0x00E,
    };

    public static Barcode Encode(string data, char startCharacter, char stopCharacter)
    {
        var normalizedStartCharacter = NormalizeGuardCharacter(startCharacter, nameof(startCharacter));
        var normalizedStopCharacter = NormalizeGuardCharacter(stopCharacter, nameof(stopCharacter));

        foreach (var character in data)
        {
            if (PayloadCharacters.IndexOf(character) < 0)
            {
                throw new ArgumentException($"The character '{character}' is not supported by Codabar.", nameof(data));
            }
        }

        var modules = new List<bool>((data.Length + 2) * 12);
        var symbols = string.Concat(normalizedStartCharacter, data, normalizedStopCharacter);
        for (var i = 0; i < symbols.Length; i++)
        {
            AppendCharacter(modules, symbols[i]);
            if (i + 1 < symbols.Length)
            {
                BarcodeEncoderHelper.AppendModules(modules, isDark: false, 1);
            }
        }

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Codabar, modules);
    }

    private static char NormalizeGuardCharacter(char character, string parameterName)
    {
        var normalizedCharacter = char.ToUpperInvariant(character);
        return normalizedCharacter switch
        {
            'A' or 'T' => 'A',
            'B' or 'N' => 'B',
            'C' or '*' => 'C',
            'D' or 'E' => 'D',
            _ => throw new ArgumentException($"The guard character '{character}' is not supported. Use A, B, C, or D.", parameterName),
        };
    }

    private static void AppendCharacter(List<bool> modules, char character)
    {
        if (!CharacterEncodings.TryGetValue(character, out var encoding))
        {
            throw new ArgumentException($"The character '{character}' is not supported by Codabar.", nameof(character));
        }

        var isDark = true;
        for (var i = 0; i < 7; i++)
        {
            var isWide = ((encoding >> (6 - i)) & 1) == 1;
            BarcodeEncoderHelper.AppendModules(modules, isDark, isWide ? 2 : 1);
            isDark = !isDark;
        }
    }
}
