using System.Numerics;

namespace Meziantou.Framework.Barcodes.Internal.Pdf417;

/// <summary>
/// Reads a PDF417 symbol (ISO/IEC 15438): the codewords of each row, placed by the row indicators, the Reed-Solomon error
/// correction over GF(929), and the Text, Byte and Numeric compaction modes of the data.
/// </summary>
/// <remarks>
/// Each row of the matrix is read on its own: a symbol row drawn on several matrix rows gets a vote per matrix row, and a
/// codeword that no row could read is an error for the error correction.
/// </remarks>
internal static class Pdf417Decoder
{
    private const int TextLatch = 900;
    private const int ByteLatch = 901;
    private const int NumericLatch = 902;
    private const int ByteShift = 913;
    private const int ReaderInitialisation = 921;
    private const int ByteLatchMultipleOf6 = 924;
    private const int EciGeneralPurpose = 925;
    private const int EciUserDefined = 926;
    private const int EciCharacterSet = 927;

    private static ReadOnlySpan<byte> MixedCharacters => "0123456789&\r\t,:#-.$/+%*=^"u8;

    private static ReadOnlySpan<byte> PunctuationCharacters => ";<>@[\\]_`~!\r\t,:\n-.$/\"|*()?{}'"u8;

    private enum TextSubMode
    {
        Alpha,
        Lower,
        Mixed,
        Punctuation,
    }

    /// <summary>Decodes the modules of a symbol, each row of the matrix running from the start pattern to the stop pattern.</summary>
    /// <returns>The reason of the failure, or <see langword="null"/>.</returns>
    public static string? TryDecode(bool[,] modules, DecodedDataBuilder data, out int correctedErrorCount)
    {
        correctedErrorCount = 0;
        var height = modules.GetLength(0);
        var width = modules.GetLength(1);
        if ((width - 1) % Pdf417Patterns.CodewordWidth is not 0 || ((width - 1) / Pdf417Patterns.CodewordWidth) - 4 is < 1 or > 30)
            return $"A matrix {width} modules wide is not the width of a PDF417 symbol.";

        var columns = ((width - 1) / Pdf417Patterns.CodewordWidth) - 4;
        var rows = new List<int[]>();
        for (var row = 0; row < height; row++)
        {
            var values = new int[width];
            for (var column = 0; column < width; column++)
            {
                values[column] = modules[row, column] ? 1 : 0;
            }

            // A row read from the right, in a mirrored or upside-down symbol, ends with the start pattern
            if (ReadPattern(values, 0, Pdf417Patterns.CodewordWidth) != Pdf417Patterns.StartPattern)
            {
                Array.Reverse(values);
            }

            rows.Add(values);
        }

        return TryDecodeRows(rows, columns, data, out correctedErrorCount);
    }

    /// <summary>Decodes the rows of modules of a symbol, 1 for dark, each from the start pattern to the stop pattern.</summary>
    public static string? TryDecodeRows(IReadOnlyList<int[]> rows, int columns, DecodedDataBuilder data, out int correctedErrorCount)
    {
        correctedErrorCount = 0;

        // The votes of the rows: the value of each codeword, and the row count, column count and level of the indicators
        var votes = new Dictionary<(int Row, int Column), Dictionary<int, int>>();
        var rowCountVotes = new Dictionary<int, int>();
        var levelVotes = new Dictionary<int, int>();
        var columnCountVotes = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            ReadRow(row, columns, votes, rowCountVotes, levelVotes, columnCountVotes);
        }

        if (rowCountVotes.Count is 0 || levelVotes.Count is 0)
            return "The row indicators of the PDF417 symbol cannot be read.";

        var rowsValue = GetMostVoted(rowCountVotes);
        var levelValue = GetMostVoted(levelVotes);
        var rowCount = (rowsValue * 3) + (levelValue % 3) + 1;
        var level = levelValue / 3;
        if (columnCountVotes.Count > 0 && GetMostVoted(columnCountVotes) + 1 != columns)
            return "The row indicators of the PDF417 symbol do not match its width.";

        if (rowCount is < 3 or > 90 || level > 8 || rowCount * columns > 928)
            return "The row indicators of the PDF417 symbol are not valid.";

        var codewords = new int[rowCount * columns];
        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                codewords[(row * columns) + column] = votes.TryGetValue((row, column), out var values) ? GetMostVoted(values) : 0;
            }
        }

        var errorCorrectionCount = Pdf417ErrorCorrection.GetCodewordCount(level);
        if (errorCorrectionCount >= codewords.Length)
            return "The PDF417 symbol has more error correction codewords than codewords.";

        correctedErrorCount = Pdf417ErrorCorrection.Correct(codewords, errorCorrectionCount);
        if (correctedErrorCount < 0)
            return "The PDF417 symbol has more errors than the error correction can fix.";

        // The symbol length descriptor counts itself, the data and the pad codewords
        var dataCount = codewords[0];
        if (dataCount < 1 || dataCount > codewords.Length - errorCorrectionCount)
            return "The symbol length descriptor of the PDF417 symbol is not valid.";

        return DecodeData(codewords.AsSpan(1, dataCount - 1), data);
    }

    private static void ReadRow(int[] row, int columns, Dictionary<(int Row, int Column), Dictionary<int, int>> votes, Dictionary<int, int> rowCountVotes, Dictionary<int, int> levelVotes, Dictionary<int, int> columnCountVotes)
    {
        var width = Pdf417Patterns.CodewordWidth;
        if (ReadPattern(row, 0, width) != Pdf417Patterns.StartPattern)
            return;

        // The left and right row indicators give the row number and, depending on the cluster, the row count, the column
        // count or the error correction level (ISO/IEC 15438, 5.6)
        var values = new (int Cluster, int Value)[columns + 2];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Pdf417Patterns.TryGetCodeword(ReadPattern(row, width * (i + 1), width), out var cluster, out var value) ? (cluster, value) : (-1, -1);
        }

        var clusterCounts = new int[3];
        foreach (var (cluster, _) in values)
        {
            if (cluster >= 0)
            {
                clusterCounts[cluster]++;
            }
        }

        var best = Math.Max(clusterCounts[0], Math.Max(clusterCounts[1], clusterCounts[2]));
        if (best is 0)
            return;

        var rowCluster = Array.IndexOf(clusterCounts, best);
        var (leftCluster, left) = values[0];
        var (rightCluster, right) = values[^1];
        var rowGroup = leftCluster == rowCluster ? left / 30 : rightCluster == rowCluster ? right / 30 : -1;
        if (rowGroup < 0)
            return;

        var rowNumber = (rowGroup * 3) + rowCluster;
        if (leftCluster == rowCluster)
        {
            AddIndicator(rowCluster switch { 0 => rowCountVotes, 1 => levelVotes, _ => columnCountVotes }, left % 30);
        }

        if (rightCluster == rowCluster)
        {
            AddIndicator(rowCluster switch { 0 => columnCountVotes, 1 => rowCountVotes, _ => levelVotes }, right % 30);
        }

        for (var column = 0; column < columns; column++)
        {
            var (cluster, value) = values[column + 1];
            if (cluster != rowCluster)
                continue;

            if (!votes.TryGetValue((rowNumber, column), out var counts))
            {
                counts = [];
                votes[(rowNumber, column)] = counts;
            }

            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        static void AddIndicator(Dictionary<int, int> counts, int value)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }
    }

    private static int ReadPattern(int[] row, int start, int count)
    {
        if (start + count > row.Length)
            return -1;

        var value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | row[start + i];
        }

        return value;
    }

    private static int GetMostVoted(Dictionary<int, int> votes)
    {
        var best = -1;
        var bestCount = 0;
        foreach (var (value, count) in votes)
        {
            if (count > bestCount || (count == bestCount && value < best))
            {
                best = value;
                bestCount = count;
            }
        }

        return best;
    }

    private static string? DecodeData(ReadOnlySpan<int> codewords, DecodedDataBuilder data)
    {
        // A symbol starts in the Alpha sub-mode of the Text compaction
        var subMode = TextSubMode.Alpha;
        var i = 0;
        while (i < codewords.Length)
        {
            var codeword = codewords[i];
            switch (codeword)
            {
                case < TextLatch:
                    subMode = DecodeText(codewords, ref i, subMode, data);
                    break;

                case TextLatch:
                    i++;
                    subMode = TextSubMode.Alpha;
                    break;

                case ByteLatch or ByteLatchMultipleOf6:
                    i++;
                    DecodeBytes(codewords, ref i, codeword is ByteLatchMultipleOf6, data);
                    subMode = TextSubMode.Alpha;
                    break;

                case NumericLatch:
                    i++;
                    var error = DecodeNumeric(codewords, ref i, data);
                    if (error is not null)
                        return error;

                    subMode = TextSubMode.Alpha;
                    break;

                case ByteShift:
                    if (i + 1 >= codewords.Length || codewords[i + 1] >= 256)
                        return "A byte shift of the PDF417 symbol is not followed by a byte.";

                    data.AppendByte(codewords[i + 1]);
                    i += 2;
                    break;

                case EciCharacterSet or EciGeneralPurpose:
                    if (i + 1 >= codewords.Length)
                        return "The ECI designator of the PDF417 symbol is not complete.";

                    data.SetEci(codeword is EciCharacterSet ? codewords[i + 1] : 810900 + codewords[i + 1]);
                    i += 2;
                    break;

                case EciUserDefined:
                    if (i + 2 >= codewords.Length)
                        return "The ECI designator of the PDF417 symbol is not complete.";

                    data.SetEci(((codewords[i + 1] + 1) * 900) + codewords[i + 2]);
                    i += 3;
                    break;

                case ReaderInitialisation:
                    i++;
                    break;

                default:
                    return $"The codeword {codeword} of the PDF417 symbol is not supported (Macro PDF417 is not supported).";
            }
        }

        return null;
    }

    private static TextSubMode DecodeText(ReadOnlySpan<int> codewords, ref int i, TextSubMode subMode, DecodedDataBuilder data)
    {
        // The sub-mode of the next character only, after a shift
        TextSubMode? shift = null;
        while (i < codewords.Length)
        {
            var codeword = codewords[i];
            if (codeword == ByteShift)
            {
                // The Text compaction resumes in the same sub-mode after the byte; a shift before it was padding
                shift = null;
                if (i + 1 >= codewords.Length || codewords[i + 1] >= 256)
                    return subMode;

                data.AppendByte(codewords[i + 1]);
                i += 2;
                continue;
            }

            if (codeword >= TextLatch)
                return subMode;

            i++;
            foreach (var value in (ReadOnlySpan<int>)[codeword / 30, codeword % 30])
            {
                var mode = shift ?? subMode;
                shift = null;
                switch (mode)
                {
                    case TextSubMode.Alpha:
                        switch (value)
                        {
                            case < 26:
                                data.AppendByte('A' + value);
                                break;
                            case 26:
                                data.AppendByte(' ');
                                break;
                            case 27:
                                subMode = TextSubMode.Lower;
                                break;
                            case 28:
                                subMode = TextSubMode.Mixed;
                                break;
                            default:
                                shift = TextSubMode.Punctuation;
                                break;
                        }

                        break;

                    case TextSubMode.Lower:
                        switch (value)
                        {
                            case < 26:
                                data.AppendByte('a' + value);
                                break;
                            case 26:
                                data.AppendByte(' ');
                                break;
                            case 27:
                                shift = TextSubMode.Alpha;
                                break;
                            case 28:
                                subMode = TextSubMode.Mixed;
                                break;
                            default:
                                shift = TextSubMode.Punctuation;
                                break;
                        }

                        break;

                    case TextSubMode.Mixed:
                        switch (value)
                        {
                            case < 25:
                                data.AppendByte(MixedCharacters[value]);
                                break;
                            case 25:
                                subMode = TextSubMode.Punctuation;
                                break;
                            case 26:
                                data.AppendByte(' ');
                                break;
                            case 27:
                                subMode = TextSubMode.Lower;
                                break;
                            case 28:
                                subMode = TextSubMode.Alpha;
                                break;
                            default:
                                shift = TextSubMode.Punctuation;
                                break;
                        }

                        break;

                    default:
                        if (value < 29)
                        {
                            data.AppendByte(PunctuationCharacters[value]);
                        }
                        else if (subMode is TextSubMode.Punctuation)
                        {
                            // Latch to Alpha; after a shift, 29 is padding
                            subMode = TextSubMode.Alpha;
                        }

                        break;
                }
            }
        }

        return subMode;
    }

    private static void DecodeBytes(ReadOnlySpan<int> codewords, ref int i, bool isMultipleOf6, DecodedDataBuilder data)
    {
        var start = i;
        while (i < codewords.Length && codewords[i] < TextLatch)
        {
            i++;
        }

        // Groups of 5 codewords hold 6 bytes in base 900. After 901, the last bytes are one codeword each: a last group of
        // 5 codewords is 5 bytes, since 6 bytes would have been a group after 924.
        var count = i - start;
        var groups = isMultipleOf6 ? count / 5 : Math.Max(0, (count - 1) / 5);
        var position = start;
        Span<byte> bytes = stackalloc byte[6];
        for (var group = 0; group < groups; group++)
        {
            var value = 0UL;
            for (var k = 0; k < 5; k++)
            {
                value = (value * 900) + (ulong)codewords[position++];
            }

            for (var k = 5; k >= 0; k--)
            {
                bytes[k] = (byte)value;
                value >>= 8;
            }

            foreach (var b in bytes)
            {
                data.AppendByte(b);
            }
        }

        while (position < i)
        {
            data.AppendByte(codewords[position++] & 0xFF);
        }
    }

    private static string? DecodeNumeric(ReadOnlySpan<int> codewords, ref int i, DecodedDataBuilder data)
    {
        // Groups of up to 15 codewords are a number in base 900, whose decimal digits follow a leading 1
        while (i < codewords.Length && codewords[i] < TextLatch)
        {
            var value = BigInteger.Zero;
            var count = 0;
            while (count < 15 && i < codewords.Length && codewords[i] < TextLatch)
            {
                value = (value * 900) + codewords[i++];
                count++;
            }

            var digits = value.ToString(CultureInfo.InvariantCulture);
            if (digits.Length < 2 || digits[0] != '1')
                return "A numeric group of the PDF417 symbol is not valid.";

            data.AppendText(digits[1..]);
        }

        return null;
    }
}
