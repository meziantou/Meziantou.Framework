using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The blurs of <see cref="ButteraugliCalculator"/> (<c>Blur</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c> of libjxl
/// 0.12.0): a Gaussian kernel truncated at 2.25 sigma, renormalized near the borders, or, for the 5-tap kernel, a separable
/// convolution that mirrors the plane at its borders. Large planes are split into row bands between up to
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers; every value is computed as by one worker.
/// </summary>
/// <remarks>
/// libjxl convolves the rows and writes them transposed, twice (<see cref="BlurScalar"/>). <see cref="Blur"/> convolves the
/// rows, then the columns, without transposing: every value is the same sum of the same products, in the same order, and
/// the vectors compute consecutive columns.
/// </remarks>
internal static class ButteraugliBlur
{
    private static readonly Dictionary<float, float[]> Kernels = [];
    private static readonly Lock KernelsLock = new();

    /// <summary>Blurs <paramref name="input"/> into <paramref name="output"/>, which can be the same plane.</summary>
    /// <exception cref="ImageResourceLimitException">The temporary plane exceeds the allocation limit.</exception>
    public static void Blur(FloatPlaneArena arena, FloatPlane input, float sigma, FloatPlane output, int maxDegreeOfParallelism)
    {
        var kernel = GetKernel(sigma);
        if (kernel.Length is 5 && input != output)
        {
            Separable5(arena, input, kernel, output, maxDegreeOfParallelism);
            return;
        }

        var width = input.Width;
        var height = input.Height;
        var scaledKernel = GetScaledKernel(kernel);
        var horizontal = arena.Rent(width, height);
        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var source = input.Span;
            var destination = horizontal.Span;
            for (var y = start; y < end; y++)
            {
                ConvolveRow(source.Slice(y * width, width), destination.Slice(y * width, width), kernel, scaledKernel);
            }
        });

        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var source = horizontal.Span;
            var destination = output.Span;
            for (var y = start; y < end; y++)
            {
                ConvolveColumns(source, width, height, y, destination.Slice(y * width, width), kernel, scaledKernel);
            }
        });

        arena.Release(horizontal);
    }

    /// <summary>The scalar reference of <see cref="Blur"/>: the rows convolved and transposed twice, as libjxl does.</summary>
    /// <exception cref="ImageResourceLimitException">The temporary plane exceeds the allocation limit.</exception>
    internal static void BlurScalar(FloatPlaneArena arena, FloatPlane input, float sigma, FloatPlane output, int maxDegreeOfParallelism)
    {
        var kernel = GetKernel(sigma);
        if (kernel.Length is 5 && input != output)
        {
            Separable5Scalar(arena, input, kernel, output, maxDegreeOfParallelism);
            return;
        }

        var transposed = arena.Rent(input.Height, input.Width);
        ConvolveAndTranspose(input, kernel, transposed, maxDegreeOfParallelism);
        ConvolveAndTranspose(transposed, kernel, output, maxDegreeOfParallelism);
        arena.Release(transposed);
    }

    /// <summary>The kernel normalized by the sum of its weights, from its first weight to its center.</summary>
    private static float[] GetScaledKernel(float[] kernel)
    {
        var offset = kernel.Length / 2;
        var weightNoBorder = 0f;
        foreach (var weight in kernel)
        {
            weightNoBorder += weight;
        }

        var scaleNoBorder = 1.0f / weightNoBorder;
        var scaledKernel = new float[offset + 1];
        for (var i = 0; i <= offset; i++)
        {
            scaledKernel[i] = kernel[i] * scaleNoBorder;
        }

        return scaledKernel;
    }

    /// <summary>
    /// Convolves a row (<see cref="ConvolveRows"/> and <see cref="ConvolveBorderColumn"/> without the transposition): the
    /// columns closer to a border than the radius of the kernel with the weights inside the row, normalized, the others
    /// with <paramref name="scaledKernel"/>.
    /// </summary>
    internal static void ConvolveRow(ReadOnlySpan<float> row, Span<float> destination, float[] kernel, float[] scaledKernel)
    {
        var width = row.Length;
        var offset = scaledKernel.Length - 1;
        var border1 = Math.Min(width, offset);
        var border2 = width > offset ? width - offset : 0;
        for (var x = 0; x < border1; x++)
        {
            destination[x] = ConvolveBorder(row, 1, width, x, kernel);
        }

        for (var x = border2; x < width; x++)
        {
            destination[x] = ConvolveBorder(row, 1, width, x, kernel);
        }

        var x0 = border1;
        if (x0 < border2 && destination.Length >= width)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(row));
            ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
            ref var weights = ref unsafe(MemoryMarshal.GetArrayDataReference(scaledKernel));
            x0 = ConvolveInterior(ref source, ref target, 1, x0, border2, ref weights, offset);
        }

        for (; x0 < border2; x0++)
        {
            destination[x0] = ConvolveInteriorScalar(row, x0, 1, scaledKernel);
        }
    }

    /// <summary>
    /// Convolves the columns of the row <paramref name="y"/> of <paramref name="plane"/>, a plane of <paramref name="width"/>
    /// columns and <paramref name="height"/> rows, into <paramref name="destination"/>, as <see cref="ConvolveRow"/> convolves a row.
    /// </summary>
    internal static void ConvolveColumns(ReadOnlySpan<float> plane, int width, int height, int y, Span<float> destination, float[] kernel, float[] scaledKernel)
    {
        if (plane.Length < width * height || destination.Length < width)
            throw new ArgumentOutOfRangeException(nameof(plane));

        var offset = scaledKernel.Length - 1;
        var border1 = Math.Min(height, offset);
        var border2 = height > offset ? height - offset : 0;
        ref var source = ref unsafe(MemoryMarshal.GetReference(plane));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        int x;
        if (y < border1 || y >= border2)
        {
            // The weights inside the plane, normalized, added in the order of the rows
            var minY = Math.Max(0, y - offset);
            var maxY = Math.Min(height - 1, y + offset);
            var weight = 0f;
            for (var j = minY; j <= maxY; j++)
            {
                weight += kernel[j - y + offset];
            }

            x = ConvolveBorderRows(ref source, ref target, width, minY, maxY, y - offset, kernel, 1.0f / weight);
            for (; x < width; x++)
            {
                destination[x] = ConvolveBorder(plane[x..], width, height, y, kernel);
            }

            return;
        }

        ref var center = ref unsafe(Unsafe.Add(ref source, (nint)y * width));
        ref var weights = ref unsafe(MemoryMarshal.GetArrayDataReference(scaledKernel));
        x = ConvolveInterior(ref center, ref target, width, 0, width, ref weights, offset);
        for (; x < width; x++)
        {
            destination[x] = ConvolveInteriorScalar(plane[x..], y, width, scaledKernel);
        }
    }

    /// <summary>The value at <paramref name="position"/>, closer to a border than the radius of the kernel: the weights inside the plane, normalized.</summary>
    private static float ConvolveBorder(ReadOnlySpan<float> values, int step, int length, int position, float[] kernel)
    {
        var offset = kernel.Length / 2;
        var min = Math.Max(0, position - offset);
        var max = Math.Min(length - 1, position + offset);
        var weight = 0f;
        for (var j = min; j <= max; j++)
        {
            weight += kernel[j - position + offset];
        }

        var scale = 1.0f / weight;
        var sum = 0f;
        for (var j = min; j <= max; j++)
        {
            sum += values[j * step] * kernel[j - position + offset];
        }

        return sum * scale;
    }

    /// <summary>
    /// The value at <paramref name="position"/>, at least the radius of the kernel away from the borders: four partial sums
    /// in the order of the unrolled loops of libjxl, then the center.
    /// </summary>
    private static float ConvolveInteriorScalar(ReadOnlySpan<float> values, int position, int step, float[] scaledKernel)
    {
        var offset = scaledKernel.Length - 1;
        float sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
        for (var j = 0; j < offset; j++)
        {
            var term = (values[(position - offset + j) * step] + values[(position + offset - j) * step]) * scaledKernel[j];
            switch (j % 4)
            {
                case 0: sum0 += term; break;
                case 1: sum1 += term; break;
                case 2: sum2 += term; break;
                default: sum3 += term; break;
            }
        }

        return values[position * step] * scaledKernel[offset] + sum0 + sum1 + sum2 + sum3;
    }

    /// <summary>
    /// Computes <see cref="ConvolveInteriorScalar"/> by vectors for the indices <c>[start, end)</c> of
    /// <paramref name="target"/>: the taps of index <c>i</c> are <paramref name="step"/> samples apart around
    /// <c>source[i]</c>. Returns the first index not computed.
    /// </summary>
    private static int ConvolveInterior(ref float source, ref float target, nint step, int start, int end, ref float weights, int offset)
    {
        var x = start;
        if (Vector512.IsHardwareAccelerated)
        {
            for (; x + Vector512<float>.Count <= end; x += Vector512<float>.Count)
            {
                unsafe { Convolve512(ref Unsafe.Add(ref source, x), step, ref weights, offset).StoreUnsafe(ref target, (nuint)x); }
            }
        }

        if (Vector256.IsHardwareAccelerated)
        {
            for (; x + Vector256<float>.Count <= end; x += Vector256<float>.Count)
            {
                unsafe { Convolve256(ref Unsafe.Add(ref source, x), step, ref weights, offset).StoreUnsafe(ref target, (nuint)x); }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; x + Vector128<float>.Count <= end; x += Vector128<float>.Count)
            {
                unsafe { Convolve128(ref Unsafe.Add(ref source, x), step, ref weights, offset).StoreUnsafe(ref target, (nuint)x); }
            }
        }

        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector512<float> Convolve512(ref float center, nint step, ref float weights, int offset)
    {
        var sum0 = Vector512<float>.Zero;
        var sum1 = Vector512<float>.Zero;
        var sum2 = Vector512<float>.Zero;
        var sum3 = Vector512<float>.Zero;
        var j = 0;
        for (; j + 4 <= offset; j += 4)
        {
            sum0 += Tap(ref center, step, ref weights, offset, j);
            sum1 += Tap(ref center, step, ref weights, offset, j + 1);
            sum2 += Tap(ref center, step, ref weights, offset, j + 2);
            sum3 += Tap(ref center, step, ref weights, offset, j + 3);
        }

        if (j < offset)
        {
            sum0 += Tap(ref center, step, ref weights, offset, j);
            if (j + 1 < offset)
            {
                sum1 += Tap(ref center, step, ref weights, offset, j + 1);
                if (j + 2 < offset)
                {
                    sum2 += Tap(ref center, step, ref weights, offset, j + 2);
                }
            }
        }

        return unsafe(Vector512.LoadUnsafe(ref center)) * Vector512.Create(unsafe(Unsafe.Add(ref weights, offset))) + sum0 + sum1 + sum2 + sum3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector512<float> Tap(ref float center, nint step, ref float weights, int offset, int j)
            => unsafe((Vector512.LoadUnsafe(ref Unsafe.Add(ref center, (j - offset) * step)) + Vector512.LoadUnsafe(ref Unsafe.Add(ref center, (offset - j) * step))) * Vector512.Create(Unsafe.Add(ref weights, j)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Convolve256(ref float center, nint step, ref float weights, int offset)
    {
        var sum0 = Vector256<float>.Zero;
        var sum1 = Vector256<float>.Zero;
        var sum2 = Vector256<float>.Zero;
        var sum3 = Vector256<float>.Zero;
        var j = 0;
        for (; j + 4 <= offset; j += 4)
        {
            sum0 += Tap(ref center, step, ref weights, offset, j);
            sum1 += Tap(ref center, step, ref weights, offset, j + 1);
            sum2 += Tap(ref center, step, ref weights, offset, j + 2);
            sum3 += Tap(ref center, step, ref weights, offset, j + 3);
        }

        if (j < offset)
        {
            sum0 += Tap(ref center, step, ref weights, offset, j);
            if (j + 1 < offset)
            {
                sum1 += Tap(ref center, step, ref weights, offset, j + 1);
                if (j + 2 < offset)
                {
                    sum2 += Tap(ref center, step, ref weights, offset, j + 2);
                }
            }
        }

        return unsafe(Vector256.LoadUnsafe(ref center)) * Vector256.Create(unsafe(Unsafe.Add(ref weights, offset))) + sum0 + sum1 + sum2 + sum3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<float> Tap(ref float center, nint step, ref float weights, int offset, int j)
            => unsafe((Vector256.LoadUnsafe(ref Unsafe.Add(ref center, (j - offset) * step)) + Vector256.LoadUnsafe(ref Unsafe.Add(ref center, (offset - j) * step))) * Vector256.Create(Unsafe.Add(ref weights, j)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Convolve128(ref float center, nint step, ref float weights, int offset)
    {
        var sum0 = Vector128<float>.Zero;
        var sum1 = Vector128<float>.Zero;
        var sum2 = Vector128<float>.Zero;
        var sum3 = Vector128<float>.Zero;
        var j = 0;
        for (; j + 4 <= offset; j += 4)
        {
            sum0 += Tap(ref center, step, ref weights, offset, j);
            sum1 += Tap(ref center, step, ref weights, offset, j + 1);
            sum2 += Tap(ref center, step, ref weights, offset, j + 2);
            sum3 += Tap(ref center, step, ref weights, offset, j + 3);
        }

        if (j < offset)
        {
            sum0 += Tap(ref center, step, ref weights, offset, j);
            if (j + 1 < offset)
            {
                sum1 += Tap(ref center, step, ref weights, offset, j + 1);
                if (j + 2 < offset)
                {
                    sum2 += Tap(ref center, step, ref weights, offset, j + 2);
                }
            }
        }

        return unsafe(Vector128.LoadUnsafe(ref center)) * Vector128.Create(unsafe(Unsafe.Add(ref weights, offset))) + sum0 + sum1 + sum2 + sum3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<float> Tap(ref float center, nint step, ref float weights, int offset, int j)
            => unsafe((Vector128.LoadUnsafe(ref Unsafe.Add(ref center, (j - offset) * step)) + Vector128.LoadUnsafe(ref Unsafe.Add(ref center, (offset - j) * step))) * Vector128.Create(Unsafe.Add(ref weights, j)));
    }

    /// <summary>
    /// Computes the border rows of <see cref="ConvolveColumns"/> by vectors: the rows <c>[minY, maxY]</c>, row <c>j</c>
    /// weighted by <c>kernel[j - first]</c>, added in order, times <paramref name="scale"/>. Returns the first column not computed.
    /// </summary>
    private static int ConvolveBorderRows(ref float source, ref float target, int width, int minY, int maxY, int first, float[] kernel, float scale)
    {
        var x = 0;
        if (Vector512.IsHardwareAccelerated)
        {
            for (; x + Vector512<float>.Count <= width; x += Vector512<float>.Count)
            {
                var sum = Vector512<float>.Zero;
                for (var j = minY; j <= maxY; j++)
                {
                    sum += unsafe(Vector512.LoadUnsafe(ref source, (nuint)(((nint)j * width) + x))) * Vector512.Create(kernel[j - first]);
                }

                unsafe { (sum * Vector512.Create(scale)).StoreUnsafe(ref target, (nuint)x); }
            }
        }

        if (Vector256.IsHardwareAccelerated)
        {
            for (; x + Vector256<float>.Count <= width; x += Vector256<float>.Count)
            {
                var sum = Vector256<float>.Zero;
                for (var j = minY; j <= maxY; j++)
                {
                    sum += unsafe(Vector256.LoadUnsafe(ref source, (nuint)(((nint)j * width) + x))) * Vector256.Create(kernel[j - first]);
                }

                unsafe { (sum * Vector256.Create(scale)).StoreUnsafe(ref target, (nuint)x); }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; x + Vector128<float>.Count <= width; x += Vector128<float>.Count)
            {
                var sum = Vector128<float>.Zero;
                for (var j = minY; j <= maxY; j++)
                {
                    sum += unsafe(Vector128.LoadUnsafe(ref source, (nuint)(((nint)j * width) + x))) * Vector128.Create(kernel[j - first]);
                }

                unsafe { (sum * Vector128.Create(scale)).StoreUnsafe(ref target, (nuint)x); }
            }
        }

        return x;
    }

    private static float[] GetKernel(float sigma)
    {
        lock (KernelsLock)
        {
            if (!Kernels.TryGetValue(sigma, out var kernel))
            {
                const float M = 2.25f;
                var scaler = -1.0 / (2.0 * sigma * sigma);
                var diff = Math.Max(1, (int)(M * Math.Abs(sigma)));
                kernel = new float[2 * diff + 1];
                for (var i = -diff; i <= diff; i++)
                {
                    kernel[i + diff] = (float)Math.Exp(scaler * i * i);
                }

                Kernels.Add(sigma, kernel);
            }

            return kernel;
        }
    }

    /// <summary>Convolves the rows and writes the result transposed: <c>output[x, y]</c> is the convolution at <c>(x, y)</c>.</summary>
    private static void ConvolveAndTranspose(FloatPlane input, float[] kernel, FloatPlane output, int maxDegreeOfParallelism)
    {
        var width = input.Width;
        var height = input.Height;
        var offset = kernel.Length / 2;

        var weightNoBorder = 0f;
        foreach (var weight in kernel)
        {
            weightNoBorder += weight;
        }

        var scaleNoBorder = 1.0f / weightNoBorder;
        var scaledKernel = new float[offset + 1];
        for (var i = 0; i <= offset; i++)
        {
            scaledKernel[i] = kernel[i] * scaleNoBorder;
        }

        var border1 = Math.Min(width, offset);
        var border2 = width > offset ? width - offset : 0;
        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            ConvolveRows(input.Span, output.Span, width, height, scaledKernel, border1, border2, start, end);
            for (var x = 0; x < border1; x++)
            {
                ConvolveBorderColumn(input.Span, width, height, kernel, x, output.Span, start, end);
            }

            for (var x = border2; x < width; x++)
            {
                ConvolveBorderColumn(input.Span, width, height, kernel, x, output.Span, start, end);
            }
        });
    }

    /// <summary>Convolves the rows <c>[start, end)</c> between the border columns.</summary>
    private static void ConvolveRows(ReadOnlySpan<float> source, Span<float> destination, int width, int height, float[] scaledKernel, int border1, int border2, int start, int end)
    {
        var offset = scaledKernel.Length - 1;
        for (var y = start; y < end; y++)
        {
            var row = source.Slice(y * width, width);
            for (var x = border1; x < border2; x++)
            {
                // Four partial sums in the order of the unrolled loops of libjxl, then the center
                float sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                for (var j = 0; j < offset; j++)
                {
                    var term = (row[x - offset + j] + row[x + offset - j]) * scaledKernel[j];
                    switch (j % 4)
                    {
                        case 0: sum0 += term; break;
                        case 1: sum1 += term; break;
                        case 2: sum2 += term; break;
                        default: sum3 += term; break;
                    }
                }

                destination[x * height + y] = row[x] * scaledKernel[offset] + sum0 + sum1 + sum2 + sum3;
            }
        }
    }

    /// <summary>Convolves column <paramref name="x"/> of the rows <c>[start, end)</c>, with the weights inside the plane.</summary>
    private static void ConvolveBorderColumn(ReadOnlySpan<float> source, int width, int height, float[] kernel, int x, Span<float> transposed, int start, int end)
    {
        var offset = kernel.Length / 2;
        var minX = Math.Max(0, x - offset);
        var maxX = Math.Min(width - 1, x + offset);
        var weight = 0f;
        for (var j = minX; j <= maxX; j++)
        {
            weight += kernel[j - x + offset];
        }

        var scale = 1.0f / weight;
        var destination = transposed.Slice(x * height, height);
        for (var y = start; y < end; y++)
        {
            var row = source.Slice(y * width, width);
            var sum = 0f;
            for (var j = minX; j <= maxX; j++)
            {
                sum += row[j] * kernel[j - x + offset];
            }

            destination[y] = sum * scale;
        }
    }

    /// <summary>The 5×5 separable convolution of libjxl (<c>Separable5</c>), which mirrors the plane at its borders.</summary>
    private static void Separable5(FloatPlaneArena arena, FloatPlane input, float[] kernel, FloatPlane output, int maxDegreeOfParallelism)
    {
        var width = input.Width;
        var height = input.Height;
        var sumWeights = 0f;
        foreach (var weight in kernel)
        {
            sumWeights += weight;
        }

        var scale = 1.0f / sumWeights;
        var w0 = kernel[2] * scale;
        var w1 = kernel[1] * scale;
        var w2 = kernel[0] * scale;

        var horizontal = arena.Rent(width, height);
        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var source = input.Span;
            var rows = horizontal.Span;
            for (var y = start; y < end; y++)
            {
                Separable5Row(source.Slice(y * width, width), rows.Slice(y * width, width), w0, w1, w2);
            }
        });

        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var rows = horizontal.Span;
            var result = output.Span;
            for (var y = start; y < end; y++)
            {
                Separable5Column(
                    rows.Slice(y * width, width),
                    rows.Slice(Mirror(y - 1, height) * width, width),
                    rows.Slice(Mirror(y + 1, height) * width, width),
                    rows.Slice(Mirror(y - 2, height) * width, width),
                    rows.Slice(Mirror(y + 2, height) * width, width),
                    result.Slice(y * width, width),
                    w0,
                    w1,
                    w2);
            }
        });

        arena.Release(horizontal);
    }

    /// <summary>The horizontal pass of <see cref="Separable5"/> on a row; the vectors compute the columns that need no mirroring.</summary>
    internal static void Separable5Row(ReadOnlySpan<float> row, Span<float> destination, float w0, float w1, float w2)
    {
        var width = row.Length;
        var x = 0;
        for (; x < Math.Min(2, width); x++)
        {
            destination[x] = Separable5Value(row, x, w0, w1, w2);
        }

        // The columns [2, width - 2) read the columns [x - 2, x + 2], inside the row
        var end = width - 2;
        if (x < end && destination.Length >= width)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(row));
            ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
            if (Vector512.IsHardwareAccelerated)
            {
                var weight0 = Vector512.Create(w0);
                var weight1 = Vector512.Create(w1);
                var weight2 = Vector512.Create(w2);
                for (; x + Vector512<float>.Count <= end; x += Vector512<float>.Count)
                {
                    unsafe
                    {
                        ref var center = ref Unsafe.Add(ref source, x);
                        var value = weight0 * Vector512.LoadUnsafe(ref center)
                            + weight1 * (Vector512.LoadUnsafe(ref Unsafe.Subtract(ref center, 1)) + Vector512.LoadUnsafe(ref Unsafe.Add(ref center, 1)))
                            + weight2 * (Vector512.LoadUnsafe(ref Unsafe.Subtract(ref center, 2)) + Vector512.LoadUnsafe(ref Unsafe.Add(ref center, 2)));
                        value.StoreUnsafe(ref target, (nuint)x);
                    }
                }
            }

            if (Vector256.IsHardwareAccelerated)
            {
                var weight0 = Vector256.Create(w0);
                var weight1 = Vector256.Create(w1);
                var weight2 = Vector256.Create(w2);
                for (; x + Vector256<float>.Count <= end; x += Vector256<float>.Count)
                {
                    unsafe
                    {
                        ref var center = ref Unsafe.Add(ref source, x);
                        var value = weight0 * Vector256.LoadUnsafe(ref center)
                            + weight1 * (Vector256.LoadUnsafe(ref Unsafe.Subtract(ref center, 1)) + Vector256.LoadUnsafe(ref Unsafe.Add(ref center, 1)))
                            + weight2 * (Vector256.LoadUnsafe(ref Unsafe.Subtract(ref center, 2)) + Vector256.LoadUnsafe(ref Unsafe.Add(ref center, 2)));
                        value.StoreUnsafe(ref target, (nuint)x);
                    }
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                var weight0 = Vector128.Create(w0);
                var weight1 = Vector128.Create(w1);
                var weight2 = Vector128.Create(w2);
                for (; x + Vector128<float>.Count <= end; x += Vector128<float>.Count)
                {
                    unsafe
                    {
                        ref var center = ref Unsafe.Add(ref source, x);
                        var value = weight0 * Vector128.LoadUnsafe(ref center)
                            + weight1 * (Vector128.LoadUnsafe(ref Unsafe.Subtract(ref center, 1)) + Vector128.LoadUnsafe(ref Unsafe.Add(ref center, 1)))
                            + weight2 * (Vector128.LoadUnsafe(ref Unsafe.Subtract(ref center, 2)) + Vector128.LoadUnsafe(ref Unsafe.Add(ref center, 2)));
                        value.StoreUnsafe(ref target, (nuint)x);
                    }
                }
            }
        }

        for (; x < width; x++)
        {
            destination[x] = Separable5Value(row, x, w0, w1, w2);
        }
    }

    /// <summary>The scalar reference of <see cref="Separable5Row"/>.</summary>
    internal static void Separable5RowScalar(ReadOnlySpan<float> row, Span<float> destination, float w0, float w1, float w2)
    {
        for (var x = 0; x < row.Length; x++)
        {
            destination[x] = Separable5Value(row, x, w0, w1, w2);
        }
    }

    private static float Separable5Value(ReadOnlySpan<float> row, int x, float w0, float w1, float w2)
    {
        var width = row.Length;
        return w0 * row[x]
            + w1 * (row[Mirror(x - 1, width)] + row[Mirror(x + 1, width)])
            + w2 * (row[Mirror(x - 2, width)] + row[Mirror(x + 2, width)]);
    }

    /// <summary>The vertical pass of <see cref="Separable5"/> on a row, from the rows around it (mirrored at the borders).</summary>
    internal static void Separable5Column(ReadOnlySpan<float> center, ReadOnlySpan<float> up1, ReadOnlySpan<float> down1, ReadOnlySpan<float> up2, ReadOnlySpan<float> down2, Span<float> destination, float w0, float w1, float w2)
    {
        var width = destination.Length;
        var x = 0;
        if (center.Length >= width && up1.Length >= width && down1.Length >= width && up2.Length >= width && down2.Length >= width)
        {
            ref var c = ref unsafe(MemoryMarshal.GetReference(center));
            ref var u1 = ref unsafe(MemoryMarshal.GetReference(up1));
            ref var d1 = ref unsafe(MemoryMarshal.GetReference(down1));
            ref var u2 = ref unsafe(MemoryMarshal.GetReference(up2));
            ref var d2 = ref unsafe(MemoryMarshal.GetReference(down2));
            ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
            if (Vector512.IsHardwareAccelerated)
            {
                var weight0 = Vector512.Create(w0);
                var weight1 = Vector512.Create(w1);
                var weight2 = Vector512.Create(w2);
                for (; x + Vector512<float>.Count <= width; x += Vector512<float>.Count)
                {
                    unsafe
                    {
                        var i = (nuint)x;
                        var value = weight0 * Vector512.LoadUnsafe(ref c, i) + weight1 * (Vector512.LoadUnsafe(ref u1, i) + Vector512.LoadUnsafe(ref d1, i)) + weight2 * (Vector512.LoadUnsafe(ref u2, i) + Vector512.LoadUnsafe(ref d2, i));
                        value.StoreUnsafe(ref target, i);
                    }
                }
            }

            if (Vector256.IsHardwareAccelerated)
            {
                var weight0 = Vector256.Create(w0);
                var weight1 = Vector256.Create(w1);
                var weight2 = Vector256.Create(w2);
                for (; x + Vector256<float>.Count <= width; x += Vector256<float>.Count)
                {
                    unsafe
                    {
                        var i = (nuint)x;
                        var value = weight0 * Vector256.LoadUnsafe(ref c, i) + weight1 * (Vector256.LoadUnsafe(ref u1, i) + Vector256.LoadUnsafe(ref d1, i)) + weight2 * (Vector256.LoadUnsafe(ref u2, i) + Vector256.LoadUnsafe(ref d2, i));
                        value.StoreUnsafe(ref target, i);
                    }
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                var weight0 = Vector128.Create(w0);
                var weight1 = Vector128.Create(w1);
                var weight2 = Vector128.Create(w2);
                for (; x + Vector128<float>.Count <= width; x += Vector128<float>.Count)
                {
                    unsafe
                    {
                        var i = (nuint)x;
                        var value = weight0 * Vector128.LoadUnsafe(ref c, i) + weight1 * (Vector128.LoadUnsafe(ref u1, i) + Vector128.LoadUnsafe(ref d1, i)) + weight2 * (Vector128.LoadUnsafe(ref u2, i) + Vector128.LoadUnsafe(ref d2, i));
                        value.StoreUnsafe(ref target, i);
                    }
                }
            }
        }

        for (; x < width; x++)
        {
            destination[x] = w0 * center[x] + w1 * (up1[x] + down1[x]) + w2 * (up2[x] + down2[x]);
        }
    }

    /// <summary>The scalar reference of <see cref="Separable5"/>.</summary>
    private static void Separable5Scalar(FloatPlaneArena arena, FloatPlane input, float[] kernel, FloatPlane output, int maxDegreeOfParallelism)
    {
        var width = input.Width;
        var height = input.Height;
        var sumWeights = 0f;
        foreach (var weight in kernel)
        {
            sumWeights += weight;
        }

        var scale = 1.0f / sumWeights;
        var w0 = kernel[2] * scale;
        var w1 = kernel[1] * scale;
        var w2 = kernel[0] * scale;

        var horizontal = arena.Rent(width, height);
        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var source = input.Span;
            var rows = horizontal.Span;
            for (var y = start; y < end; y++)
            {
                var row = source.Slice(y * width, width);
                var destination = rows.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    destination[x] = w0 * row[x]
                        + w1 * (row[Mirror(x - 1, width)] + row[Mirror(x + 1, width)])
                        + w2 * (row[Mirror(x - 2, width)] + row[Mirror(x + 2, width)]);
                }
            }
        });

        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var rows = horizontal.Span;
            var result = output.Span;
            for (var y = start; y < end; y++)
            {
                var center = rows.Slice(y * width, width);
                var up1 = rows.Slice(Mirror(y - 1, height) * width, width);
                var down1 = rows.Slice(Mirror(y + 1, height) * width, width);
                var up2 = rows.Slice(Mirror(y - 2, height) * width, width);
                var down2 = rows.Slice(Mirror(y + 2, height) * width, width);
                var destination = result.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    destination[x] = w0 * center[x] + w1 * (up1[x] + down1[x]) + w2 * (up2[x] + down2[x]);
                }
            }
        });

        arena.Release(horizontal);
    }

    /// <summary>Mirrors a coordinate into [0, size), repeating the edge sample (jxl::Mirror).</summary>
    private static int Mirror(int x, int size)
    {
        while (x < 0 || x >= size)
        {
            x = x < 0 ? -x - 1 : 2 * size - 1 - x;
        }

        return x;
    }
}
