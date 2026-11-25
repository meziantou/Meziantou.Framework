using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebTransport.Http3;

namespace Meziantou.Framework.WebTransport;

/// <summary>A WebTransport session over HTTP/3 (draft-ietf-webtrans-http3): bidirectional and unidirectional streams, and datagrams.</summary>
/// <remarks>
/// Connect to a server with <see cref="ConnectAsync"/>, or accept a session with
/// <see cref="WebTransportSessionRequest.AcceptAsync"/>. A client session has its own QUIC connection, closed with the
/// session. Dispose the session to close it.
/// </remarks>
public sealed class WebTransportSession : IAsyncDisposable
{
    private const int MaxCapsuleSize = 64 * 1024;
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);
    private static readonly SslApplicationProtocol Http3Protocol = SslApplicationProtocol.Http3;

    private readonly Http3Connection _connection;
    private readonly MsQuicStream _connectStream;
    private readonly Http3FrameReader _connectReader;
    private readonly bool _ownsConnection;
    private readonly Channel<WebTransportStream> _incomingStreams = Channel.CreateUnbounded<WebTransportStream>();
    private readonly Channel<ReadOnlyMemory<byte>> _datagrams;
    private readonly TaskCompletionSource<WebTransportCloseInfo> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _draining = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lock = new();
    private readonly HashSet<WebTransportStream> _streams = [];
    private readonly SemaphoreSlim _connectStreamWriteLock = new(1, 1);
    private WebTransportException? _closeException;
    private bool _isEstablished;
    private bool _isTerminated;
    private int _disposed;

    internal WebTransportSession(Http3Connection connection, MsQuicStream connectStream, Http3FrameReader connectReader, int datagramQueueCapacity, bool ownsConnection)
    {
        _connection = connection;
        _connectStream = connectStream;
        _connectReader = connectReader;
        _ownsConnection = ownsConnection;
        _datagrams = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(Math.Max(datagramQueueCapacity, 1)) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true });
        _ = _closed.Task.Exception;
    }

    /// <summary>Gets the identifier of the session: the identifier of its CONNECT stream.</summary>
    public long SessionId => _connectStream.Id;

    /// <summary>Gets the application protocol the server picked from the protocols the client offered (<c>wt-protocol</c>), if any.</summary>
    public string? Protocol { get; private set; }

    /// <summary>Gets the headers of the response to the CONNECT request, without the pseudo-headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; private set; } = [];

    /// <summary>Gets the address of the peer.</summary>
    public IPEndPoint? RemoteEndPoint => _connection.Quic.RemoteEndPoint;

    /// <summary>Gets the certificate of the server, for a client session.</summary>
    public X509Certificate2? RemoteCertificate => _connection.Quic.RemoteCertificate;

    /// <summary>
    /// Gets a task that completes when the session is closed cleanly, with the error code and the reason of the
    /// CLOSE_WEBTRANSPORT_SESSION capsule (0 and an empty reason when the CONNECT stream ends without one). It faults
    /// with a <see cref="WebTransportException"/> when the session ends abruptly.
    /// </summary>
    public Task<WebTransportCloseInfo> Closed => _closed.Task;

    /// <summary>Gets a task that completes when the peer asks to end the session gracefully (DRAIN_WEBTRANSPORT_SESSION capsule, or GOAWAY).</summary>
    public Task Draining => _draining.Task;

    /// <summary>Gets the maximum size of a datagram payload. It can grow during the session, and is 0 when datagrams cannot be sent.</summary>
    public int MaxDatagramSize
    {
        get
        {
            var max = _connection.Quic.MaxDatagramSendSize - VariableLengthInteger.GetEncodedLength(SessionId / 4);
            return Math.Max(0, max);
        }
    }

    internal WebTransportException? CloseException => _closeException;

    /// <summary>Opens a WebTransport session to a server.</summary>
    /// <param name="uri">The <c>https</c> URL of the session.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    /// <exception cref="WebTransportException">The connection failed, the server does not support WebTransport, or it rejected the session.</exception>
    /// <exception cref="PlatformNotSupportedException">MsQuic is not available.</exception>
    public static async ValueTask<WebTransportSession> ConnectAsync(Uri uri, WebTransportClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The URL must be an absolute https URL.", nameof(uri));

        if (!string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("The URL must not have a fragment.", nameof(uri));

        options ??= new WebTransportClientOptions();
        var protocolsHeader = options.Protocols.Count > 0 ? StructuredFieldStrings.SerializeList(options.Protocols) : null;
        var certificateHashes = options.ServerCertificateHashes.ToArray();

        // The host of the URL is already in ASCII (punycode) when the URL comes from a URL parser; IPv6 hosts lose their brackets
        var host = uri.Host.Trim('[', ']');

        var quicOptions = new MsQuicClientConnectionOptions
        {
            RemoteEndPoint = new DnsEndPoint(host, uri.Port),
            EnableDatagrams = true,
            MaxInboundBidirectionalStreams = options.MaxIncomingStreams,

            // The control stream and the QPACK streams of the server
            MaxInboundUnidirectionalStreams = Math.Min(ushort.MaxValue, options.MaxIncomingStreams + 3),
            IdleTimeout = options.IdleTimeout,
            CongestionControlAlgorithm = options.CongestionControlAlgorithm,
            ReceivedDatagramQueueCapacity = Math.Max(options.ReceivedDatagramQueueCapacity, 1),
            DefaultCloseErrorCode = Http3ErrorCode.NoError,
            DefaultStreamErrorCode = Http3ErrorCode.WebTransportSessionGone,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [Http3Protocol],
                TargetHost = host,
                RemoteCertificateValidationCallback = certificateHashes.Length > 0
                    ? (_, certificate, _, _) => IsCertificateHashAllowed(certificate, certificateHashes)
                    : options.RemoteCertificateValidationCallback,
            },
        };

        MsQuicConnection quic;
        try
        {
            quic = await MsQuicConnection.ConnectAsync(quicOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException ex)
        {
            throw new WebTransportException(WebTransportError.ConnectionFailed, "The QUIC connection failed: " + ex.Message, ex);
        }

        var connection = new Http3Connection(quic, onRequest: null);
        WebTransportSession? session = null;
        try
        {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);

            // The client waits for the SETTINGS of the server before sending a WebTransport request
            var settings = await connection.PeerSettings.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!settings.SupportsWebTransport)
                throw new WebTransportException(WebTransportError.NotSupported, "The server does not support WebTransport over HTTP/3.");

            var stream = await quic.OpenOutboundStreamAsync(MsQuicStreamType.Bidirectional, cancellationToken).ConfigureAwait(false);
            var reader = new Http3FrameReader(stream);
            session = new WebTransportSession(connection, stream, reader, options.ReceivedDatagramQueueCapacity, ownsConnection: true);
            connection.RegisterSession(session);

            var authority = uri.Authority;

            var fields = new List<QPackField>
            {
                new(":method", "CONNECT"),
                new(":protocol", "webtransport"),
                new(":scheme", "https"),
                new(":authority", authority),
                new(":path", string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery),
            };

            if (protocolsHeader is not null)
            {
                fields.Add(new("wt-available-protocols", protocolsHeader));
            }

            foreach (var header in options.Headers)
            {
                fields.Add(new(header.Key.ToLowerInvariant(), header.Value));
            }

            await Http3Connection.WriteHeadersAsync(stream, fields, completeWrites: false, cancellationToken).ConfigureAwait(false);

            List<QPackField> response;
            int status;
            while (true)
            {
                response = await Http3Connection.ReadHeadersAsync(reader, firstFrameType: null, cancellationToken).ConfigureAwait(false);
                var statusValue = response.FirstOrDefault(field => field.Name == ":status").Value;
                if (!int.TryParse(statusValue, NumberStyles.None, CultureInfo.InvariantCulture, out status) || status is < 100 or > 999)
                    throw new WebTransportException(WebTransportError.ProtocolError, "The response has no valid :status.");

                // Interim responses are skipped
                if (status is >= 200 or 101)
                    break;
            }

            if (status is < 200 or > 299)
                throw new WebTransportException(WebTransportError.SessionRejected, $"The server rejected the session with the status code {status}.") { StatusCode = status };

            var headers = response.Where(field => field.Name is not [':', ..]).Select(field => KeyValuePair.Create(field.Name, field.Value)).ToArray();
            var protocolValue = headers.FirstOrDefault(header => header.Key == "wt-protocol").Value;
            session.Protocol = protocolValue is null ? null : StructuredFieldStrings.ParseItem(protocolValue);
            session.ResponseHeaders = headers;
            session.Start();
            return session;
        }
        catch (Exception ex)
        {
            if (session is not null)
            {
                session.Terminate(closeInfo: null, new WebTransportException(WebTransportError.SessionClosed, "The session could not be established."));
            }

            await connection.CloseAsync(Http3ErrorCode.NoError).ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw ex switch
            {
                WebTransportException or OperationCanceledException => ex,
                Http3ProtocolException protocolException => new WebTransportException(WebTransportError.ProtocolError, protocolException.Message, protocolException),
                MsQuicException quicException => new WebTransportException(WebTransportError.ConnectionFailed, quicException.Message, quicException),
                _ => ex,
            };
        }
    }

    // The serverCertificateHashes rules of the WebTransport specification: the SHA-256 hash of the certificate is in the
    // list, the certificate is valid now, and its validity period is at most 14 days.
    private static bool IsCertificateHashAllowed(X509Certificate? certificate, byte[][] hashes)
    {
        if (certificate is not X509Certificate2 certificate2)
            return false;

        var now = DateTime.UtcNow;
        var notBefore = certificate2.NotBefore.ToUniversalTime();
        var notAfter = certificate2.NotAfter.ToUniversalTime();
        if (now < notBefore || now > notAfter || notAfter - notBefore > TimeSpan.FromDays(14))
            return false;

        var hash = SHA256.HashData(certificate2.RawDataMemory.Span);
        return hashes.Any(expected => expected.AsSpan().SequenceEqual(hash));
    }

    // Starts reading the capsules of the CONNECT stream, once the session is established
    internal void Start()
    {
        lock (_lock)
        {
            _isEstablished = true;
        }

        _ = ReadConnectStreamAsync();
    }

    /// <summary>Sends a datagram. Datagrams are not retransmitted: the peer may not receive it.</summary>
    /// <param name="datagram">The payload, at most <see cref="MaxDatagramSize"/> bytes.</param>
    /// <param name="cancellationToken">The token to stop waiting.</param>
    /// <returns>A task that completes when the datagram is sent on the network.</returns>
    public async Task SendDatagramAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        var quarterStreamId = SessionId / 4;
        var buffer = new byte[VariableLengthInteger.GetEncodedLength(quarterStreamId) + datagram.Length];
        var position = VariableLengthInteger.Write(buffer, quarterStreamId);
        datagram.Span.CopyTo(buffer.AsSpan(position));
        try
        {
            await _connection.Quic.SendDatagramAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException ex) when (ex.Error is MsQuicError.DatagramTooLarge or MsQuicError.DatagramsNotEnabled)
        {
            throw new WebTransportException(WebTransportError.DatagramNotSent, ex.Message, ex);
        }
        catch (MsQuicException ex)
        {
            throw _closeException ?? new WebTransportException(WebTransportError.ConnectionFailed, ex.Message, ex);
        }
    }

    /// <summary>Waits for the next datagram of the session. When the queue is full, the oldest datagrams are dropped.</summary>
    /// <exception cref="WebTransportException">The session is closed.</exception>
    public async ValueTask<ReadOnlyMemory<byte>> ReceiveDatagramAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _datagrams.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw GetClosedException();
        }
    }

    /// <summary>Opens a bidirectional stream.</summary>
    /// <param name="waitUntilAvailable"><see langword="true"/> to wait until the peer allows one more stream; <see langword="false"/> to fail when it does not allow one now.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    public ValueTask<WebTransportStream> OpenBidirectionalStreamAsync(bool waitUntilAvailable = true, CancellationToken cancellationToken = default)
        => OpenStreamAsync(MsQuicStreamType.Bidirectional, waitUntilAvailable, cancellationToken);

    /// <summary>Opens a unidirectional stream.</summary>
    /// <param name="waitUntilAvailable"><see langword="true"/> to wait until the peer allows one more stream; <see langword="false"/> to fail when it does not allow one now.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    public ValueTask<WebTransportStream> OpenUnidirectionalStreamAsync(bool waitUntilAvailable = true, CancellationToken cancellationToken = default)
        => OpenStreamAsync(MsQuicStreamType.Unidirectional, waitUntilAvailable, cancellationToken);

    private async ValueTask<WebTransportStream> OpenStreamAsync(MsQuicStreamType type, bool waitUntilAvailable, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        MsQuicStream stream;
        try
        {
            stream = await _connection.Quic.OpenOutboundStreamAsync(type, waitUntilAvailable, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException ex) when (ex.Error is MsQuicError.StreamLimitReached)
        {
            throw new WebTransportException(WebTransportError.OperationAborted, "The peer does not allow more streams now.", ex);
        }
        catch (MsQuicException ex)
        {
            throw _closeException ?? new WebTransportException(WebTransportError.ConnectionFailed, ex.Message, ex);
        }

        // The header that associates the stream with the session: the signal of a WebTransport bidirectional stream or
        // the type of a WebTransport unidirectional stream, then the session identifier
        var signal = type is MsQuicStreamType.Bidirectional ? Http3FrameType.WebTransportStream : Http3StreamType.WebTransport;
        var header = new byte[VariableLengthInteger.GetEncodedLength(signal) + VariableLengthInteger.GetEncodedLength(SessionId)];
        var position = VariableLengthInteger.Write(header, signal);
        VariableLengthInteger.Write(header.AsSpan(position), SessionId);

        var webTransportStream = new WebTransportStream(this, stream, header.Length, isOutbound: true);
        if (!TryAddStream(webTransportStream))
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw GetClosedException();
        }

        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException ex)
        {
            await webTransportStream.DisposeAsync().ConfigureAwait(false);
            throw webTransportStream.MapException(ex);
        }

        return webTransportStream;
    }

    /// <summary>Waits for the next stream the peer opens.</summary>
    /// <exception cref="WebTransportException">The session is closed.</exception>
    public async ValueTask<WebTransportStream> AcceptStreamAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _incomingStreams.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw GetClosedException();
        }
    }

    /// <summary>Asks the peer to end the session gracefully (DRAIN_WEBTRANSPORT_SESSION capsule). The session stays open.</summary>
    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        await WriteCapsuleAsync(Capsule.Encode(Capsule.DrainWebTransportSession, []), completeWrites: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes the session with an application error code and a reason (CLOSE_WEBTRANSPORT_SESSION capsule). Its streams are aborted.</summary>
    /// <param name="errorCode">The application error code.</param>
    /// <param name="reason">The reason, at most 1024 bytes in UTF-8.</param>
    /// <param name="cancellationToken">The token to stop waiting for the peer to receive the capsule.</param>
    public async ValueTask CloseAsync(uint errorCode = 0, string reason = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var capsule = Capsule.EncodeClose(errorCode, reason);
        if (!Terminate(new WebTransportCloseInfo(errorCode, reason), exception: null))
            return;

        try
        {
            await WriteCapsuleAsync(capsule, completeWrites: true, cancellationToken).ConfigureAwait(false);
            await _connectStream.WritesClosed.WaitAsync(CloseTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MsQuicException or TimeoutException or ObjectDisposedException or InvalidOperationException)
        {
        }

        await CloseOwnedConnectionAsync().ConfigureAwait(false);
    }

    /// <summary>Gets the statistics of the QUIC connection of the session.</summary>
    public MsQuicConnectionStatistics GetConnectionStatistics() => _connection.Quic.GetStatistics();

    /// <summary>Closes the session if it is open, with error code 0, and releases its resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        bool isOpen;
        lock (_lock)
        {
            isOpen = _isEstablished && !_isTerminated;
        }

        if (isOpen)
        {
            await CloseAsync().ConfigureAwait(false);
        }
        else
        {
            Terminate(closeInfo: null, new WebTransportException(WebTransportError.SessionClosed, "The session is disposed."));
        }

        await _connectStream.DisposeAsync().ConfigureAwait(false);
        if (_ownsConnection)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _connectStreamWriteLock.Dispose();
    }

    private async Task WriteCapsuleAsync(byte[] capsule, bool completeWrites, CancellationToken cancellationToken)
    {
        var frame = Http3FrameReader.EncodeFrame(Http3FrameType.Data, capsule);
        await _connectStreamWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _connectStream.WriteAsync(frame, completeWrites, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectStreamWriteLock.Release();
        }
    }

    private async Task CloseOwnedConnectionAsync()
    {
        if (_ownsConnection)
        {
            await _connection.CloseAsync(Http3ErrorCode.NoError).ConfigureAwait(false);
        }
    }

    // Reads the capsules of the CONNECT stream until it ends (RFC 9297, section 3.3)
    private async Task ReadConnectStreamAsync()
    {
        WebTransportCloseInfo? closeInfo = null;
        try
        {
            var capsuleBuffer = new List<byte>();
            var capsules = new List<Capsule>();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var header = await _connectReader.ReadFrameHeaderAsync(CancellationToken.None).ConfigureAwait(false);
                if (header is null)
                    break;

                var (type, length) = header.Value;
                if (type == Http3FrameType.Data)
                {
                    // The capsules are parsed after each chunk: a capsule larger than MaxCapsuleSize is rejected once its
                    // header is read, so the buffer stays bounded whatever the length of the DATA frame
                    var remaining = length;
                    while (remaining > 0)
                    {
                        var read = await _connectReader.ReadPayloadChunkAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, remaining)), CancellationToken.None).ConfigureAwait(false);
                        remaining -= read;
                        capsuleBuffer.AddRange(chunk.AsSpan(0, read));

                        var consumed = Capsule.ReadCapsules(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(capsuleBuffer), capsules, MaxCapsuleSize);
                        capsuleBuffer.RemoveRange(0, consumed);
                        foreach (var capsule in capsules)
                        {
                            switch (capsule.Type)
                            {
                                case Capsule.CloseWebTransportSession:
                                    var (errorCode, reason) = Capsule.DecodeClose(capsule.Value.Span);
                                    closeInfo ??= new WebTransportCloseInfo(errorCode, reason);
                                    break;

                                case Capsule.DrainWebTransportSession:
                                    OnDrain();
                                    break;

                                case Capsule.Datagram:
                                    OnDatagram(capsule.Value);
                                    break;
                            }
                        }

                        capsules.Clear();
                    }
                }
                else if (type == Http3FrameType.Headers)
                {
                    // Trailers
                    await _connectReader.SkipAsync(length, CancellationToken.None).ConfigureAwait(false);
                }
                else if (type is Http3FrameType.Settings or Http3FrameType.GoAway or Http3FrameType.MaxPushId or Http3FrameType.CancelPush or Http3FrameType.PushPromise || Http3FrameType.IsReservedHttp2Type(type))
                {
                    throw new Http3ProtocolException(Http3ErrorCode.FrameUnexpected, $"Unexpected frame 0x{type:X} on the CONNECT stream.");
                }
                else
                {
                    await _connectReader.SkipAsync(length, CancellationToken.None).ConfigureAwait(false);
                }
            }

            // The end of the CONNECT stream closes the session; without a capsule, the error code is 0
            if (Terminate(closeInfo ?? new WebTransportCloseInfo(0, ""), exception: null))
            {
                // The endpoint that receives the capsule closes its side of the CONNECT stream
                await CompleteConnectStreamAsync().ConfigureAwait(false);
                await CloseOwnedConnectionAsync().ConfigureAwait(false);
            }
        }
        catch (Http3ProtocolException ex)
        {
            _connectStream.Abort(MsQuicAbortDirection.Both, ex.ErrorCode);
            Terminate(closeInfo: null, new WebTransportException(WebTransportError.ProtocolError, ex.Message, ex));
            await CloseOwnedConnectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MsQuicException or ObjectDisposedException or InvalidOperationException)
        {
            if (closeInfo is not null && Terminate(closeInfo, exception: null))
            {
                await CloseOwnedConnectionAsync().ConfigureAwait(false);
                return;
            }

            if (Terminate(closeInfo: null, new WebTransportException(WebTransportError.SessionReset, "The session ended abruptly.", ex)))
            {
                await CloseOwnedConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task CompleteConnectStreamAsync()
    {
        await _connectStreamWriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _connectStream.CompleteWrites();
            await _connectStream.WritesClosed.WaitAsync(CloseTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MsQuicException or TimeoutException or ObjectDisposedException)
        {
        }
        finally
        {
            _connectStreamWriteLock.Release();
        }
    }

    internal void OnConnectionClosed(Exception exception)
    {
        Terminate(closeInfo: null, new WebTransportException(WebTransportError.SessionReset, "The connection of the session was closed.", exception));
    }

    internal void OnDrain() => _draining.TrySetResult();

    internal void OnDatagram(ReadOnlyMemory<byte> payload)
    {
        _datagrams.Writer.TryWrite(payload);
    }

    internal void OnIncomingStream(MsQuicStream stream, int headerLength)
    {
#pragma warning disable CA2000 // Dispose objects before losing scope: AcceptStreamAsync returns it, or it is disposed below
        var webTransportStream = new WebTransportStream(this, stream, headerLength, isOutbound: false);
#pragma warning restore CA2000
        if (!TryAddStream(webTransportStream) || !_incomingStreams.Writer.TryWrite(webTransportStream))
        {
            webTransportStream.AbortSessionGone();
            _ = webTransportStream.DisposeAsync().AsTask();
        }
    }

    private bool TryAddStream(WebTransportStream stream)
    {
        lock (_lock)
        {
            if (_isTerminated)
                return false;

            _streams.Add(stream);
            return true;
        }
    }

    internal void RemoveStream(WebTransportStream stream)
    {
        lock (_lock)
        {
            _streams.Remove(stream);
        }
    }

    // Ends the session once: its streams are reset, its queues are completed. Returns false when it already ended.
    internal bool Terminate(WebTransportCloseInfo? closeInfo, WebTransportException? exception)
    {
        WebTransportStream[] streams;
        lock (_lock)
        {
            if (_isTerminated)
                return false;

            _isTerminated = true;
            streams = [.. _streams];
            _streams.Clear();
        }

        _closeException = exception ?? new WebTransportException(WebTransportError.SessionClosed, "The session is closed.");
        foreach (var stream in streams)
        {
            if (!stream.IsCompleted)
            {
                stream.AbortSessionGone();
            }
        }

        _incomingStreams.Writer.TryComplete();
        _datagrams.Writer.TryComplete();
        _connection.UnregisterSession(this);
        if (closeInfo is not null)
        {
            _closed.TrySetResult(closeInfo);
        }
        else
        {
            _closed.TrySetException(_closeException);
        }

        return true;
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_closeException is { } exception)
            throw exception;
    }

    private WebTransportException GetClosedException() => _closeException ?? new WebTransportException(WebTransportError.SessionClosed, "The session is closed.");
}
