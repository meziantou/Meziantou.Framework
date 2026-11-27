using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// The network of a WASI component (<c>wasi:sockets</c>, see <see cref="WasiOptions.Network"/>): the TCP and UDP
/// sockets and the name lookups. <see cref="WasiHostNetwork"/> uses the network of the machine, with the rules of a
/// <see cref="WasiHostNetworkOptions"/>; <see cref="WasiInMemoryNetwork"/> is a network inside the process.
/// </summary>
/// <remarks>
/// WASI checks the states of the sockets and the arguments of the operations before calling the sockets (a socket that
/// is already bound, an address of the wrong family, a connection to the port 0, ...): the sockets only perform the
/// operations. The methods throw a <see cref="SocketException"/> to report an error: its
/// <see cref="SocketException.SocketErrorCode"/> is converted to the error code of WASI.
/// </remarks>
public abstract class WasiNetwork
{
    /// <summary>Creates a TCP socket.</summary>
    /// <param name="addressFamily">The address family: <see cref="AddressFamily.InterNetwork"/> or <see cref="AddressFamily.InterNetworkV6"/> (only IPv6, not the IPv4-mapped addresses).</param>
    /// <exception cref="SocketException">The socket cannot be created.</exception>
    public abstract WasiTcpSocket CreateTcpSocket(AddressFamily addressFamily);

    /// <summary>Creates a UDP socket.</summary>
    /// <param name="addressFamily">The address family: <see cref="AddressFamily.InterNetwork"/> or <see cref="AddressFamily.InterNetworkV6"/> (only IPv6, not the IPv4-mapped addresses).</param>
    /// <exception cref="SocketException">The socket cannot be created.</exception>
    public abstract WasiUdpSocket CreateUdpSocket(AddressFamily addressFamily);

    /// <summary>Returns the IP addresses of a host name, in the order of preference.</summary>
    /// <param name="name">The host name: WASI returns the IP addresses itself, without calling this method.</param>
    /// <param name="cancellationToken">The token that is cancelled when the component cancels the lookup.</param>
    /// <exception cref="SocketException">The name cannot be resolved: <see cref="SocketError.HostNotFound"/>, <see cref="SocketError.TryAgain"/>, <see cref="SocketError.AccessDenied"/>, ...</exception>
    public abstract ValueTask<IReadOnlyList<IPAddress>> ResolveAddressesAsync(string name, CancellationToken cancellationToken);
}

/// <summary>A TCP socket of a <see cref="WasiNetwork"/>.</summary>
/// <remarks>The options have default implementations that keep the values, without effect on the socket.</remarks>
public abstract class WasiTcpSocket : IDisposable
{
    /// <summary>Gets the local address of the socket, or null when it is not bound.</summary>
    public abstract IPEndPoint? LocalEndPoint { get; }

    /// <summary>Gets the remote address of a connected socket, or null.</summary>
    public abstract IPEndPoint? RemoteEndPoint { get; }

    /// <summary>Binds the socket to a local address. The port 0 is a free port.</summary>
    /// <param name="localEndPoint">A unicast address of the family of the socket, or the unspecified address.</param>
    /// <exception cref="SocketException">The address is not available or is in use.</exception>
    public abstract void Bind(IPEndPoint localEndPoint);

    /// <summary>Connects the socket to a remote address, after binding it to a free port when it is not bound.</summary>
    /// <param name="remoteEndPoint">A unicast address of the family of the socket, with a port.</param>
    /// <param name="cancellationToken">The token that is cancelled when the component cancels the connection.</param>
    /// <exception cref="SocketException">The connection failed.</exception>
    public abstract ValueTask ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken);

    /// <summary>Starts listening for connections. The socket is bound.</summary>
    /// <param name="backlog">The size of the queue of the pending connections.</param>
    /// <exception cref="SocketException">The socket cannot listen.</exception>
    public abstract void Listen(int backlog);

    /// <summary>Accepts a connection of a listening socket.</summary>
    /// <param name="cancellationToken">The token that is cancelled when the component drops the stream of the connections.</param>
    /// <returns>The connected socket.</returns>
    /// <exception cref="SocketException">The connection failed.</exception>
    public abstract ValueTask<WasiTcpSocket> AcceptAsync(CancellationToken cancellationToken);

    /// <summary>Sends data to the peer of a connected socket.</summary>
    /// <param name="buffer">The data.</param>
    /// <param name="cancellationToken">The token that is cancelled when the component cancels the copy.</param>
    /// <exception cref="SocketException">The connection is broken or reset.</exception>
    public abstract ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Receives data from the peer of a connected socket.</summary>
    /// <param name="buffer">The buffer that receives the data.</param>
    /// <param name="cancellationToken">The token that is cancelled when the component drops the stream of the data.</param>
    /// <returns>The number of bytes received, or 0 when the peer shut down its side of the connection.</returns>
    /// <exception cref="SocketException">The connection is reset.</exception>
    public abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Shuts down the sending side of the connection: the peer receives the end of the data.</summary>
    public abstract void ShutdownSend();

    /// <summary>Gets or sets the size of the queue of the pending connections.</summary>
    public virtual int ListenBacklog { get; set; } = 128;

    /// <summary>Gets or sets whether the socket sends keep-alive packets (<c>SO_KEEPALIVE</c>).</summary>
    public virtual bool KeepAlive { get; set; }

    /// <summary>Gets or sets the time without data before the first keep-alive packet (<c>TCP_KEEPIDLE</c>).</summary>
    public virtual TimeSpan KeepAliveIdleTime { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Gets or sets the time between the keep-alive packets (<c>TCP_KEEPINTVL</c>).</summary>
    public virtual TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(75);

    /// <summary>Gets or sets the number of keep-alive packets without answer before the connection is closed (<c>TCP_KEEPCNT</c>).</summary>
    public virtual int KeepAliveCount { get; set; } = 9;

    /// <summary>Gets or sets the time to live of the packets (<c>IP_TTL</c>, <c>IPV6_UNICAST_HOPS</c>).</summary>
    public virtual int HopLimit { get; set; } = 64;

    /// <summary>Gets or sets the size of the receive buffer (<c>SO_RCVBUF</c>).</summary>
    public virtual int ReceiveBufferSize { get; set; } = 65536;

    /// <summary>Gets or sets the size of the send buffer (<c>SO_SNDBUF</c>).</summary>
    public virtual int SendBufferSize { get; set; } = 65536;

    /// <summary>Closes the socket.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Closes the socket.</summary>
    /// <param name="disposing">Whether the method is called by <see cref="Dispose()"/>.</param>
    protected abstract void Dispose(bool disposing);
}

/// <summary>A UDP socket of a <see cref="WasiNetwork"/>.</summary>
/// <remarks>The options have default implementations that keep the values, without effect on the socket.</remarks>
public abstract class WasiUdpSocket : IDisposable
{
    /// <summary>Gets the local address of the socket, or null when it is not bound.</summary>
    public abstract IPEndPoint? LocalEndPoint { get; }

    /// <summary>Binds the socket to a local address. The port 0 is a free port.</summary>
    /// <param name="localEndPoint">An address of the family of the socket, or the unspecified address.</param>
    /// <exception cref="SocketException">The address is not available or is in use.</exception>
    public abstract void Bind(IPEndPoint localEndPoint);

    /// <summary>Limits the datagrams to a remote address (or removes the limit), after binding the socket to a free port when it is not bound.</summary>
    /// <param name="remoteEndPoint">The remote address, or null to receive and send the datagrams of any address.</param>
    /// <exception cref="SocketException">The socket cannot be connected.</exception>
    public abstract void Connect(IPEndPoint? remoteEndPoint);

    /// <summary>Sends a datagram, after binding the socket to a free port when it is not bound.</summary>
    /// <param name="datagram">The data of the datagram.</param>
    /// <param name="remoteEndPoint">The destination.</param>
    /// <param name="cancellationToken">The token that is cancelled when the component cancels the call.</param>
    /// <exception cref="SocketException">The datagram cannot be sent.</exception>
    public abstract ValueTask SendToAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint, CancellationToken cancellationToken);

    /// <summary>Receives a datagram of a bound socket.</summary>
    /// <param name="buffer">The buffer of the data, of 64 KiB.</param>
    /// <param name="cancellationToken">The token that is cancelled when the component cancels the call.</param>
    /// <returns>The length of the datagram and its source.</returns>
    /// <exception cref="SocketException">The datagram cannot be received.</exception>
    public abstract ValueTask<(int Length, IPEndPoint RemoteEndPoint)> ReceiveFromAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Gets or sets the time to live of the packets (<c>IP_TTL</c>, <c>IPV6_UNICAST_HOPS</c>).</summary>
    public virtual int HopLimit { get; set; } = 64;

    /// <summary>Gets or sets the size of the receive buffer (<c>SO_RCVBUF</c>).</summary>
    public virtual int ReceiveBufferSize { get; set; } = 65536;

    /// <summary>Gets or sets the size of the send buffer (<c>SO_SNDBUF</c>).</summary>
    public virtual int SendBufferSize { get; set; } = 65536;

    /// <summary>Closes the socket.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Closes the socket.</summary>
    /// <param name="disposing">Whether the method is called by <see cref="Dispose()"/>.</param>
    protected abstract void Dispose(bool disposing);
}
