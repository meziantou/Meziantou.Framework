using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Binarizes frames of one pixel format and size (<see cref="ThresholdOptions"/>): every pixel becomes the upper or the
/// lower color depending on whether its luminance is greater than its threshold. The working memory is rented when the
/// instance is created, before any pixel changes.
/// </summary>
internal sealed class Thresholder : IDisposable
{
    private readonly ThresholdOptions _options;
    private readonly PixelFormat _format;
    private readonly int _width;
    private readonly int _height;
    private readonly int _bytesPerPixel;
    private readonly uint _max;
    private readonly byte[] _colors;
    private readonly PooledBuffer _row;
    private readonly PooledBuffer? _luma;
    private readonly PooledBuffer? _sums;

    /// <summary>Rents the working memory: one row, and for the adaptive methods the luminance of a frame and two sums per column.</summary>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    public Thresholder(AllocationScope scope, PixelFormat format, Size size, ThresholdOptions options)
    {
        _options = options;
        _format = format;
        _width = size.Width;
        _height = size.Height;
        _bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        _max = (uint)(ToneSamples.GetLevelCount(format) - 1);
        _colors = new byte[2 * _bytesPerPixel];
        Store(options.UpperColor, format, _colors.AsSpan(0, _bytesPerPixel));
        Store(options.LowerColor, format, _colors.AsSpan(_bytesPerPixel));

        _row = ToneSamples.RentRow(scope, _width);
        try
        {
            if (IsAdaptive(options.Method))
            {
                var lumaBytes = (long)_width * _height * sizeof(ushort);
                var sumBytes = (long)_width * 2 * sizeof(long);
                if (lumaBytes > CheckedSizes.MaxBufferLength || sumBytes > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(scope.Limits);

                _luma = scope.Rent((int)lumaBytes, AllocationKind.Temporary, clear: false);
                _sums = scope.Rent((int)sumBytes, AllocationKind.Temporary, clear: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Checks that the pixels have discrete levels and that the pixel format stores both colors exactly, so that nothing is
    /// lost silently.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    /// <exception cref="ArgumentException">A color cannot be stored exactly.</exception>
    public static void Validate(PixelFormat format, ThresholdOptions options)
    {
        if (PixelFormats.IsFloatingPoint(format))
            throw new UnsupportedImageFeatureException(
                $"A threshold compares the levels of integer samples, but {format} samples are floating-point values. Convert the image to Rgba64 with Image.CloneAs first to threshold it.",
                ImageFormat.Unknown,
                "Threshold of floating-point samples");

        if (!PixelConverter.IsExactlyRepresentable(options.UpperColor, format))
            throw new ArgumentException(CreateColorMessage("upper", options.UpperColor, format), nameof(options));

        if (!PixelConverter.IsExactlyRepresentable(options.LowerColor, format))
            throw new ArgumentException(CreateColorMessage("lower", options.LowerColor, format), nameof(options));
    }

    /// <summary>Gets a value indicating whether a method needs the neighborhood of each pixel.</summary>
    public static bool IsAdaptive(ThresholdMethod method) => method is ThresholdMethod.Mean or ThresholdMethod.Sauvola;

    /// <summary>Binarizes the pixels of a frame in place.</summary>
    /// <param name="lease">The lease of the frame.</param>
    /// <param name="level">The threshold of the global methods, on the scale of the samples; ignored by the adaptive ones.</param>
    /// <param name="cancellationToken">The token checked between row bands.</param>
    public void Apply(scoped in PixelLease lease, double level, CancellationToken cancellationToken)
    {
        if (_luma is null)
        {
            ApplyGlobal(lease, level, cancellationToken);
        }
        else
        {
            ApplyAdaptive(lease, cancellationToken);
        }
    }

    public void Dispose()
    {
        _row.Dispose();
        _luma?.Dispose();
        _sums?.Dispose();
    }

    private static string CreateColorMessage(string name, Rgba64 color, PixelFormat format)
    {
        return string.Create(CultureInfo.InvariantCulture, $"The {name} color ({color.R}, {color.G}, {color.B}, {color.A}) cannot be stored exactly in {format} pixels: a pixel format without alpha needs an opaque color, a gray one a gray color, and an 8-bit one samples that are multiples of 257.");
    }

    private static void Store(Rgba64 color, PixelFormat format, Span<byte> destination)
    {
        ReadOnlySpan<Rgba64> source = [color];
        PixelConverter.ConvertRow(PixelFormat.Rgba64, unsafe(MemoryMarshal.AsBytes(source)), format, destination);
    }

    private void ApplyGlobal(scoped in PixelLease lease, double level, CancellationToken cancellationToken)
    {
        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(_row.Span))[..(4 * _width)];
        ReadOnlySpan<byte> upper = _colors.AsSpan(0, _bytesPerPixel);
        ReadOnlySpan<byte> lower = _colors.AsSpan(_bytesPerPixel);
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRowBytes(y)[..(_width * _bytesPerPixel)];
            ToneSamples.Read(_format, row, samples);
            for (var x = 0; x < _width; x++)
            {
                var luma = PixelConverter.Luma(samples[4 * x], samples[(4 * x) + 1], samples[(4 * x) + 2], _max);
                (luma > level ? upper : lower).CopyTo(row.Slice(x * _bytesPerPixel, _bytesPerPixel));
            }
        }
    }

    private void ApplyAdaptive(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        // The luminance of the whole frame is kept, since the windows read rows that are already binarized
        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(_row.Span))[..(4 * _width)];
        var luma = unsafe(MemoryMarshal.Cast<byte, ushort>(_luma!.Span))[..(_width * _height)];
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ToneSamples.Read(_format, lease.GetRowBytes(y)[..(_width * _bytesPerPixel)], samples);
            var lumaRow = luma.Slice(y * _width, _width);
            for (var x = 0; x < _width; x++)
            {
                lumaRow[x] = (ushort)PixelConverter.Luma(samples[4 * x], samples[(4 * x) + 1], samples[(4 * x) + 2], _max);
            }
        }

        // The sums and the sums of squares of the window rows, per column
        var sums = unsafe(MemoryMarshal.Cast<byte, long>(_sums!.Span))[..(2 * _width)];
        var columnSums = sums[.._width];
        var columnSquares = sums[_width..];
        sums.Clear();

        var options = _options;
        var edgeMode = options.EdgeMode;
        var radius = options.WindowSize / 2;
        var area = (long)options.WindowSize * options.WindowSize;
        var sauvola = options.Method == ThresholdMethod.Sauvola;
        var offset = options.Offset * _max;
        var k = options.SauvolaK;
        var range = options.SauvolaR * _max;
        ReadOnlySpan<byte> upper = _colors.AsSpan(0, _bytesPerPixel);
        ReadOnlySpan<byte> lower = _colors.AsSpan(_bytesPerPixel);
        for (var j = -radius; j <= radius; j++)
        {
            AddRow(luma, ConvolutionPlan.MapIndex(j, _height, edgeMode), columnSums, columnSquares, sign: 1);
        }

        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (y > 0)
            {
                AddRow(luma, ConvolutionPlan.MapIndex(y - 1 - radius, _height, edgeMode), columnSums, columnSquares, sign: -1);
                AddRow(luma, ConvolutionPlan.MapIndex(y + radius, _height, edgeMode), columnSums, columnSquares, sign: 1);
            }

            long sum = 0;
            long squares = 0;
            for (var i = -radius; i <= radius; i++)
            {
                var column = ConvolutionPlan.MapIndex(i, _width, edgeMode);
                if (column >= 0)
                {
                    sum += columnSums[column];
                    squares += columnSquares[column];
                }
            }

            var lumaRow = luma.Slice(y * _width, _width);
            var row = lease.GetRowBytes(y)[..(_width * _bytesPerPixel)];
            for (var x = 0; x < _width; x++)
            {
                var mean = (double)sum / area;
                double threshold;
                if (sauvola)
                {
                    // Exact: the numerator of the variance is never negative
                    var variance = (double)(((Int128)squares * area) - ((Int128)sum * sum)) / ((double)area * area);
                    threshold = mean * (1 + (k * ((Math.Sqrt(variance) / range) - 1)));
                }
                else
                {
                    threshold = mean - offset;
                }

                (lumaRow[x] > threshold ? upper : lower).CopyTo(row.Slice(x * _bytesPerPixel, _bytesPerPixel));

                var leaving = ConvolutionPlan.MapIndex(x - radius, _width, edgeMode);
                if (leaving >= 0)
                {
                    sum -= columnSums[leaving];
                    squares -= columnSquares[leaving];
                }

                var entering = ConvolutionPlan.MapIndex(x + radius + 1, _width, edgeMode);
                if (entering >= 0)
                {
                    sum += columnSums[entering];
                    squares += columnSquares[entering];
                }
            }
        }
    }

    private void AddRow(ReadOnlySpan<ushort> luma, int y, Span<long> columnSums, Span<long> columnSquares, int sign)
    {
        if (y < 0)
            return;

        var row = luma.Slice(y * _width, _width);
        for (var x = 0; x < row.Length; x++)
        {
            long value = row[x];
            columnSums[x] += sign * value;
            columnSquares[x] += sign * value * value;
        }
    }
}
