using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The upsampling of a channel by 2, 4 or 8 (ISO/IEC 18181-1, section K.5): each sample becomes a square of samples,
/// each one a weighted sum of the 5x5 samples around the source, clamped to the range of these samples.
/// </summary>
/// <remarks>
/// The weights of a factor are the upper triangle of a symmetric matrix indexed by the output position in a quarter of
/// the square and the input position; the kernels of the other quarters are its mirror images. Samples outside the
/// channel are the mirror image of the samples inside, the edge sample included.
/// </remarks>
internal static class JpegXlUpsampler
{
    private const int KernelSide = 5;
    private const int KernelSize = KernelSide * KernelSide;

    /// <summary>Builds the kernels of a factor: for each output position of the square, in raster order, its 5x5 weights.</summary>
    /// <param name="factor">The factor: 2, 4 or 8.</param>
    /// <param name="transformData">The transform data of the image, which may hold custom weights.</param>
    public static float[] CreateKernels(int factor, JpegXlTransformData transformData)
    {
        ArgumentNullException.ThrowIfNull(transformData);
        var weights = factor switch
        {
            2 => transformData.Upsampling2Weights ?? JpegXlUpsamplingTables.Weights2,
            4 => transformData.Upsampling4Weights ?? JpegXlUpsamplingTables.Weights4,
            8 => transformData.Upsampling8Weights ?? JpegXlUpsamplingTables.Weights8,
            _ => throw new ArgumentOutOfRangeException(nameof(factor)),
        };

        var half = factor / 2;
        var kernels = new float[factor * factor * KernelSize];
        for (var ky = 0; ky < half; ky++)
        {
            for (var kx = 0; kx < half; kx++)
            {
                var topLeft = ((ky * factor) + kx) * KernelSize;
                var topRight = ((ky * factor) + (factor - 1 - kx)) * KernelSize;
                var bottomLeft = (((factor - 1 - ky) * factor) + kx) * KernelSize;
                var bottomRight = (((factor - 1 - ky) * factor) + (factor - 1 - kx)) * KernelSize;
                for (var py = 0; py < KernelSide; py++)
                {
                    for (var px = 0; px < KernelSide; px++)
                    {
                        var j = (KernelSide * ky) + py;
                        var i = (KernelSide * kx) + px;
                        var low = Math.Min(i, j);
                        var high = Math.Max(i, j);
                        var weight = weights[(KernelSide * half * low) - (low * (low - 1) / 2) + high - low];
                        kernels[topLeft + (py * KernelSide) + px] = weight;
                        kernels[topRight + (py * KernelSide) + (KernelSide - 1 - px)] = weight;
                        kernels[bottomLeft + ((KernelSide - 1 - py) * KernelSide) + px] = weight;
                        kernels[bottomRight + ((KernelSide - 1 - py) * KernelSide) + (KernelSide - 1 - px)] = weight;
                    }
                }
            }
        }

        return kernels;
    }

    /// <summary>Upsamples a channel.</summary>
    /// <param name="source">The samples, in rows of <paramref name="sourceStride"/> values.</param>
    /// <param name="sourceStride">The distance between two rows of <paramref name="source"/>.</param>
    /// <param name="sourceWidth">The width of the channel.</param>
    /// <param name="sourceHeight">The height of the channel.</param>
    /// <param name="factor">The factor: 2, 4 or 8.</param>
    /// <param name="kernels">The kernels of the factor (<see cref="CreateKernels"/>).</param>
    /// <param name="target">Receives the samples, in rows of <paramref name="targetWidth"/> values.</param>
    /// <param name="targetWidth">The width of the result: at most the width of the channel times the factor.</param>
    /// <param name="targetHeight">The height of the result: at most the height of the channel times the factor.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <remarks>
    /// The samples whose 5x5 inputs are inside the channel and whose outputs are inside the target are computed four at a
    /// time, one per lane, with the operations of <see cref="UpsampleScalar"/> in the same order; the others are computed as
    /// the scalar reference computes them.
    /// </remarks>
    public static void Upsample(ReadOnlySpan<float> source, int sourceStride, int sourceWidth, int sourceHeight, int factor, ReadOnlySpan<float> kernels, Span<float> target, int targetWidth, int targetHeight, CancellationToken cancellationToken)
    {
        if (!Vector128.IsHardwareAccelerated || factor is not (2 or 4 or 8))
        {
            UpsampleScalar(source, sourceStride, sourceWidth, sourceHeight, factor, kernels, target, targetWidth, targetHeight, cancellationToken);
            return;
        }

        Span<float> window = stackalloc float[KernelSize];
        Span<int> rows = stackalloc int[KernelSide];
        Span<int> columns = stackalloc int[KernelSide];
        Span<Vector128<float>> windows = stackalloc Vector128<float>[KernelSize];
        Span<Vector128<float>> results = stackalloc Vector128<float>[8];

        // The samples from 2 to this end (excluded) have their five inputs inside the channel and their outputs inside the target
        var interiorEnd = Math.Min(sourceWidth - 2, targetWidth / factor);
        for (var y = 0; y < sourceHeight && y * factor < targetHeight; y++)
        {
            if ((y & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var i = 0; i < KernelSide; i++)
            {
                rows[i] = Mirror(y + i - 2, sourceHeight) * sourceStride;
            }

            var x = Math.Min(2, sourceWidth);
            UpsampleColumns(source, rows, columns, window, sourceWidth, factor, kernels, target, targetWidth, targetHeight, y, 0, x);
            for (; x + Vector128<float>.Count <= interiorEnd; x += Vector128<float>.Count)
            {
                // The 25 inputs of four neighboring samples, and their range
                for (var iy = 0; iy < KernelSide; iy++)
                {
                    var row = source.Slice(rows[iy] + x - 2, Vector128<float>.Count + KernelSide - 1);
                    for (var ix = 0; ix < KernelSide; ix++)
                    {
                        windows[(iy * KernelSide) + ix] = Vector128.Create(row.Slice(ix, Vector128<float>.Count));
                    }
                }

                var min = Vector128.Create(float.MaxValue);
                var max = Vector128.Create(float.MinValue);
                foreach (var value in windows)
                {
                    min = Vector128.Min(min, value);
                    max = Vector128.Max(max, value);
                }

                for (var oy = 0; oy < factor; oy++)
                {
                    var targetY = (y * factor) + oy;
                    if (targetY >= targetHeight)
                        break;

                    for (var ox = 0; ox < factor; ox++)
                    {
                        var kernel = kernels.Slice(((oy * factor) + ox) * KernelSize, KernelSize);
                        var sum0 = windows[0] * Vector128.Create(kernel[0]);
                        var sum1 = windows[1] * Vector128.Create(kernel[1]);
                        var sum2 = windows[2] * Vector128.Create(kernel[2]);
                        for (var i = 3; i < 24; i += 3)
                        {
                            sum0 += windows[i] * Vector128.Create(kernel[i]);
                            sum1 += windows[i + 1] * Vector128.Create(kernel[i + 1]);
                            sum2 += windows[i + 2] * Vector128.Create(kernel[i + 2]);
                        }

                        sum0 += windows[24] * Vector128.Create(kernel[24]);
                        var result = (sum1 + sum2) + sum0;
                        results[ox] = Vector128.Min(max, Vector128.Max(min, result));
                    }

                    // The lane j of the result ox is the output ox of the sample x + j: the results are interleaved
                    var output = target.Slice((targetY * targetWidth) + (x * factor), Vector128<float>.Count * factor);
                    switch (factor)
                    {
                        case 2:
                            Zip(results[0], results[1], out var low, out var high);
                            low.CopyTo(output);
                            high.CopyTo(output[4..]);
                            break;

                        case 4:
                            Transpose(results[0], results[1], results[2], results[3], out var r0, out var r1, out var r2, out var r3);
                            r0.CopyTo(output);
                            r1.CopyTo(output[4..]);
                            r2.CopyTo(output[8..]);
                            r3.CopyTo(output[12..]);
                            break;

                        default:
                            Transpose(results[0], results[1], results[2], results[3], out var a0, out var a1, out var a2, out var a3);
                            Transpose(results[4], results[5], results[6], results[7], out var b0, out var b1, out var b2, out var b3);
                            a0.CopyTo(output);
                            b0.CopyTo(output[4..]);
                            a1.CopyTo(output[8..]);
                            b1.CopyTo(output[12..]);
                            a2.CopyTo(output[16..]);
                            b2.CopyTo(output[20..]);
                            a3.CopyTo(output[24..]);
                            b3.CopyTo(output[28..]);
                            break;
                    }
                }
            }

            UpsampleColumns(source, rows, columns, window, sourceWidth, factor, kernels, target, targetWidth, targetHeight, y, x, sourceWidth);
        }
    }

    /// <summary>The scalar reference of <see cref="Upsample"/>.</summary>
    internal static void UpsampleScalar(ReadOnlySpan<float> source, int sourceStride, int sourceWidth, int sourceHeight, int factor, ReadOnlySpan<float> kernels, Span<float> target, int targetWidth, int targetHeight, CancellationToken cancellationToken)
    {
        Span<float> window = stackalloc float[KernelSize];
        Span<int> rows = stackalloc int[KernelSide];
        Span<int> columns = stackalloc int[KernelSide];
        for (var y = 0; y < sourceHeight && y * factor < targetHeight; y++)
        {
            if ((y & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var i = 0; i < KernelSide; i++)
            {
                rows[i] = Mirror(y + i - 2, sourceHeight) * sourceStride;
            }

            UpsampleColumns(source, rows, columns, window, sourceWidth, factor, kernels, target, targetWidth, targetHeight, y, 0, sourceWidth);
        }
    }

    /// <summary>Upsamples the samples <paramref name="start"/> to <paramref name="end"/> (excluded) of the row <paramref name="y"/>, one at a time.</summary>
    private static void UpsampleColumns(ReadOnlySpan<float> source, ReadOnlySpan<int> rows, Span<int> columns, Span<float> window, int sourceWidth, int factor, ReadOnlySpan<float> kernels, Span<float> target, int targetWidth, int targetHeight, int y, int start, int end)
    {
        for (var x = start; x < end && x * factor < targetWidth; x++)
        {
            for (var i = 0; i < KernelSide; i++)
            {
                columns[i] = Mirror(x + i - 2, sourceWidth);
            }

            var min = float.MaxValue;
            var max = float.MinValue;
            for (var iy = 0; iy < KernelSide; iy++)
            {
                for (var ix = 0; ix < KernelSide; ix++)
                {
                    var value = source[rows[iy] + columns[ix]];
                    window[(iy * KernelSide) + ix] = value;
                    min = MathF.Min(min, value);
                    max = MathF.Max(max, value);
                }
            }

            for (var oy = 0; oy < factor; oy++)
            {
                var targetY = (y * factor) + oy;
                if (targetY >= targetHeight)
                    break;

                for (var ox = 0; ox < factor; ox++)
                {
                    var targetX = (x * factor) + ox;
                    if (targetX >= targetWidth)
                        break;

                    // Three partial sums, of every third weight, added in a fixed order
                    var kernel = kernels.Slice(((oy * factor) + ox) * KernelSize, KernelSize);
                    var sum0 = window[0] * kernel[0];
                    var sum1 = window[1] * kernel[1];
                    var sum2 = window[2] * kernel[2];
                    for (var i = 3; i < 24; i += 3)
                    {
                        sum0 += window[i] * kernel[i];
                        sum1 += window[i + 1] * kernel[i + 1];
                        sum2 += window[i + 2] * kernel[i + 2];
                    }

                    sum0 += window[24] * kernel[24];
                    var result = (sum1 + sum2) + sum0;
                    target[(targetY * targetWidth) + targetX] = MathF.Min(max, MathF.Max(min, result));
                }
            }
        }
    }

    /// <summary>Interleaves two vectors: the lanes 0 and 1 of both, then their lanes 2 and 3.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Zip(Vector128<float> a, Vector128<float> b, out Vector128<float> low, out Vector128<float> high)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            low = AdvSimd.Arm64.ZipLow(a, b);
            high = AdvSimd.Arm64.ZipHigh(a, b);
        }
        else if (Sse.IsSupported)
        {
            low = Sse.UnpackLow(a, b);
            high = Sse.UnpackHigh(a, b);
        }
        else
        {
            low = Vector128.Create(a.GetElement(0), b.GetElement(0), a.GetElement(1), b.GetElement(1));
            high = Vector128.Create(a.GetElement(2), b.GetElement(2), a.GetElement(3), b.GetElement(3));
        }
    }

    /// <summary>Transposes four vectors of four values: the lane i of the result j is the lane j of the input i.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(Vector128<float> a, Vector128<float> b, Vector128<float> c, Vector128<float> d, out Vector128<float> r0, out Vector128<float> r1, out Vector128<float> r2, out Vector128<float> r3)
    {
        Zip(a, c, out var ac0, out var ac1);
        Zip(b, d, out var bd0, out var bd1);
        Zip(ac0, bd0, out r0, out r1);
        Zip(ac1, bd1, out r2, out r3);
    }

    private static int Mirror(int position, int size)
    {
        while (position < 0 || position >= size)
        {
            position = position < 0 ? -position - 1 : (2 * size) - 1 - position;
        }

        return position;
    }
}
