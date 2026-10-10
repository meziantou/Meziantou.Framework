using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

// Content detection, identification, eager loading and raw pixel import
public abstract partial class Image
{
    /// <summary>
    /// The number of leading bytes <see cref="DetectFormat(ReadOnlySpan{byte})"/> needs to recognize every supported format
    /// (18: the whole TGA header, which is the only format without a signature; WebP needs the 12-byte RIFF header, and
    /// PNG, GIF, JPEG and QOI are recognized from 8 bytes).
    /// </summary>
    public const int FormatDetectionPrefixLength = 18;

    /// <summary>Detects the format of encoded image data from its first bytes. File names and extensions are never used.</summary>
    /// <param name="prefix">
    /// The first bytes of the data. Provide at least <see cref="FormatDetectionPrefixLength"/> bytes; a prefix shorter than 8
    /// bytes is reported as <see cref="ImageFormat.Unknown"/>, as no valid supported image is that short, WebP is only
    /// recognized from 12 bytes and TGA only from 18.
    /// </param>
    /// <returns>The detected format, or <see cref="ImageFormat.Unknown"/> if the signature is not recognized. APNG files are reported as <see cref="ImageFormat.Png"/>.</returns>
    public static ImageFormat DetectFormat(ReadOnlySpan<byte> prefix)
    {
        // Built-in codecs only (never a test registry): this is a pure function of the bytes
        return ImageCodecRegistry.Default.Detect(prefix)?.Format ?? ImageFormat.Unknown;
    }

    /// <summary>Reads information about an image file without decoding its pixels.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageIdentifyOptions.Default"/>.</param>
    /// <returns>The image information. Values that cannot be determined in the selected mode are <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The examined data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static ImageInfo Identify(string path, ImageIdentifyOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ImageIO.Identify(path, options);
    }

    /// <summary>
    /// Reads information about an image from a stream, starting at its current position, without decoding pixels. The bytes
    /// read are consumed (the stream is not rewound) and the stream is left open. Non-seekable streams are supported.
    /// </summary>
    /// <param name="stream">The readable source stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageIdentifyOptions.Default"/>.</param>
    /// <returns>The image information.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The examined data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The data uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <remarks>
    /// TIFF, BigTIFF, ICO and CUR are random-access containers: on a seekable stream only the bytes their structure
    /// points at are read, the stream is seeked freely and its position afterward is unspecified. A non-seekable stream
    /// is buffered instead, bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/>. Use
    /// <see cref="ImageCollection"/> to read a large document or to reach a page or representation other than the first
    /// one (<see cref="ImageInfo.CollectionEntryCount"/> reports how many there are).
    /// </remarks>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static ImageInfo Identify(Stream stream, ImageIdentifyOptions? options = null)
    {
        ValidateReadableStream(stream);
        return ImageIO.Identify(stream, options);
    }

    /// <summary>Reads information about in-memory encoded image data without decoding pixels.</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageIdentifyOptions.Default"/>.</param>
    /// <returns>The image information.</returns>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The examined data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The data uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static ImageInfo Identify(ReadOnlySpan<byte> data, ImageIdentifyOptions? options = null)
        => ImageIO.Identify(data, options);

    /// <summary>Asynchronously reads information about an image file without decoding its pixels.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageIdentifyOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The image information.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <remarks>
    /// Asynchronous loads of the random-access containers (TIFF, BigTIFF, ICO, CUR) read the whole input into memory
    /// first, bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/>: their structure is a graph of file offsets,
    /// so awaiting every tag would gain nothing. Use the synchronous overloads or <see cref="ImageCollection"/> for a
    /// large document.
    /// </remarks>
    public static Task<ImageInfo> IdentifyAsync(string path, ImageIdentifyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ImageIO.IdentifyAsync(path, options, cancellationToken);
    }

    /// <summary>Asynchronously reads information about an image from a stream, starting at its current position. The stream is left open.</summary>
    /// <param name="stream">The readable source stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageIdentifyOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The image information.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <remarks>
    /// Asynchronous loads of the random-access containers (TIFF, BigTIFF, ICO, CUR) read the whole input into memory
    /// first, bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/>: their structure is a graph of file offsets,
    /// so awaiting every tag would gain nothing. Use the synchronous overloads or <see cref="ImageCollection"/> for a
    /// large document.
    /// </remarks>
    public static Task<ImageInfo> IdentifyAsync(Stream stream, ImageIdentifyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ValidateReadableStream(stream);
        return ImageIO.IdentifyAsync(stream, options, cancellationToken);
    }

    /// <summary>Decodes an image file into its default pixel format (see <see cref="ImageInfo.PixelFormat"/>).</summary>
    /// <param name="path">The path of the file. The file is closed before the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <returns>A new image owned by the caller. Every displayed frame is decoded (up to <see cref="ImageDecodeOptions.FrameLimit"/>).</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static Image Load(string path, ImageDecodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ImageIO.Load(path, options, pixelFormat: null);
    }

    /// <summary>
    /// Decodes an image from a stream, starting at its current position, into its default pixel format. Non-seekable streams
    /// are supported. The stream is left open and is not retained after the method returns.
    /// </summary>
    /// <param name="stream">The readable source stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The data uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <remarks>
    /// TIFF, BigTIFF, ICO and CUR are random-access containers: on a seekable stream only the bytes their structure
    /// points at are read, the stream is seeked freely and its position afterward is unspecified. A non-seekable stream
    /// is buffered instead, bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/>. Use
    /// <see cref="ImageCollection"/> to read a large document or to reach a page or representation other than the first
    /// one (<see cref="ImageInfo.CollectionEntryCount"/> reports how many there are).
    /// </remarks>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static Image Load(Stream stream, ImageDecodeOptions? options = null)
    {
        ValidateReadableStream(stream);
        return ImageIO.Load(stream, options, pixelFormat: null);
    }

    /// <summary>Decodes in-memory encoded image data into its default pixel format. The data is not retained.</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The data uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static Image Load(ReadOnlySpan<byte> data, ImageDecodeOptions? options = null)
        => ImageIO.Load(data, options, pixelFormat: null);

    /// <summary>Decodes an image file into the requested pixel type.</summary>
    /// <typeparam name="TPixel">The pixel type of the result.</typeparam>
    /// <param name="path">The path of the file. The file is closed before the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>. <see cref="ImageDecodeOptions.Conversion"/> controls alpha removal and color profiles.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file uses an unsupported feature, or the conversion would drop information without an explicit policy.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static Image<TPixel> Load<TPixel>(string path, ImageDecodeOptions? options = null)
        where TPixel : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return (Image<TPixel>)ImageIO.Load(path, options, pixelFormat);
    }

    /// <summary>Decodes an image from a stream, starting at its current position, into the requested pixel type. The stream is left open.</summary>
    /// <typeparam name="TPixel">The pixel type of the result.</typeparam>
    /// <param name="stream">The readable source stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The data uses an unsupported feature, or the conversion would drop information without an explicit policy.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <remarks>
    /// TIFF, BigTIFF, ICO and CUR are random-access containers: on a seekable stream only the bytes their structure
    /// points at are read, the stream is seeked freely and its position afterward is unspecified. A non-seekable stream
    /// is buffered instead, bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/>. Use
    /// <see cref="ImageCollection"/> to read a large document or to reach a page or representation other than the first
    /// one (<see cref="ImageInfo.CollectionEntryCount"/> reports how many there are).
    /// </remarks>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static Image<TPixel> Load<TPixel>(Stream stream, ImageDecodeOptions? options = null)
        where TPixel : unmanaged
    {
        ValidateReadableStream(stream);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return (Image<TPixel>)ImageIO.Load(stream, options, pixelFormat);
    }

    /// <summary>Decodes in-memory encoded image data into the requested pixel type.</summary>
    /// <typeparam name="TPixel">The pixel type of the result.</typeparam>
    /// <param name="data">The encoded data.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The data uses an unsupported feature, or the conversion would drop information without an explicit policy.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static Image<TPixel> Load<TPixel>(ReadOnlySpan<byte> data, ImageDecodeOptions? options = null)
        where TPixel : unmanaged
    {
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return (Image<TPixel>)ImageIO.Load(data, options, pixelFormat);
    }

    /// <summary>Asynchronously decodes an image file into its default pixel format, using asynchronous file I/O.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. No image is returned and no buffer is leaked.</exception>
    public static Task<Image> LoadAsync(string path, ImageDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ImageIO.LoadAsync(path, options, pixelFormat: null, cancellationToken);
    }

    /// <summary>Asynchronously decodes an image from a stream, starting at its current position, into its default pixel format. The stream is left open.</summary>
    /// <param name="stream">The readable source stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <remarks>
    /// Asynchronous loads of the random-access containers (TIFF, BigTIFF, ICO, CUR) read the whole input into memory
    /// first, bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/>: their structure is a graph of file offsets,
    /// so awaiting every tag would gain nothing. Use the synchronous overloads or <see cref="ImageCollection"/> for a
    /// large document.
    /// </remarks>
    public static Task<Image> LoadAsync(Stream stream, ImageDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ValidateReadableStream(stream);
        return ImageIO.LoadAsync(stream, options, pixelFormat: null, cancellationToken);
    }

    /// <summary>Asynchronously decodes an image file into the requested pixel type.</summary>
    /// <typeparam name="TPixel">The pixel type of the result.</typeparam>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown synchronously, before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<Image<TPixel>> LoadAsync<TPixel>(string path, ImageDecodeOptions? options = null, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return CastAsync<TPixel>(ImageIO.LoadAsync(path, options, pixelFormat, cancellationToken));
    }

    /// <summary>Asynchronously decodes an image from a stream, starting at its current position, into the requested pixel type. The stream is left open.</summary>
    /// <typeparam name="TPixel">The pixel type of the result.</typeparam>
    /// <param name="stream">The readable source stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageDecodeOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown synchronously, before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<Image<TPixel>> LoadAsync<TPixel>(Stream stream, ImageDecodeOptions? options = null, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ValidateReadableStream(stream);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return CastAsync<TPixel>(ImageIO.LoadAsync(stream, options, pixelFormat, cancellationToken));
    }

    /// <summary>Creates a single-frame image by copying raw pixels. The source memory is never wrapped or retained.</summary>
    /// <typeparam name="TPixel">The pixel type of the source and of the result.</typeparam>
    /// <param name="source">The source pixels, row by row, top to bottom.</param>
    /// <param name="width">The width of the image. Must be positive.</param>
    /// <param name="height">The height of the image. Must be positive.</param>
    /// <param name="strideInPixels">
    /// The distance between the starts of two consecutive source rows, in pixels, or 0 for tightly packed rows
    /// (equivalent to <paramref name="width"/>). Must be 0 or at least <paramref name="width"/>.
    /// </param>
    /// <param name="configuration">The configuration of the new image, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the stride is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is too short for the dimensions and stride (the last row only needs <paramref name="width"/> pixels).</exception>
    /// <exception cref="ImageResourceLimitException">The image would exceed a configured limit.</exception>
    public static Image<TPixel> ImportPixelData<TPixel>(ReadOnlySpan<TPixel> source, int width, int height, int strideInPixels = 0, ImageConfiguration? configuration = null)
        where TPixel : unmanaged
    {
        _ = PixelFormats.GetPixelFormat<TPixel>();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(strideInPixels);
        var stride = RowStride.Resolve(strideInPixels, width, nameof(strideInPixels), "pixels");
        RowStride.EnsureLength(source.Length, stride, width, height, nameof(source), "pixels");
        configuration ??= ImageConfiguration.Default;
        configuration.Limits.EnsureCanvasWithinLimits(width, height);
        return ImportPixelDataCore(source, width, height, stride, configuration, layoutOptions: null);
    }

    /// <summary>
    /// Creates a single-frame image by copying raw pixel bytes laid out like <typeparamref name="TPixel"/> (16-bit
    /// components in native endianness). The source memory is never wrapped or retained.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of the source layout and of the result.</typeparam>
    /// <param name="source">The source bytes, row by row, top to bottom.</param>
    /// <param name="width">The width of the image. Must be positive.</param>
    /// <param name="height">The height of the image. Must be positive.</param>
    /// <param name="strideInBytes">
    /// The distance between the starts of two consecutive source rows, in bytes, or 0 for tightly packed rows. Must be 0 or
    /// at least <c>width * bytesPerPixel</c>; it does not need to be a multiple of the pixel size.
    /// </param>
    /// <param name="configuration">The configuration of the new image, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the stride is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is too short for the dimensions and stride.</exception>
    /// <exception cref="ImageResourceLimitException">The image would exceed a configured limit.</exception>
    public static Image<TPixel> ImportPixelBytes<TPixel>(ReadOnlySpan<byte> source, int width, int height, int strideInBytes = 0, ImageConfiguration? configuration = null)
        where TPixel : unmanaged
    {
        _ = PixelFormats.GetPixelFormat<TPixel>();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(strideInBytes);
        var rowLength = (long)width * PixelFormats.GetBytesPerPixel(PixelFormats.GetPixelFormat<TPixel>());
        var stride = RowStride.Resolve(strideInBytes, rowLength, nameof(strideInBytes), "bytes");
        RowStride.EnsureLength(source.Length, stride, rowLength, height, nameof(source), "bytes");
        configuration ??= ImageConfiguration.Default;
        configuration.Limits.EnsureCanvasWithinLimits(width, height);
        return ImportPixelBytesCore<TPixel>(source, width, height, stride, configuration, layoutOptions: null);
    }

    /// <summary>Creates a single-frame image of a runtime pixel format for a decoder (<see cref="DecodedImageBuilder"/>): size and limits are already validated.</summary>
    internal static Image CreateForDecoder(PixelFormat format, ImageConfiguration configuration, Size size, AllocationScope scope) => format switch
    {
        PixelFormat.Rgba32 => new Image<Rgba32>(configuration, size, scope, layoutOptions: null),
        PixelFormat.Bgra32 => new Image<Bgra32>(configuration, size, scope, layoutOptions: null),
        PixelFormat.Rgb24 => new Image<Rgb24>(configuration, size, scope, layoutOptions: null),
        PixelFormat.Rgba64 => new Image<Rgba64>(configuration, size, scope, layoutOptions: null),
        PixelFormat.Gray8 => new Image<Gray8>(configuration, size, scope, layoutOptions: null),
        PixelFormat.Gray16 => new Image<Gray16>(configuration, size, scope, layoutOptions: null),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The pixel format is not supported."),
    };

    /// <summary>Gets a displayed frame of an image under construction.</summary>
    internal static ImageFrame GetFrameForDecoder(Image image, int index) => image.FrameCore(index);

    /// <summary>Appends a zeroed frame to an image under construction (the decoder charged it to its per-input tracker).</summary>
    internal static ImageFrame AppendFrameForDecoder(Image image) => image.AppendFrameCore(new FrameMetadata());

    /// <summary>Attaches a zeroed poster frame to an image under construction.</summary>
    internal static ImageFrame AttachPosterForDecoder(Image image) => image.AttachPosterFrameCore(new FrameMetadata());

    private static async Task<Image<TPixel>> CastAsync<TPixel>(Task<Image> task)
        where TPixel : unmanaged
        => (Image<TPixel>)await task.ConfigureAwait(false);

    /// <summary>Copies validated raw pixels into a new image (see <see cref="ImportPixelData{TPixel}(ReadOnlySpan{TPixel}, int, int, int, ImageConfiguration?)"/>).</summary>
    /// <remarks><c>layoutOptions</c> selects the storage layout of the new image (tests force segmented or padded storage); <see langword="null"/> is the default layout.</remarks>
    internal static Image<TPixel> ImportPixelDataCore<TPixel>(ReadOnlySpan<TPixel> source, int width, int height, long stride, ImageConfiguration configuration, PixelStorageLayoutOptions? layoutOptions)
        where TPixel : unmanaged
    {
        var image = new Image<TPixel>(configuration, new Size(width, height), scope: null, layoutOptions);
        using (var lease = image.FrameAt(0).Storage.AcquireLease())
        {
            for (var y = 0; y < height; y++)
            {
                source.Slice(RowStride.GetOffset(y, stride), width).CopyTo(lease.GetRow<TPixel>(y));
            }
        }

        return image;
    }

    /// <summary>Copies validated raw pixel bytes into a new image (see <see cref="ImportPixelBytes{TPixel}(ReadOnlySpan{byte}, int, int, int, ImageConfiguration?)"/>).</summary>
    /// <remarks><c>layoutOptions</c> selects the storage layout of the new image (tests force segmented or padded storage); <see langword="null"/> is the default layout.</remarks>
    internal static Image<TPixel> ImportPixelBytesCore<TPixel>(ReadOnlySpan<byte> source, int width, int height, long stride, ImageConfiguration configuration, PixelStorageLayoutOptions? layoutOptions)
        where TPixel : unmanaged
    {
        var image = new Image<TPixel>(configuration, new Size(width, height), scope: null, layoutOptions);
        using (var lease = image.FrameAt(0).Storage.AcquireLease())
        {
            var rowLength = lease.RowLength;
            for (var y = 0; y < height; y++)
            {
                source.Slice(RowStride.GetOffset(y, stride), rowLength).CopyTo(lease.GetRowBytes(y));
            }
        }

        return image;
    }
}
