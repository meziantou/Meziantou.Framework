using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    // The taps of a sample: 4 primary taps and 8 secondary taps
    private const int TapCount = 12;

    /// <summary>Filters the planes of <paramref name="frame"/>.</summary>
    /// <param name="frame">The deblocked frame.</param>
    /// <param name="state">The block-level state of the frame.</param>
    /// <param name="source">Scratch for the unfiltered rows: <see cref="SourceRows"/> rows of the stride of the luma plane, for each plane.</param>
    /// <param name="cancellationToken">The cancellation token of the operation.</param>
    public static void Apply(Av1FrameBuffer frame, Av1FrameState state, Span<ushort> source, CancellationToken cancellationToken)
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
        for (var r = 0; r < miRows; r += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The unfiltered rows of the band: rows 0 and 1 are the two rows above it, saved from the previous band
            for (var plane = 0; plane < planeCount; plane++)
            {
                var subY = plane > 0 ? sequence.SubsamplingY : 0;
                var buffer = frame.GetPlane(plane);
                var stride = buffer.Stride;
                var h = 8 >> subY;
                var y0 = (r * Av1Constants.MiSize) >> subY;
                var planeSource = source.Slice(plane * sourceLength, sourceLength);
                if (r > 0)
                    planeSource.Slice(h * stride, 2 * stride).CopyTo(planeSource);

                var rows = Math.Min(h + 2, buffer.AllocatedHeight - y0);
                buffer.Samples.Slice(y0 * stride, rows * stride).CopyTo(planeSource[(2 * stride)..]);
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
                FindDirection(source[..sourceLength], luma.Stride, c * Av1Constants.MiSize, bitDepth, out var yDir, out var variance);
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

    // Section 7.15.2 "CDEF direction process": the 8x8 luma block starts at row 2 of the source
    private static void FindDirection(ReadOnlySpan<ushort> source, int stride, int x0, int bitDepth, out int yDir, out int variance)
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

    // The filter of a block whose taps are all inside the frame. The window starts two rows above and two samples to the
    // left of the block: the block is at row 2 and column 2 of the window
    private static void FilterInteriorBlock(Span<ushort> destination, ReadOnlySpan<ushort> window, int stride, int w, int h, int priStr, int secStr, int damping, int dir, ReadOnlySpan<byte> priTaps, ReadOnlySpan<byte> secTaps)
    {
        // The offsets of the taps in the window: 4 primary taps, then 8 secondary taps; two taps of opposite signs for each
        // distance, the two taps of the first distance before the ones of the second
        Span<int> offsets = stackalloc int[TapCount];
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
        for (var i = 0; i < h; i++)
        {
            var center = ((i + 2) * stride) + 2;
            var x = Load(ref source, center, w);
            var max = x;
            var min = x;
            var sum = Vector128<short>.Zero;
            for (var t = 0; t < 4; t++)
            {
                var p = Load(ref source, center + offsets[t], w);
                max = Vector128.Max(p, max);
                min = Vector128.Min(p, min);
                if (priStr is not 0)
                    sum += Constrain(p - x, priThreshold, priShift) * (short)(t < 2 ? priTap0 : priTap1);
            }

            for (var t = 4; t < TapCount; t++)
            {
                var p = Load(ref source, center + offsets[t], w);
                max = Vector128.Max(p, max);
                min = Vector128.Min(p, min);
                if (secStr is not 0)
                    sum += Constrain(p - x, secThreshold, secShift) * (short)(t < 8 ? secTap0 : secTap1);
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
            }
        }

        // 8 samples, or 4 samples in the lower lanes (nothing is read after them)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<short> Load(ref ushort source, int index, int w)
        {
            ref var first = ref unsafe(Unsafe.Add(ref source, index));
            return w is 8
                ? unsafe(Vector128.LoadUnsafe(ref first)).AsInt16()
                : Vector128.CreateScalar(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<ushort, byte>(ref first)))).AsInt16();
        }

        // ConstrainShifted on every lane
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<short> Constrain(Vector128<short> diff, Vector128<short> threshold, int shift)
        {
            var absolute = Vector128.Abs(diff);
            var magnitude = Vector128.Max(Vector128<short>.Zero, Vector128.Min(absolute, threshold - (absolute >> shift)));
            var sign = diff >> 15;
            return (magnitude ^ sign) - sign;
        }
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
