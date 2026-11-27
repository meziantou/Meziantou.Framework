using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.MsQuic.Interop;

namespace Meziantou.Framework.MsQuic;

/// <summary>A QUIC stream of a <see cref="MsQuicConnection"/>.</summary>
/// <remarks>
/// One read and one write can be pending at the same time. <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>
/// returns 0 once the peer completed its writes. Canceling a read or a write aborts that side of the stream with
/// <see cref="MsQuicConnectionOptions.DefaultStreamErrorCode"/>. Disposing the stream completes the writes and aborts the
/// reads that are not finished.
/// </remarks>
public sealed class MsQuicStream : Stream
{
    // The data received and not read yet. When it is full, MsQuic stops delivering data, which stops the flow control
    // credit of the peer, until the reader frees space.
    private const int MaxBufferedBytes = 256 * 1024;

    private readonly MsQuicApi _api;
    private GCHandle _context;
    private int _contextFreed;
    private readonly MsQuicConnection _connection;
    private readonly MsQuicSafeHandle _handle;
    private readonly bool _canRead;
    private readonly bool _canWrite;
    private readonly long _defaultErrorCode;

    private readonly Lock _receiveLock = new();
    private readonly Queue<byte[]> _receivedSegments = new();
    private int _receivedSegmentOffset;
    private int _bufferedBytes;
    private bool _receiveNeedsEnable;
    private bool _readFinished;
    private Exception? _readException;
    private TaskCompletionSource? _readWaiter;
    private int _reading;

    private TaskCompletionSource? _sendCompletion;
    private Exception? _writeException;
    private bool _writesCompleted;
    private int _writing;

    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _readsClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _writesClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdownComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private long _id = -1;
    private long _bytesRead;
    private long _bytesWritten;
    private int _disposed;

    // An outbound stream, started by StartAsync
    internal MsQuicStream(MsQuicApi api, MsQuicConnection connection, MsQuicSafeHandle connectionHandle, MsQuicStreamType type, long defaultErrorCode)
    {
        _api = api;
        _connection = connection;
        Type = type;
        _canRead = type is MsQuicStreamType.Bidirectional;
        _canWrite = true;
        _defaultErrorCode = defaultErrorCode;
        if (!_canRead)
        {
            _readsClosed.TrySetResult();
        }

        var context = GCHandle.Alloc(this);
        _context = context;
        unsafe
        {
            nint handle;
            var status = api.Table->StreamOpen(connectionHandle.DangerousGetHandle(), type is MsQuicStreamType.Unidirectional ? QuicFlags.StreamOpenUnidirectional : 0, &NativeCallback, (void*)GCHandle.ToIntPtr(context), &handle);
            if (MsQuicStatus.IsFailure(status))
            {
                context.Free();
                MsQuicApi.ThrowIfFailure(status, "StreamOpen");
            }

            _handle = new MsQuicSafeHandle(handle, api.Table->StreamClose, connectionHandle);
        }
    }

    // An inbound stream, from QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED
    internal MsQuicStream(MsQuicApi api, MsQuicConnection connection, MsQuicSafeHandle connectionHandle, nint handle, int flags, long defaultErrorCode)
    {
        _api = api;
        _connection = connection;
        Type = (flags & QuicFlags.StreamOpenUnidirectional) != 0 ? MsQuicStreamType.Unidirectional : MsQuicStreamType.Bidirectional;
        _canRead = true;
        _canWrite = Type is MsQuicStreamType.Bidirectional;
        _defaultErrorCode = defaultErrorCode;
        _started = true;
        _startCompletion.TrySetResult();
        if (!_canWrite)
        {
            _writesClosed.TrySetResult();
        }

        var context = GCHandle.Alloc(this);
        _context = context;
        unsafe
        {
            _handle = new MsQuicSafeHandle(handle, api.Table->StreamClose, connectionHandle);
            api.Table->SetCallbackHandler(handle, (delegate* unmanaged[Cdecl]<nint, void*, QuicStreamEvent*, int>)&NativeCallback, (void*)GCHandle.ToIntPtr(context));

            ulong id;
            var length = (uint)sizeof(ulong);
            if (!MsQuicStatus.IsFailure(api.Table->GetParam(handle, QuicParameters.StreamId, &length, &id)))
            {
                _id = (long)id;
            }
        }
    }

    /// <summary>Gets the identifier of the stream (RFC 9000, section 2.1).</summary>
    public long Id => _id;

    /// <summary>Gets the type of the stream.</summary>
    public MsQuicStreamType Type { get; }

    /// <summary>Gets the connection of the stream.</summary>
    public MsQuicConnection Connection => _connection;

    /// <summary>Gets a task that completes when the reading side is closed: the peer completed its writes and all the data was read, or the reading side was aborted (the task is then faulted).</summary>
    public Task ReadsClosed => _readsClosed.Task;

    /// <summary>Gets a task that completes when the writing side is closed: the peer acknowledged all the data, or the writing side was aborted (the task is then faulted).</summary>
    public Task WritesClosed => _writesClosed.Task;

    /// <summary>Gets the number of bytes read from the stream.</summary>
    public long BytesRead => Interlocked.Read(ref _bytesRead);

    /// <summary>Gets the number of bytes written to the stream and accepted by MsQuic.</summary>
    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    /// <summary>
    /// Gets or sets the priority of the stream, from 0 (lowest) to 65535 (highest). The default is 32767. When several
    /// streams have data to send, MsQuic sends the data of the stream with the highest priority first.
    /// </summary>
    public int Priority
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            unsafe
            {
                ushort priority;
                var length = (uint)sizeof(ushort);
                MsQuicApi.ThrowIfFailure(_api.Table->GetParam(_handle.DangerousGetHandle(), QuicParameters.StreamPriority, &length, &priority), "GetParam(STREAM_PRIORITY)");
                return priority;
            }
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var priority = (ushort)value;
            unsafe
            {
                MsQuicApi.ThrowIfFailure(_api.Table->SetParam(_handle.DangerousGetHandle(), QuicParameters.StreamPriority, sizeof(ushort), &priority), "SetParam(STREAM_PRIORITY)");
            }
        }
    }

    /// <inheritdoc />
    public override bool CanRead => _canRead && _disposed == 0;

    /// <inheritdoc />
    public override bool CanWrite => _canWrite && _disposed == 0;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanTimeout => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    internal async ValueTask StartAsync(bool waitUntilAvailable, CancellationToken cancellationToken)
    {
        var flags = QuicFlags.StreamStartShutdownOnFail | QuicFlags.StreamStartIndicatePeerAccept;
        if (!waitUntilAvailable)
        {
            flags |= QuicFlags.StreamStartFailBlocked;
        }

        int status;
        unsafe
        {
            status = _api.Table->StreamStart(_handle.DangerousGetHandle(), flags);
        }

        MsQuicApi.ThrowIfFailure(status, "StreamStart");

        _started = true;
        using (cancellationToken.UnsafeRegister(static (state, token) => ((MsQuicStream)state!)._startCompletion.TrySetCanceled(token), this))
        {
            await _startCompletion.Task.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!_canRead)
            throw new NotSupportedException("The stream cannot be read.");

        if (Interlocked.Exchange(ref _reading, 1) == 1)
            throw new InvalidOperationException("Another read is already pending.");

        return ReadCoreAsync(buffer, cancellationToken);
    }

    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                Task waitTask;
                var enableReceive = false;
                var read = 0;
                lock (_receiveLock)
                {
                    if (_bufferedBytes > 0)
                    {
                        read = CopyReceivedData(buffer.Span);
                        if (_receiveNeedsEnable)
                        {
                            _receiveNeedsEnable = false;
                            enableReceive = true;
                        }

                        if (_readFinished && _bufferedBytes == 0)
                        {
                            _readsClosed.TrySetResult();
                        }
                    }
                    else if (_readException is not null)
                    {
                        ExceptionDispatchInfo.Throw(_readException);
                    }
                    else if (_readFinished)
                    {
                        _readsClosed.TrySetResult();
                        return 0;
                    }
                    else if (buffer.IsEmpty)
                    {
                        return 0;
                    }
                }

                if (read > 0 || buffer.IsEmpty)
                {
                    if (enableReceive)
                    {
                        EnableReceive();
                    }

                    Interlocked.Add(ref _bytesRead, read);
                    return read;
                }

                lock (_receiveLock)
                {
                    if (_bufferedBytes > 0 || _readException is not null || _readFinished)
                        continue;

                    _readWaiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    waitTask = _readWaiter.Task;
                }

                try
                {
                    await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Abort(MsQuicAbortDirection.Read, _defaultErrorCode);
                    throw;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    // Called under _receiveLock
    private int CopyReceivedData(Span<byte> destination)
    {
        var total = 0;
        while (!destination.IsEmpty && _receivedSegments.TryPeek(out var segment))
        {
            var available = segment.AsSpan(_receivedSegmentOffset);
            var count = Math.Min(available.Length, destination.Length);
            available[..count].CopyTo(destination);
            destination = destination[count..];
            total += count;
            _receivedSegmentOffset += count;
            if (_receivedSegmentOffset == segment.Length)
            {
                _receivedSegments.Dequeue();
                _receivedSegmentOffset = 0;
            }
        }

        _bufferedBytes -= total;
        return total;
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => WriteAsync(buffer, completeWrites: false, cancellationToken);

    /// <summary>Writes data to the stream, and optionally completes the writes in the same packet.</summary>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, bool completeWrites, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!_canWrite)
            throw new NotSupportedException("The stream cannot be written.");

        if (Volatile.Read(ref _writeException) is { } exception)
            return ValueTask.FromException(exception);

        if (_writesCompleted)
            throw new InvalidOperationException("The writes of the stream are completed.");

        if (buffer.IsEmpty && !completeWrites)
            return ValueTask.CompletedTask;

        if (Interlocked.Exchange(ref _writing, 1) == 1)
            throw new InvalidOperationException("Another write is already pending.");

        return WriteCoreAsync(buffer, completeWrites, cancellationToken);
    }

    private async ValueTask WriteCoreAsync(ReadOnlyMemory<byte> buffer, bool completeWrites, CancellationToken cancellationToken)
    {
        nint quicBuffer = 0;
        try
        {
            using var pin = buffer.Pin();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _sendCompletion, completion);
            if (completeWrites)
            {
                _writesCompleted = true;
            }

            var status = Send(pin, buffer.Length, completeWrites, out quicBuffer);
            if (MsQuicStatus.IsFailure(status))
            {
                Volatile.Write(ref _sendCompletion, null);
                if (Volatile.Read(ref _writeException) is { } writeException)
                    ExceptionDispatchInfo.Throw(writeException);

                MsQuicApi.ThrowIfFailure(status, "StreamSend");
            }

            // MsQuic reads the buffer until SEND_COMPLETE, even when the send is canceled: always wait for it
            using (cancellationToken.UnsafeRegister(static state => ((MsQuicStream)state!).Abort(MsQuicAbortDirection.Write, ((MsQuicStream)state!)._defaultErrorCode), this))
            {
                try
                {
                    await completion.Task.ConfigureAwait(false);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            Interlocked.Add(ref _bytesWritten, buffer.Length);
        }
        finally
        {
            FreeNative(quicBuffer);
            Volatile.Write(ref _writing, 0);
        }
    }

    private static void FreeNative(nint pointer)
    {
        unsafe
        {
            NativeMemory.Free((void*)pointer);
        }
    }

    // MsQuic reads the QUIC_BUFFER array and the data it points to until SEND_COMPLETE
    private int Send(MemoryHandle pin, int length, bool fin, out nint quicBufferPointer)
    {
        unsafe
        {
            var quicBuffer = (QuicBuffer*)NativeMemory.Alloc((nuint)sizeof(QuicBuffer));
            quicBuffer->Length = (uint)length;
            quicBuffer->Buffer = (byte*)pin.Pointer;
            quicBufferPointer = (nint)quicBuffer;
            return _api.Table->StreamSend(_handle.DangerousGetHandle(), length == 0 ? null : quicBuffer, length == 0 ? 0u : 1u, fin ? QuicFlags.SendFin : 0, null);
        }
    }

    private void EnableReceive()
    {
        unsafe
        {
            _ = _api.Table->StreamReceiveSetEnabled(_handle.DangerousGetHandle(), 1);
        }
    }

    private void Shutdown(int flags, long errorCode)
    {
        unsafe
        {
            _ = _api.Table->StreamShutdown(_handle.DangerousGetHandle(), flags, (ulong)errorCode);
        }
    }

    /// <summary>Completes the writes: sends the end of the stream (FIN) once the pending data is sent.</summary>
    public void CompleteWrites()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!_canWrite || _writesCompleted || _writeException is not null)
            return;

        _writesCompleted = true;
        if (_started)
        {
            Shutdown(QuicFlags.StreamShutdownGraceful, 0);
        }
    }

    /// <summary>Aborts one or both sides of the stream, with an application error code sent to the peer.</summary>
    /// <param name="abortDirection">The sides to abort.</param>
    /// <param name="errorCode">The application error code, from 0 to 2^62-1.</param>
    public void Abort(MsQuicAbortDirection abortDirection, long errorCode)
    {
        MsQuicConnectionOptions.ValidateErrorCode(errorCode);
        if (_disposed != 0)
            return;

        var flags = 0;
        if (abortDirection.HasFlag(MsQuicAbortDirection.Read) && _canRead)
        {
            var exception = new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The reading side of the stream was aborted.");
            TaskCompletionSource? waiter;
            lock (_receiveLock)
            {
                if (_readFinished && _bufferedBytes == 0)
                {
                    waiter = null;
                }
                else
                {
                    flags |= QuicFlags.StreamShutdownAbortReceive;
                    _readException ??= exception;
                    _receivedSegments.Clear();
                    _bufferedBytes = 0;
                    waiter = _readWaiter;
                    _readWaiter = null;
                }
            }

            _readsClosed.TrySetException(exception);
            waiter?.TrySetResult();
        }

        if (abortDirection.HasFlag(MsQuicAbortDirection.Write) && _canWrite)
        {
            var exception = new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The writing side of the stream was aborted.");
            if (Interlocked.CompareExchange(ref _writeException, exception, null) is null)
            {
                flags |= QuicFlags.StreamShutdownAbortSend;
                _writesClosed.TrySetException(exception);
            }
        }

        if (flags != 0 && _started)
        {
            Shutdown(flags, errorCode);
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = ReadAsync(rented.AsMemory(0, buffer.Length)).AsTask().GetAwaiter().GetResult();
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => WriteAsync(buffer.ToArray()).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_started && !_shutdownComplete.Task.IsCompleted)
        {
            var flags = 0;
            if (_canWrite && !_writesCompleted && _writeException is null)
            {
                _writesCompleted = true;
                flags |= QuicFlags.StreamShutdownGraceful;
            }

            if (_canRead)
            {
                lock (_receiveLock)
                {
                    if (!_readFinished && _readException is null)
                    {
                        _readException = new ObjectDisposedException(GetType().FullName);
                        flags |= QuicFlags.StreamShutdownAbortReceive;
                    }
                }
            }

            if ((flags & QuicFlags.StreamShutdownGraceful) != 0)
            {
                Shutdown(QuicFlags.StreamShutdownGraceful, 0);
            }

            if ((flags & QuicFlags.StreamShutdownAbortReceive) != 0)
            {
                Shutdown(QuicFlags.StreamShutdownAbortReceive, _defaultErrorCode);
            }

            await _shutdownComplete.Task.ConfigureAwait(false);
        }

        _handle.Dispose();
        FreeContext();
        _readsClosed.TrySetException(new ObjectDisposedException(GetType().FullName));
        _writesClosed.TrySetException(new ObjectDisposedException(GetType().FullName));
        _ = _readsClosed.Task.Exception;
        _ = _writesClosed.Task.Exception;
        await base.DisposeAsync().ConfigureAwait(false);
    }

    // MsQuic calls no callback after the last event of the handle, nor after its close function returns
    private void FreeContext()
    {
        if (Interlocked.Exchange(ref _contextFreed, 1) == 0)
        {
            unsafe
            {
                _context.Free();
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int NativeCallback(nint handle, void* context, QuicStreamEvent* streamEvent)
    {
        unsafe
        {
            if (GCHandle.FromIntPtr((nint)context).Target is not MsQuicStream stream)
                return MsQuicStatus.Success;

            try
            {
                return stream.HandleEvent(streamEvent);
            }
            catch (Exception ex)
            {
                MsQuicConnection.ReportCallbackException(ex);
                return MsQuicStatus.InternalError;
            }
        }
    }

    private int HandleEvent(QuicStreamEvent* streamEvent)
    {
        unsafe
        {
            switch (streamEvent->Type)
            {
                case QuicStreamEvent.StartComplete:
                    if (MsQuicStatus.IsFailure(streamEvent->StartCompleteStatus))
                    {
                        var status = streamEvent->StartCompleteStatus;
                        var error = MsQuicStatus.ToError(status);
                        var exception = error is MsQuicError.OperationAborted && _connection.CloseException is { } closeException
                            ? closeException
                            : new MsQuicException(error, applicationErrorCode: null, transportErrorCode: null, error is MsQuicError.StreamLimitReached ? "The peer does not allow more streams of this type." : $"The stream could not be started (status 0x{status:X8}).") { NativeErrorCode = status };
                        _startCompletion.TrySetException(exception);
                    }
                    else
                    {
                        _id = (long)streamEvent->StartCompleteId;
                        if ((streamEvent->StartCompleteFlags & 0x1) != 0)
                        {
                            _startCompletion.TrySetResult();
                        }
                    }

                    break;

                case QuicStreamEvent.PeerAccepted:
                    _startCompletion.TrySetResult();
                    break;

                case QuicStreamEvent.Receive:
                    return HandleReceive(streamEvent);

                case QuicStreamEvent.SendComplete:
                    {
                        var completion = Interlocked.Exchange(ref _sendCompletion, null);
                        if (streamEvent->SendCompleteCanceled != 0)
                        {
                            completion?.TrySetException(Volatile.Read(ref _writeException) ?? _connection.CloseException ?? new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The write was canceled."));
                        }
                        else
                        {
                            completion?.TrySetResult();
                        }

                        break;
                    }

                case QuicStreamEvent.PeerSendShutdown:
                    {
                        TaskCompletionSource? waiter;
                        lock (_receiveLock)
                        {
                            _readFinished = true;
                            if (_bufferedBytes == 0 && _readException is null)
                            {
                                _readsClosed.TrySetResult();
                            }

                            waiter = _readWaiter;
                            _readWaiter = null;
                        }

                        waiter?.TrySetResult();
                        break;
                    }

                case QuicStreamEvent.PeerSendAborted:
                    {
                        var exception = new MsQuicException(MsQuicError.StreamAborted, (long)streamEvent->AbortedErrorCode, transportErrorCode: null, $"The peer aborted the stream with the error code {streamEvent->AbortedErrorCode}.");
                        TaskCompletionSource? waiter;
                        lock (_receiveLock)
                        {
                            _readException ??= exception;
                            _receivedSegments.Clear();
                            _bufferedBytes = 0;
                            waiter = _readWaiter;
                            _readWaiter = null;
                        }

                        _readsClosed.TrySetException(exception);
                        waiter?.TrySetResult();
                        break;
                    }

                case QuicStreamEvent.PeerReceiveAborted:
                    {
                        // STOP_SENDING: the writes fail, and the writing side is reset with the same code (RFC 9000, section 3.5)
                        var exception = new MsQuicException(MsQuicError.StreamAborted, (long)streamEvent->AbortedErrorCode, transportErrorCode: null, $"The peer stopped reading the stream with the error code {streamEvent->AbortedErrorCode}.");
                        if (Interlocked.CompareExchange(ref _writeException, exception, null) is null)
                        {
                            _writesClosed.TrySetException(exception);
                            Shutdown(QuicFlags.StreamShutdownAbortSend, (long)streamEvent->AbortedErrorCode);
                        }

                        break;
                    }

                case QuicStreamEvent.SendShutdownComplete:
                    if (streamEvent->SendShutdownCompleteGraceful != 0)
                    {
                        _writesClosed.TrySetResult();
                    }
                    else
                    {
                        _writesClosed.TrySetException(Volatile.Read(ref _writeException) ?? _connection.CloseException ?? new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The writing side of the stream was aborted."));
                    }

                    break;

                case QuicStreamEvent.ShutdownComplete:
                    {
                        Exception exception = (streamEvent->ShutdownCompleteConnectionShutdown != 0 ? _connection.CloseException : null)
                            ?? new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The stream was closed.");
                        _startCompletion.TrySetException(exception);
                        TaskCompletionSource? waiter;
                        lock (_receiveLock)
                        {
                            if (!_readFinished)
                            {
                                _readException ??= exception;
                            }

                            waiter = _readWaiter;
                            _readWaiter = null;
                        }

                        if (_readException is not null)
                        {
                            _readsClosed.TrySetException(_readException);
                        }

                        waiter?.TrySetResult();
                        Interlocked.CompareExchange(ref _writeException, exception, null);
                        Interlocked.Exchange(ref _sendCompletion, null)?.TrySetException(exception);
                        _writesClosed.TrySetException(exception);
                        _ = _readsClosed.Task.Exception;
                        _ = _writesClosed.Task.Exception;
                        _ = _startCompletion.Task.Exception;
                        _shutdownComplete.TrySetResult();
                    FreeContext();
                        break;
                    }
            }

            return MsQuicStatus.Success;
        }
    }

    private int HandleReceive(QuicStreamEvent* streamEvent)
    {
        unsafe
        {
            var total = streamEvent->ReceiveTotalBufferLength;
            TaskCompletionSource? waiter;
            lock (_receiveLock)
            {
                if (_readException is not null)
                {
                    // The reads were aborted: drop the data
                    return MsQuicStatus.Success;
                }

                var available = (ulong)(MaxBufferedBytes - _bufferedBytes);
                var toCopy = (int)Math.Min(total, available);
                if (toCopy > 0)
                {
                    var segment = new byte[toCopy];
                    var destination = segment.AsSpan();
                    for (var i = 0; i < streamEvent->ReceiveBufferCount && !destination.IsEmpty; i++)
                    {
                        var buffer = streamEvent->ReceiveBuffers[i];
                        var count = (int)Math.Min(buffer.Length, (uint)destination.Length);
                        new ReadOnlySpan<byte>(buffer.Buffer, count).CopyTo(destination);
                        destination = destination[count..];
                    }

                    _receivedSegments.Enqueue(segment);
                    _bufferedBytes += toCopy;
                }

                if ((ulong)toCopy < total)
                {
                    // Partial consumption pauses the receive callbacks until StreamReceiveSetEnabled
                    streamEvent->ReceiveTotalBufferLength = (ulong)toCopy;
                    _receiveNeedsEnable = true;
                }

                waiter = _readWaiter;
                _readWaiter = null;
            }

            waiter?.TrySetResult();
            return MsQuicStatus.Success;
        }
    }
}
