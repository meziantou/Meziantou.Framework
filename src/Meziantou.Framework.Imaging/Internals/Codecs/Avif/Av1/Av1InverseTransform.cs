namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The inverse transform process of AV1 (AV1 specification, section 7.13): the one-dimensional inverse DCT (4 to 64 points),
/// ADST (4 to 16 points), identity (4 to 32 points) and Walsh-Hadamard transforms, and their two-dimensional combination.
/// The transforms are integer-exact: they follow the butterfly steps of the specification, including the intermediate rounding
/// and clamping, which every conforming decoder reproduces bit for bit. Products are computed with 64 bits so that coefficients
/// outside the conformance range give a deterministic result instead of an overflow.
/// </summary>
internal static class Av1InverseTransform
{
    /// <summary>The one-dimensional transform of the rows or of the columns of a transform type.</summary>
    internal enum Kind
    {
        Dct,
        Adst,
        Identity,
    }

    /// <summary>
    /// Transforms the dequantized coefficients of a transform block into its residual (section 7.13.3).
    /// </summary>
    /// <param name="dequant">
    /// <c>Dequant</c>: Min(32, height) rows of Min(32, width) coefficients. The coefficients of a 64-point dimension beyond 32
    /// are zero.
    /// </param>
    /// <param name="residual"><c>Residual</c>: height rows of width samples.</param>
    /// <param name="txSz">The transform size.</param>
    /// <param name="txType">The transform type (<c>PlaneTxType</c>).</param>
    /// <param name="lossless">Whether the block is lossless: the Walsh-Hadamard transform is used.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    public static void Transform(ReadOnlySpan<int> dequant, Span<int> residual, int txSz, int txType, bool lossless, int bitDepth)
    {
        var log2W = Av1Tables.TxWidthLog2[txSz];
        var log2H = Av1Tables.TxHeightLog2[txSz];
        var w = 1 << log2W;
        var h = 1 << log2H;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        var rowShift = lossless ? 0 : Av1Tables.TransformRowShift[txSz];
        var colShift = lossless ? 0 : 4;
        var rowClampRange = bitDepth + 8;
        var colClampRange = Math.Max(bitDepth + 6, 16);
        var colMin = -(1 << (colClampRange - 1));
        var colMax = (1 << (colClampRange - 1)) - 1;
        var isRectangular2To1 = Math.Abs(log2W - log2H) is 1;
        var rowKind = GetRowKind(txType);
        var colKind = GetColumnKind(txType);

        Span<int> t = stackalloc int[Math.Max(w, h)];
        for (var i = 0; i < h; i++)
        {
            var row = residual.Slice(i * w, w);
            if (i >= th)
            {
                // A row of zero coefficients transforms to zeros
                row.Clear();
                continue;
            }

            var line = t[..w];
            dequant.Slice(i * tw, tw).CopyTo(line);
            line[tw..].Clear();
            if (isRectangular2To1)
            {
                for (var j = 0; j < w; j++)
                {
                    line[j] = Round2(line[j] * 2896L, 12);
                }
            }

            if (lossless)
            {
                InverseWalshHadamard(line, 2);
            }
            else
            {
                Apply(rowKind, line, log2W, rowClampRange);
            }

            for (var j = 0; j < w; j++)
            {
                row[j] = Math.Clamp(Round2(line[j], rowShift), colMin, colMax);
            }
        }

        for (var j = 0; j < w; j++)
        {
            var column = t[..h];
            for (var i = 0; i < h; i++)
            {
                column[i] = residual[(i * w) + j];
            }

            if (lossless)
            {
                InverseWalshHadamard(column, 0);
            }
            else
            {
                Apply(colKind, column, log2H, colClampRange);
            }

            for (var i = 0; i < h; i++)
            {
                residual[(i * w) + j] = Round2(column[i], colShift);
            }
        }
    }

    internal static Kind GetRowKind(int txType) => txType switch
    {
        Av1Constants.DctDct or Av1Constants.AdstDct or Av1Constants.FlipadstDct or Av1Constants.HDct => Kind.Dct,
        Av1Constants.DctAdst or Av1Constants.AdstAdst or Av1Constants.DctFlipadst or Av1Constants.FlipadstFlipadst
            or Av1Constants.AdstFlipadst or Av1Constants.FlipadstAdst or Av1Constants.HAdst or Av1Constants.HFlipadst => Kind.Adst,
        _ => Kind.Identity,
    };

    internal static Kind GetColumnKind(int txType) => txType switch
    {
        Av1Constants.DctDct or Av1Constants.DctAdst or Av1Constants.DctFlipadst or Av1Constants.VDct => Kind.Dct,
        Av1Constants.AdstDct or Av1Constants.AdstAdst or Av1Constants.FlipadstDct or Av1Constants.FlipadstFlipadst
            or Av1Constants.AdstFlipadst or Av1Constants.FlipadstAdst or Av1Constants.VAdst or Av1Constants.VFlipadst => Kind.Adst,
        _ => Kind.Identity,
    };

    private static void Apply(Kind kind, Span<int> t, int n, int r)
    {
        switch (kind)
        {
            case Kind.Dct:
                InverseDct(t, n, r);
                break;
            case Kind.Adst:
                InverseAdst(t, n, r);
                break;
            default:
                InverseIdentity(t, n);
                break;
        }
    }

    /// <summary>The inverse DCT of 2^<paramref name="n"/> points, 2 to 6 (section 7.13.2.3).</summary>
    internal static void InverseDct(Span<int> t, int n, int r)
    {
        // Section 7.13.2.2 "Inverse DCT array permutation process"
        var length = 1 << n;
        Span<int> copy = stackalloc int[length];
        t[..length].CopyTo(copy);
        for (var i = 0; i < length; i++)
        {
            t[i] = copy[BitReverse(n, i)];
        }

        if (n is 6)
        {
            for (var i = 0; i < 16; i++)
            {
                B(t, 32 + i, 63 - i, 63 - (4 * BitReverse(4, i)), flip: false);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 8; i++)
            {
                B(t, 16 + i, 31 - i, 6 + (BitReverse(3, 7 - i) << 3), flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 16; i++)
            {
                H(t, 32 + (i * 2), 33 + (i * 2), (i & 1) is not 0, r);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 4; i++)
            {
                B(t, 8 + i, 15 - i, 12 + (BitReverse(2, 3 - i) << 4), flip: false);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 8; i++)
            {
                H(t, 16 + (2 * i), 17 + (2 * i), (i & 1) is not 0, r);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    B(t, 62 - (i * 4) - j, 33 + (i * 4) + j, 60 - (16 * BitReverse(2, i)) + (64 * j), flip: true);
                }
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 2; i++)
            {
                B(t, 4 + i, 7 - i, 56 - (32 * i), flip: false);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 4; i++)
            {
                H(t, 8 + (2 * i), 9 + (2 * i), (i & 1) is not 0, r);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    B(t, 30 - (4 * i) - j, 17 + (4 * i) + j, 24 + (j << 6) + ((1 - i) << 5), flip: true);
                }
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    H(t, 32 + (i * 4) + j, 35 + (i * 4) - j, (i & 1) is not 0, r);
                }
            }
        }

        for (var i = 0; i < 2; i++)
        {
            B(t, 2 * i, (2 * i) + 1, 32 + (16 * i), flip: i is 0);
        }

        if (n >= 3)
        {
            for (var i = 0; i < 2; i++)
            {
                H(t, 4 + (2 * i), 5 + (2 * i), i is not 0, r);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                B(t, 14 - i, 9 + i, 48 + (64 * i), flip: true);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    H(t, 16 + (4 * i) + j, 19 + (4 * i) - j, (i & 1) is not 0, r);
                }
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    B(t, 61 - (i * 8) - j, 34 + (i * 8) + j, 56 - (i * 32) + ((j >> 1) * 64), flip: true);
                }
            }
        }

        for (var i = 0; i < 2; i++)
        {
            H(t, i, 3 - i, flip: false, r);
        }

        if (n >= 3)
            B(t, 6, 5, 32, flip: true);

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    H(t, 8 + (4 * i) + j, 11 + (4 * i) - j, i is not 0, r);
                }
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                B(t, 29 - i, 18 + i, 48 + ((i >> 1) * 64), flip: true);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    H(t, 32 + (8 * i) + j, 39 + (8 * i) - j, (i & 1) is not 0, r);
                }
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 4; i++)
            {
                H(t, i, 7 - i, flip: false, r);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                B(t, 13 - i, 10 + i, 32, flip: true);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    H(t, 16 + (i * 8) + j, 23 + (i * 8) - j, i is not 0, r);
                }
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                B(t, 59 - i, 36 + i, i < 4 ? 48 : 112, flip: true);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 8; i++)
            {
                H(t, i, 15 - i, flip: false, r);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                B(t, 27 - i, 20 + i, 32, flip: true);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                H(t, 32 + i, 47 - i, flip: false, r);
                H(t, 48 + i, 63 - i, flip: true, r);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 16; i++)
            {
                H(t, i, 31 - i, flip: false, r);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                B(t, 55 - i, 40 + i, 32, flip: true);
            }

            for (var i = 0; i < 32; i++)
            {
                H(t, i, 63 - i, flip: false, r);
            }
        }
    }

    /// <summary>The inverse ADST of 2^<paramref name="n"/> points, 2 to 4 (section 7.13.2.9).</summary>
    internal static void InverseAdst(Span<int> t, int n, int r)
    {
        switch (n)
        {
            case 2:
                InverseAdst4(t);
                break;
            case 3:
                InverseAdst8(t, r);
                break;
            default:
                InverseAdst16(t, r);
                break;
        }
    }

    // Section 7.13.2.6 "Inverse ADST4 process"
    private static void InverseAdst4(Span<int> t)
    {
        long s0 = (long)Av1Constants.Sinpi19 * t[0];
        long s1 = (long)Av1Constants.Sinpi29 * t[0];
        long s2 = (long)Av1Constants.Sinpi39 * t[1];
        long s3 = (long)Av1Constants.Sinpi49 * t[2];
        long s4 = (long)Av1Constants.Sinpi19 * t[2];
        long s5 = (long)Av1Constants.Sinpi29 * t[3];
        long s6 = (long)Av1Constants.Sinpi49 * t[3];
        long a7 = (long)t[0] - t[2];
        var b7 = a7 + t[3];
        s0 += s3;
        s1 -= s4;
        s3 = s2;
        s2 = Av1Constants.Sinpi39 * b7;
        s0 += s5;
        s1 -= s6;
        var x0 = s0 + s3;
        var x1 = s1 + s3;
        var x2 = s2;
        var x3 = s0 + s1 - s3;
        t[0] = Round2(x0, 12);
        t[1] = Round2(x1, 12);
        t[2] = Round2(x2, 12);
        t[3] = Round2(x3, 12);
    }

    // Section 7.13.2.7 "Inverse ADST8 process"
    private static void InverseAdst8(Span<int> t, int r)
    {
        PermuteAdstInput(t, 3);
        for (var i = 0; i < 4; i++)
        {
            B(t, 2 * i, (2 * i) + 1, 60 - (16 * i), flip: true);
        }

        for (var i = 0; i < 4; i++)
        {
            H(t, i, 4 + i, flip: false, r);
        }

        for (var i = 0; i < 2; i++)
        {
            B(t, 4 + (3 * i), 5 + i, 48 - (32 * i), flip: true);
        }

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                H(t, (4 * j) + i, 2 + (4 * j) + i, flip: false, r);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            B(t, 2 + (4 * i), 3 + (4 * i), 32, flip: true);
        }

        PermuteAdstOutput(t, 3);
    }

    // Section 7.13.2.8 "Inverse ADST16 process"
    private static void InverseAdst16(Span<int> t, int r)
    {
        PermuteAdstInput(t, 4);
        for (var i = 0; i < 8; i++)
        {
            B(t, 2 * i, (2 * i) + 1, 62 - (8 * i), flip: true);
        }

        for (var i = 0; i < 8; i++)
        {
            H(t, i, 8 + i, flip: false, r);
        }

        for (var i = 0; i < 2; i++)
        {
            B(t, 8 + (2 * i), 9 + (2 * i), 56 - (32 * i), flip: true);
            B(t, 13 + (2 * i), 12 + (2 * i), 8 + (32 * i), flip: true);
        }

        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                H(t, (8 * j) + i, 4 + (8 * j) + i, flip: false, r);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                B(t, 4 + (8 * j) + (3 * i), 5 + (8 * j) + i, 48 - (32 * i), flip: true);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                H(t, (4 * j) + i, 2 + (4 * j) + i, flip: false, r);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            B(t, 2 + (4 * i), 3 + (4 * i), 32, flip: true);
        }

        PermuteAdstOutput(t, 4);
    }

    // Section 7.13.2.4 "Inverse ADST input array permutation process"
    private static void PermuteAdstInput(Span<int> t, int n)
    {
        var n0 = 1 << n;
        Span<int> copy = stackalloc int[16];
        t[..n0].CopyTo(copy);
        for (var i = 0; i < n0; i++)
        {
            var idx = (i & 1) is not 0 ? i - 1 : n0 - i - 1;
            t[i] = copy[idx];
        }
    }

    // Section 7.13.2.5 "Inverse ADST output array permutation process"
    private static void PermuteAdstOutput(Span<int> t, int n)
    {
        var n0 = 1 << n;
        Span<int> copy = stackalloc int[16];
        t[..n0].CopyTo(copy);
        for (var i = 0; i < n0; i++)
        {
            var a = (i >> 3) & 1;
            var b = ((i >> 2) & 1) ^ ((i >> 3) & 1);
            var c = ((i >> 1) & 1) ^ ((i >> 2) & 1);
            var d = (i & 1) ^ ((i >> 1) & 1);
            var idx = ((d << 3) | (c << 2) | (b << 1) | a) >> (4 - n);
            t[i] = (i & 1) is not 0 ? -copy[idx] : copy[idx];
        }
    }

    /// <summary>The inverse Walsh-Hadamard transform of 4 points (section 7.13.2.10).</summary>
    internal static void InverseWalshHadamard(Span<int> t, int shift)
    {
        var a = t[0] >> shift;
        var c = t[1] >> shift;
        var d = t[2] >> shift;
        var b = t[3] >> shift;
        a += c;
        d -= b;
        var e = (a - d) >> 1;
        b = e - b;
        c = e - c;
        a -= b;
        d += c;
        t[0] = a;
        t[1] = b;
        t[2] = c;
        t[3] = d;
    }

    /// <summary>The inverse identity transform of 2^<paramref name="n"/> points, 2 to 5 (section 7.13.2.15).</summary>
    internal static void InverseIdentity(Span<int> t, int n)
    {
        var length = 1 << n;
        switch (n)
        {
            case 2:
                for (var i = 0; i < length; i++)
                {
                    t[i] = Round2(t[i] * 5793L, 12);
                }

                break;
            case 3:
                for (var i = 0; i < length; i++)
                {
                    t[i] *= 2;
                }

                break;
            case 4:
                for (var i = 0; i < length; i++)
                {
                    t[i] = Round2(t[i] * 11586L, 12);
                }

                break;
            default:
                for (var i = 0; i < length; i++)
                {
                    t[i] *= 4;
                }

                break;
        }
    }

    // The butterfly rotation B( a, b, angle, flip ) of section 7.13.2.1
    private static void B(Span<int> t, int a, int b, int angle, bool flip)
    {
        long cos = Cos128(angle);
        long sin = Cos128(angle - 64);
        var x = (t[a] * cos) - (t[b] * sin);
        var y = (t[a] * sin) + (t[b] * cos);
        if (flip)
        {
            t[b] = Round2(x, 12);
            t[a] = Round2(y, 12);
        }
        else
        {
            t[a] = Round2(x, 12);
            t[b] = Round2(y, 12);
        }
    }

    // The Hadamard rotation H( a, b, flip, r ) of section 7.13.2.1
    private static void H(Span<int> t, int a, int b, bool flip, int r)
    {
        if (flip)
            (a, b) = (b, a);

        var x = t[a];
        var y = t[b];
        var min = -(1 << (r - 1));
        var max = (1 << (r - 1)) - 1;
        t[a] = (int)Math.Clamp((long)x + y, min, max);
        t[b] = (int)Math.Clamp((long)x - y, min, max);
    }

    private static int Cos128(int angle)
    {
        var angle2 = angle & 255;
        return angle2 switch
        {
            <= 64 => Av1Tables.Cos128Lookup[angle2],
            <= 128 => -Av1Tables.Cos128Lookup[128 - angle2],
            <= 192 => -Av1Tables.Cos128Lookup[angle2 - 128],
            _ => Av1Tables.Cos128Lookup[256 - angle2],
        };
    }

    private static int BitReverse(int numBits, int x)
    {
        var result = 0;
        for (var i = 0; i < numBits; i++)
        {
            var bit = (x >> i) & 1;
            result += bit << (numBits - 1 - i);
        }

        return result;
    }

    private static int Round2(long value, int bits) => bits is 0 ? (int)value : (int)((value + (1L << (bits - 1))) >> bits);
}
