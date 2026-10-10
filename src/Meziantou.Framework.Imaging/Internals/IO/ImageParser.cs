namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A synchronous, incremental (push-model) parser of encoded image data: the shared contract of every identifier and
/// decoder.
/// </summary>
/// <remarks>
/// <para>
/// The driver (<see cref="ImageInputPump"/>) owns the input: it reads from the span, stream or file (synchronously or with
/// real asynchronous I/O) into a bounded buffer and calls <see cref="Parse"/> with the unconsumed bytes. The parser consumes
/// what it can, and either completes or asks for a minimum number of contiguous bytes. All CPU work happens inside
/// <see cref="Parse"/>, on the caller's thread, so the same parser serves the synchronous and asynchronous APIs without
/// <c>Task.Run</c>.
/// </para>
/// <para>
/// Parsers must keep their requests bounded (structure headers, one metadata segment...), and stream large payloads by
/// consuming partial buffers. Consumed bytes are charged to <see cref="ImageResourceLimits.MaxEncodedBytes"/> by the driver.
/// When the input ends while the parser still needs bytes, the driver throws <see cref="CreateTruncatedException"/>.
/// </para>
/// </remarks>
/// <typeparam name="TResult">The result type (<see cref="ImageInfo"/> or <see cref="Image"/>).</typeparam>
internal abstract class ImageParser<TResult> : IDisposable
{
    protected ImageParser(ImageFormat format) => Format = format;

    /// <summary>Gets the format being parsed (reported in exceptions).</summary>
    public ImageFormat Format { get; }

    /// <summary>Parses as much of <paramref name="buffer"/> as possible.</summary>
    /// <param name="buffer">The unconsumed input bytes, starting at the current position. Never retained after the call.</param>
    /// <param name="isEndOfInput"><see langword="true"/> when no byte follows <paramref name="buffer"/>.</param>
    /// <param name="consumed">The number of bytes of <paramref name="buffer"/> consumed by this call.</param>
    /// <returns><see cref="ParseStatus.Complete"/>, or the number of contiguous bytes needed to make progress.</returns>
    public abstract ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed);

    /// <summary>Gets the result once <see cref="Parse"/> returned <see cref="ParseStatus.Complete"/>. For images, ownership is transferred to the caller.</summary>
    /// <returns>The result.</returns>
    public abstract TResult GetResult();

    /// <summary>Creates the exception reported when the input ends before the parser completes.</summary>
    /// <param name="position">The number of bytes consumed from the input.</param>
    /// <returns>The exception (an <see cref="InvalidImageContentException"/>: truncation is never a clean end of input).</returns>
    public virtual Exception CreateTruncatedException(long position)
        => new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The {ImageFormatNames.Get(Format)} data is truncated (unexpected end of input after {position} bytes)."), Format);

    /// <summary>Releases the parser state (and a partially built image that was not transferred).</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
