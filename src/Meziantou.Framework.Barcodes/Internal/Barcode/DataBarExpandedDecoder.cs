namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// Reads GS1 DataBar Expanded and Expanded Stacked symbols (ISO/IEC 24724, 5.4 and 7.2.5): the rows give pairs of
/// characters around their finder patterns, <see cref="TryAssemble"/> puts the pairs of the rows in order with the finder
/// sequence of the symbol and checks its check character, and the data characters are decoded as element strings.
/// </summary>
internal static class DataBarExpandedDecoder
{
    private const int CharacterModules = 17;
    private const int FinderModules = 15;

    private static readonly int[][] Finders = [.. DataBarExpandedEncoder.FinderPatterns, .. DataBarExpandedEncoder.FinderPatterns.Select(pattern => pattern.Reverse().ToArray())];

    /// <summary>A pair of characters: the left one, the finder pattern, and the right one when there is one.</summary>
    public sealed record Pair(int Finder, bool IsOddPair, int LeftValue, int[] LeftWidths, int RightValue, int[]? RightWidths);

    /// <summary>The pairs of a row, in reading order.</summary>
    public sealed record Row(Pair[] Pairs, int Start, int End, double ModuleWidth)
    {
        /// <summary>Gets the first scan line that reads the row, when the caller knows it.</summary>
        public int FirstLine { get; init; }

        /// <summary>Gets the last scan line that reads the row, when the caller knows it.</summary>
        public int LastLine { get; init; }

        public string Signature => string.Join(";", Pairs.Select(pair => FormattableString.Invariant($"{pair.Finder},{pair.IsOddPair},{pair.LeftValue},{pair.RightValue}")));
    }

    /// <summary>Reads the pairs of a row, from the dark run of its left guard.</summary>
    public static Row? TryReadRow(ScanLine line, int start)
    {
        // The left guard is a light and a dark module before a light element, or a dark and a light module before a dark one
        foreach (var offset in (ReadOnlySpan<int>)[1, 2])
        {
            var row = TryReadRow(line, start, start + offset);
            if (row is not null)
                return row;
        }

        return null;
    }

    private static Row? TryReadRow(ScanLine line, int start, int position)
    {
        return TryReadPairs(line, start, position, []);
    }

    // Reads the pairs from a position: a pair has a right character unless it ends the row (and the symbol)
    private static Row? TryReadPairs(ScanLine line, int start, int position, List<Pair> pairs)
    {
        if (pairs.Count >= 11 || !TryReadCharacter(line, position, reversed: false, out var leftWidths, out var leftValue))
            return null;

        var finder = RunPatterns.FindBest(line, position + 8, Finders, FinderModules);
        if (finder < 0)
            return null;

        var moduleWidth = line.GetWidth(position, 13) / (CharacterModules + FinderModules);
        var afterFinder = position + 13;
        if (TryReadCharacter(line, afterFinder, reversed: true, out var rightWidths, out var rightValue))
        {
            // A wide light element of the next pair can look like the quiet zone: the row continues when it can
            pairs.Add(new Pair(finder % 6, finder >= 6, leftValue, leftWidths, rightValue, rightWidths));
            var next = afterFinder + 8;
            var row = TryReadPairs(line, start, next, pairs);
            if (row is not null)
                return row;

            if (TryGetEnd(line, next, moduleWidth) is { } end)
                return CreateRow(line, pairs, start, end);

            pairs.RemoveAt(pairs.Count - 1);
        }

        if (TryGetEnd(line, afterFinder, moduleWidth) is { } endAfterFinder)
        {
            pairs.Add(new Pair(finder % 6, finder >= 6, leftValue, leftWidths, -1, null));
            return CreateRow(line, pairs, start, endAfterFinder);
        }

        return null;
    }

    private static Row CreateRow(ScanLine line, List<Pair> pairs, int start, int end)
    {
        var modules = 0;
        foreach (var pair in pairs)
        {
            modules += CharacterModules + FinderModules + (pair.RightWidths is null ? 0 : CharacterModules);
        }

        return new Row([.. pairs], start, end, line.GetWidth(start, end - start) / (modules + 2));
    }

    // The right guard: a dark module, or a light and a dark module, then the quiet zone
    private static int? TryGetEnd(ScanLine line, int position, double moduleWidth)
    {
        bool IsGuard(int run) => run < line.Count && line.Runs[run] < moduleWidth * 2;
        // A light run that goes to the end of the line is the edge of the image: GS1 DataBar needs no quiet zone
        bool IsQuiet(int run) => run >= line.Count - 1 || line.Runs[run] > moduleWidth * 4;

        if (ScanLine.IsDark(position) && IsGuard(position) && IsQuiet(position + 1))
            return position + 1;

        if (!ScanLine.IsDark(position) && IsGuard(position) && IsGuard(position + 1) && IsQuiet(position + 2))
            return position + 2;

        return null;
    }

    private static bool TryReadCharacter(ScanLine line, int start, bool reversed, out int[] widths, out int value)
    {
        widths = new int[8];
        value = -1;
        if (!RunPatterns.TryRound(line, start, 8, CharacterModules, widths))
            return false;

        if (reversed)
        {
            Array.Reverse(widths);
        }

        Span<int> odd = [widths[0], widths[2], widths[4], widths[6]];
        Span<int> even = [widths[1], widths[3], widths[5], widths[7]];
        var oddModules = odd[0] + odd[1] + odd[2] + odd[3];
        var group = Array.IndexOf(DataBarExpandedEncoder.OddModules, oddModules);
        if (group < 0)
            return false;

        var oddValue = DataBarWidths.GetValue(odd, oddModules, DataBarExpandedEncoder.OddWidest[group], noNarrow: false);
        var evenValue = DataBarWidths.GetValue(even, DataBarExpandedEncoder.EvenModules[group], DataBarExpandedEncoder.EvenWidest[group], noNarrow: true);
        if (oddValue < 0 || evenValue < 0)
            return false;

        value = DataBarExpandedEncoder.GroupStarts[group] + (oddValue * DataBarExpandedEncoder.Combinations[group]) + evenValue;
        return true;
    }

    /// <summary>
    /// Puts the rows of a symbol in order: the first row starts with the check character, and the finder patterns of the
    /// pairs follow the sequence of the symbol (ISO/IEC 24724, Table 11).
    /// </summary>
    /// <returns>The rows in order and the text of the symbol, or <see langword="null"/>.</returns>
    public static (List<Row> Rows, string Text)? TryAssemble(IReadOnlyList<Row> rows)
    {
        // The longest rows first: a single-row symbol is read whole rather than from parts of its row
        rows = [.. rows.OrderByDescending(row => row.Pairs.Length)];
        foreach (var first in rows)
        {
            var pair0 = first.Pairs[0];
            if (pair0.Finder is not 0 || pair0.IsOddPair)
                continue;

            // The check character gives the number of symbol characters
            var symbolCharacters = (pair0.LeftValue / 211) + 4;
            if (symbolCharacters is < 4 or > 22)
                continue;

            var pairCount = (symbolCharacters + 1) / 2;
            var sequence = DataBarExpandedEncoder.FinderSequences[pairCount - 2];
            var ordered = new List<Row>();
            var text = TryFollow(rows, first, 0, sequence, symbolCharacters, ordered);
            if (text is not null)
                return (ordered, text);
        }

        return null;
    }

    // Follows the rows that continue the finder sequence; a complete sequence must have a valid check character, else the
    // other rows are tried (a row read in the other direction has the same finder patterns, the other way around)
    private static string? TryFollow(IReadOnlyList<Row> rows, Row row, int offset, int[] sequence, int symbolCharacters, List<Row> ordered)
    {
        if (offset + row.Pairs.Length > sequence.Length)
            return null;

        for (var i = 0; i < row.Pairs.Length; i++)
        {
            var pairIndex = offset + i;
            var pair = row.Pairs[i];
            if (pair.Finder != sequence[pairIndex] || pair.IsOddPair != (pairIndex % 2 is 1))
                return null;

            // Only the last pair can lack its right character, when the number of symbol characters is odd
            var hasRight = (2 * pairIndex) + 1 < symbolCharacters;
            if ((pair.RightWidths is not null) != hasRight)
                return null;
        }

        // The rows of a stacked symbol are read by lines in their order, or in the reverse order for an upside-down symbol
        if (ordered.Count > 0)
        {
            var previous = ordered[^1];
            var isAfter = row.FirstLine > previous.LastLine;
            var isBefore = row.LastLine < previous.FirstLine;
            if (!isAfter && !isBefore)
                return null;

            if (ordered.Count > 1 && isAfter != (previous.FirstLine > ordered[^2].LastLine))
                return null;
        }

        ordered.Add(row);
        var next = offset + row.Pairs.Length;
        if (next == sequence.Length)
        {
            var text = TryDecode(ordered, symbolCharacters, sequence);
            if (text is not null)
                return text;
        }
        else
        {
            foreach (var candidate in rows)
            {
                var text = TryFollow(rows, candidate, next, sequence, symbolCharacters, ordered);
                if (text is not null)
                    return text;
            }
        }

        ordered.RemoveAt(ordered.Count - 1);
        return null;
    }

    private static string? TryDecode(List<Row> rows, int symbolCharacters, int[] sequence)
    {
        var characters = new List<(int Value, int[] Widths)>(symbolCharacters);
        foreach (var pair in rows.SelectMany(row => row.Pairs))
        {
            characters.Add((pair.LeftValue, pair.LeftWidths));
            if (pair.RightWidths is not null)
            {
                characters.Add((pair.RightValue, pair.RightWidths));
            }
        }

        // ISO/IEC 24724, 7.2.6: the weights depend on the finder pattern next to the character and on its side
        var checksum = 0;
        for (var i = 1; i < symbolCharacters; i++)
        {
            var pair = i / 2;
            var weightRow = (4 * sequence[pair]) + (pair % 2 == 0 ? 0 : 2) + (i % 2) - 1;
            for (var j = 0; j < 8; j++)
            {
                checksum += DataBarHelper.GetChecksumWeight((weightRow * 8) + j, 211) * characters[i].Widths[j];
            }
        }

        if ((211 * (symbolCharacters - 4)) + (checksum % 211) != characters[0].Value)
            return null;

        var bits = new List<bool>((symbolCharacters - 1) * 12);
        for (var i = 1; i < symbolCharacters; i++)
        {
            if (characters[i].Value > 4095)
                return null;

            for (var j = 11; j >= 0; j--)
            {
                bits.Add(((characters[i].Value >> j) & 1) != 0);
            }
        }

        return DataBarExpandedBitDecoder.Decode(bits, symbolCharacters);
    }
}
