namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Computes the mean Structural Similarity Index (SSIM) of two images of the same size, fed one row at a time.
/// </summary>
/// <remarks>
/// <para>
/// Pixels are packed as <see cref="uint"/> values holding three 8-bit color channels in bytes 0, 1 and 2 and the
/// alpha channel in byte 3. The color channels are combined with an unweighted mean, so their order does not
/// matter: the same code serves ARGB pixels, where bytes 2, 1 and 0 hold R, G and B, and RGBA pixels, where bytes
/// 0, 1 and 2 hold R, G and B.
/// </para>
/// <para>
/// The SSIM is computed in every <see cref="WindowSize"/>×<see cref="WindowSize"/> window that fits inside the
/// image, moved one pixel at a time, with uniform weights, the sample (N − 1) variance and covariance, and
/// <c>K1 = 0.01</c>, <c>K2 = 0.03</c>, <c>L = 255</c>. Each channel scores the mean over all windows, which is what
/// scikit-image's <c>structural_similarity(expected, actual, data_range=255, channel_axis=-1)</c> computes with its default
/// parameters. An image narrower or shorter than the window uses a window as wide or as tall as the image.
/// </para>
/// <para>
/// Transparency is taken into account in two ways. The color channels are premultiplied by the alpha channel, so a
/// fully transparent pixel scores the same whatever color it hides, and a color over a different opacity does not
/// look the same. The alpha channel is scored on its own, and the result is the lower of the mean color score and
/// the alpha score. For images without transparency, the alpha score is exactly <c>1</c> and the result is the
/// usual mean SSIM of the three color channels.
/// </para>
/// <para>
/// The window statistics are integer sums maintained incrementally, so the cost is linear in the number of pixels,
/// and every window score is computed from exact values in a fixed order. The result depends only on the pixels,
/// not on the hardware.
/// </para>
/// </remarks>
internal sealed class SsimAccumulator
{
    /// <summary>The width and height of the window in which the local statistics are computed.</summary>
    public const int WindowSize = 7;

    private const int ChannelCount = 4;
    private const int AlphaChannel = 3;

    // Sum of expected, sum of actual, sum of expected², sum of actual², sum of expected × actual
    private const int StatisticCount = 5;
    private const int ValuesPerColumn = ChannelCount * StatisticCount;

    private const double C1 = (0.01 * 255) * (0.01 * 255);
    private const double C2 = (0.03 * 255) * (0.03 * 255);

    private readonly int _width;
    private readonly int _height;
    private readonly int _windowWidth;
    private readonly int _windowHeight;

    // The channel values of the last _windowHeight rows, ChannelCount bytes per pixel, used as a ring buffer
    private readonly byte[] _expectedRows;
    private readonly byte[] _actualRows;

    // The statistics of every column, restricted to the rows currently held in the ring buffer. Each window
    // statistic is at most 7 × 7 × 255 × 255 = 3 186 225, so int is enough.
    private readonly int[] _columnStatistics;

    private readonly double[] _channelSsimSums = new double[ChannelCount];
    private long _windowCount;
    private int _rowCount;

    public SsimAccumulator(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _width = width;
        _height = height;
        _windowWidth = Math.Min(WindowSize, width);
        _windowHeight = Math.Min(WindowSize, height);
        _expectedRows = new byte[checked(_windowHeight * width * ChannelCount)];
        _actualRows = new byte[_expectedRows.Length];
        _columnStatistics = new int[checked(width * ValuesPerColumn)];
    }

    /// <summary>
    /// Computes the mean SSIM of two images whose pixels are stored row by row in a single buffer.
    /// </summary>
    public static double Compute(ReadOnlySpan<uint> expectedPixels, ReadOnlySpan<uint> actualPixels, int width, int height)
    {
        var accumulator = new SsimAccumulator(width, height);
        for (var y = 0; y < height; y++)
        {
            accumulator.AddRow(expectedPixels.Slice(y * width, width), actualPixels.Slice(y * width, width));
        }

        return accumulator.ComputeMeanSsim();
    }

    /// <summary>
    /// Adds the next row of both images. Both spans must contain exactly one row of pixels.
    /// </summary>
    public void AddRow(ReadOnlySpan<uint> expectedRow, ReadOnlySpan<uint> actualRow)
    {
        if (expectedRow.Length != _width)
            throw new ArgumentException("The row does not have the width of the image.", nameof(expectedRow));

        if (actualRow.Length != _width)
            throw new ArgumentException("The row does not have the width of the image.", nameof(actualRow));

        if (_rowCount == _height)
            throw new InvalidOperationException("All the rows of the image have already been added.");

        var rowLength = _width * ChannelCount;
        var slot = _rowCount % _windowHeight;
        var expectedSlot = _expectedRows.AsSpan(slot * rowLength, rowLength);
        var actualSlot = _actualRows.AsSpan(slot * rowLength, rowLength);

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
    /// Computes the score of the images once all their rows have been added. Values range from <c>-1.0</c> to
    /// <c>1.0</c> (identical); unrelated images score around <c>0.0</c>.
    /// </summary>
    public double ComputeMeanSsim()
    {
        if (_rowCount != _height)
            throw new InvalidOperationException("Not all the rows of the image have been added.");

        var colorSsim = (_channelSsimSums[0] / _windowCount + _channelSsimSums[1] / _windowCount + _channelSsimSums[2] / _windowCount) / 3;
        var alphaSsim = _channelSsimSums[AlphaChannel] / _windowCount;
        return Math.Min(colorSsim, alphaSsim);
    }

    private static void StoreChannelValues(ReadOnlySpan<uint> pixels, Span<byte> destination)
    {
        for (var x = 0; x < pixels.Length; x++)
        {
            var pixel = pixels[x];
            var alpha = pixel >> 24;
            var offset = x * ChannelCount;
            destination[offset] = Premultiply(pixel & 0xFF, alpha);
            destination[offset + 1] = Premultiply((pixel >> 8) & 0xFF, alpha);
            destination[offset + 2] = Premultiply((pixel >> 16) & 0xFF, alpha);
            destination[offset + AlphaChannel] = (byte)alpha;
        }
    }

    private static byte Premultiply(uint value, uint alpha) => (byte)((value * alpha + 127) / 255);

    private void UpdateColumnStatistics(ReadOnlySpan<byte> expectedRow, ReadOnlySpan<byte> actualRow, int sign)
    {
        var statistics = _columnStatistics.AsSpan();
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
        var columns = _columnStatistics.AsSpan();
        Span<int> window = stackalloc int[ValuesPerColumn];
        window.Clear();

        // SSIM = ((2·μx·μy + C1)(2·σxy + C2)) / ((μx² + μy² + C1)(σx² + σy² + C2)). Multiplying the first factors by
        // count² and the second ones by count × (count − 1) turns every statistic into an exact integer expression
        // of the sums, leaving a single division per channel.
        long count = _windowWidth * _windowHeight;
        var varianceScale = count > 1 ? count * (count - 1) : 1;
        var c1 = C1 * count * count;
        var c2 = C2 * varianceScale;

        var ssimSums = _channelSsimSums.AsSpan();
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
                    long meanProduct = sumExpected * sumActual;
                    var meanSquares = sumExpected * sumExpected + sumActual * sumActual;
                    var covariance = count * statistics[4] - meanProduct;
                    var variances = count * ((long)statistics[2] + statistics[3]) - meanSquares;
                    ssimSums[channel] += (2 * meanProduct + c1) * (2 * covariance + c2) / ((meanSquares + c1) * (variances + c2));
                }

                _windowCount++;
            }
        }
    }
}
