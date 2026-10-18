using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;
using IccProfile = Meziantou.Framework.Imaging.Metadata.IccProfile;
using IccProfileColorSpace = Meziantou.Framework.Imaging.Metadata.IccProfileColorSpace;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// Decodes the raster images with the codecs of Meziantou.Framework.Imaging
/// (https://html.spec.whatwg.org/multipage/images.html#img-decoding-process): PNG (including the animated PNG), JPEG, GIF,
/// WebP, BMP and ICO/CUR. The format is sniffed; the other formats the codecs read (QOI, TGA, TIFF, Netpbm, ANI) are not
/// decoded, as in the browsers. The colors are converted to sRGB (with the embedded color profile), and the EXIF orientation
/// given by the core library is applied.
/// </summary>
/// <remarks>
/// <para>
/// Only the header of the image is read when the decoder is created (and the structure of an animation: the number of frames
/// and their durations, without decoding a pixel). The frames are decoded on demand by a forward-only reader of the codecs,
/// which composites them: the reader is kept between the calls while the frames are requested in order, and reopened when an
/// earlier frame is requested, so the memory of an animation is the one of its compositor, whatever its number of frames.
/// </para>
/// <para>
/// An incomplete or corrupted image is decoded up to the last row the codec wrote before it failed (the other rows are
/// transparent, as in the browsers), when the codec writes the rows as it decodes them: PNG, baseline JPEG and BMP. An image
/// of which no row can be decoded cannot be decoded.
/// </para>
/// <para>
/// The codecs are strict where the browsers are lenient: what the browsers accept in a PNG image and the codec rejects is
/// rewritten before the image is given to the codec (<see cref="NormalizePng"/>, <see cref="Repair"/>).
/// </para>
/// </remarks>
internal sealed class ImagingImageDecoder : RenderingImageDecoder
{
    /// <summary>The number of pixels of the largest image that is decoded: the one of the largest surface of the backend.</summary>
    private const int MaxPixelCount = 268_435_456;

    /// <summary>The number of pixels whose colors are converted at once.</summary>
    private const int ColorBlockSize = 1024;

    /// <summary>
    /// The limits of the codecs: an image of at most <see cref="MaxPixelCount"/> pixels (a larger one is not decoded, whatever
    /// the size of its file), and 16 bytes per pixel for the buffers of a decoder (the canvas of the compositor, the frame, the
    /// coefficients of a progressive JPEG image). The number of frames and the pixels decoded over the life of a reader are not
    /// limited: an animation is decoded frame by frame as it is played, with the memory of one frame.
    /// </summary>
    internal static ImageConfiguration Configuration { get; } = new()
    {
        Limits = new ImageResourceLimits
        {
            MaxWidth = MaxPixelCount,
            MaxHeight = MaxPixelCount,
            MaxFramePixels = MaxPixelCount,
            MaxFrames = int.MaxValue,
            MaxTotalPixels = long.MaxValue,
            MaxEncodedBytes = long.MaxValue,
            MaxLiveAllocationBytes = 16L * MaxPixelCount,
        },
    };

    // The pixels are decoded as RGBA: the color profile of a grayscale image, which cannot label them, is applied by the decoder
    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true };
    private static readonly ImageIdentifyOptions IdentifyOptions = new() { Configuration = Configuration };
    // The colors of a CMYK JPEG image are converted to sRGB with its CMYK profile (or a profile of the printing inks when it has
    // none), as in the browsers: no pixel format holds inks
    private static readonly ImageDecodeOptions DecodeOptions = new() { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversionOptions.Default };
    private static readonly ImageReaderOptions ReaderOptions = new() { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversionOptions.Default };

    private readonly ImageFormat _format;
    private readonly int _width;
    private readonly int _height;
    private readonly int _frameCount;
    private readonly int _repetitionCount;

    // The durations of the frames of an animation in milliseconds, or null for a still image
    private readonly double[]? _durations;
    private readonly bool _isGrayscale;
    private readonly bool _hasWideSamples;

    // The encoded image: the bytes of the resource, or a copy without what the codec rejects (see NormalizePng and Repair)
    private ReadOnlyMemory<byte> _data;
    private bool _repaired;
    private bool _disposed;

    // The embedded color profile and its conversion to sRGB (created the first time the colors are converted)
    private IccProfile? _colorProfile;
    private IccColorTransform? _colorTransform;

    // The reader of the frames and the index of the next frame it reads: it holds the last composited frame of an animation
    private ImageReader<Rgba32>? _reader;
    private int _nextFrame;

    private ImagingImageDecoder(ReadOnlyMemory<byte> data, bool repaired, ImageInfo info, int frameCount, double[]? durations, int repetitionCount)
    {
        _data = data;
        _repaired = repaired;
        _format = info.Format;
        _width = info.Width;
        _height = info.Height;
        _frameCount = frameCount;
        _durations = durations;
        _repetitionCount = repetitionCount;
        _isGrayscale = info.ColorModel is ImageColorModel.Grayscale or ImageColorModel.GrayscaleAlpha;
        _hasWideSamples = info.BitsPerComponent > 8;
        _colorProfile = info.Metadata.IccProfile;
    }

    /// <inheritdoc/>
    public override int Width => _width;

    /// <inheritdoc/>
    public override int Height => _height;

    /// <inheritdoc/>
    public override int FrameCount => _frameCount;

    /// <inheritdoc/>
    public override int RepetitionCount => _repetitionCount;

    /// <inheritdoc/>
    public override double GetFrameDuration(int index) => _durations is { } durations && (uint)index < (uint)durations.Length ? durations[index] : 0;

    /// <summary>Opens an image, or returns null when it is not an image the codecs can decode.</summary>
    public static ImagingImageDecoder? Create(ReadOnlyMemory<byte> data)
    {
        // The format is sniffed (the SVG images, which are rendered by the painting of their document, are not decoded)
        if (data.IsEmpty)
            return null;

        try
        {
            // Only the formats of the web are decoded
            var format = Image.DetectFormat(data.Span);
            if (format is not (ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Gif or ImageFormat.WebP or ImageFormat.Avif or ImageFormat.Bmp or ImageFormat.Ico or ImageFormat.Cur))
                return null;

            var bytes = data;
            double[]? durations = null;
            int? repetitionCount = null;
            switch (format)
            {
                case ImageFormat.Png:
                    bytes = NormalizePng(bytes, out var animation);
                    if (animation is not null)
                    {
                        // The codec composites the frames of the animation
                        durations = new double[animation.Frames.Count];
                        for (var i = 0; i < durations.Length; i++)
                        {
                            durations[i] = animation.Frames[i].Duration;
                        }

                        repetitionCount = animation.RepetitionCount;
                    }

                    break;

                case ImageFormat.Gif:
                    durations = ReadGifFrameDurations(bytes.Span);
                    break;

                case ImageFormat.WebP:
                    durations = ReadWebPFrameDurations(bytes.Span);
                    break;
            }

            ImageInfo info;
            var repaired = false;
            try
            {
                info = Image.Identify(bytes.Span, IdentifyOptions);
            }
            catch (InvalidImageContentException) when (format == ImageFormat.Png && RepairPngAncillaryChunks(bytes.Span) is { } repairedBytes)
            {
                bytes = repairedBytes;
                repaired = true;
                info = Image.Identify(bytes.Span, IdentifyOptions);
            }

            if (info.Width < 1 || info.Height < 1)
                return null;

            // The readers of the codec read a stream: the bytes are the ones of an array
            if (!unsafe(MemoryMarshal.TryGetArray(bytes, out _)))
            {
                bytes = bytes.ToArray();
            }

            if (format == ImageFormat.Avif && info.FrameCount > 1)
            {
                // An AVIF image sequence: the durations of its frames are read from the frames (the container gives them per
                // sample); its loop count is the number of plays of the codec
                durations = ReadFrameDurations(bytes);
                var plays = info.Animation?.TotalPlays ?? 0;
                repetitionCount = durations.Length <= 1 ? 0 : plays == 0 ? -1 : plays - 1;
            }

            // A GIF or WebP image of one frame is a still image. The loop count is read by the library, as the browsers
            // interpret it (the one of the codec is a number of plays).
            var frameCount = Math.Max(1, durations?.Length ?? 0);
            if (repetitionCount is null)
            {
                durations = frameCount > 1 ? durations : null;
                repetitionCount = frameCount > 1 ? ImageMetadata.GetAnimationRepetitionCount(data.Span) ?? 0 : 0;
            }

            return new ImagingImageDecoder(bytes, repaired, info, frameCount, durations, repetitionCount.Value);
        }
        catch (Exception ex) when (IsDecodingError(ex))
        {
            // An unreadable header, an unsupported feature (e.g. a CMYK JPEG image), or an image larger than the limits
            return null;
        }
    }

    /// <inheritdoc/>
    public override RenderingImage? DecodeFrame(int index, ExifOrientation orientation, bool convertToSrgb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, FrameCount);

        try
        {
            // As in the browsers, the gamma (gAMA) and the chromaticities (cHRM) of a PNG image without an ICC profile are
            // ignored: its colors are sRGB (the codec only reports the embedded ICC profile, and never applies it)
            var transform = convertToSrgb ? GetColorTransform() : null;
            byte[]? pixels = null;
            if (_format is ImageFormat.Ico or ImageFormat.Cur)
            {
                pixels = DecodeRepresentation();
            }
            else if (transform is not null && _hasWideSamples && _frameCount == 1)
            {
                // The colors of the samples of 16 bits are converted before they are reduced to 8 bits
                pixels = DecodeWideImage(transform);
                transform = pixels is null ? transform : null;
            }

            pixels ??= DecodeSequentialFrame(index);
            if (pixels is null)
                return null;

            if (transform is not null)
            {
                ConvertColors(pixels, transform);
            }

            return ApplyOrientation(pixels, _width, _height, orientation);
        }
        catch (Exception ex) when (IsDecodingError(ex))
        {
            // The data is corrupted, uses a feature the codec does not support, or needs too much memory
            CloseReader();
            return null;
        }
    }

    /// <summary>
    /// Whether an exception is a failure of the decoding of an image (and not a programming error of the caller): the errors
    /// of the codecs, and the errors a codec may raise on corrupted data it did not expect.
    /// </summary>
    private static bool IsDecodingError(Exception exception)
        => exception is ImageException or IOException or InvalidOperationException or ArgumentException or NotSupportedException or ArithmeticException or IndexOutOfRangeException or OutOfMemoryException;

    /// <summary>
    /// Decodes the image of an icon or of a cursor: its largest representation, then the one with the most bits per pixel
    /// (the representations are not the frames of an animation, and the sequential readers do not read them).
    /// </summary>
    private byte[]? DecodeRepresentation()
    {
        using var image = Image.Load<Rgba32>(_data.Span, DecodeOptions);
        return image.Width == _width && image.Height == _height ? CopyPixels(image) : null;
    }

    /// <summary>
    /// Decodes a still image whose samples have 16 bits, with its colors converted to sRGB before the samples are reduced to
    /// 8 bits (the dark colors of a wide gamut would lose their precision otherwise), or returns null when the image is not
    /// complete or needs too much memory: it is then decoded with samples of 8 bits (see <see cref="DecodeSequentialFrame"/>).
    /// </summary>
    private byte[]? DecodeWideImage(IccColorTransform transform)
    {
        try
        {
            using var image = Image.Load<Rgba64>(_data.Span, DecodeOptions);
            if (image.Width != _width || image.Height != _height)
                return null;

            var pixels = new byte[checked(_width * _height * 4)];
            image.Frames[0].ProcessPixelRows((pixels, transform), static (accessor, state) =>
            {
                var (pixels, transform) = state;
                var sourceChannels = transform.SourceChannelCount;
                Span<ushort> source = stackalloc ushort[ColorBlockSize * 3];
                Span<ushort> converted = stackalloc ushort[ColorBlockSize * 3];
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var destination = pixels.AsSpan(y * accessor.Width * 4, accessor.Width * 4);
                    while (!row.IsEmpty)
                    {
                        // The samples of the transform have no alpha (the gray of a grayscale image is its red component)
                        var block = row[..Math.Min(ColorBlockSize, row.Length)];
                        for (var i = 0; i < block.Length; i++)
                        {
                            source[i * sourceChannels] = block[i].R;
                            if (sourceChannels == 3)
                            {
                                source[(i * 3) + 1] = block[i].G;
                                source[(i * 3) + 2] = block[i].B;
                            }
                        }

                        transform.Convert(source[..(block.Length * sourceChannels)], converted[..(block.Length * 3)]);
                        for (var i = 0; i < block.Length; i++)
                        {
                            destination[i * 4] = ToByte(converted[i * 3]);
                            destination[(i * 4) + 1] = ToByte(converted[(i * 3) + 1]);
                            destination[(i * 4) + 2] = ToByte(converted[(i * 3) + 2]);
                            destination[(i * 4) + 3] = ToByte(block[i].A);
                        }

                        row = row[block.Length..];
                        destination = destination[(block.Length * 4)..];
                    }
                }
            });

            return pixels;
        }
        catch (Exception ex) when (ex is ImageException or OutOfMemoryException)
        {
            return null;
        }

        // The nearest sample of 8 bits
        static byte ToByte(ushort sample) => (byte)(((sample * 255u) + 32767u) / 65535u);
    }

    /// <summary>
    /// Decodes a frame with the reader: the frames before it that the reader did not read yet are decoded too (each frame of
    /// an animation is composited over the previous ones), from the first one when the reader is after the frame.
    /// </summary>
    private byte[]? DecodeSequentialFrame(int index)
    {
        while (true)
        {
            // The frame is transparent before it is decoded: the rows of an incomplete image that are not decoded stay transparent
            using var frame = new Image<Rgba32>(_width, _height, Configuration);
            try
            {
                return ReadFrame(index, frame) ? CopyPixels(frame) : null;
            }
            catch (InvalidImageContentException)
            {
                // What the browsers ignore and the codec rejects is repaired the first time the codec rejects the image
                CloseReader();
                if (!_repaired)
                {
                    _repaired = true;
                    if (Repair(_format, _data.Span) is { } repaired)
                    {
                        _data = repaired;
                        continue;
                    }
                }

                // The frames of an animation that follow a frame that cannot be decoded cannot be decoded
                if (index != 0)
                    return null;

                // An incomplete (or corrupted) image is decoded up to its last decoded row: the reader writes the rows in the
                // frame as it decodes them
                var pixels = CopyPixels(frame);
                return HasDecodedPixels(pixels) ? pixels : null;
            }
        }
    }

    /// <summary>Reads the frames up to a frame into an image (which then has the pixels of the frame), or returns false when the image has fewer frames.</summary>
    private bool ReadFrame(int index, Image<Rgba32> frame)
    {
        if (_reader is null || _nextFrame > index)
        {
            CloseReader();
            _reader = OpenReader();
        }

        var reader = _reader;
        while (_nextFrame <= index)
        {
            if (!reader.ReadFrameInto(frame))
            {
                // The image has fewer frames than its structure says
                CloseReader();
                return false;
            }

            _nextFrame++;
        }

        // The reader of a still image, or of an animation whose last frame is decoded, is not needed anymore (the next frame
        // requested is an earlier one)
        if (_nextFrame >= _frameCount)
        {
            CloseReader();
        }

        return true;
    }

    /// <summary>
    /// Returns an image without the errors the browsers ignore and the codec rejects, or null when it has none: the ancillary
    /// chunks of a PNG image whose checksum is wrong, and the missing end of image marker of a JPEG image (which libjpeg adds:
    /// the codec decodes the last rows of an image only when it reads the marker that follows them).
    /// </summary>
    private static byte[]? Repair(ImageFormat format, ReadOnlySpan<byte> data)
    {
        return format switch
        {
            ImageFormat.Png => RepairPngAncillaryChunks(data),
            ImageFormat.Jpeg when !data.EndsWith<byte>([0xFF, 0xD9]) => [.. data, 0xFF, 0xD9],
            _ => null,
        };
    }

    /// <summary>
    /// Returns whether the reader wrote pixels of the first frame of the image before it failed, given the pixels of a frame
    /// that was transparent before it was decoded.
    /// </summary>
    private bool HasDecodedPixels(byte[] pixels)
    {
        if (pixels.AsSpan().ContainsAnyExcept((byte)0))
            return true;

        // Either no row was decoded, or the decoded rows are transparent black: the image is decoded again in a frame of
        // another color to tell
        using var frame = new Image<Rgba32>(_width, _height, new Rgba32(255, 255, 255, 255), Configuration);
        try
        {
            using var reader = OpenReader();
            if (reader.ReadFrameInto(frame))
                return false;
        }
        catch (InvalidImageContentException)
        {
            // The same error as the first time
        }

        return CopyPixels(frame).AsSpan().ContainsAnyExcept((byte)255);
    }

    private ImageReader<Rgba32> OpenReader()
    {
        // The data is the one of an array (see Create); the reader disposes of the stream
        _ = unsafe(MemoryMarshal.TryGetArray(_data, out var segment));
        var stream = new MemoryStream(segment.Array ?? [], segment.Offset, segment.Count, writable: false);
        return Image.OpenReader<Rgba32>(stream, ReaderOptions);
    }

    private void CloseReader()
    {
        var reader = _reader;
        _reader = null;
        _nextFrame = 0;
        reader?.Dispose();
    }

    /// <summary>Returns the pixels of the frame of a still image, in the format of <see cref="ImagingRenderingImage"/> (RGBA, not premultiplied).</summary>
    private static byte[] CopyPixels(Image<Rgba32> image)
    {
        var pixels = new byte[checked(image.Width * image.Height * 4)];
        image.Frames[0].CopyPixelBytesTo(pixels, image.Width * 4);
        return pixels;
    }

    /// <summary>
    /// Returns the conversion of the colors of the image from its embedded ICC profile to sRGB, or null when its colors are
    /// sRGB: the pixels without profile are sRGB and, as in the browsers, a profile that is invalid, or that is not the one of
    /// the color model of the image (an RGB profile for a grayscale image, a CMYK profile), is ignored.
    /// </summary>
    private IccColorTransform? GetColorTransform()
    {
        if (_colorProfile is { } profile)
        {
            _colorProfile = null;
            if (profile.ColorSpace == (_isGrayscale ? IccProfileColorSpace.Gray : IccProfileColorSpace.Rgb))
            {
                try
                {
                    // Most of the images that have a profile have the one of sRGB: their pixels are not converted
                    var transform = IccColorTransform.Create(profile, IccProfile.Srgb);
                    _colorTransform = ChangesColors(transform) ? transform : null;
                }
                catch (ImageException)
                {
                    _colorTransform = null;
                }
            }
        }

        return _colorTransform;
    }

    /// <summary>
    /// Returns whether a conversion to sRGB changes colors (by more than the rounding of a sample of 8 bits): the conversion
    /// of the profile of sRGB does not. The conversion is probed with the grays and, for an RGB profile, with the values of
    /// each primary and a lattice of colors.
    /// </summary>
    private static bool ChangesColors(IccColorTransform transform)
    {
        const int LatticeSize = 6;
        const int RgbProbeCount = (4 * 256) + (LatticeSize * LatticeSize * LatticeSize);
        Span<byte> source = stackalloc byte[RgbProbeCount * 3];
        Span<byte> converted = stackalloc byte[RgbProbeCount * 3];
        source.Clear();
        if (transform.SourceChannelCount == 1)
        {
            source = source[..256];
            for (var value = 0; value < 256; value++)
            {
                source[value] = (byte)value;
            }
        }
        else
        {
            var probes = source;
            for (var value = 0; value < 256; value++)
            {
                // A gray, a red, a green and a blue
                probes[..3].Fill((byte)value);
                probes[3] = (byte)value;
                probes[7] = (byte)value;
                probes[11] = (byte)value;
                probes = probes[12..];
            }

            const int LatticeStep = 255 / (LatticeSize - 1);
            for (var i = 0; i < LatticeSize * LatticeSize * LatticeSize; i++)
            {
                probes[0] = (byte)(i % LatticeSize * LatticeStep);
                probes[1] = (byte)(i / LatticeSize % LatticeSize * LatticeStep);
                probes[2] = (byte)(i / (LatticeSize * LatticeSize) * LatticeStep);
                probes = probes[3..];
            }
        }

        var count = source.Length / transform.SourceChannelCount;
        converted = converted[..(count * 3)];
        transform.Convert(source, converted);
        for (var i = 0; i < converted.Length; i++)
        {
            // The gray of a grayscale profile is the 3 components of its color
            var expected = transform.SourceChannelCount == 1 ? source[i / 3] : source[i];
            if (Math.Abs(converted[i] - expected) > 1)
                return true;
        }

        return false;
    }

    /// <summary>Converts the colors of pixels (RGBA, 8 bits) to sRGB.</summary>
    private static void ConvertColors(byte[] pixels, IccColorTransform transform)
    {
        // The samples of the transform have no alpha: the colors are converted by blocks (the gray of a grayscale image is
        // its red component)
        var sourceChannels = transform.SourceChannelCount;
        Span<byte> source = stackalloc byte[ColorBlockSize * 3];
        Span<byte> converted = stackalloc byte[ColorBlockSize * 3];
        for (var offset = 0; offset < pixels.Length; offset += ColorBlockSize * 4)
        {
            var block = pixels.AsSpan(offset, Math.Min(ColorBlockSize * 4, pixels.Length - offset));
            var count = block.Length / 4;
            for (var i = 0; i < count; i++)
            {
                block.Slice(i * 4, sourceChannels).CopyTo(source[(i * sourceChannels)..]);
            }

            transform.Convert(source[..(count * sourceChannels)], converted[..(count * 3)]);
            for (var i = 0; i < count; i++)
            {
                converted.Slice(i * 3, 3).CopyTo(block[(i * 4)..]);
            }
        }
    }

    /// <summary>
    /// Returns the image of the pixels drawn in an EXIF orientation (https://drafts.csswg.org/css-images-3/#valdef-image-orientation-from-image):
    /// the orientation maps the pixel (x, y) of the encoded image of size w x h to a pixel of the oriented image.
    /// </summary>
    private static ImagingRenderingImage ApplyOrientation(byte[] pixels, int width, int height, ExifOrientation orientation)
    {
        // The index of the oriented pixel is start + x * stepX + y * stepY
        var count = width * height;
        int start;
        int stepX;
        int stepY;
        switch (orientation)
        {
            case ExifOrientation.TopRight: // (w - 1 - x, y)
                (start, stepX, stepY) = (width - 1, -1, width);
                break;

            case ExifOrientation.BottomRight: // (w - 1 - x, h - 1 - y)
                (start, stepX, stepY) = (count - 1, -1, -width);
                break;

            case ExifOrientation.BottomLeft: // (x, h - 1 - y)
                (start, stepX, stepY) = (count - width, 1, -width);
                break;

            case ExifOrientation.LeftTop: // (y, x)
                (start, stepX, stepY) = (0, height, 1);
                break;

            case ExifOrientation.RightTop: // (h - 1 - y, x)
                (start, stepX, stepY) = (height - 1, height, -1);
                break;

            case ExifOrientation.RightBottom: // (h - 1 - y, w - 1 - x)
                (start, stepX, stepY) = (count - 1, -height, -1);
                break;

            case ExifOrientation.LeftBottom: // (y, w - 1 - x)
                (start, stepX, stepY) = (count - height, -height, 1);
                break;

            default:
                return new ImagingRenderingImage(width, height, pixels);
        }

        var oriented = new byte[count * 4];
        var source = unsafe(MemoryMarshal.Cast<byte, uint>(pixels.AsSpan(0, count * 4)));
        var destination = unsafe(MemoryMarshal.Cast<byte, uint>(oriented.AsSpan()));
        for (var y = 0; y < height; y++)
        {
            var row = source.Slice(y * width, width);
            var index = start + (y * stepY);
            for (var x = 0; x < row.Length; x++)
            {
                destination[index] = row[x];
                index += stepX;
            }
        }

        return orientation.SwapsAxes() ? new ImagingRenderingImage(height, width, oriented) : new ImagingRenderingImage(width, height, oriented);
    }

    /// <summary>
    /// Returns the durations in milliseconds of the frames of a GIF image whose data is complete
    /// (https://www.w3.org/Graphics/GIF/spec-gif89a.txt): the header of a GIF image does not have its number of frames, so its
    /// blocks are walked (the image data is skipped, not decoded). The delay of a frame is the one of the graphic control
    /// extension that precedes it (none: 0). The frames of an incomplete image that are cut are not counted: the codec cannot
    /// decode a part of a frame.
    /// </summary>
    /// <summary>The durations of the frames of an animation read by the codec (each frame is decoded), in milliseconds.</summary>
    private static double[] ReadFrameDurations(ReadOnlyMemory<byte> bytes)
    {
        const int MaxFrameCount = 10_000;
        var durations = new List<double>();
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = Image.OpenReader<Rgba32>(stream, ReaderOptions);
        while (durations.Count < MaxFrameCount && reader.ReadFrame() is { } frame)
        {
            using (frame)
            {
                durations.Add(frame.Frames[0].Metadata.Duration.TotalMilliseconds);
            }
        }

        return [.. durations];
    }

    private static double[] ReadGifFrameDurations(ReadOnlySpan<byte> bytes)
    {
        var durations = new List<double>();
        if (bytes.Length < 13)
            return [];

        // The header, the logical screen descriptor and the global color table
        var offset = 13;
        if ((bytes[10] & 0x80) != 0)
        {
            offset += 3 << ((bytes[10] & 0x07) + 1);
        }

        var delay = 0;
        while (offset < bytes.Length)
        {
            var block = bytes[offset];
            if (block == 0x21 && bytes.Length - offset >= 2)
            {
                // An extension: the delay of the graphic control extension is in hundredths of a second
                var data = bytes[(offset + 2)..];
                if (bytes[offset + 1] == 0xF9 && data.Length >= 5 && data[0] == 4)
                {
                    delay = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
                }

                if (!TrySkipSubBlocks(bytes, offset + 2, out offset))
                    break;
            }
            else if (block == 0x2C && bytes.Length - offset >= 10)
            {
                // An image descriptor, its local color table, the code size and the data
                var packed = bytes[offset + 9];
                offset += 10;
                if ((packed & 0x80) != 0)
                {
                    offset += 3 << ((packed & 0x07) + 1);
                }

                if (offset >= bytes.Length || !TrySkipSubBlocks(bytes, offset + 1, out offset))
                    break;

                durations.Add(delay * 10.0);
                delay = 0;
            }
            else
            {
                // The trailer (0x3B), or corrupted data
                break;
            }
        }

        return [.. durations];

        // Skips data sub-blocks up to their terminator (an empty sub-block), or returns false when the data ends before it
        static bool TrySkipSubBlocks(ReadOnlySpan<byte> bytes, int offset, out int end)
        {
            end = offset;
            while (end < bytes.Length)
            {
                var size = bytes[end];
                if (size >= bytes.Length - end)
                    return false;

                end += size + 1;
                if (size == 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns the durations in milliseconds of the frames of an animated WebP image
    /// (https://developers.google.com/speed/webp/docs/riff_container#animation): the durations of its <c>ANMF</c> chunks (the
    /// chunks are walked, the frames are not decoded), or none for a still image. The frame of an incomplete image that is cut
    /// is not counted.
    /// </summary>
    private static double[] ReadWebPFrameDurations(ReadOnlySpan<byte> bytes)
    {
        // The RIFF header (12 bytes), then the chunks: a type, a size (little-endian), a payload padded to an even size. An
        // animated image starts with a VP8X chunk whose animation flag is set.
        if (bytes.Length < 30 || !bytes.Slice(12, 4).SequenceEqual("VP8X"u8) || (bytes[20] & 0x02) == 0)
            return [];

        var durations = new List<double>();
        var offset = 12;
        while (bytes.Length - offset >= 8)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            if (length > (uint)(bytes.Length - offset - 8))
                break;

            // The duration of a frame is the 24-bit integer that follows its position and its size
            if (bytes.Slice(offset, 4).SequenceEqual("ANMF"u8) && length >= 16)
            {
                var frame = bytes.Slice(offset + 8, 16);
                durations.Add(frame[12] | (frame[13] << 8) | (frame[14] << 16));
            }

            var next = (long)offset + 8 + length + (length & 1);
            if (next >= bytes.Length)
                break;

            offset = (int)next;
        }

        return [.. durations];
    }

    /// <summary>
    /// Returns the PNG image the codec decodes for a PNG image, and its animation when it is an animated PNG image
    /// (https://w3c.github.io/png/#apng-frame-based-animation): the image itself, or a copy without what the browsers accept
    /// and the codec rejects or decodes differently. Only the headers of the chunks are read.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The chunks whose type is not made of 4 ASCII letters are removed: the browsers ignore them (https://w3c.github.io/png/#13Error-checking).</item>
    /// <item>
    /// The animation chunks of an invalid animation are removed: as in the browsers, the image is displayed as its default image
    /// (<see cref="ApngImage.TryParse"/> tells whether the animation is valid).
    /// </item>
    /// <item>An animation control chunk that follows the first frame control chunk is moved before it: the browsers accept this order.</item>
    /// <item>
    /// The bits of the transparent color (<c>tRNS</c>) of a grayscale or RGB image above the bit depth of the image are cleared:
    /// the decoders ignore them (https://w3c.github.io/png/#11tRNS).
    /// </item>
    /// </list>
    /// </remarks>
    private static ReadOnlyMemory<byte> NormalizePng(ReadOnlyMemory<byte> image, out ApngImage? animation)
    {
        animation = null;
        var data = image.Span;

        // The bytes of the image that are replaced (removed, when the replacement is empty)
        List<(int Offset, int Length, byte[] Replacement)>? edits = null;
        List<(int Offset, int Length)>? animationChunks = null;
        var animationControl = -1;
        var firstFrameControl = -1;
        var bitDepth = 0;
        var colorType = 0;
        var seenImageData = false;
        var offset = ImageMetadata.PngSignature.Length;
        while (data.Length - offset >= 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > (uint)(data.Length - offset - 12))
                break;

            var chunkLength = 12 + (int)length;
            var type = data.Slice(offset + 4, 4);
            var content = data.Slice(offset + 8, (int)length);
            if (!char.IsAsciiLetter((char)type[0]) || !char.IsAsciiLetter((char)type[1]) || !char.IsAsciiLetter((char)type[2]) || !char.IsAsciiLetter((char)type[3]))
            {
                (edits ??= []).Add((offset, chunkLength, []));
            }
            else if (type.SequenceEqual("IHDR"u8) && content.Length == 13)
            {
                bitDepth = content[8];
                colorType = content[9];
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                seenImageData = true;
            }
            else if (type.SequenceEqual("tRNS"u8) && !seenImageData && MaskPngTransparentColor(content, bitDepth, colorType) is { } transparentColor)
            {
                (edits ??= []).Add((offset, chunkLength, CreatePngChunk(type, transparentColor)));
            }
            else if (type.SequenceEqual("acTL"u8))
            {
                (animationChunks ??= []).Add((offset, chunkLength));
                animationControl = animationControl < 0 ? offset : animationControl;
            }
            else if (type.SequenceEqual("fcTL"u8))
            {
                (animationChunks ??= []).Add((offset, chunkLength));
                firstFrameControl = firstFrameControl < 0 ? offset : firstFrameControl;
            }
            else if (type.SequenceEqual("fdAT"u8))
            {
                (animationChunks ??= []).Add((offset, chunkLength));
            }

            offset += chunkLength;
            if (type.SequenceEqual("IEND"u8))
                break;
        }

        if (animationChunks is not null)
        {
            animation = ApngImage.TryParse(data);
            if (animation is null)
            {
                foreach (var (chunkOffset, chunkLength) in animationChunks)
                {
                    (edits ??= []).Add((chunkOffset, chunkLength, []));
                }
            }
            else if (firstFrameControl >= 0 && firstFrameControl < animationControl)
            {
                // The animation control chunk of a valid animation has 8 bytes of data
                const int AnimationControlLength = 12 + 8;
                edits ??= [];
                edits.Add((firstFrameControl, 0, data.Slice(animationControl, AnimationControlLength).ToArray()));
                edits.Add((animationControl, AnimationControlLength, []));
            }
        }

        return edits is null ? image : ReplaceBytes(data, edits);
    }

    /// <summary>Returns data with some of its bytes replaced (removed, when their replacement is empty).</summary>
    private static byte[] ReplaceBytes(ReadOnlySpan<byte> data, List<(int Offset, int Length, byte[] Replacement)> edits)
    {
        edits.Sort(static (a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : a.Length.CompareTo(b.Length));
        var result = new List<byte>(data.Length);
        var start = 0;
        foreach (var (offset, length, replacement) in edits)
        {
            result.AddRange(data[start..offset]);
            result.AddRange(replacement);
            start = offset + length;
        }

        result.AddRange(data[start..]);
        return result.ToArray();
    }

    /// <summary>
    /// Returns the data of the <c>tRNS</c> chunk of a grayscale or RGB image of less than 16 bits per sample without the bits
    /// of its samples (of 16 bits each) that are above the bit depth of the image, or null when it has none.
    /// </summary>
    private static byte[]? MaskPngTransparentColor(ReadOnlySpan<byte> content, int bitDepth, int colorType)
    {
        const int Grayscale = 0;
        const int Rgb = 2;
        if (bitDepth is < 1 or >= 16 || colorType is not (Grayscale or Rgb) || content.Length != (colorType == Grayscale ? 2 : 6))
            return null;

        var mask = (ushort)((1 << bitDepth) - 1);
        byte[]? result = null;
        for (var i = 0; i < content.Length; i += 2)
        {
            var sample = BinaryPrimitives.ReadUInt16BigEndian(content[i..]);
            if ((sample & ~mask) != 0)
            {
                result ??= content.ToArray();
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(i), (ushort)(sample & mask));
            }
        }

        return result;
    }

    /// <summary>Creates a chunk of a PNG image (https://w3c.github.io/png/#5Chunk-layout): its length, its type, its data and its checksum.</summary>
    private static byte[] CreatePngChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> content)
    {
        var chunk = new byte[12 + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)content.Length);
        type.CopyTo(chunk.AsSpan(4));
        content.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + content.Length), Crc32.HashToUInt32(chunk.AsSpan(4, 4 + content.Length)));
        return chunk;
    }

    /// <summary>
    /// Returns a PNG image without the ancillary chunks whose checksum is wrong, or null when it has none: as libpng in the
    /// browsers, such a chunk is ignored, except the embedded ICC profile (<c>iCCP</c>), which is used (its checksum is
    /// corrected). The codec rejects an image that has such a chunk. A critical chunk whose checksum is wrong is an error for
    /// both: the image is decoded up to it.
    /// </summary>
    private static byte[]? RepairPngAncillaryChunks(ReadOnlySpan<byte> data)
    {
        List<(int Offset, int Length, byte[] Replacement)>? edits = null;
        var offset = ImageMetadata.PngSignature.Length;
        while (data.Length - offset >= 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > (uint)(data.Length - offset - 12))
                break;

            // A chunk is ancillary when the first letter of its type is a lowercase letter (https://w3c.github.io/png/#5Chunk-naming-conventions)
            var chunkLength = 12 + (int)length;
            var type = data.Slice(offset + 4, 4);
            var content = data.Slice(offset + 8, (int)length);
            if (char.IsAsciiLetterLower((char)type[0]) && Crc32.HashToUInt32(data.Slice(offset + 4, 4 + content.Length)) != BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 8 + content.Length)..]))
            {
                (edits ??= []).Add((offset, chunkLength, type.SequenceEqual("iCCP"u8) ? CreatePngChunk(type, content) : []));
            }

            offset += chunkLength;
        }

        return edits is null ? null : ReplaceBytes(data, edits);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            CloseReader();
            _data = default;
            _colorProfile = null;
            _colorTransform = null;
        }
    }
}
