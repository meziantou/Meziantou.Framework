using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constrained directional enhancement filter of AV1 (AV1 specification, section 7.15), applied in place to a deblocked
/// frame. The filter of an 8x8 block reads the samples of the frame before the filter up to two rows and columns around the
/// block: the frame is filtered by rows of 8x8 blocks, from a copy of the rows of the band and of the two rows above it.
/// </summary>
internal static class Av1Cdef
{
    /// <summary>The number of rows of the unfiltered copy of a band: 8 rows, and 2 rows above and below.</summary>
    public const int SourceRows = 12;

    // The rows of the copy of the edge of a band of each plane: 2 rows above the edge and 2 rows below it
    private const int BandEdgeRows = 4;

    // The taps of a sample: 4 primary taps and 8 secondary taps
    private const int TapCount = 12;

    // The value of the samples outside the frame in the window of a block on an edge of the frame
    private const ushort Unavailable = ushort.MaxValue;

    // The number of samples of a row of the window of a block on an edge: 8 samples, and 2 on each side
    private const int EdgeWindowStride = 8 + 4;

    /// <summary>Filters the planes of <paramref name="frame"/>.</summary>
    /// <param name="frame">The deblocked frame.</param>
    /// <param name="state">The block-level state of the frame.</param>
    /// <param name="source">Scratch for the unfiltered rows: <see cref="SourceRows"/> rows of the stride of the luma plane, for each plane.</param>
    /// <param name="cancellationToken">The cancellation token of the operation.</param>
    /// <param name="directions">
    /// Empty, or the directions and variances of the 8x8 blocks of a frame that is filtered several times from the same
    /// deblocked samples (an encoder that searches the strengths): <see cref="GetDirectionCacheLength"/> entries, -1 for a
    /// block whose direction is not known yet, which receives it.
    /// </param>
    public static void Apply(Av1FrameBuffer frame, Av1FrameState state, Span<ushort> source, CancellationToken cancellationToken, Span<int> directions = default)
        => ApplyRows(frame, state, source, default, default, 0, frame.Header.MiRows, cancellationToken, directions);

    /// <summary>Returns the number of samples of the copies of <see cref="SaveBandEdge"/>: 2 rows above and 2 rows below the edge of a band, for each plane.</summary>
    public static int GetBandEdgeLength(Av1FrameBuffer frame) => frame.Sequence.PlaneCount * BandEdgeRows * frame.GetPlane(0).Stride;

    /// <summary>
    /// Copies the unfiltered rows around the edge of two bands of <see cref="ApplyRows"/> at the row of blocks
    /// <paramref name="row"/>, before either band is filtered: the 2 rows of each plane above the edge, then the 2 rows below
    /// it.
    /// </summary>
    public static void SaveBandEdge(Av1FrameBuffer frame, int row, Span<ushort> edge)
    {
        var lumaStride = frame.GetPlane(0).Stride;
        for (var plane = 0; plane < frame.Sequence.PlaneCount; plane++)
        {
            var buffer = frame.GetPlane(plane);
            var stride = buffer.Stride;
            var y = (row * Av1Constants.MiSize) >> (plane > 0 ? frame.Sequence.SubsamplingY : 0);
            var planeEdge = edge.Slice(plane * BandEdgeRows * lumaStride, BandEdgeRows * lumaStride);
            buffer.Samples.Slice((y - 2) * stride, Math.Min(BandEdgeRows, buffer.AllocatedHeight - y + 2) * stride).CopyTo(planeEdge);
        }
    }

    /// <summary>
    /// Filters the rows of blocks from <paramref name="rowStart"/> to <paramref name="rowEnd"/> (two even rows) of the planes
    /// of <paramref name="frame"/>, while other bands may be filtered concurrently: the unfiltered rows of the neighbouring
    /// bands come from the copies of <see cref="SaveBandEdge"/>.
    /// </summary>
    /// <param name="frame">The deblocked frame.</param>
    /// <param name="state">The block-level state of the frame.</param>
    /// <param name="source">Scratch for the unfiltered rows: <see cref="SourceRows"/> rows of the stride of the luma plane, for each plane.</param>
    /// <param name="above">The copy of <see cref="SaveBandEdge"/> at <paramref name="rowStart"/>; empty for the first band.</param>
    /// <param name="below">The copy of <see cref="SaveBandEdge"/> at <paramref name="rowEnd"/>; empty for the last band.</param>
    /// <param name="rowStart">The first row of blocks.</param>
    /// <param name="rowEnd">The row of blocks after the band.</param>
    /// <param name="cancellationToken">The cancellation token of the operation.</param>
    /// <param name="directions">The direction cache of <see cref="Apply"/>.</param>
    public static void ApplyRows(Av1FrameBuffer frame, Av1FrameState state, Span<ushort> source, ReadOnlySpan<ushort> above, ReadOnlySpan<ushort> below, int rowStart, int rowEnd, CancellationToken cancellationToken, Span<int> directions = default)
    {
        var header = frame.Header;
        var sequence = frame.Sequence;
        var planeCount = sequence.PlaneCount;
        var bitDepth = sequence.BitDepth;
        var coeffShift = bitDepth - 8;
        var lumaStride = frame.GetPlane(0).Stride;
        var sourceLength = SourceRows * lumaStride;
        var cdefIdx = state.CdefIdx;
        var flags = state.Flags;
        var miCols = header.MiCols;
        var miRows = header.MiRows;
        for (var r = rowStart; r < rowEnd; r += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The unfiltered rows of the band: rows 0 and 1 are the two rows above it, saved from the previous band, and the
            // two rows below it belong to the next band
            for (var plane = 0; plane < planeCount; plane++)
            {
                var subY = plane > 0 ? sequence.SubsamplingY : 0;
                var buffer = frame.GetPlane(plane);
                var stride = buffer.Stride;
                var h = 8 >> subY;
                var y0 = (r * Av1Constants.MiSize) >> subY;
                var planeSource = source.Slice(plane * sourceLength, sourceLength);
                if (r > rowStart)
                {
                    planeSource.Slice(h * stride, 2 * stride).CopyTo(planeSource);
                }
                else if (!above.IsEmpty)
                {
                    above.Slice(plane * BandEdgeRows * lumaStride, 2 * stride).CopyTo(planeSource);
                }

                var rows = Math.Min(h + 2, buffer.AllocatedHeight - y0);
                var bandRows = r + 2 >= rowEnd && !below.IsEmpty ? Math.Min(rows, h) : rows;
                buffer.Samples.Slice(y0 * stride, bandRows * stride).CopyTo(planeSource[(2 * stride)..]);
                if (bandRows < rows)
                    below.Slice((plane * BandEdgeRows * lumaStride) + (2 * stride), (rows - bandRows) * stride).CopyTo(planeSource[((2 + bandRows) * stride)..]);
            }

            for (var c = 0; c < miCols; c += 2)
            {
                var idx = cdefIdx[((r >> 4) * state.CdefCols) + (c >> 4)];
                if (idx is -1)
                    continue;

                var index = (r * miCols) + c;
                var skip = (flags[index] & flags[index + 1] & flags[index + miCols] & flags[index + miCols + 1] & Av1FrameState.SkipFlag) is not 0;
                if (skip)
                    continue;

                var luma = frame.GetPlane(0);
                int yDir;
                int variance;
                if (header.CdefYPriStrength[idx] is 0 && (planeCount is 1 || header.CdefUvPriStrength[idx] is 0))
                {
                    // Without primary strength, the direction is 0 and the variance is not used
                    yDir = 0;
                    variance = 0;
                }
                else if (directions.IsEmpty)
                {
                    FindDirection(source[..sourceLength], luma.Stride, c * Av1Constants.MiSize, bitDepth, out yDir, out variance);
                }
                else
                {
                    ref var cached = ref directions[((r >> 1) * ((miCols + 1) >> 1)) + (c >> 1)];
                    if (cached < 0)
                    {
                        FindDirection(source[..sourceLength], luma.Stride, c * Av1Constants.MiSize, bitDepth, out yDir, out variance);
                        cached = (variance << 3) | yDir;
                    }
                    else
                    {
                        yDir = cached & 7;
                        variance = cached >> 3;
                    }
                }

                var priStr = header.CdefYPriStrength[idx] << coeffShift;
                var secStr = header.CdefYSecStrength[idx] << coeffShift;
                var dir = priStr is 0 ? 0 : yDir;
                var varStr = (variance >> 6) is not 0 ? Math.Min(BitOperations.Log2((uint)(variance >> 6)), 12) : 0;
                priStr = variance is not 0 ? ((priStr * (4 + varStr)) + 8) >> 4 : 0;
                var damping = header.CdefDamping + coeffShift;
                FilterBlock(luma, source[..sourceLength], r, c, 0, 0, miRows, miCols, priStr, secStr, damping, dir, coeffShift);
                if (planeCount is 1)
                    continue;

                priStr = header.CdefUvPriStrength[idx] << coeffShift;
                secStr = header.CdefUvSecStrength[idx] << coeffShift;
                dir = priStr is 0 ? 0 : Av1Tables.CdefUvDir[(((sequence.SubsamplingX * 2) + sequence.SubsamplingY) * 8) + yDir];
                damping = header.CdefDamping + coeffShift - 1;
                for (var plane = 1; plane < 3; plane++)
                {
                    FilterBlock(frame.GetPlane(plane), source.Slice(plane * sourceLength, sourceLength), r, c, sequence.SubsamplingX, sequence.SubsamplingY, miRows, miCols, priStr, secStr, damping, dir, coeffShift);
                }
            }
        }
    }

    /// <summary>Returns the number of entries of the direction cache of <see cref="Apply"/> for a frame.</summary>
    public static int GetDirectionCacheLength(Av1FrameHeader header) => ((header.MiRows + 1) >> 1) * ((header.MiCols + 1) >> 1);

    /// <summary>
    /// Section 7.15.2 "CDEF direction process" for the 8x8 luma block at column <paramref name="x0"/> of row 2 of
    /// <paramref name="source"/>.
    /// </summary>
    internal static void FindDirection(ReadOnlySpan<ushort> source, int stride, int x0, int bitDepth, out int yDir, out int variance)
    {
        if (Av1Simd.IsSupported)
        {
            FindDirectionVector(source, stride, x0, bitDepth, out yDir, out variance);
        }
        else
        {
            FindDirectionScalar(source, stride, x0, bitDepth, out yDir, out variance);
        }
    }

    // FindDirectionScalar with the 8 samples of a row in the lanes of a vector: a partial sum of a direction adds the
    // samples whose index (a linear function of the row and of the column) is the same, so the samples of a row, shifted
    // by the part of the index that depends on the row, are added to 15 (or 11) lanes held by 2 vectors. The costs are the
    // same sums of products of the scalar code, in 32-bit lanes
    private static void FindDirectionVector(ReadOnlySpan<ushort> source, int stride, int x0, int bitDepth, out int yDir, out int variance)
    {
        var shift = bitDepth - 8;
        // The shifts are constants, with 8 minus them for the lane extractions (0 when the shift is 0)
        var state = default(DirectionSums);
        var row0 = Load(source, stride, x0, 0, shift);
        var row1 = Load(source, stride, x0, 1, shift);
        AddDirectionRow(ref state, row0, 0, 0);
        AddDirectionRow(ref state, row1, 1, 7);
        AddDirectionRowPair(ref state, row0 + row1, 3, 5, 0, 0);
        var row2 = Load(source, stride, x0, 2, shift);
        var row3 = Load(source, stride, x0, 3, shift);
        AddDirectionRow(ref state, row2, 2, 6);
        AddDirectionRow(ref state, row3, 3, 5);
        AddDirectionRowPair(ref state, row2 + row3, 2, 6, 1, 7);
        var row4 = Load(source, stride, x0, 4, shift);
        var row5 = Load(source, stride, x0, 5, shift);
        AddDirectionRow(ref state, row4, 4, 4);
        AddDirectionRow(ref state, row5, 5, 3);
        AddDirectionRowPair(ref state, row4 + row5, 1, 7, 2, 6);
        var row6 = Load(source, stride, x0, 6, shift);
        var row7 = Load(source, stride, x0, 7, shift);
        AddDirectionRow(ref state, row6, 6, 2);
        AddDirectionRow(ref state, row7, 7, 1);
        AddDirectionRowPair(ref state, row6 + row7, 0, 0, 3, 5);

        // The weights of the squares of the partial sums: Div_Table[ count ] for the sums of count samples
        var weights15Low = Vector128.Create((short)840, 420, 280, 210, 168, 140, 120, 105);
        var weights15High = Vector128.Create((short)120, 140, 168, 210, 280, 420, 840, 0);
        var weights11Low = Vector128.Create((short)420, 210, 140, 105, 105, 105, 105, 105);
        var weights11High = Vector128.Create((short)140, 210, 420, 0, 0, 0, 0, 0);
        var weights8 = Vector128.Create((short)105);
        Span<int> cost = stackalloc int[8];
        cost[0] = WeightedSquares(state.Diagonal0Low, weights15Low) + WeightedSquares(state.Diagonal0High, weights15High);
        cost[1] = WeightedSquares(state.Partial1Low, weights11Low) + WeightedSquares(state.Partial1High, weights11High);
        cost[2] = WeightedSquares(state.Rows, weights8);
        cost[3] = WeightedSquares(state.Partial3Low, weights11Low) + WeightedSquares(state.Partial3High, weights11High);
        cost[4] = WeightedSquares(state.Diagonal4Low, weights15Low) + WeightedSquares(state.Diagonal4High, weights15High);
        cost[5] = WeightedSquares(state.Partial5Low, weights11Low) + WeightedSquares(state.Partial5High, weights11High);
        cost[6] = WeightedSquares(state.Columns, weights8);
        cost[7] = WeightedSquares(state.Partial7Low, weights11Low) + WeightedSquares(state.Partial7High, weights11High);
        var bestCost = 0;
        yDir = 0;
        for (var i = 0; i < 8; i++)
        {
            if (cost[i] > bestCost)
            {
                bestCost = cost[i];
                yDir = i;
            }
        }

        variance = (bestCost - cost[(yDir + 4) & 7]) >> 10;

        static Vector128<short> Load(ReadOnlySpan<ushort> source, int stride, int x0, int row, int shift)
            => (Vector128.Create(source.Slice(((row + 2) * stride) + x0, 8)) >>> shift).AsInt16() - Vector128.Create((short)128);
    }

    // The sum of the squares of the partial sums times their weights, in 32 bits like the scalar costs
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WeightedSquares(Vector128<short> sums, Vector128<short> weights)
    {
        var squaresLow = Av1Simd.MultiplyWideningLower(sums, sums);
        var squaresHigh = Av1Simd.MultiplyWideningUpper(sums, sums);
        var weighted = (squaresLow * Vector128.WidenLower(weights).AsInt32()) + (squaresHigh * Vector128.WidenUpper(weights).AsInt32());
        return Av1Simd.AddAcross(weighted);
    }

    // Adds the row i of the block to the partial sums of the directions 0 to 4 and 6 (complement is 8 - i, or 0)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddDirectionRow(ref DirectionSums state, Vector128<short> x, [ConstantExpected(Min = 0, Max = 7)] byte i, [ConstantExpected(Min = 0, Max = 7)] byte complement)
    {
        // Direction 0: i + j; direction 4: 7 + i - j, i + j of the reversed row; direction 2: the sum of the row; direction 6: j
        AddShifted(ref state.Diagonal0Low, ref state.Diagonal0High, x, i, complement);
        var reversed = Vector128.Shuffle(x, Vector128.Create((short)7, 6, 5, 4, 3, 2, 1, 0));
        AddShifted(ref state.Diagonal4Low, ref state.Diagonal4High, reversed, i, complement);
        state.Rows = state.Rows.WithElement(i, Av1Simd.AddAcross(x));
        state.Columns += x;

        // Directions 1 and 3: i + j / 2 and 3 + i - j / 2, from the sums of the pairs of samples of the row
        var pairs = Av1Simd.AddPairwise(x);
        AddShifted(ref state.Partial1Low, ref state.Partial1High, pairs, i, complement);
        AddShifted(ref state.Partial3Low, ref state.Partial3High, Vector128.Shuffle(pairs, Vector128.Create((short)3, 2, 1, 0, -1, -1, -1, -1)), i, complement);
    }

    // Adds the sum of the rows 2k and 2k + 1 to the partial sums of the directions 5 (3 - k + j) and 7 (k + j)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddDirectionRowPair(ref DirectionSums state, Vector128<short> rows, [ConstantExpected(Min = 0, Max = 3)] byte shift5, [ConstantExpected(Min = 0, Max = 7)] byte complement5, [ConstantExpected(Min = 0, Max = 3)] byte shift7, [ConstantExpected(Min = 0, Max = 7)] byte complement7)
    {
        AddShifted(ref state.Partial5Low, ref state.Partial5High, rows, shift5, complement5);
        AddShifted(ref state.Partial7Low, ref state.Partial7High, rows, shift7, complement7);
    }

    // Adds the lanes of x to the lanes n to n + 7 of the 16 lanes of low and high (complement is 8 - n, or 0)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddShifted(ref Vector128<short> low, ref Vector128<short> high, Vector128<short> x, [ConstantExpected(Min = 0, Max = 7)] byte n, [ConstantExpected(Min = 0, Max = 7)] byte complement)
    {
        if (n is 0)
        {
            low += x;
            return;
        }

        low += Av1Simd.ExtractVector128(Vector128<short>.Zero, x, complement);
        high += Av1Simd.ExtractVector128(x, Vector128<short>.Zero, complement);
    }

    // The partial sums of the direction process: the sums of index 0 to 7 in the Low vector and 8 to 14 in the High one
    [StructLayout(LayoutKind.Auto)]
    private struct DirectionSums
    {
        public Vector128<short> Diagonal0Low;
        public Vector128<short> Diagonal0High;
        public Vector128<short> Diagonal4Low;
        public Vector128<short> Diagonal4High;
        public Vector128<short> Partial1Low;
        public Vector128<short> Partial1High;
        public Vector128<short> Partial3Low;
        public Vector128<short> Partial3High;
        public Vector128<short> Partial5Low;
        public Vector128<short> Partial5High;
        public Vector128<short> Partial7Low;
        public Vector128<short> Partial7High;
        public Vector128<short> Rows;
        public Vector128<short> Columns;
    }

    /// <summary>The scalar reference of <see cref="FindDirection"/>.</summary>
    internal static void FindDirectionScalar(ReadOnlySpan<ushort> source, int stride, int x0, int bitDepth, out int yDir, out int variance)
    {
        Span<int> cost = stackalloc int[8];
        Span<int> partial = stackalloc int[8 * 15];
        partial.Clear();
        cost.Clear();
        for (var i = 0; i < 8; i++)
        {
            var row = source.Slice(((i + 2) * stride) + x0, 8);
            for (var j = 0; j < 8; j++)
            {
                var x = (row[j] >> (bitDepth - 8)) - 128;
                partial[i + j] += x;
                partial[15 + i + (j / 2)] += x;
                partial[30 + i] += x;
                partial[45 + 3 + i - (j / 2)] += x;
                partial[60 + 7 + i - j] += x;
                partial[75 + 3 - (i / 2) + j] += x;
                partial[90 + j] += x;
                partial[105 + (i / 2) + j] += x;
            }
        }

        var divTable = Av1Tables.DivTable;
        for (var i = 0; i < 8; i++)
        {
            cost[2] += partial[30 + i] * partial[30 + i];
            cost[6] += partial[90 + i] * partial[90 + i];
        }

        cost[2] *= divTable[8];
        cost[6] *= divTable[8];
        for (var i = 0; i < 7; i++)
        {
            cost[0] += ((partial[i] * partial[i]) + (partial[14 - i] * partial[14 - i])) * divTable[i + 1];
            cost[4] += ((partial[60 + i] * partial[60 + i]) + (partial[60 + 14 - i] * partial[60 + 14 - i])) * divTable[i + 1];
        }

        cost[0] += partial[7] * partial[7] * divTable[8];
        cost[4] += partial[60 + 7] * partial[60 + 7] * divTable[8];
        for (var i = 1; i < 8; i += 2)
        {
            var p = partial.Slice(i * 15, 15);
            for (var j = 0; j < 4 + 1; j++)
            {
                cost[i] += p[3 + j] * p[3 + j];
            }

            cost[i] *= divTable[8];
            for (var j = 0; j < 4 - 1; j++)
            {
                cost[i] += ((p[j] * p[j]) + (p[10 - j] * p[10 - j])) * divTable[(2 * j) + 2];
            }
        }

        var bestCost = 0;
        yDir = 0;
        for (var i = 0; i < 8; i++)
        {
            if (cost[i] > bestCost)
            {
                bestCost = cost[i];
                yDir = i;
            }
        }

        variance = (bestCost - cost[(yDir + 4) & 7]) >> 10;
    }

    // Section 7.15.3 "CDEF filter process"
    private static void FilterBlock(Av1Plane plane, ReadOnlySpan<ushort> source, int r, int c, int subX, int subY, int miRows, int miCols, int priStr, int secStr, int damping, int dir, int coeffShift)
    {
        // Without strength the sum of the taps is zero, and the sample is within the range of its neighbors: it is unchanged
        if (priStr is 0 && secStr is 0)
            return;

        var stride = plane.Stride;
        var samples = plane.Samples;
        var x0 = (c * Av1Constants.MiSize) >> subX;
        var y0 = (r * Av1Constants.MiSize) >> subY;
        var w = 8 >> subX;
        var h = 8 >> subY;
        var priTaps = Av1Tables.CdefPriTaps.Slice(((priStr >> coeffShift) & 1) * 2, 2);
        var secTaps = Av1Tables.CdefSecTaps.Slice(((priStr >> coeffShift) & 1) * 2, 2);

        // The taps reach two samples around the block: when they are all inside the frame, none has to be tested
        var planeWidth = (miCols * Av1Constants.MiSize) >> subX;
        var planeHeight = (miRows * Av1Constants.MiSize) >> subY;
        if (x0 >= 2 && y0 >= 2 && x0 + w + 2 <= planeWidth && y0 + h + 2 <= planeHeight)
        {
            FilterInteriorBlock(samples.Slice((y0 * stride) + x0), source[(x0 - 2)..], stride, w, h, priStr, secStr, damping, dir, priTaps, secTaps);
            return;
        }

        FilterEdgeBlock(samples, stride, source, x0, y0, w, h, subX, subY, miRows, miCols, priStr, secStr, damping, dir, priTaps, secTaps);
    }

    /// <summary>
    /// Filters a block of a plane whose taps may be outside the frame: those are not available (<c>CdefAvailable</c> is 0).
    /// <paramref name="source"/> holds the unfiltered rows from two rows above the block.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, the block and the two rows and columns around it are copied into a window where the
    /// samples outside the frame are <see cref="Unavailable"/>, a value that no sample has, and the rows are filtered with
    /// the arithmetic of the interior blocks: as a signed value the marker is below every sample, so it is never the
    /// maximum, as an unsigned value it is above every sample, so it is never the minimum, and its taps are masked out of
    /// the sum.
    /// </remarks>
    internal static void FilterEdgeBlock(Span<ushort> samples, int stride, ReadOnlySpan<ushort> source, int x0, int y0, int w, int h, int subX, int subY, int miRows, int miCols, int priStr, int secStr, int damping, int dir, ReadOnlySpan<byte> priTaps, ReadOnlySpan<byte> secTaps)
    {
        if (!Vector128.IsHardwareAccelerated || w is not (8 or 4) || h is not (8 or 4))
        {
            FilterEdgeBlockScalar(samples, stride, source, x0, y0, w, h, subX, subY, miRows, miCols, priStr, secStr, damping, dir, priTaps, secTaps);
            return;
        }

        // The samples of the plane are those of the 8x8 luma blocks of the frame
        var planeWidth = (miCols * Av1Constants.MiSize) >> subX;
        var planeHeight = (miRows * Av1Constants.MiSize) >> subY;
        Span<ushort> window = stackalloc ushort[(8 + 4) * EdgeWindowStride];
        for (var i = 0; i < h + 4; i++)
        {
            var y = y0 - 2 + i;
            var row = window.Slice(i * EdgeWindowStride, w + 4);
            if (y < 0 || y >= planeHeight)
            {
                row.Fill(Unavailable);
                continue;
            }

            var sourceRow = source.Slice(i * stride, stride);
            for (var j = 0; j < row.Length; j++)
            {
                var x = x0 - 2 + j;
                row[j] = x >= 0 && x < planeWidth ? sourceRow[x] : Unavailable;
            }
        }

        Span<int> offsets = stackalloc int[TapCount];
        GetTapOffsets(offsets, EdgeWindowStride, dir);
        var priShift = priStr is 0 ? 0 : Math.Max(0, damping - BitOperations.Log2((uint)priStr));
        var secShift = secStr is 0 ? 0 : Math.Max(0, damping - BitOperations.Log2((uint)secStr));
        var priThreshold = Vector128.Create((short)priStr);
        var secThreshold = Vector128.Create((short)secStr);
        var unavailable = Vector128.Create(Unavailable).AsInt16();
        var eight = Vector128.Create((short)8);
        var destination = samples.Slice((y0 * stride) + x0);

        // A row of 8 samples per vector, or two rows of 4 samples (the second one in the upper lanes)
        var rowsPerVector = w is 4 ? 2 : 1;
        for (var i = 0; i < h; i += rowsPerVector)
        {
            var center = ((i + 2) * EdgeWindowStride) + 2;
            var x = Load(window, center, w);
            var max = x;
            var min = x.AsUInt16();
            var sum = Vector128<short>.Zero;
            for (var t = 0; t < TapCount; t++)
            {
                var p = Load(window, center + offsets[t], w);
                max = Vector128.Max(p, max);
                min = Vector128.Min(p.AsUInt16(), min);
                if (t < 4)
                {
                    if (priStr is not 0)
                        sum += Vector128.AndNot(ConstrainVector(p - x, priThreshold, priShift), Vector128.Equals(p, unavailable)) * (short)(t < 2 ? priTaps[0] : priTaps[1]);
                }
                else if (secStr is not 0)
                {
                    sum += Vector128.AndNot(ConstrainVector(p - x, secThreshold, secShift), Vector128.Equals(p, unavailable)) * (short)(t < 8 ? secTaps[0] : secTaps[1]);
                }
            }

            // (8 + sum - (sum < 0 ? 1 : 0)) >> 4: the sign of the sum is -1 or 0
            var filtered = x + ((eight + sum + (sum >> 15)) >> 4);
            var result = Vector128.Min(Vector128.Max(filtered, min.AsInt16()), max).AsUInt16();
            if (w is 8)
            {
                result.CopyTo(destination.Slice(i * stride, w));
            }
            else
            {
                result.GetLower().CopyTo(destination.Slice(i * stride, w));
                result.GetUpper().CopyTo(destination.Slice((i + 1) * stride, w));
            }
        }

        static Vector128<short> Load(ReadOnlySpan<ushort> window, int index, int w) => w is 8
            ? Vector128.Create(window.Slice(index, 8)).AsInt16()
            : Vector128.Create(Vector64.Create(window.Slice(index, 4)), Vector64.Create(window.Slice(index + EdgeWindowStride, 4))).AsInt16();
    }

    /// <summary>The scalar reference of <see cref="FilterEdgeBlock"/>.</summary>
    internal static void FilterEdgeBlockScalar(Span<ushort> samples, int stride, ReadOnlySpan<ushort> source, int x0, int y0, int w, int h, int subX, int subY, int miRows, int miCols, int priStr, int secStr, int damping, int dir, ReadOnlySpan<byte> priTaps, ReadOnlySpan<byte> secTaps)
    {
        for (var i = 0; i < h; i++)
        {
            for (var j = 0; j < w; j++)
            {
                var sum = 0;
                int x = source[((i + 2) * stride) + x0 + j];
                var max = x;
                var min = x;
                for (var k = 0; k < 2; k++)
                {
                    for (var sign = -1; sign <= 1; sign += 2)
                    {
                        if (TryGet(source, stride, x0 + j, y0 + i, i + 2, dir, k, sign, subX, subY, miRows, miCols, out var p))
                        {
                            sum += priTaps[k] * Constrain(p - x, priStr, damping);
                            max = Math.Max(p, max);
                            min = Math.Min(p, min);
                        }

                        for (var dirOff = -2; dirOff <= 2; dirOff += 4)
                        {
                            if (TryGet(source, stride, x0 + j, y0 + i, i + 2, (dir + dirOff) & 7, k, sign, subX, subY, miRows, miCols, out var s))
                            {
                                sum += secTaps[k] * Constrain(s - x, secStr, damping);
                                max = Math.Max(s, max);
                                min = Math.Min(s, min);
                            }
                        }
                    }
                }

                samples[((y0 + i) * stride) + x0 + j] = (ushort)Math.Clamp(x + ((8 + sum - (sum < 0 ? 1 : 0)) >> 4), min, max);
            }
        }
    }

    // The offsets of the taps in a window of the given stride: 4 primary taps, then 8 secondary taps; two taps of opposite
    // signs for each distance, the two taps of the first distance before the ones of the second
    private static void GetTapOffsets(Span<int> offsets, int stride, int dir)
    {
        var directions = Av1Tables.CdefDirections;
        for (var k = 0; k < 2; k++)
        {
            var offset = (directions[((dir * 2) + k) * 2] * stride) + directions[(((dir * 2) + k) * 2) + 1];
            offsets[2 * k] = offset;
            offsets[(2 * k) + 1] = -offset;
            var first = (dir + 6) & 7;
            var second = (dir + 2) & 7;
            var firstOffset = (directions[((first * 2) + k) * 2] * stride) + directions[(((first * 2) + k) * 2) + 1];
            var secondOffset = (directions[((second * 2) + k) * 2] * stride) + directions[(((second * 2) + k) * 2) + 1];
            offsets[4 + (4 * k)] = firstOffset;
            offsets[5 + (4 * k)] = -firstOffset;
            offsets[6 + (4 * k)] = secondOffset;
            offsets[7 + (4 * k)] = -secondOffset;
        }
    }

    // The filter of a block whose taps are all inside the frame. The window starts two rows above and two samples to the
    // left of the block: the block is at row 2 and column 2 of the window
    private static void FilterInteriorBlock(Span<ushort> destination, ReadOnlySpan<ushort> window, int stride, int w, int h, int priStr, int secStr, int damping, int dir, ReadOnlySpan<byte> priTaps, ReadOnlySpan<byte> secTaps)
    {
        Span<int> offsets = stackalloc int[TapCount];
        GetTapOffsets(offsets, stride, dir);
        var priShift = priStr is 0 ? 0 : Math.Max(0, damping - BitOperations.Log2((uint)priStr));
        var secShift = secStr is 0 ? 0 : Math.Max(0, damping - BitOperations.Log2((uint)secStr));
        FilterInteriorRows(destination, window, stride, w, h, offsets, priStr, secStr, priShift, secShift, priTaps[0], priTaps[1], secTaps[0], secTaps[1]);
    }

    /// <summary>
    /// Filters the rows of a block whose taps are all inside the frame, from the offsets of its taps in the window.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, the samples of a row of the block (8 or 4 samples) are filtered together in 16-bit
    /// lanes, with the arithmetic of <see cref="FilterInteriorRowsScalar"/>: a sample has at most 12 bits, a difference of
    /// samples 13 bits with its sign, and the sum of the 12 weighted taps (weights of at most 4, magnitudes of at most the
    /// strengths, which are below 256) stays far below 2^15.
    /// </remarks>
    internal static void FilterInteriorRows(Span<ushort> destination, ReadOnlySpan<ushort> window, int stride, int w, int h, ReadOnlySpan<int> offsets, int priStr, int secStr, int priShift, int secShift, int priTap0, int priTap1, int secTap0, int secTap1)
    {
        if (Vector128.IsHardwareAccelerated && w is 8 or 4 && AreTapsInside(destination, window, stride, w, h, offsets))
        {
            FilterInteriorRowsVector(destination, window, stride, w, h, offsets, priStr, secStr, priShift, secShift, priTap0, priTap1, secTap0, secTap1);
        }
        else
        {
            FilterInteriorRowsScalar(destination, window, stride, w, h, offsets, priStr, secStr, priShift, secShift, priTap0, priTap1, secTap0, secTap1);
        }
    }

    /// <summary>The scalar reference of <see cref="FilterInteriorRows"/>.</summary>
    internal static void FilterInteriorRowsScalar(Span<ushort> destination, ReadOnlySpan<ushort> window, int stride, int w, int h, ReadOnlySpan<int> offsets, int priStr, int secStr, int priShift, int secShift, int priTap0, int priTap1, int secTap0, int secTap1)
    {
        for (var i = 0; i < h; i++)
        {
            var center = ((i + 2) * stride) + 2;
            var row = destination.Slice(i * stride, w);
            for (var j = 0; j < w; j++, center++)
            {
                int x = window[center];
                var max = x;
                var min = x;
                var sum = 0;
                for (var t = 0; t < 4; t++)
                {
                    int p = window[center + offsets[t]];
                    max = Math.Max(p, max);
                    min = Math.Min(p, min);
                    if (priStr is not 0)
                        sum += (t < 2 ? priTap0 : priTap1) * ConstrainShifted(p - x, priStr, priShift);
                }

                for (var t = 4; t < TapCount; t++)
                {
                    int p = window[center + offsets[t]];
                    max = Math.Max(p, max);
                    min = Math.Min(p, min);
                    if (secStr is not 0)
                        sum += (t < 8 ? secTap0 : secTap1) * ConstrainShifted(p - x, secStr, secShift);
                }

                row[j] = (ushort)Math.Clamp(x + ((8 + sum - (sum < 0 ? 1 : 0)) >> 4), min, max);
            }
        }
    }

    // Whether every sample that the vector rows read and write is inside the spans (they are then accessed without checks)
    private static bool AreTapsInside(Span<ushort> destination, ReadOnlySpan<ushort> window, int stride, int w, int h, ReadOnlySpan<int> offsets)
    {
        var lowest = 0;
        var highest = 0;
        foreach (var offset in offsets)
        {
            lowest = Math.Min(lowest, offset);
            highest = Math.Max(highest, offset);
        }

        var firstCenter = (2 * stride) + 2;
        var lastCenter = ((h + 1) * stride) + 2 + (w - 1);
        return offsets.Length == TapCount && h > 0 && stride >= w && firstCenter + lowest >= 0 && lastCenter + highest < window.Length && ((h - 1) * stride) + w <= destination.Length;
    }

    private static void FilterInteriorRowsVector(Span<ushort> destination, ReadOnlySpan<ushort> window, int stride, int w, int h, ReadOnlySpan<int> offsets, int priStr, int secStr, int priShift, int secShift, int priTap0, int priTap1, int secTap0, int secTap1)
    {
        ref var source = ref unsafe(MemoryMarshal.GetReference(window));
        ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
        var priThreshold = Vector128.Create((short)priStr);
        var secThreshold = Vector128.Create((short)secStr);
        var eight = Vector128.Create((short)8);

        // A row of 8 samples per vector, or two rows of 4 samples (the second one in the upper lanes); with an odd number of
        // rows of 4 samples, the last row is alone in the lower lanes
        var rowsPerVector = w is 4 ? 2 : 1;
        for (var i = 0; i < h; i += rowsPerVector)
        {
            var center = ((i + 2) * stride) + 2;
            var lowerOnly = w is 4 && i + 1 == h;
            var secondRow = lowerOnly ? -1 : stride;
            var x = Load(ref source, center, w, secondRow);
            var max = x;
            var min = x;
            var sum = Vector128<short>.Zero;
            for (var t = 0; t < 4; t++)
            {
                var p = Load(ref source, center + offsets[t], w, secondRow);
                max = Vector128.Max(p, max);
                min = Vector128.Min(p, min);
                if (priStr is not 0)
                    sum += ConstrainVector(p - x, priThreshold, priShift) * (short)(t < 2 ? priTap0 : priTap1);
            }

            for (var t = 4; t < TapCount; t++)
            {
                var p = Load(ref source, center + offsets[t], w, secondRow);
                max = Vector128.Max(p, max);
                min = Vector128.Min(p, min);
                if (secStr is not 0)
                    sum += ConstrainVector(p - x, secThreshold, secShift) * (short)(t < 8 ? secTap0 : secTap1);
            }

            // (8 + sum - (sum < 0 ? 1 : 0)) >> 4: the sign of the sum is -1 or 0
            var filtered = x + ((eight + sum + (sum >> 15)) >> 4);
            var result = Vector128.Min(Vector128.Max(filtered, min), max).AsUInt16();
            ref var target = ref unsafe(Unsafe.Add(ref output, i * stride));
            if (w is 8)
            {
                unsafe { result.StoreUnsafe(ref target); }
            }
            else
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.As<ushort, byte>(ref target), result.AsUInt64().ToScalar()); }
                if (!lowerOnly)
                    unsafe { Unsafe.WriteUnaligned(ref Unsafe.As<ushort, byte>(ref Unsafe.Add(ref target, stride)), result.AsUInt64().GetElement(1)); }
            }
        }

        // 8 samples, or 4 samples and the 4 samples secondRow further in the upper lanes (zero when secondRow is negative:
        // nothing is read after the 4 samples of the last row)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<short> Load(ref ushort source, int index, int w, int secondRow)
        {
            ref var first = ref unsafe(Unsafe.Add(ref source, index));
            if (w is 8)
                return unsafe(Vector128.LoadUnsafe(ref first)).AsInt16();

            // Vector loads: inserting the rows from general registers would make every vector depend on the previous one
            var lower = unsafe(Vector64.LoadUnsafe(ref first));
            if (secondRow < 0)
                return lower.ToVector128().AsInt16();

            return Vector128.Create(lower, unsafe(Vector64.LoadUnsafe(ref Unsafe.Add(ref first, secondRow)))).AsInt16();
        }
    }

    // ConstrainShifted on every lane
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> ConstrainVector(Vector128<short> diff, Vector128<short> threshold, int shift)
    {
        var absolute = Vector128.Abs(diff);
        var magnitude = Vector128.Max(Vector128<short>.Zero, Vector128.Min(absolute, threshold - (absolute >> shift)));
        var sign = diff >> 15;
        return (magnitude ^ sign) - sign;
    }

    // The constrain function for a threshold that is not zero, whose damping shift is known
    private static int ConstrainShifted(int diff, int threshold, int shift)
    {
        var absolute = Math.Abs(diff);
        var magnitude = Math.Max(0, Math.Min(absolute, threshold - (absolute >> shift)));
        return diff < 0 ? -magnitude : magnitude;
    }

    // cdef_get_at: a sample is available when it is inside the frame
    private static bool TryGet(ReadOnlySpan<ushort> source, int stride, int x, int y, int sourceRow, int direction, int k, int sign, int subX, int subY, int miRows, int miCols, out int value)
    {
        var directions = Av1Tables.CdefDirections;
        var offsetY = sign * directions[((direction * 2) + k) * 2];
        var offsetX = sign * directions[(((direction * 2) + k) * 2) + 1];
        y += offsetY;
        x += offsetX;
        var candidateR = (y << subY) >> Av1Constants.MiSizeLog2;
        var candidateC = (x << subX) >> Av1Constants.MiSizeLog2;
        if (y < 0 || x < 0 || candidateR >= miRows || candidateC >= miCols)
        {
            value = 0;
            return false;
        }

        value = source[((sourceRow + offsetY) * stride) + x];
        return true;
    }

    private static int Constrain(int diff, int threshold, int damping)
    {
        if (threshold is 0)
            return 0;

        var dampingAdj = Math.Max(0, damping - BitOperations.Log2((uint)threshold));
        var magnitude = Math.Clamp(threshold - (Math.Abs(diff) >> dampingAdj), 0, Math.Abs(diff));
        return diff < 0 ? -magnitude : magnitude;
    }
}
