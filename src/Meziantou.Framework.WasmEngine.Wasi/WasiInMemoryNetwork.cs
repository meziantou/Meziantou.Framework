using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// A network inside the process: the sockets of the components connect to each other and to the host
/// (<see cref="ConnectAsync"/>, <see cref="Listen"/>), without the network of the machine. It works on every platform,
/// including the browser.
/// </summary>
/// <remarks>
/// <para>
/// The addresses of the network are the loopback addresses (<c>127.0.0.0/8</c> and <c>::1</c>) and the
/// <see cref="LocalAddresses"/>: a socket binds to one of them, or to the unspecified address (all of them). The other
/// addresses are not reachable. The ports are allocated as on a machine: a port 0 is a free port from 49152, and two
/// sockets cannot bind to the same port of the same address.
/// </para>
/// <para>
/// The names are the <see cref="HostNames"/>: <c>localhost</c> is the loopback addresses.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs" ref:region="WasiNetwork_InMemoryNetwork" ref:dedent="true">
/// // A network inside the process: the host listens and connects next to the components, without the network of the machine
/// var network = new WasiInMemoryNetwork();
/// using var listener = network.Listen(new IPEndPoint(IPAddress.Loopback, 8080));
///
/// await using var client = await network.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8080));
/// await using var server = await listener.AcceptAsync();
/// await client.WriteAsync("ping"u8.ToArray());
///
/// var buffer = new byte[4];
/// await server.ReadExactlyAsync(buffer);
/// Assert.Equal("ping", Encoding.ASCII.GetString(buffer));
///
/// var wasi = new WasiComponentContext(new WasiOptions { Network = network });
/// </code>
/// </example>
public sealed class WasiInMemoryNetwork : WasiNetwork
{
    private const int FirstEphemeralPort = 49152;

    private readonly Lock _lock = new();
    private readonly List<InMemoryTcpSocket> _tcpSockets = [];
    private readonly List<InMemoryUdpSocket> _udpSockets = [];

    /// <summary>Gets the addresses of the network, in addition to the loopback addresses. The default is no address.</summary>
    public IList<IPAddress> LocalAddresses { get; } = [];

    /// <summary>Gets the addresses of the host names (compared without case). The default is <c>localhost</c>, whose addresses are <c>127.0.0.1</c> and <c>::1</c>.</summary>
    public IDictionary<string, IReadOnlyList<IPAddress>> HostNames { get; } = new Dictionary<string, IReadOnlyList<IPAddress>>(StringComparer.OrdinalIgnoreCase)
    {
        ["localhost"] = [IPAddress.Loopback, IPAddress.IPv6Loopback],
    };

    /// <inheritdoc/>
    public override WasiTcpSocket CreateTcpSocket(AddressFamily addressFamily) => new InMemoryTcpSocket(this, CheckFamily(addressFamily));

    /// <inheritdoc/>
    public override WasiUdpSocket CreateUdpSocket(AddressFamily addressFamily) => new InMemoryUdpSocket(this, CheckFamily(addressFamily));

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IPAddress>> ResolveAddressesAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_lock)
        {
            if (HostNames.TryGetValue(name, out var addresses) && addresses.Count > 0)
                return ValueTask.FromResult<IReadOnlyList<IPAddress>>([.. addresses]);
        }

        return ValueTask.FromException<IReadOnlyList<IPAddress>>(new SocketException((int)SocketError.HostNotFound));
    }

    /// <summary>Connects the host to a listening socket of the network, such as a server of a component.</summary>
    /// <param name="remoteEndPoint">The address of the listening socket.</param>
    /// <param name="cancellationToken">The token to cancel the connection.</param>
    /// <returns>The stream of the connection: dispose it to close the connection.</returns>
    /// <exception cref="SocketException">No socket listens on the address (<see cref="SocketError.ConnectionRefused"/>).</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The stream owns the socket")]
    public async ValueTask<Stream> ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        var socket = new InMemoryTcpSocket(this, CheckFamily(remoteEndPoint.AddressFamily));
        try
        {
            await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new InMemoryTcpStream(socket);
    }

    /// <summary>Listens on an address of the network for the connections of the components, such as a server of the host.</summary>
    /// <param name="localEndPoint">The address. The port 0 is a free port.</param>
    /// <returns>The listener: dispose it to stop listening.</returns>
    /// <exception cref="SocketException">The address is not an address of the network, or is in use.</exception>
    public WasiInMemoryTcpListener Listen(IPEndPoint localEndPoint)
    {
        ArgumentNullException.ThrowIfNull(localEndPoint);
        var socket = new InMemoryTcpSocket(this, CheckFamily(localEndPoint.AddressFamily));
        try
        {
            socket.Bind(localEndPoint);
            socket.Listen(128);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new WasiInMemoryTcpListener(socket);
    }

    private static AddressFamily CheckFamily(AddressFamily addressFamily) => addressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
        ? addressFamily
        : throw new SocketException((int)SocketError.AddressFamilyNotSupported);

    private static IPAddress Unspecified(AddressFamily family) => family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;

    private static IPAddress Loopback(AddressFamily family) => family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;

    private bool IsLocal(IPAddress address) => IPAddress.IsLoopback(address) || LocalAddresses.Contains(address);

    private static bool IsUnspecified(IPAddress address) => address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    /// <summary>Whether a socket bound to <paramref name="bound"/> receives the packets sent to <paramref name="destination"/>.</summary>
    private static bool Matches(IPEndPoint bound, IPEndPoint destination) => bound.AddressFamily == destination.AddressFamily && bound.Port == destination.Port && (IsUnspecified(bound.Address) || bound.Address.Equals(destination.Address));

    private static bool Conflicts(IPEndPoint left, IPEndPoint right) => left.AddressFamily == right.AddressFamily && left.Port == right.Port && (IsUnspecified(left.Address) || IsUnspecified(right.Address) || left.Address.Equals(right.Address));

    /// <summary>Allocates a local address: the port 0 is a free port.</summary>
    private IPEndPoint Bind<T>(List<T> sockets, Func<T, IPEndPoint?> getEndPoint, IPEndPoint endPoint)
    {
        if (!IsUnspecified(endPoint.Address) && !IsLocal(endPoint.Address))
            throw new SocketException((int)SocketError.AddressNotAvailable);

        lock (_lock)
        {
            if (endPoint.Port != 0)
            {
                if (sockets.Exists(socket => getEndPoint(socket) is { } bound && Conflicts(bound, endPoint)))
                    throw new SocketException((int)SocketError.AddressAlreadyInUse);

                return endPoint;
            }

            for (var port = FirstEphemeralPort; port <= IPEndPoint.MaxPort; port++)
            {
                var candidate = new IPEndPoint(endPoint.Address, port);
                if (!sockets.Exists(socket => getEndPoint(socket) is { } bound && Conflicts(bound, candidate)))
                    return candidate;
            }

            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }
    }

    /// <summary>A pipe of bytes, from a socket to its peer.</summary>
    private sealed class BytePipe
    {
        private readonly Pipe _pipe = new(new PipeOptions(useSynchronizationContext: false));

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            FlushResult result;
            try
            {
                result = await _pipe.Writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The writer is completed: the socket shut down its side of the connection
                throw new SocketException((int)SocketError.Shutdown);
            }

            if (result.IsCompleted)
                throw new SocketException((int)SocketError.ConnectionReset);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            ReadResult result;
            try
            {
                result = await _pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                throw new SocketException((int)SocketError.Shutdown);
            }

            var data = result.Buffer;
            if (data.IsEmpty)
            {
                _pipe.Reader.AdvanceTo(data.End);
                if (result.IsCompleted)
                    return 0;

                return await ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            var count = (int)Math.Min(data.Length, buffer.Length);
            data.Slice(0, count).CopyTo(buffer.Span);
            _pipe.Reader.AdvanceTo(data.GetPosition(count));
            return count;
        }

        public void CompleteWriter() => _pipe.Writer.Complete();

        public void CompleteReader() => _pipe.Reader.Complete();
    }

    private sealed class InMemoryTcpSocket(WasiInMemoryNetwork network, AddressFamily family) : WasiTcpSocket
    {
        private Channel<InMemoryTcpSocket>? _pending;
        private BytePipe? _send;
        private BytePipe? _receive;
        private bool _registered;

        public override IPEndPoint? LocalEndPoint => BoundEndPoint;

        public IPEndPoint? BoundEndPoint { get; private set; }

        public override IPEndPoint? RemoteEndPoint => Peer;

        public IPEndPoint? Peer { get; private set; }

        public bool IsListening => _pending is not null;

        public override void Bind(IPEndPoint localEndPoint)
        {
            if (localEndPoint.AddressFamily != family)
                throw new SocketException((int)SocketError.AddressFamilyNotSupported);

            lock (network._lock)
            {
                BoundEndPoint = network.Bind(network._tcpSockets, socket => socket.BoundEndPoint, localEndPoint);
                network._tcpSockets.Add(this);
                _registered = true;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The listener accepts the socket of the server, or disposes it")]
        public override ValueTask ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (!network.IsLocal(remoteEndPoint.Address))
                throw new SocketException((int)SocketError.HostUnreachable);

            if (BoundEndPoint is null)
            {
                Bind(new IPEndPoint(IPAddress.IsLoopback(remoteEndPoint.Address) ? remoteEndPoint.Address : Loopback(family), 0));
            }
            else if (IsUnspecified(BoundEndPoint.Address))
            {
                // The local address of a socket bound to all the addresses is the address of the route to the remote address
                BoundEndPoint = new IPEndPoint(remoteEndPoint.Address, BoundEndPoint.Port);
            }

            InMemoryTcpSocket? listener;
            lock (network._lock)
            {
                listener = network._tcpSockets.Find(socket => socket.IsListening && Matches(socket.BoundEndPoint!, remoteEndPoint));
            }

            if (listener is null)
                throw new SocketException((int)SocketError.ConnectionRefused);

            // The socket of the server shares the port of the listener: it is not registered
            var server = new InMemoryTcpSocket(network, family)
            {
                BoundEndPoint = remoteEndPoint,
                Peer = BoundEndPoint,
            };

            var toServer = new BytePipe();
            var toClient = new BytePipe();
            server._receive = toServer;
            server._send = toClient;
            _send = toServer;
            _receive = toClient;
            Peer = remoteEndPoint;
            if (!listener._pending!.Writer.TryWrite(server))
                throw new SocketException((int)SocketError.ConnectionRefused);

            return ValueTask.CompletedTask;
        }

        public override void Listen(int backlog)
        {
            _pending = Channel.CreateUnbounded<InMemoryTcpSocket>();
        }

        public override async ValueTask<WasiTcpSocket> AcceptAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _pending!.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                throw new SocketException((int)SocketError.OperationAborted);
            }
        }

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => (_send ?? throw new SocketException((int)SocketError.NotConnected)).WriteAsync(buffer, cancellationToken);

        public override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) => (_receive ?? throw new SocketException((int)SocketError.NotConnected)).ReadAsync(buffer, cancellationToken);

        public override void ShutdownSend() => _send?.CompleteWriter();

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            lock (network._lock)
            {
                if (_registered)
                {
                    network._tcpSockets.Remove(this);
                    _registered = false;
                }
            }

            _send?.CompleteWriter();
            _receive?.CompleteReader();
            if (_pending is { } pending)
            {
                pending.Writer.TryComplete();
                while (pending.Reader.TryRead(out var connection))
                {
                    connection.Dispose();
                }
            }
        }
    }

    private sealed class InMemoryUdpSocket(WasiInMemoryNetwork network, AddressFamily family) : WasiUdpSocket
    {
        private const int MaxDatagramSize = 65507;

        private readonly Channel<(byte[] Data, IPEndPoint Source)> _datagrams = Channel.CreateUnbounded<(byte[], IPEndPoint)>();
        private IPEndPoint? _remoteEndPoint;
        private IPAddress? _boundAddress;
        private bool _registered;

        public override IPEndPoint? LocalEndPoint => BoundEndPoint;

        public IPEndPoint? BoundEndPoint { get; private set; }

        public override void Bind(IPEndPoint localEndPoint)
        {
            if (localEndPoint.AddressFamily != family)
                throw new SocketException((int)SocketError.AddressFamilyNotSupported);

            lock (network._lock)
            {
                BoundEndPoint = network.Bind(network._udpSockets, socket => socket.BoundEndPoint, localEndPoint);
                _boundAddress = BoundEndPoint.Address;
                network._udpSockets.Add(this);
                _registered = true;
            }
        }

        private void BindImplicitly()
        {
            if (BoundEndPoint is null)
            {
                Bind(new IPEndPoint(Unspecified(family), 0));
            }
        }

        public override void Connect(IPEndPoint? remoteEndPoint)
        {
            if (remoteEndPoint is not null)
            {
                BindImplicitly();
            }

            // A connected socket bound to all the addresses gets the address of the route to the remote address
            var address = remoteEndPoint is not null && IsUnspecified(_boundAddress!) && network.IsLocal(remoteEndPoint.Address) ? remoteEndPoint.Address : _boundAddress;
            if (address is not null && BoundEndPoint is { } bound)
            {
                BoundEndPoint = new IPEndPoint(address, bound.Port);
            }

            _remoteEndPoint = remoteEndPoint;
        }

        public override ValueTask SendToAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (datagram.Length > MaxDatagramSize)
                throw new SocketException((int)SocketError.MessageSize);

            BindImplicitly();

            // A datagram to an unknown address is lost, as on a network
            if (!network.IsLocal(remoteEndPoint.Address))
                return ValueTask.CompletedTask;

            var source = BoundEndPoint!;
            if (IsUnspecified(source.Address))
            {
                source = new IPEndPoint(remoteEndPoint.Address, source.Port);
            }

            InMemoryUdpSocket? destination;
            lock (network._lock)
            {
                destination = network._udpSockets.Find(socket => Matches(socket.BoundEndPoint!, remoteEndPoint));
            }

            destination?._datagrams.Writer.TryWrite((datagram.ToArray(), source));
            return ValueTask.CompletedTask;
        }

        public override async ValueTask<(int Length, IPEndPoint RemoteEndPoint)> ReceiveFromAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            while (true)
            {
                (byte[] Data, IPEndPoint Source) datagram;
                try
                {
                    datagram = await _datagrams.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    throw new SocketException((int)SocketError.OperationAborted);
                }

                if (_remoteEndPoint is not null && !_remoteEndPoint.Equals(datagram.Source))
                    continue;

                var length = Math.Min(datagram.Data.Length, buffer.Length);
                datagram.Data.AsSpan(0, length).CopyTo(buffer.Span);
                return (length, datagram.Source);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            lock (network._lock)
            {
                if (_registered)
                {
                    network._udpSockets.Remove(this);
                    _registered = false;
                }
            }

            _datagrams.Writer.TryComplete();
        }
    }

    /// <summary>The stream of a connection of the host.</summary>
    private sealed class InMemoryTcpStream(WasiTcpSocket socket) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => socket.ReceiveAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => socket.SendAsync(buffer, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                socket.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Returns the stream of a connection accepted by a listener of the host.</summary>
    internal static Stream CreateStream(WasiTcpSocket socket) => new InMemoryTcpStream(socket);
}

/// <summary>A listener of the host on a <see cref="WasiInMemoryNetwork"/>, created by <see cref="WasiInMemoryNetwork.Listen"/>.</summary>
public sealed class WasiInMemoryTcpListener : IDisposable
{
    private readonly WasiTcpSocket _socket;

    internal WasiInMemoryTcpListener(WasiTcpSocket socket)
    {
        _socket = socket;
    }

    /// <summary>Gets the address of the listener, with its port.</summary>
    public IPEndPoint LocalEndPoint => _socket.LocalEndPoint!;

    /// <summary>Waits for a connection of a component.</summary>
    /// <param name="cancellationToken">The token to stop waiting.</param>
    /// <returns>The stream of the connection: dispose it to close the connection.</returns>
    /// <exception cref="SocketException">The listener is disposed.</exception>
    public async ValueTask<Stream> AcceptAsync(CancellationToken cancellationToken = default)
    {
        var socket = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
        return WasiInMemoryNetwork.CreateStream(socket);
    }

    /// <summary>Stops listening.</summary>
    public void Dispose() => _socket.Dispose();
}
