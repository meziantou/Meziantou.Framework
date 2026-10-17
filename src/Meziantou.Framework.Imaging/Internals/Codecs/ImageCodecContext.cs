namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The per-input state shared by the driver and the codec of one operation (an identify call, an eager load, or a
/// sequential reader): the configuration, the incremental per-input limits (<see cref="InputResourceTracker"/>), the
/// allocation scope charged for the input buffer, decoder state and the decoded images, and the cancellation token.
/// </summary>
internal sealed class ImageCodecContext
{
    public ImageCodecContext(ImageConfiguration configuration, string operation, CancellationToken cancellationToken)
        : this(configuration, operation, sequential: null, cancellationToken)
    {
    }

    public ImageCodecContext(ImageConfiguration configuration, string operation, SequentialDecodeSession? sequential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Configuration = configuration;
        Tracker = new InputResourceTracker(configuration.Limits);
        Scope = AllocationScope.Create(configuration, operation);
        CancellationToken = cancellationToken;
        Sequential = sequential;
    }

    public ImageConfiguration Configuration { get; }

    public ImageResourceLimits Limits => Configuration.Limits;

    /// <summary>Gets the per-input limit accounting (frames, pixels, encoded bytes, metadata bytes).</summary>
    public InputResourceTracker Tracker { get; }

    /// <summary>
    /// Gets the allocation scope of the operation. An eager load creates its image in this scope, so that the image keeps
    /// its own scope once the transient input buffer and decoder state are released. A sequential reader creates every
    /// returned image in this scope: they stay charged to it until they are disposed.
    /// </summary>
    public AllocationScope Scope { get; }

    /// <summary>
    /// Gets the cancellation token of the current call. Synchronous parsers check it between bounded units of work (rows,
    /// frames). A sequential reader replaces it at the start of every call (each call has its own token).
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Gets the sequential decoding session when the input is decoded by an <see cref="ImageReader{TPixel}"/>, or
    /// <see langword="null"/> for eager loads and identification. Container walkers report the header snapshot to it and
    /// stop at its yield points; decoders create their frame sink with <see cref="DecodedFrameSink.Create"/>, which binds
    /// the frames to the reader's requests.
    /// </summary>
    public SequentialDecodeSession? Sequential { get; }

    /// <summary>
    /// Consumes a pending yield request of the sequential session: container walkers call it
    /// after the header callback and after each image end, and return <see cref="ParseStatus.Complete"/> without changing
    /// their state when it returns <see langword="true"/>, so that the next parse call resumes where they stopped.
    /// </summary>
    /// <returns><see langword="true"/> if the walker must return to the reader now.</returns>
    public bool TryConsumeYield() => Sequential?.TryConsumeYield() ?? false;
}
