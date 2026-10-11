using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG XL pixel decoder: decodes the sections of each frame that <see cref="JpegXlStructureParser"/> hands over and
/// writes the displayed frames to a <see cref="DecodedFrameSink"/>, in the default pixel format of the image.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// A frame of integer samples that is shown as it is stored is written without any floating-point computation. Every
/// other frame (a frame coded with VarDCT, a frame that is blended, a frame that later frames refer to) goes through
/// <see cref="JpegXlFrameRenderer"/>, on floating-point samples that are rounded to the depth of the pixel format.
/// </description></item>
/// <item><description>
/// Samples: integer samples of 8 or 16 bits are stored as they are; the other depths are scaled exactly to 8 bits (below 8)
/// or 16 bits, <c>(v * targetMax + max / 2) / max</c>; a sample outside the range of its depth is clamped. The alpha
/// channel is the first extra channel of that type; the other extra channels are decoded and dropped.
/// </description></item>
/// <item><description>
/// A still image ends with the input, not with its frame: metadata boxes may follow the codestream.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlDecoder : JpegXlDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private JpegXlStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private PooledBuffer? _row;
    private JpegXlFrameRenderer? _renderer;
    private uint _visibleFrameIndex;
    private uint _invisibleFrameIndex;
    private bool _stillFramePending;

    public JpegXlDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(JpegXlStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        var format = structure.DefaultPixelFormat;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.JpegXl, structure.Size, format, format, structure.Metadata.IccProfile);
    }

    public override bool OnFrame(JpegXlStructureParser structure, JpegXlFrameHeader frame, JpegXlToc toc, WebPPayloadBuffer sections)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(toc);
        ArgumentNullException.ThrowIfNull(sections);
        var sink = _sink ?? throw new InvalidOperationException("The JPEG XL header was not parsed.");
        var header = structure.Header;
        var isDisplayed = structure.IsDisplayed(frame);
        if (isDisplayed)
        {
            _visibleFrameIndex++;
            _invisibleFrameIndex = 0;
        }
        else
        {
            _invisibleFrameIndex++;
        }

        var canBeReferenced = JpegXlFrameRenderer.CanBeReferenced(frame);
        if (!isDisplayed && !canBeReferenced && frame.Type != JpegXlFrameHeader.TypeLf)
        {
            // A frame that is neither shown nor referred to by a later frame changes nothing
            JpegXlFrameDecoder.EnsureSupported(frame);
            return true;
        }

        // The pixels of a frame that is decoded without being shown (a reference, an LF frame, a layer), and those a shown
        // frame has beyond the image, are not charged with a frame: a few bytes can describe many of them
        var framePixels = (long)frame.Width * frame.Height;
        var hiddenPixels = isDisplayed ? framePixels - ((long)header.Width * header.Height) : framePixels;
        if (hiddenPixels > 0)
        {
            _context.Tracker.ChargeHiddenPixels(hiddenPixels);
        }

        var duration = header.HasAnimation ? new FrameDuration((long)frame.Duration * header.TicksPerSecondDenominator, header.TicksPerSecondNumerator) : FrameDuration.Zero;
        using (var decoded = JpegXlFrameDecoder.Decode(header, frame, toc, sections.Data, _renderer?.GetLfFrame(frame.LfLevel), _context.Scope, _context.CancellationToken))
        {
            var isUpsampled = frame.Upsampling != 1 || frame.ExtraChannelUpsampling.Any(factor => factor != 1);
            var isFiltered = frame.RestorationFilter.IsGaborEnabled || frame.RestorationFilter.EpfIterations != 0 || (frame.Flags & (JpegXlFrameHeader.FlagPatches | JpegXlFrameHeader.FlagNoise | JpegXlFrameHeader.FlagSplines)) != 0;
            if (isDisplayed && !canBeReferenced && !decoded.HasPlanes && !isUpsampled && !isFiltered && !header.IsXybEncoded && !frame.IsYCbCr && !JpegXlFrameRenderer.NeedsBlending(frame))
            {
                // Integer samples that are shown as they are stored: no floating-point value is involved
                sink.BeginFrame(duration);
                WriteFrame(sink, header, decoded.Modular);
            }
            else
            {
                _renderer ??= new JpegXlFrameRenderer(header, _context.Scope, _context.CancellationToken);
                using var canvas = _renderer.Render(decoded, frame, isDisplayed, _visibleFrameIndex, _invisibleFrameIndex);
                if (canvas is null)
                    return true;

                sink.BeginFrame(duration);
                WriteCanvas(sink, header, canvas, _renderer.OutputColorChannelCount);
            }
        }

        if (header.HasAnimation)
            return sink.EndImage();

        // The still image ends in OnEnd, after the metadata boxes that follow the codestream
        _stillFramePending = true;
        return true;
    }

    public override void OnEnd(JpegXlStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (_stillFramePending)
        {
            _stillFramePending = false;
            var sink = _sink!;
            sink.UpdateMetadata(structure.Metadata);
            sink.EndImage();
        }
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The JPEG XL file was not decoded.");
        return _sink!.Build(structure.Metadata, structure.Animation);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _row?.Dispose();
            _row = null;
            _renderer?.Dispose();
            _renderer = null;
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }

    private void WriteFrame(DecodedFrameSink sink, JpegXlImageHeader header, JpegXlModularImage channels)
    {
        var format = sink.SourcePixelFormat;
        var width = (int)header.Width;
        var height = (int)header.Height;
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        _row ??= _context.Scope.Rent(checked(width * bytesPerPixel), AllocationKind.DecoderState, clear: false);
        var row = _row.RawBuffer.AsSpan(0, width * bytesPerPixel);

        var colorCount = header.ColorChannelCount;
        var alphaIndex = header.AlphaChannelIndex;
        var alpha = alphaIndex >= 0 ? channels.Channels[colorCount + alphaIndex] : null;
        var colorMax = (1 << header.BitDepth.BitsPerSample) - 1;
        var alphaMax = alphaIndex >= 0 ? (1 << header.ExtraChannels[alphaIndex].BitDepth.BitsPerSample) - 1 : 0;
        var is16Bit = PixelFormats.GetBitsPerComponent(format) == 16;
        var targetMax = is16Bit ? ushort.MaxValue : byte.MaxValue;
        var first = channels.Channels[0];
        var second = colorCount == 3 ? channels.Channels[1] : first;
        var third = colorCount == 3 ? channels.Channels[2] : first;

        using var target = sink.LeaseCurrentFrame();
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
            }

            var a = first.GetRow(y);
            var b = second.GetRow(y);
            var c = third.GetRow(y);
            var alphaRow = alpha is null ? default : alpha.GetRow(y);
            switch (format)
            {
                case PixelFormat.Gray8:
                    for (var x = 0; x < width; x++)
                    {
                        row[x] = (byte)Scale(a[x], colorMax, targetMax);
                    }

                    break;

                case PixelFormat.Gray16:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                    for (var x = 0; x < width; x++)
                    {
                        samples[x] = (ushort)Scale(a[x], colorMax, targetMax);
                    }

                    break;
                }

                case PixelFormat.Rgb24:
                    for (var x = 0; x < width; x++)
                    {
                        row[x * 3] = (byte)Scale(a[x], colorMax, targetMax);
                        row[(x * 3) + 1] = (byte)Scale(b[x], colorMax, targetMax);
                        row[(x * 3) + 2] = (byte)Scale(c[x], colorMax, targetMax);
                    }

                    break;

                case PixelFormat.Rgba32:
                    for (var x = 0; x < width; x++)
                    {
                        row[x * 4] = (byte)Scale(a[x], colorMax, targetMax);
                        row[(x * 4) + 1] = (byte)Scale(b[x], colorMax, targetMax);
                        row[(x * 4) + 2] = (byte)Scale(c[x], colorMax, targetMax);
                        row[(x * 4) + 3] = alpha is null ? byte.MaxValue : (byte)Scale(alphaRow[x], alphaMax, targetMax);
                    }

                    break;

                default:
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                    for (var x = 0; x < width; x++)
                    {
                        samples[x * 4] = (ushort)Scale(a[x], colorMax, targetMax);
                        samples[(x * 4) + 1] = (ushort)Scale(b[x], colorMax, targetMax);
                        samples[(x * 4) + 2] = (ushort)Scale(c[x], colorMax, targetMax);
                        samples[(x * 4) + 3] = alpha is null ? ushort.MaxValue : (ushort)Scale(alphaRow[x], alphaMax, targetMax);
                    }

                    break;
                }
            }

            sink.WriteRow(target, y, row);
        }
    }

    /// <summary>Writes an image of floating-point samples: they are rounded to the depth of the pixel format.</summary>
    private void WriteCanvas(DecodedFrameSink sink, JpegXlImageHeader header, JpegXlImageBuffer canvas, int colorChannelCount)
    {
        var format = sink.SourcePixelFormat;
        var width = canvas.Width;
        var height = canvas.Height;
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        _row ??= _context.Scope.Rent(checked(width * bytesPerPixel), AllocationKind.DecoderState, clear: false);
        var row = _row.RawBuffer.AsSpan(0, width * bytesPerPixel);
        var is16Bit = PixelFormats.GetBitsPerComponent(format) == 16;
        var targetMax = is16Bit ? ushort.MaxValue : byte.MaxValue;
        var isGrayFormat = PixelFormats.IsGrayscale(format);
        var hasAlphaFormat = PixelFormats.HasAlpha(format);
        var alphaIndex = header.AlphaChannelIndex;
        var alphaChannel = alphaIndex >= 0 ? canvas.ColorChannelCount + alphaIndex : -1;

        using var target = sink.LeaseCurrentFrame();
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
            }

            // A grayscale image has a single color channel: it is replicated when the pixel format has three
            var first = canvas.GetRow(0, y);
            var second = colorChannelCount == 3 ? canvas.GetRow(1, y) : first;
            var third = colorChannelCount == 3 ? canvas.GetRow(2, y) : first;
            var alphaRow = alphaChannel >= 0 ? canvas.GetRow(alphaChannel, y) : default;
            if (is16Bit)
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                if (isGrayFormat)
                {
                    for (var x = 0; x < width; x++)
                    {
                        samples[x] = (ushort)ToInteger(first[x], targetMax);
                    }
                }
                else
                {
                    for (var x = 0; x < width; x++)
                    {
                        samples[x * 4] = (ushort)ToInteger(first[x], targetMax);
                        samples[(x * 4) + 1] = (ushort)ToInteger(second[x], targetMax);
                        samples[(x * 4) + 2] = (ushort)ToInteger(third[x], targetMax);
                        samples[(x * 4) + 3] = alphaChannel < 0 ? ushort.MaxValue : (ushort)ToInteger(alphaRow[x], targetMax);
                    }
                }
            }
            else if (isGrayFormat)
            {
                for (var x = 0; x < width; x++)
                {
                    row[x] = (byte)ToInteger(first[x], targetMax);
                }
            }
            else
            {
                var channels = hasAlphaFormat ? 4 : 3;
                for (var x = 0; x < width; x++)
                {
                    row[x * channels] = (byte)ToInteger(first[x], targetMax);
                    row[(x * channels) + 1] = (byte)ToInteger(second[x], targetMax);
                    row[(x * channels) + 2] = (byte)ToInteger(third[x], targetMax);
                    if (hasAlphaFormat)
                    {
                        row[(x * 4) + 3] = alphaChannel < 0 ? byte.MaxValue : (byte)ToInteger(alphaRow[x], targetMax);
                    }
                }
            }

            sink.WriteRow(target, y, row);
        }
    }

    /// <summary>Rounds a sample, where 1 is the largest value, to the nearest integer of a depth; a sample outside the range is clamped.</summary>
    private static int ToInteger(float value, int max)
    {
        var scaled = (value * max) + 0.5f;
        if (!(scaled > 0))
            return 0;

        return scaled >= max ? max : (int)scaled;
    }

    /// <summary>Scales a sample of a depth to another one exactly, to the nearest value; a sample outside its range is clamped.</summary>
    private static int Scale(int value, int max, int targetMax)
    {
        value = Math.Clamp(value, 0, max);
        return max == targetMax ? value : (int)((((long)value * targetMax) + (max / 2)) / max);
    }
}
