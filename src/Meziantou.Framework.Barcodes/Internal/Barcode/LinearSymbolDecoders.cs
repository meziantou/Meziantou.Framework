namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// The decoders of the linear symbologies whose characters are wide and narrow elements (Code 39, Codabar, ITF) or
/// elements of 1 to 4 modules (Code 93, Code 128). Each one reads a symbol from its first dark run, left to right.
/// </summary>
internal static class LinearSymbolDecoders
{
    private static readonly int[][] Code128Patterns = [.. Code128Encoder.Patterns.Take(106).Select(RunPatterns.FromDigits)];
    private static readonly int[] Code128Stop = RunPatterns.FromDigits(Code128Encoder.Patterns[106]);
    private static readonly int[][] Code93Patterns = [.. Code93Encoder.CharacterEncodings.Select(encoding => RunPatterns.FromModules(Convert.ToString(encoding, 2).PadLeft(9, '0')))];
    private static readonly Dictionary<string, char> Code39Characters = Code39Encoder.CharacterPatterns.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
    private static readonly Dictionary<int, char> CodabarCharacters = CodabarEncoder.CharacterEncodings.ToDictionary(pair => pair.Value, pair => pair.Key);

    // ISO/IEC 15417: 103 to 105 start in code set A, B or C, 106 is the stop character
    private const int StartCodeA = 103;
    private const int StartCodeC = 105;

    public static LinearMatch? TryDecodeCode128(ScanLine line, int start)
    {
        var first = RunPatterns.FindBest(line, start, Code128Patterns, 11);
        if (first is < StartCodeA or > StartCodeC)
            return null;

        var values = new List<int> { first };
        var position = start + 6;
        while (true)
        {
            if (position + 7 > line.Count)
                return null;

            if (RunPatterns.Matches(line, position, Code128Stop))
                break;

            var value = RunPatterns.FindBest(line, position, Code128Patterns, 11);
            if (value < 0 || value >= StartCodeA)
                return null;

            values.Add(value);
            position += 6;
        }

        // The checksum is the start character plus each character times its position, modulo 103
        if (values.Count < 2)
            return null;

        var checksum = values[0];
        for (var i = 1; i < values.Count - 1; i++)
        {
            checksum += i * values[i];
        }

        if (checksum % 103 != values[^1])
            return null;

        var (text, isGs1) = DecodeCode128Text(values);
        if (text is null)
            return null;

        var end = position + 7;
        var moduleWidth = line.GetWidth(start, end - start) / ((11 * values.Count) + 13);
        if (isGs1)
        {
            // GS1-128: the human-readable interpretation of the element strings
            var humanReadable = Gs1ElementStrings.FormatHumanReadable(text);
            if (humanReadable is null)
                return null;

            return new LinearMatch(BarcodeType.Gs1128, humanReadable, start, end, moduleWidth);
        }

        return new LinearMatch(BarcodeType.Code128, text, start, end, moduleWidth);
    }

    // The code sets A, B and C, the shift, FNC1 (a GS character, or GS1 data at the start) and FNC4 (extended ASCII)
    private static (string? Text, bool IsGs1) DecodeCode128Text(List<int> values)
    {
        var text = new StringBuilder();
        var codeSet = values[0] - StartCodeA; // 0: A, 1: B, 2: C
        var isGs1 = false;
        var shift = false;
        var fnc4 = false;
        var fnc4Latch = false;
        for (var i = 1; i < values.Count - 1; i++)
        {
            var value = values[i];
            var current = shift ? 1 - codeSet : codeSet;
            shift = false;
            if (value == 102)
            {
                if (i == 1)
                {
                    isGs1 = true;
                }
                else
                {
                    text.Append('\u001D');
                }

                continue;
            }

            if (current == 2)
            {
                switch (value)
                {
                    case < 100:
                        text.Append((char)('0' + (value / 10))).Append((char)('0' + (value % 10)));
                        break;
                    case 100:
                        codeSet = 1;
                        break;
                    case 101:
                        codeSet = 0;
                        break;
                    default:
                        return (null, false);
                }

                continue;
            }

            switch (value)
            {
                case < 96:
                    var c = current == 0 ? (value < 64 ? value + 32 : value - 64) : value + 32;
                    if (fnc4 != fnc4Latch)
                    {
                        c += 128;
                    }

                    fnc4 = false;
                    text.Append((char)c);
                    break;
                case 96 or 97:
                    // FNC3 and FNC2 are instructions to the reader
                    break;
                case 98:
                    shift = true;
                    break;
                case 99:
                    codeSet = 2;
                    break;
                case 100 or 101:
                    // In code set A, 101 is FNC4 and 100 switches to B; in code set B, 100 is FNC4 and 101 switches to A
                    if ((current == 0 && value == 101) || (current == 1 && value == 100))
                    {
                        // Two FNC4 in a row latch the extended ASCII
                        if (fnc4)
                        {
                            fnc4Latch = !fnc4Latch;
                            fnc4 = false;
                        }
                        else
                        {
                            fnc4 = true;
                        }
                    }
                    else
                    {
                        codeSet = value == 100 ? 1 : 0;
                    }

                    break;
            }
        }

        return (text.ToString(), isGs1);
    }

    public static LinearMatch? TryDecodeCode93(ScanLine line, int start)
    {
        const int Asterisk = 47;
        if (RunPatterns.FindBest(line, start, Code93Patterns, 9) != Asterisk)
            return null;

        var values = new List<int>();
        var position = start + 6;
        while (true)
        {
            var value = RunPatterns.FindBest(line, position, Code93Patterns, 9);
            if (value < 0)
                return null;

            position += 6;
            if (value == Asterisk)
                break;

            values.Add(value);
        }

        // The stop character is followed by a termination bar of 1 module
        if (position >= line.Count || values.Count < 2)
            return null;

        var encoded = string.Concat(values.Select(value => Code93Encoder.Alphabet[value]));
        var data = encoded[..^2];
        if (Code93Encoder.Alphabet[Code93Encoder.ComputeChecksumIndex(data, 20)] != encoded[^2] || Code93Encoder.Alphabet[Code93Encoder.ComputeChecksumIndex(encoded[..^1], 15)] != encoded[^1])
            return null;

        var text = DecodeCode93Extended(data);
        if (text is null)
            return null;

        var end = position + 1;
        return new LinearMatch(BarcodeType.Code93, text, start, end, line.GetWidth(start, end - start) / ((9 * (values.Count + 2)) + 1));
    }

    // The full ASCII of Code 93: the shift characters a to d (written ($), (%), (/) and (+)) and the next character
    private static string? DecodeCode93Extended(string data)
    {
        var text = new StringBuilder(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c is not ('a' or 'b' or 'c' or 'd'))
            {
                text.Append(c);
                continue;
            }

            if (i + 1 >= data.Length || data[i + 1] is < 'A' or > 'Z')
                return null;

            var next = data[++i];
            var decoded = c switch
            {
                'a' => next - 'A' + 1,
                'b' => next switch
                {
                    <= 'E' => next - 'A' + 27,
                    <= 'J' => next - 'F' + ';',
                    <= 'O' => next - 'K' + '[',
                    <= 'T' => next - 'P' + '{',
                    'U' => 0,
                    'V' => '@',
                    'W' => '`',
                    _ => 127,
                },
                'c' => next switch
                {
                    <= 'O' => next - 'A' + '!',
                    'Z' => ':',
                    _ => -1,
                },
                _ => next - 'A' + 'a',
            };

            if (decoded < 0)
                return null;

            text.Append((char)decoded);
        }

        return text.ToString();
    }

    public static LinearMatch? TryDecodeCode39(ScanLine line, int start)
    {
        if (ReadCode39Character(line, start) != '*')
            return null;

        var text = new StringBuilder();
        var position = start + 10;
        while (true)
        {
            var c = ReadCode39Character(line, position);
            if (c is null)
                return null;

            if (c == '*')
                break;

            text.Append(c.Value);
            position += 10;
        }

        if (text.Length is 0)
            return null;

        var end = position + 9;

        // A character is 12 modules wide with wide elements of 2 modules, and is separated from the next one by a module
        var characters = text.Length + 2;
        return new LinearMatch(BarcodeType.Code39, text.ToString(), start, end, line.GetWidth(start, end - start) / ((13 * characters) - 1));
    }

    private static char? ReadCode39Character(ScanLine line, int start)
    {
        Span<bool> wide = stackalloc bool[9];
        if (!TryClassify(line, start, 9, 3, wide))
            return null;

        Span<char> pattern = stackalloc char[9];
        for (var i = 0; i < 9; i++)
        {
            pattern[i] = wide[i] ? 'w' : 'n';
        }

        return Code39Characters.TryGetValue(new string(pattern), out var c) ? c : null;
    }

    public static LinearMatch? TryDecodeCodabar(ScanLine line, int start)
    {
        var first = ReadCodabarCharacter(line, start);
        if (first is not ('A' or 'B' or 'C' or 'D'))
            return null;

        var text = new StringBuilder();
        text.Append(first.Value);
        var position = start + 8;
        while (true)
        {
            var c = ReadCodabarCharacter(line, position);
            if (c is null)
                return null;

            text.Append(c.Value);
            if (c is 'A' or 'B' or 'C' or 'D')
                break;

            position += 8;
        }

        if (text.Length < 3)
            return null;

        var end = position + 7;
        return new LinearMatch(BarcodeType.Codabar, text.ToString(), start, end, GetNarrowWidth(line, start, end));
    }

    private static char? ReadCodabarCharacter(ScanLine line, int start)
    {
        // The characters have 2 or 3 wide elements of 7
        Span<bool> wide = stackalloc bool[7];
        foreach (var wideCount in (ReadOnlySpan<int>)[2, 3])
        {
            if (!TryClassify(line, start, 7, wideCount, wide))
                continue;

            var encoding = 0;
            for (var i = 0; i < 7; i++)
            {
                encoding = (encoding << 1) | (wide[i] ? 1 : 0);
            }

            if (CodabarCharacters.TryGetValue(encoding, out var c))
                return c;
        }

        return null;
    }

    public static LinearMatch? TryDecodeItf(ScanLine line, int start)
    {
        // The start pattern is 4 narrow elements, the stop pattern a wide bar and two narrow elements
        if (start + 4 > line.Count)
            return null;

        var narrow = line.GetWidth(start, 4) / 4;
        for (var i = 0; i < 4; i++)
        {
            if (line.Runs[start + i] > narrow * 1.5)
                return null;
        }

        var text = new StringBuilder();
        var position = start + 4;
        Span<bool> wide = stackalloc bool[5];
        Span<int> digits = stackalloc int[2];
        while (true)
        {
            if (position + 3 > line.Count)
                return null;

            if (IsItfStop(line, position, narrow))
                break;

            if (position + 10 > line.Count)
                return null;

            // The bars give the first digit, the spaces the second one
            for (var k = 0; k < 2; k++)
            {
                if (!TryClassify(line, position + k, 5, 2, wide, stride: 2))
                    return null;

                digits[k] = -1;
                for (var digit = 0; digit < 10; digit++)
                {
                    var pattern = ItfEncoder.Patterns[digit];
                    var isMatch = true;
                    for (var i = 0; i < 5; i++)
                    {
                        isMatch &= (pattern[i] > 1) == wide[i];
                    }

                    if (isMatch)
                    {
                        digits[k] = digit;
                        break;
                    }
                }

                if (digits[k] < 0)
                    return null;
            }

            text.Append((char)('0' + digits[0])).Append((char)('0' + digits[1]));
            position += 10;
        }

        if (text.Length is 0)
            return null;

        return new LinearMatch(BarcodeType.Itf, text.ToString(), start, position + 3, narrow);
    }

    private static bool IsItfStop(ScanLine line, int position, double narrow)
    {
        // A wide bar, a narrow space and a narrow bar, followed by the quiet zone (or the end of the line)
        return line.Runs[position] > narrow * 1.5
            && line.Runs[position + 1] < narrow * 1.5
            && line.Runs[position + 2] < narrow * 1.5
            && (position + 3 >= line.Count || line.Runs[position + 3] > narrow * 5);
    }

    /// <summary>Classifies elements as wide or narrow: the <paramref name="wideCount"/> widest ones are wide when they are clearly wider.</summary>
    private static bool TryClassify(ScanLine line, int start, int count, int wideCount, Span<bool> wide, int stride = 1)
    {
        if (start + ((count - 1) * stride) >= line.Count)
            return false;

        Span<double> widths = stackalloc double[count];
        for (var i = 0; i < count; i++)
        {
            widths[i] = line.Runs[start + (i * stride)];
        }

        Span<double> sorted = stackalloc double[count];
        widths.CopyTo(sorted);
        sorted.Sort();
        var widestNarrow = sorted[count - wideCount - 1];
        var narrowestWide = sorted[count - wideCount];
        // The wide elements are 2 to 3 times as wide as the narrow ones: a smaller gap is noise
        if (widestNarrow <= 0 || narrowestWide < widestNarrow * 1.2)
            return false;

        var threshold = (widestNarrow + narrowestWide) / 2;
        for (var i = 0; i < count; i++)
        {
            wide[i] = widths[i] > threshold;
        }

        return true;
    }

    private static double GetNarrowWidth(ScanLine line, int start, int end)
    {
        var minimum = double.MaxValue;
        for (var i = start; i < end; i++)
        {
            minimum = Math.Min(minimum, line.Runs[i]);
        }

        return minimum;
    }
}
