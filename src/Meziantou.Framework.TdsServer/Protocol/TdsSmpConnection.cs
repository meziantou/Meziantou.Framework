using System.Buffers.Binary;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds.Protocol;

/// <summary>
/// Demultiplexes the Session Multiplexing Protocol ([MC-SMP]) that clients use for Multiple Active Result Sets (MARS):
/// after the login, every TDS packet travels in an SMP DATA packet that names its session, and each session is a TDS
/// connection of its own.
/// </summary>
[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Transport streams are owned and disposed by the caller.")]
internal sealed class TdsSmpConnection : IDisposable
{
    private const byte PreLoginMarsOption = 0x04;
    private const byte PreLoginTerminator = 0xFF;
    private const byte Smid = 0x53;
    private const int HeaderLength = 16;

    /// <summary>The largest SMP payload: one TDS packet, whose length is a 16-bit value.</summary>
    private const int MaxDataLength = ushort.MaxValue;

    /// <summary>The number of DATA packets a peer may send before it has to wait for an acknowledgment. SqlClient uses the same value.</summary>
    private const uint InitialWindow = 4;

    /// <summary>The window growth after which an ACK packet is sent, so the client does not wait for the next response to send more data.</summary>
    private const uint AckThreshold = 2;

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly ILogger _logger;
    private readonly Func<Stream, CancellationToken, Task> _sessionHandler;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _sessionsLock = new();
    private readonly Dictionary<ushort, Session> _sessions = [];

    public TdsSmpConnection(Stream input, Stream output, ILogger logger, Func<Stream, CancellationToken, Task> sessionHandler)
    {
        _input = input;
        _output = output;
        _logger = logger;
        _sessionHandler = sessionHandler;
    }

    [Flags]
    private enum SmpFlags : byte
    {
        Syn = 0x01,
        Ack = 0x02,
        Fin = 0x04,
        Data = 0x08,
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }

    /// <summary>Gets a value indicating whether the PRELOGIN request asks for MARS, which makes the client switch to SMP after the login.</summary>
    public static bool IsMarsRequested(ReadOnlySpan<byte> preLoginPayload)
    {
        var position = 0;
        while (position + 5 <= preLoginPayload.Length && preLoginPayload[position] != PreLoginTerminator)
        {
            var option = preLoginPayload[position];
            var offset = BinaryPrimitives.ReadUInt16BigEndian(preLoginPayload.Slice(position + 1, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(preLoginPayload.Slice(position + 3, 2));
            if (option == PreLoginMarsOption)
            {
                return length >= 1 && offset < preLoginPayload.Length && preLoginPayload[offset] != 0;
            }

            position += 5;
        }

        return false;
    }

    /// <summary>Reads SMP packets until the client closes the connection, and runs the session handler for each session.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var sessionsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionTasks = new List<Task>();
        var header = new byte[HeaderLength];
        try
        {
            while (await TryReadExactlyAsync(_input, header, cancellationToken).ConfigureAwait(false))
            {
                if (header[0] != Smid)
                {
                    throw new InvalidDataException("Invalid SMP packet header.");
                }

                var flags = (SmpFlags)header[1];
                var sessionId = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2, 2));
                var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
                var window = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
                if (length < HeaderLength || length - HeaderLength > MaxDataLength || (flags != SmpFlags.Data && length != HeaderLength))
                {
                    throw new InvalidDataException("Invalid SMP packet length.");
                }

                switch (flags)
                {
                    case SmpFlags.Syn:
                        var newSession = new Session(this, sessionId, window);
                        lock (_sessionsLock)
                        {
                            if (!_sessions.TryAdd(sessionId, newSession))
                            {
                                throw new InvalidDataException($"The SMP session {sessionId} is already open.");
                            }
                        }

                        sessionTasks.Add(RunSessionAsync(newSession, sessionsCancellation.Token));
                        break;

                    case SmpFlags.Data:
                        var data = new byte[length - HeaderLength];
                        await _input.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);

                        // Data for a session this side already closed was in flight when it closed: drop it
                        GetSession(sessionId)?.OnDataReceived(data, window);
                        break;

                    case SmpFlags.Ack:
                        GetSession(sessionId)?.OnWindowUpdated(window);
                        break;

                    case SmpFlags.Fin:
                        if (RemoveSession(sessionId) is { } closedSession)
                        {
                            closedSession.OnFinReceived();

                            // SqlClient forgets a session when it receives its FIN, which it waits for
                            await SendFinAsync(closedSession, cancellationToken).ConfigureAwait(false);
                        }

                        break;

                    default:
                        throw new InvalidDataException($"Unsupported SMP flags 0x{(byte)flags:X2}.");
                }
            }
        }
        finally
        {
            Session[] remainingSessions;
            lock (_sessionsLock)
            {
                remainingSessions = [.. _sessions.Values];
                _sessions.Clear();
            }

            foreach (var session in remainingSessions)
            {
                session.OnFinReceived();
            }

            await sessionsCancellation.CancelAsync().ConfigureAwait(false);
            foreach (var sessionTask in sessionTasks)
            {
                try
                {
                    await sessionTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MARS session closed with exception");
                }
            }

            sessionsCancellation.Dispose();
        }
    }

    private Session? GetSession(ushort sessionId)
    {
        lock (_sessionsLock)
        {
            return _sessions.GetValueOrDefault(sessionId);
        }
    }

    private Session? RemoveSession(ushort sessionId)
    {
        lock (_sessionsLock)
        {
            return _sessions.Remove(sessionId, out var session) ? session : null;
        }
    }

    private async Task RunSessionAsync(Session session, CancellationToken cancellationToken)
    {
        // Leave the demultiplexing loop before running the handler, which completes synchronously until its first read
        await Task.Yield();
        var stream = new SessionStream(session);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                await _sessionHandler(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MARS session {SessionId} closed with exception", session.Id);
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            // The handler stopped on its own, for instance on a malformed request: close the session on the client too
            _ = RemoveSession(session.Id);
            try
            {
                await SendFinAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }
    }

    private async ValueTask SendDataAsync(Session session, byte[] data, CancellationToken cancellationToken)
    {
        var sequenceNumber = await session.WaitForSendWindowAsync(cancellationToken).ConfigureAwait(false);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.IsFinSent)
            {
                throw new IOException($"The MARS session {session.Id} is closed.");
            }

            var packet = new byte[HeaderLength + data.Length];
            WriteHeader(packet, SmpFlags.Data, session.Id, (uint)packet.Length, sequenceNumber, session.GetReceiveWindow());
            data.CopyTo(packet.AsSpan(HeaderLength));
            await _output.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask SendAckAsync(Session session, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!session.IsFinSent)
            {
                await WriteControlPacketAsync(session, SmpFlags.Ack, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask SendFinAsync(Session session, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A packet sent after the FIN names a session the client no longer knows, and SqlClient then drops the connection
            if (!session.IsFinSent)
            {
                session.IsFinSent = true;
                await WriteControlPacketAsync(session, SmpFlags.Fin, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask WriteControlPacketAsync(Session session, SmpFlags flags, CancellationToken cancellationToken)
    {
        var packet = new byte[HeaderLength];
        WriteHeader(packet, flags, session.Id, HeaderLength, session.LastSequenceNumber, session.GetReceiveWindow());
        await _output.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteHeader(Span<byte> header, SmpFlags flags, ushort sessionId, uint length, uint sequenceNumber, uint window)
    {
        header[0] = Smid;
        header[1] = (byte)flags;
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], sequenceNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], window);
    }

    private static async ValueTask<bool> TryReadExactlyAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var read = await stream.ReadAsync(destination[totalRead..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead > 0 ? throw new InvalidDataException("Unexpected end of stream.") : false;
            }

            totalRead += read;
        }

        return true;
    }

    /// <summary>The state of one SMP session: its received data and its flow control.</summary>
    private sealed class Session
    {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private readonly Lock _lock = new();
        private uint _peerWindow;
        private uint _consumedPacketCount;
        private uint _advertisedWindow = InitialWindow;
        private TaskCompletionSource? _windowChanged;

        public Session(TdsSmpConnection connection, ushort id, uint peerWindow)
        {
            Connection = connection;
            Id = id;
            _peerWindow = peerWindow;
        }

        public TdsSmpConnection Connection { get; }

        public ushort Id { get; }

        public ChannelReader<byte[]> Incoming => _incoming.Reader;

        /// <summary>Gets the sequence number of the last DATA packet sent; the first one is 1.</summary>
        public uint LastSequenceNumber { get; private set; }

        /// <summary>Gets or sets a value indicating whether the FIN packet was sent. Accessed under the write lock of the connection.</summary>
        public bool IsFinSent { get; set; }

        public void OnDataReceived(byte[] data, uint peerWindow)
        {
            OnWindowUpdated(peerWindow);
            _ = _incoming.Writer.TryWrite(data);
        }

        public void OnFinReceived()
        {
            _ = _incoming.Writer.TryComplete();
        }

        public void OnWindowUpdated(uint peerWindow)
        {
            TaskCompletionSource? windowChanged;
            lock (_lock)
            {
                if (peerWindow == _peerWindow)
                    return;

                _peerWindow = peerWindow;
                windowChanged = _windowChanged;
                _windowChanged = null;
            }

            windowChanged?.TrySetResult();
        }

        /// <summary>Waits until the client accepts one more DATA packet, and returns its sequence number.</summary>
        public async ValueTask<uint> WaitForSendWindowAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task windowChanged;
                lock (_lock)
                {
                    if (LastSequenceNumber < _peerWindow)
                    {
                        return ++LastSequenceNumber;
                    }

                    _windowChanged ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    windowChanged = _windowChanged.Task;
                }

                await windowChanged.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Returns the highest sequence number the server accepts, which every packet sent to the client carries.</summary>
        public uint GetReceiveWindow()
        {
            lock (_lock)
            {
                _advertisedWindow = InitialWindow + _consumedPacketCount;
                return _advertisedWindow;
            }
        }

        /// <summary>Counts a DATA packet as read, and returns whether the window grew enough to send an ACK.</summary>
        public bool OnPacketConsumed()
        {
            lock (_lock)
            {
                _consumedPacketCount++;
                return InitialWindow + _consumedPacketCount - _advertisedWindow >= AckThreshold;
            }
        }
    }

    /// <summary>One SMP session, exposed as the stream of a TDS connection.</summary>
    /// <remarks>
    /// Reads return the payloads of the DATA packets of the session. Writes are buffered until the stream is flushed,
    /// which <see cref="TdsPacketWriter"/> does after each TDS packet, so each DATA packet carries one TDS packet.
    /// </remarks>
    private sealed class SessionStream : Stream
    {
        private readonly Session _session;
        private readonly MemoryStream _writeBuffer = new();
        private ReadOnlyMemory<byte> _currentData;

        public SessionStream(Session session)
        {
            _session = session;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;

            while (_currentData.IsEmpty)
            {
                if (!await _session.Incoming.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    return 0;

                if (_session.Incoming.TryRead(out var data))
                {
                    _currentData = data;

                    // The client stops sending once it reaches the window: without an ACK, a request that spans more
                    // packets than the window would wait forever, since the server only answers whole requests.
                    if (_session.OnPacketConsumed())
                    {
                        await _session.Connection.SendAckAsync(_session, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            var length = Math.Min(buffer.Length, _currentData.Length);
            _currentData[..length].CopyTo(buffer);
            _currentData = _currentData[length..];
            return length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writeBuffer.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _writeBuffer.Write(buffer, offset, count);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_writeBuffer.Length == 0)
                return;

            var data = _writeBuffer.ToArray();
            _writeBuffer.SetLength(0);
            await _session.Connection.SendDataAsync(_session, data, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush()
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writeBuffer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
