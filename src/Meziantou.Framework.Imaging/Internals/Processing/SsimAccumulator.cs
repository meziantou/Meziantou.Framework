using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Computes the mean Structural Similarity Index (SSIM) of two images of the same size, fed one row at a time
/// (<see cref="ImageSimilarity.ComputeSsim(ImageFrame, ImageFrame, CancellationToken)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The SSIM is computed in every <see cref="WindowSize"/>×<see cref="WindowSize"/> window that fits inside the image, moved
/// one pixel at a time, with uniform weights, the sample (N − 1) variance and covariance, and <c>K1 = 0.01</c>,
/// <c>K2 = 0.03</c>, <c>L = 255</c>. Each channel scores the mean over all windows, which is what scikit-image's
/// <c>structural_similarity(expected, actual, data_range=255, channel_axis=-1)</c> computes with its default parameters.
/// An image narrower or shorter than the window uses a window as wide or as tall as the image.
/// </para>
/// <para>
/// Transparency is taken into account in two ways. The color channels are premultiplied by the alpha channel, so a fully
/// transparent pixel scores the same whatever color it hides, and a color over a different opacity does not look the
/// same. The alpha channel is scored on its own, and the result is the lower of the mean color score and the alpha score.
/// For images without transparency, the alpha score is exactly <c>1</c> and the result is the usual mean SSIM of the three
/// color channels.
/// </para>
/// <para>
/// The window statistics are integer sums maintained incrementally, so the cost is linear in the number of pixels, and
/// every window score is computed from exact values in a fixed order. The result depends only on the pixels, not on the
/// hardware. The ring of the last <see cref="WindowSize"/> rows of both images and the statistics of every column are
/// rented in one buffer from the scope.
/// </para>
/// <para>
/// The scores of the windows are added by bands of <see cref="RowBands.SumBandRows"/> rows of windows, and the sums of the
/// bands are added in order. <see cref="Compute"/> splits the bands between up to
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers, each with an accumulator that starts at the first row of
/// its first window, so the result does not depend on the number of workers.
/// </para>
/// </remarks>
internal sealed class SsimAccumulator : IDisposable
{
    /// <summary>The width and height of the window in which the local statistics are computed.</summary>
    public const int WindowSize = 7;

    private const int ChannelCount = 4;
    private const int AlphaChannel = 3;

    // Per column: Σx, Σy, Σx², Σy², Σxy, each of the four channels
    private const int StatisticCount = 5;
    private const int ValuesPerColumn = ChannelCount * StatisticCount;

    private const double C1 = (0.01 * 255) * (0.01 * 255);
    private const double C2 = (0.03 * 255) * (0.03 * 255);

    private readonly int _width;
    private readonly int _height;
    private readonly int _windowWidth;
    private readonly int _windowHeight;
    private readonly int _ringLength;
    private readonly int _firstWindowRow;
    private readonly int _rowLimit;
    private readonly double[] _bandSums;
    private PooledBuffer? _buffer;
    private int _rowCount;

    /// <summary>Rents the row ring and the column statistics of two images of the given size.</summary>
    /// <param name="scope">The scope charged for the buffer.</param>
    /// <param name="width">The width of the images.</param>
    /// <param name="height">The height of the images.</param>
    /// <exception cref="ImageResourceLimitException">The buffer exceeds the allocation limit.</exception>
    public SsimAccumulator(AllocationScope scope, int width, int height)
        : this(scope, width, height, firstWindowRow: 0, windowRowEnd: GetWindowRowCount(height), bandSums: null)
    {
    }

    /// <summary>
    /// Rents the row ring and the column statistics of an accumulator of the windows whose first row is in
    /// <c>[firstWindowRow, windowRowEnd)</c>: it is given the rows from <paramref name="firstWindowRow"/> to the last row of
    /// the last window, and adds the scores of the windows to the sums of their bands in <paramref name="bandSums"/>.
    /// </summary>
    private SsimAccumulator(AllocationScope scope, int width, int height, int firstWindowRow, int windowRowEnd, double[]? bandSums)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _width = width;
        _height = height;
        _windowWidth = Math.Min(WindowSize, width);
        _windowHeight = Math.Min(WindowSize, height);
        _firstWindowRow = firstWindowRow;
        _rowLimit = windowRowEnd - firstWindowRow + _windowHeight - 1;
        _bandSums = bandSums ?? new double[GetBandCount(height) * ChannelCount];

        // The ring holds bytes and the statistics ints; the ring length is a multiple of 4, which keeps the ints aligned.
        // Each window statistic is at most 7 × 7 × 255 × 255 = 3 186 225, so int is enough.
        _ringLength = _windowHeight * width * ChannelCount;
        _buffer = ResampleWeights.Rent<byte>(scope, (2L * _ringLength) + ((long)width * ValuesPerColumn * sizeof(int)));
    }

    private Span<byte> Buffer => (_buffer ?? throw new ObjectDisposedException(nameof(SsimAccumulator))).Span;

    private Span<byte> ExpectedRows => Buffer[.._ringLength];

    private Span<byte> ActualRows => Buffer.Slice(_ringLength, _ringLength);

    private Span<int> ColumnStatistics => unsafe(MemoryMarshal.Cast<byte, int>(Buffer[(2 * _ringLength)..]))[..(_width * ValuesPerColumn)];

    /// <summary>Adds the next row of both images. Both spans must contain exactly one row of pixels.</summary>
    public void AddRow(ReadOnlySpan<Rgba32> expectedRow, ReadOnlySpan<Rgba32> actualRow)
    {
        if (expectedRow.Length != _width)
            throw new ArgumentException("The row does not have the width of the image.", nameof(expectedRow));

        if (actualRow.Length != _width)
            throw new ArgumentException("The row does not have the width of the image.", nameof(actualRow));

        if (_rowCount == _rowLimit)
            throw new InvalidOperationException("All the rows of the image have already been added.");

        var rowLength = _width * ChannelCount;
        var slot = _rowCount % _windowHeight;
        var expectedSlot = ExpectedRows.Slice(slot * rowLength, rowLength);
        var actualSlot = ActualRows.Slice(slot * rowLength, rowLength);

        // The slot holds the row that is leaving the window
        if (_rowCount >= _windowHeight)
        {
            UpdateColumnStatistics(expectedSlot, actualSlot, ColumnStatistics, sign: -1);
        }

        StoreChannelValues(expectedRow, expectedSlot);
        StoreChannelValues(actualRow, actualSlot);
        UpdateColumnStatistics(expectedSlot, actualSlot, ColumnStatistics, sign: 1);
        _rowCount++;

        if (_rowCount >= _windowHeight)
        {
            AccumulateWindowRow();
        }
    }

    /// <summary>
    /// Computes the score of the images once all their rows have been added. Values range from <c>-1.0</c> to <c>1.0</c>
    /// (identical); unrelated images score around <c>0.0</c>.
    /// </summary>
    public double ComputeMeanSsim()
    {
        if (_rowCount != _height)
            throw new InvalidOperationException("Not all the rows of the image have been added.");

        return ComputeMeanSsim(_bandSums, _width, _height);
    }

    /// <summary>
    /// Computes the score of two frames of the same size, leased by the caller, with up to
    /// <paramref name="maxDegreeOfParallelism"/> workers.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static double Compute(AllocationScope scope, PixelStorage expected, PixelFormat expectedFormat, PixelStorage actual, PixelFormat actualFormat, int maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        var width = expected.Width;
        var height = expected.Height;
        var windowRows = GetWindowRowCount(height);
        var bands = GetBandCount(height);
        var workers = Math.Min(RowBands.GetWorkerCount(new Size(width, height), maxDegreeOfParallelism), bands);
        var bandSums = new double[bands * ChannelCount];
        using var frames = new ComparedFrames(scope, expected, expectedFormat, actual, actualFormat, workers);
        var accumulators = new SsimAccumulator?[workers];
        try
        {
            for (var worker = 0; worker < workers; worker++)
            {
                var (first, end) = GetWindowRows(worker);
                accumulators[worker] = new SsimAccumulator(scope, width, height, first, end, bandSums);
            }

            if (workers is 1)
            {
                AddRows(0);
            }
            else
            {
                RowBands.RunParallelInBandOrder(workers, AddRows);
            }
        }
        finally
        {
            foreach (var accumulator in accumulators)
            {
                accumulator?.Dispose();
            }
        }

        return ComputeMeanSsim(bandSums, width, height);

        (int First, int End) GetWindowRows(int worker)
            => (RowBands.GetBandStart(bands, worker, workers) * RowBands.SumBandRows, Math.Min(windowRows, RowBands.GetBandStart(bands, worker + 1, workers) * RowBands.SumBandRows));

        void AddRows(int worker)
        {
            var accumulator = accumulators[worker]!;
            var (first, end) = GetWindowRows(worker);
            var lastRow = end + accumulator._windowHeight - 1;
            for (var y = first; y < lastRow; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                accumulator.AddRow(frames.GetExpectedRow(worker, y), frames.GetActualRow(worker, y));
            }
        }
    }

    /// <summary>Gets the number of rows of windows: the windows that fit inside the image, moved one row at a time.</summary>
    private static int GetWindowRowCount(int height) => height - Math.Min(WindowSize, height) + 1;

    private static int GetBandCount(int height) => (GetWindowRowCount(height) + RowBands.SumBandRows - 1) / RowBands.SumBandRows;

    /// <summary>Adds up the sums of the bands in order, and computes the mean scores.</summary>
    private static double ComputeMeanSsim(double[] bandSums, int width, int height)
    {
        Span<double> sums = stackalloc double[ChannelCount];
        for (var i = 0; i < bandSums.Length; i += ChannelCount)
        {
            for (var channel = 0; channel < ChannelCount; channel++)
            {
                sums[channel] += bandSums[i + channel];
            }
        }

        var windowCount = (long)GetWindowRowCount(height) * (width - Math.Min(WindowSize, width) + 1);
        var colorSsim = (sums[0] / windowCount + sums[1] / windowCount + sums[2] / windowCount) / 3;
        var alphaSsim = sums[AlphaChannel] / windowCount;
        return Math.Min(colorSsim, alphaSsim);
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }

    private static void StoreChannelValues(ReadOnlySpan<Rgba32> pixels, Span<byte> destination)
    {
        for (var x = 0; x < pixels.Length; x++)
        {
            var pixel = pixels[x];
            var offset = x * ChannelCount;
            destination[offset] = Premultiply(pixel.R, pixel.A);
            destination[offset + 1] = Premultiply(pixel.G, pixel.A);
            destination[offset + 2] = Premultiply(pixel.B, pixel.A);
            destination[offset + AlphaChannel] = pixel.A;
        }
    }

    /// <summary>Premultiplies an 8-bit sample by an 8-bit alpha, rounded to the nearest.</summary>
    internal static byte Premultiply(uint value, uint alpha) => (byte)((value * alpha + 127) / 255);

    private void AccumulateWindowRow()
    {
        // The window row whose last row was just added: its scores go to the sums of its band
        var windowRow = _firstWindowRow + _rowCount - _windowHeight;
        AccumulateWindows(ColumnStatistics, _width, _windowWidth, _windowHeight, _bandSums.AsSpan(windowRow / RowBands.SumBandRows * ChannelCount, ChannelCount));
    }

    /// <summary>
    /// Adds the values of a row to the statistics of its columns (<paramref name="sign"/> 1), or removes them (-1): per
    /// pixel, the <see cref="StatisticCount"/> sums Σx, Σy, Σx², Σy², Σxy of its <see cref="ChannelCount"/> channels. The
    /// vectors compute the four channels of a pixel; the sums are exact integers.
    /// </summary>
    internal static void UpdateColumnStatistics(ReadOnlySpan<byte> expectedRow, ReadOnlySpan<byte> actualRow, Span<int> statistics, int sign)
    {
        var x = 0;
        var width = expectedRow.Length / ChannelCount;
        if (Vector128.IsHardwareAccelerated && actualRow.Length >= expectedRow.Length && statistics.Length >= width * ValuesPerColumn)
        {
            ref var expected0 = ref unsafe(MemoryMarshal.GetReference(expectedRow));
            ref var actual0 = ref unsafe(MemoryMarshal.GetReference(actualRow));
            ref var target = ref unsafe(MemoryMarshal.GetReference(statistics));
            var factor = Vector128.Create(sign);
            for (; x < width; x++)
            {
                unsafe
                {
                    var e = Widen(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref expected0, x * ChannelCount)));
                    var a = Widen(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref actual0, x * ChannelCount)));
                    var column = (nuint)(x * ValuesPerColumn);
                    (Vector128.LoadUnsafe(ref target, column) + (factor * e)).StoreUnsafe(ref target, column);
                    (Vector128.LoadUnsafe(ref target, column + ChannelCount) + (factor * a)).StoreUnsafe(ref target, column + ChannelCount);
                    (Vector128.LoadUnsafe(ref target, column + (2 * ChannelCount)) + (factor * (e * e))).StoreUnsafe(ref target, column + (2 * ChannelCount));
                    (Vector128.LoadUnsafe(ref target, column + (3 * ChannelCount)) + (factor * (a * a))).StoreUnsafe(ref target, column + (3 * ChannelCount));
                    (Vector128.LoadUnsafe(ref target, column + (4 * ChannelCount)) + (factor * (e * a))).StoreUnsafe(ref target, column + (4 * ChannelCount));
                }
            }
        }

        UpdateColumnStatisticsScalar(expectedRow, actualRow, statistics, sign, x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Widen(uint pixel) => Vector128.WidenLower(Vector128.WidenLower(Vector128.CreateScalar(pixel).AsByte())).AsInt32();
    }

    /// <summary>The scalar reference of <see cref="UpdateColumnStatistics"/>, from pixel <paramref name="start"/>.</summary>
    internal static void UpdateColumnStatisticsScalar(ReadOnlySpan<byte> expectedRow, ReadOnlySpan<byte> actualRow, Span<int> statistics, int sign, int start = 0)
    {
        for (var i = start * ChannelCount; i < expectedRow.Length; i++)
        {
            int expected = expectedRow[i];
            int actual = actualRow[i];
            var column = statistics[((i / ChannelCount * ValuesPerColumn) + (i % ChannelCount))..];
            column[4 * ChannelCount] += sign * expected * actual;
            column[3 * ChannelCount] += sign * actual * actual;
            column[2 * ChannelCount] += sign * expected * expected;
            column[ChannelCount] += sign * actual;
            column[0] += sign * expected;
        }
    }

    /// <summary>
    /// Adds the scores of the windows of a window row to <paramref name="ssimSums"/> (one per channel), from the statistics
    /// of the columns (<see cref="UpdateColumnStatistics"/>), in the order of the windows. The vectors compute the four
    /// channels of a window: the statistics are exact in 32-bit integers (at most 7 × 7 × 255 × 255 × 2), then the score
    /// is computed in double precision as by <see cref="AccumulateWindowsScalar"/>.
    /// </summary>
    internal static void AccumulateWindows(ReadOnlySpan<int> columns, int width, int windowWidth, int windowHeight, Span<double> ssimSums)
    {
        if (!Vector128.IsHardwareAccelerated || !(Sse2.IsSupported || AdvSimd.Arm64.IsSupported) || columns.Length < width * ValuesPerColumn || ssimSums.Length < ChannelCount)
        {
            AccumulateWindowsScalar(columns, width, windowWidth, windowHeight, ssimSums);
            return;
        }

        // SSIM = ((2·μx·μy + C1)(2·σxy + C2)) / ((μx² + μy² + C1)(σx² + σy² + C2)), multiplied as in the scalar reference
        long count = windowWidth * windowHeight;
        var varianceScale = count > 1 ? count * (count - 1) : 1;
        var c1 = C1 * count * count;
        var c2 = C2 * varianceScale;
        var countVector = Vector128.Create((int)count);
        ref var column0 = ref unsafe(MemoryMarshal.GetReference(columns));
        var sumExpected = Vector128<int>.Zero;
        var sumActual = Vector128<int>.Zero;
        var sumExpected2 = Vector128<int>.Zero;
        var sumActual2 = Vector128<int>.Zero;
        var sumProduct = Vector128<int>.Zero;
        var useAvx = Avx.IsSupported && Vector256.IsHardwareAccelerated;
        var total = useAvx ? Vector256.Create(ssimSums[0], ssimSums[1], ssimSums[2], ssimSums[3]) : default;
        var total01 = Vector128.Create(ssimSums[0], ssimSums[1]);
        var total23 = Vector128.Create(ssimSums[2], ssimSums[3]);
        for (var x = 0; x < width; x++)
        {
            unsafe
            {
                var entering = (nuint)(x * ValuesPerColumn);
                if (x >= windowWidth)
                {
                    var leaving = (nuint)((x - windowWidth) * ValuesPerColumn);
                    sumExpected += Vector128.LoadUnsafe(ref column0, entering) - Vector128.LoadUnsafe(ref column0, leaving);
                    sumActual += Vector128.LoadUnsafe(ref column0, entering + ChannelCount) - Vector128.LoadUnsafe(ref column0, leaving + ChannelCount);
                    sumExpected2 += Vector128.LoadUnsafe(ref column0, entering + (2 * ChannelCount)) - Vector128.LoadUnsafe(ref column0, leaving + (2 * ChannelCount));
                    sumActual2 += Vector128.LoadUnsafe(ref column0, entering + (3 * ChannelCount)) - Vector128.LoadUnsafe(ref column0, leaving + (3 * ChannelCount));
                    sumProduct += Vector128.LoadUnsafe(ref column0, entering + (4 * ChannelCount)) - Vector128.LoadUnsafe(ref column0, leaving + (4 * ChannelCount));
                }
                else
                {
                    sumExpected += Vector128.LoadUnsafe(ref column0, entering);
                    sumActual += Vector128.LoadUnsafe(ref column0, entering + ChannelCount);
                    sumExpected2 += Vector128.LoadUnsafe(ref column0, entering + (2 * ChannelCount));
                    sumActual2 += Vector128.LoadUnsafe(ref column0, entering + (3 * ChannelCount));
                    sumProduct += Vector128.LoadUnsafe(ref column0, entering + (4 * ChannelCount));
                }
            }

            if (x < windowWidth - 1)
                continue;

            var meanProduct = sumExpected * sumActual;
            var meanSquares = (sumExpected * sumExpected) + (sumActual * sumActual);
            var covariance = (countVector * sumProduct) - meanProduct;
            var variances = (countVector * (sumExpected2 + sumActual2)) - meanSquares;
            if (useAvx)
            {
                var two = Vector256.Create(2.0);
                var score = ((two * Avx.ConvertToVector256Double(meanProduct)) + Vector256.Create(c1)) * ((two * Avx.ConvertToVector256Double(covariance)) + Vector256.Create(c2))
                    / ((Avx.ConvertToVector256Double(meanSquares) + Vector256.Create(c1)) * (Avx.ConvertToVector256Double(variances) + Vector256.Create(c2)));
                total += score;
            }
            else
            {
                total01 += Score(ToDoubleLower(meanProduct), ToDoubleLower(covariance), ToDoubleLower(meanSquares), ToDoubleLower(variances), c1, c2);
                total23 += Score(ToDoubleUpper(meanProduct), ToDoubleUpper(covariance), ToDoubleUpper(meanSquares), ToDoubleUpper(variances), c1, c2);
            }
        }

        if (useAvx)
        {
            total01 = total.GetLower();
            total23 = total.GetUpper();
        }

        ssimSums[0] = total01.GetElement(0);
        ssimSums[1] = total01.GetElement(1);
        ssimSums[2] = total23.GetElement(0);
        ssimSums[3] = total23.GetElement(1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<double> Score(Vector128<double> meanProduct, Vector128<double> covariance, Vector128<double> meanSquares, Vector128<double> variances, double c1, double c2)
        {
            var two = Vector128.Create(2.0);
            return ((two * meanProduct) + Vector128.Create(c1)) * ((two * covariance) + Vector128.Create(c2)) / ((meanSquares + Vector128.Create(c1)) * (variances + Vector128.Create(c2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<double> ToDoubleLower(Vector128<int> value) => Sse2.IsSupported ? Sse2.ConvertToVector128Double(value) : Vector128.ConvertToDouble(Vector128.WidenLower(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<double> ToDoubleUpper(Vector128<int> value) => Sse2.IsSupported ? Sse2.ConvertToVector128Double(Sse2.ShiftRightLogical128BitLane(value, 8)) : Vector128.ConvertToDouble(Vector128.WidenUpper(value));
    }

    /// <summary>The scalar reference of <see cref="AccumulateWindows"/>.</summary>
    internal static void AccumulateWindowsScalar(ReadOnlySpan<int> columns, int width, int windowWidth, int windowHeight, Span<double> ssimSums)
    {
        Span<int> window = stackalloc int[ValuesPerColumn];
        window.Clear();

        // SSIM = ((2·μx·μy + C1)(2·σxy + C2)) / ((μx² + μy² + C1)(σx² + σy² + C2)). Multiplying the first factors by
        // count² and the second ones by count × (count − 1) turns every statistic into an exact integer expression of the
        // sums, leaving a single division per channel.
        long count = windowWidth * windowHeight;
        var varianceScale = count > 1 ? count * (count - 1) : 1;
        var c1 = C1 * count * count;
        var c2 = C2 * varianceScale;
        for (var x = 0; x < width; x++)
        {
            var entering = columns.Slice(x * ValuesPerColumn, ValuesPerColumn);
            if (x >= windowWidth)
            {
                var leaving = columns.Slice((x - windowWidth) * ValuesPerColumn, ValuesPerColumn);
                for (var i = 0; i < ValuesPerColumn; i++)
                {
                    window[i] += entering[i] - leaving[i];
                }
            }
            else
            {
                for (var i = 0; i < ValuesPerColumn; i++)
                {
                    window[i] += entering[i];
                }
            }

            if (x >= windowWidth - 1)
            {
                for (var channel = 0; channel < ChannelCount; channel++)
                {
                    long sumExpected = window[channel];
                    long sumActual = window[ChannelCount + channel];
                    var meanProduct = sumExpected * sumActual;
                    var meanSquares = sumExpected * sumExpected + sumActual * sumActual;
                    var covariance = count * window[(4 * ChannelCount) + channel] - meanProduct;
                    var variances = count * ((long)window[(2 * ChannelCount) + channel] + window[(3 * ChannelCount) + channel]) - meanSquares;
                    ssimSums[channel] += (2 * meanProduct + c1) * (2 * covariance + c2) / ((meanSquares + c1) * (variances + c2));
                }
            }
        }
    }
}
