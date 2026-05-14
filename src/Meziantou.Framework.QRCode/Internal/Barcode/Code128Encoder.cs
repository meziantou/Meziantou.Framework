namespace Meziantou.Framework.Internal.Barcodes;

internal static class Code128Encoder
{
    private const int CodeSetC = 99;
    private const int CodeSetB = 100;
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
        var codewords = CreateCodewords(data);
        var modules = EncodeCodewords(codewords);

        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Code128, modules);
    }

    private static List<int> CreateCodewords(string data)
    {
        var codewords = new List<int>(data.Length + 4);
        var index = 0;
        var currentCodeSet = SelectInitialCodeSet(data);
        codewords.Add(currentCodeSet == CodeSet.C ? StartCodeC : StartCodeB);

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

                codewords.Add(CodeSetB);
                currentCodeSet = CodeSet.B;
                continue;
            }

            var digits = CountConsecutiveDigits(data, index);
            if (ShouldSwitchToCodeSetC(digits, data.Length - index))
            {
                if ((digits & 1) == 1)
                {
                    codewords.Add(GetCodeSetBValue(data[index], nameof(data)));
                    index++;
                }

                codewords.Add(CodeSetC);
                currentCodeSet = CodeSet.C;
                continue;
            }

            codewords.Add(GetCodeSetBValue(data[index], nameof(data)));
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

        return CodeSet.B;
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

    private static int GetCodeSetBValue(char character, string parameterName)
    {
        if (character is < ' ' or > '\u007f')
        {
            throw new ArgumentException($"The character '{character}' is not supported by Code 128.", parameterName);
        }

        return character - ' ';
    }

    private static int CalculateChecksum(List<int> codewords)
    {
        var checksum = codewords[0];
        for (var i = 1; i < codewords.Count; i++)
        {
            checksum += codewords[i] * i;
        }

        return checksum % 103;
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
        public const int B = 0;
        public const int C = 1;
    }
}
