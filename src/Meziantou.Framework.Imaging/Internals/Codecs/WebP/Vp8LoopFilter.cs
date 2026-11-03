using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The VP8 loop filter (RFC 6386 section 15), applied in place to the reconstructed planes in macroblock raster order (the
/// whole frame, or one macroblock row at a time): per macroblock, its left edge, its inner vertical edges, its top edge, then
/// its inner horizontal edges. The simple filter changes only luma; the normal filter changes luma and chroma.
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
/// The normal filter has a vector path: the 8 pixels across an edge (p3 to q3) of its 16 positions are 8 vectors of byte
/// lanes, one lane per position, and every lane computes the result of the scalar filter (<see cref="ApplyScalar"/>). The
/// same edge of the Cb and Cr planes (8 positions each) shares the vectors. The pixels of a vertical edge are transposed
/// by interleaving the rows (bytes, pairs, groups of 4, then halves), and an edge whose positions are all left unfiltered is
/// not written back.
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

    /// <summary>
    /// Filters macroblock row <paramref name="mbY"/> of planes that hold the frame rows from <paramref name="lumaOrigin"/>
    /// (<c>lumaOrigin / 2</c> for chroma): the rows of a frame filtered one after the other, in order, give the result of
    /// <see cref="Apply(Vp8Planes, ReadOnlySpan{byte}, int, int, bool, int)"/>. The macroblock row reads and changes the 4 pixel
    /// rows above it, and is read and changed by the next macroblock row in its last 4 pixel rows.
    /// </summary>
    /// <param name="planes">The reconstructed planes.</param>
    /// <param name="rowInfo">The filter information of the macroblocks of the row.</param>
    /// <param name="mbY">The macroblock row.</param>
    /// <param name="lumaOrigin">The frame row of the first luma row of the planes (a multiple of 8).</param>
    /// <param name="simple">Whether the simple filter is selected (otherwise the normal filter).</param>
    /// <param name="sharpness">The sharpness level (0 to 7).</param>
    public static void ApplyRow(Vp8Planes planes, ReadOnlySpan<byte> rowInfo, int mbY, int lumaOrigin, bool simple, int sharpness)
        => ApplyRow(planes, rowInfo, mbY, lumaOrigin, simple, sharpness, Vector128.IsHardwareAccelerated);

    private static void Apply(Vp8Planes planes, ReadOnlySpan<byte> filterInfo, int mbWidth, int mbHeight, bool simple, int sharpness, bool useVectors)
    {
        for (var mbY = 0; mbY < mbHeight; mbY++)
        {
            ApplyRow(planes, filterInfo.Slice(mbY * mbWidth, mbWidth), mbY, lumaOrigin: 0, simple, sharpness, useVectors);
        }
    }

    private static void ApplyRow(Vp8Planes planes, ReadOnlySpan<byte> rowInfo, int mbY, int lumaOrigin, bool simple, int sharpness, bool useVectors)
    {
        var y = planes.Y;
        var u = planes.U;
        var v = planes.V;
        var yStride = planes.YStride;
        var uvStride = planes.UVStride;
        var yRow = ((mbY * 16) - lumaOrigin) * yStride;
        var uvRow = ((mbY * 8) - (lumaOrigin / 2)) * uvStride;
        for (var mbX = 0; mbX < rowInfo.Length; mbX++)
        {
            var info = rowInfo[mbX];
            var level = info & 0x3F;
            if (level == 0)
                continue;

            var inner = (info & 0x80) != 0;
            var interior = GetInteriorLimit(level, sharpness);
            var edgeLimit = ((level + 2) * 2) + interior;
            var subLimit = (level * 2) + interior;
            var hevThreshold = level >= 40 ? 2 : level >= 15 ? 1 : 0;
            var yOffset = yRow + (mbX * 16);
            var uvOffset = uvRow + (mbX * 8);
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

            if (useVectors)
            {
                // Each plane gets its edges in the order of the scalar filter; Cb and Cr share the vectors (8 lanes each)
                if (mbX > 0)
                {
                    LumaEdge(y, yOffset, yStride, vertical: true, edgeLimit, interior, hevThreshold, macroblock: true);
                    ChromaEdge(u, v, uvOffset, uvStride, vertical: true, edgeLimit, interior, hevThreshold, macroblock: true);
                }

                if (inner)
                {
                    LumaEdge(y, yOffset + 4, yStride, vertical: true, subLimit, interior, hevThreshold, macroblock: false);
                    LumaEdge(y, yOffset + 8, yStride, vertical: true, subLimit, interior, hevThreshold, macroblock: false);
                    LumaEdge(y, yOffset + 12, yStride, vertical: true, subLimit, interior, hevThreshold, macroblock: false);
                    ChromaEdge(u, v, uvOffset + 4, uvStride, vertical: true, subLimit, interior, hevThreshold, macroblock: false);
                }

                if (mbY > 0)
                {
                    LumaEdge(y, yOffset, yStride, vertical: false, edgeLimit, interior, hevThreshold, macroblock: true);
                    ChromaEdge(u, v, uvOffset, uvStride, vertical: false, edgeLimit, interior, hevThreshold, macroblock: true);
                }

                if (inner)
                {
                    LumaEdge(y, yOffset + (4 * yStride), yStride, vertical: false, subLimit, interior, hevThreshold, macroblock: false);
                    LumaEdge(y, yOffset + (8 * yStride), yStride, vertical: false, subLimit, interior, hevThreshold, macroblock: false);
                    LumaEdge(y, yOffset + (12 * yStride), yStride, vertical: false, subLimit, interior, hevThreshold, macroblock: false);
                    ChromaEdge(u, v, uvOffset + (4 * uvStride), uvStride, vertical: false, subLimit, interior, hevThreshold, macroblock: false);
                }

                continue;
            }

            if (mbX > 0)
            {
                Edge(y, yOffset, 1, yStride, 16, edgeLimit, interior, hevThreshold, macroblock: true);
                Edge(u, uvOffset, 1, uvStride, 8, edgeLimit, interior, hevThreshold, macroblock: true);
                Edge(v, uvOffset, 1, uvStride, 8, edgeLimit, interior, hevThreshold, macroblock: true);
            }

            if (inner)
            {
                Edge(y, yOffset + 4, 1, yStride, 16, subLimit, interior, hevThreshold, macroblock: false);
                Edge(y, yOffset + 8, 1, yStride, 16, subLimit, interior, hevThreshold, macroblock: false);
                Edge(y, yOffset + 12, 1, yStride, 16, subLimit, interior, hevThreshold, macroblock: false);
                Edge(u, uvOffset + 4, 1, uvStride, 8, subLimit, interior, hevThreshold, macroblock: false);
                Edge(v, uvOffset + 4, 1, uvStride, 8, subLimit, interior, hevThreshold, macroblock: false);
            }

            if (mbY > 0)
            {
                Edge(y, yOffset, yStride, 1, 16, edgeLimit, interior, hevThreshold, macroblock: true);
                Edge(u, uvOffset, uvStride, 1, 8, edgeLimit, interior, hevThreshold, macroblock: true);
                Edge(v, uvOffset, uvStride, 1, 8, edgeLimit, interior, hevThreshold, macroblock: true);
            }

            if (inner)
            {
                Edge(y, yOffset + (4 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold, macroblock: false);
                Edge(y, yOffset + (8 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold, macroblock: false);
                Edge(y, yOffset + (12 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold, macroblock: false);
                Edge(u, uvOffset + (4 * uvStride), uvStride, 1, 8, subLimit, interior, hevThreshold, macroblock: false);
                Edge(v, uvOffset + (4 * uvStride), uvStride, 1, 8, subLimit, interior, hevThreshold, macroblock: false);
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

    /// <summary>Filters a luma edge of the normal filter: 16 positions, one per byte lane.</summary>
    private static void LumaEdge(Span<byte> plane, int offset, int stride, bool vertical, int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        Vector128<byte> p3, p2, p1, p0, q0, q1, q2, q3;
        if (!vertical)
        {
            ref var origin = ref GetEdgeOrigin(plane, offset, stride, 1, 16, 4);
            p3 = LoadRow(ref origin, -4 * stride);
            p2 = LoadRow(ref origin, -3 * stride);
            p1 = LoadRow(ref origin, -2 * stride);
            p0 = LoadRow(ref origin, -stride);
            q0 = LoadRow(ref origin, 0);
            q1 = LoadRow(ref origin, stride);
            q2 = LoadRow(ref origin, 2 * stride);
            q3 = LoadRow(ref origin, 3 * stride);
            if (!FilterLanes(p3, ref p2, ref p1, ref p0, ref q0, ref q1, ref q2, q3, edgeLimit, interior, hevThreshold, macroblock))
                return;

            StoreRow(p2, ref origin, -3 * stride);
            StoreRow(p1, ref origin, -2 * stride);
            StoreRow(p0, ref origin, -stride);
            StoreRow(q0, ref origin, 0);
            StoreRow(q1, ref origin, stride);
            StoreRow(q2, ref origin, 2 * stride);
            return;
        }

        // Rows 0-7 of the edge in the low halves, rows 8-15 in the high halves
        ref var left = ref unsafe(Unsafe.Add(ref GetEdgeOrigin(plane, offset, 1, stride, 16, 4), -4));
        ref var lower = ref unsafe(Unsafe.Add(ref left, 8 * stride));
        LoadColumns(ref left, ref lower, stride, out p3, out p2, out p1, out p0, out q0, out q1, out q2, out q3);
        if (FilterLanes(p3, ref p2, ref p1, ref p0, ref q0, ref q1, ref q2, q3, edgeLimit, interior, hevThreshold, macroblock))
        {
            StoreColumns(ref left, ref lower, stride, p3, p2, p1, p0, q0, q1, q2, q3);
        }
    }

    /// <summary>Filters the same edge of the Cb and Cr planes with the normal filter: 8 positions each, Cb in the low byte lanes and Cr in the high ones.</summary>
    private static void ChromaEdge(Span<byte> u, Span<byte> v, int offset, int stride, bool vertical, int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        Vector128<byte> p3, p2, p1, p0, q0, q1, q2, q3;
        if (!vertical)
        {
            ref var uOrigin = ref GetEdgeOrigin(u, offset, stride, 1, 8, 4);
            ref var vOrigin = ref GetEdgeOrigin(v, offset, stride, 1, 8, 4);
            p3 = LoadPair(ref uOrigin, ref vOrigin, -4 * stride);
            p2 = LoadPair(ref uOrigin, ref vOrigin, -3 * stride);
            p1 = LoadPair(ref uOrigin, ref vOrigin, -2 * stride);
            p0 = LoadPair(ref uOrigin, ref vOrigin, -stride);
            q0 = LoadPair(ref uOrigin, ref vOrigin, 0);
            q1 = LoadPair(ref uOrigin, ref vOrigin, stride);
            q2 = LoadPair(ref uOrigin, ref vOrigin, 2 * stride);
            q3 = LoadPair(ref uOrigin, ref vOrigin, 3 * stride);
            if (!FilterLanes(p3, ref p2, ref p1, ref p0, ref q0, ref q1, ref q2, q3, edgeLimit, interior, hevThreshold, macroblock))
                return;

            StorePair(p2, ref uOrigin, ref vOrigin, -3 * stride);
            StorePair(p1, ref uOrigin, ref vOrigin, -2 * stride);
            StorePair(p0, ref uOrigin, ref vOrigin, -stride);
            StorePair(q0, ref uOrigin, ref vOrigin, 0);
            StorePair(q1, ref uOrigin, ref vOrigin, stride);
            StorePair(q2, ref uOrigin, ref vOrigin, 2 * stride);
            return;
        }

        ref var uLeft = ref unsafe(Unsafe.Add(ref GetEdgeOrigin(u, offset, 1, stride, 8, 4), -4));
        ref var vLeft = ref unsafe(Unsafe.Add(ref GetEdgeOrigin(v, offset, 1, stride, 8, 4), -4));
        LoadColumns(ref uLeft, ref vLeft, stride, out p3, out p2, out p1, out p0, out q0, out q1, out q2, out q3);
        if (FilterLanes(p3, ref p2, ref p1, ref p0, ref q0, ref q1, ref q2, q3, edgeLimit, interior, hevThreshold, macroblock))
        {
            StoreColumns(ref uLeft, ref vLeft, stride, p3, p2, p1, p0, q0, q1, q2, q3);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> LoadRow(ref byte origin, int offset) => unsafe(Vector128.LoadUnsafe(ref Unsafe.Add(ref origin, offset)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreRow(Vector128<byte> value, ref byte origin, int offset)
    {
        unsafe { value.StoreUnsafe(ref Unsafe.Add(ref origin, offset)); }
    }

    // The 8 bytes at offset of the first plane (low half) and of the second one (high half)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> LoadPair(ref byte first, ref byte second, int offset)
        => Vector128.Create(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref first, offset))), unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref second, offset)))).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StorePair(Vector128<byte> value, ref byte first, ref byte second, int offset)
    {
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, offset), value.AsUInt64().ToScalar()); }
        unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref second, offset), value.AsUInt64().GetElement(1)); }
    }

    /// <summary>
    /// Loads 8 rows of 8 bytes at <paramref name="first"/> and 8 rows at <paramref name="second"/> and transposes them: column
    /// k of the 16 rows is vector k (the rows of <paramref name="first"/> in the low byte lanes).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LoadColumns(ref byte first, ref byte second, int stride, out Vector128<byte> c0, out Vector128<byte> c1, out Vector128<byte> c2, out Vector128<byte> c3, out Vector128<byte> c4, out Vector128<byte> c5, out Vector128<byte> c6, out Vector128<byte> c7)
    {
        var r0 = LoadPair(ref first, ref second, 0);
        var r1 = LoadPair(ref first, ref second, stride);
        var r2 = LoadPair(ref first, ref second, 2 * stride);
        var r3 = LoadPair(ref first, ref second, 3 * stride);
        var r4 = LoadPair(ref first, ref second, 4 * stride);
        var r5 = LoadPair(ref first, ref second, 5 * stride);
        var r6 = LoadPair(ref first, ref second, 6 * stride);
        var r7 = LoadPair(ref first, ref second, 7 * stride);
        if (!CanInterleave)
        {
            var w0 = r0.AsUInt64();
            var w1 = r1.AsUInt64();
            var w2 = r2.AsUInt64();
            var w3 = r3.AsUInt64();
            var w4 = r4.AsUInt64();
            var w5 = r5.AsUInt64();
            var w6 = r6.AsUInt64();
            var w7 = r7.AsUInt64();
            Transpose8x8(ref w0, ref w1, ref w2, ref w3, ref w4, ref w5, ref w6, ref w7);
            (c0, c1, c2, c3, c4, c5, c6, c7) = (w0.AsByte(), w1.AsByte(), w2.AsByte(), w3.AsByte(), w4.AsByte(), w5.AsByte(), w6.AsByte(), w7.AsByte());
            return;
        }

        // Bytes of the rows (0, 1), (2, 3), ... interleaved, then pairs of bytes, then groups of 4: the 8 rows of a column in
        // each 64-bit half (the two halves of each row vector stay apart: rows of the first block and of the second)
        var t0 = Interleave8Low(r0, r1);
        var t1 = Interleave8High(r0, r1);
        var t2 = Interleave8Low(r2, r3);
        var t3 = Interleave8High(r2, r3);
        var t4 = Interleave8Low(r4, r5);
        var t5 = Interleave8High(r4, r5);
        var t6 = Interleave8Low(r6, r7);
        var t7 = Interleave8High(r6, r7);
        var u0 = Interleave16Low(t0, t2);
        var u1 = Interleave16High(t0, t2);
        var u2 = Interleave16Low(t4, t6);
        var u3 = Interleave16High(t4, t6);
        var u4 = Interleave16Low(t1, t3);
        var u5 = Interleave16High(t1, t3);
        var u6 = Interleave16Low(t5, t7);
        var u7 = Interleave16High(t5, t7);
        var v0 = Interleave32Low(u0, u2);
        var v1 = Interleave32High(u0, u2);
        var v2 = Interleave32Low(u1, u3);
        var v3 = Interleave32High(u1, u3);
        var v4 = Interleave32Low(u4, u6);
        var v5 = Interleave32High(u4, u6);
        var v6 = Interleave32Low(u5, u7);
        var v7 = Interleave32High(u5, u7);
        c0 = Interleave64Low(v0, v4);
        c1 = Interleave64High(v0, v4);
        c2 = Interleave64Low(v1, v5);
        c3 = Interleave64High(v1, v5);
        c4 = Interleave64Low(v2, v6);
        c5 = Interleave64High(v2, v6);
        c6 = Interleave64Low(v3, v7);
        c7 = Interleave64High(v3, v7);
    }

    /// <summary>The inverse of <see cref="LoadColumns"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreColumns(ref byte first, ref byte second, int stride, Vector128<byte> c0, Vector128<byte> c1, Vector128<byte> c2, Vector128<byte> c3, Vector128<byte> c4, Vector128<byte> c5, Vector128<byte> c6, Vector128<byte> c7)
    {
        Vector128<byte> r0, r1, r2, r3, r4, r5, r6, r7;
        if (!CanInterleave)
        {
            var w0 = c0.AsUInt64();
            var w1 = c1.AsUInt64();
            var w2 = c2.AsUInt64();
            var w3 = c3.AsUInt64();
            var w4 = c4.AsUInt64();
            var w5 = c5.AsUInt64();
            var w6 = c6.AsUInt64();
            var w7 = c7.AsUInt64();
            Transpose8x8(ref w0, ref w1, ref w2, ref w3, ref w4, ref w5, ref w6, ref w7);
            (r0, r1, r2, r3, r4, r5, r6, r7) = (w0.AsByte(), w1.AsByte(), w2.AsByte(), w3.AsByte(), w4.AsByte(), w5.AsByte(), w6.AsByte(), w7.AsByte());
        }
        else
        {
            // Columns (0, 1), (2, 3), ... interleaved, then pairs, then groups of 4 bytes: rows (r, r + 8) in the 64-bit halves
            var t0 = Interleave8Low(c0, c1);
            var t1 = Interleave8High(c0, c1);
            var t2 = Interleave8Low(c2, c3);
            var t3 = Interleave8High(c2, c3);
            var t4 = Interleave8Low(c4, c5);
            var t5 = Interleave8High(c4, c5);
            var t6 = Interleave8Low(c6, c7);
            var t7 = Interleave8High(c6, c7);
            var u0 = Interleave16Low(t0, t2);
            var u1 = Interleave16High(t0, t2);
            var u2 = Interleave16Low(t4, t6);
            var u3 = Interleave16High(t4, t6);
            var u4 = Interleave16Low(t1, t3);
            var u5 = Interleave16High(t1, t3);
            var u6 = Interleave16Low(t5, t7);
            var u7 = Interleave16High(t5, t7);

            // Rows (0, 1), (2, 3), (4, 5), (6, 7) of the first block, then of the second, in the 64-bit halves
            var rows01 = Interleave32Low(u0, u2);
            var rows23 = Interleave32High(u0, u2);
            var rows45 = Interleave32Low(u1, u3);
            var rows67 = Interleave32High(u1, u3);
            var rows89 = Interleave32Low(u4, u6);
            var rows1011 = Interleave32High(u4, u6);
            var rows1213 = Interleave32Low(u5, u7);
            var rows1415 = Interleave32High(u5, u7);
            r0 = Interleave64Low(rows01, rows89);
            r1 = Interleave64High(rows01, rows89);
            r2 = Interleave64Low(rows23, rows1011);
            r3 = Interleave64High(rows23, rows1011);
            r4 = Interleave64Low(rows45, rows1213);
            r5 = Interleave64High(rows45, rows1213);
            r6 = Interleave64Low(rows67, rows1415);
            r7 = Interleave64High(rows67, rows1415);
        }

        StorePair(r0, ref first, ref second, 0);
        StorePair(r1, ref first, ref second, stride);
        StorePair(r2, ref first, ref second, 2 * stride);
        StorePair(r3, ref first, ref second, 3 * stride);
        StorePair(r4, ref first, ref second, 4 * stride);
        StorePair(r5, ref first, ref second, 5 * stride);
        StorePair(r6, ref first, ref second, 6 * stride);
        StorePair(r7, ref first, ref second, 7 * stride);
    }

    // Two-input element interleaves (x64 PUNPCKL/PUNPCKH, Arm64 ZIP1/ZIP2); the transposes fall back to Transpose8x8 elsewhere
    private static bool CanInterleave => Sse2.IsSupported || AdvSimd.Arm64.IsSupported;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave8Low(Vector128<byte> a, Vector128<byte> b) => Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.ZipLow(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave8High(Vector128<byte> a, Vector128<byte> b) => Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.ZipHigh(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave16Low(Vector128<byte> a, Vector128<byte> b) => (Sse2.IsSupported ? Sse2.UnpackLow(a.AsUInt16(), b.AsUInt16()) : AdvSimd.Arm64.ZipLow(a.AsUInt16(), b.AsUInt16())).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave16High(Vector128<byte> a, Vector128<byte> b) => (Sse2.IsSupported ? Sse2.UnpackHigh(a.AsUInt16(), b.AsUInt16()) : AdvSimd.Arm64.ZipHigh(a.AsUInt16(), b.AsUInt16())).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave32Low(Vector128<byte> a, Vector128<byte> b) => (Sse2.IsSupported ? Sse2.UnpackLow(a.AsUInt32(), b.AsUInt32()) : AdvSimd.Arm64.ZipLow(a.AsUInt32(), b.AsUInt32())).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave32High(Vector128<byte> a, Vector128<byte> b) => (Sse2.IsSupported ? Sse2.UnpackHigh(a.AsUInt32(), b.AsUInt32()) : AdvSimd.Arm64.ZipHigh(a.AsUInt32(), b.AsUInt32())).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave64Low(Vector128<byte> a, Vector128<byte> b) => (Sse2.IsSupported ? Sse2.UnpackLow(a.AsUInt64(), b.AsUInt64()) : AdvSimd.Arm64.ZipLow(a.AsUInt64(), b.AsUInt64())).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Interleave64High(Vector128<byte> a, Vector128<byte> b) => (Sse2.IsSupported ? Sse2.UnpackHigh(a.AsUInt64(), b.AsUInt64()) : AdvSimd.Arm64.ZipHigh(a.AsUInt64(), b.AsUInt64())).AsByte();

    // An edge of the normal filter, one position at a time: a macroblock edge or an inner (sub-block) edge
    private static void Edge(Span<byte> plane, int offset, int step, int along, int count, int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        if (macroblock)
        {
            MacroblockEdge(plane, offset, step, along, count, edgeLimit, interior, hevThreshold);
        }
        else
        {
            SubBlockEdge(plane, offset, step, along, count, edgeLimit, interior, hevThreshold);
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
    /// <returns><see langword="false"/> when no lane is filtered (the vectors are unchanged).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool FilterLanes(Vector128<byte> p3, ref Vector128<byte> p2, ref Vector128<byte> p1, ref Vector128<byte> p0, ref Vector128<byte> q0, ref Vector128<byte> q1, ref Vector128<byte> q2, Vector128<byte> q3, int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        var interiorLimit = Vector128.Create((byte)interior);
        var edgeSum = Vector128.AddSaturate(Vector128.AddSaturate(Difference(p0, q0), Difference(p0, q0)), Vector128.ShiftRightLogical(Difference(p1, q1), 1));
        var filter = Vector128.LessThanOrEqual(edgeSum, Vector128.Create((byte)edgeLimit))
            & Vector128.LessThanOrEqual(Difference(p3, p2), interiorLimit) & Vector128.LessThanOrEqual(Difference(p2, p1), interiorLimit)
            & Vector128.LessThanOrEqual(Difference(p1, p0), interiorLimit) & Vector128.LessThanOrEqual(Difference(q3, q2), interiorLimit)
            & Vector128.LessThanOrEqual(Difference(q2, q1), interiorLimit) & Vector128.LessThanOrEqual(Difference(q1, q0), interiorLimit);
        if (filter == Vector128<byte>.Zero)
            return false;

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

        return true;

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
