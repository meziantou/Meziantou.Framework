using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The inverse transforms of the VarDCT mode (ISO/IEC 18181-1, section I.2.3): from the dequantized coefficients of a
/// transform to its samples. The DCT of the format maps a constant block to its value: the forward transform divides by
/// the number of samples, the inverse one is <c>x[n] = X[0] + sqrt(2) * sum(X[k] * cos(pi * k * (n + 1/2) / N))</c>.
/// </summary>
/// <remarks>
/// <para>
/// Layout of the coefficients: a block has the smaller side of its transform as rows. When the transform is wider than it
/// is high, a row holds the horizontal frequencies of one vertical frequency; otherwise (square transforms included) the
/// block is transposed, and a row holds the vertical frequencies of one horizontal frequency.
/// </para>
/// <para>
/// The one-dimensional inverse DCT is computed by halving: the even coefficients are the inverse DCT of half the length,
/// and the odd ones become one after replacing each by its sum with the previous odd one, divided by twice the cosine of
/// the sample position. Every operation is a float32 one in a fixed order, so that the result does not depend on the
/// machine.
/// </para>
/// </remarks>
internal static class JpegXlInverseTransform
{
    /// <summary>The largest side of a transform, in samples.</summary>
    public const int MaxSize = 256;

    /// <summary>
    /// The number of float values of scratch memory the transforms of at most <see cref="MaxSize"/> samples per side need:
    /// the vectorized DCT keeps up to eight lines of the largest side, their transform, and as much scratch memory.
    /// </summary>
    public const int ScratchLength = 24 * MaxSize;

    // For a transform of N samples, at N / 2 + i: 1 / (2 * cos((i + 1/2) * pi / N))
    private static readonly float[] HalfSecants = CreateHalfSecants();

    // For each number of blocks N (1 to 32), at N + k: 1 / (cos(k * pi / 16N) * cos(k * pi / 8N) * cos(k * pi / 4N)), the
    // factor that turns the coefficient k of a DCT of N values into the coefficient k of a DCT of 8N samples
    private static readonly float[] LowFrequencyScales = CreateLowFrequencyScales();

    // For each number of values N (1 to 32), at N + (k * N) + n: cos(pi * k * (n + 1/2) / N) * (k == 0 ? 1 : sqrt(2)) / N
    private static readonly float[][] ForwardBasis = CreateForwardBasis();

    /// <summary>Computes the samples of a block from its coefficients, for every transform type.</summary>
    /// <param name="transform">The transform type.</param>
    /// <param name="coefficients">The coefficients, in the layout of the format; they are overwritten.</param>
    /// <param name="output">Receives the samples, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows of <paramref name="output"/>.</param>
    /// <param name="scratch">Scratch memory of <see cref="ScratchLength"/> values.</param>
    public static void Apply(int transform, Span<float> coefficients, Span<float> output, int stride, Span<float> scratch)
    {
        switch (transform)
        {
            case JpegXlVarDctTables.TransformIdentity:
                ApplyIdentity(coefficients, output, stride);
                break;

            case JpegXlVarDctTables.TransformDct2:
                ApplyDct2(coefficients, output, stride);
                break;

            case JpegXlVarDctTables.TransformDct4:
                ApplyDct4(coefficients, output, stride, scratch);
                break;

            case JpegXlVarDctTables.TransformDct4x8:
            case JpegXlVarDctTables.TransformDct8x4:
                ApplyHalves(coefficients, output, stride, scratch, isWide: transform == JpegXlVarDctTables.TransformDct4x8);
                break;

            case >= JpegXlVarDctTables.TransformAfv0 and <= JpegXlVarDctTables.TransformAfv3:
                ApplyAfv(transform - JpegXlVarDctTables.TransformAfv0, coefficients, output, stride, scratch);
                break;

            default:
                InverseDct(coefficients, JpegXlVarDctTables.BlocksHigh[transform] * 8, JpegXlVarDctTables.BlocksWide[transform] * 8, output, stride, scratch);
                break;
        }
    }

    /// <summary>The inverse DCT of a block of <paramref name="rows"/> by <paramref name="columns"/> samples.</summary>
    /// <param name="coefficients">The coefficients, in the layout of the format; they are overwritten.</param>
    /// <param name="rows">The number of rows of samples.</param>
    /// <param name="columns">The number of columns of samples.</param>
    /// <param name="output">Receives the samples.</param>
    /// <param name="stride">The distance between two rows of <paramref name="output"/>.</param>
    /// <param name="scratch">Scratch memory of at least 24 times the larger side.</param>
    public static void InverseDct(Span<float> coefficients, int rows, int columns, Span<float> output, int stride, Span<float> scratch)
    {
        // Four (or eight) one-dimensional transforms at once, one per lane, each with the operations of the scalar one in the
        // same order
        if (Vector128.IsHardwareAccelerated && rows >= 4 && columns >= 4)
        {
            if (rows == 8 && columns == 8)
            {
                InverseDct8x8(coefficients, output, stride);
                return;
            }

            if (Vector256.IsHardwareAccelerated && Avx.IsSupported && rows >= 8 && columns >= 8)
            {
                InverseDctVector256(coefficients, rows, columns, output, stride, scratch);
                return;
            }

            InverseDctVector(coefficients, rows, columns, output, stride, scratch);
            return;
        }

        InverseDctScalar(coefficients, rows, columns, output, stride, scratch);
    }

    /// <summary>The scalar reference of <see cref="InverseDct(Span{float}, int, int, Span{float}, int, Span{float})"/>.</summary>
    public static void InverseDctScalar(Span<float> coefficients, int rows, int columns, Span<float> output, int stride, Span<float> scratch)
    {
        var line = scratch[..Math.Max(rows, columns)];
        var work = scratch.Slice(Math.Max(rows, columns), Math.Max(rows, columns));
        if (rows < columns)
        {
            // Rows of horizontal frequencies: each row becomes samples, then each column of vertical frequencies
            for (var v = 0; v < rows; v++)
            {
                InverseDct(coefficients.Slice(v * columns, columns), work);
            }

            var column = line[..rows];
            for (var x = 0; x < columns; x++)
            {
                for (var v = 0; v < rows; v++)
                {
                    column[v] = coefficients[(v * columns) + x];
                }

                InverseDct(column, work);
                for (var y = 0; y < rows; y++)
                {
                    output[(y * stride) + x] = column[y];
                }
            }
        }
        else
        {
            // Transposed: rows of vertical frequencies, which become the samples of a column for one horizontal frequency
            for (var u = 0; u < columns; u++)
            {
                InverseDct(coefficients.Slice(u * rows, rows), work);
            }

            var row = line[..columns];
            for (var y = 0; y < rows; y++)
            {
                for (var u = 0; u < columns; u++)
                {
                    row[u] = coefficients[(u * rows) + y];
                }

                InverseDct(row, work);
                row.CopyTo(output.Slice(y * stride, columns));
            }
        }
    }

    /// <summary>The one-dimensional inverse DCT of a power-of-two number of values, in place.</summary>
    /// <param name="values">The coefficients, replaced with the samples.</param>
    /// <param name="scratch">Scratch memory of the same length.</param>
    public static void InverseDct(Span<float> values, Span<float> scratch)
    {
        var count = values.Length;
        if (count == 1)
            return;

        if (count == 2)
        {
            var sum = values[0] + values[1];
            values[1] = values[0] - values[1];
            values[0] = sum;
            return;
        }

        var half = count / 2;
        var even = scratch[..half];
        var odd = scratch.Slice(half, half);
        for (var i = 0; i < half; i++)
        {
            even[i] = values[2 * i];
        }

        odd[0] = MathF.Sqrt(2) * values[1];
        for (var i = 1; i < half; i++)
        {
            odd[i] = values[(2 * i) - 1] + values[(2 * i) + 1];
        }

        InverseDct(even, values[..half]);
        InverseDct(odd, values.Slice(half, half));
        var secants = HalfSecants.AsSpan(half, half);
        for (var i = 0; i < half; i++)
        {
            var scaled = odd[i] * secants[i];
            values[i] = even[i] + scaled;
            values[count - 1 - i] = even[i] - scaled;
        }
    }

    /// <summary>
    /// The inverse DCT of a block whose sides are multiples of 4: the one-dimensional transforms are computed four at a
    /// time, one per lane of a vector. The lines that are contiguous in memory are transposed 4x4 values at a time on the
    /// way in or out; the others are read in place, and their samples stored as vectors.
    /// </summary>
    private static void InverseDctVector(Span<float> coefficients, int rows, int columns, Span<float> output, int stride, Span<float> scratch)
    {
        // Every position read or written is checked here, once for the block
        var size = Math.Max(rows, columns);
        if (coefficients.Length < rows * columns || output.Length < ((rows - 1) * stride) + columns || scratch.Length < 12 * size)
            throw new ArgumentOutOfRangeException(nameof(output));

        var vectors = unsafe(MemoryMarshal.Cast<float, Vector128<float>>(scratch));
        var line = vectors[..size];
        var samples = vectors.Slice(size, size);
        ref var work = ref vectors[2 * size];
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        ref var first = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var lineStart = ref unsafe(Unsafe.As<Vector128<float>, float>(ref MemoryMarshal.GetReference(line)));
        if (rows < columns)
        {
            // Rows of horizontal frequencies, four at a time, then the columns of vertical frequencies, four at a time
            for (var v = 0; v < rows; v += 4)
            {
                var block = coefficients.Slice(v * columns, 4 * columns);
                LoadTransposed(block, columns, line);
                InverseDctLines(ref lineStart, 4, ref first, ref work, columns);
                StoreTransposed(samples, block, columns);
            }

            for (var x = 0; x < columns; x += 4)
            {
                InverseDctLines(ref coefficients[x], columns, ref first, ref work, rows);
                for (var y = 0; y < rows; y++)
                {
                    unsafe
                    {
                        Unsafe.Add(ref first, y).StoreUnsafe(ref target, (nuint)((y * stride) + x));
                    }
                }
            }
        }
        else
        {
            // Transposed: rows of vertical frequencies, four at a time, then the rows of samples, four at a time
            for (var u = 0; u < columns; u += 4)
            {
                var block = coefficients.Slice(u * rows, 4 * rows);
                LoadTransposed(block, rows, line);
                InverseDctLines(ref lineStart, 4, ref first, ref work, rows);
                StoreTransposed(samples, block, rows);
            }

            for (var y = 0; y < rows; y += 4)
            {
                InverseDctLines(ref coefficients[y], rows, ref first, ref work, columns);
                for (var x = 0; x < columns; x += 4)
                {
                    unsafe
                    {
                        ref var column = ref Unsafe.Add(ref first, x);
                        StoreRows(column, Unsafe.Add(ref column, 1), Unsafe.Add(ref column, 2), Unsafe.Add(ref column, 3), ref Unsafe.Add(ref target, y * stride), (nuint)stride, (nuint)x);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The operations of <see cref="InverseDctVector"/> on eight lines at a time, for a block whose sides are multiples of 8:
    /// the lines that are contiguous in memory are transposed 8x8 values at a time.
    /// </summary>
    private static void InverseDctVector256(Span<float> coefficients, int rows, int columns, Span<float> output, int stride, Span<float> scratch)
    {
        var size = Math.Max(rows, columns);
        if (coefficients.Length < rows * columns || output.Length < ((rows - 1) * stride) + columns || scratch.Length < 24 * size)
            throw new ArgumentOutOfRangeException(nameof(output));

        var vectors = unsafe(MemoryMarshal.Cast<float, Vector256<float>>(scratch));
        ref var line = ref vectors[0];
        ref var first = ref vectors[size];
        ref var work = ref vectors[2 * size];
        ref var source = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        ref var lineStart = ref unsafe(Unsafe.As<Vector256<float>, float>(ref line));
        unsafe
        {
            if (rows < columns)
            {
                // Rows of horizontal frequencies, eight at a time, then the columns of vertical frequencies, eight at a time
                for (var v = 0; v < rows; v += 8)
                {
                    ref var block = ref Unsafe.Add(ref source, v * columns);
                    LoadTransposed(ref block, columns, ref line);
                    InverseDctLines(ref lineStart, 8, ref first, ref work, columns);
                    StoreTransposed(ref first, ref block, columns);
                }

                for (var x = 0; x < columns; x += 8)
                {
                    InverseDctLines(ref Unsafe.Add(ref source, x), columns, ref first, ref work, rows);
                    for (var y = 0; y < rows; y++)
                    {
                        Unsafe.Add(ref first, y).StoreUnsafe(ref target, (nuint)((y * stride) + x));
                    }
                }
            }
            else
            {
                // Transposed: rows of vertical frequencies, eight at a time, then the rows of samples, eight at a time
                for (var u = 0; u < columns; u += 8)
                {
                    ref var block = ref Unsafe.Add(ref source, u * rows);
                    LoadTransposed(ref block, rows, ref line);
                    InverseDctLines(ref lineStart, 8, ref first, ref work, rows);
                    StoreTransposed(ref first, ref block, rows);
                }

                for (var y = 0; y < rows; y += 8)
                {
                    InverseDctLines(ref Unsafe.Add(ref source, y), rows, ref first, ref work, columns);
                    for (var x = 0; x < columns; x += 8)
                    {
                        ref var column = ref Unsafe.Add(ref first, x);
                        var r0 = column;
                        var r1 = Unsafe.Add(ref column, 1);
                        var r2 = Unsafe.Add(ref column, 2);
                        var r3 = Unsafe.Add(ref column, 3);
                        var r4 = Unsafe.Add(ref column, 4);
                        var r5 = Unsafe.Add(ref column, 5);
                        var r6 = Unsafe.Add(ref column, 6);
                        var r7 = Unsafe.Add(ref column, 7);
                        Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
                        ref var row = ref Unsafe.Add(ref target, (y * stride) + x);
                        r0.StoreUnsafe(ref row);
                        r1.StoreUnsafe(ref row, (nuint)stride);
                        r2.StoreUnsafe(ref row, (nuint)(2 * stride));
                        r3.StoreUnsafe(ref row, (nuint)(3 * stride));
                        r4.StoreUnsafe(ref row, (nuint)(4 * stride));
                        r5.StoreUnsafe(ref row, (nuint)(5 * stride));
                        r6.StoreUnsafe(ref row, (nuint)(6 * stride));
                        r7.StoreUnsafe(ref row, (nuint)(7 * stride));
                    }
                }
            }
        }
    }

    /// <summary>Loads eight lines of <paramref name="length"/> values, one after the other, as the lanes of <paramref name="destination"/>.</summary>
    private static void LoadTransposed(ref float source, int length, ref Vector256<float> destination)
    {
        unsafe
        {
            for (var k = 0; k < length; k += 8)
            {
                ref var start = ref Unsafe.Add(ref source, k);
                var r0 = Vector256.LoadUnsafe(ref start);
                var r1 = Vector256.LoadUnsafe(ref start, (nuint)length);
                var r2 = Vector256.LoadUnsafe(ref start, (nuint)(2 * length));
                var r3 = Vector256.LoadUnsafe(ref start, (nuint)(3 * length));
                var r4 = Vector256.LoadUnsafe(ref start, (nuint)(4 * length));
                var r5 = Vector256.LoadUnsafe(ref start, (nuint)(5 * length));
                var r6 = Vector256.LoadUnsafe(ref start, (nuint)(6 * length));
                var r7 = Vector256.LoadUnsafe(ref start, (nuint)(7 * length));
                Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
                ref var vectors = ref Unsafe.Add(ref destination, k);
                vectors = r0;
                Unsafe.Add(ref vectors, 1) = r1;
                Unsafe.Add(ref vectors, 2) = r2;
                Unsafe.Add(ref vectors, 3) = r3;
                Unsafe.Add(ref vectors, 4) = r4;
                Unsafe.Add(ref vectors, 5) = r5;
                Unsafe.Add(ref vectors, 6) = r6;
                Unsafe.Add(ref vectors, 7) = r7;
            }
        }
    }

    /// <summary>Stores the lanes of <paramref name="source"/> as eight lines of <paramref name="length"/> values, one after the other.</summary>
    private static void StoreTransposed(ref Vector256<float> source, ref float destination, int length)
    {
        unsafe
        {
            for (var k = 0; k < length; k += 8)
            {
                ref var vectors = ref Unsafe.Add(ref source, k);
                var r0 = vectors;
                var r1 = Unsafe.Add(ref vectors, 1);
                var r2 = Unsafe.Add(ref vectors, 2);
                var r3 = Unsafe.Add(ref vectors, 3);
                var r4 = Unsafe.Add(ref vectors, 4);
                var r5 = Unsafe.Add(ref vectors, 5);
                var r6 = Unsafe.Add(ref vectors, 6);
                var r7 = Unsafe.Add(ref vectors, 7);
                Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
                ref var start = ref Unsafe.Add(ref destination, k);
                r0.StoreUnsafe(ref start);
                r1.StoreUnsafe(ref start, (nuint)length);
                r2.StoreUnsafe(ref start, (nuint)(2 * length));
                r3.StoreUnsafe(ref start, (nuint)(3 * length));
                r4.StoreUnsafe(ref start, (nuint)(4 * length));
                r5.StoreUnsafe(ref start, (nuint)(5 * length));
                r6.StoreUnsafe(ref start, (nuint)(6 * length));
                r7.StoreUnsafe(ref start, (nuint)(7 * length));
            }
        }
    }

    /// <summary>The operations of <see cref="InverseDctLines(ref float, nint, ref Vector128{float}, ref Vector128{float}, int)"/> on eight lines at a time.</summary>
    private static void InverseDctLines(ref float source, nint sourceStride, ref Vector256<float> destination, ref Vector256<float> scratch, int count)
    {
        unsafe
        {
            switch (count)
            {
                case 8:
                {
                    var v0 = Vector256.LoadUnsafe(ref source);
                    var v1 = Vector256.LoadUnsafe(ref source, (nuint)sourceStride);
                    var v2 = Vector256.LoadUnsafe(ref source, (nuint)(2 * sourceStride));
                    var v3 = Vector256.LoadUnsafe(ref source, (nuint)(3 * sourceStride));
                    var v4 = Vector256.LoadUnsafe(ref source, (nuint)(4 * sourceStride));
                    var v5 = Vector256.LoadUnsafe(ref source, (nuint)(5 * sourceStride));
                    var v6 = Vector256.LoadUnsafe(ref source, (nuint)(6 * sourceStride));
                    var v7 = Vector256.LoadUnsafe(ref source, (nuint)(7 * sourceStride));
                    InverseDct8(ref v0, ref v1, ref v2, ref v3, ref v4, ref v5, ref v6, ref v7);
                    destination = v0;
                    Unsafe.Add(ref destination, 1) = v1;
                    Unsafe.Add(ref destination, 2) = v2;
                    Unsafe.Add(ref destination, 3) = v3;
                    Unsafe.Add(ref destination, 4) = v4;
                    Unsafe.Add(ref destination, 5) = v5;
                    Unsafe.Add(ref destination, 6) = v6;
                    Unsafe.Add(ref destination, 7) = v7;
                    return;
                }

                case 16:
                    InverseDct16(ref source, sourceStride, ref destination);
                    return;
            }

            var half = count / 2;
            var previous = Vector256.LoadUnsafe(ref source, (nuint)sourceStride);
            scratch = Vector256.Create(MathF.Sqrt(2)) * previous;
            for (var i = 1; i < half; i++)
            {
                var next = Vector256.LoadUnsafe(ref source, (nuint)(((2 * i) + 1) * sourceStride));
                Unsafe.Add(ref scratch, i) = previous + next;
                previous = next;
            }

            ref var odd = ref Unsafe.Add(ref destination, half);
            ref var rest = ref Unsafe.Add(ref scratch, half);
            InverseDctLines(ref source, 2 * sourceStride, ref destination, ref rest, half);
            InverseDctLines(ref Unsafe.As<Vector256<float>, float>(ref scratch), 8, ref odd, ref rest, half);
            ref var secants = ref MemoryMarshal.GetArrayDataReference(HalfSecants);
            for (var i = 0; i < half / 2; i++)
            {
                var j = half - 1 - i;
                var evenI = Unsafe.Add(ref destination, i);
                var evenJ = Unsafe.Add(ref destination, j);
                var scaledI = Unsafe.Add(ref odd, i) * Vector256.Create(Unsafe.Add(ref secants, half + i));
                var scaledJ = Unsafe.Add(ref odd, j) * Vector256.Create(Unsafe.Add(ref secants, half + j));
                Unsafe.Add(ref destination, i) = evenI + scaledI;
                Unsafe.Add(ref odd, j) = evenI - scaledI;
                Unsafe.Add(ref destination, j) = evenJ + scaledJ;
                Unsafe.Add(ref odd, i) = evenJ - scaledJ;
            }
        }
    }

    /// <summary>The leaf of <see cref="InverseDctLines(ref float, nint, ref Vector256{float}, ref Vector256{float}, int)"/>: eight inverse DCTs of 16 values.</summary>
    private static void InverseDct16(ref float source, nint stride, ref Vector256<float> destination)
    {
        unsafe
        {
            var e0 = Vector256.LoadUnsafe(ref source);
            var v1 = Vector256.LoadUnsafe(ref source, (nuint)stride);
            var e1 = Vector256.LoadUnsafe(ref source, (nuint)(2 * stride));
            var v3 = Vector256.LoadUnsafe(ref source, (nuint)(3 * stride));
            var e2 = Vector256.LoadUnsafe(ref source, (nuint)(4 * stride));
            var v5 = Vector256.LoadUnsafe(ref source, (nuint)(5 * stride));
            var e3 = Vector256.LoadUnsafe(ref source, (nuint)(6 * stride));
            var v7 = Vector256.LoadUnsafe(ref source, (nuint)(7 * stride));
            var e4 = Vector256.LoadUnsafe(ref source, (nuint)(8 * stride));
            var v9 = Vector256.LoadUnsafe(ref source, (nuint)(9 * stride));
            var e5 = Vector256.LoadUnsafe(ref source, (nuint)(10 * stride));
            var v11 = Vector256.LoadUnsafe(ref source, (nuint)(11 * stride));
            var e6 = Vector256.LoadUnsafe(ref source, (nuint)(12 * stride));
            var v13 = Vector256.LoadUnsafe(ref source, (nuint)(13 * stride));
            var e7 = Vector256.LoadUnsafe(ref source, (nuint)(14 * stride));
            var v15 = Vector256.LoadUnsafe(ref source, (nuint)(15 * stride));
            var o0 = Vector256.Create(MathF.Sqrt(2)) * v1;
            var o1 = v1 + v3;
            var o2 = v3 + v5;
            var o3 = v5 + v7;
            var o4 = v7 + v9;
            var o5 = v9 + v11;
            var o6 = v11 + v13;
            var o7 = v13 + v15;
            InverseDct8(ref e0, ref e1, ref e2, ref e3, ref e4, ref e5, ref e6, ref e7);
            InverseDct8(ref o0, ref o1, ref o2, ref o3, ref o4, ref o5, ref o6, ref o7);
            var secants = HalfSecants;
            var scaled = o0 * Vector256.Create(secants[8]);
            destination = e0 + scaled;
            Unsafe.Add(ref destination, 15) = e0 - scaled;
            scaled = o1 * Vector256.Create(secants[9]);
            Unsafe.Add(ref destination, 1) = e1 + scaled;
            Unsafe.Add(ref destination, 14) = e1 - scaled;
            scaled = o2 * Vector256.Create(secants[10]);
            Unsafe.Add(ref destination, 2) = e2 + scaled;
            Unsafe.Add(ref destination, 13) = e2 - scaled;
            scaled = o3 * Vector256.Create(secants[11]);
            Unsafe.Add(ref destination, 3) = e3 + scaled;
            Unsafe.Add(ref destination, 12) = e3 - scaled;
            scaled = o4 * Vector256.Create(secants[12]);
            Unsafe.Add(ref destination, 4) = e4 + scaled;
            Unsafe.Add(ref destination, 11) = e4 - scaled;
            scaled = o5 * Vector256.Create(secants[13]);
            Unsafe.Add(ref destination, 5) = e5 + scaled;
            Unsafe.Add(ref destination, 10) = e5 - scaled;
            scaled = o6 * Vector256.Create(secants[14]);
            Unsafe.Add(ref destination, 6) = e6 + scaled;
            Unsafe.Add(ref destination, 9) = e6 - scaled;
            scaled = o7 * Vector256.Create(secants[15]);
            Unsafe.Add(ref destination, 7) = e7 + scaled;
            Unsafe.Add(ref destination, 8) = e7 - scaled;
        }
    }

    /// <summary>
    /// Four one-dimensional inverse DCTs of a power-of-two number of values (4 or more), one per lane: the operations of
    /// <see cref="InverseDct(Span{float}, Span{float})"/>, in the same order. The values are read in place, a vector of four
    /// lines every <paramref name="sourceStride"/> floats, and the samples written one vector after the other.
    /// </summary>
    /// <param name="source">The first vector of coefficients.</param>
    /// <param name="sourceStride">The distance between two vectors of coefficients, in floats.</param>
    /// <param name="destination">Receives the <paramref name="count"/> vectors of samples; it does not overlap the coefficients.</param>
    /// <param name="scratch">Scratch memory of <paramref name="count"/> vectors.</param>
    /// <param name="count">The number of values of a line.</param>
    private static void InverseDctLines(ref float source, nint sourceStride, ref Vector128<float> destination, ref Vector128<float> scratch, int count)
    {
        unsafe
        {
            switch (count)
            {
                case 4:
                {
                    var v0 = Vector128.LoadUnsafe(ref source);
                    var v1 = Vector128.LoadUnsafe(ref source, (nuint)sourceStride);
                    var v2 = Vector128.LoadUnsafe(ref source, (nuint)(2 * sourceStride));
                    var v3 = Vector128.LoadUnsafe(ref source, (nuint)(3 * sourceStride));
                    InverseDct4(ref v0, ref v1, ref v2, ref v3);
                    destination = v0;
                    Unsafe.Add(ref destination, 1) = v1;
                    Unsafe.Add(ref destination, 2) = v2;
                    Unsafe.Add(ref destination, 3) = v3;
                    return;
                }

                case 8:
                {
                    var v0 = Vector128.LoadUnsafe(ref source);
                    var v1 = Vector128.LoadUnsafe(ref source, (nuint)sourceStride);
                    var v2 = Vector128.LoadUnsafe(ref source, (nuint)(2 * sourceStride));
                    var v3 = Vector128.LoadUnsafe(ref source, (nuint)(3 * sourceStride));
                    var v4 = Vector128.LoadUnsafe(ref source, (nuint)(4 * sourceStride));
                    var v5 = Vector128.LoadUnsafe(ref source, (nuint)(5 * sourceStride));
                    var v6 = Vector128.LoadUnsafe(ref source, (nuint)(6 * sourceStride));
                    var v7 = Vector128.LoadUnsafe(ref source, (nuint)(7 * sourceStride));
                    InverseDct8(ref v0, ref v1, ref v2, ref v3, ref v4, ref v5, ref v6, ref v7);
                    destination = v0;
                    Unsafe.Add(ref destination, 1) = v1;
                    Unsafe.Add(ref destination, 2) = v2;
                    Unsafe.Add(ref destination, 3) = v3;
                    Unsafe.Add(ref destination, 4) = v4;
                    Unsafe.Add(ref destination, 5) = v5;
                    Unsafe.Add(ref destination, 6) = v6;
                    Unsafe.Add(ref destination, 7) = v7;
                    return;
                }

                case 16:
                    InverseDct16(ref source, sourceStride, ref destination);
                    return;
            }

            // The odd coefficients, each added to the previous one, go to the scratch memory; the even ones are read in place
            var half = count / 2;
            var previous = Vector128.LoadUnsafe(ref source, (nuint)sourceStride);
            scratch = Vector128.Create(MathF.Sqrt(2)) * previous;
            for (var i = 1; i < half; i++)
            {
                var next = Vector128.LoadUnsafe(ref source, (nuint)(((2 * i) + 1) * sourceStride));
                Unsafe.Add(ref scratch, i) = previous + next;
                previous = next;
            }

            ref var odd = ref Unsafe.Add(ref destination, half);
            ref var rest = ref Unsafe.Add(ref scratch, half);
            InverseDctLines(ref source, 2 * sourceStride, ref destination, ref rest, half);
            InverseDctLines(ref Unsafe.As<Vector128<float>, float>(ref scratch), 4, ref odd, ref rest, half);

            // The samples i and count - 1 - i are written over the even and odd transforms at i and half - 1 - i, which the
            // pair at half - 1 - i reads: both pairs are computed together
            ref var secants = ref MemoryMarshal.GetArrayDataReference(HalfSecants);
            for (var i = 0; i < half / 2; i++)
            {
                var j = half - 1 - i;
                var evenI = Unsafe.Add(ref destination, i);
                var evenJ = Unsafe.Add(ref destination, j);
                var scaledI = Unsafe.Add(ref odd, i) * Vector128.Create(Unsafe.Add(ref secants, half + i));
                var scaledJ = Unsafe.Add(ref odd, j) * Vector128.Create(Unsafe.Add(ref secants, half + j));
                Unsafe.Add(ref destination, i) = evenI + scaledI;
                Unsafe.Add(ref odd, j) = evenI - scaledI;
                Unsafe.Add(ref destination, j) = evenJ + scaledJ;
                Unsafe.Add(ref odd, i) = evenJ - scaledJ;
            }
        }
    }

    /// <summary>The leaf of <see cref="InverseDctLines(ref float, nint, ref Vector128{float}, ref Vector128{float}, int)"/>: four inverse DCTs of 16 values, in registers.</summary>
    private static void InverseDct16(ref float source, nint stride, ref Vector128<float> destination)
    {
        unsafe
        {
            var e0 = Vector128.LoadUnsafe(ref source);
            var v1 = Vector128.LoadUnsafe(ref source, (nuint)stride);
            var e1 = Vector128.LoadUnsafe(ref source, (nuint)(2 * stride));
            var v3 = Vector128.LoadUnsafe(ref source, (nuint)(3 * stride));
            var e2 = Vector128.LoadUnsafe(ref source, (nuint)(4 * stride));
            var v5 = Vector128.LoadUnsafe(ref source, (nuint)(5 * stride));
            var e3 = Vector128.LoadUnsafe(ref source, (nuint)(6 * stride));
            var v7 = Vector128.LoadUnsafe(ref source, (nuint)(7 * stride));
            var e4 = Vector128.LoadUnsafe(ref source, (nuint)(8 * stride));
            var v9 = Vector128.LoadUnsafe(ref source, (nuint)(9 * stride));
            var e5 = Vector128.LoadUnsafe(ref source, (nuint)(10 * stride));
            var v11 = Vector128.LoadUnsafe(ref source, (nuint)(11 * stride));
            var e6 = Vector128.LoadUnsafe(ref source, (nuint)(12 * stride));
            var v13 = Vector128.LoadUnsafe(ref source, (nuint)(13 * stride));
            var e7 = Vector128.LoadUnsafe(ref source, (nuint)(14 * stride));
            var v15 = Vector128.LoadUnsafe(ref source, (nuint)(15 * stride));
            var o0 = Vector128.Create(MathF.Sqrt(2)) * v1;
            var o1 = v1 + v3;
            var o2 = v3 + v5;
            var o3 = v5 + v7;
            var o4 = v7 + v9;
            var o5 = v9 + v11;
            var o6 = v11 + v13;
            var o7 = v13 + v15;
            InverseDct8(ref e0, ref e1, ref e2, ref e3, ref e4, ref e5, ref e6, ref e7);
            InverseDct8(ref o0, ref o1, ref o2, ref o3, ref o4, ref o5, ref o6, ref o7);
            var secants = HalfSecants;
            var scaled = o0 * Vector128.Create(secants[8]);
            destination = e0 + scaled;
            Unsafe.Add(ref destination, 15) = e0 - scaled;
            scaled = o1 * Vector128.Create(secants[9]);
            Unsafe.Add(ref destination, 1) = e1 + scaled;
            Unsafe.Add(ref destination, 14) = e1 - scaled;
            scaled = o2 * Vector128.Create(secants[10]);
            Unsafe.Add(ref destination, 2) = e2 + scaled;
            Unsafe.Add(ref destination, 13) = e2 - scaled;
            scaled = o3 * Vector128.Create(secants[11]);
            Unsafe.Add(ref destination, 3) = e3 + scaled;
            Unsafe.Add(ref destination, 12) = e3 - scaled;
            scaled = o4 * Vector128.Create(secants[12]);
            Unsafe.Add(ref destination, 4) = e4 + scaled;
            Unsafe.Add(ref destination, 11) = e4 - scaled;
            scaled = o5 * Vector128.Create(secants[13]);
            Unsafe.Add(ref destination, 5) = e5 + scaled;
            Unsafe.Add(ref destination, 10) = e5 - scaled;
            scaled = o6 * Vector128.Create(secants[14]);
            Unsafe.Add(ref destination, 6) = e6 + scaled;
            Unsafe.Add(ref destination, 9) = e6 - scaled;
            scaled = o7 * Vector128.Create(secants[15]);
            Unsafe.Add(ref destination, 7) = e7 + scaled;
            Unsafe.Add(ref destination, 8) = e7 - scaled;
        }
    }

    /// <summary>
    /// The inverse DCT of a block of 8x8 samples, kept in registers: the operations of <see cref="InverseDctVector"/> in
    /// the same order, without its stores and loads between the two passes. With eight lanes, each pass is one 8x8
    /// transposition and eight transforms; with four, it is four 4x4 transpositions and two sets of four.
    /// </summary>
    /// <param name="coefficients">The 64 coefficients, a row of vertical frequencies per horizontal frequency.</param>
    /// <param name="output">Receives the samples.</param>
    /// <param name="stride">The distance between two rows of <paramref name="output"/>.</param>
    private static void InverseDct8x8(ReadOnlySpan<float> coefficients, Span<float> output, int stride)
    {
        // Every position read or written is checked here, once for the block
        if (coefficients.Length < 64 || stride < 8 || output.Length < (7 * stride) + 8)
            throw new ArgumentOutOfRangeException(nameof(output));

        ref var source = ref unsafe(MemoryMarshal.GetReference(coefficients));
        ref var target = ref unsafe(MemoryMarshal.GetReference(output));
        if (Vector256.IsHardwareAccelerated && Avx.IsSupported)
        {
            InverseDct8x8Vector256(ref source, ref target, (nuint)stride);
            return;
        }

        InverseDct8x8Vector128(ref source, ref target, (nuint)stride);
    }

    private static void InverseDct8x8Vector128(ref float source, ref float target, nuint stride)
    {
        // The vertical frequencies of the horizontal frequencies 0 to 3, then 4 to 7, as lanes: the samples of each column
        Transpose(Load128(ref source, 0), Load128(ref source, 8), Load128(ref source, 16), Load128(ref source, 24), out var a0, out var a1, out var a2, out var a3);
        Transpose(Load128(ref source, 4), Load128(ref source, 12), Load128(ref source, 20), Load128(ref source, 28), out var a4, out var a5, out var a6, out var a7);
        InverseDct8(ref a0, ref a1, ref a2, ref a3, ref a4, ref a5, ref a6, ref a7);
        Transpose(Load128(ref source, 32), Load128(ref source, 40), Load128(ref source, 48), Load128(ref source, 56), out var b0, out var b1, out var b2, out var b3);
        Transpose(Load128(ref source, 36), Load128(ref source, 44), Load128(ref source, 52), Load128(ref source, 60), out var b4, out var b5, out var b6, out var b7);
        InverseDct8(ref b0, ref b1, ref b2, ref b3, ref b4, ref b5, ref b6, ref b7);

        // The horizontal frequencies of the rows 0 to 3, then 4 to 7, as lanes: the samples of each row
        Transpose(a0, a1, a2, a3, out var c0, out var c1, out var c2, out var c3);
        Transpose(b0, b1, b2, b3, out var c4, out var c5, out var c6, out var c7);
        InverseDct8(ref c0, ref c1, ref c2, ref c3, ref c4, ref c5, ref c6, ref c7);
        StoreRows(c0, c1, c2, c3, ref target, stride);
        StoreRows(c4, c5, c6, c7, ref target, stride, column: 4);
        Transpose(a4, a5, a6, a7, out var d0, out var d1, out var d2, out var d3);
        Transpose(b4, b5, b6, b7, out var d4, out var d5, out var d6, out var d7);
        InverseDct8(ref d0, ref d1, ref d2, ref d3, ref d4, ref d5, ref d6, ref d7);
        ref var lower = ref unsafe(Unsafe.Add(ref target, 4 * stride));
        StoreRows(d0, d1, d2, d3, ref lower, stride);
        StoreRows(d4, d5, d6, d7, ref lower, stride, column: 4);
    }

    /// <summary>Stores four columns of four samples, one vector per column, as four rows.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreRows(Vector128<float> column0, Vector128<float> column1, Vector128<float> column2, Vector128<float> column3, ref float target, nuint stride, nuint column = 0)
    {
        Transpose(column0, column1, column2, column3, out var row0, out var row1, out var row2, out var row3);
        unsafe
        {
            row0.StoreUnsafe(ref target, column);
            row1.StoreUnsafe(ref target, stride + column);
            row2.StoreUnsafe(ref target, (2 * stride) + column);
            row3.StoreUnsafe(ref target, (3 * stride) + column);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Load128(ref float source, nuint offset) => unsafe(Vector128.LoadUnsafe(ref source, offset));

    private static void InverseDct8x8Vector256(ref float source, ref float target, nuint stride)
    {
        Vector256<float> r0, r1, r2, r3, r4, r5, r6, r7;
        unsafe
        {
            r0 = Vector256.LoadUnsafe(ref source);
            r1 = Vector256.LoadUnsafe(ref source, 8);
            r2 = Vector256.LoadUnsafe(ref source, 16);
            r3 = Vector256.LoadUnsafe(ref source, 24);
            r4 = Vector256.LoadUnsafe(ref source, 32);
            r5 = Vector256.LoadUnsafe(ref source, 40);
            r6 = Vector256.LoadUnsafe(ref source, 48);
            r7 = Vector256.LoadUnsafe(ref source, 56);
        }

        // The vertical frequencies of each horizontal frequency as lanes, then the horizontal frequencies of each row
        Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        InverseDct8(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        InverseDct8(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        unsafe
        {
            r0.StoreUnsafe(ref target);
            r1.StoreUnsafe(ref target, stride);
            r2.StoreUnsafe(ref target, 2 * stride);
            r3.StoreUnsafe(ref target, 3 * stride);
            r4.StoreUnsafe(ref target, 4 * stride);
            r5.StoreUnsafe(ref target, 5 * stride);
            r6.StoreUnsafe(ref target, 6 * stride);
            r7.StoreUnsafe(ref target, 7 * stride);
        }
    }

    /// <summary>Transposes eight vectors of eight values in place: the lane i of the vector j becomes the lane j of the vector i.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Transpose(ref Vector256<float> r0, ref Vector256<float> r1, ref Vector256<float> r2, ref Vector256<float> r3, ref Vector256<float> r4, ref Vector256<float> r5, ref Vector256<float> r6, ref Vector256<float> r7)
    {
        // Pairs of rows interleaved, then quarters of four rows, within each half; then the halves exchanged
        var t0 = Avx.UnpackLow(r0, r1);
        var t1 = Avx.UnpackHigh(r0, r1);
        var t2 = Avx.UnpackLow(r2, r3);
        var t3 = Avx.UnpackHigh(r2, r3);
        var t4 = Avx.UnpackLow(r4, r5);
        var t5 = Avx.UnpackHigh(r4, r5);
        var t6 = Avx.UnpackLow(r6, r7);
        var t7 = Avx.UnpackHigh(r6, r7);
        var s0 = Avx.Shuffle(t0, t2, 0x44);
        var s1 = Avx.Shuffle(t0, t2, 0xEE);
        var s2 = Avx.Shuffle(t1, t3, 0x44);
        var s3 = Avx.Shuffle(t1, t3, 0xEE);
        var s4 = Avx.Shuffle(t4, t6, 0x44);
        var s5 = Avx.Shuffle(t4, t6, 0xEE);
        var s6 = Avx.Shuffle(t5, t7, 0x44);
        var s7 = Avx.Shuffle(t5, t7, 0xEE);
        r0 = Avx.Permute2x128(s0, s4, 0x20);
        r1 = Avx.Permute2x128(s1, s5, 0x20);
        r2 = Avx.Permute2x128(s2, s6, 0x20);
        r3 = Avx.Permute2x128(s3, s7, 0x20);
        r4 = Avx.Permute2x128(s0, s4, 0x31);
        r5 = Avx.Permute2x128(s1, s5, 0x31);
        r6 = Avx.Permute2x128(s2, s6, 0x31);
        r7 = Avx.Permute2x128(s3, s7, 0x31);
    }

    /// <summary>Four one-dimensional inverse DCTs of eight values, one per lane, in place: the operations of the transform of eight values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InverseDct8(ref Vector128<float> v0, ref Vector128<float> v1, ref Vector128<float> v2, ref Vector128<float> v3, ref Vector128<float> v4, ref Vector128<float> v5, ref Vector128<float> v6, ref Vector128<float> v7)
    {
        var e0 = v0;
        var e1 = v2;
        var e2 = v4;
        var e3 = v6;
        var o0 = Vector128.Create(MathF.Sqrt(2)) * v1;
        var o1 = v1 + v3;
        var o2 = v3 + v5;
        var o3 = v5 + v7;
        InverseDct4(ref e0, ref e1, ref e2, ref e3);
        InverseDct4(ref o0, ref o1, ref o2, ref o3);
        var secants = HalfSecants;
        var scaled = o0 * Vector128.Create(secants[4]);
        v0 = e0 + scaled;
        v7 = e0 - scaled;
        scaled = o1 * Vector128.Create(secants[5]);
        v1 = e1 + scaled;
        v6 = e1 - scaled;
        scaled = o2 * Vector128.Create(secants[6]);
        v2 = e2 + scaled;
        v5 = e2 - scaled;
        scaled = o3 * Vector128.Create(secants[7]);
        v3 = e3 + scaled;
        v4 = e3 - scaled;
    }

    /// <summary>Eight one-dimensional inverse DCTs of eight values, one per lane, in place: the operations of the transform of eight values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InverseDct8(ref Vector256<float> v0, ref Vector256<float> v1, ref Vector256<float> v2, ref Vector256<float> v3, ref Vector256<float> v4, ref Vector256<float> v5, ref Vector256<float> v6, ref Vector256<float> v7)
    {
        var e0 = v0;
        var e1 = v2;
        var e2 = v4;
        var e3 = v6;
        var o0 = Vector256.Create(MathF.Sqrt(2)) * v1;
        var o1 = v1 + v3;
        var o2 = v3 + v5;
        var o3 = v5 + v7;
        InverseDct4(ref e0, ref e1, ref e2, ref e3);
        InverseDct4(ref o0, ref o1, ref o2, ref o3);
        var secants = HalfSecants;
        var scaled = o0 * Vector256.Create(secants[4]);
        v0 = e0 + scaled;
        v7 = e0 - scaled;
        scaled = o1 * Vector256.Create(secants[5]);
        v1 = e1 + scaled;
        v6 = e1 - scaled;
        scaled = o2 * Vector256.Create(secants[6]);
        v2 = e2 + scaled;
        v5 = e2 - scaled;
        scaled = o3 * Vector256.Create(secants[7]);
        v3 = e3 + scaled;
        v4 = e3 - scaled;
    }

    /// <summary>Loads four lines of <paramref name="length"/> values, one after the other, as the lanes of <paramref name="destination"/>.</summary>
    private static void LoadTransposed(ReadOnlySpan<float> source, int length, Span<Vector128<float>> destination)
    {
        for (var k = 0; k < length; k += 4)
        {
            Transpose(
                Vector128.Create(source.Slice(k, 4)),
                Vector128.Create(source.Slice(length + k, 4)),
                Vector128.Create(source.Slice((2 * length) + k, 4)),
                Vector128.Create(source.Slice((3 * length) + k, 4)),
                out var r0,
                out var r1,
                out var r2,
                out var r3);
            destination[k] = r0;
            destination[k + 1] = r1;
            destination[k + 2] = r2;
            destination[k + 3] = r3;
        }
    }

    /// <summary>Stores the lanes of <paramref name="source"/> as four lines of <paramref name="length"/> values, one after the other.</summary>
    private static void StoreTransposed(ReadOnlySpan<Vector128<float>> source, Span<float> destination, int length)
    {
        for (var k = 0; k < length; k += 4)
        {
            Transpose(source[k], source[k + 1], source[k + 2], source[k + 3], out var r0, out var r1, out var r2, out var r3);
            r0.CopyTo(destination.Slice(k, 4));
            r1.CopyTo(destination.Slice(length + k, 4));
            r2.CopyTo(destination.Slice((2 * length) + k, 4));
            r3.CopyTo(destination.Slice((3 * length) + k, 4));
        }
    }

    /// <summary>Transposes four vectors of four values: the lane i of the result j is the lane j of the input i.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Transpose(Vector128<float> a, Vector128<float> b, Vector128<float> c, Vector128<float> d, out Vector128<float> r0, out Vector128<float> r1, out Vector128<float> r2, out Vector128<float> r3)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            var ac0 = AdvSimd.Arm64.ZipLow(a, c);
            var bd0 = AdvSimd.Arm64.ZipLow(b, d);
            var ac1 = AdvSimd.Arm64.ZipHigh(a, c);
            var bd1 = AdvSimd.Arm64.ZipHigh(b, d);
            r0 = AdvSimd.Arm64.ZipLow(ac0, bd0);
            r1 = AdvSimd.Arm64.ZipHigh(ac0, bd0);
            r2 = AdvSimd.Arm64.ZipLow(ac1, bd1);
            r3 = AdvSimd.Arm64.ZipHigh(ac1, bd1);
        }
        else if (Sse.IsSupported)
        {
            var ac0 = Sse.UnpackLow(a, c);
            var bd0 = Sse.UnpackLow(b, d);
            var ac1 = Sse.UnpackHigh(a, c);
            var bd1 = Sse.UnpackHigh(b, d);
            r0 = Sse.UnpackLow(ac0, bd0);
            r1 = Sse.UnpackHigh(ac0, bd0);
            r2 = Sse.UnpackLow(ac1, bd1);
            r3 = Sse.UnpackHigh(ac1, bd1);
        }
        else
        {
            r0 = Vector128.Create(a.GetElement(0), b.GetElement(0), c.GetElement(0), d.GetElement(0));
            r1 = Vector128.Create(a.GetElement(1), b.GetElement(1), c.GetElement(1), d.GetElement(1));
            r2 = Vector128.Create(a.GetElement(2), b.GetElement(2), c.GetElement(2), d.GetElement(2));
            r3 = Vector128.Create(a.GetElement(3), b.GetElement(3), c.GetElement(3), d.GetElement(3));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InverseDct4(ref Vector128<float> v0, ref Vector128<float> v1, ref Vector128<float> v2, ref Vector128<float> v3)
    {
        // Each half is a transform of two values: their sum and their difference
        var e0 = v0 + v2;
        var e1 = v0 - v2;
        var o = Vector128.Create(MathF.Sqrt(2)) * v1;
        var o1 = v1 + v3;
        var o0 = o + o1;
        o1 = o - o1;
        var scaled = o0 * Vector128.Create(HalfSecants[2]);
        v0 = e0 + scaled;
        v3 = e0 - scaled;
        scaled = o1 * Vector128.Create(HalfSecants[3]);
        v1 = e1 + scaled;
        v2 = e1 - scaled;
    }

    /// <summary>Eight one-dimensional inverse DCTs of four values, one per lane: the operations of the transform of four lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InverseDct4(ref Vector256<float> v0, ref Vector256<float> v1, ref Vector256<float> v2, ref Vector256<float> v3)
    {
        var e0 = v0 + v2;
        var e1 = v0 - v2;
        var o = Vector256.Create(MathF.Sqrt(2)) * v1;
        var o1 = v1 + v3;
        var o0 = o + o1;
        o1 = o - o1;
        var scaled = o0 * Vector256.Create(HalfSecants[2]);
        v0 = e0 + scaled;
        v3 = e0 - scaled;
        scaled = o1 * Vector256.Create(HalfSecants[3]);
        v1 = e1 + scaled;
        v2 = e1 - scaled;
    }

    /// <summary>
    /// Gets a value indicating whether the inverse DCT of 8x8 samples of a block whose only non-zero coefficient is its
    /// lowest frequency gives that value to every sample, bit for bit (<see cref="FillConstantBlock"/>): every operation
    /// adds or subtracts a zero to it, or multiplies a zero. It does unless the value is a negative zero (adding a
    /// positive zero gives a positive one) or not a number.
    /// </summary>
    public static bool IsConstantBlockExact(float lowestFrequency) => float.IsFinite(lowestFrequency) && BitConverter.SingleToInt32Bits(lowestFrequency) != int.MinValue;

    /// <summary>Sets the 8x8 samples of a block to its lowest frequency, the samples of a DCT without other coefficient.</summary>
    /// <param name="lowestFrequency">The lowest frequency of the block, for which <see cref="IsConstantBlockExact"/> holds.</param>
    /// <param name="output">Receives the samples.</param>
    /// <param name="stride">The distance between two rows of <paramref name="output"/>.</param>
    public static void FillConstantBlock(float lowestFrequency, Span<float> output, int stride)
    {
        for (var y = 0; y < 8; y++)
        {
            output.Slice(y * stride, 8).Fill(lowestFrequency);
        }
    }

    /// <summary>
    /// Computes the lowest frequencies of a transform of several blocks from the LF samples of its blocks, one per block
    /// of 8x8 samples: the DCT of these samples, each coefficient scaled to the coefficient of the same frequency of the
    /// DCT of all the samples.
    /// </summary>
    /// <param name="lowFrequency">The LF samples, starting at the first block of the transform.</param>
    /// <param name="lowFrequencyStride">The distance between two rows of <paramref name="lowFrequency"/>.</param>
    /// <param name="blocksHigh">The height of the transform, in blocks.</param>
    /// <param name="blocksWide">The width of the transform, in blocks.</param>
    /// <param name="coefficients">The coefficients of the transform, in the layout of the format; the lowest frequencies are written.</param>
    /// <param name="scratch">Scratch memory of <see cref="ScratchLength"/> values.</param>
    public static void SetLowestFrequencies(ReadOnlySpan<float> lowFrequency, int lowFrequencyStride, int blocksHigh, int blocksWide, Span<float> coefficients, Span<float> scratch)
    {
        var stride = Math.Max(blocksHigh, blocksWide) * 8;
        if (blocksHigh == 1 && blocksWide == 1)
        {
            coefficients[0] = lowFrequency[0];
            return;
        }

        // The DCT of each row, then of each column
        var rowBasis = ForwardBasis[blocksWide];
        var columnBasis = ForwardBasis[blocksHigh];
        var rowScales = LowFrequencyScales.AsSpan(blocksWide, blocksWide);
        var columnScales = LowFrequencyScales.AsSpan(blocksHigh, blocksHigh);
        var horizontal = scratch[..(blocksHigh * blocksWide)];
        for (var y = 0; y < blocksHigh; y++)
        {
            var source = lowFrequency.Slice(y * lowFrequencyStride, blocksWide);
            for (var u = 0; u < blocksWide; u++)
            {
                var basis = rowBasis.AsSpan(u * blocksWide, blocksWide);
                var sum = 0f;
                for (var x = 0; x < blocksWide; x++)
                {
                    sum += source[x] * basis[x];
                }

                horizontal[(y * blocksWide) + u] = sum;
            }
        }

        var isTransposed = blocksHigh >= blocksWide;
        for (var v = 0; v < blocksHigh; v++)
        {
            var basis = columnBasis.AsSpan(v * blocksHigh, blocksHigh);
            for (var u = 0; u < blocksWide; u++)
            {
                var sum = 0f;
                for (var y = 0; y < blocksHigh; y++)
                {
                    sum += horizontal[(y * blocksWide) + u] * basis[y];
                }

                var value = sum * columnScales[v] * rowScales[u];
                coefficients[isTransposed ? (u * stride) + v : (v * stride) + u] = value;
            }
        }
    }

    /// <summary>
    /// The transform that stores samples: the block is four blocks of 4x4 samples; each stores the differences of its
    /// samples with the sample of its second row and second column, which is its mean minus the mean of the differences.
    /// </summary>
    private static void ApplyIdentity(ReadOnlySpan<float> coefficients, Span<float> output, int stride)
    {
        Span<float> means = stackalloc float[4];
        SplitLowestFrequency(coefficients, means);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                var sum = 0f;
                for (var iy = 0; iy < 4; iy++)
                {
                    for (var ix = 0; ix < 4; ix++)
                    {
                        if (ix != 0 || iy != 0)
                        {
                            sum += coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)];
                        }
                    }
                }

                var reference = means[(y * 2) + x] - (sum * (1f / 16));
                for (var iy = 0; iy < 4; iy++)
                {
                    for (var ix = 0; ix < 4; ix++)
                    {
                        output[(((y * 4) + iy) * stride) + (x * 4) + ix] = coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)] + reference;
                    }
                }

                // The first position holds the mean: the difference of the first sample is stored where the reference would be
                output[(((y * 4) + 1) * stride) + (x * 4) + 1] = reference;
                output[(y * 4 * stride) + (x * 4)] = coefficients[((y + 2) * 8) + x + 2] + reference;
            }
        }
    }

    /// <summary>Three nested transforms of 2x2 values: of the first 2x2 coefficients, then of the first 4x4, then of all of them.</summary>
    private static void ApplyDct2(Span<float> coefficients, Span<float> output, int stride)
    {
        Span<float> work = stackalloc float[64];
        for (var size = 2; size <= 8; size *= 2)
        {
            var half = size / 2;
            for (var y = 0; y < half; y++)
            {
                for (var x = 0; x < half; x++)
                {
                    var c00 = coefficients[(y * 8) + x];
                    var c01 = coefficients[(y * 8) + half + x];
                    var c10 = coefficients[((y + half) * 8) + x];
                    var c11 = coefficients[((y + half) * 8) + half + x];
                    work[(y * 2 * 8) + (x * 2)] = c00 + c01 + c10 + c11;
                    work[(y * 2 * 8) + (x * 2) + 1] = c00 + c01 - c10 - c11;
                    work[(((y * 2) + 1) * 8) + (x * 2)] = c00 - c01 + c10 - c11;
                    work[(((y * 2) + 1) * 8) + (x * 2) + 1] = c00 - c01 - c10 + c11;
                }
            }

            for (var y = 0; y < size; y++)
            {
                work.Slice(y * 8, size).CopyTo(coefficients.Slice(y * 8, size));
            }
        }

        for (var y = 0; y < 8; y++)
        {
            coefficients.Slice(y * 8, 8).CopyTo(output.Slice(y * stride, 8));
        }
    }

    /// <summary>Four DCTs of 4x4 samples, whose coefficients are interleaved; their lowest frequencies come from the first 2x2 coefficients.</summary>
    private static void ApplyDct4(ReadOnlySpan<float> coefficients, Span<float> output, int stride, Span<float> scratch)
    {
        Span<float> means = stackalloc float[4];
        Span<float> block = stackalloc float[16];
        SplitLowestFrequency(coefficients, means);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                for (var iy = 0; iy < 4; iy++)
                {
                    for (var ix = 0; ix < 4; ix++)
                    {
                        block[(iy * 4) + ix] = coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)];
                    }
                }

                block[0] = means[(y * 2) + x];
                InverseDct(block, 4, 4, output[((y * 4 * stride) + (x * 4))..], stride, scratch);
            }
        }
    }

    /// <summary>Two DCTs of 4x8 samples, one above the other, or of 8x4 samples, side by side; their coefficients are in alternate rows.</summary>
    private static void ApplyHalves(ReadOnlySpan<float> coefficients, Span<float> output, int stride, Span<float> scratch, bool isWide)
    {
        Span<float> block = stackalloc float[32];
        var first = coefficients[0];
        var second = coefficients[8];
        for (var half = 0; half < 2; half++)
        {
            for (var iy = 0; iy < 4; iy++)
            {
                coefficients.Slice((half + (iy * 2)) * 8, 8).CopyTo(block.Slice(iy * 8, 8));
            }

            block[0] = half == 0 ? first + second : first - second;
            if (isWide)
            {
                InverseDct(block, 4, 8, output[(half * 4 * stride)..], stride, scratch);
            }
            else
            {
                InverseDct(block, 8, 4, output[(half * 4)..], stride, scratch);
            }
        }
    }

    /// <summary>
    /// The AFV transform: a corner of 4x4 samples has its own basis, the 4x4 samples beside it a DCT, and the other half
    /// of the block a DCT of 4x8 samples. The kind selects the corner.
    /// </summary>
    private static void ApplyAfv(int kind, ReadOnlySpan<float> coefficients, Span<float> output, int stride, Span<float> scratch)
    {
        var isRight = (kind & 1) != 0;
        var isBottom = (kind & 2) != 0;
        var c00 = coefficients[0];
        var c01 = coefficients[1];
        var c10 = coefficients[8];
        Span<float> block = stackalloc float[32];
        Span<float> samples = stackalloc float[16];

        // The corner: the coefficients at even rows and even columns
        var corner = block[..16];
        for (var iy = 0; iy < 4; iy++)
        {
            for (var ix = 0; ix < 4; ix++)
            {
                corner[(iy * 4) + ix] = coefficients[(iy * 2 * 8) + (ix * 2)];
            }
        }

        corner[0] = (c00 + c10 + c01) * 4;
        samples.Clear();
        var basis = JpegXlVarDctTables.AfvBasis;
        for (var j = 0; j < 16; j++)
        {
            var coefficient = corner[j];
            var row = basis.Slice(j * 16, 16);
            for (var i = 0; i < 16; i++)
            {
                samples[i] += coefficient * row[i];
            }
        }

        var cornerX = isRight ? 4 : 0;
        var cornerY = isBottom ? 4 : 0;
        for (var iy = 0; iy < 4; iy++)
        {
            for (var ix = 0; ix < 4; ix++)
            {
                output[((cornerY + iy) * stride) + cornerX + ix] = samples[((isBottom ? 3 - iy : iy) * 4) + (isRight ? 3 - ix : ix)];
            }
        }

        // The 4x4 samples beside the corner: the coefficients at even rows and odd columns
        for (var iy = 0; iy < 4; iy++)
        {
            for (var ix = 0; ix < 4; ix++)
            {
                corner[(iy * 4) + ix] = coefficients[(iy * 2 * 8) + (ix * 2) + 1];
            }
        }

        corner[0] = c00 + c10 - c01;
        InverseDct(corner, 4, 4, output[((cornerY * stride) + (isRight ? 0 : 4))..], stride, scratch);

        // The other half of the block: the coefficients of the odd rows
        for (var iy = 0; iy < 4; iy++)
        {
            coefficients.Slice((1 + (iy * 2)) * 8, 8).CopyTo(block.Slice(iy * 8, 8));
        }

        block[0] = c00 - c10;
        InverseDct(block, 4, 8, output[((isBottom ? 0 : 4) * stride)..], stride, scratch);
    }

    /// <summary>Gets the lowest frequency of each quarter of a block from its first 2x2 coefficients.</summary>
    private static void SplitLowestFrequency(ReadOnlySpan<float> coefficients, Span<float> means)
    {
        var c00 = coefficients[0];
        var c01 = coefficients[1];
        var c10 = coefficients[8];
        var c11 = coefficients[9];
        means[0] = c00 + c01 + c10 + c11;
        means[1] = c00 + c01 - c10 - c11;
        means[2] = c00 - c01 + c10 - c11;
        means[3] = c00 - c01 - c10 + c11;
    }

    private static float[] CreateHalfSecants()
    {
        var result = new float[MaxSize];
        for (var half = 1; half < MaxSize; half *= 2)
        {
            for (var i = 0; i < half; i++)
            {
                result[half + i] = (float)(1 / (2 * Math.Cos((i + 0.5) * Math.PI / (2 * half))));
            }
        }

        return result;
    }

    private static float[] CreateLowFrequencyScales()
    {
        var result = new float[64];
        for (var count = 1; count <= 32; count *= 2)
        {
            for (var k = 0; k < count; k++)
            {
                result[count + k] = (float)(1 / (Math.Cos(k * Math.PI / (16 * count)) * Math.Cos(k * Math.PI / (8 * count)) * Math.Cos(k * Math.PI / (4 * count))));
            }
        }

        return result;
    }

    private static float[][] CreateForwardBasis()
    {
        var result = new float[33][];
        for (var count = 1; count <= 32; count *= 2)
        {
            var basis = result[count] = new float[count * count];
            for (var k = 0; k < count; k++)
            {
                for (var n = 0; n < count; n++)
                {
                    basis[(k * count) + n] = (float)(Math.Cos(Math.PI * k * (n + 0.5) / count) * (k == 0 ? 1 : Math.Sqrt(2)) / count);
                }
            }
        }

        return result;
    }
}
