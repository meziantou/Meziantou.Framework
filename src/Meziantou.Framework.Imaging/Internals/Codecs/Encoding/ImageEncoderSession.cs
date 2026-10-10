namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The internal encoder hook implemented by the codecs: the encoding state of
/// one output, driven by <see cref="ImageWriterCore"/> for writers and eager saves. This is not a
/// public plug-in interface.
/// </summary>
/// <remarks>
/// <para>
/// The shared writer owns everything that is not format specific: the option snapshot and the container constraints
/// (<see cref="ImageOutputCapabilities"/>), the metadata policy (<see cref="MetadataWritePlan"/>, computed before any output),
/// call ordering, frame-count checks, frame validation (canvas size, pixel format, liveness), overlapping-call rejection,
/// faulting, cancellation between steps, flushing (synchronous or truly asynchronous) and atomic path publication.
/// </para>
/// <para>
/// Sessions are synchronous push-model encoders, like decoders: each operation starts with a <c>Begin*</c> call, then the
/// writer calls <see cref="Encode"/> repeatedly, flushing <see cref="ImageOutputBuffer"/> to the destination between calls
/// (with <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> for asynchronous calls), until it returns
/// <see langword="true"/>. A session should do a bounded amount of work per call (for example a band of rows) so that the
/// buffered output stays bounded; it never performs I/O.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="ValidateFrame"/> is the codec preflight: called before anything of the frame is written, its exceptions
/// (alpha or precision loss without policy, unrepresentable duration...) leave the writer usable.
/// </description></item>
/// <item><description>
/// Frames are borrowed until the operation completes: a session may keep a reference between <c>Begin*</c> and the final
/// <see cref="Encode"/> call (leasing the pixels per call), but must copy whatever it needs later (for example the previous
/// frame of a delta encoder) into private state charged to <see cref="ImageEncoderSessionOptions.Scope"/>.
/// </description></item>
/// <item><description>
/// Any exception from <c>Begin*</c> or <see cref="Encode"/> faults the writer: the session is disposed, nothing else is
/// written and a path output is not published.
/// </description></item>
/// <item><description>
/// Call order, already validated by the writer: optional <see cref="BeginPosterFrame"/> (only when
/// <see cref="ImageOutputCapabilities.SupportsPosterFrame"/>), then <see cref="BeginFrame"/> for each frame (index 0, 1...),
/// then <see cref="BeginComplete"/> once (at least one frame, count equal to the expected count when known).
/// </description></item>
/// </list>
/// </remarks>
internal abstract class ImageEncoderSession : IDisposable
{
    protected ImageEncoderSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    public ImageEncoderSessionOptions Options { get; }

    /// <summary>Gets the cancellation token of the current writer call; sessions may check it between bounded units of work.</summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>Validates that a frame can be encoded before anything of it is written. Exceptions leave the writer usable.</summary>
    /// <param name="frame">The frame (canvas size and pixel format already validated).</param>
    /// <param name="isPoster"><see langword="true"/> for the separate poster frame.</param>
    public virtual void ValidateFrame(ImageFrame frame, bool isPoster)
    {
    }

    /// <summary>Starts writing the separate poster frame (before frame zero).</summary>
    /// <param name="frame">The poster, borrowed until the operation completes. Its duration is ignored.</param>
    public abstract void BeginPosterFrame(ImageFrame frame);

    /// <summary>Starts writing a displayed frame.</summary>
    /// <param name="frame">The frame, borrowed until the operation completes. Its duration is <c>frame.Metadata.Duration</c>.</param>
    /// <param name="index">The index of the frame in playback order.</param>
    public abstract void BeginFrame(ImageFrame frame, int index);

    /// <summary>Starts writing the trailing data (for example the IEND chunk or the GIF trailer).</summary>
    /// <param name="frameCount">The number of displayed frames written (validated against the expected count).</param>
    public abstract void BeginComplete(int frameCount);

    /// <summary>Performs a bounded unit of work of the current operation, appending the encoded bytes to <paramref name="output"/>.</summary>
    /// <param name="output">The output buffer, flushed by the writer between calls.</param>
    /// <returns><see langword="true"/> when the current operation is finished.</returns>
    public abstract bool Encode(ImageOutputBuffer output);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the private state (row buffers, compressor, palette...). Called on completion, on fault and on abort.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}
