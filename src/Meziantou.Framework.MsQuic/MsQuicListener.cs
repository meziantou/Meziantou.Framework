using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading.Channels;
using Meziantou.Framework.MsQuic.Interop;

namespace Meziantou.Framework.MsQuic;

/// <summary>Listens for QUIC connections.</summary>
public sealed class MsQuicListener : IAsyncDisposable
{
    private readonly MsQuicApi _api;
    private GCHandle _context;
    private int _contextFreed;
    private readonly MsQuicSafeHandle _handle;
    private readonly MsQuicListenerOptions _options;
    private readonly Channel<MsQuicConnection> _acceptedConnections = Channel.CreateUnbounded<MsQuicConnection>();
    private readonly TaskCompletionSource _stopComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Lock _configurationsLock = new();
    private readonly Dictionary<MsQuicServerConnectionOptions, MsQuicSafeHandle> _configurations = new(ReferenceEqualityComparer.Instance);
    private int _pendingConnections;
    private int _disposed;

    private MsQuicListener(MsQuicApi api, MsQuicListenerOptions options)
    {
        _api = api;
        _options = options;
        var context = GCHandle.Alloc(this);
        _context = context;
        nint handle;
        unsafe
        {
            var status = api.Table->ListenerOpen(api.Registration, &NativeCallback, (void*)GCHandle.ToIntPtr(context), &handle);
            if (MsQuicStatus.IsFailure(status))
            {
                context.Free();
                MsQuicApi.ThrowIfFailure(status, "ListenerOpen");
            }

            _handle = new MsQuicSafeHandle(handle, api.Table->ListenerClose);
        }

        try
        {
            using var alpn = new MsQuicConfiguration.ApplicationProtocolBuffers(options.ApplicationProtocols);
            Span<byte> address = stackalloc byte[MsQuicAddress.Size];
            MsQuicAddress.Write(options.ListenEndPoint, address, dualMode: true);
            unsafe
            {
                fixed (byte* addressPointer = address)
                {
                    MsQuicApi.ThrowIfFailure(api.Table->ListenerStart(handle, alpn.Buffers, (uint)alpn.Count, addressPointer), "ListenerStart");
                    var length = (uint)MsQuicAddress.Size;
                    MsQuicApi.ThrowIfFailure(api.Table->GetParam(handle, QuicParameters.ListenerLocalAddress, &length, addressPointer), "GetParam(LISTENER_LOCAL_ADDRESS)");
                }
            }

            LocalEndPoint = MsQuicAddress.Read(address) ?? options.ListenEndPoint;
        }
        catch
        {
            _handle.Dispose();
            FreeContext();
            throw;
        }
    }

    /// <summary>Gets a value indicating whether MsQuic is available, see <see cref="MsQuicLibrary"/>.</summary>
    public static bool IsSupported => MsQuicLibrary.IsSupported;

    /// <summary>Gets the address the listener listens on, with the port it picked when the options set port 0.</summary>
    public IPEndPoint LocalEndPoint { get; }

    /// <summary>Starts listening.</summary>
    /// <exception cref="PlatformNotSupportedException">MsQuic is not available.</exception>
    /// <exception cref="MsQuicException">The listener could not start, for instance because the address is in use.</exception>
    public static ValueTask<MsQuicListener> ListenAsync(MsQuicListenerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        MsQuicListenerOptions.Validate(options);
        cancellationToken.ThrowIfCancellationRequested();
        var api = MsQuicApi.Get();
#pragma warning disable CA2000 // Dispose objects before losing scope: the caller owns the listener
        return ValueTask.FromResult(new MsQuicListener(api, options));
#pragma warning restore CA2000
    }

    /// <summary>Waits for the next connection whose handshake completed.</summary>
    /// <exception cref="ObjectDisposedException">The listener is disposed.</exception>
    public async ValueTask<MsQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            var connection = await _acceptedConnections.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref _pendingConnections);
            return connection;
        }
        catch (ChannelClosedException ex)
        {
            throw new ObjectDisposedException(GetType().FullName, ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await _disposeCancellation.CancelAsync().ConfigureAwait(false);
        unsafe
        {
            _api.Table->ListenerStop(_handle.DangerousGetHandle());
        }

        await _stopComplete.Task.ConfigureAwait(false);
        _handle.Dispose();
        FreeContext();

        _acceptedConnections.Writer.TryComplete();
        while (_acceptedConnections.Reader.TryRead(out var connection))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        lock (_configurationsLock)
        {
            // MsQuic keeps a reference on the configuration of each connection
            foreach (var configuration in _configurations.Values)
            {
                configuration.Dispose();
            }

            _configurations.Clear();
        }

        _disposeCancellation.Dispose();
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
    private static int NativeCallback(nint handle, void* context, QuicListenerEvent* listenerEvent)
    {
        unsafe
        {
            if (GCHandle.FromIntPtr((nint)context).Target is not MsQuicListener listener)
                return MsQuicStatus.Success;

            try
            {
                switch (listenerEvent->Type)
                {
                    case QuicListenerEvent.NewConnection:
                        return listener.OnNewConnection(listenerEvent->NewConnectionConnection, listenerEvent->NewConnectionInfo);

                    case QuicListenerEvent.StopComplete:
                        listener._stopComplete.TrySetResult();
                        break;
                }
            }
            catch
            {
                return MsQuicStatus.InternalError;
            }

            return MsQuicStatus.Success;
        }
    }

    private int OnNewConnection(nint handle, QuicNewConnectionInfo* info)
    {
        unsafe
        {
            if (_disposed != 0 || Interlocked.Increment(ref _pendingConnections) > _options.ListenBacklog)
            {
                Interlocked.Decrement(ref _pendingConnections);
                return MsQuicStatus.ConnectionRefused;
            }

            var serverName = MsQuicApi.GetAsciiString(info->ServerName, info->ServerNameLength);
    #pragma warning disable CA2000 // Dispose objects before losing scope: HandshakeAsync disposes it or hands it to AcceptConnectionAsync
            var connection = MsQuicConnection.CreateInbound(_api, handle, info);
    #pragma warning restore CA2000

            // MsQuic waits for ConnectionSetConfiguration to continue the handshake: it is called once the options are known
            _ = HandshakeAsync(connection, serverName);
            return MsQuicStatus.Success;
        }
    }

    private async Task HandshakeAsync(MsQuicConnection connection, string serverName)
    {
        try
        {
            var cancellationToken = _disposeCancellation.Token;
#pragma warning disable CA5398 // Avoid hardcoded SslProtocols values: QUIC always uses TLS 1.3 (RFC 9001)
            var clientHello = new SslClientHelloInfo(serverName, SslProtocols.Tls13);
#pragma warning restore CA5398
            var options = await _options.ConnectionOptionsCallback!(clientHello, cancellationToken).ConfigureAwait(false);
            if (options is null)
                throw new InvalidOperationException($"{nameof(MsQuicListenerOptions.ConnectionOptionsCallback)} returned null.");

            MsQuicServerConnectionOptions.Validate(options);
            var configuration = GetConfiguration(options);
            await connection.StartServerAsync(options, configuration, cancellationToken).ConfigureAwait(false);
            if (!_acceptedConnections.Writer.TryWrite(connection))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            Interlocked.Decrement(ref _pendingConnections);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private MsQuicSafeHandle GetConfiguration(MsQuicServerConnectionOptions options)
    {
        lock (_configurationsLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (!_configurations.TryGetValue(options, out var configuration))
            {
                configuration = MsQuicConfiguration.CreateServer(_api, options, _options.ApplicationProtocols);
                _configurations.Add(options, configuration);
            }

            return configuration;
        }
    }
}
