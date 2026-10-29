using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Computes the mean Structural Similarity Index (SSIM) of two images of the same size, fed one row at a time
/// (<see cref="ImageSimilarity.ComputeSsim"/>).
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

    // Per channel: Σx, Σy, Σx², Σy², Σxy
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
            UpdateColumnStatistics(expectedSlot, actualSlot, sign: -1);
        }

        StoreChannelValues(expectedRow, expectedSlot);
        StoreChannelValues(actualRow, actualSlot);
        UpdateColumnStatistics(expectedSlot, actualSlot, sign: 1);
        _rowCount++;

        if (_rowCount >= _windowHeight)
        {
            AccumulateWindows();
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

    private void UpdateColumnStatistics(ReadOnlySpan<byte> expectedRow, ReadOnlySpan<byte> actualRow, int sign)
    {
        var statistics = ColumnStatistics;
        for (var i = 0; i < expectedRow.Length; i++)
        {
            int expected = expectedRow[i];
            int actual = actualRow[i];
            var column = statistics.Slice(i * StatisticCount, StatisticCount);
            column[4] += sign * expected * actual;
            column[3] += sign * actual * actual;
            column[2] += sign * expected * expected;
            column[1] += sign * actual;
            column[0] += sign * expected;
        }
    }

    private void AccumulateWindows()
    {
        var columns = ColumnStatistics;
        Span<int> window = stackalloc int[ValuesPerColumn];
        window.Clear();

        // SSIM = ((2·μx·μy + C1)(2·σxy + C2)) / ((μx² + μy² + C1)(σx² + σy² + C2)). Multiplying the first factors by
        // count² and the second ones by count × (count − 1) turns every statistic into an exact integer expression of the
        // sums, leaving a single division per channel.
        long count = _windowWidth * _windowHeight;
        var varianceScale = count > 1 ? count * (count - 1) : 1;
        var c1 = C1 * count * count;
        var c2 = C2 * varianceScale;

        // The window row whose last row was just added: its scores go to the sums of its band
        var windowRow = _firstWindowRow + _rowCount - _windowHeight;
        var ssimSums = _bandSums.AsSpan(windowRow / RowBands.SumBandRows * ChannelCount, ChannelCount);
        for (var x = 0; x < _width; x++)
        {
            var entering = columns.Slice(x * ValuesPerColumn, ValuesPerColumn);
            if (x >= _windowWidth)
            {
                var leaving = columns.Slice((x - _windowWidth) * ValuesPerColumn, ValuesPerColumn);
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

            if (x >= _windowWidth - 1)
            {
                for (var channel = 0; channel < ChannelCount; channel++)
                {
                    var statistics = window.Slice(channel * StatisticCount, StatisticCount);
                    long sumExpected = statistics[0];
                    long sumActual = statistics[1];
                    var meanProduct = sumExpected * sumActual;
                    var meanSquares = sumExpected * sumExpected + sumActual * sumActual;
                    var covariance = count * statistics[4] - meanProduct;
                    var variances = count * ((long)statistics[2] + statistics[3]) - meanSquares;
                    ssimSums[channel] += (2 * meanProduct + c1) * (2 * covariance + c2) / ((meanSquares + c1) * (variances + c2));
                }
            }
        }
    }
}
