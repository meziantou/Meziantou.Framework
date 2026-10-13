using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns decoded component planes of 12-bit samples into output rows: chroma upsampling to the frame size, color
/// conversion at the precision of the samples, then the scaling of every sample to 16 bits, in the source layout of the
/// sink (<see cref="PixelFormat.Gray16"/>, or opaque <see cref="PixelFormat.Rgba64"/> for the other frames, as there is
/// no 16-bit RGB format). When the sink's plan is an identity, rows are produced directly in the frame storage.
/// </summary>
/// <remarks>
/// <para>
/// The upsampling filters and the color conversion are those of 8-bit samples (<see cref="JpegRowWriter"/>), with the
/// chroma samples centered on <c>2^(P - 1)</c> and the results clamped to <c>0..2^P - 1</c> for a precision <c>P</c>: a
/// 12-bit file decodes like in libjpeg-turbo, whose 12-bit samples are those of its portable anymaps.
/// </para>
/// <para>
/// A sample <c>s</c> becomes <c>(s * 65535 + (2^P - 1) / 2) / (2^P - 1)</c> (integer division): the nearest 16-bit value of
/// the same fraction of the range, so that black and white are exact.
/// </para>
/// <para>
/// Four components are inks: the amounts (the complement of the stored samples, see <see cref="JpegRowWriter"/>) are
/// scaled to 16 bits, then converted to RGB by the 16-bit ICC conversion.
/// </para>
/// <para>The kernels are scalar.</para>
/// </remarks>
internal sealed class JpegDeepRowWriter : JpegBandWriter
{
    private readonly JpegFrameComponent[] _components;
    private readonly DecodedFrameSink _sink;
    private readonly ImageColorModel _colorModel;

    // The conversion of the inks of a four-component frame to RGB; null for the other frames
    private readonly IccPipeline? _inkConversion;
    private readonly int _width;
    private readonly int _precision;
    private readonly int _max;
    private readonly PooledBuffer?[] _upsampled;

    // A row of the source layout, when the sink converts it
    private PooledBuffer? _sourceRow;

    // Three samples per pixel at the precision of the frame: converted colors, or cyan, magenta and yellow amounts
    private PooledBuffer? _colorRow;

    // Five 16-bit samples per pixel: the ink amounts and an opaque alpha sample, which the conversion copies
    private PooledBuffer? _inkRow;

    public JpegDeepRowWriter(JpegFrameComponent[] components, DecodedFrameSink sink, ImageColorModel colorModel, IccPipeline? inkConversion, int width, int height, int maxVertical, int precision, AllocationScope scope)
        : base(height, maxVertical)
    {
        if ((components.Length == 4) != (inkConversion is not null))
            throw new ArgumentException("A four-component frame, and only it, needs the conversion of its inks.", nameof(inkConversion));

        _components = components;
        _sink = sink;
        _colorModel = colorModel;
        _inkConversion = inkConversion;
        _width = width;
        _precision = precision;
        _max = (1 << precision) - 1;
        _upsampled = new PooledBuffer?[components.Length];
        try
        {
            foreach (var component in components)
            {
                if (component.HorizontalRatio != 1 || component.VerticalRatio != 1)
                {
                    _upsampled[component.Index] = scope.Rent(checked(width * sizeof(ushort)), AllocationKind.DecoderState, clear: false);
                }
            }

            if (!sink.Plan.IsIdentity)
            {
                _sourceRow = scope.Rent(checked(width * PixelFormats.GetBytesPerPixel(sink.SourcePixelFormat)), AllocationKind.DecoderState, clear: false);
            }

            if (components.Length > 1)
            {
                _colorRow = scope.Rent(checked(width * 3 * sizeof(ushort)), AllocationKind.DecoderState, clear: false);
            }

            if (components.Length == 4)
            {
                _inkRow = scope.Rent(checked(width * 5 * sizeof(ushort)), AllocationKind.DecoderState, clear: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected override void WriteRows(int first, int last)
    {
        using var lease = _sink.LeaseCurrentFrame();
        for (var y = first; y < last; y++)
        {
            WriteRow(lease, y);
        }
    }

    protected override void Dispose(bool disposing)
    {
        for (var i = 0; i < _upsampled.Length; i++)
        {
            _upsampled[i]?.Dispose();
            _upsampled[i] = null;
        }

        _sourceRow?.Dispose();
        _sourceRow = null;
        _colorRow?.Dispose();
        _colorRow = null;
        _inkRow?.Dispose();
        _inkRow = null;
        base.Dispose(disposing);
    }

    /// <summary>Scales samples of a precision to 16 bits: <c>(s * 65535 + max / 2) / max</c>.</summary>
    internal static void Scale(ReadOnlySpan<ushort> samples, Span<ushort> destination, int max)
    {
        for (var x = 0; x < samples.Length; x++)
        {
            destination[x] = Scale(samples[x], max);
        }
    }

    /// <summary>Converts full-range YCbCr samples of a precision to interleaved RGB samples of the same precision.</summary>
    internal static void ConvertYCbCr(ReadOnlySpan<ushort> luma, ReadOnlySpan<ushort> blue, ReadOnlySpan<ushort> red, Span<ushort> destination, int precision)
    {
        var center = 1 << (precision - 1);
        var max = (1 << precision) - 1;
        for (var x = 0; x < luma.Length; x++)
        {
            int yValue = luma[x];
            var cb = blue[x] - center;
            var cr = red[x] - center;
            destination[3 * x] = (ushort)Math.Clamp(yValue + (((91881 * cr) + 32768) >> 16), 0, max);
            destination[(3 * x) + 1] = (ushort)Math.Clamp(yValue + (((-22554 * cb) - (46802 * cr) + 32768) >> 16), 0, max);
            destination[(3 * x) + 2] = (ushort)Math.Clamp(yValue + (((116130 * cb) + 32768) >> 16), 0, max);
        }
    }

    /// <summary>Horizontal triangle filter (factor 2).</summary>
    internal static void UpsampleHorizontal(ReadOnlySpan<ushort> samples, Span<ushort> output)
    {
        var last = samples.Length - 1;
        for (var j = 0; j <= last; j++)
        {
            var center = 3 * samples[j];
            var x = 2 * j;
            output[x] = (ushort)((center + samples[Math.Max(j - 1, 0)] + 1) >> 2);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (ushort)((center + samples[Math.Min(j + 1, last)] + 2) >> 2);
            }
        }
    }

    /// <summary>Vertical triangle filter (factor 2): <c>(3 near + far + bias) &gt;&gt; 2</c>.</summary>
    internal static void UpsampleVertical(ReadOnlySpan<ushort> near, ReadOnlySpan<ushort> far, int bias, Span<ushort> output)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = (ushort)(((3 * near[x]) + far[x] + bias) >> 2);
        }
    }

    /// <summary>Both factors 2: column sums <c>c = 3 near + far</c>, then the horizontal triangle filter of the sums.</summary>
    internal static void UpsampleBoth(ReadOnlySpan<ushort> near, ReadOnlySpan<ushort> far, Span<ushort> output)
    {
        var last = near.Length - 1;
        var previous = (3 * near[0]) + far[0];
        var current = previous;
        for (var j = 0; j <= last; j++)
        {
            var next = j < last ? (3 * near[j + 1]) + far[j + 1] : current;
            var x = 2 * j;
            output[x] = (ushort)(((3 * current) + previous + 8) >> 4);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (ushort)(((3 * current) + next + 7) >> 4);
            }

            previous = current;
            current = next;
        }
    }

    private static ushort Scale(int sample, int max) => (ushort)(((sample * 65535) + (max >> 1)) / max);

    private static void Replicate(ReadOnlySpan<ushort> samples, int factor, Span<ushort> output)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = samples[x / factor];
        }
    }

    private void WriteRow(scoped in PixelLease lease, int y)
    {
        var identity = _sink.Plan.IsIdentity;
        var length = _width * PixelFormats.GetBytesPerPixel(_sink.SourcePixelFormat);
        var rowBytes = identity ? lease.GetRowBytes(y)[..length] : _sourceRow!.RawBuffer.AsSpan(0, length);
        var row = unsafe(MemoryMarshal.Cast<byte, ushort>(rowBytes));
        var max = _max;
        if (_components.Length == 1)
        {
            // Grayscale: the scaled luma samples are the output (a single-component frame is never subsampled)
            Scale(_components[0].GetDeepRow(y)[.._width], row, max);
        }
        else
        {
            var c0 = GetSamples(_components[0], y);
            var c1 = GetSamples(_components[1], y);
            var c2 = GetSamples(_components[2], y);
            var color = unsafe(MemoryMarshal.Cast<byte, ushort>(_colorRow!.RawBuffer.AsSpan(0, _width * 3 * sizeof(ushort))));
            if (_inkConversion is not null)
            {
                var black = GetSamples(_components[3], y);
                var inks = unsafe(MemoryMarshal.Cast<byte, ushort>(_inkRow!.RawBuffer.AsSpan(0, _width * 5 * sizeof(ushort))));
                if (_colorModel == ImageColorModel.Ycck)
                {
                    // The color conversion gives the cyan, magenta and yellow amounts; only the black samples are complemented
                    ConvertYCbCr(c0, c1, c2, color, _precision);
                    for (var x = 0; x < black.Length; x++)
                    {
                        inks[5 * x] = Scale(color[3 * x], max);
                        inks[(5 * x) + 1] = Scale(color[(3 * x) + 1], max);
                        inks[(5 * x) + 2] = Scale(color[(3 * x) + 2], max);
                        inks[(5 * x) + 3] = Scale(max - black[x], max);
                        inks[(5 * x) + 4] = ushort.MaxValue;
                    }
                }
                else
                {
                    for (var x = 0; x < black.Length; x++)
                    {
                        inks[5 * x] = Scale(max - c0[x], max);
                        inks[(5 * x) + 1] = Scale(max - c1[x], max);
                        inks[(5 * x) + 2] = Scale(max - c2[x], max);
                        inks[(5 * x) + 3] = Scale(max - black[x], max);
                        inks[(5 * x) + 4] = ushort.MaxValue;
                    }
                }

                // Four inks become three colors; the fifth sample is copied as the alpha sample
                ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_inkConversion, inks, row, 5, 4);
            }
            else
            {
                if (_colorModel == ImageColorModel.Rgb)
                {
                    for (var x = 0; x < c0.Length; x++)
                    {
                        color[3 * x] = c0[x];
                        color[(3 * x) + 1] = c1[x];
                        color[(3 * x) + 2] = c2[x];
                    }
                }
                else
                {
                    ConvertYCbCr(c0, c1, c2, color, _precision);
                }

                for (var x = 0; x < c0.Length; x++)
                {
                    row[4 * x] = Scale(color[3 * x], max);
                    row[(4 * x) + 1] = Scale(color[(3 * x) + 1], max);
                    row[(4 * x) + 2] = Scale(color[(3 * x) + 2], max);
                    row[(4 * x) + 3] = ushort.MaxValue;
                }
            }
        }

        if (!identity)
        {
            _sink.WriteRow(lease, y, rowBytes);
        }
    }

    /// <summary>Gets the samples of a component for an output row, upsampled to the frame width (the rules of <see cref="JpegRowWriter"/>).</summary>
    private ReadOnlySpan<ushort> GetSamples(JpegFrameComponent component, int y)
    {
        var horizontal = component.HorizontalRatio;
        var vertical = component.VerticalRatio;
        if (horizontal == 1 && vertical == 1)
            return component.GetDeepRow(y)[.._width];

        var output = unsafe(MemoryMarshal.Cast<byte, ushort>(_upsampled[component.Index]!.RawBuffer.AsSpan(0, _width * sizeof(ushort))));
        if (horizontal == 2 && component.Width <= 2)
        {
            Replicate(component.GetDeepRow(y / vertical), horizontal, output);
        }
        else if (vertical == 2 && horizontal <= 2)
        {
            var row = y >> 1;
            var upper = (y & 1) == 0;
            var other = Math.Clamp(upper ? row - 1 : row + 1, 0, component.Height - 1);
            var near = component.GetDeepRow(row);
            var far = component.GetDeepRow(other);
            if (horizontal == 2)
            {
                UpsampleBoth(near, far, output);
            }
            else
            {
                UpsampleVertical(near, far, upper ? 1 : 2, output);
            }
        }
        else if (vertical == 1 && horizontal == 2)
        {
            UpsampleHorizontal(component.GetDeepRow(y), output);
        }
        else
        {
            Replicate(component.GetDeepRow(y / vertical), horizontal, output);
        }

        return output;
    }
}
