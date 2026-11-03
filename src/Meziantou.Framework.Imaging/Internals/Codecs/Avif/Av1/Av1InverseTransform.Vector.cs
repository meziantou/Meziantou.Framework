using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
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

    // The 2D transform of TransformScalar with the operations that give its results for the bit depth: the 32-bit products
    // of NarrowLanes128 and NarrowLanes256 when no product of the kernels can exceed 32 bits (IsNarrowExact, for inputs of
    // the dequantization range: inRange for the rows), the 64-bit products of Lanes128 and Lanes256 otherwise. The inputs of
    // the columns are clamped to the column range, which is 16 bits for a bit depth of at most 10
    // destination: when not empty, the predicted samples (from the top left sample of the block, destinationStride per row)
    // that the residual is added to, clamped to the samples of the bit depth, with the rows in reverse order for flipUD;
    // residual is then a scratch that is left with values of no use
    private static void TransformVector(ReadOnlySpan<int> dequant, Span<int> residual, int txSz, int txType, int bitDepth, int rows, int columns, bool inRange, Span<ushort> destination, int destinationStride, bool flipUD)
    {
        if (bitDepth > 10)
        {
            TransformVector<Lanes128, Lanes128, Lanes256>(dequant, residual, txSz, txType, bitDepth, rows, columns, destination, destinationStride, flipUD);
        }
        else if (inRange && IsNarrowExact(bitDepth + 8, GetRowKind(txType), Av1Tables.TxWidthLog2[txSz]))
        {
            TransformVector<NarrowLanes128, NarrowLanes128, NarrowLanes256>(dequant, residual, txSz, txType, bitDepth, rows, columns, destination, destinationStride, flipUD);
        }
        else
        {
            TransformVector<Lanes128, NarrowLanes128, NarrowLanes256>(dequant, residual, txSz, txType, bitDepth, rows, columns, destination, destinationStride, flipUD);
        }
    }

    /// <summary>
    /// Returns whether the one-dimensional kernel of <paramref name="kind"/> on 2^<paramref name="n"/> points computes every
    /// product and sum within 32 bits for inputs and clamps of <paramref name="r"/> bits: then the 32-bit operations of
    /// <see cref="NarrowLanes128"/> give the results of the 64-bit ones for every input.
    /// </summary>
    // The bounds of the magnitudes, propagated through the steps of the kernels (inputs and H outputs below 2^(r - 1), the
    // outputs of B at most sqrt(2) times their inputs, the products by the 12-bit constants): for r = 16, every product and
    // sum is below 2^29.2 (the 4-point ADST, whose last output sums 3 products of an input); for r = 18, below 2^30.5 except
    // the 4-point ADST, which may reach 2^31.2
    internal static bool IsNarrowExact(int r, Kind kind, int n) => r <= 16 || (r <= 18 && !(kind is Kind.Adst && n is 2));

    // The 2D transform of TransformScalar: the rows by groups of 4 (transposed into the lanes of the vectors), then the
    // columns by groups of 4 (whose samples are the lanes of consecutive rows), or by groups of 8 with AVX2. Only the first
    // rows rows and columns columns of coefficients may not be zero: the other rows transform to zeros, and the DCT of rows
    // or columns whose last inputs are zero is InverseDctSparse, which does not read them
    [SkipLocalsInit]
    private static void TransformVector<TRows, TColumns, TColumns256>(ReadOnlySpan<int> dequant, Span<int> residual, int txSz, int txType, int bitDepth, int rows, int columns, Span<ushort> destination, int destinationStride, bool flipUD)
        where TRows : struct, ILanes<Vector128<int>>
        where TColumns : struct, ILanes<Vector128<int>>
        where TColumns256 : struct, ILanes<Vector256<int>>
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
        var sparseRowInputs = GetSparseInputs(rowKind, log2W, columns);

        // Every element is written before it is read: the kernels read the first 2^n elements, which are loaded first
        Span<Vector128<int>> t = unsafe(stackalloc Vector128<int>[64]);
        var lastRow = -1;
        var rowGroups = Math.Min(h, (rows + 3) & ~3);
        for (var i = 0; i < rowGroups; i += 4)
        {
            var rowSamples = residual.Slice(i * w, 4 * w);
            var any = false;
            for (var r = 0; r < 4 && i + r < rows; r++)
            {
                if (dequant.Slice((i + r) * tw, columns).ContainsAnyExcept(0))
                {
                    any = true;
                    lastRow = i + r;
                }
            }

            if (!any)
            {
                // A row of zero coefficients transforms to zeros
                rowSamples.Clear();
                continue;
            }

            var loaded = sparseRowInputs is 0 ? tw : sparseRowInputs;
            for (var k = 0; k < loaded; k += 4)
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

            if (sparseRowInputs is 0)
                t[tw..w].Clear();

            if (isRectangular2To1)
            {
                // The zeros after the coefficients stay zeros
                for (var k = 0; k < loaded; k++)
                {
                    t[k] = TRows.MultiplyRound12(t[k], 2896);
                }
            }

            if (sparseRowInputs is not 0)
            {
                InverseDctSparse<TRows, Vector128<int>>(t, log2W, rowClampRange, sparseRowInputs);
            }
            else
            {
                Apply<TRows, Vector128<int>>(rowKind, t[..w], log2W, rowClampRange);
            }

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
                row0.CopyTo(rowSamples[k..]);
                row1.CopyTo(rowSamples[(w + k)..]);
                row2.CopyTo(rowSamples[((2 * w) + k)..]);
                row3.CopyTo(rowSamples[((3 * w) + k)..]);
            }
        }

        var add = !destination.IsEmpty;
        if (lastRow < 0)
        {
            // A residual of zeros changes no sample
            if (!add)
                residual[(rowGroups * w)..].Clear();

            return;
        }

        if (lastRow is 0 && !residual[..w].ContainsAnyExcept(residual[0]))
        {
            TransformConstantRows(residual, w, h, log2H, colKind, colClampRange, lossless: false, ColumnShift);
            if (add)
            {
                var maxSample = (1 << bitDepth) - 1;
                for (var i = 0; i < h; i++)
                {
                    Av1Reconstruction.AddConstant(destination, destinationStride, 0, flipUD ? h - 1 - i : i, w, 1, residual[i * w], maxSample);
                }
            }

            return;
        }

        // The rows after the last group of coefficients are zeros, which the sparse columns do not read
        var sparseColumnInputs = GetSparseInputs(colKind, log2H, lastRow + 1);
        if (sparseColumnInputs is 0)
            residual[(rowGroups * w)..].Clear();

        if (Avx2.IsSupported && w >= 8)
        {
            TransformColumns256<TColumns256>(residual, w, h, log2H, colKind, colClampRange, sparseColumnInputs, destination, destinationStride, flipUD, (1 << bitDepth) - 1);
            return;
        }

        var maxSamples = Vector128.Create((1 << bitDepth) - 1);

        var columnLength = sparseColumnInputs is 0 ? h : sparseColumnInputs;
        if (add && AdvSimd.Arm64.IsSupported && w >= 8)
        {
            TransformColumnPairsAdd<TColumns>(residual, t, w, h, log2H, colKind, colClampRange, sparseColumnInputs, destination, destinationStride, flipUD, (1 << bitDepth) - 1);
            return;
        }

        for (var j = 0; j < w; j += 4)
        {
            var column = t[..h];
            for (var i = 0; i < columnLength; i++)
            {
                column[i] = Vector128.Create(residual.Slice((i * w) + j, 4));
            }

            if (sparseColumnInputs is not 0)
            {
                InverseDctSparse<TColumns, Vector128<int>>(column, log2H, colClampRange, sparseColumnInputs);
            }
            else
            {
                Apply<TColumns, Vector128<int>>(colKind, column, log2H, colClampRange);
            }

            if (add)
            {
                for (var i = 0; i < h; i++)
                {
                    AddResidual4(destination, ((flipUD ? h - 1 - i : i) * destinationStride) + j, Av1Simd.ShiftRightArithmeticRounded(column[i], ColumnShift), maxSamples);
                }

                continue;
            }

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
    private static void TransformColumns256<TLanes>(Span<int> residual, int w, int h, int log2H, Kind colKind, int colClampRange, int sparseInputs, Span<ushort> destination, int destinationStride, bool flipUD, int maxSample)
        where TLanes : struct, ILanes<Vector256<int>>
    {
        var maxSamples = Vector256.Create(maxSample);
        // Every element is written before it is read
        Span<Vector256<int>> column = unsafe(stackalloc Vector256<int>[64])[..h];
        var columnLength = sparseInputs is 0 ? h : sparseInputs;
        for (var j = 0; j < w; j += 8)
        {
            for (var i = 0; i < columnLength; i++)
            {
                column[i] = Vector256.Create(residual.Slice((i * w) + j, 8));
            }

            if (sparseInputs is not 0)
            {
                InverseDctSparse<TLanes, Vector256<int>>(column, log2H, colClampRange, sparseInputs);
            }
            else
            {
                Apply<TLanes, Vector256<int>>(colKind, column, log2H, colClampRange);
            }

            if (!destination.IsEmpty)
            {
                for (var i = 0; i < h; i++)
                {
                    var samples = destination.Slice(((flipUD ? h - 1 - i : i) * destinationStride) + j, 8);
                    var predicted = Avx2.ConvertToVector256Int32(Vector128.Create(samples));
                    var sum = Vector256.Min(Vector256.Max(predicted + Lanes256.ShiftRightArithmeticRounded(column[i], ColumnShift), Vector256<int>.Zero), maxSamples);
                    Vector256.Narrow(sum, sum).GetLower().AsUInt16().CopyTo(samples);
                }

                continue;
            }

            for (var i = 0; i < h; i++)
            {
                Lanes256.ShiftRightArithmeticRounded(column[i], ColumnShift).CopyTo(residual.Slice((i * w) + j, 8));
            }
        }
    }

    // The columns of TransformVector with AdvSimd, by pairs of groups of 4 that the residual adds to 8 predicted samples at once:
    // the residual narrowed with saturation to 16 bits and added with saturation, then clamped, which is the clamp of the
    // 32-bit sum (a residual outside 16 bits gives the bound that its sign gives anyway)
    [SkipLocalsInit]
    private static void TransformColumnPairsAdd<TLanes>(Span<int> residual, Span<Vector128<int>> first, int w, int h, int log2H, Kind colKind, int colClampRange, int sparseInputs, Span<ushort> destination, int destinationStride, bool flipUD, int maxSample)
        where TLanes : struct, ILanes<Vector128<int>>
    {
        // Every element is written before it is read
        Span<Vector128<int>> second = unsafe(stackalloc Vector128<int>[64])[..h];
        first = first[..h];
        var maxSamples = Vector128.Create((short)maxSample);
        var columnLength = sparseInputs is 0 ? h : sparseInputs;
        for (var j = 0; j < w; j += 8)
        {
            for (var i = 0; i < columnLength; i++)
            {
                first[i] = Vector128.Create(residual.Slice((i * w) + j, 4));
                second[i] = Vector128.Create(residual.Slice((i * w) + j + 4, 4));
            }

            if (sparseInputs is not 0)
            {
                InverseDctSparse<TLanes, Vector128<int>>(first, log2H, colClampRange, sparseInputs);
                InverseDctSparse<TLanes, Vector128<int>>(second, log2H, colClampRange, sparseInputs);
            }
            else
            {
                Apply<TLanes, Vector128<int>>(colKind, first, log2H, colClampRange);
                Apply<TLanes, Vector128<int>>(colKind, second, log2H, colClampRange);
            }

            for (var i = 0; i < h; i++)
            {
                var samples = destination.Slice(((flipUD ? h - 1 - i : i) * destinationStride) + j, 8);
                var low = AdvSimd.ShiftRightArithmeticRounded(first[i], ColumnShift);
                var high = AdvSimd.ShiftRightArithmeticRounded(second[i], ColumnShift);
                var sum = AdvSimd.AddSaturate(Vector128.Create(samples).AsInt16(), AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(low), high));
                Vector128.Min(Vector128.Max(sum, Vector128<short>.Zero), maxSamples).AsUInt16().CopyTo(samples);
            }
        }
    }

    // Adds 4 residual samples to the 4 predicted samples at destination[index], clamped to 0 to the lanes of maxSamples
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddResidual4(Span<ushort> destination, int index, Vector128<int> residual, Vector128<int> maxSamples)
    {
        ref var samples = ref unsafe(Unsafe.As<ushort, byte>(ref destination.Slice(index, 4)[0]));
        var predicted = Vector128.WidenLower(Vector128.CreateScalar(unsafe(Unsafe.ReadUnaligned<ulong>(ref samples))).AsUInt16()).AsInt32();
        var sum = Vector128.Min(Vector128.Max(predicted + residual, Vector128<int>.Zero), maxSamples);
        var narrowed = Vector128.Narrow(sum, sum).AsUInt64().ToScalar();
        unsafe
        {
            Unsafe.WriteUnaligned(ref samples, narrowed);
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

    // InverseDct on vectors: the same steps. The kernels of 4 to 32 points are written out, with constant indices and angles
    // (the rotations then multiply by constants); the 64-point one runs the loops of the scalar kernel
    private static void InverseDct<TLanes, TVector>(Span<TVector> t, int n, int r)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var min = TLanes.Create(-(1 << (r - 1)));
        var max = TLanes.Create((1 << (r - 1)) - 1);
        switch (n)
        {
            case 2:
                InverseDct4<TLanes, TVector>(t, min, max);
                return;
            case 3:
                InverseDct8<TLanes, TVector>(t, min, max);
                return;
            case 4:
                InverseDct16<TLanes, TVector>(t, min, max);
                return;
            case 5:
                InverseDct32<TLanes, TVector>(t, min, max);
                return;
        }

        var length = 1 << n;
        for (var i = 0; i < length; i++)
        {
            var j = BitReverse(n, i);
            if (j > i)
                (t[i], t[j]) = (t[j], t[i]);
        }

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

    private static void InverseDct4<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..4];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        (t1, t2) = (t2, t1);
        (t0, t1) = TLanes.Rotate(t0, t1, 32, flip: true);
        (t2, t3) = TLanes.Rotate(t2, t3, 48, flip: false);
        (t0, t3) = TLanes.Hadamard(t0, t3, flip: false, min, max);
        (t1, t2) = TLanes.Hadamard(t1, t2, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
    }

    private static void InverseDct8<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..8];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var t4 = t[4];
        var t5 = t[5];
        var t6 = t[6];
        var t7 = t[7];
        (t1, t4) = (t4, t1);
        (t3, t6) = (t6, t3);
        (t4, t7) = TLanes.Rotate(t4, t7, 56, flip: false);
        (t5, t6) = TLanes.Rotate(t5, t6, 24, flip: false);
        (t0, t1) = TLanes.Rotate(t0, t1, 32, flip: true);
        (t2, t3) = TLanes.Rotate(t2, t3, 48, flip: false);
        (t4, t5) = TLanes.Hadamard(t4, t5, flip: false, min, max);
        (t6, t7) = TLanes.Hadamard(t6, t7, flip: true, min, max);
        (t0, t3) = TLanes.Hadamard(t0, t3, flip: false, min, max);
        (t1, t2) = TLanes.Hadamard(t1, t2, flip: false, min, max);
        (t6, t5) = TLanes.Rotate(t6, t5, 32, flip: true);
        (t0, t7) = TLanes.Hadamard(t0, t7, flip: false, min, max);
        (t1, t6) = TLanes.Hadamard(t1, t6, flip: false, min, max);
        (t2, t5) = TLanes.Hadamard(t2, t5, flip: false, min, max);
        (t3, t4) = TLanes.Hadamard(t3, t4, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
    }

    private static void InverseDct16<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..16];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var t4 = t[4];
        var t5 = t[5];
        var t6 = t[6];
        var t7 = t[7];
        var t8 = t[8];
        var t9 = t[9];
        var t10 = t[10];
        var t11 = t[11];
        var t12 = t[12];
        var t13 = t[13];
        var t14 = t[14];
        var t15 = t[15];
        (t1, t8) = (t8, t1);
        (t2, t4) = (t4, t2);
        (t3, t12) = (t12, t3);
        (t5, t10) = (t10, t5);
        (t7, t14) = (t14, t7);
        (t11, t13) = (t13, t11);
        (t8, t15) = TLanes.Rotate(t8, t15, 60, flip: false);
        (t9, t14) = TLanes.Rotate(t9, t14, 28, flip: false);
        (t10, t13) = TLanes.Rotate(t10, t13, 44, flip: false);
        (t11, t12) = TLanes.Rotate(t11, t12, 12, flip: false);
        (t4, t7) = TLanes.Rotate(t4, t7, 56, flip: false);
        (t5, t6) = TLanes.Rotate(t5, t6, 24, flip: false);
        (t8, t9) = TLanes.Hadamard(t8, t9, flip: false, min, max);
        (t10, t11) = TLanes.Hadamard(t10, t11, flip: true, min, max);
        (t12, t13) = TLanes.Hadamard(t12, t13, flip: false, min, max);
        (t14, t15) = TLanes.Hadamard(t14, t15, flip: true, min, max);
        (t0, t1) = TLanes.Rotate(t0, t1, 32, flip: true);
        (t2, t3) = TLanes.Rotate(t2, t3, 48, flip: false);
        (t4, t5) = TLanes.Hadamard(t4, t5, flip: false, min, max);
        (t6, t7) = TLanes.Hadamard(t6, t7, flip: true, min, max);
        (t14, t9) = TLanes.Rotate(t14, t9, 48, flip: true);
        (t13, t10) = TLanes.Rotate(t13, t10, 112, flip: true);
        (t0, t3) = TLanes.Hadamard(t0, t3, flip: false, min, max);
        (t1, t2) = TLanes.Hadamard(t1, t2, flip: false, min, max);
        (t6, t5) = TLanes.Rotate(t6, t5, 32, flip: true);
        (t8, t11) = TLanes.Hadamard(t8, t11, flip: false, min, max);
        (t9, t10) = TLanes.Hadamard(t9, t10, flip: false, min, max);
        (t12, t15) = TLanes.Hadamard(t12, t15, flip: true, min, max);
        (t13, t14) = TLanes.Hadamard(t13, t14, flip: true, min, max);
        (t0, t7) = TLanes.Hadamard(t0, t7, flip: false, min, max);
        (t1, t6) = TLanes.Hadamard(t1, t6, flip: false, min, max);
        (t2, t5) = TLanes.Hadamard(t2, t5, flip: false, min, max);
        (t3, t4) = TLanes.Hadamard(t3, t4, flip: false, min, max);
        (t13, t10) = TLanes.Rotate(t13, t10, 32, flip: true);
        (t12, t11) = TLanes.Rotate(t12, t11, 32, flip: true);
        (t0, t15) = TLanes.Hadamard(t0, t15, flip: false, min, max);
        (t1, t14) = TLanes.Hadamard(t1, t14, flip: false, min, max);
        (t2, t13) = TLanes.Hadamard(t2, t13, flip: false, min, max);
        (t3, t12) = TLanes.Hadamard(t3, t12, flip: false, min, max);
        (t4, t11) = TLanes.Hadamard(t4, t11, flip: false, min, max);
        (t5, t10) = TLanes.Hadamard(t5, t10, flip: false, min, max);
        (t6, t9) = TLanes.Hadamard(t6, t9, flip: false, min, max);
        (t7, t8) = TLanes.Hadamard(t7, t8, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
        t[8] = t8;
        t[9] = t9;
        t[10] = t10;
        t[11] = t11;
        t[12] = t12;
        t[13] = t13;
        t[14] = t14;
        t[15] = t15;
    }

    private static void InverseDct32<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..32];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var t4 = t[4];
        var t5 = t[5];
        var t6 = t[6];
        var t7 = t[7];
        var t8 = t[8];
        var t9 = t[9];
        var t10 = t[10];
        var t11 = t[11];
        var t12 = t[12];
        var t13 = t[13];
        var t14 = t[14];
        var t15 = t[15];
        var t16 = t[16];
        var t17 = t[17];
        var t18 = t[18];
        var t19 = t[19];
        var t20 = t[20];
        var t21 = t[21];
        var t22 = t[22];
        var t23 = t[23];
        var t24 = t[24];
        var t25 = t[25];
        var t26 = t[26];
        var t27 = t[27];
        var t28 = t[28];
        var t29 = t[29];
        var t30 = t[30];
        var t31 = t[31];
        (t1, t16) = (t16, t1);
        (t2, t8) = (t8, t2);
        (t3, t24) = (t24, t3);
        (t5, t20) = (t20, t5);
        (t6, t12) = (t12, t6);
        (t7, t28) = (t28, t7);
        (t9, t18) = (t18, t9);
        (t11, t26) = (t26, t11);
        (t13, t22) = (t22, t13);
        (t15, t30) = (t30, t15);
        (t19, t25) = (t25, t19);
        (t23, t29) = (t29, t23);
        (t16, t31) = TLanes.Rotate(t16, t31, 62, flip: false);
        (t17, t30) = TLanes.Rotate(t17, t30, 30, flip: false);
        (t18, t29) = TLanes.Rotate(t18, t29, 46, flip: false);
        (t19, t28) = TLanes.Rotate(t19, t28, 14, flip: false);
        (t20, t27) = TLanes.Rotate(t20, t27, 54, flip: false);
        (t21, t26) = TLanes.Rotate(t21, t26, 22, flip: false);
        (t22, t25) = TLanes.Rotate(t22, t25, 38, flip: false);
        (t23, t24) = TLanes.Rotate(t23, t24, 6, flip: false);
        (t8, t15) = TLanes.Rotate(t8, t15, 60, flip: false);
        (t9, t14) = TLanes.Rotate(t9, t14, 28, flip: false);
        (t10, t13) = TLanes.Rotate(t10, t13, 44, flip: false);
        (t11, t12) = TLanes.Rotate(t11, t12, 12, flip: false);
        (t16, t17) = TLanes.Hadamard(t16, t17, flip: false, min, max);
        (t18, t19) = TLanes.Hadamard(t18, t19, flip: true, min, max);
        (t20, t21) = TLanes.Hadamard(t20, t21, flip: false, min, max);
        (t22, t23) = TLanes.Hadamard(t22, t23, flip: true, min, max);
        (t24, t25) = TLanes.Hadamard(t24, t25, flip: false, min, max);
        (t26, t27) = TLanes.Hadamard(t26, t27, flip: true, min, max);
        (t28, t29) = TLanes.Hadamard(t28, t29, flip: false, min, max);
        (t30, t31) = TLanes.Hadamard(t30, t31, flip: true, min, max);
        (t4, t7) = TLanes.Rotate(t4, t7, 56, flip: false);
        (t5, t6) = TLanes.Rotate(t5, t6, 24, flip: false);
        (t8, t9) = TLanes.Hadamard(t8, t9, flip: false, min, max);
        (t10, t11) = TLanes.Hadamard(t10, t11, flip: true, min, max);
        (t12, t13) = TLanes.Hadamard(t12, t13, flip: false, min, max);
        (t14, t15) = TLanes.Hadamard(t14, t15, flip: true, min, max);
        (t30, t17) = TLanes.Rotate(t30, t17, 56, flip: true);
        (t29, t18) = TLanes.Rotate(t29, t18, 120, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 24, flip: true);
        (t25, t22) = TLanes.Rotate(t25, t22, 88, flip: true);
        (t0, t1) = TLanes.Rotate(t0, t1, 32, flip: true);
        (t2, t3) = TLanes.Rotate(t2, t3, 48, flip: false);
        (t4, t5) = TLanes.Hadamard(t4, t5, flip: false, min, max);
        (t6, t7) = TLanes.Hadamard(t6, t7, flip: true, min, max);
        (t14, t9) = TLanes.Rotate(t14, t9, 48, flip: true);
        (t13, t10) = TLanes.Rotate(t13, t10, 112, flip: true);
        (t16, t19) = TLanes.Hadamard(t16, t19, flip: false, min, max);
        (t17, t18) = TLanes.Hadamard(t17, t18, flip: false, min, max);
        (t20, t23) = TLanes.Hadamard(t20, t23, flip: true, min, max);
        (t21, t22) = TLanes.Hadamard(t21, t22, flip: true, min, max);
        (t24, t27) = TLanes.Hadamard(t24, t27, flip: false, min, max);
        (t25, t26) = TLanes.Hadamard(t25, t26, flip: false, min, max);
        (t28, t31) = TLanes.Hadamard(t28, t31, flip: true, min, max);
        (t29, t30) = TLanes.Hadamard(t29, t30, flip: true, min, max);
        (t0, t3) = TLanes.Hadamard(t0, t3, flip: false, min, max);
        (t1, t2) = TLanes.Hadamard(t1, t2, flip: false, min, max);
        (t6, t5) = TLanes.Rotate(t6, t5, 32, flip: true);
        (t8, t11) = TLanes.Hadamard(t8, t11, flip: false, min, max);
        (t9, t10) = TLanes.Hadamard(t9, t10, flip: false, min, max);
        (t12, t15) = TLanes.Hadamard(t12, t15, flip: true, min, max);
        (t13, t14) = TLanes.Hadamard(t13, t14, flip: true, min, max);
        (t29, t18) = TLanes.Rotate(t29, t18, 48, flip: true);
        (t28, t19) = TLanes.Rotate(t28, t19, 48, flip: true);
        (t27, t20) = TLanes.Rotate(t27, t20, 112, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 112, flip: true);
        (t0, t7) = TLanes.Hadamard(t0, t7, flip: false, min, max);
        (t1, t6) = TLanes.Hadamard(t1, t6, flip: false, min, max);
        (t2, t5) = TLanes.Hadamard(t2, t5, flip: false, min, max);
        (t3, t4) = TLanes.Hadamard(t3, t4, flip: false, min, max);
        (t13, t10) = TLanes.Rotate(t13, t10, 32, flip: true);
        (t12, t11) = TLanes.Rotate(t12, t11, 32, flip: true);
        (t16, t23) = TLanes.Hadamard(t16, t23, flip: false, min, max);
        (t17, t22) = TLanes.Hadamard(t17, t22, flip: false, min, max);
        (t18, t21) = TLanes.Hadamard(t18, t21, flip: false, min, max);
        (t19, t20) = TLanes.Hadamard(t19, t20, flip: false, min, max);
        (t24, t31) = TLanes.Hadamard(t24, t31, flip: true, min, max);
        (t25, t30) = TLanes.Hadamard(t25, t30, flip: true, min, max);
        (t26, t29) = TLanes.Hadamard(t26, t29, flip: true, min, max);
        (t27, t28) = TLanes.Hadamard(t27, t28, flip: true, min, max);
        (t0, t15) = TLanes.Hadamard(t0, t15, flip: false, min, max);
        (t1, t14) = TLanes.Hadamard(t1, t14, flip: false, min, max);
        (t2, t13) = TLanes.Hadamard(t2, t13, flip: false, min, max);
        (t3, t12) = TLanes.Hadamard(t3, t12, flip: false, min, max);
        (t4, t11) = TLanes.Hadamard(t4, t11, flip: false, min, max);
        (t5, t10) = TLanes.Hadamard(t5, t10, flip: false, min, max);
        (t6, t9) = TLanes.Hadamard(t6, t9, flip: false, min, max);
        (t7, t8) = TLanes.Hadamard(t7, t8, flip: false, min, max);
        (t27, t20) = TLanes.Rotate(t27, t20, 32, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 32, flip: true);
        (t25, t22) = TLanes.Rotate(t25, t22, 32, flip: true);
        (t24, t23) = TLanes.Rotate(t24, t23, 32, flip: true);
        (t0, t31) = TLanes.Hadamard(t0, t31, flip: false, min, max);
        (t1, t30) = TLanes.Hadamard(t1, t30, flip: false, min, max);
        (t2, t29) = TLanes.Hadamard(t2, t29, flip: false, min, max);
        (t3, t28) = TLanes.Hadamard(t3, t28, flip: false, min, max);
        (t4, t27) = TLanes.Hadamard(t4, t27, flip: false, min, max);
        (t5, t26) = TLanes.Hadamard(t5, t26, flip: false, min, max);
        (t6, t25) = TLanes.Hadamard(t6, t25, flip: false, min, max);
        (t7, t24) = TLanes.Hadamard(t7, t24, flip: false, min, max);
        (t8, t23) = TLanes.Hadamard(t8, t23, flip: false, min, max);
        (t9, t22) = TLanes.Hadamard(t9, t22, flip: false, min, max);
        (t10, t21) = TLanes.Hadamard(t10, t21, flip: false, min, max);
        (t11, t20) = TLanes.Hadamard(t11, t20, flip: false, min, max);
        (t12, t19) = TLanes.Hadamard(t12, t19, flip: false, min, max);
        (t13, t18) = TLanes.Hadamard(t13, t18, flip: false, min, max);
        (t14, t17) = TLanes.Hadamard(t14, t17, flip: false, min, max);
        (t15, t16) = TLanes.Hadamard(t15, t16, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
        t[8] = t8;
        t[9] = t9;
        t[10] = t10;
        t[11] = t11;
        t[12] = t12;
        t[13] = t13;
        t[14] = t14;
        t[15] = t15;
        t[16] = t16;
        t[17] = t17;
        t[18] = t18;
        t[19] = t19;
        t[20] = t20;
        t[21] = t21;
        t[22] = t22;
        t[23] = t23;
        t[24] = t24;
        t[25] = t25;
        t[26] = t26;
        t[27] = t27;
        t[28] = t28;
        t[29] = t29;
        t[30] = t30;
        t[31] = t31;
    }
    // The number of inputs of a DCT of 2^n points that InverseDctSparse reads, when the inputs from count are zero: 4 for 8 to 32
    // points, 8 for 16 and 32 points, 0 when no kernel applies
    private static int GetSparseInputs(Kind kind, int n, int count)
    {
        if (kind is not Kind.Dct || n is < 3 or > 5)
            return 0;

        if (count <= 4)
            return 4;

        return count <= 8 && n >= 4 ? 8 : 0;
    }

    // InverseDct of 2^n points whose inputs from the given number (GetSparseInputs) are zero, which it does not read: the
    // steps of InverseDct without the rotations of zeros, a rotation of one zero as two products (B( a, 0 ) is
    // Round2( a * cos ), Round2( a * sin )) and a Hadamard rotation of one zero as clamps (H( a, 0 ) is Clamp( a ) twice,
    // H( 0, b ) is Clamp( b ) and Clamp( -b ), the values staying far from the limits of 32 bits), without the clamps of the
    // outputs of a Hadamard rotation. The values are those of InverseDct for every input
    private static void InverseDctSparse<TLanes, TVector>(Span<TVector> t, int n, int r, int inputs)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var min = TLanes.Create(-(1 << (r - 1)));
        var max = TLanes.Create((1 << (r - 1)) - 1);
        switch (n)
        {
            case 3:
                InverseDct8Sparse4<TLanes, TVector>(t, min, max);
                break;
            case 4 when inputs is 4:
                InverseDct16Sparse4<TLanes, TVector>(t, min, max);
                break;
            case 4:
                InverseDct16Sparse8<TLanes, TVector>(t, min, max);
                break;
            case 5 when inputs is 4:
                InverseDct32Sparse4<TLanes, TVector>(t, min, max);
                break;
            default:
                InverseDct32Sparse8<TLanes, TVector>(t, min, max);
                break;
        }
    }

    private static void InverseDct8Sparse4<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..8];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        TVector t4, t5, t6, t7;
        t4 = t1;
        t6 = t3;
        t7 = TLanes.MultiplyRound12(t4, 4017);
        t4 = TLanes.MultiplyRound12(t4, 799);
        t5 = TLanes.MultiplyRound12(t6, -2276);
        t6 = TLanes.MultiplyRound12(t6, 3406);
        t1 = TLanes.MultiplyRound12(t0, 2896);
        t0 = TLanes.MultiplyRound12(t0, 2896);
        t3 = TLanes.MultiplyRound12(t2, 3784);
        t2 = TLanes.MultiplyRound12(t2, 1567);
        (t4, t5) = TLanes.Hadamard(t4, t5, flip: false, min, max);
        (t7, t6) = TLanes.Hadamard(t7, t6, flip: false, min, max);
        (t0, t3) = TLanes.Hadamard(t0, t3, flip: false, min, max);
        (t1, t2) = TLanes.Hadamard(t1, t2, flip: false, min, max);
        (t6, t5) = TLanes.Rotate(t6, t5, 32, flip: true);
        (t0, t7) = TLanes.Hadamard(t0, t7, flip: false, min, max);
        (t1, t6) = TLanes.Hadamard(t1, t6, flip: false, min, max);
        (t2, t5) = TLanes.Hadamard(t2, t5, flip: false, min, max);
        (t3, t4) = TLanes.Hadamard(t3, t4, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
    }

    private static void InverseDct16Sparse4<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..16];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        TVector t4, t5, t6, t7, t8, t9, t10, t11, t12, t13, t14, t15;
        t8 = t1;
        t4 = t2;
        t12 = t3;
        t15 = TLanes.MultiplyRound12(t8, 4076);
        t8 = TLanes.MultiplyRound12(t8, 401);
        t11 = TLanes.MultiplyRound12(t12, -1189);
        t12 = TLanes.MultiplyRound12(t12, 3920);
        t7 = TLanes.MultiplyRound12(t4, 4017);
        t4 = TLanes.MultiplyRound12(t4, 799);
        t8 = TLanes.Clamp(t8, min, max);
        t9 = t8;
        t11 = TLanes.Clamp(t11, min, max);
        t10 = t11;
        t12 = TLanes.Clamp(t12, min, max);
        t13 = t12;
        t15 = TLanes.Clamp(t15, min, max);
        t14 = t15;
        t1 = TLanes.MultiplyRound12(t0, 2896);
        t0 = TLanes.MultiplyRound12(t0, 2896);
        t4 = TLanes.Clamp(t4, min, max);
        t5 = t4;
        t7 = TLanes.Clamp(t7, min, max);
        t6 = t7;
        (t14, t9) = TLanes.Rotate(t14, t9, 48, flip: true);
        (t13, t10) = TLanes.Rotate(t13, t10, 112, flip: true);
        t0 = TLanes.Clamp(t0, min, max);
        t3 = t0;
        t1 = TLanes.Clamp(t1, min, max);
        t2 = t1;
        (t6, t5) = TLanes.Rotate(t6, t5, 32, flip: true);
        (t8, t11) = TLanes.Hadamard(t8, t11, flip: false, min, max);
        (t9, t10) = TLanes.Hadamard(t9, t10, flip: false, min, max);
        (t15, t12) = TLanes.Hadamard(t15, t12, flip: false, min, max);
        (t14, t13) = TLanes.Hadamard(t14, t13, flip: false, min, max);
        (t0, t7) = TLanes.Hadamard(t0, t7, flip: false, min, max);
        (t1, t6) = TLanes.Hadamard(t1, t6, flip: false, min, max);
        (t2, t5) = TLanes.Hadamard(t2, t5, flip: false, min, max);
        (t3, t4) = TLanes.Hadamard(t3, t4, flip: false, min, max);
        (t13, t10) = TLanes.Rotate(t13, t10, 32, flip: true);
        (t12, t11) = TLanes.Rotate(t12, t11, 32, flip: true);
        (t0, t15) = TLanes.Hadamard(t0, t15, flip: false, min, max);
        (t1, t14) = TLanes.Hadamard(t1, t14, flip: false, min, max);
        (t2, t13) = TLanes.Hadamard(t2, t13, flip: false, min, max);
        (t3, t12) = TLanes.Hadamard(t3, t12, flip: false, min, max);
        (t4, t11) = TLanes.Hadamard(t4, t11, flip: false, min, max);
        (t5, t10) = TLanes.Hadamard(t5, t10, flip: false, min, max);
        (t6, t9) = TLanes.Hadamard(t6, t9, flip: false, min, max);
        (t7, t8) = TLanes.Hadamard(t7, t8, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
        t[8] = t8;
        t[9] = t9;
        t[10] = t10;
        t[11] = t11;
        t[12] = t12;
        t[13] = t13;
        t[14] = t14;
        t[15] = t15;
    }

    private static void InverseDct32Sparse4<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..32];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        TVector t4, t5, t6, t7, t8, t9, t10, t11, t12, t13, t14, t15, t16, t17, t18, t19, t20, t21, t22, t23, t24, t25, t26, t27, t28, t29, t30, t31;
        t16 = t1;
        t8 = t2;
        t24 = t3;
        t31 = TLanes.MultiplyRound12(t16, 4091);
        t16 = TLanes.MultiplyRound12(t16, 201);
        t23 = TLanes.MultiplyRound12(t24, -601);
        t24 = TLanes.MultiplyRound12(t24, 4052);
        t15 = TLanes.MultiplyRound12(t8, 4076);
        t8 = TLanes.MultiplyRound12(t8, 401);
        t16 = TLanes.Clamp(t16, min, max);
        t17 = t16;
        t23 = TLanes.Clamp(t23, min, max);
        t22 = t23;
        t24 = TLanes.Clamp(t24, min, max);
        t25 = t24;
        t31 = TLanes.Clamp(t31, min, max);
        t30 = t31;
        t8 = TLanes.Clamp(t8, min, max);
        t9 = t8;
        t15 = TLanes.Clamp(t15, min, max);
        t14 = t15;
        (t30, t17) = TLanes.Rotate(t30, t17, 56, flip: true);
        (t25, t22) = TLanes.Rotate(t25, t22, 88, flip: true);
        t1 = TLanes.MultiplyRound12(t0, 2896);
        t0 = TLanes.MultiplyRound12(t0, 2896);
        (t14, t9) = TLanes.Rotate(t14, t9, 48, flip: true);
        t19 = t16;
        t17 = TLanes.Clamp(t17, min, max);
        t18 = t17;
        t20 = t23;
        t22 = TLanes.Clamp(t22, min, max);
        t21 = t22;
        t27 = t24;
        t25 = TLanes.Clamp(t25, min, max);
        t26 = t25;
        t28 = t31;
        t30 = TLanes.Clamp(t30, min, max);
        t29 = t30;
        t0 = TLanes.Clamp(t0, min, max);
        t3 = t0;
        t1 = TLanes.Clamp(t1, min, max);
        t2 = t1;
        t11 = t8;
        t9 = TLanes.Clamp(t9, min, max);
        t10 = t9;
        t12 = t15;
        t14 = TLanes.Clamp(t14, min, max);
        t13 = t14;
        (t29, t18) = TLanes.Rotate(t29, t18, 48, flip: true);
        (t28, t19) = TLanes.Rotate(t28, t19, 48, flip: true);
        (t27, t20) = TLanes.Rotate(t27, t20, 112, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 112, flip: true);
        t7 = t0;
        t6 = t1;
        t5 = t2;
        t4 = t3;
        (t13, t10) = TLanes.Rotate(t13, t10, 32, flip: true);
        (t12, t11) = TLanes.Rotate(t12, t11, 32, flip: true);
        (t16, t23) = TLanes.Hadamard(t16, t23, flip: false, min, max);
        (t17, t22) = TLanes.Hadamard(t17, t22, flip: false, min, max);
        (t18, t21) = TLanes.Hadamard(t18, t21, flip: false, min, max);
        (t19, t20) = TLanes.Hadamard(t19, t20, flip: false, min, max);
        (t31, t24) = TLanes.Hadamard(t31, t24, flip: false, min, max);
        (t30, t25) = TLanes.Hadamard(t30, t25, flip: false, min, max);
        (t29, t26) = TLanes.Hadamard(t29, t26, flip: false, min, max);
        (t28, t27) = TLanes.Hadamard(t28, t27, flip: false, min, max);
        (t0, t15) = TLanes.Hadamard(t0, t15, flip: false, min, max);
        (t1, t14) = TLanes.Hadamard(t1, t14, flip: false, min, max);
        (t2, t13) = TLanes.Hadamard(t2, t13, flip: false, min, max);
        (t3, t12) = TLanes.Hadamard(t3, t12, flip: false, min, max);
        (t4, t11) = TLanes.Hadamard(t4, t11, flip: false, min, max);
        (t5, t10) = TLanes.Hadamard(t5, t10, flip: false, min, max);
        (t6, t9) = TLanes.Hadamard(t6, t9, flip: false, min, max);
        (t7, t8) = TLanes.Hadamard(t7, t8, flip: false, min, max);
        (t27, t20) = TLanes.Rotate(t27, t20, 32, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 32, flip: true);
        (t25, t22) = TLanes.Rotate(t25, t22, 32, flip: true);
        (t24, t23) = TLanes.Rotate(t24, t23, 32, flip: true);
        (t0, t31) = TLanes.Hadamard(t0, t31, flip: false, min, max);
        (t1, t30) = TLanes.Hadamard(t1, t30, flip: false, min, max);
        (t2, t29) = TLanes.Hadamard(t2, t29, flip: false, min, max);
        (t3, t28) = TLanes.Hadamard(t3, t28, flip: false, min, max);
        (t4, t27) = TLanes.Hadamard(t4, t27, flip: false, min, max);
        (t5, t26) = TLanes.Hadamard(t5, t26, flip: false, min, max);
        (t6, t25) = TLanes.Hadamard(t6, t25, flip: false, min, max);
        (t7, t24) = TLanes.Hadamard(t7, t24, flip: false, min, max);
        (t8, t23) = TLanes.Hadamard(t8, t23, flip: false, min, max);
        (t9, t22) = TLanes.Hadamard(t9, t22, flip: false, min, max);
        (t10, t21) = TLanes.Hadamard(t10, t21, flip: false, min, max);
        (t11, t20) = TLanes.Hadamard(t11, t20, flip: false, min, max);
        (t12, t19) = TLanes.Hadamard(t12, t19, flip: false, min, max);
        (t13, t18) = TLanes.Hadamard(t13, t18, flip: false, min, max);
        (t14, t17) = TLanes.Hadamard(t14, t17, flip: false, min, max);
        (t15, t16) = TLanes.Hadamard(t15, t16, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
        t[8] = t8;
        t[9] = t9;
        t[10] = t10;
        t[11] = t11;
        t[12] = t12;
        t[13] = t13;
        t[14] = t14;
        t[15] = t15;
        t[16] = t16;
        t[17] = t17;
        t[18] = t18;
        t[19] = t19;
        t[20] = t20;
        t[21] = t21;
        t[22] = t22;
        t[23] = t23;
        t[24] = t24;
        t[25] = t25;
        t[26] = t26;
        t[27] = t27;
        t[28] = t28;
        t[29] = t29;
        t[30] = t30;
        t[31] = t31;
    }

    private static void InverseDct16Sparse8<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..16];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var t4 = t[4];
        var t5 = t[5];
        var t6 = t[6];
        var t7 = t[7];
        TVector t8, t9, t10, t11, t12, t13, t14, t15;
        t8 = t1;
        (t2, t4) = (t4, t2);
        t12 = t3;
        t10 = t5;
        t14 = t7;
        t15 = TLanes.MultiplyRound12(t8, 4076);
        t8 = TLanes.MultiplyRound12(t8, 401);
        t9 = TLanes.MultiplyRound12(t14, -2598);
        t14 = TLanes.MultiplyRound12(t14, 3166);
        t13 = TLanes.MultiplyRound12(t10, 3612);
        t10 = TLanes.MultiplyRound12(t10, 1931);
        t11 = TLanes.MultiplyRound12(t12, -1189);
        t12 = TLanes.MultiplyRound12(t12, 3920);
        t7 = TLanes.MultiplyRound12(t4, 4017);
        t4 = TLanes.MultiplyRound12(t4, 799);
        t5 = TLanes.MultiplyRound12(t6, -2276);
        t6 = TLanes.MultiplyRound12(t6, 3406);
        (t8, t9) = TLanes.Hadamard(t8, t9, flip: false, min, max);
        (t11, t10) = TLanes.Hadamard(t11, t10, flip: false, min, max);
        (t12, t13) = TLanes.Hadamard(t12, t13, flip: false, min, max);
        (t15, t14) = TLanes.Hadamard(t15, t14, flip: false, min, max);
        t1 = TLanes.MultiplyRound12(t0, 2896);
        t0 = TLanes.MultiplyRound12(t0, 2896);
        t3 = TLanes.MultiplyRound12(t2, 3784);
        t2 = TLanes.MultiplyRound12(t2, 1567);
        (t4, t5) = TLanes.Hadamard(t4, t5, flip: false, min, max);
        (t7, t6) = TLanes.Hadamard(t7, t6, flip: false, min, max);
        (t14, t9) = TLanes.Rotate(t14, t9, 48, flip: true);
        (t13, t10) = TLanes.Rotate(t13, t10, 112, flip: true);
        (t0, t3) = TLanes.Hadamard(t0, t3, flip: false, min, max);
        (t1, t2) = TLanes.Hadamard(t1, t2, flip: false, min, max);
        (t6, t5) = TLanes.Rotate(t6, t5, 32, flip: true);
        (t8, t11) = TLanes.Hadamard(t8, t11, flip: false, min, max);
        (t9, t10) = TLanes.Hadamard(t9, t10, flip: false, min, max);
        (t15, t12) = TLanes.Hadamard(t15, t12, flip: false, min, max);
        (t14, t13) = TLanes.Hadamard(t14, t13, flip: false, min, max);
        (t0, t7) = TLanes.Hadamard(t0, t7, flip: false, min, max);
        (t1, t6) = TLanes.Hadamard(t1, t6, flip: false, min, max);
        (t2, t5) = TLanes.Hadamard(t2, t5, flip: false, min, max);
        (t3, t4) = TLanes.Hadamard(t3, t4, flip: false, min, max);
        (t13, t10) = TLanes.Rotate(t13, t10, 32, flip: true);
        (t12, t11) = TLanes.Rotate(t12, t11, 32, flip: true);
        (t0, t15) = TLanes.Hadamard(t0, t15, flip: false, min, max);
        (t1, t14) = TLanes.Hadamard(t1, t14, flip: false, min, max);
        (t2, t13) = TLanes.Hadamard(t2, t13, flip: false, min, max);
        (t3, t12) = TLanes.Hadamard(t3, t12, flip: false, min, max);
        (t4, t11) = TLanes.Hadamard(t4, t11, flip: false, min, max);
        (t5, t10) = TLanes.Hadamard(t5, t10, flip: false, min, max);
        (t6, t9) = TLanes.Hadamard(t6, t9, flip: false, min, max);
        (t7, t8) = TLanes.Hadamard(t7, t8, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
        t[8] = t8;
        t[9] = t9;
        t[10] = t10;
        t[11] = t11;
        t[12] = t12;
        t[13] = t13;
        t[14] = t14;
        t[15] = t15;
    }

    private static void InverseDct32Sparse8<TLanes, TVector>(Span<TVector> t, TVector min, TVector max)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        t = t[..32];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var t4 = t[4];
        var t5 = t[5];
        var t6 = t[6];
        var t7 = t[7];
        TVector t8, t9, t10, t11, t12, t13, t14, t15, t16, t17, t18, t19, t20, t21, t22, t23, t24, t25, t26, t27, t28, t29, t30, t31;
        t16 = t1;
        t8 = t2;
        t24 = t3;
        t20 = t5;
        t12 = t6;
        t28 = t7;
        t31 = TLanes.MultiplyRound12(t16, 4091);
        t16 = TLanes.MultiplyRound12(t16, 201);
        t19 = TLanes.MultiplyRound12(t28, -1380);
        t28 = TLanes.MultiplyRound12(t28, 3857);
        t27 = TLanes.MultiplyRound12(t20, 3973);
        t20 = TLanes.MultiplyRound12(t20, 995);
        t23 = TLanes.MultiplyRound12(t24, -601);
        t24 = TLanes.MultiplyRound12(t24, 4052);
        t15 = TLanes.MultiplyRound12(t8, 4076);
        t8 = TLanes.MultiplyRound12(t8, 401);
        t11 = TLanes.MultiplyRound12(t12, -1189);
        t12 = TLanes.MultiplyRound12(t12, 3920);
        t16 = TLanes.Clamp(t16, min, max);
        t17 = t16;
        t19 = TLanes.Clamp(t19, min, max);
        t18 = t19;
        t20 = TLanes.Clamp(t20, min, max);
        t21 = t20;
        t23 = TLanes.Clamp(t23, min, max);
        t22 = t23;
        t24 = TLanes.Clamp(t24, min, max);
        t25 = t24;
        t27 = TLanes.Clamp(t27, min, max);
        t26 = t27;
        t28 = TLanes.Clamp(t28, min, max);
        t29 = t28;
        t31 = TLanes.Clamp(t31, min, max);
        t30 = t31;
        t7 = TLanes.MultiplyRound12(t4, 4017);
        t4 = TLanes.MultiplyRound12(t4, 799);
        t8 = TLanes.Clamp(t8, min, max);
        t9 = t8;
        t11 = TLanes.Clamp(t11, min, max);
        t10 = t11;
        t12 = TLanes.Clamp(t12, min, max);
        t13 = t12;
        t15 = TLanes.Clamp(t15, min, max);
        t14 = t15;
        (t30, t17) = TLanes.Rotate(t30, t17, 56, flip: true);
        (t29, t18) = TLanes.Rotate(t29, t18, 120, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 24, flip: true);
        (t25, t22) = TLanes.Rotate(t25, t22, 88, flip: true);
        t1 = TLanes.MultiplyRound12(t0, 2896);
        t0 = TLanes.MultiplyRound12(t0, 2896);
        t4 = TLanes.Clamp(t4, min, max);
        t5 = t4;
        t7 = TLanes.Clamp(t7, min, max);
        t6 = t7;
        (t14, t9) = TLanes.Rotate(t14, t9, 48, flip: true);
        (t13, t10) = TLanes.Rotate(t13, t10, 112, flip: true);
        (t16, t19) = TLanes.Hadamard(t16, t19, flip: false, min, max);
        (t17, t18) = TLanes.Hadamard(t17, t18, flip: false, min, max);
        (t23, t20) = TLanes.Hadamard(t23, t20, flip: false, min, max);
        (t22, t21) = TLanes.Hadamard(t22, t21, flip: false, min, max);
        (t24, t27) = TLanes.Hadamard(t24, t27, flip: false, min, max);
        (t25, t26) = TLanes.Hadamard(t25, t26, flip: false, min, max);
        (t31, t28) = TLanes.Hadamard(t31, t28, flip: false, min, max);
        (t30, t29) = TLanes.Hadamard(t30, t29, flip: false, min, max);
        t0 = TLanes.Clamp(t0, min, max);
        t3 = t0;
        t1 = TLanes.Clamp(t1, min, max);
        t2 = t1;
        (t6, t5) = TLanes.Rotate(t6, t5, 32, flip: true);
        (t8, t11) = TLanes.Hadamard(t8, t11, flip: false, min, max);
        (t9, t10) = TLanes.Hadamard(t9, t10, flip: false, min, max);
        (t15, t12) = TLanes.Hadamard(t15, t12, flip: false, min, max);
        (t14, t13) = TLanes.Hadamard(t14, t13, flip: false, min, max);
        (t29, t18) = TLanes.Rotate(t29, t18, 48, flip: true);
        (t28, t19) = TLanes.Rotate(t28, t19, 48, flip: true);
        (t27, t20) = TLanes.Rotate(t27, t20, 112, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 112, flip: true);
        (t0, t7) = TLanes.Hadamard(t0, t7, flip: false, min, max);
        (t1, t6) = TLanes.Hadamard(t1, t6, flip: false, min, max);
        (t2, t5) = TLanes.Hadamard(t2, t5, flip: false, min, max);
        (t3, t4) = TLanes.Hadamard(t3, t4, flip: false, min, max);
        (t13, t10) = TLanes.Rotate(t13, t10, 32, flip: true);
        (t12, t11) = TLanes.Rotate(t12, t11, 32, flip: true);
        (t16, t23) = TLanes.Hadamard(t16, t23, flip: false, min, max);
        (t17, t22) = TLanes.Hadamard(t17, t22, flip: false, min, max);
        (t18, t21) = TLanes.Hadamard(t18, t21, flip: false, min, max);
        (t19, t20) = TLanes.Hadamard(t19, t20, flip: false, min, max);
        (t31, t24) = TLanes.Hadamard(t31, t24, flip: false, min, max);
        (t30, t25) = TLanes.Hadamard(t30, t25, flip: false, min, max);
        (t29, t26) = TLanes.Hadamard(t29, t26, flip: false, min, max);
        (t28, t27) = TLanes.Hadamard(t28, t27, flip: false, min, max);
        (t0, t15) = TLanes.Hadamard(t0, t15, flip: false, min, max);
        (t1, t14) = TLanes.Hadamard(t1, t14, flip: false, min, max);
        (t2, t13) = TLanes.Hadamard(t2, t13, flip: false, min, max);
        (t3, t12) = TLanes.Hadamard(t3, t12, flip: false, min, max);
        (t4, t11) = TLanes.Hadamard(t4, t11, flip: false, min, max);
        (t5, t10) = TLanes.Hadamard(t5, t10, flip: false, min, max);
        (t6, t9) = TLanes.Hadamard(t6, t9, flip: false, min, max);
        (t7, t8) = TLanes.Hadamard(t7, t8, flip: false, min, max);
        (t27, t20) = TLanes.Rotate(t27, t20, 32, flip: true);
        (t26, t21) = TLanes.Rotate(t26, t21, 32, flip: true);
        (t25, t22) = TLanes.Rotate(t25, t22, 32, flip: true);
        (t24, t23) = TLanes.Rotate(t24, t23, 32, flip: true);
        (t0, t31) = TLanes.Hadamard(t0, t31, flip: false, min, max);
        (t1, t30) = TLanes.Hadamard(t1, t30, flip: false, min, max);
        (t2, t29) = TLanes.Hadamard(t2, t29, flip: false, min, max);
        (t3, t28) = TLanes.Hadamard(t3, t28, flip: false, min, max);
        (t4, t27) = TLanes.Hadamard(t4, t27, flip: false, min, max);
        (t5, t26) = TLanes.Hadamard(t5, t26, flip: false, min, max);
        (t6, t25) = TLanes.Hadamard(t6, t25, flip: false, min, max);
        (t7, t24) = TLanes.Hadamard(t7, t24, flip: false, min, max);
        (t8, t23) = TLanes.Hadamard(t8, t23, flip: false, min, max);
        (t9, t22) = TLanes.Hadamard(t9, t22, flip: false, min, max);
        (t10, t21) = TLanes.Hadamard(t10, t21, flip: false, min, max);
        (t11, t20) = TLanes.Hadamard(t11, t20, flip: false, min, max);
        (t12, t19) = TLanes.Hadamard(t12, t19, flip: false, min, max);
        (t13, t18) = TLanes.Hadamard(t13, t18, flip: false, min, max);
        (t14, t17) = TLanes.Hadamard(t14, t17, flip: false, min, max);
        (t15, t16) = TLanes.Hadamard(t15, t16, flip: false, min, max);
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
        t[8] = t8;
        t[9] = t9;
        t[10] = t10;
        t[11] = t11;
        t[12] = t12;
        t[13] = t13;
        t[14] = t14;
        t[15] = t15;
        t[16] = t16;
        t[17] = t17;
        t[18] = t18;
        t[19] = t19;
        t[20] = t20;
        t[21] = t21;
        t[22] = t22;
        t[23] = t23;
        t[24] = t24;
        t[25] = t25;
        t[26] = t26;
        t[27] = t27;
        t[28] = t28;
        t[29] = t29;
        t[30] = t30;
        t[31] = t31;
    }


    // InverseAdst8 and InverseAdst16 on vectors: the same steps, written out with constant indices and angles
    private static void InverseAdst8<TLanes, TVector>(Span<TVector> t, int r)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var min = TLanes.Create(-(1 << (r - 1)));
        var max = TLanes.Create((1 << (r - 1)) - 1);
        t = t[..8];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var t4 = t[4];
        var t5 = t[5];
        var t6 = t[6];
        var t7 = t[7];
        (t0, t1, t2, t3, t4, t5, t6, t7) = (t7, t0, t5, t2, t3, t4, t1, t6);
        (t0, t1) = TLanes.Rotate(t0, t1, 60, flip: true);
        (t2, t3) = TLanes.Rotate(t2, t3, 44, flip: true);
        (t4, t5) = TLanes.Rotate(t4, t5, 28, flip: true);
        (t6, t7) = TLanes.Rotate(t6, t7, 12, flip: true);
        (t0, t4) = TLanes.Hadamard(t0, t4, flip: false, min, max);
        (t1, t5) = TLanes.Hadamard(t1, t5, flip: false, min, max);
        (t2, t6) = TLanes.Hadamard(t2, t6, flip: false, min, max);
        (t3, t7) = TLanes.Hadamard(t3, t7, flip: false, min, max);
        (t4, t5) = TLanes.Rotate(t4, t5, 48, flip: true);
        (t7, t6) = TLanes.Rotate(t7, t6, 16, flip: true);
        (t0, t2) = TLanes.Hadamard(t0, t2, flip: false, min, max);
        (t4, t6) = TLanes.Hadamard(t4, t6, flip: false, min, max);
        (t1, t3) = TLanes.Hadamard(t1, t3, flip: false, min, max);
        (t5, t7) = TLanes.Hadamard(t5, t7, flip: false, min, max);
        (t2, t3) = TLanes.Rotate(t2, t3, 32, flip: true);
        (t6, t7) = TLanes.Rotate(t6, t7, 32, flip: true);
        (t1, t2, t3, t4, t5, t6, t7) = (TLanes.Negate(t4), t6, TLanes.Negate(t2), t3, TLanes.Negate(t7), t5, TLanes.Negate(t1));
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
    }

    private static void InverseAdst16<TLanes, TVector>(Span<TVector> t, int r)
        where TLanes : struct, ILanes<TVector>
        where TVector : unmanaged
    {
        var min = TLanes.Create(-(1 << (r - 1)));
        var max = TLanes.Create((1 << (r - 1)) - 1);
        t = t[..16];
        var t0 = t[0];
        var t1 = t[1];
        var t2 = t[2];
        var t3 = t[3];
        var t4 = t[4];
        var t5 = t[5];
        var t6 = t[6];
        var t7 = t[7];
        var t8 = t[8];
        var t9 = t[9];
        var t10 = t[10];
        var t11 = t[11];
        var t12 = t[12];
        var t13 = t[13];
        var t14 = t[14];
        var t15 = t[15];
        (t0, t1, t2, t3, t4, t5, t6, t7, t8, t9, t10, t11, t12, t13, t14, t15) = (t15, t0, t13, t2, t11, t4, t9, t6, t7, t8, t5, t10, t3, t12, t1, t14);
        (t0, t1) = TLanes.Rotate(t0, t1, 62, flip: true);
        (t2, t3) = TLanes.Rotate(t2, t3, 54, flip: true);
        (t4, t5) = TLanes.Rotate(t4, t5, 46, flip: true);
        (t6, t7) = TLanes.Rotate(t6, t7, 38, flip: true);
        (t8, t9) = TLanes.Rotate(t8, t9, 30, flip: true);
        (t10, t11) = TLanes.Rotate(t10, t11, 22, flip: true);
        (t12, t13) = TLanes.Rotate(t12, t13, 14, flip: true);
        (t14, t15) = TLanes.Rotate(t14, t15, 6, flip: true);
        (t0, t8) = TLanes.Hadamard(t0, t8, flip: false, min, max);
        (t1, t9) = TLanes.Hadamard(t1, t9, flip: false, min, max);
        (t2, t10) = TLanes.Hadamard(t2, t10, flip: false, min, max);
        (t3, t11) = TLanes.Hadamard(t3, t11, flip: false, min, max);
        (t4, t12) = TLanes.Hadamard(t4, t12, flip: false, min, max);
        (t5, t13) = TLanes.Hadamard(t5, t13, flip: false, min, max);
        (t6, t14) = TLanes.Hadamard(t6, t14, flip: false, min, max);
        (t7, t15) = TLanes.Hadamard(t7, t15, flip: false, min, max);
        (t8, t9) = TLanes.Rotate(t8, t9, 56, flip: true);
        (t13, t12) = TLanes.Rotate(t13, t12, 8, flip: true);
        (t10, t11) = TLanes.Rotate(t10, t11, 24, flip: true);
        (t15, t14) = TLanes.Rotate(t15, t14, 40, flip: true);
        (t0, t4) = TLanes.Hadamard(t0, t4, flip: false, min, max);
        (t8, t12) = TLanes.Hadamard(t8, t12, flip: false, min, max);
        (t1, t5) = TLanes.Hadamard(t1, t5, flip: false, min, max);
        (t9, t13) = TLanes.Hadamard(t9, t13, flip: false, min, max);
        (t2, t6) = TLanes.Hadamard(t2, t6, flip: false, min, max);
        (t10, t14) = TLanes.Hadamard(t10, t14, flip: false, min, max);
        (t3, t7) = TLanes.Hadamard(t3, t7, flip: false, min, max);
        (t11, t15) = TLanes.Hadamard(t11, t15, flip: false, min, max);
        (t4, t5) = TLanes.Rotate(t4, t5, 48, flip: true);
        (t12, t13) = TLanes.Rotate(t12, t13, 48, flip: true);
        (t7, t6) = TLanes.Rotate(t7, t6, 16, flip: true);
        (t15, t14) = TLanes.Rotate(t15, t14, 16, flip: true);
        (t0, t2) = TLanes.Hadamard(t0, t2, flip: false, min, max);
        (t4, t6) = TLanes.Hadamard(t4, t6, flip: false, min, max);
        (t8, t10) = TLanes.Hadamard(t8, t10, flip: false, min, max);
        (t12, t14) = TLanes.Hadamard(t12, t14, flip: false, min, max);
        (t1, t3) = TLanes.Hadamard(t1, t3, flip: false, min, max);
        (t5, t7) = TLanes.Hadamard(t5, t7, flip: false, min, max);
        (t9, t11) = TLanes.Hadamard(t9, t11, flip: false, min, max);
        (t13, t15) = TLanes.Hadamard(t13, t15, flip: false, min, max);
        (t2, t3) = TLanes.Rotate(t2, t3, 32, flip: true);
        (t6, t7) = TLanes.Rotate(t6, t7, 32, flip: true);
        (t10, t11) = TLanes.Rotate(t10, t11, 32, flip: true);
        (t14, t15) = TLanes.Rotate(t14, t15, 32, flip: true);
        (t1, t2, t3, t4, t5, t6, t7, t8, t9, t10, t11, t12, t13, t14, t15) = (TLanes.Negate(t8), t12, TLanes.Negate(t4), t6, TLanes.Negate(t14), t10, TLanes.Negate(t2), t3, TLanes.Negate(t11), t15, TLanes.Negate(t7), t5, TLanes.Negate(t13), t9, TLanes.Negate(t1));
        t[0] = t0;
        t[1] = t1;
        t[2] = t2;
        t[3] = t3;
        t[4] = t4;
        t[5] = t5;
        t[6] = t6;
        t[7] = t7;
        t[8] = t8;
        t[9] = t9;
        t[10] = t10;
        t[11] = t11;
        t[12] = t12;
        t[13] = t13;
        t[14] = t14;
        t[15] = t15;
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

        /// <summary>The lanes clamped to [<paramref name="min"/>, <paramref name="max"/>].</summary>
        static abstract TVector Clamp(TVector value, TVector min, TVector max);

        /// <summary>The lanes shifted left by <paramref name="count"/> bits.</summary>
        static abstract TVector ShiftLeft(TVector value, int count);

        /// <summary><c>Round2( value * factor, 12 )</c> of the lanes, with a 64-bit product.</summary>
        static abstract TVector MultiplyRound12(TVector value, int factor);

        /// <summary>The butterfly rotation <c>B( a, b, angle, flip )</c>.</summary>
        static abstract void B(Span<TVector> t, int a, int b, int angle, bool flip);

        /// <summary>The Hadamard rotation <c>H( a, b, flip, r )</c>, whose clamp range is [<paramref name="min"/>, <paramref name="max"/>].</summary>
        static abstract void H(Span<TVector> t, int a, int b, bool flip, TVector min, TVector max);

        /// <summary><see cref="B"/> on the values of <c>T[ a ]</c> and <c>T[ b ]</c>: their new values.</summary>
        static abstract (TVector A, TVector B) Rotate(TVector a, TVector b, int angle, bool flip);

        /// <summary><see cref="H"/> on the values of <c>T[ a ]</c> and <c>T[ b ]</c>: their new values.</summary>
        static abstract (TVector A, TVector B) Hadamard(TVector a, TVector b, bool flip, TVector min, TVector max);

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
        public static Vector128<int> Clamp(Vector128<int> value, Vector128<int> min, Vector128<int> max) => Vector128.Min(Vector128.Max(value, min), max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> ShiftLeft(Vector128<int> value, int count) => value << count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> MultiplyRound12(Vector128<int> value, int factor)
        {
            var factors = Vector128.Create(factor);
            return Av1Simd.RoundNarrow12Split(Av1Simd.MultiplySplitFirst(value, factors), Av1Simd.MultiplySplitSecond(value, factors));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void B(Span<Vector128<int>> t, int a, int b, int angle, bool flip) => (t[a], t[b]) = Rotate(t[a], t[b], angle, flip);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector128<int> A, Vector128<int> B) Rotate(Vector128<int> va, Vector128<int> vb, int angle, bool flip)
        {
            var cos = Vector128.Create(Cos128(angle));
            var sin = Vector128.Create(Cos128(angle - 64));
            var x = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndSubtract(Av1Simd.MultiplySplitFirst(va, cos), vb, sin),
                Av1Simd.MultiplySplitSecondAndSubtract(Av1Simd.MultiplySplitSecond(va, cos), vb, sin));
            var y = Av1Simd.RoundNarrow12Split(
                Av1Simd.MultiplySplitFirstAndAdd(Av1Simd.MultiplySplitFirst(va, sin), vb, cos),
                Av1Simd.MultiplySplitSecondAndAdd(Av1Simd.MultiplySplitSecond(va, sin), vb, cos));
            return flip ? (y, x) : (x, y);
        }

        // Clamp of the 64-bit sum is Clamp of the saturated sum
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void H(Span<Vector128<int>> t, int a, int b, bool flip, Vector128<int> min, Vector128<int> max) => (t[a], t[b]) = Hadamard(t[a], t[b], flip, min, max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector128<int> A, Vector128<int> B) Hadamard(Vector128<int> a, Vector128<int> b, bool flip, Vector128<int> min, Vector128<int> max)
        {
            // H( b, a ) when flipped: T[ b ] is the clamped sum and T[ a ] the clamped difference of b and a
            var (x, y) = flip ? (b, a) : (a, b);
            var sum = Vector128.Min(Vector128.Max(Av1Simd.AddSaturate(x, y), min), max);
            var difference = Vector128.Min(Vector128.Max(Av1Simd.SubtractSaturate(x, y), min), max);
            return flip ? (difference, sum) : (sum, difference);
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

    // The operations on Vector128 with 32-bit products and sums, for the kernels that IsNarrowExact accepts: the values of
    // the 64-bit operations of Lanes128, which never exceed 32 bits there, so that the sums of H cannot saturate either
    private readonly struct NarrowLanes128 : ILanes<Vector128<int>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Create(int value) => Vector128.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Negate(Vector128<int> value) => -value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Clamp(Vector128<int> value, Vector128<int> min, Vector128<int> max) => Vector128.Min(Vector128.Max(value, min), max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> ShiftLeft(Vector128<int> value, int count) => value << count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> MultiplyRound12(Vector128<int> value, int factor) => Round12(value * factor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void B(Span<Vector128<int>> t, int a, int b, int angle, bool flip) => (t[a], t[b]) = Rotate(t[a], t[b], angle, flip);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector128<int> A, Vector128<int> B) Rotate(Vector128<int> va, Vector128<int> vb, int angle, bool flip)
        {
            var cos = Cos128(angle);
            var sin = Cos128(angle - 64);
            var x = Round12((va * cos) - (vb * sin));
            var y = Round12((va * sin) + (vb * cos));
            return flip ? (y, x) : (x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void H(Span<Vector128<int>> t, int a, int b, bool flip, Vector128<int> min, Vector128<int> max) => (t[a], t[b]) = Hadamard(t[a], t[b], flip, min, max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector128<int> A, Vector128<int> B) Hadamard(Vector128<int> a, Vector128<int> b, bool flip, Vector128<int> min, Vector128<int> max)
        {
            // H( b, a ) when flipped: T[ b ] is the clamped sum and T[ a ] the clamped difference of b and a
            var (x, y) = flip ? (b, a) : (a, b);
            var sum = Vector128.Min(Vector128.Max(x + y, min), max);
            var difference = Vector128.Min(Vector128.Max(x - y, min), max);
            return flip ? (difference, sum) : (sum, difference);
        }

        // The steps of the scalar kernel
        public static void InverseAdst4(Span<Vector128<int>> t)
        {
            var t0 = t[0];
            var t1 = t[1];
            var t2 = t[2];
            var t3 = t[3];
            var s0 = (t0 * Av1Constants.Sinpi19) + (t2 * Av1Constants.Sinpi49) + (t3 * Av1Constants.Sinpi29);
            var s1 = (t0 * Av1Constants.Sinpi29) - (t2 * Av1Constants.Sinpi19) - (t3 * Av1Constants.Sinpi49);
            var s3 = t1 * Av1Constants.Sinpi39;
            t[0] = Round12(s0 + s3);
            t[1] = Round12(s1 + s3);
            t[2] = Round12((t0 - t2 + t3) * Av1Constants.Sinpi39);
            t[3] = Round12(s0 + s1 - s3);
        }

        // Round2( x, 12 ) of values whose rounding cannot overflow
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> Round12(Vector128<int> value)
        {
            if (AdvSimd.Arm64.IsSupported)
                return AdvSimd.ShiftRightArithmeticRounded(value, 12);

            return (value + Vector128.Create(1 << 11)) >> 12;
        }
    }

    // NarrowLanes128 on Vector256, with AVX2 (x64 only)
    private readonly struct NarrowLanes256 : ILanes<Vector256<int>>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> Create(int value) => Vector256.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> Negate(Vector256<int> value) => -value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> Clamp(Vector256<int> value, Vector256<int> min, Vector256<int> max) => Vector256.Min(Vector256.Max(value, min), max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> ShiftLeft(Vector256<int> value, int count) => value << count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> MultiplyRound12(Vector256<int> value, int factor) => Round12(value * factor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void B(Span<Vector256<int>> t, int a, int b, int angle, bool flip) => (t[a], t[b]) = Rotate(t[a], t[b], angle, flip);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector256<int> A, Vector256<int> B) Rotate(Vector256<int> va, Vector256<int> vb, int angle, bool flip)
        {
            var cos = Cos128(angle);
            var sin = Cos128(angle - 64);
            var x = Round12((va * cos) - (vb * sin));
            var y = Round12((va * sin) + (vb * cos));
            return flip ? (y, x) : (x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void H(Span<Vector256<int>> t, int a, int b, bool flip, Vector256<int> min, Vector256<int> max) => (t[a], t[b]) = Hadamard(t[a], t[b], flip, min, max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector256<int> A, Vector256<int> B) Hadamard(Vector256<int> a, Vector256<int> b, bool flip, Vector256<int> min, Vector256<int> max)
        {
            // H( b, a ) when flipped: T[ b ] is the clamped sum and T[ a ] the clamped difference of b and a
            var (x, y) = flip ? (b, a) : (a, b);
            var sum = Vector256.Min(Vector256.Max(x + y, min), max);
            var difference = Vector256.Min(Vector256.Max(x - y, min), max);
            return flip ? (difference, sum) : (sum, difference);
        }

        // The steps of the scalar kernel
        public static void InverseAdst4(Span<Vector256<int>> t)
        {
            var t0 = t[0];
            var t1 = t[1];
            var t2 = t[2];
            var t3 = t[3];
            var s0 = (t0 * Av1Constants.Sinpi19) + (t2 * Av1Constants.Sinpi49) + (t3 * Av1Constants.Sinpi29);
            var s1 = (t0 * Av1Constants.Sinpi29) - (t2 * Av1Constants.Sinpi19) - (t3 * Av1Constants.Sinpi49);
            var s3 = t1 * Av1Constants.Sinpi39;
            t[0] = Round12(s0 + s3);
            t[1] = Round12(s1 + s3);
            t[2] = Round12((t0 - t2 + t3) * Av1Constants.Sinpi39);
            t[3] = Round12(s0 + s1 - s3);
        }

        // Round2( x, 12 ) of values whose rounding cannot overflow
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<int> Round12(Vector256<int> value) => (value + Vector256.Create(1 << 11)) >> 12;
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
        public static Vector256<int> Clamp(Vector256<int> value, Vector256<int> min, Vector256<int> max) => Vector256.Min(Vector256.Max(value, min), max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> ShiftLeft(Vector256<int> value, int count) => value << count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> MultiplyRound12(Vector256<int> value, int factor)
        {
            var factors = Vector256.Create(factor);
            return RoundNarrow12(Multiply(value, factors), Multiply(Odd(value), factors));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void B(Span<Vector256<int>> t, int a, int b, int angle, bool flip) => (t[a], t[b]) = Rotate(t[a], t[b], angle, flip);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector256<int> A, Vector256<int> B) Rotate(Vector256<int> va, Vector256<int> vb, int angle, bool flip)
        {
            var cos = Vector256.Create(Cos128(angle));
            var sin = Vector256.Create(Cos128(angle - 64));
            var oddA = Odd(va);
            var oddB = Odd(vb);
            var x = RoundNarrow12(Multiply(va, cos) - Multiply(vb, sin), Multiply(oddA, cos) - Multiply(oddB, sin));
            var y = RoundNarrow12(Multiply(va, sin) + Multiply(vb, cos), Multiply(oddA, sin) + Multiply(oddB, cos));
            return flip ? (y, x) : (x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void H(Span<Vector256<int>> t, int a, int b, bool flip, Vector256<int> min, Vector256<int> max) => (t[a], t[b]) = Hadamard(t[a], t[b], flip, min, max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector256<int> A, Vector256<int> B) Hadamard(Vector256<int> a, Vector256<int> b, bool flip, Vector256<int> min, Vector256<int> max)
        {
            // H( b, a ) when flipped: T[ b ] is the clamped sum and T[ a ] the clamped difference of b and a
            var (x, y) = flip ? (b, a) : (a, b);
            var sum = Vector256.Min(Vector256.Max(Vector256.AddSaturate(x, y), min), max);
            var difference = Vector256.Min(Vector256.Max(Vector256.SubtractSaturate(x, y), min), max);
            return flip ? (difference, sum) : (sum, difference);
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
