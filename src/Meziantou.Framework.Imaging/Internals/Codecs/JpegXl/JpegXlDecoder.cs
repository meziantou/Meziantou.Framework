using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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

    // The number of complete passes at each checkpoint of the frame that is being received
    private readonly List<int> _stages = [];

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
        // The rows are written as RGBA for the gray formats with alpha and 16-bit RGB
        var format = structure.DefaultPixelFormat;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.JpegXl, structure.Size, format, PixelFormatWidening.GetWidenedFormat(format), structure.Metadata.IccProfile);
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
        _stages.Clear();
        if (isDisplayed)
        {
            _visibleFrameIndex++;
            _invisibleFrameIndex = 0;
            _context.Sequential?.SetFramePassCount(frame.PassCount);
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
        using (var decoded = JpegXlFrameDecoder.Decode(header, frame, toc, sections.Data, _renderer?.GetLfFrame(frame.LfLevel), _context.Configuration.MaxDegreeOfParallelism, _context.Scope, _context.CancellationToken))
        {
            if (isDisplayed && CanWriteSamplesAsStored(header, frame, decoded, canBeReferenced))
            {
                sink.BeginFrame(duration);
                WriteFrame(sink, header, decoded.Modular);
            }
            else
            {
                _renderer ??= new JpegXlFrameRenderer(header, _context.Scope, _context.CancellationToken, _context.Configuration.MaxDegreeOfParallelism);
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

    /// <summary>
    /// The stages of a frame that can be shown before it is complete: once <c>LfGlobal</c>, the LF groups and <c>HfGlobal</c>
    /// are received (the image at one eighth of its resolution), then once every group of each pass but the last is.
    /// </summary>
    public override long[] GetPayloadCheckpoints(JpegXlStructureParser structure, JpegXlFrameHeader frame, JpegXlToc toc)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(toc);
        _stages.Clear();
        if (frame.Type != JpegXlFrameHeader.TypeRegular || toc.Sizes.Length == 1)
            return [];

        var lfGroupCount = (int)frame.LfGroupCount;
        var groupCount = (int)frame.GroupCount;
        var checkpoints = new List<long>(frame.PassCount);
        long boundary = 0;
        for (var section = 0; section < 2 + lfGroupCount; section++)
        {
            boundary = Math.Max(boundary, toc.Offsets[section] + toc.Sizes[section]);
        }

        for (var stage = 0; stage < frame.PassCount; stage++)
        {
            // The table of contents may be permuted: a stage ends with the last of its sections
            if (stage > 0)
            {
                var first = 2 + lfGroupCount + ((stage - 1) * groupCount);
                for (var section = first; section < first + groupCount; section++)
                {
                    boundary = Math.Max(boundary, toc.Offsets[section] + toc.Sizes[section]);
                }
            }

            // A stage that brings nothing before the end of the frame, or after the previous stage, is not shown
            if (boundary >= toc.TotalLength)
                break;

            if (boundary == 0)
                continue;

            if (checkpoints.Count > 0 && checkpoints[^1] == boundary)
            {
                _stages.RemoveAt(_stages.Count - 1);
                checkpoints.RemoveAt(checkpoints.Count - 1);
            }

            _stages.Add(stage);
            checkpoints.Add(boundary);
        }

        return [.. checkpoints];
    }

    public override void OnFramePartial(JpegXlStructureParser structure, JpegXlFrameHeader frame, JpegXlToc toc, WebPPayloadBuffer sections, int checkpoint)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(toc);
        ArgumentNullException.ThrowIfNull(sections);
        var sink = _sink ?? throw new InvalidOperationException("The JPEG XL header was not parsed.");
        if (!sink.WantsRefinement)
            return;

        // The passes that are complete: none at the first stage, which only has the LF image
        var passCount = _stages[checkpoint];
        var header = structure.Header;

        // An intermediate rendering decodes the frame once more: its pixels count like the ones of a frame that is not shown
        _context.Tracker.ChargeHiddenPixels((long)frame.Width * frame.Height);
        using var decoded = JpegXlFrameDecoder.DecodePartial(header, frame, toc, sections.Data, passCount, _renderer?.GetLfFrame(frame.LfLevel), _context.Configuration.MaxDegreeOfParallelism, _context.Scope, _context.CancellationToken);
        if (decoded is null)
            return;

        var duration = header.HasAnimation ? new FrameDuration((long)frame.Duration * header.TicksPerSecondDenominator, header.TicksPerSecondNumerator) : FrameDuration.Zero;
        var downsampling = passCount == 0 ? 8 : 1 << frame.GetModularShiftBracket(passCount - 1).MinShift;
        if (CanWriteSamplesAsStored(header, frame, decoded, canBeReferenced: false))
        {
            sink.BeginRefinement(duration);
            WriteFrame(sink, header, decoded.Modular);
        }
        else
        {
            _renderer ??= new JpegXlFrameRenderer(header, _context.Scope, _context.CancellationToken, _context.Configuration.MaxDegreeOfParallelism);
            using var canvas = _renderer.Render(decoded, frame, isDisplayed: true, _visibleFrameIndex + 1, invisibleFrameIndex: 0, isIntermediate: true);
            if (canvas is null)
                return;

            sink.BeginRefinement(duration);
            WriteCanvas(sink, header, canvas, _renderer.OutputColorChannelCount);
        }

        sink.EndRefinement(downsampling, passCount);
    }

    /// <summary>Gets a value indicating whether the samples of a frame are integer samples that are shown as they are stored: no floating-point value is involved.</summary>
    private static bool CanWriteSamplesAsStored(JpegXlImageHeader header, JpegXlFrameHeader frame, JpegXlDecodedFrame decoded, bool canBeReferenced)
    {
        var isUpsampled = frame.Upsampling != 1 || frame.ExtraChannelUpsampling.Any(factor => factor != 1);
        var isFiltered = frame.RestorationFilter.IsGaborEnabled || frame.RestorationFilter.EpfIterations != 0 || (frame.Flags & (JpegXlFrameHeader.FlagPatches | JpegXlFrameHeader.FlagNoise | JpegXlFrameHeader.FlagSplines)) != 0;
        return !canBeReferenced && !decoded.HasPlanes && !isUpsampled && !isFiltered && !header.IsXybEncoded && !frame.IsYCbCr && !JpegXlFrameRenderer.NeedsBlending(frame);
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
                    WriteRgb24Row(a, b, c, colorMax, row);
                    break;

                case PixelFormat.Rgba32:
                    WriteRgba32Row(a, b, c, alphaRow, colorMax, alpha is null ? 0 : alphaMax, row);
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

    /// <summary>
    /// Writes a row of Rgb24 pixels from three channels whose largest value is <paramref name="max"/>. Samples of 8 bits are
    /// clamped and packed four pixels at a time; the bytes are those of <see cref="WriteRgb24RowScalar"/>.
    /// </summary>
    internal static void WriteRgb24Row(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, int max, Span<byte> row)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && max == byte.MaxValue)
        {
            // Each pixel packed in an integer, then the fourth byte of each removed: the 16 bytes stored overlap the next pixels,
            // which the next iteration writes
            var indices = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15, 15, 15, 15);
            for (; x <= red.Length - 4 && (x * 3) + 16 <= row.Length; x += 4)
            {
                var pixels = Pack(red.Slice(x, 4), green.Slice(x, 4), blue.Slice(x, 4), Vector128<int>.Zero);
                Vector128.Shuffle(pixels.AsByte(), indices).CopyTo(row.Slice(x * 3, 16));
            }
        }

        WriteRgb24RowScalar(red, green, blue, max, row, x);
    }

    /// <summary>The scalar reference of <see cref="WriteRgb24Row"/>, from the pixel <paramref name="start"/> on.</summary>
    internal static void WriteRgb24RowScalar(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, int max, Span<byte> row, int start = 0)
    {
        for (var x = start; x < red.Length; x++)
        {
            row[x * 3] = (byte)Scale(red[x], max, byte.MaxValue);
            row[(x * 3) + 1] = (byte)Scale(green[x], max, byte.MaxValue);
            row[(x * 3) + 2] = (byte)Scale(blue[x], max, byte.MaxValue);
        }
    }

    /// <summary>
    /// Writes a row of Rgba32 pixels from three channels whose largest value is <paramref name="max"/> and an alpha channel
    /// whose largest value is <paramref name="alphaMax"/>, or opaque pixels when <paramref name="alphaMax"/> is 0. Samples of
    /// 8 bits are clamped and packed four pixels at a time; the bytes are those of <see cref="WriteRgba32RowScalar"/>.
    /// </summary>
    internal static void WriteRgba32Row(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, ReadOnlySpan<int> alpha, int max, int alphaMax, Span<byte> row)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && max == byte.MaxValue && alphaMax is 0 or byte.MaxValue)
        {
            var opaque = Vector128.Create(byte.MaxValue << 24);
            for (; x <= red.Length - 4; x += 4)
            {
                var pixels = alphaMax == 0
                    ? Pack(red.Slice(x, 4), green.Slice(x, 4), blue.Slice(x, 4), opaque)
                    : Pack(red.Slice(x, 4), green.Slice(x, 4), blue.Slice(x, 4), Clamp(Vector128.Create(alpha.Slice(x, 4))) << 24);
                pixels.AsByte().CopyTo(row.Slice(x * 4, 16));
            }
        }

        WriteRgba32RowScalar(red, green, blue, alpha, max, alphaMax, row, x);
    }

    /// <summary>The scalar reference of <see cref="WriteRgba32Row"/>, from the pixel <paramref name="start"/> on.</summary>
    internal static void WriteRgba32RowScalar(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, ReadOnlySpan<int> alpha, int max, int alphaMax, Span<byte> row, int start = 0)
    {
        for (var x = start; x < red.Length; x++)
        {
            row[x * 4] = (byte)Scale(red[x], max, byte.MaxValue);
            row[(x * 4) + 1] = (byte)Scale(green[x], max, byte.MaxValue);
            row[(x * 4) + 2] = (byte)Scale(blue[x], max, byte.MaxValue);
            row[(x * 4) + 3] = alphaMax == 0 ? byte.MaxValue : (byte)Scale(alpha[x], alphaMax, byte.MaxValue);
        }
    }

    /// <summary>Packs four pixels of 8-bit samples, little-endian, red in the lowest byte; <paramref name="high"/> holds the fourth byte.</summary>
    private static Vector128<int> Pack(ReadOnlySpan<int> red, ReadOnlySpan<int> green, ReadOnlySpan<int> blue, Vector128<int> high)
        => Clamp(Vector128.Create(red)) | (Clamp(Vector128.Create(green)) << 8) | (Clamp(Vector128.Create(blue)) << 16) | high;

    /// <summary>Clamps four samples to the range of a byte, as <see cref="Scale"/> does when the sample has 8 bits.</summary>
    private static Vector128<int> Clamp(Vector128<int> value) => Vector128.Min(Vector128.Max(value, Vector128<int>.Zero), Vector128.Create((int)byte.MaxValue));

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
                WriteCanvasRow(first, second, third, alphaRow, hasAlphaFormat, alphaChannel >= 0, row);
            }

            sink.WriteRow(target, y, row);
        }
    }

    /// <summary>
    /// Writes a row of Rgb24 or Rgba32 pixels from floating-point samples (opaque when the image has no alpha channel),
    /// four pixels at a time: each lane rounds and clamps as <see cref="ToInteger"/> does, and the bytes are those of
    /// <see cref="WriteCanvasRowScalar"/>.
    /// </summary>
    internal static void WriteCanvasRow(ReadOnlySpan<float> first, ReadOnlySpan<float> second, ReadOnlySpan<float> third, ReadOnlySpan<float> alpha, bool hasAlphaFormat, bool hasAlpha, Span<byte> row)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var opaque = Vector128.Create(byte.MaxValue << 24);
            var indices = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15, 15, 15, 15);
            var pixelSize = hasAlphaFormat ? 4 : 3;
            for (; x <= first.Length - 4 && (x * pixelSize) + 16 <= row.Length; x += 4)
            {
                var high = hasAlphaFormat && hasAlpha ? ToByte(Vector128.Create(alpha.Slice(x, 4))) << 24 : opaque;
                var pixels = ToByte(Vector128.Create(first.Slice(x, 4))) | (ToByte(Vector128.Create(second.Slice(x, 4))) << 8) | (ToByte(Vector128.Create(third.Slice(x, 4))) << 16) | high;
                var bytes = hasAlphaFormat ? pixels.AsByte() : Vector128.Shuffle(pixels.AsByte(), indices);
                bytes.CopyTo(row.Slice(x * pixelSize, 16));
            }
        }

        WriteCanvasRowScalar(first, second, third, alpha, hasAlphaFormat, hasAlpha, row, x);

        // ToInteger for 8-bit samples
        static Vector128<int> ToByte(Vector128<float> value)
        {
            var max = Vector128.Create((float)byte.MaxValue);
            var scaled = (value * max) + Vector128.Create(0.5f);
            var result = Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(scaled, max).AsInt32(), Vector128.Create((int)byte.MaxValue), Vector128.ConvertToInt32(scaled));
            return Vector128.ConditionalSelect(Vector128.GreaterThan(scaled, Vector128<float>.Zero).AsInt32(), result, Vector128<int>.Zero);
        }
    }

    /// <summary>The scalar reference of <see cref="WriteCanvasRow"/>, from the pixel <paramref name="start"/> on.</summary>
    internal static void WriteCanvasRowScalar(ReadOnlySpan<float> first, ReadOnlySpan<float> second, ReadOnlySpan<float> third, ReadOnlySpan<float> alpha, bool hasAlphaFormat, bool hasAlpha, Span<byte> row, int start = 0)
    {
        var channels = hasAlphaFormat ? 4 : 3;
        for (var x = start; x < first.Length; x++)
        {
            row[x * channels] = (byte)ToInteger(first[x], byte.MaxValue);
            row[(x * channels) + 1] = (byte)ToInteger(second[x], byte.MaxValue);
            row[(x * channels) + 2] = (byte)ToInteger(third[x], byte.MaxValue);
            if (hasAlphaFormat)
            {
                row[(x * 4) + 3] = hasAlpha ? (byte)ToInteger(alpha[x], byte.MaxValue) : byte.MaxValue;
            }
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
