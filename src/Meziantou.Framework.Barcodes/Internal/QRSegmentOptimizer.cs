namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Splits a text into segments of different encoding modes, and chooses the smallest standard QR version that holds them.
/// </summary>
/// <remarks>
/// The size of the character count indicators changes at versions 10 and 27, so the best segmentation is computed once
/// per range of versions. Whenever the whole text in a single mode takes no more bits than the best segmentation found,
/// or when segments are not allowed, the single mode is used, so the symbols of homogeneous texts do not change.
/// </remarks>
internal static class QRSegmentOptimizer
{
    private const int ModeCount = 4;
    private const int Unreachable = int.MaxValue / 2;

    private static ReadOnlySpan<EncodingMode> Modes => [EncodingMode.Numeric, EncodingMode.Alphanumeric, EncodingMode.Byte, EncodingMode.Kanji];

    public static (QRSegment[] Segments, int Version) Optimize(string data, ErrorCorrectionLevel ecLevel, int minVersion, int maxVersion, int headerBitCount, bool allowSegments, bool allowKanji)
    {
        QRSegment[]? segments = null;
        var bitCount = -1;
        var lastGroupVersion = 0;
        for (var version = minVersion; version <= maxVersion; version++)
        {
            var groupVersion = version switch
            {
                <= 9 => 9,
                <= 26 => 26,
                _ => 40,
            };

            if (groupVersion != lastGroupVersion)
            {
                lastGroupVersion = groupVersion;
                segments = ChooseSegments(data, version, allowSegments, allowKanji);
                bitCount = GetBitCount(data, segments, version);
            }

            if (bitCount >= 0 && headerBitCount + bitCount <= QRCodeVersion.GetDataCodewords(version, ecLevel) * 8)
            {
                return (segments!, version);
            }
        }

        throw DataAnalyzer.CreateDataTooLongException(minVersion, maxVersion);
    }

    /// <summary>
    /// Gets the number of bits of the segments with their mode and character count indicators, or -1 when a count does
    /// not fit in its indicator.
    /// </summary>
    public static int GetBitCount(string data, QRSegment[] segments, int version)
    {
        var bitCount = 0;
        foreach (var segment in segments)
        {
            var countBits = QRCodeVersion.GetCharacterCountBits(version, segment.Mode);
            if (segment.GetCharacterCount(data) >= (1 << countBits))
                return -1;

            bitCount += 4 + countBits + segment.GetDataBitCount(data);
        }

        return bitCount;
    }

    private static QRSegment[] ChooseSegments(string data, int version, bool allowSegments, bool allowKanji)
    {
        var single = new[] { new QRSegment(DataAnalyzer.DetermineMode(data, allowKanji), 0, data.Length) };
        if (!allowSegments)
            return single;

        var optimized = ComputeSegments(data, version, allowKanji);
        if (optimized.Length is 1)
            return single;

        var singleBitCount = GetBitCount(data, single, version);
        var optimizedBitCount = GetBitCount(data, optimized, version);
        if (singleBitCount >= 0 && (optimizedBitCount < 0 || singleBitCount <= optimizedBitCount))
            return single;

        return optimized;
    }

    /// <summary>
    /// Finds the segmentation with the fewest bits by dynamic programming over the code points.
    /// </summary>
    /// <remarks>
    /// The costs are in sixths of a bit, so that a digit (10 bits per 3) and an alphanumeric character (11 bits per 2)
    /// have an integral cost. The state after a code point is the mode the next code point continues in; switching to
    /// another mode pays its mode and character count indicators, after rounding the cost of the current segment up to a
    /// whole bit. This is the algorithm of Project Nayuki's QR Code generator.
    /// </remarks>
    private static QRSegment[] ComputeSegments(string data, int version, bool allowKanji)
    {
        Span<int> headerCosts = stackalloc int[ModeCount];
        for (var mode = 0; mode < ModeCount; mode++)
        {
            headerCosts[mode] = (4 + QRCodeVersion.GetCharacterCountBits(version, Modes[mode])) * 6;
        }

        if (!allowKanji)
        {
            headerCosts[3] = Unreachable;
        }

        // charModes[unit * ModeCount + state] is the mode of the code point when the state after it is 'state', or -1
        var unitStarts = new List<int>(data.Length);
        var charModes = new List<sbyte>(data.Length * ModeCount);
        Span<int> previousCosts = stackalloc int[ModeCount];
        Span<int> costs = stackalloc int[ModeCount];
        Span<int> stayCosts = stackalloc int[ModeCount];
        Span<sbyte> modes = stackalloc sbyte[ModeCount];
        headerCosts.CopyTo(previousCosts);

        var index = 0;
        while (index < data.Length)
        {
            var c = data[index];
            var length = char.IsHighSurrogate(c) && index + 1 < data.Length && char.IsLowSurrogate(data[index + 1]) ? 2 : 1;

            costs.Fill(Unreachable);
            modes.Fill(-1);
            if (c is >= '0' and <= '9')
            {
                costs[0] = previousCosts[0] + 20;
                modes[0] = 0;
            }

            if (DataAnalyzer.IsAlphanumeric(c))
            {
                costs[1] = previousCosts[1] + 33;
                modes[1] = 1;
            }

            costs[2] = previousCosts[2] + (Encoding.UTF8.GetByteCount(data.AsSpan(index, length)) * 48);
            modes[2] = 2;

            if (allowKanji && length is 1 && DataAnalyzer.IsKanji(c))
            {
                costs[3] = previousCosts[3] + 78;
                modes[3] = 3;
            }

            // Start a new segment after this code point to switch to another mode
            costs.CopyTo(stayCosts);
            for (var to = 0; to < ModeCount; to++)
            {
                if (headerCosts[to] >= Unreachable)
                    continue;

                for (var from = 0; from < ModeCount; from++)
                {
                    if (from == to || modes[from] < 0 || stayCosts[from] >= Unreachable)
                        continue;

                    var cost = ((stayCosts[from] + 5) / 6 * 6) + headerCosts[to];
                    if (cost < costs[to])
                    {
                        costs[to] = cost;
                        modes[to] = (sbyte)from;
                    }
                }
            }

            unitStarts.Add(index);
            foreach (var mode in modes)
            {
                charModes.Add(mode);
            }

            costs.CopyTo(previousCosts);
            index += length;
        }

        var state = 0;
        for (var mode = 1; mode < ModeCount; mode++)
        {
            if (previousCosts[mode] < previousCosts[state])
            {
                state = mode;
            }
        }

        // Walk back from the last code point, and merge the code points of the same mode
        var segments = new List<QRSegment>();
        var end = data.Length;
        var segmentMode = -1;
        for (var unit = unitStarts.Count - 1; unit >= 0; unit--)
        {
            var mode = charModes[(unit * ModeCount) + state];
            if (mode != segmentMode)
            {
                if (segmentMode >= 0)
                {
                    segments.Add(new QRSegment(Modes[segmentMode], unitStarts[unit + 1], end - unitStarts[unit + 1]));
                    end = unitStarts[unit + 1];
                }

                segmentMode = mode;
            }

            state = mode;
        }

        segments.Add(new QRSegment(Modes[segmentMode], 0, end));
        segments.Reverse();
        return [.. segments];
    }
}
