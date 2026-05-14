using System.Text;

namespace Meziantou.Framework.Internal.Barcodes;

internal static class Code93Encoder
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%abcd*";

    private static readonly int[] CharacterEncodings =
    [
        0x114, 0x148, 0x144, 0x142, 0x128, 0x124, 0x122, 0x150, 0x112, 0x10A, // 0-9
        0x1A8, 0x1A4, 0x1A2, 0x194, 0x192, 0x18A, 0x168, 0x164, 0x162, 0x134, // A-J
        0x11A, 0x158, 0x14C, 0x146, 0x12C, 0x116, 0x1B4, 0x1B2, 0x1AC, 0x1A6, // K-T
        0x196, 0x19A, 0x16C, 0x166, 0x136, 0x13A, // U-Z
        0x12E, 0x1D4, 0x1D2, 0x1CA, 0x16E, 0x176, 0x1AE, // - - %
        0x126, 0x1DA, 0x1D6, 0x132, 0x15E, // a-d, *
    ];

    private const int AsteriskEncoding = 0x15E;

    public static Barcode Encode(string data)
    {
        var encodedData = ConvertToExtended(data);
        var modules = new List<bool>((encodedData.Length + 4) * 9 + 1);

        AppendCharacter(modules, AsteriskEncoding);
        foreach (var character in encodedData)
        {
            var indexInAlphabet = Alphabet.IndexOf(character);
            if (indexInAlphabet < 0)
            {
                throw new ArgumentException($"The character '{character}' is not supported by Code 93.", nameof(data));
            }

            AppendCharacter(modules, CharacterEncodings[indexInAlphabet]);
        }

        var firstChecksumIndex = ComputeChecksumIndex(encodedData, 20);
        var firstChecksumCharacter = Alphabet[firstChecksumIndex];
        AppendCharacter(modules, CharacterEncodings[firstChecksumIndex]);

        var secondChecksumIndex = ComputeChecksumIndex(string.Concat(encodedData, firstChecksumCharacter), 15);
        AppendCharacter(modules, CharacterEncodings[secondChecksumIndex]);

        AppendCharacter(modules, AsteriskEncoding);
        modules.Add(true);

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Code93, modules);
    }

    private static int ComputeChecksumIndex(string data, int maxWeight)
    {
        var weight = 1;
        var total = 0;
        for (var i = data.Length - 1; i >= 0; i--)
        {
            var indexInAlphabet = Alphabet.IndexOf(data[i]);
            total += indexInAlphabet * weight;
            if (++weight > maxWeight)
            {
                weight = 1;
            }
        }

        return total % 47;
    }

    private static void AppendCharacter(List<bool> modules, int encoding)
    {
        for (var i = 8; i >= 0; i--)
        {
            modules.Add(((encoding >> i) & 1) != 0);
        }
    }

    // Uses the standard Code 93 extended encoding with shift symbols a-d.
    private static string ConvertToExtended(string data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var character in data)
        {
            if (character == 0)
            {
                sb.Append("bU");
            }
            else if (character <= 26)
            {
                sb.Append('a');
                sb.Append((char)('A' + character - 1));
            }
            else if (character <= 31)
            {
                sb.Append('b');
                sb.Append((char)('A' + character - 27));
            }
            else if (character is ' ' or '$' or '%' or '+')
            {
                sb.Append(character);
            }
            else if (character <= ',')
            {
                sb.Append('c');
                sb.Append((char)('A' + character - '!'));
            }
            else if (character <= '9')
            {
                sb.Append(character);
            }
            else if (character == ':')
            {
                sb.Append("cZ");
            }
            else if (character <= '?')
            {
                sb.Append('b');
                sb.Append((char)('F' + character - ';'));
            }
            else if (character == '@')
            {
                sb.Append("bV");
            }
            else if (character <= 'Z')
            {
                sb.Append(character);
            }
            else if (character <= '_')
            {
                sb.Append('b');
                sb.Append((char)('K' + character - '['));
            }
            else if (character == '`')
            {
                sb.Append("bW");
            }
            else if (character <= 'z')
            {
                sb.Append('d');
                sb.Append((char)('A' + character - 'a'));
            }
            else if (character <= 127)
            {
                sb.Append('b');
                sb.Append((char)('P' + character - '{'));
            }
            else
            {
                throw new ArgumentException($"The character '{character}' is not supported by Code 93.", nameof(data));
            }
        }

        return sb.ToString();
    }
}
