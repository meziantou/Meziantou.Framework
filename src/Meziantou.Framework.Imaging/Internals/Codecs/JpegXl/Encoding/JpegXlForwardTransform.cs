using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the VarDCT mode: from the samples of a block to the coefficients that
/// <see cref="JpegXlInverseTransform"/> turns back into these samples, in the layout of the format.
/// </summary>
/// <remarks>
/// <para>
/// The one-dimensional DCT undoes the steps of the inverse transform of the decoder, by halving, in the opposite order: the
/// samples are folded (the sums of the samples at the same distance from both ends, and their differences multiplied by
/// twice the cosine of the sample position), the sums and the differences are each transformed at half the length, the
/// first gives the even coefficients, and the odd ones are taken back from the sums of two consecutive odd coefficients
/// that the second gives. Each step is the exact inverse of a step of the decoder, so that the two directions cannot
/// disagree beyond rounding. The transforms of the lines are left multiplied by their length; the coefficients are scaled
/// once, by a power of two, at the end.
/// </para>
/// <para>
/// Every operation is a float32 one in a fixed order. The format does not define a forward transform: these coefficients
/// are only required to be close to the ones that give the samples back.
/// </para>
/// </remarks>
internal static class JpegXlForwardTransform
{
    /// <summary>The largest side of a transform the encoder uses, in samples.</summary>
    public const int MaxSize = 64;

    private const float InverseSquareRoot2 = 0.70710677f;

    // For a transform of N samples, at N / 2 + i: 2 * cos((i + 1/2) * pi / N), which multiplies the differences of the samples
    private static readonly float[] DoubleCosines = CreateDoubleCosines();

    // For a transform of H by W blocks, at log2(H) * 4 + log2(W): the matrix that gives the LF samples of its blocks from
    // its lowest frequencies, in rows of H * W values
    private static readonly float[]?[] LowFrequencyMatrices = new float[]?[16];

    /// <summary>The DCT of a block of <paramref name="rows"/> by <paramref name="columns"/> samples.</summary>
    /// <param name="samples">The samples, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows of <paramref name="samples"/>.</param>
    /// <param name="rows">The number of rows of samples: a power of two, at most <see cref="MaxSize"/>.</param>
    /// <param name="columns">The number of columns of samples: a power of two, at most <see cref="MaxSize"/>.</param>
    /// <param name="coefficients">Receives the coefficients, in the layout of the format: the smaller side as rows, transposed when the block is not wider than it is high.</param>
    /// <param name="scratch">Scratch memory of <paramref name="rows"/> times <paramref name="columns"/> values.</param>
    public static void ForwardDct(ReadOnlySpan<float> samples, int stride, int rows, int columns, Span<float> coefficients, Span<float> scratch)
    {
        // Four lines at once, one per lane, each with the operations of the scalar code in the same order
        if (Vector128.IsHardwareAccelerated && rows >= 4 && columns >= 4)
        {
            if (rows == 8 && columns == 8)
            {
                ForwardDct8x8(samples, stride, coefficients);
                return;
            }

            ForwardDctVector(samples, stride, rows, columns, coefficients, scratch);
            return;
        }

        ForwardDctScalar(samples, stride, rows, columns, coefficients, scratch);
    }

    /// <summary>
    /// The DCT of a block whose sides are multiples of 4: the rows of samples are transformed four at a time (transposed
    /// 4x4 values at a time on the way in and out), then the columns of horizontal frequencies four at a time.
    /// </summary>
    /// <remarks>Every element of the stack buffers is written before it is read.</remarks>
    [System.Runtime.CompilerServices.SkipLocalsInit]
    private static void ForwardDctVector(ReadOnlySpan<float> samples, int stride, int rows, int columns, Span<float> coefficients, Span<float> scratch)
    {
        Span<Vector128<float>> lines = unsafe(stackalloc Vector128<float>[MaxSize]);
        Span<Vector128<float>> work = unsafe(stackalloc Vector128<float>[MaxSize]);

        // The horizontal frequencies of each row of samples, for four rows at a time
        var horizontal = scratch[..(rows * columns)];
        var line = lines[..columns];
        for (var y = 0; y < rows; y += 4)
        {
            for (var x = 0; x < columns; x += 4)
            {
                JpegXlInverseTransform.Transpose(
                    Vector128.Create(samples.Slice((y * stride) + x, 4)),
                    Vector128.Create(samples.Slice(((y + 1) * stride) + x, 4)),
                    Vector128.Create(samples.Slice(((y + 2) * stride) + x, 4)),
                    Vector128.Create(samples.Slice(((y + 3) * stride) + x, 4)),
                    out var r0,
                    out var r1,
                    out var r2,
                    out var r3);
                line[x] = r0;
                line[x + 1] = r1;
                line[x + 2] = r2;
                line[x + 3] = r3;
            }

            ForwardDct(line, work);
            for (var u = 0; u < columns; u += 4)
            {
                JpegXlInverseTransform.Transpose(line[u], line[u + 1], line[u + 2], line[u + 3], out var r0, out var r1, out var r2, out var r3);
                r0.CopyTo(horizontal.Slice((y * columns) + u, 4));
                r1.CopyTo(horizontal.Slice(((y + 1) * columns) + u, 4));
                r2.CopyTo(horizontal.Slice(((y + 2) * columns) + u, 4));
                r3.CopyTo(horizontal.Slice(((y + 3) * columns) + u, 4));
            }
        }

        // Then the vertical frequencies of each column of horizontal frequencies, for four columns at a time, scaled
        var isWide = rows < columns;
        var scale = Vector128.Create(1f / (rows * columns));
        var column = lines[..rows];
        for (var u = 0; u < columns; u += 4)
        {
            for (var y = 0; y < rows; y++)
            {
                column[y] = Vector128.Create(horizontal.Slice((y * columns) + u, 4));
            }

            ForwardDct(column, work);
            for (var v = 0; v < rows; v += 4)
            {
                var s0 = column[v] * scale;
                var s1 = column[v + 1] * scale;
                var s2 = column[v + 2] * scale;
                var s3 = column[v + 3] * scale;
                if (isWide)
                {
                    s0.CopyTo(coefficients.Slice((v * columns) + u, 4));
                    s1.CopyTo(coefficients.Slice(((v + 1) * columns) + u, 4));
                    s2.CopyTo(coefficients.Slice(((v + 2) * columns) + u, 4));
                    s3.CopyTo(coefficients.Slice(((v + 3) * columns) + u, 4));
                }
                else
                {
                    JpegXlInverseTransform.Transpose(s0, s1, s2, s3, out var r0, out var r1, out var r2, out var r3);
                    r0.CopyTo(coefficients.Slice((u * rows) + v, 4));
                    r1.CopyTo(coefficients.Slice(((u + 1) * rows) + v, 4));
                    r2.CopyTo(coefficients.Slice(((u + 2) * rows) + v, 4));
                    r3.CopyTo(coefficients.Slice(((u + 3) * rows) + v, 4));
                }
            }
        }
    }

    /// <summary>The scalar reference of <see cref="ForwardDct(ReadOnlySpan{float}, int, int, int, Span{float}, Span{float})"/>.</summary>
    [System.Runtime.CompilerServices.SkipLocalsInit]
    public static void ForwardDctScalar(ReadOnlySpan<float> samples, int stride, int rows, int columns, Span<float> coefficients, Span<float> scratch)
    {
        Span<float> line = unsafe(stackalloc float[MaxSize]);
        Span<float> work = unsafe(stackalloc float[MaxSize]);

        // The horizontal frequencies of each row of samples
        var horizontal = scratch[..(rows * columns)];
        for (var y = 0; y < rows; y++)
        {
            var target = horizontal.Slice(y * columns, columns);
            samples.Slice(y * stride, columns).CopyTo(target);
            ForwardDct(target, work);
        }

        // Then the vertical frequencies of each column of horizontal frequencies, scaled
        var isWide = rows < columns;
        var scale = 1f / (rows * columns);
        var column = line[..rows];
        for (var u = 0; u < columns; u++)
        {
            for (var y = 0; y < rows; y++)
            {
                column[y] = horizontal[(y * columns) + u];
            }

            ForwardDct(column, work);
            for (var v = 0; v < rows; v++)
            {
                coefficients[isWide ? (v * columns) + u : (u * rows) + v] = column[v] * scale;
            }
        }
    }

    /// <summary>
    /// The one-dimensional DCT of a power-of-two number of values, in place, multiplied by their number: the transform that
    /// <see cref="JpegXlInverseTransform.InverseDct(Span{float}, Span{float})"/> undoes, once divided by the length.
    /// </summary>
    /// <param name="values">The samples, replaced with the coefficients.</param>
    /// <param name="scratch">Scratch memory of at least the same length.</param>
    internal static void ForwardDct(Span<float> values, Span<float> scratch)
    {
        var count = values.Length;
        if (count == 1)
            return;

        if (count == 2)
        {
            var first = values[0];
            var second = values[1];
            values[0] = first + second;
            values[1] = first - second;
            return;
        }

        // The sums give the even coefficients, the differences the sums of two consecutive odd coefficients
        var half = count / 2;
        var even = scratch[..half];
        var odd = scratch.Slice(half, half);
        var cosines = DoubleCosines.AsSpan(half, half);
        for (var i = 0; i < half; i++)
        {
            var first = values[i];
            var last = values[count - 1 - i];
            even[i] = first + last;
            odd[i] = (first - last) * cosines[i];
        }

        ForwardDct(even, values[..half]);
        ForwardDct(odd, values.Slice(half, half));
        for (var i = 0; i < half; i++)
        {
            values[2 * i] = even[i];
        }

        values[1] = odd[0] * InverseSquareRoot2;
        for (var i = 1; i < half; i++)
        {
            values[(2 * i) + 1] = odd[i] - values[(2 * i) - 1];
        }
    }

    /// <summary>
    /// Four one-dimensional DCTs of a power-of-two number of values (4 or more), one per lane, in place: the operations of
    /// <see cref="ForwardDct(Span{float}, Span{float})"/>, in the same order.
    /// </summary>
    private static void ForwardDct(Span<Vector128<float>> values, Span<Vector128<float>> scratch)
    {
        var count = values.Length;
        switch (count)
        {
            case 4:
                ForwardDct4(ref values[0], ref values[1], ref values[2], ref values[3]);
                return;

            case 8:
                ForwardDct8(values);
                return;
        }

        var half = count / 2;
        var even = scratch[..half];
        var odd = scratch.Slice(half, half);
        var cosines = DoubleCosines.AsSpan(half, half);
        for (var i = 0; i < half; i++)
        {
            var first = values[i];
            var last = values[count - 1 - i];
            even[i] = first + last;
            odd[i] = (first - last) * Vector128.Create(cosines[i]);
        }

        ForwardDct(even, values[..half]);
        ForwardDct(odd, values.Slice(half, half));
        for (var i = 0; i < half; i++)
        {
            values[2 * i] = even[i];
        }

        values[1] = odd[0] * Vector128.Create(InverseSquareRoot2);
        for (var i = 1; i < half; i++)
        {
            values[(2 * i) + 1] = odd[i] - values[(2 * i) - 1];
        }
    }

    private static void ForwardDct8(Span<Vector128<float>> values)
    {
        values = values[..8];
        ForwardDct8(ref values[0], ref values[1], ref values[2], ref values[3], ref values[4], ref values[5], ref values[6], ref values[7]);
    }

    /// <summary>
    /// The DCT of a block of 8x8 samples, every line in registers: the rows four at a time (the samples of a column as
    /// lanes), then the columns four at a time (the horizontal frequencies of a row as lanes), with the operations of the
    /// scalar code in the same order.
    /// </summary>
    private static void ForwardDct8x8(ReadOnlySpan<float> samples, int stride, Span<float> coefficients)
    {
        // Every position read or written is checked here, once for the block
        if (stride < 8 || samples.Length < (7 * stride) + 8 || coefficients.Length < 64)
            throw new ArgumentOutOfRangeException(nameof(samples));

        ref var source = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var target = ref unsafe(MemoryMarshal.GetReference(coefficients));
        var step = (nuint)stride;

        // The horizontal frequencies of the rows 0 to 3 (a*) and 4 to 7 (b*), one row per lane
        JpegXlInverseTransform.Transpose(Load(ref source, 0), Load(ref source, step), Load(ref source, 2 * step), Load(ref source, 3 * step), out var a0, out var a1, out var a2, out var a3);
        JpegXlInverseTransform.Transpose(Load(ref source, 4), Load(ref source, step + 4), Load(ref source, (2 * step) + 4), Load(ref source, (3 * step) + 4), out var a4, out var a5, out var a6, out var a7);
        ForwardDct8(ref a0, ref a1, ref a2, ref a3, ref a4, ref a5, ref a6, ref a7);
        JpegXlInverseTransform.Transpose(Load(ref source, 4 * step), Load(ref source, 5 * step), Load(ref source, 6 * step), Load(ref source, 7 * step), out var b0, out var b1, out var b2, out var b3);
        JpegXlInverseTransform.Transpose(Load(ref source, (4 * step) + 4), Load(ref source, (5 * step) + 4), Load(ref source, (6 * step) + 4), Load(ref source, (7 * step) + 4), out var b4, out var b5, out var b6, out var b7);
        ForwardDct8(ref b0, ref b1, ref b2, ref b3, ref b4, ref b5, ref b6, ref b7);

        // The vertical frequencies of the horizontal frequencies 0 to 3, then 4 to 7, one horizontal frequency per lane;
        // the block is square, so the coefficients of a horizontal frequency are a row of the layout of the format
        JpegXlInverseTransform.Transpose(a0, a1, a2, a3, out var c0, out var c1, out var c2, out var c3);
        JpegXlInverseTransform.Transpose(b0, b1, b2, b3, out var c4, out var c5, out var c6, out var c7);
        ForwardDct8(ref c0, ref c1, ref c2, ref c3, ref c4, ref c5, ref c6, ref c7);
        Store(c0, c1, c2, c3, c4, c5, c6, c7, ref target, 0);
        JpegXlInverseTransform.Transpose(a4, a5, a6, a7, out c0, out c1, out c2, out c3);
        JpegXlInverseTransform.Transpose(b4, b5, b6, b7, out c4, out c5, out c6, out c7);
        ForwardDct8(ref c0, ref c1, ref c2, ref c3, ref c4, ref c5, ref c6, ref c7);
        Store(c0, c1, c2, c3, c4, c5, c6, c7, ref target, 32);

        static Vector128<float> Load(ref float source, nuint offset) => unsafe(Vector128.LoadUnsafe(ref source, offset));

        // Scales the vertical frequencies 0 to 7 of four horizontal frequencies, and writes them as four rows of 8 values
        static void Store(Vector128<float> v0, Vector128<float> v1, Vector128<float> v2, Vector128<float> v3, Vector128<float> v4, Vector128<float> v5, Vector128<float> v6, Vector128<float> v7, ref float target, nuint offset)
        {
            var scale = Vector128.Create(1f / 64);
            JpegXlInverseTransform.Transpose(v0 * scale, v1 * scale, v2 * scale, v3 * scale, out var r0, out var r1, out var r2, out var r3);
            JpegXlInverseTransform.Transpose(v4 * scale, v5 * scale, v6 * scale, v7 * scale, out var r4, out var r5, out var r6, out var r7);
            unsafe
            {
                r0.StoreUnsafe(ref target, offset);
                r4.StoreUnsafe(ref target, offset + 4);
                r1.StoreUnsafe(ref target, offset + 8);
                r5.StoreUnsafe(ref target, offset + 12);
                r2.StoreUnsafe(ref target, offset + 16);
                r6.StoreUnsafe(ref target, offset + 20);
                r3.StoreUnsafe(ref target, offset + 24);
                r7.StoreUnsafe(ref target, offset + 28);
            }
        }
    }

    /// <summary>Four one-dimensional DCTs of eight values, one per lane, in place: the operations of the scalar transform of eight values.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void ForwardDct8(ref Vector128<float> v0, ref Vector128<float> v1, ref Vector128<float> v2, ref Vector128<float> v3, ref Vector128<float> v4, ref Vector128<float> v5, ref Vector128<float> v6, ref Vector128<float> v7)
    {
        var cosines = DoubleCosines;
        var e0 = v0 + v7;
        var o0 = (v0 - v7) * Vector128.Create(cosines[4]);
        var e1 = v1 + v6;
        var o1 = (v1 - v6) * Vector128.Create(cosines[5]);
        var e2 = v2 + v5;
        var o2 = (v2 - v5) * Vector128.Create(cosines[6]);
        var e3 = v3 + v4;
        var o3 = (v3 - v4) * Vector128.Create(cosines[7]);
        ForwardDct4(ref e0, ref e1, ref e2, ref e3);
        ForwardDct4(ref o0, ref o1, ref o2, ref o3);
        v0 = e0;
        v2 = e1;
        v4 = e2;
        v6 = e3;
        var odd = o0 * Vector128.Create(InverseSquareRoot2);
        v1 = odd;
        odd = o1 - odd;
        v3 = odd;
        odd = o2 - odd;
        v5 = odd;
        v7 = o3 - odd;
    }

    /// <summary>Four one-dimensional DCTs of four values, one per lane: the operations of the scalar transform of four values.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void ForwardDct4(ref Vector128<float> v0, ref Vector128<float> v1, ref Vector128<float> v2, ref Vector128<float> v3)
    {
        // Each half is a transform of two values: their sum and their difference
        var e0 = v0 + v3;
        var o0 = (v0 - v3) * Vector128.Create(DoubleCosines[2]);
        var e1 = v1 + v2;
        var o1 = (v1 - v2) * Vector128.Create(DoubleCosines[3]);
        v0 = e0 + e1;
        v2 = e0 - e1;
        var odd = (o0 + o1) * Vector128.Create(InverseSquareRoot2);
        v1 = odd;
        v3 = (o0 - o1) - odd;
    }

    /// <summary>
    /// Computes the LF samples of the blocks of a transform from its lowest frequencies: the samples from which
    /// <see cref="JpegXlInverseTransform.SetLowestFrequencies"/> computes these coefficients again.
    /// </summary>
    /// <param name="coefficients">The coefficients of the transform, in the layout of the format.</param>
    /// <param name="blocksHigh">The height of the transform, in blocks.</param>
    /// <param name="blocksWide">The width of the transform, in blocks.</param>
    /// <param name="lowFrequency">Receives the LF sample of each block, row by row.</param>
    public static void GetLowFrequencies(ReadOnlySpan<float> coefficients, int blocksHigh, int blocksWide, Span<float> lowFrequency)
    {
        if (blocksHigh == 1 && blocksWide == 1)
        {
            lowFrequency[0] = coefficients[0];
            return;
        }

        var count = blocksHigh * blocksWide;
        var index = (System.Numerics.BitOperations.Log2((uint)blocksHigh) * 4) + System.Numerics.BitOperations.Log2((uint)blocksWide);
        var matrix = LowFrequencyMatrices[index] ?? CreateLowFrequencyMatrix(blocksHigh, blocksWide, index);

        // The lowest frequencies, in the order of the coefficients that SetLowestFrequencies writes
        Span<float> lowest = stackalloc float[count];
        var stride = Math.Max(blocksHigh, blocksWide) * 8;
        var isTransposed = blocksHigh >= blocksWide;
        for (var v = 0; v < blocksHigh; v++)
        {
            for (var u = 0; u < blocksWide; u++)
            {
                lowest[(v * blocksWide) + u] = coefficients[isTransposed ? (u * stride) + v : (v * stride) + u];
            }
        }

        for (var i = 0; i < count; i++)
        {
            var row = matrix.AsSpan(i * count, count);
            var sum = 0f;
            for (var j = 0; j < count; j++)
            {
                sum += row[j] * lowest[j];
            }

            lowFrequency[i] = sum;
        }
    }

    private static float[] CreateLowFrequencyMatrix(int blocksHigh, int blocksWide, int index)
    {
        // The coefficients that each LF sample alone gives are a column of the matrix of the decoder, which is inverted
        var count = blocksHigh * blocksWide;
        var stride = Math.Max(blocksHigh, blocksWide) * 8;
        var isTransposed = blocksHigh >= blocksWide;
        var forward = new double[count * count];
        var samples = new float[count];
        var coefficients = new float[blocksHigh * blocksWide * 64];
        var scratch = new float[JpegXlInverseTransform.ScratchLength];
        for (var sample = 0; sample < count; sample++)
        {
            Array.Clear(samples);
            samples[sample] = 1;
            JpegXlInverseTransform.SetLowestFrequencies(samples, blocksWide, blocksHigh, blocksWide, coefficients, scratch);
            for (var v = 0; v < blocksHigh; v++)
            {
                for (var u = 0; u < blocksWide; u++)
                {
                    forward[(((v * blocksWide) + u) * count) + sample] = coefficients[isTransposed ? (u * stride) + v : (v * stride) + u];
                }
            }
        }

        // Gauss-Jordan elimination with partial pivoting: the matrix is a scaled DCT, far from singular
        var inverse = new double[count * count];
        for (var i = 0; i < count; i++)
        {
            inverse[(i * count) + i] = 1;
        }

        for (var column = 0; column < count; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < count; row++)
            {
                if (Math.Abs(forward[(row * count) + column]) > Math.Abs(forward[(pivot * count) + column]))
                {
                    pivot = row;
                }
            }

            if (pivot != column)
            {
                for (var j = 0; j < count; j++)
                {
                    (forward[(column * count) + j], forward[(pivot * count) + j]) = (forward[(pivot * count) + j], forward[(column * count) + j]);
                    (inverse[(column * count) + j], inverse[(pivot * count) + j]) = (inverse[(pivot * count) + j], inverse[(column * count) + j]);
                }
            }

            var scale = 1 / forward[(column * count) + column];
            for (var j = 0; j < count; j++)
            {
                forward[(column * count) + j] *= scale;
                inverse[(column * count) + j] *= scale;
            }

            for (var row = 0; row < count; row++)
            {
                if (row == column)
                    continue;

                var factor = forward[(row * count) + column];
                if (factor == 0)
                    continue;

                for (var j = 0; j < count; j++)
                {
                    forward[(row * count) + j] -= factor * forward[(column * count) + j];
                    inverse[(row * count) + j] -= factor * inverse[(column * count) + j];
                }
            }
        }

        var matrix = new float[count * count];
        for (var i = 0; i < matrix.Length; i++)
        {
            matrix[i] = (float)inverse[i];
        }

        // Two threads may compute the same table: either one is kept
        LowFrequencyMatrices[index] = matrix;
        return matrix;
    }

    private static float[] CreateDoubleCosines()
    {
        var result = new float[MaxSize];
        for (var half = 1; half < MaxSize; half *= 2)
        {
            for (var i = 0; i < half; i++)
            {
                result[half + i] = (float)(2 * Math.Cos((i + 0.5) * Math.PI / (2 * half)));
            }
        }

        return result;
    }
}
