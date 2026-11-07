using System.Runtime.InteropServices;

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

        // The rows of the window of the output row, premultiplied, then the statistics of every column
        using var buffer = ResampleWeights.Rent<double>(scope, (2L * windowHeight * rowLength) + ((long)rowLength * StatisticCount));
        var span = unsafe(MemoryMarshal.Cast<byte, double>(buffer.Span));
        var expectedRows = span[..(windowHeight * rowLength)];
        var actualRows = span.Slice(windowHeight * rowLength, windowHeight * rowLength);
        var columns = span.Slice(2 * windowHeight * rowLength, rowLength * StatisticCount);
        double count = windowWidth * windowHeight;
        var covarianceScale = count > 1 ? count / (count - 1) : 1;
        const double C1 = 0.01 * 0.01;
        const double C2 = 0.03 * 0.03;
        Span<double> window = stackalloc double[StatisticCount];
        Span<double> scores = stackalloc double[ChannelCount];
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var k = 0; k < windowHeight; k++)
            {
                var source = ConvolutionPlan.MapIndex(y - (windowHeight / 2) + k, height, ConvolutionEdgeMode.Mirror);
                Premultiply(expectedReader.GetRow(expected, source), expectedRows.Slice(k * rowLength, rowLength));
                Premultiply(actualReader.GetRow(actual, source), actualRows.Slice(k * rowLength, rowLength));
            }

            columns.Clear();
            for (var k = 0; k < windowHeight; k++)
            {
                var e = expectedRows.Slice(k * rowLength, rowLength);
                var a = actualRows.Slice(k * rowLength, rowLength);
                for (var i = 0; i < rowLength; i++)
                {
                    var column = columns.Slice(i * StatisticCount, StatisticCount);
                    column[0] += e[i];
                    column[1] += a[i];
                    column[2] += e[i] * e[i];
                    column[3] += a[i] * a[i];
                    column[4] += e[i] * a[i];
                }
            }

            var output = unsafe(MemoryMarshal.Cast<byte, Rgba32>(destination.GetRowSpanCore(y)))[..width];
            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < ChannelCount; channel++)
                {
                    window.Clear();
                    for (var k = 0; k < windowWidth; k++)
                    {
                        var source = ConvolutionPlan.MapIndex(x - (windowWidth / 2) + k, width, ConvolutionEdgeMode.Mirror);
                        var statistics = columns.Slice(((source * ChannelCount) + channel) * StatisticCount, StatisticCount);
                        for (var i = 0; i < StatisticCount; i++)
                        {
                            window[i] += statistics[i];
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

                var ssim = Math.Min((scores[0] + scores[1] + scores[2]) / 3, scores[AlphaChannel]);
                var level = ToByte((1 - ssim) * gain);
                output[x] = new Rgba32(level, level, level, byte.MaxValue);
            }
        }
    }

    /// <summary>Gets the side of the SSIM window along a side of the frame: 7, or the largest odd size that fits.</summary>
    private static int GetWindowSize(int length) => length >= SsimAccumulator.WindowSize ? SsimAccumulator.WindowSize : length % 2 == 0 ? length - 1 : length;
}
