using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
                    MacroblockEdge(y, yOffset, 1, yStride, 16, edgeLimit, interior, hevThreshold);
                    MacroblockEdge(u, uvOffset, 1, uvStride, 8, edgeLimit, interior, hevThreshold);
                    MacroblockEdge(v, uvOffset, 1, uvStride, 8, edgeLimit, interior, hevThreshold);
                }

                if (inner)
                {
                    SubBlockEdge(y, yOffset + 4, 1, yStride, 16, subLimit, interior, hevThreshold);
                    SubBlockEdge(y, yOffset + 8, 1, yStride, 16, subLimit, interior, hevThreshold);
                    SubBlockEdge(y, yOffset + 12, 1, yStride, 16, subLimit, interior, hevThreshold);
                    SubBlockEdge(u, uvOffset + 4, 1, uvStride, 8, subLimit, interior, hevThreshold);
                    SubBlockEdge(v, uvOffset + 4, 1, uvStride, 8, subLimit, interior, hevThreshold);
                }

                if (mbY > 0)
                {
                    MacroblockEdge(y, yOffset, yStride, 1, 16, edgeLimit, interior, hevThreshold);
                    MacroblockEdge(u, uvOffset, uvStride, 1, 8, edgeLimit, interior, hevThreshold);
                    MacroblockEdge(v, uvOffset, uvStride, 1, 8, edgeLimit, interior, hevThreshold);
                }

                if (inner)
                {
                    SubBlockEdge(y, yOffset + (4 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold);
                    SubBlockEdge(y, yOffset + (8 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold);
                    SubBlockEdge(y, yOffset + (12 * yStride), yStride, 1, 16, subLimit, interior, hevThreshold);
                    SubBlockEdge(u, uvOffset + (4 * uvStride), uvStride, 1, 8, subLimit, interior, hevThreshold);
                    SubBlockEdge(v, uvOffset + (4 * uvStride), uvStride, 1, 8, subLimit, interior, hevThreshold);
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
