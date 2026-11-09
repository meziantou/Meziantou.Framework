using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The base class of the codecs of the formats the library does not read or write itself. Register a codec in
/// <see cref="ImageConfiguration.Codecs"/> to detect, identify, load and save its format through the entry points of the
/// built-in formats.
/// </summary>
/// <remarks>
/// <para>
/// A codec recognizes its files from their first bytes (<see cref="IsMatch"/>), never from their names. The library reads the
/// whole file, within <see cref="ImageResourceLimits.MaxEncodedBytes"/>, before it calls <see cref="Decode"/> or
/// <see cref="Identify"/> with the bytes: a codec never performs I/O, and the same code serves the span, stream and path
/// overloads, synchronous and asynchronous.
/// </para>
/// <para>
/// The decoder creates its image with <see cref="CustomImageDecodeContext.CreateImage(PixelFormat, int, int)"/>, which applies
/// the resource limits of the configuration and charges the pixels to the load, fills its pixels (for example with
/// <see cref="ImageFrame{TPixel}.ProcessPixelRows(PixelRowsAction{TPixel})"/>) and returns it. The library then applies the
/// frame limit and the pixel type of a typed load (<see cref="Image.Load{TPixel}(Stream, ImageDecodeOptions?)"/>), so a codec
/// can always produce the pixel format that holds its samples best.
/// </para>
/// <para>
/// Throw <see cref="InvalidImageContentException"/> for malformed data, <see cref="UnsupportedImageFeatureException"/> for
/// valid data the codec does not support, with <see cref="ImageFormat.Custom"/> as their format: the library propagates the
/// exceptions of a codec unchanged.
/// </para>
/// <para>
/// A codec is shared by every operation of its configurations: it must be immutable and thread-safe.
/// </para>
/// </remarks>
public abstract class CustomImageCodec
{
    /// <summary>Initializes a codec of a format.</summary>
    /// <param name="formatInfo">The description of the format, created with the public constructor of <see cref="ImageFormatInfo"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="formatInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="formatInfo"/> describes a built-in format.</exception>
    protected CustomImageCodec(ImageFormatInfo formatInfo)
    {
        ArgumentNullException.ThrowIfNull(formatInfo);
        if (formatInfo.Format != ImageFormat.Custom)
            throw new ArgumentException("A custom codec describes its own format: create its description with the public constructor of ImageFormatInfo.", nameof(formatInfo));

        FormatInfo = formatInfo;
    }

    /// <summary>Gets the description of the format: its name, its media types and its file extensions.</summary>
    public ImageFormatInfo FormatInfo { get; }

    /// <summary>Determines whether data starts with the signature of the format.</summary>
    /// <param name="prefix">
    /// The first bytes of the data: <see cref="Image.FormatDetectionPrefixLength"/> bytes, or the whole data when it is
    /// shorter (at least 8 bytes: a shorter input is never recognized).
    /// </param>
    /// <returns><see langword="true"/> if the codec reads the data.</returns>
    public abstract bool IsMatch(ReadOnlySpan<byte> prefix);

    /// <summary>Decodes a file.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="context">The context of the load: create the image with it.</param>
    /// <returns>An image created by <see cref="CustomImageDecodeContext.CreateImage(PixelFormat, int, int)"/>.</returns>
    public abstract Image Decode(ReadOnlySpan<byte> data, CustomImageDecodeContext context);

    /// <summary>Describes a file without returning its pixels.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="context">The context of the identification; its <see cref="CustomImageDecodeContext.IdentifyMode"/> is set.</param>
    /// <returns>The description, created by <see cref="CreateImageInfo(int, int, PixelFormat, int?, ImageMetadata?)"/> or <see cref="CreateImageInfo(Image)"/>.</returns>
    /// <remarks>
    /// The default implementation decodes the file and describes the image (<see cref="CreateImageInfo(Image)"/>). Override
    /// it to read the header only.
    /// </remarks>
    public virtual ImageInfo Identify(ReadOnlySpan<byte> data, CustomImageDecodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var image = Decode(data, context);
        return CreateImageInfo(image);
    }

    /// <summary>
    /// Creates the encoder of the format with its default settings, which <see cref="Image.Save(string, ImageEncoder?)"/> uses
    /// for the file extensions of <see cref="FormatInfo"/>, or <see langword="null"/> (the default) when the codec only reads
    /// the format.
    /// </summary>
    /// <returns>A new encoder, or <see langword="null"/>.</returns>
    public virtual CustomImageEncoder? CreateEncoder() => null;

    /// <summary>Creates the description of a file of this format from its header.</summary>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="pixelFormat">The pixel format <see cref="Decode"/> produces.</param>
    /// <param name="frameCount">The number of displayed frames, or <see langword="null"/> when the header does not tell it.</param>
    /// <param name="metadata">The metadata of the file, or <see langword="null"/> for none. The description stores a copy.</param>
    /// <returns>The description, whose <see cref="ImageInfo.FormatInfo"/> is <see cref="FormatInfo"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the frame count is not positive, or <paramref name="pixelFormat"/> is not defined.</exception>
    protected ImageInfo CreateImageInfo(int width, int height, PixelFormat pixelFormat, int? frameCount = 1, ImageMetadata? metadata = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!Enum.IsDefined(pixelFormat))
            throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "The pixel format is not valid.");

        return new ImageInfo(
            ImageFormat.Custom,
            new Size(width, height),
            pixelFormat,
            GetColorModel(pixelFormat),
            PixelFormats.GetBitsPerComponent(pixelFormat),
            frameCount,
            frameCount is null ? null : frameCount > 1,
            hasPosterFrame: null,
            PixelFormats.HasAlpha(pixelFormat) ? null : false,
            animation: null,
            metadata ?? new ImageMetadata(),
            ImageIdentifyMode.Header)
        {
            CustomFormatInfo = FormatInfo,
        };
    }

    /// <summary>Creates the description of a decoded image of this format.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The description of its size, pixel format, frames, animation settings and metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="image"/> is disposed.</exception>
    protected ImageInfo CreateImageInfo(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var format = image.PixelFormat;
        return new ImageInfo(
            ImageFormat.Custom,
            image.Size,
            format,
            GetColorModel(format),
            PixelFormats.GetBitsPerComponent(format),
            image.Frames.Count,
            image.IsAnimated,
            image.PosterFrame is not null,
            PixelFormats.HasAlpha(format) ? null : false,
            image.Animation,
            image.Metadata,
            ImageIdentifyMode.FullScan)
        {
            CustomFormatInfo = FormatInfo,
        };
    }

    private static ImageColorModel GetColorModel(PixelFormat format)
    {
        // An alpha mask is described as gray with alpha, as the collections describe it
        if (PixelFormats.IsGrayscale(format) || !PixelFormats.HasColor(format))
            return PixelFormats.HasAlpha(format) ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale;

        return PixelFormats.HasAlpha(format) ? ImageColorModel.Rgba : ImageColorModel.Rgb;
    }
}
