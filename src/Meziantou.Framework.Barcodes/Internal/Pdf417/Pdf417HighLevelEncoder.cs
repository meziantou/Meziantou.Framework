namespace Meziantou.Framework.Barcodes.Internal.Pdf417;

/// <summary>
/// Converts the data to data codewords with the Text, Byte and Numeric compaction modes (ISO/IEC 15438, 5.4). Runs of 13
/// digits or more use the Numeric compaction, runs of 5 text characters or more the Text compaction, and the other bytes the
/// Byte compaction (a single byte between texts is a shift from the Text compaction).
/// </summary>
internal static class Pdf417HighLevelEncoder
{
    private const int TextLatch = 900;
    private const int ByteLatch = 901;
    private const int NumericLatch = 902;
    private const int ByteShift = 913;
    private const int ByteLatchMultipleOf6 = 924;
    private const int EciGeneralPurpose = 925;
    private const int EciUserDefined = 926;
    private const int EciCharacterSet = 927;

    private const int MinimumNumericCount = 13;
    private const int MinimumTextCount = 5;

    // The values of the characters in the Mixed and Punctuation sub-modes, indexed by the character
    private static ReadOnlySpan<byte> MixedCharacters => "0123456789&\r\t,:#-.$/+%*=^"u8;

    private static ReadOnlySpan<byte> PunctuationCharacters => ";<>@[\\]_`~!\r\t,:\n-.$/\"|*()?{}'"u8;

    private enum Mode
    {
        Text,
        Byte,
        Numeric,
    }

    private enum TextSubMode
    {
        Alpha,
        Lower,
        Mixed,
        Punctuation,
    }

    public static void Encode(ReadOnlySpan<byte> data, int? eci, List<int> codewords)
    {
        if (eci is { } assignmentNumber)
        {
            AppendEci(assignmentNumber, codewords);
        }

        // A symbol starts in the Alpha sub-mode of the Text compaction
        var mode = Mode.Text;
        var subMode = TextSubMode.Alpha;
        var position = 0;
        while (position < data.Length)
        {
            var digitCount = CountDigits(data[position..]);
            if (digitCount >= MinimumNumericCount)
            {
                codewords.Add(NumericLatch);
                mode = Mode.Numeric;
                subMode = TextSubMode.Alpha;
                EncodeNumeric(data.Slice(position, digitCount), codewords);
                position += digitCount;
                continue;
            }

            var textCount = CountText(data[position..]);
            if (textCount >= MinimumTextCount || textCount == data.Length - position)
            {
                if (mode is not Mode.Text)
                {
                    codewords.Add(TextLatch);
                    mode = Mode.Text;
                    subMode = TextSubMode.Alpha;
                }

                subMode = EncodeText(data.Slice(position, textCount), subMode, codewords);
                position += textCount;
                continue;
            }

            var byteCount = Math.Max(1, CountBytes(data[position..]));
            if (byteCount == 1 && mode is Mode.Text)
            {
                // The Text compaction resumes after the byte, in the same sub-mode
                codewords.Add(ByteShift);
                codewords.Add(data[position]);
            }
            else
            {
                EncodeBytes(data.Slice(position, byteCount), codewords);
                mode = Mode.Byte;
                subMode = TextSubMode.Alpha;
            }

            position += byteCount;
        }
    }

    private static void AppendEci(int assignmentNumber, List<int> codewords)
    {
        switch (assignmentNumber)
        {
            case < 900:
                codewords.Add(EciCharacterSet);
                codewords.Add(assignmentNumber);
                break;

            case < 810900:
                codewords.Add(EciUserDefined);
                codewords.Add((assignmentNumber / 900) - 1);
                codewords.Add(assignmentNumber % 900);
                break;

            default:
                codewords.Add(EciGeneralPurpose);
                codewords.Add(assignmentNumber - 810900);
                break;
        }
    }

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool IsText(byte value) => value is (byte)'\t' or (byte)'\n' or (byte)'\r' or (>= 32 and <= 126);

    private static int CountDigits(ReadOnlySpan<byte> data)
    {
        var count = 0;
        while (count < data.Length && IsDigit(data[count]))
        {
            count++;
        }

        return count;
    }

    // The number of text characters before a run of digits long enough for the Numeric compaction
    private static int CountText(ReadOnlySpan<byte> data)
    {
        var index = 0;
        while (index < data.Length)
        {
            var digitCount = CountDigits(data[index..]);
            if (digitCount >= MinimumNumericCount)
                return index;

            if (digitCount > 0)
            {
                index += digitCount;
                continue;
            }

            if (!IsText(data[index]))
                break;

            index++;
        }

        return index;
    }

    // The number of bytes before a run of digits or of text long enough for the Numeric or the Text compaction
    private static int CountBytes(ReadOnlySpan<byte> data)
    {
        var index = 0;
        while (index < data.Length)
        {
            if (CountDigits(data[index..]) >= MinimumNumericCount)
                return index;

            var textCount = 0;
            while (textCount < MinimumTextCount && index + textCount < data.Length && IsText(data[index + textCount]))
            {
                textCount++;
            }

            if (textCount >= MinimumTextCount)
                return index;

            index++;
        }

        return index;
    }

    private static TextSubMode EncodeText(ReadOnlySpan<byte> data, TextSubMode subMode, List<int> codewords)
    {
        // Each codeword holds two values from 0 to 29
        var values = new List<int>(data.Length * 2);
        var index = 0;
        while (index < data.Length)
        {
            var value = data[index];
            switch (subMode)
            {
                case TextSubMode.Alpha:
                    if (IsUpperOrSpace(value))
                    {
                        values.Add(value is (byte)' ' ? 26 : value - 'A');
                    }
                    else if (IsLowerOrSpace(value))
                    {
                        values.Add(27); // Latch to Lower
                        subMode = TextSubMode.Lower;
                        continue;
                    }
                    else if (IsMixed(value))
                    {
                        values.Add(28); // Latch to Mixed
                        subMode = TextSubMode.Mixed;
                        continue;
                    }
                    else
                    {
                        values.Add(29); // Shift to Punctuation
                        values.Add(PunctuationCharacters.IndexOf(value));
                    }

                    break;

                case TextSubMode.Lower:
                    if (IsLowerOrSpace(value))
                    {
                        values.Add(value is (byte)' ' ? 26 : value - 'a');
                    }
                    else if (IsUpperOrSpace(value))
                    {
                        values.Add(27); // Shift to Alpha
                        values.Add(value - 'A');
                    }
                    else if (IsMixed(value))
                    {
                        values.Add(28); // Latch to Mixed
                        subMode = TextSubMode.Mixed;
                        continue;
                    }
                    else
                    {
                        values.Add(29); // Shift to Punctuation
                        values.Add(PunctuationCharacters.IndexOf(value));
                    }

                    break;

                case TextSubMode.Mixed:
                    if (IsMixed(value))
                    {
                        values.Add(value is (byte)' ' ? 26 : MixedCharacters.IndexOf(value));
                    }
                    else if (IsUpperOrSpace(value))
                    {
                        values.Add(28); // Latch to Alpha
                        subMode = TextSubMode.Alpha;
                        continue;
                    }
                    else if (IsLowerOrSpace(value))
                    {
                        values.Add(27); // Latch to Lower
                        subMode = TextSubMode.Lower;
                        continue;
                    }
                    else if (index + 1 < data.Length && IsPunctuation(data[index + 1]))
                    {
                        values.Add(25); // Latch to Punctuation
                        subMode = TextSubMode.Punctuation;
                        continue;
                    }
                    else
                    {
                        values.Add(29); // Shift to Punctuation
                        values.Add(PunctuationCharacters.IndexOf(value));
                    }

                    break;

                default:
                    if (IsPunctuation(value))
                    {
                        values.Add(PunctuationCharacters.IndexOf(value));
                    }
                    else
                    {
                        values.Add(29); // Latch to Alpha
                        subMode = TextSubMode.Alpha;
                        continue;
                    }

                    break;
            }

            index++;
        }

        if (values.Count % 2 != 0)
        {
            // The padding value 29 is a shift to Punctuation, or a latch to Alpha in the Punctuation sub-mode
            values.Add(29);
            if (subMode is TextSubMode.Punctuation)
            {
                subMode = TextSubMode.Alpha;
            }
        }

        for (var i = 0; i < values.Count; i += 2)
        {
            codewords.Add((values[i] * 30) + values[i + 1]);
        }

        return subMode;
    }

    private static bool IsUpperOrSpace(byte value) => value is (>= (byte)'A' and <= (byte)'Z') or (byte)' ';

    private static bool IsLowerOrSpace(byte value) => value is (>= (byte)'a' and <= (byte)'z') or (byte)' ';

    private static bool IsMixed(byte value) => value is (byte)' ' || MixedCharacters.Contains(value);

    private static bool IsPunctuation(byte value) => PunctuationCharacters.Contains(value);

    private static void EncodeBytes(ReadOnlySpan<byte> data, List<int> codewords)
    {
        // 924 tells that the last group of 5 codewords holds 6 bytes; after 901, the last bytes are one codeword each
        codewords.Add(data.Length % 6 == 0 ? ByteLatchMultipleOf6 : ByteLatch);
        Span<int> group = stackalloc int[5];
        while (data.Length >= 6)
        {
            var value = 0UL;
            for (var i = 0; i < 6; i++)
            {
                value = (value << 8) | data[i];
            }

            for (var i = 4; i >= 0; i--)
            {
                group[i] = (int)(value % 900);
                value /= 900;
            }

            foreach (var codeword in group)
            {
                codewords.Add(codeword);
            }

            data = data[6..];
        }

        foreach (var value in data)
        {
            codewords.Add(value);
        }
    }

    private static void EncodeNumeric(ReadOnlySpan<byte> digits, List<int> codewords)
    {
        // Each group of up to 44 digits, preceded by the digit 1, is a number written in base 900
        Span<byte> number = stackalloc byte[45];
        Span<int> group = stackalloc int[15];
        while (!digits.IsEmpty)
        {
            var length = Math.Min(44, digits.Length);
            var dividend = number[..(length + 1)];
            dividend[0] = 1;
            for (var i = 0; i < length; i++)
            {
                dividend[i + 1] = (byte)(digits[i] - '0');
            }

            var count = 0;
            var start = 0;
            while (start < dividend.Length)
            {
                // Divides the decimal number by 900; the remainder is the next codeword, least significant first
                var remainder = 0;
                for (var i = start; i < dividend.Length; i++)
                {
                    var current = (remainder * 10) + dividend[i];
                    dividend[i] = (byte)(current / 900);
                    remainder = current % 900;
                }

                group[count++] = remainder;
                while (start < dividend.Length && dividend[start] == 0)
                {
                    start++;
                }
            }

            for (var i = count - 1; i >= 0; i--)
            {
                codewords.Add(group[i]);
            }

            digits = digits[length..];
        }
    }
}
