namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Splits a text into segments of different encoding modes, with the modes and the indicator sizes of a standard QR, Micro
/// QR or rMQR version, and chooses the smallest standard QR version that holds them.
/// </summary>
/// <remarks>
/// The size of the character count indicators of standard QR codes changes at versions 10 and 27, so the best
/// segmentation is computed once per range of versions. Whenever the whole text in a single mode takes no more bits than the best segmentation found,
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
                segments = ChooseSegments(data, QRCodeType.Standard, version, allowSegments, allowKanji, out bitCount);
            }

            if (bitCount >= 0 && headerBitCount + bitCount <= QRCodeVersion.GetDataCodewords(version, ecLevel) * 8)
            {
                return (segments!, version);
            }
        }

        throw DataAnalyzer.CreateDataTooLongException(minVersion, maxVersion);
    }

    /// <summary>Gets the segments of the text, ready to encode.</summary>
    public static QRCodeSegment[] ToSegments(string data, QRSegment[] segments)
    {
        var result = new QRCodeSegment[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            result[i] = QRCodeSegment.FromValidatedText(segments[i].Mode, data.Substring(segments[i].Start, segments[i].Length));
        }

        return result;
    }

    /// <summary>
    /// Gets the number of bits of the segments with their mode and character count indicators, or -1 when the symbol
    /// does not support a mode or a count does not fit in its indicator.
    /// </summary>
    public static int GetBitCount(string data, QRSegment[] segments, QRCodeType type, int version)
    {
        if (type is QRCodeType.MicroQR && version is 1 && segments.Length is not 1)
            return -1;

        var bitCount = 0;
        foreach (var segment in segments)
        {
            var countBits = QRSymbolFormat.GetCharacterCountBits(type, version, segment.Mode);
            if (countBits < 0 || segment.GetCharacterCount(data) >= (1 << countBits))
                return -1;

            bitCount += QRSymbolFormat.GetModeIndicatorBits(type, version) + countBits + segment.GetDataBitCount(data);
        }

        return bitCount;
    }

    /// <summary>
    /// Chooses the segments of a text in a symbol of the given type and version.
    /// </summary>
    /// <param name="data">The text.</param>
    /// <param name="type">The type of symbol.</param>
    /// <param name="version">The version of the symbol, which sets the sizes of the character count indicators.</param>
    /// <param name="allowSegments"><see langword="false"/> to encode the whole text in a single mode.</param>
    /// <param name="allowKanji"><see langword="false"/> to never use Kanji mode.</param>
    /// <param name="bitCount">The number of bits of the segments, or -1 when a count does not fit in its indicator.</param>
    /// <returns>The segments, or <see langword="null"/> when the version cannot encode a character of the text.</returns>
    public static QRSegment[]? ChooseSegments(string data, QRCodeType type, int version, bool allowSegments, bool allowKanji, out int bitCount)
    {
        var singleMode = DataAnalyzer.DetermineMode(data, allowKanji);
        QRSegment[] single = [new QRSegment(singleMode, 0, data.Length)];
        var singleBitCount = QRSymbolFormat.GetCharacterCountBits(type, version, singleMode) < 0 ? -2 : GetBitCount(data, single, type, version);
        if (!allowSegments)
        {
            bitCount = singleBitCount;
            return singleBitCount is -2 ? null : single;
        }

        var optimized = ComputeSegments(data, type, version, allowKanji);
        if (optimized is null)
        {
            bitCount = -1;
            return null;
        }

        // The single mode, unless the symbol does not support it, whenever it takes no more bits than the segments
        var optimizedBitCount = GetBitCount(data, optimized, type, version);
        if (singleBitCount is not -2 && (optimized.Length is 1 || (singleBitCount >= 0 && (optimizedBitCount < 0 || singleBitCount <= optimizedBitCount))))
        {
            bitCount = singleBitCount;
            return single;
        }

        bitCount = optimizedBitCount;
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
    private static QRSegment[]? ComputeSegments(string data, QRCodeType type, int version, bool allowKanji)
    {
        // The cost of starting a segment of each mode, or Unreachable when the symbol does not support the mode
        Span<int> headerCosts = stackalloc int[ModeCount];
        for (var mode = 0; mode < ModeCount; mode++)
        {
            var countBits = QRSymbolFormat.GetCharacterCountBits(type, version, Modes[mode]);
            headerCosts[mode] = countBits < 0 || (mode is 3 && !allowKanji) ? Unreachable : (QRSymbolFormat.GetModeIndicatorBits(type, version) + countBits) * 6;
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
            if (previousCosts[0] < Unreachable && c is >= '0' and <= '9')
            {
                costs[0] = previousCosts[0] + 20;
                modes[0] = 0;
            }

            if (previousCosts[1] < Unreachable && DataAnalyzer.IsAlphanumeric(c))
            {
                costs[1] = previousCosts[1] + 33;
                modes[1] = 1;
            }

            if (previousCosts[2] < Unreachable)
            {
                costs[2] = previousCosts[2] + (Encoding.UTF8.GetByteCount(data.AsSpan(index, length)) * 48);
                modes[2] = 2;
            }

            if (previousCosts[3] < Unreachable && length is 1 && DataAnalyzer.IsKanji(c))
            {
                costs[3] = previousCosts[3] + 78;
                modes[3] = 3;
            }

            // No mode of the symbol encodes the character
            if (modes.IndexOfAnyExcept((sbyte)-1) < 0)
                return null;

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
