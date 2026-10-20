using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The inverse transform of 4 rows or 4 columns at once, in the 32-bit lanes of vectors (the operations of
/// <see cref="Av1Simd"/>). Each step is the step of the scalar kernels, with the same 64-bit arithmetic: the products of the
/// rotations are widening multiplications, their sums are 64-bit, Round2 is a rounding of the 64-bit value (which cannot
/// overflow) and the cast to 32 bits is a truncating narrowing; the sums of the Hadamard rotations, which the scalar code
/// computes in 64 bits before clamping them, are saturating additions, whose clamped result is the same. The results are
/// the scalar ones for every input. The one-dimensional kernels are written once for the operations of
/// <see cref="ILanes{TVector}"/>: on x64 with AVX2, the columns are transformed 8 at a time in the 32-bit lanes of
/// <see cref="Vector256{T}"/>.
/// </content>
internal static partial class Av1InverseTransform
{
    private const int ColumnShift = 4;

    // The 2D transform of TransformScalar: the rows by groups of 4 (transposed into the lanes of the vectors), then the
    // columns by groups of 4 (whose samples are the lanes of consecutive rows)
    [SkipLocalsInit]
    private static void TransformVector(ReadOnlySpan<int> dequant, Span<int> residual, int txSz, int txType, int bitDepth)
    {
        var log2W = Av1Tables.TxWidthLog2[txSz];
        var log2H = Av1Tables.TxHeightLog2[txSz];
        var w = 1 << log2W;
        var h = 1 << log2H;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        var rowShift = Av1Tables.TransformRowShift[txSz];
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
                Av1Simd.Transpose(
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
                    t[k] = Lanes128.MultiplyRound12(t[k], 2896);
                }
            }

            Apply<Lanes128, Vector128<int>>(rowKind, t[..w], log2W, rowClampRange);
            for (var k = 0; k < w; k += 4)
            {
                Av1Simd.Transpose(
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
            TransformConstantRows(residual, w, h, log2H, colKind, colClampRange, lossless: false, ColumnShift);
            return;
        }

        if (Avx2.IsSupported && w >= 8)
        {
            TransformColumns256(residual, w, h, log2H, colKind, colClampRange);
            return;
        }

        for (var j = 0; j < w; j += 4)
        {
            var column = t[..h];
            for (var i = 0; i < h; i++)
            {
                column[i] = Vector128.Create(residual.Slice((i * w) + j, 4));
            }

            Apply<Lanes128, Vector128<int>>(colKind, column, log2H, colClampRange);
            for (var i = 0; i < h; i++)
            {
                Av1Simd.ShiftRightArithmeticRounded(column[i], ColumnShift).CopyTo(residual.Slice((i * w) + j, 4));
            }
        }

        static Vector128<int> RowOutput(Vector128<int> value, int shift, Vector128<int> min, Vector128<int> max)
        {
            if (shift is not 0)
                value = Av1Simd.ShiftRightArithmeticRounded(value, shift);

            return Vector128.Min(Vector128.Max(value, min), max);
        }
    }

    // The columns of TransformVector by groups of 8, for a block at least 8 samples wide
    [SkipLocalsInit]
    private static void TransformColumns256(Span<int> residual, int w, int h, int log2H, Kind colKind, int colClampRange)
    {
        // Every element is written before it is read
        Span<Vector256<int>> column = unsafe(stackalloc Vector256<int>[64])[..h];
        for (var j = 0; j < w; j += 8)
        {
            for (var i = 0; i < h; i++)
            {
                column[i] = Vector256.Create(residual.Slice((i * w) + j, 8));
            }

            Apply<Lanes256, Vector256<int>>(colKind, column, log2H, colClampRange);
            for (var i = 0; i < h; i++)
            {
                Lanes256.ShiftRightArithmeticRounded(column[i], ColumnShift).CopyTo(residual.Slice((i * w) + j, 8));
            }
        }
    }

    private static void Apply<TLanes, TVector>(Kind kind, Span<TVector> t, int n, int r)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        switch (kind)
        {
            case Kind.Dct:
                InverseDct<TLanes, TVector>(t, n, r);
                break;
            case Kind.Adst:
                switch (n)
                {
                    case 2:
                        TLanes.InverseAdst4(t);
                        break;
                    case 3:
                        InverseAdst8<TLanes, TVector>(t, r);
                        break;
                    default:
                        InverseAdst16<TLanes, TVector>(t, r);
                        break;
                }

                break;
            default:
                InverseIdentity<TLanes, TVector>(t, n);
                break;
        }
    }

    // InverseDct on vectors: the same steps
    private static void InverseDct<TLanes, TVector>(Span<TVector> t, int n, int r)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var length = 1 << n;
        for (var i = 0; i < length; i++)
        {
            var j = BitReverse(n, i);
            if (j > i)
                (t[i], t[j]) = (t[j], t[i]);
        }

        var min = TLanes.Create(-(1 << (r - 1)));
        var max = TLanes.Create((1 << (r - 1)) - 1);
        if (n is 6)
        {
            for (var i = 0; i < 16; i++)
            {
                TLanes.B(t, 32 + i, 63 - i, 63 - (4 * BitReverse(4, i)), flip: false);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 8; i++)
            {
                TLanes.B(t, 16 + i, 31 - i, 6 + (BitReverse(3, 7 - i) << 3), flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 16; i++)
            {
                TLanes.H(t, 32 + (i * 2), 33 + (i * 2), (i & 1) is not 0, min, max);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 4; i++)
            {
                TLanes.B(t, 8 + i, 15 - i, 12 + (BitReverse(2, 3 - i) << 4), flip: false);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 8; i++)
            {
                TLanes.H(t, 16 + (2 * i), 17 + (2 * i), (i & 1) is not 0, min, max);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    TLanes.B(t, 62 - (i * 4) - j, 33 + (i * 4) + j, 60 - (16 * BitReverse(2, i)) + (64 * j), flip: true);
                }
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 2; i++)
            {
                TLanes.B(t, 4 + i, 7 - i, 56 - (32 * i), flip: false);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 4; i++)
            {
                TLanes.H(t, 8 + (2 * i), 9 + (2 * i), (i & 1) is not 0, min, max);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    TLanes.B(t, 30 - (4 * i) - j, 17 + (4 * i) + j, 24 + (j << 6) + ((1 - i) << 5), flip: true);
                }
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    TLanes.H(t, 32 + (i * 4) + j, 35 + (i * 4) - j, (i & 1) is not 0, min, max);
                }
            }
        }

        for (var i = 0; i < 2; i++)
        {
            TLanes.B(t, 2 * i, (2 * i) + 1, 32 + (16 * i), flip: i is 0);
        }

        if (n >= 3)
        {
            for (var i = 0; i < 2; i++)
            {
                TLanes.H(t, 4 + (2 * i), 5 + (2 * i), i is not 0, min, max);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                TLanes.B(t, 14 - i, 9 + i, 48 + (64 * i), flip: true);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    TLanes.H(t, 16 + (4 * i) + j, 19 + (4 * i) - j, (i & 1) is not 0, min, max);
                }
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    TLanes.B(t, 61 - (i * 8) - j, 34 + (i * 8) + j, 56 - (i * 32) + ((j >> 1) * 64), flip: true);
                }
            }
        }

        for (var i = 0; i < 2; i++)
        {
            TLanes.H(t, i, 3 - i, flip: false, min, max);
        }

        if (n >= 3)
            TLanes.B(t, 6, 5, 32, flip: true);

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    TLanes.H(t, 8 + (4 * i) + j, 11 + (4 * i) - j, i is not 0, min, max);
                }
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                TLanes.B(t, 29 - i, 18 + i, 48 + ((i >> 1) * 64), flip: true);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    TLanes.H(t, 32 + (8 * i) + j, 39 + (8 * i) - j, (i & 1) is not 0, min, max);
                }
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 4; i++)
            {
                TLanes.H(t, i, 7 - i, flip: false, min, max);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                TLanes.B(t, 13 - i, 10 + i, 32, flip: true);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    TLanes.H(t, 16 + (i * 8) + j, 23 + (i * 8) - j, i is not 0, min, max);
                }
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                TLanes.B(t, 59 - i, 36 + i, i < 4 ? 48 : 112, flip: true);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 8; i++)
            {
                TLanes.H(t, i, 15 - i, flip: false, min, max);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                TLanes.B(t, 27 - i, 20 + i, 32, flip: true);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                TLanes.H(t, 32 + i, 47 - i, flip: false, min, max);
                TLanes.H(t, 48 + i, 63 - i, flip: true, min, max);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 16; i++)
            {
                TLanes.H(t, i, 31 - i, flip: false, min, max);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                TLanes.B(t, 55 - i, 40 + i, 32, flip: true);
            }

            for (var i = 0; i < 32; i++)
            {
                TLanes.H(t, i, 63 - i, flip: false, min, max);
            }
        }
    }

    // InverseAdst8 on vectors: the same steps
    private static void InverseAdst8<TLanes, TVector>(Span<TVector> t, int r)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var min = TLanes.Create(-(1 << (r - 1)));
        var max = TLanes.Create((1 << (r - 1)) - 1);
        PermuteAdstInput(t, 3);
        for (var i = 0; i < 4; i++)
        {
            TLanes.B(t, 2 * i, (2 * i) + 1, 60 - (16 * i), flip: true);
        }

        for (var i = 0; i < 4; i++)
        {
            TLanes.H(t, i, 4 + i, flip: false, min, max);
        }

        for (var i = 0; i < 2; i++)
        {
            TLanes.B(t, 4 + (3 * i), 5 + i, 48 - (32 * i), flip: true);
        }

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                TLanes.H(t, (4 * j) + i, 2 + (4 * j) + i, flip: false, min, max);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            TLanes.B(t, 2 + (4 * i), 3 + (4 * i), 32, flip: true);
        }

        PermuteAdstOutput<TLanes, TVector>(t, 3);
    }

    // InverseAdst16 on vectors: the same steps
    private static void InverseAdst16<TLanes, TVector>(Span<TVector> t, int r)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var min = TLanes.Create(-(1 << (r - 1)));
        var max = TLanes.Create((1 << (r - 1)) - 1);
        PermuteAdstInput(t, 4);
        for (var i = 0; i < 8; i++)
        {
            TLanes.B(t, 2 * i, (2 * i) + 1, 62 - (8 * i), flip: true);
        }

        for (var i = 0; i < 8; i++)
        {
            TLanes.H(t, i, 8 + i, flip: false, min, max);
        }

        for (var i = 0; i < 2; i++)
        {
            TLanes.B(t, 8 + (2 * i), 9 + (2 * i), 56 - (32 * i), flip: true);
            TLanes.B(t, 13 + (2 * i), 12 + (2 * i), 8 + (32 * i), flip: true);
        }

        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                TLanes.H(t, (8 * j) + i, 4 + (8 * j) + i, flip: false, min, max);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                TLanes.B(t, 4 + (8 * j) + (3 * i), 5 + (8 * j) + i, 48 - (32 * i), flip: true);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                TLanes.H(t, (4 * j) + i, 2 + (4 * j) + i, flip: false, min, max);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            TLanes.B(t, 2 + (4 * i), 3 + (4 * i), 32, flip: true);
        }

        PermuteAdstOutput<TLanes, TVector>(t, 4);
    }

    private static void PermuteAdstInput<TVector>(Span<TVector> t, int n)
        where TVector : unmanaged
    {
        var n0 = 1 << n;
        Span<TVector> copy = unsafe(stackalloc TVector[16]);
        t[..n0].CopyTo(copy);
        for (var i = 0; i < n0; i++)
        {
            var idx = (i & 1) is not 0 ? i - 1 : n0 - i - 1;
            t[i] = copy[idx];
        }
    }

    private static void PermuteAdstOutput<TLanes, TVector>(Span<TVector> t, int n)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var n0 = 1 << n;
        Span<TVector> copy = unsafe(stackalloc TVector[16]);
        t[..n0].CopyTo(copy);
        for (var i = 0; i < n0; i++)
        {
            var a = (i >> 3) & 1;
            var b = ((i >> 2) & 1) ^ ((i >> 3) & 1);
            var c = ((i >> 1) & 1) ^ ((i >> 2) & 1);
            var d = (i & 1) ^ ((i >> 1) & 1);
            var idx = ((d << 3) | (c << 2) | (b << 1) | a) >> (4 - n);
            t[i] = (i & 1) is not 0 ? TLanes.Negate(copy[idx]) : copy[idx];
        }
    }

    private static void InverseIdentity<TLanes, TVector>(Span<TVector> t, int n)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var length = 1 << n;
        for (var i = 0; i < length; i++)
        {
            t[i] = n switch
            {
                2 => TLanes.MultiplyRound12(t[i], 5793),
                3 => TLanes.ShiftLeft(t[i], 1),
                4 => TLanes.MultiplyRound12(t[i], 11586),
                _ => TLanes.ShiftLeft(t[i], 2),
            };
        }
    }

    /// <summary>The operations of the vector kernels on vectors of 32-bit lanes, each lane a row or a column.</summary>
    /// <typeparam name="TVector">The type of the vectors.</typeparam>
    private interface ILanes<TVector>
        where TVector : unmanaged
    {
        /// <summary>A vector whose lanes are <paramref name="value"/>.</summary>
        static abstract TVector Create(int value);

        /// <summary>The negation of the lanes.</summary>
        static abstract TVector Negate(TVector value);

        /// <summary>The lanes shifted left by <paramref name="count"/> bits.</summary>
        static abstract TVector ShiftLeft(TVector value, int count);

        /// <summary><c>Round2( value * factor, 12 )</c> of the lanes, with a 64-bit product.</summary>
        static abstract TVector MultiplyRound12(TVector value, int factor);

        /// <summary>The butterfly rotation <c>B( a, b, angle, flip )</c>.</summary>
        static abstract void B(Span<TVector> t, int a, int b, int angle, bool flip);

        /// <summary>The Hadamard rotation <c>H( a, b, flip, r )</c>, whose clamp range is [<paramref name="min"/>, <paramref name="max"/>].</summary>
        static abstract void H(Span<TVector> t, int a, int b, bool flip, TVector min, TVector max);

        /// <summary>The inverse ADST of 4 points.</summary>
        static abstract void InverseAdst4(Span<TVector> t);
    }

    // The operations on Vector128: the 64-bit products are split in two vectors of 2 lanes (Av1Simd.MultiplySplitFirst and
    // MultiplySplitSecond), which RoundNarrow12Split joins
    private readonly struct Lanes128 : ILanes<Vector128<int>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Create(int value) => Vector128.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Negate(Vector128<int> value) => -value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> ShiftLeft(Vector128<int> value, int count) => value << count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> MultiplyRound12(Vector128<int> value, int factor)
        {
            var factors = Vector128.Create(factor);
            return Av1Simd.RoundNarrow12Split(Av1Simd.MultiplySplitFirst(value, factors), Av1Simd.MultiplySplitSecond(value, factors));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void B(Span<Vector128<int>> t, int a, int b, int angle, bool flip)
        {
            var cos = Vector128.Create(Cos128(angle));
            var sin = Vector128.Create(Cos128(angle - 64));
            var va = t[a];
            var vb = t[b];
            var x = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirst(va, cos), vb, sin),
                Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecond(va, cos), vb, sin));
            var y = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirst(va, sin), vb, cos),
                Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecond(va, sin), vb, cos));
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

        // Clamp of the 64-bit sum is Clamp of the saturated sum
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void H(Span<Vector128<int>> t, int a, int b, bool flip, Vector128<int> min, Vector128<int> max)
        {
            if (flip)
                (a, b) = (b, a);

            var x = t[a];
            var y = t[b];
            t[a] = Vector128.Min(Vector128.Max(Av1Simd.AddSaturate(x, y), min), max);
            t[b] = Vector128.Min(Vector128.Max(Av1Simd.SubtractSaturate(x, y), min), max);
        }

        // The 64-bit products and sums of the scalar kernel, for the first then the second lanes of the split.
        // Sinpi39 * (t0 - t2 + t3) is computed as the sum of the 3 products, the same 64-bit value
        public static void InverseAdst4(Span<Vector128<int>> t)
        {
            var t0 = t[0];
            var t1 = t[1];
            var t2 = t[2];
            var t3 = t[3];
            var x0 = Adst4Outputs(t0, t1, t2, t3, second: false, out var x1, out var x2, out var x3);
            var y0 = Adst4Outputs(t0, t1, t2, t3, second: true, out var y1, out var y2, out var y3);
            t[0] = Av1Simd.RoundNarrow12Split(x0, y0);
            t[1] = Av1Simd.RoundNarrow12Split(x1, y1);
            t[2] = Av1Simd.RoundNarrow12Split(x2, y2);
            t[3] = Av1Simd.RoundNarrow12Split(x3, y3);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static Vector128<long> Adst4Outputs(Vector128<int> t0, Vector128<int> t1, Vector128<int> t2, Vector128<int> t3, bool second, out Vector128<long> x1, out Vector128<long> x2, out Vector128<long> x3)
            {
                var s0 = Multiply(t0, Av1Constants.Sinpi19, second);
                var s1 = Multiply(t0, Av1Constants.Sinpi29, second);
                var s2 = Multiply(t1, Av1Constants.Sinpi39, second);
                var s3 = Multiply(t2, Av1Constants.Sinpi49, second);
                var s4 = Multiply(t2, Av1Constants.Sinpi19, second);
                var s5 = Multiply(t3, Av1Constants.Sinpi29, second);
                var s6 = Multiply(t3, Av1Constants.Sinpi49, second);
                var sinpi39 = Vector128.Create(Av1Constants.Sinpi39);
                var b7 = second
                    ? Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecond(t0, sinpi39), t2, sinpi39), t3, sinpi39)
                    : Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirst(t0, sinpi39), t2, sinpi39), t3, sinpi39);
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static Vector128<long> Multiply(Vector128<int> value, int factor, bool second)
                => second ? Av1Simd.MultiplySplitSecond(value, Vector128.Create(factor)) : Av1Simd.MultiplySplitFirst(value, Vector128.Create(factor));
        }
    }

    // The operations on Vector256, with AVX2 (x64 only): vpmuldq multiplies the lanes 0, 2, 4 and 6, so the products are
    // split in the even lanes and the odd lanes, which a shift by 32 bits brings to the even lanes
    private readonly struct Lanes256 : ILanes<Vector256<int>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> Create(int value) => Vector256.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> Negate(Vector256<int> value) => -value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> ShiftLeft(Vector256<int> value, int count) => value << count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> MultiplyRound12(Vector256<int> value, int factor)
        {
            var factors = Vector256.Create(factor);
            return RoundNarrow12(Multiply(value, factors), Multiply(Odd(value), factors));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void B(Span<Vector256<int>> t, int a, int b, int angle, bool flip)
        {
            var cos = Vector256.Create(Cos128(angle));
            var sin = Vector256.Create(Cos128(angle - 64));
            var va = t[a];
            var vb = t[b];
            var oddA = Odd(va);
            var oddB = Odd(vb);
            var x = RoundNarrow12(Multiply(va, cos) - Multiply(vb, sin), Multiply(oddA, cos) - Multiply(oddB, sin));
            var y = RoundNarrow12(Multiply(va, sin) + Multiply(vb, cos), Multiply(oddA, sin) + Multiply(oddB, cos));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void H(Span<Vector256<int>> t, int a, int b, bool flip, Vector256<int> min, Vector256<int> max)
        {
            if (flip)
                (a, b) = (b, a);

            var x = t[a];
            var y = t[b];
            t[a] = Vector256.Min(Vector256.Max(Vector256.AddSaturate(x, y), min), max);
            t[b] = Vector256.Min(Vector256.Max(Vector256.SubtractSaturate(x, y), min), max);
        }

        // The outputs of the scalar kernel are sums of products of the inputs by constants, exact in 64 bits whatever the
        // order of the sums: each one is computed from the 4 products, for the even then the odd lanes
        public static void InverseAdst4(Span<Vector256<int>> t)
        {
            var t0 = t[0];
            var t1 = t[1];
            var t2 = t[2];
            var t3 = t[3];
            var o0 = Odd(t0);
            var o1 = Odd(t1);
            var o2 = Odd(t2);
            var o3 = Odd(t3);
            t[0] = Output(t0, t1, t2, t3, o0, o1, o2, o3, Av1Constants.Sinpi19, Av1Constants.Sinpi39, Av1Constants.Sinpi49, Av1Constants.Sinpi29);
            t[1] = Output(t0, t1, t2, t3, o0, o1, o2, o3, Av1Constants.Sinpi29, Av1Constants.Sinpi39, -Av1Constants.Sinpi19, -Av1Constants.Sinpi49);
            t[2] = Output(t0, t1, t2, t3, o0, o1, o2, o3, Av1Constants.Sinpi39, 0, -Av1Constants.Sinpi39, Av1Constants.Sinpi39);
            t[3] = Output(t0, t1, t2, t3, o0, o1, o2, o3, Av1Constants.Sinpi19 + Av1Constants.Sinpi29, -Av1Constants.Sinpi39, Av1Constants.Sinpi49 - Av1Constants.Sinpi19, Av1Constants.Sinpi29 - Av1Constants.Sinpi49);

            // o0 to o3 are the odd lanes of t0 to t3 in the even lanes
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static Vector256<int> Output(Vector256<int> t0, Vector256<int> t1, Vector256<int> t2, Vector256<int> t3, Vector256<int> o0, Vector256<int> o1, Vector256<int> o2, Vector256<int> o3, int c0, int c1, int c2, int c3)
            {
                var f0 = Vector256.Create(c0);
                var f1 = Vector256.Create(c1);
                var f2 = Vector256.Create(c2);
                var f3 = Vector256.Create(c3);
                return RoundNarrow12(
                    Multiply(t0, f0) + Multiply(t1, f1) + Multiply(t2, f2) + Multiply(t3, f3),
                    Multiply(o0, f0) + Multiply(o1, f1) + Multiply(o2, f2) + Multiply(o3, f3));
            }
        }

        /// <summary><c>Round2( x, shift )</c> of the lanes, for a shift of at least 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> ShiftRightArithmeticRounded(Vector256<int> value, int shift) => (value >> shift) + ((value >> (shift - 1)) & Vector256<int>.One);

        // The 64-bit products of the lanes 0, 2, 4 and 6 of value and of factor, whose lanes are equal
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<long> Multiply(Vector256<int> value, Vector256<int> factor) => Avx2.Multiply(value, factor);

        // The lanes 1, 3, 5 and 7 in the lanes 0, 2, 4 and 6, for Multiply
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<int> Odd(Vector256<int> value) => Avx2.ShiftRightLogical(value.AsInt64(), 32).AsInt32();

        // (int)Round2( x, 12 ) of the even and the odd lanes: the bits 12 to 43 of the rounded values, the odd ones in the
        // upper halves of the 64-bit lanes of odd shifted left by 20 bits
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<int> RoundNarrow12(Vector256<long> even, Vector256<long> odd)
        {
            var rounding = Vector256.Create(1L << 11);
            return Avx2.Blend(Vector256.ShiftRightLogical(even + rounding, 12).AsInt32(), Vector256.ShiftLeft(odd + rounding, 20).AsInt32(), 0xAA);
        }
    }
}
