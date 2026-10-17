namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The implementation of the public eager entry points (<c>Image.Identify</c>, <c>Image.Load</c> and their asynchronous
/// variants) over the shared input layer. Arguments are validated by the public methods before
/// calling into this class, so that validation errors are thrown synchronously.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Spans are parsed in place (never copied); streams are read from their current position through an <see cref="ImageInputBuffer"/> and never closed; path overloads own and close their file stream.</description></item>
/// <item><description>Asynchronous variants read with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> (files are opened with <see cref="FileOptions.Asynchronous"/>) and run the synchronous parsers between reads, on the calling context: no <c>Task.Run</c>.</description></item>
/// <item><description>Every operation has its own <see cref="ImageCodecContext"/>: per-input limits, cancellation, and an allocation scope that a loaded image keeps.</description></item>
/// <item><description>Nothing is retained after the call returns; the bytes read are consumed from the caller's stream (no rewinding).</description></item>
/// </list>
/// </remarks>
internal static class ImageIO
{
    public static ImageInfo Identify(ReadOnlySpan<byte> data, ImageIdentifyOptions? options)
    {
        options ??= ImageIdentifyOptions.Default;
        var context = new ImageCodecContext(options.Configuration, "Identify", CancellationToken.None);
        var codec = ImageInputPump.Detect(ImageCodecRegistry.Current, data, context);
        using var parser = codec.CreateIdentifyParser(options.Mode, context);
        return ImageInputPump.Run(data, parser, context);
    }

    public static ImageInfo Identify(Stream stream, ImageIdentifyOptions? options)
        => IdentifyCore(stream, ownsStream: false, options);

    public static ImageInfo Identify(string path, ImageIdentifyOptions? options)
        => IdentifyCore(OpenFile(path, asynchronous: false), ownsStream: true, options);

    public static Task<ImageInfo> IdentifyAsync(Stream stream, ImageIdentifyOptions? options, CancellationToken cancellationToken)
        => IdentifyCoreAsync(ImageCodecRegistry.Current, stream, ownsStream: false, options, cancellationToken);

    public static Task<ImageInfo> IdentifyAsync(string path, ImageIdentifyOptions? options, CancellationToken cancellationToken)
        => IdentifyCoreAsync(ImageCodecRegistry.Current, path, options, cancellationToken);

    public static Image Load(ReadOnlySpan<byte> data, ImageDecodeOptions? options, PixelFormat? pixelFormat)
    {
        options ??= ImageDecodeOptions.Default;
        var context = new ImageCodecContext(options.Configuration, "Image", CancellationToken.None);
        var codec = ImageInputPump.Detect(ImageCodecRegistry.Current, data, context);
        using var parser = codec.CreateDecodeParser(ImageDecodeRequest.Create(options, pixelFormat), context);
        return ImageInputPump.Run(data, parser, context);
    }

    public static Image Load(Stream stream, ImageDecodeOptions? options, PixelFormat? pixelFormat)
        => LoadCore(stream, ownsStream: false, options, pixelFormat);

    public static Image Load(string path, ImageDecodeOptions? options, PixelFormat? pixelFormat)
        => LoadCore(OpenFile(path, asynchronous: false), ownsStream: true, options, pixelFormat);

    public static Task<Image> LoadAsync(Stream stream, ImageDecodeOptions? options, PixelFormat? pixelFormat, CancellationToken cancellationToken)
        => LoadCoreAsync(ImageCodecRegistry.Current, stream, ownsStream: false, options, pixelFormat, cancellationToken);

    public static Task<Image> LoadAsync(string path, ImageDecodeOptions? options, PixelFormat? pixelFormat, CancellationToken cancellationToken)
        => LoadCoreAsync(ImageCodecRegistry.Current, path, options, pixelFormat, cancellationToken);

    /// <summary>
    /// Creates the random-access view a TIFF, BigTIFF, ICO, CUR or ANI input needs, when the source stream can provide one
    /// without copying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These containers are graphs of file offsets, so they cannot be read forward only. On a seekable stream the decoder
    /// reads exactly the bytes the structure points at: describing or decoding the first page of a one-gigabyte document
    /// reads a few kilobytes plus that page. The stream is then seeked freely and its position afterward is unspecified
    /// (the whole remainder of the stream is the input).
    /// </para>
    /// <para>
    /// When the stream cannot seek, the caller falls back to <see cref="WholeInputParser{TResult}"/>, which buffers the
    /// input up to <see cref="ImageResourceLimits.MaxEncodedBytes"/>. The asynchronous entry points always take that path:
    /// an asynchronous decode would otherwise await every tag of the structure for no gain.
    /// </para>
    /// </remarks>
    /// <returns>The view, or <see langword="null"/> when the codec reads a byte stream or the stream cannot seek.</returns>
    private static RandomAccessSource? TryCreateRandomAccessSource(ImageCodec codec, Stream stream, long origin, ImageCodecContext context)
    {
        if (!codec.RequiresRandomAccess || !stream.CanSeek)
            return null;

        return new RandomAccessSource(RandomAccessInput.Create(stream, origin, ownsStream: false), context, codec.Format);
    }

    /// <summary>Opens a file for reading; the caller owns the stream. Internal buffering is disabled (the input buffer reads directly).</summary>
    public static FileStream OpenFile(string path, bool asynchronous)
    {
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 0,
            Options = asynchronous ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.SequentialScan,
        });
    }

    private static ImageInfo IdentifyCore(Stream stream, bool ownsStream, ImageIdentifyOptions? options)
    {
        options ??= ImageIdentifyOptions.Default;
        var context = new ImageCodecContext(options.Configuration, "Identify", CancellationToken.None);
        var origin = stream.CanSeek ? stream.Position : 0;
        using var input = new ImageInputBuffer(stream, ownsStream, context);
        var codec = ImageInputPump.Detect(ImageCodecRegistry.Current, input, context);
        if (TryCreateRandomAccessSource(codec, stream, origin, context) is { } source)
            return codec.IdentifyRandomAccess(source, options.Mode, context);

        using var parser = codec.CreateIdentifyParser(options.Mode, context);
        return ImageInputPump.Run(input, parser, context);
    }

    private static async Task<ImageInfo> IdentifyCoreAsync(ImageCodecRegistry registry, string path, ImageIdentifyOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await IdentifyCoreAsync(registry, OpenFile(path, asynchronous: true), ownsStream: true, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImageInfo> IdentifyCoreAsync(ImageCodecRegistry registry, Stream stream, bool ownsStream, ImageIdentifyOptions? options, CancellationToken cancellationToken)
    {
        options ??= ImageIdentifyOptions.Default;
        var context = new ImageCodecContext(options.Configuration, "Identify", cancellationToken);
        var input = new ImageInputBuffer(stream, ownsStream, context);
        await using (input.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codec = await ImageInputPump.DetectAsync(registry, input, context).ConfigureAwait(false);
            using var parser = codec.CreateIdentifyParser(options.Mode, context);
            return await ImageInputPump.RunAsync(input, parser, context).ConfigureAwait(false);
        }
    }

    private static Image LoadCore(Stream stream, bool ownsStream, ImageDecodeOptions? options, PixelFormat? pixelFormat)
    {
        options ??= ImageDecodeOptions.Default;
        var context = new ImageCodecContext(options.Configuration, "Image", CancellationToken.None);
        var origin = stream.CanSeek ? stream.Position : 0;
        using var input = new ImageInputBuffer(stream, ownsStream, context);
        var codec = ImageInputPump.Detect(ImageCodecRegistry.Current, input, context);
        if (TryCreateRandomAccessSource(codec, stream, origin, context) is { } source)
            return codec.DecodeRandomAccess(source, ImageDecodeRequest.Create(options, pixelFormat), context);

        using var parser = codec.CreateDecodeParser(ImageDecodeRequest.Create(options, pixelFormat), context);
        return ImageInputPump.Run(input, parser, context);
    }

    private static async Task<Image> LoadCoreAsync(ImageCodecRegistry registry, string path, ImageDecodeOptions? options, PixelFormat? pixelFormat, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await LoadCoreAsync(registry, OpenFile(path, asynchronous: true), ownsStream: true, options, pixelFormat, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Image> LoadCoreAsync(ImageCodecRegistry registry, Stream stream, bool ownsStream, ImageDecodeOptions? options, PixelFormat? pixelFormat, CancellationToken cancellationToken)
    {
        options ??= ImageDecodeOptions.Default;
        var context = new ImageCodecContext(options.Configuration, "Image", cancellationToken);
        var input = new ImageInputBuffer(stream, ownsStream, context);
        await using (input.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codec = await ImageInputPump.DetectAsync(registry, input, context).ConfigureAwait(false);
            using var parser = codec.CreateDecodeParser(ImageDecodeRequest.Create(options, pixelFormat), context);
            return await ImageInputPump.RunAsync(input, parser, context).ConfigureAwait(false);
        }
    }
}
