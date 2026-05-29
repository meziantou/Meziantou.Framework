using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// A <see cref="MemoryStream"/> whose storage is a chain of pooled byte arrays rented from a process-wide pool shared
/// by all instances. The stream also implements <see cref="IBufferWriter{T}"/> (explicit implementation), so it can be
/// used as a destination for APIs such as <c>Utf8JsonWriter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="MemoryStream"/>, growing the stream never reallocates and copies a single backing array: new
/// pooled blocks are appended to a chain. Blocks are only ever rented in the discrete sizes configured by
/// <see cref="PooledMemoryStreamOptions"/>, which keeps the shared pool buckets small and reusable.
/// </para>
/// <para>
/// Buffers (including the array returned by <see cref="GetBuffer"/> / <see cref="TryGetBuffer"/>, and the spans
/// returned by the <see cref="IBufferWriter{T}"/> members) are owned by the stream and are returned to the pool when
/// the stream is disposed. Accessing them after the stream is disposed is undefined behavior.
/// </para>
/// <para>This type is not thread-safe; the shared pool is.</para>
/// </remarks>
public sealed class PooledMemoryStream : MemoryStream, IBufferWriter<byte>
{
    private readonly PooledMemoryStreamOptions _options;
    private readonly List<Segment> _segments;
    private long _length;
    private long _position;
    private long _capacity;
    private bool _isOpen;

    // Cursor caching the last located segment so sequential access doesn't re-walk from segment 0 every call.
    // _cursorStart is the logical offset at which segment _cursorIndex begins (sum of Used of earlier segments).
    private int _cursorIndex;
    private long _cursorStart;

    /// <summary>Initializes a new instance using <see cref="PooledMemoryStreamOptions.Default"/>.</summary>
    public PooledMemoryStream()
        : this(PooledMemoryStreamOptions.Default, initialCapacity: 0)
    {
    }

    /// <summary>Initializes a new instance using <see cref="PooledMemoryStreamOptions.Default"/> and reserves at least <paramref name="initialCapacity"/> bytes.</summary>
    public PooledMemoryStream(int initialCapacity)
        : this(PooledMemoryStreamOptions.Default, initialCapacity)
    {
    }

    /// <summary>Initializes a new instance using the specified <paramref name="options"/>.</summary>
    public PooledMemoryStream(PooledMemoryStreamOptions options)
        : this(options, initialCapacity: 0)
    {
    }

    /// <summary>Initializes a new instance using the specified <paramref name="options"/> and reserves at least <paramref name="initialCapacity"/> bytes.</summary>
    public PooledMemoryStream(PooledMemoryStreamOptions options, int initialCapacity)
        : base(Array.Empty<byte>())
    {
        ArgumentNullException.ThrowIfNull(options);
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), initialCapacity, "The capacity must be greater than or equal to 0.");

        options.Freeze();
        _options = options;
        _segments = [];
        _isOpen = true;

        if (initialCapacity > 0)
            EnsureCapacityAtLeast(initialCapacity);
    }

    /// <inheritdoc />
    public override bool CanRead => _isOpen;

    /// <inheritdoc />
    public override bool CanSeek => _isOpen;

    /// <inheritdoc />
    public override bool CanWrite => _isOpen;

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            EnsureOpen();
            return _length;
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            EnsureOpen();
            return _position;
        }
        set
        {
            EnsureOpen();
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The position must be greater than or equal to 0.");

            _position = value;
        }
    }

    /// <inheritdoc />
    public override int Capacity
    {
        get
        {
            EnsureOpen();
            return (int)_capacity;
        }
        set
        {
            EnsureOpen();
            if (value < _length)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The capacity cannot be smaller than the current length.");

            if (value > _capacity)
            {
                EnsureCapacityAtLeast(value);
            }
            else
            {
                TrimTrailingEmptySegments(value);
                ResetCursor();
            }
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin loc)
    {
        EnsureOpen();
        var newPosition = loc switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentException("Invalid seek origin.", nameof(loc)),
        };

        if (newPosition < 0)
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");

        _position = newPosition;
        return newPosition;
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        EnsureOpen();
        if (value < 0 || value > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The length is out of range.");

        if (value > _length)
        {
            ExtendLengthWithZeros(value);
        }
        else if (value < _length)
        {
            Truncate(value);
        }

        // MemoryStream clamps the position to the new length when it would otherwise be past the end.
        if (_position > value)
            _position = value;
    }

    /// <inheritdoc />
    public override int ReadByte()
    {
        EnsureOpen();
        if (_position >= _length)
            return -1;

        Locate(_position, out var segmentIndex, out var offset);
        var value = _segments[segmentIndex].Array[offset];
        _position++;
        return value;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadCore(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => ReadCore(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<int>(cancellationToken);

        try
        {
            return Task.FromResult(Read(buffer, offset, count));
        }
        catch (Exception ex)
        {
            return Task.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);

        try
        {
            return new ValueTask<int>(ReadCore(buffer.Span));
        }
        catch (Exception ex)
        {
            return ValueTask.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        EnsureOpen();
        if (_position == _length)
        {
            if (_length + 1 > Array.MaxLength)
                throw new IOException("Stream was too long.");

            var index = EnsureAppendCapacity();
            ref var segment = ref CollectionsMarshal.AsSpan(_segments)[index];
            segment.Array[segment.Used] = value;
            segment.Used++;
            _length++;
            _position++;
        }
        else
        {
            Span<byte> single = [value];
            WriteCore(single);
        }
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        EnsureOpen();
        WriteCore(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureOpen();
        WriteCore(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        try
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        try
        {
            EnsureOpen();
            WriteCore(buffer.Span);
            return ValueTask.CompletedTask;
        }
        catch (Exception ex)
        {
            return ValueTask.FromException(ex);
        }
    }

    /// <inheritdoc />
    public override void CopyTo(Stream destination, int bufferSize)
    {
        ValidateCopyToArguments(destination, bufferSize);
        EnsureOpen();
        if (_position >= _length)
            return;

        Locate(_position, out var segmentIndex, out var offset);
        var remaining = _length - _position;
        while (remaining > 0)
        {
            var segment = _segments[segmentIndex];
            var chunk = (int)Math.Min(segment.Used - offset, remaining);
            destination.Write(segment.Array, offset, chunk);
            _position += chunk;
            remaining -= chunk;
            segmentIndex++;
            offset = 0;
        }
    }

    /// <inheritdoc />
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        ValidateCopyToArguments(destination, bufferSize);
        EnsureOpen();
        cancellationToken.ThrowIfCancellationRequested();
        if (_position >= _length)
            return;

        Locate(_position, out var segmentIndex, out var offset);
        var remaining = _length - _position;
        while (remaining > 0)
        {
            var segment = _segments[segmentIndex];
            var chunk = (int)Math.Min(segment.Used - offset, remaining);
            await destination.WriteAsync(segment.Array.AsMemory(offset, chunk), cancellationToken).ConfigureAwait(false);
            _position += chunk;
            remaining -= chunk;
            segmentIndex++;
            offset = 0;
        }
    }

    /// <summary>Writes the entire contents of this stream to another stream, regardless of the current position.</summary>
    public override void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        EnsureOpen();
        foreach (var segment in _segments)
        {
            if (segment.Used > 0)
                stream.Write(segment.Array, 0, segment.Used);
        }
    }

    /// <summary>Returns a new array containing the contents of the stream.</summary>
    public override byte[] ToArray()
    {
        EnsureOpen();
        var result = GC.AllocateUninitializedArray<byte>((int)_length);
        CopyAllTo(result);
        return result;
    }

    /// <summary>
    /// Consolidates the stream contents into a single contiguous array and returns it. The returned array may be
    /// larger than <see cref="Length"/>; only the first <see cref="Length"/> bytes are valid. The array is owned by
    /// the stream and is returned to the pool when the stream is disposed.
    /// </summary>
    public override byte[] GetBuffer()
    {
        EnsureOpen();
        return Consolidate();
    }

    /// <inheritdoc />
    public override bool TryGetBuffer(out ArraySegment<byte> buffer)
    {
        EnsureOpen();
        var array = Consolidate();
        buffer = new ArraySegment<byte>(array, 0, (int)_length);
        return true;
    }

    void IBufferWriter<byte>.Advance(int count)
    {
        EnsureOpen();
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "The count must be greater than or equal to 0.");

        if (count == 0)
            return;

        if (_segments.Count == 0 || count > _segments[^1].Array.Length - _segments[^1].Used)
            throw new InvalidOperationException("Cannot advance past the end of the reserved buffer.");

        if (_length + count > Array.MaxLength)
            throw new IOException("Stream was too long.");

        CollectionsMarshal.AsSpan(_segments)[_segments.Count - 1].Used += count;
        _length += count;
        _position = _length;
    }

    /// <summary>
    /// Returns a contiguous region of memory to write to. Data written through <see cref="IBufferWriter{T}"/> is
    /// always appended at the end of the stream (after <see cref="Length"/>); after <c>Advance</c>, the
    /// <see cref="Position"/> is moved to the new end.
    /// </summary>
    Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
    {
        var segment = _segments[ReserveTail(sizeHint)];
        return segment.Array.AsMemory(segment.Used);
    }

    /// <inheritdoc cref="IBufferWriter{T}.GetSpan" />
    Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint)
    {
        var segment = _segments[ReserveTail(sizeHint)];
        return segment.Array.AsSpan(segment.Used);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_isOpen)
        {
            _isOpen = false;
            ReturnSegmentsToPool();
            _segments.Clear();
            _length = 0;
            _position = 0;
            _capacity = 0;
            ResetCursor();
        }

        base.Dispose(disposing);
    }

    private int ReadCore(Span<byte> destination)
    {
        EnsureOpen();
        if (destination.IsEmpty || _position >= _length)
            return 0;

        var available = _length - _position;
        var toRead = (int)Math.Min(available, destination.Length);
        Locate(_position, out var segmentIndex, out var offset);

        var segments = CollectionsMarshal.AsSpan(_segments);
        var read = 0;
        while (read < toRead)
        {
            ref readonly var segment = ref segments[segmentIndex];
            var chunk = Math.Min(segment.Used - offset, toRead - read);
            segment.Array.AsSpan(offset, chunk).CopyTo(destination.Slice(read));
            read += chunk;
            offset += chunk;
            if (offset == segment.Used)
            {
                segmentIndex++;
                offset = 0;
            }
        }

        _position += read;
        return read;
    }

    private void WriteCore(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return;

        var endPosition = _position + source.Length;
        if (endPosition > Array.MaxLength)
            throw new IOException("Stream was too long.");

        if (_position > _length)
            ExtendLengthWithZeros(_position);

        var written = 0;

        // Phase 1: overwrite bytes that already exist in [position, length).
        if (_position < _length)
        {
            Locate(_position, out var segmentIndex, out var offset);
            while (written < source.Length && _position < _length)
            {
                var segment = _segments[segmentIndex];
                var toCopy = Math.Min(segment.Used - offset, source.Length - written);
                source.Slice(written, toCopy).CopyTo(segment.Array.AsSpan(offset));
                written += toCopy;
                _position += toCopy;
                offset += toCopy;
                if (offset == segment.Used)
                {
                    segmentIndex++;
                    offset = 0;
                }
            }
        }

        // Phase 2: append the remaining bytes at the end of the stream.
        while (written < source.Length)
        {
            // EnsureAppendCapacity may grow the list, so resolve the index before taking the span/ref.
            var index = EnsureAppendCapacity();
            ref var segment = ref CollectionsMarshal.AsSpan(_segments)[index];
            var destinationOffset = segment.Used;
            var room = segment.Array.Length - destinationOffset;
            var toCopy = Math.Min(room, source.Length - written);
            source.Slice(written, toCopy).CopyTo(segment.Array.AsSpan(destinationOffset));
            segment.Used += toCopy;
            _length += toCopy;
            _position += toCopy;
            written += toCopy;
        }
    }

    private void ExtendLengthWithZeros(long target)
    {
        while (_length < target)
        {
            var index = EnsureAppendCapacity();
            ref var segment = ref CollectionsMarshal.AsSpan(_segments)[index];
            var offset = segment.Used;
            var room = segment.Array.Length - offset;
            var toZero = (int)Math.Min(room, target - _length);
            Array.Clear(segment.Array, offset, toZero);
            segment.Used += toZero;
            _length += toZero;
        }
    }

    private int ReserveTail(int sizeHint)
    {
        EnsureOpen();
        if (sizeHint < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeHint), sizeHint, "The size hint must be greater than or equal to 0.");

        var needed = Math.Max(sizeHint, 1);

        if (_segments.Count > 0)
        {
            var last = _segments[^1];
            if (last.Array.Length - last.Used >= needed)
                return _segments.Count - 1;
        }

        var desired = Math.Max(needed, _options.GetBlockSize(_capacity));
        return AddSegment(_options.GetContiguousBlockSize(desired));
    }

    private int EnsureAppendCapacity()
    {
        if (_segments.Count > 0)
        {
            var last = _segments[^1];
            if (last.Used < last.Array.Length)
                return _segments.Count - 1;
        }

        return AddSegment(_options.GetBlockSize(_capacity));
    }

    private int AddSegment(int size)
    {
        var array = PooledBufferPool.Shared.Rent(size);
        _segments.Add(new Segment(array));
        _capacity += array.Length;
        return _segments.Count - 1;
    }

    private void EnsureCapacityAtLeast(long target)
    {
        while (_capacity < target)
        {
            var remaining = target - _capacity;
            var desired = Math.Max((int)Math.Min(remaining, Array.MaxLength), _options.GetBlockSize(_capacity));
            AddSegment(_options.GetContiguousBlockSize(desired));
        }
    }

    private void TrimTrailingEmptySegments(long targetCapacity)
    {
        while (_segments.Count > 0)
        {
            var last = _segments[^1];
            if (last.Used != 0 || _capacity - last.Array.Length < targetCapacity)
                break;

            _capacity -= last.Array.Length;
            _segments.RemoveAt(_segments.Count - 1);
            PooledBufferPool.Shared.Return(last.Array, _options.MaxRetainedBytesPerBucket, _options.ClearOnReturn);
        }
    }

    private void Truncate(long value)
    {
        ResetCursor();
        long cumulative = 0;
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (value <= cumulative + segment.Used)
            {
                var newUsed = (int)(value - cumulative);
                for (var j = _segments.Count - 1; j > i; j--)
                {
                    _capacity -= _segments[j].Array.Length;
                    PooledBufferPool.Shared.Return(_segments[j].Array, _options.MaxRetainedBytesPerBucket, _options.ClearOnReturn);
                    _segments.RemoveAt(j);
                }

                if (newUsed == 0)
                {
                    _capacity -= segment.Array.Length;
                    _segments.RemoveAt(i);
                    PooledBufferPool.Shared.Return(segment.Array, _options.MaxRetainedBytesPerBucket, _options.ClearOnReturn);
                }
                else
                {
                    CollectionsMarshal.AsSpan(_segments)[i].Used = newUsed;
                }

                _length = value;
                return;
            }

            cumulative += segment.Used;
        }
    }

    private byte[] Consolidate()
    {
        if (_length == 0)
            return Array.Empty<byte>();

        if (_segments.Count == 1)
            return _segments[0].Array;

        var size = _options.GetContiguousBlockSize((int)_length);
        var array = PooledBufferPool.Shared.Rent(size);
        CopyAllTo(array);

        ReturnSegmentsToPool();
        _segments.Clear();
        _capacity = 0;

        _segments.Add(new Segment(array) { Used = (int)_length });
        _capacity = array.Length;
        ResetCursor();
        return array;
    }

    private void CopyAllTo(Span<byte> destination)
    {
        var position = 0;
        foreach (ref readonly var segment in CollectionsMarshal.AsSpan(_segments))
        {
            if (segment.Used > 0)
            {
                segment.Array.AsSpan(0, segment.Used).CopyTo(destination.Slice(position));
                position += segment.Used;
            }
        }
    }

    private void ReturnSegmentsToPool()
    {
        foreach (var segment in _segments)
            PooledBufferPool.Shared.Return(segment.Array, _options.MaxRetainedBytesPerBucket, _options.ClearOnReturn);
    }

    private void Locate(long position, out int segmentIndex, out int offset)
    {
        // Resume from the cached cursor for forward/sequential access; fall back to walking from the start.
        int i;
        long cumulative;
        if (_cursorIndex < _segments.Count && position >= _cursorStart)
        {
            i = _cursorIndex;
            cumulative = _cursorStart;
        }
        else
        {
            i = 0;
            cumulative = 0;
        }

        var segments = CollectionsMarshal.AsSpan(_segments);
        for (; i < segments.Length; i++)
        {
            var used = segments[i].Used;
            if (position < cumulative + used)
            {
                segmentIndex = i;
                offset = (int)(position - cumulative);
                _cursorIndex = i;
                _cursorStart = cumulative;
                return;
            }

            cumulative += used;
        }

        segmentIndex = segments.Length;
        offset = 0;
    }

    private void ResetCursor()
    {
        _cursorIndex = 0;
        _cursorStart = 0;
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(!_isOpen, this);
    }

    // Mutable value type stored inline in the segment list, so no per-block wrapper object is allocated.
    // Mutate 'Used' in place through CollectionsMarshal.AsSpan(_segments); a plain indexer read returns a copy.
    private struct Segment(byte[] array)
    {
        public readonly byte[] Array = array;
        public int Used;
    }
}
