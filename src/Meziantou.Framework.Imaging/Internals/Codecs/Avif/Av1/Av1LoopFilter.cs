namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The loop filter process of AV1 (AV1 specification, section 7.14): the deblocking of the edges of the transform blocks,
/// applied in place to a reconstructed frame, on all vertical edges of a plane then on all its horizontal edges.
/// </summary>
internal ref struct Av1LoopFilter
{
    private readonly Av1SequenceHeader _sequence;
    private readonly Av1FrameHeader _header;
    private readonly Av1FrameState _state;
    private readonly ReadOnlySpan<byte> _miSizes;
    private readonly ReadOnlySpan<byte> _yModes;
    private readonly ReadOnlySpan<byte> _flags;
    private readonly ReadOnlySpan<byte> _segmentIds;
    private readonly ReadOnlySpan<sbyte> _refFrames;
    private readonly ReadOnlySpan<sbyte> _deltaLfs;
    private readonly int _miCols;
    private readonly int _bitDepth;
    private Span<ushort> _samples;
    private int _stride;
    private int _lvl;
    private int _limit;
    private int _blimit;
    private int _thresh;

    private Av1LoopFilter(Av1FrameBuffer frame, Av1FrameState state)
    {
        _sequence = frame.Sequence;
        _header = frame.Header;
        _state = state;
        _miSizes = state.MiSizes;
        _yModes = state.YModes;
        _flags = state.Flags;
        _segmentIds = state.SegmentIds;
        _refFrames = _header.FrameIsIntra ? default : state.RefFrames;
        _deltaLfs = _header.DeltaLfPresent ? state.DeltaLfs : default;
        _miCols = _header.MiCols;
        _bitDepth = _sequence.BitDepth;
    }

    /// <summary>Filters the planes of <paramref name="frame"/> (section 7.14.1).</summary>
    public static void Apply(Av1FrameBuffer frame, Av1FrameState state, CancellationToken cancellationToken)
    {
        var filter = new Av1LoopFilter(frame, state);
        var header = frame.Header;
        var sequence = frame.Sequence;
        for (var plane = 0; plane < sequence.PlaneCount; plane++)
        {
            if (plane is not 0 && header.LoopFilterLevel[1 + plane] is 0)
                continue;

            var planeBuffer = frame.GetPlane(plane);
            filter._samples = planeBuffer.Samples;
            filter._stride = planeBuffer.Stride;
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
        var txSizes = _state.GetLoopFilterTxSizes(plane);
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
            isBlockEdge = xP % (4 * Av1Tables.Num4x4BlocksWide[planeSize]) is 0;
            isTxEdge = xP % Av1Tables.TxWidth[txSz] is 0;
            baseSize = Math.Min(Av1Tables.TxWidth[prevTxSz], Av1Tables.TxWidth[txSz]);
        }
        else
        {
            isBlockEdge = yP % (4 * Av1Tables.Num4x4BlocksHigh[planeSize]) is 0;
            isTxEdge = yP % Av1Tables.TxHeight[txSz] is 0;
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
        var deltaLf = 0;
        if (!_deltaLfs.IsEmpty)
            deltaLf = _deltaLfs[(index * Av1Constants.FrameLfCount) + (_header.DeltaLfMulti ? i : 0)];

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

        _lvl = lvlSeg;
        var sharpness = _header.LoopFilterSharpness;
        var shift = sharpness > 4 ? 2 : sharpness > 0 ? 1 : 0;
        _limit = sharpness > 0 ? Math.Clamp(lvlSeg >> shift, 1, 9 - sharpness) : Math.Max(1, lvlSeg >> shift);
        _blimit = (2 * (lvlSeg + 2)) + _limit;
        _thresh = lvlSeg >> 4;
    }

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
        Span<int> filtered = stackalloc int[12];
        for (var i = -n; i < n; i++)
        {
            var t = 0;
            for (var j = -n; j <= n; j++)
            {
                var p = Math.Clamp(i + j, -(n + 1), n);
                var tap = Math.Abs(j) <= n2 ? 2 : 1;
                t += samples[position + (p * step)] * tap;
            }

            filtered[i + n] = (t + (1 << (log2Size - 1))) >> log2Size;
        }

        for (var i = -n; i < n; i++)
        {
            samples[position + (i * step)] = (ushort)filtered[i + n];
        }
    }
}
