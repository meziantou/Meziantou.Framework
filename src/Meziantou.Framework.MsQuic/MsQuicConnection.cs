using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using Meziantou.Framework.MsQuic.Interop;

namespace Meziantou.Framework.MsQuic;

/// <summary>A QUIC connection: its streams and its datagrams (RFC 9221).</summary>
/// <remarks>
/// Create a client connection with <see cref="ConnectAsync"/>, or accept one with
/// <see cref="MsQuicListener.AcceptConnectionAsync"/>. Dispose the connection to close it: disposing closes it with
/// <see cref="MsQuicConnectionOptions.DefaultCloseErrorCode"/> unless <see cref="CloseAsync"/> closed it.
/// </remarks>
public sealed class MsQuicConnection : IAsyncDisposable
{
    private readonly MsQuicApi _api;
    private GCHandle _context;
    private int _contextFreed;
    private readonly MsQuicSafeHandle _handle;
    private readonly bool _isServer;
    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdownComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<MsQuicException> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<MsQuicStream> _inboundStreams = Channel.CreateUnbounded<MsQuicStream>(new UnboundedChannelOptions { SingleWriter = true });
    private Channel<byte[]> _receivedDatagrams = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true });
    private MsQuicConnectionOptions? _options;
    private MsQuicSafeHandle? _configuration;
    private SslClientAuthenticationOptions? _clientAuthenticationOptions;
    private string _targetHostName = "";
    private X509Certificate2? _remoteCertificate;
    private Exception? _certificateValidationException;
    private volatile MsQuicException? _closeException;
    private volatile bool _datagramSendEnabled;
    private volatile int _maxDatagramSendSize;
    private bool _started;
    private int _closeRequested;
    private int _disposed;

    private MsQuicConnection(MsQuicApi api, bool isServer)
    {
        _api = api;
        _isServer = isServer;
        var context = GCHandle.Alloc(this);
        _context = context;
        unsafe
        {
            nint handle;
            var status = api.Table->ConnectionOpen(api.Registration, &NativeCallback, (void*)GCHandle.ToIntPtr(context), &handle);
            if (MsQuicStatus.IsFailure(status))
            {
                context.Free();
                MsQuicApi.ThrowIfFailure(status, "ConnectionOpen");
            }

            _handle = new MsQuicSafeHandle(handle, api.Table->ConnectionClose);
        }
    }

    // A connection received by a listener (QUIC_LISTENER_EVENT_NEW_CONNECTION)
    private MsQuicConnection(MsQuicApi api, nint handle, QuicNewConnectionInfo* info)
    {
        unsafe
        {
            _api = api;
            _isServer = true;
            _started = true;
            var context = GCHandle.Alloc(this);
            _context = context;
            _handle = new MsQuicSafeHandle(handle, api.Table->ConnectionClose);
            api.Table->SetCallbackHandler(handle, (delegate* unmanaged[Cdecl]<nint, void*, QuicConnectionEvent*, int>)&NativeCallback, (void*)GCHandle.ToIntPtr(context));
            LocalEndPoint = MsQuicAddress.Read(new ReadOnlySpan<byte>(info->LocalAddress, MsQuicAddress.Size));
            RemoteEndPoint = MsQuicAddress.Read(new ReadOnlySpan<byte>(info->RemoteAddress, MsQuicAddress.Size));
            _targetHostName = MsQuicApi.GetAsciiString(info->ServerName, info->ServerNameLength);
        }
    }

    /// <summary>Gets a value indicating whether MsQuic is available, see <see cref="MsQuicLibrary"/>.</summary>
    public static bool IsSupported => MsQuicLibrary.IsSupported;

    /// <summary>Gets the address of the peer.</summary>
    public IPEndPoint? RemoteEndPoint { get; private set; }

    /// <summary>Gets the local address of the connection.</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    /// <summary>Gets the application protocol (ALPN) negotiated during the handshake.</summary>
    public SslApplicationProtocol NegotiatedApplicationProtocol { get; private set; }

    /// <summary>Gets the server name: the target host of a client connection, the server name indication (SNI) the client sent for a server connection.</summary>
    public string TargetHostName => _targetHostName;

    /// <summary>Gets the certificate of the server, for a client connection.</summary>
    public X509Certificate2? RemoteCertificate => _remoteCertificate;

    /// <summary>Gets a value indicating whether datagrams can be sent: both peers enabled them and the connection is established.</summary>
    public bool IsDatagramSendEnabled => _datagramSendEnabled;

    /// <summary>Gets the maximum size of a datagram payload. It can grow during the connection, as path MTU discovery finds a larger MTU.</summary>
    public int MaxDatagramSendSize => _datagramSendEnabled ? _maxDatagramSendSize : 0;

    /// <summary>
    /// Gets a task that completes when the connection is closed, with the exception that describes why: an exception
    /// whose <see cref="MsQuicException.Error"/> is <see cref="MsQuicError.ConnectionAborted"/> when the peer closed it,
    /// <see cref="MsQuicError.OperationAborted"/> when it was closed locally...
    /// </summary>
    public Task<MsQuicException> Closed => _closed.Task;

    internal MsQuicException? CloseException => _closeException;

    /// <summary>Connects to a QUIC server.</summary>
    /// <exception cref="PlatformNotSupportedException">MsQuic is not available.</exception>
    /// <exception cref="MsQuicException">The connection failed.</exception>
    public static async ValueTask<MsQuicConnection> ConnectAsync(MsQuicClientConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        MsQuicClientConnectionOptions.Validate(options);
        var api = MsQuicApi.Get();

        string targetHost;
        IPAddress[] addresses;
        int port;
        switch (options.RemoteEndPoint)
        {
            case IPEndPoint ipEndPoint:
                targetHost = options.ClientAuthenticationOptions.TargetHost ?? ipEndPoint.Address.ToString();
                addresses = [ipEndPoint.Address];
                port = ipEndPoint.Port;
                break;

            case DnsEndPoint dnsEndPoint:
                targetHost = options.ClientAuthenticationOptions.TargetHost ?? dnsEndPoint.Host;
                port = dnsEndPoint.Port;
                if (IPAddress.TryParse(dnsEndPoint.Host, out var address))
                {
                    addresses = [address];
                }
                else
                {
                    try
                    {
                        addresses = await Dns.GetHostAddressesAsync(dnsEndPoint.Host, dnsEndPoint.AddressFamily, cancellationToken).ConfigureAwait(false);
                    }
                    catch (SocketException ex)
                    {
                        throw new MsQuicException(MsQuicError.HostUnreachable, applicationErrorCode: null, transportErrorCode: null, $"The host name '{dnsEndPoint.Host}' could not be resolved: {ex.Message}", ex);
                    }

                    if (addresses.Length == 0)
                        throw new MsQuicException(MsQuicError.HostUnreachable, applicationErrorCode: null, transportErrorCode: null, $"The host name '{dnsEndPoint.Host}' has no address.");
                }

                break;

            default:
                throw new UnreachableException();
        }

        // The addresses are tried in order, as a server may listen on IPv4 only while the name also resolves to IPv6
        for (var i = 0; ; i++)
        {
            var connection = new MsQuicConnection(api, isServer: false);
            try
            {
                await connection.StartClientAsync(options, new IPEndPoint(addresses[i], port), targetHost, cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch (MsQuicException) when (i < addresses.Length - 1 && !cancellationToken.IsCancellationRequested)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private async ValueTask StartClientAsync(MsQuicClientConnectionOptions options, IPEndPoint remoteEndPoint, string targetHost, CancellationToken cancellationToken)
    {
        _options = options;
        _clientAuthenticationOptions = options.ClientAuthenticationOptions;
        _targetHostName = targetHost;
        RemoteEndPoint = remoteEndPoint;
        _receivedDatagrams = CreateDatagramChannel(options);
        _configuration = MsQuicConfiguration.CreateClient(_api, options);
        Start(remoteEndPoint, targetHost);
        _started = true;
        await WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Start(IPEndPoint remoteEndPoint, string targetHost)
    {
        Span<byte> address = stackalloc byte[MsQuicAddress.Size];
        MsQuicAddress.Write(remoteEndPoint, address, dualMode: false);
        var hostBytes = Encoding.UTF8.GetBytes(targetHost + "\0");
        unsafe
        {
            fixed (byte* addressPointer = address)
            fixed (byte* host = hostBytes)
            {
                MsQuicApi.ThrowIfFailure(_api.Table->SetParam(_handle.DangerousGetHandle(), QuicParameters.ConnectionRemoteAddress, MsQuicAddress.Size, addressPointer), "SetParam(CONN_REMOTE_ADDRESS)");
                MsQuicApi.ThrowIfFailure(_api.Table->ConnectionStart(_handle.DangerousGetHandle(), _configuration!.DangerousGetHandle(), MsQuicAddress.GetNativeFamily(remoteEndPoint.AddressFamily), host, (ushort)remoteEndPoint.Port), "ConnectionStart");
            }
        }
    }

    internal static MsQuicConnection CreateInbound(MsQuicApi api, nint handle, QuicNewConnectionInfo* info)
    {
        unsafe
        {
            return new(api, handle, info);
        }
    }

    // Server side: applies the options of the listener and waits for the end of the handshake
    internal async ValueTask StartServerAsync(MsQuicServerConnectionOptions options, MsQuicSafeHandle configuration, CancellationToken cancellationToken)
    {
        _options = options;
        _receivedDatagrams = CreateDatagramChannel(options);
        SetConfiguration(configuration);
        await WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    // The configuration is shared by the connections of a listener, which disposes it: the reference keeps it open during
    // the call (MsQuic references it once the call returns), and a disposed configuration throws ObjectDisposedException
    private void SetConfiguration(MsQuicSafeHandle configuration)
    {
        var addedReference = false;
        try
        {
            configuration.DangerousAddRef(ref addedReference);
            unsafe
            {
                MsQuicApi.ThrowIfFailure(_api.Table->ConnectionSetConfiguration(_handle.DangerousGetHandle(), configuration.DangerousGetHandle()), "ConnectionSetConfiguration");
            }
        }
        finally
        {
            if (addedReference)
            {
                configuration.DangerousRelease();
            }
        }
    }

    private async ValueTask WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _connected.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Shutdown(_options!.DefaultCloseErrorCode);
            throw;
        }
    }

    private static Channel<byte[]> CreateDatagramChannel(MsQuicConnectionOptions options)
        => Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.ReceivedDatagramQueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true });

    /// <summary>Opens a stream.</summary>
    /// <param name="type">The type of the stream.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    /// <returns>The stream, once the peer allows it (its stream limit).</returns>
    public ValueTask<MsQuicStream> OpenOutboundStreamAsync(MsQuicStreamType type, CancellationToken cancellationToken = default)
        => OpenOutboundStreamAsync(type, waitUntilAvailable: true, cancellationToken);

    /// <summary>Opens a stream.</summary>
    /// <param name="type">The type of the stream.</param>
    /// <param name="waitUntilAvailable">
    /// <see langword="true"/> to wait until the peer allows one more stream; <see langword="false"/> to fail with
    /// <see cref="MsQuicError.StreamLimitReached"/> when it does not allow one now.
    /// </param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    public async ValueTask<MsQuicStream> OpenOutboundStreamAsync(MsQuicStreamType type, bool waitUntilAvailable, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ThrowIfClosed();
        var stream = new MsQuicStream(_api, this, _handle, type, _options!.DefaultStreamErrorCode);
        try
        {
            await stream.StartAsync(waitUntilAvailable, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return stream;
    }

    /// <summary>Waits for the next stream the peer opens.</summary>
    /// <exception cref="MsQuicException">The connection is closed.</exception>
    public async ValueTask<MsQuicStream> AcceptInboundStreamAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            return await _inboundStreams.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw GetClosedException();
        }
    }

    /// <summary>Sends a QUIC datagram (RFC 9221). Datagrams are not retransmitted: the peer may not receive it.</summary>
    /// <param name="datagram">The payload, at most <see cref="MaxDatagramSendSize"/> bytes.</param>
    /// <param name="cancellationToken">The token to stop waiting. The datagram is still sent.</param>
    /// <returns>A task that completes when the datagram is sent on the network.</returns>
    /// <exception cref="MsQuicException">Datagrams are not enabled, the datagram is too large, or the connection closed before it was sent.</exception>
    public Task SendDatagramAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ThrowIfClosed();
        if (!_datagramSendEnabled)
            throw new MsQuicException(MsQuicError.DatagramsNotEnabled, applicationErrorCode: null, transportErrorCode: null, "Datagrams are not enabled on the connection.");

        if (datagram.Length > _maxDatagramSendSize)
            throw new MsQuicException(MsQuicError.DatagramTooLarge, applicationErrorCode: null, transportErrorCode: null, $"The datagram is {datagram.Length} bytes, more than the maximum of {_maxDatagramSendSize} bytes.");

        var operation = new DatagramSendOperation(datagram.Span);
        int status;
        unsafe
        {
            status = _api.Table->DatagramSend(_handle.DangerousGetHandle(), operation.Buffer, 1, 0, (void*)GCHandle.ToIntPtr(operation.Handle));
        }

        if (MsQuicStatus.IsFailure(status))
        {
            operation.Free();
            if (status == MsQuicStatus.InvalidParameter)
                throw new MsQuicException(MsQuicError.DatagramTooLarge, applicationErrorCode: null, transportErrorCode: null, "The datagram is too large.") { NativeErrorCode = status };

            ThrowIfClosed();
            MsQuicApi.ThrowIfFailure(status, "DatagramSend");
        }

        return operation.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Waits for the next datagram the peer sends.</summary>
    /// <exception cref="MsQuicException">The connection is closed.</exception>
    public async ValueTask<byte[]> ReceiveDatagramAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            return await _receivedDatagrams.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw GetClosedException();
        }
    }

    /// <summary>Gets the statistics of the connection: round-trip time, bytes and packets sent and received, losses...</summary>
    public MsQuicConnectionStatistics GetStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var statistics = default(QuicStatisticsV2);
        unsafe
        {
            var length = (uint)sizeof(QuicStatisticsV2);
            MsQuicApi.ThrowIfFailure(_api.Table->GetParam(_handle.DangerousGetHandle(), QuicParameters.ConnectionStatisticsV2, &length, &statistics), "GetParam(CONN_STATISTICS_V2)");
        }

        return new MsQuicConnectionStatistics
        {
            SmoothedRoundTripTime = TimeSpan.FromMicroseconds(statistics.Rtt),
            MinRoundTripTime = TimeSpan.FromMicroseconds(statistics.MinRtt),
            MaxRoundTripTime = TimeSpan.FromMicroseconds(statistics.MaxRtt),
            RoundTripTimeVariation = TimeSpan.FromMicroseconds(statistics.RttVariance),
            PathMtu = statistics.SendPathMtu,
            BytesSent = (long)statistics.SendTotalBytes,
            PacketsSent = (long)statistics.SendTotalPackets,
            StreamBytesSent = (long)statistics.SendTotalStreamBytes,
            BytesReceived = (long)statistics.RecvTotalBytes,
            PacketsReceived = (long)statistics.RecvTotalPackets,
            StreamBytesReceived = (long)statistics.RecvTotalStreamBytes,
            PacketsLost = (long)(statistics.SendSuspectedLostPackets - Math.Min(statistics.SendSuspectedLostPackets, statistics.SendSpuriousLostPackets)),
            PacketsDropped = (long)statistics.RecvDroppedPackets,
            CongestionEvents = statistics.SendCongestionCount,
            CongestionWindow = statistics.SendCongestionWindow,
        };
    }

    /// <summary>Closes the connection with an application error code, and waits until the peer is notified.</summary>
    /// <param name="errorCode">The application error code, from 0 to 2^62-1.</param>
    /// <param name="cancellationToken">The token to stop waiting.</param>
    public async ValueTask CloseAsync(long errorCode, CancellationToken cancellationToken = default)
    {
        MsQuicConnectionOptions.ValidateErrorCode(errorCode);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        Shutdown(errorCode);
        if (_started)
        {
            await _shutdownComplete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void Shutdown(long errorCode)
    {
        if (Interlocked.Exchange(ref _closeRequested, 1) == 0 && _started)
        {
            unsafe
            {
                _api.Table->ConnectionShutdown(_handle.DangerousGetHandle(), QuicFlags.ConnectionShutdownNone, (ulong)errorCode);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_started)
        {
            Shutdown(_options?.DefaultCloseErrorCode ?? 0);
            await _shutdownComplete.Task.ConfigureAwait(false);
        }

        // Streams the application did not accept
        _inboundStreams.Writer.TryComplete();
        while (_inboundStreams.Reader.TryRead(out var stream))
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        _handle.Dispose();
        FreeContext();
        _configuration?.Dispose();
        _remoteCertificate?.Dispose();
        _closed.TrySetResult(_closeException ?? new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The connection was disposed."));
    }

    internal static void ReportCallbackException(Exception exception)
    {
        Debug.Fail("Unexpected exception in an MsQuic callback: " + exception);
    }

    private void ThrowIfClosed()
    {
        if (_closeException is { } exception)
            throw exception;
    }

    private MsQuicException GetClosedException() => _closeException ?? new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The connection is closed.");

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
    private static int NativeCallback(nint handle, void* context, QuicConnectionEvent* connectionEvent)
    {
        unsafe
        {
            if (GCHandle.FromIntPtr((nint)context).Target is not MsQuicConnection connection)
                return MsQuicStatus.Success;

            try
            {
                return connection.HandleEvent(connectionEvent);
            }
            catch (Exception ex)
            {
                ReportCallbackException(ex);
                return MsQuicStatus.InternalError;
            }
        }
    }

    private int HandleEvent(QuicConnectionEvent* connectionEvent)
    {
        unsafe
        {
            switch (connectionEvent->Type)
            {
                case QuicConnectionEvent.Connected:
                    NegotiatedApplicationProtocol = new SslApplicationProtocol(new ReadOnlySpan<byte>(connectionEvent->ConnectedNegotiatedAlpn, connectionEvent->ConnectedNegotiatedAlpnLength).ToArray());
                    LocalEndPoint ??= GetAddress(QuicParameters.ConnectionLocalAddress);
                    _connected.TrySetResult();
                    break;

                case QuicConnectionEvent.ShutdownInitiatedByTransport:
                    {
                        var status = connectionEvent->ShutdownByTransportStatus;
                        var error = MsQuicStatus.ToError(status);
                        var message = error switch
                        {
                            MsQuicError.ConnectionIdle => "The connection was idle for longer than the idle timeout.",
                            MsQuicError.ConnectionTimeout => "The connection timed out.",
                            MsQuicError.ConnectionRefused => "The peer refused the connection.",
                            MsQuicError.HostUnreachable => "The host is unreachable.",
                            MsQuicError.HandshakeFailure when _certificateValidationException is not null => "The certificate of the server is not valid: " + _certificateValidationException.Message,
                            MsQuicError.HandshakeFailure => "The TLS handshake failed.",
                            _ => $"The transport closed the connection (status 0x{status:X8}).",
                        };
                        var exception = new MsQuicException(error, applicationErrorCode: null, (long)connectionEvent->ShutdownByTransportErrorCode, message, _certificateValidationException) { NativeErrorCode = status };
                        SetCloseException(exception);
                        break;
                    }

                case QuicConnectionEvent.ShutdownInitiatedByPeer:
                    {
                        var errorCode = (long)connectionEvent->ShutdownByPeerErrorCode;
                        SetCloseException(new MsQuicException(MsQuicError.ConnectionAborted, errorCode, transportErrorCode: null, $"The peer closed the connection with the error code {errorCode}."));
                        break;
                    }

                case QuicConnectionEvent.ShutdownComplete:
                    {
                        var exception = SetCloseException(new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The connection was closed locally."));
                        _datagramSendEnabled = false;
                        _inboundStreams.Writer.TryComplete();
                        _receivedDatagrams.Writer.TryComplete();
                        _closed.TrySetResult(exception);
                        _shutdownComplete.TrySetResult();
                    FreeContext();
                        break;
                    }

                case QuicConnectionEvent.PeerStreamStarted:
                    {
    #pragma warning disable CA2000 // Dispose objects before losing scope: AcceptInboundStreamAsync or DisposeAsync returns it
                        var stream = new MsQuicStream(_api, this, _handle, connectionEvent->PeerStreamStartedStream, connectionEvent->PeerStreamStartedFlags, _options?.DefaultStreamErrorCode ?? 0);
    #pragma warning restore CA2000
                        if (!_inboundStreams.Writer.TryWrite(stream))
                        {
                            _ = stream.DisposeAsync().AsTask();
                        }

                        break;
                    }

                case QuicConnectionEvent.DatagramStateChanged:
                    _maxDatagramSendSize = connectionEvent->DatagramStateChangedMaxSendLength;
                    _datagramSendEnabled = connectionEvent->DatagramStateChangedSendEnabled != 0;
                    break;

                case QuicConnectionEvent.DatagramReceived:
                    {
                        var buffer = connectionEvent->DatagramReceivedBuffer;
                        _receivedDatagrams.Writer.TryWrite(new ReadOnlySpan<byte>(buffer->Buffer, (int)buffer->Length).ToArray());
                        break;
                    }

                case QuicConnectionEvent.DatagramSendStateChanged:
                    DatagramSendOperation.OnStateChanged(connectionEvent->DatagramSendStateChangedClientContext, connectionEvent->DatagramSendStateChangedState, this);
                    break;

                case QuicConnectionEvent.PeerCertificateReceived:
                    return ValidateRemoteCertificate(connectionEvent->PeerCertificateReceivedCertificate, connectionEvent->PeerCertificateReceivedChain);
            }

            return MsQuicStatus.Success;
        }
    }

    private MsQuicException SetCloseException(MsQuicException exception)
    {
#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile: Interlocked provides the barrier
        var previous = Interlocked.CompareExchange(ref _closeException, exception, null);
#pragma warning restore CS0420
        var result = previous ?? exception;
        _connected.TrySetException(result);
        _ = _connected.Task.Exception;
        return result;
    }

    private IPEndPoint? GetAddress(uint parameter)
    {
        Span<byte> address = stackalloc byte[MsQuicAddress.Size];
        unsafe
        {
            fixed (byte* addressPointer = address)
            {
                var length = (uint)MsQuicAddress.Size;
                if (MsQuicStatus.IsFailure(_api.Table->GetParam(_handle.DangerousGetHandle(), parameter, &length, addressPointer)))
                    return null;
            }
        }

        return MsQuicAddress.Read(address);
    }

    private int ValidateRemoteCertificate(void* certificatePointer, void* chainPointer)
    {
        unsafe
        {
            if (_isServer || _clientAuthenticationOptions is null)
                return MsQuicStatus.Success;

            X509Certificate2? certificate = null;
            X509Certificate2Collection? additionalCertificates = null;
            try
            {
                if (_api.UsesSchannel)
                {
                    if (certificatePointer is not null)
                    {
                        certificate = new X509Certificate2((nint)certificatePointer);
                    }
                }
                else
                {
                    var certificateBuffer = (QuicBuffer*)certificatePointer;
                    if (certificateBuffer is not null && certificateBuffer->Length > 0)
                    {
                        certificate = X509CertificateLoader.LoadCertificate(new ReadOnlySpan<byte>(certificateBuffer->Buffer, (int)certificateBuffer->Length));
                    }

                    var chainBuffer = (QuicBuffer*)chainPointer;
                    if (chainBuffer is not null && chainBuffer->Length > 0)
                    {
                        additionalCertificates = [];
    #pragma warning disable SYSLIB0057 // The chain is a PKCS #7 blob, which X509CertificateLoader does not load
                        additionalCertificates.Import(new ReadOnlySpan<byte>(chainBuffer->Buffer, (int)chainBuffer->Length));
    #pragma warning restore SYSLIB0057
                    }
                }

                if (MsQuicCertificateValidation.Validate(this, _clientAuthenticationOptions, _targetHostName, certificate, additionalCertificates, out var validationError))
                {
                    _remoteCertificate = certificate;
                    certificate = null;
                    return MsQuicStatus.Success;
                }

                _certificateValidationException = validationError;
                return MsQuicStatus.BadCertificate;
            }
            catch (Exception ex)
            {
                _certificateValidationException = ex;
                return MsQuicStatus.BadCertificate;
            }
            finally
            {
                certificate?.Dispose();
                if (additionalCertificates is not null)
                {
                    foreach (var additionalCertificate in additionalCertificates)
                    {
                        additionalCertificate.Dispose();
                    }
                }
            }
        }
    }

    // A datagram being sent: MsQuic reads its buffer until the send state is final, so it is copied to native memory
    // and freed then.
    private sealed class DatagramSendOperation
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private nint _block;

        public DatagramSendOperation(ReadOnlySpan<byte> payload)
        {
            unsafe
            {
                var block = (byte*)NativeMemory.Alloc((nuint)(sizeof(QuicBuffer) + payload.Length));
                var buffer = (QuicBuffer*)block;
                buffer->Length = (uint)payload.Length;
                buffer->Buffer = block + sizeof(QuicBuffer);
                payload.CopyTo(new Span<byte>(buffer->Buffer, payload.Length));
                _block = (nint)block;
            }

            Handle = GCHandle.Alloc(this);
        }

        public QuicBuffer* Buffer => (QuicBuffer*)_block;

        public GCHandle Handle { get; }

        public Task Task => _completion.Task;

        public void Free()
        {
            unsafe
            {
                unsafe
                {
                    NativeMemory.Free((void*)_block);
                }

                _block = 0;
                Handle.Free();
            }
        }

        public static void OnStateChanged(void* context, int state, MsQuicConnection connection)
        {
            unsafe
            {
                if (context is null)
                    return;

                var operation = (DatagramSendOperation)GCHandle.FromIntPtr((nint)context).Target!;
                if (state == QuicFlags.DatagramSendCanceled)
                {
                    operation._completion.TrySetException(connection._closeException ?? new MsQuicException(MsQuicError.OperationAborted, applicationErrorCode: null, transportErrorCode: null, "The datagram was not sent."));
                    _ = operation._completion.Task.Exception;
                }
                else if (state != QuicFlags.DatagramSendUnknown)
                {
                    operation._completion.TrySetResult();
                }

                if (state >= QuicFlags.DatagramSendLostDiscarded)
                {
                    operation.Free();
                }
            }
        }
    }
}
