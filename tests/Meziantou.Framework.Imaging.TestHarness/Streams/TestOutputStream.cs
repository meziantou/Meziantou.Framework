namespace Meziantou.Framework.Imaging.TestHarness.Streams;

/// <summary>
/// A configurable write-only stream for writer and save tests: optional seeking (non-seekable by default: writers must
/// never seek, except to patch earlier bytes when <see cref="AllowPatching"/> is set), forbidden synchronous or asynchronous writes, injected I/O failures, cancellation after a number of bytes,
/// a hook awaited before asynchronous writes, and diagnostics (bytes, calls, flushes, disposal).
/// </summary>
public sealed class TestOutputStream : Stream
{
    private readonly MemoryStream _data = new();
    private bool _disposed;

    /// <summary>Gets or initializes a value indicating whether the stream reports <see cref="CanSeek"/>. Defaults to <see langword="false"/>.</summary>
    public bool Seekable { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether a seekable stream lets writers move back and rewrite earlier bytes (the
    /// seek-and-patch output of WebP animations, which declare their size before the frames). Defaults to <see langword="false"/>:
    /// any seek fails the test.
    /// </summary>
    public bool AllowPatching { get; init; }

    /// <summary>Gets or initializes a value indicating whether synchronous writes fail the test.</summary>
    public bool ForbidSynchronousWrites { get; init; }

    /// <summary>Gets or initializes a value indicating whether asynchronous writes fail the test.</summary>
    public bool ForbidAsynchronousWrites { get; init; }

    /// <summary>Gets or initializes the position at which writes throw <see cref="InjectedIOException"/> (bytes before it are written), or -1 for none.</summary>
    public long FailAtPosition { get; init; } = -1;

    /// <summary>Gets or initializes a source canceled once <see cref="CancelAtPosition"/> bytes were written.</summary>
    public CancellationTokenSource? CancellationSource { get; init; }

    /// <summary>Gets or initializes the position at which <see cref="CancellationSource"/> is canceled.</summary>
    public long CancelAtPosition { get; init; } = -1;

    /// <summary>Gets or initializes a callback awaited at the start of every asynchronous write.</summary>
    public Func<Task>? BeforeAsyncWrite { get; init; }

    /// <summary>Gets the number of bytes written.</summary>
    public long BytesWritten => _data.Length;

    /// <summary>Gets the number of synchronous write calls.</summary>
    public int SynchronousWriteCount { get; private set; }

    /// <summary>Gets the number of asynchronous write calls.</summary>
    public int AsynchronousWriteCount { get; private set; }

    /// <summary>Gets the number of flush calls (synchronous or asynchronous).</summary>
    public int FlushCount { get; private set; }

    /// <summary>Gets a value indicating whether the stream was disposed.</summary>
    public bool IsDisposed => _disposed;

    public override bool CanRead => false;

    public override bool CanSeek => Seekable && !_disposed;

    public override bool CanWrite => !_disposed;

    public override long Length => Seekable ? _data.Length : throw new NotSupportedException("The stream is not seekable.");

    public override long Position
    {
        get => Seekable ? _data.Position : throw new NotSupportedException("The stream is not seekable.");
        set
        {
            if (!Seekable || !AllowPatching)
                throw new NotSupportedException("Writers must never seek.");

            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _data.Length);
            _data.Position = value;
        }
    }

    /// <summary>Gets a copy of the bytes written.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray() => _data.ToArray();

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ForbidSynchronousWrites)
            throw new InvalidOperationException("A synchronous flush was issued by an asynchronous code path.");

        FlushCount++;
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ForbidAsynchronousWrites)
            throw new InvalidOperationException("An asynchronous flush was issued by a synchronous code path.");

        FlushCount++;
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _data.Position + offset,
            _ => _data.Length + offset,
        };
        return _data.Position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (ForbidSynchronousWrites)
            throw new InvalidOperationException("A synchronous write was issued by an asynchronous code path.");

        SynchronousWriteCount++;
        WriteCore(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (ForbidAsynchronousWrites)
            throw new InvalidOperationException("An asynchronous write was issued by a synchronous code path.");

        AsynchronousWriteCount++;
        if (BeforeAsyncWrite is not null)
        {
            await BeforeAsyncWrite().ConfigureAwait(false);
        }

        // Complete asynchronously, like real I/O
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        WriteCore(buffer.Span);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    private void WriteCore(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (FailAtPosition >= 0 && _data.Position + buffer.Length > FailAtPosition)
        {
            _data.Write(buffer[..(int)Math.Max(0, FailAtPosition - _data.Position)]);
            throw new InjectedIOException(_data.Position);
        }

        _data.Write(buffer);
        if (CancellationSource is not null && CancelAtPosition >= 0 && _data.Length >= CancelAtPosition)
        {
            CancellationSource.Cancel();
        }
    }
}
