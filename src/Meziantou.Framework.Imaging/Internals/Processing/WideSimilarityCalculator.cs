using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Computes the SSIM and the PSNR of two frames at 16-bit precision or on fractions in double precision
/// (<see cref="ImageSimilarityOptions"/>), with the definitions of the 8-bit comparisons (<see cref="SsimAccumulator"/>,
/// <see cref="PsnrCalculator"/>) and another range.
/// </summary>
/// <remarks>
/// <para>
/// The colors are premultiplied by alpha (16-bit samples rounded to the nearest, fractions exactly), and the alpha channel
/// is scored on its own with the range of alpha; the result is the lower of the color score and the alpha score.
/// </para>
/// <para>
/// SSIM: the window statistics are sums of the values of the rows of the window, added in the order of the rows, then of
/// the columns, without running sums, so a window only adds its own values. 16-bit sums are integers below 2^53, so every
/// statistic is exact, as for 8 bits. The scores of the windows are added by bands of <see cref="RowBands.SumBandRows"/>
/// rows of windows, and the band sums in order; PSNR adds the errors of bands of the same number of rows (exactly as
/// integers for 16 bits), so neither depends on the number of workers.
/// </para>
/// </remarks>
internal static class WideSimilarityCalculator
{
    private const int ChannelCount = 4;
    private const int AlphaChannel = 3;
    private const int StatisticCount = 5;

    /// <summary>Computes the mean SSIM of two leased frames of the same size.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample cannot be compared at the precision.</exception>
    public static double ComputeSsim(AllocationScope scope, PixelStorage expected, PixelFormat expectedFormat, PixelStorage actual, PixelFormat actualFormat, ImageSimilarityOptions options, int maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        var width = expected.Width;
        var height = expected.Height;
        var windowWidth = Math.Min(SsimAccumulator.WindowSize, width);
        var windowHeight = Math.Min(SsimAccumulator.WindowSize, height);
        var windowRows = height - windowHeight + 1;
        var bands = (windowRows + RowBands.SumBandRows - 1) / RowBands.SumBandRows;
        var workers = Math.Min(RowBands.GetWorkerCount(new Size(width, height), maxDegreeOfParallelism), bands);
        var bandSums = new double[bands * ChannelCount];
        var precision = options.Precision;
        var colorRange = WideRowReader.GetColorRange(precision, options.DataRange);
        var alphaRange = WideRowReader.GetAlphaRange(precision);
        using var readers = new Readers(scope, expectedFormat, actualFormat, width, precision, workers);

        // Per worker: the ring of the rows of a window of both frames, and the statistics of the columns
        var ringLength = windowHeight * width * ChannelCount;
        var buffers = new PooledBuffer?[workers];
        try
        {
            for (var worker = 0; worker < workers; worker++)
            {
                buffers[worker] = ResampleWeights.Rent<double>(scope, (2L * ringLength) + ((long)width * ChannelCount * StatisticCount));
            }

            if (workers is 1)
            {
                Run(0);
            }
            else
            {
                RowBands.RunParallelInBandOrder(workers, Run);
            }
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }

        Span<double> sums = stackalloc double[ChannelCount];
        for (var i = 0; i < bandSums.Length; i += ChannelCount)
        {
            for (var channel = 0; channel < ChannelCount; channel++)
            {
                sums[channel] += bandSums[i + channel];
            }
        }

        var windowCount = (long)windowRows * (width - windowWidth + 1);
        var colorSsim = (sums[0] / windowCount + sums[1] / windowCount + sums[2] / windowCount) / 3;
        return Math.Min(colorSsim, sums[AlphaChannel] / windowCount);

        void Run(int worker)
        {
            var buffer = unsafe(MemoryMarshal.Cast<byte, double>(buffers[worker]!.Span));
            var expectedRing = buffer[..ringLength];
            var actualRing = buffer.Slice(ringLength, ringLength);
            var columns = buffer.Slice(2 * ringLength, width * ChannelCount * StatisticCount);
            var first = RowBands.GetBandStart(bands, worker, workers) * RowBands.SumBandRows;
            var end = Math.Min(windowRows, RowBands.GetBandStart(bands, worker + 1, workers) * RowBands.SumBandRows);
            var rowLength = width * ChannelCount;
            for (var y = first; y < end + windowHeight - 1; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = y % windowHeight;
                Premultiply(readers.GetExpectedRow(worker, expected, y), expectedRing.Slice(slot * rowLength, rowLength), precision);
                Premultiply(readers.GetActualRow(worker, actual, y), actualRing.Slice(slot * rowLength, rowLength), precision);
                var windowRow = y - windowHeight + 1;
                if (windowRow < first)
                    continue;

                ComputeColumnStatistics(expectedRing, actualRing, columns, windowRow, windowHeight, rowLength);
                AccumulateWindows(columns, width, windowWidth, windowHeight, colorRange, alphaRange, bandSums.AsSpan(windowRow / RowBands.SumBandRows * ChannelCount, ChannelCount));
            }
        }
    }

    /// <summary>Computes the PSNR of two leased frames of the same size.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample cannot be compared at the precision.</exception>
    public static double ComputePsnr(AllocationScope scope, PixelStorage expected, PixelFormat expectedFormat, PixelStorage actual, PixelFormat actualFormat, ImageSimilarityOptions options, int maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        var width = expected.Width;
        var height = expected.Height;
        var bands = (height + RowBands.SumBandRows - 1) / RowBands.SumBandRows;
        var workers = Math.Min(RowBands.GetWorkerCount(new Size(width, height), maxDegreeOfParallelism), bands);
        var precision = options.Precision;
        var exact = precision == SimilarityPrecision.SixteenBit;
        var colorErrors = new double[bands];
        var alphaErrors = new double[bands];
        var colorIntegers = new UInt128[bands];
        var alphaIntegers = new UInt128[bands];
        using var readers = new Readers(scope, expectedFormat, actualFormat, width, precision, workers);
        if (workers is 1)
        {
            Run(0);
        }
        else
        {
            RowBands.RunParallelInBandOrder(workers, Run);
        }

        double colorError;
        double alphaError;
        if (exact)
        {
            UInt128 color = 0;
            UInt128 alpha = 0;
            for (var band = 0; band < bands; band++)
            {
                color += colorIntegers[band];
                alpha += alphaIntegers[band];
            }

            colorError = (double)color;
            alphaError = (double)alpha;
        }
        else
        {
            colorError = 0;
            alphaError = 0;
            for (var band = 0; band < bands; band++)
            {
                colorError += colorErrors[band];
                alphaError += alphaErrors[band];
            }
        }

        var pixelCount = (double)width * height;
        var colorRange = WideRowReader.GetColorRange(precision, options.DataRange);
        var alphaRange = WideRowReader.GetAlphaRange(precision);
        return Math.Min(ToPsnr(colorError / (3 * pixelCount), colorRange), ToPsnr(alphaError / pixelCount, alphaRange));

        void Run(int worker)
        {
            for (var band = RowBands.GetBandStart(bands, worker, workers); band < RowBands.GetBandStart(bands, worker + 1, workers); band++)
            {
                UInt128 colorInteger = 0;
                UInt128 alphaInteger = 0;
                var color = 0d;
                var alpha = 0d;
                for (var y = band * RowBands.SumBandRows; y < Math.Min(height, (band + 1) * RowBands.SumBandRows); y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var expectedRow = readers.GetExpectedRow(worker, expected, y);
                    var actualRow = readers.GetActualRow(worker, actual, y);
                    for (var x = 0; x < expectedRow.Length; x += ChannelCount)
                    {
                        var expectedAlpha = expectedRow[x + AlphaChannel];
                        var actualAlpha = actualRow[x + AlphaChannel];
                        for (var c = 0; c < AlphaChannel; c++)
                        {
                            var difference = WideRowReader.Premultiply(expectedRow[x + c], expectedAlpha, precision) - WideRowReader.Premultiply(actualRow[x + c], actualAlpha, precision);
                            if (exact)
                            {
                                colorInteger += (ulong)(difference * difference);
                            }
                            else
                            {
                                color += difference * difference;
                            }
                        }

                        var alphaDifference = expectedAlpha - actualAlpha;
                        if (exact)
                        {
                            alphaInteger += (ulong)(alphaDifference * alphaDifference);
                        }
                        else
                        {
                            alpha += alphaDifference * alphaDifference;
                        }
                    }
                }

                colorIntegers[band] = colorInteger;
                alphaIntegers[band] = alphaInteger;
                colorErrors[band] = color;
                alphaErrors[band] = alpha;
            }
        }
    }

    private static double ToPsnr(double meanSquaredError, double peak) => meanSquaredError is 0 ? double.PositiveInfinity : 10 * Math.Log10(peak * peak / meanSquaredError);

    private static void Premultiply(ReadOnlySpan<double> row, Span<double> destination, SimilarityPrecision precision)
    {
        for (var i = 0; i < row.Length; i += ChannelCount)
        {
            var alpha = row[i + AlphaChannel];
            destination[i] = WideRowReader.Premultiply(row[i], alpha, precision);
            destination[i + 1] = WideRowReader.Premultiply(row[i + 1], alpha, precision);
            destination[i + 2] = WideRowReader.Premultiply(row[i + 2], alpha, precision);
            destination[i + AlphaChannel] = alpha;
        }
    }

    /// <summary>
    /// Sets the statistics of every column of the window rows that start at <paramref name="windowRow"/>, adding the rows in
    /// order: per pixel, <see cref="StatisticCount"/> groups of the <see cref="ChannelCount"/> channels. The vectors compute
    /// the four channels of a pixel.
    /// </summary>
    internal static void ComputeColumnStatistics(ReadOnlySpan<double> expectedRing, ReadOnlySpan<double> actualRing, Span<double> columns, int windowRow, int windowHeight, int rowLength)
    {
        var width = rowLength / ChannelCount;
        var x = 0;
        if (Vector128.IsHardwareAccelerated && expectedRing.Length >= windowHeight * rowLength && actualRing.Length >= windowHeight * rowLength && columns.Length >= rowLength * StatisticCount)
        {
            ref var expected0 = ref unsafe(MemoryMarshal.GetReference(expectedRing));
            ref var actual0 = ref unsafe(MemoryMarshal.GetReference(actualRing));
            ref var target = ref unsafe(MemoryMarshal.GetReference(columns));
            if (Vector256.IsHardwareAccelerated)
            {
                for (; x < width; x++)
                {
                    var sumExpected = Vector256<double>.Zero;
                    var sumActual = Vector256<double>.Zero;
                    var sumExpected2 = Vector256<double>.Zero;
                    var sumActual2 = Vector256<double>.Zero;
                    var sumProduct = Vector256<double>.Zero;
                    for (var row = windowRow; row < windowRow + windowHeight; row++)
                    {
                        var offset = (nuint)((row % windowHeight * rowLength) + (x * ChannelCount));
                        var e = unsafe(Vector256.LoadUnsafe(ref expected0, offset));
                        var a = unsafe(Vector256.LoadUnsafe(ref actual0, offset));
                        sumExpected += e;
                        sumActual += a;
                        sumExpected2 += e * e;
                        sumActual2 += a * a;
                        sumProduct += e * a;
                    }

                    var column = (nuint)(x * ChannelCount * StatisticCount);
                    unsafe
                    {
                        sumExpected.StoreUnsafe(ref target, column);
                        sumActual.StoreUnsafe(ref target, column + ChannelCount);
                        sumExpected2.StoreUnsafe(ref target, column + (2 * ChannelCount));
                        sumActual2.StoreUnsafe(ref target, column + (3 * ChannelCount));
                        sumProduct.StoreUnsafe(ref target, column + (4 * ChannelCount));
                    }
                }
            }
            else
            {
                // Half a pixel (two channels) at a time
                for (var half = 0; half < 2 * width; half++)
                {
                    var sumExpected = Vector128<double>.Zero;
                    var sumActual = Vector128<double>.Zero;
                    var sumExpected2 = Vector128<double>.Zero;
                    var sumActual2 = Vector128<double>.Zero;
                    var sumProduct = Vector128<double>.Zero;
                    for (var row = windowRow; row < windowRow + windowHeight; row++)
                    {
                        var offset = (nuint)((row % windowHeight * rowLength) + (half * 2));
                        var e = unsafe(Vector128.LoadUnsafe(ref expected0, offset));
                        var a = unsafe(Vector128.LoadUnsafe(ref actual0, offset));
                        sumExpected += e;
                        sumActual += a;
                        sumExpected2 += e * e;
                        sumActual2 += a * a;
                        sumProduct += e * a;
                    }

                    var column = (nuint)(((half >> 1) * ChannelCount * StatisticCount) + ((half & 1) * 2));
                    unsafe
                    {
                        sumExpected.StoreUnsafe(ref target, column);
                        sumActual.StoreUnsafe(ref target, column + ChannelCount);
                        sumExpected2.StoreUnsafe(ref target, column + (2 * ChannelCount));
                        sumActual2.StoreUnsafe(ref target, column + (3 * ChannelCount));
                        sumProduct.StoreUnsafe(ref target, column + (4 * ChannelCount));
                    }
                }

                x = width;
            }
        }

        ComputeColumnStatisticsScalar(expectedRing, actualRing, columns, windowRow, windowHeight, rowLength, x);
    }

    /// <summary>The scalar reference of <see cref="ComputeColumnStatistics"/>, from pixel <paramref name="start"/>.</summary>
    internal static void ComputeColumnStatisticsScalar(ReadOnlySpan<double> expectedRing, ReadOnlySpan<double> actualRing, Span<double> columns, int windowRow, int windowHeight, int rowLength, int start = 0)
    {
        columns[(start * ChannelCount * StatisticCount)..(rowLength * StatisticCount)].Clear();
        for (var row = windowRow; row < windowRow + windowHeight; row++)
        {
            var slot = row % windowHeight;
            var expected = expectedRing.Slice(slot * rowLength, rowLength);
            var actual = actualRing.Slice(slot * rowLength, rowLength);
            for (var i = start * ChannelCount; i < rowLength; i++)
            {
                var e = expected[i];
                var a = actual[i];
                var column = columns.Slice((i / ChannelCount * StatisticCount * ChannelCount) + (i % ChannelCount));
                column[0] += e;
                column[ChannelCount] += a;
                column[2 * ChannelCount] += e * e;
                column[3 * ChannelCount] += a * a;
                column[4 * ChannelCount] += e * a;
            }
        }
    }

    /// <summary>
    /// Adds the scores of the windows of a window row to <paramref name="sums"/>, per channel, in the order of the windows.
    /// The vectors compute the four channels of a window.
    /// </summary>
    internal static void AccumulateWindows(ReadOnlySpan<double> columns, int width, int windowWidth, int windowHeight, double colorRange, double alphaRange, Span<double> sums)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && columns.Length >= width * ChannelCount * StatisticCount && sums.Length >= ChannelCount)
        {
            // The expression of the 8-bit accumulator: the first factors multiplied by count², the second ones by count × (count - 1)
            double count = windowWidth * windowHeight;
            var varianceScale = count > 1 ? count * (count - 1) : 1;
            var colorC1 = (0.01 * colorRange) * (0.01 * colorRange) * count * count;
            var colorC2 = (0.03 * colorRange) * (0.03 * colorRange) * varianceScale;
            var alphaC1 = (0.01 * alphaRange) * (0.01 * alphaRange) * count * count;
            var alphaC2 = (0.03 * alphaRange) * (0.03 * alphaRange) * varianceScale;
            ref var column0 = ref unsafe(MemoryMarshal.GetReference(columns));
            if (Vector256.IsHardwareAccelerated)
            {
                var c1 = Vector256.Create(colorC1, colorC1, colorC1, alphaC1);
                var c2 = Vector256.Create(colorC2, colorC2, colorC2, alphaC2);
                var countVector = Vector256.Create(count);
                var two = Vector256.Create(2.0);
                var total = Vector256.Create(sums[0], sums[1], sums[2], sums[3]);
                for (; x + windowWidth <= width; x++)
                {
                    var sumExpected = Vector256<double>.Zero;
                    var sumActual = Vector256<double>.Zero;
                    var sumExpected2 = Vector256<double>.Zero;
                    var sumActual2 = Vector256<double>.Zero;
                    var sumProduct = Vector256<double>.Zero;
                    for (var column = x; column < x + windowWidth; column++)
                    {
                        var offset = (nuint)(column * ChannelCount * StatisticCount);
                        unsafe
                        {
                            sumExpected += Vector256.LoadUnsafe(ref column0, offset);
                            sumActual += Vector256.LoadUnsafe(ref column0, offset + ChannelCount);
                            sumExpected2 += Vector256.LoadUnsafe(ref column0, offset + (2 * ChannelCount));
                            sumActual2 += Vector256.LoadUnsafe(ref column0, offset + (3 * ChannelCount));
                            sumProduct += Vector256.LoadUnsafe(ref column0, offset + (4 * ChannelCount));
                        }
                    }

                    var meanProduct = sumExpected * sumActual;
                    var meanSquares = (sumExpected * sumExpected) + (sumActual * sumActual);
                    var covariance = (countVector * sumProduct) - meanProduct;
                    var variances = (countVector * (sumExpected2 + sumActual2)) - meanSquares;
                    total += (two * meanProduct + c1) * (two * covariance + c2) / ((meanSquares + c1) * (variances + c2));
                }

                sums[0] = total.GetElement(0);
                sums[1] = total.GetElement(1);
                sums[2] = total.GetElement(2);
                sums[3] = total.GetElement(3);
            }
            else
            {
                var c1Color = Vector128.Create(colorC1);
                var c2Color = Vector128.Create(colorC2);
                var c1Alpha = Vector128.Create(colorC1, alphaC1);
                var c2Alpha = Vector128.Create(colorC2, alphaC2);
                var countVector = Vector128.Create(count);
                var two = Vector128.Create(2.0);
                var total01 = Vector128.Create(sums[0], sums[1]);
                var total23 = Vector128.Create(sums[2], sums[3]);
                for (; x + windowWidth <= width; x++)
                {
                    total01 += Score(ref column0, x, 0, windowWidth, countVector, two, c1Color, c2Color);
                    total23 += Score(ref column0, x, 2, windowWidth, countVector, two, c1Alpha, c2Alpha);
                }

                sums[0] = total01.GetElement(0);
                sums[1] = total01.GetElement(1);
                sums[2] = total23.GetElement(0);
                sums[3] = total23.GetElement(1);
            }
        }

        AccumulateWindowsScalar(columns, width, windowWidth, windowHeight, colorRange, alphaRange, sums, x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<double> Score(ref double column0, int x, int channel, int windowWidth, Vector128<double> countVector, Vector128<double> two, Vector128<double> c1, Vector128<double> c2)
        {
            var sumExpected = Vector128<double>.Zero;
            var sumActual = Vector128<double>.Zero;
            var sumExpected2 = Vector128<double>.Zero;
            var sumActual2 = Vector128<double>.Zero;
            var sumProduct = Vector128<double>.Zero;
            for (var column = x; column < x + windowWidth; column++)
            {
                var offset = (nuint)((column * ChannelCount * StatisticCount) + channel);
                unsafe
                {
                    sumExpected += Vector128.LoadUnsafe(ref column0, offset);
                    sumActual += Vector128.LoadUnsafe(ref column0, offset + ChannelCount);
                    sumExpected2 += Vector128.LoadUnsafe(ref column0, offset + (2 * ChannelCount));
                    sumActual2 += Vector128.LoadUnsafe(ref column0, offset + (3 * ChannelCount));
                    sumProduct += Vector128.LoadUnsafe(ref column0, offset + (4 * ChannelCount));
                }
            }

            var meanProduct = sumExpected * sumActual;
            var meanSquares = (sumExpected * sumExpected) + (sumActual * sumActual);
            var covariance = (countVector * sumProduct) - meanProduct;
            var variances = (countVector * (sumExpected2 + sumActual2)) - meanSquares;
            return (two * meanProduct + c1) * (two * covariance + c2) / ((meanSquares + c1) * (variances + c2));
        }
    }

    /// <summary>The scalar reference of <see cref="AccumulateWindows"/>, from the window <paramref name="start"/>.</summary>
    internal static void AccumulateWindowsScalar(ReadOnlySpan<double> columns, int width, int windowWidth, int windowHeight, double colorRange, double alphaRange, Span<double> sums, int start = 0)
    {
        // The expression of the 8-bit accumulator: the first factors multiplied by count², the second ones by count × (count - 1)
        double count = windowWidth * windowHeight;
        var varianceScale = count > 1 ? count * (count - 1) : 1;
        Span<double> window = stackalloc double[StatisticCount];
        for (var x = start; x + windowWidth <= width; x++)
        {
            for (var channel = 0; channel < ChannelCount; channel++)
            {
                window.Clear();
                for (var column = x; column < x + windowWidth; column++)
                {
                    for (var i = 0; i < StatisticCount; i++)
                    {
                        window[i] += columns[(((column * StatisticCount) + i) * ChannelCount) + channel];
                    }
                }

                var range = channel == AlphaChannel ? alphaRange : colorRange;
                var c1 = (0.01 * range) * (0.01 * range) * count * count;
                var c2 = (0.03 * range) * (0.03 * range) * varianceScale;
                var meanProduct = window[0] * window[1];
                var meanSquares = (window[0] * window[0]) + (window[1] * window[1]);
                var covariance = (count * window[4]) - meanProduct;
                var variances = (count * (window[2] + window[3])) - meanSquares;
                sums[channel] += (2 * meanProduct + c1) * (2 * covariance + c2) / ((meanSquares + c1) * (variances + c2));
            }
        }
    }

    /// <summary>The row readers of both frames, one pair per worker, rented on the calling thread.</summary>
    private sealed class Readers : IDisposable
    {
        private readonly WideRowReader?[] _expected;
        private readonly WideRowReader?[] _actual;

        public Readers(AllocationScope scope, PixelFormat expectedFormat, PixelFormat actualFormat, int width, SimilarityPrecision precision, int workers)
        {
            _expected = new WideRowReader?[workers];
            _actual = new WideRowReader?[workers];
            try
            {
                for (var worker = 0; worker < workers; worker++)
                {
                    _expected[worker] = WideRowReader.Create(scope, expectedFormat, width, precision);
                    _actual[worker] = WideRowReader.Create(scope, actualFormat, width, precision);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public ReadOnlySpan<double> GetExpectedRow(int worker, PixelStorage storage, int y) => _expected[worker]!.GetRow(storage, y);

        public ReadOnlySpan<double> GetActualRow(int worker, PixelStorage storage, int y) => _actual[worker]!.GetRow(storage, y);

        public void Dispose()
        {
            for (var worker = 0; worker < _expected.Length; worker++)
            {
                _expected[worker]?.Dispose();
                _expected[worker] = null;
                _actual[worker]?.Dispose();
                _actual[worker] = null;
            }
        }
    }
}
