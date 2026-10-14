namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The loop filter process of AV1 (AV1 specification, section 7.14): the deblocking of the edges of the transform blocks,
/// applied in place to a reconstructed frame, on all vertical edges of a plane then on all its horizontal edges.
/// </summary>
internal ref struct Av1LoopFilter
{
    // The number of filter levels of a frame without deltas per block: one per level index, segment, reference frame and mode type
    private const int LevelCount = Av1Constants.FrameLfCount * Av1Constants.MaxSegments * Av1Constants.TotalRefsPerFrame * 2;

    private readonly Av1SequenceHeader _sequence;
    private readonly Av1FrameHeader _header;
    private readonly ReadOnlySpan<byte> _miSizes;
    private readonly ReadOnlySpan<byte> _yModes;
    private readonly ReadOnlySpan<byte> _flags;
    private readonly ReadOnlySpan<byte> _segmentIds;
    private readonly ReadOnlySpan<sbyte> _refFrames;
    private readonly ReadOnlySpan<sbyte> _deltaLfs;
    private readonly int _miCols;
    private readonly int _bitDepth;

    // The filter levels of the blocks when they do not depend on a delta of the block (DeltaLfPresent is 0), indexed by
    // GetLevelIndex; empty otherwise
    private readonly Span<byte> _levels;
    private Span<ushort> _samples;
    private int _stride;
    private ReadOnlySpan<byte> _txSizes;
    private int _lvl;
    private int _limit;
    private int _blimit;
    private int _thresh;

    private Av1LoopFilter(Av1FrameBuffer frame, Av1FrameState state, Span<byte> levels)
    {
        _sequence = frame.Sequence;
        _header = frame.Header;
        _miSizes = state.MiSizes;
        _yModes = state.YModes;
        _flags = state.Flags;
        _segmentIds = state.SegmentIds;
        _refFrames = _header.FrameIsIntra ? default : state.RefFrames;
        _deltaLfs = _header.DeltaLfPresent ? state.DeltaLfs : default;
        _miCols = _header.MiCols;
        _bitDepth = _sequence.BitDepth;
        if (!_header.DeltaLfPresent)
        {
            _levels = levels;
            for (var i = 0; i < Av1Constants.FrameLfCount; i++)
            {
                for (var segment = 0; segment < Av1Constants.MaxSegments; segment++)
                {
                    for (var reference = 0; reference < Av1Constants.TotalRefsPerFrame; reference++)
                    {
                        for (var modeType = 0; modeType < 2; modeType++)
                        {
                            _levels[GetLevelIndex(i, segment, reference, modeType)] = (byte)ComputeLevel(i, segment, reference, modeType, deltaLf: 0);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Filters the planes of <paramref name="frame"/> (section 7.14.1).</summary>
    public static void Apply(Av1FrameBuffer frame, Av1FrameState state, CancellationToken cancellationToken)
    {
        Span<byte> levels = stackalloc byte[LevelCount];
        var filter = new Av1LoopFilter(frame, state, levels);
        var header = frame.Header;
        var sequence = frame.Sequence;
        for (var plane = 0; plane < sequence.PlaneCount; plane++)
        {
            if (plane is not 0 && header.LoopFilterLevel[1 + plane] is 0)
                continue;

            var planeBuffer = frame.GetPlane(plane);
            filter._samples = planeBuffer.Samples;
            filter._stride = planeBuffer.Stride;
            filter._txSizes = state.GetLoopFilterTxSizes(plane);
            var rowStep = plane is 0 ? 1 : 1 << sequence.SubsamplingY;
            var colStep = plane is 0 ? 1 : 1 << sequence.SubsamplingX;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var row = 0; row < header.MiRows; row += rowStep)
                {
                    if ((row & 15) is 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    for (var col = 0; col < header.MiCols; col += colStep)
                    {
                        filter.FilterEdge(plane, pass, row, col);
                    }
                }
            }
        }
    }

    // Section 7.14.2 "Edge loop filter process"
    private void FilterEdge(int plane, int pass, int row, int col)
    {
        var subX = plane is 0 ? 0 : _sequence.SubsamplingX;
        var subY = plane is 0 ? 0 : _sequence.SubsamplingY;
        var dx = pass is 0 ? 1 : 0;
        var dy = 1 - dx;
        var x = col * Av1Constants.MiSize;
        var y = row * Av1Constants.MiSize;
        row |= subY;
        col |= subX;
        if (x >= _header.FrameWidth || y >= _header.FrameHeight || (pass is 0 && x is 0) || (pass is 1 && y is 0))
            return;

        var xP = x >> subX;
        var yP = y >> subY;
        var prevRow = row - (dy << subY);
        var prevCol = col - (dx << subX);
        var index = (row * _miCols) + col;
        var miSize = _miSizes[index];
        var txSizes = _txSizes;
        var txSz = txSizes[((row >> subY) * _miCols) + (col >> subX)];
        var planeSize = Av1Tables.SubsampledSize[(((miSize * 2) + subX) * 2) + subY];
        var skip = (_flags[index] & Av1FrameState.SkipFlag) is not 0;
        var isIntra = _refFrames.IsEmpty || _refFrames[index * 2] <= Av1Constants.IntraFrame;
        var prevTxSz = txSizes[((prevRow >> subY) * _miCols) + (prevCol >> subX)];

        bool isBlockEdge;
        bool isTxEdge;
        int baseSize;
        if (pass is 0)
        {
            // The block and transform sizes are powers of two, and xP is not negative
            isBlockEdge = (xP & ((4 * Av1Tables.Num4x4BlocksWide[planeSize]) - 1)) is 0;
            isTxEdge = (xP & (Av1Tables.TxWidth[txSz] - 1)) is 0;
            baseSize = Math.Min(Av1Tables.TxWidth[prevTxSz], Av1Tables.TxWidth[txSz]);
        }
        else
        {
            isBlockEdge = (yP & ((4 * Av1Tables.Num4x4BlocksHigh[planeSize]) - 1)) is 0;
            isTxEdge = (yP & (Av1Tables.TxHeight[txSz] - 1)) is 0;
            baseSize = Math.Min(Av1Tables.TxHeight[prevTxSz], Av1Tables.TxHeight[txSz]);
        }

        var applyFilter = isTxEdge && (isBlockEdge || !skip || isIntra);
        if (!applyFilter)
            return;

        // Section 7.14.3 "Filter size process"
        var filterSize = Math.Min(plane is 0 ? 16 : 8, baseSize);
        ComputeFilterStrength(row, col, plane, pass);
        if (_lvl is 0)
            ComputeFilterStrength(prevRow, prevCol, plane, pass);

        if (_lvl is 0)
            return;

        for (var i = 0; i < Av1Constants.MiSize; i++)
        {
            FilterSamples(xP + (dy * i), yP + (dx * i), plane, dx, dy, filterSize);
        }
    }

    // Sections 7.14.4 "Adaptive filter strength process" and 7.14.5 "Adaptive filter strength selection process"
    private void ComputeFilterStrength(int row, int col, int plane, int pass)
    {
        var index = (row * _miCols) + col;
        int segment = _segmentIds[index];
        var reference = _refFrames.IsEmpty ? Av1Constants.IntraFrame : _refFrames[index * 2];
        int mode = _yModes[index];
        var modeType = mode >= Av1Constants.Nearestmv && mode is not Av1Constants.Globalmv and not Av1Constants.GlobalGlobalmv ? 1 : 0;
        var i = plane is 0 ? pass : plane + 1;
        int lvlSeg;
        if (!_levels.IsEmpty && (uint)segment < Av1Constants.MaxSegments && (uint)reference < Av1Constants.TotalRefsPerFrame)
        {
            lvlSeg = _levels[GetLevelIndex(i, segment, reference, modeType)];
        }
        else
        {
            var deltaLf = 0;
            if (!_deltaLfs.IsEmpty)
                deltaLf = _deltaLfs[(index * Av1Constants.FrameLfCount) + (_header.DeltaLfMulti ? i : 0)];

            lvlSeg = ComputeLevel(i, segment, reference, modeType, deltaLf);
        }

        _lvl = lvlSeg;
        var sharpness = _header.LoopFilterSharpness;
        var shift = sharpness > 4 ? 2 : sharpness > 0 ? 1 : 0;
        _limit = sharpness > 0 ? Math.Clamp(lvlSeg >> shift, 1, 9 - sharpness) : Math.Max(1, lvlSeg >> shift);
        _blimit = (2 * (lvlSeg + 2)) + _limit;
        _thresh = lvlSeg >> 4;
    }

    // The filter level of section 7.14.4 (lvlSeg) of a block, from the index i of the level (the pass for the luma, 1 + the
    // plane for the chroma), its segment, its first reference frame and its mode type
    private readonly int ComputeLevel(int i, int segment, int reference, int modeType, int deltaLf)
    {
        var lvlSeg = Math.Clamp(deltaLf + _header.LoopFilterLevel[i], 0, Av1Constants.MaxLoopFilter);
        var feature = Av1Constants.SegLvlAltLfYV + i;
        if (_header.IsSegmentationFeatureActive(segment, feature))
            lvlSeg = Math.Clamp(_header.Segmentation.GetData(segment, feature) + lvlSeg, 0, Av1Constants.MaxLoopFilter);

        if (_header.LoopFilterDeltaEnabled)
        {
            var nShift = lvlSeg >> 5;
            if (reference is Av1Constants.IntraFrame)
            {
                lvlSeg += _header.LoopFilterDeltas.RefDeltas[Av1Constants.IntraFrame] << nShift;
            }
            else
            {
                lvlSeg += (_header.LoopFilterDeltas.RefDeltas[reference] << nShift) + (_header.LoopFilterDeltas.ModeDeltas[modeType] << nShift);
            }

            lvlSeg = Math.Clamp(lvlSeg, 0, Av1Constants.MaxLoopFilter);
        }

        return lvlSeg;
    }

    private static int GetLevelIndex(int i, int segment, int reference, int modeType)
        => (((((i * Av1Constants.MaxSegments) + segment) * Av1Constants.TotalRefsPerFrame) + reference) * 2) + modeType;

    // Section 7.14.6 "Sample filtering process"
    private readonly void FilterSamples(int x, int y, int plane, int dx, int dy, int filterSize)
    {
        var samples = _samples;
        var step = (dy * _stride) + dx;
        var position = (y * _stride) + x;
        int q0 = samples[position];
        int q1 = samples[position + step];
        int p0 = samples[position - step];
        int p1 = samples[position - (2 * step)];

        // Section 7.14.6.2 "Filter mask process"
        var shift = _bitDepth - 8;
        var hevMask = Math.Abs(p1 - p0) > (_thresh << shift) || Math.Abs(q1 - q0) > (_thresh << shift);
        int filterLen;
        if (filterSize is 4)
        {
            filterLen = 4;
        }
        else if (plane is not 0)
        {
            filterLen = 6;
        }
        else
        {
            filterLen = filterSize is 8 ? 8 : 16;
        }

        var limitBd = _limit << shift;
        var blimitBd = _blimit << shift;
        var masked = Math.Abs(p1 - p0) > limitBd || Math.Abs(q1 - q0) > limitBd || (Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) / 2) > blimitBd;
        int q2 = 0;
        int p2 = 0;
        int q3 = 0;
        int p3 = 0;
        if (filterLen >= 6)
        {
            q2 = samples[position + (2 * step)];
            p2 = samples[position - (3 * step)];
            masked |= Math.Abs(p2 - p1) > limitBd || Math.Abs(q2 - q1) > limitBd;
        }

        if (filterLen >= 8)
        {
            q3 = samples[position + (3 * step)];
            p3 = samples[position - (4 * step)];
            masked |= Math.Abs(p3 - p2) > limitBd || Math.Abs(q3 - q2) > limitBd;
        }

        if (masked)
            return;

        var thresholdBd = 1 << shift;
        var flatMask = false;
        if (filterSize >= 8)
        {
            flatMask = Math.Abs(p1 - p0) <= thresholdBd && Math.Abs(q1 - q0) <= thresholdBd && Math.Abs(p2 - p0) <= thresholdBd && Math.Abs(q2 - q0) <= thresholdBd;
            if (filterLen >= 8)
                flatMask &= Math.Abs(p3 - p0) <= thresholdBd && Math.Abs(q3 - q0) <= thresholdBd;
        }

        var flatMask2 = false;
        if (filterSize >= 16)
        {
            flatMask2 = true;
            for (var k = 4; k <= 6; k++)
            {
                int q = samples[position + (k * step)];
                int p = samples[position - ((k + 1) * step)];
                flatMask2 &= Math.Abs(p - p0) <= thresholdBd && Math.Abs(q - q0) <= thresholdBd;
            }
        }

        if (filterSize is 4 || !flatMask)
        {
            NarrowFilter(samples, position, step, hevMask);
        }
        else if (filterSize is 8 || !flatMask2)
        {
            WideFilter(samples, position, step, plane, log2Size: 3);
        }
        else
        {
            WideFilter(samples, position, step, plane, log2Size: 4);
        }
    }

    // Section 7.14.6.3 "Narrow filter process"
    private readonly void NarrowFilter(Span<ushort> samples, int position, int step, bool hevMask)
    {
        var offset = 0x80 << (_bitDepth - 8);
        var min = -(1 << (_bitDepth - 1));
        var max = (1 << (_bitDepth - 1)) - 1;
        var ps1 = samples[position - (2 * step)] - offset;
        var ps0 = samples[position - step] - offset;
        var qs0 = samples[position] - offset;
        var qs1 = samples[position + step] - offset;
        var filter = hevMask ? Math.Clamp(ps1 - qs1, min, max) : 0;
        filter = Math.Clamp(filter + (3 * (qs0 - ps0)), min, max);
        var filter1 = Math.Clamp(filter + 4, min, max) >> 3;
        var filter2 = Math.Clamp(filter + 3, min, max) >> 3;
        samples[position] = (ushort)(Math.Clamp(qs0 - filter1, min, max) + offset);
        samples[position - step] = (ushort)(Math.Clamp(ps0 + filter2, min, max) + offset);
        if (!hevMask)
        {
            filter = (filter1 + 1) >> 1;
            samples[position + step] = (ushort)(Math.Clamp(qs1 - filter, min, max) + offset);
            samples[position - (2 * step)] = (ushort)(Math.Clamp(ps1 + filter, min, max) + offset);
        }
    }

    // Section 7.14.6.4 "Wide filter process"
    private static void WideFilter(Span<ushort> samples, int position, int step, int plane, int log2Size)
    {
        int n;
        if (log2Size is 4)
        {
            n = 6;
        }
        else
        {
            n = plane is 0 ? 3 : 2;
        }

        var n2 = log2Size is 3 && plane is 0 ? 0 : 1;

        // The taps of output i are the samples i + j for j from -n to n, with a weight of 2 for |j| <= n2 and 1 otherwise,
        // their positions clamped to -(n + 1) to n: the sum of a window of 2n + 1 samples plus a window of 2n2 + 1 samples,
        // both sliding by one sample from one output to the next. The samples are read before any of them is written.
        Span<int> values = stackalloc int[14];
        values = values[..((2 * n) + 2)];
        for (var k = 0; k < values.Length; k++)
        {
            values[k] = samples[position + ((k - n - 1) * step)];
        }

        var wide = 0;
        for (var j = -n; j <= n; j++)
        {
            wide += Tap(values, -n + j, n);
        }

        var narrow = 0;
        for (var j = -n2; j <= n2; j++)
        {
            narrow += Tap(values, -n + j, n);
        }

        var round = 1 << (log2Size - 1);
        for (var i = -n; i < n; i++)
        {
            samples[position + (i * step)] = (ushort)((wide + narrow + round) >> log2Size);
            wide += Tap(values, i + n + 1, n) - Tap(values, i - n, n);
            narrow += Tap(values, i + n2 + 1, n) - Tap(values, i - n2, n);
        }

        static int Tap(ReadOnlySpan<int> values, int p, int n) => values[Math.Clamp(p, -(n + 1), n) + n + 1];
    }
}
