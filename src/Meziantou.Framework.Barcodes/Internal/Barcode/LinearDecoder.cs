namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// Finds the linear barcodes of scan lines: each line is read in both directions, a symbol must have its quiet zones, the
/// same symbol on several lines is one result, and the rows of the stacked GS1 DataBar symbols are put together.
/// </summary>
internal static class LinearDecoder
{
    // The quiet zone before and after a symbol, in modules (narrow elements for Code 39, Codabar and ITF). The symbologies
    // require 10 modules or more (GS1 DataBar only 1); less is accepted to read symbols drawn with a smaller quiet zone,
    // but the symbologies without check character need more, so that they are not read inside other symbols.
    private static double GetQuietZoneModules(BarcodeType type) => type switch
    {
        BarcodeType.Code39 or BarcodeType.Codabar or BarcodeType.Itf => 7,
        BarcodeType.Gs1DataBar or BarcodeType.Gs1DataBarLimited => 1,
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

        for (var start = 1; start < line.Count; start += 2)
        {
            var matched = false;
            var compensated = GetCompensatedLine(line, start);
            foreach (var decoder in Decoders)
            {
                var match = decoder(line, start) ?? (compensated is null ? null : decoder(compensated, start));
                if (match is null || !HasQuietZones(match.Start, match.End, match.ModuleWidth, GetQuietZoneModules(match.Type)))
                    continue;

                var (from, to) = GetRange(match.Start, match.End);
                AddResult(results, match.Type, match.Text, lineIndex, lineIndex, from, to);

                // Continue after the symbol: the next dark run
                start = match.End - (ScanLine.IsDark(match.End) ? 2 : 1);
                matched = true;
                break;
            }

            if (matched)
                continue;

            // A row whose bars grew can read as valid characters with other values: both readings are candidates, and the
            // checksum of the whole symbol chooses
            foreach (var candidate in (ReadOnlySpan<ScanLine?>)[line, compensated])
            {
                if (candidate is null)
                    continue;

                if (DataBarDecoder.TryReadHalf(candidate, start) is { } half && HasQuietZones(half.Start, half.End, half.ModuleWidth, 1))
                {
                    var (from, to) = GetRange(half.Start, half.End);
                    halves.Add((half, lineIndex, from, to));
                }

                if (DataBarExpandedDecoder.TryReadRow(candidate, start) is { } row && HasQuietZones(row.Start, row.End, row.ModuleWidth, 1))
                {
                    var (from, to) = GetRange(row.Start, row.End);
                    expandedRows.Add((row, lineIndex, from, to));
                }
            }
        }
    }

    // The bars of a printed symbol spread, and a threshold that is not in the middle of the gray levels moves all the edges
    // the same way: the narrowest bar and the narrowest space of the start of a symbol are as wide, the difference is twice
    // the growth of each edge. Returns the line with the growth removed, or null when there is none to remove.
    private static ScanLine? GetCompensatedLine(ScanLine line, int start)
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

        return line.Compensate((narrowestBar - narrowestSpace) / 4);
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

    // GS1 DataBar Stacked and Stacked Omnidirectional: a top row above a bottom row, whose checksum matches. The rows of
    // Stacked Omnidirectional are as high as each other, the top row of Stacked is shorter.
    private static void AssembleStackedOmnidirectional(List<(DataBarDecoder.Half Half, int Line, long Start, long End)> halves, List<BarcodeDecodeResult> results)
    {
        foreach (var top in halves.Where(item => item.Half.IsTop).GroupBy(item => string.Join(",", item.Half.Values)))
        {
            var topFirst = top.Min(item => item.Line);
            var topLast = top.Max(item => item.Line);
            var topStart = top.Min(item => item.Start);
            var topEnd = top.Max(item => item.End);
            foreach (var bottom in halves.Where(item => !item.Half.IsTop && item.Line > topLast && item.Start < topEnd && topStart < item.End).GroupBy(item => string.Join(",", item.Half.Values)))
            {
                var text = DataBarDecoder.TryCombine(top.First().Half, bottom.First().Half);
                if (text is null)
                    continue;

                var bottomFirst = bottom.Min(item => item.Line);
                var bottomLast = bottom.Max(item => item.Line);
                var topHeight = topLast - topFirst + 1;
                var bottomHeight = bottomLast - bottomFirst + 1;
                var type = topHeight * 6 < bottomHeight * 5 ? BarcodeType.Gs1DataBarStacked : BarcodeType.Gs1DataBarStackedOmnidirectional;
                AddResult(results, type, text, topFirst, bottomLast, Math.Min(topStart, bottom.Min(item => item.Start)), Math.Max(topEnd, bottom.Max(item => item.End)));
                break;
            }
        }
    }

    // GS1 DataBar Expanded and Expanded Stacked: the rows that make a symbol, in the order of their finder patterns
    private static void AssembleExpanded(List<(DataBarExpandedDecoder.Row Row, int Line, long Start, long End)> rows, List<BarcodeDecodeResult> results)
    {
        if (rows.Count is 0)
            return;

        var distinct = rows.GroupBy(item => item.Row.Signature).ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in distinct)
        {
            if (used.Contains(group.Key))
                continue;

            // The rows of a symbol overlap horizontally and are within a few lines of each other
            var candidates = distinct.Where(other => !used.Contains(other.Key) && other.Any(item => group.Any(first => item.Start < first.End && first.Start < item.End))).Select(other => other.First().Row).ToList();
            var assembled = DataBarExpandedDecoder.TryAssemble(candidates);
            if (assembled is null)
                continue;

            var (orderedRows, text) = assembled.Value;
            var members = distinct.Where(other => orderedRows.Any(row => row.Signature == other.Key)).SelectMany(other => other).ToList();
            foreach (var row in orderedRows)
            {
                used.Add(row.Signature);
            }

            var type = orderedRows.Count > 1 ? BarcodeType.Gs1DataBarExpandedStacked : BarcodeType.Gs1DataBarExpanded;
            AddResult(results, type, text, members.Min(item => item.Line), members.Max(item => item.Line), members.Min(item => item.Start), members.Max(item => item.End));
        }
    }
}
