using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The two-dimensional Haar-like wavelet of the PIZ compression (compression notes, section 5.3), applied to one plane of
/// 16-bit words: a 14-bit variant (signed pairs, every value truncated to 16 bits) when the range-compressed values are
/// below 2^14, and a 16-bit variant (modular arithmetic) otherwise.
/// </summary>
/// <remarks>
/// A plane is <c>nx</c> by <c>ny</c> words; the word of (x, y) is at <c>offset + x * xStride + y * yStride</c>. The level
/// steps are the powers of two p with 2p at most <c>min(nx, ny)</c>, increasing when encoding and decreasing when
/// decoding. At each level the quads of 2p by 2p positions are transformed, then the column at <c>2p floor(nx / 2p)</c>
/// when the bit p of nx is set, and the row at <c>2p floor(ny / 2p)</c> when the bit p of ny is set (section 5.3.3).
/// <para>
/// The quads of the first level of a plane of contiguous words (p = 1, three quarters of the quads) are transformed 8 at a
/// time in vectors (<see cref="LevelOneVector"/>), with the operations of <see cref="Forward"/> and <see cref="Inverse"/>
/// on 16-bit lanes (their results are those of the integer forms modulo 2^16); <see cref="EncodeScalar"/> and
/// <see cref="DecodeScalar"/> are the reference.
/// </para>
/// </remarks>
internal static class ExrWavelet
{
    private static bool UseVectors => BitConverter.IsLittleEndian && (AdvSimd.Arm64.IsSupported || Sse41.IsSupported);

    /// <summary>Applies the forward transform to a plane.</summary>
    public static void Encode(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits)
        => Encode(words, offset, nx, xStride, ny, yStride, fourteenBits, UseVectors);

    /// <summary>The scalar reference of <see cref="Encode(Span{ushort}, int, int, int, int, int, bool)"/>.</summary>
    internal static void EncodeScalar(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits)
        => Encode(words, offset, nx, xStride, ny, yStride, fourteenBits, useVectors: false);

    /// <summary>Applies the inverse transform to a plane.</summary>
    public static void Decode(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits)
        => Decode(words, offset, nx, xStride, ny, yStride, fourteenBits, UseVectors);

    /// <summary>The scalar reference of <see cref="Decode(Span{ushort}, int, int, int, int, int, bool)"/>.</summary>
    internal static void DecodeScalar(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits)
        => Decode(words, offset, nx, xStride, ny, yStride, fourteenBits, useVectors: false);

    private static void Encode(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits, bool useVectors)
    {
        var n = Math.Min(nx, ny);
        for (var p = 1; 2 * p <= n; p *= 2)
        {
            Level(words, offset, nx, xStride, ny, yStride, p, fourteenBits, forward: true, useVectors);
        }
    }

    private static void Decode(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits, bool useVectors)
    {
        var n = Math.Min(nx, ny);
        if (n < 2)
            return;

        var p = 1;
        while (4 * p <= n)
        {
            p *= 2;
        }

        for (; p >= 1; p /= 2)
        {
            Level(words, offset, nx, xStride, ny, yStride, p, fourteenBits, forward: false, useVectors);
        }
    }

    private static void Level(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, int p, bool fourteenBits, bool forward, bool useVectors)
    {
        // The quads are independent: the vectors take the first columns of every pair of rows, the loop below the others
        var first = 0;
        if (useVectors && p == 1 && xStride == 1)
        {
            first = LevelOneVector(words, offset, nx, ny, yStride, fourteenBits, forward);
        }

        var step = 2 * p;
        var ox = p * xStride;
        var oy = p * yStride;
        var xEnd = step * (nx / step);
        var yEnd = step * (ny / step);
        var oddColumn = (nx & p) != 0;
        var oddRow = (ny & p) != 0;
        for (var y = 0; y + step <= ny; y += step)
        {
            var row = offset + (y * yStride);
            for (var x = first; x + step <= nx; x += step)
            {
                var i00 = row + (x * xStride);
                Quad(words, i00, i00 + ox, i00 + oy, i00 + ox + oy, fourteenBits, forward);
            }

            if (oddColumn)
            {
                var i = row + (xEnd * xStride);
                Pair(words, i, i + oy, fourteenBits, forward);
            }
        }

        if (oddRow)
        {
            var row = offset + (yEnd * yStride);
            for (var x = 0; x + step <= nx; x += step)
            {
                var i = row + (x * xStride);
                Pair(words, i, i + ox, fourteenBits, forward);
            }
        }
    }

    /// <summary>
    /// Transforms the quads of the first level (p = 1) of a plane of contiguous words whose first columns fill whole vectors,
    /// 8 quads (16 words of two rows) at a time.
    /// </summary>
    /// <returns>The number of columns transformed in each pair of rows.</returns>
    private static int LevelOneVector(Span<ushort> words, int offset, int nx, int ny, int yStride, bool fourteenBits, bool forward)
    {
        var columns = nx & ~(Vector128<ushort>.Count * 2 - 1);
        if (columns == 0)
            return 0;

        _ = words.Slice(offset, ((ny - 1) * yStride) + nx);
        ref var start = ref unsafe(MemoryMarshal.GetReference(words));
        for (var y = 0; y + 2 <= ny; y += 2)
        {
            ref var top = ref unsafe(Unsafe.Add(ref start, offset + (y * yStride)));
            ref var bottom = ref unsafe(Unsafe.Add(ref top, yStride));
            for (nuint x = 0; x < (nuint)columns; x += (nuint)(Vector128<ushort>.Count * 2))
            {
                // The words of the even and the odd columns of the two rows
                Deinterleave(unsafe(Vector128.LoadUnsafe(ref top, x)), unsafe(Vector128.LoadUnsafe(ref top, x + (nuint)Vector128<ushort>.Count)), out var a00, out var a10);
                Deinterleave(unsafe(Vector128.LoadUnsafe(ref bottom, x)), unsafe(Vector128.LoadUnsafe(ref bottom, x + (nuint)Vector128<ushort>.Count)), out var a01, out var a11);
                Vector128<ushort> b00, b10, b01, b11;
                if (forward)
                {
                    var (l1, h1) = ForwardVector(a00, a10, fourteenBits);
                    var (l2, h2) = ForwardVector(a01, a11, fourteenBits);
                    (b00, b01) = ForwardVector(l1, l2, fourteenBits);
                    (b10, b11) = ForwardVector(h1, h2, fourteenBits);
                }
                else
                {
                    var (l1, l2) = InverseVector(a00, a01, fourteenBits);
                    var (h1, h2) = InverseVector(a10, a11, fourteenBits);
                    (b00, b10) = InverseVector(l1, h1, fourteenBits);
                    (b01, b11) = InverseVector(l2, h2, fourteenBits);
                }

                Interleave(b00, b10, out var topLow, out var topHigh);
                Interleave(b01, b11, out var bottomLow, out var bottomHigh);
                unsafe { topLow.StoreUnsafe(ref top, x); }
                unsafe { topHigh.StoreUnsafe(ref top, x + (nuint)Vector128<ushort>.Count); }
                unsafe { bottomLow.StoreUnsafe(ref bottom, x); }
                unsafe { bottomHigh.StoreUnsafe(ref bottom, x + (nuint)Vector128<ushort>.Count); }
            }
        }

        return columns;
    }

    /// <summary><see cref="Forward"/> on 16-bit lanes: the halved sums without overflow, and the differences modulo 2^16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<ushort> Low, Vector128<ushort> High) ForwardVector(Vector128<ushort> a, Vector128<ushort> b, bool fourteenBits)
    {
        var one = Vector128.Create((ushort)1);
        if (fourteenBits)
        {
            var sa = a.AsInt16();
            var sb = b.AsInt16();
            var mean = Vector128.ShiftRightArithmetic(sa, 1) + Vector128.ShiftRightArithmetic(sb, 1) + (sa & sb & one.AsInt16());
            return (mean.AsUInt16(), a - b);
        }

        // a + 32768, and the mean shifted by 32768 when the difference is negative
        var offset = a ^ Vector128.Create((ushort)0x8000);
        var m = Vector128.ShiftRightLogical(offset, 1) + Vector128.ShiftRightLogical(b, 1) + (offset & b & one);
        m ^= Vector128.LessThan(offset, b) & Vector128.Create((ushort)0x8000);
        return (m, offset - b);
    }

    /// <summary><see cref="Inverse"/> on 16-bit lanes, modulo 2^16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<ushort> A, Vector128<ushort> B) InverseVector(Vector128<ushort> low, Vector128<ushort> high, bool fourteenBits)
    {
        if (fourteenBits)
        {
            var h = high.AsInt16();
            var a = low.AsInt16() + (h & Vector128.Create((short)1)) + Vector128.ShiftRightArithmetic(h, 1);
            return (a.AsUInt16(), (a - h).AsUInt16());
        }

        var b = low - Vector128.ShiftRightLogical(high, 1);
        return ((high + b) ^ Vector128.Create((ushort)0x8000), b);
    }

    /// <summary>Separates the words at even and odd positions of 16 consecutive words.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Deinterleave(Vector128<ushort> low, Vector128<ushort> high, out Vector128<ushort> even, out Vector128<ushort> odd)
    {
        if (Sse41.IsSupported)
        {
            var mask = Vector128.Create(0xFFFFu);
            even = Sse41.PackUnsignedSaturate((low.AsUInt32() & mask).AsInt32(), (high.AsUInt32() & mask).AsInt32());
            odd = Sse41.PackUnsignedSaturate(Sse2.ShiftRightLogical(low.AsUInt32(), 16).AsInt32(), Sse2.ShiftRightLogical(high.AsUInt32(), 16).AsInt32());
            return;
        }

        even = AdvSimd.Arm64.UnzipEven(low, high);
        odd = AdvSimd.Arm64.UnzipOdd(low, high);
    }

    /// <summary>Interleaves the words of two vectors: <c>a0 b0 a1 b1 ...</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Interleave(Vector128<ushort> a, Vector128<ushort> b, out Vector128<ushort> low, out Vector128<ushort> high)
    {
        if (Sse2.IsSupported)
        {
            low = Sse2.UnpackLow(a, b);
            high = Sse2.UnpackHigh(a, b);
            return;
        }

        low = AdvSimd.Arm64.ZipLow(a, b);
        high = AdvSimd.Arm64.ZipHigh(a, b);
    }

    private static void Quad(Span<ushort> words, int i00, int i10, int i01, int i11, bool fourteenBits, bool forward)
    {
        if (forward)
        {
            var (l1, h1) = Forward(words[i00], words[i10], fourteenBits);
            var (l2, h2) = Forward(words[i01], words[i11], fourteenBits);
            (words[i00], words[i01]) = Forward(l1, l2, fourteenBits);
            (words[i10], words[i11]) = Forward(h1, h2, fourteenBits);
        }
        else
        {
            var (l1, l2) = Inverse(words[i00], words[i01], fourteenBits);
            var (h1, h2) = Inverse(words[i10], words[i11], fourteenBits);
            (words[i00], words[i10]) = Inverse(l1, h1, fourteenBits);
            (words[i01], words[i11]) = Inverse(l2, h2, fourteenBits);
        }
    }

    private static void Pair(Span<ushort> words, int first, int second, bool fourteenBits, bool forward)
        => (words[first], words[second]) = forward ? Forward(words[first], words[second], fourteenBits) : Inverse(words[first], words[second], fourteenBits);

    /// <summary>The forward pair transform (section 5.3.1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (ushort Low, ushort High) Forward(ushort a, ushort b, bool fourteenBits)
    {
        if (fourteenBits)
        {
            int ai = (short)a;
            int bi = (short)b;
            return ((ushort)((ai + bi) >> 1), (ushort)(ai - bi));
        }

        var ao = (a + 32768) & 0xFFFF;
        var m = (ao + b) >> 1;
        var d = ao - b;
        if (d < 0)
        {
            m = (m + 32768) & 0xFFFF;
        }

        return ((ushort)m, (ushort)d);
    }

    /// <summary>The inverse pair transform (section 5.3.1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (ushort A, ushort B) Inverse(ushort low, ushort high, bool fourteenBits)
    {
        if (fourteenBits)
        {
            int h = (short)high;
            var a = (short)low + (h & 1) + (h >> 1);
            return ((ushort)a, (ushort)(a - h));
        }

        var b = (low - (high >> 1)) & 0xFFFF;
        var aValue = (high + b - 32768) & 0xFFFF;
        return ((ushort)aValue, (ushort)b);
    }
}
