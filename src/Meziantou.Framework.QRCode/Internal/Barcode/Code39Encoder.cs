namespace Meziantou.Framework.Internal.Barcodes;

internal static class Code39Encoder
{
    private const int NarrowWidth = 1;
    private const int WideWidth = 2;
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    private static readonly Dictionary<char, string> CharacterPatterns = new()
    {
        ['0'] = "nnnwwnwnn",
        ['1'] = "wnnwnnnnw",
        ['2'] = "nnwwnnnnw",
        ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw",
        ['5'] = "wnnwwnnnn",
        ['6'] = "nnwwwnnnn",
        ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn",
        ['9'] = "nnwwnnwnn",
        ['A'] = "wnnnnwnnw",
        ['B'] = "nnwnnwnnw",
        ['C'] = "wnwnnwnnn",
        ['D'] = "nnnnwwnnw",
        ['E'] = "wnnnwwnnn",
        ['F'] = "nnwnwwnnn",
        ['G'] = "nnnnnwwnw",
        ['H'] = "wnnnnwwnn",
        ['I'] = "nnwnnwwnn",
        ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww",
        ['L'] = "nnwnnnnww",
        ['M'] = "wnwnnnnwn",
        ['N'] = "nnnnwnnww",
        ['O'] = "wnnnwnnwn",
        ['P'] = "nnwnwnnwn",
        ['Q'] = "nnnnnnwww",
        ['R'] = "wnnnnnwwn",
        ['S'] = "nnwnnnwwn",
        ['T'] = "nnnnwnwwn",
        ['U'] = "wwnnnnnnw",
        ['V'] = "nwwnnnnnw",
        ['W'] = "wwwnnnnnn",
        ['X'] = "nwnnwnnnw",
        ['Y'] = "wwnnwnnnn",
        ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw",
        ['.'] = "wwnnnnwnn",
        [' '] = "nwwnnnwnn",
        ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn",
        ['+'] = "nwnnnwnwn",
        ['%'] = "nnnwnwnwn",
        ['*'] = "nwnnwnwnn",
    };

    public static Barcode Encode(string data, bool includeChecksum)
    {
        ValidateData(data);

        var symbols = new List<char>(data.Length + 3)
        {
            '*',
        };
        symbols.AddRange(data);
        if (includeChecksum)
        {
            symbols.Add(GetChecksumCharacter(data));
        }

        symbols.Add('*');

        var modules = new List<bool>(symbols.Count * 16);
        for (var i = 0; i < symbols.Count; i++)
        {
            AppendCharacter(modules, symbols[i]);
            if (i + 1 < symbols.Count)
            {
                BarcodeEncoderHelper.AppendModules(modules, isDark: false, NarrowWidth);
            }
        }

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Code39, modules);
    }

    private static void ValidateData(string data)
    {
        foreach (var character in data)
        {
            if (!CharacterPatterns.ContainsKey(character) || character == '*')
            {
                throw new ArgumentException($"The character '{character}' is not supported by Code 39.", nameof(data));
            }
        }
    }

    private static char GetChecksumCharacter(string data)
    {
        var checksum = 0;
        foreach (var character in data)
        {
            checksum += Alphabet.IndexOf(character, StringComparison.Ordinal);
        }

        return Alphabet[checksum % Alphabet.Length];
    }

    private static void AppendCharacter(List<bool> modules, char character)
    {
        var pattern = CharacterPatterns[character];
        for (var i = 0; i < pattern.Length; i++)
        {
            var width = pattern[i] == 'w' ? WideWidth : NarrowWidth;
            var isDark = i % 2 == 0;
            BarcodeEncoderHelper.AppendModules(modules, isDark, width);
        }
    }
}
