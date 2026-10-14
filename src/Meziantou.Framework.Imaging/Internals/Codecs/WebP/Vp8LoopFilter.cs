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
    // a horizontal edge, columns for a vertical one (two 8 x 8 transpositions of the rows of 8 pixels across the edge)
    private static void EdgeVector(Span<byte> plane, int offset, int step, int along, int count, int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        _ = GetEdgeOrigin(plane, offset, step, along, count, 4);
        Span<ulong> columns = stackalloc ulong[16];
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
            // columns[ k ] holds the pixel k of the rows 0 to 7, columns[ 8 + k ] the one of the rows 8 to 15
            for (var i = 0; i < count; i++)
            {
                columns[i] = BinaryPrimitives.ReadUInt64LittleEndian(plane.Slice(offset - 4 + (i * along), 8));
            }

            Transpose8x8(columns[..8]);
            Transpose8x8(columns[8..]);
            p3 = Vector128.Create(columns[0], columns[8]).AsByte();
            p2 = Vector128.Create(columns[1], columns[9]).AsByte();
            p1 = Vector128.Create(columns[2], columns[10]).AsByte();
            p0 = Vector128.Create(columns[3], columns[11]).AsByte();
            q0 = Vector128.Create(columns[4], columns[12]).AsByte();
            q1 = Vector128.Create(columns[5], columns[13]).AsByte();
            q2 = Vector128.Create(columns[6], columns[14]).AsByte();
            q3 = Vector128.Create(columns[7], columns[15]).AsByte();
        }

        var limits = new FilterLimits(edgeLimit, interior, hevThreshold, macroblock);
        var p2Low = Widen(p2, upper: false);
        var p1Low = Widen(p1, upper: false);
        var p0Low = Widen(p0, upper: false);
        var q0Low = Widen(q0, upper: false);
        var q1Low = Widen(q1, upper: false);
        var q2Low = Widen(q2, upper: false);
        FilterLanes(Widen(p3, upper: false), ref p2Low, ref p1Low, ref p0Low, ref q0Low, ref q1Low, ref q2Low, Widen(q3, upper: false), limits);
        var p2High = Widen(p2, upper: true);
        var p1High = Widen(p1, upper: true);
        var p0High = Widen(p0, upper: true);
        var q0High = Widen(q0, upper: true);
        var q1High = Widen(q1, upper: true);
        var q2High = Widen(q2, upper: true);
        if (count > 8)
        {
            FilterLanes(Widen(p3, upper: true), ref p2High, ref p1High, ref p0High, ref q0High, ref q1High, ref q2High, Widen(q3, upper: true), limits);
        }

        p2 = Narrow(p2Low, p2High);
        p1 = Narrow(p1Low, p1High);
        p0 = Narrow(p0Low, p0High);
        q0 = Narrow(q0Low, q0High);
        q1 = Narrow(q1Low, q1High);
        q2 = Narrow(q2Low, q2High);
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
        Split(p2, columns, 1);
        Split(p1, columns, 2);
        Split(p0, columns, 3);
        Split(q0, columns, 4);
        Split(q1, columns, 5);
        Split(q2, columns, 6);
        Transpose8x8(columns[..8]);
        Transpose8x8(columns[8..]);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(plane.Slice(offset - 4 + (i * along), 8), columns[i]);
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

        static void Split(Vector128<byte> value, Span<ulong> columns, int k)
        {
            columns[k] = value.AsUInt64().GetElement(0);
            columns[8 + k] = value.AsUInt64().GetElement(1);
        }

        static Vector128<short> Widen(Vector128<byte> value, bool upper) => (upper ? Vector128.WidenUpper(value) : Vector128.WidenLower(value)).AsInt16();

        static Vector128<byte> Narrow(Vector128<short> low, Vector128<short> high) => Vector128.Narrow(low.AsUInt16(), high.AsUInt16());
    }

    // Transposes 8 x 8 bytes, a row per value (its byte k is the column k): exchanges the bytes, then the pairs of bytes,
    // then the groups of 4 bytes, between the rows and the columns
    private static void Transpose8x8(Span<ulong> rows)
    {
        const ulong Mask1 = 0x00FF00FF00FF00FF;
        const ulong Mask2 = 0x0000FFFF0000FFFF;
        const ulong Mask4 = 0x00000000FFFFFFFF;
        for (var i = 0; i < 8; i += 2)
        {
            var a = rows[i];
            var b = rows[i + 1];
            rows[i] = (a & Mask1) | ((b & Mask1) << 8);
            rows[i + 1] = ((a >> 8) & Mask1) | (b & ~Mask1);
        }

        for (var i = 0; i < 8; i += 4)
        {
            for (var j = i; j < i + 2; j++)
            {
                var a = rows[j];
                var b = rows[j + 2];
                rows[j] = (a & Mask2) | ((b & Mask2) << 16);
                rows[j + 2] = ((a >> 16) & Mask2) | (b & ~Mask2);
            }
        }

        for (var j = 0; j < 4; j++)
        {
            var a = rows[j];
            var b = rows[j + 4];
            rows[j] = (a & Mask4) | ((b & Mask4) << 32);
            rows[j + 4] = ((a >> 32) & Mask4) | (b & ~Mask4);
        }
    }

    // The filter of MacroblockEdge or SubBlockEdge for 8 positions: the pixels are 0 to 255 in 16-bit lanes, and every
    // intermediate value of the scalar filter fits them (at most 27 * 127 + 63)
    private static void FilterLanes(Vector128<short> p3, ref Vector128<short> p2, ref Vector128<short> p1, ref Vector128<short> p0, ref Vector128<short> q0, ref Vector128<short> q1, ref Vector128<short> q2, Vector128<short> q3, in FilterLimits limits)
    {
        var interior = limits.Interior;
        var filter = Vector128.LessThanOrEqual((Vector128.Abs(p0 - q0) * 2) + (Vector128.Abs(p1 - q1) >> 1), limits.EdgeLimit)
            & Vector128.LessThanOrEqual(Vector128.Abs(p3 - p2), interior) & Vector128.LessThanOrEqual(Vector128.Abs(p2 - p1), interior)
            & Vector128.LessThanOrEqual(Vector128.Abs(p1 - p0), interior) & Vector128.LessThanOrEqual(Vector128.Abs(q3 - q2), interior)
            & Vector128.LessThanOrEqual(Vector128.Abs(q2 - q1), interior) & Vector128.LessThanOrEqual(Vector128.Abs(q1 - q0), interior);
        if (filter == Vector128<short>.Zero)
            return;

        var hev = Vector128.GreaterThan(Vector128.Abs(p1 - p0), limits.HevThreshold) | Vector128.GreaterThan(Vector128.Abs(q1 - q0), limits.HevThreshold);
        var offset = Vector128.Create((short)128);
        var sp2 = p2 - offset;
        var sp1 = p1 - offset;
        var sp0 = p0 - offset;
        var sq0 = q0 - offset;
        var sq1 = q1 - offset;
        var sq2 = q2 - offset;
        var three = Vector128.Create((short)3);
        var four = Vector128.Create((short)4);
        if (limits.Macroblock)
        {
            // The filter value of CommonAdjust with the outer taps, which is also w of the 3 taps of a flat edge
            var w = Clamp128(Clamp128(sp1 - sq1) + (Vector128.Create((short)3) * (sq0 - sp0)));
            var b = Clamp128(w + three) >> 3;
            var a = Clamp128(w + four) >> 3;
            var q0Hev = ToUnsigned(sq0 - a);
            var p0Hev = ToUnsigned(sp0 + b);
            var round = Vector128.Create((short)63);
            a = Clamp128(((Vector128.Create((short)27) * w) + round) >> 7);
            var q0Flat = ToUnsigned(sq0 - a);
            var p0Flat = ToUnsigned(sp0 + a);
            a = Clamp128(((Vector128.Create((short)18) * w) + round) >> 7);
            var q1Flat = ToUnsigned(sq1 - a);
            var p1Flat = ToUnsigned(sp1 + a);
            a = Clamp128(((Vector128.Create((short)9) * w) + round) >> 7);
            var q2Flat = ToUnsigned(sq2 - a);
            var p2Flat = ToUnsigned(sp2 + a);
            var flat = filter & ~hev;
            q0 = Vector128.ConditionalSelect(filter, Vector128.ConditionalSelect(hev, q0Hev, q0Flat), q0);
            p0 = Vector128.ConditionalSelect(filter, Vector128.ConditionalSelect(hev, p0Hev, p0Flat), p0);
            q1 = Vector128.ConditionalSelect(flat, q1Flat, q1);
            p1 = Vector128.ConditionalSelect(flat, p1Flat, p1);
            q2 = Vector128.ConditionalSelect(flat, q2Flat, q2);
            p2 = Vector128.ConditionalSelect(flat, p2Flat, p2);
        }
        else
        {
            // CommonAdjust with the outer taps where the edge variance is high, then the adjustment of p1 and q1 elsewhere
            var value = Clamp128((Clamp128(sp1 - sq1) & hev) + (three * (sq0 - sp0)));
            var b = Clamp128(value + three) >> 3;
            var a = Clamp128(value + four) >> 3;
            q0 = Vector128.ConditionalSelect(filter, ToUnsigned(sq0 - a), q0);
            p0 = Vector128.ConditionalSelect(filter, ToUnsigned(sp0 + b), p0);
            a = (a + Vector128<short>.One) >> 1;
            var outer = filter & ~hev;
            q1 = Vector128.ConditionalSelect(outer, ToUnsigned(sq1 - a), q1);
            p1 = Vector128.ConditionalSelect(outer, ToUnsigned(sp1 + a), p1);
        }

        static Vector128<short> Clamp128(Vector128<short> value) => Vector128.Min(Vector128.Max(value, Vector128.Create((short)-128)), Vector128.Create((short)127));

        static Vector128<short> ToUnsigned(Vector128<short> value) => Clamp128(value) + Vector128.Create((short)128);
    }

    private readonly struct FilterLimits(int edgeLimit, int interior, int hevThreshold, bool macroblock)
    {
        public Vector128<short> EdgeLimit { get; } = Vector128.Create((short)edgeLimit);

        public Vector128<short> Interior { get; } = Vector128.Create((short)interior);

        public Vector128<short> HevThreshold { get; } = Vector128.Create((short)hevThreshold);

        public bool Macroblock { get; } = macroblock;
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
