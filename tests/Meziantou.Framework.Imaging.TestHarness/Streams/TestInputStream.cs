namespace Meziantou.Framework.Imaging.TestHarness.Streams;

/// <summary>
/// A configurable read-only stream over a byte array for input-layer tests: optional seeking, short reads, an initial
/// offset, injected I/O failures, forbidden synchronous or asynchronous reads, cancellation after a number of bytes, and
/// diagnostics (bytes read, calls, disposal).
/// </summary>
public sealed class TestInputStream : Stream
{
    private readonly byte[] _data;
    private long _position;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="TestInputStream"/> class.</summary>
    /// <param name="data">The bytes served by the stream (not copied).</param>
    /// <param name="initialPosition">The initial position (bytes before it are never read by the code under test).</param>
    public TestInputStream(byte[] data, int initialPosition = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(initialPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialPosition, data.Length);
        _data = data;
        _position = initialPosition;
        InitialPosition = initialPosition;
    }

    /// <summary>Gets or initializes a value indicating whether the stream reports <see cref="CanSeek"/> (and supports <see cref="Seek"/>, <see cref="Length"/>, <see cref="Position"/>).</summary>
    public bool Seekable { get; init; } = true;

    /// <summary>Gets or initializes the maximum number of bytes returned by one read (short reads), or 0 for no limit.</summary>
    public int MaxBytesPerRead { get; init; }

    /// <summary>Gets or initializes the position at which reads throw <see cref="InjectedIOException"/>, or -1 for none.</summary>
    public long FailAtPosition { get; init; } = -1;

    /// <summary>Gets or initializes a value indicating whether synchronous reads fail the test (asynchronous code paths must not block).</summary>
    public bool ForbidSynchronousReads { get; init; }

    /// <summary>Gets or initializes a value indicating whether asynchronous reads fail the test.</summary>
    public bool ForbidAsynchronousReads { get; init; }

    /// <summary>Gets or initializes a source canceled once <see cref="CancelAtPosition"/> bytes were read.</summary>
    public CancellationTokenSource? CancellationSource { get; init; }

    /// <summary>Gets or initializes the position at which <see cref="CancellationSource"/> is canceled.</summary>
    public long CancelAtPosition { get; init; } = -1;

    /// <summary>Gets or initializes a callback awaited at the start of every asynchronous read (tests hold a read to observe a pending operation).</summary>
    public Func<Task>? BeforeAsyncRead { get; init; }

    /// <summary>Gets the initial position.</summary>
    public int InitialPosition { get; }

    /// <summary>Gets the current read position (also available for non-seekable streams).</summary>
    public long ReadPosition => _position;

    /// <summary>Gets the number of bytes read by the code under test.</summary>
    public long BytesRead => _position - InitialPosition;

    /// <summary>Gets the number of synchronous read calls.</summary>
    public int SynchronousReadCount { get; private set; }

    /// <summary>Gets the number of asynchronous read calls.</summary>
    public int AsynchronousReadCount { get; private set; }

    /// <summary>Gets a value indicating whether the stream was disposed.</summary>
    public bool IsDisposed => _disposed;

    public override bool CanRead => !_disposed;

    public override bool CanSeek => Seekable && !_disposed;

    public override bool CanWrite => false;

    public override long Length => Seekable ? _data.Length : throw new NotSupportedException("The stream is not seekable.");

    public override long Position
    {
        get => Seekable ? _position : throw new NotSupportedException("The stream is not seekable.");
        set
        {
            if (!Seekable)
                throw new NotSupportedException("The stream is not seekable.");

            _position = value;
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (ForbidSynchronousReads)
            throw new InvalidOperationException("A synchronous read was issued by an asynchronous code path.");

        SynchronousReadCount++;
        return ReadCore(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (ForbidAsynchronousReads)
            throw new InvalidOperationException("An asynchronous read was issued by a synchronous code path.");

        AsynchronousReadCount++;
        if (BeforeAsyncRead is not null)
        {
            await BeforeAsyncRead().ConfigureAwait(false);
        }

        // Complete asynchronously, like real I/O
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return ReadCore(buffer.Span);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!Seekable)
            throw new NotSupportedException("The stream is not seekable.");

        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _data.Length + offset,
        };

        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    private int ReadCore(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (FailAtPosition >= 0 && _position >= FailAtPosition)
            throw new InjectedIOException(_position);

        var count = (int)Math.Min(buffer.Length, Math.Max(0, _data.Length - _position));
        if (MaxBytesPerRead > 0)
        {
            count = Math.Min(count, MaxBytesPerRead);
        }

        if (FailAtPosition >= 0)
        {
            count = (int)Math.Min(count, FailAtPosition - _position);
        }

        _data.AsSpan((int)_position, count).CopyTo(buffer);
        _position += count;
        if (CancellationSource is not null && CancelAtPosition >= 0 && _position >= CancelAtPosition)
        {
            CancellationSource.Cancel();
        }

        return count;
    }
}
