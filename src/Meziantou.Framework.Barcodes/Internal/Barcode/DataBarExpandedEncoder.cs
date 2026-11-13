namespace Meziantou.Framework.Barcodes.Internal.Linear;

// GS1 DataBar Expanded and Expanded Stacked (ISO/IEC 24724:2011, sections 5.4, 7.2.5 and 7.2.8)
internal static class DataBarExpandedEncoder
{
    // The FNC1 separator in the data of the general-purpose field
    private const char Fnc1 = '\u001D';

    private const int MaxSymbolCharacters = 22;

    // The groups of the characters, Table 9
    internal static readonly int[] GroupStarts = [0, 348, 1388, 2948, 3988];
    internal static readonly int[] Combinations = [4, 20, 52, 104, 204];
    internal static readonly int[] OddModules = [12, 10, 8, 6, 4];
    internal static readonly int[] EvenModules = [5, 7, 9, 11, 13];
    internal static readonly int[] OddWidest = [7, 5, 4, 3, 1];
    internal static readonly int[] EvenWidest = [2, 4, 5, 6, 8];

    // The finder patterns A to F, Table 10, as they are read from the left in their first form (A1, B1, ...)
    internal static readonly int[][] FinderPatterns =
    [
        [1, 8, 4, 1, 1],
        [3, 6, 4, 1, 1],
        [3, 4, 6, 1, 1],
        [3, 2, 8, 1, 1],
        [2, 6, 5, 1, 1],
        [2, 2, 9, 1, 1],
    ];

    // The finder patterns of the symbols of 2 to 11 pairs of characters, Table 11 (0 for A, 5 for F)
    internal static readonly int[][] FinderSequences =
    [
        [0, 0],
        [0, 1, 1],
        [0, 2, 1, 3],
        [0, 4, 1, 3, 2],
        [0, 4, 1, 3, 3, 5],
        [0, 4, 1, 3, 4, 5, 5],
        [0, 0, 1, 1, 2, 2, 3, 3],
        [0, 0, 1, 1, 2, 2, 3, 4, 4],
        [0, 0, 1, 1, 2, 2, 3, 4, 5, 5],
        [0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5],
    ];

    private enum Mode
    {
        Numeric,
        Alphanumeric,
        Iso646,
    }

    public static Barcode Encode(ReadOnlySpan<Gs1Element> elements, int segmentsPerRow)
    {
        var text = new StringBuilder();
        foreach (var element in elements)
        {
            if (element is null)
                throw new ArgumentException("The elements cannot contain null.", nameof(elements));

            text.Append(element.ToString());
        }

        var bits = EncodeData(elements, segmentsPerRow);
        var dataCharacters = bits.Count / 12;
        var symbolCharacters = dataCharacters + 1;

        // The check character, then the data characters
        var characters = new int[symbolCharacters][];
        for (var i = 0; i < dataCharacters; i++)
        {
            var value = 0;
            for (var j = 0; j < 12; j++)
            {
                value = (value << 1) | (bits[(i * 12) + j] ? 1 : 0);
            }

            characters[i + 1] = EncodeCharacter(value);
        }

        var pairs = (symbolCharacters + 1) / 2;
        var finders = FinderSequences[pairs - 2];
        var checksum = 0;
        for (var i = 1; i < symbolCharacters; i++)
        {
            // Section 7.2.6: the weights depend on the finder pattern next to the character and on its side
            var pair = i / 2;
            var row = (4 * finders[pair]) + (pair % 2 == 0 ? 0 : 2) + (i % 2) - 1;
            for (var j = 0; j < 8; j++)
            {
                checksum += DataBarHelper.GetChecksumWeight((row * 8) + j, 211) * characters[i][j];
            }
        }

        characters[0] = EncodeCharacter((211 * (symbolCharacters - 4)) + (checksum % 211));

        // Each pair: the left character, read from the left, its finder pattern, then the right character, read from the
        // right. The finder patterns of the odd pairs are reversed.
        var blocks = new List<Element>[pairs];
        for (var pair = 0; pair < pairs; pair++)
        {
            var block = new List<Element>(21);
            foreach (var width in characters[2 * pair])
            {
                block.Add(new Element(width, IsFinderCore: false));
            }

            var finder = FinderPatterns[finders[pair]];
            for (var j = 0; j < 5; j++)
            {
                var width = pair % 2 == 0 ? finder[j] : finder[4 - j];
                var isCore = pair % 2 == 0 ? j < 3 : j >= 2;
                block.Add(new Element(width, isCore));
            }

            if ((2 * pair) + 1 < symbolCharacters)
            {
                var right = characters[(2 * pair) + 1];
                for (var j = 7; j >= 0; j--)
                {
                    block.Add(new Element(right[j], IsFinderCore: false));
                }
            }

            blocks[pair] = block;
        }

        var barcodeText = text.ToString();
        if (segmentsPerRow == 0)
        {
            var row = BuildRow(blocks, 0, pairs, reversed: false, specialCase: false);
            return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Gs1DataBarExpanded, [.. row.Modules], barcodeText);
        }

        return CreateStackedBarcode(blocks, segmentsPerRow / 2, barcodeText);
    }

    // Section 7.2.8: rows of `pairsPerRow` pairs, separated by 3 separator rows
    private static Barcode CreateStackedBarcode(List<Element>[] blocks, int pairsPerRow, string text)
    {
        var rowCount = (blocks.Length + pairsPerRow - 1) / pairsPerRow;
        var width = 0;
        var rows = new List<(bool[] Modules, int Height)>();
        Row? previous = null;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var firstBlock = rowIndex * pairsPerRow;
            var blockCount = Math.Min(pairsPerRow, blocks.Length - firstBlock);
            var rowNumber = rowIndex + 1;

            // The even rows are read from the right when the rows have an even number of pairs, except a last row with
            // an odd number of pairs, which is read from the left one module to the right
            var specialCase = rowNumber == rowCount && rowNumber % 2 == 0 && pairsPerRow % 2 == 0 && blockCount % 2 == 1;
            var reversed = rowNumber % 2 == 0 && pairsPerRow % 2 == 0 && !specialCase;
            var row = BuildRow(blocks, firstBlock, blockCount, reversed, specialCase);
            if (rowIndex == 0)
            {
                // The first row is the widest: it is full, or it is the only row
                width = row.Modules.Length;
            }

            if (previous is not null)
            {
                rows.Add((CreateSeparator(previous, width, 0), 1));
                var middle = new bool[width];
                for (var j = 5; j < width - 4; j += 2)
                {
                    middle[j] = true;
                }

                rows.Add((middle, 1));
                rows.Add((CreateSeparator(row, width, specialCase ? 1 : 0), 1));
            }

            rows.Add((Pad(row.Modules, width), 34));
            previous = row;
        }

        return DataBarHelper.CreateStackedBarcode(BarcodeType.Gs1DataBarExpandedStacked, rows, text);
    }

    private static Row BuildRow(List<Element>[] blocks, int firstBlock, int blockCount, bool reversed, bool specialCase)
    {
        var elements = new List<Element>();
        for (var i = 0; i < blockCount; i++)
        {
            elements.AddRange(blocks[firstBlock + i]);
        }

        // The color of the first element after the left guard: the color it has in the single-row symbol, where the
        // element n of the pairs (counting the left guard) is light when n is even
        var firstIndex = 2 + (21 * firstBlock);
        if (reversed)
        {
            elements.Reverse();
            var lastBlock = firstBlock + blockCount - 1;
            firstIndex = 2 + (21 * lastBlock) + blocks[lastBlock].Count - 1;
        }

        var firstIsLight = firstIndex % 2 == 0;

        // The left guard, a space and a bar or a bar and a space, so that its second element differs from the next one
        var modules = new List<bool>();
        var isDark = !firstIsLight;
        BarcodeEncoderHelper.AppendModules(modules, isDark, specialCase ? 2 : 1);
        isDark = !isDark;
        BarcodeEncoderHelper.AppendModules(modules, isDark, 1);
        isDark = !isDark;

        var finderRegions = new List<(int Start, int End)>();
        var coreStart = -1;
        foreach (var element in elements)
        {
            if (element.IsFinderCore && coreStart < 0)
            {
                coreStart = modules.Count;
            }
            else if (!element.IsFinderCore && coreStart >= 0)
            {
                finderRegions.Add((coreStart, modules.Count));
                coreStart = -1;
            }

            BarcodeEncoderHelper.AppendModules(modules, isDark, element.Width);
            isDark = !isDark;
        }

        if (coreStart >= 0)
        {
            finderRegions.Add((coreStart, modules.Count));
        }

        // The right guard
        BarcodeEncoderHelper.AppendModules(modules, isDark, 1);
        isDark = !isDark;
        BarcodeEncoderHelper.AppendModules(modules, isDark, 1);

        return new Row([.. modules], finderRegions, reversed);
    }

    // Section 7.2.8.1: the complement of the row, except over the finder patterns, whose light modules alternate dark and
    // light in the reading direction of the row
    private static bool[] CreateSeparator(Row row, int width, int start)
    {
        var separator = new bool[width];
        for (var j = 4 + start; j < row.Modules.Length - 4; j++)
        {
            separator[j] = !row.Modules[j];
        }

        foreach (var (regionStart, regionEnd) in row.FinderRegions)
        {
            var dark = true;
            for (var k = 0; k < regionEnd - regionStart; k++)
            {
                var j = row.Reversed ? regionEnd - 1 - k : regionStart + k;
                if (row.Modules[j])
                {
                    separator[j] = false;
                    dark = true;
                }
                else
                {
                    separator[j] = dark;
                    dark = !dark;
                }
            }
        }

        return separator;
    }

    private static bool[] Pad(bool[] modules, int width)
    {
        if (modules.Length == width)
            return modules;

        var result = new bool[width];
        modules.CopyTo(result, 0);
        return result;
    }

    private static int[] EncodeCharacter(int value)
    {
        var group = GroupStarts.Length - 1;
        while (value < GroupStarts[group])
        {
            group--;
        }

        var groupValue = value - GroupStarts[group];
        Span<int> odd = stackalloc int[4];
        Span<int> even = stackalloc int[4];
        DataBarHelper.GetWidths(odd, groupValue / Combinations[group], OddModules[group], OddWidest[group], noNarrow: false);
        DataBarHelper.GetWidths(even, groupValue % Combinations[group], EvenModules[group], EvenWidest[group], noNarrow: true);

        var widths = new int[8];
        for (var i = 0; i < 4; i++)
        {
            widths[2 * i] = odd[i];
            widths[(2 * i) + 1] = even[i];
        }

        return widths;
    }

    // Section 7.2.5: the linkage flag, the encodation method and its compressed fields, then the general-purpose field
    private static List<bool> EncodeData(ReadOnlySpan<Gs1Element> elements, int segmentsPerRow)
    {
        var bits = new List<bool>(252);
        Append(bits, 0, 1); // no 2D composite component

        var first = elements[0];
        var variableLengthFieldPosition = -1;
        var generalPurposeStart = 0;
        string? generalPurposePrefix = null;
        // The compressed methods omit the check digit of the GTIN, which a reader computes: a GTIN with another check
        // digit is encoded as general-purpose data, so that it is read back as it is
        var gtin = first.ApplicationIdentifier == "01" && DataBarHelper.ComputeGtinCheckDigit(first.Value.AsSpan(0, 13)) == first.Value[13] - '0' ? first.Value : null;
        var variableMeasure = gtin is not null && gtin[0] == '9';

        if (variableMeasure && TryEncodeWeight(bits, elements))
        {
            generalPurposeStart = elements.Length;
        }
        else if (variableMeasure && elements.Length >= 2 && elements[1].ApplicationIdentifier is ['3', '9', '2', >= '0' and <= '3'] && elements[1].Value.All(char.IsAsciiDigit) && elements[1].Value.Length <= 15)
        {
            // (01) and the price (392n): method 01100
            Append(bits, 0b01100, 5);
            variableLengthFieldPosition = bits.Count;
            Append(bits, 0, 2);
            AppendGtin(bits, gtin!);
            Append(bits, elements[1].ApplicationIdentifier[3] - '0', 2);
            generalPurposePrefix = elements[1].Value;
            generalPurposeStart = 2;
        }
        else if (variableMeasure && elements.Length >= 2 && elements[1].ApplicationIdentifier is ['3', '9', '3', >= '0' and <= '3'] && elements[1].Value.Length is >= 4 and <= 18 && elements[1].Value.All(char.IsAsciiDigit))
        {
            // (01) and the price with its ISO 4217 currency code (393n): method 01101
            Append(bits, 0b01101, 5);
            variableLengthFieldPosition = bits.Count;
            Append(bits, 0, 2);
            AppendGtin(bits, gtin!);
            Append(bits, elements[1].ApplicationIdentifier[3] - '0', 2);
            Append(bits, int.Parse(elements[1].Value.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture), 10);
            generalPurposePrefix = elements[1].Value[3..];
            generalPurposeStart = 2;
        }
        else if (gtin is not null)
        {
            // (01): method 1, the indicator digit in 4 bits, the next 12 digits in groups of 3, the check digit omitted
            Append(bits, 1, 1);
            variableLengthFieldPosition = bits.Count;
            Append(bits, 0, 2);
            Append(bits, gtin[0] - '0', 4);
            AppendGtin(bits, gtin);
            generalPurposeStart = 1;
        }
        else
        {
            // General-purpose data only: method 00
            Append(bits, 0, 2);
            variableLengthFieldPosition = bits.Count;
            Append(bits, 0, 2);
        }

        var data = new StringBuilder();
        if (generalPurposePrefix is not null)
        {
            data.Append(generalPurposePrefix);
            if (generalPurposeStart < elements.Length)
            {
                data.Append(Fnc1);
            }
        }

        for (var i = generalPurposeStart; i < elements.Length; i++)
        {
            data.Append(elements[i].ApplicationIdentifier).Append(elements[i].Value);
            if (i < elements.Length - 1 && Gs1128Encoder.GetPredefinedValueLength(elements[i].ApplicationIdentifier) < 0)
            {
                data.Append(Fnc1);
            }
        }

        var mode = EncodeGeneralPurpose(bits, data.ToString(), segmentsPerRow);

        // Section 7.2.5.5.4: the padding, a latch to alphanumeric mode in numeric mode, then repetitions of the latch to
        // ISO/IEC 646 mode
        var remainder = GetRemainder(bits.Count, segmentsPerRow);
        if (remainder > 0 && mode == Mode.Numeric)
        {
            var count = Math.Min(4, remainder);
            Append(bits, 0, count);
            remainder -= count;
        }

        for (var i = 0; i < remainder; i++)
        {
            bits.Add(i % 5 == 2);
        }

        if (bits.Count > (MaxSymbolCharacters - 1) * 12)
            throw new ArgumentException($"The elements do not fit in a GS1 DataBar Expanded symbol: they need {(bits.Count + 11) / 12 + 1} symbol characters, the maximum is {MaxSymbolCharacters}.", nameof(elements));

        // Section 7.2.5.4.1: the variable length symbol field, the parity of the number of symbol characters and whether
        // there are more than 14
        if (variableLengthFieldPosition >= 0)
        {
            var symbolCharacters = (bits.Count / 12) + 1;
            bits[variableLengthFieldPosition] = symbolCharacters % 2 == 1;
            bits[variableLengthFieldPosition + 1] = symbolCharacters > 14;
        }

        return bits;
    }

    // (01) of a variable measure trade item and its weight (310n, 320n), optionally followed by a date (11, 13, 15, 17):
    // methods 0100, 0101 and 0111000 to 0111111
    private static bool TryEncodeWeight(List<bool> bits, ReadOnlySpan<Gs1Element> elements)
    {
        if (elements.Length is not (2 or 3))
            return false;

        var weightIdentifier = elements[1].ApplicationIdentifier;
        if (weightIdentifier is not ['3', '1' or '2', '0', _] || !int.TryParse(elements[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var weight))
            return false;

        var isPounds = weightIdentifier[1] == '2';
        var gtin = elements[0].Value;
        if (elements.Length == 2)
        {
            if (weightIdentifier == "3103" && weight <= 32767)
            {
                Append(bits, 0b0100, 4);
                AppendGtin(bits, gtin);
                Append(bits, weight, 15);
                return true;
            }

            if ((weightIdentifier == "3202" && weight <= 9999) || (weightIdentifier == "3203" && weight <= 22767))
            {
                Append(bits, 0b0101, 4);
                AppendGtin(bits, gtin);
                Append(bits, weightIdentifier == "3203" ? weight + 10000 : weight, 15);
                return true;
            }
        }

        if (weight > 99999)
            return false;

        var dateCode = 0;
        var date = 38400; // no date
        if (elements.Length == 3)
        {
            dateCode = elements[2].ApplicationIdentifier switch
            {
                "11" => 0,
                "13" => 1,
                "15" => 2,
                "17" => 3,
                _ => -1,
            };

            var value = elements[2].Value;
            if (dateCode < 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return false;

            var year = ((value[0] - '0') * 10) + (value[1] - '0');
            var month = ((value[2] - '0') * 10) + (value[3] - '0');
            var day = ((value[4] - '0') * 10) + (value[5] - '0');
            if (month is < 1 or > 12 || day > 31)
                return false;

            date = (year * 384) + ((month - 1) * 32) + day;
        }

        Append(bits, 0b0111, 4);
        Append(bits, dateCode, 2);
        Append(bits, isPounds ? 1 : 0, 1);
        AppendGtin(bits, gtin);
        Append(bits, ((weightIdentifier[3] - '0') * 100000) + weight, 20);
        Append(bits, date, 16);
        return true;
    }

    // The 12 digits of a GTIN after the indicator digit, without the check digit, in groups of 3 digits
    private static void AppendGtin(List<bool> bits, string gtin)
    {
        for (var i = 1; i < 13; i += 3)
        {
            Append(bits, int.Parse(gtin.AsSpan(i, 3), NumberStyles.None, CultureInfo.InvariantCulture), 10);
        }
    }

    // Section 7.2.5.5: numeric, alphanumeric and ISO/IEC 646 modes. Returns the mode at the end of the data.
    private static Mode EncodeGeneralPurpose(List<bool> bits, string data, int segmentsPerRow)
    {
        var mode = Mode.Numeric;
        var i = 0;
        while (i < data.Length)
        {
            var c = data[i];
            switch (mode)
            {
                case Mode.Numeric:
                    if (i + 1 < data.Length && IsNumeric(c) && IsNumeric(data[i + 1]) && !(c == Fnc1 && data[i + 1] == Fnc1))
                    {
                        Append(bits, (11 * NumericValue(c)) + NumericValue(data[i + 1]) + 8, 7);
                        i += 2;
                    }
                    else if (i + 1 == data.Length && char.IsAsciiDigit(c))
                    {
                        // A single digit at the end: in 4 bits when they complete a symbol character, else with a FNC1
                        var remainder = GetRemainder(bits.Count, segmentsPerRow);
                        if (remainder is >= 4 and <= 6)
                        {
                            Append(bits, c - '0' + 1, 4);
                        }
                        else
                        {
                            Append(bits, (11 * (c - '0')) + 10 + 8, 7);
                        }

                        i++;
                    }
                    else
                    {
                        Append(bits, 0, 4); // latch to alphanumeric
                        mode = Mode.Alphanumeric;
                    }

                    break;

                case Mode.Alphanumeric:
                    if (c == Fnc1)
                    {
                        Append(bits, 0b01111, 5);
                        mode = Mode.Numeric;
                        i++;
                    }
                    else if (!IsAlphanumeric(c))
                    {
                        Append(bits, 0b00100, 5); // latch to ISO/IEC 646
                        mode = Mode.Iso646;
                    }
                    else if (IsNumericRun(data, i, 6) || IsNumericToEnd(data, i, 4))
                    {
                        Append(bits, 0, 3); // latch to numeric
                        mode = Mode.Numeric;
                    }
                    else
                    {
                        AppendAlphanumeric(bits, c);
                        i++;
                    }

                    break;

                default:
                    if (c == Fnc1)
                    {
                        Append(bits, 0b01111, 5);
                        mode = Mode.Numeric;
                        i++;
                    }
                    else if (!NeedsIso646(data, i, 10) && IsNumericRun(data, i, 4))
                    {
                        Append(bits, 0, 3); // latch to numeric
                        mode = Mode.Numeric;
                    }
                    else if (!NeedsIso646(data, i, 10) && IsAlphanumericRun(data, i, 5))
                    {
                        Append(bits, 0b00100, 5); // latch to alphanumeric
                        mode = Mode.Alphanumeric;
                    }
                    else
                    {
                        AppendIso646(bits, c);
                        i++;
                    }

                    break;
            }
        }

        return mode;
    }

    // The number of bits to the end of the next symbol character, the symbol having at least 3 data characters, and the
    // last row of a stacked symbol at least 2 symbol characters
    private static int GetRemainder(int length, int segmentsPerRow)
    {
        var total = Math.Max(36, (length + 11) / 12 * 12);
        if (segmentsPerRow > 0 && ((total / 12) + 1) % segmentsPerRow == 1)
        {
            total += 12;
        }

        return total - length;
    }

    private static bool IsNumeric(char c) => char.IsAsciiDigit(c) || c == Fnc1;

    private static int NumericValue(char c) => c == Fnc1 ? 10 : c - '0';

    private static bool IsAlphanumeric(char c) => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c) || c is Fnc1 or '*' or ',' or '-' or '.' or '/';

    private static bool IsNumericRun(string data, int start, int count)
    {
        if (start + count > data.Length)
            return false;

        for (var i = start; i < start + count; i++)
        {
            if (!IsNumeric(data[i]))
                return false;
        }

        return true;
    }

    private static bool IsNumericToEnd(string data, int start, int minimum)
    {
        return data.Length - start >= minimum && IsNumericRun(data, start, data.Length - start);
    }

    private static bool IsAlphanumericRun(string data, int start, int count)
    {
        if (start + count > data.Length)
            return false;

        for (var i = start; i < start + count; i++)
        {
            if (!IsAlphanumeric(data[i]))
                return false;
        }

        return true;
    }

    private static bool NeedsIso646(string data, int start, int count)
    {
        for (var i = start; i < Math.Min(data.Length, start + count); i++)
        {
            if (!IsAlphanumeric(data[i]))
                return true;
        }

        return false;
    }

    private static void AppendAlphanumeric(List<bool> bits, char c)
    {
        switch (c)
        {
            case >= '0' and <= '9':
                Append(bits, c - '0' + 5, 5);
                break;
            case >= 'A' and <= 'Z':
                Append(bits, c - 'A' + 32, 6);
                break;
            default:
                Append(bits, c switch
                {
                    '*' => 58,
                    ',' => 59,
                    '-' => 60,
                    '.' => 61,
                    _ => 62, // '/'
                }, 6);
                break;
        }
    }

    private static void AppendIso646(List<bool> bits, char c)
    {
        switch (c)
        {
            case >= '0' and <= '9':
                Append(bits, c - '0' + 5, 5);
                break;
            case >= 'A' and <= 'Z':
                Append(bits, c - 'A' + 64, 7);
                break;
            case >= 'a' and <= 'z':
                Append(bits, c - 'a' + 90, 7);
                break;
            default:
                Append(bits, c switch
                {
                    '!' => 232,
                    '"' => 233,
                    '%' => 234,
                    '&' => 235,
                    '\'' => 236,
                    '(' => 237,
                    ')' => 238,
                    '*' => 239,
                    '+' => 240,
                    ',' => 241,
                    '-' => 242,
                    '.' => 243,
                    '/' => 244,
                    ':' => 245,
                    ';' => 246,
                    '<' => 247,
                    '=' => 248,
                    '>' => 249,
                    '?' => 250,
                    '_' => 251,
                    _ => 252, // space
                }, 8);
                break;
        }
    }

    private static void Append(List<bool> bits, int value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    private readonly record struct Element(int Width, bool IsFinderCore);

    private sealed record Row(bool[] Modules, List<(int Start, int End)> FinderRegions, bool Reversed);
}
