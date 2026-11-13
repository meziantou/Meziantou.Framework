using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The zoom and spin blurs of <see cref="ImageProcessingExtensions.RadialBlur(Image, RadialBlurOptions, CancellationToken)"/>:
/// every pixel becomes the mean of <c>n</c> bilinear samples of the frame, evenly spaced along a path centered on the
/// pixel (a segment of the line through the center, or an arc of the circle around it).
/// </summary>
/// <remarks>
/// <para>
/// The path of a pixel at offset <c>d</c> from the center has the length <c>L = Amount × |d|</c> (zoom) or
/// <c>L = Amount × π / 180 × |d|</c> (spin), and <c>n = min(MaxSampleCount, ceil(L) + 1)</c> samples, which are at most
/// one pixel apart unless the cap is reached; a pixel at the center keeps its value. The samples of a zoom are at the
/// offsets <c>d × (1 + Amount × (i / (n - 1) - 1/2))</c>, those of a spin at the rotations of <c>d</c> by
/// <c>Amount × (i / (n - 1) - 1/2)</c> degrees (by <c>360 × i / n - 180</c> for the whole circle, whose ends are the same
/// point). A sample at a pixel center reads that pixel only.
/// </para>
/// <para>
/// The frame is read from its working copy (<see cref="WorkingFrameCopy"/>), so its rows are rewritten in place in any
/// order: large frames are split into row bands, one per worker, and every pixel is computed alone, so the result does not
/// depend on the number of workers.
/// </para>
/// <para>
/// Pixels of 4 working samples are summed in the lanes of a vector of 4 doubles (two vectors of 2 doubles without 256-bit
/// vectors), each lane in the order of the scalar sums.
/// </para>
/// </remarks>
internal static class RadialBlurrer
{
    /// <summary>Blurs a leased frame of the format of the copy in place.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static void Apply(PixelStorage storage, WorkingFrameCopy copy, RadialBlurOptions options, CancellationToken cancellationToken)
    {
        copy.Load(storage, cancellationToken);
        var height = copy.Size.Height;
        var workers = copy.Workers;
        if (workers == 1)
        {
            BlurRows(storage, copy, options, worker: 0, 0, height, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => BlurRows(storage, copy, options, band, RowBands.GetBandStart(height, band, workers), RowBands.GetBandStart(height, band + 1, workers), cancellationToken));
    }

    /// <summary>Gets the number of samples of a path of <paramref name="length"/> pixels.</summary>
    internal static int GetSampleCount(double length, int maxSampleCount)
        => length >= maxSampleCount - 1 ? maxSampleCount : Math.Max(1, (int)Math.Ceiling(length) + 1);

    private static void BlurRows(PixelStorage storage, WorkingFrameCopy copy, RadialBlurOptions options, int worker, int start, int end, CancellationToken cancellationToken)
    {
        var width = copy.Size.Width;
        var channels = copy.WorkingChannels;
        var output = copy.GetRowBuffer(worker)[..(width * channels)];
        var spin = options.Kind == RadialBlurKind.Spin;
        var amount = spin ? options.Amount * Math.PI / 180 : options.Amount;
        var wholeCircle = spin && options.Amount == 360;
        var centerX = options.Center.X;
        var centerY = options.Center.Y;
        var maxSampleCount = options.MaxSampleCount;
        var vector = channels == 4 && Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar;

        // By sample count n, at offset n (n - 1) / 2: the cosines and sines of the n angles of a spin, or the n scales of a zoom
        var rotations = spin ? CreateRotations(amount, wholeCircle, maxSampleCount) : null;
        var scales = spin ? null : CreateScales(amount, maxSampleCount);
        for (var y = start; y < end; y++)
        {
            if ((y - start) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var dy = y + 0.5 - centerY;
            for (var x = 0; x < width; x++)
            {
                var dx = x + 0.5 - centerX;
                var count = GetSampleCount(amount * Math.Sqrt((dx * dx) + (dy * dy)), maxSampleCount);
                var pixel = output.Slice(x * channels, channels);
                if (count == 1)
                {
                    var row = copy.GetRow(y);
                    for (var c = 0; c < channels; c++)
                    {
                        pixel[c] = row[(x * channels) + c];
                    }

                    continue;
                }

                if (vector)
                {
                    if (Vector256.IsHardwareAccelerated)
                    {
                        BlurPixelVector256(copy, rotations, scales, count, centerX, centerY, dx, dy, pixel);
                    }
                    else
                    {
                        BlurPixelVector128(copy, rotations, scales, count, centerX, centerY, dx, dy, pixel);
                    }

                    continue;
                }

                var sums = new Sums();
                if (spin)
                {
                    var offset = count * (count - 1) / 2;
                    for (var i = 0; i < count; i++)
                    {
                        var (cos, sin) = rotations![offset + i];
                        AddBilinearSample(copy, centerX + (dx * cos) - (dy * sin), centerY + (dx * sin) + (dy * cos), channels, ref sums);
                    }
                }
                else
                {
                    var offset = count * (count - 1) / 2;
                    for (var i = 0; i < count; i++)
                    {
                        var scale = scales![offset + i];
                        AddBilinearSample(copy, centerX + (dx * scale), centerY + (dy * scale), channels, ref sums);
                    }
                }

                pixel[0] = sums.S0 / count;
                if (channels > 1)
                {
                    pixel[1] = sums.S1 / count;
                    pixel[2] = sums.S2 / count;
                    if (channels == 4)
                    {
                        pixel[3] = sums.S3 / count;
                    }
                }
            }

            copy.Store(storage, y, output);
        }
    }

    /// <summary>Gets the scales of the samples of a zoom of every sample count from 1 to <paramref name="maxSampleCount"/>, the scales of count <c>n</c> at offset <c>n (n - 1) / 2</c>.</summary>
    private static double[] CreateScales(double amount, int maxSampleCount)
    {
        var scales = new double[maxSampleCount * (maxSampleCount + 1) / 2];
        for (var count = 1; count <= maxSampleCount; count++)
        {
            var offset = count * (count - 1) / 2;
            for (var i = 0; i < count; i++)
            {
                scales[offset + i] = count == 1 ? 1 : 1 + (amount * (((double)i / (count - 1)) - 0.5));
            }
        }

        return scales;
    }

    /// <summary>Gets the cosines and sines of the angles of every sample count from 1 to <paramref name="maxSampleCount"/>, the angles of count <c>n</c> at offset <c>n (n - 1) / 2</c>.</summary>
    private static (double Cos, double Sin)[] CreateRotations(double radians, bool wholeCircle, int maxSampleCount)
    {
        var rotations = new (double Cos, double Sin)[maxSampleCount * (maxSampleCount + 1) / 2];
        for (var count = 1; count <= maxSampleCount; count++)
        {
            var offset = count * (count - 1) / 2;
            for (var i = 0; i < count; i++)
            {
                var angle = count == 1 ? 0 : wholeCircle ? (2 * Math.PI * i / count) - Math.PI : radians * (((double)i / (count - 1)) - 0.5);
                var (sin, cos) = Math.SinCos(angle);
                rotations[offset + i] = (cos, sin);
            }
        }

        return rotations;
    }

    /// <summary>Adds the bilinear interpolation of the working samples at a point (stored-pixel coordinates) to <paramref name="sums"/>.</summary>
    private static void AddBilinearSample(WorkingFrameCopy copy, double x, double y, int channels, ref Sums sums)
    {
        // Pixel centers are at integer coordinates once half a pixel is removed. The coordinates are bounded so that the
        // integer parts fit, whatever the center; the edge mode maps them back
        const double Bound = 1 << 30;
        var u = Math.Clamp(x - 0.5, -Bound, Bound);
        var v = Math.Clamp(y - 0.5, -Bound, Bound);
        var x0 = Math.Floor(u);
        var y0 = Math.Floor(v);
        var fx = u - x0;
        var fy = v - y0;
        var column = (int)x0;
        var row = (int)y0;
        var size = copy.Size;
        if (column >= 0 && column + 1 < size.Width && row >= 0 && row + 1 < size.Height)
        {
            // Inside the frame: the four pixels, without the edge mode (a weight of 0 skips a pixel that is not a number)
            var top = copy.GetRow(row);
            var bottom = copy.GetRow(row + 1);
            var offset = column * channels;
            AddPixel(top, offset, (1 - fx) * (1 - fy), channels, ref sums);
            AddPixel(top, offset + channels, fx * (1 - fy), channels, ref sums);
            AddPixel(bottom, offset, (1 - fx) * fy, channels, ref sums);
            AddPixel(bottom, offset + channels, fx * fy, channels, ref sums);
            return;
        }

        AddRow(copy, row, column, fx, 1 - fy, channels, ref sums);
        if (fy != 0)
        {
            AddRow(copy, row + 1, column, fx, fy, channels, ref sums);
        }
    }

    /// <summary>Sets a pixel of 4 working samples to the mean of its samples (<paramref name="rotations"/> of a spin, or <paramref name="scales"/> of a zoom), summed in two vectors of 2 lanes.</summary>
    private static void BlurPixelVector128(WorkingFrameCopy copy, (double Cos, double Sin)[]? rotations, double[]? scales, int count, double centerX, double centerY, double dx, double dy, Span<double> pixel)
    {
        var sums = default(VectorSums);
        var offset = count * (count - 1) / 2;
        if (rotations is not null)
        {
            for (var i = 0; i < count; i++)
            {
                var (cos, sin) = rotations[offset + i];
                sums = AddBilinearSample(copy, centerX + (dx * cos) - (dy * sin), centerY + (dx * sin) + (dy * cos), sums);
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var scale = scales![offset + i];
                sums = AddBilinearSample(copy, centerX + (dx * scale), centerY + (dy * scale), sums);
            }
        }

        var divisor = Vector128.Create((double)count);
        unsafe
        {
            ref var first = ref MemoryMarshal.GetReference(pixel);
            Vector128.StoreUnsafe(sums.Low / divisor, ref first);
            Vector128.StoreUnsafe(sums.High / divisor, ref first, 2);
        }
    }

    /// <summary>Sets a pixel of 4 working samples to the mean of its samples, summed in the 4 lanes of one vector.</summary>
    private static void BlurPixelVector256(WorkingFrameCopy copy, (double Cos, double Sin)[]? rotations, double[]? scales, int count, double centerX, double centerY, double dx, double dy, Span<double> pixel)
    {
        var sums = Vector256<double>.Zero;
        var offset = count * (count - 1) / 2;
        if (rotations is not null)
        {
            for (var i = 0; i < count; i++)
            {
                var (cos, sin) = rotations[offset + i];
                sums = AddBilinearSample(copy, centerX + (dx * cos) - (dy * sin), centerY + (dx * sin) + (dy * cos), sums);
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var scale = scales![offset + i];
                sums = AddBilinearSample(copy, centerX + (dx * scale), centerY + (dy * scale), sums);
            }
        }

        unsafe { Vector256.StoreUnsafe(sums / Vector256.Create((double)count), ref MemoryMarshal.GetReference(pixel)); }
    }

    /// <summary>Adds the bilinear interpolation of the 4 working samples at a point to the lanes of <paramref name="sums"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> AddBilinearSample(WorkingFrameCopy copy, double x, double y, Vector256<double> sums)
    {
        const double Bound = 1 << 30;
        var u = Math.Clamp(x - 0.5, -Bound, Bound);
        var v = Math.Clamp(y - 0.5, -Bound, Bound);
        var x0 = Math.Floor(u);
        var y0 = Math.Floor(v);
        var fx = u - x0;
        var fy = v - y0;
        var column = (int)x0;
        var row = (int)y0;
        var size = copy.Size;
        if (column >= 0 && column + 1 < size.Width && row >= 0 && row + 1 < size.Height)
        {
            var top = copy.GetRow(row);
            var bottom = copy.GetRow(row + 1);
            var offset = column * 4;
            sums = AddPixel(top, offset, (1 - fx) * (1 - fy), sums);
            sums = AddPixel(top, offset + 4, fx * (1 - fy), sums);
            sums = AddPixel(bottom, offset, (1 - fx) * fy, sums);
            return AddPixel(bottom, offset + 4, fx * fy, sums);
        }

        var pair = AddOutsideSample(copy, row, column, fx, fy, new VectorSums { Low = sums.GetLower(), High = sums.GetUpper() });
        return Vector256.Create(pair.Low, pair.High);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> AddPixel(ReadOnlySpan<float> samples, int offset, double weight, Vector256<double> sums)
    {
        if (weight == 0)
            return sums;

        var pixel = unsafe(Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(samples.Slice(offset, 4))));
        return sums + (Vector256.Create(weight) * Avx.ConvertToVector256Double(pixel));
    }

    /// <summary>Adds the bilinear interpolation of the 4 working samples at a point to <paramref name="sums"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VectorSums AddBilinearSample(WorkingFrameCopy copy, double x, double y, VectorSums sums)
    {
        const double Bound = 1 << 30;
        var u = Math.Clamp(x - 0.5, -Bound, Bound);
        var v = Math.Clamp(y - 0.5, -Bound, Bound);
        var x0 = Math.Floor(u);
        var y0 = Math.Floor(v);
        var fx = u - x0;
        var fy = v - y0;
        var column = (int)x0;
        var row = (int)y0;
        var size = copy.Size;
        if (column >= 0 && column + 1 < size.Width && row >= 0 && row + 1 < size.Height)
        {
            var top = copy.GetRow(row);
            var bottom = copy.GetRow(row + 1);
            var offset = column * 4;
            sums = AddPixel(top, offset, (1 - fx) * (1 - fy), sums);
            sums = AddPixel(top, offset + 4, fx * (1 - fy), sums);
            sums = AddPixel(bottom, offset, (1 - fx) * fy, sums);
            return AddPixel(bottom, offset + 4, fx * fy, sums);
        }

        return AddOutsideSample(copy, row, column, fx, fy, sums);
    }

    /// <summary>Adds a sample that reads pixels beyond the edges with the scalar sums, which hold the sums of the lanes in the meantime.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static VectorSums AddOutsideSample(WorkingFrameCopy copy, int row, int column, double fx, double fy, VectorSums vectorSums)
    {
        var sums = new Sums { S0 = vectorSums.Low.GetElement(0), S1 = vectorSums.Low.GetElement(1), S2 = vectorSums.High.GetElement(0), S3 = vectorSums.High.GetElement(1) };
        AddRow(copy, row, column, fx, 1 - fy, channels: 4, ref sums);
        if (fy != 0)
        {
            AddRow(copy, row + 1, column, fx, fy, channels: 4, ref sums);
        }

        return new VectorSums { Low = Vector128.Create(sums.S0, sums.S1), High = Vector128.Create(sums.S2, sums.S3) };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VectorSums AddPixel(ReadOnlySpan<float> samples, int offset, double weight, VectorSums sums)
    {
        if (weight == 0)
            return sums;

        var pixel = unsafe(Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(samples.Slice(offset, 4))));
        var factor = Vector128.Create(weight);
        return new VectorSums { Low = sums.Low + (factor * Vector128.WidenLower(pixel)), High = sums.High + (factor * Vector128.WidenUpper(pixel)) };
    }

    private static void AddRow(WorkingFrameCopy copy, int row, int column, double fx, double rowWeight, int channels, ref Sums sums)
    {
        var source = copy.MapRow(row);
        if (source < 0)
            return;

        var samples = copy.GetRow(source);
        AddMappedPixel(copy, samples, column, rowWeight * (1 - fx), channels, ref sums);
        if (fx != 0)
        {
            AddMappedPixel(copy, samples, column + 1, rowWeight * fx, channels, ref sums);
        }
    }

    private static void AddMappedPixel(WorkingFrameCopy copy, ReadOnlySpan<float> samples, int column, double weight, int channels, ref Sums sums)
    {
        var source = (uint)column < (uint)copy.Size.Width ? column : copy.MapColumn(column);
        if (source >= 0)
        {
            AddPixel(samples, source * channels, weight, channels, ref sums);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddPixel(ReadOnlySpan<float> samples, int offset, double weight, int channels, ref Sums sums)
    {
        if (weight == 0)
            return;

        sums.S0 += weight * samples[offset];
        if (channels > 1)
        {
            sums.S1 += weight * samples[offset + 1];
            sums.S2 += weight * samples[offset + 2];
            if (channels == 4)
            {
                sums.S3 += weight * samples[offset + 3];
            }
        }
    }

    /// <summary>The sums of the 4 working samples of a pixel: samples 0 and 1 in the lanes of <see cref="Low"/>, 2 and 3 in those of <see cref="High"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct VectorSums
    {
        public Vector128<double> Low;
        public Vector128<double> High;
    }

    /// <summary>The sums of the samples of a pixel, one per working sample (1, 3 or 4).</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Sums
    {
        public double S0;
        public double S1;
        public double S2;
        public double S3;
    }
}
