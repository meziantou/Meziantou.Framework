namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// Reads GS1 DataBar Omnidirectional and Limited symbols (ISO/IEC 24724, 5.2 and 5.3), and the rows of the stacked
/// variants, which the caller pairs with <see cref="TryCombine"/>.
/// </summary>
internal static class DataBarDecoder
{
    private static readonly int[][] LeftFinders = [.. Enumerable.Range(0, 9).Select(i => DataBarEncoder.OmnidirectionalFinderPatterns.AsSpan(i * 5, 5).ToArray())];
    private static readonly int[][] RightFinders = [.. LeftFinders.Select(pattern => pattern.Reverse().ToArray())];
    private static readonly int[][] LimitedCheckPatterns = [.. DataBarEncoder.LimitedCheckCharacters.Select(RunPatterns.FromDigits)];

    /// <summary>A row of a stacked symbol: the left half (characters 1 and 2) or the right half (characters 3 and 4).</summary>
    public sealed record Half(bool IsTop, int[] Values, int[][] Widths, int Finder, int Start, int End, double ModuleWidth);

    public static LinearMatch? TryDecodeOmnidirectional(ScanLine line, int start)
    {
        // From the first bar of the left guard: character 1, the left finder, character 2 (reversed), character 4, the right
        // finder (reversed), character 3 (reversed) and the right guard: 45 elements, 95 modules
        if (start + 45 > line.Count)
            return null;

        var widths = new int[4][];
        var values = new int[4];
        if (!TryReadCharacter(line, start + 1, outside: true, reversed: false, out widths[0], out values[0])
            || !TryReadCharacter(line, start + 14, outside: false, reversed: true, out widths[1], out values[1])
            || !TryReadCharacter(line, start + 22, outside: false, reversed: false, out widths[3], out values[3])
            || !TryReadCharacter(line, start + 35, outside: true, reversed: true, out widths[2], out values[2]))
        {
            return null;
        }

        var leftFinder = RunPatterns.FindBest(line, start + 9, LeftFinders, 15);
        var rightFinder = RunPatterns.FindBest(line, start + 30, RightFinders, 15);
        if (leftFinder < 0 || rightFinder < 0 || !RunPatterns.Matches(line, start + 43, [1, 1]))
            return null;

        var text = GetOmnidirectionalText(values, widths, leftFinder, rightFinder);
        if (text is null)
            return null;

        return new LinearMatch(BarcodeType.Gs1DataBar, text, start, start + 45, line.GetWidth(start, 45) / 95);
    }

    /// <summary>Reads a row of a GS1 DataBar Stacked or Stacked Omnidirectional symbol.</summary>
    public static Half? TryReadHalf(ScanLine line, int start)
    {
        // The top row: the bar of the left guard, character 1, the left finder, character 2 (reversed), a bar and a space
        if (start + 24 <= line.Count
            && TryReadCharacter(line, start + 1, outside: true, reversed: false, out var widths0, out var value0)
            && TryReadCharacter(line, start + 14, outside: false, reversed: true, out var widths1, out var value1)
            && line.Runs[start + 22] < line.GetWidth(start + 1, 8) / 16 * 2)
        {
            var finder = RunPatterns.FindBest(line, start + 9, LeftFinders, 15);
            if (finder >= 0)
                return new Half(IsTop: true, [value0, value1], [widths0, widths1], finder, start, start + 23, line.GetWidth(start, 23) / 48);
        }

        // The bottom row: a bar and a space, character 4, the right finder (reversed), character 3 (reversed), the right guard
        if (start + 25 <= line.Count
            && RunPatterns.Matches(line, start, [1, 1])
            && TryReadCharacter(line, start + 2, outside: false, reversed: false, out var widths3, out var value3)
            && TryReadCharacter(line, start + 15, outside: true, reversed: true, out var widths2, out var value2)
            && RunPatterns.Matches(line, start + 23, [1, 1]))
        {
            var finder = RunPatterns.FindBest(line, start + 10, RightFinders, 15);
            if (finder >= 0)
                return new Half(IsTop: false, [value2, value3], [widths2, widths3], finder, start, start + 25, line.GetWidth(start, 25) / 50);
        }

        return null;
    }

    /// <summary>Combines the top and bottom rows of a stacked symbol, and returns its text when its checksum matches.</summary>
    public static string? TryCombine(Half top, Half bottom)
    {
        return GetOmnidirectionalText([top.Values[0], top.Values[1], bottom.Values[0], bottom.Values[1]], [top.Widths[0], top.Widths[1], bottom.Widths[0], bottom.Widths[1]], top.Finder, bottom.Finder);
    }

    private static string? GetOmnidirectionalText(int[] values, int[][] widths, int leftFinder, int rightFinder)
    {
        // The checksum gives the pair of finder patterns, among the 9 x 9 pairs less 2
        var checksum = 0;
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 8; j++)
            {
                checksum += DataBarHelper.GetChecksumWeight((i * 8) + j, 79) * widths[i][j];
            }
        }

        checksum %= 79;
        if (checksum >= 8)
        {
            checksum++;
        }

        if (checksum >= 72)
        {
            checksum++;
        }

        if (checksum / 9 != leftFinder || checksum % 9 != rightFinder)
            return null;

        var left = ((long)values[0] * 1597) + values[1];
        var right = ((long)values[2] * 1597) + values[3];
        var value = (left * 4537077) + right;
        if (value >= 10_000_000_000_000)
            return null;

        return GetGtinText(value);
    }

    public static LinearMatch? TryDecodeLimited(ScanLine line, int start)
    {
        // From the first bar of the left guard: the left character, the check character, the right character and the right
        // guard: 45 elements, 73 modules
        if (start + 45 > line.Count || !RunPatterns.Matches(line, start + 43, [1, 1]))
            return null;

        Span<int> widths = stackalloc int[28];
        if (!TryReadLimitedCharacter(line, start + 1, widths[..14], out var left) || !TryReadLimitedCharacter(line, start + 29, widths[14..], out var right))
            return null;

        var checksum = 0;
        for (var i = 0; i < 28; i++)
        {
            checksum += DataBarHelper.GetChecksumWeight(i, 89) * widths[i];
        }

        if (RunPatterns.FindBest(line, start + 15, LimitedCheckPatterns, 18) != checksum % 89)
            return null;

        var value = ((long)left * 2013571) + right;
        if (value >= 2_000_000_000_000)
            return null;

        return new LinearMatch(BarcodeType.Gs1DataBarLimited, GetGtinText(value), start, start + 45, line.GetWidth(start, 45) / 73);
    }

    private static string GetGtinText(long value)
    {
        var digits = value.ToString("D13", CultureInfo.InvariantCulture);
        return "(01)" + digits + DataBarHelper.ComputeGtinCheckDigit(digits).ToString(CultureInfo.InvariantCulture);
    }

    // An outside character is 16 modules wide, an inside character 15 (ISO/IEC 24724, Table 3)
    private static bool TryReadCharacter(ScanLine line, int start, bool outside, bool reversed, out int[] widths, out int value)
    {
        widths = new int[8];
        value = -1;
        if (!RunPatterns.TryRound(line, start, 8, outside ? 16 : 15, widths))
            return false;

        if (reversed)
        {
            Array.Reverse(widths);
        }

        Span<int> odd = [widths[0], widths[2], widths[4], widths[6]];
        Span<int> even = [widths[1], widths[3], widths[5], widths[7]];
        var oddModules = odd[0] + odd[1] + odd[2] + odd[3];
        var first = outside ? 0 : 5;
        var last = outside ? 4 : 8;
        for (var group = first; group <= last; group++)
        {
            if (DataBarEncoder.OmnidirectionalOddModules[group] != oddModules)
                continue;

            var oddValue = DataBarWidths.GetValue(odd, oddModules, DataBarEncoder.OmnidirectionalOddWidest[group], noNarrow: outside);
            var evenValue = DataBarWidths.GetValue(even, DataBarEncoder.OmnidirectionalEvenModules[group], DataBarEncoder.OmnidirectionalEvenWidest[group], noNarrow: !outside);
            if (oddValue < 0 || evenValue < 0)
                return false;

            // The outside characters have the odd value first, the inside ones the even value first
            var combinations = DataBarEncoder.OmnidirectionalCombinations[group];
            value = DataBarEncoder.OmnidirectionalGroupStarts[group] + (outside ? (oddValue * combinations) + evenValue : (evenValue * combinations) + oddValue);
            return true;
        }

        return false;
    }

    // A character of GS1 DataBar Limited: 14 elements, 26 modules (ISO/IEC 24724, Table 6)
    private static bool TryReadLimitedCharacter(ScanLine line, int start, Span<int> widths, out int value)
    {
        value = -1;
        if (!RunPatterns.TryRound(line, start, 14, 26, widths))
            return false;

        Span<int> odd = stackalloc int[7];
        Span<int> even = stackalloc int[7];
        var oddModules = 0;
        for (var i = 0; i < 7; i++)
        {
            odd[i] = widths[2 * i];
            even[i] = widths[(2 * i) + 1];
            oddModules += odd[i];
        }

        var group = Array.IndexOf(DataBarEncoder.LimitedOddModules, oddModules);
        if (group < 0)
            return false;

        var oddValue = DataBarWidths.GetValue(odd, oddModules, DataBarEncoder.LimitedOddWidest[group], noNarrow: true);
        var evenValue = DataBarWidths.GetValue(even, DataBarEncoder.LimitedEvenModules[group], DataBarEncoder.LimitedEvenWidest[group], noNarrow: false);
        if (oddValue < 0 || evenValue < 0)
            return false;

        value = DataBarEncoder.LimitedGroupStarts[group] + (oddValue * DataBarEncoder.LimitedCombinations[group]) + evenValue;
        return true;
    }
}
