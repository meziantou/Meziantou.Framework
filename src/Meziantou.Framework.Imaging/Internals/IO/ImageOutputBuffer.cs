using System.Buffers;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bounded write buffer between an <see cref="ImageEncoderSession"/> and the destination stream.
/// Sessions append encoded bytes (it is an <see cref="IBufferWriter{T}"/>); the writer flushes it between
/// <see cref="ImageEncoderSession.Encode"/> calls with <see cref="Stream.Write(ReadOnlySpan{byte})"/> or, for asynchronous calls,
/// <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> only. The destination is never sought.
/// </summary>
/// <remarks>
/// The buffer is rented from the writer's allocation scope (<see cref="AllocationKind.Temporary"/>) and grows only to the
/// largest amount a session appends between two flushes; it is released when the writer completes, faults or is disposed.
/// </remarks>
internal sealed class ImageOutputBuffer : IBufferWriter<byte>, IDisposable
{
    /// <summary>The number of buffered bytes from which the writer flushes before the end of an operation.</summary>
    public const int DefaultFlushThreshold = 64 * 1024;

    private const int MinimumCapacity = 4096;

    private readonly AllocationScope _scope;
    private PooledBuffer? _buffer;
    private int _length;
    private List<(long Offset, byte[] Data)>? _patches;

    public ImageOutputBuffer(AllocationScope scope, int flushThreshold = DefaultFlushThreshold)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(flushThreshold);
        _scope = scope;
        FlushThreshold = flushThreshold;
    }

    /// <summary>Gets the number of buffered bytes from which <see cref="ShouldFlush"/> is true.</summary>
    public int FlushThreshold { get; }

    /// <summary>Gets the number of buffered (not yet flushed) bytes.</summary>
    public int Length => _length;

    /// <summary>Gets the total number of bytes appended since creation (flushed or not).</summary>
    public long TotalBytes { get; private set; }

    /// <summary>Gets a value indicating whether the writer should flush now.</summary>
    public bool ShouldFlush => _length >= FlushThreshold;

    /// <summary>
    /// Gets the bytes to write over already written output once the operation is complete (sizes known only at the end, such
    /// as the WebP RIFF size), or <see langword="null"/>. Only outputs whose capabilities require a seekable destination add
    /// patches; the writer applies them after the final flush.
    /// </summary>
    public IReadOnlyList<(long Offset, byte[] Data)>? Patches => _patches;

    /// <summary>Records bytes to write at <paramref name="offset"/> (from the start of the output) when the output completes.</summary>
    /// <param name="offset">The offset from the first byte appended to this buffer; the bytes must already be appended.</param>
    /// <param name="data">The bytes (copied).</param>
    public void AddPatch(long offset, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset + data.Length > TotalBytes)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "A patch must cover bytes that were already written.");

        (_patches ??= []).Add((offset, data.ToArray()));
    }

    /// <summary>Gets the buffered bytes.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer is null ? ReadOnlyMemory<byte>.Empty : _buffer.Memory[.._length];

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_buffer is null || count > _buffer.Length - _length)
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");

        _length += count;
        TotalBytes += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer!.Memory[_length..];
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer!.Span[_length..];
    }

    /// <summary>Appends bytes.</summary>
    /// <param name="data">The bytes.</param>
    public void Write(ReadOnlySpan<byte> data)
    {
        data.CopyTo(GetSpan(data.Length));
        Advance(data.Length);
    }

    /// <summary>Writes the buffered bytes to a stream and empties the buffer.</summary>
    public void FlushTo(Stream stream)
    {
        if (_length == 0)
            return;

        stream.Write(_buffer!.Span[.._length]);
        _length = 0;
    }

    /// <summary>Asynchronously writes the buffered bytes to a stream and empties the buffer.</summary>
    public async ValueTask FlushToAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (_length == 0)
            return;

        await stream.WriteAsync(_buffer!.Memory[.._length], cancellationToken).ConfigureAwait(false);
        _length = 0;
    }

    /// <summary>Releases the buffer (buffered bytes are discarded).</summary>
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _length = 0;
    }

    private void Ensure(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        var required = Math.Max(sizeHint, 1);
        if (_buffer is not null && _buffer.Length - _length >= required)
            return;

        var minimum = (long)_length + required;
        if (minimum > Array.MaxLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, Array.MaxLength, minimum);

        var capacity = (int)Math.Min(Array.MaxLength, Math.Max(minimum, Math.Max(MinimumCapacity, (long)(_buffer?.Length ?? 0) * 2)));
        var larger = _scope.Rent(capacity, AllocationKind.Temporary, clear: false);
        if (_buffer is not null)
        {
            _buffer.Span[.._length].CopyTo(larger.Span);
            _buffer.Dispose();
        }

        _buffer = larger;
    }
}
