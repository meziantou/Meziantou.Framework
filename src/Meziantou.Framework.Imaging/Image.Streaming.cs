using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Sequential (bounded-memory) readers and writers
public abstract partial class Image
{
    /// <summary>
    /// Opens a file for sequential frame-by-frame decoding. Only one displayed frame of compositor state is kept in memory,
    /// regardless of the number of frames, as long as the caller disposes the frames it reads.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of the frames returned by the reader.</typeparam>
    /// <param name="path">The path of the file. The reader owns the file stream and closes it when disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageReaderOptions.Default"/>.</param>
    /// <returns>A reader positioned before the first frame, whose <see cref="ImageReader{TPixel}.Info"/> is available.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The header is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static ImageReader<TPixel> OpenReader<TPixel>(string path, ImageReaderOptions? options = null)
        where TPixel : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return new ImageReader<TPixel>(ImageReaderCore.Open(ImageIO.OpenFile(path, asynchronous: false), ownsStream: true, options, pixelFormat));
    }

    /// <summary>Opens a stream for sequential frame-by-frame decoding, starting at its current position. Non-seekable streams are supported.</summary>
    /// <typeparam name="TPixel">The pixel type of the frames returned by the reader.</typeparam>
    /// <param name="stream">The readable source stream. It is left open unless <see cref="ImageReaderOptions.LeaveOpen"/> is <see langword="false"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageReaderOptions.Default"/>.</param>
    /// <returns>A reader positioned before the first frame.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="InvalidImageContentException">The header is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The data uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static ImageReader<TPixel> OpenReader<TPixel>(Stream stream, ImageReaderOptions? options = null)
        where TPixel : unmanaged
    {
        ValidateReadableStream(stream);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return new ImageReader<TPixel>(ImageReaderCore.Open(stream, ownsStream: false, options, pixelFormat));
    }

    /// <summary>Asynchronously opens a file for sequential frame-by-frame decoding.</summary>
    /// <typeparam name="TPixel">The pixel type of the frames returned by the reader.</typeparam>
    /// <param name="path">The path of the file. The reader owns the file stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageReaderOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A reader positioned before the first frame.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown synchronously, before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<ImageReader<TPixel>> OpenReaderAsync<TPixel>(string path, ImageReaderOptions? options = null, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return OpenReaderCoreAsync<TPixel>(path, options, pixelFormat, cancellationToken);
    }

    /// <summary>Asynchronously opens a stream for sequential frame-by-frame decoding, starting at its current position.</summary>
    /// <typeparam name="TPixel">The pixel type of the frames returned by the reader.</typeparam>
    /// <param name="stream">The readable source stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ImageReaderOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A reader positioned before the first frame.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown synchronously, before any I/O.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<ImageReader<TPixel>> OpenReaderAsync<TPixel>(Stream stream, ImageReaderOptions? options = null, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ValidateReadableStream(stream);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return OpenReaderCoreAsync<TPixel>(stream, options, pixelFormat, cancellationToken);
    }

    /// <summary>
    /// Creates a writer that encodes frames one by one to a file. Output goes to a temporary file in the same directory that
    /// is published to <paramref name="path"/> only by a successful <see cref="ImageWriter{TPixel}.Complete"/>; disposing an
    /// uncompleted or failed writer deletes the temporary file and leaves any existing destination unchanged.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of the frames written.</typeparam>
    /// <param name="path">The destination path.</param>
    /// <param name="options">The writer options. <see cref="ImageWriterOptions.Encoder"/> may be <see langword="null"/> to select the encoder from the file extension.</param>
    /// <returns>A writer ready to accept the poster frame (if any) and the displayed frames.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct. Thrown before any I/O.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty or has an unrecognized extension while no encoder is specified, or the options are
    /// invalid for the encoder (for example PNG without <see cref="ImageWriterOptions.ExpectedFrameCount"/>).
    /// </exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static ImageWriter<TPixel> CreateWriter<TPixel>(string path, ImageWriterOptions options)
        where TPixel : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(options);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        return new ImageWriter<TPixel>(ImageWriterCore.Create(path, options, pixelFormat, asynchronous: true));
    }

    /// <summary>Creates a writer that encodes frames one by one to a stream, starting at its current position. Seeking is never required.</summary>
    /// <typeparam name="TPixel">The pixel type of the frames written.</typeparam>
    /// <param name="stream">The writable destination stream. It is left open unless <see cref="ImageWriterOptions.LeaveOpen"/> is <see langword="false"/>.</param>
    /// <param name="options">The writer options. <see cref="ImageWriterOptions.Encoder"/> is required.</param>
    /// <returns>A writer ready to accept the poster frame (if any) and the displayed frames.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="stream"/> is not writable, no encoder is specified, or the options are invalid for the encoder.
    /// </exception>
    public static ImageWriter<TPixel> CreateWriter<TPixel>(Stream stream, ImageWriterOptions options)
        where TPixel : unmanaged
    {
        ValidateWritableStream(stream);
        ArgumentNullException.ThrowIfNull(options);
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        if (options.Encoder is null)
            throw new ArgumentException("An encoder is required when writing to a stream.", nameof(options));

        return new ImageWriter<TPixel>(ImageWriterCore.Create(stream, options, pixelFormat));
    }

    private static async Task<ImageReader<TPixel>> OpenReaderCoreAsync<TPixel>(string path, ImageReaderOptions? options, PixelFormat pixelFormat, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ImageReader<TPixel>(await ImageReaderCore.OpenAsync(ImageIO.OpenFile(path, asynchronous: true), ownsStream: true, options, pixelFormat, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<ImageReader<TPixel>> OpenReaderCoreAsync<TPixel>(Stream stream, ImageReaderOptions? options, PixelFormat pixelFormat, CancellationToken cancellationToken)
        where TPixel : unmanaged
        => new(await ImageReaderCore.OpenAsync(stream, ownsStream: false, options, pixelFormat, cancellationToken).ConfigureAwait(false));
}
