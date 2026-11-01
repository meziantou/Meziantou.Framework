namespace Meziantou.Framework.Barcodes.Internal.Linear;

internal static class Code128Encoder
{
    private const int Shift = 98;
    private const int CodeSetC = 99;
    private const int CodeSetB = 100;
    private const int CodeSetA = 101;
    private const int StartCodeA = 103;
    private const int StartCodeB = 104;
    private const int StartCodeC = 105;
    private const int StopCode = 106;

    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", // 0-9
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132", // 10-19
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", // 20-29
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", // 30-39
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331", // 40-49
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", // 50-59
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214", // 60-69
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111", // 70-79
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", // 80-89
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141", // 90-99
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112", // 100-106
    ];

    public static Barcode Encode(string data)
    {
        foreach (var character in data)
        {
            if (!char.IsAscii(character))
            {
                throw new ArgumentException($"The character '{character}' is not supported by Code 128.", nameof(data));
            }
        }

        var codewords = CreateCodewords(data);
        var modules = EncodeCodewords(codewords);

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Code128, modules, data);
    }

    // Follows the minimal-length guidelines of ISO/IEC 15417 Annex E: code set C for runs of digits, code set A for
    // control characters and code set B for lowercase letters, with a SHIFT for a single character of the other set.
    private static List<int> CreateCodewords(string data)
    {
        var codewords = new List<int>(data.Length + 4);
        var index = 0;
        var currentCodeSet = SelectInitialCodeSet(data);
        codewords.Add(currentCodeSet switch
        {
            CodeSet.A => StartCodeA,
            CodeSet.B => StartCodeB,
            _ => StartCodeC,
        });

        while (index < data.Length)
        {
            if (currentCodeSet == CodeSet.C)
            {
                if (CanEncodeCodeSetCPair(data, index))
                {
                    codewords.Add(GetCodeSetCValue(data, index));
                    index += 2;
                    continue;
                }

                currentCodeSet = SelectAlphanumericCodeSet(data, index);
                codewords.Add(currentCodeSet == CodeSet.A ? CodeSetA : CodeSetB);
                continue;
            }

            var digits = CountConsecutiveDigits(data, index);
            if (ShouldSwitchToCodeSetC(digits, data.Length - index))
            {
                if ((digits & 1) == 1)
                {
                    codewords.Add(GetValue(data[index], currentCodeSet));
                    index++;
                }

                codewords.Add(CodeSetC);
                currentCodeSet = CodeSet.C;
                continue;
            }

            var character = data[index];
            if (!IsInCodeSet(character, currentCodeSet))
            {
                // A single character of the other set is shifted when the next character that only one set holds
                // belongs to the current set: it saves the switch back
                var otherCodeSet = currentCodeSet == CodeSet.A ? CodeSet.B : CodeSet.A;
                if (FindExclusiveCodeSet(data, index + 1) == currentCodeSet)
                {
                    codewords.Add(Shift);
                    codewords.Add(GetValue(character, otherCodeSet));
                    index++;
                    continue;
                }

                codewords.Add(otherCodeSet == CodeSet.A ? CodeSetA : CodeSetB);
                currentCodeSet = otherCodeSet;
            }

            codewords.Add(GetValue(character, currentCodeSet));
            index++;
        }

        codewords.Add(CalculateChecksum(codewords));
        codewords.Add(StopCode);

        return codewords;
    }

    private static List<bool> EncodeCodewords(List<int> codewords)
    {
        var modules = new List<bool>(codewords.Count * 11);
        foreach (var codeword in codewords)
        {
            var pattern = Patterns[codeword];
            var isBar = true;
            foreach (var widthChar in pattern)
            {
                var width = widthChar - '0';
                BarcodeEncoderHelper.AppendModules(modules, isBar, width);
                isBar = !isBar;
            }
        }

        return modules;
    }

    private static int SelectInitialCodeSet(string data)
    {
        var leadingDigits = CountConsecutiveDigits(data, 0);
        if (leadingDigits >= 4 || (leadingDigits == data.Length && leadingDigits >= 2))
        {
            return CodeSet.C;
        }

        return SelectAlphanumericCodeSet(data, 0);
    }

    // Code set A when a control character comes before any lowercase letter, otherwise code set B
    private static int SelectAlphanumericCodeSet(string data, int startIndex)
    {
        return FindExclusiveCodeSet(data, startIndex) == CodeSet.A ? CodeSet.A : CodeSet.B;
    }

    // Returns the code set of the first character from startIndex that only one of code sets A and B holds, or -1
    private static int FindExclusiveCodeSet(string data, int startIndex)
    {
        for (var i = startIndex; i < data.Length; i++)
        {
            var character = data[i];
            if (character < ' ')
                return CodeSet.A;

            if (character >= '`')
                return CodeSet.B;
        }

        return -1;
    }

    private static bool IsInCodeSet(char character, int codeSet)
    {
        return codeSet == CodeSet.A ? character < '`' : character >= ' ';
    }

    private static bool ShouldSwitchToCodeSetC(int digits, int remainingLength)
    {
        return digits >= 4 || (digits >= 2 && digits == remainingLength);
    }

    private static bool CanEncodeCodeSetCPair(string data, int index)
    {
        return index + 1 < data.Length
            && char.IsAsciiDigit(data[index])
            && char.IsAsciiDigit(data[index + 1]);
    }

    private static int GetCodeSetCValue(string data, int index)
    {
        return ((data[index] - '0') * 10) + (data[index + 1] - '0');
    }

    private static int GetValue(char character, int codeSet)
    {
        // Code set A holds the characters 0x20-0x5F as values 0-63 and the control characters as values 64-95;
        // code set B holds the characters 0x20-0x7F as values 0-95
        return codeSet == CodeSet.A && character < ' ' ? character + 64 : character - ' ';
    }

    private static int CalculateChecksum(List<int> codewords)
    {
        // Reduce at each step: the weighted sum of a long symbol overflows an int
        var checksum = codewords[0] % 103;
        for (var i = 1; i < codewords.Count; i++)
        {
            checksum = (checksum + (codewords[i] * (i % 103))) % 103;
        }

        return checksum;
    }

    private static int CountConsecutiveDigits(string data, int startIndex)
    {
        var count = 0;
        for (var i = startIndex; i < data.Length; i++)
        {
            if (!char.IsAsciiDigit(data[i]))
                break;

            count++;
        }

        return count;
    }

    private static class CodeSet
    {
        public const int A = 0;
        public const int B = 1;
        public const int C = 2;
    }
}
