namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The entry points of <see cref="JpegXlTranscoder"/>: they run the push parsers of the two formats on a span or on a
/// stream, like <see cref="ImageIO"/>, and write the output once it is whole.
/// </summary>
internal static class JpegXlTranscodeIO
{
    private const string Operation = "JpegXlTranscoder";

    public static bool HasJpegReconstructionData(ReadOnlySpan<byte> data, JpegXlTranscodeOptions? options)
    {
        var context = CreateContext(options, CancellationToken.None);
        EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, data, context), ImageFormat.JpegXl);
        using var parser = new JpegXlReconstructionProbe(context);
        return ImageInputPump.Run(data, parser, context);
    }

    public static bool HasJpegReconstructionData(Stream stream, bool ownsStream, JpegXlTranscodeOptions? options)
    {
        var context = CreateContext(options, CancellationToken.None);
        using var input = new ImageInputBuffer(stream, ownsStream, context);
        EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, input, context), ImageFormat.JpegXl);
        using var parser = new JpegXlReconstructionProbe(context);
        return ImageInputPump.Run(input, parser, context);
    }

    public static async Task<bool> HasJpegReconstructionDataAsync(Stream stream, bool ownsStream, JpegXlTranscodeOptions? options, CancellationToken cancellationToken)
    {
        var context = CreateContext(options, cancellationToken);
        var input = new ImageInputBuffer(stream, ownsStream, context);
        await using (input.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFormat(await ImageInputPump.DetectAsync(ImageCodecRegistry.Current, input, context).ConfigureAwait(false), ImageFormat.JpegXl);
            using var parser = new JpegXlReconstructionProbe(context);
            return await ImageInputPump.RunAsync(input, parser, context).ConfigureAwait(false);
        }
    }

    public static bool ReconstructJpeg(ReadOnlySpan<byte> data, Stream destination, JpegXlTranscodeOptions? options, bool throwIfMissing)
    {
        var context = CreateContext(options, CancellationToken.None);
        EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, data, context), ImageFormat.JpegXl);
        using var parser = new JpegXlReconstructionParser(context);
        var reconstructor = ImageInputPump.Run(data, parser, context);
        if (!HasData(reconstructor, throwIfMissing))
            return false;

        destination.Write(reconstructor.JpegFile.Span);
        return true;
    }

    public static bool ReconstructJpeg(Stream stream, bool ownsStream, Stream? destination, string? destinationPath, JpegXlTranscodeOptions? options, bool throwIfMissing)
    {
        var context = CreateContext(options, CancellationToken.None);
        using var input = new ImageInputBuffer(stream, ownsStream, context);
        EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, input, context), ImageFormat.JpegXl);
        using var parser = new JpegXlReconstructionParser(context);
        var reconstructor = ImageInputPump.Run(input, parser, context);
        if (!HasData(reconstructor, throwIfMissing))
            return false;

        Write(reconstructor.JpegFile, destination, destinationPath);
        return true;
    }

    public static async Task<bool> ReconstructJpegAsync(Stream stream, bool ownsStream, Stream? destination, string? destinationPath, JpegXlTranscodeOptions? options, bool throwIfMissing, CancellationToken cancellationToken)
    {
        var context = CreateContext(options, cancellationToken);
        var input = new ImageInputBuffer(stream, ownsStream, context);
        await using (input.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFormat(await ImageInputPump.DetectAsync(ImageCodecRegistry.Current, input, context).ConfigureAwait(false), ImageFormat.JpegXl);
            using var parser = new JpegXlReconstructionParser(context);
            var reconstructor = await ImageInputPump.RunAsync(input, parser, context).ConfigureAwait(false);
            if (!HasData(reconstructor, throwIfMissing))
                return false;

            await WriteAsync(reconstructor.JpegFile, destination, destinationPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    public static void RecompressJpeg(ReadOnlySpan<byte> data, Stream? destination, string? destinationPath, JpegXlTranscodeOptions? options)
    {
        options ??= JpegXlTranscodeOptions.Default;
        var context = CreateContext(options, CancellationToken.None);
        EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, data, context), ImageFormat.Jpeg);
        Write(JpegXlJpegRecompressor.Recompress(data, options.Effort, context), destination, destinationPath);
    }

    public static void RecompressJpeg(Stream stream, bool ownsStream, Stream? destination, string? destinationPath, JpegXlTranscodeOptions? options)
    {
        options ??= JpegXlTranscodeOptions.Default;
        var context = CreateContext(options, CancellationToken.None);
        using var input = new ImageInputBuffer(stream, ownsStream, context);
        EnsureFormat(ImageInputPump.Detect(ImageCodecRegistry.Current, input, context), ImageFormat.Jpeg);
        using var parser = new JpegRecompressionParser(options.Effort, context);
        Write(ImageInputPump.Run(input, parser, context), destination, destinationPath);
    }

    public static async Task RecompressJpegAsync(Stream stream, bool ownsStream, Stream? destination, string? destinationPath, JpegXlTranscodeOptions? options, CancellationToken cancellationToken)
    {
        options ??= JpegXlTranscodeOptions.Default;
        var context = CreateContext(options, cancellationToken);
        var input = new ImageInputBuffer(stream, ownsStream, context);
        await using (input.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFormat(await ImageInputPump.DetectAsync(ImageCodecRegistry.Current, input, context).ConfigureAwait(false), ImageFormat.Jpeg);
            using var parser = new JpegRecompressionParser(options.Effort, context);
            await WriteAsync(await ImageInputPump.RunAsync(input, parser, context).ConfigureAwait(false), destination, destinationPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes a file that is whole in memory: to the stream of the caller, or to a temporary file that replaces the destination once it is written.</summary>
    public static void Write(ReadOnlyMemory<byte> file, Stream? destination, string? destinationPath)
    {
        if (destination is not null)
        {
            destination.Write(file.Span);
            return;
        }

        var output = AtomicFileOutput.Create(destinationPath!, asynchronous: false);
        try
        {
            output.Stream.Write(file.Span);
            output.Publish();
        }
        catch
        {
            output.Abort();
            throw;
        }
    }

    public static async ValueTask WriteAsync(ReadOnlyMemory<byte> file, Stream? destination, string? destinationPath, CancellationToken cancellationToken)
    {
        if (destination is not null)
        {
            await destination.WriteAsync(file, cancellationToken).ConfigureAwait(false);
            return;
        }

        var output = AtomicFileOutput.Create(destinationPath!, asynchronous: true);
        try
        {
            await output.Stream.WriteAsync(file, cancellationToken).ConfigureAwait(false);
            await output.PublishAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await output.AbortAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static ImageCodecContext CreateContext(JpegXlTranscodeOptions? options, CancellationToken cancellationToken)
        => new((options ?? JpegXlTranscodeOptions.Default).Configuration, Operation, cancellationToken);

    /// <summary>Checks that the input, whose format is recognized, has the format the operation takes.</summary>
    public static void EnsureFormat(ImageCodec codec, ImageFormat expected)
    {
        if (codec.Format != expected)
            throw new UnsupportedImageFeatureException($"The input is a {ImageFormatNames.Get(codec.Format)} file: this operation takes a {ImageFormatNames.Get(expected)} file.", codec.Format, $"transcoding a {ImageFormatNames.Get(codec.Format)} file");
    }

    private static bool HasData(JpegXlJpegReconstructor reconstructor, bool throwIfMissing)
    {
        if (reconstructor.HasReconstructionData)
            return true;

        if (throwIfMissing)
            throw JpegXlFormat.Unsupported("The JPEG XL file does not hold the data that rebuild a JPEG file: it was not made from a JPEG file, or not in a way that keeps the file.", "JPEG reconstruction data");

        return false;
    }
}
