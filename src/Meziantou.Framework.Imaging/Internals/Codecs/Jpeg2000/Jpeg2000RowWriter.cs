using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the samples of the tile-components of a tile into rows of pixels: the palette, the replication of the
/// sub-sampled chrominances, the conversion of sYCC to RGB, the scaling to 8 or 16 bits, and the order of the channels.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Depths: samples of 8 bits (in an image without deeper channel) and of 16 bits are stored as they are; the others are
/// scaled exactly, <c>(v * targetMax + max / 2) / max</c>, to 8 bits when no channel has more, to 16 bits otherwise.
/// </description></item>
/// <item><description>
/// Sub-sampling: a pixel at the column <c>x</c> of the reference grid takes the sample <c>floor(x / XRsiz)</c> of a
/// component, or the first sample of the tile-component when that one is before it; likewise for the rows.
/// </description></item>
/// <item><description>
/// sYCC (IEC 61966-2-1, Amendment 1): <c>R = Y + 1.402 Cr</c>, <c>G = Y - 0.344136 Cb - 0.714136 Cr</c>,
/// <c>B = Y + 1.772 Cb</c>, where the chrominances are offset by half of their range. The products use constants with 16
/// fractional bits (91881, 22553, 46802 and 116130), the sum is rounded half up and clamped to the range of the depth.
/// </description></item>
/// <item><description>
/// CMYK: the samples are amounts of ink, 0 for none (T.801, Table M.25). They are scaled like the other samples, then
/// converted to the destination profile of the load, with the opacity channel copied.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class Jpeg2000RowWriter : IDisposable
{
    private const int FixedPointBits = 16;
    private const long RedFromRedDifference = 91881;
    private const long GreenFromBlueDifference = 22553;
    private const long GreenFromRedDifference = 46802;
    private const long BlueFromBlueDifference = 116130;

    private readonly ImageCodecContext _context;
    private readonly Jpeg2000Siz _siz;
    private readonly Jpeg2000ChannelLayout _layout;
    private readonly DecodedFrameSink _sink;
    private readonly Jpeg2000Channel[] _channels;
    private readonly IccPipeline? _inkConversion;
    private Av1PooledArray<int>? _values;
    private PooledBuffer? _row;
    private PooledBuffer? _inks;

    public Jpeg2000RowWriter(ImageCodecContext context, Jpeg2000Siz siz, Jpeg2000ChannelLayout layout, DecodedFrameSink sink, IccPipeline? inkConversion)
    {
        _context = context;
        _siz = siz;
        _layout = layout;
        _sink = sink;
        _inkConversion = inkConversion;
        _channels = layout.Alpha is { } alpha ? [.. layout.Colors, alpha] : layout.Colors;
    }

    /// <summary>Writes the pixels of a tile whose tile-components hold their samples.</summary>
    /// <exception cref="InvalidImageContentException">A sample is not an index of the palette.</exception>
    public void WriteTile(Jpeg2000TileGeometry geometry)
    {
        var width = (int)(geometry.X1 - geometry.X0);
        var height = (int)(geometry.Y1 - geometry.Y0);
        var left = (int)(geometry.X0 - _siz.XOffset);
        var top = (int)(geometry.Y0 - _siz.YOffset);
        var channelCount = _channels.Length;
        var sourceFormat = _sink.SourcePixelFormat;
        var sourceBytesPerPixel = PixelFormats.GetBytesPerPixel(sourceFormat);
        var destinationBytesPerPixel = PixelFormats.GetBytesPerPixel(_sink.DestinationPixelFormat);

        if (_values is null || _values.Length < (long)width * channelCount)
        {
            _values?.Dispose();
            _values = null;
            _values = Av1PooledArray<int>.Rent(_context.Scope, (long)width * channelCount, clear: false);
        }

        if (_row is null || _row.Length < (long)width * sourceBytesPerPixel)
        {
            _row?.Dispose();
            _row = null;
            _row = _context.Scope.Rent(checked(width * sourceBytesPerPixel), AllocationKind.DecoderState, clear: false);
        }

        var values = _values.Span;
        var row = _row.RawBuffer.AsSpan(0, width * sourceBytesPerPixel);
        var targetMax = _layout.IsSixteenBit ? ushort.MaxValue : byte.MaxValue;
        using var lease = _sink.LeaseCurrentFrame();
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
            }

            for (var k = 0; k < channelCount; k++)
            {
                ReadChannel(geometry, _channels[k], geometry.Y0 + y, values.Slice(k * width, width));
            }

            if (_layout.Kind == Jpeg2000ColorKind.Sycc)
            {
                ConvertSycc(values[..width], values.Slice(width, width), values.Slice(2 * width, width), _layout.Colors[0].Precision);
            }

            for (var k = 0; k < channelCount; k++)
            {
                Scale(values.Slice(k * width, width), _channels[k].Precision, targetMax);
            }

            if (_inkConversion is { } inkConversion)
            {
                ConvertInks(inkConversion, values, width, row);
            }
            else
            {
                Pack(values, width, sourceFormat, row);
            }

            var destination = lease.GetRowBytes(top + y).Slice(left * destinationBytesPerPixel, width * destinationBytesPerPixel);
            if (_sink.Plan.IsIdentity)
            {
                row.CopyTo(destination);
            }
            else
            {
                _sink.Plan.ConvertRow(row, destination);
            }
        }
    }

    public void Dispose()
    {
        _values?.Dispose();
        _values = null;
        _row?.Dispose();
        _row = null;
        _inks?.Dispose();
        _inks = null;
    }

    /// <summary>Converts luminance and chrominance samples to red, green and blue samples of the same depth, in place.</summary>
    internal static void ConvertSycc(Span<int> first, Span<int> second, Span<int> third, int precision)
    {
        var offset = 1 << (precision - 1);
        var maximum = (1 << precision) - 1;
        const long Half = 1L << (FixedPointBits - 1);
        for (var i = 0; i < first.Length; i++)
        {
            long luminance = first[i];
            long blueDifference = second[i] - offset;
            long redDifference = third[i] - offset;
            var red = luminance + (((RedFromRedDifference * redDifference) + Half) >> FixedPointBits);
            var green = luminance + ((Half - (GreenFromBlueDifference * blueDifference) - (GreenFromRedDifference * redDifference)) >> FixedPointBits);
            var blue = luminance + (((BlueFromBlueDifference * blueDifference) + Half) >> FixedPointBits);
            first[i] = (int)Math.Clamp(red, 0, maximum);
            second[i] = (int)Math.Clamp(green, 0, maximum);
            third[i] = (int)Math.Clamp(blue, 0, maximum);
        }
    }

    /// <summary>Scales samples of <paramref name="precision"/> bits to the range 0 to <paramref name="targetMax"/>, in place.</summary>
    internal static void Scale(Span<int> values, int precision, int targetMax)
    {
        var max = (1 << precision) - 1;
        if (max == targetMax)
            return;

        var half = max / 2;
        foreach (ref var value in values)
        {
            value = (int)((((long)value * targetMax) + half) / max);
        }
    }

    private void ReadChannel(Jpeg2000TileGeometry geometry, Jpeg2000Channel channel, long y, Span<int> values)
    {
        var size = _siz.Components[channel.Component];
        var component = geometry.Components[channel.Component];
        var componentWidth = component.Width;
        if (componentWidth == 0 || component.Height == 0)
        {
            // A tile narrower than the sub-sampling may hold no sample of a component
            values.Clear();
            return;
        }

        var rowIndex = (int)(Math.Clamp(y / size.YSubsampling, component.Y0, component.Y1 - 1) - component.Y0);
        var samples = component.Samples.Slice(rowIndex * componentWidth, componentWidth);
        if (size.XSubsampling == 1)
        {
            samples.CopyTo(values);
        }
        else
        {
            var x = geometry.X0;
            for (var i = 0; i < values.Length; i++, x++)
            {
                values[i] = samples[(int)(Math.Clamp(x / size.XSubsampling, component.X0, component.X1 - 1) - component.X0)];
            }
        }

        if (channel.PaletteColumn >= 0)
        {
            var column = _layout.Palette!.GetColumn(channel.PaletteColumn);
            foreach (ref var value in values)
            {
                if ((uint)value >= (uint)column.Length)
                    throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"a sample is the index {value} of a palette of {column.Length} entries."));

                value = column[value];
            }
        }
    }

    private void ConvertInks(IccPipeline conversion, ReadOnlySpan<int> values, int width, Span<byte> row)
    {
        // The four inks of each pixel, then its opacity: bytes become Rgb24 or Rgba32, 16-bit samples become Rgba64
        var channelCount = _channels.Length;
        var isSixteenBit = _layout.IsSixteenBit;
        var samplesPerPixel = isSixteenBit ? 5 : channelCount;
        var length = checked(width * samplesPerPixel * (isSixteenBit ? sizeof(ushort) : 1));
        if (_inks is null || _inks.Length < length)
        {
            _inks?.Dispose();
            _inks = null;
            _inks = _context.Scope.Rent(length, AllocationKind.DecoderState, clear: false);
        }

        if (!isSixteenBit)
        {
            var inks = _inks.RawBuffer.AsSpan(0, length);
            for (var k = 0; k < channelCount; k++)
            {
                var channel = values.Slice(k * width, width);
                for (var x = 0; x < width; x++)
                {
                    inks[(x * channelCount) + k] = (byte)channel[x];
                }
            }

            ColorConversionKernels.Convert<byte, IccByteSample>(conversion, inks, row, channelCount, channelCount - 1);
            return;
        }

        var deepInks = unsafe(MemoryMarshal.Cast<byte, ushort>(_inks.RawBuffer.AsSpan(0, length)));
        for (var k = 0; k < 5; k++)
        {
            if (k >= channelCount)
            {
                for (var x = 0; x < width; x++)
                {
                    deepInks[(x * 5) + k] = ushort.MaxValue;
                }

                continue;
            }

            var channel = values.Slice(k * width, width);
            for (var x = 0; x < width; x++)
            {
                deepInks[(x * 5) + k] = (ushort)channel[x];
            }
        }

        ColorConversionKernels.Convert<ushort, IccUInt16Sample>(conversion, deepInks, unsafe(MemoryMarshal.Cast<byte, ushort>(row)), 5, 4);
    }

    private void Pack(ReadOnlySpan<int> values, int width, PixelFormat format, Span<byte> row)
    {
        var colorCount = _layout.Colors.Length;
        var first = values[..width];
        var second = colorCount >= 3 ? values.Slice(width, width) : first;
        var third = colorCount >= 3 ? values.Slice(2 * width, width) : first;
        var alpha = _layout.Alpha is null ? default : values.Slice(colorCount * width, width);
        switch (format)
        {
            case PixelFormat.Gray8:
                for (var x = 0; x < width; x++)
                {
                    row[x] = (byte)first[x];
                }

                break;

            case PixelFormat.Gray16:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                for (var x = 0; x < width; x++)
                {
                    samples[x] = (ushort)first[x];
                }

                break;
            }

            case PixelFormat.Rgb24:
                for (var x = 0; x < width; x++)
                {
                    row[3 * x] = (byte)first[x];
                    row[(3 * x) + 1] = (byte)second[x];
                    row[(3 * x) + 2] = (byte)third[x];
                }

                break;

            case PixelFormat.Rgba32:
                for (var x = 0; x < width; x++)
                {
                    row[4 * x] = (byte)first[x];
                    row[(4 * x) + 1] = (byte)second[x];
                    row[(4 * x) + 2] = (byte)third[x];
                    row[(4 * x) + 3] = alpha.IsEmpty ? byte.MaxValue : (byte)alpha[x];
                }

                break;

            case PixelFormat.Rgba64:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                for (var x = 0; x < width; x++)
                {
                    samples[4 * x] = (ushort)first[x];
                    samples[(4 * x) + 1] = (ushort)second[x];
                    samples[(4 * x) + 2] = (ushort)third[x];
                    samples[(4 * x) + 3] = alpha.IsEmpty ? ushort.MaxValue : (ushort)alpha[x];
                }

                break;
            }

            default:
                throw new InvalidOperationException($"The pixel format {format} is not a layout of the JPEG 2000 decoder.");
        }
    }
}
