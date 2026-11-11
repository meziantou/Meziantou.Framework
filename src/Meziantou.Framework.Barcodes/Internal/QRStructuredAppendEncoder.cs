namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Splits data across a structured append sequence of standard QR codes (ISO/IEC 18004, 8.3).
/// </summary>
/// <remarks>
/// The sequence has the fewest symbols that the largest allowed version gives, then the smallest version that gives as
/// few symbols: every symbol of the sequence has the same version. Each symbol is filled with as much data as it holds,
/// in order, so the last one can hold less.
/// </remarks>
internal static class QRStructuredAppendEncoder
{
    public const int MaxSymbolCount = 16;

    /// <summary>Splits a text across the symbols of a sequence.</summary>
    public static QRStructuredAppendPlan Plan(string data, ErrorCorrectionLevel ecLevel, int minVersion, int maxVersion, int? eciAssignmentNumber)
    {
        // As in a single symbol: with an ECI designator, the encoding of the bytes is explicit, and Kanji mode is not used
        var allowSegments = eciAssignmentNumber is not null || DataAnalyzer.CanSegmentWithoutEci(data);
        var allowKanji = eciAssignmentNumber is null;
        var eci = eciAssignmentNumber is { } number ? QRCodeSegment.CreateEci(number) : null;
        var headerBitCount = DataEncoder.StructuredAppendBitCount + (eciAssignmentNumber is { } value ? DataEncoder.GetEciBitCount(value) : 0);

        QRCodeSegment[]? Build(int start, int end, int version)
        {
            var part = data[start..end];
            var segments = QRSegmentOptimizer.ChooseSegments(part, QRCodeType.Standard, version, allowSegments, allowKanji, out var bitCount);
            if (segments is null || bitCount < 0 || headerBitCount + bitCount > QRCodeVersion.GetDataCodewords(version, ecLevel) * 8)
                return null;

            var result = QRSegmentOptimizer.ToSegments(part, segments);
            return eci is null ? result : [eci, .. result];
        }

        // A symbol never ends between the two halves of a surrogate pair: each symbol holds a valid text
        bool IsBoundary(int index) => index == data.Length || !char.IsLowSurrogate(data[index]) || !char.IsHighSurrogate(data[index - 1]);

        return Plan(data.Length, Build, IsBoundary, minVersion, maxVersion);
    }

    /// <summary>Splits segments across the symbols of a sequence.</summary>
    public static QRStructuredAppendPlan Plan(IReadOnlyList<QRCodeSegment> segments, ErrorCorrectionLevel ecLevel, int minVersion, int maxVersion)
    {
        // The data units (characters or bytes) are numbered across the segments; an ECI designator is at the position of
        // the unit that follows it
        var offsets = new int[segments.Count];
        var unitCount = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            offsets[i] = unitCount;
            unitCount += segments[i].UnitCount;
        }

        QRCodeSegment[]? Build(int start, int end, int version)
        {
            var parts = new List<QRCodeSegment>();

            // The ECI designator in force at the start of the symbol is repeated, so that the symbol reads alone
            QRCodeSegment? eci = null;
            for (var i = 0; i < segments.Count && offsets[i] <= start; i++)
            {
                if (segments[i].EncodingMode is EncodingMode.Eci)
                {
                    eci = segments[i];
                }
            }

            if (eci is not null)
            {
                parts.Add(eci);
            }

            for (var i = 0; i < segments.Count && offsets[i] < end; i++)
            {
                var segment = segments[i];
                if (segment.EncodingMode is EncodingMode.Eci)
                {
                    if (offsets[i] > start)
                    {
                        parts.Add(segment);
                    }

                    continue;
                }

                var segmentStart = Math.Max(start, offsets[i]);
                var segmentEnd = Math.Min(end, offsets[i] + segment.UnitCount);
                if (segmentStart < segmentEnd)
                {
                    parts.Add(segment.Slice(segmentStart - offsets[i], segmentEnd - segmentStart));
                }
            }

            var bitCount = QRSymbolFormat.GetBitCount(parts, QRCodeType.Standard, version);
            if (bitCount < 0 || DataEncoder.StructuredAppendBitCount + bitCount > QRCodeVersion.GetDataCodewords(version, ecLevel) * 8)
                return null;

            return [.. parts];
        }

        return Plan(unitCount, Build, static _ => true, minVersion, maxVersion);
    }

    public static QRCode[] Encode(QRStructuredAppendPlan plan, ErrorCorrectionLevel ecLevel)
    {
        var result = new QRCode[plan.Parts.Length];
        for (var i = 0; i < plan.Parts.Length; i++)
        {
            var structuredAppend = new QRCodeStructuredAppend(i, plan.Parts.Length, plan.Parity);
            var dataCodewords = DataEncoder.Encode(plan.Parts[i], plan.Version, ecLevel, structuredAppend);
            var allCodewords = ErrorCorrectionEncoder.AddErrorCorrection(dataCodewords, plan.Version, ecLevel);
            var modules = MaskEvaluator.BuildMatrix(allCodewords, plan.Version, ecLevel);
            result[i] = new QRCode(modules, plan.Version, QRCodeType.Standard, ecLevel, structuredAppend);
        }

        return result;
    }

    private static QRStructuredAppendPlan Plan(int unitCount, Func<int, int, int, QRCodeSegment[]?> build, Func<int, bool> isBoundary, int minVersion, int maxVersion)
    {
        var parts = Split(unitCount, build, isBoundary, maxVersion) ?? throw CreateDataTooLongException(minVersion, maxVersion);

        // The smallest version that needs no more symbols than the largest one
        var version = maxVersion;
        var low = minVersion;
        var high = maxVersion - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = Split(unitCount, build, isBoundary, middle);
            if (candidate is not null && candidate.Count <= parts.Count)
            {
                parts = candidate;
                version = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        var parity = 0;
        foreach (var part in parts)
        {
            foreach (var segment in part)
            {
                parity ^= segment.GetParity();
            }
        }

        return new QRStructuredAppendPlan([.. parts], version, (byte)parity);
    }

    /// <summary>
    /// Splits the units into the fewest symbols of a version, filling each symbol in order, or returns <see langword="null"/>
    /// when they need more than 16 symbols.
    /// </summary>
    /// <remarks>
    /// A symbol that holds a run of units holds any shorter run with the same start, so the end of each symbol is found by
    /// a binary search, and filling the symbols in order gives the fewest symbols.
    /// </remarks>
    private static List<QRCodeSegment[]>? Split(int unitCount, Func<int, int, int, QRCodeSegment[]?> build, Func<int, bool> isBoundary, int version)
    {
        var result = new List<QRCodeSegment[]>();
        var position = 0;
        while (position < unitCount)
        {
            if (result.Count == MaxSymbolCount)
                return null;

            var low = position + 1;
            var high = unitCount;
            QRCodeSegment[]? segments = null;
            var segmentsEnd = -1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var end = isBoundary(middle) ? middle : middle - 1;
                if (end <= position)
                {
                    low = middle + 1;
                    continue;
                }

                var candidate = build(position, end, version);
                if (candidate is not null)
                {
                    segments = candidate;
                    segmentsEnd = end;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            // Not even one character fits in the symbol
            if (segments is null)
                return null;

            result.Add(segments);
            position = segmentsEnd;
        }

        return result;
    }

    private static InvalidOperationException CreateDataTooLongException(int minVersion, int maxVersion)
    {
        return maxVersion is 40
            ? new InvalidOperationException($"The data is too long to be split across {MaxSymbolCount} QR codes.")
            : new InvalidOperationException($"The data is too long to be split across {MaxSymbolCount} QR codes of version {(minVersion == maxVersion ? $"{maxVersion}" : $"{minVersion} to {maxVersion}")}.");
    }
}
