namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Drives an <see cref="ImageParser{TResult}"/> over a span, or over an <see cref="ImageInputBuffer"/> with synchronous or
/// asynchronous reads. The loop is identical for every source, so span, stream and path overloads report the same results
/// and the same error categories.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImageResourceLimits.MaxEncodedBytes"/> bounds the bytes a parser may <em>examine</em> from the start of the
/// input: a span is exposed only up to the limit, and a stream is never read past it. When the parser needs bytes beyond
/// the limit, the operation fails with <see cref="ImageResourceLimitException"/> (<see cref="ImageResourceLimitKind.EncodedBytes"/>,
/// requested = position + required bytes); a limit failure is never reported as truncation, and truncation is never
/// reported as success.
/// </para>
/// <para>
/// Order of checks when the parser needs more bytes: cancellation, the encoded-byte limit, then truncation
/// (<see cref="ImageParser{TResult}.CreateTruncatedException"/>).
/// </para>
/// </remarks>
internal static class ImageInputPump
{
    /// <summary>The number of bytes read before format detection (<see cref="Image.FormatDetectionPrefixLength"/>).</summary>
    public const int DetectionPrefixLength = Image.FormatDetectionPrefixLength;

    public static TResult Run<TResult>(ReadOnlySpan<byte> data, ImageParser<TResult> parser, ImageCodecContext context)
    {
        var limit = context.Limits.MaxEncodedBytes;
        var window = data.Length > limit ? data[..(int)limit] : data;
        var isEndOfInput = window.Length == data.Length;
        var position = 0;
        while (true)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var remaining = window[position..];
            var status = parser.Parse(remaining, isEndOfInput, out var consumed);
            ValidateConsumed(parser, consumed, remaining.Length);
            if (consumed > 0)
            {
                context.Tracker.ChargeEncodedBytes(consumed);
                position += consumed;
            }

            if (status.IsComplete)
                return parser.GetResult();

            if (status.RequiredBytes <= window.Length - position)
            {
                EnsureProgress(parser, consumed);
                continue;
            }

            EnsureRequestWithinLimit(context, position, status.RequiredBytes);
            throw parser.CreateTruncatedException(position);
        }
    }

    public static TResult Run<TResult>(ImageInputBuffer input, ImageParser<TResult> parser, ImageCodecContext context)
    {
        while (true)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var status = ParseBuffered(input, parser, out var consumed);
            if (status.IsComplete)
                return parser.GetResult();

            if (status.RequiredBytes <= input.BufferedLength)
            {
                EnsureProgress(parser, consumed);
                continue;
            }

            EnsureRequestWithinLimit(context, input.Position, status.RequiredBytes);
            if (input.IsEndOfInput)
                throw parser.CreateTruncatedException(input.Position);

            input.Fill(status.RequiredBytes);
        }
    }

    public static async ValueTask<TResult> RunAsync<TResult>(ImageInputBuffer input, ImageParser<TResult> parser, ImageCodecContext context)
    {
        while (true)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var status = ParseBuffered(input, parser, out var consumed);
            if (status.IsComplete)
                return parser.GetResult();

            if (status.RequiredBytes <= input.BufferedLength)
            {
                EnsureProgress(parser, consumed);
                continue;
            }

            EnsureRequestWithinLimit(context, input.Position, status.RequiredBytes);
            if (input.IsEndOfInput)
                throw parser.CreateTruncatedException(input.Position);

            await input.FillAsync(status.RequiredBytes, context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Buffers the detection prefix (or the whole input when it is shorter) and selects the codec.</summary>
    /// <exception cref="UnknownImageFormatException">No codec recognizes the signature.</exception>
    /// <exception cref="ImageResourceLimitException">The encoded-byte limit is smaller than the detection prefix.</exception>
    public static ImageCodec Detect(ImageCodecRegistry registry, ImageInputBuffer input, ImageCodecContext context)
    {
        input.Fill(GetDetectionRequest(context));
        return DetectBuffered(registry, input, context);
    }

    /// <summary>Asynchronously buffers the detection prefix and selects the codec.</summary>
    /// <exception cref="UnknownImageFormatException">No codec recognizes the signature.</exception>
    /// <exception cref="ImageResourceLimitException">The encoded-byte limit is smaller than the detection prefix.</exception>
    public static async ValueTask<ImageCodec> DetectAsync(ImageCodecRegistry registry, ImageInputBuffer input, ImageCodecContext context)
    {
        await input.FillAsync(GetDetectionRequest(context), context.CancellationToken).ConfigureAwait(false);
        return DetectBuffered(registry, input, context);
    }

    /// <summary>Selects the codec from the first bytes of in-memory data.</summary>
    /// <exception cref="UnknownImageFormatException">No codec recognizes the signature.</exception>
    /// <exception cref="ImageResourceLimitException">The encoded-byte limit is smaller than the detection prefix.</exception>
    public static ImageCodec Detect(ImageCodecRegistry registry, ReadOnlySpan<byte> data, ImageCodecContext context)
    {
        if (data.Length >= DetectionPrefixLength)
        {
            EnsureRequestWithinLimit(context, 0, DetectionPrefixLength);
        }

        return registry.Detect(data) ?? throw CreateUnknownFormatException(data.Length);
    }

    private static int GetDetectionRequest(ImageCodecContext context) => (int)Math.Min(DetectionPrefixLength, context.Limits.MaxEncodedBytes);

    private static ImageCodec DetectBuffered(ImageCodecRegistry registry, ImageInputBuffer input, ImageCodecContext context)
    {
        if (input.BufferedLength < DetectionPrefixLength && !input.IsEndOfInput)
        {
            // The limit stopped the read before the prefix: the input is longer than the limit allows
            EnsureRequestWithinLimit(context, 0, DetectionPrefixLength);
        }

        var buffered = input.Buffered.Span;
        return registry.Detect(buffered) ?? throw CreateUnknownFormatException(buffered.Length);
    }

    private static UnknownImageFormatException CreateUnknownFormatException(int length)
    {
        return length < ImageCodecRegistry.MinimumPrefixLength
            ? new UnknownImageFormatException(string.Create(CultureInfo.InvariantCulture, $"The image format is not recognized: the input has {length} bytes, fewer than the {ImageCodecRegistry.MinimumPrefixLength} bytes of a signature."))
            : new UnknownImageFormatException("The image format is not recognized: the data does not start with the signature of a supported format.");
    }

    private static ParseStatus ParseBuffered<TResult>(ImageInputBuffer input, ImageParser<TResult> parser, out int consumed)
    {
        var buffered = input.Buffered.Span;
        var status = parser.Parse(buffered, input.IsEndOfInput, out consumed);
        ValidateConsumed(parser, consumed, buffered.Length);
        input.Consume(consumed);
        return status;
    }

    private static void EnsureRequestWithinLimit(ImageCodecContext context, long position, int requiredBytes)
    {
        var limit = context.Limits.MaxEncodedBytes;
        var requested = position + requiredBytes;
        if (requested > limit)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, limit, requested);
    }

    private static void ValidateConsumed<TResult>(ImageParser<TResult> parser, int consumed, int length)
    {
        if ((uint)consumed > (uint)length)
            throw new InvalidOperationException($"The {parser.GetType().Name} parser consumed {consumed} bytes of a {length}-byte buffer.");
    }

    private static void EnsureProgress<TResult>(ImageParser<TResult> parser, int consumed)
    {
        // A parser may yield with buffered data left (bounded work per call), but it must make progress
        if (consumed == 0)
            throw new InvalidOperationException($"The {parser.GetType().Name} parser requested bytes that were already buffered without consuming any input.");
    }
}
