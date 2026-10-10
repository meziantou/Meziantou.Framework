namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Drives an <see cref="ImageParser{TResult}"/> over a span, or over an <see cref="ImageInputBuffer"/> with synchronous or
/// asynchronous reads. The loop is identical for every source, so span, stream and path overloads report the same results
/// and the same error categories.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImageResourceLimits.MaxEncodedBytes"/> bounds the bytes a parser may <em>examine</em> from the start of the
/// input: a span is exposed only up to the limit, and no byte of a stream past it is buffered. When the parser needs bytes
/// beyond the limit, the operation fails with <see cref="ImageResourceLimitException"/> (<see cref="ImageResourceLimitKind.EncodedBytes"/>,
/// requested = position + required bytes); a limit failure is never reported as truncation, and truncation is never
/// reported as success.
/// </para>
/// <para>
/// Order of checks when the parser needs more bytes: cancellation, the encoded-byte limit, then truncation
/// (<see cref="ImageParser{TResult}.CreateTruncatedException"/>).
/// </para>
/// <para>
/// A request the end of the input satisfies (<see cref="ParseStatus.NeedMoreDataOrEnd"/>, and the format detection prefix
/// of an input shorter than the prefix) only fails with the limit when the input continues past it: an input that ends
/// exactly at the limit is complete, whatever the source. A span knows its length; a stream is read up to the limit, then
/// probed with a single discarded byte (<see cref="ImageInputBuffer.ProbeEndOfInput"/>), the only read past the limit.
/// The stream is filled or probed once per parser call, so the request that fails is the one the parser made with every
/// byte the limit allows in its buffer, exactly as with a span.
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

            switch (GetInputAction(input, parser, context, status, consumed, out var minimum))
            {
                case InputAction.Fill:
                    input.Fill(minimum);
                    break;

                case InputAction.Probe:
                    input.ProbeEndOfInput();
                    EnsureInputEnded(input, context, status);
                    break;
            }
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

            switch (GetInputAction(input, parser, context, status, consumed, out var minimum))
            {
                case InputAction.Fill:
                    await input.FillAsync(minimum, context.CancellationToken).ConfigureAwait(false);
                    break;

                case InputAction.Probe:
                    await input.ProbeEndOfInputAsync(context.CancellationToken).ConfigureAwait(false);
                    EnsureInputEnded(input, context, status);
                    break;
            }
        }
    }

    /// <summary>Buffers the detection prefix (or the whole input when it is shorter) and selects the codec.</summary>
    /// <exception cref="UnknownImageFormatException">No codec recognizes the signature.</exception>
    /// <exception cref="ImageResourceLimitException">The encoded-byte limit is smaller than the detection prefix.</exception>
    public static ImageCodec Detect(ImageCodecRegistry registry, ImageInputBuffer input, ImageCodecContext context)
    {
        input.Fill(GetDetectionRequest(context));
        if (IsDetectionPrefixCutByLimit(input))
        {
            input.ProbeEndOfInput();
        }

        return DetectBuffered(registry, input, context);
    }

    /// <summary>Asynchronously buffers the detection prefix and selects the codec.</summary>
    /// <exception cref="UnknownImageFormatException">No codec recognizes the signature.</exception>
    /// <exception cref="ImageResourceLimitException">The encoded-byte limit is smaller than the detection prefix.</exception>
    public static async ValueTask<ImageCodec> DetectAsync(ImageCodecRegistry registry, ImageInputBuffer input, ImageCodecContext context)
    {
        await input.FillAsync(GetDetectionRequest(context), context.CancellationToken).ConfigureAwait(false);
        if (IsDetectionPrefixCutByLimit(input))
        {
            await input.ProbeEndOfInputAsync(context.CancellationToken).ConfigureAwait(false);
        }

        return DetectBuffered(registry, input, context);
    }

    /// <summary>Selects the codec from the first bytes of in-memory data.</summary>
    /// <exception cref="UnknownImageFormatException">No codec recognizes the signature.</exception>
    /// <exception cref="ImageResourceLimitException">The encoded-byte limit is smaller than the detection prefix.</exception>
    public static ImageCodec Detect(ImageCodecRegistry registry, ReadOnlySpan<byte> data, ImageCodecContext context)
    {
        if (Math.Min(data.Length, DetectionPrefixLength) > context.Limits.MaxEncodedBytes)
        {
            // The limit cuts the prefix (the whole input when it is shorter): the input is longer than the limit allows
            EnsureRequestWithinLimit(context, 0, DetectionPrefixLength);
        }

        return registry.Detect(data) ?? throw CreateUnknownFormatException(data.Length);
    }

    private enum InputAction
    {
        /// <summary>The buffer already holds the request: call the parser again.</summary>
        Parse,

        /// <summary>Read from the stream until the buffer holds the reported number of bytes.</summary>
        Fill,

        /// <summary>Every byte the limit allows is buffered: find out whether the input ends there.</summary>
        Probe,
    }

    private static int GetDetectionRequest(ImageCodecContext context) => (int)Math.Min(DetectionPrefixLength, context.Limits.MaxEncodedBytes);

    /// <summary>
    /// Gets a value indicating whether the limit stopped the read of the detection prefix: an input that ends there is
    /// detected from its bytes like a short span, so the end must be told from a longer input.
    /// </summary>
    private static bool IsDetectionPrefixCutByLimit(ImageInputBuffer input) => input.BufferedLength < DetectionPrefixLength && input.IsReadLimitReached;

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

    /// <summary>
    /// Decides what a stream driver does with a request, so that the synchronous and asynchronous loops cannot diverge:
    /// the checks and their order are those of the span driver.
    /// </summary>
    private static InputAction GetInputAction<TResult>(ImageInputBuffer input, ImageParser<TResult> parser, ImageCodecContext context, ParseStatus status, int consumed, out int minimum)
    {
        minimum = status.RequiredBytes;
        if (status.RequiredBytes <= input.BufferedLength)
        {
            EnsureProgress(parser, consumed);
            return InputAction.Parse;
        }

        if (!status.AcceptsEndOfInput || input.IsEndOfInput)
        {
            EnsureRequestWithinLimit(context, input.Position, status.RequiredBytes);
            if (input.IsEndOfInput)
                throw parser.CreateTruncatedException(input.Position);

            return InputAction.Fill;
        }

        // The end of the input satisfies the request, so the limit only fails it when the input continues past the limit
        if (input.IsReadLimitReached)
            return InputAction.Probe;

        // Never buffer more than the limit allows: the parser is called again with what was read, and repeats its request
        minimum = (int)Math.Min(status.RequiredBytes, context.Limits.MaxEncodedBytes - input.Position);
        return InputAction.Fill;
    }

    /// <summary>Reports the limit once a probe found that the input continues past it.</summary>
    private static void EnsureInputEnded(ImageInputBuffer input, ImageCodecContext context, ParseStatus status)
    {
        if (input.ExceedsLimit)
        {
            EnsureRequestWithinLimit(context, input.Position, status.RequiredBytes);
        }
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
