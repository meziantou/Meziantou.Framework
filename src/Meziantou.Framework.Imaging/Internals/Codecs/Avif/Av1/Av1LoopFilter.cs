using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

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
    /// <param name="frame">The reconstructed frame.</param>
    /// <param name="state">The block-level state of the frame.</param>
    /// <param name="workers">
    /// The number of workers: the rows of blocks are split into <see cref="Av1PostFilter.GetBandRow"/> bands, filtered
    /// concurrently, all the vertical edges of a plane before its horizontal edges.
    /// </param>
    /// <param name="cancellationToken">The cancellation token of the operation.</param>
    /// <remarks>
    /// An edge reads at most half the size of the transform blocks on each side of it (7 samples of a filter of size 16, 4
    /// of size 8 and 2 of size 4), so the edges of different rows of blocks of a pass never touch the same sample: the
    /// samples do not depend on the order of the rows, nor on the number of workers.
    /// </remarks>
    public static void Apply(Av1FrameBuffer frame, Av1FrameState state, int workers, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        for (var plane = 0; plane < frame.Sequence.PlaneCount; plane++)
        {
            if (plane is not 0 && header.LoopFilterLevel[1 + plane] is 0)
                continue;

            for (var pass = 0; pass < 2; pass++)
            {
                if (workers is 1)
                {
                    FilterRows(frame, state, plane, pass, 0, header.MiRows, cancellationToken);
                    continue;
                }

                RowBands.RunParallel(workers, band => FilterRows(frame, state, plane, pass, Av1PostFilter.GetBandRow(header.MiRows, band, workers), Av1PostFilter.GetBandRow(header.MiRows, band + 1, workers), cancellationToken));
            }
        }
    }

    // The edges of a pass of a plane on the rows of blocks from rowStart to rowEnd, two multiples of 4. The edges are filtered
    // by pairs of consecutive edges along their lines: the vertical edges of two rows of blocks, or the horizontal edges of
    // two columns. The lines of the edges of a pair are distinct and the edges that cross a line are still filtered in order,
    // so the samples are those of the order of section 7.14.1
    private static void FilterRows(Av1FrameBuffer frame, Av1FrameState state, int plane, int pass, int rowStart, int rowEnd, CancellationToken cancellationToken)
    {
        Span<byte> levels = stackalloc byte[LevelCount];
        var filter = new Av1LoopFilter(frame, state, levels);
        var sequence = frame.Sequence;
        var planeBuffer = frame.GetPlane(plane);
        filter._samples = planeBuffer.Samples;
        filter._stride = planeBuffer.Stride;
        filter._txSizes = state.GetLoopFilterTxSizes(plane);
        var rowStep = plane is 0 ? 1 : 1 << sequence.SubsamplingY;
        var colStep = plane is 0 ? 1 : 1 << sequence.SubsamplingX;
        var miCols = frame.Header.MiCols;
        for (var row = rowStart; row < rowEnd; row += pass is 0 ? 2 * rowStep : rowStep)
        {
            if ((row & 15) is 0)
                cancellationToken.ThrowIfCancellationRequested();

            if (pass is 0)
            {
                for (var col = 0; col < miCols; col += colStep)
                {
                    filter.FilterEdgePair(plane, pass: 0, row, col, row + rowStep, col);
                }
            }
            else
            {
                for (var col = 0; col < miCols; col += 2 * colStep)
                {
                    filter.FilterEdgePair(plane, pass: 1, row, col, row, col + colStep);
                }
            }
        }
    }

    // Filters the edge (row, col) and the next one along its lines (row2, col2), outside the frame for the last edge of an odd
    // number: two edges of the same size are filtered together, 8 lines per vector
    private void FilterEdgePair(int plane, int pass, int row, int col, int row2, int col2)
    {
        var hasFirst = TryGetEdge(plane, pass, row, col, out var position, out var filterSize);
        var (limit, blimit, thresh) = (_limit, _blimit, _thresh);
        var position2 = 0;
        var filterSize2 = 0;
        var hasSecond = row2 < _header.MiRows && col2 < _miCols && TryGetEdge(plane, pass, row2, col2, out position2, out filterSize2);
        var step = pass is 0 ? 1 : _stride;
        var lineStep = pass is 0 ? _stride : 1;
        if (hasFirst && hasSecond && filterSize == filterSize2)
        {
            FilterLinePairs(_samples, position, step, lineStep, plane, filterSize, _bitDepth, limit, blimit, thresh, _limit, _blimit, _thresh);
            return;
        }

        if (hasFirst)
            FilterLines(_samples, position, step, lineStep, plane, filterSize, _bitDepth, limit, blimit, thresh);

        if (hasSecond)
            FilterLines(_samples, position2, step, lineStep, plane, filterSize2, _bitDepth, _limit, _blimit, _thresh);
    }

    // Section 7.14.2 "Edge loop filter process": whether the edge is filtered, the position of its first line, its filter
    // size, and its levels in _limit, _blimit and _thresh
    private bool TryGetEdge(int plane, int pass, int row, int col, out int position, out int filterSize)
    {
        position = 0;
        filterSize = 0;
        var subX = plane is 0 ? 0 : _sequence.SubsamplingX;
        var subY = plane is 0 ? 0 : _sequence.SubsamplingY;
        var dx = pass is 0 ? 1 : 0;
        var dy = 1 - dx;
        var x = col * Av1Constants.MiSize;
        var y = row * Av1Constants.MiSize;
        row |= subY;
        col |= subX;
        if (x >= _header.FrameWidth || y >= _header.FrameHeight || (pass is 0 && x is 0) || (pass is 1 && y is 0))
            return false;

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
            return false;

        // Section 7.14.3 "Filter size process"
        filterSize = Math.Min(plane is 0 ? 16 : 8, baseSize);
        ComputeFilterStrength(row, col, plane, pass);
        if (_lvl is 0)
            ComputeFilterStrength(prevRow, prevCol, plane, pass);

        // The 4 lines of the edge start at the position: rows for a vertical edge, columns for a horizontal one
        position = (yP * _stride) + xP;
        return _lvl is not 0;
    }

    /// <summary>
    /// Filters the 4 lines of an edge (section 7.14.6 for each line): the line i crosses the edge at
    /// <paramref name="position"/> + i * <paramref name="lineStep"/>, and its samples are <paramref name="step"/> apart.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, the 4 lines are the lanes of the vectors: each line only reads and writes its own
    /// samples, and every lane computes the masks and the filters of its line with the integer arithmetic of
    /// <see cref="FilterSamples"/>, then takes the result that its masks select.
    /// </remarks>
    internal static void FilterLines(Span<ushort> samples, int position, int step, int lineStep, int plane, int filterSize, int bitDepth, int limit, int blimit, int thresh)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            FilterLinesVector(samples, position, step, lineStep, plane, filterSize, bitDepth, limit, blimit, thresh);
        }
        else
        {
            FilterLinesScalar(samples, position, step, lineStep, plane, filterSize, bitDepth, limit, blimit, thresh);
        }
    }

    /// <summary>The scalar reference of <see cref="FilterLines"/>.</summary>
    internal static void FilterLinesScalar(Span<ushort> samples, int position, int step, int lineStep, int plane, int filterSize, int bitDepth, int limit, int blimit, int thresh)
    {
        for (var i = 0; i < Av1Constants.MiSize; i++)
        {
            FilterSamples(samples, position + (i * lineStep), step, plane, filterSize, bitDepth, limit, blimit, thresh);
        }
    }

    /// <summary>
    /// Filters the 8 lines of two consecutive edges of the same filter size: the lines of the edge at
    /// <paramref name="position"/>, with the levels <paramref name="limit0"/>, <paramref name="blimit0"/> and
    /// <paramref name="thresh0"/>, then the lines of the next edge, 4 lines further, with the levels <paramref name="limit1"/>,
    /// <paramref name="blimit1"/> and <paramref name="thresh1"/>.
    /// </summary>
    /// <remarks>
    /// With the <see cref="Av1Simd"/> operations, the 8 lines are the 16-bit lanes of the vectors, the lanes of each edge
    /// with its levels: the differences of samples have 13 bits with their sign, the narrow filter stays within 15 bits
    /// (3 * 4095 + 2048), and the sums of the wide filters, at most 16 * 4095 + 8, are computed modulo 2^16 as unsigned
    /// values, which they fit.
    /// </remarks>
    internal static void FilterLinePairs(Span<ushort> samples, int position, int step, int lineStep, int plane, int filterSize, int bitDepth, int limit0, int blimit0, int thresh0, int limit1, int blimit1, int thresh1)
    {
        if (Av1Simd.IsSupported && (step is 1 || lineStep is 1))
        {
            FilterLinePairsVector(samples, position, step, lineStep, plane, filterSize, bitDepth, limit0, blimit0, thresh0, limit1, blimit1, thresh1);
        }
        else
        {
            FilterLinesScalar(samples, position, step, lineStep, plane, filterSize, bitDepth, limit0, blimit0, thresh0);
            FilterLinesScalar(samples, position + (Av1Constants.MiSize * lineStep), step, lineStep, plane, filterSize, bitDepth, limit1, blimit1, thresh1);
        }
    }

    // FilterLinesVector for the 8 lines of two edges in 16-bit lanes
    [SkipLocalsInit]
    private static void FilterLinePairsVector(Span<ushort> samples, int position, int step, int lineStep, int plane, int filterSize, int bitDepth, int limit0, int blimit0, int thresh0, int limit1, int blimit1, int thresh1)
    {
        var shift = bitDepth - 8;
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

        // The samples of the lines at the offsets -8 to 7 from the edge, index 8 + offset
        var reach = filterSize >= 16 ? 7 : filterLen >= 8 ? 4 : filterLen >= 6 ? 3 : 2;
        Span<Vector128<short>> v = unsafe(stackalloc Vector128<short>[16]);
        int loaded;
        if (step is 1)
        {
            // Vertical edges: 8 or 16 consecutive samples of 8 rows, transposed into the lanes
            loaded = reach > 4 ? 8 : 4;
            for (var start = -loaded; start < loaded; start += 8)
            {
                Av1Simd.LoadTransposed8(samples, position + start, lineStep, v.Slice(8 + start, 8));
            }
        }
        else
        {
            // Horizontal edges: the 8 lines are 8 consecutive samples of a row
            loaded = reach;
            for (var k = -reach; k < reach; k++)
            {
                v[8 + k] = Vector128.Create(samples.Slice(position + (k * step), 8)).AsInt16();
            }
        }

        var p0 = v[7];
        var p1 = v[6];
        var q0 = v[8];
        var q1 = v[9];
        var limitBd = Halves(limit0 << shift, limit1 << shift);
        var threshBd = Halves(thresh0 << shift, thresh1 << shift);
        var hevMask = Vector128.GreaterThan(Vector128.Abs(p1 - p0), threshBd) | Vector128.GreaterThan(Vector128.Abs(q1 - q0), threshBd);
        var masked = Vector128.GreaterThan(Vector128.Abs(p1 - p0), limitBd)
            | Vector128.GreaterThan(Vector128.Abs(q1 - q0), limitBd)
            | Vector128.GreaterThan((Vector128.Abs(p0 - q0) * 2) + (Vector128.Abs(p1 - q1) >> 1), Halves(blimit0 << shift, blimit1 << shift));
        if (filterLen >= 6)
            masked |= Vector128.GreaterThan(Vector128.Abs(v[5] - p1), limitBd) | Vector128.GreaterThan(Vector128.Abs(v[10] - q1), limitBd);

        if (filterLen >= 8)
            masked |= Vector128.GreaterThan(Vector128.Abs(v[4] - v[5]), limitBd) | Vector128.GreaterThan(Vector128.Abs(v[11] - v[10]), limitBd);

        if (masked == Vector128<short>.AllBitsSet)
            return;

        var thresholdBd = Vector128.Create((short)(1 << shift));
        var flatMask = Vector128<short>.Zero;
        if (filterSize >= 8)
        {
            flatMask = Vector128.LessThanOrEqual(Vector128.Abs(p1 - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(q1 - q0), thresholdBd)
                & Vector128.LessThanOrEqual(Vector128.Abs(v[5] - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(v[10] - q0), thresholdBd);
            if (filterLen >= 8)
                flatMask &= Vector128.LessThanOrEqual(Vector128.Abs(v[4] - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(v[11] - q0), thresholdBd);
        }

        var flatMask2 = Vector128<short>.Zero;
        if (filterSize >= 16)
        {
            flatMask2 = Vector128<short>.AllBitsSet;
            for (var k = 4; k <= 6; k++)
            {
                flatMask2 &= Vector128.LessThanOrEqual(Vector128.Abs(v[7 - k] - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(v[8 + k] - q0), thresholdBd);
            }
        }

        // The lines that each filter applies to
        var unmasked = ~masked;
        var useNarrow = filterSize is 4 ? unmasked : unmasked & ~flatMask;
        var useWide8 = filterSize is 4 ? Vector128<short>.Zero : filterSize is 8 ? unmasked & flatMask : unmasked & flatMask & ~flatMask2;
        var useWide16 = filterSize >= 16 ? unmasked & flatMask & flatMask2 : Vector128<short>.Zero;
        Span<Vector128<short>> output = unsafe(stackalloc Vector128<short>[16]);
        v.Slice(8 - loaded, 2 * loaded).CopyTo(output[(8 - loaded)..]);
        if (useNarrow != Vector128<short>.Zero)
            NarrowFilterPairs(v, output, hevMask, useNarrow, bitDepth);

        if (useWide8 != Vector128<short>.Zero)
            WideFilterPairs(v, output, useWide8, plane is 0 ? 3 : 2, plane is 0 ? 0 : 1, log2Size: 3);

        if (useWide16 != Vector128<short>.Zero)
            WideFilterPairs(v, output, useWide16, 6, 1, log2Size: 4);

        if (step is 1)
        {
            // The rows are written back whole: the samples that no filter changed are written with their value
            for (var start = -loaded; start < loaded; start += 8)
            {
                Av1Simd.StoreTransposed8(samples, position + start, lineStep, output.Slice(8 + start, 8));
            }

            return;
        }

        // The filters write the offsets -6 to 5 at most
        var written = Math.Min(reach, 6);
        for (var k = -written; k < written; k++)
        {
            output[8 + k].AsUInt16().CopyTo(samples.Slice(position + (k * step), 8));
        }

        static Vector128<short> Halves(int first, int second) => Vector128.Create(Vector64.Create((short)first), Vector64.Create((short)second));
    }

    // NarrowFilterVector in 16-bit lanes
    private static void NarrowFilterPairs(ReadOnlySpan<Vector128<short>> v, Span<Vector128<short>> output, Vector128<short> hevMask, Vector128<short> selected, int bitDepth)
    {
        var offset = Vector128.Create((short)(0x80 << (bitDepth - 8)));
        var min = Vector128.Create((short)-(1 << (bitDepth - 1)));
        var max = Vector128.Create((short)((1 << (bitDepth - 1)) - 1));
        var ps1 = v[6] - offset;
        var ps0 = v[7] - offset;
        var qs0 = v[8] - offset;
        var qs1 = v[9] - offset;
        var filter = Vector128.ConditionalSelect(hevMask, Clamp(ps1 - qs1, min, max), Vector128<short>.Zero);
        filter = Clamp(filter + ((qs0 - ps0) * 3), min, max);
        var filter1 = Clamp(filter + Vector128.Create((short)4), min, max) >> 3;
        var filter2 = Clamp(filter + Vector128.Create((short)3), min, max) >> 3;
        output[8] = Vector128.ConditionalSelect(selected, Clamp(qs0 - filter1, min, max) + offset, output[8]);
        output[7] = Vector128.ConditionalSelect(selected, Clamp(ps0 + filter2, min, max) + offset, output[7]);
        var outer = selected & ~hevMask;
        filter = (filter1 + Vector128<short>.One) >> 1;
        output[9] = Vector128.ConditionalSelect(outer, Clamp(qs1 - filter, min, max) + offset, output[9]);
        output[6] = Vector128.ConditionalSelect(outer, Clamp(ps1 + filter, min, max) + offset, output[6]);

        static Vector128<short> Clamp(Vector128<short> value, Vector128<short> min, Vector128<short> max) => Vector128.Min(Vector128.Max(value, min), max);
    }

    // WideFilterVector in 16-bit lanes: the sums are unsigned and wrap, their final values fit 16 bits
    private static void WideFilterPairs(ReadOnlySpan<Vector128<short>> v, Span<Vector128<short>> output, Vector128<short> selected, int n, int n2, int log2Size)
    {
        var wide = Vector128<ushort>.Zero;
        for (var j = -n; j <= n; j++)
        {
            wide += Tap(v, -n + j, n);
        }

        var narrow = Vector128<ushort>.Zero;
        for (var j = -n2; j <= n2; j++)
        {
            narrow += Tap(v, -n + j, n);
        }

        var round = Vector128.Create((ushort)(1 << (log2Size - 1)));
        for (var i = -n; i < n; i++)
        {
            output[8 + i] = Vector128.ConditionalSelect(selected, ((wide + narrow + round) >> log2Size).AsInt16(), output[8 + i]);
            wide += Tap(v, i + n + 1, n) - Tap(v, i - n, n);
            narrow += Tap(v, i + n2 + 1, n) - Tap(v, i - n2, n);
        }

        static Vector128<ushort> Tap(ReadOnlySpan<Vector128<short>> v, int p, int n) => v[8 + Math.Clamp(p, -(n + 1), n)].AsUInt16();
    }

    // The entries of the vectors of the samples are written before they are read: the masks and the filters read the
    // offsets -reach to reach - 1, which are loaded, and the outputs are copied from them
    [SkipLocalsInit]
    private static void FilterLinesVector(Span<ushort> samples, int position, int step, int lineStep, int plane, int filterSize, int bitDepth, int limit, int blimit, int thresh)
    {
        var shift = bitDepth - 8;
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

        // The samples of the lines at the offsets -8 to 7 from the edge, index 8 + offset: the ones that the masks and the
        // filter of the edge read
        var reach = filterSize >= 16 ? 7 : filterLen >= 8 ? 4 : filterLen >= 6 ? 3 : 2;
        Span<Vector128<int>> v = unsafe(stackalloc Vector128<int>[16]);
        var transposed = step is 1 && Av1Simd.IsSupported;
        if (transposed)
        {
            // A vertical edge: the samples of a line are consecutive samples of a row, 8 on each side of the edge for the
            // widest filter and 4 otherwise, transposed into the lanes (the edge has at least as many samples on each side)
            var half = reach > 4 ? 8 : 4;
            for (var start = -half; start < half; start += 8)
            {
                var first = position + start;
                Av1Simd.LoadTransposed(samples, first, lineStep, v.Slice(8 + start, 8));
            }
        }
        else
        {
            for (var k = -reach; k < reach; k++)
            {
                v[8 + k] = Load(samples, position + (k * step), lineStep);
            }
        }

        var p0 = v[7];
        var p1 = v[6];
        var q0 = v[8];
        var q1 = v[9];
        var limitBd = Vector128.Create(limit << shift);
        var hevMask = Vector128.GreaterThan(Vector128.Abs(p1 - p0), Vector128.Create(thresh << shift)) | Vector128.GreaterThan(Vector128.Abs(q1 - q0), Vector128.Create(thresh << shift));
        var masked = Vector128.GreaterThan(Vector128.Abs(p1 - p0), limitBd)
            | Vector128.GreaterThan(Vector128.Abs(q1 - q0), limitBd)
            | Vector128.GreaterThan((Vector128.Abs(p0 - q0) * 2) + (Vector128.Abs(p1 - q1) >> 1), Vector128.Create(blimit << shift));
        if (filterLen >= 6)
            masked |= Vector128.GreaterThan(Vector128.Abs(v[5] - p1), limitBd) | Vector128.GreaterThan(Vector128.Abs(v[10] - q1), limitBd);

        if (filterLen >= 8)
            masked |= Vector128.GreaterThan(Vector128.Abs(v[4] - v[5]), limitBd) | Vector128.GreaterThan(Vector128.Abs(v[11] - v[10]), limitBd);

        if (masked == Vector128<int>.AllBitsSet)
            return;

        var thresholdBd = Vector128.Create(1 << shift);
        var flatMask = Vector128<int>.Zero;
        if (filterSize >= 8)
        {
            flatMask = Vector128.LessThanOrEqual(Vector128.Abs(p1 - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(q1 - q0), thresholdBd)
                & Vector128.LessThanOrEqual(Vector128.Abs(v[5] - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(v[10] - q0), thresholdBd);
            if (filterLen >= 8)
                flatMask &= Vector128.LessThanOrEqual(Vector128.Abs(v[4] - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(v[11] - q0), thresholdBd);
        }

        var flatMask2 = Vector128<int>.Zero;
        if (filterSize >= 16)
        {
            flatMask2 = Vector128<int>.AllBitsSet;
            for (var k = 4; k <= 6; k++)
            {
                flatMask2 &= Vector128.LessThanOrEqual(Vector128.Abs(v[7 - k] - p0), thresholdBd) & Vector128.LessThanOrEqual(Vector128.Abs(v[8 + k] - q0), thresholdBd);
            }
        }

        // The lines that each filter applies to
        var unmasked = ~masked;
        var useNarrow = filterSize is 4 ? unmasked : unmasked & ~flatMask;
        var useWide8 = filterSize is 4 ? Vector128<int>.Zero : filterSize is 8 ? unmasked & flatMask : unmasked & flatMask & ~flatMask2;
        var useWide16 = filterSize >= 16 ? unmasked & flatMask & flatMask2 : Vector128<int>.Zero;
        Span<Vector128<int>> output = unsafe(stackalloc Vector128<int>[16]);
        var loaded = transposed ? (reach > 4 ? 8 : 4) : reach;
        v.Slice(8 - loaded, 2 * loaded).CopyTo(output[(8 - loaded)..]);
        if (useNarrow != Vector128<int>.Zero)
            NarrowFilterVector(v, output, hevMask, useNarrow, bitDepth);

        if (useWide8 != Vector128<int>.Zero)
            WideFilterVector(v, output, useWide8, plane is 0 ? 3 : 2, plane is 0 ? 0 : 1, log2Size: 3);

        if (useWide16 != Vector128<int>.Zero)
            WideFilterVector(v, output, useWide16, 6, 1, log2Size: 4);

        // The filters write the offsets -6 to 5 at most
        if (transposed)
        {
            // The rows are written back whole: the samples that no filter changed are written with their value
            for (var start = -loaded; start < loaded; start += 8)
            {
                Av1Simd.StoreTransposed(samples, position + start, lineStep, output.Slice(8 + start, 8));
            }

            return;
        }

        var written = Math.Min(reach, 6);
        for (var k = -written; k < written; k++)
        {
            Store(samples, position + (k * step), lineStep, output[8 + k]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Load(Span<ushort> samples, int position, int lineStep)
        {
            if (lineStep is 1)
                return Vector128.WidenLower(Vector64.Create(samples.Slice(position, 4)).ToVector128()).AsInt32();

            return Vector128.Create(samples[position], samples[position + lineStep], samples[position + (2 * lineStep)], samples[position + (3 * lineStep)]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Store(Span<ushort> samples, int position, int lineStep, Vector128<int> values)
        {
            if (lineStep is 1)
            {
                Vector128.Narrow(values.AsUInt32(), values.AsUInt32()).GetLower().CopyTo(samples.Slice(position, 4));
                return;
            }

            samples[position] = (ushort)values.GetElement(0);
            samples[position + lineStep] = (ushort)values.GetElement(1);
            samples[position + (2 * lineStep)] = (ushort)values.GetElement(2);
            samples[position + (3 * lineStep)] = (ushort)values.GetElement(3);
        }
    }

    // NarrowFilter on the lanes of selected: the samples p1 to q1 of the output
    private static void NarrowFilterVector(ReadOnlySpan<Vector128<int>> v, Span<Vector128<int>> output, Vector128<int> hevMask, Vector128<int> selected, int bitDepth)
    {
        var offset = Vector128.Create(0x80 << (bitDepth - 8));
        var min = Vector128.Create(-(1 << (bitDepth - 1)));
        var max = Vector128.Create((1 << (bitDepth - 1)) - 1);
        var ps1 = v[6] - offset;
        var ps0 = v[7] - offset;
        var qs0 = v[8] - offset;
        var qs1 = v[9] - offset;
        var filter = Vector128.ConditionalSelect(hevMask, Clamp(ps1 - qs1, min, max), Vector128<int>.Zero);
        filter = Clamp(filter + ((qs0 - ps0) * 3), min, max);
        var filter1 = Clamp(filter + Vector128.Create(4), min, max) >> 3;
        var filter2 = Clamp(filter + Vector128.Create(3), min, max) >> 3;
        output[8] = Vector128.ConditionalSelect(selected, Clamp(qs0 - filter1, min, max) + offset, output[8]);
        output[7] = Vector128.ConditionalSelect(selected, Clamp(ps0 + filter2, min, max) + offset, output[7]);
        var outer = selected & ~hevMask;
        filter = (filter1 + Vector128<int>.One) >> 1;
        output[9] = Vector128.ConditionalSelect(outer, Clamp(qs1 - filter, min, max) + offset, output[9]);
        output[6] = Vector128.ConditionalSelect(outer, Clamp(ps1 + filter, min, max) + offset, output[6]);

        static Vector128<int> Clamp(Vector128<int> value, Vector128<int> min, Vector128<int> max) => Vector128.Min(Vector128.Max(value, min), max);
    }

    // WideFilter on the lanes of selected: the outputs at the offsets -n to n - 1, with the sliding sums of the scalar code
    private static void WideFilterVector(ReadOnlySpan<Vector128<int>> v, Span<Vector128<int>> output, Vector128<int> selected, int n, int n2, int log2Size)
    {
        var wide = Vector128<int>.Zero;
        for (var j = -n; j <= n; j++)
        {
            wide += Tap(v, -n + j, n);
        }

        var narrow = Vector128<int>.Zero;
        for (var j = -n2; j <= n2; j++)
        {
            narrow += Tap(v, -n + j, n);
        }

        var round = Vector128.Create(1 << (log2Size - 1));
        for (var i = -n; i < n; i++)
        {
            output[8 + i] = Vector128.ConditionalSelect(selected, (wide + narrow + round) >> log2Size, output[8 + i]);
            wide += Tap(v, i + n + 1, n) - Tap(v, i - n, n);
            narrow += Tap(v, i + n2 + 1, n) - Tap(v, i - n2, n);
        }

        static Vector128<int> Tap(ReadOnlySpan<Vector128<int>> v, int p, int n) => v[8 + Math.Clamp(p, -(n + 1), n)];
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
    private static void FilterSamples(Span<ushort> samples, int position, int step, int plane, int filterSize, int bitDepth, int limit, int blimit, int thresh)
    {
        int q0 = samples[position];
        int q1 = samples[position + step];
        int p0 = samples[position - step];
        int p1 = samples[position - (2 * step)];

        // Section 7.14.6.2 "Filter mask process"
        var shift = bitDepth - 8;
        var hevMask = Math.Abs(p1 - p0) > (thresh << shift) || Math.Abs(q1 - q0) > (thresh << shift);
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

        var limitBd = limit << shift;
        var blimitBd = blimit << shift;
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
            NarrowFilter(samples, position, step, hevMask, bitDepth);
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
    private static void NarrowFilter(Span<ushort> samples, int position, int step, bool hevMask, int bitDepth)
    {
        var offset = 0x80 << (bitDepth - 8);
        var min = -(1 << (bitDepth - 1));
        var max = (1 << (bitDepth - 1)) - 1;
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
