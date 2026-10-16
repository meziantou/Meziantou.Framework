using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The VP8 loop filter (RFC 6386 section 15), applied in place to the reconstructed planes once every macroblock is
/// reconstructed, in macroblock raster order: per macroblock, its left edge, its inner vertical edges, its top edge, then its
/// inner horizontal edges. The simple filter changes only luma; the normal filter changes luma and chroma.
/// </summary>
/// <remarks>
/// <para>
/// Per macroblock, with filter level <c>L</c> (0 disables filtering) and sharpness <c>S</c>: the interior limit is
/// <c>L &gt;&gt; (S &gt; 4 ? 2 : 1)</c> when <c>S &gt; 0</c>, at most <c>9 - S</c>, at least 1; the macroblock-edge limit is
/// <c>(L + 2) * 2 + interior</c> and the sub-block edge limit <c>L * 2 + interior</c>; the high-edge-variance threshold is 2
/// from level 40, 1 from level 15, otherwise 0. Inner edges are filtered only for macroblocks flagged so by the decoder.
/// </para>
/// <para>
/// Every edge segment checks once that all the pixels it reads and writes lie in the plane; the pixels are then accessed
/// without per-pixel bounds checks.
/// </para>
/// <para>
/// The normal filter has a vector path: the 8 pixels across an edge (p3 to q3) of its 16 or 8 positions are 8 vectors of
/// 16-bit lanes, one lane per position, and every lane computes the result of the scalar filter
/// (<see cref="ApplyScalar"/>). The pixels of a vertical edge go through a transposed copy.
/// </para>
/// </remarks>
internal static class Vp8LoopFilter
{
    /// <summary>Filters the planes.</summary>
    /// <param name="planes">The reconstructed planes.</param>
    /// <param name="filterInfo">Per macroblock: the level in bits 0-5 and the inner-edge flag in bit 7.</param>
    /// <param name="mbWidth">The number of macroblock columns.</param>
    /// <param name="mbHeight">The number of macroblock rows.</param>
    /// <param name="simple">Whether the simple filter is selected (otherwise the normal filter).</param>
    /// <param name="sharpness">The sharpness level (0 to 7).</param>
    public static void Apply(Vp8Planes planes, ReadOnlySpan<byte> filterInfo, int mbWidth, int mbHeight, bool simple, int sharpness)
        => Apply(planes, filterInfo, mbWidth, mbHeight, simple, sharpness, Vector128.IsHardwareAccelerated);

    /// <summary>Filters the planes one pixel position at a time: the reference of the vector path.</summary>
    internal static void ApplyScalar(Vp8Planes planes, ReadOnlySpan<byte> filterInfo, int mbWidth, int mbHeight, bool simple, int sharpness)
        => Apply(planes, filterInfo, mbWidth, mbHeight, simple, sharpness, useVectors: false);

    private static void Apply(Vp8Planes planes, ReadOnlySpan<byte> filterInfo, int mbWidth, int mbHeight, bool simple, int sharpness, bool useVectors)
    {
        var y = planes.Y;
        var u = planes.U;
        var v = planes.V;
        var yStride = planes.YStride;
        var uvStride = planes.UVStride;
        for (var mbY = 0; mbY < mbHeight; mbY++)
        {
            for (var mbX = 0; mbX < mbWidth; mbX++)
            {
                var info = filterInfo[(mbY * mbWidth) + mbX];
                var level = info & 0x3F;
                if (level == 0)
                    continue;

                var inner = (info & 0x80) != 0;
                var interior = GetInteriorLimit(level, sharpness);
                var edgeLimit = ((level + 2) * 2) + interior;
                var subLimit = (level * 2) + interior;
                var hevThreshold = level >= 40 ? 2 : level >= 15 ? 1 : 0;
                var yOffset = (mbY * 16 * yStride) + (mbX * 16);
                var uvOffset = (mbY * 8 * uvStride) + (mbX * 8);
                if (simple)
                {
                    if (mbX > 0)
                    {
                        SimpleEdge(y, yOffset, 1, yStride, edgeLimit);
                    }

                    if (inner)
                    {
                        SimpleEdge(y, yOffset + 4, 1, yStride, subLimit);
                        SimpleEdge(y, yOffset + 8, 1, yStride, subLimit);
                        SimpleEdge(y, yOffset + 12, 1, yStride, subLimit);
                    }

                    if (mbY > 0)
                    {
                        SimpleEdge(y, yOffset, yStride, 1, edgeLimit);
                    }

                    if (inner)
                    {
                        SimpleEdge(y, yOffset + (4 * yStride), yStride, 1, subLimit);
                        SimpleEdge(y, yOffset + (8 * yStride), yStride, 1, subLimit);
                        SimpleEdge(y, yOffset + (12 * yStride), yStride, 1, subLimit);
                    }

                    continue;
                }

                if (mbX > 0)
                {
                    Edge(y, yOffset, 1, yStride, 16, edgeLimit, interior, hevThreshold, macroblock: true, useVectors);
                    Edge(u, uvOffset, 1, uvStride, 8, edgeLimit, interior, hevThreshold, macroblock: true, useVectors);
                    Edge(v, uvOffset, 1, uvStride, 8, edgeLimit, interior, hevThreshold, macroblock: true, useVectors);
                }

                if (inner)
                {
                    Edge(y, yOffset + 4, 1, yStride, 16, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(y, yOffset + 8, 1, yStride, 16, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(y, yOffset + 12, 1, yStride, 16, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(u, uvOffset + 4, 1, uvStride, 8, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(v, uvOffset + 4, 1, uvStride, 8, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                }

                if (mbY > 0)
                {
                    Edge(y, yOffset, yStride, 1, 16, edgeLimit, interior, hevThreshold, macroblock: true, useVectors);
                    Edge(u, uvOffset, uvStride, 1, 8, edgeLimit, interior, hevThreshold, macroblock: true, useVectors);
                    Edge(v, uvOffset, uvStride, 1, 8, edgeLimit, interior, hevThreshold, macroblock: true, useVectors);
                }

                if (inner)
                {
                    Edge(y, yOffset + (4 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(y, yOffset + (8 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(y, yOffset + (12 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(u, uvOffset + (4 * uvStride), uvStride, 1, 8, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                    Edge(v, uvOffset + (4 * uvStride), uvStride, 1, 8, subLimit, interior, hevThreshold, macroblock: false, useVectors);
                }
            }
        }
    }

    /// <summary>Computes the interior limit of a filter level.</summary>
    public static int GetInteriorLimit(int level, int sharpness)
    {
        var interior = level;
        if (sharpness > 0)
        {
            interior >>= sharpness > 4 ? 2 : 1;
            interior = Math.Min(interior, 9 - sharpness);
        }

        return Math.Max(interior, 1);
    }

    /// <summary>
    /// Returns a reference to the first pixel after the edge (q0) of the first position, after checking that every pixel at
    /// <paramref name="reach"/> pixels on each side of the <paramref name="count"/> positions lies in the plane.
    /// </summary>
    private static ref byte GetEdgeOrigin(Span<byte> plane, int offset, int step, int along, int count, int reach)
    {
        var first = offset - (reach * step);
        var last = offset + ((count - 1) * along) + ((reach - 1) * step);
        if (first < 0 || last >= plane.Length)
            throw new InvalidOperationException("The loop-filter edge lies outside the plane.");

        return ref unsafe(Unsafe.Add(ref MemoryMarshal.GetReference(plane), offset));
    }

    /// <summary>Filters the 16 luma positions of an edge with the simple filter (p1, p0 | q0, q1).</summary>
    private static void SimpleEdge(Span<byte> plane, int offset, int step, int along, int edgeLimit)
    {
        ref var origin = ref GetEdgeOrigin(plane, offset, step, along, 16, 2);
        for (var i = 0; i < 16; i++)
        {
            ref var q0 = ref unsafe(Unsafe.Add(ref origin, i * along));
            int p1 = unsafe(Unsafe.Add(ref q0, -2 * step));
            int p0 = unsafe(Unsafe.Add(ref q0, -step));
            int q0Value = q0;
            int q1 = unsafe(Unsafe.Add(ref q0, step));
            if ((Math.Abs(p0 - q0Value) * 2) + (Math.Abs(p1 - q1) >> 1) > edgeLimit)
                continue;

            CommonAdjust(ref q0, step, p1 - 128, p0 - 128, q0Value - 128, q1 - 128, useOuterTaps: true);
        }
    }

    // An edge of the normal filter: a macroblock edge or an inner (sub-block) edge
    private static void Edge(Span<byte> plane, int offset, int step, int along, int count, int edgeLimit, int interior, int hevThreshold, bool macroblock, bool useVectors)
    {
        if (useVectors)
        {
            EdgeVector(plane, offset, step, along, count, edgeLimit, interior, hevThreshold, macroblock);
        }
        else if (macroblock)
        {
            MacroblockEdge(plane, offset, step, along, count, edgeLimit, interior, hevThreshold);
        }
        else
        {
            SubBlockEdge(plane, offset, step, along, count, edgeLimit, interior, hevThreshold);
        }
    }

    // The pixels p3 to q3 of the positions of the edge are 8 vectors of 16 bytes, one byte per position: rows of the plane for
    // a horizontal edge, columns for a vertical one (the rows of 8 pixels across the edge are transposed as two 8 x 8 blocks,
    // rows 0-7 in the low halves of the vectors and rows 8-15 in the high halves)
    private static void EdgeVector(Span<byte> plane, int offset, int step, int along, int count, int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        _ = GetEdgeOrigin(plane, offset, step, along, count, 4);
        Vector128<byte> p3, p2, p1, p0, q0, q1, q2, q3;
        if (along is 1)
        {
            p3 = Load(plane, offset - (4 * step), count);
            p2 = Load(plane, offset - (3 * step), count);
            p1 = Load(plane, offset - (2 * step), count);
            p0 = Load(plane, offset - step, count);
            q0 = Load(plane, offset, count);
            q1 = Load(plane, offset + step, count);
            q2 = Load(plane, offset + (2 * step), count);
            q3 = Load(plane, offset + (3 * step), count);
        }
        else
        {
            var start = offset - 4;
            var r0 = LoadRows(plane, start, along, 0, count);
            var r1 = LoadRows(plane, start, along, 1, count);
            var r2 = LoadRows(plane, start, along, 2, count);
            var r3 = LoadRows(plane, start, along, 3, count);
            var r4 = LoadRows(plane, start, along, 4, count);
            var r5 = LoadRows(plane, start, along, 5, count);
            var r6 = LoadRows(plane, start, along, 6, count);
            var r7 = LoadRows(plane, start, along, 7, count);
            Transpose8x8(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            p3 = r0.AsByte();
            p2 = r1.AsByte();
            p1 = r2.AsByte();
            p0 = r3.AsByte();
            q0 = r4.AsByte();
            q1 = r5.AsByte();
            q2 = r6.AsByte();
            q3 = r7.AsByte();
        }

        // With 8 positions, the high lanes are not stored: their results do not matter
        FilterLanes(p3, ref p2, ref p1, ref p0, ref q0, ref q1, ref q2, q3, edgeLimit, interior, hevThreshold, macroblock);
        if (along is 1)
        {
            Store(p2, plane, offset - (3 * step), count);
            Store(p1, plane, offset - (2 * step), count);
            Store(p0, plane, offset - step, count);
            Store(q0, plane, offset, count);
            Store(q1, plane, offset + step, count);
            Store(q2, plane, offset + (2 * step), count);
            return;
        }

        // The rows are written whole: p3 and q3 keep their values
        {
            var r0 = p3.AsUInt64();
            var r1 = p2.AsUInt64();
            var r2 = p1.AsUInt64();
            var r3 = p0.AsUInt64();
            var r4 = q0.AsUInt64();
            var r5 = q1.AsUInt64();
            var r6 = q2.AsUInt64();
            var r7 = q3.AsUInt64();
            Transpose8x8(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
            var start = offset - 4;
            StoreRows(r0, plane, start, along, 0, count);
            StoreRows(r1, plane, start, along, 1, count);
            StoreRows(r2, plane, start, along, 2, count);
            StoreRows(r3, plane, start, along, 3, count);
            StoreRows(r4, plane, start, along, 4, count);
            StoreRows(r5, plane, start, along, 5, count);
            StoreRows(r6, plane, start, along, 6, count);
            StoreRows(r7, plane, start, along, 7, count);
        }

        static Vector128<byte> Load(ReadOnlySpan<byte> plane, int offset, int count)
            => count is 16 ? Vector128.Create(plane.Slice(offset, 16)) : Vector128.CreateScalar(BinaryPrimitives.ReadUInt64LittleEndian(plane.Slice(offset, 8))).AsByte();

        static void Store(Vector128<byte> value, Span<byte> plane, int offset, int count)
        {
            if (count is 16)
            {
                value.CopyTo(plane.Slice(offset, 16));
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(plane.Slice(offset, 8), value.AsUInt64().ToScalar());
            }
        }

        // The 8 pixels of row i (low half) and of row 8 + i (high half, zero when the edge has 8 positions)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<ulong> LoadRows(ReadOnlySpan<byte> plane, int start, int along, int i, int count)
        {
            var low = BinaryPrimitives.ReadUInt64LittleEndian(plane.Slice(start + (i * along), 8));
            return count is 16
                ? Vector128.Create(low, BinaryPrimitives.ReadUInt64LittleEndian(plane.Slice(start + ((8 + i) * along), 8)))
                : Vector128.CreateScalar(low);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StoreRows(Vector128<ulong> value, Span<byte> plane, int start, int along, int i, int count)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(plane.Slice(start + (i * along), 8), value.GetElement(0));
            if (count is 16)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(plane.Slice(start + ((8 + i) * along), 8), value.GetElement(1));
            }
        }

    }

    // Transposes two 8 x 8 blocks of bytes at once, one in each 64-bit half: a row per value (its byte k is the column k).
    // Exchanges the bytes, then the pairs of bytes, then the groups of 4 bytes, between the rows and the columns
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose8x8(ref Vector128<ulong> r0, ref Vector128<ulong> r1, ref Vector128<ulong> r2, ref Vector128<ulong> r3, ref Vector128<ulong> r4, ref Vector128<ulong> r5, ref Vector128<ulong> r6, ref Vector128<ulong> r7)
    {
        const ulong Mask1 = 0x00FF00FF00FF00FF;
        const ulong Mask2 = 0x0000FFFF0000FFFF;
        const ulong Mask4 = 0x00000000FFFFFFFF;
        Exchange(ref r0, ref r1, Mask1, 8);
        Exchange(ref r2, ref r3, Mask1, 8);
        Exchange(ref r4, ref r5, Mask1, 8);
        Exchange(ref r6, ref r7, Mask1, 8);
        Exchange(ref r0, ref r2, Mask2, 16);
        Exchange(ref r1, ref r3, Mask2, 16);
        Exchange(ref r4, ref r6, Mask2, 16);
        Exchange(ref r5, ref r7, Mask2, 16);
        Exchange(ref r0, ref r4, Mask4, 32);
        Exchange(ref r1, ref r5, Mask4, 32);
        Exchange(ref r2, ref r6, Mask4, 32);
        Exchange(ref r3, ref r7, Mask4, 32);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Exchange(ref Vector128<ulong> a, ref Vector128<ulong> b, ulong mask, int shift)
        {
            var m = Vector128.Create(mask);
            var first = (a & m) | ((b & m) << shift);
            b = ((a >> shift) & m) | Vector128.AndNot(b, m);
            a = first;
        }
    }

    // The filter of MacroblockEdge or SubBlockEdge for 16 positions, one per byte lane. The signed values (pixel - 128) are
    // computed with saturating byte arithmetic, which gives the clamped results of the scalar filter:
    // - SubtractSaturate(p1, q1) is Clamp128(p1 - q1);
    // - Clamp128(a + 3 (q0 - p0)) is a + d + d + d with d = SubtractSaturate(q0, p0), each addition saturating: when
    //   |q0 - p0| >= 128 both saturate toward its sign, otherwise the partial sums move monotonically toward the final sum
    //   and saturate exactly when it is out of range;
    // - the differences and the edge-limit sum are unsigned saturating differences and sums (the limits are below 255);
    // - the flat filter's (k w + 63) >> 7 is computed in 16-bit lanes (|27 w + 63| < 2^15).
    private static void FilterLanes(Vector128<byte> p3, ref Vector128<byte> p2, ref Vector128<byte> p1, ref Vector128<byte> p0, ref Vector128<byte> q0, ref Vector128<byte> q1, ref Vector128<byte> q2, Vector128<byte> q3, int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        var interiorLimit = Vector128.Create((byte)interior);
        var edgeSum = Vector128.AddSaturate(Vector128.AddSaturate(Difference(p0, q0), Difference(p0, q0)), Vector128.ShiftRightLogical(Difference(p1, q1), 1));
        var filter = Vector128.LessThanOrEqual(edgeSum, Vector128.Create((byte)edgeLimit))
            & Vector128.LessThanOrEqual(Difference(p3, p2), interiorLimit) & Vector128.LessThanOrEqual(Difference(p2, p1), interiorLimit)
            & Vector128.LessThanOrEqual(Difference(p1, p0), interiorLimit) & Vector128.LessThanOrEqual(Difference(q3, q2), interiorLimit)
            & Vector128.LessThanOrEqual(Difference(q2, q1), interiorLimit) & Vector128.LessThanOrEqual(Difference(q1, q0), interiorLimit);
        if (filter == Vector128<byte>.Zero)
            return;

        var threshold = Vector128.Create((byte)hevThreshold);
        var hev = Vector128.GreaterThan(Difference(p1, p0), threshold) | Vector128.GreaterThan(Difference(q1, q0), threshold);
        var sign = Vector128.Create((byte)0x80);
        var sp2 = (p2 ^ sign).AsSByte();
        var sp1 = (p1 ^ sign).AsSByte();
        var sp0 = (p0 ^ sign).AsSByte();
        var sq0 = (q0 ^ sign).AsSByte();
        var sq1 = (q1 ^ sign).AsSByte();
        var sq2 = (q2 ^ sign).AsSByte();
        var step = Vector128.SubtractSaturate(sq0, sp0);
        var three = Vector128.Create((sbyte)3);
        var four = Vector128.Create((sbyte)4);
        if (macroblock)
        {
            // The filter value of CommonAdjust with the outer taps, which is also w of the 3 taps of a flat edge
            var w = Vector128.AddSaturate(Vector128.AddSaturate(Vector128.AddSaturate(Vector128.SubtractSaturate(sp1, sq1), step), step), step);
            var b = Vector128.ShiftRightArithmetic(Vector128.AddSaturate(w, three), 3);
            var a = Vector128.ShiftRightArithmetic(Vector128.AddSaturate(w, four), 3);
            var q0Hev = Vector128.SubtractSaturate(sq0, a);
            var p0Hev = Vector128.AddSaturate(sp0, b);
            var wLow = Vector128.WidenLower(w);
            var wHigh = Vector128.WidenUpper(w);
            var a27 = Taps(wLow, wHigh, 27);
            var a18 = Taps(wLow, wHigh, 18);
            var a9 = Taps(wLow, wHigh, 9);
            var flat = filter & ~hev;
            q0 = Vector128.ConditionalSelect(filter, Vector128.ConditionalSelect(hev.AsSByte(), q0Hev, Vector128.SubtractSaturate(sq0, a27)).AsByte() ^ sign, q0);
            p0 = Vector128.ConditionalSelect(filter, Vector128.ConditionalSelect(hev.AsSByte(), p0Hev, Vector128.AddSaturate(sp0, a27)).AsByte() ^ sign, p0);
            q1 = Vector128.ConditionalSelect(flat, Vector128.SubtractSaturate(sq1, a18).AsByte() ^ sign, q1);
            p1 = Vector128.ConditionalSelect(flat, Vector128.AddSaturate(sp1, a18).AsByte() ^ sign, p1);
            q2 = Vector128.ConditionalSelect(flat, Vector128.SubtractSaturate(sq2, a9).AsByte() ^ sign, q2);
            p2 = Vector128.ConditionalSelect(flat, Vector128.AddSaturate(sp2, a9).AsByte() ^ sign, p2);
        }
        else
        {
            // CommonAdjust with the outer taps where the edge variance is high, then the adjustment of p1 and q1 elsewhere
            var outerTaps = Vector128.SubtractSaturate(sp1, sq1) & hev.AsSByte();
            var value = Vector128.AddSaturate(Vector128.AddSaturate(Vector128.AddSaturate(outerTaps, step), step), step);
            var b = Vector128.ShiftRightArithmetic(Vector128.AddSaturate(value, three), 3);
            var a = Vector128.ShiftRightArithmetic(Vector128.AddSaturate(value, four), 3);
            q0 = Vector128.ConditionalSelect(filter, Vector128.SubtractSaturate(sq0, a).AsByte() ^ sign, q0);
            p0 = Vector128.ConditionalSelect(filter, Vector128.AddSaturate(sp0, b).AsByte() ^ sign, p0);

            // a is between -16 and 15: (a + 1) >> 1 does not saturate
            a = Vector128.ShiftRightArithmetic(a + Vector128<sbyte>.One, 1);
            var outer = filter & ~hev;
            q1 = Vector128.ConditionalSelect(outer, Vector128.SubtractSaturate(sq1, a).AsByte() ^ sign, q1);
            p1 = Vector128.ConditionalSelect(outer, Vector128.AddSaturate(sp1, a).AsByte() ^ sign, p1);
        }

        static Vector128<byte> Difference(Vector128<byte> a, Vector128<byte> b) => Vector128.SubtractSaturate(a, b) | Vector128.SubtractSaturate(b, a);

        // (k w + 63) >> 7 for the 16 lanes, between -27 and 27
        static Vector128<sbyte> Taps(Vector128<short> low, Vector128<short> high, short k)
        {
            var round = Vector128.Create((short)63);
            var factor = Vector128.Create(k);
            return Vector128.Narrow(Vector128.ShiftRightArithmetic((low * factor) + round, 7), Vector128.ShiftRightArithmetic((high * factor) + round, 7));
        }
    }

    private static void MacroblockEdge(Span<byte> plane, int offset, int step, int along, int count, int edgeLimit, int interior, int hevThreshold)
    {
        ref var origin = ref GetEdgeOrigin(plane, offset, step, along, count, 4);
        for (var i = 0; i < count; i++)
        {
            ref var q0 = ref unsafe(Unsafe.Add(ref origin, i * along));
            int p3 = unsafe(Unsafe.Add(ref q0, -4 * step));
            int p2 = unsafe(Unsafe.Add(ref q0, -3 * step));
            int p1 = unsafe(Unsafe.Add(ref q0, -2 * step));
            int p0 = unsafe(Unsafe.Add(ref q0, -step));
            int q0Value = q0;
            int q1 = unsafe(Unsafe.Add(ref q0, step));
            int q2 = unsafe(Unsafe.Add(ref q0, 2 * step));
            int q3 = unsafe(Unsafe.Add(ref q0, 3 * step));
            if (!ShouldFilter(p3, p2, p1, p0, q0Value, q1, q2, q3, edgeLimit, interior))
                continue;

            if (Math.Abs(p1 - p0) > hevThreshold || Math.Abs(q1 - q0Value) > hevThreshold)
            {
                CommonAdjust(ref q0, step, p1 - 128, p0 - 128, q0Value - 128, q1 - 128, useOuterTaps: true);
                continue;
            }

            var sp2 = p2 - 128;
            var sp1 = p1 - 128;
            var sp0 = p0 - 128;
            var sq0 = q0Value - 128;
            var sq1 = q1 - 128;
            var sq2 = q2 - 128;
            var w = Clamp128(Clamp128(sp1 - sq1) + (3 * (sq0 - sp0)));
            var a = Clamp128(((27 * w) + 63) >> 7);
            q0 = ToUnsigned(sq0 - a);
            unsafe { Unsafe.Add(ref q0, -step) = ToUnsigned(sp0 + a); }
            a = Clamp128(((18 * w) + 63) >> 7);
            unsafe { Unsafe.Add(ref q0, step) = ToUnsigned(sq1 - a); }
            unsafe { Unsafe.Add(ref q0, -2 * step) = ToUnsigned(sp1 + a); }
            a = Clamp128(((9 * w) + 63) >> 7);
            unsafe { Unsafe.Add(ref q0, 2 * step) = ToUnsigned(sq2 - a); }
            unsafe { Unsafe.Add(ref q0, -3 * step) = ToUnsigned(sp2 + a); }
        }
    }

    private static void SubBlockEdge(Span<byte> plane, int offset, int step, int along, int count, int edgeLimit, int interior, int hevThreshold)
    {
        ref var origin = ref GetEdgeOrigin(plane, offset, step, along, count, 4);
        for (var i = 0; i < count; i++)
        {
            ref var q0 = ref unsafe(Unsafe.Add(ref origin, i * along));
            int p3 = unsafe(Unsafe.Add(ref q0, -4 * step));
            int p2 = unsafe(Unsafe.Add(ref q0, -3 * step));
            int p1 = unsafe(Unsafe.Add(ref q0, -2 * step));
            int p0 = unsafe(Unsafe.Add(ref q0, -step));
            int q0Value = q0;
            int q1 = unsafe(Unsafe.Add(ref q0, step));
            int q2 = unsafe(Unsafe.Add(ref q0, 2 * step));
            int q3 = unsafe(Unsafe.Add(ref q0, 3 * step));
            if (!ShouldFilter(p3, p2, p1, p0, q0Value, q1, q2, q3, edgeLimit, interior))
                continue;

            var hev = Math.Abs(p1 - p0) > hevThreshold || Math.Abs(q1 - q0Value) > hevThreshold;
            var a = (CommonAdjust(ref q0, step, p1 - 128, p0 - 128, q0Value - 128, q1 - 128, useOuterTaps: hev) + 1) >> 1;
            if (!hev)
            {
                unsafe { Unsafe.Add(ref q0, step) = ToUnsigned(q1 - 128 - a); }
                unsafe { Unsafe.Add(ref q0, -2 * step) = ToUnsigned(p1 - 128 + a); }
            }
        }
    }

    /// <summary>Adjusts p0 and q0 (signed values) and returns the filter value applied to q0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CommonAdjust(ref byte q0, int step, int p1, int p0, int q0Value, int q1, bool useOuterTaps)
    {
        var a = Clamp128((useOuterTaps ? Clamp128(p1 - q1) : 0) + (3 * (q0Value - p0)));
        var b = Clamp128(a + 3) >> 3;
        a = Clamp128(a + 4) >> 3;
        q0 = ToUnsigned(q0Value - a);
        unsafe { Unsafe.Add(ref q0, -step) = ToUnsigned(p0 + b); }
        return a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldFilter(int p3, int p2, int p1, int p0, int q0, int q1, int q2, int q3, int edgeLimit, int interior)
        => (Math.Abs(p0 - q0) * 2) + (Math.Abs(p1 - q1) >> 1) <= edgeLimit
            && Math.Abs(p3 - p2) <= interior && Math.Abs(p2 - p1) <= interior && Math.Abs(p1 - p0) <= interior
            && Math.Abs(q3 - q2) <= interior && Math.Abs(q2 - q1) <= interior && Math.Abs(q1 - q0) <= interior;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToUnsigned(int value) => (byte)(Clamp128(value) + 128);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp128(int value) => value < -128 ? -128 : value > 127 ? 127 : value;
}
