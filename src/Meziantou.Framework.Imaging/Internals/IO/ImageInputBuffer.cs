using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A bounded read buffer over a source stream. It reads from the stream's current position,
/// never seeks (non-seekable streams work the same way), tolerates short reads, and keeps the unconsumed bytes so that the
/// format detection prefix is replayed to the codec parser without rewinding the stream.
/// </summary>
/// <remarks>
/// <para>
/// The buffer is rented from the operation's <see cref="AllocationScope"/> (decoder state). It grows only up to the largest
/// contiguous request of a parser (bounded structures such as one metadata segment). No byte beyond
/// <see cref="ImageResourceLimits.MaxEncodedBytes"/> is buffered, so the parser examines exactly the same bytes as with an
/// in-memory span. Consumed bytes are charged to the per-input tracker. Bytes read ahead are consumed from the caller's
/// stream: eager APIs do not rewind.
/// </para>
/// <para>
/// A stream only reports its end by returning no byte, so telling an input that ends exactly at the limit from one that
/// exceeds it takes one read past the limit: <see cref="ProbeEndOfInput"/> reads a single byte, which is never buffered
/// and never handed to a parser. It is the only read past the limit, and the driver only issues it for a request the end
/// of the input satisfies (<see cref="ParseStatus.NeedMoreDataOrEnd"/>, the format detection prefix).
/// </para>
/// <para>
/// Synchronous fills use <see cref="Stream.Read(Span{byte})"/>; asynchronous fills use
/// <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> only. The stream is disposed only when it is owned (path overloads).
/// </para>
/// </remarks>
internal sealed class ImageInputBuffer : IDisposable, IAsyncDisposable
{
    /// <summary>The default capacity of the read buffer.</summary>
    public const int DefaultCapacity = 16 * 1024;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly ImageCodecContext _context;
    private PooledBuffer? _buffer;
    private int _start;
    private int _end;
    private bool _endOfInput;
    private bool _exceedsLimit;
    private byte[]? _probe;

    public ImageInputBuffer(Stream stream, bool ownsStream, ImageCodecContext context, int initialCapacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _stream = stream;
        _ownsStream = ownsStream;
        _context = context;
        InitialCapacity = initialCapacity;
    }

    /// <summary>Gets the capacity of the first buffer (tests use small values to exercise growth and compaction).</summary>
    public int InitialCapacity { get; }

    /// <summary>Gets the unconsumed buffered bytes. The memory is only valid until the next fill or consume.</summary>
    public ReadOnlyMemory<byte> Buffered => _buffer is null ? ReadOnlyMemory<byte>.Empty : _buffer.Memory[_start.._end];

    /// <summary>Gets the number of unconsumed buffered bytes.</summary>
    public int BufferedLength => _end - _start;

    /// <summary>Gets a value indicating whether the stream reported its end (no byte follows <see cref="Buffered"/>).</summary>
    public bool IsEndOfInput => _endOfInput;

    /// <summary>Gets the number of bytes consumed since the start of the input.</summary>
    public long Position { get; private set; }

    /// <summary>Gets the current buffer capacity (diagnostics).</summary>
    public int Capacity => _buffer?.Length ?? 0;

    /// <summary>Consumes bytes from the start of <see cref="Buffered"/>, charging them to the per-input encoded-byte limit.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <exception cref="ImageResourceLimitException">The encoded-byte limit is exceeded.</exception>
    public void Consume(int count)
    {
        Debug.Assert(count >= 0 && count <= BufferedLength);
        if (count == 0)
            return;

        _context.Tracker.ChargeEncodedBytes(count);
        _start += count;
        Position += count;
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }
    }

    /// <summary>
    /// Gets a value indicating whether reading stopped because every byte allowed by
    /// <see cref="ImageResourceLimits.MaxEncodedBytes"/> is buffered or consumed (the stream may have more).
    /// </summary>
    public bool IsReadLimitReached => !_endOfInput && Position + BufferedLength >= _context.Limits.MaxEncodedBytes;

    /// <summary>
    /// Gets a value indicating whether a probe found a byte past <see cref="ImageResourceLimits.MaxEncodedBytes"/>: the
    /// input is longer than the limit allows.
    /// </summary>
    public bool ExceedsLimit => _exceedsLimit;

    /// <summary>
    /// Reads until at least <paramref name="minimum"/> unconsumed bytes are buffered, the stream ends, or the encoded-byte
    /// limit is reached: this method never reads a byte beyond <see cref="ImageResourceLimits.MaxEncodedBytes"/>.
    /// </summary>
    /// <param name="minimum">The minimum number of contiguous bytes.</param>
    /// <exception cref="ImageResourceLimitException">The buffer would exceed the allocation limit.</exception>
    /// <exception cref="IOException">The stream failed (propagated unchanged).</exception>
    public void Fill(int minimum)
    {
        PrepareFill(minimum);
        while (BufferedLength < minimum && !_endOfInput)
        {
            var free = GetReadableSpace();
            if (free == 0)
                break;

            var read = _stream.Read(_buffer!.Span.Slice(_end, free));
            OnRead(read);
        }
    }

    /// <summary>Asynchronously reads until at least <paramref name="minimum"/> unconsumed bytes are buffered, the stream ends, or the encoded-byte limit is reached.</summary>
    /// <param name="minimum">The minimum number of contiguous bytes.</param>
    /// <param name="cancellationToken">The cancellation token, passed to the stream.</param>
    /// <returns>A task completing when the bytes are buffered.</returns>
    public async ValueTask FillAsync(int minimum, CancellationToken cancellationToken)
    {
        PrepareFill(minimum);
        while (BufferedLength < minimum && !_endOfInput)
        {
            var free = GetReadableSpace();
            if (free == 0)
                break;

            cancellationToken.ThrowIfCancellationRequested();
            var read = await _stream.ReadAsync(_buffer!.Memory.Slice(_end, free), cancellationToken).ConfigureAwait(false);
            OnRead(read);
        }
    }

    /// <summary>
    /// Tells an input that ends exactly at the encoded-byte limit from one that exceeds it, once every byte the limit allows
    /// is buffered or consumed (<see cref="IsReadLimitReached"/>): reads one byte, which is discarded. The outcome is
    /// <see cref="IsEndOfInput"/> or <see cref="ExceedsLimit"/>, and it is final: the stream is never probed twice.
    /// </summary>
    /// <exception cref="IOException">The stream failed (propagated unchanged).</exception>
    public void ProbeEndOfInput()
    {
        if (!CanProbe())
            return;

        Span<byte> probe = stackalloc byte[1];
        OnProbed(_stream.Read(probe));
    }

    /// <summary>Asynchronously tells an input that ends exactly at the encoded-byte limit from one that exceeds it.</summary>
    /// <param name="cancellationToken">The cancellation token, passed to the stream.</param>
    /// <returns>A task completing when <see cref="IsEndOfInput"/> or <see cref="ExceedsLimit"/> is known.</returns>
    public async ValueTask ProbeEndOfInputAsync(CancellationToken cancellationToken)
    {
        if (!CanProbe())
            return;

        cancellationToken.ThrowIfCancellationRequested();
        _probe ??= new byte[1];
        OnProbed(await _stream.ReadAsync(_probe, cancellationToken).ConfigureAwait(false));
    }

    public void Dispose()
    {
        ReleaseBuffer();
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseBuffer();
        if (_ownsStream)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void PrepareFill(int minimum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        if (_buffer is null)
        {
            _buffer = _context.Scope.Rent(Math.Max(minimum, InitialCapacity), AllocationKind.DecoderState, clear: false);
            return;
        }

        if (_buffer.Length - _start >= minimum && _end < _buffer.Length)
            return;

        if (_buffer.Length >= minimum)
        {
            // Compact: move the unconsumed bytes to the start of the buffer
            _buffer.Span[_start.._end].CopyTo(_buffer.Span);
        }
        else
        {
            var capacity = (int)Math.Min(Array.MaxLength, Math.Max(minimum, (long)_buffer.Length * 2));
            var larger = _context.Scope.Rent(capacity, AllocationKind.DecoderState, clear: false);
            _buffer.Span[_start.._end].CopyTo(larger.Span);
            _buffer.Dispose();
            _buffer = larger;
        }

        _end -= _start;
        _start = 0;
    }

    /// <summary>Gets the number of bytes that can be read into the buffer without exceeding the buffer or the encoded-byte limit.</summary>
    private int GetReadableSpace()
    {
        var budget = _context.Limits.MaxEncodedBytes - (Position + BufferedLength);
        return (int)Math.Clamp(budget, 0, _buffer!.Length - _end);
    }

    private bool CanProbe()
    {
        Debug.Assert(_endOfInput || IsReadLimitReached);
        return !_endOfInput && !_exceedsLimit;
    }

    private void OnProbed(int read)
    {
        if (read <= 0)
        {
            _endOfInput = true;
        }
        else
        {
            _exceedsLimit = true;
        }
    }

    private void OnRead(int read)
    {
        if (read <= 0)
        {
            _endOfInput = true;
            return;
        }

        _end += read;
    }

    private void ReleaseBuffer()
    {
        _buffer?.Dispose();
        _buffer = null;
        _start = 0;
        _end = 0;
    }
}
