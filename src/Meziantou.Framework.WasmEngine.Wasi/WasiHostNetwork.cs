using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>The uses of an address that a <see cref="WasiSocketAddressCheck"/> checks.</summary>
public enum WasiSocketAddressUse
{
    /// <summary>A TCP socket binds to the local address.</summary>
    TcpBind,

    /// <summary>A TCP socket connects to the remote address.</summary>
    TcpConnect,

    /// <summary>A UDP socket binds to the local address.</summary>
    UdpBind,

    /// <summary>A UDP socket limits its datagrams to the remote address.</summary>
    UdpConnect,

    /// <summary>A UDP socket sends a datagram to the remote address.</summary>
    UdpOutgoingDatagram,
}

/// <summary>Decides whether a component can use an address (see <see cref="WasiHostNetworkOptions.AddressCheck"/>).</summary>
/// <param name="endPoint">The address and the port.</param>
/// <param name="use">What the component does with the address.</param>
/// <returns>Whether the component can use the address: otherwise the operation fails with <c>access-denied</c>.</returns>
public delegate bool WasiSocketAddressCheck(IPEndPoint endPoint, WasiSocketAddressUse use);

/// <summary>The rules of a <see cref="WasiHostNetwork"/>: what the component can do on the network of the machine.</summary>
/// <remarks>By default, the component cannot use any address and cannot look up the names: give it only the access it needs.</remarks>
public sealed class WasiHostNetworkOptions
{
    /// <summary>Gets a check that allows every address.</summary>
    public static WasiSocketAddressCheck AllowAll { get; } = (_, _) => true;

    /// <summary>Gets a check that allows the loopback addresses only (<c>127.0.0.0/8</c> and <c>::1</c>): the component cannot reach the other machines.</summary>
    public static WasiSocketAddressCheck AllowLoopback { get; } = (endPoint, _) => IPAddress.IsLoopback(endPoint.Address);

    /// <summary>Gets the check of the addresses that the component binds to, connects to and sends datagrams to, or null to deny every address.</summary>
    public WasiSocketAddressCheck? AddressCheck { get; init; }

    /// <summary>Gets whether the component can look up host names (<c>ip-name-lookup</c>). The IP addresses written as names are always allowed: they are not looked up.</summary>
    public bool AllowNameLookup { get; init; }

    /// <summary>Gets whether the component can create TCP sockets. The default is true.</summary>
    public bool AllowTcp { get; init; } = true;

    /// <summary>Gets whether the component can create UDP sockets. The default is true.</summary>
    public bool AllowUdp { get; init; } = true;
}

/// <summary>A network that uses the sockets of the machine (<see cref="Socket"/>), with the rules of a <see cref="WasiHostNetworkOptions"/>.</summary>
/// <remarks>
/// The IPv6 sockets only use IPv6 (<c>IPV6_V6ONLY</c>). The TCP sockets reuse the addresses of the closed connections
/// (<c>SO_REUSEADDR</c>, the default on Windows). The sockets are not supported on the browser.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs" ref:region="WasiNetwork_HostNetwork" ref:dedent="true">
/// // The component can only use the loopback addresses (the services of this machine), and cannot look up names
/// var network = new WasiHostNetwork(new WasiHostNetworkOptions
/// {
///     AddressCheck = WasiHostNetworkOptions.AllowLoopback,
///     AllowNameLookup = false,
///     AllowUdp = false,
/// });
///
/// var wasi = new WasiComponentContext(new WasiOptions { Network = network });
/// </code>
/// </example>
[UnsupportedOSPlatform("browser")]
public sealed class WasiHostNetwork : WasiNetwork
{
    private readonly WasiHostNetworkOptions _options;

    /// <summary>Initializes a new instance of the <see cref="WasiHostNetwork"/> class.</summary>
    /// <param name="options">The rules of the network.</param>
    public WasiHostNetwork(WasiHostNetworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    private static SocketException AccessDenied() => new((int)SocketError.AccessDenied);

    /// <summary>Returns the size of a buffer as the component set it: Linux doubles the size it is given, for its bookkeeping.</summary>
    private static int GetBufferSize(int value) => OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ? value / 2 : value;

    /// <summary>The hop limit of the system for a socket that does not set one (macOS reports -1 for it).</summary>
    private const int DefaultHopLimit = 64;

    /// <summary>Returns the hop limit of a socket: the default hop limit of the system when the socket does not set one.</summary>
    private static int GetHopLimit(Socket socket) => socket.Ttl is var ttl and > 0 ? ttl : DefaultHopLimit;

    private void Check(IPEndPoint endPoint, WasiSocketAddressUse use)
    {
        if (_options.AddressCheck?.Invoke(endPoint, use) != true)
            throw AccessDenied();
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The WASI socket owns the socket")]
    public override WasiTcpSocket CreateTcpSocket(AddressFamily addressFamily)
    {
        if (!_options.AllowTcp)
            throw AccessDenied();

        return new HostTcpSocket(this, CreateSocket(addressFamily, SocketType.Stream, ProtocolType.Tcp));
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The WASI socket owns the socket")]
    public override WasiUdpSocket CreateUdpSocket(AddressFamily addressFamily)
    {
        if (!_options.AllowUdp)
            throw AccessDenied();

        var socket = CreateSocket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows())
        {
            // Windows reports the ICMP "port unreachable" of a previous datagram as a reset of the next receive (SIO_UDP_CONNRESET)
            socket.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        }

        return new HostUdpSocket(this, socket);
    }

    private static Socket CreateSocket(AddressFamily addressFamily, SocketType type, ProtocolType protocol)
    {
        if (addressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            throw new SocketException((int)SocketError.AddressFamilyNotSupported);

        var socket = new Socket(addressFamily, type, protocol);
        try
        {
            if (addressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = false;
            }

            if (!OperatingSystem.IsWindows() && type == SocketType.Stream)
            {
                // SocketOptionName.ReuseAddress also sets SO_REUSEPORT on Unix, which lets another socket bind to the port of
                // a listening socket: only SO_REUSEADDR is set (SOL_SOCKET and SO_REUSEADDR of Linux, or of the BSDs)
                var (level, name) = OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ? (1, 2) : (0xFFFF, 4);
                socket.SetRawSocketOption(level, name, BitConverter.GetBytes(1));
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return socket;
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<IPAddress>> ResolveAddressesAsync(string name, CancellationToken cancellationToken)
    {
        if (!_options.AllowNameLookup)
            throw AccessDenied();

        return await Dns.GetHostAddressesAsync(name, cancellationToken).ConfigureAwait(false);
    }

    [UnsupportedOSPlatform("browser")]
    private sealed class HostTcpSocket(WasiHostNetwork network, Socket socket) : WasiTcpSocket
    {
        public override IPEndPoint? LocalEndPoint => socket.IsBound ? socket.LocalEndPoint as IPEndPoint : null;

        public override IPEndPoint? RemoteEndPoint
        {
            get
            {
                try
                {
                    return socket.RemoteEndPoint as IPEndPoint;
                }
                catch (SocketException)
                {
                    return null;
                }
            }
        }

        public override void Bind(IPEndPoint localEndPoint)
        {
            network.Check(localEndPoint, WasiSocketAddressUse.TcpBind);
            socket.Bind(localEndPoint);
        }

        public override async ValueTask ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            network.Check(remoteEndPoint, WasiSocketAddressUse.TcpConnect);
            await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);
        }

        public override void Listen(int backlog) => socket.Listen(backlog);

        public override async ValueTask<WasiTcpSocket> AcceptAsync(CancellationToken cancellationToken)
        {
            var accepted = await socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return new HostTcpSocket(network, accepted);
        }

        public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            while (!buffer.IsEmpty)
            {
                var sent = await socket.SendAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                buffer = buffer[sent..];
            }
        }

        public override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) => socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

        public override void ShutdownSend() => socket.Shutdown(SocketShutdown.Send);

        public override bool KeepAlive
        {
            get => (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)! != 0;
            set => socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, value);
        }

        public override TimeSpan KeepAliveIdleTime
        {
            get => TimeSpan.FromSeconds((int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime)!);
            set => socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, ToSeconds(value));
        }

        public override TimeSpan KeepAliveInterval
        {
            get => TimeSpan.FromSeconds((int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval)!);
            set => socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, ToSeconds(value));
        }

        public override int KeepAliveCount
        {
            get => (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount)!;
            set => socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Math.Clamp(value, 1, MaxKeepAliveCount));
        }

        public override int HopLimit
        {
            get => GetHopLimit(socket);
            set => socket.Ttl = (short)Math.Clamp(value, 1, 255);
        }

        public override int ReceiveBufferSize
        {
            get => GetBufferSize(socket.ReceiveBufferSize);
            set => socket.ReceiveBufferSize = value;
        }

        public override int SendBufferSize
        {
            get => GetBufferSize(socket.SendBufferSize);
            set => socket.SendBufferSize = value;
        }

        // The systems take the times in seconds, up to 32767 (Linux): the longer times are clamped, a time shorter than a second is a second
        private static int ToSeconds(TimeSpan value) => (int)Math.Clamp(Math.Ceiling(value.TotalSeconds), 1, MaxKeepAliveSeconds);

        private const int MaxKeepAliveSeconds = 32767;

        private const int MaxKeepAliveCount = 127;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                socket.Dispose();
            }
        }
    }

    [UnsupportedOSPlatform("browser")]
    private sealed class HostUdpSocket(WasiHostNetwork network, Socket socket) : WasiUdpSocket
    {
        private Socket _socket = socket;
        private IPEndPoint? _remoteEndPoint;
        private IPEndPoint? _boundEndPoint;

        public override IPEndPoint? LocalEndPoint => _socket.IsBound ? _socket.LocalEndPoint as IPEndPoint : null;

        public override void Bind(IPEndPoint localEndPoint)
        {
            network.Check(localEndPoint, WasiSocketAddressUse.UdpBind);
            _socket.Bind(localEndPoint);
            _boundEndPoint = (IPEndPoint)_socket.LocalEndPoint!;
        }

        private void BindImplicitly()
        {
            if (!_socket.IsBound)
            {
                Bind(new IPEndPoint(_socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            }
        }

        // The system filters the datagrams of a connected socket, and gives it the local address of the route
        public override void Connect(IPEndPoint? remoteEndPoint)
        {
            if (remoteEndPoint is null)
            {
                Disconnect();
                return;
            }

            network.Check(remoteEndPoint, WasiSocketAddressUse.UdpConnect);
            BindImplicitly();
            Disconnect();
            _socket.Connect(remoteEndPoint);
            _remoteEndPoint = remoteEndPoint;
        }

        /// <summary>
        /// .NET cannot disconnect a UDP socket (connect to AF_UNSPEC): the socket is replaced with a socket bound to the
        /// address it was bound to, with the same options.
        /// </summary>
        private void Disconnect()
        {
            if (_remoteEndPoint is null)
                return;

            var replacement = CreateSocket(_socket.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                // A socket that does not set its hop limit keeps the default of the system
                if (_socket.Ttl > 0)
                {
                    replacement.Ttl = _socket.Ttl;
                }

                replacement.ReceiveBufferSize = GetBufferSize(_socket.ReceiveBufferSize);
                replacement.SendBufferSize = GetBufferSize(_socket.SendBufferSize);
                if (OperatingSystem.IsWindows())
                {
                    replacement.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
                }
            }
            catch
            {
                replacement.Dispose();
                throw;
            }

            // The replacement is kept even when its bind fails (the port was taken meanwhile): the socket is not connected anymore
            _socket.Dispose();
            _socket = replacement;
            _remoteEndPoint = null;
            replacement.Bind(_boundEndPoint!);
        }

        public override async ValueTask SendToAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            network.Check(remoteEndPoint, WasiSocketAddressUse.UdpOutgoingDatagram);
            BindImplicitly();

            // Some systems (macOS) reject an address on a connected socket
            _ = _remoteEndPoint is not null
                ? await _socket.SendAsync(datagram, SocketFlags.None, cancellationToken).ConfigureAwait(false)
                : await _socket.SendToAsync(datagram, SocketFlags.None, remoteEndPoint, cancellationToken).ConfigureAwait(false);
        }

        public override async ValueTask<(int Length, IPEndPoint RemoteEndPoint)> ReceiveFromAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var any = new IPEndPoint(_socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            var result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, any, cancellationToken).ConfigureAwait(false);
            return (result.ReceivedBytes, (IPEndPoint)result.RemoteEndPoint);
        }

        public override int HopLimit
        {
            get => GetHopLimit(_socket);
            set => _socket.Ttl = (short)Math.Clamp(value, 1, 255);
        }

        public override int ReceiveBufferSize
        {
            get => GetBufferSize(_socket.ReceiveBufferSize);
            set => _socket.ReceiveBufferSize = value;
        }

        public override int SendBufferSize
        {
            get => GetBufferSize(_socket.SendBufferSize);
            set => _socket.SendBufferSize = value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _socket.Dispose();
            }
        }
    }
}
