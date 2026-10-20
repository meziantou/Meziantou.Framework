using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the encoder: the inverses of the transforms of the decoding process (AV1 specification,
/// section 7.13), which is the only direction that the specification describes.
/// </summary>
internal static class Av1ForwardTransform
{
    // The fractional bits of the coefficients before their last rounding
    private const int FractionBits = 4;

    // The columns are scaled so that their outputs are below 2^ColumnBits for samples of 12 bits: the column transforms of
    // 2^n points multiply the magnitudes by at most 2^n
    private const int ColumnBits = 22;

    private static readonly int[] SquaredGains = MeasureSquaredGains();

    /// <summary>The one-dimensional operations of the transforms, on the samples of a line or on 4 lines at once.</summary>
    /// <typeparam name="T">A sample, or a vector of the samples of 4 lines.</typeparam>
    private interface ILanes<T>
        where T : unmanaged
    {
        static abstract T Add(T left, T right);

        static abstract T Subtract(T left, T right);

        static abstract T Negate(T value);

        static abstract T ShiftLeft(T value, int count);

        /// <summary><c>Round2( value * factor, 12 )</c>, with a 64-bit product.</summary>
        static abstract T MultiplyRound12(T value, int factor);

        /// <summary><c>Round2( value, shift )</c>, for a shift of at least 1.</summary>
        static abstract T ShiftRightRounded(T value, int shift);

        /// <summary>
        /// The rotation of the decoder (section 7.13.2.1): <c>Round2( a * cos - b * sin, 12 )</c> and
        /// <c>Round2( a * sin + b * cos, 12 )</c>, with 64-bit products.
        /// </summary>
        static abstract void Rotate(T a, T b, int cos, int sin, out T x, out T y);

        /// <summary>The transpose of the inverse ADST of 4 points (section 7.13.2.6).</summary>
        static abstract void ForwardAdst4(Span<T> t);
    }

    /// <summary>
    /// Returns the squared gain of the transforms of a size, with 8 fractional bits: the ratio between the sum of the
    /// squares of the coefficients of a residual and the sum of the squares of its samples. The transforms of a size have
    /// the same gain whatever their type, so a squared error of coefficients divided by the gain is the squared error of
    /// the samples, which is how the transform types of a block are compared without reconstructing it.
    /// </summary>
    public static int GetSquaredGain(int txSz) => SquaredGains[txSz];

    /// <summary>
    /// Transforms the residual of a transform block into coefficients, in the scale of the dequantized coefficients: the
    /// coefficients that the inverse transform process (section 7.13.3) turns back into the residual, up to its rounding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one-dimensional inverse transforms of the specification are sequences of rotations (<c>B</c>), of Hadamard
    /// rotations (<c>H</c>) and of permutations, whose matrix M has orthogonal columns of squared norm 2^n / 2 for 2^n points,
    /// up to the precision of the integer rotations (7 parts in 10,000). The forward transform of a kernel is its transpose
    /// divided by 2^n / 2, and the transpose is the same steps in the reverse order, each one transposed: a Hadamard rotation
    /// and a flipped rotation are symmetric, a rotation that is not flipped is the rotation of the opposite angle, and a
    /// permutation is undone. Like the decoder, each rotation rounds its outputs to integers.
    /// </para>
    /// <para>
    /// The inverse process scales the coefficients of a 2:1 rectangle by 1/sqrt(2), shifts the output of the row transforms
    /// by <c>Transform_Row_Shift</c> and the output of the column transforms by 4, and mirrors the residual of the
    /// <c>FLIPADST</c> types: the forward transform undoes each of these. Its columns are transformed first, from samples
    /// shifted left so that the roundings of the rotations stay below the precision of the coefficients, and its rows end
    /// with <see cref="FractionBits"/> fractional bits. The samples of the residual are differences of samples of at most
    /// 12 bits: every value stays below 2^27.
    /// </para>
    /// </remarks>
    /// <param name="residual">The residual: height rows of width samples.</param>
    /// <param name="coefficients"><c>Dequant</c>: Min(32, height) rows of Min(32, width) coefficients.</param>
    /// <param name="txSz">The transform size.</param>
    /// <param name="txType">The transform type (<c>PlaneTxType</c>).</param>
    public static void Forward(ReadOnlySpan<int> residual, Span<int> coefficients, int txSz, int txType)
    {
        if (Av1Simd.IsSupported)
        {
            ForwardVector(residual, coefficients, txSz, txType);
        }
        else
        {
            ForwardScalar(residual, coefficients, txSz, txType);
        }
    }

    /// <summary>The scalar reference of <see cref="Forward"/>.</summary>
    internal static void ForwardScalar(ReadOnlySpan<int> residual, Span<int> coefficients, int txSz, int txType)
    {
        var layout = new Layout(txSz, txType);
        var w = layout.Width;
        var h = layout.Height;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);

        // The columns: th outputs per column, in the order of the rows of the residual (the FLIPADST of the rows is undone
        // when the rows are read)
        Span<int> intermediate = stackalloc int[th * w];
        Span<int> line = stackalloc int[64];
        for (var c = 0; c < w; c++)
        {
            var column = line[..h];
            for (var r = 0; r < h; r++)
            {
                column[r] = residual[((layout.FlipUD ? h - 1 - r : r) * w) + c] << layout.ColumnShift;
            }

            Apply<ScalarLanes, int>(layout.ColumnKind, column, layout.Log2H);
            for (var k = 0; k < th; k++)
            {
                intermediate[(k * w) + c] = ScalarLanes.ShiftRightRounded(column[k], layout.MiddleShift);
            }
        }

        for (var k = 0; k < th; k++)
        {
            var row = line[..w];
            var source = intermediate.Slice(k * w, w);
            for (var c = 0; c < w; c++)
            {
                row[c] = source[layout.FlipLR ? w - 1 - c : c];
            }

            Apply<ScalarLanes, int>(layout.RowKind, row, layout.Log2W);
            for (var l = 0; l < tw; l++)
            {
                coefficients[(k * tw) + l] = Finish<ScalarLanes, int>(row[l], layout.IsRectangular2To1);
            }
        }
    }

    // The steps of ForwardScalar, on 4 columns then on 4 rows at once. The outputs of the columns are transposed by groups
    // of 4 (the vector c of the group g holds the outputs 4g to 4g + 3 of the column c), so that the lanes of the vectors of
    // a row group are 4 rows, which are transformed in place; their outputs are transposed back into the rows of the
    // coefficients
    [SkipLocalsInit]
    private static void ForwardVector(ReadOnlySpan<int> residual, Span<int> coefficients, int txSz, int txType)
    {
        var layout = new Layout(txSz, txType);
        var w = layout.Width;
        var h = layout.Height;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        residual = residual[..(w * h)];
        coefficients = coefficients[..(tw * th)];

        // Every element is written before it is read: the column kernels read the h vectors that are loaded first, and the
        // column groups write the th / 4 groups of w vectors that the row kernels read
        Span<Vector128<int>> column = unsafe(stackalloc Vector128<int>[64])[..h];
        Span<Vector128<int>> intermediate = unsafe(stackalloc Vector128<int>[th / 4 * w]);

        // The rows of the residual in the order of the column kernel, from the last one for FLIPADST: every load is inside
        // the residual (w x h samples)
        var firstRow = layout.FlipUD ? (h - 1) * w : 0;
        var rowStep = layout.FlipUD ? -w : w;
        ref var samples = ref unsafe(MemoryMarshal.GetReference(residual));
        for (var c = 0; c < w; c += 4)
        {
            var offset = firstRow + c;
            for (var r = 0; r < h; r++, offset += rowStep)
            {
                column[r] = unsafe(Vector128.LoadUnsafe(ref samples, (nuint)offset)) << layout.ColumnShift;
            }

            Apply<VectorLanes, Vector128<int>>(layout.ColumnKind, column, layout.Log2H);
            for (var k = 0; k < th; k += 4)
            {
                var group = intermediate.Slice((k >> 2) * w, w);
                Av1Simd.Transpose(
                    VectorLanes.ShiftRightRounded(column[k], layout.MiddleShift),
                    VectorLanes.ShiftRightRounded(column[k + 1], layout.MiddleShift),
                    VectorLanes.ShiftRightRounded(column[k + 2], layout.MiddleShift),
                    VectorLanes.ShiftRightRounded(column[k + 3], layout.MiddleShift),
                    out group[c],
                    out group[c + 1],
                    out group[c + 2],
                    out group[c + 3]);
            }
        }

        for (var k = 0; k < th; k += 4)
        {
            var row = intermediate.Slice((k >> 2) * w, w);
            if (layout.FlipLR)
            {
                for (var c = 0; c < w / 2; c++)
                {
                    (row[c], row[w - 1 - c]) = (row[w - 1 - c], row[c]);
                }
            }

            Apply<VectorLanes, Vector128<int>>(layout.RowKind, row, layout.Log2W);
            var output = coefficients.Slice(k * tw, 4 * tw);
            for (var l = 0; l < tw; l += 4)
            {
                Av1Simd.Transpose(
                    Finish<VectorLanes, Vector128<int>>(row[l], layout.IsRectangular2To1),
                    Finish<VectorLanes, Vector128<int>>(row[l + 1], layout.IsRectangular2To1),
                    Finish<VectorLanes, Vector128<int>>(row[l + 2], layout.IsRectangular2To1),
                    Finish<VectorLanes, Vector128<int>>(row[l + 3], layout.IsRectangular2To1),
                    out var row0,
                    out var row1,
                    out var row2,
                    out var row3);
                row0.CopyTo(output[l..]);
                row1.CopyTo(output[(tw + l)..]);
                row2.CopyTo(output[((2 * tw) + l)..]);
                row3.CopyTo(output[((3 * tw) + l)..]);
            }
        }
    }

    // The output of a row transform in the scale of the coefficients: sqrt(2) for a 2:1 rectangle, then the rounding of the
    // fractional bits
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T Finish<TLanes, T>(T value, bool isRectangular2To1)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        if (isRectangular2To1)
            value = TLanes.MultiplyRound12(value, 5793);

        return TLanes.ShiftRightRounded(value, FractionBits);
    }

    private static void Apply<TLanes, T>(Av1InverseTransform.Kind kind, Span<T> t, int n)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        switch (kind)
        {
            case Av1InverseTransform.Kind.Dct:
                switch (n)
                {
                    case 2:
                        ForwardDct4<TLanes, T>(t);
                        break;
                    case 3:
                        ForwardDct8<TLanes, T>(t);
                        break;
                    case 4:
                        ForwardDct16<TLanes, T>(t);
                        break;
                    default:
                        ForwardDct<TLanes, T>(t, n);
                        break;
                }

                break;
            case Av1InverseTransform.Kind.Adst:
                switch (n)
                {
                    case 2:
                        TLanes.ForwardAdst4(t);
                        break;
                    case 3:
                        ForwardAdst8<TLanes, T>(t);
                        break;
                    default:
                        ForwardAdst16<TLanes, T>(t);
                        break;
                }

                break;
            default:
                ForwardIdentity<TLanes, T>(t, n);
                break;
        }
    }

    // The Hadamard rotation H( a, b, flip, r ) without its clamp: the matrix of the rotation is symmetric, so it is its own
    // transpose
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void H<TLanes, T>(Span<T> t, int a, int b, bool flip)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        if (flip)
            (a, b) = (b, a);

        var x = t[a];
        var y = t[b];
        t[a] = TLanes.Add(x, y);
        t[b] = TLanes.Subtract(x, y);
    }

    // The butterfly rotation B( a, b, angle, flip ) of the decoder: a flipped rotation is symmetric, the transpose of a
    // rotation that is not flipped is the rotation of the opposite angle, which the callers give
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void B<TLanes, T>(Span<T> t, int a, int b, int angle, bool flip)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        TLanes.Rotate(t[a], t[b], Cos128(angle), Cos128(angle - 64), out var x, out var y);
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

    // cos128( angle ) of section 7.13.2.1, a constant when the angle is one
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Cos128(int angle) => Cos128Values[angle & 255];

    /// <summary>
    /// cos128( angle ) for the angles 0 to 255: <c>Cos128_Lookup</c> (section 7.13.2.1) extended by the symmetries of the
    /// cosine, which <c>Av1ForwardTransformTests</c> checks against the table of the specification.
    /// </summary>
    internal static ReadOnlySpan<short> Cos128Values =>
    [
        4096, 4095, 4091, 4085, 4076, 4065, 4052, 4036, 4017, 3996, 3973, 3948, 3920, 3889, 3857, 3822,
        3784, 3745, 3703, 3659, 3612, 3564, 3513, 3461, 3406, 3349, 3290, 3229, 3166, 3102, 3035, 2967,
        2896, 2824, 2751, 2675, 2598, 2520, 2440, 2359, 2276, 2191, 2106, 2019, 1931, 1842, 1751, 1660,
        1567, 1474, 1380, 1285, 1189, 1092, 995, 897, 799, 700, 601, 501, 401, 301, 201, 101,
        0, -101, -201, -301, -401, -501, -601, -700, -799, -897, -995, -1092, -1189, -1285, -1380, -1474,
        -1567, -1660, -1751, -1842, -1931, -2019, -2106, -2191, -2276, -2359, -2440, -2520, -2598, -2675, -2751, -2824,
        -2896, -2967, -3035, -3102, -3166, -3229, -3290, -3349, -3406, -3461, -3513, -3564, -3612, -3659, -3703, -3745,
        -3784, -3822, -3857, -3889, -3920, -3948, -3973, -3996, -4017, -4036, -4052, -4065, -4076, -4085, -4091, -4095,
        -4096, -4095, -4091, -4085, -4076, -4065, -4052, -4036, -4017, -3996, -3973, -3948, -3920, -3889, -3857, -3822,
        -3784, -3745, -3703, -3659, -3612, -3564, -3513, -3461, -3406, -3349, -3290, -3229, -3166, -3102, -3035, -2967,
        -2896, -2824, -2751, -2675, -2598, -2520, -2440, -2359, -2276, -2191, -2106, -2019, -1931, -1842, -1751, -1660,
        -1567, -1474, -1380, -1285, -1189, -1092, -995, -897, -799, -700, -601, -501, -401, -301, -201, -101,
        0, 101, 201, 301, 401, 501, 601, 700, 799, 897, 995, 1092, 1189, 1285, 1380, 1474,
        1567, 1660, 1751, 1842, 1931, 2019, 2106, 2191, 2276, 2359, 2440, 2520, 2598, 2675, 2751, 2824,
        2896, 2967, 3035, 3102, 3166, 3229, 3290, 3349, 3406, 3461, 3513, 3564, 3612, 3659, 3703, 3745,
        3784, 3822, 3857, 3889, 3920, 3948, 3973, 3996, 4017, 4036, 4052, 4065, 4076, 4085, 4091, 4095,
    ];

    // The transposes of InverseDct (section 7.13.2.3): its steps from the last to the first, which ForwardDct lists for every
    // size. The kernels of 4 to 16 points are written out: the steps of their sizes, with constant indices and angles
    private static void ForwardDct4<TLanes, T>(Span<T> t)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        t = t[..4];
        H<TLanes, T>(t, 0, 3, flip: false);
        H<TLanes, T>(t, 1, 2, flip: false);
        B<TLanes, T>(t, 0, 1, 32, flip: true);
        B<TLanes, T>(t, 2, 3, -48, flip: false);
        (t[1], t[2]) = (t[2], t[1]);
    }

    private static void ForwardDct8<TLanes, T>(Span<T> t)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        t = t[..8];
        H<TLanes, T>(t, 0, 7, flip: false);
        H<TLanes, T>(t, 1, 6, flip: false);
        H<TLanes, T>(t, 2, 5, flip: false);
        H<TLanes, T>(t, 3, 4, flip: false);
        B<TLanes, T>(t, 6, 5, 32, flip: true);
        H<TLanes, T>(t, 0, 3, flip: false);
        H<TLanes, T>(t, 1, 2, flip: false);
        H<TLanes, T>(t, 4, 5, flip: false);
        H<TLanes, T>(t, 6, 7, flip: true);
        B<TLanes, T>(t, 0, 1, 32, flip: true);
        B<TLanes, T>(t, 2, 3, -48, flip: false);
        B<TLanes, T>(t, 4, 7, -56, flip: false);
        B<TLanes, T>(t, 5, 6, -24, flip: false);
        (t[1], t[4]) = (t[4], t[1]);
        (t[3], t[6]) = (t[6], t[3]);
    }

    private static void ForwardDct16<TLanes, T>(Span<T> t)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        t = t[..16];
        for (var i = 0; i < 8; i++)
        {
            H<TLanes, T>(t, i, 15 - i, flip: false);
        }

        B<TLanes, T>(t, 13, 10, 32, flip: true);
        B<TLanes, T>(t, 12, 11, 32, flip: true);
        H<TLanes, T>(t, 0, 7, flip: false);
        H<TLanes, T>(t, 1, 6, flip: false);
        H<TLanes, T>(t, 2, 5, flip: false);
        H<TLanes, T>(t, 3, 4, flip: false);
        H<TLanes, T>(t, 8, 11, flip: false);
        H<TLanes, T>(t, 9, 10, flip: false);
        H<TLanes, T>(t, 12, 15, flip: true);
        H<TLanes, T>(t, 13, 14, flip: true);
        B<TLanes, T>(t, 6, 5, 32, flip: true);
        H<TLanes, T>(t, 0, 3, flip: false);
        H<TLanes, T>(t, 1, 2, flip: false);
        B<TLanes, T>(t, 14, 9, 48, flip: true);
        B<TLanes, T>(t, 13, 10, 112, flip: true);
        H<TLanes, T>(t, 4, 5, flip: false);
        H<TLanes, T>(t, 6, 7, flip: true);
        B<TLanes, T>(t, 0, 1, 32, flip: true);
        B<TLanes, T>(t, 2, 3, -48, flip: false);
        H<TLanes, T>(t, 8, 9, flip: false);
        H<TLanes, T>(t, 10, 11, flip: true);
        H<TLanes, T>(t, 12, 13, flip: false);
        H<TLanes, T>(t, 14, 15, flip: true);
        B<TLanes, T>(t, 4, 7, -56, flip: false);
        B<TLanes, T>(t, 5, 6, -24, flip: false);
        B<TLanes, T>(t, 8, 15, -60, flip: false);
        B<TLanes, T>(t, 9, 14, -28, flip: false);
        B<TLanes, T>(t, 10, 13, -44, flip: false);
        B<TLanes, T>(t, 11, 12, -12, flip: false);
        (t[1], t[8]) = (t[8], t[1]);
        (t[2], t[4]) = (t[4], t[2]);
        (t[3], t[12]) = (t[12], t[3]);
        (t[5], t[10]) = (t[10], t[5]);
        (t[7], t[14]) = (t[14], t[7]);
        (t[11], t[13]) = (t[13], t[11]);
    }

    // The kernels of 32 and 64 points: every step of InverseDct, from the last to the first, with the bit reversal of its
    // first step last
    private static void ForwardDct<TLanes, T>(Span<T> t, int n)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        if (n is 6)
        {
            for (var i = 0; i < 32; i++)
            {
                H<TLanes, T>(t, i, 63 - i, flip: false);
            }

            for (var i = 0; i < 8; i++)
            {
                B<TLanes, T>(t, 55 - i, 40 + i, 32, flip: true);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 16; i++)
            {
                H<TLanes, T>(t, i, 31 - i, flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                H<TLanes, T>(t, 32 + i, 47 - i, flip: false);
                H<TLanes, T>(t, 48 + i, 63 - i, flip: true);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                B<TLanes, T>(t, 27 - i, 20 + i, 32, flip: true);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 8; i++)
            {
                H<TLanes, T>(t, i, 15 - i, flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                B<TLanes, T>(t, 59 - i, 36 + i, i < 4 ? 48 : 112, flip: true);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    H<TLanes, T>(t, 16 + (i * 8) + j, 23 + (i * 8) - j, i is not 0);
                }
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                B<TLanes, T>(t, 13 - i, 10 + i, 32, flip: true);
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 4; i++)
            {
                H<TLanes, T>(t, i, 7 - i, flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    H<TLanes, T>(t, 32 + (8 * i) + j, 39 + (8 * i) - j, (i & 1) is not 0);
                }
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                B<TLanes, T>(t, 29 - i, 18 + i, 48 + ((i >> 1) * 64), flip: true);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    H<TLanes, T>(t, 8 + (4 * i) + j, 11 + (4 * i) - j, i is not 0);
                }
            }
        }

        if (n >= 3)
            B<TLanes, T>(t, 6, 5, 32, flip: true);

        for (var i = 0; i < 2; i++)
        {
            H<TLanes, T>(t, i, 3 - i, flip: false);
        }

        if (n is 6)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    B<TLanes, T>(t, 61 - (i * 8) - j, 34 + (i * 8) + j, 56 - (i * 32) + ((j >> 1) * 64), flip: true);
                }
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    H<TLanes, T>(t, 16 + (4 * i) + j, 19 + (4 * i) - j, (i & 1) is not 0);
                }
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 2; i++)
            {
                B<TLanes, T>(t, 14 - i, 9 + i, 48 + (64 * i), flip: true);
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 2; i++)
            {
                H<TLanes, T>(t, 4 + (2 * i), 5 + (2 * i), i is not 0);
            }
        }

        B<TLanes, T>(t, 0, 1, 32, flip: true);
        B<TLanes, T>(t, 2, 3, -48, flip: false);
        if (n is 6)
        {
            for (var i = 0; i < 8; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    H<TLanes, T>(t, 32 + (i * 4) + j, 35 + (i * 4) - j, (i & 1) is not 0);
                }
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    B<TLanes, T>(t, 30 - (4 * i) - j, 17 + (4 * i) + j, 24 + (j << 6) + ((1 - i) << 5), flip: true);
                }
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 4; i++)
            {
                H<TLanes, T>(t, 8 + (2 * i), 9 + (2 * i), (i & 1) is not 0);
            }
        }

        if (n >= 3)
        {
            for (var i = 0; i < 2; i++)
            {
                B<TLanes, T>(t, 4 + i, 7 - i, -(56 - (32 * i)), flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    B<TLanes, T>(t, 62 - (i * 4) - j, 33 + (i * 4) + j, 60 - (16 * BitReverse(2, i)) + (64 * j), flip: true);
                }
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 8; i++)
            {
                H<TLanes, T>(t, 16 + (2 * i), 17 + (2 * i), (i & 1) is not 0);
            }
        }

        if (n >= 4)
        {
            for (var i = 0; i < 4; i++)
            {
                B<TLanes, T>(t, 8 + i, 15 - i, -(12 + (BitReverse(2, 3 - i) << 4)), flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 16; i++)
            {
                H<TLanes, T>(t, 32 + (i * 2), 33 + (i * 2), (i & 1) is not 0);
            }
        }

        if (n >= 5)
        {
            for (var i = 0; i < 8; i++)
            {
                B<TLanes, T>(t, 16 + i, 31 - i, -(6 + (BitReverse(3, 7 - i) << 3)), flip: false);
            }
        }

        if (n is 6)
        {
            for (var i = 0; i < 16; i++)
            {
                B<TLanes, T>(t, 32 + i, 63 - i, -(63 - (4 * BitReverse(4, i))), flip: false);
            }
        }

        var length = 1 << n;
        for (var i = 0; i < length; i++)
        {
            var j = BitReverse(n, i);
            if (j > i)
                (t[i], t[j]) = (t[j], t[i]);
        }
    }

    // The transpose of InverseAdst8 (section 7.13.2.7): every rotation is flipped
    private static void ForwardAdst8<TLanes, T>(Span<T> t)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        t = t[..8];
        UndoAdstOutputPermutation8<TLanes, T>(t);
        B<TLanes, T>(t, 2, 3, 32, flip: true);
        B<TLanes, T>(t, 6, 7, 32, flip: true);
        H<TLanes, T>(t, 0, 2, flip: false);
        H<TLanes, T>(t, 4, 6, flip: false);
        H<TLanes, T>(t, 1, 3, flip: false);
        H<TLanes, T>(t, 5, 7, flip: false);
        B<TLanes, T>(t, 4, 5, 48, flip: true);
        B<TLanes, T>(t, 7, 6, 16, flip: true);
        H<TLanes, T>(t, 0, 4, flip: false);
        H<TLanes, T>(t, 1, 5, flip: false);
        H<TLanes, T>(t, 2, 6, flip: false);
        H<TLanes, T>(t, 3, 7, flip: false);
        B<TLanes, T>(t, 0, 1, 60, flip: true);
        B<TLanes, T>(t, 2, 3, 44, flip: true);
        B<TLanes, T>(t, 4, 5, 28, flip: true);
        B<TLanes, T>(t, 6, 7, 12, flip: true);
        UndoAdstInputPermutation8(t);
    }

    // The transpose of InverseAdst16 (section 7.13.2.8): every rotation is flipped
    private static void ForwardAdst16<TLanes, T>(Span<T> t)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        t = t[..16];
        UndoAdstOutputPermutation16<TLanes, T>(t);
        B<TLanes, T>(t, 2, 3, 32, flip: true);
        B<TLanes, T>(t, 6, 7, 32, flip: true);
        B<TLanes, T>(t, 10, 11, 32, flip: true);
        B<TLanes, T>(t, 14, 15, 32, flip: true);
        for (var i = 0; i < 2; i++)
        {
            H<TLanes, T>(t, i, 2 + i, flip: false);
            H<TLanes, T>(t, 4 + i, 6 + i, flip: false);
            H<TLanes, T>(t, 8 + i, 10 + i, flip: false);
            H<TLanes, T>(t, 12 + i, 14 + i, flip: false);
        }

        B<TLanes, T>(t, 4, 5, 48, flip: true);
        B<TLanes, T>(t, 12, 13, 48, flip: true);
        B<TLanes, T>(t, 7, 6, 16, flip: true);
        B<TLanes, T>(t, 15, 14, 16, flip: true);
        for (var i = 0; i < 4; i++)
        {
            H<TLanes, T>(t, i, 4 + i, flip: false);
            H<TLanes, T>(t, 8 + i, 12 + i, flip: false);
        }

        B<TLanes, T>(t, 8, 9, 56, flip: true);
        B<TLanes, T>(t, 13, 12, 8, flip: true);
        B<TLanes, T>(t, 10, 11, 24, flip: true);
        B<TLanes, T>(t, 15, 14, 40, flip: true);
        for (var i = 0; i < 8; i++)
        {
            H<TLanes, T>(t, i, 8 + i, flip: false);
        }

        B<TLanes, T>(t, 0, 1, 62, flip: true);
        B<TLanes, T>(t, 2, 3, 54, flip: true);
        B<TLanes, T>(t, 4, 5, 46, flip: true);
        B<TLanes, T>(t, 6, 7, 38, flip: true);
        B<TLanes, T>(t, 8, 9, 30, flip: true);
        B<TLanes, T>(t, 10, 11, 22, flip: true);
        B<TLanes, T>(t, 12, 13, 14, flip: true);
        B<TLanes, T>(t, 14, 15, 6, flip: true);
        UndoAdstInputPermutation16(t);
    }

    // The transposes of the inverse ADST output array permutation (section 7.13.2.5), which reads T[ i ] from the index idx,
    // negated for the odd values of i: T[ idx ] is the value i
    private static void UndoAdstOutputPermutation8<TLanes, T>(Span<T> t)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        t = t[..8];
        var v1 = t[1];
        var v2 = t[2];
        var v3 = t[3];
        var v4 = t[4];
        var v5 = t[5];
        var v6 = t[6];
        var v7 = t[7];
        t[1] = TLanes.Negate(v7);
        t[2] = TLanes.Negate(v3);
        t[3] = v4;
        t[4] = TLanes.Negate(v1);
        t[5] = v6;
        t[6] = v2;
        t[7] = TLanes.Negate(v5);
    }

    private static void UndoAdstOutputPermutation16<TLanes, T>(Span<T> t)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
    {
        t = t[..16];
        var v1 = t[1];
        var v2 = t[2];
        var v3 = t[3];
        var v4 = t[4];
        var v5 = t[5];
        var v6 = t[6];
        var v7 = t[7];
        var v8 = t[8];
        var v9 = t[9];
        var v10 = t[10];
        var v11 = t[11];
        var v12 = t[12];
        var v13 = t[13];
        var v14 = t[14];
        var v15 = t[15];
        t[1] = TLanes.Negate(v15);
        t[2] = TLanes.Negate(v7);
        t[3] = v8;
        t[4] = TLanes.Negate(v3);
        t[5] = v12;
        t[6] = v4;
        t[7] = TLanes.Negate(v11);
        t[8] = TLanes.Negate(v1);
        t[9] = v14;
        t[10] = v6;
        t[11] = TLanes.Negate(v9);
        t[12] = v2;
        t[13] = TLanes.Negate(v13);
        t[14] = TLanes.Negate(v5);
        t[15] = v10;
    }

    // The transposes of the inverse ADST input array permutation (section 7.13.2.4), which reads T[ i ] from the index idx:
    // T[ idx ] is the value i
    private static void UndoAdstInputPermutation8<T>(Span<T> t)
        where T : unmanaged
    {
        t = t[..8];
        var v0 = t[0];
        var v1 = t[1];
        var v2 = t[2];
        var v3 = t[3];
        var v4 = t[4];
        var v5 = t[5];
        var v6 = t[6];
        var v7 = t[7];
        t[0] = v1;
        t[1] = v6;
        t[2] = v3;
        t[3] = v4;
        t[4] = v5;
        t[5] = v2;
        t[6] = v7;
        t[7] = v0;
    }

    private static void UndoAdstInputPermutation16<T>(Span<T> t)
        where T : unmanaged
    {
        t = t[..16];
        var v0 = t[0];
        var v1 = t[1];
        var v2 = t[2];
        var v3 = t[3];
        var v4 = t[4];
        var v5 = t[5];
        var v6 = t[6];
        var v7 = t[7];
        var v8 = t[8];
        var v9 = t[9];
        var v10 = t[10];
        var v11 = t[11];
        var v12 = t[12];
        var v13 = t[13];
        var v14 = t[14];
        var v15 = t[15];
        t[0] = v1;
        t[1] = v14;
        t[2] = v3;
        t[3] = v12;
        t[4] = v5;
        t[5] = v10;
        t[6] = v7;
        t[7] = v8;
        t[8] = v9;
        t[9] = v6;
        t[10] = v11;
        t[11] = v4;
        t[12] = v13;
        t[13] = v2;
        t[14] = v15;
        t[15] = v0;
    }

    // The low n bits of x in the reverse order
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BitReverse(int n, int x) => BitReverse6[x] >> (6 - n);

    private static ReadOnlySpan<byte> BitReverse6 =>
    [
        0, 32, 16, 48, 8, 40, 24, 56, 4, 36, 20, 52, 12, 44, 28, 60, 2, 34, 18, 50, 10, 42, 26, 58, 6, 38, 22, 54, 14, 46, 30, 62,
        1, 33, 17, 49, 9, 41, 25, 57, 5, 37, 21, 53, 13, 45, 29, 61, 3, 35, 19, 51, 11, 43, 27, 59, 7, 39, 23, 55, 15, 47, 31, 63,
    ];

    // The inverse identity transform (section 7.13.2.15) multiplies by a scale s whose square is 2^n / 2: it is its own
    // transpose
    private static void ForwardIdentity<TLanes, T>(Span<T> t, int n)
        where TLanes : struct, ILanes<T>
        where T : unmanaged
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

    // The gains are measured on the transform of a residual, here a fixed sequence of samples
    private static int[] MeasureSquaredGains()
    {
        var gains = new int[Av1Constants.TxSizesAll];
        Span<int> residual = stackalloc int[64 * 64];
        Span<int> coefficients = stackalloc int[32 * 32];
        for (var txSz = 0; txSz < gains.Length; txSz++)
        {
            int w = Av1Tables.TxWidth[txSz];
            int h = Av1Tables.TxHeight[txSz];
            var state = 12345u;
            long samples = 0;
            for (var i = 0; i < w * h; i++)
            {
                // A linear congruential generator: the same samples on every platform
                state = (state * 1664525) + 1013904223;
                residual[i] = (int)(state >> 24) - 128;
                samples += (long)residual[i] * residual[i];
            }

            var count = Math.Min(32, w) * Math.Min(32, h);
            Forward(residual[..(w * h)], coefficients[..count], txSz, Av1Constants.DctDct);
            long energy = 0;
            foreach (var coefficient in coefficients[..count])
            {
                energy += (long)coefficient * coefficient;
            }

            gains[txSz] = (int)Math.Max(1, ((energy << 8) + (samples / 2)) / samples);
        }

        return gains;
    }

    /// <summary>The shifts and the kernels of a transform size and type.</summary>
    /// <remarks>
    /// The coefficients are M_h^T X M_w / ((2^log2H / 2) (2^log2W / 2)) times the scale of the inverse process,
    /// 2^(4 + Transform_Row_Shift) (and sqrt(2) for a 2:1 rectangle): with S = Transform_Row_Shift + 6 - log2W - log2H, the
    /// product of the transposed kernels times 2^S. The columns are shifted left by <see cref="ColumnShift"/> and their outputs
    /// rounded by <see cref="MiddleShift"/> bits, which leaves <see cref="FractionBits"/> fractional bits after the rows:
    /// ColumnShift - MiddleShift - S is <see cref="FractionBits"/>. MiddleShift is log2W - Transform_Row_Shift, at least 1.
    /// </remarks>
    private readonly struct Layout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Layout(int txSz, int txType)
        {
            Log2W = Av1Tables.TxWidthLog2[txSz];
            Log2H = Av1Tables.TxHeightLog2[txSz];
            var rowShift = Av1Tables.TransformRowShift[txSz];
            ColumnShift = ColumnBits - 12 - Log2H;
            MiddleShift = ColumnShift - FractionBits - (rowShift + 6 - Log2W - Log2H);
            IsRectangular2To1 = Math.Abs(Log2W - Log2H) is 1;
            RowKind = Av1InverseTransform.GetRowKind(txType);
            ColumnKind = Av1InverseTransform.GetColumnKind(txType);
            FlipUD = txType is Av1Constants.FlipadstDct or Av1Constants.FlipadstAdst or Av1Constants.VFlipadst or Av1Constants.FlipadstFlipadst;
            FlipLR = txType is Av1Constants.DctFlipadst or Av1Constants.AdstFlipadst or Av1Constants.HFlipadst or Av1Constants.FlipadstFlipadst;
        }

        public int Log2W { get; }

        public int Log2H { get; }

        public int Width => 1 << Log2W;

        public int Height => 1 << Log2H;

        public int ColumnShift { get; }

        public int MiddleShift { get; }

        public bool IsRectangular2To1 { get; }

        public Av1InverseTransform.Kind RowKind { get; }

        public Av1InverseTransform.Kind ColumnKind { get; }

        public bool FlipUD { get; }

        public bool FlipLR { get; }
    }

    // The operations on one sample, with the 64-bit products of the decoder
    private readonly struct ScalarLanes : ILanes<int>
    {
        public static int Add(int left, int right) => left + right;

        public static int Subtract(int left, int right) => left - right;

        public static int Negate(int value) => -value;

        public static int ShiftLeft(int value, int count) => value << count;

        public static int MultiplyRound12(int value, int factor) => (int)((((long)value * factor) + 2048) >> 12);

        public static int ShiftRightRounded(int value, int shift) => (value + (1 << (shift - 1))) >> shift;

        public static void Rotate(int a, int b, int cos, int sin, out int x, out int y)
        {
            x = (int)(((((long)a * cos) - ((long)b * sin)) + 2048) >> 12);
            y = (int)(((((long)a * sin) + ((long)b * cos)) + 2048) >> 12);
        }

        // The transpose of the matrix of the inverse ADST4, whose rows are (Sinpi19, Sinpi39, Sinpi49, Sinpi29),
        // (Sinpi29, Sinpi39, -Sinpi19, -Sinpi49), (Sinpi39, 0, -Sinpi39, Sinpi39) and (Sinpi49, -Sinpi39, Sinpi29, -Sinpi19)
        public static void ForwardAdst4(Span<int> t)
        {
            long x0 = t[0];
            long x1 = t[1];
            long x2 = t[2];
            long x3 = t[3];
            t[0] = Round12((Av1Constants.Sinpi19 * x0) + (Av1Constants.Sinpi29 * x1) + (Av1Constants.Sinpi39 * x2) + (Av1Constants.Sinpi49 * x3));
            t[1] = Round12((Av1Constants.Sinpi39 * x0) + (Av1Constants.Sinpi39 * x1) - (Av1Constants.Sinpi39 * x3));
            t[2] = Round12((Av1Constants.Sinpi49 * x0) - (Av1Constants.Sinpi19 * x1) - (Av1Constants.Sinpi39 * x2) + (Av1Constants.Sinpi29 * x3));
            t[3] = Round12((Av1Constants.Sinpi29 * x0) - (Av1Constants.Sinpi49 * x1) + (Av1Constants.Sinpi39 * x2) - (Av1Constants.Sinpi19 * x3));

            static int Round12(long value) => (int)((value + 2048) >> 12);
        }
    }

    // The operations on the lanes of 4 lines: the rotations of the vector inverse transform, whose 64-bit products and
    // roundings are the scalar ones
    private readonly struct VectorLanes : ILanes<Vector128<int>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Add(Vector128<int> left, Vector128<int> right) => left + right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Subtract(Vector128<int> left, Vector128<int> right) => left - right;

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
        public static Vector128<int> ShiftRightRounded(Vector128<int> value, int shift) => (value + Vector128.Create(1 << (shift - 1))) >> shift;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Rotate(Vector128<int> a, Vector128<int> b, int cos, int sin, out Vector128<int> x, out Vector128<int> y)
        {
            var c = Vector128.Create(cos);
            var s = Vector128.Create(sin);
            x = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirst(a, c), b, s),
                Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecond(a, c), b, s));
            y = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirst(a, s), b, c),
                Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecond(a, s), b, c));
        }

        // The sums of products of ScalarLanes.ForwardAdst4, exact in 64 bits, for the first then the second lanes of the
        // split
        public static void ForwardAdst4(Span<Vector128<int>> t)
        {
            var x0 = t[0];
            var x1 = t[1];
            var x2 = t[2];
            var x3 = t[3];
            var sinpi19 = Vector128.Create(Av1Constants.Sinpi19);
            var sinpi29 = Vector128.Create(Av1Constants.Sinpi29);
            var sinpi39 = Vector128.Create(Av1Constants.Sinpi39);
            var sinpi49 = Vector128.Create(Av1Constants.Sinpi49);
            t[0] = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirst(x0, sinpi19), x1, sinpi29), x2, sinpi39), x3, sinpi49),
                Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecond(x0, sinpi19), x1, sinpi29), x2, sinpi39), x3, sinpi49));
            t[1] = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirst(x0, sinpi39), x1, sinpi39), x3, sinpi39),
                Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecond(x0, sinpi39), x1, sinpi39), x3, sinpi39));
            t[2] = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirst(x0, sinpi49), x1, sinpi19), x2, sinpi39), x3, sinpi29),
                Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecond(x0, sinpi49), x1, sinpi19), x2, sinpi39), x3, sinpi29));
            t[3] = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirst(x0, sinpi29), x1, sinpi49), x2, sinpi39), x3, sinpi19),
                Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecond(x0, sinpi29), x1, sinpi49), x2, sinpi39), x3, sinpi19));
        }
    }

    /// <summary>
    /// Transforms the 4x4 residual of a lossless block into the coefficients that the inverse Walsh-Hadamard transform turns
    /// back into exactly the same residual (the inverse of section 7.13.3 with <c>Lossless</c> equal to 1).
    /// </summary>
    /// <remarks>
    /// The inverse process transforms the rows then the columns, so the forward process inverts the columns then the rows.
    /// The dequantized coefficients of a lossless block are 4 times the coded ones (the quantizers of the quantizer index 0
    /// are 4), and the inverse row transform starts by dividing them by 4: the coded coefficients are the outputs of this
    /// method.
    /// </remarks>
    /// <param name="residual">The residual: 4 rows of 4 samples.</param>
    /// <param name="coefficients">The coefficients: 4 rows of 4 values.</param>
    public static void ForwardWalshHadamard4x4(ReadOnlySpan<int> residual, Span<int> coefficients)
    {
        Span<int> t = stackalloc int[4];
        for (var j = 0; j < 4; j++)
        {
            for (var i = 0; i < 4; i++)
            {
                t[i] = residual[(i * 4) + j];
            }

            ForwardWalshHadamard(t);
            for (var i = 0; i < 4; i++)
            {
                coefficients[(i * 4) + j] = t[i];
            }
        }

        for (var i = 0; i < 4; i++)
        {
            ForwardWalshHadamard(coefficients.Slice(i * 4, 4));
        }
    }

    /// <summary>
    /// The forward Walsh-Hadamard transform of 4 points: the steps of the inverse transform (section 7.13.2.10) undone in the
    /// reverse order. Every step adds to a value a function of the others, so each one is exactly reversible.
    /// </summary>
    internal static void ForwardWalshHadamard(Span<int> t)
    {
        // The inverse transform ends with T[ 0 ] = a, T[ 1 ] = b, T[ 2 ] = c, T[ 3 ] = d
        var a = t[0];
        var b = t[1];
        var c = t[2];
        var d = t[3];

        // d += c and a -= b
        d -= c;
        a += b;

        // c = e - c and b = e - b, with e = (a - d) >> 1 computed from the values that these steps do not change
        var e = (a - d) >> 1;
        c = e - c;
        b = e - b;

        // d -= b and a += c
        d += b;
        a -= c;

        // The inverse transform starts with a = T[ 0 ], c = T[ 1 ], d = T[ 2 ], b = T[ 3 ]
        t[0] = a;
        t[1] = c;
        t[2] = d;
        t[3] = b;
    }
}
