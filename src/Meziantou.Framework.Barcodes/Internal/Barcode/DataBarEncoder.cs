namespace Meziantou.Framework.Barcodes.Internal.Linear;

// GS1 DataBar Omnidirectional, Stacked, Stacked Omnidirectional and Limited (ISO/IEC 24724:2011, sections 5.2 and 5.3)
internal static class DataBarEncoder
{
    // The groups of the characters of GS1 DataBar Omnidirectional, Table 3: outside characters (groups 0 to 4), then
    // inside characters (groups 5 to 8)
    internal static readonly int[] OmnidirectionalGroupStarts = [0, 161, 961, 2015, 2715, 0, 336, 1036, 1516];
    internal static readonly int[] OmnidirectionalCombinations = [1, 10, 34, 70, 126, 4, 20, 48, 81];
    internal static readonly int[] OmnidirectionalOddModules = [12, 10, 8, 6, 4, 5, 7, 9, 11];
    internal static readonly int[] OmnidirectionalEvenModules = [4, 6, 8, 10, 12, 10, 8, 6, 4];
    internal static readonly int[] OmnidirectionalOddWidest = [8, 6, 4, 3, 1, 2, 4, 6, 8];
    internal static readonly int[] OmnidirectionalEvenWidest = [1, 3, 5, 6, 8, 7, 5, 3, 1];

    // Table 4: the 9 finder patterns, from the left
    internal static readonly int[] OmnidirectionalFinderPatterns =
    [
        3, 8, 2, 1, 1,
        3, 5, 5, 1, 1,
        3, 3, 7, 1, 1,
        3, 1, 9, 1, 1,
        2, 7, 4, 1, 1,
        2, 5, 6, 1, 1,
        2, 3, 8, 1, 1,
        1, 5, 7, 1, 1,
        1, 3, 9, 1, 1,
    ];

    // The groups of the characters of GS1 DataBar Limited, Table 6
    internal static readonly int[] LimitedGroupStarts = [0, 183064, 820064, 1000776, 1491021, 1979845, 1996939];
    internal static readonly int[] LimitedCombinations = [28, 728, 6454, 203, 2408, 1, 16632];
    internal static readonly int[] LimitedOddModules = [17, 13, 9, 15, 11, 19, 7];
    internal static readonly int[] LimitedEvenModules = [9, 13, 17, 11, 15, 7, 19];
    internal static readonly int[] LimitedOddWidest = [6, 5, 3, 5, 4, 8, 1];
    internal static readonly int[] LimitedEvenWidest = [3, 4, 6, 4, 5, 1, 8];

    // The 89 check characters of GS1 DataBar Limited, Table 7: the widths of their 14 elements, by checksum
    internal static readonly string[] LimitedCheckCharacters =
    [
        "11111111113311", "11111111123211", "11111111133111", "11111112113211", "11111112123111", // 0-4
        "11111113113111", "11111211113211", "11111211123111", "11111212113111", "11111311113111", // 5-9
        "11121111113211", "11121111123111", "11121112113111", "11121211113111", "11131111113111", // 10-14
        "12111111113211", "12111111123111", "12111112113111", "12111211113111", "12121111113111", // 15-19
        "13111111113111", "11111111212311", "11111111222211", "11111111232111", "11111112212211", // 20-24
        "11111112222111", "11111113212111", "11111211212211", "11111211222111", "11111212212111", // 25-29
        "11111311212111", "11121111212211", "11121111222111", "11121112212111", "11121211212111", // 30-34
        "11131111212111", "12111111212211", "12111111222111", "12111112212111", "12111211212111", // 35-39
        "12121111212111", "13111111212111", "11111111311311", "11111111321211", "11111112311211", // 40-44
        "11121111311211", "12111111311211", "11111121112311", "11111121122211", "11111121132111", // 45-49
        "11111122112211", "11121121112211", "11121121122111", "11121122112111", "11121221112111", // 50-54
        "11131121112111", "12111121112211", "12111121122111", "12121121112111", "11112111112311", // 55-59
        "11112111122211", "11112111132111", "11112112112211", "11112112122111", "11112211112211", // 60-64
        "12112111112211", "12112111122111", "12112112112111", "12112211112111", "12122111112111", // 65-69
        "13112111112111", "11211111112311", "11211111122211", "11211111132111", "11211112112211", // 70-74
        "11211112122111", "11211113112111", "11211211112211", "11211211122111", "11221111112211", // 75-79
        "21111111122211", "21111111132111", "21111112112211", "21111112122111", "21111113112111", // 80-84
        "21111211122111", "21111212112111", "21121111122111", "21111111221211", // 85-88
    ];

    public static Barcode EncodeOmnidirectional(string data, BarcodeType type)
    {
        var gtin = DataBarHelper.NormalizeGtin(data, "GS1 DataBar");
        var value = long.Parse(gtin.AsSpan(0, 13), NumberStyles.None, CultureInfo.InvariantCulture);
        var left = value / 4537077;
        var right = value % 4537077;
        Span<int> characters = [(int)(left / 1597), (int)(left % 1597), (int)(right / 1597), (int)(right % 1597)];

        Span<int> widths = stackalloc int[32];
        var checksum = 0;
        for (var i = 0; i < 4; i++)
        {
            var characterWidths = widths.Slice(i * 8, 8);
            EncodeOmnidirectionalCharacter(characterWidths, characters[i], outside: i is 0 or 2);
            for (var j = 0; j < 8; j++)
            {
                checksum += DataBarHelper.GetChecksumWeight((i * 8) + j, 79) * characterWidths[j];
            }
        }

        // The checksum selects the left and right finder patterns, among the 9 x 9 pairs less the 2 unused ones
        checksum %= 79;
        if (checksum >= 8)
        {
            checksum++;
        }

        if (checksum >= 72)
        {
            checksum++;
        }

        var leftFinder = checksum / 9;
        var rightFinder = checksum % 9;

        // The 46 elements of the symbol, from the left guard (a space) to the right guard (a bar): character 1, the left
        // finder, character 2 reversed, character 4, the right finder reversed, character 3 reversed
        Span<int> elements = stackalloc int[46];
        elements[0] = 1;
        elements[1] = 1;
        elements[44] = 1;
        elements[45] = 1;
        for (var i = 0; i < 8; i++)
        {
            elements[i + 2] = widths[i];
            elements[i + 15] = widths[8 + 7 - i];
            elements[i + 23] = widths[24 + i];
            elements[i + 36] = widths[16 + 7 - i];
        }

        for (var i = 0; i < 5; i++)
        {
            elements[i + 10] = OmnidirectionalFinderPatterns[(leftFinder * 5) + i];
            elements[i + 31] = OmnidirectionalFinderPatterns[(rightFinder * 5) + 4 - i];
        }

        var text = "(01)" + gtin;
        if (type is BarcodeType.Gs1DataBar)
        {
            var modules = new List<bool>(96);
            DataBarHelper.AppendElements(modules, elements, firstIsDark: false);
            return BarcodeEncoderHelper.CreateBarcode(type, modules, text);
        }

        // Section 5.3.2: the left half on the top row, ending with a bar and a space, and the right half on the bottom
        // row, starting with a bar and a space
        var topList = new List<bool>(50);
        DataBarHelper.AppendElements(topList, elements[..23], firstIsDark: false);
        DataBarHelper.AppendElements(topList, [1, 1], firstIsDark: true);
        var bottomList = new List<bool>(50);
        DataBarHelper.AppendElements(bottomList, [1, 1], firstIsDark: true);
        DataBarHelper.AppendElements(bottomList, elements[23..], firstIsDark: true);
        var top = topList.ToArray();
        var bottom = bottomList.ToArray();

        if (type is BarcodeType.Gs1DataBarStacked)
        {
            // A 1-module separator: the complement of the modules above and below where they agree, alternating where they
            // do not, light at the ends
            var separator = new bool[50];
            for (var i = 1; i < 46; i++)
            {
                separator[i] = top[i] == bottom[i] ? !top[i] : !separator[i - 1];
            }

            separator[1] = false;
            separator[2] = false;
            separator[3] = false;

            return DataBarHelper.CreateStackedBarcode(type, [(top, 5), (separator, 1), (bottom, 7)], text);
        }

        // Section 5.3.2.2: three 1-module separators. The outer ones are the complement of their row, except over the
        // finder patterns, whose light modules alternate dark and light; the middle one alternates.
        var topSeparator = CreateOmnidirectionalSeparator(top, finderStart: 18, isFinderValue3: false);
        var bottomSeparator = CreateOmnidirectionalSeparator(bottom, finderStart: 19, isFinderValue3: rightFinder == 3);
        var middleSeparator = new bool[50];
        for (var i = 5; i < 46; i += 2)
        {
            middleSeparator[i] = true;
        }

        return DataBarHelper.CreateStackedBarcode(type, [(top, 33), (topSeparator, 1), (middleSeparator, 1), (bottomSeparator, 1), (bottom, 33)], text);
    }

    public static Barcode EncodeLimited(string data)
    {
        var gtin = DataBarHelper.NormalizeGtin(data, "GS1 DataBar Limited");
        if (gtin[0] is not ('0' or '1'))
            throw new ArgumentException("The GTIN of a GS1 DataBar Limited symbol must start with 0 or 1.", nameof(data));

        var value = long.Parse(gtin.AsSpan(0, 13), NumberStyles.None, CultureInfo.InvariantCulture);
        Span<int> widths = stackalloc int[28];
        EncodeLimitedCharacter(widths[..14], (int)(value / 2013571));
        EncodeLimitedCharacter(widths[14..], (int)(value % 2013571));

        var checksum = 0;
        for (var i = 0; i < 28; i++)
        {
            checksum += DataBarHelper.GetChecksumWeight(i, 89) * widths[i];
        }

        checksum %= 89;

        // The left guard (a space), the left character, the check character, the right character and the right guard
        Span<int> elements = stackalloc int[46];
        elements[0] = 1;
        elements[1] = 1;
        widths[..14].CopyTo(elements[2..]);
        GetLimitedCheckCharacter(elements.Slice(16, 14), checksum);
        widths[14..].CopyTo(elements[30..]);
        elements[44] = 1;
        elements[45] = 1;

        var modules = new List<bool>(74);
        DataBarHelper.AppendElements(modules, elements, firstIsDark: false);
        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Gs1DataBarLimited, modules, "(01)" + gtin);
    }

    private static void EncodeOmnidirectionalCharacter(Span<int> widths, int value, bool outside)
    {
        var group = outside
            ? value switch
            {
                < 161 => 0,
                < 961 => 1,
                < 2015 => 2,
                < 2715 => 3,
                _ => 4,
            }
            : value switch
            {
                < 336 => 5,
                < 1036 => 6,
                < 1516 => 7,
                _ => 8,
            };

        // The outside characters have the odd value first, the inside ones the even value first
        var groupValue = value - OmnidirectionalGroupStarts[group];
        var combinations = OmnidirectionalCombinations[group];
        var oddValue = outside ? groupValue / combinations : groupValue % combinations;
        var evenValue = outside ? groupValue % combinations : groupValue / combinations;

        Span<int> odd = stackalloc int[4];
        Span<int> even = stackalloc int[4];
        DataBarHelper.GetWidths(odd, oddValue, OmnidirectionalOddModules[group], OmnidirectionalOddWidest[group], noNarrow: outside);
        DataBarHelper.GetWidths(even, evenValue, OmnidirectionalEvenModules[group], OmnidirectionalEvenWidest[group], noNarrow: !outside);
        Interleave(widths, odd, even);
    }

    private static void EncodeLimitedCharacter(Span<int> widths, int value)
    {
        var group = LimitedGroupStarts.Length - 1;
        while (value < LimitedGroupStarts[group])
        {
            group--;
        }

        var groupValue = value - LimitedGroupStarts[group];
        var combinations = LimitedCombinations[group];

        Span<int> odd = stackalloc int[7];
        Span<int> even = stackalloc int[7];
        DataBarHelper.GetWidths(odd, groupValue / combinations, LimitedOddModules[group], LimitedOddWidest[group], noNarrow: true);
        DataBarHelper.GetWidths(even, groupValue % combinations, LimitedEvenModules[group], LimitedEvenWidest[group], noNarrow: false);
        Interleave(widths, odd, even);
    }

    private static void GetLimitedCheckCharacter(Span<int> widths, int checksum)
    {
        var pattern = LimitedCheckCharacters[checksum];
        for (var i = 0; i < 14; i++)
        {
            widths[i] = pattern[i] - '0';
        }
    }

    private static void Interleave(Span<int> widths, ReadOnlySpan<int> odd, ReadOnlySpan<int> even)
    {
        for (var i = 0; i < odd.Length; i++)
        {
            widths[2 * i] = odd[i];
            widths[(2 * i) + 1] = even[i];
        }
    }

    // The separator modules over the 13 widest modules of a finder pattern (the first ones in the top row, the last ones
    // in the bottom row, where the finder pattern is reversed) alternate dark and light over its light modules, from the left
    private static bool[] CreateOmnidirectionalSeparator(bool[] row, int finderStart, bool isFinderValue3)
    {
        var separator = new bool[row.Length];
        for (var i = 4; i < row.Length - 4; i++)
        {
            separator[i] = !row[i];
        }

        var dark = true;
        for (var i = finderStart; i < finderStart + 13; i++)
        {
            if (isFinderValue3)
            {
                // Section 5.3.2.2: the single dark module over finder value 3 is shifted one module to the right, over
                // the start of its 3-module bar
                separator[i] = i == finderStart + 10;
            }
            else if (row[i])
            {
                separator[i] = false;
                dark = true;
            }
            else
            {
                separator[i] = dark;
                dark = !dark;
            }
        }

        return separator;
    }
}
