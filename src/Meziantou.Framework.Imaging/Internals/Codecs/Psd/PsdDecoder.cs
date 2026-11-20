using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The PSD pixel sink, shared by eager loads and sequential readers: <see cref="PsdStructureParser"/> gives it each row of
/// each channel of the composite image, which it gathers into whole pixels, and it writes the rows of the image once the
/// last channel is read.
/// </summary>
/// <remarks>
/// <para>
/// The image data stores the channels one after the other, so a pixel is only known once every channel is read: the
/// samples of the color channels and of the transparency channel are gathered in a staging image of the samples as they
/// are stored (native byte order), and turned into the rows of the source layout at the end. A bitmap sample of 1 is
/// black. The channels after the color channels and the transparency channel (saved selections, spot colors) are not
/// part of the composite and are skipped.
/// </para>
/// <para>
/// When the composite has a transparency channel, Photoshop stores its colors blended over white ("matted"): a stored
/// sample is <c>c × α + w × (1 − α)</c>, <c>w</c> being white (the largest sample; 128 for the a* and b* samples of Lab
/// images; no ink for CMYK images, whose samples are stored inverted). The decoder removes the matte:
/// <c>c = (s − w × (1 − α)) / α</c>, rounded to the nearest integer and clipped to the range of the samples (not clipped
/// for floating-point samples). Where α is 0 the colors are undefined, and the stored samples are kept, as ImageMagick reads them.
/// A floating-point NaN is read quiet (its sign and payload kept), as a conversion to another pixel format makes it.
/// </para>
/// <para>
/// CMYK samples are stored inverted (the largest value is no ink). They are loaded as CMYK pixels when the request asks
/// for them (or for another format with <see cref="PixelConversionOptions.CmykConversion"/>), or converted to RGB with
/// <see cref="CmykConversionOptions"/>, as the other CMYK decoders do. Lab samples (L* from 0 to 100 as 0 to 255, a* and b*
/// offset by 128) are only loaded with <see cref="CieLabConversionOptions"/>.
/// </para>
/// </remarks>
internal sealed class PsdDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private PsdStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private Image? _staging;
    private PooledBuffer? _output;
    private PooledBuffer? _inks;
    private IccPipeline? _colorConversion;
    private Layout _layout;
    private int _samplesPerPixel;
    private bool _hasAlphaChannel;

    public PsdDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        _request = request;
        _context = context;
    }

    private enum Layout
    {
        Gray,
        Rgb,
        Indexed,
        CmykInks,
        CmykConverted,
        Lab,
        Float,
    }

    public void OnHeaderComplete(PsdStructureParser structure)
    {
        if (structure.HasNoRealComposite)
            throw PsdFormat.Unsupported("The PSD file has no composite image (it was saved with \"Maximize Compatibility\" off): its image data is blank, and the library does not render layers.", "No composite image");

        _structure = structure;
        var profile = structure.Metadata.IccProfile;
        var depth = structure.Depth;
        var hasAlpha = structure.HasAlpha;
        var is16Bit = depth == 16;
        PixelFormat sourceFormat;
        switch (structure.Mode)
        {
            case PsdFormat.ModeGrayscale or PsdFormat.ModeRgb when depth == 32:
                _layout = Layout.Float;
                sourceFormat = PixelFormat.RgbaFloat;
                break;

            case PsdFormat.ModeBitmap or PsdFormat.ModeGrayscale or PsdFormat.ModeDuotone:
                _layout = Layout.Gray;
                sourceFormat = (hasAlpha, is16Bit) switch
                {
                    (false, false) => PixelFormat.Gray8,
                    (false, true) => PixelFormat.Gray16,
                    (true, false) => PixelFormat.Rgba32,
                    (true, true) => PixelFormat.Rgba64,
                };
                break;

            case PsdFormat.ModeRgb:
                _layout = Layout.Rgb;
                sourceFormat = is16Bit ? PixelFormat.Rgba64 : hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
                break;

            case PsdFormat.ModeIndexed:
                _layout = Layout.Indexed;
                sourceFormat = hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
                break;

            case PsdFormat.ModeCmyk:
            {
                var requestsCmyk = _request.PixelFormat is { } requested && PixelFormats.IsCmyk(requested);
                if (_request.CmykConversion is null || requestsCmyk)
                {
                    // The inks are the rows, labeled with the CMYK profile: the sink converts them when the request asks
                    // for another pixel format with a CMYK conversion
                    if (!requestsCmyk && (_request.PixelFormat is null || _request.Conversion.CmykConversion is null))
                        throw PsdFormat.Unsupported("The PSD file stores CMYK samples, which are only loaded as CMYK pixels (Cmyk32, Cmyk64) or with a color conversion: load the image as Cmyk32 or Cmyk64, or set the CmykConversion property of the decode options.", "CMYK samples without color conversion");

                    if (hasAlpha)
                        throw PsdFormat.Unsupported("The PSD file stores CMYK samples with transparency, which the CMYK pixel formats cannot hold: set the CmykConversion property of the decode options to load it as RGBA.", "CMYK samples with transparency as CMYK pixels");

                    _layout = Layout.CmykInks;
                    sourceFormat = is16Bit ? PixelFormat.Cmyk64 : PixelFormat.Cmyk32;
                }
                else
                {
                    // Only a CMYK profile is adopted for CMYK samples. The rows are converted to the destination profile,
                    // which labels the image
                    var conversion = _request.CmykConversion;
                    var transform = conversion.CreateTransform(profile) ?? throw PsdFormat.Unsupported("The CMYK PSD file has no CMYK color profile and CmykConversionOptions.AssumedSourceProfile is null.", "CMYK samples without color profile");
                    _colorConversion = transform.Pipeline;
                    profile = conversion.DestinationProfile;
                    _layout = Layout.CmykConverted;
                    sourceFormat = is16Bit ? PixelFormat.Rgba64 : hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
                }

                break;
            }

            case PsdFormat.ModeLab:
            {
                // L*a*b* colors are colorimetric: they are converted to the destination profile, which labels the image
                var conversion = _request.CieLabConversion ?? throw PsdFormat.Unsupported("The PSD file stores Lab samples, which are only loaded with a color conversion: set the CieLabConversion property of the decode options.", "Lab samples without color conversion");
                _colorConversion = conversion.CreatePipeline(IccMatrixStage.CreateScale(100, 255, 255, 0, -128, -128), adaptation: null);
                profile = conversion.DestinationProfile;
                _layout = Layout.Lab;
                sourceFormat = hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
                break;
            }

            default:
                throw new InvalidOperationException("The color mode was not validated.");
        }

        _hasAlphaChannel = hasAlpha && structure.Mode != PsdFormat.ModeIndexed;
        _samplesPerPixel = _layout == Layout.Float ? 4 : structure.ColorChannelCount + (_hasAlphaChannel ? 1 : 0);
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Psd, structure.Size, structure.DefaultPixelFormat, sourceFormat, profile);
        var bytesPerSample = depth switch
        {
            32 => 4,
            16 => 2,
            _ => 1,
        };

        _staging = Image.CreateForDecoder(GetStagingFormat(_samplesPerPixel * bytesPerSample), _context.Configuration, structure.Size, _context.Scope);
        var scope = _context.Scope;
        _output = scope.Rent(checked(structure.Width * PixelFormats.GetBytesPerPixel(sourceFormat)), AllocationKind.DecoderState, clear: false);
        if (_layout == Layout.CmykConverted)
        {
            _inks = scope.Rent(checked(structure.Width * 5 * bytesPerSample), AllocationKind.DecoderState, clear: false);
        }
    }

    /// <summary>Stores a row of a channel in the staging image.</summary>
    /// <param name="structure">The parser.</param>
    /// <param name="channel">The index of the channel in the file.</param>
    /// <param name="y">The row.</param>
    /// <param name="row">The samples of the row, as stored (big-endian, 1-bit samples packed most significant bit first).</param>
    public void OnRow(PsdStructureParser structure, int channel, int y, ReadOnlySpan<byte> row)
    {
        var colors = structure.ColorChannelCount;
        int slot;
        if (channel < colors)
        {
            slot = channel;
        }
        else if (_hasAlphaChannel && channel == colors)
        {
            slot = _samplesPerPixel - 1;
        }
        else
        {
            return;
        }

        using var lease = _staging!.FrameCore(0).GetStorage().AcquireLease();
        var target = lease.GetRowBytes(y);
        var width = structure.Width;
        var stride = _samplesPerPixel;
        switch (structure.Depth)
        {
            case 1:
                for (var x = 0; x < width; x++)
                {
                    target[x] = ((row[x >> 3] >> (7 - (x & 7))) & 1) != 0 ? (byte)0 : byte.MaxValue;
                }

                break;

            case 8:
                for (var x = 0; x < width; x++)
                {
                    target[(x * stride) + slot] = row[x];
                }

                break;

            case 16:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(target));
                for (var x = 0; x < width; x++)
                {
                    samples[(x * stride) + slot] = BinaryPrimitives.ReadUInt16BigEndian(row[(x * 2)..]);
                }

                break;
            }

            default:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, float>(target));
                for (var x = 0; x < width; x++)
                {
                    samples[(x * stride) + slot] = QuietNaN(BinaryPrimitives.ReadSingleBigEndian(row[(x * 4)..]));
                }

                break;
            }
        }
    }

    public void OnEnd(PsdStructureParser structure)
    {
        var sink = _sink ?? throw new InvalidOperationException("The PSD header was not parsed.");
        var staging = _staging!;
        var frame = staging.FrameCore(0);
        var height = structure.Height;
        using (var source = frame.GetStorage().AcquireLease())
        {
            if (_layout == Layout.Float)
            {
                // The samples are completed in place: a tone mapping needs the peak of the whole image before the rows go
                for (var y = 0; y < height; y++)
                {
                    CompleteFloats(structure, unsafe(MemoryMarshal.Cast<byte, float>(source.GetRowBytes(y))));
                }
            }
        }

        if (_layout == Layout.Float && !sink.Plan.IsIdentity)
        {
            Image.PrepareConversion(sink.Plan, [frame]);
        }

        sink.BeginFrame(FrameDuration.Zero);
        using (var source = frame.GetStorage().AcquireLease())
        using (var destination = sink.LeaseCurrentFrame())
        {
            for (var y = 0; y < height; y++)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                var row = source.GetRowBytes(y);
                sink.WriteRow(destination, y, _layout == Layout.Float ? row : ConvertRow(structure, row));
            }
        }

        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The PSD data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
        _staging?.Dispose();
        _staging = null;
        _output?.Dispose();
        _output = null;
        _inks?.Dispose();
        _inks = null;
    }

    /// <summary>Gets the smallest pixel format whose pixels hold the stored samples of a pixel: the staging image only holds bytes.</summary>
    private static PixelFormat GetStagingFormat(int bytesPerPixel) => bytesPerPixel switch
    {
        1 => PixelFormat.Gray8,
        2 => PixelFormat.La16,
        3 => PixelFormat.Rgb24,
        4 => PixelFormat.Rgba32,
        5 or 6 => PixelFormat.Rgb48,
        7 or 8 => PixelFormat.Rgba64,
        _ => PixelFormat.RgbaFloat,
    };

    /// <summary>
    /// Sets the quiet bit of a NaN, keeping its sign and payload: a conversion between floating-point formats goes through
    /// a double, which quiets a signaling NaN, so the rows give the same bits in every pixel format.
    /// </summary>
    private static float QuietNaN(float value) => float.IsNaN(value) ? BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) | 0x0040_0000) : value;

    /// <summary>
    /// Removes the matte of a sample blended over white: <c>(s × max − w × (max − α)) / α</c>, rounded to the nearest integer and
    /// clipped to [0, max]; the stored sample where α is 0.
    /// </summary>
    private static int Unmatte(int sample, int alpha, int white, int max)
    {
        if (alpha == max)
            return sample;

        if (alpha == 0)
            return sample;

        var numerator = ((long)sample * max) - ((long)white * (max - alpha));
        if (numerator <= 0)
            return 0;

        return (int)Math.Min(max, ((2 * numerator) + alpha) / (2L * alpha));
    }

    private void CompleteFloats(PsdStructureParser structure, Span<float> pixels)
    {
        var isGray = structure.ColorChannelCount == 1;
        for (var x = 0; x < structure.Width; x++)
        {
            var pixel = pixels.Slice(x * 4, 4);
            if (isGray)
            {
                pixel[1] = pixel[0];
                pixel[2] = pixel[0];
            }

            if (!_hasAlphaChannel)
            {
                pixel[3] = 1;
                continue;
            }

            var alpha = pixel[3];
            if (alpha is 1 or 0)
                continue;

            for (var c = 0; c < 3; c++)
            {
                pixel[c] = (float)((pixel[c] - (1.0 - alpha)) / alpha);
            }
        }
    }

    private ReadOnlySpan<byte> ConvertRow(PsdStructureParser structure, Span<byte> stored)
    {
        var width = structure.Width;
        var stride = _samplesPerPixel;
        var colors = structure.ColorChannelCount;
        var output = _output!.Span;
        if (structure.Depth == 16)
        {
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(stored))[..(width * stride)];
            if (_hasAlphaChannel)
            {
                RemoveMatte(samples, colors);
            }

            var words = unsafe(MemoryMarshal.Cast<byte, ushort>(output));
            switch (_layout)
            {
                case Layout.Gray when !_hasAlphaChannel:
                    return stored[..(width * stride * 2)];

                case Layout.Gray:
                    for (var x = 0; x < width; x++)
                    {
                        var gray = samples[x * 2];
                        words[x * 4] = gray;
                        words[(x * 4) + 1] = gray;
                        words[(x * 4) + 2] = gray;
                        words[(x * 4) + 3] = samples[(x * 2) + 1];
                    }

                    return output[..(width * 8)];

                case Layout.Rgb when _hasAlphaChannel:
                    return stored[..(width * stride * 2)];

                case Layout.Rgb:
                    for (var x = 0; x < width; x++)
                    {
                        words[x * 4] = samples[x * 3];
                        words[(x * 4) + 1] = samples[(x * 3) + 1];
                        words[(x * 4) + 2] = samples[(x * 3) + 2];
                        words[(x * 4) + 3] = ushort.MaxValue;
                    }

                    return output[..(width * 8)];

                case Layout.CmykInks:
                    for (var i = 0; i < width * 4; i++)
                    {
                        words[i] = (ushort)(ushort.MaxValue - samples[i]);
                    }

                    return output[..(width * 8)];

                case Layout.CmykConverted:
                {
                    var inks = unsafe(MemoryMarshal.Cast<byte, ushort>(_inks!.Span))[..(width * 5)];
                    for (var x = 0; x < width; x++)
                    {
                        for (var c = 0; c < 4; c++)
                        {
                            inks[(x * 5) + c] = (ushort)(ushort.MaxValue - samples[(x * stride) + c]);
                        }

                        inks[(x * 5) + 4] = _hasAlphaChannel ? samples[(x * stride) + 4] : ushort.MaxValue;
                    }

                    ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_colorConversion!, inks, words[..(width * 4)], 5, 4);
                    return output[..(width * 8)];
                }

                default:
                    throw new InvalidOperationException("The layout has no 16-bit samples.");
            }
        }

        var bytes = stored[..(width * stride)];
        if (_hasAlphaChannel)
        {
            RemoveMatte(bytes, colors, isLab: _layout == Layout.Lab);
        }

        switch (_layout)
        {
            case Layout.Gray when !_hasAlphaChannel:
            case Layout.Rgb:
                return bytes;

            case Layout.Gray:
                for (var x = 0; x < width; x++)
                {
                    var gray = bytes[x * 2];
                    output[x * 4] = gray;
                    output[(x * 4) + 1] = gray;
                    output[(x * 4) + 2] = gray;
                    output[(x * 4) + 3] = bytes[(x * 2) + 1];
                }

                return output[..(width * 4)];

            case Layout.Indexed:
            {
                var table = structure.ColorTable!;
                var transparent = structure.TransparentIndex;
                var pixelLength = transparent is null ? 3 : 4;
                for (var x = 0; x < width; x++)
                {
                    var index = bytes[x];
                    var offset = x * pixelLength;
                    output[offset] = table[index];
                    output[offset + 1] = table[256 + index];
                    output[offset + 2] = table[512 + index];
                    if (transparent is { } value)
                    {
                        output[offset + 3] = index == value ? (byte)0 : byte.MaxValue;
                    }
                }

                return output[..(width * pixelLength)];
            }

            case Layout.CmykInks:
                for (var i = 0; i < width * 4; i++)
                {
                    output[i] = (byte)(byte.MaxValue - bytes[i]);
                }

                return output[..(width * 4)];

            case Layout.CmykConverted:
            {
                var inks = _inks!.Span[..(width * stride)];
                for (var i = 0; i < inks.Length; i++)
                {
                    inks[i] = i % stride < 4 ? (byte)(byte.MaxValue - bytes[i]) : bytes[i];
                }

                var length = width * (stride - 1);
                ColorConversionKernels.Convert<byte, IccByteSample>(_colorConversion!, inks, output[..length], stride, stride - 1);
                return output[..length];
            }

            case Layout.Lab:
            {
                var length = width * stride;
                ColorConversionKernels.Convert<byte, IccByteSample>(_colorConversion!, bytes, output[..length], stride, stride);
                return output[..length];
            }

            default:
                throw new InvalidOperationException("The layout has no 8-bit samples.");
        }
    }

    private static void RemoveMatte(Span<byte> samples, int colors, bool isLab)
    {
        var stride = colors + 1;
        for (var offset = 0; offset < samples.Length; offset += stride)
        {
            var alpha = samples[offset + colors];
            for (var c = 0; c < colors; c++)
            {
                var white = isLab && c > 0 ? 128 : byte.MaxValue;
                samples[offset + c] = (byte)Unmatte(samples[offset + c], alpha, white, byte.MaxValue);
            }
        }
    }

    private static void RemoveMatte(Span<ushort> samples, int colors)
    {
        var stride = colors + 1;
        for (var offset = 0; offset < samples.Length; offset += stride)
        {
            var alpha = samples[offset + colors];
            for (var c = 0; c < colors; c++)
            {
                samples[offset + c] = (ushort)Unmatte(samples[offset + c], alpha, ushort.MaxValue, ushort.MaxValue);
            }
        }
    }
}
