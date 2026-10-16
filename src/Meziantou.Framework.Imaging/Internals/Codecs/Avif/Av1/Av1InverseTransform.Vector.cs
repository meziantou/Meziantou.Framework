using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The inverse transform of 4 rows or 4 columns at once, in the 32-bit lanes of AdvSimd vectors. Each step is the step of the
/// scalar kernels, with the same 64-bit arithmetic: the products of the rotations are widening multiplications, their sums
/// are 64-bit, Round2 is a rounding shift of the 64-bit value (which cannot overflow) and the cast to 32 bits is a
/// truncating narrowing; the sums of the Hadamard rotations, which the scalar code computes in 64 bits before clamping them,
/// are saturating additions, whose clamped result is the same. The results are the scalar ones for every input.
/// </content>
internal static partial class Av1InverseTransform
{
    // The 2D transform of TransformScalar: the rows by groups of 4 (transposed into the lanes of the vectors), then the
    // columns by groups of 4 (whose samples are the lanes of consecutive rows)
    [SkipLocalsInit]
    private static void TransformAdvSimd(ReadOnlySpan<int> dequant, Span<int> residual, int txSz, int txType, int bitDepth)
    {
        var log2W = Av1Tables.TxWidthLog2[txSz];
        var log2H = Av1Tables.TxHeightLog2[txSz];
        var w = 1 << log2W;
        var h = 1 << log2H;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        var rowShift = Av1Tables.TransformRowShift[txSz];
        const int ColShift = 4;
        var rowClampRange = bitDepth + 8;
        var colClampRange = Math.Max(bitDepth + 6, 16);
        var colMin = Vector128.Create(-(1 << (colClampRange - 1)));
        var colMax = Vector128.Create((1 << (colClampRange - 1)) - 1);
        var isRectangular2To1 = Math.Abs(log2W - log2H) is 1;
        var rowKind = GetRowKind(txType);
        var colKind = GetColumnKind(txType);
        dequant = dequant[..(tw * th)];
        residual = residual[..(w * h)];

        // Every element is written before it is read: the kernels read the first 2^n elements, which are loaded first
        Span<Vector128<int>> t = unsafe(stackalloc Vector128<int>[64]);
        var lastRow = -1;
        for (var i = 0; i < h; i += 4)
        {
            var rows = residual.Slice(i * w, 4 * w);
            var any = false;
            for (var r = 0; r < 4 && i + r < th; r++)
            {
                if (dequant.Slice((i + r) * tw, tw).ContainsAnyExcept(0))
                {
                    any = true;
                    lastRow = i + r;
                }
            }

            if (!any)
            {
                // A row of zero coefficients transforms to zeros
                rows.Clear();
                continue;
            }

            for (var k = 0; k < tw; k += 4)
            {
                Transpose(
                    Vector128.Create(dequant.Slice((i * tw) + k, 4)),
                    Vector128.Create(dequant.Slice(((i + 1) * tw) + k, 4)),
                    Vector128.Create(dequant.Slice(((i + 2) * tw) + k, 4)),
                    Vector128.Create(dequant.Slice(((i + 3) * tw) + k, 4)),
                    out t[k],
                    out t[k + 1],
                    out t[k + 2],
                    out t[k + 3]);
            }

            t[tw..w].Clear();
            if (isRectangular2To1)
            {
                for (var k = 0; k < w; k++)
                {
                    t[k] = MultiplyRound12(t[k], 2896);
                }
            }

            Apply(rowKind, t[..w], log2W, rowClampRange);
            for (var k = 0; k < w; k += 4)
            {
                Transpose(
                    RowOutput(t[k], rowShift, colMin, colMax),
                    RowOutput(t[k + 1], rowShift, colMin, colMax),
                    RowOutput(t[k + 2], rowShift, colMin, colMax),
                    RowOutput(t[k + 3], rowShift, colMin, colMax),
                    out var row0,
                    out var row1,
                    out var row2,
                    out var row3);
                row0.CopyTo(rows[k..]);
                row1.CopyTo(rows[(w + k)..]);
                row2.CopyTo(rows[((2 * w) + k)..]);
                row3.CopyTo(rows[((3 * w) + k)..]);
            }
        }

        if (lastRow < 0)
            return;

        if (lastRow is 0 && !residual[..w].ContainsAnyExcept(residual[0]))
        {
            TransformConstantRows(residual, w, h, log2H, colKind, colClampRange, lossless: false, ColShift);
            return;
        }

        for (var j = 0; j < w; j += 4)
        {
            var column = t[..h];
            for (var i = 0; i < h; i++)
            {
                column[i] = Vector128.Create(residual.Slice((i * w) + j, 4));
            }

            Apply(colKind, column, log2H, colClampRange);
            for (var i = 0; i < h; i++)
            {
                AdvSimd.ShiftRightArithmeticRounded(column[i], ColShift).CopyTo(residual.Slice((i * w) + j, 4));
            }
        }

        static Vector128<int> RowOutput(Vector128<int> value, int shift, Vector128<int> min, Vector128<int> max)
        {
            if (shift is not 0)
                value = ShiftRightArithmeticRounded(value, shift);

            return Vector128.Min(Vector128.Max(value, min), max);
        }
    }

    // A 4x4 transposition: the lanes of the outputs are the vectors of the inputs
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(Vector128<int> v0, Vector128<int> v1, Vector128<int> v2, Vector128<int> v3, out Vector128<int> r0, out Vector128<int> r1, out Vector128<int> r2, out Vector128<int> r3)
    {
        var t0 = AdvSimd.Arm64.TransposeEven(v0, v1).AsInt64();
        var t1 = AdvSimd.Arm64.TransposeOdd(v0, v1).AsInt64();
        var t2 = AdvSimd.Arm64.TransposeEven(v2, v3).AsInt64();
        var t3 = AdvSimd.Arm64.TransposeOdd(v2, v3).AsInt64();
        r0 = AdvSimd.Arm64.TransposeEven(t0, t2).AsInt32();
        r1 = AdvSimd.Arm64.TransposeEven(t1, t3).AsInt32();
        r2 = AdvSimd.Arm64.TransposeOdd(t0, t2).AsInt32();
        r3 = AdvSimd.Arm64.TransposeOdd(t1, t3).AsInt32();
    }

    private static Vector128<int> ShiftRightArithmeticRounded(Vector128<int> value, int shift) => shift switch
    {
        1 => AdvSimd.ShiftRightArithmeticRounded(value, 1),
        2 => AdvSimd.ShiftRightArithmeticRounded(value, 2),
        _ => (value >> shift) + ((value >> (shift - 1)) & Vector128<int>.One),
    };

    private static void Apply(Kind kind, Span<Vector128<int>> t, int n, int r)
    {
        switch (kind)
        {
            case Kind.Dct:
                InverseDct(t, n, r);
                break;
            case Kind.Adst:
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

                break;
            default:
                InverseIdentity(t, n);
                break;
        }
    }

    // InverseDct on vectors: the same steps
    private static void InverseDct(Span<Vector128<int>> t, int n, int r)
    {
        var length = 1 << n;
        for (var i = 0; i < length; i++)
        {
            var j = BitReverse(n, i);
            if (j > i)
                (t[i], t[j]) = (t[j], t[i]);
        }

        var min = Vector128.Create(-(1 << (r - 1)));
        var max = Vector128.Create((1 << (r - 1)) - 1);
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
                H(t, 32 + (i * 2), 33 + (i * 2), (i & 1) is not 0, min, max);
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
                H(t, 16 + (2 * i), 17 + (2 * i), (i & 1) is not 0, min, max);
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
                H(t, 8 + (2 * i), 9 + (2 * i), (i & 1) is not 0, min, max);
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
                    H(t, 32 + (i * 4) + j, 35 + (i * 4) - j, (i & 1) is not 0, min, max);
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
                H(t, 4 + (2 * i), 5 + (2 * i), i is not 0, min, max);
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
                    H(t, 16 + (4 * i) + j, 19 + (4 * i) - j, (i & 1) is not 0, min, max);
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
            H(t, i, 3 - i, flip: false, min, max);
        }

        if (n >= 3)
            B(t, 6, 5, 32, flip: true);

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    H(t, 8 + (4 * i) + j, 11 + (4 * i) - j, i is not 0, min, max);
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
                    H(t, 32 + (8 * i) + j, 39 + (8 * i) - j, (i & 1) is not 0, min, max);
                }
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 4; i++)
            {
                H(t, i, 7 - i, flip: false, min, max);
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
                    H(t, 16 + (i * 8) + j, 23 + (i * 8) - j, i is not 0, min, max);
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
                H(t, i, 15 - i, flip: false, min, max);
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
                H(t, 32 + i, 47 - i, flip: false, min, max);
                H(t, 48 + i, 63 - i, flip: true, min, max);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 16; i++)
            {
                H(t, i, 31 - i, flip: false, min, max);
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
                H(t, i, 63 - i, flip: false, min, max);
            }
        }
    }

    // InverseAdst4 on vectors: the 64-bit products and sums of the scalar kernel, for the 2 lower then the 2 upper lanes.
    // Sinpi39 * (t0 - t2 + t3) is computed as the sum of the 3 products, the same 64-bit value
    private static void InverseAdst4(Span<Vector128<int>> t)
    {
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var x0 = Adst4Outputs(t0.GetLower(), t1.GetLower(), t2.GetLower(), t3.GetLower(), out var x1, out var x2, out var x3);
        var y0 = Adst4Outputs(t0.GetUpper(), t1.GetUpper(), t2.GetUpper(), t3.GetUpper(), out var y1, out var y2, out var y3);
        t[0] = Narrow(x0, y0);
        t[1] = Narrow(x1, y1);
        t[2] = Narrow(x2, y2);
        t[3] = Narrow(x3, y3);

        static Vector128<long> Adst4Outputs(Vector64<int> t0, Vector64<int> t1, Vector64<int> t2, Vector64<int> t3, out Vector128<long> x1, out Vector128<long> x2, out Vector128<long> x3)
        {
            var s0 = AdvSimd.MultiplyWideningLower(t0, Vector64.Create(Av1Constants.Sinpi19));
            var s1 = AdvSimd.MultiplyWideningLower(t0, Vector64.Create(Av1Constants.Sinpi29));
            var s2 = AdvSimd.MultiplyWideningLower(t1, Vector64.Create(Av1Constants.Sinpi39));
            var s3 = AdvSimd.MultiplyWideningLower(t2, Vector64.Create(Av1Constants.Sinpi49));
            var s4 = AdvSimd.MultiplyWideningLower(t2, Vector64.Create(Av1Constants.Sinpi19));
            var s5 = AdvSimd.MultiplyWideningLower(t3, Vector64.Create(Av1Constants.Sinpi29));
            var s6 = AdvSimd.MultiplyWideningLower(t3, Vector64.Create(Av1Constants.Sinpi49));
            var sinpi39 = Vector64.Create(Av1Constants.Sinpi39);
            var b7 = AdvSimd.MultiplyWideningLowerAndAdd(AdvSimd.MultiplyWideningLowerAndSubtract(AdvSimd.MultiplyWideningLower(t0, sinpi39), t2, sinpi39), t3, sinpi39);
            s0 += s3;
            s1 -= s4;
            s3 = s2;
            s2 = b7;
            s0 += s5;
            s1 -= s6;
            var x0 = s0 + s3;
            x1 = s1 + s3;
            x2 = s2;
            x3 = s0 + s1 - s3;
            return x0;
        }
    }

    // InverseAdst8 on vectors: the same steps
    private static void InverseAdst8(Span<Vector128<int>> t, int r)
    {
        var min = Vector128.Create(-(1 << (r - 1)));
        var max = Vector128.Create((1 << (r - 1)) - 1);
        PermuteAdstInput(t, 3);
        for (var i = 0; i < 4; i++)
        {
            B(t, 2 * i, (2 * i) + 1, 60 - (16 * i), flip: true);
        }

        for (var i = 0; i < 4; i++)
        {
            H(t, i, 4 + i, flip: false, min, max);
        }

        for (var i = 0; i < 2; i++)
        {
            B(t, 4 + (3 * i), 5 + i, 48 - (32 * i), flip: true);
        }

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                H(t, (4 * j) + i, 2 + (4 * j) + i, flip: false, min, max);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            B(t, 2 + (4 * i), 3 + (4 * i), 32, flip: true);
        }

        PermuteAdstOutput(t, 3);
    }

    // InverseAdst16 on vectors: the same steps
    private static void InverseAdst16(Span<Vector128<int>> t, int r)
    {
        var min = Vector128.Create(-(1 << (r - 1)));
        var max = Vector128.Create((1 << (r - 1)) - 1);
        PermuteAdstInput(t, 4);
        for (var i = 0; i < 8; i++)
        {
            B(t, 2 * i, (2 * i) + 1, 62 - (8 * i), flip: true);
        }

        for (var i = 0; i < 8; i++)
        {
            H(t, i, 8 + i, flip: false, min, max);
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
                H(t, (8 * j) + i, 4 + (8 * j) + i, flip: false, min, max);
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
                H(t, (4 * j) + i, 2 + (4 * j) + i, flip: false, min, max);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            B(t, 2 + (4 * i), 3 + (4 * i), 32, flip: true);
        }

        PermuteAdstOutput(t, 4);
    }

    private static void PermuteAdstInput(Span<Vector128<int>> t, int n)
    {
        var n0 = 1 << n;
        Span<Vector128<int>> copy = unsafe(stackalloc Vector128<int>[16]);
        t[..n0].CopyTo(copy);
        for (var i = 0; i < n0; i++)
        {
            var idx = (i & 1) is not 0 ? i - 1 : n0 - i - 1;
            t[i] = copy[idx];
        }
    }

    private static void PermuteAdstOutput(Span<Vector128<int>> t, int n)
    {
        var n0 = 1 << n;
        Span<Vector128<int>> copy = unsafe(stackalloc Vector128<int>[16]);
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

    private static void InverseIdentity(Span<Vector128<int>> t, int n)
    {
        var length = 1 << n;
        for (var i = 0; i < length; i++)
        {
            t[i] = n switch
            {
                2 => MultiplyRound12(t[i], 5793),
                3 => t[i] << 1,
                4 => MultiplyRound12(t[i], 11586),
                _ => t[i] << 2,
            };
        }
    }

    // Round2( value * factor, 12 ) with a 64-bit product
    private static Vector128<int> MultiplyRound12(Vector128<int> value, int factor)
    {
        var low = AdvSimd.MultiplyWideningLower(value.GetLower(), Vector64.Create(factor));
        var high = AdvSimd.MultiplyWideningUpper(value, Vector128.Create(factor));
        return Narrow(low, high);
    }

    // (int)Round2( value, 12 ) of two vectors of 64-bit values
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Narrow(Vector128<long> low, Vector128<long> high)
    {
        return AdvSimd.ExtractNarrowingUpper(AdvSimd.ExtractNarrowingLower(AdvSimd.ShiftRightArithmeticRounded(low, 12)), AdvSimd.ShiftRightArithmeticRounded(high, 12));
    }

    // The butterfly rotation B( a, b, angle, flip ) on vectors
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void B(Span<Vector128<int>> t, int a, int b, int angle, bool flip)
    {
        var cos = Cos128(angle);
        var sin = Cos128(angle - 64);
        var va = t[a];
        var vb = t[b];
        var cosLow = Vector64.Create(cos);
        var sinLow = Vector64.Create(sin);
        var cosHigh = Vector128.Create(cos);
        var sinHigh = Vector128.Create(sin);
        var x = Narrow(
            AdvSimd.MultiplyWideningLowerAndSubtract(AdvSimd.MultiplyWideningLower(va.GetLower(), cosLow), vb.GetLower(), sinLow),
            AdvSimd.MultiplyWideningUpperAndSubtract(AdvSimd.MultiplyWideningUpper(va, cosHigh), vb, sinHigh));
        var y = Narrow(
            AdvSimd.MultiplyWideningLowerAndAdd(AdvSimd.MultiplyWideningLower(va.GetLower(), sinLow), vb.GetLower(), cosLow),
            AdvSimd.MultiplyWideningUpperAndAdd(AdvSimd.MultiplyWideningUpper(va, sinHigh), vb, cosHigh));
        if (flip)
        {
            t[b] = x;
            t[a] = y;
        }
        else
        {
            t[a] = x;
            t[b] = y;
        }
    }

    // The Hadamard rotation H( a, b, flip, r ) on vectors: Clamp of the 64-bit sum is Clamp of the saturated sum
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void H(Span<Vector128<int>> t, int a, int b, bool flip, Vector128<int> min, Vector128<int> max)
    {
        if (flip)
            (a, b) = (b, a);

        var x = t[a];
        var y = t[b];
        t[a] = Vector128.Min(Vector128.Max(AdvSimd.AddSaturate(x, y), min), max);
        t[b] = Vector128.Min(Vector128.Max(AdvSimd.SubtractSaturate(x, y), min), max);
    }
}
