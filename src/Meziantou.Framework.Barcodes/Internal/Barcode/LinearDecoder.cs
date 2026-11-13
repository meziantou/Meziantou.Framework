namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// Finds the linear barcodes of scan lines: each line is read in both directions, a symbol must have its quiet zones, the
/// same symbol on several lines is one result, and the rows of the stacked GS1 DataBar symbols are put together.
/// </summary>
internal static class LinearDecoder
{
    // The quiet zone before and after a symbol, in modules (narrow elements for Code 39, Codabar and ITF). The symbologies
    // require 10 modules or more (GS1 DataBar none: its guards delimit it); less is accepted to read symbols drawn with a
    // smaller quiet zone, but the symbologies without check character need more, so that they are not read inside other
    // symbols.
    private static double GetQuietZoneModules(BarcodeType type) => type switch
    {
        BarcodeType.Code39 or BarcodeType.Codabar or BarcodeType.Itf => 7,
        BarcodeType.Gs1DataBar or BarcodeType.Gs1DataBarLimited => 0,
        _ => 4,
    };

    private delegate LinearMatch? SymbolDecoder(ScanLine line, int start);

    // The symbologies with the strongest check characters first: a weaker one can match the bars of another symbol
    private static readonly SymbolDecoder[] Decoders =
    [
        LinearSymbolDecoders.TryDecodeCode128,
        LinearSymbolDecoders.TryDecodeCode93,
        DataBarDecoder.TryDecodeOmnidirectional,
        DataBarDecoder.TryDecodeLimited,
        EanUpcDecoder.TryDecode,
        LinearSymbolDecoders.TryDecodeCode39,
        LinearSymbolDecoders.TryDecodeCodabar,
        LinearSymbolDecoders.TryDecodeItf,
    ];

    public static List<BarcodeDecodeResult> Decode(IReadOnlyList<int[]> scanLines)
    {
        var results = new List<BarcodeDecodeResult>();
        var halves = new List<(DataBarDecoder.Half Half, int Line, long Start, long End)>();
        var expandedRows = new List<(DataBarExpandedDecoder.Row Row, int Line, long Start, long End)>();
        for (var lineIndex = 0; lineIndex < scanLines.Count; lineIndex++)
        {
            var forward = new ScanLine(scanLines[lineIndex]);
            if (forward.Count < 2)
                continue;

            var backward = forward.Reverse();
            foreach (var (line, isReversed) in new[] { (forward, false), (backward, true) })
            {
                ScanLineSymbols(line, isReversed, lineIndex, (long)Math.Round(forward.TotalWidth, MidpointRounding.AwayFromZero), results, halves, expandedRows);
            }
        }

        AssembleStackedOmnidirectional(halves, results);
        AssembleExpanded(expandedRows, results);
        return results;
    }

    private static void ScanLineSymbols(ScanLine line, bool isReversed, int lineIndex, long totalWidth, List<BarcodeDecodeResult> results, List<(DataBarDecoder.Half Half, int Line, long Start, long End)> halves, List<(DataBarExpandedDecoder.Row Row, int Line, long Start, long End)> expandedRows)
    {
        (long Start, long End) GetRange(int start, int end)
        {
            var from = (long)Math.Round(line.GetPosition(start), MidpointRounding.AwayFromZero);
            var to = (long)Math.Round(line.GetPosition(end), MidpointRounding.AwayFromZero);
            return isReversed ? (totalWidth - to, totalWidth - from) : (from, to);
        }

        bool HasQuietZones(int start, int end, double moduleWidth, double modules)
        {
            var minimum = moduleWidth * modules;
            return line.Runs[start - 1] >= minimum && (end >= line.Count || line.Runs[end] >= minimum);
        }

        // The lines with the growth of the bars removed, by growth: the growth is estimated at each start, and is the same
        // along a symbol
        var compensatedLines = new Dictionary<double, ScanLine>();
        ScanLine? GetCompensated(int start)
        {
            var growth = EstimateGrowth(line, start);
            if (growth is not { } value)
                return null;

            if (!compensatedLines.TryGetValue(value, out var compensated))
            {
                compensated = line.Compensate(value);
                compensatedLines[value] = compensated;
            }

            return compensated;
        }

        LinearMatch? Decode(ScanLine candidate, int start)
        {
            foreach (var decoder in Decoders)
            {
                var match = decoder(candidate, start);
                if (match is not null && HasQuietZones(match.Start, match.End, match.ModuleWidth, GetQuietZoneModules(match.Type)))
                    return match;
            }

            return null;
        }

        for (var start = 1; start < line.Count; start += 2)
        {
            var compensated = default(ScanLine);
            var match = Decode(line, start);
            if (match is null)
            {
                compensated = GetCompensated(start);
                if (compensated is not null)
                {
                    match = Decode(compensated, start);
                }
            }

            if (match is not null)
            {
                var (from, to) = GetRange(match.Start, match.End);
                AddResult(results, match.Type, match.Text, lineIndex, lineIndex, from, to);

                // Continue after the symbol: the next dark run
                start = match.End - (ScanLine.IsDark(match.End) ? 2 : 1);
                continue;
            }

            // A row whose bars grew can read as valid characters with other values: both readings are candidates, and the
            // checksum of the whole symbol chooses
            foreach (var candidate in (ReadOnlySpan<ScanLine?>)[line, compensated])
            {
                if (candidate is null)
                    continue;

                if (DataBarDecoder.TryReadHalf(candidate, start) is { } half)
                {
                    var (from, to) = GetRange(half.Start, half.End);
                    halves.Add((half, lineIndex, from, to));
                }

                if (DataBarExpandedDecoder.TryReadRow(candidate, start) is { } row)
                {
                    var (from, to) = GetRange(row.Start, row.End);
                    expandedRows.Add((row, lineIndex, from, to));
                }
            }
        }
    }

    // The bars of a printed symbol spread, and a threshold that is not in the middle of the gray levels moves all the edges
    // the same way: the narrowest bar and the narrowest space of the start of a symbol are as wide, the difference is four
    // times the growth of each edge. Returns the growth, rounded to a quarter of a unit, or null when there is none to remove.
    private static double? EstimateGrowth(ScanLine line, int start)
    {
        var narrowestBar = double.MaxValue;
        var narrowestSpace = double.MaxValue;
        for (var i = start; i < Math.Min(line.Count, start + 24); i++)
        {
            if (ScanLine.IsDark(i))
            {
                narrowestBar = Math.Min(narrowestBar, line.Runs[i]);
            }
            else
            {
                narrowestSpace = Math.Min(narrowestSpace, line.Runs[i]);
            }
        }

        if (narrowestBar == double.MaxValue || narrowestSpace == double.MaxValue || Math.Abs(narrowestBar - narrowestSpace) < (narrowestBar + narrowestSpace) / 8)
            return null;

        return Math.Round((narrowestBar - narrowestSpace) / 4 * 4, MidpointRounding.AwayFromZero) / 4;
    }

    // The same symbol on several lines, or read in both directions, is one result: it has the same text where it overlaps
    private static void AddResult(List<BarcodeDecodeResult> results, BarcodeType type, string text, int firstLine, int lastLine, long start, long end)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (result.Type == type && result.Text == text && result.Start < end && start < result.End && firstLine <= result.LastScanLine + 1 && result.FirstScanLine <= lastLine + 1)
            {
                results[i] = new BarcodeDecodeResult(type, text, Math.Min(result.FirstScanLine, firstLine), Math.Max(result.LastScanLine, lastLine), Math.Min(result.Start, start), Math.Max(result.End, end));
                return;
            }
        }

        results.Add(new BarcodeDecodeResult(type, text, firstLine, lastLine, start, end));
    }

    // The scan lines that read the same row of a label are next to each other, but for a few lines that cannot read it
    private const int MaximumLineGap = 3;

    // The detections of the same row data that are near each other: one per row of each label, so that a label printed
    // twice gives two labels
    private static List<List<T>> GroupNearby<T>(IEnumerable<T> items, Func<T, string> getKey, Func<T, int> getLine, Func<T, long> getStart, Func<T, long> getEnd)
    {
        var groups = new List<List<T>>();
        foreach (var sameKey in items.GroupBy(getKey, StringComparer.Ordinal))
        {
            var keyGroups = new List<(List<T> Items, int LastLine, long Start, long End)>();
            foreach (var item in sameKey.OrderBy(getLine))
            {
                var line = getLine(item);
                var start = getStart(item);
                var end = getEnd(item);
                var index = keyGroups.FindIndex(group => line - group.LastLine <= MaximumLineGap && start < group.End && group.Start < end);
                if (index < 0)
                {
                    keyGroups.Add(([item], line, start, end));
                }
                else
                {
                    var group = keyGroups[index];
                    group.Items.Add(item);
                    keyGroups[index] = (group.Items, Math.Max(group.LastLine, line), Math.Min(group.Start, start), Math.Max(group.End, end));
                }
            }

            groups.AddRange(keyGroups.Select(group => group.Items));
        }

        return groups;
    }

    // The number of lines between two ranges of lines, 0 when they overlap
    private static int GetLineDistance(int firstLine1, int lastLine1, int firstLine2, int lastLine2) => Math.Max(0, Math.Max(firstLine2 - lastLine1, firstLine1 - lastLine2));

    // GS1 DataBar Stacked and Stacked Omnidirectional: a top row above a bottom row, whose checksum matches. The rows of
    // Stacked Omnidirectional are as high as each other, the top row of Stacked is shorter. The bottom row of a top row is
    // the nearest one that matches it: the same label printed twice has two of each.
    private static void AssembleStackedOmnidirectional(List<(DataBarDecoder.Half Half, int Line, long Start, long End)> halves, List<BarcodeDecodeResult> results)
    {
        var rows = GroupNearby(halves, item => (item.Half.IsTop ? "T" : "B") + string.Join(",", item.Half.Values), item => item.Line, item => item.Start, item => item.End)
            .Select(group => (group[0].Half, FirstLine: group.Min(item => item.Line), LastLine: group.Max(item => item.Line), Start: group.Min(item => item.Start), End: group.Max(item => item.End)))
            .ToList();
        var usedBottoms = new HashSet<int>();
        foreach (var top in rows.Where(row => row.Half.IsTop))
        {
            // The bottom row is after the top row, or before it when the lines cross an upside-down symbol from the bottom
            var bottoms = Enumerable.Range(0, rows.Count)
                .Where(index => !usedBottoms.Contains(index) && !rows[index].Half.IsTop && (rows[index].FirstLine > top.LastLine || rows[index].LastLine < top.FirstLine) && rows[index].Start < top.End && top.Start < rows[index].End)
                .OrderBy(index => GetLineDistance(top.FirstLine, top.LastLine, rows[index].FirstLine, rows[index].LastLine));
            foreach (var index in bottoms)
            {
                var bottom = rows[index];
                var text = DataBarDecoder.TryCombine(top.Half, bottom.Half);
                if (text is null)
                    continue;

                var topHeight = top.LastLine - top.FirstLine + 1;
                var bottomHeight = bottom.LastLine - bottom.FirstLine + 1;
                var type = topHeight * 6 < bottomHeight * 5 ? BarcodeType.Gs1DataBarStacked : BarcodeType.Gs1DataBarStackedOmnidirectional;
                AddResult(results, type, text, Math.Min(top.FirstLine, bottom.FirstLine), Math.Max(top.LastLine, bottom.LastLine), Math.Min(top.Start, bottom.Start), Math.Max(top.End, bottom.End));
                usedBottoms.Add(index);
                break;
            }
        }
    }

    // GS1 DataBar Expanded and Expanded Stacked: the rows that make a symbol, in the order of their finder patterns. The rows
    // of a stacked symbol are below each other: the lines that read them are in the same order (or the reverse order, for
    // an upside-down symbol), which keeps the combinations of rows of other symbols out. The same label printed twice has
    // two of each row: each is a separate row, and the rows of a symbol are the nearest ones.
    private static void AssembleExpanded(List<(DataBarExpandedDecoder.Row Row, int Line, long Start, long End)> rows, List<BarcodeDecodeResult> results)
    {
        if (rows.Count is 0)
            return;

        var groups = GroupNearby(rows, item => item.Row.Signature, item => item.Line, item => item.Start, item => item.End);
        var groupRows = groups.Select(group => group[0].Row with { FirstLine = group.Min(item => item.Line), LastLine = group.Max(item => item.Line) }).ToList();
        var used = new bool[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            if (used[i])
                continue;

            // The rows of a symbol overlap horizontally
            var candidates = Enumerable.Range(0, groups.Count)
                .Where(other => !used[other] && groups[other].Any(item => groups[i].Any(first => item.Start < first.End && first.Start < item.End)))
                .ToList();
            var assembled = DataBarExpandedDecoder.TryAssemble([.. candidates.Select(index => groupRows[index])]);
            if (assembled is null)
                continue;

            var (orderedRows, text) = assembled.Value;
            var memberIndexes = candidates.Where(index => orderedRows.Any(row => ReferenceEquals(row, groupRows[index]))).ToList();
            foreach (var index in memberIndexes)
            {
                used[index] = true;
            }

            var members = memberIndexes.SelectMany(index => groups[index]).ToList();
            var type = orderedRows.Count > 1 ? BarcodeType.Gs1DataBarExpandedStacked : BarcodeType.Gs1DataBarExpanded;
            AddResult(results, type, text, members.Min(item => item.Line), members.Max(item => item.Line), members.Min(item => item.Start), members.Max(item => item.End));
        }
    }
}
