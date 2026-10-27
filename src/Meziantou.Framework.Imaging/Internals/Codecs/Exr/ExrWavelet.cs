using System.Runtime.CompilerServices;

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
/// </remarks>
internal static class ExrWavelet
{
    /// <summary>Applies the forward transform to a plane.</summary>
    public static void Encode(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits)
    {
        var n = Math.Min(nx, ny);
        for (var p = 1; 2 * p <= n; p *= 2)
        {
            Level(words, offset, nx, xStride, ny, yStride, p, fourteenBits, forward: true);
        }
    }

    /// <summary>Applies the inverse transform to a plane.</summary>
    public static void Decode(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, bool fourteenBits)
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
            Level(words, offset, nx, xStride, ny, yStride, p, fourteenBits, forward: false);
        }
    }

    private static void Level(Span<ushort> words, int offset, int nx, int xStride, int ny, int yStride, int p, bool fourteenBits, bool forward)
    {
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
            for (var x = 0; x + step <= nx; x += step)
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
