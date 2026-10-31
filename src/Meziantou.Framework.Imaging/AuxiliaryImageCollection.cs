using System.Collections;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The images that a file stores next to its primary image without presenting them: the thumbnails, depth maps, gain maps
/// and mattes of HEIC and AVIF files, and the images that follow the primary image of a JPEG Multi-Picture Format file
/// (the gain map of an Ultra HDR photo). <see cref="Image.Load(string, ImageDecodeOptions?)"/> never reads them.
/// </summary>
/// <remarks>
/// <para>
/// Loading reads the whole input into memory (up to <see cref="ImageResourceLimits.MaxEncodedBytes"/>) and its structure
/// only: each image is decoded on demand by <see cref="AuxiliaryImage.Decode(ImageDecodeOptions?)"/>, with its own limits.
/// The collection holds no resource that needs to be disposed, and its images can be decoded from several threads at once.
/// </para>
/// <para>
/// The images are listed in the order of the file. A file of another format, or a file without such images, gives an empty
/// collection. The description of an image is optional data: an image whose description cannot be read (an index of a
/// JPEG file that points outside the file, a HEIF item without size) is not listed, and gain map parameters that cannot
/// be read leave <see cref="AuxiliaryImage.GainMapMetadata"/> <see langword="null"/> (<see cref="DecodeHdr"/> then throws
/// the error).
/// </para>
/// </remarks>
public sealed class AuxiliaryImageCollection : IReadOnlyList<AuxiliaryImage>
{
    private readonly byte[] _data;
    private readonly List<AuxiliaryImage> _images;

    private AuxiliaryImageCollection(byte[] data, ImageFormat format, ImageConfiguration configuration, CancellationToken cancellationToken)
    {
        _data = data;
        Format = format;
        Configuration = configuration;
        _images = format switch
        {
            ImageFormat.Heic => HeifAuxiliaryScanner.Scan(this, data, HeicBinding.Instance, cancellationToken),
            ImageFormat.Avif => HeifAuxiliaryScanner.Scan(this, data, AvifBinding.Instance, cancellationToken),
            ImageFormat.Jpeg => JpegAuxiliaryScanner.Scan(this, data),
            _ => [],
        };
    }

    /// <summary>Gets the format of the file.</summary>
    public ImageFormat Format { get; }

    /// <summary>Gets the configuration the collection was loaded with, which decodes its images unless other options are given.</summary>
    public ImageConfiguration Configuration { get; }

    /// <summary>Gets the number of images.</summary>
    public int Count => _images.Count;

    /// <summary>
    /// Gets the gain map that <see cref="DecodeHdr"/> applies: the first gain map whose parameters are known, or else the
    /// first gain map, or <see langword="null"/> when the file has none.
    /// </summary>
    public AuxiliaryImage? GainMapImage => _images.Find(image => image.Kind is AuxiliaryImageKind.GainMap && image.GainMapMetadata is not null) ?? _images.Find(image => image.Kind is AuxiliaryImageKind.GainMap);

    /// <summary>Gets an image.</summary>
    /// <param name="index">The index of the image.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public AuxiliaryImage this[int index] => _images[index];

    /// <summary>Lists the images of a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The images.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the file is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The structure of a HEIC or AVIF file is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The file is larger than <see cref="ImageResourceLimits.MaxEncodedBytes"/>.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static AuxiliaryImageCollection Load(string path, ImageConfiguration? configuration = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        configuration ??= ImageConfiguration.Default;
        using var stream = ImageIO.OpenFile(path, asynchronous: false);
        return Create(ReadAll(stream, configuration), configuration);
    }

    /// <summary>Lists the images of a stream, read from its current position to its end.</summary>
    /// <param name="stream">The stream; it is not closed.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The images.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the data is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The structure of a HEIC or AVIF file is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The stream has more than <see cref="ImageResourceLimits.MaxEncodedBytes"/> bytes.</exception>
    public static AuxiliaryImageCollection Load(Stream stream, ImageConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream is not readable.", nameof(stream));

        configuration ??= ImageConfiguration.Default;
        return Create(ReadAll(stream, configuration), configuration);
    }

    /// <summary>Lists the images of encoded data, which is copied.</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The images.</returns>
    /// <exception cref="UnknownImageFormatException">The format of the data is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The structure of a HEIC or AVIF file is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The data is larger than <see cref="ImageResourceLimits.MaxEncodedBytes"/>.</exception>
    public static AuxiliaryImageCollection Load(ReadOnlySpan<byte> data, ImageConfiguration? configuration = null)
    {
        configuration ??= ImageConfiguration.Default;
        if (data.Length > configuration.Limits.MaxEncodedBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, configuration.Limits.MaxEncodedBytes, data.Length);

        return Create(data.ToArray(), configuration);
    }

    /// <summary>
    /// Decodes the primary image and its gain map (<see cref="GainMapImage"/>), and applies the gain map for a display of a
    /// given headroom (<see cref="Metadata.GainMapMetadata.Apply(Image, Image, double, CancellationToken)"/>).
    /// </summary>
    /// <param name="hdrHeadroom">
    /// The headroom of the display, in stops: the log2 of the ratio of its peak light to the white of standard dynamic range.
    /// 0 gives the base image in linear light; the <see cref="Metadata.GainMapMetadata.AlternateHdrHeadroom"/> of the gain
    /// map, or more, gives the full alternate rendition.
    /// </param>
    /// <param name="options">The decoding options of both images, or <see langword="null"/> for the configuration the collection was loaded with.</param>
    /// <param name="cancellationToken">The token checked while the gain map is applied.</param>
    /// <returns>
    /// A new image, owned by the caller, of linear light where 1 is the white of standard dynamic range, in the primaries
    /// of the base image (or of the alternate rendition when the gain map applies in its color space). The primary image
    /// decodes exactly as with <see cref="Image.Load(ReadOnlySpan{byte}, ImageDecodeOptions?)"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hdrHeadroom"/> is negative or not finite.</exception>
    /// <exception cref="InvalidOperationException">The file has no gain map.</exception>
    /// <exception cref="InvalidImageContentException">An image or the parameters of the gain map are malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The parameters of the gain map are not known (a gain map of Apple without ISO 21496-1 metadata, a newer version of
    /// the metadata), or an image uses a feature that is not supported.
    /// </exception>
    /// <exception cref="ImageResourceLimitException">An image exceeds a resource limit.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public Image<RgbaFloat> DecodeHdr(double hdrHeadroom, ImageDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(hdrHeadroom) || hdrHeadroom < 0)
            throw new ArgumentOutOfRangeException(nameof(hdrHeadroom), hdrHeadroom, "The headroom must be a finite number of at least 0.");

        var gainMap = GainMapImage ?? throw new InvalidOperationException("The file has no gain map.");
        if (gainMap.GainMapMetadata is not { } metadata)
        {
            if (gainMap.MetadataError is { } error)
                throw new InvalidImageContentException(error.Message, Format, error);

            throw new UnsupportedImageFeatureException("The parameters of the gain map of the file are not known.", Format, "Gain map without known parameters");
        }

        options = GetOptions(options);
        using var baseImage = ImageIO.Load(_data, options, pixelFormat: null);
        using var map = DecodeEntry(gainMap, options, pixelFormat: null, cancellationToken);
        return GainMapRenderer.Render(baseImage, map, metadata, metadata.GetWeight(hdrHeadroom), gainMap.AlternatePrimaries, cancellationToken);
    }

    /// <summary>Returns an enumerator over the images.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<AuxiliaryImage> GetEnumerator() => _images.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal Image DecodeEntry(AuxiliaryImage image, ImageDecodeOptions? options, PixelFormat? pixelFormat, CancellationToken cancellationToken)
    {
        options = GetOptions(options);
        if (image.JpegRange is var (offset, length))
            return ImageIO.Load(_data.AsSpan(offset, length), options, pixelFormat);

        // A HEIF item is decoded as the primary item would be, by the decoder of the file
        var context = new ImageCodecContext(options.Configuration, "Image", cancellationToken);
        var codec = ImageInputPump.Detect(ImageCodecRegistry.Current, _data, context);
        var request = ImageDecodeRequest.Create(options, pixelFormat) with { HeifItemId = image.HeifItemId };
        using var parser = codec.CreateDecodeParser(request, context);
        return ImageInputPump.Run(_data, parser, context);
    }

    private static AuxiliaryImageCollection Create(byte[] data, ImageConfiguration configuration)
    {
        var context = new ImageCodecContext(configuration, "AuxiliaryImages", CancellationToken.None);
        var codec = ImageInputPump.Detect(ImageCodecRegistry.Current, data, context);
        return new AuxiliaryImageCollection(data, codec.Format, configuration, CancellationToken.None);
    }

    private static byte[] ReadAll(Stream stream, ImageConfiguration configuration)
    {
        var limit = configuration.Limits.MaxEncodedBytes;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > limit)
                throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, limit, buffer.Length + read);

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private ImageDecodeOptions GetOptions(ImageDecodeOptions? options)
    {
        return options ?? (Configuration == ImageConfiguration.Default
            ? ImageDecodeOptions.Default
            : new ImageDecodeOptions { Configuration = Configuration });
    }
}
