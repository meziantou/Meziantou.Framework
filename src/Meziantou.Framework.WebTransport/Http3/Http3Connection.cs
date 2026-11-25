using Meziantou.Framework.MsQuic;

namespace Meziantou.Framework.WebTransport.Http3;

// The HTTP/3 layer of a QUIC connection, reduced to what WebTransport needs (RFC 9114, draft-ietf-webtrans-http3): the
// control streams and their SETTINGS, the QPACK streams (ignored: the dynamic table has capacity 0), the CONNECT
// requests, and the routing of the WebTransport streams and datagrams to their session.
internal sealed class Http3Connection : IAsyncDisposable
{
    public const int MaxFieldSectionSize = 64 * 1024;

    // The streams that reference a session this endpoint does not know yet (the CONNECT request is not processed yet)
    private const int MaxBufferedStreams = 32;

    private readonly TaskCompletionSource<Http3Settings> _peerSettings = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<long, WebTransportSession> _sessions = [];
    private readonly Dictionary<long, List<(MsQuicStream Stream, int HeaderLength)>> _bufferedStreams = [];
    private readonly Action<Http3Connection, MsQuicStream, Http3FrameReader, List<QPackField>>? _onRequest;
    private int _bufferedStreamCount;
    private int _hasPeerControlStream;
    private MsQuicStream? _controlStream;
    private Exception? _closeException;
    private int _disposed;

    public Http3Connection(MsQuicConnection connection, Action<Http3Connection, MsQuicStream, Http3FrameReader, List<QPackField>>? onRequest)
    {
        Quic = connection;
        _onRequest = onRequest;
    }

    public MsQuicConnection Quic { get; }

    public bool IsServer => _onRequest is not null;

    public Task<Http3Settings> PeerSettings => _peerSettings.Task;

    public Exception? CloseException => _closeException;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _controlStream = await Quic.OpenOutboundStreamAsync(MsQuicStreamType.Unidirectional, cancellationToken).ConfigureAwait(false);
        var settings = Http3Settings.EncodeLocalSettings(MaxFieldSectionSize);
        var buffer = new byte[1 + settings.Length];
        buffer[0] = (byte)Http3StreamType.Control;
        settings.CopyTo(buffer, 1);

        // The control stream stays open for the lifetime of the connection: closing it is a connection error
        await _controlStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);

        _ = AcceptStreamsAsync();
        _ = ReceiveDatagramsAsync();
    }

    public void RegisterSession(WebTransportSession session)
    {
        List<(MsQuicStream Stream, int HeaderLength)>? buffered;
        lock (_lock)
        {
            _sessions[session.SessionId] = session;
            if (_bufferedStreams.Remove(session.SessionId, out buffered))
            {
                _bufferedStreamCount -= buffered.Count;
            }
        }

        if (buffered is not null)
        {
            foreach (var (stream, headerLength) in buffered)
            {
                session.OnIncomingStream(stream, headerLength);
            }
        }
    }

    public void UnregisterSession(WebTransportSession session)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(session.SessionId, out var registered) && ReferenceEquals(registered, session))
            {
                _sessions.Remove(session.SessionId);
            }
        }
    }

    public bool HasSessions
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count > 0;
            }
        }
    }

    public static async Task WriteHeadersAsync(MsQuicStream stream, IEnumerable<QPackField> fields, bool completeWrites, CancellationToken cancellationToken)
    {
        var frame = Http3FrameReader.EncodeFrame(Http3FrameType.Headers, QPackEncoder.Encode(fields));
        await stream.WriteAsync(frame, completeWrites, cancellationToken).ConfigureAwait(false);
    }

    // Reads the frames of a request or response up to its HEADERS frame (RFC 9114, section 4.1)
    public static async Task<List<QPackField>> ReadHeadersAsync(Http3FrameReader reader, long? firstFrameType, CancellationToken cancellationToken)
    {
        while (true)
        {
            long type;
            long length;
            if (firstFrameType is { } knownType)
            {
                type = knownType;
                length = await reader.ReadVariableLengthIntegerAsync(cancellationToken).ConfigureAwait(false);
                firstFrameType = null;
            }
            else
            {
                var header = await reader.ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false);
                if (header is null)
                    throw new Http3ProtocolException(Http3ErrorCode.RequestIncomplete, "The stream ended before the HEADERS frame.");

                (type, length) = header.Value;
            }

            switch (type)
            {
                case Http3FrameType.Headers:
                    var payload = await reader.ReadPayloadAsync(length, MaxFieldSectionSize, cancellationToken).ConfigureAwait(false);
                    return QPackDecoder.Decode(payload, MaxFieldSectionSize);

                case Http3FrameType.Data:
                case Http3FrameType.Settings:
                case Http3FrameType.GoAway:
                case Http3FrameType.MaxPushId:
                case Http3FrameType.CancelPush:
                case Http3FrameType.PushPromise:
                    throw new Http3ProtocolException(Http3ErrorCode.FrameUnexpected, $"Unexpected frame 0x{type:X} before the HEADERS frame.");

                default:
                    if (Http3FrameType.IsReservedHttp2Type(type))
                        throw new Http3ProtocolException(Http3ErrorCode.FrameUnexpected, $"Unexpected frame 0x{type:X}.");

                    // Unknown frame types are ignored (RFC 9114, section 9)
                    await reader.SkipAsync(length, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    public async ValueTask CloseAsync(long errorCode)
    {
        try
        {
            await Quic.CloseAsync(errorCode).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CloseOnProtocolError(Http3ProtocolException exception)
    {
        _closeException ??= exception;
        _ = CloseAsync(exception.ErrorCode).AsTask();
    }

    private async Task AcceptStreamsAsync()
    {
        try
        {
            while (true)
            {
                var stream = await Quic.AcceptInboundStreamAsync(_disposeCancellation.Token).ConfigureAwait(false);
                _ = HandleInboundStreamAsync(stream);
            }
        }
        catch (Exception ex) when (ex is MsQuicException or OperationCanceledException or ObjectDisposedException)
        {
            OnConnectionClosed(ex);
        }
    }

    private void OnConnectionClosed(Exception exception)
    {
        _closeException ??= exception;
        _peerSettings.TrySetException(new WebTransportException(WebTransportError.ConnectionFailed, "The connection was closed before the server sent its SETTINGS.", exception));
        _ = _peerSettings.Task.Exception;

        WebTransportSession[] sessions;
        List<MsQuicStream> buffered = [];
        lock (_lock)
        {
            sessions = [.. _sessions.Values];
            foreach (var list in _bufferedStreams.Values)
            {
                buffered.AddRange(list.Select(item => item.Stream));
            }

            _bufferedStreams.Clear();
            _bufferedStreamCount = 0;
        }

        foreach (var session in sessions)
        {
            session.OnConnectionClosed(exception);
        }

        foreach (var stream in buffered)
        {
            _ = stream.DisposeAsync().AsTask();
        }
    }

    private async Task HandleInboundStreamAsync(MsQuicStream stream)
    {
        var cancellationToken = _disposeCancellation.Token;
        var keepStream = false;
        try
        {
            var first = await VariableLengthInteger.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (first is not { } type)
                return;

            var typeLength = VariableLengthInteger.GetEncodedLength(type);
            if (stream.Type is MsQuicStreamType.Unidirectional)
            {
                switch (type)
                {
                    case Http3StreamType.Control:
                        if (Interlocked.Exchange(ref _hasPeerControlStream, 1) == 1)
                            throw new Http3ProtocolException(Http3ErrorCode.StreamCreationError, "The peer opened a second control stream.");

                        keepStream = true;
                        await ReadControlStreamAsync(stream, cancellationToken).ConfigureAwait(false);
                        return;

                    case Http3StreamType.QPackEncoder:
                    case Http3StreamType.QPackDecoder:
                        // The dynamic table has capacity 0: the instructions of these streams are ignored, but the
                        // streams are critical, so they are read until the connection ends
                        keepStream = true;
                        await stream.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
                        return;

                    case Http3StreamType.WebTransport:
                        {
                            var sessionId = await ReadSessionIdAsync(stream, cancellationToken).ConfigureAwait(false);
                            keepStream = RouteStream(sessionId, stream, typeLength + VariableLengthInteger.GetEncodedLength(sessionId));
                            return;
                        }

                    case Http3StreamType.Push:
                        // This endpoint never sends MAX_PUSH_ID, so the server cannot push (RFC 9114, section 4.6)
                        throw new Http3ProtocolException(IsServer ? Http3ErrorCode.StreamCreationError : Http3ErrorCode.IdError, "Unexpected push stream.");

                    default:
                        // Unknown stream types are ignored (RFC 9114, section 6.2)
                        stream.Abort(MsQuicAbortDirection.Read, Http3ErrorCode.StreamCreationError);
                        return;
                }
            }

            if (type == Http3FrameType.WebTransportStream)
            {
                var sessionId = await ReadSessionIdAsync(stream, cancellationToken).ConfigureAwait(false);
                keepStream = RouteStream(sessionId, stream, typeLength + VariableLengthInteger.GetEncodedLength(sessionId));
                return;
            }

            if (!IsServer)
                throw new Http3ProtocolException(Http3ErrorCode.StreamCreationError, "The server opened a bidirectional stream that is not a WebTransport stream.");

            // A request stream: the first integer is the type of its first frame
            var reader = new Http3FrameReader(stream);
            List<QPackField> fields;
            try
            {
                fields = await ReadHeadersAsync(reader, type, cancellationToken).ConfigureAwait(false);
            }
            catch (Http3ProtocolException ex) when (ex.ErrorCode is Http3ErrorCode.RequestIncomplete or Http3ErrorCode.MessageError)
            {
                stream.Abort(MsQuicAbortDirection.Both, ex.ErrorCode);
                return;
            }

            keepStream = true;
            _onRequest!(this, stream, reader, fields);
        }
        catch (Http3ProtocolException ex)
        {
            CloseOnProtocolError(ex);
        }
        catch (Exception ex) when (ex is MsQuicException or OperationCanceledException or ObjectDisposedException or EndOfStreamException)
        {
            // The stream or the connection was closed
        }
        finally
        {
            if (!keepStream)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<long> ReadSessionIdAsync(MsQuicStream stream, CancellationToken cancellationToken)
    {
        var sessionId = await VariableLengthInteger.ReadAsync(stream, cancellationToken).ConfigureAwait(false)
            ?? throw new Http3ProtocolException(Http3ErrorCode.FrameError, "The WebTransport stream ends before its session identifier.");

        // The session identifier is the identifier of a client-initiated bidirectional stream (the CONNECT stream)
        if ((sessionId & 0x3) != 0)
            throw new Http3ProtocolException(Http3ErrorCode.IdError, "Invalid WebTransport session identifier.");

        return sessionId;
    }

    // Returns false when the stream was rejected (it is then disposed by the caller)
    private bool RouteStream(long sessionId, MsQuicStream stream, int headerLength)
    {
        WebTransportSession? session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
            {
                if (_bufferedStreamCount >= MaxBufferedStreams)
                {
                    stream.Abort(MsQuicAbortDirection.Both, Http3ErrorCode.WebTransportBufferedStreamRejected);
                    return false;
                }

                if (!_bufferedStreams.TryGetValue(sessionId, out var list))
                {
                    list = [];
                    _bufferedStreams.Add(sessionId, list);
                }

                list.Add((stream, headerLength));
                _bufferedStreamCount++;
                return true;
            }
        }

        session.OnIncomingStream(stream, headerLength);
        return true;
    }

    // Streams buffered for a session that will not exist: its CONNECT request was rejected
    public void RejectBufferedStreams(long sessionId)
    {
        List<(MsQuicStream Stream, int HeaderLength)>? buffered;
        lock (_lock)
        {
            if (_bufferedStreams.Remove(sessionId, out buffered))
            {
                _bufferedStreamCount -= buffered.Count;
            }
        }

        if (buffered is not null)
        {
            foreach (var (stream, _) in buffered)
            {
                stream.Abort(MsQuicAbortDirection.Both, Http3ErrorCode.WebTransportBufferedStreamRejected);
                _ = stream.DisposeAsync().AsTask();
            }
        }
    }

    private async Task ReadControlStreamAsync(MsQuicStream stream, CancellationToken cancellationToken)
    {
        var reader = new Http3FrameReader(stream);
        var first = true;
        while (true)
        {
            var header = await reader.ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (header is null)
                throw new Http3ProtocolException(Http3ErrorCode.ClosedCriticalStream, "The peer closed its control stream.");

            var (type, length) = header.Value;
            if (first)
            {
                if (type != Http3FrameType.Settings)
                    throw new Http3ProtocolException(Http3ErrorCode.MissingSettings, "The first frame of the control stream is not SETTINGS.");

                var payload = await reader.ReadPayloadAsync(length, MaxFieldSectionSize, cancellationToken).ConfigureAwait(false);
                _peerSettings.TrySetResult(Http3Settings.Parse(payload));
                first = false;
                continue;
            }

            switch (type)
            {
                case Http3FrameType.GoAway:
                    {
                        _ = await reader.ReadPayloadAsync(length, 8, cancellationToken).ConfigureAwait(false);
                        WebTransportSession[] sessions;
                        lock (_lock)
                        {
                            sessions = [.. _sessions.Values];
                        }

                        foreach (var session in sessions)
                        {
                            session.OnDrain();
                        }

                        break;
                    }

                case Http3FrameType.Data:
                case Http3FrameType.Headers:
                case Http3FrameType.PushPromise:
                case Http3FrameType.Settings:
                case Http3FrameType.WebTransportStream:
                    throw new Http3ProtocolException(Http3ErrorCode.FrameUnexpected, $"Unexpected frame 0x{type:X} on the control stream.");

                default:
                    if (Http3FrameType.IsReservedHttp2Type(type))
                        throw new Http3ProtocolException(Http3ErrorCode.FrameUnexpected, $"Unexpected frame 0x{type:X} on the control stream.");

                    await reader.SkipAsync(length, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task ReceiveDatagramsAsync()
    {
        try
        {
            while (true)
            {
                var datagram = await Quic.ReceiveDatagramAsync(_disposeCancellation.Token).ConfigureAwait(false);

                // An HTTP datagram starts with the quarter stream identifier of its request (RFC 9297, section 2.1)
                if (!VariableLengthInteger.TryRead(datagram, out var quarterStreamId, out var length) || quarterStreamId > VariableLengthInteger.MaxValue / 4)
                {
                    CloseOnProtocolError(new Http3ProtocolException(Http3ErrorCode.DatagramError, "Invalid HTTP datagram."));
                    return;
                }

                WebTransportSession? session;
                lock (_lock)
                {
                    _sessions.TryGetValue(quarterStreamId * 4, out session);
                }

                // A datagram of an unknown session is dropped
                session?.OnDatagram(datagram.AsMemory(length));
            }
        }
        catch (Exception ex) when (ex is MsQuicException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await _disposeCancellation.CancelAsync().ConfigureAwait(false);
        await Quic.DisposeAsync().ConfigureAwait(false);
        if (_controlStream is not null)
        {
            await _controlStream.DisposeAsync().ConfigureAwait(false);
        }

        OnConnectionClosed(new ObjectDisposedException(nameof(Http3Connection)));
        _disposeCancellation.Dispose();
    }
}
