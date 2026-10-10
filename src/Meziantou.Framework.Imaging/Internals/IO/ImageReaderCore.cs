using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel-type-independent implementation of <see cref="ImageReader{TPixel}"/>: one input
/// (<see cref="ImageInputBuffer"/>), one <see cref="ImageCodecContext"/> whose allocation scope is the reader scope, and the
/// codec's eager decode parser driven one step at a time by <see cref="SequentialDecodeParser"/>.
/// </summary>
/// <remarks>
/// <para>State machine: <c>Ready</c> → (<c>Ended</c> at the clean end of input) / <c>Faulted</c> / <c>Disposed</c>.</para>
/// <list type="bullet">
/// <item><description>
/// One operation at a time: an overlapping call throws <see cref="InvalidOperationException"/> without affecting the call in
/// progress (best-effort detection, not synchronization).
/// </description></item>
/// <item><description>
/// Preflight failures (invalid destination, poster window closed, pre-canceled token, overlapping call) leave the reader
/// usable. Any failure once decoding started (malformed data, limits, I/O, cancellation, conversion policy) faults it:
/// decoder and compositor state are released immediately and later calls throw <see cref="InvalidOperationException"/>.
/// </description></item>
/// <item><description>
/// <see langword="null"/>/<see langword="false"/> means a clean end of input only: the container was fully validated, or the
/// frame limit was reached.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class ImageReaderCore : IDisposable, IAsyncDisposable
{
    private readonly ImageCodecContext _context;
    private readonly ImageInputBuffer _input;
    private readonly SequentialDecodeSession _session;
    private readonly int? _frameLimit;
    private readonly PixelConversionOptions _conversion;
    private SequentialDecodeParser? _parser;
    private ReaderState _state;
    private int _busy;
    private bool _posterWindowClosed;

    private ImageReaderCore(ImageCodecContext context, ImageInputBuffer input, SequentialDecodeSession session, ImageReaderOptions options, PixelFormat pixelFormat)
    {
        _context = context;
        _input = input;
        _session = session;
        _frameLimit = options.FrameLimit;
        _conversion = options.Conversion;
        PixelFormat = pixelFormat;
    }

    private enum ReaderState
    {
        Ready,
        Ended,
        Faulted,
        Disposed,
    }

    public PixelFormat PixelFormat { get; }

    public ImageInfo Info { get; private set; } = null!;

    public int FramesRead { get; private set; }

    /// <summary>Gets the reader scope: input buffer, decoder/compositor state and every image returned by the reader (tests only).</summary>
    internal AllocationScope Scope => _context.Scope;

    /// <summary>Gets the per-input limit accounting (tests only).</summary>
    internal InputResourceTracker Tracker => _context.Tracker;

    public static ImageReaderCore Open(Stream stream, bool ownsStream, ImageReaderOptions? options, PixelFormat pixelFormat)
    {
        var reader = Create(stream, ownsStream, options, pixelFormat, CancellationToken.None);
        try
        {
            var codec = ImageInputPump.Detect(ImageCodecRegistry.Current, reader._input, reader._context);
            reader.CreateParser(codec);
            reader.OnOpened(ImageInputPump.Run(reader._input, reader._parser!, reader._context));
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    public static async Task<ImageReaderCore> OpenAsync(Stream stream, bool ownsStream, ImageReaderOptions? options, PixelFormat pixelFormat, CancellationToken cancellationToken)
    {
        var reader = Create(stream, ownsStream, options, pixelFormat, cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codec = await ImageInputPump.DetectAsync(ImageCodecRegistry.Current, reader._input, reader._context).ConfigureAwait(false);
            reader.CreateParser(codec);
            reader.OnOpened(await ImageInputPump.RunAsync(reader._input, reader._parser!, reader._context).ConfigureAwait(false));
            reader._context.CancellationToken = CancellationToken.None;
            return reader;
        }
        catch
        {
            await reader.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Image? ReadPosterFrame()
    {
        Enter(CancellationToken.None);
        try
        {
            if (!BeginPosterRead())
                return null;

            return CompleteStep(SequentialRequest.Poster, Run(SequentialRequest.Poster, destination: null));
        }
        finally
        {
            Exit();
        }
    }

    public async ValueTask<Image?> ReadPosterFrameAsync(CancellationToken cancellationToken)
    {
        Enter(cancellationToken);
        try
        {
            if (!BeginPosterRead())
                return null;

            return CompleteStep(SequentialRequest.Poster, await RunAsync(SequentialRequest.Poster, destination: null).ConfigureAwait(false));
        }
        finally
        {
            Exit();
        }
    }

    public Image? ReadFrame()
    {
        Enter(CancellationToken.None);
        try
        {
            if (!BeginFrameRead())
                return null;

            return CompleteStep(SequentialRequest.Frame, Run(SequentialRequest.Frame, destination: null));
        }
        finally
        {
            Exit();
        }
    }

    public async ValueTask<Image?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        Enter(cancellationToken);
        try
        {
            if (!BeginFrameRead())
                return null;

            return CompleteStep(SequentialRequest.Frame, await RunAsync(SequentialRequest.Frame, destination: null).ConfigureAwait(false));
        }
        finally
        {
            Exit();
        }
    }

    public bool ReadFrameInto(Image destination)
    {
        Enter(CancellationToken.None);
        try
        {
            ValidateDestination(destination);
            if (!BeginFrameRead())
                return false;

            return CompleteStep(SequentialRequest.Frame, Run(SequentialRequest.Frame, destination)) is not null;
        }
        finally
        {
            Exit();
        }
    }

    public ValueTask<bool> ReadFrameIntoAsync(Image destination, CancellationToken cancellationToken)
    {
        // Argument errors are reported synchronously
        ValidateDestinationShape(destination);
        return ReadFrameIntoCoreAsync(destination, cancellationToken);
    }

    private async ValueTask<bool> ReadFrameIntoCoreAsync(Image destination, CancellationToken cancellationToken)
    {
        Enter(cancellationToken);
        try
        {
            ValidateDestination(destination);
            if (!BeginFrameRead())
                return false;

            return CompleteStep(SequentialRequest.Frame, await RunAsync(SequentialRequest.Frame, destination).ConfigureAwait(false)) is not null;
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

        _parser?.Dispose();
        _parser = null;
        _input.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (!BeginDispose())
            return;

        _parser?.Dispose();
        _parser = null;
        await _input.DisposeAsync().ConfigureAwait(false);
    }

    private static ImageReaderCore Create(Stream stream, bool ownsStream, ImageReaderOptions? options, PixelFormat pixelFormat, CancellationToken cancellationToken)
    {
        options ??= ImageReaderOptions.Default;
        var session = new SequentialDecodeSession();
        var context = new ImageCodecContext(options.Configuration, "ImageReader", session, cancellationToken);
        var input = new ImageInputBuffer(stream, ownsStream || !options.LeaveOpen, context);
        return new ImageReaderCore(context, input, session, options, pixelFormat);
    }

    private void CreateParser(ImageCodec codec)
    {
        // A random-access container (TIFF, ICO, CUR) has no frame sequence to stream: its entries are pages or alternative
        // representations located by file offsets, and they are never animation frames
        if (codec.RequiresRandomAccess)
            throw new UnsupportedImageFeatureException($"{ImageFormatNames.Get(codec.Format)} cannot be read by a sequential ImageReader: it is a random-access container whose entries are not animation frames. Use ImageCollection to enumerate them, or Image.Load for the first one.", codec.Format, "Sequential reading");

        var request = new ImageDecodeRequest(PixelFormat, _frameLimit, _conversion);
        _parser = new SequentialDecodeParser(codec.CreateDecodeParser(request, _context), _session);
    }

    private void OnOpened(SequentialDecodeEvent result)
    {
        if (result != SequentialDecodeEvent.Header || _session.HeaderInfo is null)
            throw new InvalidOperationException($"The {ImageFormatNames.Get(_parser!.Format)} decoder does not support sequential reading: it did not report its header.");

        Info = _session.HeaderInfo;
    }

    private void Enter(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_state == ReaderState.Disposed, this);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("Another operation is in progress on this reader: a reader supports one operation at a time.");

        try
        {
            ObjectDisposedException.ThrowIf(_state == ReaderState.Disposed, this);
            if (_state == ReaderState.Faulted)
                throw new InvalidOperationException("The reader is faulted: a previous operation failed. Dispose it.");

            // A pre-canceled token is a preflight failure: nothing was read, the reader stays usable
            cancellationToken.ThrowIfCancellationRequested();
            _context.CancellationToken = cancellationToken;
        }
        catch
        {
            Volatile.Write(ref _busy, 0);
            throw;
        }
    }

    private void Exit()
    {
        _context.CancellationToken = CancellationToken.None;
        Volatile.Write(ref _busy, 0);
    }

    private bool BeginDispose()
    {
        if (_state == ReaderState.Disposed)
            return false;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("The reader cannot be disposed while an operation is in progress.");

        _state = ReaderState.Disposed;
        Volatile.Write(ref _busy, 0);
        return true;
    }

    /// <returns><see langword="true"/> if a poster must be decoded; <see langword="false"/> if the input has none.</returns>
    private bool BeginPosterRead()
    {
        if (_posterWindowClosed)
            throw new InvalidOperationException(FramesRead > 0 ? "The poster frame must be read before the first displayed frame." : "The poster frame can only be read once.");

        _posterWindowClosed = true;

        // The header snapshot tells whether a separate poster precedes the first frame (unknown is treated as none)
        return Info.HasPosterFrame == true && _state == ReaderState.Ready;
    }

    /// <returns><see langword="true"/> if a frame must be decoded; <see langword="false"/> at the clean end of input or at the frame limit.</returns>
    private bool BeginFrameRead()
    {
        _posterWindowClosed = true;
        if (_state == ReaderState.Ready && FramesRead >= _frameLimit)
        {
            // Deliberate prefix selection: the rest of the data is never examined; the decoder state is released
            _state = ReaderState.Ended;
            _parser?.Dispose();
            _parser = null;
        }

        return _state == ReaderState.Ready;
    }

    private void ValidateDestination(Image destination)
    {
        ValidateDestinationShape(destination);
        destination.Owner.EnsureCanModify("read a frame into the image");
    }

    private void ValidateDestinationShape(Image destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.ThrowIfDisposed();
        if (destination.PixelFormat != PixelFormat)
            throw new ArgumentException($"The destination has the pixel format {destination.PixelFormat}; the reader produces {PixelFormat}.", nameof(destination));

        if (destination.Size != Info.Size)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination is {destination.Width}x{destination.Height} but the canvas is {Info.Width}x{Info.Height}."), nameof(destination));

        if (destination.Frames.Count != 1)
            throw new ArgumentException("The destination must have exactly one frame.", nameof(destination));

        if (destination.PosterFrame is not null)
            throw new ArgumentException("The destination must not have a poster frame.", nameof(destination));

        if (destination.Animation is not null)
            throw new ArgumentException("The destination must not have animation settings (it receives a single still frame).", nameof(destination));
    }

    private SequentialDecodeEvent Run(SequentialRequest request, Image? destination)
    {
        _session.BeginRequest(request, destination);
        try
        {
            return ImageInputPump.Run(_input, _parser!, _context);
        }
        catch
        {
            Fault();
            throw;
        }
        finally
        {
            _session.EndRequest();
        }
    }

    private async ValueTask<SequentialDecodeEvent> RunAsync(SequentialRequest request, Image? destination)
    {
        _session.BeginRequest(request, destination);
        try
        {
            return await ImageInputPump.RunAsync(_input, _parser!, _context).ConfigureAwait(false);
        }
        catch
        {
            Fault();
            throw;
        }
        finally
        {
            _session.EndRequest();
        }
    }

    /// <summary>Turns the result of a step into the result of a call.</summary>
    private Image? CompleteStep(SequentialRequest request, SequentialDecodeEvent result)
    {
        switch (result)
        {
            case SequentialDecodeEvent.Image:
            {
                // The sink binds images to the request: a skipped poster is discarded, a frame never answers a poster request
                var image = _session.TakeImage(out var isPoster);
                Debug.Assert(isPoster == (request == SequentialRequest.Poster));
                if (!isPoster)
                {
                    FramesRead++;
                }

                return image;
            }

            case SequentialDecodeEvent.End:
                if (request == SequentialRequest.Poster)
                {
                    Fault();
                    throw new InvalidOperationException("The header announced a separate poster frame, but the input ended without one.");
                }

                // Clean end of input: release the decoder state now; the input stays open until the reader is disposed
                _state = ReaderState.Ended;
                _parser?.Dispose();
                _parser = null;
                return null;

            default:
                Fault();
                throw new InvalidOperationException("The decoder reported its header twice.");
        }
    }

    private void Fault()
    {
        if (_state is ReaderState.Ready or ReaderState.Ended)
        {
            _state = ReaderState.Faulted;
        }

        // Release the decoder and compositor state (and any partially decoded image) immediately
        _parser?.Dispose();
        _parser = null;
    }
}
