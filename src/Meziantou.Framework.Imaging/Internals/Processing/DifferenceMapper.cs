using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Draws the difference map of two frames of the same size
/// (<see cref="ImageSimilarity.CreateDifferenceMap(ImageFrame, ImageFrame, DifferenceMapOptions?, CancellationToken)"/>)
/// into an opaque <see cref="Rgba32"/> storage.
/// </summary>
/// <remarks>
/// The frames are read as fractions of their sample range (<see cref="WideRowReader"/>, <see cref="SimilarityPrecision.FloatingPoint"/>),
/// colors premultiplied by alpha. The local SSIM of <see cref="DifferenceMapKind.Ssim"/> uses the window of the pixel
/// (7×7, or the largest odd size that fits the frame) with the rows and columns beyond the edges mirrored
/// (<see cref="ConvolutionEdgeMode.Mirror"/>), as scikit-image's <c>structural_similarity(full=True)</c> does, and the
/// constants of <see cref="SsimAccumulator"/> on a range of 1. The output samples are rounded to the nearest, ties upward.
/// </remarks>
internal static class DifferenceMapper
{
    private const int ChannelCount = 4;
    private const int AlphaChannel = 3;
    private const int StatisticCount = 5;
    private const double C1 = 0.01 * 0.01;
    private const double C2 = 0.03 * 0.03;

    /// <summary>Draws the map of two leased frames into <paramref name="destination"/>, an <see cref="Rgba32"/> storage of their size.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is not finite.</exception>
    public static void Draw(AllocationScope scope, PixelStorage expected, PixelFormat expectedFormat, PixelStorage actual, PixelFormat actualFormat, PixelStorage destination, DifferenceMapOptions options, CancellationToken cancellationToken)
    {
        var width = expected.Width;
        using var expectedReader = WideRowReader.Create(scope, expectedFormat, width, SimilarityPrecision.FloatingPoint);
        using var actualReader = WideRowReader.Create(scope, actualFormat, width, SimilarityPrecision.FloatingPoint);
        if (options.Kind == DifferenceMapKind.Ssim)
        {
            DrawSsim(scope, expected, expectedReader, actual, actualReader, destination, options.Gain, cancellationToken);
            return;
        }

        using var buffer = ResampleWeights.Rent<double>(scope, ChannelCount * (long)width);
        var premultiplied = unsafe(MemoryMarshal.Cast<byte, double>(buffer.Span))[..(ChannelCount * width)];
        for (var y = 0; y < expected.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Premultiply(actualReader.GetRow(actual, y), premultiplied);
            var expectedRow = expectedReader.GetRow(expected, y);
            var output = unsafe(MemoryMarshal.Cast<byte, Rgba32>(destination.GetRowSpanCore(y)))[..width];
            for (var x = 0; x < width; x++)
            {
                var pixel = expectedRow.Slice(x * ChannelCount, ChannelCount);
                var alpha = pixel[AlphaChannel];
                var difference = Math.Abs(alpha - premultiplied[(x * ChannelCount) + AlphaChannel]);
                for (var c = 0; c < AlphaChannel; c++)
                {
                    difference = Math.Max(difference, Math.Abs((pixel[c] * alpha) - premultiplied[(x * ChannelCount) + c]));
                }

                if (options.Kind == DifferenceMapKind.Absolute)
                {
                    var level = ToByte(difference * options.Gain);
                    output[x] = new Rgba32(level, level, level, byte.MaxValue);
                }
                else
                {
                    output[x] = Composite(difference > options.Threshold ? options.HighlightColor : options.LowlightColor, pixel);
                }
            }
        }
    }

    /// <summary>Composites an 8-bit color over a pixel of straight fractions (clamped to [0, 1]): the source-over operator.</summary>
    private static Rgba32 Composite(Rgba32 overlay, ReadOnlySpan<double> pixel)
    {
        var overlayAlpha = overlay.A / 255d;
        var pixelAlpha = Math.Clamp(pixel[AlphaChannel], 0, 1);
        var alpha = overlayAlpha + ((1 - overlayAlpha) * pixelAlpha);
        return new Rgba32(Blend(overlay.R, pixel[0]), Blend(overlay.G, pixel[1]), Blend(overlay.B, pixel[2]), ToByte(alpha));

        byte Blend(byte color, double under) => alpha is 0 ? (byte)0 : ToByte(((overlayAlpha * color / 255d) + ((1 - overlayAlpha) * pixelAlpha * Math.Clamp(under, 0, 1))) / alpha);
    }

    private static byte ToByte(double fraction) => (byte)Math.Floor((Math.Clamp(fraction, 0, 1) * byte.MaxValue) + 0.5);

    private static void Premultiply(ReadOnlySpan<double> row, Span<double> destination)
    {
        for (var i = 0; i < row.Length; i += ChannelCount)
        {
            var alpha = row[i + AlphaChannel];
            destination[i] = row[i] * alpha;
            destination[i + 1] = row[i + 1] * alpha;
            destination[i + 2] = row[i + 2] * alpha;
            destination[i + AlphaChannel] = alpha;
        }
    }

    private static void DrawSsim(AllocationScope scope, PixelStorage expected, WideRowReader expectedReader, PixelStorage actual, WideRowReader actualReader, PixelStorage destination, double gain, CancellationToken cancellationToken)
    {
        var width = expected.Width;
        var height = expected.Height;
        var windowWidth = GetWindowSize(width);
        var windowHeight = GetWindowSize(height);
        var rowLength = width * ChannelCount;

        // A ring of the premultiplied rows (row r in slot r % windowHeight: the rows of a window are consecutive), the
        // statistics of every column, then the scores of the output row
        using var buffer = ResampleWeights.Rent<double>(scope, (2L * windowHeight * rowLength) + ((long)rowLength * StatisticCount) + width);
        var span = unsafe(MemoryMarshal.Cast<byte, double>(buffer.Span));
        var expectedRows = span[..(windowHeight * rowLength)];
        var actualRows = span.Slice(windowHeight * rowLength, windowHeight * rowLength);
        var columns = span.Slice(2 * windowHeight * rowLength, rowLength * StatisticCount);
        var ssim = span.Slice((2 * windowHeight * rowLength) + (rowLength * StatisticCount), width);
        Span<int> slotRows = stackalloc int[windowHeight];
        slotRows.Fill(-1);
        Span<int> slots = stackalloc int[windowHeight];
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The rows are read in the order of the window, the first time they are in one
            for (var k = 0; k < windowHeight; k++)
            {
                var source = ConvolutionPlan.MapIndex(y - (windowHeight / 2) + k, height, ConvolutionEdgeMode.Mirror);
                var slot = source % windowHeight;
                if (slotRows[slot] != source)
                {
                    Premultiply(expectedReader.GetRow(expected, source), expectedRows.Slice(slot * rowLength, rowLength));
                    Premultiply(actualReader.GetRow(actual, source), actualRows.Slice(slot * rowLength, rowLength));
                    slotRows[slot] = source;
                }

                slots[k] = slot;
            }

            SumColumns(expectedRows, actualRows, slots, rowLength, columns);
            ComputeScores(columns, windowWidth, windowHeight, ssim);
            var output = unsafe(MemoryMarshal.Cast<byte, Rgba32>(destination.GetRowSpanCore(y)))[..width];
            for (var x = 0; x < width; x++)
            {
                var level = ToByte((1 - ssim[x]) * gain);
                output[x] = new Rgba32(level, level, level, byte.MaxValue);
            }
        }
    }

    /// <summary>
    /// Sets the statistics of every column of a window of rows: the sums of the values of both frames, of their squares and
    /// of their products (<see cref="StatisticCount"/> groups of <see cref="ChannelCount"/> values per pixel), adding the
    /// rows in the order of <paramref name="slots"/>. The vectors compute the four channels of a pixel.
    /// </summary>
    internal static void SumColumns(ReadOnlySpan<double> expectedRows, ReadOnlySpan<double> actualRows, ReadOnlySpan<int> slots, int rowLength, Span<double> columns)
    {
        var x = 0;
        var width = rowLength / ChannelCount;
        foreach (var slot in slots)
        {
            if ((uint)slot >= (uint)(expectedRows.Length / Math.Max(1, rowLength)) || (slot + 1) * rowLength > actualRows.Length)
                throw new ArgumentOutOfRangeException(nameof(slots));
        }

        if (columns.Length < width * ChannelCount * StatisticCount)
            throw new ArgumentOutOfRangeException(nameof(columns));

        ref var expected0 = ref unsafe(MemoryMarshal.GetReference(expectedRows));
        ref var actual0 = ref unsafe(MemoryMarshal.GetReference(actualRows));
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
                foreach (var slot in slots)
                {
                    var offset = (nuint)((slot * rowLength) + (x * ChannelCount));
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
        else if (Vector128.IsHardwareAccelerated)
        {
            for (; x < 2 * width; x++)
            {
                // Half a pixel: two channels
                var sumExpected = Vector128<double>.Zero;
                var sumActual = Vector128<double>.Zero;
                var sumExpected2 = Vector128<double>.Zero;
                var sumActual2 = Vector128<double>.Zero;
                var sumProduct = Vector128<double>.Zero;
                foreach (var slot in slots)
                {
                    var offset = (nuint)((slot * rowLength) + (x * 2));
                    var e = unsafe(Vector128.LoadUnsafe(ref expected0, offset));
                    var a = unsafe(Vector128.LoadUnsafe(ref actual0, offset));
                    sumExpected += e;
                    sumActual += a;
                    sumExpected2 += e * e;
                    sumActual2 += a * a;
                    sumProduct += e * a;
                }

                var column = (nuint)(((x >> 1) * ChannelCount * StatisticCount) + ((x & 1) * 2));
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

        SumColumnsScalar(expectedRows, actualRows, slots, rowLength, columns, x);
    }

    /// <summary>The scalar reference of <see cref="SumColumns"/>, from pixel <paramref name="start"/>.</summary>
    internal static void SumColumnsScalar(ReadOnlySpan<double> expectedRows, ReadOnlySpan<double> actualRows, ReadOnlySpan<int> slots, int rowLength, Span<double> columns, int start = 0)
    {
        columns[(start * ChannelCount * StatisticCount)..(rowLength * StatisticCount)].Clear();
        foreach (var slot in slots)
        {
            var e = expectedRows.Slice(slot * rowLength, rowLength);
            var a = actualRows.Slice(slot * rowLength, rowLength);
            for (var i = start * ChannelCount; i < rowLength; i++)
            {
                var column = columns.Slice(((i / ChannelCount * StatisticCount) * ChannelCount) + (i % ChannelCount));
                column[0] += e[i];
                column[ChannelCount] += a[i];
                column[2 * ChannelCount] += e[i] * e[i];
                column[3 * ChannelCount] += a[i] * a[i];
                column[4 * ChannelCount] += e[i] * a[i];
            }
        }
    }

    /// <summary>
    /// Computes the SSIM of the window centered on every pixel of a row from the statistics of the columns
    /// (<see cref="SumColumns"/>), the columns beyond the edges mirrored: the lower of the mean color score and the alpha
    /// score. The vectors compute the four channels of a pixel.
    /// </summary>
    internal static void ComputeScores(ReadOnlySpan<double> columns, int windowWidth, int windowHeight, Span<double> ssim)
    {
        var width = ssim.Length;
        if (columns.Length < width * ChannelCount * StatisticCount)
            throw new ArgumentOutOfRangeException(nameof(columns));

        var x = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            double count = windowWidth * windowHeight;
            var covarianceScale = Vector256.Create(count > 1 ? count / (count - 1) : 1);
            var countVector = Vector256.Create(count);
            var two = Vector256.Create(2.0);
            var c1 = Vector256.Create(C1);
            var c2 = Vector256.Create(C2);
            ref var column0 = ref unsafe(MemoryMarshal.GetReference(columns));
            for (; x < width; x++)
            {
                var sumExpected = Vector256<double>.Zero;
                var sumActual = Vector256<double>.Zero;
                var sumExpected2 = Vector256<double>.Zero;
                var sumActual2 = Vector256<double>.Zero;
                var sumProduct = Vector256<double>.Zero;
                var first = x - (windowWidth / 2);
                var inside = first >= 0 && first + windowWidth <= width;
                for (var k = 0; k < windowWidth; k++)
                {
                    var source = inside ? first + k : ConvolutionPlan.MapIndex(first + k, width, ConvolutionEdgeMode.Mirror);
                    var column = (nuint)(source * ChannelCount * StatisticCount);
                    unsafe
                    {
                        sumExpected += Vector256.LoadUnsafe(ref column0, column);
                        sumActual += Vector256.LoadUnsafe(ref column0, column + ChannelCount);
                        sumExpected2 += Vector256.LoadUnsafe(ref column0, column + (2 * ChannelCount));
                        sumActual2 += Vector256.LoadUnsafe(ref column0, column + (3 * ChannelCount));
                        sumProduct += Vector256.LoadUnsafe(ref column0, column + (4 * ChannelCount));
                    }
                }

                var meanExpected = sumExpected / countVector;
                var meanActual = sumActual / countVector;
                var varianceExpected = covarianceScale * ((sumExpected2 / countVector) - (meanExpected * meanExpected));
                var varianceActual = covarianceScale * ((sumActual2 / countVector) - (meanActual * meanActual));
                var covariance = covarianceScale * ((sumProduct / countVector) - (meanExpected * meanActual));
                var scores = ((two * meanExpected * meanActual) + c1) * ((two * covariance) + c2) / (((meanExpected * meanExpected) + (meanActual * meanActual) + c1) * (varianceExpected + varianceActual + c2));
                ssim[x] = Math.Min((scores.GetElement(0) + scores.GetElement(1) + scores.GetElement(2)) / 3, scores.GetElement(AlphaChannel));
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            double count = windowWidth * windowHeight;
            var covarianceScale = Vector128.Create(count > 1 ? count / (count - 1) : 1);
            var countVector = Vector128.Create(count);
            var two = Vector128.Create(2.0);
            var c1 = Vector128.Create(C1);
            var c2 = Vector128.Create(C2);
            ref var column0 = ref unsafe(MemoryMarshal.GetReference(columns));
            for (; x < width; x++)
            {
                var scores01 = Score128(ref column0, x, 0);
                var scores23 = Score128(ref column0, x, 2);
                ssim[x] = Math.Min((scores01.GetElement(0) + scores01.GetElement(1) + scores23.GetElement(0)) / 3, scores23.GetElement(1));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            Vector128<double> Score128(ref double column0, int x, int channel)
            {
                var sumExpected = Vector128<double>.Zero;
                var sumActual = Vector128<double>.Zero;
                var sumExpected2 = Vector128<double>.Zero;
                var sumActual2 = Vector128<double>.Zero;
                var sumProduct = Vector128<double>.Zero;
                var first = x - (windowWidth / 2);
                var inside = first >= 0 && first + windowWidth <= width;
                for (var k = 0; k < windowWidth; k++)
                {
                    var source = inside ? first + k : ConvolutionPlan.MapIndex(first + k, width, ConvolutionEdgeMode.Mirror);
                    var column = (nuint)((source * ChannelCount * StatisticCount) + channel);
                    unsafe
                    {
                        sumExpected += Vector128.LoadUnsafe(ref column0, column);
                        sumActual += Vector128.LoadUnsafe(ref column0, column + ChannelCount);
                        sumExpected2 += Vector128.LoadUnsafe(ref column0, column + (2 * ChannelCount));
                        sumActual2 += Vector128.LoadUnsafe(ref column0, column + (3 * ChannelCount));
                        sumProduct += Vector128.LoadUnsafe(ref column0, column + (4 * ChannelCount));
                    }
                }

                var meanExpected = sumExpected / countVector;
                var meanActual = sumActual / countVector;
                var varianceExpected = covarianceScale * ((sumExpected2 / countVector) - (meanExpected * meanExpected));
                var varianceActual = covarianceScale * ((sumActual2 / countVector) - (meanActual * meanActual));
                var covariance = covarianceScale * ((sumProduct / countVector) - (meanExpected * meanActual));
                return ((two * meanExpected * meanActual) + c1) * ((two * covariance) + c2) / (((meanExpected * meanExpected) + (meanActual * meanActual) + c1) * (varianceExpected + varianceActual + c2));
            }
        }

        ComputeScoresScalar(columns, windowWidth, windowHeight, ssim, x);
    }

    /// <summary>The scalar reference of <see cref="ComputeScores"/>, from pixel <paramref name="start"/>.</summary>
    internal static void ComputeScoresScalar(ReadOnlySpan<double> columns, int windowWidth, int windowHeight, Span<double> ssim, int start = 0)
    {
        var width = ssim.Length;
        double count = windowWidth * windowHeight;
        var covarianceScale = count > 1 ? count / (count - 1) : 1;
        Span<double> window = stackalloc double[StatisticCount];
        Span<double> scores = stackalloc double[ChannelCount];
        for (var x = start; x < width; x++)
        {
            for (var channel = 0; channel < ChannelCount; channel++)
            {
                window.Clear();
                for (var k = 0; k < windowWidth; k++)
                {
                    var source = ConvolutionPlan.MapIndex(x - (windowWidth / 2) + k, width, ConvolutionEdgeMode.Mirror);
                    for (var i = 0; i < StatisticCount; i++)
                    {
                        window[i] += columns[(((source * StatisticCount) + i) * ChannelCount) + channel];
                    }
                }

                // The means and the sample variances and covariance, as scikit-image computes them
                var meanExpected = window[0] / count;
                var meanActual = window[1] / count;
                var varianceExpected = covarianceScale * ((window[2] / count) - (meanExpected * meanExpected));
                var varianceActual = covarianceScale * ((window[3] / count) - (meanActual * meanActual));
                var covariance = covarianceScale * ((window[4] / count) - (meanExpected * meanActual));
                scores[channel] = ((2 * meanExpected * meanActual) + C1) * ((2 * covariance) + C2) / (((meanExpected * meanExpected) + (meanActual * meanActual) + C1) * (varianceExpected + varianceActual + C2));
            }

            ssim[x] = Math.Min((scores[0] + scores[1] + scores[2]) / 3, scores[AlphaChannel]);
        }
    }

    /// <summary>Gets the side of the SSIM window along a side of the frame: 7, or the largest odd size that fits.</summary>
    private static int GetWindowSize(int length) => length >= SsimAccumulator.WindowSize ? SsimAccumulator.WindowSize : length % 2 == 0 ? length - 1 : length;
}
