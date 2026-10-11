using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel-type-independent implementation of <see cref="ImageWriter{TPixel}"/> and of the eager <c>Image.Save</c> methods
///: the option snapshot, the shared container constraints, the call-order state
/// machine, flushing and atomic path publication around a codec's <see cref="ImageEncoderSession"/>.
/// </summary>
/// <remarks>
/// <para>State machine: <c>Ready</c> → <c>Completed</c> (successful <see cref="Complete"/>), or <c>Faulted</c>; then <c>Disposed</c>.</para>
/// <list type="bullet">
/// <item><description>
/// Creation validates everything that does not depend on pixels before any output exists (encoder resolution, frame count
/// and animation constraints, canvas limits, metadata policy, encoder availability); stream writers write nothing until the
/// first frame, path writers create their temporary file last.
/// </description></item>
/// <item><description>
/// Preflight failures (state and argument errors, frame-count overflow, missing frames at completion, a pre-canceled token,
/// codec frame validation, overlapping calls) leave the writer usable. Failures once encoding started (codec errors, I/O,
/// cancellation) fault it: the session and buffers are released immediately and a path output is not published.
/// </description></item>
/// <item><description>Frames are borrowed until the call (or the returned task) completes; nothing references them afterward.</description></item>
/// <item><description>A successful <see cref="Complete"/> may be repeated (no effect). Disposal without one aborts the output.</description></item>
/// <item><description>
/// Caller streams are left open unless <see cref="ImageWriterOptions.LeaveOpen"/> is false; they are flushed by completion. They
/// are never sought, except by outputs whose capabilities require a seekable destination (animated WebP): such writers reject a
/// non-seekable stream when they are created, and write the session's patches (<see cref="ImageOutputBuffer.Patches"/>) at
/// completion, after the last byte, relative to the stream position at creation.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class ImageWriterCore : IDisposable, IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly AtomicFileOutput? _file;
    private readonly ImageOutputBuffer _output;
    private readonly long _startPosition;
    private ImageEncoderSession? _session;
    private WriterState _state;
    private int _busy;
    private bool _posterWritten;

    private ImageWriterCore(ImageWriterOptions options, ImageOutputCapabilities capabilities, PixelFormat pixelFormat, ImageEncoderSession session, AllocationScope scope, Stream stream, bool ownsStream, AtomicFileOutput? file)
    {
        Options = options;
        Capabilities = capabilities;
        PixelFormat = pixelFormat;
        Scope = scope;
        _session = session;
        _stream = stream;
        _ownsStream = ownsStream;
        _file = file;
        _output = new ImageOutputBuffer(scope);
        _startPosition = capabilities.RequiresSeekableOutput ? stream.Position : 0;
    }

    private enum WriterState
    {
        Ready,
        Completed,
        Faulted,
        Disposed,
    }

    /// <summary>Gets the option snapshot (encoder resolved; metadata and animation settings cloned).</summary>
    public ImageWriterOptions Options { get; }

    public ImageOutputCapabilities Capabilities { get; }

    public PixelFormat PixelFormat { get; }

    public Size CanvasSize => Options.CanvasSize;

    public ImageFormat Format => Capabilities.Format;

    public int FramesWritten { get; private set; }

    /// <summary>Gets the writer allocation scope (session state and output buffer; tests only).</summary>
    internal AllocationScope Scope { get; }

    /// <summary>Gets the temporary file path of a path writer (tests only).</summary>
    internal string? TemporaryPath => _file?.TemporaryPath;

    /// <summary>Creates a writer over a caller stream.</summary>
    /// <exception cref="ArgumentException">No encoder, or the options are invalid for the output.</exception>
    public static ImageWriterCore Create(Stream stream, ImageWriterOptions options, PixelFormat pixelFormat)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var snapshot = CreateSnapshot(options, encoder: null);
        var (capabilities, session, scope) = Prepare(snapshot, pixelFormat, stream);
        return new ImageWriterCore(snapshot, capabilities, pixelFormat, session, scope, stream, ownsStream: !snapshot.LeaveOpen, file: null);
    }

    /// <summary>Creates a writer publishing a file atomically. The encoder is inferred from the extension when not specified.</summary>
    /// <exception cref="ArgumentException">The extension is not recognized, or the options are invalid for the output.</exception>
    public static ImageWriterCore Create(string path, ImageWriterOptions options, PixelFormat pixelFormat, bool asynchronous)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = CreateSnapshot(options, options.Encoder ?? ImageEncoder.FromPath(path));
        var (capabilities, session, scope) = Prepare(snapshot, pixelFormat, stream: null);
        AtomicFileOutput file;
        try
        {
            file = AtomicFileOutput.Create(path, asynchronous);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        return new ImageWriterCore(snapshot, capabilities, pixelFormat, session, scope, file.Stream, ownsStream: true, file);
    }

    /// <summary>
    /// Runs the frame preflight (the metadata policy of the frame settings, then <see cref="ImageEncoderSession.ValidateFrame"/>)
    /// on every image of an eager save before any output, so that an unrepresentable frame (for example a duration that does
    /// not fit the format, or a cursor hotspot the format cannot store) fails before the first byte instead of after the
    /// preceding frames were written.
    /// </summary>
    /// <param name="poster">The separate poster, if any.</param>
    /// <param name="frames">The displayed frames.</param>
    public void PreflightFrames(ImageFrame? poster, IEnumerable<ImageFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var session = _session ?? throw new InvalidOperationException("The writer has no active encoding session.");
        if (poster is not null)
        {
            Preflight(session, poster, isPoster: true);
        }

        foreach (var frame in frames)
        {
            Preflight(session, frame, isPoster: false);
        }
    }

    public void WritePosterFrame(ImageFrame frame)
    {
        Enter(CancellationToken.None);
        try
        {
            ValidatePoster(frame);
            Run(isPoster: true, frame);
        }
        finally
        {
            Exit();
        }
    }

    public ValueTask WritePosterFrameAsync(ImageFrame frame, CancellationToken cancellationToken)
    {
        // Argument errors are reported synchronously
        ArgumentNullException.ThrowIfNull(frame);
        ValidateFrameShape(frame);
        return WritePosterFrameCoreAsync(frame, cancellationToken);
    }

    public void WriteFrame(ImageFrame frame)
    {
        Enter(CancellationToken.None);
        try
        {
            ValidateFrame(frame);
            Run(isPoster: false, frame);
        }
        finally
        {
            Exit();
        }
    }

    public ValueTask WriteFrameAsync(ImageFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateFrameShape(frame);
        return WriteFrameCoreAsync(frame, cancellationToken);
    }

    public void Complete()
    {
        if (EnterComplete(CancellationToken.None))
            return;

        try
        {
            ValidateCompletion();
            var session = BeginOperation(CancellationToken.None);
            try
            {
                session.BeginComplete(FramesWritten);
                Pump(session);
                ApplyPatches();
                if (_file is not null)
                {
                    _file.Publish();
                }
                else
                {
                    _stream.Flush();
                }

                OnCompleted();
            }
            catch
            {
                Fault();
                throw;
            }
        }
        finally
        {
            Exit();
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (EnterComplete(cancellationToken))
            return;

        try
        {
            ValidateCompletion();
            var session = BeginOperation(cancellationToken);
            try
            {
                session.BeginComplete(FramesWritten);
                await PumpAsync(session, cancellationToken).ConfigureAwait(false);
                await ApplyPatchesAsync(cancellationToken).ConfigureAwait(false);
                if (_file is not null)
                {
                    await _file.PublishAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                OnCompleted();
            }
            catch
            {
                Fault();
                throw;
            }
        }
        finally
        {
            Exit();
        }
    }

    public void Dispose()
    {
        if (!BeginDispose())
            return;

        ReleaseSession();
        _file?.Abort();
        if (_ownsStream && _file is null)
        {
            _stream.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!BeginDispose())
            return;

        ReleaseSession();
        if (_file is not null)
        {
            await _file.AbortAsync().ConfigureAwait(false);
        }
        else if (_ownsStream)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ImageWriterOptions CreateSnapshot(ImageWriterOptions options, ImageEncoder? encoder)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = options.CreateSnapshot();
        if (encoder is null)
        {
            if (snapshot.Encoder is null)
                throw new ArgumentException("An encoder is required when writing to a stream.", nameof(options));

            return snapshot;
        }

        return new ImageWriterOptions(snapshot.CanvasSize)
        {
            Encoder = encoder,
            ExpectedFrameCount = snapshot.ExpectedFrameCount,
            Metadata = snapshot.Metadata,
            Animation = snapshot.Animation,
            Configuration = snapshot.Configuration,
            LeaveOpen = snapshot.LeaveOpen,
        };
    }

    /// <summary>Validates the snapshot and creates the session, before any output exists.</summary>
    private static (ImageOutputCapabilities Capabilities, ImageEncoderSession Session, AllocationScope Scope) Prepare(ImageWriterOptions snapshot, PixelFormat pixelFormat, Stream? stream)
    {
        var encoder = snapshot.Encoder!;
        var capabilities = ImageOutputCapabilities.ForWriter(snapshot);
        if (capabilities.RequiresSeekableOutput && capabilities.Format == ImageFormat.Ani && stream is { CanSeek: false })
            throw new ArgumentException("ANI output requires a seekable stream: the frame and step counts and the sizes are written before the frames and patched once every frame is written. Write to a seekable stream or a path.", nameof(stream));

        if (capabilities.RequiresSeekableOutput && stream is { CanSeek: false })
            throw new ArgumentException($"{capabilities.Name} animation output requires a seekable stream: the file size is written before the frames and patched once every frame is written. Write to a seekable stream or a path, or write a still image (ImageWriterOptions.ExpectedFrameCount = 1, no animation settings).", nameof(stream));

        var metadata = MetadataWritePlan.Create(snapshot.Metadata, capabilities.Format, encoder.MetadataHandling, snapshot.CanvasSize, pixelFormat);
        var animation = capabilities.IsAnimated ? snapshot.Animation?.Clone() ?? new AnimationMetadata() : null;
        var scope = AllocationScope.Create(snapshot.Configuration, "ImageWriter");
        var options = new ImageEncoderSessionOptions(encoder, capabilities, snapshot.CanvasSize, pixelFormat, snapshot.ExpectedFrameCount, metadata, animation, snapshot.Configuration, scope);
        var session = ImageEncoderRegistry.Current.Get(capabilities.Format).CreateSession(options);
        return (capabilities, session, scope);
    }

    private async ValueTask WritePosterFrameCoreAsync(ImageFrame frame, CancellationToken cancellationToken)
    {
        Enter(cancellationToken);
        try
        {
            ValidatePoster(frame);
            await RunAsync(isPoster: true, frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    private async ValueTask WriteFrameCoreAsync(ImageFrame frame, CancellationToken cancellationToken)
    {
        Enter(cancellationToken);
        try
        {
            ValidateFrame(frame);
            await RunAsync(isPoster: false, frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    private void Enter(CancellationToken cancellationToken)
    {
        EnterCore();
        try
        {
            if (_state == WriterState.Completed)
                throw new InvalidOperationException("The writer is completed: no frame can be written after Complete.");

            EnsureNotFaulted();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            Exit();
            throw;
        }
    }

    /// <returns><see langword="true"/> when the writer is already completed (repeating a successful completion has no effect).</returns>
    private bool EnterComplete(CancellationToken cancellationToken)
    {
        EnterCore();
        try
        {
            if (_state == WriterState.Completed)
            {
                Exit();
                return true;
            }

            EnsureNotFaulted();
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
        catch
        {
            Exit();
            throw;
        }
    }

    private void EnterCore()
    {
        ObjectDisposedException.ThrowIf(_state == WriterState.Disposed, this);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("Another operation is in progress on this writer: a writer supports one operation at a time.");

        if (_state == WriterState.Disposed)
        {
            Exit();
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    private void Exit() => Volatile.Write(ref _busy, 0);

    private void EnsureNotFaulted()
    {
        if (_state == WriterState.Faulted)
            throw new InvalidOperationException("The writer is faulted: a previous operation failed and the output is aborted. Dispose it.");
    }

    private bool BeginDispose()
    {
        if (_state == WriterState.Disposed)
            return false;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("The writer cannot be disposed while an operation is in progress.");

        _state = WriterState.Disposed;
        Exit();
        return true;
    }

    private void ValidatePoster(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!Capabilities.SupportsPosterFrame)
            throw new InvalidOperationException($"{Capabilities.Name} output does not support poster frames: only animated PNG output stores a separate poster.");

        if (_posterWritten)
            throw new InvalidOperationException("The poster frame was already written.");

        if (FramesWritten > 0)
            throw new InvalidOperationException("The poster frame must be written before the first displayed frame.");

        ValidateFrameShape(frame);
        Preflight(_session!, frame, isPoster: true);
    }

    private void ValidateFrame(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (FramesWritten >= (Options.ExpectedFrameCount ?? Capabilities.MaxFrameCount ?? int.MaxValue))
        {
            throw new InvalidOperationException(Options.ExpectedFrameCount is { } expected
                ? string.Create(CultureInfo.InvariantCulture, $"The expected frame count ({expected}) was already written.")
                : $"{Capabilities.Name} output stores a single frame.");
        }

        ValidateFrameShape(frame);
        Preflight(_session!, frame, isPoster: false);
    }

    /// <summary>
    /// The preflight of one frame, shared by eager saves and sequential writes: the metadata policy of the per-frame
    /// settings (a cursor hotspot the output cannot store), then the codec's own checks.
    /// </summary>
    private void Preflight(ImageEncoderSession session, ImageFrame frame, bool isPoster)
    {
        MetadataWritePlan.ValidateFrameMetadata(frame.MetadataCore, Format, Options.Encoder!.MetadataHandling);
        session.ValidateFrame(frame, isPoster);
    }

    private void ValidateFrameShape(ImageFrame frame)
    {
        // Liveness first (ObjectDisposedException), then geometry and type: frames are never resized or converted implicitly
        var storage = frame.GetStorage();
        if (frame.PixelFormat != PixelFormat)
            throw new ArgumentException($"The frame has the pixel format {frame.PixelFormat}; the writer encodes {PixelFormat}.", nameof(frame));

        if (storage.Width != CanvasSize.Width || storage.Height != CanvasSize.Height)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The frame is {storage.Width}x{storage.Height} but the canvas is {CanvasSize.Width}x{CanvasSize.Height}."), nameof(frame));
    }

    private void ValidateCompletion()
    {
        if (FramesWritten == 0)
            throw new InvalidOperationException("No frame was written: an image has at least one displayed frame.");

        if (Options.ExpectedFrameCount is { } expected && FramesWritten != expected)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{FramesWritten} frame(s) were written but ImageWriterOptions.ExpectedFrameCount is {expected}."));
    }

    private ImageEncoderSession BeginOperation(CancellationToken cancellationToken)
    {
        var session = _session!;
        session.CancellationToken = cancellationToken;
        return session;
    }

    private void Run(bool isPoster, ImageFrame frame)
    {
        var session = BeginOperation(CancellationToken.None);
        try
        {
            Begin(session, isPoster, frame);
            Pump(session);
            OnWritten(isPoster);
        }
        catch
        {
            Fault();
            throw;
        }
        finally
        {
            session.CancellationToken = CancellationToken.None;
        }
    }

    private async ValueTask RunAsync(bool isPoster, ImageFrame frame, CancellationToken cancellationToken)
    {
        var session = BeginOperation(cancellationToken);
        try
        {
            Begin(session, isPoster, frame);
            await PumpAsync(session, cancellationToken).ConfigureAwait(false);
            OnWritten(isPoster);
        }
        catch
        {
            Fault();
            throw;
        }
        finally
        {
            session.CancellationToken = CancellationToken.None;
        }
    }

    private void Begin(ImageEncoderSession session, bool isPoster, ImageFrame frame)
    {
        if (isPoster)
        {
            session.BeginPosterFrame(frame);
        }
        else
        {
            session.BeginFrame(frame, FramesWritten);
        }
    }

    private void OnWritten(bool isPoster)
    {
        if (isPoster)
        {
            _posterWritten = true;
        }
        else
        {
            FramesWritten++;
        }
    }

    private void Pump(ImageEncoderSession session)
    {
        while (true)
        {
            session.CancellationToken.ThrowIfCancellationRequested();
            var done = session.Encode(_output);
            if (done || _output.ShouldFlush)
            {
                _output.FlushTo(_stream);
            }

            if (done)
                return;
        }
    }

    private async ValueTask PumpAsync(ImageEncoderSession session, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var done = session.Encode(_output);
            if (done || _output.ShouldFlush)
            {
                await _output.FlushToAsync(_stream, cancellationToken).ConfigureAwait(false);
            }

            if (done)
                return;
        }
    }

    /// <summary>Writes the session's patches over the already written output, then returns to the end of the output.</summary>
    private void ApplyPatches()
    {
        if (_output.Patches is not { Count: > 0 } patches)
            return;

        var end = _stream.Position;
        foreach (var (offset, data) in patches)
        {
            _stream.Position = _startPosition + offset;
            _stream.Write(data);
        }

        _stream.Position = end;
    }

    private async ValueTask ApplyPatchesAsync(CancellationToken cancellationToken)
    {
        if (_output.Patches is not { Count: > 0 } patches)
            return;

        var end = _stream.Position;
        foreach (var (offset, data) in patches)
        {
            _stream.Position = _startPosition + offset;
            await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        _stream.Position = end;
    }

    private void OnCompleted()
    {
        _state = WriterState.Completed;
        ReleaseSession();
    }

    private void Fault()
    {
        _state = WriterState.Faulted;
        ReleaseSession();

        // A failed path output is never published: delete the temporary file now (the destination is untouched)
        _file?.Abort();
    }

    private void ReleaseSession()
    {
        _session?.Dispose();
        _session = null;
        _output.Dispose();
    }
}
